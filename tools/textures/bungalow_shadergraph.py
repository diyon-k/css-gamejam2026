"""Writes Unity/Assets/Art/Environment/Bungalow/Shaders/SG_BungalowLit.shadergraph (+ BungalowWear.hlsl.meta).

  python -I tools/textures/bungalow_shadergraph.py

A URP Lit Shader Graph (metallic workflow, opaque, front faces; lightmaps, light probes, shadowmask, Forward+ and
the SRP Batcher come with the URP Lit target). All the surface maths lives in BungalowWear.hlsl, called from one
Custom Function node (file mode). The graph only declares the properties, feeds them plus UV0 / UV2 into that node and
wires its outputs to the master stack (tiling via _BaseMap's tiling/offset; the kit's UV0 is already scaled to
1 UV = 1 texture tile, see bungalow_layout.SURFACE_TILE_M, so kit materials use tiling 1):

  _BaseMap/_BaseColor/_BumpMap/_BumpScale/_MaskMap/_Metallic/_Smoothness/_OcclusionStrength   (URP Lit names)
  _WearMap (kit wear atlas on Unity's uv2 = TEXCOORD1, the lightmap layout bungalow_wear.py packs: R AO, G edge wear,
  B dirt, A polish) + strengths and tints.

The file is Shader Graph's multi-JSON format (SG 17 / Unity 6). Object ids are stable (md5 of a name), so reruns
produce the same file and the same GUID for the HLSL include.
"""
import hashlib
import json
import os
import uuid

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Unity", "Assets", "Art", "Environment",
                    "Bungalow", "Shaders")
GRAPH = os.path.join(ROOT, "SG_BungalowLit.shadergraph")
HLSL = os.path.join(ROOT, "BungalowWear.hlsl")
HLSL_GUID = hashlib.md5(b"blink-bungalow-wear-hlsl").hexdigest()   # used only if Unity has not made a .meta yet


def _hlsl_guid():
    """The include's GUID: read from its .meta if Unity already imported it, else write a .meta with ours."""
    meta = HLSL + ".meta"
    if os.path.exists(meta):
        for line in open(meta, encoding="utf-8"):
            if line.startswith("guid:"):
                return line.split()[1]
    lines = ["fileFormatVersion: 2", f"guid: {HLSL_GUID}", "ShaderIncludeImporter:", "  externalObjects: {}",
             "  userData: ", "  assetBundleName: ", "  assetBundleVariant: ", ""]
    with open(meta, "w", newline="\n") as f:
        f.write("\n".join(lines))
    return HLSL_GUID


def oid(name):
    return hashlib.md5(("sg:" + name).encode()).hexdigest()


def guid(name):
    return str(uuid.UUID(hashlib.md5(("guid:" + name).encode()).hexdigest()))


OBJS = []


def add(o):
    OBJS.append(o)
    return o


def draw(x, y, w=0.0, h=0.0):
    return {"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": float(x), "y": float(y),
                                               "width": float(w), "height": float(h)}}


NODE_COMMON = {"m_Group": {"m_Id": ""}, "synonyms": [], "m_Precision": 0, "m_PreviewExpanded": False,
               "m_DismissedVersion": 0, "m_PreviewMode": 0, "m_CustomColors": {"m_SerializableColors": []}}


def vec(n, v=0.0):
    keys = "xyzw"[:n]
    return {k: float(v) for k in keys}


# ------------------------------------------------------------------ slots
def slot_v1(key, sid, name, out, value=0.0, stage=3, shader_name=None):
    return add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.Vector1MaterialSlot", "m_ObjectId": oid(key),
                "m_Id": sid, "m_DisplayName": name, "m_SlotType": 1 if out else 0, "m_Hidden": False,
                "m_ShaderOutputName": shader_name or name, "m_StageCapability": stage, "m_Value": float(value),
                "m_DefaultValue": float(value), "m_Labels": []})


def slot_vn(key, n, sid, name, out, stage=3, shader_name=None):
    t = {2: "Vector2MaterialSlot", 3: "Vector3MaterialSlot", 4: "Vector4MaterialSlot"}[n]
    return add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph." + t, "m_ObjectId": oid(key), "m_Id": sid,
                "m_DisplayName": name, "m_SlotType": 1 if out else 0, "m_Hidden": False,
                "m_ShaderOutputName": shader_name or name, "m_StageCapability": stage, "m_Value": vec(n),
                "m_DefaultValue": vec(n), "m_Labels": []})


def slot_tex_in(key, sid, name):
    return add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.Texture2DInputMaterialSlot",
                "m_ObjectId": oid(key), "m_Id": sid, "m_DisplayName": name, "m_SlotType": 0, "m_Hidden": False,
                "m_ShaderOutputName": name, "m_StageCapability": 3, "m_BareResource": False,
                "m_Texture": {"m_SerializedTexture": "{\"texture\":{\"instanceID\":0}}", "m_Guid": ""},
                "m_DefaultType": 0})


def slot_tex_out(key, name):
    return add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.Texture2DMaterialSlot", "m_ObjectId": oid(key),
                "m_Id": 0, "m_DisplayName": name, "m_SlotType": 1, "m_Hidden": False, "m_ShaderOutputName": "Out",
                "m_StageCapability": 3, "m_BareResource": False})


# ------------------------------------------------------------------ properties
PROPS = []   # (prop object, kind)


def _prop_common(key, display, ref):
    return {"m_ObjectId": oid("prop:" + key), "m_Guid": {"m_GuidSerialized": guid(key)},
            "promotedFromAssetID": "", "promotedFromCategoryName": "", "promotedOrdering": -1,
            "m_Name": display, "m_DefaultRefNameVersion": 1, "m_RefNameGeneratedByDisplayName": display,
            "m_DefaultReferenceName": ref, "m_OverrideReferenceName": "", "m_GeneratePropertyBlock": True,
            "m_UseCustomSlotLabel": False, "m_CustomSlotLabel": "", "m_DismissedVersion": 0, "m_Precision": 0,
            "overrideHLSLDeclaration": False, "hlslDeclarationOverride": 0, "hideConnector": False,
            "m_Hidden": False, "m_PerRendererData": False, "m_customAttributes": []}


def prop_tex(key, display, ref, default_type, main=False, tiling=False):
    p = {"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.Internal.Texture2DShaderProperty",
         **_prop_common(key, display, ref),
         "m_Value": {"m_SerializedTexture": "{\"texture\":{\"instanceID\":0}}", "m_Guid": ""},
         "isMainTexture": main, "useTilingAndOffset": tiling, "m_Modifiable": True,
         "m_DefaultType": default_type}
    PROPS.append((add(p), "tex"))
    return p


def prop_float(key, display, ref, value, lo=0.0, hi=1.0):
    p = {"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty",
         **_prop_common(key, display, ref), "m_Value": float(value), "m_FloatType": 1, "m_LiteralFloatMode": False,
         "m_RangeValues": {"x": float(lo), "y": float(hi)}, "m_SliderType": 0, "m_SliderPower": 3.0,
         "m_EnumType": 0, "m_CSharpEnumString": "", "m_EnumNames": ["Default"], "m_EnumValues": [0]}
    PROPS.append((add(p), "float"))
    return p


def prop_color(key, display, ref, rgba, main=False):
    p = {"m_SGVersion": 3, "m_Type": "UnityEditor.ShaderGraph.Internal.ColorShaderProperty",
         **_prop_common(key, display, ref), "m_Value": dict(zip("rgba", map(float, rgba))),
         "isMainColor": main, "m_ColorMode": 0}
    PROPS.append((add(p), "color"))
    return p


# ------------------------------------------------------------------ nodes
NODES, EDGES = [], []


def property_node(p, kind, x, y):
    key = "pn:" + p["m_DefaultReferenceName"]
    if kind == "tex":
        s = slot_tex_out(key + ":out", p["m_Name"])
    elif kind == "float":
        s = slot_v1(key + ":out", 0, p["m_Name"], True, shader_name="Out")
    else:
        s = slot_vn(key + ":out", 4, 0, p["m_Name"], True, shader_name="Out")
    n = add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.PropertyNode", "m_ObjectId": oid(key),
             **NODE_COMMON, "m_Name": "Property", "m_DrawState": draw(x, y, 180, 34),
             "m_Slots": [{"m_Id": s["m_ObjectId"]}], "m_Property": {"m_Id": p["m_ObjectId"]}})
    NODES.append(n)
    return n


def uv_node(channel, x, y):
    key = f"uv{channel}"
    s = slot_vn(key + ":out", 4, 0, "Out", True)
    n = add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.UVNode", "m_ObjectId": oid(key), **NODE_COMMON,
             "m_Name": "UV", "m_DrawState": draw(x, y, 145, 129), "m_Slots": [{"m_Id": s["m_ObjectId"]}],
             "m_OutputChannel": channel})
    NODES.append(n)
    return n


def edge(out_node, out_slot, in_node, in_slot):
    EDGES.append({"m_OutputSlot": {"m_Node": {"m_Id": out_node["m_ObjectId"]}, "m_SlotId": out_slot},
                  "m_InputSlot": {"m_Node": {"m_Id": in_node["m_ObjectId"]}, "m_SlotId": in_slot}})


BLOCKS_V, BLOCKS_F = [], []


def block(desc, slot):
    n = add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.BlockNode", "m_ObjectId": oid("blk:" + desc),
             **NODE_COMMON, "m_PreviewExpanded": True, "m_Name": desc, "m_DrawState": draw(0, 0),
             "m_Slots": [{"m_Id": slot["m_ObjectId"]}], "m_SerializedDescriptor": desc})
    NODES.append(n)
    (BLOCKS_V if desc.startswith("Vertex") else BLOCKS_F).append(n)
    return n


def block_slot(key, typ, name, shader_name, stage, value, extra):
    o = {"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph." + typ, "m_ObjectId": oid("bs:" + key), "m_Id": 0,
         "m_DisplayName": name, "m_SlotType": 0, "m_Hidden": False, "m_ShaderOutputName": shader_name,
         "m_StageCapability": stage, "m_Value": value, "m_DefaultValue": value, "m_Labels": []}
    o.update(extra)
    return add(o)


def build():
    # properties (order = material inspector order)
    base = prop_tex("BaseMap", "Base Map", "_BaseMap", 0, main=True, tiling=True)
    col = prop_color("BaseColor", "Base Color", "_BaseColor", (1, 1, 1, 1), main=True)
    bump = prop_tex("BumpMap", "Normal Map", "_BumpMap", 3)
    bscale = prop_float("BumpScale", "Normal Scale", "_BumpScale", 1.0, 0.0, 2.0)
    mask = prop_tex("MaskMap", "Mask Map (R Metal, G AO, A Smooth)", "_MaskMap", 0)
    metal = prop_float("Metallic", "Metallic", "_Metallic", 1.0)
    smooth = prop_float("Smoothness", "Smoothness", "_Smoothness", 1.0)
    occ = prop_float("OcclusionStrength", "Occlusion Strength", "_OcclusionStrength", 1.0)
    wear = prop_tex("WearMap", "Wear Atlas (uv2/TEXCOORD1: R AO, G Edge, B Dirt, A Polish)", "_WearMap", 5)
    w_ao = prop_float("WearAOStrength", "Wear AO Strength", "_WearAOStrength", 1.0)
    e_str = prop_float("EdgeWearStrength", "Edge Wear Strength", "_EdgeWearStrength", 1.0, 0.0, 2.0)
    e_col = prop_color("EdgeWearColor", "Edge Wear Tint (A = amount)", "_EdgeWearColor", (0.62, 0.5, 0.38, 0.35))
    e_lit = prop_float("EdgeWearLighten", "Edge Wear Lighten", "_EdgeWearLighten", 0.45, 0.0, 2.0)
    d_str = prop_float("DirtStrength", "Dirt Strength", "_DirtStrength", 1.0, 0.0, 2.0)
    d_col = prop_color("DirtColor", "Dirt Color (multiply)", "_DirtColor", (0.42, 0.37, 0.3, 1))
    d_rgh = prop_float("DirtRoughen", "Dirt Roughen", "_DirtRoughen", 0.6)
    p_str = prop_float("PolishStrength", "Polish Strength", "_PolishStrength", 1.0, 0.0, 2.0)
    p_smo = prop_float("PolishSmoothness", "Polish Smoothness", "_PolishSmoothness", 0.72)

    # custom function node: inputs in HLSL signature order
    ins = [("BaseMap", "tex", base), ("BumpMap", "tex", bump), ("MaskMap", "tex", mask), ("WearMap", "tex", wear),
           ("UV0", "uv", 0), ("UV2", "uv", 1),   # "UV2" = Unity mesh.uv2 = TEXCOORD1 = SG channel UV1
           ("BaseColor", "v4", col), ("BumpScale", "v1", bscale), ("Metallic", "v1", metal),
           ("Smoothness", "v1", smooth), ("OcclusionStrength", "v1", occ), ("WearAOStrength", "v1", w_ao),
           ("EdgeWearStrength", "v1", e_str), ("EdgeWearColor", "v4", e_col), ("EdgeWearLighten", "v1", e_lit),
           ("DirtStrength", "v1", d_str), ("DirtColor", "v4", d_col), ("DirtRoughen", "v1", d_rgh),
           ("PolishStrength", "v1", p_str), ("PolishSmoothness", "v1", p_smo)]
    outs = [("Albedo", 3), ("NormalTS", 3), ("MetallicOut", 1), ("SmoothnessOut", 1), ("Occlusion", 1)]
    cf_slots, sid = [], 0
    in_ids = {}
    for name, kind, _ in ins:
        k = "cf:in:" + name
        if kind == "tex":
            s = slot_tex_in(k, sid, name)
        elif kind == "uv":
            s = slot_vn(k, 2, sid, name, False)
        elif kind == "v4":
            s = slot_vn(k, 4, sid, name, False)
        else:
            s = slot_v1(k, sid, name, False)
        in_ids[name] = sid
        cf_slots.append(s)
        sid += 1
    out_ids = {}
    for name, n in outs:
        k = "cf:out:" + name
        s = slot_v1(k, sid, name, True) if n == 1 else slot_vn(k, n, sid, name, True)
        out_ids[name] = sid
        cf_slots.append(s)
        sid += 1
    cf = add({"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.CustomFunctionNode",
              "m_ObjectId": oid("cf"), **NODE_COMMON, "synonyms": ["code", "HLSL"],
              "m_Name": "BungalowWear (Custom Function)", "m_DrawState": draw(-520, 0, 320, 520),
              "m_Slots": [{"m_Id": s["m_ObjectId"]} for s in cf_slots], "m_SourceType": 0,
              "m_FunctionName": "BungalowWear", "m_FunctionSource": _hlsl_guid(), "m_FunctionSourceUsePragmas": True,
              "m_FunctionBody": "Enter function body here..."})
    NODES.append(cf)

    y = -200
    for name, kind, p in ins:
        if kind == "uv":
            n = uv_node(p, -900, y)
            y += 140
        else:
            pk = dict((q["m_ObjectId"], k) for q, k in PROPS)[p["m_ObjectId"]]
            n = property_node(p, pk, -900, y)
            y += 44
        edge(n, 0, cf, in_ids[name])

    # master stack
    vpos = block("VertexDescription.Position", block_slot("vpos", "PositionMaterialSlot", "Position", "Position", 1,
                                                         vec(3), {"m_Space": 0}))
    block("VertexDescription.Normal", block_slot("vnrm", "NormalMaterialSlot", "Normal", "Normal", 1, vec(3),
                                                 {"m_Space": 0}))
    block("VertexDescription.Tangent", block_slot("vtan", "TangentMaterialSlot", "Tangent", "Tangent", 1, vec(3),
                                                  {"m_Space": 0}))
    grey = {"x": 0.5, "y": 0.5, "z": 0.5}
    fb = block("SurfaceDescription.BaseColor", block_slot(
        "albedo", "ColorRGBMaterialSlot", "Base Color", "BaseColor", 2, grey,
        {"m_ColorMode": 0, "m_DefaultColor": {"r": 0.5, "g": 0.5, "b": 0.5, "a": 1.0}}))
    fn = block("SurfaceDescription.NormalTS", block_slot("nts", "NormalMaterialSlot", "Normal (Tangent Space)",
                                                         "NormalTS", 2, vec(3), {"m_Space": 3}))
    fm = block("SurfaceDescription.Metallic", block_slot("met", "Vector1MaterialSlot", "Metallic", "Metallic", 2,
                                                         0.0, {}))
    fs = block("SurfaceDescription.Smoothness", block_slot("smo", "Vector1MaterialSlot", "Smoothness", "Smoothness",
                                                           2, 0.5, {}))
    fe = block("SurfaceDescription.Emission", block_slot(
        "emi", "ColorRGBMaterialSlot", "Emission", "Emission", 2, vec(3),
        {"m_ColorMode": 1, "m_DefaultColor": {"r": 0.0, "g": 0.0, "b": 0.0, "a": 1.0}}))
    fo = block("SurfaceDescription.Occlusion", block_slot("occ", "Vector1MaterialSlot", "Ambient Occlusion",
                                                          "Occlusion", 2, 1.0, {}))
    edge(cf, out_ids["Albedo"], fb, 0)
    edge(cf, out_ids["NormalTS"], fn, 0)
    edge(cf, out_ids["MetallicOut"], fm, 0)
    edge(cf, out_ids["SmoothnessOut"], fs, 0)
    edge(cf, out_ids["Occlusion"], fo, 0)
    _ = (vpos, fe)

    sub = add({"m_SGVersion": 2, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalLitSubTarget",
               "m_ObjectId": oid("litsub"), "m_WorkflowMode": 1, "m_NormalDropOffSpace": 0, "m_ClearCoat": False,
               "m_BlendModePreserveSpecular": True})
    tgt = add({"m_SGVersion": 1, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget",
               "m_ObjectId": oid("urp"), "m_Datas": [], "m_ActiveSubTarget": {"m_Id": sub["m_ObjectId"]},
               "m_AllowMaterialOverride": False, "m_SurfaceType": 0, "m_ZTestMode": 4, "m_ZWriteControl": 0,
               "m_AlphaMode": 0, "m_RenderFace": 2, "m_AlphaClip": False, "m_CastShadows": True,
               "m_ReceiveShadows": True, "m_DisableTint": False, "m_AdditionalMotionVectorMode": 0,
               "m_AlembicMotionVectors": False, "m_SupportsLODCrossFade": False, "m_CustomEditorGUI": "",
               "m_SupportVFX": False})
    cat = add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.CategoryData", "m_ObjectId": oid("cat"),
               "m_Name": "", "m_ChildObjectList": [{"m_Id": p["m_ObjectId"]} for p, _ in PROPS]})
    graph = {"m_SGVersion": 3, "m_Type": "UnityEditor.ShaderGraph.GraphData", "m_ObjectId": oid("graph"),
             "m_Properties": [{"m_Id": p["m_ObjectId"]} for p, _ in PROPS], "m_Keywords": [], "m_Dropdowns": [],
             "m_CategoryData": [{"m_Id": cat["m_ObjectId"]}],
             "m_Nodes": [{"m_Id": n["m_ObjectId"]} for n in NODES], "m_GroupDatas": [], "m_StickyNoteDatas": [],
             "m_Edges": EDGES,
             "m_VertexContext": {"m_Position": {"x": 0.0, "y": 0.0},
                                 "m_Blocks": [{"m_Id": n["m_ObjectId"]} for n in BLOCKS_V]},
             "m_FragmentContext": {"m_Position": {"x": 0.0, "y": 200.0},
                                   "m_Blocks": [{"m_Id": n["m_ObjectId"]} for n in BLOCKS_F]},
             "m_PreviewData": {"serializedMesh": {"m_SerializedMesh": "{\"mesh\":{\"instanceID\":0}}",
                                                  "m_Guid": ""}, "preventRotation": False},
             "m_Path": "BLINK/Bungalow", "m_GraphPrecision": 1, "m_PreviewMode": 2, "m_OutputNode": {"m_Id": ""},
             "m_SubDatas": [], "m_ActiveTargets": [{"m_Id": tgt["m_ObjectId"]}]}
    rest = sorted((o for o in OBJS), key=lambda o: o["m_ObjectId"])
    text = json.dumps(graph, indent=4) + "\n\n" + "\n\n".join(json.dumps(o, indent=4) for o in rest) + "\n\n"
    with open(GRAPH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print("wrote", os.path.normpath(GRAPH), len(OBJS), "objects;", len(NODES), "nodes;", len(EDGES), "edges")


if __name__ == "__main__":
    build()
