"""Tiny procedural-modelling kit for the BLINK mansion assets (Blender 4.5, bpy/bmesh).

Convention: Blender units = metres, Z up, asset FRONT faces -Y (Blender front view).
Exported with forward -Z / up Y + baked space transform, so in Unity the front faces +Z.
Every helper returns a mesh object whose geometry is already in world space (object at origin);
`xf` moves/rotates the mesh data, `join` merges parts, `finish` sets normals + box UVs.
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

# name: (preview colour sRGB, texture family, tint sRGB (multiplies the family albedo), smoothness multiplier, alpha)
# Unity mirrors these values in Materials/*.mat; metallic comes from the family mask (R).
MATERIALS = {
    "Wood_Dark": ((0.16, 0.085, 0.05), "Wood", (0.34, 0.2, 0.13), 0.8, 1),
    "Wood_Light": ((0.42, 0.28, 0.16), "Wood", (0.66, 0.47, 0.3), 0.75, 1),
    "Brass": ((0.78, 0.6, 0.3), "Metal", (0.95, 0.76, 0.42), 1.0, 1),
    "Iron": ((0.11, 0.11, 0.115), "Metal", (0.24, 0.23, 0.23), 0.7, 1),
    "Silver": ((0.62, 0.62, 0.6), "Metal", (0.86, 0.86, 0.83), 1.0, 1),
    "Fabric_Red": ((0.42, 0.06, 0.06), "Fabric", (0.6, 0.1, 0.1), 1.0, 1),
    "Fabric_Linen": ((0.78, 0.74, 0.64), "Fabric", (1.0, 0.96, 0.85), 1.0, 1),
    "Wax": ((0.92, 0.88, 0.74), "Smooth", (0.98, 0.94, 0.8), 0.5, 1),
    "Glass": ((0.65, 0.75, 0.75), None, (0.7, 0.78, 0.78), 0.92, 0.25),
    "Stone": ((0.5, 0.48, 0.45), "Stone", (0.76, 0.73, 0.69), 1.0, 1),
    "Paper": ((0.86, 0.8, 0.66), "Smooth", (0.93, 0.87, 0.72), 0.15, 1),
    "Ceramic": ((0.92, 0.91, 0.86), "Smooth", (0.98, 0.97, 0.93), 0.85, 1),
    "Leather_Brown": ((0.3, 0.15, 0.08), "Leather", (0.45, 0.25, 0.14), 1.0, 1),
    "Leather_Green": ((0.1, 0.2, 0.12), "Leather", (0.18, 0.32, 0.2), 1.0, 1),
    # Bungalow grey-box: flat colours by surface type (no texture family), replaced by the scan materials later
    "GB_Floor": ((0.42, 0.33, 0.25), None, (0.42, 0.33, 0.25), 0.2, 1),
    "GB_FloorTile": ((0.58, 0.58, 0.53), None, (0.58, 0.58, 0.53), 0.2, 1),
    "GB_Wall": ((0.76, 0.74, 0.69), None, (0.76, 0.74, 0.69), 0.1, 1),
    "GB_Ceiling": ((0.88, 0.88, 0.86), None, (0.88, 0.88, 0.86), 0.1, 1),
    "GB_Trim": ((0.24, 0.15, 0.09), None, (0.24, 0.15, 0.09), 0.3, 1),
    "GB_Stair": ((0.5, 0.38, 0.27), None, (0.5, 0.38, 0.27), 0.2, 1),
    "GB_Basement": ((0.55, 0.56, 0.57), None, (0.55, 0.56, 0.57), 0.1, 1),
    "GB_Brick": ((0.86, 0.84, 0.8), None, (0.86, 0.84, 0.8), 0.1, 1),
    "GB_Glass": ((0.55, 0.7, 0.8), None, (0.55, 0.7, 0.8), 0.8, 1),
    "GB_Gate": ((0.2, 0.5, 0.25), None, (0.2, 0.5, 0.25), 0.3, 1),
    "GB_Ground": ((0.24, 0.27, 0.21), None, (0.24, 0.27, 0.21), 0.1, 1),
}
TEX_DIR = None  # set by build_all to the Textures folder

rng = random.Random(7)


def seed(s):
    rng.seed(s)


def _lin(c):
    return tuple(x ** 2.2 for x in c)


def _img(path, srgb):
    im = bpy.data.images.load(path, check_existing=True)
    im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    return im


def mat(name):
    """Shared node material: family albedo x tint, mask (R metal, G AO, A smooth), normal map.
    Also holds an unlinked 'MaskEmit' emission (R metal, G AO, B smooth) used when baking mask maps."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    col, fam, tint, smul, alpha = MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*col, alpha)
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    bsdf = N["Principled BSDF"]
    out = N["Material Output"]
    emit = N.new("ShaderNodeEmission")
    emit.name = "MaskEmit"
    comb = N.new("ShaderNodeCombineColor")
    L.new(comb.outputs[0], emit.inputs["Color"])
    aemit = N.new("ShaderNodeEmission")
    aemit.name = "AlbedoEmit"
    aemit.inputs["Color"].default_value = (*_lin(tint), 1)
    if fam and TEX_DIR:
        alb = N.new("ShaderNodeTexImage")
        alb.image = _img(os.path.join(TEX_DIR, fam + "_Albedo.png"), True)
        msk = N.new("ShaderNodeTexImage")
        msk.image = _img(os.path.join(TEX_DIR, fam + "_Mask.png"), False)
        nrm = N.new("ShaderNodeTexImage")
        nrm.image = _img(os.path.join(TEX_DIR, fam + "_Normal.png"), False)
        mix = N.new("ShaderNodeMix")
        mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.inputs[0].default_value = 1.0
        L.new(alb.outputs["Color"], mix.inputs[6])
        mix.inputs[7].default_value = (*_lin(tint), 1)
        L.new(mix.outputs[2], bsdf.inputs["Base Color"])
        L.new(mix.outputs[2], aemit.inputs["Color"])
        sep = N.new("ShaderNodeSeparateColor")
        L.new(msk.outputs["Color"], sep.inputs[0])
        L.new(sep.outputs[0], bsdf.inputs["Metallic"])
        sm = N.new("ShaderNodeMath")
        sm.operation = 'MULTIPLY'
        sm.inputs[1].default_value = smul
        L.new(msk.outputs["Alpha"], sm.inputs[0])
        rough = N.new("ShaderNodeMath")
        rough.operation = 'SUBTRACT'
        rough.inputs[0].default_value = 1.0
        L.new(sm.outputs[0], rough.inputs[1])
        L.new(rough.outputs[0], bsdf.inputs["Roughness"])
        nmap = N.new("ShaderNodeNormalMap")
        L.new(nrm.outputs["Color"], nmap.inputs["Color"])
        L.new(nmap.outputs[0], bsdf.inputs["Normal"])
        L.new(sep.outputs[0], comb.inputs[0])
        L.new(sep.outputs[1], comb.inputs[1])
        L.new(sm.outputs[0], comb.inputs[2])
    else:
        bsdf.inputs["Base Color"].default_value = (*_lin(tint), 1)
        bsdf.inputs["Roughness"].default_value = 1 - smul
        bsdf.inputs["Metallic"].default_value = 0.0
        comb.inputs[1].default_value = 1.0
        comb.inputs[2].default_value = smul
    if name == "Glass":
        bsdf.inputs["Alpha"].default_value = alpha
    if name == "Wax":
        bsdf.inputs["Subsurface Weight"].default_value = 0.3
    L.new(bsdf.outputs[0], out.inputs["Surface"])
    return m


def bake_mode(kind=None):
    """Route every shared material's output to MaskEmit / AlbedoEmit for EMIT bakes, or back to the BSDF (None).
    (Cycles' DIFFUSE colour pass is black on metals, so albedo is baked as emission.)"""
    node = {"mask": "MaskEmit", "albedo": "AlbedoEmit", None: "Principled BSDF"}[kind]
    for m in bpy.data.materials:
        if m.use_nodes and "MaskEmit" in m.node_tree.nodes:
            nt = m.node_tree
            src = nt.nodes[node]
            nt.links.new(src.outputs[0], nt.nodes["Material Output"].inputs["Surface"])


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def _uv_box(bm):
    """Cube projection in the part's own frame, 1 tile per metre; V follows the part's longer in-plane axis
    (so wood grain runs along legs, rails and boards). Random offset per part."""
    uv = bm.loops.layers.uv.verify()
    if not bm.verts:
        return
    lo = [min(v.co[i] for v in bm.verts) for i in range(3)]
    ext = [max(v.co[i] for v in bm.verts) - lo[i] for i in range(3)]
    ou, ov = rng.random(), rng.random()
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [i for i in range(3) if i != ax]
        if ext[a] > ext[b]:
            a, b = b, a
        for lp in f.loops:
            c = lp.vert.co
            lp[uv].uv = (c[a] + ou, c[b] + ov)


def _uv_cyl(bm):
    """Cylindrical UVs around Z for lathed parts: U around (metres of circumference), V up."""
    uv = bm.loops.layers.uv.verify()
    rs = [math.hypot(v.co.x, v.co.y) for v in bm.verts]
    R = max(sum(rs) / max(len(rs), 1), 0.002)
    ov = rng.random()
    for f in bm.faces:
        cen = f.calc_center_median()
        us = []
        for lp in f.loops:
            c = lp.vert.co
            if math.hypot(c.x, c.y) < 1e-6:
                c = cen
            us.append((math.atan2(c.y, c.x) / (2 * math.pi)) % 1.0)
        if max(us) - min(us) > 0.5:
            us = [u + 1 if u < 0.5 else u for u in us]
        for lp, u in zip(f.loops, us):
            lp[uv].uv = (u * 2 * math.pi * R, lp.vert.co.z + ov)


def _obj(name, bm, m, uv='box'):
    if uv == 'box':
        _uv_box(bm)
    elif uv == 'cyl':
        _uv_cyl(bm)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat(m))
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def xf(o, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Transform mesh data (rot in degrees, XYZ euler). Returns o for chaining."""
    r = Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
    o.data.transform(Matrix.Translation(loc) @ r @ Matrix.Diagonal((*scale, 1)))
    return o


def jitter(o, amt):
    if amt:
        for v in o.data.vertices:
            v.co += Vector((rng.uniform(-amt, amt) for _ in range(3)))
    return o


def box(name, size, m, loc=(0, 0, 0), bev=0.006, seg=1, jit=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bev > 0:
        bev = min(bev, min(size) * 0.45)
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev, segments=seg, profile=0.5,
                        affect='EDGES', clamp_overlap=True)
    return jitter(xf(_obj(name, bm, m), loc), jit)


def box_z(name, x0x1, y0y1, z0z1, m, **kw):
    """Box from min/max ranges (easier for furniture layout)."""
    (x0, x1), (y0, y1), (z0, z1) = x0x1, y0y1, z0z1
    return box(name, (x1 - x0, y1 - y0, z1 - z0), m, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), **kw)


def lathe(name, prof, m, seg=16, closed=False, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Revolve a (radius, z) profile around Z. r=0 points become poles. closed=True joins last->first."""
    bm = bmesh.new()
    angs = [2 * math.pi * i / seg for i in range(seg)]
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for a in angs])
    pairs = list(zip(rings, rings[1:]))
    if closed:
        pairs.append((rings[-1], rings[0]))
    for A, B in pairs:
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
    return xf(_obj(name, bm, m, uv='cyl'), loc, rot, scale)


def cyl(name, r, h, m, loc=(0, 0, 0), rot=(0, 0, 0), seg=12, r2=None, bev=0.0):
    """Cylinder standing on loc (base centre) along +Z before rot."""
    r2 = r if r2 is None else r2
    if bev:
        prof = [(0, 0), (r - bev, 0), (r, bev), (r2, h - bev), (r2 - bev, h), (0, h)]
    else:
        prof = [(0, 0), (r, 0), (r2, h), (0, h)]
    o = lathe(name, prof, m, seg)
    return xf(o, loc, rot)


def torus(name, R, r, m, loc=(0, 0, 0), rot=(0, 0, 0), seg=16, rseg=6, scale=(1, 1, 1)):
    prof = [(R + r * math.cos(2 * math.pi * i / rseg), r * math.sin(2 * math.pi * i / rseg)) for i in range(rseg)]
    o = lathe(name, prof, m, seg, closed=True)
    return xf(o, loc, rot, scale)


def tube(name, pts, radius, m, res=6, bevres=2, taper=None):
    """Smooth bezier tube through pts (auto handles), converted to mesh."""
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.resolution_u = res
    cu.bevel_depth = radius
    cu.bevel_resolution = bevres
    cu.use_fill_caps = True
    sp = cu.splines.new('BEZIER')
    sp.bezier_points.add(len(pts) - 1)
    for bp, p in zip(sp.bezier_points, pts):
        bp.co = p
        bp.handle_left_type = bp.handle_right_type = 'AUTO'
    if taper:
        for bp, t in zip(sp.bezier_points, taper):
            bp.radius = t
    co = bpy.data.objects.new(name + "_crv", cu)
    bpy.context.scene.collection.objects.link(co)
    return to_mesh(co, name)


def to_mesh(o, name=None):
    """Bake modifiers/curve into a plain mesh object (replaces o)."""
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    mats = [s.material for s in o.material_slots] if o.type == 'MESH' else []
    n = name or o.name
    bpy.data.objects.remove(o, do_unlink=True)
    new = bpy.data.objects.new(n, me)
    bpy.context.scene.collection.objects.link(new)
    if not me.materials and mats:
        for mm in mats:
            me.materials.append(mm)
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    bm = bmesh.new()
    bm.from_mesh(me)
    _uv_box(bm)
    bm.to_mesh(me)
    bm.free()
    return new


def set_mat(o, m):
    o.data.materials.clear()
    o.data.materials.append(mat(m))
    return o


def extrude_poly(name, pts, thick, m, plane='XZ', bev=0.0):
    """Flat 2D outline (u, v) extruded by thick. plane 'XZ' -> thickness along Y, 'XY' -> along Z."""
    bm = bmesh.new()
    if plane == 'XZ':
        vs = [bm.verts.new((u, -thick / 2, v)) for u, v in pts]
        d = Vector((0, thick, 0))
    else:
        vs = [bm.verts.new((u, v, -thick / 2)) for u, v in pts]
        d = Vector((0, 0, thick))
    f = bm.faces.new(vs)
    res = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, verts=[e for e in res['geom'] if isinstance(e, bmesh.types.BMVert)], vec=d)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bev > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev, segments=1, affect='EDGES', clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], ngon_method='BEAUTY')
    return _obj(name, bm, m)


def sweep_x(name, prof, x0, x1, m):
    """Extrude a closed (y, z) profile along X from x0 to x1 (mouldings, handrails)."""
    bm = bmesh.new()
    a = [bm.verts.new((x0, y, z)) for y, z in prof]
    b = [bm.verts.new((x1, y, z)) for y, z in prof]
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(a)
    bm.faces.new(b)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], ngon_method='BEAUTY')
    return _obj(name, bm, m)


def rounded_box(name, size, radius, m, loc=(0, 0, 0), cuts=6, bulge=0.0, jit=0.0):
    """Soft cushion: subdivided cube pushed onto a rounded box; bulge puffs the big faces."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=cuts, use_grid_fill=True)
    h = Vector(size) / 2
    r = min(radius, min(h) * 0.98)
    for v in bm.verts:
        p = Vector((v.co.x * h.x, v.co.y * h.y, v.co.z * h.z))
        inner = Vector((max(-(h[i] - r), min(h[i] - r, p[i])) for i in range(3)))
        d = p - inner
        q = inner + (d.normalized() * r if d.length > 1e-9 else d)
        if bulge:
            # puff the middle of each face outwards
            u = Vector((v.co.x, v.co.y, v.co.z))
            for i in range(3):
                others = [u[j] for j in range(3) if j != i]
                f = (1 - others[0] ** 2) * (1 - others[1] ** 2)
                q[i] += math.copysign(bulge * f * abs(u[i]) ** 4, u[i])
        v.co = q
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return jitter(xf(_obj(name, bm, m), loc), jit)


def grid(name, sx, sy, nx, ny, m):
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=nx, y_segments=ny, size=0.5)
    for v in bm.verts:
        v.co.x *= sx
        v.co.y *= sy
    return _obj(name, bm, m)


def solidify(o, t, offset=-1):
    md = o.modifiers.new("s", 'SOLIDIFY')
    md.thickness = t
    md.offset = offset
    return to_mesh(o)


def mirror_copy(o, axis=0, name=None):
    """Duplicate o mirrored across the given axis plane through origin."""
    me = o.data.copy()
    s = [1, 1, 1]
    s[axis] = -1
    me.transform(Matrix.Diagonal((*s, 1)))
    me.flip_normals()
    c = bpy.data.objects.new(name or o.name + "_m", me)
    bpy.context.scene.collection.objects.link(c)
    return c


def dup(o, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), name=None):
    c = bpy.data.objects.new(name or o.name + "_d", o.data.copy())
    bpy.context.scene.collection.objects.link(c)
    return xf(c, loc, rot, scale)


def radial(o, n, start=0.0):
    """o plus n-1 copies rotated about Z. Returns list."""
    out = [o]
    for i in range(1, n):
        out.append(dup(o, rot=(0, 0, start + 360.0 * i / n)))
    return out


def join(objs, name):
    objs = [o for o in objs if o is not None]
    if len(objs) > 1:
        with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs,
                                       selected_objects=objs):
            bpy.ops.object.join()
    o = objs[0]
    o.name = name
    o.data.name = name
    return o


def finish(o, sharp_deg=50.0):
    """Smooth shading with sharp edges above angle, merge tiny doubles (UVs are set per part)."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    thr = math.radians(sharp_deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) != 2 or e.calc_face_angle(0) > thr:
            e.smooth = False
    bm.to_mesh(o.data)
    bm.free()
    return o


def set_origin(o, p):
    """Move object origin to world point p without moving geometry."""
    p = Vector(p)
    o.data.transform(Matrix.Translation(-p))
    o.location = p
    return o


def child(c, parent):
    c.parent = parent
    c.matrix_parent_inverse = parent.matrix_world.inverted()
    return c


def empty(name, loc, parent):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    e.empty_display_size = 0.03
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    return child(e, parent)


def uv_lightmap(o, margin=0.02, name="UVLightmap"):
    """Second UV layer for Unity's baked lightmaps (Unity reads it as UV2; set the importer's Generate Lightmap
    UVs off). Smart UV islands (coplanar faces stay together), packed with a margin, never overlapping.
    The first UV layer stays active for render/export order."""
    me = o.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    if name in me.uv_layers:
        me.uv_layers.remove(me.uv_layers[name])
    uv0 = me.uv_layers[0]
    lm = me.uv_layers.new(name=name)
    me.uv_layers.active = lm
    for ob in bpy.context.view_layer.objects:
        ob.select_set(ob is o)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    me.uv_layers.active = uv0
    uv0.active_render = True
    return o


def tri_count():
    n = 0
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            n += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return n


def bounds():
    bpy.context.view_layer.update()
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            for v in o.data.vertices:
                w = o.matrix_world @ v.co
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
    return lo, hi
