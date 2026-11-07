"""Unity-side config for the Blender assets, done by editing YAML (works without the Editor bridge).

  python tools/blender/unity_setup.py [--target mansion|bungalow]
      (after build_all.py and one Unity refresh, so .meta files exist; mansion is the default)

- Textures/*.png importers: normal maps as NormalMap, albedo sRGB, masks linear with alpha, mips, HQ compression
  (BC7 / BC5 on desktop), max 1024 (512 for Candelabra).
- Materials/*.mat: URP Lit wired to the family / baked maps (_BaseMap, _BumpMap, _MetallicGlossMap, _OcclusionMap
  + keywords), tint + smoothness from lib.MATERIALS. Creates <Asset>_Baked.mat for baked assets.
- *.fbx.meta: remaps every material name found in the FBX onto Materials/<name>.mat.
- bungalow only: *.fbx.meta (recursive) importer settings: no cameras / lights / visibility / animation / colliders,
  not readable, Generate Lightmap UVs OFF (the lightmap UVs come from Blender as UV2, lib.uv_lightmap).
Folders that don't exist yet (no textures or materials) are skipped. When the mansion template material
(Materials/Wood_Dark.mat) is missing, e.g. under a sparse checkout, materials are written from LIT_TEMPLATE.
Refresh Unity afterwards.
"""
import os
import re
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "..", "..", "Unity", "Assets", "Art")
TARGET_ROOT = {
    "mansion": os.path.join(ART, "Mansion", "Blender"),
    "bungalow": os.path.join(ART, "Environment", "Bungalow"),
}
TARGET = "mansion"
ROOT = TARGET_ROOT[TARGET]
TEX = os.path.join(ROOT, "Textures")
MATS = os.path.join(ROOT, "Materials")

# Mirrors lib.MATERIALS (family, tint sRGB, smoothness multiplier); lib imports bpy so it can't be imported here.
SHARED = {
    "Wood_Dark": ("Wood", (0.34, 0.2, 0.13), 0.8), "Wood_Light": ("Wood", (0.66, 0.47, 0.3), 0.75),
    "Brass": ("Metal", (0.95, 0.76, 0.42), 1.0), "Iron": ("Metal", (0.24, 0.23, 0.23), 0.7),
    "Silver": ("Metal", (0.86, 0.86, 0.83), 1.0), "Fabric_Red": ("Fabric", (0.6, 0.1, 0.1), 1.0),
    "Fabric_Linen": ("Fabric", (1.0, 0.96, 0.85), 1.0), "Wax": ("Smooth", (0.98, 0.94, 0.8), 0.5),
    "Glass": (None, None, None), "Stone": ("Stone", (0.76, 0.73, 0.69), 1.0),
    "Paper": ("Smooth", (0.93, 0.87, 0.72), 0.15), "Ceramic": ("Smooth", (0.98, 0.97, 0.93), 0.85),
    "Leather_Brown": ("Leather", (0.45, 0.25, 0.14), 1.0), "Leather_Green": ("Leather", (0.18, 0.32, 0.2), 1.0),
}
BAKED = ["FourPosterBed", "Chandelier", "Armchair", "Candelabra", "Bookshelf"]
TARGET_SHARED = {"mansion": SHARED, "bungalow": {}}
TARGET_BAKED = {"mansion": BAKED, "bungalow": []}

# Plain URP Lit material (Unity 6 / URP 17 Lit, from the project's own Lit materials), used as the copy template
# when the mansion template Materials/Wood_Dark.mat is not on disk.
LIT_TEMPLATE = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_Name: Template\n  m_Shader: {fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}\n  m_Parent: {fileID: 0}\n  m_ModifiedSerializedProperties: 0\n  m_ValidKeywords: []\n  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 0\n  m_DoubleSidedGI: 0\n  m_CustomRenderQueue: -1\n  stringTagMap:\n    RenderType: Opaque\n  disabledShaderPasses:\n  - MOTIONVECTORS\n  m_LockedProperties: \n  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n    - _BaseMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _BumpMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _DetailAlbedoMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _DetailMask:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _DetailNormalMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _EmissionMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _MainTex:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _MetallicGlossMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _OcclusionMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _ParallaxMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - _SpecGlossMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - unity_Lightmaps:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - unity_LightmapsInd:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    - unity_ShadowMasks:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n    m_Ints: []\n    m_Floats:\n    - _AddPrecomputedVelocity: 0\n    - _AlphaClip: 0\n    - _AlphaToMask: 0\n    - _Blend: 0\n    - _BlendModePreserveSpecular: 1\n    - _BumpScale: 1\n    - _ClearCoatMask: 0\n    - _ClearCoatSmoothness: 0\n    - _Cull: 2\n    - _Cutoff: 0.5\n    - _DetailAlbedoMapScale: 1\n    - _DetailNormalMapScale: 1\n    - _DstBlend: 0\n    - _DstBlendAlpha: 0\n    - _EnvironmentReflections: 1\n    - _GlossMapScale: 0\n    - _Glossiness: 0\n    - _GlossyReflections: 0\n    - _Metallic: 0\n    - _OcclusionStrength: 1\n    - _Parallax: 0.005\n    - _QueueOffset: 0\n    - _ReceiveShadows: 1\n    - _Smoothness: 0.5\n    - _SmoothnessTextureChannel: 0\n    - _SpecularHighlights: 1\n    - _SrcBlend: 1\n    - _SrcBlendAlpha: 1\n    - _Surface: 0\n    - _WorkflowMode: 1\n    - _ZWrite: 1\n    m_Colors:\n    - _BaseColor: {r: 1, g: 1, b: 1, a: 1}\n    - _Color: {r: 1, g: 1, b: 1, a: 1}\n    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}\n    - _SpecColor: {r: 0.19999996, g: 0.19999996, b: 0.19999996, a: 1}\n  m_BuildTextureStacks: []\n  m_AllowLocking: 1\n--- !u!114 &7537660392728052172\nMonoBehaviour:\n  m_ObjectHideFlags: 11\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: d0353a89b1f911e48b9e16bdc9f2e058, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Unity.RenderPipelines.Universal.Editor::UnityEditor.Rendering.Universal.AssetVersion\n  version: 10\n'


def set_target(name):
    global TARGET, ROOT, TEX, MATS, SHARED, BAKED
    TARGET = name
    ROOT = TARGET_ROOT[name]
    TEX = os.path.join(ROOT, "Textures")
    MATS = os.path.join(ROOT, "Materials")
    SHARED = TARGET_SHARED[name]
    BAKED = TARGET_BAKED[name]


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def write(p, s):
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(s)


def guid_of(path):
    return re.search(r"^guid: (\w+)", read(path + ".meta"), re.M).group(1)


def sub(s, pat, rep, count=0):
    new, n = re.subn(pat, rep, s, count=count, flags=re.M)
    assert n, pat
    return new


def textures():
    if not os.path.isdir(TEX):
        return
    for f in sorted(os.listdir(TEX)):
        if not f.endswith(".png"):
            continue
        p = os.path.join(TEX, f + ".meta")
        s = read(p)
        normal, mask = f.endswith("_Normal.png"), f.endswith("_Mask.png")
        size = 512 if TARGET == "mansion" and f.startswith("Candelabra_") else 1024
        s = sub(s, r"^(    enableMipMap:) \d+", r"\1 1")
        s = sub(s, r"^(    sRGBTexture:) \d+", r"\g<1> %d" % (0 if normal or mask else 1))
        s = sub(s, r"^(  textureType:) \d+", r"\g<1> %d" % (1 if normal else 0))
        s = sub(s, r"^(  alphaUsage:) \d+", r"\g<1> %d" % (1 if mask else 0))
        s = sub(s, r"^(    aniso:) -?\d+", r"\1 4")
        s = sub(s, r"^(  maxTextureSize:) \d+", r"\g<1> %d" % size)
        s = sub(s, r"^(    maxTextureSize:) \d+", r"\g<1> %d" % size)
        s = sub(s, r"^(    textureCompression:) \d+", r"\1 2")
        write(p, s)


def tex_ref(name):
    p = os.path.join(TEX, name + ".png")
    return "{fileID: 2800000, guid: %s, type: 3}" % guid_of(p)


def wire(s, prefix, tint, smooth, keep_kw):
    for slot, suffix in (("_BaseMap", "Albedo"), ("_MainTex", "Albedo"), ("_BumpMap", "Normal"),
                         ("_MetallicGlossMap", "Mask"), ("_OcclusionMap", "Mask")):
        s = sub(s, r"(    - %s:\n        m_Texture: )\{[^}]*\}" % slot, r"\g<1>" + tex_ref(prefix + "_" + suffix))
    kws = sorted(set(keep_kw) | {"_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP"})
    s = sub(s, r"^  m_ValidKeywords:.*\n(  - .*\n)*", "  m_ValidKeywords:\n" + "".join("  - %s\n" % k for k in kws))
    s = sub(s, r"^(    - _BaseColor: )\{[^}]*\}", r"\g<1>{r: %g, g: %g, b: %g, a: 1}" % tint)
    s = sub(s, r"^(    - _Smoothness:) .*", r"\g<1> %g" % smooth)
    s = sub(s, r"^(    - _GlossMapScale:) .*", r"\g<1> %g" % smooth)
    s = sub(s, r"^(    - _Metallic:) .*", r"\1 1")
    s = sub(s, r"^(    - _SmoothnessTextureChannel:) .*", r"\1 0")
    return s


def template():
    p = os.path.join(MATS, "Wood_Dark.mat")
    return read(p) if os.path.exists(p) else LIT_TEMPLATE


def materials():
    for name, (fam, tint, smooth) in SHARED.items():
        p = os.path.join(MATS, name + ".mat")
        if fam is None or not os.path.exists(p):
            continue
        s = read(p)
        keep = re.findall(r"^  - (_EMISSION|_SURFACE_TYPE_TRANSPARENT)$", s, re.M)
        write(p, wire(s, fam, tint, smooth, keep))
    tmpl = None
    for a in BAKED:
        if not os.path.exists(os.path.join(TEX, a + "_Albedo.png.meta")):
            continue
        p = os.path.join(MATS, a + "_Baked.mat")
        tmpl = tmpl or template()
        os.makedirs(MATS, exist_ok=True)
        s = sub(tmpl, r"^(  m_Name:) .*", r"\g<1> %s_Baked" % a)
        write(p, wire(s, a, (1, 1, 1), 1.0, []))
        if not os.path.exists(p + ".meta"):
            write(p + ".meta", "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n"
                  "  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                  % uuid.uuid4().hex)


def models():
    """Bungalow FBX importer settings (all FBX under ROOT, recursive)."""
    keys = (("importCameras", 0), ("importLights", 0), ("importVisibility", 0), ("addColliders", 0),
            ("isReadable", 0), ("generateSecondaryUV", 0))
    for d, _, files in os.walk(ROOT):
        for f in sorted(files):
            p = os.path.join(d, f + ".meta")
            if not f.endswith(".fbx") or not os.path.exists(p):
                continue
            s = read(p)
            for k, v in keys:
                s = sub(s, r"^(    %s:) \d+" % k, r"\g<1> %d" % v)
            s = sub(s, r"^(  importAnimation:) \d+", r"\1 0")
            write(p, s)
            print("model", f)


def remaps():
    if not os.path.isdir(MATS):
        return
    entry = ("  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
             "      name: %s\n    second: {fileID: 2100000, guid: %s, type: 2}\n")
    for f in sorted(os.listdir(ROOT)):
        if not f.endswith(".fbx") or not os.path.exists(os.path.join(ROOT, f + ".meta")):
            continue
        with open(os.path.join(ROOT, f), "rb") as fh:
            data = fh.read()
        names = [m[:-4] for m in sorted(os.listdir(MATS)) if m.endswith(".mat")
                 and (m[:-4].encode() + b"\x00\x01Material") in data]
        lines = "".join(entry % (n, guid_of(os.path.join(MATS, n + ".mat"))) for n in names)
        p = os.path.join(ROOT, f + ".meta")
        s = read(p)
        s = sub(s, r"^  externalObjects:.*\n((  - |    |      ).*\n)*", "  externalObjects:\n" + lines, count=1)
        write(p, s)
        print(f, names)


STANDALONE_BC7 = ("  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 1024\n"
                  "    resizeAlgorithm: 0\n    textureFormat: 25\n    textureCompression: 2\n    compressionQuality: 50\n"
                  "    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 1\n"
                  "    ignorePlatformSupport: 0\n    androidETC2FallbackOverride: 0\n"
                  "    forceMaximumCompressionQuality_BC6H_BC7: 0\n")


def kit_textures():
    """Bungalow kit textures (Phase 3): Textures/Trim (trim sheet) and Textures/KitWear (UV2 wear atlas).
    Mips on, 1024, BC7 on Standalone; *_Normal = Normal Map; *_Mask and KitWear linear with alpha (smoothness /
    polish); the albedo sRGB. KitWear is an atlas, so it clamps."""
    for sub_dir in ("Trim", "KitWear"):
        d = os.path.join(TEX, sub_dir)
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            p = os.path.join(d, f + ".meta")
            if not f.endswith(".png") or not os.path.exists(p):
                continue
            s = read(p)
            normal = f.endswith("_Normal.png")
            linear = normal or f.endswith("_Mask.png") or sub_dir == "KitWear"
            alpha = f.endswith("_Mask.png") or sub_dir == "KitWear"
            s = sub(s, r"^(    enableMipMap:) \d+", r"\1 1")
            s = sub(s, r"^(    sRGBTexture:) \d+", r"\g<1> %d" % (0 if linear else 1))
            s = sub(s, r"^(  textureType:) \d+", r"\g<1> %d" % (1 if normal else 0))
            s = sub(s, r"^(  alphaUsage:) \d+", r"\g<1> %d" % (1 if alpha else 0))
            s = sub(s, r"^(  alphaIsTransparency:) \d+", r"\1 0")
            s = sub(s, r"^(    aniso:) -?\d+", r"\1 8")
            for ax in ("U", "V", "W"):
                s = sub(s, r"^(    wrap%s:) -?\d+" % ax, r"\g<1> %d" % (1 if sub_dir == "KitWear" else 0))
            s = sub(s, r"^(  maxTextureSize:) \d+", r"\1 1024")
            s = sub(s, r"^(    maxTextureSize:) \d+", r"\1 1024")
            s = sub(s, r"^(    textureCompression:) \d+", r"\1 2")
            if re.search(r"^    buildTarget: Standalone$", s, re.M):
                s = sub(s, r"(    buildTarget: Standalone\n(?:    .*\n)*?    textureFormat:) -?\d+", r"\1 25", 1)
                s = sub(s, r"(    buildTarget: Standalone\n(?:    .*\n)*?    overridden:) \d+", r"\1 1", 1)
            else:
                s = sub(s, r"(  - serializedVersion: \d+\n    buildTarget: DefaultTexturePlatform\n(?:    .*\n)*)",
                        r"\g<1>" + STANDALONE_BC7.replace("\\", "\\\\"), 1)
            write(p, s)
            print("texture", sub_dir + "/" + f)


def kit_remaps():
    """Remap the kit FBX material names (M_*) onto Materials/M_*.mat (owned by the materials pass)."""
    d = os.path.join(ROOT, "Kit")
    if not (os.path.isdir(MATS) and os.path.isdir(d)):
        return
    entry = ("  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n"
             "      name: %s\n    second: {fileID: 2100000, guid: %s, type: 2}\n")
    for f in sorted(os.listdir(d)):
        p = os.path.join(d, f + ".meta")
        if not f.endswith(".fbx") or not os.path.exists(p):
            continue
        with open(os.path.join(d, f), "rb") as fh:
            data = fh.read()
        names = [m[:-4] for m in sorted(os.listdir(MATS)) if m.endswith(".mat")
                 and (m[:-4].encode() + b"\x00\x01Material") in data]
        lines = "".join(entry % (n, guid_of(os.path.join(MATS, n + ".mat"))) for n in names)
        s = read(p)
        s = sub(s, r"^  externalObjects:.*\n((  - |    |      ).*\n)*", "  externalObjects:\n" + lines, count=1)
        write(p, s)
        print("Kit/" + f, names)


if __name__ == "__main__":
    args = sys.argv[1:]
    set_target(args[args.index("--target") + 1] if "--target" in args else "mansion")
    textures()
    materials()
    if TARGET == "bungalow":
        models()
        kit_textures()
        kit_remaps()
    remaps()
    sys.exit(0)
