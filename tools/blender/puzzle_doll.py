"""BLINK P2: Lily's doll and her key (bpy). A seated Victorian bisque doll, built procedurally, one material.

  python -I tools/blender/puzzle_doll_textures.py                       (the atlas: Base / Normal / Mask, 2048 x 2048)
  blender -b --factory-startup --python tools/blender/puzzle_doll.py -- [--render DIR] [--no-export]

Exports Unity/Assets/Art/Puzzles/LilysDoll/LilysDoll.fbx and LilysKey.fbx (build_all.export conventions: metres,
Blender -Y front = Unity +Z, forward -Z / up Y). Both use the atlas material "LilysDoll".

Doll hierarchy (what PuzzleProps.BuildDoll looks up by name):
  LilysDoll          root, origin on the chair seat under the pelvis, front +Z
    Body             one mesh: bodice, skirt, pinafore, sleeves, lace, hands, legs, stockings, shoes
    Lap              empty where the hands cradle the key
    Head             empty at the neck base (the pivot LilysDollRoom turns). The 13 degree sideways tilt and the 14 degree
                     nod are baked into the head's geometry, so they survive the look-at rotation.
    HeadMesh         skull, nose, glass eyes, hair, ringlets, the stray strand (origin at the neck, like Head)
    Face             empty between the eyes
  HeadMesh and Face are siblings of Head here because the FBX exporter mangles the local transforms of anything nested
  under an empty; PuzzleProps.BuildDoll re-parents both under Head (world position kept).
Key hierarchy: LilysKey (one mesh), origin at its middle, bow towards -X, bit towards +X and -Z (Unity), lying flat.

Coordinates below use P(x, f, z): x sideways, f FORWARD (the doll's front), z up; Blender y = -f.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import puzzle_doll_textures as T  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Unity", "Assets", "Art", "Puzzles", "LilysDoll")
TEX = os.path.join(OUT, "Textures")

TILT_ROLL = -13.0   # degrees, head leans sideways
TILT_NOD = 14.0      # degrees, chin down


def P(x, f, z):
    return Vector((x, -f, z))


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------------------------------------ mesh building

class Mesh:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def v(self, p):
        return self.bm.verts.new(p)

    def face(self, verts, uvs, flat=False):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        for lp, uv in zip(f.loops, uvs):
            lp[self.uv].uv = uv
        f.smooth = not flat
        return f

    def face_out(self, vs, uvs, centre):
        c = sum((v.co for v in vs), Vector()) / len(vs)
        n = (vs[1].co - vs[0].co).cross(vs[-1].co - vs[0].co)
        if n.dot(c - centre) < 0:
            vs, uvs = vs[::-1], uvs[::-1]
        return self.face(vs, uvs)

    def tris(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    def apply(self, mat):
        bmesh.ops.transform(self.bm, matrix=mat, verts=self.bm.verts)

    def obj(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        for p in me.polygons:
            p.use_smooth = True
        return o


def rect_uv(r):
    u0, v0, u1, v1 = r
    return lambda u, v: (u0 + (u1 - u0) * u, v0 + (v1 - v0) * v)


def grid(M, pts, rect, closed=True, flip=False, cap0=False, cap1=False, urep=1.0, metric_v=True):
    """Quads between consecutive rings. pts[k][i]: ring k (closed around i). u wraps around, v runs along the rings
    (by length, so texel density stays even). Ring order is counter-clockwise about the direction of travel, which
    gives outward normals; flip=True for the other way."""
    K, S = len(pts), len(pts[0])
    uv = rect_uv(rect)
    verts = [[M.v(p) for p in row] for row in pts]
    ns = S if closed else S - 1
    if metric_v:
        acc = [0.0]
        for k in range(1, K):
            acc.append(acc[-1] + sum((pts[k][i] - pts[k - 1][i]).length for i in range(S)) / S)
        vs = [a / max(acc[-1], 1e-9) for a in acc]
    else:
        vs = [k / (K - 1) for k in range(K)]
    for k in range(K - 1):
        for i in range(ns):
            i2 = (i + 1) % S if closed else i + 1
            quad = [verts[k][i], verts[k][i2], verts[k + 1][i2], verts[k + 1][i]]
            uvs = [uv(i / ns * urep, vs[k]), uv((i + 1) / ns * urep, vs[k]),
                   uv((i + 1) / ns * urep, vs[k + 1]), uv(i / ns * urep, vs[k + 1])]
            if flip:
                quad, uvs = quad[::-1], uvs[::-1]
            M.face(quad, uvs)
    for end, on in ((0, cap0), (K - 1, cap1)):
        if not on:
            continue
        c = sum(pts[end], Vector()) / S
        cv = M.v(c)
        uc = uv(0.5, vs[end])
        for i in range(S):
            i2 = (i + 1) % S
            tri = [cv, verts[end][i], verts[end][i2]] if end == K - 1 else [cv, verts[end][i2], verts[end][i]]
            if flip:
                tri = [tri[0], tri[2], tri[1]]
            M.face(tri, [uc, uv(i / S, vs[end]), uv((i + 1) / S, vs[end])])


def frames(pts, ref):
    out = []
    prev = None
    n = len(pts)
    for k in range(n):
        t = (pts[min(k + 1, n - 1)] - pts[max(k - 1, 0)]).normalized()
        base = ref if prev is None else prev
        nn = base - t * base.dot(t)
        if nn.length < 1e-6:
            nn = Vector((0, 0, 1)) - t * t.z
        nn.normalize()
        out.append((t, nn, t.cross(nn)))
        prev = nn
    return out


def ring(c, n, b, rx, ry, seg, mod=None):
    pts = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        p = c + n * (rx * math.cos(a)) + b * (ry * math.sin(a))
        pts.append(mod(i, a, p) if mod else p)
    return pts


def tube(M, pts, rx, ry=None, seg=8, ref=Vector((1, 0, 0)), rect=None, caps=(False, False), flip=False, mod=None,
         urep=1.0):
    ry = ry or rx
    fr = frames(pts, ref)
    rings = [ring(pts[k], fr[k][1], fr[k][2], rx[k], ry[k], seg, (lambda i, a, p, k=k: mod(k, i, a, p)) if mod else None)
             for k in range(len(pts))]
    grid(M, rings, rect, flip=flip, cap0=caps[0], cap1=caps[1], urep=urep)


def hermite(pts, n):
    """n samples along a Catmull-Rom spline through pts."""
    m = len(pts) - 1
    out = []
    for s in range(n):
        u = s / (n - 1) * m
        i = min(int(u), m - 1)
        t = u - i
        p0, p1 = pts[i], pts[i + 1]
        m0 = (pts[i + 1] - pts[i - 1]) * 0.5 if i > 0 else (p1 - p0)
        m1 = (pts[i + 2] - pts[i]) * 0.5 if i + 2 <= m else (p1 - p0)
        t2, t3 = t * t, t * t * t
        out.append((2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1)
    return out


def sphere_part(M, centre, radii, rect_fn, rings=6, seg=10, polar_max=math.pi, front=Vector((0, -1, 0)), rot=None):
    """UV sphere (front pole towards -Y), uv by planar front projection through rect_fn(x_local, z_local)."""
    verts = {}
    cx, cy, cz = radii

    def pt(k, i):
        th = polar_max * k / rings
        ph = 2 * math.pi * i / seg
        # pole axis = -Y
        x = math.sin(th) * math.cos(ph)
        z = math.sin(th) * math.sin(ph)
        y = -math.cos(th)
        q = Vector((x * cx, y * cy, z * cz))
        return rot @ q if rot is not None else q
    rows = [[None] * seg for _ in range(rings + 1)]
    for k in range(rings + 1):
        for i in range(seg):
            if k == 0 and i > 0:
                rows[0][i] = rows[0][0]
            else:
                rows[k][i] = M.v(centre + pt(k, i))
    for k in range(rings):
        for i in range(seg):
            i2 = (i + 1) % seg
            a, b, c, d = rows[k][i], rows[k][i2], rows[k + 1][i2], rows[k + 1][i]
            quad = [a, b, c, d] if k > 0 else [a, c, d]
            quad = list(dict.fromkeys(quad))
            # winding: outward is -Y at the pole; check against the normal and flip if needed
            nrm = (quad[1].co - quad[0].co).cross(quad[-1].co - quad[0].co)
            if nrm.dot(quad[0].co - centre) < 0:
                quad = quad[::-1]
            M.face(quad, [rect_fn(v.co - centre) for v in quad])


# ------------------------------------------------------------------------------------------------ the body

# torso rings: z -> (centre f, half width x, half depth f); the bodice, the pinafore bib and the collar share this
TORSO = [(0.100, 0.000, 0.060, 0.044), (0.125, 0.003, 0.056, 0.041), (0.160, 0.012, 0.062, 0.045),
         (0.200, 0.026, 0.067, 0.049), (0.232, 0.038, 0.071, 0.045), (0.252, 0.043, 0.056, 0.036),
         (0.264, 0.045, 0.036, 0.028), (0.275, 0.046, 0.025, 0.024)]
NECK = P(0, 0.046, 0.275)     # the head pivot


def torso_at(z):
    z = max(TORSO[0][0], min(TORSO[-1][0], z))
    for a, b in zip(TORSO, TORSO[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            return tuple(lerp(a[i], b[i], t) for i in (1, 2, 3))
    return TORSO[-1][1:]


def skirt_pt(theta, t, off=0.0):
    """The skirt surface: theta 0 = front, increasing towards +x; t 0 = waist, 1 = hem."""
    sx, cf = math.sin(theta), math.cos(theta)
    s = t ** 0.85
    hem_f = 0.06 + 0.155 * cf
    hem_x = 0.145 * sx
    hem_z = 0.056 - 0.044 * sx * sx - 0.018 * max(-cf, 0.0)
    x = lerp(0.060 * sx, hem_x, s)
    f = lerp(0.044 * cf, hem_f, s)
    z = lerp(0.100, hem_z, t)
    z += 0.012 * math.sin(math.pi * t) * (0.55 + 0.45 * cf)
    fold = 0.0042 * t * (math.sin(theta * 9 + 1.3) + 0.5 * math.sin(theta * 17 + 0.4))
    x += fold * sx
    f += fold * cf
    z += 0.0035 * t * t * math.sin(theta * 14 + 0.7) + off
    return P(x, f, max(z, 0.004))


def build_skirt(M):
    seg, rows = 32, 7
    pts = [[skirt_pt(2 * math.pi * i / seg - 0.0, k / (rows - 1)) for i in range(seg)] for k in range(rows)]
    grid(M, pts, T.sub("skirt"), flip=True)
    # petticoat frill under the hem: lace, scalloped, 16 scallops (one pattern repeat each)
    seg, scal = 48, 16
    fr = []
    for kk, (w, drop) in enumerate(((0.0, 0.0), (0.014, -0.004), (0.030, -0.008))):
        row = []
        for i in range(seg):
            th = 2 * math.pi * i / seg
            p = skirt_pt(th, 1.0)
            out = Vector((math.sin(th), -math.cos(th), 0)).normalized()
            sc = abs(math.cos(math.pi * scal * i / seg))
            ww = w * (0.55 + 0.45 * (1.0 - sc ** 0.6)) if kk == 2 else w
            q = p + out * (ww + (0.004 if kk else 0)) + Vector((0, 0, drop - 0.002 * kk * noise.noise(Vector((i * 0.7, kk, 3)))))
            q.z = max(q.z, 0.003)
            row.append(q)
        fr.append(row)
    grid(M, fr, T.sub("frill", repeats=16), flip=True, urep=1.0, metric_v=False)


def build_torso(M):
    pts, rx, ry = [], [], []
    for z, cf, w, d in TORSO:
        pts.append(P(0, cf, z))
        rx.append(w)
        ry.append(d)
    tube(M, pts, rx, ry, seg=16, rect=T.sub("bodice"), caps=(False, True))


def build_pinafore(M):
    # bib on the bodice
    cols = 9
    rows = [0.103, 0.12, 0.14, 0.16, 0.18, 0.2, 0.22, 0.238]
    pts = []
    for z in rows:
        cf, w, d = torso_at(z)
        row = []
        for i in range(cols):
            a = lerp(-0.8, 0.8, i / (cols - 1))
            wx = 0.0 + w * 0.8 * 1.0
            row.append(P((w + 0.0045) * math.sin(a), cf + (d + 0.0045) * math.cos(a), z))
        pts.append(row)
    grid(M, pts, T.sub("bib"), closed=False)
    # apron over the lap
    cols, nrows = 13, 7
    pts = []
    for k in range(nrows):
        t = 0.0 + 0.8 * k / (nrows - 1)
        pts.append([skirt_pt(lerp(-0.85, 0.85, i / (cols - 1)), t, off=0.0035) for i in range(cols)])
    grid(M, pts, T.sub("apron"), closed=False, flip=True)
    # straps over the shoulders (thin flat ribbons), bib top corner to the back of the bodice
    for sx in (-1, 1):
        path = [P(sx * 0.034, 0.046, 0.239), P(sx * 0.046, 0.042, 0.256), P(sx * 0.052, 0.020, 0.254), P(sx * 0.050, -0.010, 0.230)]
        sp = hermite(path, 7)
        tube(M, sp, [0.0065] * 7, [0.0012] * 7, seg=4, ref=Vector((0, 0, 1)), rect=T.sub("strap"), caps=(True, True))
    # frill along the bib's top edge
    row0, row1 = [], []
    for i in range(14):
        a = lerp(-0.8, 0.8, i / 13)
        cf, w, d = torso_at(0.238)
        p = P((w + 0.0045) * math.sin(a), cf + (d + 0.0045) * math.cos(a), 0.238)
        sc = abs(math.cos(math.pi * 3.5 * i / 13))
        row0.append(p)
        row1.append(p + Vector((0, -0.004, 0.008 + 0.004 * sc)))
    grid(M, [row0, row1], T.sub("frill_small"), closed=False)


def build_collar(M):
    seg, scal = 48, 16
    zs = [0.282, 0.272, 0.259, 0.243]
    flare = [0.003, 0.006, 0.0105, 0.0165]
    rows = []
    for k, (z, fl) in enumerate(zip(zs, flare)):
        cf, w, d = torso_at(min(z, 0.275))
        row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            sc = abs(math.cos(math.pi * scal * i / seg)) if k == 3 else 0.0
            ee = fl * (1.0 - (0.4 * (1 - sc ** 0.6) if k == 3 else 0.0))
            row.append(P((w + ee) * math.sin(a) * (1.0 if w > 0.03 else 1.0), cf + (d + ee) * math.cos(a), z - (0.004 if k == 3 else 0.0)))
        rows.append(row)
    # keep the neck hole clear and the whole ring a touch higher than the shoulder line
    grid(M, rows, T.sub("collar", repeats=32), flip=True, metric_v=False)


def build_arm(M, S, E, W, droop=0.0):
    path = hermite([S, E, W], 6)
    rx = [0.0155, 0.0215, 0.0195, 0.0155, 0.0133, 0.0122]
    tube(M, path, rx, [r * 0.95 for r in rx], seg=10, ref=Vector((0, 1, 0)), rect=T.sub("sleeve"), caps=(True, False))
    d = (W - E).normalized()
    # lace cuff over the wrist
    scal = 10
    rows = []
    for k, (off, r) in enumerate(((-0.014, 0.0135), (-0.002, 0.0165), (0.010, 0.0245))):
        cen = W + d * off
        t, n, b = frames([cen - d, cen + d], Vector((0, 0, 1)))[0]
        row = []
        for i in range(30):
            a = 2 * math.pi * i / 30
            sc = abs(math.cos(math.pi * scal * i / 30))
            rr = r * (1.0 - (0.22 * (1 - sc ** 0.6) if k == 2 else 0.0))
            row.append(cen + n * (rr * math.cos(a)) + b * (rr * math.sin(a)))
        rows.append(row)
    grid(M, rows, T.sub("cuff", repeats=10), flip=False, metric_v=False)
    return d


def build_hand(M, W, d, sx, curl=1.0, lift=0.0):
    """Porcelain hand, palm up, fingers curled round the key. W the wrist, d the forearm direction."""
    hd = Vector((d.x * 0.85, d.y * 0.85, 0.0)).normalized()
    up = Vector((0, 0, 1))
    side = hd.cross(up)   # across the palm
    base = W + hd * 0.004 + Vector((0, 0, lift))
    palm = [base, base + hd * 0.016, base + hd * 0.030]
    tube(M, palm, [0.0108, 0.0150, 0.0140], [0.0050, 0.0066, 0.0058], seg=8, ref=side, rect=T.sub("palm"), caps=(True, False))
    end = palm[-1]
    for j, off in enumerate((-0.0105, -0.0035, 0.0035, 0.0105)):
        L = (0.016, 0.021, 0.020, 0.0165)[j]
        p0 = end + side * off + up * 0.0008
        p1 = p0 + hd * (L * 0.55) + up * 0.0012 * curl
        p2 = p1 + hd * (L * 0.38) + up * (0.0075 * curl)
        p3 = p2 + hd * (L * 0.08) + up * (0.0065 * curl) - side * (off * 0.4)
        tube(M, [p0, p1, p2, p3], [0.0042, 0.0039, 0.0034, 0.0027], seg=5, ref=side, rect=T.sub("finger"), caps=(False, True))
    # thumb
    ts = -sx
    t0 = base + hd * 0.016 + side * (0.010 * ts)
    t1 = t0 + hd * 0.010 + side * (0.008 * ts) + up * 0.002
    t2 = t1 + hd * 0.008 + side * (0.002 * ts) + up * 0.006 * curl
    tube(M, [t0, t1, t2], [0.0054, 0.0046, 0.0036], seg=5, ref=side, rect=T.sub("finger"), caps=(False, True))


def build_leg(M, hip, knee, ankle, yaw_sign, toe_out):
    pts = hermite([hip, knee, ankle], 7)
    rx = [0.0275, 0.0265, 0.0245, 0.0225, 0.0195, 0.0170, 0.0150]
    tube(M, pts, rx, rx, seg=8, ref=Vector((0, 0, 1)), rect=T.sub("stocking"), caps=(False, False))
    # shoe: black patent, ankle to toe, toed out and pointing up a little
    yaw = math.radians(toe_out) * yaw_sign
    pit = math.radians(26)
    a = Vector((math.sin(yaw) * math.cos(pit), -math.cos(yaw) * math.cos(pit), math.sin(pit))).normalized()
    s0 = ankle - a * 0.016 + Vector((0, 0, -0.002))
    sp = [s0 + a * (0.095 * k / 5) for k in range(6)]
    wd = [0.0150, 0.0182, 0.0188, 0.0172, 0.0148, 0.0108]
    ht = [0.0185, 0.0205, 0.0185, 0.0150, 0.0120, 0.0090]
    # heel and toe shaping: flatten the sole by dropping the ring centres a little
    sp = [p - Vector((0, 0, 1)) * 0.0 for p in sp]
    tube(M, sp, wd, ht, seg=10, ref=Vector((math.cos(yaw), math.sin(yaw), 0)), rect=T.sub("shoe"), caps=(True, True))


def build_body():
    M = Mesh()
    build_torso(M)
    build_skirt(M)
    build_pinafore(M)
    build_collar(M)
    # arms: the right one higher, the left sagging, both hands under the key
    SR, ER, WR = P(0.066, 0.036, 0.232), P(0.090, 0.060, 0.166), P(0.044, 0.103, 0.100)
    SL, EL, WL = P(-0.068, 0.034, 0.224), P(-0.097, 0.074, 0.146), P(-0.046, 0.111, 0.094)
    dR = build_arm(M, SR, ER, WR)
    dL = build_arm(M, SL, EL, WL)
    build_hand(M, WR, dR, 1, curl=1.0)
    build_hand(M, WL, dL, -1, curl=0.8, lift=0.0)
    build_leg(M, P(0.034, 0.005, 0.027), P(0.050, 0.175, 0.022), P(0.066, 0.300, 0.016), 1, 22)
    build_leg(M, P(-0.034, 0.005, 0.027), P(-0.052, 0.172, 0.022), P(-0.080, 0.298, 0.016), -1, 34)
    return M


# ------------------------------------------------------------------------------------------------ the head

HC = Vector((0.0, 0.004, 0.108))    # head ellipsoid centre (f is +, x/z as usual)
HR = (0.078, 0.082, 0.090)


def head_surface(d):
    """d: unit direction in (x, f, z). Returns the sculpted skull surface point (x, f, z)."""
    dx, df, dz = d
    x, f, z = HR[0] * dx, HR[1] * df, HR[2] * dz
    lower = max(0.0, min(1.0, (0.25 - dz) / 1.25))
    front = max(df, 0.0)
    x *= 1.0 - 0.27 * lower ** 1.6
    f *= 1.0 - 0.10 * lower
    f += 0.009 * lower ** 2 * front
    z -= 0.004 * lower ** 2
    w = front * math.exp(-((dz + 0.15) / 0.35) ** 2)
    x *= 1.0 + 0.05 * w
    f += 0.0045 * math.exp(-((dz - 0.2) / 0.16) ** 2) * front ** 2           # brow
    f -= 0.003 * math.exp(-((abs(x) - 0.04) / 0.013) ** 2 - ((HC.z + z - 0.113) / 0.012) ** 2) * front   # eye sockets
    return Vector((x + HC.x, f + HC.y, z + HC.z))


def face_uv(p, dest="face"):
    return T.face_planar(p.x, p.z)


def dirv(theta, phi):
    return (math.sin(theta) * math.sin(phi), math.sin(theta) * math.cos(phi), math.cos(theta))


def build_head():
    """Returns (Mesh in head-local doll coords (x, y=-f, z), face anchor)."""
    M = Mesh()
    P_ = lambda v: Vector((v.x, -v.y, v.z))   # (x, f, z) -> Blender
    rect_back = rect_uv(T.sub("head_back"))
    # skull
    seg, K = 28, 16
    ring_dirs = []
    verts = {}
    top = M.v(P_(head_surface(dirv(0.0, 0.0))))
    bot = M.v(P_(head_surface(dirv(math.pi, 0.0))))
    rows = []
    for k in range(1, K):
        th = math.pi * k / K
        rows.append([M.v(P_(head_surface(dirv(th, 2 * math.pi * i / seg)))) for i in range(seg)])

    def uvf(v, phi):
        p = v.co
        if p.y <= 0.0 + 0.0 and True:     # y = -f: front half has y <= 0.005
            pass
        return None

    def put(vs, phis):
        # face centre decides: front half -> planar into the face cell, back -> wrapped into the porcelain cell
        cf = sum((-v.co.y for v in vs)) / len(vs)
        out = []
        for v, ph in zip(vs, phis):
            p = v.co
            if cf > -0.004:
                out.append(T.face_planar(p.x, p.z))
            else:
                out.append(rect_back(ph / (2 * math.pi), min(max(p.z / 0.2, 0.0), 1.0)))
        return out

    def addf(vs, phis):
        # outward = away from the head centre
        c = sum((v.co for v in vs), Vector()) / len(vs)
        if len(vs) >= 3:
            n = (vs[1].co - vs[0].co).cross(vs[-1].co - vs[0].co)
            if n.dot(c - P_(HC)) < 0:
                vs, phis = vs[::-1], phis[::-1]
        M.face(vs, put(vs, phis))
    for i in range(seg):
        i2 = (i + 1) % seg
        p0, p1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
        addf([top, rows[0][i2], rows[0][i]], [0, p1, p0])
        addf([bot, rows[-1][i], rows[-1][i2]], [0, p0, p1])
    for k in range(len(rows) - 1):
        for i in range(seg):
            i2 = (i + 1) % seg
            p0, p1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
            addf([rows[k][i], rows[k][i2], rows[k + 1][i2], rows[k + 1][i]], [p0, p1, p1, p0])
    # nose
    nose_c = Vector((0.0, 0.084, 0.0845))
    sphere_part(M, P_(nose_c), (0.0075, 0.0085, 0.0095), lambda q: T.face_planar(q.x, q.z + 0.0845), rings=4, seg=8,
                polar_max=math.radians(130))
    # neck stub: tucked into the head, down into the collar
    tube(M, [Vector((0, -0.046 * 0 - 0.004, -0.014)), Vector((0, -0.004, 0.040))], [0.0235, 0.0245], [0.0235, 0.0245],
         seg=10, ref=Vector((1, 0, 0)), rect=T.sub("neck"), caps=(True, False))
    # glass eyes: a little too big, set well forward
    eyes = []
    for sx in (-1, 1):
        ec = Vector((0.0375 * sx, 0.0645, 0.1125))
        R = 0.0198
        sphere_part(M, P_(ec), (R, R, R), (lambda q, ec=ec: T.eye_planar(q.x, q.z, R)), rings=5, seg=12,
                    polar_max=math.radians(100))
        eyes.append(ec)
    # hair cap
    build_hair(M)
    return M, Vector((0.0, 0.0895, 0.1125))


def hairline(phi):
    """Polar angle (from the crown) where the hair stops at azimuth phi (0 = front)."""
    return math.radians(57 + 72 * (1 - math.cos(phi)) / 2)


def build_hair(M):
    P_ = lambda v: Vector((v.x, -v.y, v.z))
    seg, K = 40, 10
    uvh = rect_uv(T.sub("hair_cap"))
    crown_dir = (0, 0, 1)
    rows = []
    for k in range(1, K + 1):
        row = []
        for i in range(seg):
            phi = 2 * math.pi * i / seg
            th = hairline(phi) * k / K
            d = dirv(th, phi)
            s = head_surface(d)
            thick = 0.0036 + 0.0030 * smooth((th / hairline(phi)) - 0.55) - 0.0016 * smooth((th / hairline(phi)) - 0.9) * 10 + 0.0012 * noise.noise(Vector((d[0] * 9, d[1] * 9, d[2] * 9)))
            n = Vector(d)
            row.append(P_(s + n * thick))
        rows.append(row)
    top = M.v(P_(head_surface(crown_dir) + Vector((0, 0, 0.0045))))
    verts = [[M.v(p) for p in r] for r in rows]
    for i in range(seg):
        i2 = (i + 1) % seg
        M.face_out([top, verts[0][i2], verts[0][i]], [uvh(0.5, 0.0), uvh((i + 1) / seg, 1 / K), uvh(i / seg, 1 / K)], P_(HC))
    for k in range(K - 1):
        for i in range(seg):
            i2 = (i + 1) % seg
            M.face_out([verts[k][i], verts[k][i2], verts[k + 1][i2], verts[k + 1][i]],
                   [uvh(i / seg, (k + 1) / K), uvh((i + 1) / seg, (k + 1) / K), uvh((i + 1) / seg, (k + 2) / K), uvh(i / seg, (k + 2) / K)], P_(HC))
    # ringlets: tight tapering corkscrews hanging from under the hairline, three a side
    specs = [(80, 1.0, 0.00), (106, 0.9, 0.35), (132, 1.05, 0.70)]
    for sgn in (-1, 1):
        for deg, length, phase in specs:
            phi = math.radians(deg) * sgn
            th0 = hairline(phi) - 0.10
            s0 = head_surface(dirv(th0, phi)) + Vector(dirv(th0, phi)) * 0.004
            L = 0.125 * length
            n = 40
            pts = []
            for q in range(n):
                u = q / (n - 1)
                z = s0.z - L * u
                rad = 0.0095 * (1 - 0.35 * u) * min(1.0, u * 6 + 0.25)
                ang = 2 * math.pi * (4.5 * u + phase + (0.3 if sgn > 0 else 0.6))
                outx = s0.x * (1 + 0.20 * u) + rad * math.cos(ang) * 0.9
                outf = s0.y * (1 - 0.10 * u) + rad * math.sin(ang) - 0.004 * u
                pts.append(P_(Vector((outx, outf, z))))
            rr = [lerp(0.0040, 0.0019, (q / (n - 1)) ** 0.9) for q in range(n)]
            tube(M, pts, rr, rr, seg=5, ref=Vector((0, 1, 0)), rect=T.sub("ringlet"), caps=(False, True))
    build_hair_band(M)


def build_hair_band(M):
    """A velvet Alice band over the crown (same red cloth as the dress), ear to ear just behind the fringe, with a
    bow and two tails at the +x side."""
    P_ = lambda v: Vector((v.x, -v.y, v.z))
    cloth = T.sub("sleeve")
    pts = []
    n = 26
    for q in range(n):
        a = math.radians(lerp(-98, 98, q / (n - 1)))
        d = Vector((math.sin(a), 0.20, math.cos(a))).normalized()     # (x, f, z): a touch forward of the crown
        s = head_surface(tuple(d))
        pts.append(P_(s + d * 0.0078))
    wid = [0.0085] * n
    thk = [0.0016] * n
    tube(M, pts, wid, thk, seg=6, ref=Vector((0, 1, 0)), rect=cloth, caps=(True, True))
    # the bow sits on the band at a = +52 degrees
    a = math.radians(52)
    d = Vector((math.sin(a), 0.20, math.cos(a))).normalized()
    c = P_(head_surface(tuple(d)) + d * 0.0098)
    nrm = P_(d).normalized()
    tan = Vector((math.cos(a), 0, -math.sin(a))).normalized()          # along the band, towards the ear (blender)
    wdir = nrm.cross(tan).normalized()
    for sgn in (-1, 1):
        zaxis = tan * sgn
        yaxis = wdir
        xaxis = nrm
        # loop: long along the band, thin along the normal; tipped up a little
        rot = Matrix(((zaxis.x, yaxis.x, xaxis.x), (zaxis.y, yaxis.y, xaxis.y), (zaxis.z, yaxis.z, xaxis.z)))
        sphere_part(M, c + zaxis * 0.0125 + nrm * 0.0035, (0.0135, 0.0072, 0.0038), lambda q: T.sub_uv_cloth(q), rings=5, seg=10, rot=rot)
    sphere_part(M, c + nrm * 0.0048, (0.0050, 0.0050, 0.0042), lambda q: T.sub_uv_cloth(q), rings=4, seg=8)
    # two tails
    for k, tilt in enumerate((0.55, 1.15)):
        td = (tan * math.cos(tilt) + Vector((0, 0, -1)) * math.sin(tilt)).normalized()
        k0 = c + nrm * 0.004
        path = [k0, k0 + td * 0.016 + nrm * 0.006, k0 + td * 0.030 + nrm * 0.010 + Vector((0, 0, -0.004 * (k + 1))),
                k0 + td * 0.040 + nrm * 0.011 + Vector((0, 0, -0.016 - 0.01 * k))]
        sp = hermite(path, 6)
        wl = [lerp(0.0062, 0.0050, i / 5) for i in range(6)]
        tube(M, sp, wl, [0.0014] * 6, seg=4, ref=nrm, rect=cloth, caps=(True, True))


def build_strand(M, anchor_y_front):
    """One stray strand: falls from the fringe down the -x cheek (BVH ray cast onto hair and skull), then lifts off the
    face and finishes in a small curl."""
    me = bpy.data.meshes.new("tmp_head")
    M.bm.to_mesh(me)
    bm2 = bmesh.new()
    bm2.from_mesh(me)
    tree = BVHTree.FromBMesh(bm2)
    pts = []
    n = 22
    for q in range(n):
        u = q / (n - 1)
        x = -0.030 - 0.034 * u + 0.007 * math.sin(u * 8.0)
        z = 0.158 - 0.105 * u + 0.003 * math.sin(u * 6 + 1)
        hit = tree.ray_cast(Vector((x, -0.3, z)), Vector((0, 1, 0)))
        if hit[0] is None:
            continue
        pts.append(hit[0] + hit[1] * (0.0016 + 0.0016 * u))
    bm2.free()
    bpy.data.meshes.remove(me)
    # flyaway curl at the end, lifting off the cheek
    last = pts[-1]
    ctr = last + Vector((-0.002, -0.006, -0.008))
    for q in range(1, 13):
        ang = 0.5 + q * 0.55
        r = 0.0085 * (1 - q / 16)
        pts.append(ctr + Vector((-r * math.sin(ang) * 0.8, -0.0035 * q, r * math.cos(ang)))
                   - Vector((0, 0, r)) * 0.0 - Vector((0, 0, 0.0004 * q)))
    m = len(pts)
    rr = [lerp(0.0016, 0.0006, (i / (m - 1)) ** 0.8) for i in range(m)]
    tube(M, pts, rr, rr, seg=5, ref=Vector((0, 0, 1)), rect=T.sub("ringlet"), caps=(True, True))


# ------------------------------------------------------------------------------------------------ the key

def build_key():
    """Brass key lying flat. Built in Unity-oriented coords (xu, yu, zu) then converted: bow at -xu, bit at +xu, -zu."""
    M = Mesh()
    U = lambda x, y, z: Vector((-(x + 0.010), -z, y))
    rect = T.sub("key")
    uv = rect_uv(rect)
    # shaft + collars along x (Unity), radii profile
    xs = [-0.0345, -0.0310, -0.0290, -0.0230, 0.0000, 0.0300, 0.0340, 0.0355, 0.0385, 0.0415]
    rs = [0.0030, 0.0030, 0.0044, 0.0044, 0.0027, 0.0027, 0.0042, 0.0042, 0.0030, 0.0027]
    pts = [U(x, 0, 0) for x in xs]
    tube(M, pts, rs, rs, seg=8, ref=Vector((0, 0, 1)), rect=rect, caps=(False, True))
    # bow: a ring in the xz plane (a trefoil would be nicer; a ring with three bobbles reads fine)
    R0, r0 = 0.0125, 0.0026
    ring_pts = []
    n = 18
    for i in range(n):
        a = 2 * math.pi * i / n
        ring_pts.append(U(-0.0335 - R0 + R0 * math.cos(a), 0.0, R0 * math.sin(a)))
    loop = ring_pts + ring_pts[:3]
    # tube along the closed loop (handled as a long open tube whose ends overlap)
    tube(M, loop, [r0 * (1.0 + 0.45 * (1 if i % 6 == 0 else 0)) for i in range(len(loop))], [r0] * len(loop), seg=6,
         ref=Vector((0, 0, 1)), rect=rect, caps=(False, False))
    # bit: a flat plate with two notches, extruded in y
    outline = [(0.0385, 0.0), (0.0385, -0.0140), (0.0345, -0.0140), (0.0345, -0.0095), (0.0300, -0.0095),
               (0.0300, -0.0140), (0.0258, -0.0140), (0.0258, -0.0052), (0.0230, -0.0052), (0.0230, 0.0)]
    t = 0.0017
    top = [M.v(U(x, t, z)) for x, z in outline]
    bot = [M.v(U(x, -t, z)) for x, z in outline]
    cuv = lambda i: uv(0.5 + 0.1 * outline[i][0] / 0.04, 0.5 + 0.1 * outline[i][1] / 0.02)
    M.face(top, [cuv(i) for i in range(len(top))], flat=True)
    M.face(bot[::-1], [cuv(i) for i in reversed(range(len(bot)))], flat=True)
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        M.face([top[i], bot[i], bot[j], top[j]], [uv(0.2, 0.2), uv(0.2, 0.3), uv(0.3, 0.3), uv(0.3, 0.2)], flat=True)
    bmesh.ops.recalc_face_normals(M.bm, faces=M.bm.faces)
    return M


# ------------------------------------------------------------------------------------------------ scene, export

def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for d in list(coll):
            coll.remove(d)


def material():
    m = bpy.data.materials.new("LilysDoll")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    def img(name, srgb):
        p = os.path.join(TEX, name)
        if not os.path.exists(p):
            return None
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(p, check_existing=True)
        t.image.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
        return t
    base, nrm, mask = img("LilysDoll_Base.png", True), img("LilysDoll_Normal.png", False), img("LilysDoll_Mask.png", False)
    if base:
        nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    if nrm:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if mask:
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(mask.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(mask.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    bsdf.inputs["Specular IOR Level"].default_value = 0.5
    return m


def export(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_tspace=True, use_mesh_modifiers=True, add_leaf_bones=False,
        bake_anim=False, path_mode='STRIP', use_custom_props=False)


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    e.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(e)
    if parent:
        e.parent = parent            # parents here are never rotated or scaled, so local = world - parent
        e.location = loc - parent.location
    else:
        e.location = loc
    return e


def build_doll(mat):
    body = build_body()
    head, face_local = build_head()
    # the strand needs the skull to land on, so it joins the head after the skull exists
    build_strand(head, face_local)
    tilt = Matrix.Rotation(math.radians(TILT_NOD), 4, 'X') @ Matrix.Rotation(math.radians(TILT_ROLL), 4, 'Y')
    head.apply(tilt)
    face_pos = tilt @ Vector((face_local.x, -face_local.y, face_local.z))
    n_body, n_head = body.tris(), head.tris()

    root = empty("LilysDoll", Vector((0, 0, 0)))
    ob = body.obj("Body")
    ob.parent = root
    ob.data.materials.append(mat)
    lap = empty("Lap", P(0, 0.135, 0.1015), root)
    hd = empty("Head", NECK, root)
    oh = head.obj("HeadMesh")
    oh.parent = root
    oh.location = NECK
    oh.data.materials.append(mat)
    empty("Face", NECK + face_pos, root)
    bpy.context.view_layer.update()
    return root, (n_body, n_head)


def seat_dummy():
    """A chair seat for the renders only."""
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.scale = (0.21, 0.21, 0.02)
    o.location = (0, 0.1, -0.02)
    o.name = "SeatDummy"
    m = bpy.data.materials.new("seat")
    m.diffuse_color = (0.12, 0.07, 0.04, 1)
    o.data.materials.append(m)
    return o


def render(out_dir, root):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 48
    sc.cycles.use_denoising = True
    sc.cycles.device = 'CPU'
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Filmic' if 'Filmic' in bpy.types.ColorManagedViewSettings.bl_rna.properties['view_transform'].enum_items.keys() else 'Standard'
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.012, 0.014, 0.02, 1)
    bg.inputs[1].default_value = 1.0
    sc.world = world

    def light(name, kind, loc, energy, col, size=0.3):
        d = bpy.data.lights.new(name, kind)
        d.energy = energy
        d.color = col
        if kind == 'POINT':
            d.shadow_soft_size = size
        o = bpy.data.objects.new(name, d)
        o.location = loc
        sc.collection.objects.link(o)
        return o
    # dim warm lamp ahead and above (the Unity DollLight), a cool window fill from the side, a rim from behind
    light("key", 'POINT', Vector((0.25, -0.9, 1.25)), 55, (1.0, 0.74, 0.5), 0.12)
    light("fill", 'POINT', Vector((-1.8, -0.4, 0.7)), 10, (0.55, 0.65, 1.0), 0.4)
    light("rim", 'POINT', Vector((0.5, 1.3, 0.9)), 14, (1.0, 0.8, 0.6), 0.2)
    seat_dummy()
    km = build_key()
    ko = km.obj("KeyPreview")
    ko.data.materials.append(bpy.data.materials["LilysDoll"])
    ko.location = P(0, 0.138, 0.1085)
    ko.rotation_euler = (0, 0, math.radians(-20))

    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam

    def shot(name, pos, target, lens, res=(900, 900)):
        cam.location = pos
        cam.data.lens = lens
        d = (target - pos).normalized()
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
    tgt = Vector((0, -0.1, 0.24))
    shot("front", Vector((0.0, -1.15, 0.30)), tgt, 62)
    shot("three_quarter", Vector((-0.75, -0.85, 0.42)), tgt, 62)
    shot("face", Vector((0.05, -0.55, 0.36)), Vector((0.0, -0.1, 0.34)), 70)
    shot("hands", Vector((0.0, -0.55, 0.40)), Vector((0.0, -0.12, 0.11)), 70)
    shot("side", Vector((1.2, -0.25, 0.28)), tgt, 62)
    # doorway: 4 m away, game-like fov, then cropped by the narrower lens in a second shot
    shot("doorway", Vector((0.3, -4.0, 1.5)), Vector((0, -0.1, 0.3)), 24, (1280, 720))
    shot("doorway_zoom", Vector((0.3, -4.0, 1.5)), Vector((0, -0.1, 0.3)), 75, (900, 900))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    clear()
    mat = material()
    root, (nb, nh) = build_doll(mat)
    print(f"DOLL tris: body={nb} head={nh} total={nb + nh}")
    if "--render" in argv:
        render(argv[argv.index("--render") + 1], root)
    elif "--no-export" not in argv:
        pass
    if "--no-export" not in argv:
        export("LilysDoll", [root] + list(root.children_recursive))
        # the key
        for o in list(bpy.context.scene.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        km = build_key()
        ko = km.obj("LilysKey")
        ko.data.materials.append(mat)
        print(f"KEY tris: {km.tris()}")
        export("LilysKey", [ko])


if __name__ == "__main__":
    main()
