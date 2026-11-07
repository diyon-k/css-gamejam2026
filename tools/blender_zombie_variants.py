import bpy, bmesh, numpy as np, os, sys
from mathutils import Vector
SRC = r"C:/Users/jackp/github/css-gamejam2026/Unity/Assets/Art/Zombies/ZombieGirl"
OUT = r"C:/Users/jackp/github/css-gamejam2026/Unity/Assets/Art/Zombies"
PREV = sys.argv[sys.argv.index("--")+1]

# name: (inflate cm per bone-group keyword, diffuse tint fn)
def bloater(rgb):  # sickly green-yellow, swollen
    g = rgb.mean(-1, keepdims=True)
    out = rgb*0.55 + g*np.array([0.55,0.75,0.30])*0.75
    return np.clip(out*1.05, 0, 1)
def husk(rgb):     # charred, ashen grey-blue, darker
    g = rgb.mean(-1, keepdims=True)
    out = g*np.array([0.62,0.66,0.74]) * 0.75 + rgb*0.12
    return np.clip(out, 0, 1)
VARIANTS = {
  "ZombieBloater": ({"Spine":4.5, "Hips":3.5, "UpLeg":2.5, "Arm":2.0, "Neck":1.5, "Shoulder":2.0}, bloater),
  "ZombieHusk":    ({"Spine":-0.8, "Hips":-0.5, "UpLeg":-0.6, "Neck":-0.4}, husk),
}

def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC + "/ZombieGirl.fbx")

def displace(o, table):
    s = o.matrix_world.to_scale()[0]  # local units per world metre
    names = {g.index: g.name for g in o.vertex_groups}
    me = o.data
    me.calc_normals_split() if hasattr(me, "calc_normals_split") else None
    # average normal per position so split verts move together (no tearing)
    from collections import defaultdict
    acc = defaultdict(lambda: Vector())
    for v in me.vertices: acc[v.co.to_tuple(4)] += v.normal
    for v in me.vertices:
        amt = 0.0
        for g in v.groups:
            n = names[g.group]
            # ForeArm/Hand/Head excluded: "Arm" must not match "ForeArm"
            for k, cm in table.items():
                if k in n and not (k == "Arm" and "ForeArm" in n):
                    amt += g.weight * cm; break
        if amt:
            v.co += acc[v.co.to_tuple(4)].normalized() * (amt / 100.0 / s)

def tint(img_path, fn, out_path):
    im = bpy.data.images.load(img_path)
    px = np.empty(len(im.pixels), np.float32); im.pixels.foreach_get(px)
    px = px.reshape(-1, 4); px[:, :3] = fn(px[:, :3])
    out = bpy.data.images.new(os.path.basename(out_path), im.size[0], im.size[1], alpha=True)
    out.pixels.foreach_set(px.ravel()); out.filepath_raw = out_path; out.file_format = 'PNG'; out.save()
    return out

def preview(path):
    sc = bpy.context.scene
    arm = bpy.data.objects["Armature"]
    cam = bpy.data.cameras.new("c"); co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co)
    co.location = (0, -4.2, 1.0); co.rotation_euler = (1.5708, 0, 0); sc.camera = co
    l = bpy.data.lights.new("l", 'SUN'); l.energy = 4; lo = bpy.data.objects.new("l", l); sc.collection.objects.link(lo)
    lo.rotation_euler = (0.8, 0.2, 0.4)
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'TEXTURE'; sc.display.shading.light = 'STUDIO'
    sc.render.resolution_x, sc.render.resolution_y = 500, 700
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)

load(); preview(PREV + "/ZombieGirl.png")
for name, (table, fn) in VARIANTS.items():
    load()
    os.makedirs(f"{OUT}/{name}/Textures", exist_ok=True)
    body = tint(SRC + "/Textures/zombie_body_diffuse.png", fn, f"{OUT}/{name}/Textures/{name}_body_diffuse.png")
    head = tint(SRC + "/Textures/zombie_diffuse.png", fn, f"{OUT}/{name}/Textures/{name}_diffuse.png")
    for o in bpy.data.objects:
        if o.type == 'MESH':
            displace(o, table)
            o.name = o.name.replace("ZombieGirl", name); o.data.name = o.name
    for m in bpy.data.materials:  # swap diffuse for the preview only; Unity materials get their own
        if not m.use_nodes: continue
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                p = os.path.basename(n.image.filepath).lower()
                if p == "zombie_body_diffuse.png": n.image = body
                elif p == "zombie_diffuse.png": n.image = head
    preview(f"{PREV}/{name}.png")
    bpy.ops.export_scene.fbx(filepath=f"{OUT}/{name}/{name}.fbx", object_types={'ARMATURE','MESH'},
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    print("EXPORTED", name)
