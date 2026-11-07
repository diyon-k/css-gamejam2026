"""BLINK Bungalow modular kit (Phase 3): the house shell built from kit pieces, all driven by bungalow_layout.

  blender -b --factory-startup --python tools/blender/build_all.py -- --target bungalow Bungalow_Shell Bungalow_Kit
          [--no-wear] [--preview DIR]

-> Unity/Assets/Art/Environment/Bungalow/Kit/Bungalow_Shell.fbx            the whole shell (replaces the grey-box)
   Unity/Assets/Art/Environment/Bungalow/Kit/Bungalow_Shell_colliders.json box colliders (identical to the grey-box)
   Unity/Assets/Art/Environment/Bungalow/Kit/Bungalow_Kit.fbx              one of each piece type, at the origin
   Unity/Assets/Art/Environment/Bungalow/Textures/KitWear/KitWear.png      shared wear atlas on UV2 (bungalow_wear)

Pieces (each generated from the layout tables, nothing placed by hand):
  walls (real thickness, openings cut from OPENINGS, faces skinned per room from ROOM_FINISH incl. bands),
  floors (plank boards cut on the scan's seams with sag + chipped edges, parquet / lino / concrete slabs), ceilings,
  skirting / dado / tile cap / picture rail / cornice runs (broken at openings, wrapped round the chimney breast),
  door casings + linings + stops + plinths, door leaves (4-panel, D1 double with one leaf hung 3 deg off, ledged D5),
  windows (frames, double-hung sashes, glass, WL1 boards, basement hoppers), arch casings A1-A4 with keystones and
  corbels, the O1 cased opening with its transom, the stair (bullnosed treads with chipped nosings, risers, two
  stringers, under-stair boarding), turned balusters (two missing), handrails, newels (NE cap cracked), the G1 gate,
  joists / trimmers, radiators + basement pipes, the fireplace (brick breast, lintel, hearth, moulded mantel), water.

Coordinates: geometry is authored in Unity metres (x east, y up, z north) and mapped into Blender with U2B (a mirror),
so faces are wound outward in Unity space and reversed on emit. Materials are the SPEC 3 names (M_*); UV0 follows the
contract in bungalow_layout.SURFACE_TILE_M (surfaces) and bungalow_trim.STRIPS (M_Trim); UV2 is the wear / lightmap
atlas (bungalow_wear.pack_uv2). Colliders are the grey-box boxes, taken from bungalow_greybox so the navmesh is
identical; door leaves add nav-ignored boxes. Importing this module has no side effects.
"""
import json
import math
import os
import random
import sys
import zlib

import bmesh
import bpy
from mathutils import Matrix, Vector

import bungalow_layout as L
import bungalow_trim as T
import lib

BUILDERS = {}
SUBDIR = {}
SHARP = {}
OPTS = dict(wear=True)            # build_all passes --no-wear through here
TRIM_M = T.TRIM_UV_M
UP = Vector((0.0, 1.0, 0.0))
_STATE = dict(colliders={}, nav_ignore=[], non_static=[], pivots={}, tris={})
rng = random.Random(31)


def U2B(p):
    return Vector((-p[0], -p[2], p[1]))


def V3(x, y, z):
    return Vector((float(x), float(y), float(z)))


# ------------------------------------------------------------------ mesh builder (Unity coordinates)
class MB:
    """Accumulates faces (Unity coords, outward winding) with a material, UV0 per corner and a polish value."""

    def __init__(self, name):
        self.name = name
        self.v = []
        self.f = []
        self.xf = None            # optional Matrix applied to every point (rotation + translation, no mirror)

    def face(self, pts, mat, uvs, polish=0.0):
        if self.xf is not None:
            pts = [self.xf @ Vector(p) for p in pts]
        i0 = len(self.v)
        self.v.extend(Vector(p) for p in pts)
        self.f.append((list(range(i0, i0 + len(pts))), mat, [tuple(u) for u in uvs], float(polish)))

    def extend(self, other):
        off = len(self.v)
        self.v.extend(other.v)
        self.f.extend(([i + off for i in idx], m, uv, p) for idx, m, uv, p in other.f)


def newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i, a in enumerate(pts):
        b = pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def center(pts):
    c = Vector((0.0, 0.0, 0.0))
    for p in pts:
        c += p
    return c / len(pts)


# ------------------------------------------------------------------ UV functions
def right_of(n):
    """Viewer's right on a face with normal n (Unity numeric space, Y up)."""
    r = Vector((-n.z, 0.0, n.x))
    return r.normalized() if r.length > 1e-6 else Vector((1.0, 0.0, 0.0))


def uv_world(mat, pts, n):
    """World box mapping at 1 UV = one tile of the material (SURFACE_TILE_M)."""
    t = L.SURFACE_TILE_M.get(mat, 1.0)
    a = max(range(3), key=lambda i: abs(n[i]))
    if a == 1:
        return [(p.x / t, p.z / t) for p in pts]
    r = right_of(Vector((n.x, 0.0, n.z)))
    return [(p.dot(r) / t, p.y / t) for p in pts]


class TrimUV:
    """Trim-sheet mapping for one part: long axis -> U (grain), cross-section -> V fitted inside the strip."""

    def __init__(self, strip, pts, frame=None, uoff=None):
        self.v0, self.v1 = T.v_range(strip)
        if frame is None:
            lo = [min(p[i] for p in pts) for i in range(3)]
            hi = [max(p[i] for p in pts) for i in range(3)]
            ext = [hi[i] - lo[i] for i in range(3)]
            order = sorted(range(3), key=lambda i: -ext[i])
            ax = [Vector([1.0 if j == i else 0.0 for j in range(3)]) for i in order]
            frame = ax
        self.F = [Vector(f).normalized() for f in frame]
        loc = [[p.dot(f) for p in pts] for f in self.F]
        self.lo = [min(c) for c in loc]
        self.ext = [max(max(c) - min(c), 1e-4) for c in loc]
        self.uoff = rng.random() if uoff is None else uoff

    def _v(self, k, c):
        s = min(1.0 / TRIM_M, (self.v1 - self.v0) / self.ext[k])
        return self.v0 + (c - self.lo[k]) * s

    def __call__(self, pts, n):
        d = [abs(n.dot(f)) for f in self.F]
        k = max(range(3), key=lambda i: d[i])
        out = []
        for p in pts:
            c = [p.dot(f) for f in self.F]
            if k == 0:          # end grain: cross-section into the strip
                out.append((c[1] / TRIM_M + self.uoff, self._v(2, c[2])))
            elif k == 1:
                out.append((c[0] / TRIM_M + self.uoff, self._v(2, c[2])))
            else:
                out.append((c[0] / TRIM_M + self.uoff, self._v(1, c[1])))
        return out


def absorb(mb, bm, mat, uvfn, polish=None):
    """Copy a Unity-space bmesh into mb. mat: name or fn(c, n); uvfn(pts, n) or fn(mat)->uvfn; polish fn(c, n)."""
    bm.normal_update()
    for f in bm.faces:
        pts = [Vector(v.co) for v in f.verts]
        n = f.normal.copy()
        c = center(pts)
        m = mat(c, n) if callable(mat) else mat
        fn = uvfn(m) if getattr(uvfn, "per_mat", False) else uvfn
        mb.face(pts, m, fn(pts, n), polish(c, n) if polish else 0.0)
    bm.free()


def world_uv():
    def f(m):
        return lambda pts, n: uv_world(m, pts, n)
    f.per_mat = True
    return f


# ------------------------------------------------------------------ bmesh primitives (Unity space)
def bm_box(x, y, z, bev=0.0, seg=1):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    sx, sy, sz = x[1] - x[0], y[1] - y[0], z[1] - z[0]
    for v in bm.verts:
        v.co = Vector(((x[0] + x[1]) / 2 + v.co.x * sx, (y[0] + y[1]) / 2 + v.co.y * sy,
                       (z[0] + z[1]) / 2 + v.co.z * sz))
    if bev > 0:
        bev = min(bev, min(sx, sy, sz) * 0.45)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev, segments=seg, profile=0.5, affect='EDGES',
                        clamp_overlap=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def trim_box(mb, x, y, z, strip, bev=0.004, mat="M_Trim", polish=None, frame=None, seg=1, uoff=None):
    bm = bm_box(x, y, z, bev, seg)
    uv = TrimUV(strip, [v.co for v in bm.verts], frame, uoff)
    absorb(mb, bm, mat, uv, polish)


def bm_lathe(prof, base, axis=(0, 1, 0), seg=10, ref=None):
    """Revolve [(r, h)] about axis through base. Poles where r == 0. Closed solid when the profile starts and ends
    on the axis."""
    ax = Vector(axis).normalized()
    e1 = Vector(ref) if ref else (Vector((1, 0, 0)) if abs(ax.x) < 0.9 else Vector((0, 0, 1)))
    e1 = (e1 - ax * e1.dot(ax)).normalized()
    e2 = ax.cross(e1)
    base = Vector(base)
    bm = bmesh.new()
    rings = []
    for r, h in prof:
        if r < 1e-6:
            rings.append([bm.verts.new(base + ax * h)])
        else:
            rings.append([bm.verts.new(base + ax * h + (e1 * math.cos(a) + e2 * math.sin(a)) * r)
                          for a in (2 * math.pi * i / seg for i in range(seg))])
    for A, B in zip(rings, rings[1:]):
        if len(A) == 1 and len(B) == 1:
            continue
        for i in range(seg):
            j = (i + 1) % seg
            if len(A) == 1:
                bm.faces.new((A[0], B[j], B[i]))
            elif len(B) == 1:
                bm.faces.new((A[i], A[j], B[0]))
            else:
                bm.faces.new((A[i], A[j], B[j], B[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def trim_lathe(mb, prof, base, strip, axis=(0, 1, 0), seg=10, polish=None, mat="M_Trim"):
    bm = bm_lathe(prof, base, axis, seg)
    ax = Vector(axis).normalized()
    e1 = Vector((1, 0, 0)) if abs(ax.x) < 0.9 else Vector((0, 0, 1))
    e1 = (e1 - ax * e1.dot(ax)).normalized()
    absorb(mb, bm, mat, TrimUV(strip, [v.co for v in bm.verts], (ax, e1, ax.cross(e1))), polish)


def bm_prism(pts2, plane, t_rng):
    """Closed 2D outline [(p, q)] in a plane (fn (p, q, t) -> Vector), extruded over t_rng."""
    bm = bmesh.new()
    a = [bm.verts.new(plane(p, q, t_rng[0])) for p, q in pts2]
    b = [bm.verts.new(plane(p, q, t_rng[1])) for p, q in pts2]
    bm.faces.new(a)
    bm.faces.new(b[::-1])
    n = len(pts2)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], b[i], b[j], a[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


# ------------------------------------------------------------------ sweep (mouldings, casings, linings, treads)
def poly_area(q):
    return 0.5 * sum(q[i][0] * q[(i + 1) % len(q)][1] - q[(i + 1) % len(q)][0] * q[i][1] for i in range(len(q)))


def sweep(mb, path, W, profile, mat, strip=None, vmap=None, away=None, back=True, ribbon=False, caps=True,
          polish=None, profiles=None, umul=1.0 / TRIM_M, uoff=None, closed=False):
    """Sweep a 2D profile [(s, t)] along a polyline. s runs along N = W x T (mitred, pointed away from `away`),
    t along the constant out vector W. back=True: the closing edge (last -> first) lies on a wall and is not built.
    ribbon: a 2-point profile built as one strip facing -N. profiles: one profile per path point (chips).
    UVs: u = path length * umul, v = profile length fitted into the strip (or vmap=(v0, v1))."""
    W = Vector(W).normalized()
    P = [Vector(p) for p in path]
    n = len(P)
    if closed:
        P = P + [P[0]]
        n += 1
    Ts = [(P[i + 1] - P[i]).normalized() for i in range(n - 1)]
    Ns = []
    for i in range(n):
        ns = []
        if i > 0:
            ns.append(W.cross(Ts[i - 1]).normalized())
        elif closed:
            ns.append(W.cross(Ts[-1]).normalized())
        if i < n - 1:
            ns.append(W.cross(Ts[i]).normalized())
        elif closed:
            ns.append(W.cross(Ts[0]).normalized())
        m = (ns[0] + ns[-1]).normalized()
        Ns.append(m / max(m.dot(ns[0]), 0.35))
    if away is not None and Ns[0].dot(P[0] - Vector(away)) < 0:
        Ns = [-v for v in Ns]
    profs = profiles or [profile] * n
    if not ribbon and poly_area(profs[0]) < 0:
        profs = [p[::-1] for p in profs]
    prof0 = profs[0]
    k = len(prof0)
    edges = list(range(k - 1)) if (back or ribbon) else list(range(k))
    seglen = [math.dist(prof0[j], prof0[(j + 1) % k]) for j in edges]
    tot = sum(seglen) or 1.0
    if vmap is None:
        v0, v1 = T.v_range(strip)
        vs = min(1.0 / TRIM_M, (v1 - v0) / tot)
    else:
        v0, v1 = vmap
        vs = (v1 - v0) / tot
    vcum = [v0]
    for s in seglen:
        vcum.append(vcum[-1] + s * vs)
    uo = rng.random() if uoff is None else uoff
    ucum = [0.0]
    for i in range(n - 1):
        ucum.append(ucum[-1] + (P[i + 1] - P[i]).length)

    def pt(i, j):
        s, t = profs[i][j % k]
        return P[i] + Ns[i] * s + W * t

    for i in range(n - 1):
        Tm = Ts[i]
        Nm = (Ns[i] + Ns[i + 1]).normalized()
        for jj, j in enumerate(edges):
            q = [pt(i, j), pt(i + 1, j), pt(i + 1, j + 1), pt(i, j + 1)]
            uv = [(ucum[i] * umul + uo, vcum[jj]), (ucum[i + 1] * umul + uo, vcum[jj]),
                  (ucum[i + 1] * umul + uo, vcum[jj + 1]), (ucum[i] * umul + uo, vcum[jj + 1])]
            if ribbon:
                exp = -Nm
            else:
                a, b = prof0[j], prof0[(j + 1) % k]
                es, et = b[0] - a[0], b[1] - a[1]
                exp = Nm * et - W * es
            nn = newell(q)
            if nn.length < 1e-12:
                continue
            if nn.dot(exp) < 0:
                q, uv = q[::-1], uv[::-1]
            c = center(q)
            mb.face(q, mat, uv, polish(c, nn.normalized()) if polish else 0.0)
    if caps and not ribbon and not closed:
        for i, sgn in ((0, -1), (n - 1, 1)):
            q = [pt(i, j) for j in range(k)]
            Tc = Ts[0] if i == 0 else Ts[-1]
            nn = newell(q)
            if nn.length < 1e-12:
                continue
            if nn.dot(Tc * sgn) < 0:
                q = q[::-1]
            pr = [profs[i][j] for j in range(k)]
            if nn.dot(Tc * sgn) < 0:
                pr = pr[::-1]
            smin = min(p[0] for p in pr)
            tmin = min(p[1] for p in pr)
            uv = [((p[0] - smin) * umul + uo, v0 + (p[1] - tmin) * vs) for p in pr]
            mb.face(q, mat, uv, polish(center(q), Tc * sgn) if polish else 0.0)


# ------------------------------------------------------------------ layout helpers
def wall_axes(w):
    return (0, 2) if w["axis"] == "x" else (2, 0)


def wp(w, a, y, t):
    """Point in a wall's frame: a along the wall, y up, t across (thickness axis coordinate)."""
    return V3(a, y, t) if w["axis"] == "x" else V3(t, y, a)


def wall_dirs(w):
    """(along unit, thickness unit) vectors."""
    return (Vector((1, 0, 0)), Vector((0, 0, 1))) if w["axis"] == "x" else (Vector((0, 0, 1)), Vector((1, 0, 0)))


def room_at(p, pad=0.0):
    for rid, r in L.ROOMS.items():
        lv = L.LEVELS[r["level"]]
        top = lv["ceiling"]
        if (r["x"][0] - pad < p.x < r["x"][1] + pad and r["z"][0] - pad < p.z < r["z"][1] + pad
                and lv["floor"] - 1e-3 < p.y < top + 1e-3):
            return rid
    return None


def style_of(rid, part):
    fin = L.ROOM_FINISH.get(rid) if rid else None
    return L.TRIM_STYLE[fin["trim"] if fin else "paint_light"][part]


def side_of_normal(n):
    if abs(n.z) > abs(n.x):
        return "N" if n.z < 0 else "S"
    return "E" if n.x < 0 else "W"


def surface_mat(c, n):
    """Wall-surface material from the room the face looks into (ROOM_FINISH wall + bands)."""
    if abs(n.y) > 0.7:
        return L.EXTERIOR_FINISH
    q = c + n.normalized() * 0.06
    rid = room_at(q)
    if rid is None:
        fp = L.FOOTPRINT
        return L.EXTERIOR_FINISH
    fin = L.ROOM_FINISH[rid]
    side = side_of_normal(n)
    along = c.x if side in "NS" else c.z
    for b in fin.get("bands", []):
        if b.get("side") and b["side"] != side:
            continue
        if b.get("span") and not (b["span"][0] - 1e-4 <= along <= b["span"][1] + 1e-4):
            continue
        if b["y"][0] - 1e-4 <= c.y <= b["y"][1] + 1e-4:
            return b["mat"]
    return fin["wall"]


def band_cuts(axis):
    """Extra cut lines for wall panels: (along cuts, y cuts) at room edges, band edges and floor / ceiling levels."""
    al, ys = set(), {0.0, -L.SLAB, L.CEILING, L.BASEMENT}
    for rid, r in L.ROOMS.items():
        rng_ = r["x"] if axis == "x" else r["z"]
        al.update(rng_)
        for b in L.ROOM_FINISH[rid].get("bands", []):
            ys.update(b["y"])
            if b.get("span"):
                al.update(b["span"])
    return sorted(al), sorted(ys)


def panel_cut(mb, rng3, t_axis, holes, matfn, cuts_p=(), cuts_q=(), skip=None, split=None):
    """Axis-aligned block with rectangular through-holes along t_axis, cut on extra grid lines so every face lies
    in one material zone. holes = [((p0, p1), (q0, q1))] in the two other axes (ascending axis order).
    matfn(c, n) -> material; skip(c, n) -> True to drop a face (hidden faces stay out of the UV2 atlas);
    split(c, n) -> another MB to put the face in (or None). UVs: world mapping per material."""
    p_ax, q_ax = [i for i in range(3) if i != t_axis]
    Pr, Qr, Tr = rng3[p_ax], rng3[q_ax], rng3[t_axis]
    ps = sorted({Pr[0], Pr[1], *(v for h in holes for v in h[0] if Pr[0] < v < Pr[1]),
                 *(v for v in cuts_p if Pr[0] + 1e-4 < v < Pr[1] - 1e-4)})
    qs = sorted({Qr[0], Qr[1], *(v for h in holes for v in h[1] if Qr[0] < v < Qr[1]),
                 *(v for v in cuts_q if Qr[0] + 1e-4 < v < Qr[1] - 1e-4)})
    ni, nj = len(ps) - 1, len(qs) - 1

    def in_hole(pm, qm):
        return any(h[0][0] < pm < h[0][1] and h[1][0] < qm < h[1][1] for h in holes)

    solid = [[not in_hole((ps[i] + ps[i + 1]) / 2, (qs[j] + qs[j + 1]) / 2) for j in range(nj)] for i in range(ni)]

    def Vt(i, j, k):
        c = [0.0, 0.0, 0.0]
        c[p_ax], c[q_ax], c[t_axis] = ps[i], qs[j], Tr[k]
        return Vector(c)

    def S(i, j):
        return 0 <= i < ni and 0 <= j < nj and solid[i][j]

    tdir = Vector([1.0 if a == t_axis else 0.0 for a in range(3)])
    pdir = Vector([1.0 if a == p_ax else 0.0 for a in range(3)])
    qdir = Vector([1.0 if a == q_ax else 0.0 for a in range(3)])

    def add(q, n):
        nn = newell(q)
        if nn.dot(n) < 0:
            q = q[::-1]
        c = center(q)
        if skip and skip(c, n):
            return
        m = matfn(c, n)
        (split and split(c, n) or mb).face(q, m, uv_world(m, q, n))

    for i in range(ni):
        for j in range(nj):
            if not solid[i][j]:
                continue
            add([Vt(i, j, 0), Vt(i + 1, j, 0), Vt(i + 1, j + 1, 0), Vt(i, j + 1, 0)], -tdir)
            add([Vt(i, j, 1), Vt(i + 1, j, 1), Vt(i + 1, j + 1, 1), Vt(i, j + 1, 1)], tdir)
            if not S(i - 1, j):
                add([Vt(i, j, 0), Vt(i, j + 1, 0), Vt(i, j + 1, 1), Vt(i, j, 1)], -pdir)
            if not S(i + 1, j):
                add([Vt(i + 1, j, 0), Vt(i + 1, j + 1, 0), Vt(i + 1, j + 1, 1), Vt(i + 1, j, 1)], pdir)
            if not S(i, j - 1):
                add([Vt(i, j, 0), Vt(i + 1, j, 0), Vt(i + 1, j, 1), Vt(i, j, 1)], -qdir)
            if not S(i, j + 1):
                add([Vt(i, j + 1, 0), Vt(i + 1, j + 1, 0), Vt(i + 1, j + 1, 1), Vt(i, j + 1, 1)], qdir)


def arc(a0, a1, spring, crown, n=16, grow=0.0):
    w, h = a1 - a0, crown - spring
    R = (w * w / 4 + h * h) / (2 * h)
    am, cy = (a0 + a1) / 2, crown - R
    half = math.asin(min(1.0, (w / 2) / R))
    return [(am + (R + grow) * math.sin(-half + 2 * half * i / n), cy + (R + grow) * math.cos(-half + 2 * half * i / n))
            for i in range(n + 1)]


# ------------------------------------------------------------------ emit to Blender
_MATS = {}


def kit_mat(name):
    """Placeholder Blender material (flat preview colour); Unity remaps the name onto the real .mat."""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        col = PREVIEW_COL.get(name, (0.5, 0.5, 0.5))
        m.diffuse_color = (*col, 1.0)
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*[c ** 2.2 for c in col], 1)
    return m


PREVIEW_COL = {"M_FloorPlank": (0.30, 0.22, 0.15), "M_Parquet": (0.45, 0.30, 0.18), "M_Lino": (0.55, 0.53, 0.45),
               "M_Concrete": (0.45, 0.45, 0.43), "M_Wall_Damask": (0.42, 0.08, 0.07), "M_Wall_Pale": (0.72, 0.68, 0.55),
               "M_Wall_Floral": (0.36, 0.45, 0.33), "M_Plaster": (0.78, 0.77, 0.72), "M_BrickWhite": (0.80, 0.78, 0.74),
               "M_Plaster_Kitchen": (0.70, 0.75, 0.78),
               "M_Tile": (0.85, 0.85, 0.82), "M_Trim": (0.25, 0.15, 0.09), "M_Glass": (0.5, 0.6, 0.65),
               "M_Water": (0.12, 0.13, 0.10)}


def emit(mb, name=None):
    name = name or mb.name
    if not mb.f:
        return None
    verts = [U2B(p) for p in mb.v]
    faces = [idx[::-1] for idx, _, _, _ in mb.f]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    mats = sorted({m for _, m, _, _ in mb.f})
    for m in mats:
        me.materials.append(kit_mat(m))
    mi = {m: i for i, m in enumerate(mats)}
    uvl = me.uv_layers.new(name="UVMap")
    pol = me.attributes.new("polish", 'FLOAT', 'FACE')
    for poly, (idx, m, uvs, p) in zip(me.polygons, mb.f):
        poly.material_index = mi[m]
        pol.data[poly.index].value = p
        for k, li in enumerate(range(poly.loop_start, poly.loop_start + poly.loop_total)):
            uvl.data[li].uv = uvs[::-1][k]
    me.validate(clean_customdata=False)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], ngon_method='BEAUTY')
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def collide(name, x, y, z, r=(0.0, 0.0, 0.0)):
    _STATE["colliders"].setdefault(name, []).append(dict(
        c=[round((x[0] + x[1]) / 2, 4), round((y[0] + y[1]) / 2, 4), round((z[0] + z[1]) / 2, 4)],
        s=[round(x[1] - x[0], 4), round(y[1] - y[0], 4), round(z[1] - z[0], 4)], r=[round(v, 4) for v in r]))


# ------------------------------------------------------------------ walls
def outside_footprint(p):
    return not (0.0 < p.x < L.FOOTPRINT[0] and 0.0 < p.z < L.FOOTPRINT[1])


def build_walls():
    """Wall runs; outside faces go to <wall>_Outside (low UV2 density), hidden faces (tops, bottoms, basement
    faces against the ground) are not built."""
    out = []
    for wid, w in L.WALLS.items():
        mb = MB(wid)
        outside = MB(wid + "_Outside")
        basement = wid.startswith("Wall_B_")

        def skip(c, n, basement=basement):
            if abs(n.y) > 0.7:
                return True
            return basement and room_at(c + n * 0.06) is None

        def split(c, n, outside=outside):
            return outside if outside_footprint(c + n * 0.06) else None
        al, th = wall_axes(w)
        holes = []
        for oid in w["openings"]:
            o = L.OPENINGS[oid]
            holes.append((o["span"], o["y"]) if w["axis"] == "x" else (o["y"], o["span"]))
        rng3 = [None, None, None]
        rng3[al], rng3[1], rng3[th] = w["a"], w["y"], w["t"]
        cuts_a, cuts_y = band_cuts(w["axis"])
        if w["axis"] == "x":
            panel_cut(mb, rng3, th, holes, surface_mat, cuts_p=cuts_a, cuts_q=cuts_y, skip=skip, split=split)
        else:
            panel_cut(mb, rng3, th, holes, surface_mat, cuts_p=cuts_y, cuts_q=cuts_a, skip=skip, split=split)
        # arch spandrels: wall material on both faces, plaster on the hidden arc underside (lined in wood)
        for oid in w["openings"]:
            o = L.OPENINGS[oid]
            if o["kind"] != "arch":
                continue
            (a0, a1), (y0, crown), sp = o["span"], o["y"], o["spring"]
            pts = arc(a0, a1, sp, crown)
            h = len(pts) // 2
            for poly in ([(a0, crown)] + pts[:h + 1], pts[h:] + [(a1, crown)]):
                bm = bm_prism(poly, lambda p, q, t: wp(w, p, q, t), w["t"])
                absorb(mb, bm, surface_mat, world_uv())
        out.append(emit(mb))
        out.append(emit(outside))
    return out


# ------------------------------------------------------------------ floors and ceilings
SEAMS_V = sorted(1.0 - r / 1024.0 for r in L.FLOOR_PLANK_SEAMS_PX)


def rect_sub(r, holes):
    """Rectangle ((x0, x1), (z0, z1)) minus holes -> list of rectangles."""
    out = [r]
    for h in holes:
        nxt = []
        for (x0, x1), (z0, z1) in out:
            hx0, hx1 = max(x0, h[0][0]), min(x1, h[0][1])
            hz0, hz1 = max(z0, h[1][0]), min(z1, h[1][1])
            if hx0 >= hx1 or hz0 >= hz1:
                nxt.append(((x0, x1), (z0, z1)))
                continue
            if x0 < hx0:
                nxt.append(((x0, hx0), (z0, z1)))
            if hx1 < x1:
                nxt.append(((hx1, x1), (z0, z1)))
            if z0 < hz0:
                nxt.append(((hx0, hx1), (z0, hz0)))
            if hz1 < z1:
                nxt.append(((hx0, hx1), (hz1, z1)))
        out = nxt
    return out


def sag_at(rid, x, z):
    d = 0.0
    for s in L.DECAY["sag"]:
        if s["room"] != rid:
            continue
        (x0, x1), (z0, z1) = s["x"], s["z"]
        if x0 <= x <= x1 and z0 <= z <= z1:
            fx = math.sin(math.pi * (x - x0) / (x1 - x0))
            fz = math.sin(math.pi * (z - z0) / (z1 - z0))
            d = max(d, s["depth"] * fx * fz)
    return d


def plank_floor(mb, rid, r):
    """Boards cut on the M_FloorPlank scan's seams (UV0 world-aligned at 1 tile = 2 m), with per-board height
    jitter, sag zones from DECAY and chipped edges. Each board: top + two chamfers meeting the neighbour at the seam."""
    fin = L.ROOM_FINISH[rid]
    tile = L.SURFACE_TILE_M["M_FloorPlank"]
    along_x = fin["boards"] == "x"
    holes = [(h["x"], h["z"]) for h in r.get("floor_holes", [])]
    acr = r["z"] if along_x else r["x"]
    alg = r["x"] if along_x else r["z"]
    ov = 0.0
    # seams in metres across the room (v = c / tile for boards along x; v = -c / tile for boards along z)
    sgn = 1.0 if along_x else -1.0
    seams = []
    k0 = math.floor(sgn * acr[0] / tile) - 2
    for k in range(k0, k0 + int((acr[1] - acr[0]) / tile) + 6):
        for sv in SEAMS_V:
            c = sgn * (k + sv - ov) * tile
            if acr[0] < c < acr[1]:
                seams.append(c)
    edges = sorted(set([acr[0], acr[1]] + seams))
    ch, dep = 0.005, 0.007

    def P(a, c, y):
        return V3(a, y, c) if along_x else V3(c, y, a)

    def uvp(p):
        return (p.x / tile, p.z / tile) if along_x else (p.z / tile, -p.x / tile)

    for b0, b1 in zip(edges, edges[1:]):
        rects = rect_sub(((alg[0], alg[1]), (b0, b1)) if along_x else ((b0, b1), (alg[0], alg[1])), holes)
        dy = rng.uniform(-0.0015, 0.0015)
        tilt = rng.uniform(-0.0012, 0.0012)
        for rc in rects:
            a_r, c_r = (rc[0], rc[1]) if along_x else (rc[1], rc[0])
            c0, c1 = c_r
            seam0, seam1 = (c0 > acr[0] + 1e-6), (c1 < acr[1] - 1e-6)
            n_a = max(2, int(math.ceil((a_r[1] - a_r[0]) / 0.45)) + 1)
            secs = [a_r[0] + (a_r[1] - a_r[0]) * i / (n_a - 1) for i in range(n_a)]
            chips = {}
            nchip = int(rng.random() < 0.5) + int((a_r[1] - a_r[0]) * L.DECAY["chip_rate"] * rng.random())
            for _ in range(nchip):
                a = rng.uniform(a_r[0] + 0.05, a_r[1] - 0.05)
                side = rng.choice((0, 1))
                w = rng.uniform(0.02, 0.05)
                for da, f in ((-w, 0.0), (0.0, 1.0), (w * 0.7, 0.0)):
                    key = round(a + da, 4)
                    if f >= chips.get(key, (0, 0.0))[1]:
                        chips[key] = (side, f)
            secs = sorted(set([round(s, 4) for s in secs] + list(chips)))
            secs = [s for s in secs if a_r[0] - 1e-6 <= s <= a_r[1] + 1e-6]
            cross = [c0, c0 + ch, c1 - ch, c1]

            def ring(a):
                side, f = chips.get(round(a, 4), (0, 0.0))
                pts = []
                for k, c in enumerate(cross):
                    edge = (k == 0 and seam0) or (k == 3 and seam1)
                    y = (-dep if edge else 0.0) + dy + tilt * (c - (c0 + c1) / 2) / max(c1 - c0, 0.05)
                    cc = c
                    if f > 0 and ((side == 0 and k == 1) or (side == 1 and k == 2)):
                        cc = c + (0.016 if side == 0 else -0.016) * f
                        y -= 0.009 * f
                    y -= sag_at(rid, *((a, cc) if along_x else (cc, a)))
                    pts.append(P(a, cc, y))
                return pts

            rings = [ring(a) for a in secs]
            for i in range(len(rings) - 1):
                A, B = rings[i], rings[i + 1]
                for k in range(3):
                    q = [A[k], B[k], B[k + 1], A[k + 1]]
                    if newell(q).y < 0:
                        q = q[::-1]
                    mb.face(q, "M_FloorPlank", [uvp(p) for p in q])
            # board ends at hole edges are visible (the well): close them down to the slab
            for A, at_end in ((rings[0], False), (rings[-1], True)):
                a = A[0].x if along_x else A[0].z
                probe = a + (0.01 if at_end else -0.01)
                pp = P(probe, (c0 + c1) / 2, 0.0)
                if any(h[0][0] < pp.x < h[0][1] and h[1][0] < pp.z < h[1][1] for h in holes):
                    bot = [P(a, c, -0.03) for c in (c0, c1)]
                    q = [A[0], A[1], A[2], A[3], bot[1], bot[0]]
                    out = Vector((1, 0, 0)) if along_x else Vector((0, 0, 1))
                    out = out if at_end else -out
                    if newell(q).dot(out) < 0:
                        q = q[::-1]
                    mb.face(q, "M_FloorPlank", [((p.y) / tile, (p.z if along_x else p.x) / tile) for p in q])


def build_floors():
    out = []
    for rid, r in L.ROOMS.items():
        lv = L.LEVELS[r["level"]]
        fin = L.ROOM_FINISH[rid]
        y0 = lv["floor"]
        holes = [(h["x"], h["z"]) for h in r.get("floor_holes", [])]
        mb = MB("Floor_" + rid)
        planks = fin["floor"] == "M_FloorPlank"
        top = y0 - (0.007 if planks else 0.0)     # plank rooms: slab top = the boards' groove bottom

        def mat(c, n, fm=fin["floor"]):
            return fm if n.y > 0.7 else "M_Plaster"

        def skip(c, n, planks=planks, holes=holes, lvl=r["level"]):
            if n.y > 0.7:
                return planks
            if n.y < -0.7:      # slab underside: only where it is the basement ceiling
                return lvl != "ground" or not any(L.ROOMS[b]["x"][0] < c.x < L.ROOMS[b]["x"][1] and
                                                  L.ROOMS[b]["z"][0] < c.z < L.ROOMS[b]["z"][1] for b in ("B1", "B2"))
            q = c + n * 0.05    # slab edges: only at holes (the stair well), the rest are inside walls
            return not any(h[0][0] < q.x < h[0][1] and h[1][0] < q.z < h[1][1] for h in holes)
        bx = sorted({v for b in ("B1", "B2") for v in L.ROOMS[b]["x"]})
        bz = sorted({v for b in ("B1", "B2") for v in L.ROOMS[b]["z"]})
        panel_cut(mb, (r["x"], (y0 - lv["slab"], top), r["z"]), 1, holes, mat, cuts_p=bx, cuts_q=bz, skip=skip)
        if planks:
            plank_floor(mb, rid, r)
        out.append(emit(mb))
        if lv.get("ceiling_piece"):
            mb = MB("Ceiling_" + rid)
            ch = [(h["x"], h["z"]) for h in r.get("ceiling_holes", [])]
            panel_cut(mb, (r["x"], (lv["ceiling"], lv["ceiling_top"]), r["z"]), 1, ch,
                      lambda c, n, m=fin["ceiling"]: m, skip=lambda c, n: n.y > -0.7)
            out.append(emit(mb))
            _STATE["nav_ignore"].append("Ceiling_" + rid)
    ld = L.STAIR["landing"]
    mb = MB("Floor_B1_Landing")
    panel_cut(mb, (ld["x"], (L.BASEMENT - L.BASEMENT_SLAB, L.BASEMENT), ld["z"]), 1, [],
              lambda c, n: "M_Concrete" if n.y > 0.7 else "M_Plaster")
    out.append(emit(mb))
    # flood water (no collider, nav-ignored): one plane over both basement rooms, cut round the stair flight
    mb = MB("Basement_Water")
    wy = L.DECAY["water_y"]
    st = L.STAIR
    for rid in ("B1", "B2"):
        r = L.ROOMS[rid]
        for (x0, x1), (z0, z1) in rect_sub((r["x"], r["z"]), [(st["x"], (st["z_top"], st["z_bottom"] + 0.03))]):
            q = [V3(x0, wy, z0), V3(x0, wy, z1), V3(x1, wy, z1), V3(x1, wy, z0)]
            if newell(q).y < 0:
                q = q[::-1]
            mb.face(q, "M_Water", uv_world("M_Water", q, UP))
    out.append(emit(mb))
    _STATE["nav_ignore"].append("Basement_Water")
    return out


# ------------------------------------------------------------------ mouldings
PROFILES = {   # (depth out of the wall, height above MOULDING_Y)
    "skirting": [(0.0, 0.0), (0.022, 0.0), (0.022, 0.150), (0.017, 0.164), (0.021, 0.180), (0.018, 0.192),
                 (0.010, 0.204), (0.005, 0.212), (0.0, 0.215)],
    "dado": [(0.0, 0.0), (0.010, 0.0), (0.020, 0.010), (0.026, 0.024), (0.022, 0.038), (0.012, 0.048), (0.0, 0.050)],
    "tilecap": [(0.0, 0.0), (0.012, 0.0), (0.018, 0.010), (0.020, 0.020), (0.014, 0.032), (0.0, 0.036)],
    "picture": [(0.0, 0.0), (0.008, 0.0), (0.020, 0.008), (0.024, 0.022), (0.014, 0.030), (0.016, 0.040),
                (0.0, 0.046)],
    "cornice": [(0.0, 0.0), (0.012, 0.0), (0.016, 0.012), (0.020, 0.028), (0.024, 0.034)] +
               [(0.11 - 0.086 * math.cos(a), 0.034 + 0.072 * math.sin(a))
                for a in (math.pi / 2 * i / 6 for i in range(1, 7))] + [(0.118, 0.106), (0.118, 0.110), (0.0, 0.110)],
}


def opening_extent(o):
    """(along span incl. casing, y range incl. casing / sill / transom) of an opening, for breaking mouldings."""
    a0, a1 = o["span"]
    y0, y1 = o["y"]
    if o["kind"] == "window":
        return (a0 - 0.13, a1 + 0.13), (y0 - 0.20, y1 + 0.13)
    if o["kind"] == "transom":
        return (a0 - L.CASING - 0.01, a1 + L.CASING + 0.01), (y0, y1 + 0.07)
    top = y1 + L.CASING + (0.04 if o["kind"] == "door" else 0.0)
    return (a0 - L.CASING - 0.015, a1 + L.CASING + 0.015), (y0, top)


def moulding_runs(rid):
    """[(kind, p0, p1, W)] runs of each moulding along the room's walls, broken at openings, floor holes, the A4
    arch and the chimney breast (wrapped round it)."""
    r = L.ROOMS[rid]
    fin = L.ROOM_FINISH[rid]
    y_lv = r["y"]
    sides = {"W": (r["x"][0], r["z"], Vector((1, 0, 0))), "E": (r["x"][1], r["z"], Vector((-1, 0, 0))),
             "S": (r["z"][0], r["x"], Vector((0, 0, 1))), "N": (r["z"][1], r["x"], Vector((0, 0, -1)))}
    runs = []
    br = L.FIREPLACE["breast"] if rid == "Bedroom" else None
    for kind in fin["mouldings"]:
        prof = PROFILES[kind]
        h = max(p[1] for p in prof)
        my0 = y_lv + L.MOULDING_Y[kind]
        my1 = my0 + h
        for side, (coord, span, W) in sides.items():
            cuts = []
            axis = "z" if side in "WE" else "x"
            for oid, o in L.OPENINGS.items():
                w = L.WALLS[o["wall"]]
                if w["axis"] != axis or not any(abs(t - coord) < 1e-6 for t in w["t"]):
                    continue
                (e0, e1), (oy0, oy1) = opening_extent(o)
                if oy0 < my1 and oy1 > my0:
                    cuts.append((e0, e1))
            if kind == "skirting":
                for hl in r.get("floor_holes", []):
                    hx, hz = hl["x"], hl["z"]
                    if axis == "z" and (abs(hx[0] - coord) < 1e-6 or abs(hx[1] - coord) < 1e-6):
                        cuts.append(hz)
                    if axis == "x" and (abs(hz[0] - coord) < 1e-6 or abs(hz[1] - coord) < 1e-6):
                        cuts.append(hx)
            if rid == "CentralHall" and axis == "z" and my1 > L.A4["spring"]:
                cuts.append(L.A4["z"])
            if br and side == "S":
                cuts.append(br["x"])
            segs = [span]
            for c0, c1 in sorted(cuts):
                nxt = []
                for s0, s1 in segs:
                    if c1 <= s0 or c0 >= s1:
                        nxt.append((s0, s1))
                        continue
                    if s0 < c0:
                        nxt.append((s0, c0))
                    if c1 < s1:
                        nxt.append((c1, s1))
                segs = nxt
            for s0, s1 in segs:
                if s1 - s0 < 0.03:
                    continue
                if axis == "z":
                    p0, p1 = V3(coord, my0, s0), V3(coord, my0, s1)
                else:
                    p0, p1 = V3(s0, my0, coord), V3(s1, my0, coord)
                runs.append((kind, p0, p1, W))
        if br:   # wrap the chimney breast (outside corners: runs extended by the profile depth)
            d = max(p[0] for p in prof)
            (bx0, bx1), (bz0, bz1) = br["x"], br["z"]
            fb = L.FIREPLACE["firebox"]
            front = [(bx0 - d, bx1 + d)]
            if kind == "skirting":
                front = [(bx0 - d, fb["x"][0]), (fb["x"][1], bx1 + d)]
            for f0, f1 in front:
                runs.append((kind, V3(f0, my0, bz1), V3(f1, my0, bz1), Vector((0, 0, 1))))
            runs.append((kind, V3(bx0, my0, bz0), V3(bx0, my0, bz1 + d), Vector((-1, 0, 0))))
            runs.append((kind, V3(bx1, my0, bz0), V3(bx1, my0, bz1 + d), Vector((1, 0, 0))))
    return runs


def build_mouldings():
    out = []
    for rid in L.ROOMS:
        runs = moulding_runs(rid)
        if not runs:
            continue
        mb = MB("Mouldings_" + rid)
        strip = style_of(rid, "trim")
        for kind, p0, p1, W in runs:
            prof = [(h, d) for d, h in PROFILES[kind]]    # sweep profile: s = up (N), t = out of the wall (W)
            below = p0 - UP
            sweep(mb, [p0, p1], W, prof, "M_Trim", strip, away=below, back=True)
        out.append(emit(mb))
    return out


# ------------------------------------------------------------------ openings: casings, linings, doors, windows
CASE_PROF = [(-0.006, 0.0), (-0.006, 0.010), (0.004, 0.019), (0.030, 0.023), (0.085, 0.024), (0.104, 0.019),
             (0.117, 0.011), (0.122, 0.0)]


def face_info(w, side):
    """side 0 = low face (t0, W = -thickness dir), 1 = high face."""
    _, td = wall_dirs(w)
    return (w["t"][0], -td) if side == 0 else (w["t"][1], td)


def face_room(w, side, a, y):
    t, W = face_info(w, side)
    return room_at(wp(w, a, y, t) + W * 0.15)


def casing_path(w, pts2, t):
    return [wp(w, a, y, t) for a, y in pts2]


def casings(mb, w, path2, strip_fn, scale=1.0, plinth=True, faces=(0, 1), y_floor=None, polish=None):
    """Architraves on both wall faces along a 2D path (a, y) round an opening, plus plinth blocks at the feet."""
    prof = [(s * scale, t) for s, t in CASE_PROF]
    am = sum(a for a, _ in path2) / len(path2)
    ym = sum(y for _, y in path2) / len(path2)
    for side in faces:
        t, W = face_info(w, side)
        rid = face_room(w, side, am, max(ym, (y_floor or 0) + 1.0))
        strip = strip_fn(rid)
        path = casing_path(w, path2, t)
        sweep(mb, path, W, prof, "M_Trim", strip, away=wp(w, am, ym, t), back=True, polish=polish)
        if plinth and y_floor is not None:
            for a_edge, sgn in ((path2[0][0], -1), (path2[-1][0], 1)):
                a_rng = sorted((a_edge - sgn * 0.012, a_edge + sgn * (0.122 * scale + 0.012)))
                t_rng = sorted((t, t + (W.x + W.z) * 0.032))
                x, z = (a_rng, t_rng) if w["axis"] == "x" else (t_rng, a_rng)
                trim_box(mb, x, (y_floor, y_floor + 0.24), z, strip, bev=0.004)


def lining(mb, w, path2, strip, inset=-0.004):
    t0 = w["t"][0]
    _, td = wall_dirs(w)
    am = sum(a for a, _ in path2) / len(path2)
    ym = sum(y for _, y in path2) / len(path2)
    th = w["t"][1] - w["t"][0]
    sweep(mb, casing_path(w, path2, t0), td, [(inset, -0.003), (inset, th + 0.003)], "M_Trim", strip,
          away=wp(w, am, ym, t0), ribbon=True)


def door_stop(mb, w, o, strip):
    """Stop bead round the jamb and head on the swing-side face, where the leaf closes."""
    (a0, a1), (y0, y1) = o["span"], o["y"]
    sd = o.get("swing_dir", 1)
    tf = w["t"][1] if sd > 0 else w["t"][0]
    tt = sorted((tf - sd * 0.075, tf - sd * 0.05))
    for a_rng, y_rng in (((a0, a0 + 0.012), (y0, y1)), ((a1 - 0.012, a1), (y0, y1)), ((a0, a1), (y1 - 0.012, y1))):
        x, z = (a_rng, tt) if w["axis"] == "x" else (tt, a_rng)
        trim_box(mb, x, y_rng, z, strip, bev=0.002)


def leaf_mesh(mb, style, w_, h, strip, th=0.045):
    """Door leaf in local space: x 0..w_ from the hinge edge, y 0..h, z -th/2..th/2."""
    pol_latch = lambda c, n: 1.0 if (c.x > w_ - 0.16 and 0.85 < c.y < 1.2) or (c.x > w_ - 0.01) else 0.0  # noqa
    t2 = th / 2
    if style == "ledged":
        nb = max(4, int(round(w_ / 0.15)))
        bw = w_ / nb
        for i in range(nb):
            trim_box(mb, (i * bw + 0.002, (i + 1) * bw - 0.002), (0, h), (-0.012, 0.012), strip, bev=0.005,
                     polish=pol_latch)
        for yl in (0.2, h / 2, h - 0.25):
            trim_box(mb, (0.04, w_ - 0.04), (yl - 0.075, yl + 0.075), (0.012, 0.04), strip, bev=0.006)
        ang = math.atan2((h / 2 - 0.25) - 0.2, w_ - 0.16)
        for (ya, yb) in ((0.2, h / 2), (h / 2, h - 0.25)):
            ln = math.hypot(w_ - 0.16, yb - ya - 0.15)
            bm = bm_box((-ln / 2, ln / 2), (-0.06, 0.06), (0.012, 0.036), 0.005)
            a = math.atan2(yb - ya - 0.15, w_ - 0.16)
            bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(a, 3, 'Z'), cent=(0, 0, 0))
            bmesh.ops.translate(bm, verts=bm.verts, vec=(w_ / 2, (ya + yb) / 2, 0))
            absorb(mb, bm, "M_Trim", TrimUV(strip, [v.co for v in bm.verts]))
        for yl in (0.2, h - 0.25):    # strap hinges
            trim_box(mb, (-0.01, 0.45), (yl - 0.02, yl + 0.02), (-0.016, -0.012), "metal_paint", bev=0.002)
        trim_lathe(mb, [(0, 0), (0.02, 0), (0.02, 0.01), (0.008, 0.02), (0.012, 0.06), (0, 0.065)],
                   (w_ - 0.07, 1.0, 0.04), "metal_paint", axis=(0, 0, 1), seg=8, polish=lambda c, n: 1.0)
        return
    st = 0.12 if w_ > 0.7 else 0.10
    rails = [(0.0, 0.22), (h * 0.44, h * 0.44 + 0.17), (h - 0.13, h)]
    cols = [(st, w_ / 2 - 0.05), (w_ / 2 + 0.05, w_ - st)] if style == "panel4" else [(st, w_ - st)]
    frame_kw = dict(bev=0.004)
    trim_box(mb, (0, st), (0, h), (-t2, t2), strip, polish=None, **frame_kw)
    trim_box(mb, (w_ - st, w_), (0, h), (-t2, t2), strip, polish=pol_latch, **frame_kw)
    for y0, y1 in rails:
        trim_box(mb, (st, w_ - st), (y0, y1), (-t2, t2), strip, frame=((1, 0, 0), (0, 1, 0), (0, 0, 1)), **frame_kw)
    if style == "panel4":
        trim_box(mb, (w_ / 2 - 0.05, w_ / 2 + 0.05), (0.22, h - 0.13), (-t2, t2), strip, **frame_kw)
    for (ya, yb) in ((0.22, h * 0.44), (h * 0.44 + 0.17, h - 0.13)):
        for xa, xb in cols:
            # raised-and-fielded panel: a thinner board with a wide bevel, set back from the frame faces
            trim_box(mb, (xa - 0.006, xb + 0.006), (ya - 0.006, yb + 0.006), (-t2 * 0.55, t2 * 0.55), strip,
                     bev=0.016)
            for sz in (-1, 1):   # small bead moulding round each panel
                z = (sz * t2 - 0.004, sz * t2 + 0.002) if sz > 0 else (sz * t2 - 0.002, sz * t2 + 0.004)
                for xr, yr in (((xa, xb), (ya, ya + 0.012)), ((xa, xb), (yb - 0.012, yb)),
                               ((xa, xa + 0.012), (ya, yb)), ((xb - 0.012, xb), (ya, yb))):
                    trim_box(mb, xr, yr, z, strip, bev=0.003)
    kx = w_ - 0.065
    for sz in (-1, 1):
        trim_box(mb, (kx - 0.022, kx + 0.022), (0.86, 1.08), sorted((sz * t2, sz * (t2 + 0.003))), "brass",
                 bev=0.002, polish=lambda c, n: 1.0)
        trim_lathe(mb, [(0, 0), (0.011, 0), (0.007, 0.012), (0.007, 0.022), (0.018, 0.032), (0.017, 0.045),
                        (0, 0.048)], (kx, 1.0, sz * (t2 + 0.003)), "brass", axis=(0, 0, sz), seg=10,
                   polish=lambda c, n: 1.0)
    for y in (0.25, h * 0.5, h - 0.3):
        trim_lathe(mb, [(0, 0), (0.009, 0), (0.009, 0.1), (0, 0.1)], (-0.004, y - 0.05, t2 * 0.6), "brass",
                   axis=(0, 1, 0), seg=8)


def door_leaves(oid, o, w):
    """Leaf object(s) for a door opening, hung from DOOR_LEAVES (open angle towards the swing side, sag)."""
    dl = L.DOOR_LEAVES[oid]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    sd = o.get("swing_dir", 1)
    ad, td = wall_dirs(w)
    tf = w["t"][1] if sd > 0 else w["t"][0]
    leaves = []
    if dl["style"] == "double":
        half = (a1 - a0) / 2
        leaves.append(("L", a0, +1, half - 0.004, dl["open"][0], dl["sag"][0]))
        leaves.append(("R", a1, -1, half - 0.004, dl["open"][1], dl["sag"][1]))
    else:
        low = o["hinge"] in ("S", "W")
        leaves.append(("", a0 if low else a1, 1 if low else -1, (a1 - a0) - 0.008, dl["open"], dl.get("sag", 0.0)))
    swing_room = room_at(wp(w, (a0 + a1) / 2, y0 + 1.0, tf) + td * sd * 0.3)
    strip = style_of(swing_room, "door")
    th = 0.045
    out = []
    for tag, a_h, dirn, lw, ang, sag in leaves:
        name = f"{oid}_Leaf{tag}"
        mb = MB(name)
        h = (y1 - y0) - 0.012 - (0.02 if sag else 0.008)
        local = MB(name)
        leaf_mesh(local, dl["style"], lw, h, strip, th)
        # sag: rotate in the leaf plane about the top hinge corner (latch side drops), then rest it on the floor
        Ms = Matrix.Identity(4)
        if sag:
            Ms = (Matrix.Translation((0, h, 0)) @ Matrix.Rotation(math.radians(-sag), 4, 'Z') @
                  Matrix.Translation((0, -h, 0)))
        lo = min((Ms @ p).y for p in local.v)
        lift = -lo + 0.002 if sag else 0.008
        x_dir = ad * dirn
        s_dir = td * sd
        th_r = math.radians(ang)
        xp = x_dir * math.cos(th_r) + s_dir * math.sin(th_r)
        zp = -x_dir * math.sin(th_r) + s_dir * math.cos(th_r)
        if xp.cross(UP).dot(zp) < 0:      # keep a proper rotation (no mirror)
            zp = -zp
        hinge = wp(w, a_h + dirn * 0.004, y0 + lift, tf - sd * 0.026)    # closes against the stop bead
        M = Matrix(((xp.x, 0, zp.x, hinge.x), (xp.y, 1, zp.y, hinge.y), (xp.z, 0, zp.z, hinge.z), (0, 0, 0, 1)))
        mb.xf = M @ Ms
        for idx, m, uv, p in local.f:
            mb.face([local.v[i] for i in idx], m, uv, p)
        mb.xf = None
        o_ = emit(mb)
        out.append(o_)
        # nav-ignored box collider so the player can't walk through a leaf (navmesh unchanged)
        cx = hinge + xp * (lw / 2) + UP * (h / 2)
        yaw = math.degrees(math.atan2(-xp.z, xp.x))
        _STATE["colliders"].setdefault(name, []).append(dict(c=[round(v, 4) for v in cx],
                                                             s=[round(lw, 4), round(h, 4), th],
                                                             r=[0.0, round(yaw, 3), 0.0]))
        _STATE["nav_ignore"].append(name)
    return out


def threshold(mb, w, span, y0, strip="varnish_door", top=0.004):
    """Board across the wall thickness in an opening (covers the wall's top face at floor level)."""
    t_rng = (w["t"][0] - 0.01, w["t"][1] + 0.01)
    x, z = (span, t_rng) if w["axis"] == "x" else (t_rng, span)
    trim_box(mb, x, (y0 - 0.02, y0 + top), z, strip, bev=0.004, polish=lambda c, n: 0.8 if n.y > 0.5 else 0.0)


def build_door(oid):
    o = L.OPENINGS[oid]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    mb = MB(oid + "_Frame")
    path = [(a0, y0), (a0, y1), (a1, y1), (a1, y0)]
    sd = o.get("swing_dir", 1)
    t_sw = w["t"][1] if sd > 0 else w["t"][0]
    _, td = wall_dirs(w)
    swing_room = room_at(wp(w, (a0 + a1) / 2, y0 + 1.0, t_sw) + td * sd * 0.3)
    jamb = style_of(swing_room, "trim")
    lining(mb, w, path, jamb)
    door_stop(mb, w, o, jamb)
    casings(mb, w, path, lambda rid: style_of(rid, "trim"), y_floor=y0)
    threshold(mb, w, (a0, a1), y0, "varnish_door" if jamb != "raw" else "raw", top=0.012)
    return [emit(mb)] + door_leaves(oid, o, w)


def build_arch(oid):
    o = L.OPENINGS[oid]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, crown), sp = o["span"], o["y"], o["spring"]
    pts = arc(a0, a1, sp, crown, n=20)
    path = [(a0, y0)] + pts + [(a1, y0)]
    mb = MB(oid)
    lining(mb, w, path, "varnish")
    casings(mb, w, path, lambda rid: "varnish", scale=1.15, y_floor=y0)
    threshold(mb, w, (a0, a1), y0)
    for side in (0, 1):     # keystone at the crown, standing proud of the casing
        t, W = face_info(w, side)
        am = (a0 + a1) / 2
        c = wp(w, am, crown, t)
        bm = bm_prism([(-0.05, -0.02), (0.05, -0.02), (0.07, 0.17), (-0.07, 0.17)],
                      lambda p, q, tt: c + wall_dirs(w)[0] * p + UP * q + W * tt, (0.0, 0.04))
        absorb(mb, bm, "M_Trim", TrimUV("varnish", [v.co for v in bm.verts]))
    return emit(mb)


def build_a4():
    a = L.A4
    (x0, x1), sp, cr = a["x"], a["spring"], a["crown"]
    w = dict(axis="x", t=a["z"], a=a["x"])
    pts = arc(x0, x1, sp, cr, n=24)
    mb = MB("A4")
    soffit = [(x0, L.CEILING), (x0, sp)] + pts[1:-1] + [(x1, sp), (x1, L.CEILING)]
    bm = bm_prism(soffit, lambda p, q, t: V3(p, q, t), a["z"])
    absorb(mb, bm, surface_mat, world_uv())
    lining(mb, w, pts, "varnish")
    for side in (0, 1):
        t, W = face_info(w, side)
        sweep(mb, casing_path(w, pts, t), W, [(s * 1.15, tt) for s, tt in CASE_PROF], "M_Trim", "varnish",
              away=wp(w, (x0 + x1) / 2, sp, t), back=True)
    for xa, xb in ((x0, x0 + 0.14), (x1 - 0.14, x1)):     # corbels under the springings
        bm = bm_prism([(xa, sp - 0.22), (xb, sp - 0.02), (xb, sp), (xa, sp)] if xa == x0 else
                      [(xa, sp - 0.02), (xb, sp - 0.22), (xb, sp), (xa, sp)],
                      lambda p, q, t: V3(p, q, t), (a["z"][0] - 0.03, a["z"][1] + 0.03))
        absorb(mb, bm, "M_Trim", TrimUV("varnish", [v.co for v in bm.verts]))
    return emit(mb)


def build_cased(oid):
    o, tr = L.OPENINGS[oid], L.OPENINGS[oid + "_Transom"]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    ty0, ty1 = tr["y"]
    mb = MB(oid)
    path = [(a0, y0), (a0, ty1), (a1, ty1), (a1, y0)]
    lining(mb, w, path, "varnish")
    casings(mb, w, path, lambda rid: "varnish", y_floor=y0)
    threshold(mb, w, (a0, a1), y0)
    t_rng = (w["t"][0] - 0.022, w["t"][1] + 0.022)
    x, z = ((a0, a1), t_rng) if w["axis"] == "x" else (t_rng, (a0, a1))
    trim_box(mb, x, (y1, ty0), z, "varnish", bev=0.006)
    tm = (w["t"][0] + w["t"][1]) / 2
    nb = tr["bars"]
    for i in range(nb):
        ac = a0 + (a1 - a0) * (i + 1) / (nb + 1)
        base = wp(w, ac, ty0, tm)
        trim_lathe(mb, [(0, 0), (0.016, 0), (0.016, 0.03), (0.010, 0.05), (0.014, 0.12), (0.008, 0.2),
                        (0.014, 0.28), (0.010, ty1 - ty0 - 0.05), (0.016, ty1 - ty0 - 0.03),
                        (0.016, ty1 - ty0), (0, ty1 - ty0)], base, "varnish_turned", seg=8)
    return emit(mb)


def build_window(oid):
    o = L.OPENINGS[oid]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    ad, td = wall_dirs(w)
    tm = (w["t"][0] + w["t"][1]) / 2
    # interior face = the face that looks into a room
    side_in = 0 if face_room(w, 0, (a0 + a1) / 2, (y0 + y1) / 2) else 1
    t_in, W_in = face_info(w, side_in)
    rid = face_room(w, side_in, (a0 + a1) / 2, (y0 + y1) / 2)
    strip = style_of(rid, "window")
    mb = MB(oid)
    glass = MB(oid + "_Glass")
    hopper = o.get("hopper")
    path = [(a0, y0), (a0, y1), (a1, y1), (a1, y0)]
    lining(mb, w, path + [(a0, y0)], strip)
    if not hopper:
        sweep(mb, casing_path(w, path, t_in), W_in, [(s * 0.8, t) for s, t in CASE_PROF], "M_Trim", strip,
              away=wp(w, (a0 + a1) / 2, (y0 + y1) / 2, t_in), back=True)
        # stool (inner sill board through the wall) and apron
        tin = sorted((t_in + (W_in.x + W_in.z) * 0.06, w["t"][1 - side_in]))
        x, z = ((a0 - 0.13, a1 + 0.13), tin) if w["axis"] == "x" else (tin, (a0 - 0.13, a1 + 0.13))
        trim_box(mb, x, (y0 - 0.03, y0), z, strip, bev=0.006, polish=lambda c, n: 0.4 if n.y > 0.5 else 0.0)
        ta = sorted((t_in, t_in + (W_in.x + W_in.z) * 0.02))
        x, z = ((a0 - 0.08, a1 + 0.08), ta) if w["axis"] == "x" else (ta, (a0 - 0.08, a1 + 0.08))
        trim_box(mb, x, (y0 - 0.18, y0 - 0.03), z, strip, bev=0.004)
        # outside sill
        t_out, W_out = face_info(w, 1 - side_in)
        to = sorted((t_out - (W_out.x + W_out.z) * 0.06, t_out + (W_out.x + W_out.z) * 0.06))
        x, z = ((a0 - 0.06, a1 + 0.06), to) if w["axis"] == "x" else (to, (a0 - 0.06, a1 + 0.06))
        bm = bm_box(x, (y0 - 0.06, y0), z, 0.008)
        absorb(mb, bm, "M_Concrete", world_uv())
    # sashes: double-hung (lower sash on the interior side, raised a little on some), hoppers tilt in
    fw, ft = 0.05, 0.04
    sashes = []
    if hopper:
        sashes.append((y0, y1, tm, 0.0, "metal_paint"))
    else:
        ym = (y0 + y1) / 2
        lift = rng.choice((0.0, 0.0, 0.06, 0.14))
        s_in = W_in * 0.025
        sashes.append((y0 + 0.01 + lift, ym + 0.02 + lift, tm + (s_in.x + s_in.z), 0.0, strip))
        sashes.append((ym - 0.02, y1 - 0.01, tm - (s_in.x + s_in.z), 0.0, strip))
        # parting beads on the jambs
        for aa in ((a0, a0 + 0.012), (a1 - 0.012, a1)):
            tt = (tm - 0.006, tm + 0.006)
            x, z = (aa, tt) if w["axis"] == "x" else (tt, aa)
            trim_box(mb, x, (y0, y1), z, strip, bev=0.002)
    for sy0, sy1, ts, tilt, sstrip in sashes:
        sw = (a0 + 0.006, a1 - 0.006)
        part = MB("sash")
        tt = (ts - ft / 2, ts + ft / 2)

        def box(ar, yr, tr_, bev=0.003, st=sstrip):
            x, z = (ar, tr_) if w["axis"] == "x" else (tr_, ar)
            trim_box(part, x, yr, z, st, bev=bev)

        box((sw[0], sw[0] + fw), (sy0, sy1), tt)
        box((sw[1] - fw, sw[1]), (sy0, sy1), tt)
        box((sw[0] + fw, sw[1] - fw), (sy0, sy0 + fw), tt)
        box((sw[0] + fw, sw[1] - fw), (sy1 - fw, sy1), tt)
        am = (a0 + a1) / 2
        box((am - 0.012, am + 0.012), (sy0 + fw, sy1 - fw), (ts - 0.014, ts + 0.014))
        gpart = MB("pane")
        for pa in ((sw[0] + fw, am - 0.012), (am + 0.012, sw[1] - fw)):
            x, z = (pa, (ts - 0.002, ts + 0.002)) if w["axis"] == "x" else ((ts - 0.002, ts + 0.002), pa)
            bm = bm_box(x, (sy0 + fw, sy1 - fw), z, 0.0)
            absorb(gpart, bm, "M_Glass", world_uv())
        if hopper:   # tilt the hopper sash inwards about its bottom rail
            tin = -W_in
            ang = math.radians(14)
            piv = wp(w, am, sy0, ts)
            R = Matrix.Rotation(ang, 4, ad if (ad.cross(UP)).dot(W_in) > 0 else -ad)
            Mx = Matrix.Translation(piv) @ R @ Matrix.Translation(-piv)
            for p_ in (part, gpart):
                p_.v = [Mx @ v for v in p_.v]
        mb.extend(part)
        glass.extend(gpart)
    out = [emit(mb), emit(glass)]
    if o.get("boarded"):
        bmb = MB(oid + "_Boards")
        t_out, W_out = face_info(w, 1 - side_in)
        tb = sorted((t_out, t_out + (W_out.x + W_out.z) * 0.025))
        for i, yb in enumerate((y0 + 0.12, y0 + 0.55, y0 + 1.0)):
            x, z = ((a0 - 0.1, a1 + 0.1), tb) if w["axis"] == "x" else (tb, (a0 - 0.1, a1 + 0.1))
            bm = bm_box(x, (yb, yb + 0.16), z, 0.006)
            ang = math.radians(rng.uniform(-4, 4))
            c = Vector(((x[0] + x[1]) / 2, yb + 0.08, (z[0] + z[1]) / 2))
            bmesh.ops.rotate(bm, verts=bm.verts, cent=c, matrix=Matrix.Rotation(ang, 3, W_out))
            absorb(bmb, bm, "M_Trim", TrimUV("raw", [v.co for v in bm.verts]))
        out.append(emit(bmb))
    if o.get("well"):
        wl = o["well"]
        n = "Ext_LightWell_" + oid
        lmb = MB(n)
        panel_cut(lmb, (wl["x"], (L.GROUND_BOTTOM, wl["floor"]), wl["z"]), 1, [], lambda c, n_: "M_Concrete")
        out.append(emit(lmb))
    return out


# ------------------------------------------------------------------ stair, rail, balustrade, gate
def y_pitch(z):
    s = L.STAIR
    return -(z - s["z_top"]) * s["rise"] / s["run"]


def board_vmap():
    """A random board of the M_FloorPlank scan as a (v0, v1) range (inset 3 px)."""
    sv = SEAMS_V + [SEAMS_V[0] + 1.0]
    i = rng.randrange(len(SEAMS_V))
    return sv[i] + 0.003, sv[i + 1] - 0.003


TREAD_T, NOSE = 0.032, 0.026


def tread_profile(back, front, y, chip=0.0, chip_d=0.0):
    """Bullnosed tread in (s = z, t = y), front = nosing edge (+z). chip pulls the nose back / down."""
    r = TREAD_T / 2
    pts = [(back, y - TREAD_T), (front - r - chip, y - TREAD_T)]
    for i in range(1, 6):
        a = -math.pi / 2 + math.pi * i / 6
        pts.append((front - r - chip + r * math.cos(a), y - r + r * math.sin(a) - (chip_d if i >= 3 else 0.0)))
    pts += [(front - r - chip * 0.6, y - chip_d * 0.5), (back, y)]
    return pts


def build_stair():
    st = L.STAIR
    x0, x1 = st["x"]
    zt, rise, run = st["z_top"], st["rise"], st["run"]
    xs0, xs1 = x0 + 0.03, st["rail_x"][0]          # treads housed between the two stringers
    xc = (xs0 + xs1) / 2
    mb = MB("Stair")

    def tread_polish(c, n):
        """Feet polish the middle of each nosing and the front of the tread."""
        walk = max(0.0, 1.0 - abs(c.x - xc) / 0.5)
        nose = n.z > 0.3 or (n.y > 0.5 and (c.z - zt) % run > run - 0.12)
        return walk * (1.0 if nose else 0.35)

    for k in range(0, st["treads"] + 1):
        y = -k * rise
        front = zt + k * run + NOSE
        back = zt + (k - 1) * run if k > 0 else zt - 0.06
        if k == 0:
            back = zt - 0.004          # top: only the landing nosing lip in front of the hall boards
        # path along x with chip sections
        xs = [xs0 + (xs1 - xs0) * i / 6 for i in range(7)]
        chips = {}
        for _ in range(rng.choice((1, 2, 2, 3))):
            cx = rng.uniform(xs0 + 0.06, xs1 - 0.06) if rng.random() < 0.6 else rng.gauss(xc, 0.2)
            cx = min(max(cx, xs0 + 0.05), xs1 - 0.05)
            wdt = rng.uniform(0.03, 0.08)
            dep = rng.uniform(0.006, 0.014)
            for dx, f in ((-wdt / 2, 0.0), (-wdt / 4, 0.8), (wdt / 6, 1.0), (wdt / 2, 0.0)):
                chips[round(cx + dx, 4)] = max(f * dep, chips.get(round(cx + dx, 4), 0.0))
        xs = sorted(set([round(v, 4) for v in xs] + list(chips)))
        path = [V3(xv, 0.0, 0.0) for xv in xs]
        profs = [tread_profile(back, front, y, chips.get(round(xv, 4), 0.0), chips.get(round(xv, 4), 0.0) * 0.5)
                 for xv in xs]
        vm = board_vmap()
        sweep(mb, path, UP, None, "M_FloorPlank", vmap=vm, profiles=[[(-s, t) for s, t in p] for p in profs],
              back=False, caps=True, umul=1.0 / L.SURFACE_TILE_M["M_FloorPlank"], away=V3(xs0, 0, 10.0),
              polish=tread_polish)
        # riser below this tread's nose (riser k+1)
        if k < st["risers"]:
            rz = zt + k * run
            ry0 = -(k + 1) * rise
            ry1 = y - TREAD_T
            trim_box(mb, (xs0, xs1), (ry0, ry1), (rz - 0.02, rz), "varnish_door", bev=0.002,
                     frame=((1, 0, 0), (0, 1, 0), (0, 0, 1)))
    # stringers (wall side and rail side) along the pitch, clipped to the hall floor and the basement floor
    k_ = rise / run
    for name, xr, top, strip in (("wall", (x0, x0 + 0.03), 0.07, "varnish"), ("outer", st["rail_x"], 0.05, "varnish")):
        bot = -0.30
        pts = [(zt + top / k_, 0.0),                                       # top line meets y = 0
               (zt + (top + 2.85) / k_, L.BASEMENT),                       # top line meets the basement floor
               (zt + (bot + 2.85) / k_, L.BASEMENT),                       # bottom line meets the basement floor
               (zt + bot / k_, 0.0)]                                       # bottom line meets y = 0 (inside the slab)
        bm = bm_prism([(y, z) for z, y in pts], lambda p, q, t: V3(t, p, q), xr)
        L_ax = Vector((0, -rise, run)).normalized()
        absorb(mb, bm, "M_Trim", TrimUV(strip, [v.co for v in bm.verts], (L_ax, Vector((1, 0, 0)), L_ax.cross(
            Vector((1, 0, 0))))))
    # under-stair boarding below the outer stringer (encloses the solid flight the colliders describe)
    zb_end = zt + (-0.30 + 2.85) / k_
    z = zt
    bx = (st["rail_x"][0] + 0.004, st["rail_x"][0] + 0.022)
    while z < zb_end - 0.02:
        z1 = min(z + 0.12, zb_end)
        ytop0, ytop1 = y_pitch(z) - 0.30, y_pitch(z1) - 0.30
        poly = [(z + 0.002, L.BASEMENT), (z1 - 0.002, L.BASEMENT), (z1 - 0.002, max(ytop1, L.BASEMENT + 0.01)),
                (z + 0.002, ytop0)]
        bm = bm_prism([(y, zz) for zz, y in poly], lambda p, q, t: V3(t, p, q), bx)
        absorb(mb, bm, "M_Trim", TrimUV("raw", [v.co for v in bm.verts]))
        z = z1
    return emit(mb)


BAL_PROF = [(0, 0), (0.022, 0), (0.022, 0.12), (0.017, 0.13), (0.017, 0.15), (0.021, 0.17), (0.026, 0.27),
            (0.024, 0.33), (0.017, 0.41), (0.014, 0.54), (0.016, 0.61), (0.020, 0.655), (0.016, 0.675),
            (0.016, 0.69), (0.022, 0.70), (0.022, 0.80), (0, 0.80)]


def baluster(mb, base, h, strip, stub=False):
    prof = [(r, z * h / 0.80) for r, z in BAL_PROF]
    if stub:
        prof = [(r, z * h / 0.80) for r, z in BAL_PROF[:5]] + [(0.008, 0.12 * h / 0.8), (0, 0.13 * h / 0.8)]
    trim_lathe(mb, prof, base, strip, seg=8)


HANDRAIL = [(-0.034, -0.055), (0.034, -0.055), (0.034, -0.035), (0.030, -0.02), (0.036, -0.005), (0.030, 0.008),
            (0.012, 0.014), (-0.012, 0.014), (-0.030, 0.008), (-0.036, -0.005), (-0.030, -0.02), (-0.034, -0.035)]


def handrail(mb, p0, p1, strip):
    """Mushroom handrail along p0 -> p1 (points on the top centreline); its top faces are polished."""
    d = (Vector(p1) - Vector(p0)).normalized()
    side = d.cross(UP).normalized()
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    W = side.cross(d).normalized()
    if W.y < 0:
        W = -W
    sweep(mb, [p0, p1], side, [(t, s) for s, t in HANDRAIL], "M_Trim", strip, back=False,
          polish=lambda c, n: 1.0 if n.y > 0.35 else 0.15, away=Vector(p0) - UP)


def newel(mb, x, z, y0, h, strip, cracked=False):
    s = 0.05
    trim_box(mb, (x - s - 0.01, x + s + 0.01), (y0, y0 + 0.16), (z - s - 0.01, z + s + 0.01), strip, bev=0.01)
    trim_box(mb, (x - s, x + s), (y0 + 0.16, y0 + h - 0.08), (z - s, z + s), strip, bev=0.008)
    for sx, sz in ((1, 0), (-1, 0), (0, 1), (0, -1)):     # raised panels on each face of the shaft
        xr = sorted((x + sx * s, x + sx * (s + 0.005))) if sx else (x - 0.03, x + 0.03)
        zr = sorted((z + sz * s, z + sz * (s + 0.005))) if sz else (z - 0.03, z + 0.03)
        trim_box(mb, xr, (y0 + 0.26, y0 + h - 0.2), zr, strip, bev=0.002)
    capy = y0 + h - 0.08
    if not cracked:
        trim_box(mb, (x - s - 0.018, x + s + 0.018), (capy, capy + 0.035), (z - s - 0.018, z + s + 0.018), strip,
                 bev=0.01, polish=lambda c, n: 0.8 if n.y > 0.5 else 0.2)
        trim_box(mb, (x - s - 0.008, x + s + 0.008), (capy + 0.035, capy + 0.05), (z - s - 0.008, z + s + 0.008),
                 strip, bev=0.006, polish=lambda c, n: 0.8)
        trim_lathe(mb, [(0, 0), (0.03, 0), (0.024, 0.012), (0.035, 0.04), (0.038, 0.06), (0.03, 0.085),
                        (0.012, 0.1), (0, 0.105)], (x, capy + 0.05, z), strip, seg=12, polish=lambda c, n: 0.9)
        return
    # cracked cap: split along x, the outer half dropped and tilted, the finial knocked off-centre
    for half, (xa, xb), tilt, drop in ((0, (x - s - 0.018, x - 0.002), 0.0, 0.0),
                                       (1, (x + 0.003, x + s + 0.018), 7.0, 0.012)):
        bm = bm_box((xa, xb), (capy, capy + 0.05), (z - s - 0.018, z + s + 0.018), 0.008)
        if tilt:
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(x + 0.003, capy, z),
                             matrix=Matrix.Rotation(math.radians(-tilt), 3, 'Z'))
            bmesh.ops.translate(bm, verts=bm.verts, vec=(0.004, -drop, 0))
        absorb(mb, bm, "M_Trim", TrimUV(strip, [v.co for v in bm.verts]), lambda c, n: 0.6 if n.y > 0.5 else 0.1)
    trim_lathe(mb, [(0, 0), (0.03, 0), (0.024, 0.012), (0.035, 0.04), (0.038, 0.06), (0.03, 0.085), (0.012, 0.1),
                    (0, 0.105)], (x - 0.022, capy + 0.05, z + 0.01), strip, axis=(0.18, 1, 0.05), seg=12)


def build_stair_rail():
    st = L.STAIR
    rx = (st["rail_x"][0] + st["rail_x"][1]) / 2
    zt, rise, run = st["z_top"], st["rise"], st["run"]
    hr = st["handrail_above_pitch"]
    mb = MB("Stair_Rail")
    zb = st["z_bottom"]
    for k in range(1, st["treads"] + 1):
        for f in (0.25, 0.75):
            zc = zt + (k - 1 + f) * run
            base = y_pitch(zc) + 0.05
            top = min(y_pitch(zc) + hr - 0.055, L.TRIMMER_UNDERSIDE)
            if top - base < 0.12:
                continue
            baluster(mb, V3(rx, base, zc), top - base, "varnish_turned")
    z_hr = zt + (hr - L.TRIMMER_UNDERSIDE) * run / rise
    handrail(mb, V3(rx, y_pitch(z_hr) + hr, z_hr), V3(rx, y_pitch(zb) + hr, zb + 0.02), "varnish_turned")
    newel(mb, rx + 0.03, zb + 0.07, L.BASEMENT, (y_pitch(zb) + hr + 0.12) - L.BASEMENT, "varnish_turned")
    return emit(mb)


def build_balustrade():
    b = L.BALUSTRADE
    e, n = b["east"], b["north"]
    zt = L.STAIR["z_top"]
    xe = (e["x"][0] + e["x"][1]) / 2
    zn = (n["z"][0] + n["z"][1]) / 2
    nh, bh, sp = b["newel_h"], b["height"], b["spacing"]
    strip = "varnish_turned"
    mb = MB("Stairwell_Balustrade")
    missing = {(r, i) for r, i in L.DECAY["missing_balusters"]}
    # newels: stair top, NE corner (cap cracked), west end against the wall
    newel(mb, xe, zt, 0.0, nh, strip, cracked=L.DECAY["cracked_newel"] == "S")
    newel(mb, xe, zn, 0.0, nh, strip, cracked=L.DECAY["cracked_newel"] == "NE")
    newel(mb, n["x"][0] + 0.06, zn, 0.0, nh, strip, cracked=L.DECAY["cracked_newel"] == "W")
    runs = (("east", V3(xe, 0, zt + 0.07), V3(xe, 0, zn - 0.07)),
            ("north", V3(n["x"][0] + 0.12, 0, zn), V3(xe - 0.07, 0, zn)))
    for run, p0, p1 in runs:
        ln = (p1 - p0).length
        d = (p1 - p0).normalized()
        side = d.cross(UP).normalized()
        # shoe rail on the floor and the handrail
        sweep(mb, [p0, p1], side, [(0.0, -0.03), (0.0, 0.03), (0.05, 0.03), (0.065, 0.015), (0.065, -0.015),
                                   (0.05, -0.03)], "M_Trim", "varnish", back=False, away=p0 - UP)
        handrail(mb, p0 + UP * bh, p1 + UP * bh, strip)
        k = int(ln / sp)
        for i in range(k):
            c = p0 + d * (ln * (i + 0.5) / k)
            if (run, i) in missing:
                baluster(mb, c + UP * 0.065, bh - 0.055 - 0.065, strip, stub=True)
                continue
            baluster(mb, c + UP * 0.065, bh - 0.055 - 0.065, strip)
    return emit(mb)


def build_gate():
    g = L.GATE
    hx, hy, hz = g["hinge"]
    x = (hx - 0.012, hx + 0.012)
    z0, z1 = hz + 0.03, hz + g["leaf"]
    y0, y1 = hy + 0.06, hy + g["height"]
    mb = MB("G1_Gate")
    s = "metal_paint"
    trim_box(mb, x, (y0, y1), (z0, z0 + 0.035), s, bev=0.004)
    trim_box(mb, x, (y0, y1), (z1 - 0.035, z1), s, bev=0.004)
    trim_box(mb, x, (y1 - 0.04, y1), (z0, z1), s, bev=0.004, polish=lambda c, n: 0.6 if n.y > 0.5 else 0.0)
    trim_box(mb, x, (y0, y0 + 0.04), (z0, z1), s, bev=0.004)
    trim_box(mb, (hx - 0.008, hx + 0.008), (y0 + 0.28, y0 + 0.31), (z0, z1), s, bev=0.003)
    nb = 9
    for i in range(nb):
        zc = z0 + 0.035 + (z1 - z0 - 0.07) * (i + 0.5) / nb
        trim_lathe(mb, [(0, 0), (0.007, 0), (0.007, y1 - y0 - 0.08), (0, y1 - y0 - 0.08)], (hx, y0 + 0.04, zc), s,
                   seg=6)
        trim_lathe(mb, [(0, 0), (0.012, 0), (0.0, 0.05)], (hx, y1, zc), s, seg=6)    # spear tips
    for yy in (y0 + 0.1, y1 - 0.15):     # hinge pins on the newel side
        trim_lathe(mb, [(0, 0), (0.012, 0), (0.012, 0.07), (0, 0.07)], (hx, yy, z0 - 0.012), "brass", seg=8)
    trim_box(mb, (hx - 0.03, hx + 0.03), (y1 - 0.25, y1 - 0.17), (z1 - 0.06, z1 + 0.02), "brass", bev=0.004,
             polish=lambda c, n: 1.0)
    o = emit(mb)
    lib.set_origin(o, U2B((hx, hy, hz)))
    _STATE["non_static"].append("G1_Gate")
    _STATE["pivots"]["G1_Gate"] = [hx, hy, hz]
    return o


# ------------------------------------------------------------------ basement structure, radiators, pipes
def joist(mb, x, y, z, along="x", sag=0.0):
    bm = bm_box(x, y, z, 0.006)
    if sag:
        lo, hi = (x if along == "x" else z)
        for v in bm.verts:
            c = v.co.x if along == "x" else v.co.z
            f = math.sin(math.pi * (c - lo) / (hi - lo))
            v.co.y -= sag * f
    frame = ((1, 0, 0), (0, 1, 0), (0, 0, 1)) if along == "x" else ((0, 0, 1), (0, 1, 0), (1, 0, 0))
    absorb(mb, bm, "M_Trim", TrimUV("raw", [v.co for v in bm.verts], frame))


def build_beams():
    out = []
    for bid, b in L.BEAMS.items():
        mb = MB(bid)
        along = "x" if (b["x"][1] - b["x"][0]) > (b["z"][1] - b["z"][0]) else "z"
        joist(mb, b["x"], b["y"], b["z"], along)
        out.append(emit(mb))
    j, st = L.JOIST, L.STAIR
    for rid in ("B1", "B2"):
        r = L.ROOMS[rid]
        mb = MB("Joists_" + rid)
        z = j["first_z"]
        while z < r["z"][1]:
            zr = (z - j["width"] / 2, z + j["width"] / 2)
            clash = rid == "B1" and any(not (zr[1] <= b["z"][0] or zr[0] >= b["z"][1])
                                        for b in (L.BEAMS["Trimmer_N"], L.BEAMS["Trimmer_S"]))
            if not clash:
                x0 = r["x"][0]
                if rid == "B1" and st["opening"]["z"][0] <= z <= st["opening"]["z"][1]:
                    x0 = L.BEAMS["Header_E"]["x"][1]
                joist(mb, (x0, r["x"][1]), (-L.SLAB - j["depth"], -L.SLAB - 0.002), zr, "x",
                      sag=rng.uniform(0.002, 0.009))
            z = round(z + j["pitch"], 4)
        out.append(emit(mb))
    return out


def build_radiators():
    out = []
    R = L.RADIATOR
    for rid_, r in L.RADIATORS.items():
        mb = MB(rid_)
        y0, y1 = R["y"]
        a0, a1 = r["span"]
        n = max(4, int((a1 - a0) / R["column"]))
        step = (a1 - a0) / n
        t_back = r["face"] + r["inward"] * R["gap"]
        t_front = t_back + r["inward"] * R["depth"]
        tm = (t_back + t_front) / 2
        dep = abs(t_front - t_back)
        s = "metal_paint"

        def P(a, y, t):
            return V3(a, y, t) if r["axis"] == "x" else V3(t, y, a)

        def box(ar, yr, tr_, bev=0.006):
            tr_ = sorted(tr_)
            x, z = (ar, tr_) if r["axis"] == "x" else (tr_, ar)
            trim_box(mb, x, yr, z, s, bev=bev, frame=((0, 1, 0), (1, 0, 0), (0, 0, 1)))

        ax = (0, 1, 0)
        for i in range(n):
            ac = a0 + step * (i + 0.5)
            for tt in (t_back + r["inward"] * 0.025, t_front - r["inward"] * 0.025):
                trim_lathe(mb, [(0, 0), (0.013, 0), (0.013, y1 - y0 - 0.16), (0, y1 - y0 - 0.16)],
                           P(ac, y0 + 0.08, tt), s, seg=6)
            box((ac - step * 0.42, ac + step * 0.42), (y1 - 0.09, y1), (t_back, t_front))
            box((ac - step * 0.42, ac + step * 0.42), (y0, y0 + 0.09), (t_back, t_front))
        for a_ in (a0 + step * 0.5, a1 - step * 0.5):       # feet
            box((a_ - 0.03, a_ + 0.03), (0.0, y0 + 0.01), (tm - dep * 0.45, tm + dep * 0.45), 0.004)
        for a_, sgn in ((a0, -1), (a1, 1)):                  # valves + tails down through the floor
            pa = P(a_ + sgn * 0.045, y0 + 0.04, t_back + r["inward"] * 0.03)
            trim_lathe(mb, [(0, 0), (0.016, 0), (0.016, 0.06), (0.022, 0.065), (0.022, 0.09), (0.008, 0.1),
                            (0.008, 0.13), (0.02, 0.135), (0.02, 0.15), (0, 0.155)], pa, "brass", seg=8,
                       polish=lambda c, n: 0.6)
            trim_lathe(mb, [(0, 0), (0.014, 0), (0.014, y0 + 0.04), (0, y0 + 0.04)], P(a_ + sgn * 0.045, 0.0,
                                                                                       t_back + r["inward"] * 0.03),
                       s, seg=6)
            hub = (a_ + sgn * 0.045, a_) if sgn < 0 else (a_, a_ + sgn * 0.045)
            box(tuple(sorted(hub)), (y0 + 0.04, y0 + 0.07), (t_back + r["inward"] * 0.015, t_back + r["inward"] *
                                                             0.045), 0.003)
        out.append(emit(mb))
    return out


def build_pipes():
    mb = MB("Basement_Pipes")
    s = "metal_paint"
    rr = L.PIPE_R
    for pid, pts in L.PIPES.items():
        P = [Vector(p) for p in pts]
        for a, b in zip(P, P[1:]):
            d = b - a
            trim_lathe(mb, [(0, 0), (rr, 0), (rr, d.length), (0, d.length)], a, s, axis=tuple(d.normalized()),
                       seg=8)
        for c in P[1:-1]:      # elbows
            trim_lathe(mb, [(0, -rr * 1.35), (rr * 1.0, -rr * 1.2), (rr * 1.35, 0), (rr * 1.0, rr * 1.2),
                            (0, rr * 1.35)], c, s, seg=8)
        for a, b in zip(P, P[1:]):     # hanger straps from the joists every 1.2 m on horizontal runs
            d = b - a
            if abs(d.y) > 0.05 or a.y > -0.3:
                continue
            nseg = int(d.length / 1.2)
            for i in range(1, nseg + 1):
                c = a + d * (i / (nseg + 1))
                trim_box(mb, (c.x - 0.012, c.x + 0.012), (c.y, -0.45), (c.z - 0.012, c.z + 0.012), s, bev=0.003)
    return emit(mb)


# ------------------------------------------------------------------ fireplace
def build_fireplace():
    f = L.FIREPLACE
    br, fb = f["breast"], f["firebox"]
    out = []
    mb = MB("Fireplace_Front")
    panel_cut(mb, (br["x"], (0.0, L.CEILING), (fb["z"][1], br["z"][1])), 2, [(fb["x"], (0.0, fb["h"]))],
              lambda c, n: "M_BrickWhite")
    # steel lintel over the firebox
    trim_box(mb, (fb["x"][0] - 0.12, fb["x"][1] + 0.12), (fb["h"], fb["h"] + 0.07), (br["z"][1] - 0.02,
                                                                                       br["z"][1] + 0.008),
             "metal_paint", bev=0.003)
    out.append(emit(mb))
    mb = MB("Fireplace_Back")
    panel_cut(mb, (br["x"], (0.0, L.CEILING), (br["z"][0], fb["z"][1])), 2, [], lambda c, n: "M_BrickWhite")
    out.append(emit(mb))
    mb = MB("Fireplace_Hearth")
    h = f["hearth"]
    bm = bm_box(h["x"], (0.0, h["h"]), h["z"], 0.008)
    absorb(mb, bm, "M_Concrete", world_uv())
    out.append(emit(mb))
    mb = MB("Fireplace_Mantel")
    m = f["mantel"]
    strip = style_of("Bedroom", "door")
    (mx0, mx1), (mz0, mz1), (my0, my1) = m["x"], m["z"], m["y"]
    # moulded shelf: swept profile along x (front edge rounded, a cove under it)
    prof = [(mz0, my0 + 0.02), (mz1 - 0.03, my0 + 0.02), (mz1 - 0.012, my0), (mz1, my0 + 0.012), (mz1, my1 - 0.01),
            (mz1 - 0.01, my1), (mz0, my1)]
    sweep(mb, [V3(mx0, 0, 0), V3(mx1, 0, 0)], Vector((0, 1, 0)), [(-z, y) for z, y in prof], "M_Trim", strip,
          back=False, away=V3(mx0, 0, 5.0), polish=lambda c, n: 0.5 if n.y > 0.5 else 0.0)
    for cx in (mx0 + 0.12, mx1 - 0.12):     # corbels
        bm = bm_prism([(my0 - 0.22, br["z"][1]), (my0, br["z"][1]), (my0, mz1 - 0.04), (my0 - 0.05, mz1 - 0.05)],
                      lambda p, q, t: V3(t, p, q), (cx - 0.04, cx + 0.04))
        absorb(mb, bm, "M_Trim", TrimUV(strip, [v.co for v in bm.verts]))
    out.append(emit(mb))
    return out


def ground_hidden(c, n):
    """Ground faces nobody sees: the underside, the outer rim and the edges against the house walls (light-well
    edges stay)."""
    g = L.EXTERIOR["ground"]
    if n.y < -0.7:
        return True
    if abs(n.y) < 0.7:
        rim = not (g["x"][0] + 0.01 < c.x < g["x"][1] - 0.01 and g["z"][0] + 0.01 < c.z < g["z"][1] - 0.01)
        house = -0.01 <= c.x <= L.FOOTPRINT[0] + 0.01 and -0.01 <= c.z <= L.FOOTPRINT[1] + 0.01
        return rim or house
    return False


def build_exterior():
    ex = L.EXTERIOR
    holes = [((0.0, L.FOOTPRINT[0]), (0.0, L.FOOTPRINT[1]))]
    for o in L.OPENINGS.values():
        if o.get("well"):
            holes.append((o["well"]["x"], o["well"]["z"]))
    mb = MB("Ext_Ground")
    panel_cut(mb, (ex["ground"]["x"], (L.GROUND_BOTTOM, L.GRADE), ex["ground"]["z"]), 1, holes,
              lambda c, n: "M_Concrete", skip=ground_hidden)
    out = [emit(mb)]
    for nm in ("front_step", "back_step"):
        n = "Ext_" + "".join(s.capitalize() for s in nm.split("_"))
        mb = MB(n)
        for s in ex[nm]:
            bm = bm_box(s["x"], s["y"], s["z"], 0.01)
            absorb(mb, bm, "M_Concrete", world_uv())
        out.append(emit(mb))
    return out


# ------------------------------------------------------------------ build
def group(name, objs):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    bpy.context.scene.collection.objects.link(e)
    for o in objs:
        if o is not None:
            lib.child(o, e)
    return e


def greybox_colliders():
    """Run the grey-box builder for its collider boxes only (so the navmesh sources stay identical), then clear."""
    import bungalow_greybox as G
    keep = lib.uv_lightmap
    lib.uv_lightmap = lambda o, *a, **k: o
    try:
        lib.clear_scene()
        G.build()
        names = {o.name for o in bpy.context.scene.objects if o.type == 'MESH'}
        cols = {k: [dict(b) for b in v] for k, v in G._COLLIDERS.items()}
        nav, ns, piv = list(G._NAV_IGNORE), list(G._NON_STATIC), dict(G._PIVOTS)
    finally:
        lib.uv_lightmap = keep
        lib.clear_scene()
    return cols, nav, ns, piv, names


def build_shell(bake=True):
    """The whole house from kit pieces. Returns the root group."""
    global rng
    rng = random.Random(31)
    gcols, gnav, gns, gpiv, gnames = greybox_colliders()
    _STATE.update(colliders={}, nav_ignore=[], non_static=[], pivots={}, tris={})
    groups = []
    floors = build_floors()
    groups.append(group("Floors", [o for o in floors if not o.name.startswith("Ceiling_")]))
    groups.append(group("Ceilings", [o for o in floors if o.name.startswith("Ceiling_")]))
    groups.append(group("Walls", build_walls()))
    groups.append(group("Mouldings", build_mouldings()))
    ops = []
    for oid, o in L.OPENINGS.items():
        k = o["kind"]
        if k == "door":
            ops += build_door(oid)
        elif k == "arch":
            ops.append(build_arch(oid))
        elif k == "cased":
            ops.append(build_cased(oid))
        elif k == "window":
            ops += build_window(oid)
    ops.append(build_a4())
    groups.append(group("Openings", [o for o in ops if o and not o.name.startswith("Ext_")]))
    groups.append(group("Stairwell", [build_stair(), build_stair_rail(), build_balustrade(), build_gate()]))
    groups.append(group("Basement", build_beams() + [build_pipes()]))
    groups.append(group("Radiators", build_radiators()))
    groups.append(group("Fireplace", build_fireplace()))
    groups.append(group("Exterior", build_exterior() + [o for o in ops if o and o.name.startswith("Ext_")]))
    # grey-box colliders, attached to the kit object of the same name (or to an empty carrying them)
    meshes = {o.name: o for o in bpy.context.scene.objects if o.type == 'MESH'}
    extra = []
    for name, boxes in gcols.items():
        _STATE["colliders"].setdefault(name, []).extend(boxes)
        if name not in meshes:
            e = bpy.data.objects.new(name, None)
            e.empty_display_type = 'CUBE'
            e.empty_display_size = 0.05
            bpy.context.scene.collection.objects.link(e)
            extra.append(e)
    if extra:
        groups.append(group("Colliders", extra))
    _STATE["nav_ignore"] = sorted(set(_STATE["nav_ignore"]) | set(gnav))
    _STATE["non_static"] = sorted(set(_STATE["non_static"]) | set(gns))
    _STATE["pivots"].update(gpiv)
    _STATE["missing_from_kit"] = sorted(n for n in gcols if n not in meshes)
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            lib.finish(o, 50.0)
            _STATE["tris"][o.name] = sum(len(p.vertices) - 2 for p in o.data.polygons)
    import bungalow_wear as Wr
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    Wr.pack_uv2(objs)
    if bake and OPTS["wear"]:
        Wr.bake(objs)
    return groups[0]


# Pieces exported to the kit library (one of each type, re-centred at the origin, laid out along +X in Unity).
KIT_PIECES = [("Kit_Wall_Ext_S", "Wall_Ext_S"), ("Kit_Floor_PlankRoom", "Floor_CentralHall"),
              ("Kit_Floor_Parquet", "Floor_Living"), ("Kit_Ceiling", "Ceiling_Living"),
              ("Kit_Mouldings_Hall", "Mouldings_CentralHall"), ("Kit_DoorFrame", "D2_Frame"),
              ("Kit_DoorLeaf_Panel4", "D2_Leaf"), ("Kit_DoorLeaf_DoubleSagged", "D1_LeafR"),
              ("Kit_DoorLeaf_Ledged", "D5_Leaf"), ("Kit_Arch", "A1"), ("Kit_Arch_Overhead", "A4"),
              ("Kit_CasedOpening", "O1"), ("Kit_Window", "WR1"), ("Kit_Window_Glass", "WR1_Glass"),
              ("Kit_Window_Hopper", "BW1"), ("Kit_Stair", "Stair"), ("Kit_StairRail", "Stair_Rail"),
              ("Kit_Balustrade", "Stairwell_Balustrade"), ("Kit_Gate", "G1_Gate"), ("Kit_Joists", "Joists_B2"),
              ("Kit_Radiator", "Radiator_Hall"), ("Kit_Pipes", "Basement_Pipes"),
              ("Kit_Fireplace_Front", "Fireplace_Front"), ("Kit_Mantel", "Fireplace_Mantel")]


def build_kit():
    """Library: copies of shell pieces (same UV2, so they share the wear atlas) re-centred and laid out in a row."""
    build_shell(bake=False)
    meshes = {o.name: o for o in bpy.context.scene.objects if o.type == 'MESH'}
    lib_objs = []
    x = 0.0
    for kname, src in KIT_PIECES:
        s = meshes[src]
        me = s.data.copy()
        me.name = kname
        me.transform(s.matrix_world)
        vs = [v.co for v in me.vertices]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        c = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
        wid = hi.x - lo.x
        me.transform(Matrix.Translation(-c))
        o = bpy.data.objects.new(kname, me)
        bpy.context.scene.collection.objects.link(o)
        o.location = U2B((x + wid / 2, 0.0, 0.0))
        x += wid + 1.0
        lib_objs.append(o)
    for o in list(bpy.context.scene.objects):
        if o not in lib_objs:
            bpy.data.objects.remove(o, do_unlink=True)
    return group("Bungalow_Kit_Pieces", lib_objs)


def after_export(name, out_dir):
    if name != "Bungalow_Shell":
        return
    meshes = {o.name: o for o in bpy.context.scene.objects if o.type == 'MESH'}
    names = sorted(set(meshes) | set(_STATE["colliders"]))
    data = dict(source="tools/blender/bungalow_kit.py (boxes from bungalow_greybox)", units="m, Unity world space",
                objects=[dict(name=n, boxes=_STATE["colliders"].get(n, []), navIgnore=n in _STATE["nav_ignore"],
                              isStatic=n not in _STATE["non_static"], pivot=_STATE["pivots"].get(n, [0.0, 0.0, 0.0]),
                              tris=sum(len(p.vertices) - 2 for p in meshes[n].data.polygons) if n in meshes else 0)
                         for n in names])
    p = os.path.join(out_dir, name + "_colliders.json")
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1)
    tot = sum(d["tris"] for d in data["objects"])
    print(f"wrote {p} ({len(data['objects'])} objects, {sum(len(d['boxes']) for d in data['objects'])} boxes, "
          f"{tot} tris; collider-only empties: {_STATE.get('missing_from_kit')})")


BUILDERS["Bungalow_Shell"] = build_shell
BUILDERS["Bungalow_Kit"] = build_kit
SUBDIR["Bungalow_Shell"] = "Kit"
SUBDIR["Bungalow_Kit"] = "Kit"
