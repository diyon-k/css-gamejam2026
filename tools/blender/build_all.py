"""Build + export every mansion asset to Unity.

  blender -b --factory-startup --python tools/blender/build_all.py -- [AssetName ...] [--preview DIR] [--lit]
          [--no-export] [--textures]

1. Shared tiling PBR sets (textures.py) -> Unity/Assets/Art/Mansion/Blender/Textures/<Family>_*.png
   (generated when missing or with --textures).
2. Each asset is built (assets.py) with per-part UVs on the shared materials.
   Assets in BAKE are reduced (decimate) and Smart-UV unwrapped, then the full-detail build is Cycles-baked
   (selected-to-active) into <Asset>_Albedo/_Normal/_Mask.png and one <Asset>_Baked material;
   Glass/Wax faces keep their shared materials.
3. FBX -> Unity/Assets/Art/Mansion/Blender/<Asset>.fbx (metres, forward -Z, up Y, transforms baked, tangents),
   so Unity imports at scale 1 / rotation 0 with the asset front facing +Z.
"""
import math
import os
import sys
import zlib

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import lib  # noqa: E402
import assets  # noqa: E402
import textures  # noqa: E402

OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Mansion", "Blender"))
TEX = os.path.join(OUT, "Textures")

# asset: (target tris for the low-poly, bake resolution, cage extrusion m)
BAKE = {
    "FourPosterBed": (3500, 1024, 0.02),
    "Chandelier": (3000, 1024, 0.01),
    "Armchair": (2000, 1024, 0.015),
    "Candelabra": (1500, 512, 0.006),
    "Bookshelf": (1800, 1024, 0.012),
}
KEEP_SHARED = ("Glass", "Wax")   # transparent / emissive slots stay on the shared materials


def export(name):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"),
        use_selection=False, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_tspace=True, use_mesh_modifiers=True, add_leaf_bones=False,
        bake_anim=False, path_mode='STRIP', use_custom_props=False)


def _select(objs, active):
    for o in bpy.context.scene.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = active


def _save(img, path):
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()


def bake(name, hi, target, res, ext):
    """Decimate a copy of hi, unwrap it, bake hi onto it. Returns the low object (named `name`)."""
    sc = bpy.context.scene
    hi.name = name + "_HIGH"
    lo = bpy.data.objects.new(name, hi.data.copy())
    sc.collection.objects.link(lo)
    for c in list(hi.children):
        c.parent = lo
    tris = sum(len(p.vertices) - 2 for p in lo.data.polygons)
    md = lo.modifiers.new("dec", 'DECIMATE')
    md.ratio = min(1.0, target / tris)
    md.use_collapse_triangulate = True
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(lo.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    lo.modifiers.clear()
    lo.data = me
    me.name = name
    lib.finish(lo, 40.0)
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    _select([lo], lo)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')

    face_mats = [me.materials[p.material_index].name for p in me.polygons]
    bm_ = bpy.data.materials.new(name + "_Baked")
    bm_.use_nodes = True
    nt = bm_.node_tree
    imgs = {}
    for key, srgb in (("Albedo", True), ("Normal", False), ("Emit", False), ("AO", False)):
        im = bpy.data.images.new(f"{name}_{key}", res, res, alpha=True)
        im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = im
        imgs[key] = (im, node)
    me.materials.clear()
    me.materials.append(bm_)
    for p in me.polygons:
        p.material_index = 0

    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 1
    bk = sc.render.bake
    bk.use_selected_to_active = True
    bk.cage_extrusion = ext
    bk.max_ray_distance = ext * 3
    bk.margin = 6
    _select([hi, lo], lo)

    def run(key, btype, **kw):
        nt.nodes.active = imgs[key][1]
        bpy.ops.object.bake(type=btype, use_selected_to_active=True, cage_extrusion=ext,
                            max_ray_distance=ext * 3, margin=6, target='IMAGE_TEXTURES', **kw)

    run("Normal", 'NORMAL', normal_space='TANGENT')
    lib.bake_mode("albedo")
    run("Albedo", 'EMIT')
    lib.bake_mode("mask")
    run("Emit", 'EMIT')
    lib.bake_mode(None)
    sc.cycles.samples = 48
    run("AO", 'AO')

    # pack URP mask: R metallic, G AO (tiling AO x baked AO), A smoothness
    px = lambda k: np.array(imgs[k][0].pixels[:], dtype=np.float32).reshape(res, res, 4)  # noqa: E731
    emit, ao = px("Emit"), px("AO")
    mask = np.dstack([emit[..., 0], emit[..., 1] * ao[..., 0], np.zeros((res, res)), emit[..., 2]])
    mimg = bpy.data.images.new(f"{name}_Mask", res, res, alpha=True)
    mimg.colorspace_settings.name = 'Non-Color'
    mimg.pixels.foreach_set(mask.astype(np.float32).ravel())
    os.makedirs(TEX, exist_ok=True)
    _save(imgs["Albedo"][0], os.path.join(TEX, name + "_Albedo.png"))
    _save(imgs["Normal"][0], os.path.join(TEX, name + "_Normal.png"))
    _save(mimg, os.path.join(TEX, name + "_Mask.png"))

    # wire the baked material for previews
    L = nt.links
    bsdf = nt.nodes["Principled BSDF"]
    L.new(imgs["Albedo"][1].outputs[0], bsdf.inputs["Base Color"])
    mnode = nt.nodes.new("ShaderNodeTexImage")
    mnode.image = mimg
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    L.new(mnode.outputs[0], sep.inputs[0])
    L.new(sep.outputs[0], bsdf.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    L.new(mnode.outputs["Alpha"], inv.inputs[1])
    L.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    L.new(imgs["Normal"][1].outputs[0], nm.inputs["Color"])
    L.new(nm.outputs[0], bsdf.inputs["Normal"])
    bm_.diffuse_color = (0.5, 0.4, 0.35, 1)

    for keep in KEEP_SHARED:
        if keep in face_mats:
            me.materials.append(lib.mat(keep))
            idx = len(me.materials) - 1
            for p, fm in zip(me.polygons, face_mats):
                if fm == keep:
                    p.material_index = idx
    bpy.data.objects.remove(hi, do_unlink=True)
    return lo


def preview(name, path, lit):
    sc = bpy.context.scene
    lo, hi = lib.bounds()
    c = (lo + hi) / 2
    rad = (hi - lo).length / 2
    if lit:
        try:
            sc.render.engine = 'BLENDER_EEVEE_NEXT'
        except TypeError:
            sc.render.engine = 'BLENDER_EEVEE'
        if sc.world is None:
            sc.world = bpy.data.worlds.new("W")
        sc.world.use_nodes = True
        sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.07, 1)
        sc.world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
        sc.view_settings.view_transform = 'AgX'
        for ln, typ, e, rot in (("Key", 'SUN', 4.0, (50, 10, -35)), ("Rim", 'SUN', 1.5, (60, 0, 150))):
            ld = bpy.data.lights.new(ln, typ)
            ld.energy = e
            lo_ = bpy.data.objects.new(ln, ld)
            lo_.rotation_euler = [math.radians(a) for a in rot]
            sc.collection.objects.link(lo_)
    else:
        sc.render.engine = 'BLENDER_WORKBENCH'
        sh = sc.display.shading
        sh.light = 'STUDIO'
        sh.color_type = 'MATERIAL'
        sh.show_cavity = True
        sh.cavity_type = 'BOTH'
        sh.show_shadows = True
        if sc.world is None:
            sc.world = bpy.data.worlds.new("W")
        sc.world.color = (0.18, 0.18, 0.2)
    sc.render.resolution_x = sc.render.resolution_y = 640
    d = Vector(assets.VIEW.get(name, (0.9, -1.6, 0.8))).normalized()
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    sc.collection.objects.link(cam)
    cam.data.lens = 50
    cam.location = c + d * (rad / math.sin(cam.data.angle / 2) * 1.02)
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam.data.clip_start = rad * 0.01
    cam.data.clip_end = rad * 20
    sc.camera = cam
    sc.render.filepath = os.path.join(path, name + ("_lit" if lit else "") + ".png")
    bpy.ops.render.render(write_still=True)
    for o in list(sc.objects):
        if o.type in ('CAMERA', 'LIGHT'):
            bpy.data.objects.remove(o, do_unlink=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    prev = None
    if "--preview" in argv:
        i = argv.index("--preview")
        prev = argv[i + 1]
        del argv[i:i + 2]
    do_export = "--no-export" not in argv
    lit = "--lit" in argv
    if "--textures" in argv or not os.path.exists(os.path.join(TEX, "Wood_Albedo.png")):
        textures.build(TEX)
    lib.TEX_DIR = TEX
    names = [a for a in argv if not a.startswith("--")] or list(assets.BUILDERS)
    for name in names:
        lib.clear_scene()
        lib.seed(zlib.crc32(name.encode()))
        root = assets.BUILDERS[name]()
        for o in bpy.context.scene.objects:
            if o.type == 'MESH':
                lib.finish(o, assets.SHARP.get(name, 50.0))
        before = lib.tri_count()
        if name in BAKE:
            bake(name, root, *BAKE[name])
        lo, hi = lib.bounds()
        size = hi - lo
        kids = sorted(o.name for o in bpy.context.scene.objects if o.parent)
        print(f"ASSET {name}: tris={lib.tri_count()} (built {before}) size=({size.x:.3f}, {size.y:.3f}, "
              f"{size.z:.3f}) children={kids}")
        if do_export:
            export(name)
        if prev:
            preview(name, prev, lit)


main()
