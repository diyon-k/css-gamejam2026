"""BLINK Bungalow Phase 4: render surface materials in Blender on the same wall/floor sample as Unity.

  blender -b --factory-startup --python tools/blender/bungalow_materials_compare.py -- \
      --out <dir> [--materials M_Wall_Damask M_FloorPlank M_BrickWhite] [--samples 64]

Mirrors BungalowMatPreview.RenderCompare (Unity, Assets/Art/Environment/Bungalow/Materials/Editor):
  * wall 2.4 x 1.8 m (Unity z = +1, facing the camera) on a 2.4 x 2.0 m floor, UV0 = metres / SURFACE_TILE_M (the kit's
    UV contract; materials use tiling 1), the same textures from Unity/Assets/Art/Environment/Bungalow/Textures.
  * one white sun, Unity intensity 1 (Blender strength pi, since URP's diffuse has no 1/pi), direction = Unity
    Euler(45, 35, 0); flat ambient linear 0.08 on diffuse rays only (Unity: ambient Flat, reflection intensity 0);
    camera background 0.5 sRGB; no shadows; Standard view transform (no tonemapping, as the Unity camera has no post).
  * camera at Unity (0, 1.2, -1.6) looking at (0, 0.55, 0.6), vertical FOV 50, 800 x 600.
Unity axes (x right, y up, z forward) map to Blender (x right, y forward, z up) as (ux, uz, uy).
URP reads occlusion only for indirect light, so the mask AO is left out here; roughness = 1 - mask.A,
metallic = mask.R, normal = OpenGL tangent map.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bungalow_layout as L  # noqa: E402  (plain Python: SURFACE_TILE_M is the UV0 tile contract)
TEX = os.path.normpath(os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Environment", "Bungalow", "Textures"))

# name -> (base, normal, mask, normal strength, smoothness scale); mirrors BungalowMaterialsBuilder.
# Tile size (metres per UV unit) comes from bungalow_layout.SURFACE_TILE_M, like the kit's UV0.
MATS = {
    "M_FloorPlank": ("M_FloorPlank", "M_FloorPlank", "M_FloorPlank", 1.0, 1.0),
    "M_Parquet": ("M_Parquet", "M_Parquet", "M_Parquet", 1.0, 0.7),
    "M_Wall_Damask": ("M_Wall_Damask", "M_Wall_Shared", "M_Wall_Shared", 1.0, 1.0),
    "M_Wall_Pale": ("M_Wall_Pale", "M_Wall_Shared", "M_Wall_Shared", 1.0, 1.0),
    "M_Wall_Floral": ("M_Wall_Floral", "M_Wall_Floral", "M_Wall_Floral", 1.0, 1.0),
    "M_BrickWhite": ("M_BrickWhite", "M_BrickWhite", "M_BrickWhite", 1.0, 1.0),
    "M_Plaster": ("M_Plaster", "M_Plaster", "M_Plaster", 1.0, 1.0),
}


def U(x, y, z):
    return Vector((x, z, y))


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out, mats, samples = None, ["M_Wall_Damask", "M_FloorPlank", "M_BrickWhite"], 64
    i = 0
    while i < len(a):
        if a[i] == "--out":
            out = a[i + 1]; i += 2
        elif a[i] == "--samples":
            samples = int(a[i + 1]); i += 2
        elif a[i] == "--materials":
            mats = []
            i += 1
            while i < len(a) and not a[i].startswith("--"):
                mats.append(a[i]); i += 1
        else:
            i += 1
    return out, mats, samples


def img(path, colour):
    im = bpy.data.images.load(path, check_existing=True)
    im.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    im.alpha_mode = "CHANNEL_PACKED"
    return im


def material(name):
    b, n, k, nstr, sscale = MATS[name]
    tile = L.SURFACE_TILE_M[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    uv = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    t = 1.0 / tile
    mp.inputs["Scale"].default_value = (t, t, 1.0)
    nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])

    def tex(file, colour):
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img(file, colour)
        node.interpolation = "Linear"
        nt.links.new(mp.outputs["Vector"], node.inputs["Vector"])
        return node

    base = tex(os.path.join(TEX, b, f"{b}_Base.png"), True)
    nrm = tex(os.path.join(TEX, n, f"{n}_Normal.png"), False)
    msk = tex(os.path.join(TEX, k, f"{k}_Mask.png"), False)
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.inputs["Strength"].default_value = nstr
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(msk.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    sm = nt.nodes.new("ShaderNodeMath")
    sm.operation = "MULTIPLY"
    sm.inputs[1].default_value = sscale
    nt.links.new(msk.outputs["Alpha"], sm.inputs[0])
    rough = nt.nodes.new("ShaderNodeMath")
    rough.operation = "SUBTRACT"
    rough.inputs[0].default_value = 1.0
    nt.links.new(sm.outputs["Value"], rough.inputs[1])
    nt.links.new(rough.outputs["Value"], bsdf.inputs["Roughness"])
    return m


def quad(name, corners_unity, uvs, mat):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [bm.verts.new(U(*c)) for c in corners_unity]
    f = bm.faces.new(vs)
    lay = bm.loops.layers.uv.verify()
    for loop, uv in zip(f.loops, uvs):
        loop[lay].uv = uv
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(mat)
    return ob


def setup_scene(samples):
    sc = bpy.context.scene
    for o in list(sc.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 800, 600
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.0
    sc.view_settings.gamma = 1.0
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    # world: background 0.5 sRGB for camera rays, 0.08 linear for diffuse rays, black for glossy
    w = bpy.data.worlds.new("Neutral")
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    lp = nt.nodes.new("ShaderNodeLightPath")
    bg_cam = nt.nodes.new("ShaderNodeBackground")
    bg_cam.inputs["Color"].default_value = (0.214, 0.214, 0.214, 1)
    bg_dif = nt.nodes.new("ShaderNodeBackground")
    bg_dif.inputs["Color"].default_value = (0.08, 0.08, 0.08, 1)
    black = nt.nodes.new("ShaderNodeBackground")
    black.inputs["Color"].default_value = (0, 0, 0, 1)
    mix1 = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(lp.outputs["Is Diffuse Ray"], mix1.inputs["Fac"])
    nt.links.new(black.outputs["Background"], mix1.inputs[1])
    nt.links.new(bg_dif.outputs["Background"], mix1.inputs[2])
    mix2 = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(lp.outputs["Is Camera Ray"], mix2.inputs["Fac"])
    nt.links.new(mix1.outputs["Shader"], mix2.inputs[1])
    nt.links.new(bg_cam.outputs["Background"], mix2.inputs[2])
    nt.links.new(mix2.outputs["Shader"], out.inputs["Surface"])
    sc.world = w
    # sun = Unity directional Euler(45, 35, 0), intensity 1 -> strength pi; no shadows
    f = Vector((math.cos(math.radians(45)) * math.sin(math.radians(35)), -math.sin(math.radians(45)),
                math.cos(math.radians(45)) * math.cos(math.radians(35))))     # Unity forward
    d = U(f.x, f.y, f.z)
    sd = bpy.data.lights.new("Sun", "SUN")
    sd.energy = math.pi
    sd.angle = math.radians(0.5)
    sd.use_shadow = False
    sun = bpy.data.objects.new("Sun", sd)
    sun.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(sun)
    cd = bpy.data.cameras.new("Cam")
    cd.sensor_fit = "VERTICAL"
    cd.angle = math.radians(50)
    cd.clip_start = 0.03
    cam = bpy.data.objects.new("Cam", cd)
    pos, tgt = U(0, 1.2, -1.6), U(0, 0.55, 0.6)
    cam.location = pos
    cam.rotation_euler = (tgt - pos).to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(cam)
    sc.camera = cam


def main():
    out, mats, samples = args()
    if not out:
        raise SystemExit("--out <dir> required")
    os.makedirs(out, exist_ok=True)
    for name in mats:
        setup_scene(samples)
        m = material(name)
        # wall: Unity x -1.2..1.2, y 0..1.8, z = 1.0, facing -z; UV = (x, y)
        quad("wall", [(1.2, 0, 1.0), (1.2, 1.8, 1.0), (-1.2, 1.8, 1.0), (-1.2, 0, 1.0)],
             [(1.2, 0), (1.2, 1.8), (-1.2, 1.8), (-1.2, 0)], m)
        # floor: Unity x -1.2..1.2, z -1..1, y = 0, facing +y; UV = (x, z)
        quad("floor", [(-1.2, 0, -1.0), (1.2, 0, -1.0), (1.2, 0, 1.0), (-1.2, 0, 1.0)],
             [(-1.2, -1.0), (1.2, -1.0), (1.2, 1.0), (-1.2, 1.0)], m)
        for o in bpy.context.scene.objects:
            if o.type == "MESH":
                o.data.update()
        bpy.context.scene.render.filepath = os.path.join(out, f"compare_blender_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("rendered", name)


main()
