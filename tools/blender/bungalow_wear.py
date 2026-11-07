"""BLINK Bungalow kit wear (Phase 3): the shared UV2 atlas and the 1K kit wear bake.

UV2 (Blender's second UV layer "UVLightmap", Unity mesh.uv2 / TEXCOORD1) is ONE non-overlapping layout for every kit
mesh in the shell, used both by Unity's lightmapper and by the wear atlas:
  - each object is Smart-UV-unwrapped on its own, then placed as one compact rectangle in the 1K atlas, so a mesh's
    UV2 bounds are tight (good for lightmap packing) and no two meshes overlap;
  - texel density is weighted: trim / detail pieces get WEIGHT_TRIM x the area share of walls, floors and ceilings;
  - islands keep >= MARGIN_PX between them, rectangles are snapped to the pixel grid.

KitWear.png (1024 x 1024 RGBA, linear) channel contract, read by the kit Shader Graph on UV2:
  R = baked ambient occlusion (Cycles AO, 0.35 m)           1 = open, 0 = occluded
  G = edge wear (convex edges: bevel-normal vs face normal, gated by local AO, broken up by noise) + foot-traffic
      wear on up-facing surfaces away from walls (worn paths on floors, tread middles)
  B = dirt (cavity x upward-facing, grime along the skirting / corners of floors, a kick band 0.5 m up vertical faces)
  A = polish (nosings, handrail tops, door latch stiles / knobs, thresholds; from the kit's "polish" face attribute)
Texels outside every island: R = 1, G = B = A = 0.
"""
import math
import os

import bpy
import numpy as np

RES = 1024
MARGIN_PX = 3
WEIGHT_TRIM = 4.0
LAYER = "UVLightmap"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Environment", "Bungalow", "Textures",
                                   "KitWear", "KitWear.png"))
STRUCTURE = ("Wall_", "Floor_", "Ceiling_", "Fireplace_Front", "Fireplace_Back")
PLANK_FLOORS = ("Floor_CentralHall", "Floor_EntranceHall", "Floor_Bedroom")


def weight(o):
    n = o.name
    if n == "Ext_Ground":
        return 0.04
    if n.startswith("Ext_") or n.endswith("_Outside"):
        return 0.15
    if n.startswith("Joists_"):
        return 1.0
    if n in PLANK_FLOORS:          # the hero surface: boards, chips, sag
        return 2.5
    if n.startswith("Ceiling_"):   # flat plaster seen from below: AO in the corners only
        return 0.4
    if n.startswith(("Basement_Water",)) or n.endswith("_Glass"):
        return 0.3
    if n.startswith(STRUCTURE):
        return 1.0
    return WEIGHT_TRIM


def _area(o):
    return sum(p.area for p in o.data.polygons)


def _project(o, margin):
    me = o.data
    if LAYER in me.uv_layers:
        me.uv_layers.remove(me.uv_layers[LAYER])
    uv0 = me.uv_layers[0]
    lm = me.uv_layers.new(name=LAYER)
    me.uv_layers.active = lm
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    me.uv_layers.active = uv0
    uv0.active_render = True


def _uvs(o):
    d = o.data.uv_layers[LAYER].data
    a = np.empty(len(d) * 2, np.float32)
    d.foreach_get("uv", a)
    return a.reshape(-1, 2)


def _shelf(rects, gap):
    """Shelf-pack [(w, h)] into the unit square. Returns origins or None if it doesn't fit."""
    order = sorted(range(len(rects)), key=lambda i: -rects[i][1])
    out = [None] * len(rects)
    x = y = shelf_h = 0.0
    for i in order:
        w, h = rects[i]
        if w > 1.0:
            return None
        if x + w > 1.0 + 1e-9:
            y += shelf_h + gap
            x, shelf_h = 0.0, 0.0
        if y + h > 1.0 + 1e-9:
            return None
        out[i] = (x, y)
        x += w + gap
        shelf_h = max(shelf_h, h)
    return out


def pack_uv2(objs, res=RES, margin_px=MARGIN_PX):
    """One non-overlapping UV2 layout across objs (see module doc). Returns {name: (x, y, w, h)} in pixels."""
    objs = [o for o in objs if o.type == 'MESH' and len(o.data.polygons)]
    wa = {o.name: weight(o) * _area(o) for o in objs}
    tot = sum(wa.values())
    for n, v in sorted(wa.items(), key=lambda kv: -kv[1])[:30]:
        print(f"   UV2 weight x area {n:28s} {v:8.1f}")
    side = {n: math.sqrt(v / tot * 0.55) for n, v in wa.items()}    # first guess of each square's side (UV)
    for o in objs:
        px = max(side[o.name] * res, 8.0)
        _project(o, min(0.08, margin_px / px))
    boxes = {}
    for o in objs:
        uv = _uvs(o)
        lo, hi = uv.min(0), uv.max(0)
        boxes[o.name] = (lo, np.maximum(hi - lo, 1e-4))
    gap = (margin_px + 1) / res

    def rects(g):
        return [(math.ceil(boxes[o.name][1][0] * side[o.name] * g * res + 1) / res,
                 math.ceil(boxes[o.name][1][1] * side[o.name] * g * res + 1) / res) for o in objs]

    lo_g, hi_g = 0.2, 3.0
    best = None
    for _ in range(22):
        g = (lo_g + hi_g) / 2
        pos = _shelf(rects(g), gap)
        if pos:
            lo_g, best = g, (g, pos)
        else:
            hi_g = g
    g, pos = best
    out = {}
    for o, (x, y) in zip(objs, pos):
        lo, ext = boxes[o.name]
        s = side[o.name] * g
        x0, y0 = math.ceil(x * res) / res, math.ceil(y * res) / res
        uv = _uvs(o)
        uv = (uv - lo) * s + np.array([x0, y0], np.float32)
        o.data.uv_layers[LAYER].data.foreach_set("uv", uv.astype(np.float32).ravel())
        out[o.name] = (round(x0 * res), round(y0 * res), round(ext[0] * s * res), round(ext[1] * s * res))
    used = sum(w * h for _, _, w, h in out.values()) / res / res
    print(f"UV2 atlas: {len(objs)} meshes, scale {g:.3f}, rect fill {used:.0%}, "
          f"structure {res * math.sqrt(0.55 / tot) * g:.1f} px/m, trim {res * math.sqrt(0.55 * WEIGHT_TRIM / tot) * g:.1f}"
          f" px/m")
    return out


# ------------------------------------------------------------------ bake
def _gpu(sc):
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for kind in ("OPTIX", "CUDA", "HIP", "ONEAPI"):
            try:
                prefs.compute_device_type = kind
            except TypeError:
                continue
            prefs.get_devices()
            devs = [d for d in prefs.devices if d.type == kind]
            if devs:
                for d in prefs.devices:
                    d.use = d.type == kind
                sc.cycles.device = 'GPU'
                print("bake device", kind, [d.name for d in devs])
                return
    except Exception as e:      # noqa: BLE001  (no GPU backend: CPU is fine)
        print("GPU setup failed:", e)
    sc.cycles.device = 'CPU'
    print("bake device CPU")


def _nodes_for(m):
    """Add (once) the wear-pass emission network to a kit material. Returns the dict of pass sockets."""
    nt = m.node_tree
    N, Lk = nt.nodes, nt.links
    if "Wear_EDGE" in N:
        return {k: N["Wear_" + k] for k in ("EDGE", "DIRT", "POLISH")}

    def node(t, name=None, **kw):
        n = N.new(t)
        if name:
            n.name = name
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def math_(op, a, b=None, c=None, clamp=False):
        n = node("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                Lk.new(v, n.inputs[i])
        return n.outputs[0]

    geo = node("ShaderNodeNewGeometry")
    pos = geo.outputs["Position"]
    noise = node("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 9.0
    noise.inputs["Detail"].default_value = 6.0
    Lk.new(pos, noise.inputs["Vector"])
    nz = math_('MULTIPLY_ADD', noise.outputs["Fac"], 2.2, -0.65)          # ~ -0.3..1.3 broken mask
    nz = math_('MAXIMUM', nz, 0.0)
    # edge wear: bevel normal vs true normal, only on convex edges (local AO stays high)
    bev = node("ShaderNodeBevel")
    bev.inputs["Radius"].default_value = 0.03      # wide enough to survive ~2-4 cm atlas texels
    bev.samples = 8
    dot = node("ShaderNodeVectorMath", operation='DOT_PRODUCT')
    Lk.new(bev.outputs[0], dot.inputs[0])
    Lk.new(geo.outputs["True Normal"], dot.inputs[1])
    edge = math_('MULTIPLY', math_('SUBTRACT', 1.0, dot.outputs["Value"]), 9.0, clamp=True)
    aol = node("ShaderNodeAmbientOcclusion", only_local=True, samples=12)
    aol.inputs["Distance"].default_value = 0.04
    convex = math_('MULTIPLY_ADD', aol.outputs["AO"], 3.0, -2.0)
    convex = math_('MAXIMUM', convex, 0.0)
    edge = math_('MULTIPLY', math_('MULTIPLY', edge, convex), math_('MULTIPLY_ADD', nz, 0.7, 0.3), clamp=True)
    e_emit = node("ShaderNodeEmission", "Wear_EDGE")
    # foot traffic: up-facing surfaces away from walls (large-radius AO stays open) wear through, in broad patches
    sepn = node("ShaderNodeSeparateXYZ")
    Lk.new(geo.outputs["Normal"], sepn.inputs[0])
    upf = math_('MULTIPLY_ADD', sepn.outputs["Z"], 5.0, -4.0)            # only near-horizontal tops
    upf = math_('MAXIMUM', upf, 0.0)
    aot = node("ShaderNodeAmbientOcclusion", only_local=False, samples=12)
    aot.inputs["Distance"].default_value = 0.7
    open_ = math_('MULTIPLY_ADD', aot.outputs["AO"], 2.5, -1.5)
    open_ = math_('MAXIMUM', open_, 0.0)
    path = node("ShaderNodeTexNoise")
    path.inputs["Scale"].default_value = 0.9
    path.inputs["Detail"].default_value = 2.0
    Lk.new(geo.outputs["Position"], path.inputs["Vector"])
    pth = math_('MULTIPLY_ADD', path.outputs["Fac"], 2.0, -0.55)
    pth = math_('MAXIMUM', pth, 0.0)
    traffic = math_('MULTIPLY', math_('MULTIPLY', upf, open_), math_('MULTIPLY', pth, 0.6))
    edge = math_('ADD', edge, math_('MULTIPLY', traffic, math_('MULTIPLY_ADD', nz, 0.5, 0.5)), clamp=True)
    Lk.new(edge, e_emit.inputs["Color"])
    # dirt: cavity x upward-facing + grime band where walls meet the floors (ground y 0, basement y -2.85)
    aod = node("ShaderNodeAmbientOcclusion", only_local=False, samples=16)
    aod.inputs["Distance"].default_value = 0.25
    cav = math_('SUBTRACT', 1.0, aod.outputs["AO"])
    cav = math_('MULTIPLY', cav, 1.6, clamp=True)
    sep = node("ShaderNodeSeparateXYZ")
    Lk.new(geo.outputs["Normal"], sep.inputs[0])
    up = math_('MAXIMUM', sep.outputs["Z"], 0.0)
    psep = node("ShaderNodeSeparateXYZ")
    Lk.new(pos, psep.inputs[0])
    z = psep.outputs["Z"]

    def band(z0):
        rel = math_('SUBTRACT', z, z0)
        ramp = math_('SUBTRACT', 1.0, math_('MULTIPLY', rel, 1.0 / 0.5), clamp=True)    # kick / mop band
        return math_('MULTIPLY', ramp, math_('GREATER_THAN', rel, -0.02))
    floor_band = math_('MAXIMUM', band(0.0), band(-2.85))
    side = math_('SUBTRACT', 1.0, up, clamp=True)
    dirt = math_('ADD', math_('MULTIPLY', cav, math_('MULTIPLY_ADD', up, 0.7, 0.3)),
                 math_('MULTIPLY', math_('MULTIPLY', floor_band, side), 0.85))
    # floors gather grime along the skirting and in the corners (large-radius AO, upward faces only)
    aow = node("ShaderNodeAmbientOcclusion", only_local=False, samples=16)
    aow.inputs["Distance"].default_value = 0.7
    edge_room = math_('MULTIPLY', math_('SUBTRACT', 1.0, aow.outputs["AO"]), 1.4, clamp=True)
    dirt = math_('ADD', dirt, math_('MULTIPLY', edge_room, up))
    big = node("ShaderNodeTexNoise")
    big.inputs["Scale"].default_value = 1.6
    big.inputs["Detail"].default_value = 3.0
    Lk.new(pos, big.inputs["Vector"])
    dirt = math_('MULTIPLY', dirt, math_('MULTIPLY_ADD', big.outputs["Fac"], 1.2, 0.2), clamp=True)
    d_emit = node("ShaderNodeEmission", "Wear_DIRT")
    Lk.new(dirt, d_emit.inputs["Color"])
    # polish from the kit's face attribute
    att = node("ShaderNodeAttribute", attribute_name="polish")
    pol = math_('MULTIPLY', att.outputs["Fac"], math_('MULTIPLY_ADD', noise.outputs["Fac"], 0.5, 0.75), clamp=True)
    p_emit = node("ShaderNodeEmission", "Wear_POLISH")
    Lk.new(pol, p_emit.inputs["Color"])
    return {"EDGE": e_emit, "DIRT": d_emit, "POLISH": p_emit}


def _route(mats, key):
    for m in mats:
        nt = m.node_tree
        out = nt.nodes["Material Output"]
        src = nt.nodes["Wear_" + key] if key else nt.nodes["Principled BSDF"]
        nt.links.new(src.outputs[0], out.inputs["Surface"])


def bake(objs, out_path=OUT, res=RES, ao_samples=96):
    """Bake AO / edge / dirt / polish for every mesh on its UV2 into one RGBA atlas (module doc)."""
    sc = bpy.context.scene
    objs = [o for o in objs if o.type == 'MESH' and len(o.data.polygons)]
    sc.render.engine = 'CYCLES'
    _gpu(sc)
    if sc.world is None:
        sc.world = bpy.data.worlds.new("BakeWorld")
    sc.world.light_settings.distance = 0.35
    mats = sorted({s.material for o in objs for s in o.material_slots if s.material}, key=lambda m: m.name)
    imgs = {}
    for key, fill in (("AO", 1.0), ("EDGE", 0.0), ("DIRT", 0.0), ("POLISH", 0.0)):
        im = bpy.data.images.get("KitWear_" + key) or bpy.data.images.new("KitWear_" + key, res, res, alpha=False,
                                                                           float_buffer=True)
        im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(np.full(res * res * 4, fill, np.float32))
        imgs[key] = im
    tex_nodes = []
    for m in mats:
        _nodes_for(m)
        tn = m.node_tree.nodes.get("Wear_Target") or m.node_tree.nodes.new("ShaderNodeTexImage")
        tn.name = "Wear_Target"
        tex_nodes.append((m, tn))
    for o in objs:
        uv = o.data.uv_layers[LAYER]
        o.data.uv_layers.active = uv
        uv.active_render = True
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bk = sc.render.bake
    bk.use_selected_to_active = False
    bk.margin = 4
    bk.margin_type = 'EXTEND'
    bk.target = 'IMAGE_TEXTURES'
    try:
        for key, btype, spp in (("AO", 'AO', ao_samples), ("EDGE", 'EMIT', 24), ("DIRT", 'EMIT', 48),
                                ("POLISH", 'EMIT', 4)):
            for m, tn in tex_nodes:
                tn.image = imgs[key]
                m.node_tree.nodes.active = tn
            _route(mats, key if btype == 'EMIT' else None)
            sc.cycles.samples = spp
            print("baking", key, "...", flush=True)
            bpy.ops.object.bake(type=btype, margin=4, use_clear=False)
    finally:
        _route(mats, None)
        for o in objs:
            o.data.uv_layers.active = o.data.uv_layers[0]
            o.data.uv_layers[0].active_render = True
    px = {k: np.array(im.pixels[:], np.float32).reshape(res, res, 4)[..., 0] for k, im in imgs.items()}
    rgba = np.dstack([px["AO"], px["EDGE"], px["DIRT"], px["POLISH"]])
    out = bpy.data.images.get("KitWear") or bpy.data.images.new("KitWear", res, res, alpha=True)
    out.colorspace_settings.name = 'Non-Color'
    out.alpha_mode = 'CHANNEL_PACKED'
    out.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    out.filepath_raw = out_path
    out.file_format = 'PNG'
    out.save()
    for k, v in px.items():
        print(f"  {k}: mean {v.mean():.3f}  max {v.max():.3f}")
    print("wrote", out_path)
    return out_path


# ------------------------------------------------------------------ preview materials (Blender renders only)
def preview_materials(trim_dir, wear_path, scans=None, amount=1.0):
    """Wire every kit material for a lit preview: M_Trim from the trim sheet (UV0), other surfaces flat or from
    `scans` {material: diffuse path}, all modulated by the wear atlas on UV2 with the same intent as the Unity kit
    shader: base *= lerp(1, AO, .85) * (1 - .55 dirt); edge wear lightens / desaturates; polish lowers roughness.
    amount: 0 = before wear, 1 = after. Returns the 'WearAmount' value nodes (set .outputs[0].default_value)."""
    wear = bpy.data.images.load(wear_path, check_existing=True)
    wear.colorspace_settings.name = 'Non-Color'
    wear.alpha_mode = 'CHANNEL_PACKED'
    knobs = []
    for m in bpy.data.materials:
        if not m.name.startswith("M_") or not m.use_nodes:
            continue
        nt = m.node_tree
        N, Lk = nt.nodes, nt.links
        for n in [n for n in N if n.name.startswith("PV_")]:
            N.remove(n)
        bsdf = N["Principled BSDF"]
        Lk.new(bsdf.outputs[0], N["Material Output"].inputs["Surface"])

        def node(t, name, **kw):
            n = N.new(t)
            n.name = "PV_" + name
            for k, v in kw.items():
                setattr(n, k, v)
            return n

        uv0 = node("ShaderNodeUVMap", "uv0", uv_map="UVMap")
        uv1 = node("ShaderNodeUVMap", "uv1", uv_map=LAYER)
        base_col = None
        rough = 0.6
        if m.name == "M_Trim":
            alb = node("ShaderNodeTexImage", "alb")
            alb.image = bpy.data.images.load(os.path.join(trim_dir, "Trim_Albedo.png"), check_existing=True)
            Lk.new(uv0.outputs[0], alb.inputs[0])
            msk = node("ShaderNodeTexImage", "msk")
            msk.image = bpy.data.images.load(os.path.join(trim_dir, "Trim_Mask.png"), check_existing=True)
            msk.image.colorspace_settings.name = 'Non-Color'
            Lk.new(uv0.outputs[0], msk.inputs[0])
            nrm = node("ShaderNodeTexImage", "nrm")
            nrm.image = bpy.data.images.load(os.path.join(trim_dir, "Trim_Normal.png"), check_existing=True)
            nrm.image.colorspace_settings.name = 'Non-Color'
            Lk.new(uv0.outputs[0], nrm.inputs[0])
            nm = node("ShaderNodeNormalMap", "nm", uv_map="UVMap")
            Lk.new(nrm.outputs[0], nm.inputs["Color"])
            Lk.new(nm.outputs[0], bsdf.inputs["Normal"])
            sep = node("ShaderNodeSeparateColor", "sep")
            Lk.new(msk.outputs[0], sep.inputs[0])
            Lk.new(sep.outputs[0], bsdf.inputs["Metallic"])
            base_col = alb.outputs[0]
            rough_sock = node("ShaderNodeMath", "rough", operation='SUBTRACT')
            rough_sock.inputs[0].default_value = 1.0
            Lk.new(msk.outputs["Alpha"], rough_sock.inputs[1])
            rough = rough_sock.outputs[0]
        elif scans and m.name in scans:
            alb = node("ShaderNodeTexImage", "alb")
            alb.image = bpy.data.images.load(scans[m.name], check_existing=True)
            Lk.new(uv0.outputs[0], alb.inputs[0])
            base_col = alb.outputs[0]
        if base_col is None:
            rgb = node("ShaderNodeRGB", "flat")
            rgb.outputs[0].default_value = tuple(m.diffuse_color)
            base_col = rgb.outputs[0]
        wt = node("ShaderNodeTexImage", "wear")
        wt.image = wear
        wt.interpolation = 'Linear'
        Lk.new(uv1.outputs[0], wt.inputs[0])
        ws = node("ShaderNodeSeparateColor", "wsep")
        Lk.new(wt.outputs["Color"], ws.inputs[0])
        amt = node("ShaderNodeValue", "WearAmount")
        amt.outputs[0].default_value = amount
        knobs.append(amt)

        def mix(a, b, f, blend='MIX', name="mix"):
            n = node("ShaderNodeMix", name, data_type='RGBA', blend_type=blend)
            for sock, v in ((n.inputs[0], f), (n.inputs[6], a), (n.inputs[7], b)):
                if isinstance(v, (float, int)):
                    sock.default_value = v
                elif isinstance(v, tuple):
                    sock.default_value = v
                else:
                    Lk.new(v, sock)
            return n.outputs[2]

        def mul(a, b, name):
            n = node("ShaderNodeMath", name, operation='MULTIPLY')
            for i, v in enumerate((a, b)):
                if isinstance(v, (float, int)):
                    n.inputs[i].default_value = v
                else:
                    Lk.new(v, n.inputs[i])
            return n.outputs[0]

        # AO
        ao_f = mul(amt.outputs[0], 0.85, "aof")
        aocol = node("ShaderNodeCombineColor", "aoc")
        for i in range(3):
            Lk.new(ws.outputs[0], aocol.inputs[i])
        c = mix(base_col, aocol.outputs[0], ao_f, 'MULTIPLY', "ao")
        # edge wear: lighter, desaturated (worn varnish / paint)
        light = node("ShaderNodeHueSaturation", "light")
        light.inputs["Saturation"].default_value = 0.55
        light.inputs["Value"].default_value = 1.9
        Lk.new(c, light.inputs["Color"])
        c = mix(c, light.outputs[0], mul(ws.outputs[1], mul(amt.outputs[0], 0.85, "ef"), "edge"), 'MIX', "edgem")
        # dirt: darker grey-brown
        c = mix(c, (0.05, 0.04, 0.03, 1.0), mul(ws.outputs[2], mul(amt.outputs[0], 0.6, "df"), "dirt"), 'MIX',
                "dirtm")
        Lk.new(c, bsdf.inputs["Base Color"])
        # polish: smoother (and a touch darker)
        pol = mul(wt.outputs["Alpha"], amt.outputs[0], "pol")
        rn = node("ShaderNodeMath", "rpol", operation='MULTIPLY_ADD')
        Lk.new(pol, rn.inputs[0])
        rn.inputs[1].default_value = -0.45
        if isinstance(rough, float):
            rn.inputs[2].default_value = rough
        else:
            Lk.new(rough, rn.inputs[2])
        Lk.new(rn.outputs[0], bsdf.inputs["Roughness"])
    return knobs
