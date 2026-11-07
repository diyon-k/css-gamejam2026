"""Unity-side config for the Blender assets, done by editing YAML (works without the Editor bridge).

  python tools/blender/unity_setup.py      (after build_all.py and one Unity refresh, so .meta files exist)

- Textures/*.png importers: normal maps as NormalMap, albedo sRGB, masks linear with alpha, mips, HQ compression
  (BC7 / BC5 on desktop), max 1024 (512 for Candelabra).
- Materials/*.mat: URP Lit wired to the family / baked maps (_BaseMap, _BumpMap, _MetallicGlossMap, _OcclusionMap
  + keywords), tint + smoothness from lib.MATERIALS. Creates <Asset>_Baked.mat for baked assets.
- *.fbx.meta: remaps every material name found in the FBX onto Materials/<name>.mat.
Refresh Unity afterwards.
"""
import os
import re
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Mansion", "Blender")
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
    for f in sorted(os.listdir(TEX)):
        if not f.endswith(".png"):
            continue
        p = os.path.join(TEX, f + ".meta")
        s = read(p)
        normal, mask = f.endswith("_Normal.png"), f.endswith("_Mask.png")
        size = 512 if f.startswith("Candelabra_") else 1024
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


def materials():
    for name, (fam, tint, smooth) in SHARED.items():
        if fam is None:
            continue
        p = os.path.join(MATS, name + ".mat")
        s = read(p)
        keep = re.findall(r"^  - (_EMISSION|_SURFACE_TYPE_TRANSPARENT)$", s, re.M)
        write(p, wire(s, fam, tint, smooth, keep))
    template = read(os.path.join(MATS, "Wood_Dark.mat"))
    for a in BAKED:
        if not os.path.exists(os.path.join(TEX, a + "_Albedo.png.meta")):
            continue
        p = os.path.join(MATS, a + "_Baked.mat")
        s = sub(template, r"^(  m_Name:) .*", r"\g<1> %s_Baked" % a)
        write(p, wire(s, a, (1, 1, 1), 1.0, []))
        if not os.path.exists(p + ".meta"):
            write(p + ".meta", "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n"
                  "  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                  % uuid.uuid4().hex)


def remaps():
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


if __name__ == "__main__":
    textures()
    materials()
    remaps()
    sys.exit(0)
