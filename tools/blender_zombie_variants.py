# Builds zombie body-type variants from ZombieGirl on the SAME skeleton (bind poses unchanged),
# so Unity prefab variants can swap meshes onto the existing rig/ragdoll.
# Mesh is reshaped radially around each bone; the head is scaled about its centre.
# Also writes uv_labels.json (UV triangles per region) for tools/zombie_textures.py.
# Run: blender -b --python tools/blender_zombie_variants.py -- <out dir for uv_labels.json>
import bpy, json, os, sys
from mathutils import Vector

SRC = r"C:/Users/jackp/github/css-gamejam2026/Unity/Assets/Art/Zombies/ZombieGirl/ZombieGirl.fbx"
OUT = r"C:/Users/jackp/github/css-gamejam2026/Unity/Assets/Art/Zombies"
TMP = sys.argv[sys.argv.index("--") + 1]

# region -> (side x, front -y, back +y, vertical z) thickness multipliers; a float means uniform.
VARIANTS = {
    "ZombieBloater": dict(head=0.9, Hips=(1.45, 1.9, 1.35, 1), Spine=(1.5, 2.3, 1.35, 1), Spine1=(1.5, 2.1, 1.35, 1),
                          Spine2=(1.35, 1.5, 1.3, 1), Neck=1.5, Shoulder=1.3, Arm=1.55, ForeArm=1.35, Hand=1.15,
                          UpLeg=1.5, Leg=1.3, Foot=1.1),
    "ZombieHusk": dict(head=1.06, Hips=0.8, Spine=(0.7, 0.55, 0.85, 1), Spine1=(0.72, 0.6, 0.85, 1), Spine2=(0.85, 0.85, 0.9, 1),
                       Neck=0.65, Shoulder=0.85, Arm=0.6, ForeArm=0.6, Hand=0.85, UpLeg=0.62, Leg=0.6, Foot=0.9),
    "ZombieBrute": dict(head=0.82, Hips=1.2, Spine=(1.25, 1.2, 1.2, 1), Spine1=(1.4, 1.3, 1.3, 1), Spine2=(1.6, 1.45, 1.5, 1),
                        Neck=1.7, Shoulder=1.7, Arm=1.9, ForeArm=1.7, Hand=1.45, UpLeg=1.35, Leg=1.3, Foot=1.15),
    "ZombieStalker": dict(head=1.15, Hips=0.85, Spine=(0.8, 0.7, 0.9, 1), Spine1=(0.82, 0.75, 0.9, 1), Spine2=0.88,
                          Neck=0.75, Shoulder=0.9, Arm=0.75, ForeArm=0.72, Hand=1.3, UpLeg=0.8, Leg=0.75, Foot=1.05),
}

def region(bone):
    n = bone.replace("mixamorig:", "").replace("Left", "").replace("Right", "")
    if n.startswith("Hand"): return "Hand"          # fingers follow the hand
    if n.startswith("Toe"): return "Foot"
    if n.startswith("Head") or n.endswith("Eye"): return "Head"
    return n

def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    arm = bpy.data.objects["Armature"]
    segs = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}
    return arm, segs

def deform(o, segs, spec):
    M = o.matrix_world; Mi = M.inverted()
    names = {g.index: g.name for g in o.vertex_groups}
    hb = segs["mixamorig:Head"]; centre = hb[0] + Vector((0, 0, 0.08))
    for v in o.data.vertices:
        p = M @ v.co; acc = Vector(); ws = 0.0
        for g in v.groups:
            if g.weight <= 0 or names[g.group] not in segs: continue
            r = region(names[g.group])
            if r == "Head":
                t = centre + (p - centre) * spec["head"]
            else:
                f = spec.get(r, 1.0)
                sx, sf, sb, sz = (f, f, f, f) if isinstance(f, float) else f
                a, b = segs[names[g.group]]; d = b - a; L = d.length
                d = d / L if L > 1e-6 else Vector((0, 0, 1))
                c = a + d * max(0.0, min(L, (p - a).dot(d)))
                rv = p - c; par = d * rv.dot(d); perp = rv - par
                perp = Vector((perp.x * sx, perp.y * (sf if perp.y < 0 else sb), perp.z * sz))
                t = c + par + perp
            acc += t * g.weight; ws += g.weight
        if ws: v.co = Mi @ (acc / ws)

def dump_uv_labels(path):
    tris = []
    for o in bpy.data.objects:
        if o.type != 'MESH': continue
        names = {g.index: g.name for g in o.vertex_groups}
        uv = o.data.uv_layers.active.data
        for p in o.data.polygons:
            if o.name.endswith("Top"): lab = "shirt"
            elif o.name.endswith("Pants"): lab = "pants"
            else:
                head = sum(g.weight for vi in p.vertices for g in o.data.vertices[vi].groups
                           if region(names[g.group]) in ("Head", "Neck")) / len(p.vertices)
                lab = "head" if head > 0.5 else "skin"
            tris.append([lab, [list(uv[li].uv) for li in p.loop_indices]])
    json.dump(tris, open(path, "w"))

arm, segs = load()
dump_uv_labels(os.path.join(TMP, "uv_labels.json"))
for name, spec in VARIANTS.items():
    arm, segs = load()
    for o in list(bpy.data.objects):
        if o.type == 'MESH':
            deform(o, segs, spec)
            o.name = o.name.replace("ZombieGirl", name); o.data.name = o.name
    os.makedirs(f"{OUT}/{name}", exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=f"{OUT}/{name}/{name}.fbx", object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    print("EXPORTED", name)
