"""BLINK Bungalow grey-box (Phase 2): the shell built to exact dimensions from bungalow_layout, as plain blocks.

  blender -b --factory-startup --python tools/blender/build_all.py -- --target bungalow [--preview DIR]

-> Unity/Assets/Art/Environment/Bungalow/Greybox/Bungalow_Greybox.fbx
   Unity/Assets/Art/Environment/Bungalow/Greybox/Bungalow_Greybox_colliders.json
   Unity/Assets/Art/Environment/Bungalow/bungalow_layout.json

Every piece is a named mesh object (Floor_<Room>, Ceiling_<Room>, Wall_*, D1-D5, A1-A4, O1, WR1.., Stair, G1_Gate,
...) grouped under a few empties, with flat GB_* materials, box UVs on UV0 and a lightmap layout on UV1.
Geometry is authored in Unity coordinates (x east, y up, z north) and mapped into Blender with U2B; the FBX export
(forward -Z, up Y, baked space transform) maps it straight back, so the Unity transforms stay identity.
The colliders JSON lists simple box colliders (Unity world space, optional X tilt) per object for the assembly step.
Importing this module has no side effects; build() needs bpy.
"""
import json
import math
import os

import bmesh
import bpy
from mathutils import Vector

import bungalow_layout as L
import lib

BUILDERS = {}
SUBDIR = {}
SHARP = {}
_COLLIDERS = {}      # object name -> [dict(c=[x,y,z], s=[sx,sy,sz], r=[rx,ry,rz])]
_NAV_IGNORE = []     # objects the navmesh bake skips (ceilings: their tops are not floors)
_NON_STATIC = []     # objects that will move (G1 gate)
_PIVOTS = {}         # object name -> Unity pivot (objects whose origin is not the world origin)


def U2B(x, y, z):
    """Unity point -> Blender point for the FBX export used by build_all (Unity = (-bx, bz, -by))."""
    return Vector((-x, -z, y))


def _r(v):
    return round(float(v), 4)


def collide(name, x, y, z, rot_x=0.0):
    _COLLIDERS.setdefault(name, []).append(dict(
        c=[_r((x[0] + x[1]) / 2), _r((y[0] + y[1]) / 2), _r((z[0] + z[1]) / 2)],
        s=[_r(x[1] - x[0]), _r(y[1] - y[0]), _r(z[1] - z[0])], r=[_r(rot_x), 0.0, 0.0]))


def collide_tilted(name, center, size, rot_x):
    _COLLIDERS.setdefault(name, []).append(dict(c=[_r(v) for v in center], s=[_r(v) for v in size],
                                               r=[_r(rot_x), 0.0, 0.0]))


# ------------------------------------------------------------------ mesh builders (Unity coordinates)
def panel(name, rng, t_axis, holes, m, col=None):
    """Axis-aligned block rng = ((x0, x1), (y0, y1), (z0, z1)) with rectangular through-holes along t_axis
    (0 = x, 1 = y, 2 = z). holes = [((p0, p1), (q0, q1))] in the other two axes, in ascending axis order.
    Built from a cut grid, so there are no internal faces or T-junctions. col = collider key (merged boxes)."""
    p_ax, q_ax = [i for i in range(3) if i != t_axis]
    P, Q, T = rng[p_ax], rng[q_ax], rng[t_axis]
    ps = sorted({P[0], P[1], *(v for h in holes for v in h[0] if P[0] < v < P[1])})
    qs = sorted({Q[0], Q[1], *(v for h in holes for v in h[1] if Q[0] < v < Q[1])})
    ni, nj = len(ps) - 1, len(qs) - 1

    def in_hole(pm, qm):
        return any(h[0][0] < pm < h[0][1] and h[1][0] < qm < h[1][1] for h in holes)

    solid = [[not in_hole((ps[i] + ps[i + 1]) / 2, (qs[j] + qs[j + 1]) / 2) for j in range(nj)] for i in range(ni)]
    bm = bmesh.new()
    vc = {}

    def V(i, j, k):
        key = (i, j, k)
        if key not in vc:
            c = [0.0, 0.0, 0.0]
            c[p_ax], c[q_ax], c[t_axis] = ps[i], qs[j], T[k]
            vc[key] = bm.verts.new(U2B(*c))
        return vc[key]

    def S(i, j):
        return 0 <= i < ni and 0 <= j < nj and solid[i][j]

    for i in range(ni):
        for j in range(nj):
            if not solid[i][j]:
                continue
            for k in (0, 1):
                bm.faces.new([V(i, j, k), V(i + 1, j, k), V(i + 1, j + 1, k), V(i, j + 1, k)])
            if not S(i - 1, j):
                bm.faces.new([V(i, j, 0), V(i, j + 1, 0), V(i, j + 1, 1), V(i, j, 1)])
            if not S(i + 1, j):
                bm.faces.new([V(i + 1, j, 0), V(i + 1, j + 1, 0), V(i + 1, j + 1, 1), V(i + 1, j, 1)])
            if not S(i, j - 1):
                bm.faces.new([V(i, j, 0), V(i + 1, j, 0), V(i + 1, j, 1), V(i, j, 1)])
            if not S(i, j + 1):
                bm.faces.new([V(i, j + 1, 0), V(i + 1, j + 1, 0), V(i + 1, j + 1, 1), V(i, j + 1, 1)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if col:
        # merge solid cells into boxes: vertical runs per column, then identical runs across columns
        runs = []
        for i in range(ni):
            r, j = [], 0
            while j < nj:
                if solid[i][j]:
                    j0 = j
                    while j < nj and solid[i][j]:
                        j += 1
                    r.append((j0, j))
                else:
                    j += 1
            runs.append(set(r))
        open_ = {}
        for i in range(ni + 1):
            cur = runs[i] if i < ni else set()
            for run in [r for r in open_ if r not in cur]:
                i0 = open_.pop(run)
                b = [None, None, None]
                b[p_ax], b[q_ax], b[t_axis] = (ps[i0], ps[i]), (qs[run[0]], qs[run[1]]), T
                collide(col, *b)
            for run in cur:
                open_.setdefault(run, i)
    return lib._obj(name, bm, m)


def block(name, x, y, z, m, col=None):
    return panel(name, (x, y, z), 1, [], m, col)


def prism(name, pts, t_axis, t_rng, m):
    """Closed 2D outline pts [(p, q)] in the two axes other than t_axis (ascending axis order), extruded
    along t_axis over t_rng."""
    p_ax, q_ax = [i for i in range(3) if i != t_axis]

    def P3(p, q, t):
        c = [0.0, 0.0, 0.0]
        c[p_ax], c[q_ax], c[t_axis] = p, q, t
        return U2B(*c)

    bm = bmesh.new()
    a = [bm.verts.new(P3(p, q, t_rng[0])) for p, q in pts]
    b = [bm.verts.new(P3(p, q, t_rng[1])) for p, q in pts]
    bm.faces.new(a)
    bm.faces.new(b[::-1])
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], b[i], b[j], a[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], ngon_method='BEAUTY')
    return lib._obj(name, bm, m)


def group(name, objs):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    bpy.context.scene.collection.objects.link(e)
    for o in objs:
        if o is not None:
            lib.child(o, e)
    return e


def joined(name, parts):
    parts = [p for p in parts if p is not None]
    return lib.join(parts, name) if parts else None


# ------------------------------------------------------------------ wall helpers
def _wall_axes(w):
    """(along axis, thickness axis) indices for a wall dict."""
    return (0, 2) if w["axis"] == "x" else (2, 0)


def wall_rng(w, a, y, t=None):
    al, th = _wall_axes(w)
    r = [None, None, None]
    r[al], r[1], r[th] = a, y, (t or w["t"])
    return tuple(r)


def wall_hole(w, a, y):
    """Hole ((p0, p1), (q0, q1)) in ascending axis order for panel()."""
    return (a, y) if w["axis"] == "x" else (y, a)


def wall_pts(w, pts):
    """[(a, y)] outline in the wall plane -> prism pts in ascending axis order."""
    return pts if w["axis"] == "x" else [(y, a) for a, y in pts]


def wall_prism(name, w, pts, m, grow=0.0):
    t = (w["t"][0] - grow, w["t"][1] + grow)
    return prism(name, wall_pts(w, pts), _wall_axes(w)[1], t, m)


def arc(a0, a1, spring, crown, n=12, grow=0.0):
    """Segmental arc from (a0, spring) over the crown to (a1, spring); grow offsets the radius outwards."""
    w, h = a1 - a0, crown - spring
    R = (w * w / 4 + h * h) / (2 * h)
    am, cy = (a0 + a1) / 2, crown - R
    half = math.asin(min(1.0, (w / 2) / R))
    return [(am + (R + grow) * math.sin(-half + 2 * half * i / n), cy + (R + grow) * math.cos(-half + 2 * half * i / n))
            for i in range(n + 1)]


# ------------------------------------------------------------------ pieces
FLOOR_MAT = {"Kitchen": "GB_FloorTile", "Bathroom": "GB_FloorTile"}
LEVEL_FLOOR_MAT = {"basement": "GB_Basement"}


def floors_and_ceilings():
    """One floor slab per room (and a ceiling slab on levels that have one), cut by the room's floor_holes /
    ceiling_holes. New rooms, levels, wells and hatches are layout entries only."""
    floors, ceils = [], []
    st = L.STAIR
    for rid, r in L.ROOMS.items():
        lv = L.LEVELS[r["level"]]
        y0 = lv["floor"]
        holes = [(h["x"], h["z"]) for h in r.get("floor_holes", [])]
        mat = FLOOR_MAT.get(rid) or LEVEL_FLOOR_MAT.get(r["level"], "GB_Floor")
        n = "Floor_" + rid
        floors.append(panel(n, (r["x"], (y0 - lv["slab"], y0), r["z"]), 1, holes, mat, col=n))
        if lv.get("ceiling_piece"):
            n = "Ceiling_" + rid
            holes = [(h["x"], h["z"]) for h in r.get("ceiling_holes", [])]
            ceils.append(panel(n, (r["x"], (lv["ceiling"], lv["ceiling_top"]), r["z"]), 1, holes, "GB_Ceiling",
                               col=n))
            _NAV_IGNORE.append(n)
    ld = st["landing"]
    floors.append(block("Floor_B1_Landing", ld["x"], (L.BASEMENT - L.BASEMENT_SLAB, L.BASEMENT), ld["z"],
                        "GB_Basement", col="Floor_B1_Landing"))
    return floors, ceils


def walls():
    out = []
    for wid, w in L.WALLS.items():
        holes = []
        for oid in w["openings"]:
            o = L.OPENINGS[oid]
            holes.append(wall_hole(w, o["span"], o["y"]))
        mat = "GB_Basement" if wid.startswith("Wall_B_") else "GB_Wall"
        out.append(panel(wid, wall_rng(w, w["a"], w["y"]), _wall_axes(w)[1], holes, mat, col=wid))
    return out


def door(oid):
    o = L.OPENINGS[oid]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    c, t = L.CASING, (w["t"][0] - 0.02, w["t"][1] + 0.02)
    parts = [block(oid + "_jL", *_xyz(w, (a0 - c, a0), (y0, y1 + c), t), "GB_Trim"),
             block(oid + "_jR", *_xyz(w, (a1, a1 + c), (y0, y1 + c), t), "GB_Trim"),
             block(oid + "_hd", *_xyz(w, (a0, a1), (y1, y1 + c), t), "GB_Trim")]
    return joined(oid, parts)


def _xyz(w, a, y, t):
    """Range triple (x, y, z) for an in-wall box: a along the wall, y up, t across the wall."""
    al, th = _wall_axes(w)
    r = [None, None, None]
    r[al], r[1], r[th] = a, y, t
    return tuple(r)


def arch(oid):
    o = L.OPENINGS[oid]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, crown), spring = o["span"], o["y"], o["spring"]
    pts = arc(a0, a1, spring, crown)
    n = len(pts) - 1
    left = [(a0, crown)] + pts[:n // 2 + 1]
    right = pts[n // 2:] + [(a1, crown)]
    c, t = L.CASING, (w["t"][0] - 0.02, w["t"][1] + 0.02)
    outer = arc(a0, a1, spring, crown, grow=c)
    band = [(a0 - c, spring)] + outer + [(a1 + c, spring)] + pts[::-1]
    parts = [wall_prism(oid + "_spL", w, left, "GB_Wall"), wall_prism(oid + "_spR", w, right, "GB_Wall"),
             wall_prism(oid + "_band", w, band, "GB_Trim", grow=0.02),
             block(oid + "_jL", *_xyz(w, (a0 - c, a0), (y0, spring), t), "GB_Trim"),
             block(oid + "_jR", *_xyz(w, (a1, a1 + c), (y0, spring), t), "GB_Trim")]
    return joined(oid, parts)


def arch_a4():
    a = L.A4
    (x0, x1), sp, cr = a["x"], a["spring"], a["crown"]
    pts = arc(x0, x1, sp, cr)
    soffit = [(x0, L.CEILING), (x0, sp)] + pts[1:-1] + [(x1, sp), (x1, L.CEILING)]
    band = [(x0, sp)] + pts[1:-1] + [(x1, sp)]
    outer = [(x1, sp + L.CASING)] + arc(x0, x1, sp, cr, grow=L.CASING)[-2:0:-1] + [(x0, sp + L.CASING)]
    z = a["z"]
    parts = [prism("A4_soffit", soffit, 2, z, "GB_Wall"),
             prism("A4_band", band + outer, 2, (z[0] - 0.02, z[1] + 0.02), "GB_Trim")]
    return joined("A4", parts)


def cased(oid):
    """O1: cased opening 1.6 x 2.1 with a transom (3 bars) above."""
    o, tr = L.OPENINGS[oid], L.OPENINGS[oid + "_Transom"]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    c, t = L.CASING, (w["t"][0] - 0.02, w["t"][1] + 0.02)
    ty0, ty1 = tr["y"]
    parts = [block(oid + "_jL", *_xyz(w, (a0 - c, a0), (y0, ty1 + 0.05), t), "GB_Trim"),
             block(oid + "_jR", *_xyz(w, (a1, a1 + c), (y0, ty1 + 0.05), t), "GB_Trim"),
             block(oid + "_rail", *_xyz(w, (a0, a1), (y1, ty0), t), "GB_Trim"),
             block(oid + "_top", *_xyz(w, (a0, a1), (ty1, ty1 + 0.05), t), "GB_Trim")]
    nb = tr["bars"]
    for i in range(nb):
        ac = a0 + (a1 - a0) * (i + 1) / (nb + 1)
        parts.append(block(f"{oid}_bar{i}", *_xyz(w, (ac - 0.02, ac + 0.02), (ty0, ty1),
                                                  (w["t"][0] + 0.05, w["t"][1] - 0.05)), "GB_Trim"))
    return joined(oid, parts)


def window(oid):
    o = L.OPENINGS[oid]
    w = L.WALLS[o["wall"]]
    (a0, a1), (y0, y1) = o["span"], o["y"]
    f = 0.07
    t = (w["t"][0] - 0.02, w["t"][1] + 0.02)
    tm = (w["t"][0] + w["t"][1]) / 2
    parts = [block(oid + "_jL", *_xyz(w, (a0 - f, a0), (y0 - 0.05, y1 + f), t), "GB_Trim"),
             block(oid + "_jR", *_xyz(w, (a1, a1 + f), (y0 - 0.05, y1 + f), t), "GB_Trim"),
             block(oid + "_hd", *_xyz(w, (a0, a1), (y1, y1 + f), t), "GB_Trim"),
             block(oid + "_sill", *_xyz(w, (a0 - f, a1 + f), (y0 - 0.05, y0), (w["t"][0] - 0.06, w["t"][1] + 0.06)),
                   "GB_Trim"),
             block(oid + "_mull", *_xyz(w, ((a0 + a1) / 2 - 0.025, (a0 + a1) / 2 + 0.025), (y0, y1),
                                        (tm - 0.03, tm + 0.03)), "GB_Trim")]
    frame = joined(oid, parts)
    gname = oid + "_Glass"
    glass = block(gname, *_xyz(w, (a0, a1), (y0, y1), (tm - 0.01, tm + 0.01)), "GB_Glass", col=gname)
    out = [frame, glass]
    if o.get("boarded"):
        # boards on the outside face (the outside of a south wall is low z, of an east wall high x)
        # outside = the face away from the footprint centre (or o["outside"] = "low" / "high" for wings)
        mid = L.FOOTPRINT[0 if w["axis"] == "z" else 1] / 2
        on_low_side = o.get("outside", "low" if sum(w["t"]) / 2 < mid else "high") == "low"
        tb = (w["t"][0] - 0.03, w["t"][0]) if on_low_side else (w["t"][1], w["t"][1] + 0.03)
        boards = [block(f"{oid}_board{i}", *_xyz(w, (a0 - 0.08, a1 + 0.08), (yb, yb + 0.16), tb), "GB_Trim")
                  for i, yb in enumerate((y0 + 0.12, y0 + 0.55, y0 + 1.0))]
        out.append(joined(oid + "_Boards", boards))
    if o.get("well"):
        wl = o["well"]
        n = "Ext_LightWell_" + oid
        out.append(block(n, wl["x"], (L.GROUND_BOTTOM, wl["floor"]), wl["z"], "GB_Basement", col=n))
    return out


def stair():
    st = L.STAIR
    x, zt, rise, run = st["x"], st["z_top"], st["rise"], st["run"]
    # solid flight profile in (y, z): 13 treads, 13 risers in the mesh + the slab edge = 14 risers
    pts = [(L.BASEMENT, zt)]
    for k in range(1, st["treads"] + 1):
        y = -k * rise
        pts += [(y, zt + (k - 1) * run), (y, zt + k * run)]
    pts.append((L.BASEMENT, st["z_bottom"]))
    flight = prism("Stair", pts, 0, x, "GB_Stair")
    # colliders: a ramp through the nosings (smooth CharacterController / navmesh slope) + the solid treads
    th = math.atan2(rise, run)
    z_end = zt + (0 - L.BASEMENT) / math.tan(th)            # pitch line meets the basement floor (6.92)
    length = math.hypot(z_end - zt, 0 - L.BASEMENT)
    thick = 0.3
    up = (0.0, math.cos(th), math.sin(th))
    mid = (0.0, L.BASEMENT / 2, (zt + z_end) / 2)
    collide_tilted("Stair", ((x[0] + x[1]) / 2, mid[1] - up[1] * thick / 2, mid[2] - up[2] * thick / 2),
                   (x[1] - x[0], thick, length), math.degrees(th))
    for k in range(1, st["treads"] + 1):
        collide("Stair", x, (L.BASEMENT, -k * rise), (zt + (k - 1) * run, zt + k * run))
    return flight


def stair_rail():
    """Stair-side guard in the well along x 5.035-5.075: balusters from each tread up to the handrail (or to the
    header underside where the handrail would run into the slab), sloped handrail, foot newel."""
    st = L.STAIR
    rx, zt, rise, run = st["rail_x"], st["z_top"], st["rise"], st["run"]
    pitch = lambda z: -(z - zt) * rise / run  # noqa: E731
    hr = st["handrail_above_pitch"]
    parts = []
    for k in range(1, st["treads"] + 1):
        for f in (0.25, 0.75):
            zc = zt + (k - 1 + f) * run
            top = min(pitch(zc) + hr - 0.05, L.TRIMMER_UNDERSIDE)
            if top < -k * rise + 0.1:   # top treads: the header and slab edge already close the side
                continue
            parts.append(block(f"SR_b{k}_{f}", rx, (-k * rise, top), (zc - 0.02, zc + 0.02), "GB_Trim"))
    z_hr = zt + (hr - L.TRIMMER_UNDERSIDE) * run / rise    # where pitch + hr drops below the header (4.89)
    zb = st["z_bottom"]
    rail = [(pitch(z_hr) + hr - 0.06, z_hr), (pitch(z_hr) + hr, z_hr), (pitch(zb) + hr, zb), (pitch(zb) + hr - 0.06, zb)]
    parts.append(prism("SR_handrail", rail, 0, (rx[0] - 0.01, rx[1] + 0.01), "GB_Trim"))
    parts.append(block("SR_newel", (rx[0], rx[0] + 0.1), (L.BASEMENT, pitch(zb) + hr + 0.05), (zb, zb + 0.1),
                       "GB_Trim"))
    o = joined("Stair_Rail", parts)
    # one upright box per tread (a tilted box would overhang the landing past the stair foot)
    for k in range(1, st["treads"] + 1):
        z0, z1 = zt + (k - 1) * run, zt + k * run
        collide("Stair_Rail", rx, (-k * rise, pitch(z0) + hr + 0.1), (z0, z1))
    collide("Stair_Rail", (rx[0], rx[0] + 0.1), (L.BASEMENT, pitch(zb) + hr + 0.05), (zb, zb + 0.1))
    return o


def balustrade():
    b = L.BALUSTRADE
    e, n = b["east"], b["north"]
    nw, nh, bh, bw, sp = b["newel"], b["newel_h"], b["height"], b["baluster"], b["spacing"]
    parts = []
    ex0, ex1 = e["x"]
    nz0, nz1 = n["z"]
    # newels: stair top (east), NE corner, against the west wall
    zt = L.STAIR["z_top"]
    parts.append(block("BL_newel_S", e["x"], (0.0, nh), (zt - nw / 2, zt + nw / 2), "GB_Trim"))
    parts.append(block("BL_newel_NE", e["x"], (0.0, nh), (nz0, nz1), "GB_Trim"))
    parts.append(block("BL_newel_W", (n["x"][0], n["x"][0] + nw), (0.0, nh), (nz0, nz1), "GB_Trim"))
    # east run
    za, zb = zt + nw / 2, nz0
    parts.append(block("BL_e_rail", e["x"], (bh - 0.06, bh), (za, zb), "GB_Trim"))
    parts.append(block("BL_e_base", e["x"], (0.0, 0.06), (za, zb), "GB_Trim"))
    xm = (ex0 + ex1) / 2
    k = int((zb - za) / sp)
    for i in range(k):
        zc = za + (zb - za) * (i + 0.5) / k
        parts.append(block(f"BL_e_b{i}", (xm - bw / 2, xm + bw / 2), (0.06, bh - 0.06), (zc - bw / 2, zc + bw / 2),
                           "GB_Trim"))
    # north run
    xa, xb = n["x"][0] + nw, ex0
    parts.append(block("BL_n_rail", (xa, xb), (bh - 0.06, bh), n["z"], "GB_Trim"))
    parts.append(block("BL_n_base", (xa, xb), (0.0, 0.06), n["z"], "GB_Trim"))
    zm = (nz0 + nz1) / 2
    k = int((xb - xa) / sp)
    for i in range(k):
        xc = xa + (xb - xa) * (i + 0.5) / k
        parts.append(block(f"BL_n_b{i}", (xc - bw / 2, xc + bw / 2), (0.06, bh - 0.06), (zm - bw / 2, zm + bw / 2),
                           "GB_Trim"))
    o = joined("Stairwell_Balustrade", parts)
    collide("Stairwell_Balustrade", e["x"], (0.0, nh), (zt - nw / 2, nz1))
    collide("Stairwell_Balustrade", (n["x"][0], ex0), (0.0, nh), n["z"])
    return o


def gate():
    """G1 placeholder leaf, OPEN: hinged at the stair-top newel, folded back along the east balustrade inside the
    well. Origin = hinge; closing it is a yaw of -90 about the origin."""
    g = L.GATE
    hx, hy, hz = g["hinge"]
    x = (hx - 0.02, hx + 0.02)
    z0, z1 = hz + 0.02, hz + g["leaf"]
    y0, y1 = hy + 0.06, hy + g["height"]
    parts = [block("G1_stile0", x, (y0, y1), (z0, z0 + 0.04), "GB_Gate"),
             block("G1_stile1", x, (y0, y1), (z1 - 0.04, z1), "GB_Gate"),
             block("G1_top", x, (y1 - 0.05, y1), (z0, z1), "GB_Gate"),
             block("G1_bot", x, (y0, y0 + 0.05), (z0, z1), "GB_Gate")]
    for i in range(6):
        zc = z0 + (z1 - z0) * (i + 1) / 7
        parts.append(block(f"G1_bar{i}", (hx - 0.01, hx + 0.01), (y0 + 0.05, y1 - 0.05), (zc - 0.01, zc + 0.01),
                           "GB_Gate"))
    o = joined("G1_Gate", parts)
    lib.set_origin(o, U2B(hx, hy, hz))
    collide("G1_Gate", x, (y0, y1), (z0, z1))
    _NON_STATIC.append("G1_Gate")
    _PIVOTS["G1_Gate"] = [hx, hy, hz]
    return o


def beams():
    out = []
    for bid, b in L.BEAMS.items():
        out.append(block(bid, b["x"], b["y"], b["z"], "GB_Trim", col=bid))
    j, st = L.JOIST, L.STAIR
    for rid in ("B1", "B2"):
        r = L.ROOMS[rid]
        parts = []
        z = j["first_z"]
        i = 0
        while z < r["z"][1]:
            zr = (z - j["width"] / 2, z + j["width"] / 2)
            clash = rid == "B1" and any(not (zr[1] <= b["z"][0] or zr[0] >= b["z"][1])
                                        for b in (L.BEAMS["Trimmer_N"], L.BEAMS["Trimmer_S"]))
            if not clash:
                x0 = r["x"][0]
                if rid == "B1" and st["opening"]["z"][0] <= z <= st["opening"]["z"][1]:
                    x0 = L.BEAMS["Header_E"]["x"][1]
                parts.append(block(f"{rid}_j{i}", (x0, r["x"][1]), (-L.SLAB - j["depth"], -L.SLAB), zr, "GB_Trim"))
            z = round(z + j["pitch"], 4)
            i += 1
        out.append(joined(f"Joists_{rid}", parts))
    return out


def fireplace():
    f = L.FIREPLACE
    br, fb = f["breast"], f["firebox"]
    front = (fb["z"][1], br["z"][1])
    o = [panel("Fireplace_Front", (br["x"], (0.0, L.CEILING), front), 2, [(fb["x"], (0.0, fb["h"]))], "GB_Brick",
               col="Fireplace_Front"),
         block("Fireplace_Back", br["x"], (0.0, L.CEILING), (br["z"][0], fb["z"][1]), "GB_Brick",
               col="Fireplace_Back"),
         block("Fireplace_Hearth", f["hearth"]["x"], (0.0, f["hearth"]["h"]), f["hearth"]["z"], "GB_Basement",
               col="Fireplace_Hearth"),
         block("Fireplace_Mantel", f["mantel"]["x"], f["mantel"]["y"], f["mantel"]["z"], "GB_Trim",
               col="Fireplace_Mantel")]
    return o


def exterior():
    ex = L.EXTERIOR
    holes = [((0.0, L.FOOTPRINT[0]), (0.0, L.FOOTPRINT[1]))]
    for o in L.OPENINGS.values():
        if o.get("well"):
            holes.append((o["well"]["x"], o["well"]["z"]))
    out = [panel("Ext_Ground", (ex["ground"]["x"], (L.GROUND_BOTTOM, L.GRADE), ex["ground"]["z"]), 1, holes,
                 "GB_Ground", col="Ext_Ground")]
    for nm in ("front_step", "back_step"):
        n = "Ext_" + "".join(s.capitalize() for s in nm.split("_"))
        out.append(joined(n, [block(f"{n}_{i}", s["x"], s["y"], s["z"], "GB_Basement", col=n)
                              for i, s in enumerate(ex[nm])]))
    return out


# ------------------------------------------------------------------ build
def build():
    for d in (_COLLIDERS, _PIVOTS):
        d.clear()
    del _NAV_IGNORE[:], _NON_STATIC[:]
    floors, ceils = floors_and_ceilings()
    groups = [group("Floors", floors), group("Ceilings", ceils), group("Walls", walls())]
    openings = []
    for oid, o in L.OPENINGS.items():
        if o["kind"] == "door":
            openings.append(door(oid))
        elif o["kind"] == "arch":
            openings.append(arch(oid))
        elif o["kind"] == "cased":
            openings.append(cased(oid))
        elif o["kind"] == "window":
            openings += window(oid)
    openings.append(arch_a4())
    groups.append(group("Openings", [o for o in openings if not o.name.startswith("Ext_")]))
    groups.append(group("Stairwell", [stair(), stair_rail(), balustrade(), gate()]))
    groups.append(group("Basement_Beams", beams()))
    groups.append(group("Fireplace", fireplace()))
    groups.append(group("Exterior", exterior() + [o for o in openings if o.name.startswith("Ext_")]))
    for o in list(bpy.context.scene.objects):
        if o.type == 'MESH':
            lib.uv_lightmap(o)
    return groups[0]


def after_export(name, out_dir):
    """Sidecar data for the Unity assembly step: colliders (Unity world space) and the layout JSON."""
    meshes = {o.name: o for o in bpy.context.scene.objects if o.type == 'MESH'}
    data = dict(source="tools/blender/bungalow_greybox.py", units="m, Unity world space",
                objects=[dict(name=n, boxes=_COLLIDERS.get(n, []), navIgnore=n in _NAV_IGNORE,
                              isStatic=n not in _NON_STATIC, pivot=_PIVOTS.get(n, [0.0, 0.0, 0.0]),
                              tris=sum(len(p.vertices) - 2 for p in meshes[n].data.polygons))
                         for n in sorted(meshes)])
    missing = sorted(set(_COLLIDERS) - set(meshes))
    assert not missing, f"colliders for unknown objects: {missing}"
    p = os.path.join(out_dir, name + "_colliders.json")
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=1)
    print("wrote", p, "and", L.write_json())


BUILDERS["Bungalow_Greybox"] = build
SUBDIR["Bungalow_Greybox"] = "Greybox"
