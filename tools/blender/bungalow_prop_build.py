"""BLINK Bungalow (Phase 5): build, decimate, atlas-map and export every prop and fixture model.

  python -I tools/blender/bungalow_prop_textures.py --src <download root>          (the 4 atlases + cell table)
  blender -b --factory-startup --python tools/blender/bungalow_prop_build.py -- --src <download root>
          [Model ...] [--preview DIR] [--no-export]
  python -I tools/blender/bungalow_props.py                                         (placement + fixtures JSON)

Per model (bungalow_prop_models.MODELS): build -> smooth/sharp normals -> UV2 (lib.uv_lightmap, non-overlapping) ->
FBX Unity/Assets/Art/Environment/Bungalow/Props/<Model>.fbx (build_all.export: metres, forward -Z, up Y), and a row
in Props/bungalow_prop_models.json: triangles, Unity-local bounds, the top surface height (for things placed on
it), pivot type, collider / static kind, and for fixtures the Unity-local emitter offset.
--preview renders a textured Workbench thumbnail per model plus a contact sheet into DIR.
"""
import json
import math
import os
import sys
import zlib

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import lib  # noqa: E402
import build_all  # noqa: E402
import bungalow_prop_models as PM  # noqa: E402

PROPS_DIR = os.path.join(build_all.TARGET_OUT["bungalow"], "Props")
MODELS_JSON = os.path.join(PROPS_DIR, "bungalow_prop_models.json")
TEXDIR = os.path.join(build_all.TARGET_OUT["bungalow"], "Textures")


def to_unity(v):
    return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def preview_materials():
    """Give the atlas materials their Base maps so Workbench (TEXTURE colour) shows them."""
    for a in ("Soft", "Wood", "Small", "Utility"):
        m = lib.mat("M_Atlas_" + a)
        nt = m.node_tree
        if "AtlasTex" in nt.nodes:
            continue
        t = nt.nodes.new("ShaderNodeTexImage")
        t.name = "AtlasTex"
        t.image = bpy.data.images.load(os.path.join(TEXDIR, f"Atlas_{a}", f"Atlas_{a}_Base.png"), check_existing=True)
        nt.links.new(t.outputs[0], nt.nodes["Principled BSDF"].inputs["Base Color"])
        nt.nodes.active = t


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def levels(o):
    """Heights of the broad up-facing surfaces (shelves), lowest first."""
    by_z = {}
    for p in o.data.polygons:
        if p.normal.z > 0.95:
            z = round(p.center.z, 2)
            by_z[z] = by_z.get(z, 0.0) + p.area
    return [z for z, a in sorted(by_z.items()) if a > 0.08 and z > 0.05]


def top_height(o):
    """Highest broad up-facing surface (for placing things on it): area-weighted z of faces with n.z > 0.95."""
    best = None
    by_z = {}
    for p in o.data.polygons:
        if p.normal.z > 0.95:
            z = round(p.center.z, 2)
            by_z[z] = by_z.get(z, 0.0) + p.area
    for z, a in sorted(by_z.items(), reverse=True):
        if a > 0.02:
            best = z
            break
    return best


def purge():
    lib.clear_scene()
    for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.images):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)
    for m in list(bpy.data.materials):
        if m.users == 0 and not m.name.startswith(("M_Atlas_", "M_Shade", "M_Glass")):
            bpy.data.materials.remove(m)


def build_one(name, export=True):
    fn, pivot, col, kind = PM.MODELS[name]
    purge()
    lib.seed(zlib.crc32(name.encode()))
    PM.R.seed(zlib.crc32(name.encode()))
    root = fn()
    root.name = name
    root.data.name = name
    if pivot == "wall" and fn.__name__ in ("wall_clock", "power_box_01"):
        PM.centre_wall(root)
    bpy.context.view_layer.update()
    for o in list(bpy.context.scene.objects):
        if o.type == 'MESH':
            lib.finish(o, 40.0)
            lib.uv_lightmap(o, margin=0.01)
    lo, hi = PM.bounds(root)
    n = sum(tris(o) for o in bpy.context.scene.objects if o.type == 'MESH')
    row = dict(tris=n, pivot=pivot, collider=col, kind=kind,
               lo=to_unity((hi.x, lo.y, lo.z)), hi=to_unity((lo.x, hi.y, hi.z)),
               size=[round(hi.x - lo.x, 3), round(hi.z - lo.z, 3), round(hi.y - lo.y, 3)],
               top=top_height(root), levels=levels(root), materials=sorted({m.name for o in bpy.context.scene.objects if o.type == 'MESH'
                                                       for m in o.data.materials if m}))
    # (lo/hi above: Unity lo = (-hi.x, lo.z, -hi.y), hi = (-lo.x, hi.z, -lo.y))
    row["lo"] = [round(-hi.x, 4), round(lo.z, 4), round(-hi.y, 4)]
    row["hi"] = [round(-lo.x, 4), round(hi.z, 4), round(-lo.y, 4)]
    em = bpy.data.objects.get(name + "_Emitter")
    if em:
        row["emitter"] = to_unity(em.location)
        row["emissive"] = name + "_Emissive" if bpy.data.objects.get(name + "_Emissive") else ""
    if export:
        build_all.export(name, "Props")
    print(f"MODEL {name}: tris={n} size={row['size']} top={row['top']} mats={row['materials']}"
          + (f" emitter={row.get('emitter')}" if em else ""))
    return row


def render_preview(name, out_dir):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_cavity = True
    sh.show_shadows = False
    sc.render.resolution_x = sc.render.resolution_y = 320
    sc.render.film_transparent = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.color = (0.12, 0.12, 0.13)
    lo, hi = lib.bounds()
    c = (lo + hi) / 2
    rad = max((hi - lo).length / 2, 0.05)
    d = Vector((0.55, -1.0, 0.45)).normalized()
    cam = bpy.data.objects.new("PCam", bpy.data.cameras.new("PCam"))
    sc.collection.objects.link(cam)
    cam.data.lens = 50
    cam.location = c + d * (rad / math.sin(cam.data.angle / 2) * 1.05)
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam.data.clip_start = rad * 0.01
    cam.data.clip_end = rad * 20
    sc.camera = cam
    sc.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)


def contact_sheet(out_dir, names):
    import numpy as np
    ims = []
    for n in names:
        p = os.path.join(out_dir, n + ".png")
        if os.path.exists(p):
            im = bpy.data.images.load(p)
            a = np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4)
            ims.append((n, a))
            bpy.data.images.remove(im)
    if not ims:
        return
    cols = 8
    s = 320
    rows = (len(ims) + cols - 1) // cols
    sheet = np.zeros((rows * s, cols * s, 4), np.float32)
    for i, (n, a) in enumerate(ims):
        r, c = divmod(i, cols)
        y0 = (rows - 1 - r) * s
        sheet[y0:y0 + s, c * s:(c + 1) * s] = a[:s, :s]
    img = bpy.data.images.new("sheet", cols * s, rows * s, alpha=True)
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = os.path.join(out_dir, "_contact_models.png")
    img.file_format = 'PNG'
    img.save()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--src" in argv:
        i = argv.index("--src")
        PM.SRC = argv[i + 1]
        del argv[i:i + 2]
    prev = None
    if "--preview" in argv:
        i = argv.index("--preview")
        prev = argv[i + 1]
        del argv[i:i + 2]
        os.makedirs(prev, exist_ok=True)
    export = "--no-export" not in argv
    build_all.set_target("bungalow")
    PM.load_cells()
    names = [a for a in argv if not a.startswith("--")] or list(PM.MODELS)
    data = {}
    if os.path.exists(MODELS_JSON):
        with open(MODELS_JSON, encoding="utf-8") as f:
            data = json.load(f).get("models", {})
    failed = []
    for n in names:
        try:
            data[n] = build_one(n, export)
            if prev:
                preview_materials()
                render_preview(n, prev)
        except Exception as e:  # keep going, report at the end
            import traceback
            traceback.print_exc()
            failed.append(f"{n}: {e}")
    data = {k: data[k] for k in PM.MODELS if k in data}
    if export:
        os.makedirs(PROPS_DIR, exist_ok=True)
        with open(MODELS_JSON, "w", encoding="utf-8", newline="\n") as f:
            json.dump(dict(source="tools/blender/bungalow_prop_build.py", units="m, Unity model space (front +Z)",
                           models=data), f, indent=1)
    if prev:
        contact_sheet(prev, names)
    print("TOTAL model tris (one of each):", sum(r["tris"] for r in data.values()))
    print("FAILED:", failed)


if __name__ == "__main__":
    main()
