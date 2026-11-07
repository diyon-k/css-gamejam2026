"""BLINK Bungalow (Phase 5): prop and light-fixture models (bpy). Driven by bungalow_prop_build.py.

Blender convention (lib.py): metres, Z up, the model FRONT faces -Y, so in Unity the front faces +Z.
Pivots ("pivot" in MODELS):
  floor   - footprint centre at the floor (furniture, floor clutter)
  wall    - back face centre on the wall (pictures, sconces, cabinets on the wall); the model lies in y <= 0
  ceiling - the ceiling contact point (fan, chandelier, domes, pendants); the model hangs in z <= 0
  top     - base centre (things that sit on a table or shelf: lamps, clocks, clutter)
Every model is ONE mesh object named after the model (atlas material + optional M_Shade / M_Glass slots). Fixtures
add a child mesh "<Model>_Emissive" (the M_Shade glass / shade / bulb, so Phase 6 can find and drive it) and an empty
"<Model>_Emitter" at the light position.

Textures: every atlas part is UV-fitted into a cell of the 4 prop atlases (bungalow_prop_textures.py):
  fit(part, key)       swatch cells: the part's metric box / cylinder UVs are scaled by the cell's real size and
                       compressed (never stretched) when the part is bigger than the cell
  fit(part, key, "stretch")   the part's UV bounds fill the cell (canvases, covers, labels, VCR, speaker)
  ph(asset, tris)      a Poly Haven model, decimated, its own 0..1 UVs remapped into its cell
"""
import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import lib
import assets
from lib import box_z, cyl, lathe, rounded_box, torus, tube, xf, join, grid, solidify

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "Unity", "Assets", "Art")
MANSION_PH = os.path.join(ART, "Mansion", "PolyHaven")
ATLAS_JSON = os.path.join(ART, "Environment", "Bungalow", "Props", "bungalow_prop_atlas.json")
SRC = None            # download root (dl_<asset>/), set by the build script
CELLS = {}            # key -> dict(atlas, uv, m)
ATLAS_MATS = ("M_Atlas_Soft", "M_Atlas_Wood", "M_Atlas_Small", "M_Atlas_Utility")
R = random.Random(5)

# preview colours for the materials the FBX carries (Unity remaps them onto the real .mat assets)
for _n, _c in (("M_Atlas_Soft", (0.3, 0.12, 0.1)), ("M_Atlas_Wood", (0.25, 0.15, 0.08)),
               ("M_Atlas_Small", (0.5, 0.45, 0.4)), ("M_Atlas_Utility", (0.6, 0.6, 0.55)),
               ("M_Shade", (0.95, 0.88, 0.7)), ("M_Glass", (0.6, 0.7, 0.7))):
    lib.MATERIALS[_n] = (_c, None, _c, 0.5, 1)


def load_cells():
    CELLS.clear()
    with open(ATLAS_JSON, encoding="utf-8") as f:
        data = json.load(f)
    for atlas, a in data["atlases"].items():
        for k, c in a["cells"].items():
            CELLS[k] = dict(atlas="M_Atlas_" + atlas, uv=c["uv"], m=c["m"])


# ------------------------------------------------------------------ UV fitting
def _uv_bounds(me, layer):
    us = [d.uv.x for d in layer.data]
    vs = [d.uv.y for d in layer.data]
    return min(us), min(vs), max(us), max(vs)


def fit(o, key, mode="swatch", rot=False):
    """Remap o's UV0 into atlas cell `key` and give it that atlas material. rot swaps U/V first."""
    c = CELLS[key]
    me = o.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    ly = me.uv_layers[0]
    if rot:
        for d in ly.data:
            d.uv = (d.uv.y, d.uv.x)
    u0, v0, u1, v1 = _uv_bounds(me, ly)
    cu0, cv0, cu1, cv1 = c["uv"]
    if mode == "stretch" or not c["m"][0]:
        su = 1.0 / max(u1 - u0, 1e-6)
        sv = 1.0 / max(v1 - v0, 1e-6)
        ou = ov = 0.0
    else:
        mu, mv = c["m"]
        k = min(1.0, mu / max(u1 - u0, 1e-6), mv / max(v1 - v0, 1e-6))
        su, sv = k / mu, k / mv
        ou = R.uniform(0, max(0.0, 1 - (u1 - u0) * su))
        ov = R.uniform(0, max(0.0, 1 - (v1 - v0) * sv))
    for d in ly.data:
        u = ou + (d.uv.x - u0) * su
        v = ov + (d.uv.y - v0) * sv
        d.uv = (cu0 + min(max(u, 0), 1) * (cu1 - cu0), cv0 + min(max(v, 0), 1) * (cv1 - cv0))
    me.materials.clear()
    me.materials.append(lib.mat(c["atlas"]))
    return o


def F(o, key, mode="swatch", rot=False):
    return fit(o, key, mode, rot)


def shade(o):
    lib.set_mat(o, "M_Shade")
    return o


def glass(o):
    lib.set_mat(o, "M_Glass")
    return o


# ------------------------------------------------------------------ Poly Haven import
def _ph_fbx(asset):
    for d in (os.path.join(MANSION_PH, asset), os.path.join(SRC or "", "dl_" + asset)):
        if os.path.isdir(d):
            for f in os.listdir(d):
                if f.endswith(".fbx"):
                    return os.path.join(d, f)
    raise FileNotFoundError(asset)


def ph(asset, tris, keys=None, objects=None, drop=(), name=None, recentre=True, split=()):
    """Import a Poly Haven FBX, join it, decimate to `tris`, remap each material's UVs into its atlas cell.
    keys: {material name substring -> cell key | "GLASS" | "SHADE" | None (delete)}; default cell = the asset.
    objects: only keep objects whose name starts with one of these. drop: object names to delete.
    split: objects whose name starts with one of these stay separate, as child meshes <name>_Drawer1.. (top first),
    each pivoted at its own centre so it can slide (openable drawers); the triangle budget is shared by triangle count."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=_ph_fbx(asset))
    new = [o for o in bpy.data.objects if o not in before]
    meshes = []
    for o in new:
        keep = o.type == 'MESH' and not any(o.name.startswith(d) for d in drop) and \
            (objects is None or any(o.name.startswith(p) for p in objects))
        if keep:
            meshes.append(o)
    bpy.context.view_layer.update()
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.data = o.data.copy()
        o.data.transform(mw)
        o.matrix_world = Matrix.Identity(4)
    for o in new:
        if o not in meshes:
            bpy.data.objects.remove(o, do_unlink=True)
    parts = [m for m in meshes if any(m.name.startswith(s) for s in split)]
    body = [m for m in meshes if m not in parts]
    count = {m: sum(len(p.vertices) - 2 for p in m.data.polygons) for m in meshes}
    total = max(1, sum(count.values()))

    def budget(objs):
        return int(round(tris * sum(count[m] for m in objs) / total)) if tris else 0

    part_budgets = [budget([p]) for p in parts]
    o = _ph_mesh(join(body, name or asset), asset, keys, budget(body))
    parts = [_ph_mesh(p, asset, keys, b) for p, b in zip(parts, part_budgets)]
    if recentre:
        # floor-centre on the whole model (body + split parts), as centre_floor did on the joined mesh
        lo, hi = bounds(o)
        for p in parts:
            plo, phi = bounds(p)
            lo = Vector([min(lo[i], plo[i]) for i in range(3)])
            hi = Vector([max(hi[i], phi[i]) for i in range(3)])
        t = Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
        for m in [o] + parts:
            m.data.transform(t)
    parts.sort(key=lambda p: -sum(bounds(p)[k].z for k in (0, 1)))
    for i, p in enumerate(parts, 1):
        plo, phi = bounds(p)
        c = (plo + phi) / 2
        p.data.transform(Matrix.Translation(-c))
        p.location = c
        p.name = p.data.name = f"{name or asset}_Drawer{i}"
        lib.child(p, o)
    return o


def _ph_mesh(o, asset, keys, tris):
    """ph() per mesh: material cells, dropped faces, decimation to `tris`, atlas UV remap."""
    me = o.data
    # materials -> cells
    keys = keys or {}
    slot_key = []
    for m in me.materials:
        mn = m.name if m else ""
        k = asset
        for sub, val in keys.items():
            if sub in mn:
                k = val
                break
        else:
            if "glass" in mn.lower():
                k = "GLASS"
        slot_key.append(k)
    # delete faces of dropped materials
    bm = bmesh.new()
    bm.from_mesh(me)
    dead = [f for f in bm.faces if slot_key[f.material_index] is None]
    if dead:
        bmesh.ops.delete(bm, geom=dead, context='FACES')
    bm.to_mesh(me)
    bm.free()
    # decimate
    n = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris and n > tris:
        md = o.modifiers.new("dec", 'DECIMATE')
        md.ratio = tris / n
        md.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get()
        nm = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        o.modifiers.clear()
        o.data = nm
        me = nm
    # UV remap per face (faces of a material go into its cell; UVs outside 0..1 are wrapped per face)
    ly = me.uv_layers[0]
    new_mats = []
    for k in slot_key:
        if k == "GLASS":
            mm = lib.mat("M_Glass")
        elif k == "SHADE":
            mm = lib.mat("M_Shade")
        elif k is None:
            mm = lib.mat("M_Glass")
        else:
            mm = lib.mat(CELLS[k]["atlas"])
        if mm.name not in [x.name for x in new_mats]:
            new_mats.append(mm)
    idx_map = {}
    for i, k in enumerate(slot_key):
        nmn = "M_Glass" if k in ("GLASS", None) else "M_Shade" if k == "SHADE" else CELLS[k]["atlas"]
        idx_map[i] = [x.name for x in new_mats].index(nmn)
    for p in me.polygons:
        k = slot_key[p.material_index] if p.material_index < len(slot_key) else asset
        if k not in ("GLASS", "SHADE", None):
            cu0, cv0, cu1, cv1 = CELLS[k]["uv"]
            ls = list(p.loop_indices)
            mu = sum(ly.data[i].uv.x for i in ls) / len(ls)
            mv = sum(ly.data[i].uv.y for i in ls) / len(ls)
            fu, fv = math.floor(mu), math.floor(mv)
            for i in ls:
                u = min(max(ly.data[i].uv.x - fu, 0.0), 1.0)
                v = min(max(ly.data[i].uv.y - fv, 0.0), 1.0)
                ly.data[i].uv = (cu0 + u * (cu1 - cu0), cv0 + v * (cv1 - cv0))
        p.material_index = idx_map.get(p.material_index, 0)
    for i, m in enumerate(new_mats):
        if i < len(me.materials):
            me.materials[i] = m
        else:
            me.materials.append(m)
    while len(me.materials) > len(new_mats):
        me.materials.pop()
    return o


def bounds(o):
    ws = [v.co for v in o.data.vertices]
    lo = Vector([min(w[i] for w in ws) for i in range(3)])
    hi = Vector([max(w[i] for w in ws) for i in range(3)])
    return lo, hi


def centre_floor(o):
    lo, hi = bounds(o)
    o.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
    return o


def centre_wall(o, z_centre=True):
    """Back (max y) on y = 0, centred in x, and in z (z_centre) or bottom at 0."""
    lo, hi = bounds(o)
    o.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -hi.y, -(lo.z + hi.z) / 2 if z_centre else -lo.z)))
    return o


def rot_mesh(o, deg_z=0.0, deg_x=0.0, deg_y=0.0):
    o.data.transform(Matrix.Rotation(math.radians(deg_z), 4, 'Z') @ Matrix.Rotation(math.radians(deg_y), 4, 'Y') @
                     Matrix.Rotation(math.radians(deg_x), 4, 'X'))
    return o


def emitter(model, root, loc, emissive_parts):
    """Fixture convention: child mesh <model>_Emissive (M_Shade) and an empty <model>_Emitter at the light."""
    em = None
    if emissive_parts:
        em = join(emissive_parts, model + "_Emissive")
        lib.set_mat(em, "M_Shade")
        lib.child(em, root)
    e = lib.empty(model + "_Emitter", loc, root)
    return em, e


# ------------------------------------------------------------------ shared self-built parts
def prof_scale(prof, s):
    return [(r * s, z * s) for r, z in prof]


def lamp_shade(name, r_bot, r_top, h, z0, seg=20):
    """Open conical drum shade (double-sided via thickness)."""
    prof = [(r_bot, 0), (r_top, h), (r_top - 0.004, h), (r_bot - 0.004, 0.0005)]
    return lathe(name, prof, "M_Shade", seg, closed=True, loc=(0, 0, z0))


def tulip(name, r, h, loc, rot=(0, 0, 0), seg=10):
    prof = [(0.012, 0), (r * 0.55, h * 0.25), (r * 0.9, h * 0.6), (r, h * 0.9), (r * 1.05, h),
            (r * 1.0, h), (r * 0.86, h * 0.6), (r * 0.5, h * 0.25), (0.008, 0.004)]
    o = lathe(name, prof, "M_Shade", seg, closed=True)
    return xf(o, loc, rot)


def bulb(name, loc, r=0.03, seg=8):
    prof = [(0.0, -r * 2.2), (r * 0.5, -r * 2.1), (r * 0.95, -r * 1.5), (r, -r * 0.9), (r * 0.55, -r * 0.2),
            (r * 0.45, 0), (0, 0)]
    o = lathe(name, prof, "M_Shade", seg)
    return xf(o, loc)


# ================================================================== FIXTURES (SPEC 4.2 / 5.1)
def FX_CeilingFan(name="FX_CeilingFan"):
    """L1 (ref 2): 4-blade ceiling fan, 3 glass tulip light kit, pull chain. Pivot = ceiling."""
    p = []
    p.append(F(lathe("Canopy", [(0, 0), (0.075, 0), (0.07, -0.04), (0.02, -0.07), (0, -0.07)], "M_Atlas_Small", 16),
               "small_brass"))
    p.append(F(cyl("Rod", 0.011, 0.22, "M_Atlas_Small", loc=(0, 0, -0.29), seg=8), "small_brass"))
    p.append(F(lathe("Motor", [(0, 0), (0.06, 0), (0.12, -0.03), (0.135, -0.08), (0.12, -0.13), (0.07, -0.15),
                               (0, -0.15)], "M_Atlas_Small", 20, loc=(0, 0, -0.29)), "small_brass"))
    for i in range(4):
        a = 90 * i + 20
        iron = box_z("Iron", (0.1, 0.28), (-0.018, 0.018), (-0.405, -0.395), "M_Atlas_Small", bev=0.003)
        iron = F(xf(iron, rot=(0, 0, a)), "small_iron")
        blade = box_z("Blade", (0.25, 0.73), (-0.07, 0.07), (-0.407, -0.397), "M_Atlas_Wood", bev=0.002)
        for v in blade.data.vertices:            # taper and a slight droop
            t = (v.co.x - 0.25) / 0.48
            v.co.y *= 1.0 + 0.25 * t
            v.co.z -= 0.025 * t * t
        blade = F(xf(blade, rot=(4, 0, a)), "wood_varnish")
        p += [iron, blade]
    p.append(F(lathe("KitBody", [(0, 0), (0.08, 0), (0.085, -0.04), (0.06, -0.09), (0.02, -0.11), (0, -0.11)],
                     "M_Atlas_Small", 16, loc=(0, 0, -0.44)), "small_brass"))
    glass_parts = []
    for i in range(3):
        a = math.radians(120 * i + 60)
        arm = tube("KitArm", [(0.05 * math.cos(a), 0.05 * math.sin(a), -0.49),
                              (0.12 * math.cos(a), 0.12 * math.sin(a), -0.5),
                              (0.15 * math.cos(a), 0.15 * math.sin(a), -0.47)], 0.007, "M_Atlas_Small", res=4,
                   bevres=1)
        p.append(F(arm, "small_brass"))
        c = (0.155 * math.cos(a), 0.155 * math.sin(a), -0.47)
        t = tulip("Tulip", 0.055, 0.1, c, rot=(0, 0, 0))
        # tulips open downwards and tilt out (ref 2)
        t = xf(t, (-c[0], -c[1], -c[2]))
        t.data.transform(Matrix.Rotation(math.radians(180 - 30), 4, Vector((-math.sin(a), math.cos(a), 0))))
        t = xf(t, c)
        glass_parts.append(t)
        glass_parts.append(bulb("Bulb", (c[0] * 1.15, c[1] * 1.15, c[2] - 0.05), 0.022))
    chain = []
    for k in range(6):
        chain.append(F(torus("Link", 0.006, 0.0018, "M_Atlas_Small", loc=(0, 0, -0.56 - k * 0.02),
                             rot=(90, 0, 90 * (k % 2)), seg=4, rseg=3), "small_brass"))
    p += chain
    p.append(F(lathe("Pull", [(0, 0), (0.008, -0.004), (0.008, -0.02), (0, -0.026)], "M_Atlas_Small", 8,
                     loc=(0, 0, -0.68)), "small_brass"))
    root = join(p, name)
    emitter(name, root, (0, 0, -0.45), glass_parts)
    return root


def FX_Chandelier3(name="FX_Chandelier3"):
    """H1 (ref 1): 3-arm chandelier, chain drop, fabric drum shades. Pivot = ceiling; shades at z -0.55."""
    p = [F(lathe("Canopy", [(0, 0), (0.07, 0), (0.065, -0.035), (0.015, -0.05), (0, -0.05)], "M_Atlas_Small", 14),
           "small_brass")]
    for k in range(7):
        p.append(F(torus("Link", 0.016, 0.003, "M_Atlas_Small", loc=(0, 0, -0.07 - k * 0.033),
                         rot=(90, 0, 90 * (k % 2)), seg=6, rseg=3), "small_brass"))
    p.append(F(lathe("Body", [(0, 0), (0.02, 0), (0.035, -0.06), (0.05, -0.16), (0.03, -0.22), (0.045, -0.26),
                              (0.02, -0.3), (0, -0.33)], "M_Atlas_Small", 14, loc=(0, 0, -0.3)), "small_brass"))
    shades = []
    for i in range(3):
        a = math.radians(120 * i + 90)
        ca, sa = math.cos(a), math.sin(a)
        arm = tube("Arm", [(0.03 * ca, 0.03 * sa, -0.5), (0.16 * ca, 0.16 * sa, -0.58), (0.28 * ca, 0.28 * sa, -0.57),
                           (0.3 * ca, 0.3 * sa, -0.5)], 0.008, "M_Atlas_Small", res=5, bevres=1)
        p.append(F(arm, "small_brass"))
        p.append(F(lathe("Cup", [(0, 0), (0.025, 0.0), (0.03, 0.03), (0.012, 0.035), (0, 0.035)], "M_Atlas_Small",
                         10, loc=(0.3 * ca, 0.3 * sa, -0.5)), "small_brass"))
        shades.append(lamp_shade("Drum", 0.085, 0.08, 0.13, 0, 12))
        shades[-1] = xf(shades[-1], (0.3 * ca, 0.3 * sa, -0.52))
        shades.append(bulb("Bulb", (0.3 * ca, 0.3 * sa, -0.42), 0.02))
    root = join(p, name)
    emitter(name, root, (0, 0, -0.55), shades)
    return root


def _sconce_plate(p, key):
    p.append(F(lathe("Plate", [(0, 0), (0.05, 0), (0.055, 0.012), (0.035, 0.022), (0, 0.022)], "M_Atlas_Small", 14,
                     rot=(90, 0, 0)), key))


def FX_SconceBrass(name="FX_SconceBrass", broken=False):
    """H2/H3/H5/H6/W1 (ref 1): small brass sconce with a shaded candle arm. Pivot = wall (back plate centre).
    Light at (0, -0.10 front, +0.08 up)."""
    p = []
    _sconce_plate(p, "small_brass")
    arm = tube("Arm", [(0, -0.02, 0), (0, -0.07, -0.03), (0, -0.10, 0.0), (0, -0.10, 0.03)], 0.007, "M_Atlas_Small",
               res=5, bevres=1)
    p.append(F(arm, "small_brass"))
    p.append(F(lathe("Cup", [(0, 0), (0.025, 0), (0.03, 0.02), (0.012, 0.025), (0, 0.025)], "M_Atlas_Small", 10,
                     loc=(0, -0.10, 0.03)), "small_brass"))
    em = [lamp_shade("Shade", 0.07, 0.045, 0.1, 0, 12)]
    if broken:
        em = [xf(em[0], (0, 0, 0), (35, 10, 0))]
        em[0] = xf(em[0], (0.02, -0.12, 0.0))
    else:
        em[0] = xf(em[0], (0, -0.10, 0.045))
        em.append(bulb("Bulb", (0, -0.10, 0.12), 0.018))
    root = join(p, name)
    emitter(name, root, (0, -0.10, 0.08), em)
    return root


def FX_SconceBroken(name="FX_SconceBroken"):
    return FX_SconceBrass(name, broken=True)


def FX_SconceTulip(name="FX_SconceTulip"):
    """L3/L4 (ref 2): dark bronze plate, curved arm, upturned frosted tulip. Light at (0, -0.12, +0.10)."""
    p = []
    _sconce_plate(p, "small_iron")
    arm = tube("Arm", [(0, -0.02, -0.04), (0, -0.09, -0.05), (0, -0.12, 0.0), (0, -0.12, 0.05)], 0.008,
               "M_Atlas_Small", res=5, bevres=1)
    p.append(F(arm, "small_iron"))
    em = [tulip("Tulip", 0.06, 0.12, (0, -0.12, 0.05)), bulb("Bulb", (0, -0.12, 0.14), 0.018)]
    root = join(p, name)
    emitter(name, root, (0, -0.12, 0.11), em)
    return root


def FX_TableLamp(name="FX_TableLamp", s=1.0):
    """L2/H4/R1 (ref 2): turned brass and ceramic base, white conical shade. Pivot = base centre (top).
    Light at the bulb, 0.27 above the base (s scales the lamp)."""
    p = []
    base = [(0, 0), (0.075, 0), (0.08, 0.012), (0.06, 0.025), (0.03, 0.04), (0.06, 0.09), (0.075, 0.14),
            (0.06, 0.2), (0.02, 0.23), (0.012, 0.25), (0, 0.25)]
    p.append(F(lathe("Base", prof_scale(base, s), "M_Atlas_Small", 12), "ceramic"))
    p.append(F(cyl("Stem", 0.006 * s, 0.07 * s, "M_Atlas_Small", loc=(0, 0, 0.25 * s), seg=8), "small_brass"))
    harp = tube("Harp", [(-0.07 * s, 0, 0.27 * s), (-0.07 * s, 0, 0.4 * s), (0, 0, 0.44 * s), (0.07 * s, 0, 0.4 * s),
                         (0.07 * s, 0, 0.27 * s)], 0.003, "M_Atlas_Small", res=4, bevres=1)
    p.append(F(harp, "small_brass"))
    p.append(F(lathe("Finial", [(0, 0), (0.008, 0.005), (0.006, 0.02), (0, 0.025)], "M_Atlas_Small", 8,
                     loc=(0, 0, 0.44 * s)), "small_brass"))
    em = [lamp_shade("Shade", 0.17 * s, 0.1 * s, 0.2 * s, 0.24 * s, 16), bulb("Bulb", (0, 0, 0.32 * s), 0.025 * s)]
    root = join(p, name)
    emitter(name, root, (0, 0, 0.27 * s), em)
    return root


def FX_TableLampSmall(name="FX_TableLampSmall"):
    """R2: smaller bedside version (s = 0.85, light 0.23 above the base)."""
    return FX_TableLamp(name, 0.85)


def FX_Dome(name="FX_Dome"):
    """E1/K1/K2: flush glass dome, brass rim and finial. Pivot = ceiling; light at z -0.08."""
    p = [F(lathe("Rim", [(0, 0), (0.19, 0), (0.19, -0.015), (0.175, -0.022), (0, -0.022)], "M_Atlas_Small", 16),
           "small_brass"),
         F(lathe("Finial", [(0, 0), (0.012, 0), (0.015, -0.015), (0, -0.03)], "M_Atlas_Small", 8,
                 loc=(0, 0, -0.125)), "small_brass")]
    prof = [(0.17, -0.02), (0.165, -0.06), (0.13, -0.1), (0.07, -0.122), (0, -0.128)]
    dome = lathe("Dome", prof + [(0, -0.124), (0.068, -0.118), (0.127, -0.097), (0.161, -0.058), (0.166, -0.02)],
                 "M_Shade", 16, closed=True)
    root = join(p, name)
    emitter(name, root, (0, 0, -0.08), [dome])
    return root


def FX_BulbPendant(name="FX_BulbPendant"):
    """C1/C2 (basement): ceiling rose, 0.36 m cord, bakelite socket, bare bulb. Pivot = slab underside; light z -0.40."""
    p = [F(cyl("Rose", 0.04, 0.025, "M_Atlas_Small", loc=(0, 0, -0.025), seg=12), "small_black"),
         F(cyl("Cord", 0.0035, 0.33, "M_Atlas_Small", loc=(0, 0, -0.355), seg=6), "small_black"),
         F(lathe("Socket", [(0, 0), (0.016, 0), (0.019, -0.01), (0.02, -0.045), (0.015, -0.05), (0, -0.05)],
                 "M_Atlas_Small", 12, loc=(0, 0, -0.335)), "small_black")]
    root = join(p, name)
    emitter(name, root, (0, 0, -0.42), [bulb("Bulb", (0, 0, -0.385), 0.03)])
    return root


# ================================================================== RUGS (9-slice with tiled field)
def rug(name, L, W, field, border, corner, bw, tile, bend=0.0):
    """Flat rug L (x) by W (y), 8 mm thick: corners / border strips / a tiled field, each quad mapped onto its cell
    (alternate field tiles mirrored, so the repeat has no seams). bend curls the corners up a little."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    th = 0.008

    def quad(x0, x1, y0, y1, key, uvq):
        c = CELLS[key]["uv"]
        vs = [bm.verts.new((x, y, th)) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
        f = bm.faces.new(vs)
        for lp, (u, v) in zip(f.loops, uvq):
            lp[uvl].uv = (c[0] + u * (c[2] - c[0]), c[1] + v * (c[3] - c[1]))
    hx, hy = L / 2, W / 2
    fx0, fx1, fy0, fy1 = -hx + bw, hx - bw, -hy + bw, hy - bw
    nx = max(1, round((fx1 - fx0) / tile))
    ny = max(1, round((fy1 - fy0) / tile))
    for i in range(nx):
        for j in range(ny):
            x0 = fx0 + (fx1 - fx0) * i / nx
            x1 = fx0 + (fx1 - fx0) * (i + 1) / nx
            y0 = fy0 + (fy1 - fy0) * j / ny
            y1 = fy0 + (fy1 - fy0) * (j + 1) / ny
            a, b = (0, 1) if i % 2 == 0 else (1, 0)
            c, d = (0, 1) if j % 2 == 0 else (1, 0)
            quad(x0, x1, y0, y1, field, [(a, c), (b, c), (b, d), (a, d)])
    # border strips: U along the edge (repeats), V = 0 at the outer edge
    nbx = max(1, round((fx1 - fx0) / (tile)))
    nby = max(1, round((fy1 - fy0) / (tile)))
    for i in range(nbx):
        x0 = fx0 + (fx1 - fx0) * i / nbx
        x1 = fx0 + (fx1 - fx0) * (i + 1) / nbx
        a, b = (0, 1) if i % 2 == 0 else (1, 0)
        quad(x0, x1, -hy, fy0, border, [(a, 0), (b, 0), (b, 1), (a, 1)])
        quad(x0, x1, fy1, hy, border, [(a, 1), (b, 1), (b, 0), (a, 0)])
    for j in range(nby):
        y0 = fy0 + (fy1 - fy0) * j / nby
        y1 = fy0 + (fy1 - fy0) * (j + 1) / nby
        a, b = (0, 1) if j % 2 == 0 else (1, 0)
        quad(-hx, fx0, y0, y1, border, [(0, a), (1, a), (1, b), (0, b)])
        quad(fx1, hx, y0, y1, border, [(1, a), (0, a), (0, b), (1, b)])
    for (x0, x1, y0, y1) in ((-hx, fx0, -hy, fy0), (fx1, hx, -hy, fy0), (fx1, hx, fy1, hy), (-hx, fx0, fy1, hy)):
        quad(x0, x1, y0, y1, corner, [(0, 0), (1, 0), (1, 1), (0, 1)])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # thickness: extrude down to the floor, so the edge reads
    edges = [e for e in bm.edges if e.is_boundary]
    res = bmesh.ops.extrude_edge_only(bm, edges=edges)
    for v in [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co.z = 0.0
    for v in bm.verts:
        if bend and v.co.z > 0:
            dx = max(0, abs(v.co.x) - (hx - 0.25))
            dy = max(0, abs(v.co.y) - (hy - 0.25))
            v.co.z += bend * (dx * dy) * 16
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(lib.mat(CELLS[field]["atlas"]))
    return o


def Rug_Oriental():
    return rug("Rug_Oriental", 2.7, 1.85, "rug_or_field", "rug_or_border", "rug_or_corner", 0.22, 0.42, 0.05)


def Rug_OrientalSmall():
    return rug("Rug_OrientalSmall", 1.6, 1.0, "rug_or_field", "rug_or_border", "rug_or_corner", 0.16, 0.34)


def Rug_Geo():
    return rug("Rug_Geo", 2.0, 1.4, "rug_geo_field", "rug_geo_border", "rug_geo_corner", 0.18, 0.5, 0.04)


def Runner_Hall():
    return rug("Runner_Hall", 5.2, 0.8, "runner_field", "runner_border", "rug_or_corner", 0.09, 0.4)


def Runner_Entrance():
    return rug("Runner_Entrance", 2.6, 0.8, "runner_field", "runner_border", "rug_or_corner", 0.09, 0.4, 0.03)


# ================================================================== SOFT furnishings
def Settee_Plaid():
    """Ref 3 plaid settee, 1.6 x 0.78, low wooden legs (Soft atlas)."""
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(F(lathe("Leg", [(0, 0), (0.022, 0), (0.026, 0.06), (0.03, 0.1), (0, 0.1)], "M_Atlas_Soft", 8,
                             loc=(sx * 0.72, sy * 0.3, 0)), "soft_wood"))
    p.append(F(rounded_box("Base", (1.6, 0.76, 0.22), 0.03, "M_Atlas_Soft", (0, 0, 0.21), cuts=3), "plaid"))
    for sx in (-0.37, 0.37):
        p.append(F(rounded_box("Seat", (0.72, 0.6, 0.14), 0.05, "M_Atlas_Soft", (sx, -0.06, 0.38), cuts=3,
                               bulge=0.02, jit=0.004), "plaid"))
    back = rounded_box("Back", (1.6, 0.18, 0.5), 0.06, "M_Atlas_Soft", cuts=3, bulge=0.015, jit=0.003)
    p.append(F(xf(back, (0, 0.3, 0.55), (-8, 0, 0)), "plaid"))
    for sx in (-1, 1):
        p.append(F(rounded_box("Arm", (0.16, 0.76, 0.36), 0.06, "M_Atlas_Soft", (sx * 0.74, 0, 0.45), cuts=3),
                   "plaid", rot=True))
    for sx, a in ((-0.45, 12), (0.5, -18)):
        cush = rounded_box("Cushion", (0.38, 0.12, 0.36), 0.05, "M_Atlas_Soft", cuts=3, bulge=0.03, jit=0.004)
        p.append(F(xf(cush, (sx, 0.17, 0.57), (-20, 0, a)), "blanket"))
    return join(p, "Settee_Plaid")


def Curtains_Window():
    """A pair of tattered drapes on a rod for a 1.6 m window (head 2.40). Pivot = wall, rod centre at z 0."""
    p = []
    p.append(F(cyl("Rod", 0.012, 2.1, "M_Atlas_Soft", loc=(-1.05, -0.09, 0), rot=(0, 90, 0), seg=8), "soft_wood"))
    for sx in (-1, 1):
        p.append(F(lathe("Ball", [(0, 0), (0.02, 0.01), (0.022, 0.03), (0, 0.045)], "M_Atlas_Soft", 8,
                         loc=(sx * 1.07, -0.09, 0), rot=(0, sx * 90, 0)), "soft_wood"))
        x0, x1 = (sx * 1.02, sx * 0.62) if sx < 0 else (sx * 0.62, sx * 1.02)
        d = assets.drape("Drape", (x0, -0.09), (x1, -0.09), -0.02, 1.55, "M_Atlas_Soft", pleats=6, amp=0.035,
                      tatter=0.18, n=18)
        p.append(F(d, "curtain", rot=False))
    return join(p, "Curtains_Window")


def Towel_Rail():
    """Wall towel rail with a hanging towel (bathroom). Pivot = wall, rail at z 0."""
    p = []
    for sx in (-1, 1):
        p.append(F(cyl("Bracket", 0.012, 0.08, "M_Atlas_Soft", loc=(sx * 0.3, 0, 0), rot=(90, 0, 0), seg=8),
                   "soft_wood"))
    p.append(F(cyl("Rail", 0.012, 0.62, "M_Atlas_Soft", loc=(-0.31, -0.08, 0), rot=(0, 90, 0), seg=8), "soft_wood"))
    t = grid("Towel", 0.45, 0.9, 6, 12, "M_Atlas_Soft")
    for v in t.data.vertices:
        y = v.co.y                                     # -0.45..0.45 along the fold
        ang = max(-1.0, min(1.0, y / 0.06))
        v.co = Vector((v.co.x + 0.02, -0.08 + (0.015 * math.cos(ang * 1.5) if abs(y) < 0.06 else
                                                (0.013 if y > 0 else -0.013)), -abs(y) * 0.95 + 0.012))
        v.co.y += 0.006 * math.sin(v.co.x * 20)
    t = solidify(t, 0.008, 0)
    p.append(F(t, "towel"))
    return join(p, "Towel_Rail")


def Bed_Blanket():
    """A crumpled blanket thrown over the bed foot (placed on GothicBed_01). Pivot = floor under its centre."""
    g = grid("Blanket", 1.5, 1.2, 14, 12, "M_Atlas_Soft")
    for v in g.data.vertices:
        x, y = v.co.x, v.co.y
        z = 0.0
        if abs(x) > 0.6:                               # drapes down the sides
            z = -(abs(x) - 0.6) * 1.0
            x = math.copysign(0.62 + (abs(x) - 0.6) * 0.2, x)
        if y < -0.5:
            z += -(-0.5 - y) * 1.1
            y = -0.52 - (-0.5 - y) * 0.15
        z += 0.03 * math.sin(x * 9) * math.cos(y * 7) + R.uniform(-0.008, 0.008)
        v.co = Vector((x, y, z))
    g = solidify(g, 0.012, 0)
    return join([F(g, "blanket")], "Bed_Blanket")


# ================================================================== WOOD furniture (self-built)
def Wardrobe_Painted():
    """Bedroom wardrobe 1.5 x 0.58 x 2.0, dark painted (ref 3 trim colour), two panelled doors, one ajar."""
    W = "M_Atlas_Wood"
    p = [F(box_z("Carcass", (-0.75, 0.75), (-0.26, 0.29), (0.1, 1.92), W, bev=0.01), "wood_paint_dark"),
         F(box_z("Plinth", (-0.77, 0.77), (-0.28, 0.29), (0.0, 0.1), W, bev=0.008), "wood_paint_dark"),
         F(box_z("Cornice", (-0.8, 0.8), (-0.32, 0.29), (1.92, 2.0), W, bev=0.012), "wood_paint_dark")]
    for sx, ajar in ((-1, 0.0), (1, 14.0)):
        # leaf built from its hinge along -sx (left door hinged at x -0.74 spans +x, right door spans -x)
        xa, xb = (0.0, 0.73) if sx < 0 else (-0.73, 0.0)
        door = F(box_z("Door", (xa, xb), (-0.022, 0.0), (0.12, 1.9), W, bev=0.006), "wood_paint_dark", rot=True)
        pan = [F(box_z("Panel", (xa + 0.08, xb - 0.08), (-0.03, -0.02), z, W, bev=0.01), "wood_paint_dark", rot=True)
               for z in ((0.22, 0.95), (1.05, 1.8))]
        kx = 0.66 if sx < 0 else -0.66
        kn = F(lathe("Knob", [(0, 0), (0.012, 0), (0.014, -0.02), (0, -0.026)], W, 8, loc=(kx, -0.022, 1.0),
                     rot=(90, 0, 0)), "wood_brass")
        d = join([door] + pan + [kn], "Leaf")
        d = xf(d, rot=(0, 0, ajar if sx > 0 else -ajar))
        p.append(xf(d, (sx * 0.74, -0.265, 0)))
    return join(p, "Wardrobe_Painted")


def CoffeeTable_Glass():
    """Ref 2: dark wood frame with a glass top and a lower shelf, 1.05 x 0.55 x 0.42."""
    W = "M_Atlas_Wood"
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(F(box_z("Leg", (sx * 0.5 - 0.025, sx * 0.5 + 0.025), (sy * 0.25 - 0.025, sy * 0.25 + 0.025),
                             (0, 0.4), W, bev=0.004), "wood_varnish", rot=True))
    for sy in (-1, 1):
        p.append(F(box_z("Rail", (-0.5, 0.5), (sy * 0.265 - 0.012, sy * 0.265 + 0.012), (0.36, 0.4), W, bev=0.003),
                   "wood_varnish"))
    for sx in (-1, 1):
        p.append(F(box_z("Rail", (sx * 0.515 - 0.012, sx * 0.515 + 0.012), (-0.27, 0.27), (0.36, 0.4), W, bev=0.003),
                   "wood_varnish", rot=True))
    p.append(F(box_z("Shelf", (-0.48, 0.48), (-0.23, 0.23), (0.1, 0.12), W, bev=0.003), "wood_varnish2"))
    p.append(glass(box_z("Glass", (-0.51, 0.51), (-0.26, 0.26), (0.4, 0.408), "M_Glass", bev=0.0)))
    return join(p, "CoffeeTable_Glass")


def Console_Hall():
    """Unused fallback console (ClassicConsole_01 is the hall console)."""
    return ph("ClassicConsole_01", 2200)


def CoatStand():
    W = "M_Atlas_Wood"
    p = [F(lathe("Pole", [(0, 0), (0.03, 0), (0.025, 0.1), (0.018, 0.12), (0.018, 1.65), (0.026, 1.7),
                          (0.02, 1.74), (0.03, 1.76), (0, 1.8)], W, 10), "wood_varnish", rot=True)]
    for i in range(3):
        a = 120 * i
        leg = box_z("Foot", (0.0, 0.32), (-0.02, 0.02), (0.0, 0.05), W, bev=0.008)
        for v in leg.data.vertices:
            v.co.z += 0.12 * max(0.0, 0.2 - v.co.x)
        p.append(F(xf(leg, rot=(0, 0, a)), "wood_varnish"))
    for i in range(4):
        a = math.radians(90 * i + 45)
        hk = tube("Hook", [(0.02 * math.cos(a), 0.02 * math.sin(a), 1.62), (0.11 * math.cos(a), 0.11 * math.sin(a), 1.66),
                           (0.13 * math.cos(a), 0.13 * math.sin(a), 1.72)], 0.008, W, res=4, bevres=1)
        p.append(F(hk, "wood_brass"))
    # a coat and a hat left behind
    coat = rounded_box("Coat", (0.42, 0.2, 0.95), 0.08, W, (0.12, 0.02, 1.15), cuts=4, bulge=0.03, jit=0.01)
    for v in coat.data.vertices:
        t = (1.62 - v.co.z) / 0.95
        v.co.x = 0.12 + (v.co.x - 0.12) * (0.45 + 0.6 * t)
    p.append(F(coat, "wood_paint_dark", rot=True))
    p.append(F(lathe("Hat", [(0, 0), (0.15, 0), (0.15, 0.008), (0.09, 0.012), (0.085, 0.11), (0, 0.12)], W, 14,
                     loc=(-0.11, 0.0, 1.69), rot=(10, -15, 0)), "wood_paint_dark"))
    return join(p, "CoatStand")


def Workbench():
    """Basement workbench 2.0 x 0.62 x 0.9: raw boards, iron legs, lower shelf, a few tools."""
    U = "M_Atlas_Utility"
    p = []
    for k in range(4):
        p.append(F(box_z("Plank", (-1.0, 1.0), (-0.31 + k * 0.155, -0.31 + (k + 1) * 0.155 - 0.006), (0.86, 0.9), U,
                         bev=0.004, jit=0.002), "util_raw"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(F(box_z("Leg", (sx * 0.93 - 0.035, sx * 0.93 + 0.035), (sy * 0.25 - 0.035, sy * 0.25 + 0.035),
                             (0, 0.86), U, bev=0.004), "util_raw", rot=True))
    p.append(F(box_z("Shelf", (-0.9, 0.9), (-0.24, 0.24), (0.18, 0.21), U, bev=0.003), "util_raw"))
    for sy in (-1, 1):
        p.append(F(box_z("Apron", (-0.9, 0.9), (sy * 0.27 - 0.012, sy * 0.27 + 0.012), (0.72, 0.86), U, bev=0.003),
                   "util_raw"))
    p.append(F(box_z("Toolbox", (0.3, 0.75), (-0.05, 0.17), (0.21, 0.42), U, bev=0.01), "util_metal_paint"))
    p.append(F(box_z("Can", (-0.75, -0.6), (-0.1, 0.05), (0.9, 1.08), U, bev=0.01), "util_metal_paint", rot=True))
    p.append(F(cyl("Paint", 0.09, 0.17, U, loc=(-0.4, 0.1, 0.9), seg=12), "util_metal_paint", rot=True))
    hammer = join([F(box_z("Handle", (-0.15, 0.15), (-0.012, 0.012), (0.9, 0.92), U, bev=0.004), "util_raw"),
                   F(box_z("Head", (0.13, 0.16), (-0.05, 0.05), (0.9, 0.93), U, bev=0.004), "steel")], "Hammer")
    p.append(xf(hammer, rot=(0, 0, 25)))
    return join(p, "Workbench")


def KitchenTable():
    """Painted kitchen table 1.6 x 0.9 x 0.76 (painted_wooden_chair_01 style)."""
    U = "M_Atlas_Utility"
    p = [F(box_z("Top", (-0.8, 0.8), (-0.45, 0.45), (0.73, 0.76), U, bev=0.006), "util_paint_light")]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(F(lathe("Leg", [(0, 0), (0.025, 0), (0.028, 0.2), (0.022, 0.4), (0.03, 0.55), (0.032, 0.66),
                                     (0.032, 0.73), (0, 0.73)], U, 8, loc=(sx * 0.72, sy * 0.38, 0)),
                       "util_paint_light", rot=True))
    for sy in (-1, 1):
        p.append(F(box_z("Apron", (-0.7, 0.7), (sy * 0.38 - 0.012, sy * 0.38 + 0.012), (0.62, 0.73), U), "util_paint_light"))
    for sx in (-1, 1):
        p.append(F(box_z("Apron", (sx * 0.72 - 0.012, sx * 0.72 + 0.012), (-0.36, 0.36), (0.62, 0.73), U),
                   "util_paint_light", rot=True))
    return join(p, "KitchenTable")


def SinkUnit():
    """Kitchen sink unit under WK1: painted cupboard 1.2 x 0.6 x 0.9 with a ceramic butler sink and a tap."""
    U = "M_Atlas_Utility"
    p = [F(box_z("Cupboard", (-0.6, 0.6), (-0.28, 0.3), (0.08, 0.86), U, bev=0.008), "util_paint_light"),
         F(box_z("Kick", (-0.58, 0.58), (-0.25, 0.3), (0.0, 0.08), U), "castiron"),
         F(box_z("Worktop", (-0.62, 0.62), (-0.31, 0.3), (0.86, 0.9), U, bev=0.006), "util_raw")]
    for sx in (-1, 1):
        p.append(F(box_z("DoorLine", (sx * 0.29 - 0.004, sx * 0.29 + 0.004), (-0.29, -0.28), (0.12, 0.82), U),
                   "castiron"))
        p.append(F(cyl("Knob", 0.012, 0.02, U, loc=(sx * 0.05, -0.28, 0.72), rot=(90, 0, 0), seg=8), "steel"))
    sink = box_z("Sink", (-0.32, 0.32), (-0.3, 0.12), (0.72, 0.95), U, bev=0.02)
    inner = box_z("SinkIn", (-0.28, 0.28), (-0.26, 0.08), (0.77, 0.951), U, bev=0.02)
    for v in inner.data.vertices:
        v.co.z = max(v.co.z, 0.77)
    inner.data.flip_normals()
    p += [F(sink, "enamel_white"), F(inner, "enamel_white")]
    tap = tube("Tap", [(0, 0.24, 0.9), (0, 0.24, 1.08), (0, 0.16, 1.12), (0, 0.06, 1.06)], 0.012, U, res=5,
               bevres=1)
    p.append(F(tap, "steel"))
    for sx in (-1, 1):
        p.append(F(cyl("TapHead", 0.02, 0.05, U, loc=(sx * 0.1, 0.24, 0.9), seg=8), "steel"))
    return join(p, "SinkUnit")


def Fridge_Retro():
    """Rounded 1950s fridge 0.72 x 0.68 x 1.55, cream enamel, chrome handle, rust at the foot."""
    U = "M_Atlas_Utility"
    body = rounded_box("Body", (0.72, 0.66, 1.45), 0.09, U, (0, 0.01, 0.82), cuts=5)
    p = [F(body, "enamel_cream", rot=True),
         F(box_z("Plinth", (-0.33, 0.33), (-0.28, 0.3), (0.0, 0.1), U, bev=0.01), "castiron"),
         F(box_z("DoorGap", (-0.34, 0.34), (-0.326, -0.32), (1.12, 1.125), U), "castiron"),
         F(tube("Handle", [(0.24, -0.33, 0.95), (0.27, -0.37, 0.98), (0.27, -0.37, 1.18), (0.24, -0.33, 1.21)], 0.014,
                U, res=4, bevres=1), "steel"),
         F(box_z("Badge", (-0.08, 0.08), (-0.335, -0.325), (1.4, 1.43), U), "steel")]
    return join(p, "Fridge_Retro")


def ClawfootTub():
    """Cast-iron roll-top tub 1.68 x 0.76 (outside dark painted, inside stained white enamel), 4 claw feet, taps."""
    U = "M_Atlas_Utility"
    rings_out, rings_in = [], []
    n = 28

    def ring(a, b, z, k=4.0):
        pts = []
        for i in range(n):
            t = 2 * math.pi * i / n
            c, s = math.cos(t), math.sin(t)
            pts.append((a * math.copysign(abs(c) ** (2 / k), c), b * math.copysign(abs(s) ** (2 / k), s), z))
        return pts
    prof_out = [(0.62, 0.26, 0.16), (0.74, 0.32, 0.3), (0.82, 0.37, 0.55), (0.84, 0.38, 0.62), (0.8, 0.34, 0.64)]
    prof_in = [(0.78, 0.31, 0.62), (0.72, 0.27, 0.5), (0.6, 0.2, 0.24), (0.45, 0.12, 0.2)]
    bm = bmesh.new()
    allr = [[bm.verts.new(p) for p in ring(a, b, z)] for a, b, z in prof_out + prof_in]
    for A, B in zip(allr, allr[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((A[i], A[j], B[j], B[i]))
    bot_o = bm.faces.new(list(reversed(allr[0])))
    bot_i = bm.faces.new(allr[-1])
    bmesh.ops.triangulate(bm, faces=[bot_o, bot_i])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Tub")
    bm.to_mesh(me)
    bm.free()
    shell = bpy.data.objects.new("Tub", me)
    bpy.context.scene.collection.objects.link(shell)
    # split inside / outside by face height and normal: inner faces point towards the centre
    me.uv_layers.new(name="UVMap")
    inner_faces = set()
    for f in me.polygons:
        c = f.center
        if Vector((c.x, c.y, 0)).dot(f.normal) < 0 and c.z > 0.19 or (f.normal.z > 0.5 and c.z < 0.3):
            inner_faces.add(f.index)
    # metric planar UVs (x along, z up / y across)
    for f in me.polygons:
        for li in f.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            me.uv_layers[0].data[li].uv = (v.x, v.z + v.y * 0.5)
    # duplicate into two objects
    outer = shell
    inner = bpy.data.objects.new("TubIn", me.copy())
    bpy.context.scene.collection.objects.link(inner)
    for obj, keep_inner in ((outer, False), (inner, True)):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        dead = [f for f in bm.faces if (f.index in inner_faces) != keep_inner]
        bmesh.ops.delete(bm, geom=dead, context='FACES')
        bm.to_mesh(obj.data)
        bm.free()
    p = [F(outer, "castiron"), F(inner, "enamel_white")]
    for sx in (-1, 1):
        for sy in (-1, 1):
            foot = lathe("Foot", [(0, 0), (0.045, 0), (0.05, 0.02), (0.03, 0.06), (0.035, 0.12), (0.05, 0.17),
                                  (0, 0.18)], U, 8, loc=(sx * 0.6, sy * 0.24, 0))
            p.append(F(foot, "castiron", rot=True))
    for sy in (-0.07, 0.07):
        p.append(F(tube("Tap", [(0.8, sy, 0.62), (0.8, sy, 0.76), (0.74, sy, 0.8), (0.7, sy, 0.76)], 0.011, U, res=4,
                        bevres=1), "steel"))
    return join(p, "ClawfootTub")


def Toilet_Old():
    """Toilet with a low cistern, 0.4 x 0.7 x 0.8; pivot = floor at the pan footprint, cistern at +y (the wall)."""
    U = "M_Atlas_Utility"
    ped = lathe("Pedestal", [(0, 0), (0.15, 0), (0.14, 0.03), (0.1, 0.15), (0.12, 0.3), (0, 0.3)], U, 16,
                loc=(0, -0.05, 0))
    ped = xf(ped, scale=(1, 1.35, 1))
    bowl = lathe("Bowl", [(0.1, 0.28), (0.17, 0.33), (0.19, 0.39), (0.19, 0.4), (0.16, 0.4), (0.12, 0.33),
                          (0.06, 0.26), (0, 0.25)], U, 18)
    bowl = xf(bowl, (0, -0.07, 0), scale=(1, 1.3, 1))
    seat = torus("Seat", 0.16, 0.022, U, loc=(0, -0.07, 0.42), seg=18, rseg=5, scale=(1, 1.3, 0.6))
    p = [F(ped, "enamel_white", rot=True), F(bowl, "enamel_white"), F(seat, "util_raw"),
         F(box_z("Cistern", (-0.22, 0.22), (0.12, 0.3), (0.45, 0.82), U, bev=0.02), "enamel_white"),
         F(box_z("Lid", (-0.23, 0.23), (0.11, 0.31), (0.82, 0.85), U, bev=0.01), "enamel_white"),
         F(cyl("Lever", 0.008, 0.08, U, loc=(-0.2, 0.13, 0.76), rot=(0, 90, 0), seg=6), "steel"),
         F(cyl("Pipe", 0.025, 0.12, U, loc=(0, 0.17, 0.35), seg=8), "enamel_white")]
    return join(p, "Toilet_Old")


def PedestalSink():
    U = "M_Atlas_Utility"
    p = [F(lathe("Pedestal", [(0, 0), (0.12, 0), (0.11, 0.03), (0.07, 0.1), (0.06, 0.6), (0.09, 0.7), (0, 0.7)], U, 14,
                 loc=(0, -0.08, 0)), "enamel_white", rot=True)]
    basin = rounded_box("Basin", (0.6, 0.45, 0.18), 0.05, U, (0, -0.2, 0.78), cuts=3)
    bowl = lathe("Bowl", [(0.2, 0.0), (0.18, -0.06), (0.12, -0.1), (0, -0.11)], U, 16, loc=(0, -0.22, 0.871))
    bowl = xf(bowl, scale=(1, 0.75, 1))
    bowl.data.flip_normals()
    p += [F(basin, "enamel_white"), F(bowl, "enamel_white")]
    for sx in (-1, 1):
        p.append(F(tube("Tap", [(sx * 0.12, -0.04, 0.87), (sx * 0.12, -0.04, 0.95), (sx * 0.1, -0.1, 0.96)], 0.01, U,
                        res=3, bevres=1), "steel"))
        p.append(F(cyl("Head", 0.022, 0.025, U, loc=(sx * 0.12, -0.04, 0.95), seg=8), "steel"))
    return join(p, "PedestalSink")


def MirrorCabinet():
    """Bathroom mirror cabinet 0.55 x 0.14 x 0.65, painted, a cracked mirror door. Pivot = wall, centre."""
    U = "M_Atlas_Utility"
    p = [F(box_z("Box", (-0.275, 0.275), (-0.14, 0.0), (-0.325, 0.325), U, bev=0.008), "util_paint_light"),
         F(box_z("Shelf", (-0.3, 0.3), (-0.17, 0.0), (-0.345, -0.325), U, bev=0.006), "util_paint_light")]
    p.append(glass(box_z("Mirror", (-0.235, 0.235), (-0.146, -0.14), (-0.28, 0.28), "M_Glass", bev=0.0)))
    p.append(F(box_z("Back", (-0.235, 0.235), (-0.142, -0.141), (-0.28, 0.28), U, bev=0.0), "castiron"))
    p.append(F(cyl("Knob", 0.01, 0.02, U, loc=(0.22, -0.145, 0.0), rot=(90, 0, 0), seg=8), "steel"))
    return join(p, "MirrorCabinet")


# ================================================================== SMALL items (self-built)
def _bottle(name, key, h, r, loc, tilt=(0, 0, 0)):
    prof = [(0, 0), (r, 0), (r, h * 0.62), (r * 0.9, h * 0.7), (r * 0.36, h * 0.82), (r * 0.32, h * 0.98),
            (r * 0.38, h), (0, h)]
    b = F(lathe(name, prof, "M_Atlas_Small", 10), key, rot=True)
    lab = F(lathe("Label", [(r * 1.01, h * 0.2), (r * 1.01, h * 0.48)], "M_Atlas_Small", 10), "label", "stretch",
            rot=True)
    return xf(join([b, lab], name), loc, tilt)


def _glass_cup(name, loc, h=0.1, r=0.035, key="ceramic"):
    prof = [(0, 0), (r * 0.8, 0), (r, h), (r * 0.92, h), (r * 0.72, 0.006), (0, 0.006)]
    return F(lathe(name, prof, "M_Atlas_Small", 10, closed=True, loc=loc), key, rot=True)


def _magazine(name, w, d, t, loc, rot, idx):
    m = box_z(name, (-w / 2, w / 2), (-d / 2, d / 2), (0, t), "M_Atlas_Small", bev=0.0)
    # cover on top: map the whole box to one quarter of the magazines cell
    c = CELLS["magazines"]["uv"]
    qx, qy = idx % 2, 1 - idx // 2
    u0 = c[0] + (c[2] - c[0]) * qx * 0.5
    v0 = c[1] + (c[3] - c[1]) * qy * 0.5
    du, dv = (c[2] - c[0]) * 0.5, (c[3] - c[1]) * 0.5
    ly = m.data.uv_layers[0]
    for poly in m.data.polygons:
        for li in poly.loop_indices:
            v = m.data.vertices[m.data.loops[li].vertex_index].co
            ly.data[li].uv = (u0 + (v.x / w + 0.5) * du * 0.96 + du * 0.02, v0 + (v.y / d + 0.5) * dv * 0.96 + dv * 0.02)
    m.data.materials.clear()
    m.data.materials.append(lib.mat("M_Atlas_Small"))
    return xf(m, loc, rot)


def Clutter_CoffeeTable():
    """Ref 2 coffee-table clutter: 4 bottles, 2 glasses, magazines (top) + magazines on the shelf. Pivot = table floor
    (the table top is at 0.408, the shelf at 0.12)."""
    top = 0.408
    p = [_bottle("Bottle", "glass_green", 0.3, 0.035, (-0.3, 0.05, top)),
         _bottle("Bottle", "glass_brown", 0.26, 0.032, (-0.18, -0.1, top)),
         _bottle("Bottle", "glass_green", 0.3, 0.035, (0.32, 0.12, top)),
         _bottle("Bottle", "glass_brown", 0.24, 0.03, (0.1, 0.0, top + 0.03), (90, 0, 30)),
         _glass_cup("Cup", (-0.05, 0.12, top), key="glass_green"), _glass_cup("Cup", (0.2, -0.13, top), 0.09),
         _magazine("Mag", 0.21, 0.28, 0.006, (-0.02, -0.08, top), (0, 0, 20), 0),
         _magazine("Mag", 0.21, 0.28, 0.006, (0.05, -0.05, top + 0.006), (0, 0, -15), 1),
         _magazine("Mag", 0.21, 0.28, 0.006, (-0.25, 0.0, 0.12), (0, 0, 80), 2),
         _magazine("Mag", 0.21, 0.28, 0.006, (0.2, 0.02, 0.12), (0, 0, 95), 3)]
    return join(p, "Clutter_CoffeeTable")


def Clutter_SideTable():
    """Ref 2 foreground side table: lace doily, a VCR, a stack of magazines. Pivot = the table top centre."""
    S = "M_Atlas_Small"
    d = F(cyl("Doily", 0.3, 0.003, S, seg=24), "doily", "stretch")
    # map the doily disc planar so the lace pattern is round
    ly = d.data.uv_layers[0]
    c = CELLS["doily"]["uv"]
    for poly in d.data.polygons:
        for li in poly.loop_indices:
            v = d.data.vertices[d.data.loops[li].vertex_index].co
            ly.data[li].uv = (c[0] + (v.x / 0.6 + 0.5) * (c[2] - c[0]), c[1] + (v.y / 0.6 + 0.5) * (c[3] - c[1]))
    vcr = box_z("VCR", (-0.2, 0.2), (-0.15, 0.15), (0.004, 0.09), S, bev=0.004)
    ly = vcr.data.uv_layers[0]
    c = CELLS["vcr"]["uv"]
    for poly in vcr.data.polygons:
        for li in poly.loop_indices:
            v = vcr.data.vertices[vcr.data.loops[li].vertex_index].co
            if abs(poly.normal.y) > 0.5:      # front / back: the lower half of the cell (the face)
                uv = ((v.x + 0.2) / 0.4, (v.z - 0.004) / 0.086 * 0.5)
            else:                             # top and sides: the silver lid half
                uv = ((v.x + 0.2) / 0.4, 0.55 + (v.y + 0.15) / 0.3 * 0.4)
            ly.data[li].uv = (c[0] + uv[0] * (c[2] - c[0]), c[1] + uv[1] * (c[3] - c[1]))
    vcr.data.materials.clear()
    vcr.data.materials.append(lib.mat(S))
    vcr = xf(vcr, (-0.05, 0.02, 0), (0, 0, 8))
    p = [d, vcr]
    for k in range(4):
        p.append(_magazine("Mag", 0.21, 0.28, 0.008, (0.0, 0.0, 0.094 + k * 0.008), (0, 0, R.uniform(-25, 25)), k))
    return join(p, "Clutter_SideTable")


def Speaker():
    """Ref 2 floor-standing speaker 0.32 x 0.28 x 0.62, veneer cabinet, speaker face cell on the front."""
    S = "M_Atlas_Small"
    cab = box_z("Cabinet", (-0.16, 0.16), (-0.14, 0.14), (0.0, 0.62), S, bev=0.006)
    face = box_z("Face", (-0.15, 0.15), (-0.145, -0.14), (0.02, 0.6), S, bev=0.0)
    return join([F(cab, "small_varnish", rot=True), F(face, "speaker", "stretch")], "Speaker")


def MantelClutter():
    """Ref 3 mantel ornaments around the clock: 2 brass candlesticks, a small framed photo. Pivot = mantel top."""
    S = "M_Atlas_Small"
    p = []
    for x in (-0.75, 0.78):
        p.append(F(lathe("Candlestick", [(0, 0), (0.045, 0), (0.04, 0.015), (0.012, 0.04), (0.018, 0.1),
                                         (0.01, 0.17), (0.03, 0.19), (0.012, 0.2), (0, 0.2)], S, 10, loc=(x, 0, 0)),
                   "small_brass", rot=True))
        p.append(F(cyl("Candle", 0.011, 0.09, S, loc=(x, 0, 0.2), seg=8), "ceramic", rot=True))
    fr = box_z("Photo", (-0.06, 0.06), (-0.01, 0.0), (0.0, 0.15), S, bev=0.003)
    p.append(F(xf(fr, (0.45, 0.04, 0), (-12, 0, -10)), "small_brass"))
    return join(p, "MantelClutter")


def UmbrellaStand():
    S = "M_Atlas_Small"
    p = [F(lathe("Stand", [(0, 0), (0.12, 0), (0.12, 0.02), (0.1, 0.06), (0.11, 0.5), (0.12, 0.52), (0.1, 0.52),
                           (0.09, 0.06), (0, 0.06)], S, 14, closed=False), "small_brass", rot=True)]
    for k, (a, b) in enumerate(((8, 0), (-6, 10))):
        um = lathe("Umbrella", [(0.003, 0.0), (0.035, 0.3), (0.045, 0.55), (0.02, 0.72), (0.006, 0.82), (0, 0.84)],
                   S, 8)
        um = F(um, "small_black", rot=True)
        h = F(tube("Handle", [(0, 0, 0.84), (0, 0, 0.9), (0.04, 0, 0.93), (0.06, 0, 0.9)], 0.009, S, res=4, bevres=1),
              "small_varnish")
        p.append(xf(join([um, h], "Umb"), (0.03 * (1 - 2 * k), 0.02, 0.02), (a, b, 40 * k)))
    return join(p, "UmbrellaStand")


def Shoes_Pair():
    S = "M_Atlas_Small"
    p = []
    for k, (x, a) in enumerate(((-0.07, 8), (0.09, -20))):
        sh = rounded_box("Shoe", (0.1, 0.28, 0.09), 0.035, S, (0, 0, 0.045), cuts=3, bulge=0.01)
        for v in sh.data.vertices:
            if v.co.y < -0.05:
                v.co.z *= 0.7 + 0.3 * (v.co.y + 0.14) / 0.09
        sole = box_z("Sole", (-0.05, 0.05), (-0.14, 0.14), (0, 0.012), S, bev=0.004)
        s = join([F(sh, "small_black"), F(sole, "small_varnish")], "Shoe")
        p.append(xf(s, (x, 0, 0), (0, 0, a) if k == 0 else (80, 0, a)))
        if k == 1:
            xf(p[-1], (0, 0, 0.05))
    return join(p, "Shoes_Pair")


def PlantPot():
    """Ref 1 potted plant: anthurium on a plant stand in a glazed pot (~1.05 m). Pivot = floor."""
    S = "M_Atlas_Small"
    stand = [F(lathe("StandTop", [(0, 0.5), (0.17, 0.5), (0.17, 0.53), (0, 0.53)], S, 12), "small_varnish")]
    for i in range(3):
        a = math.radians(120 * i)
        stand.append(F(box_z("StandLeg", (-0.015, 0.015), (-0.015, 0.015), (0, 0.5), S),
                       "small_varnish", rot=True))
        stand[-1] = xf(stand[-1], (0.13 * math.cos(a), 0.13 * math.sin(a), 0), (0, 0, 0))
    pot = F(lathe("Pot", [(0, 0), (0.1, 0), (0.12, 0.04), (0.15, 0.2), (0.16, 0.22), (0.14, 0.23), (0.13, 0.21),
                          (0, 0.2)], S, 16, loc=(0, 0, 0.53)), "ceramic", rot=True)
    plant = ph("anthurium_botany_01", 1300, objects=["anthurium_botany_01_a"], name="Plant")
    centre_floor(plant)
    plant = xf(plant, (0, 0, 0.72), scale=(1.15, 1.15, 1.3))
    return join(stand + [pot, plant], "PlantPot")


def Picture_Oval():
    """Ref 2 oval portrait: carved gilt-dark oval frame 0.42 x 0.54, generated canvas. Pivot = wall, centre."""
    S = "M_Atlas_Small"
    a, b = 0.17, 0.23
    can = cyl("Canvas", 1.0, 0.004, S, rot=(90, 0, 0), seg=28)
    can = xf(can, (0, 0.0, 0), scale=(a, 1, b))
    # planar UV for the canvas
    ly = can.data.uv_layers[0]
    c = CELLS["canvas_portrait"]["uv"]
    for poly in can.data.polygons:
        for li in poly.loop_indices:
            v = can.data.vertices[can.data.loops[li].vertex_index].co
            ly.data[li].uv = (c[0] + (0.5 - v.x / (2 * a)) * (c[2] - c[0]), c[1] + (v.z / (2 * b) + 0.5) * (c[3] - c[1]))
    can.data.materials.clear()
    can.data.materials.append(lib.mat(S))
    can = xf(can, (0, -0.012, 0))
    fr = torus("Frame", 1.0, 0.11, S, rot=(90, 0, 0), seg=32, rseg=6, scale=(1, 1, 1))
    fr = xf(fr, scale=(a + 0.035, 0.25, b + 0.035))
    for v in fr.data.vertices:
        pass
    fr = F(fr, "small_brass")
    o = join([can, fr], "Picture_Oval")
    return centre_wall(o)


def Picture_Wide():
    """Bedroom picture (ref 3): wide dark frame 0.9 x 0.48 with a generated seascape. Pivot = wall, centre."""
    S = "M_Atlas_Small"
    W, H = 0.9, 0.48
    can = box_z("Canvas", (-W / 2 + 0.06, W / 2 - 0.06), (-0.02, -0.01), (-H / 2 + 0.06, H / 2 - 0.06), S, bev=0.0)
    ly = can.data.uv_layers[0]
    c = CELLS["canvas_bedroom"]["uv"]
    for poly in can.data.polygons:
        for li in poly.loop_indices:
            v = can.data.vertices[can.data.loops[li].vertex_index].co
            ly.data[li].uv = (c[0] + (0.5 - v.x / (W - 0.12)) * (c[2] - c[0]),
                              c[1] + (v.z / (H - 0.12) + 0.5) * (c[3] - c[1]))
    can.data.materials.clear()
    can.data.materials.append(lib.mat(S))
    p = [can]
    for (x0, x1, z0, z1) in ((-W / 2, W / 2, -H / 2, -H / 2 + 0.06), (-W / 2, W / 2, H / 2 - 0.06, H / 2),
                             (-W / 2, -W / 2 + 0.06, -H / 2, H / 2), (W / 2 - 0.06, W / 2, -H / 2, H / 2)):
        p.append(F(box_z("Frame", (x0, x1), (-0.035, 0.0), (z0, z1), S, bev=0.008), "small_paint_dark",
                   rot=(x1 - x0) < (z1 - z0)))
    o = join(p, "Picture_Wide")
    return centre_wall(o)


def Picture_Landscape():
    """Ref 2 landscape: Poly Haven fancy_picture_frame_01 with the generated meadow on its canvas."""
    o = ph("fancy_picture_frame_01", 900, keys={"canvas": "canvas_landscape"})
    o.name = o.data.name = "Picture_Landscape"
    return centre_wall(o)


def Picture_Hall():
    o = ph("hanging_picture_frame_02", 900, keys={"artwork": "hanging_picture_frame_02_artwork", "glass": None})
    o.name = o.data.name = "Picture_Hall"
    return centre_wall(o)


def _sheet(name, key, w, d, loc, rot, curl=0.02):
    g = grid(name, w, d, 3, 2, "M_Atlas_Small")
    for v in g.data.vertices:
        v.co.z = curl * ((v.co.x / w * 2) ** 2) + R.uniform(-0.002, 0.002)
    g = F(g, key, "stretch" if key in ("magazines",) else "swatch")
    return xf(g, loc, rot)


def Debris_Paper():
    """Scattered paper sheets and newspaper over ~1.4 x 1.0 m (single-sided sheets lie flat; 2 sided via solidify
    is not worth it). Pivot = floor."""
    p = []
    for k in range(9):
        key = "newspaper" if k % 3 == 0 else "paper"
        w, d = (0.42, 0.3) if key == "newspaper" else (0.21, 0.29)
        p.append(_sheet("Sheet", key, w, d, (R.uniform(-0.7, 0.7), R.uniform(-0.5, 0.5), 0.004 + k * 0.0012),
                        (0, 0, R.uniform(0, 360)), R.uniform(0.0, 0.04)))
    p.append(_magazine("Mag", 0.21, 0.28, 0.006, (0.3, -0.3, 0.003), (0, 0, 40), 2))
    return join(p, "Debris_Paper")


def Debris_PaperFloat():
    """Floating paper and a soggy box lid on the basement water. Pivot = water surface."""
    p = []
    for k in range(7):
        p.append(_sheet("Sheet", "paper", 0.21, 0.29, (R.uniform(-1.0, 1.0), R.uniform(-0.8, 0.8), 0.001 * k),
                        (0, 0, R.uniform(0, 360)), 0.03))
    lid = box_z("Lid", (-0.2, 0.2), (-0.15, 0.15), (-0.02, 0.03), "M_Atlas_Small", bev=0.006)
    p.append(xf(F(lid, "small_cardboard"), (0.5, 0.4, 0), (4, -6, 25)))
    return join(p, "Debris_PaperFloat")


def _chunk(name, s, loc, key):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=s)
    for v in bm.verts:
        v.co.x *= R.uniform(0.7, 1.4)
        v.co.y *= R.uniform(0.7, 1.4)
        v.co.z *= R.uniform(0.3, 0.6)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            me.uv_layers[0].data[li].uv = (v.x + v.z, v.y + v.z)
    me.materials.append(lib.mat("M_Atlas_Small"))
    o = F(o, key)
    return xf(o, (loc[0], loc[1], loc[2] + s * 0.3), (0, 0, R.uniform(0, 360)))


def Debris_Plaster():
    """Fallen plaster chunks and crumbs under the torn wallpaper, plus 3 lath sticks. Pivot = floor, ~1.0 x 0.6."""
    p = [_chunk("Chunk", R.uniform(0.02, 0.07), (R.uniform(-0.5, 0.5), R.uniform(-0.3, 0.3), 0), "plaster_chunk")
         for _ in range(10)]
    for k in range(3):
        st = box_z("Lath", (-0.3, 0.3), (-0.012, 0.012), (0, 0.007), "M_Atlas_Small", bev=0.0)
        p.append(xf(F(st, "small_varnish"), (R.uniform(-0.3, 0.3), R.uniform(-0.2, 0.2), 0.01 * k),
                    (0, R.uniform(-4, 4), R.uniform(0, 180))))
    return join(p, "Debris_Plaster")


def Debris_Dishes():
    """Broken plates and a cup on the kitchen floor. Pivot = floor."""
    S = "M_Atlas_Small"
    p = []
    for k in range(6):
        a0 = R.uniform(0, 360)
        sh = lathe("Shard", [(0.0, 0.0), (0.1, 0.003), (0.12, 0.012)], S, 8)
        bm = bmesh.new()
        bm.from_mesh(sh.data)
        keep_a = math.radians(R.uniform(50, 110))
        dead = [v for v in bm.verts if math.atan2(v.co.y, v.co.x) % (2 * math.pi) > keep_a]
        bmesh.ops.delete(bm, geom=dead, context='VERTS')
        bm.to_mesh(sh.data)
        bm.free()
        p.append(xf(F(sh, "ceramic"), (R.uniform(-0.4, 0.4), R.uniform(-0.3, 0.3), 0.002), (0, 0, a0)))
    p.append(xf(_glass_cup("Cup", (0, 0, 0), 0.08, 0.04), (0.2, 0.1, 0.04), (90, 0, 30)))
    for k in range(3):
        pl = lathe("Plate", [(0, 0), (0.08, 0), (0.11, 0.012), (0.12, 0.018), (0.115, 0.02), (0.08, 0.006),
                             (0, 0.006)], S, 16, closed=False)
        p.append(xf(F(pl, "ceramic"), (-0.3 + R.uniform(-0.05, 0.05), -0.2, 0.0 + k * 0.012), (R.uniform(-3, 3), 0, 0)))
    return join(p, "Debris_Dishes")


def Dishes_Stack():
    """Plates, bowls and cups for the counter / dresser shelves. Pivot = surface."""
    S = "M_Atlas_Small"
    p = []
    for k in range(3):
        pl = lathe("Plate", [(0, 0), (0.08, 0), (0.11, 0.012), (0.12, 0.018), (0.115, 0.02), (0.08, 0.006),
                             (0, 0.006)], S, 10)
        p.append(F(xf(pl, (-0.12, 0, k * 0.014)), "ceramic"))
    for k in range(1):
        bw = lathe("Bowl", [(0, 0), (0.04, 0), (0.07, 0.05), (0.065, 0.05), (0.035, 0.008), (0, 0.008)], S, 14)
        p.append(F(xf(bw, (0.1, 0.0, k * 0.03)), "ceramic"))
    for x in (0.25, 0.33):
        p.append(_glass_cup("Mug", (x, 0.05, 0), 0.09, 0.035))
    return join(p, "Dishes_Stack")


def Jars_Shelf():
    """A shelf's worth of jars and tins (basement shelves), ~0.9 m long. Pivot = shelf top centre."""
    S = "M_Atlas_Utility"
    p = []
    x = -0.42
    while x < 0.42:
        r = R.uniform(0.035, 0.055)
        h = R.uniform(0.1, 0.2)
        key = R.choice(["glass_jar", "glass_jar", "util_metal_paint"])
        jar = lathe("Jar", [(0, 0), (r, 0), (r, h * 0.85), (r * 0.85, h * 0.92), (r * 0.85, h), (0, h)], S, 7)
        yy = R.uniform(-0.08, 0.08)
        p.append(F(xf(jar, (x + r, yy, 0)), key, rot=True))
        if key == "glass_jar":
            p.append(F(cyl("Lid", r * 0.88, 0.015, S, loc=(x + r, yy, h), seg=7), "util_metal_paint"))
        x += 2 * r + R.uniform(0.01, 0.05)
    for k in range(2):
        bx = box_z("Box", (-0.12, 0.12), (-0.1, 0.1), (0, R.uniform(0.12, 0.2)), S, bev=0.005)
        p.append(F(xf(bx, (R.uniform(-0.3, 0.3), 0.0, 0.0), (0, 0, R.uniform(-10, 10))), "util_paper"))
    return join(p, "Jars_Shelf")


# ================================================================== Poly Haven models (decimated, re-baked)
def _ph(asset, tris, **kw):
    def build():
        return ph(asset, tris, **kw)
    build.__name__ = asset
    return build


def GothicBed_01():
    return ph("GothicBed_01", 4600)


def Rockingchair_01():
    return ph("Rockingchair_01", 3000)


def Sofa_01():
    return ph("Sofa_01", 2600)


def ArmChair_01():
    return ph("ArmChair_01", 2000)


def Generator():
    return ph("portable_generator", 4600, name="portable_generator")


def KnockedChair():
    """painted_wooden_chair_01 lying on its back (kitchen). Pivot = floor."""
    o = ph("painted_wooden_chair_01", 724, name="KnockedChair")
    rot_mesh(o, 0, -88)
    centre_floor(o)
    return o


def BoxStack():
    """Living-room boxes (ref 2): 3 cardboard boxes stacked, slightly askew. Pivot = floor."""
    a = ph("cardboard_box_01", 300, name="BoxA")
    b = ph("cardboard_box_01", 300, name="BoxB")
    c = ph("cardboard_box_01", 300, name="BoxC")
    xf(b, (0.05, 0.03, 0.34), (0, 0, 12), (0.9, 0.9, 0.85))
    xf(c, (0.55, -0.05, 0), (0, 0, 80), (1.1, 1.0, 1.0))
    return join([a, b, c], "BoxStack")


def BoxFallen():
    o = ph("cardboard_box_01", 300, name="BoxFallen")
    rot_mesh(o, 25, 0, 100)
    centre_floor(o)
    return o


def TrashBags():
    a = ph("trashbag", 450, name="BagA")
    b = ph("trashbag", 450, name="BagB")
    xf(b, (0.48, 0.15, 0), (0, 0, 140), (0.9, 0.9, 0.8))
    return join([a, b], "TrashBags")


def Pans_Stove():
    """Enamel pot on a burner and a brass pan, for the stove top. Pivot = stove top."""
    a = ph("pot_enamel_01", 550, name="Pot")
    b = ph("brass_pan_01", 400, name="Pan")
    xf(a, (-0.1, 0.05, 0))
    xf(b, (0.1, -0.05, 0), (0, 0, -30))
    return join([a, b], "Pans_Stove")


# Registry: model -> (builder, pivot, collider, kind). kind: furniture (static, navmesh obstacle) / prop (static,
# small: no lightmap contribution) / debris (no collider) / fixture / wall / rug
MODELS = {
    # fixtures
    "FX_CeilingFan": (FX_CeilingFan, "ceiling", "none", "fixture"),
    "FX_Chandelier3": (FX_Chandelier3, "ceiling", "none", "fixture"),
    "FX_SconceBrass": (FX_SconceBrass, "wall", "none", "fixture"),
    "FX_SconceBroken": (FX_SconceBroken, "wall", "none", "fixture"),
    "FX_SconceTulip": (FX_SconceTulip, "wall", "none", "fixture"),
    "FX_TableLamp": (FX_TableLamp, "top", "none", "fixture"),
    "FX_TableLampSmall": (FX_TableLampSmall, "top", "none", "fixture"),
    "FX_Dome": (FX_Dome, "ceiling", "none", "fixture"),
    "FX_BulbPendant": (FX_BulbPendant, "ceiling", "none", "fixture"),
    # rugs
    "Rug_Oriental": (Rug_Oriental, "floor", "none", "rug"),
    "Rug_OrientalSmall": (Rug_OrientalSmall, "floor", "none", "rug"),
    "Rug_Geo": (Rug_Geo, "floor", "none", "rug"),
    "Runner_Hall": (Runner_Hall, "floor", "none", "rug"),
    "Runner_Entrance": (Runner_Entrance, "floor", "none", "rug"),
    # Poly Haven furniture
    "Sofa_01": (Sofa_01, "floor", "box", "furniture"),
    "ArmChair_01": (ArmChair_01, "floor", "box", "furniture"),
    "GothicCommode_01": (_ph("GothicCommode_01", 1600, split=("GothicCommode_01_door",)), "floor", "box", "furniture"),
    "ClassicNightstand_01": (_ph("ClassicNightstand_01", 800), "floor", "box", "furniture"),
    "GothicBed_01": (GothicBed_01, "floor", "box", "furniture"),
    "Rockingchair_01": (Rockingchair_01, "floor", "box", "furniture"),
    "WoodenTable_01": (_ph("WoodenTable_01", 952), "floor", "box", "furniture"),
    "wooden_bookshelf_worn": (_ph("wooden_bookshelf_worn", 1500), "floor", "box", "furniture"),
    "vintage_grandfather_clock_01": (_ph("vintage_grandfather_clock_01", 2000), "floor", "box", "furniture"),
    "ClassicConsole_01": (_ph("ClassicConsole_01", 1600), "floor", "box", "furniture"),
    "GreenChair_01": (_ph("GreenChair_01", 1500), "floor", "box", "furniture"),
    "side_table_tall_01": (_ph("side_table_tall_01", 900), "floor", "box", "furniture"),
    "small_wooden_table_01": (_ph("small_wooden_table_01", 1000), "floor", "box", "furniture"),
    "painted_wooden_bench": (_ph("painted_wooden_bench", 630), "floor", "box", "furniture"),
    "painted_wooden_cabinet": (_ph("painted_wooden_cabinet", 1500), "floor", "box", "furniture"),
    "painted_wooden_cabinet_02": (_ph("painted_wooden_cabinet_02", 966), "floor", "box", "furniture"),
    "painted_wooden_chair_01": (_ph("painted_wooden_chair_01", 724), "floor", "box", "furniture"),
    "KnockedChair": (KnockedChair, "floor", "box", "furniture"),
    "electric_stove": (_ph("electric_stove", 1800), "floor", "box", "furniture"),
    "steel_frame_shelves_01": (_ph("steel_frame_shelves_01", 1200), "floor", "box", "furniture"),
    "wooden_crate_01": (_ph("wooden_crate_01", 700), "floor", "box", "furniture"),
    "wooden_crate_02": (_ph("wooden_crate_02", 800), "floor", "box", "furniture"),
    "barrel_03": (_ph("barrel_03", 900), "floor", "box", "furniture"),
    "portable_generator": (Generator, "floor", "box", "furniture"),
    "metal_jerrycan_green": (_ph("metal_jerrycan_green", 900), "floor", "box", "prop"),
    "wooden_bucket_01": (_ph("wooden_bucket_01", 450), "floor", "box", "prop"),
    "TrashBags": (TrashBags, "floor", "box", "prop"),
    "BoxStack": (BoxStack, "floor", "box", "furniture"),
    "BoxFallen": (BoxFallen, "floor", "none", "debris"),
    "throw_pillows_01": (_ph("throw_pillows_01", 700), "top", "none", "prop"),
    "vintage_suitcase": (_ph("vintage_suitcase", 900, keys={"_02": "vintage_suitcase_02", "_01": "vintage_suitcase_01"}),
                         "floor", "box", "prop"),
    "mantel_clock_01": (_ph("mantel_clock_01", 1000), "top", "none", "prop"),
    "ceramic_vase_01": (_ph("ceramic_vase_01", 300), "top", "none", "prop"),
    "ceramic_vase_03": (_ph("ceramic_vase_03", 300), "top", "none", "prop"),
    "wall_clock": (_ph("wall_clock", 700), "wall", "none", "wall"),
    "power_box_01": (_ph("power_box_01", 1200), "wall", "none", "wall"),
    "bench_vice_01": (_ph("bench_vice_01", 700), "top", "none", "prop"),
    "Pans_Stove": (Pans_Stove, "top", "none", "prop"),
    # self-built furniture
    "Settee_Plaid": (Settee_Plaid, "floor", "box", "furniture"),
    "Wardrobe_Painted": (Wardrobe_Painted, "floor", "box", "furniture"),
    "CoffeeTable_Glass": (CoffeeTable_Glass, "floor", "box", "furniture"),
    "CoatStand": (CoatStand, "floor", "box", "prop"),
    "Workbench": (Workbench, "floor", "box", "furniture"),
    "KitchenTable": (KitchenTable, "floor", "box", "furniture"),
    "SinkUnit": (SinkUnit, "floor", "box", "furniture"),
    "Fridge_Retro": (Fridge_Retro, "floor", "box", "furniture"),
    "ClawfootTub": (ClawfootTub, "floor", "box", "furniture"),
    "Toilet_Old": (Toilet_Old, "floor", "box", "furniture"),
    "PedestalSink": (PedestalSink, "floor", "box", "furniture"),
    "MirrorCabinet": (MirrorCabinet, "wall", "none", "wall"),
    "Towel_Rail": (Towel_Rail, "wall", "none", "wall"),
    "Curtains_Window": (Curtains_Window, "wall", "none", "wall"),
    "Bed_Blanket": (Bed_Blanket, "floor", "none", "prop"),
    "Speaker": (Speaker, "floor", "box", "prop"),
    "PlantPot": (PlantPot, "floor", "box", "prop"),
    "UmbrellaStand": (UmbrellaStand, "floor", "box", "prop"),
    "Shoes_Pair": (Shoes_Pair, "floor", "none", "debris"),
    # pictures and clutter
    "Picture_Oval": (Picture_Oval, "wall", "none", "wall"),
    "Picture_Wide": (Picture_Wide, "wall", "none", "wall"),
    "Picture_Landscape": (Picture_Landscape, "wall", "none", "wall"),
    "Picture_Hall": (Picture_Hall, "wall", "none", "wall"),
    "Clutter_CoffeeTable": (Clutter_CoffeeTable, "floor", "none", "prop"),
    "Clutter_SideTable": (Clutter_SideTable, "top", "none", "prop"),
    "MantelClutter": (MantelClutter, "top", "none", "prop"),
    "Dishes_Stack": (Dishes_Stack, "top", "none", "prop"),
    "Jars_Shelf": (Jars_Shelf, "top", "none", "prop"),
    "Debris_Paper": (Debris_Paper, "floor", "none", "debris"),
    "Debris_PaperFloat": (Debris_PaperFloat, "floor", "none", "debris"),
    "Debris_Plaster": (Debris_Plaster, "floor", "none", "debris"),
    "Debris_Dishes": (Debris_Dishes, "floor", "none", "debris"),
}
