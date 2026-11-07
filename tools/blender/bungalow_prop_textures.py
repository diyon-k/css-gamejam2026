"""BLINK Bungalow (Phase 5): the four 1K prop atlases (M_Atlas_Soft / _Wood / _Small / _Utility).

Plain Python (numpy + Pillow, no bpy). Run with the isolated interpreter:

  python -I tools/blender/bungalow_prop_textures.py --src <download root>

<download root> holds one fresh folder per Poly Haven download, dl_<asset>/ (models: FBX 1k + diff / nor_gl / arm jpg;
textures dirty_carpet and fabric_pattern_05). The Mansion PolyHaven folder in the repo is read in place.

Every cell of an atlas is a re-bake in image space: a Poly Haven model keeps its own 0..1 UV layout, so its 1K maps
are resampled into its cell and bungalow_prop_build.py remaps the model's UV0 into the cell rect. Self-built props use
"swatch" cells (crops of the Phase 3 trim sheet, or seeded procedural materials) and the generated pictures, rugs,
plaid, magazines etc. Each cell keeps a 4 px gutter filled by edge extension so mips don't bleed.

Outputs (all 1024 x 1024):
  Unity/Assets/Art/Environment/Bungalow/Textures/Atlas_<Name>/Atlas_<Name>_Base.png    sRGB (A = cutout on Small)
                                                                  _Normal.png  OpenGL Y+ tangent normal
                                                                  _Mask.png    linear R metal, G AO, B 0, A smooth
  Unity/Assets/Art/Environment/Bungalow/Props/bungalow_prop_atlas.json   cell UV rects (V = 0 at the bottom)
"""
import json
import math
import os
import sys
import zlib

try:
    import numpy as np
except ImportError:  # `python -I` drops the user site-packages; add only that (never the cwd or a download dir)
    import site
    sys.path.append(site.getusersitepackages())
    import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "Unity", "Assets", "Art")
BUNG = os.path.join(ART, "Environment", "Bungalow")
TEX_OUT = os.path.join(BUNG, "Textures")
ATLAS_JSON = os.path.join(BUNG, "Props", "bungalow_prop_atlas.json")
MANSION_PH = os.path.join(ART, "Mansion", "PolyHaven")
TRIM = os.path.join(TEX_OUT, "Trim")
RES = 1024
SRC = None   # --src

# ------------------------------------------------------------------ cell table
# kind "ph": a Poly Haven model texture set; args = (asset, material suffix or "", grade)
# kind "trim": a 256 x 128 crop of trim-sheet strip k (0.5 x 0.25 m); kind "gen": a generator in GEN
# "m" = the real-world size (u, v) in metres one cell covers (swatches; self-built UVs are fitted with it)
ATLASES = {
    "Soft": [
        dict(key="Sofa_01", w=512, h=512, kind="ph", args=("Sofa_01", "", "fabric_dark")),
        dict(key="ArmChair_01", w=384, h=384, kind="ph", args=("ArmChair_01", "", "fabric_brown")),
        dict(key="throw_pillows_01", w=256, h=256, kind="ph", args=("throw_pillows_01", "", "dusty")),
        dict(key="GreenChair_01", w=256, h=256, kind="ph", args=("GreenChair_01", "", "dusty")),
        dict(key="plaid", w=256, h=256, kind="gen", args=("plaid",), m=(0.32, 0.32)),
        dict(key="rug_or_field", w=256, h=256, kind="gen", args=("rug_or_field",), m=(0.5, 0.5)),
        dict(key="rug_or_border", w=256, h=128, kind="gen", args=("rug_or_border",), m=(0.5, 0.25)),
        dict(key="rug_or_corner", w=128, h=128, kind="gen", args=("rug_or_corner",), m=(0.25, 0.25)),
        dict(key="rug_geo_field", w=256, h=256, kind="gen", args=("rug_geo_field",), m=(0.5, 0.5)),
        dict(key="rug_geo_border", w=256, h=128, kind="gen", args=("rug_geo_border",), m=(0.5, 0.25)),
        dict(key="rug_geo_corner", w=128, h=128, kind="gen", args=("rug_geo_corner",), m=(0.25, 0.25)),
        dict(key="runner_field", w=256, h=128, kind="gen", args=("runner_field",), m=(0.6, 0.3)),
        dict(key="runner_border", w=256, h=64, kind="gen", args=("runner_border",), m=(0.6, 0.15)),
        dict(key="curtain", w=128, h=256, kind="gen", args=("curtain",), m=(0.5, 1.0)),
        dict(key="blanket", w=256, h=256, kind="gen", args=("blanket",), m=(1.0, 1.0)),
        dict(key="linen", w=128, h=128, kind="gen", args=("linen",), m=(0.6, 0.6)),
        dict(key="towel", w=128, h=128, kind="gen", args=("towel",), m=(0.5, 0.5)),
        dict(key="soft_wood", w=128, h=128, kind="trim", args=(0,), m=(0.25, 0.25)),
    ],
    "Wood": [
        dict(key="GothicBed_01", w=512, h=512, kind="ph", args=("GothicBed_01", "", "bed")),
        dict(key="GothicCommode_01", w=384, h=384, kind="ph", args=("GothicCommode_01", "", "aged")),
        dict(key="Rockingchair_01", w=256, h=256, kind="ph", args=("Rockingchair_01", "", "aged")),
        dict(key="ClassicConsole_01", w=256, h=256, kind="ph", args=("ClassicConsole_01", "", "aged")),
        dict(key="ClassicNightstand_01", w=256, h=256, kind="ph", args=("ClassicNightstand_01", "", "aged")),
        dict(key="WoodenTable_01", w=256, h=256, kind="ph", args=("WoodenTable_01", "", "darken")),
        dict(key="wooden_bookshelf_worn", w=256, h=256, kind="ph", args=("wooden_bookshelf_worn", "", "darken")),
        dict(key="vintage_grandfather_clock_01", w=256, h=256, kind="ph",
             args=("vintage_grandfather_clock_01", "", "aged")),
        dict(key="side_table_tall_01", w=128, h=128, kind="ph", args=("side_table_tall_01", "", "darken")),
        dict(key="wood_varnish", w=256, h=128, kind="trim", args=(0,), m=(0.5, 0.25)),
        dict(key="wood_varnish2", w=256, h=128, kind="trim", args=(1,), m=(0.5, 0.25)),
        dict(key="wood_paint_dark", w=256, h=128, kind="trim", args=(3,), m=(0.5, 0.25)),
        dict(key="wood_raw", w=256, h=128, kind="trim", args=(5,), m=(0.5, 0.25)),
        dict(key="wood_brass", w=128, h=64, kind="trim", args=(7,), m=(0.25, 0.125)),
        dict(key="wood_iron", w=128, h=64, kind="gen", args=("iron",), m=(0.25, 0.125)),
    ],
    "Small": [
        dict(key="anthurium_botany_01", w=256, h=256, kind="ph", args=("anthurium_botany_01", "", "plant"),
             alpha=True),
        dict(key="fancy_picture_frame_01", w=256, h=256, kind="ph", args=("fancy_picture_frame_01", "", "aged")),
        dict(key="hanging_picture_frame_02", w=128, h=128, kind="ph", args=("hanging_picture_frame_02", "", "aged")),
        dict(key="hanging_picture_frame_02_artwork", w=192, h=128, kind="ph",
             args=("hanging_picture_frame_02", "artwork", "old_photo")),
        dict(key="canvas_landscape", w=256, h=192, kind="gen", args=("landscape",)),
        dict(key="canvas_portrait", w=192, h=256, kind="gen", args=("portrait",)),
        dict(key="canvas_bedroom", w=256, h=128, kind="gen", args=("seascape",)),
        dict(key="mantel_clock_01", w=192, h=192, kind="ph", args=("mantel_clock_01", "", "aged")),
        dict(key="vintage_suitcase_01", w=192, h=192, kind="ph", args=("vintage_suitcase", "01", "aged")),
        dict(key="vintage_suitcase_02", w=128, h=128, kind="ph", args=("vintage_suitcase", "02", "aged")),
        dict(key="small_wooden_table_01", w=192, h=192, kind="ph", args=("small_wooden_table_01", "", "darken")),
        dict(key="painted_wooden_bench", w=192, h=192, kind="ph", args=("painted_wooden_bench", "", "dusty")),
        dict(key="cardboard_box_01", w=256, h=256, kind="ph", args=("cardboard_box_01", "", "dusty")),
        dict(key="ceramic_vase_01", w=128, h=128, kind="ph", args=("ceramic_vase_01", "", "dusty")),
        dict(key="ceramic_vase_03", w=128, h=128, kind="ph", args=("ceramic_vase_03", "", "dusty")),
        dict(key="wall_clock", w=128, h=128, kind="ph", args=("wall_clock", "", "aged")),
        dict(key="pot_enamel_01", w=128, h=128, kind="ph", args=("pot_enamel_01", "", "dusty")),
        dict(key="brass_pan_01", w=128, h=128, kind="ph", args=("brass_pan_01", "", "dusty")),
        dict(key="magazines", w=256, h=256, kind="gen", args=("magazines",)),
        dict(key="vcr", w=128, h=128, kind="gen", args=("vcr",)),
        dict(key="speaker", w=128, h=128, kind="gen", args=("speaker",)),
        dict(key="doily", w=128, h=128, kind="gen", args=("doily",), alpha=True),
        dict(key="paper", w=128, h=128, kind="gen", args=("paper",), m=(0.3, 0.3)),
        dict(key="newspaper", w=128, h=128, kind="gen", args=("newspaper",), m=(0.45, 0.45)),
        dict(key="glass_green", w=64, h=64, kind="gen", args=("glass_green",), m=(0.15, 0.15)),
        dict(key="glass_brown", w=64, h=64, kind="gen", args=("glass_brown",), m=(0.15, 0.15)),
        dict(key="label", w=64, h=64, kind="gen", args=("label",), m=(0.08, 0.08)),
        dict(key="ceramic", w=64, h=64, kind="gen", args=("ceramic",), m=(0.2, 0.2)),
        dict(key="plaster_chunk", w=64, h=64, kind="gen", args=("plaster_chunk",), m=(0.2, 0.2)),
        dict(key="leaf", w=64, h=64, kind="gen", args=("leaf",), m=(0.2, 0.2)),
        dict(key="small_varnish", w=128, h=64, kind="trim", args=(0,), m=(0.25, 0.125)),
        dict(key="small_paint_dark", w=128, h=64, kind="trim", args=(3,), m=(0.25, 0.125)),
        dict(key="small_brass", w=128, h=64, kind="trim", args=(7,), m=(0.25, 0.125)),
        dict(key="small_metal_paint", w=128, h=64, kind="trim", args=(6,), m=(0.25, 0.125)),
        dict(key="small_iron", w=64, h=64, kind="gen", args=("iron",), m=(0.15, 0.15)),
        dict(key="small_black", w=64, h=64, kind="gen", args=("black_plastic",), m=(0.15, 0.15)),
        dict(key="small_cardboard", w=128, h=128, kind="gen", args=("cardboard",), m=(0.5, 0.5)),
    ],
    "Utility": [
        dict(key="portable_generator", w=512, h=512, kind="ph", args=("portable_generator", "", "generator")),
        dict(key="electric_stove", w=384, h=384, kind="ph", args=("electric_stove", "", "grimy")),
        dict(key="steel_frame_shelves_01", w=256, h=256, kind="ph", args=("steel_frame_shelves_01", "", "grimy")),
        dict(key="painted_wooden_cabinet", w=256, h=256, kind="ph", args=("painted_wooden_cabinet", "", "kitchen")),
        dict(key="painted_wooden_cabinet_02", w=256, h=256, kind="ph",
             args=("painted_wooden_cabinet_02", "", "kitchen")),
        dict(key="painted_wooden_chair_01", w=192, h=192, kind="ph", args=("painted_wooden_chair_01", "", "kitchen")),
        dict(key="wooden_crate_01", w=192, h=192, kind="ph", args=("wooden_crate_01", "", "grimy")),
        dict(key="wooden_crate_02", w=128, h=128, kind="ph", args=("wooden_crate_02", "", "grimy")),
        dict(key="trashbag", w=192, h=192, kind="ph", args=("trashbag", "", "none")),
        dict(key="barrel_03", w=128, h=128, kind="ph", args=("barrel_03", "", "grimy")),
        dict(key="metal_jerrycan_green", w=128, h=128, kind="ph", args=("metal_jerrycan_green", "", "grimy")),
        dict(key="power_box_01", w=128, h=128, kind="ph", args=("power_box_01", "", "grimy")),
        dict(key="bench_vice_01", w=128, h=128, kind="ph", args=("bench_vice_01", "", "grimy")),
        dict(key="wooden_bucket_01", w=128, h=128, kind="ph", args=("wooden_bucket_01", "", "grimy")),
        dict(key="enamel_white", w=192, h=192, kind="gen", args=("enamel_white",), m=(0.6, 0.6)),
        dict(key="enamel_cream", w=192, h=192, kind="gen", args=("enamel_cream",), m=(0.6, 0.6)),
        dict(key="castiron", w=128, h=128, kind="gen", args=("castiron",), m=(0.4, 0.4)),
        dict(key="steel", w=128, h=64, kind="gen", args=("steel",), m=(0.25, 0.125)),
        dict(key="util_paint_light", w=256, h=128, kind="trim", args=(4,), m=(0.5, 0.25)),
        dict(key="util_raw", w=256, h=128, kind="trim", args=(5,), m=(0.5, 0.25)),
        dict(key="util_metal_paint", w=128, h=64, kind="trim", args=(6,), m=(0.25, 0.125)),
        dict(key="util_rubber", w=64, h=64, kind="gen", args=("black_plastic",), m=(0.15, 0.15)),
        dict(key="util_paper", w=64, h=64, kind="gen", args=("paper",), m=(0.3, 0.3)),
        dict(key="glass_jar", w=64, h=64, kind="gen", args=("glass_jar",), m=(0.15, 0.15)),
    ],
}
ALPHA_ATLASES = {"Small"}   # Base alpha is a cutout mask (plant leaves, doily lace); the others are opaque


# ------------------------------------------------------------------ small image helpers (float arrays, 0..1)
def rng(seed):
    return np.random.default_rng(seed)


def noise(h, w, cells, seed, octaves=4, persist=0.5):
    """Tileable fBm value noise in 0..1. cells = grid cells across the width at the first octave."""
    r = rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        c = max(1, int(cells * 2 ** o))
        ch = max(1, int(round(c * h / w)))
        g = r.random((ch, c)).astype(np.float32)
        big = np.tile(g, (3, 3))
        im = Image.fromarray((big * 255).astype(np.uint8)).resize((w * 3, h * 3), Image.BICUBIC)
        a = np.asarray(im, np.float32)[h:2 * h, w:2 * w] / 255.0
        out += a * amp
        tot += amp
        amp *= persist
    return out / tot


def smooth(x, a, b):
    t = np.clip((x - a) / (b - a + 1e-9), 0, 1)
    return t * t * (3 - 2 * t)


def normal_from_height(hm, strength):
    dx = (np.roll(hm, -1, 1) - np.roll(hm, 1, 1)) * strength
    dy = (np.roll(hm, -1, 0) - np.roll(hm, 1, 0)) * strength
    n = np.dstack([-dx, dy, np.ones_like(hm)])     # OpenGL Y+ (image rows go down, so +dy flips)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5


def flat_normal(h, w):
    return np.dstack([np.full((h, w), 0.5), np.full((h, w), 0.5), np.ones((h, w))]).astype(np.float32)


def mask(h, w, metal=0.0, ao=1.0, smooth_=0.3):
    def f(v):
        return v if isinstance(v, np.ndarray) else np.full((h, w), v, np.float32)
    return np.dstack([f(metal), f(ao), np.zeros((h, w), np.float32), f(smooth_)])


def load(path, size, mode="RGB"):
    im = Image.open(path).convert(mode)
    return np.asarray(im.resize(size, Image.LANCZOS), np.float32) / 255.0


def lum(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def to_lin(c):
    return np.power(np.clip(c, 0, 1), 2.2)


def to_srgb(c):
    return np.power(np.clip(c, 0, 1), 1 / 2.2)


def palette(idx, cols):
    cols = np.asarray(cols, np.float32) / 255.0
    return cols[np.clip(idx, 0, len(cols) - 1)]


# ------------------------------------------------------------------ Poly Haven texture sets
def ph_paths(asset, sub):
    """(diff, nor_gl, arm) jpg paths for a model (Mansion folder in the repo, else the download root)."""
    pre = asset + ("_" + sub if sub else "")
    cands = []
    local = os.path.join(MANSION_PH, asset)
    if os.path.isdir(local):
        cands.append((local, os.path.join(local, "Source~")))
    if SRC:
        d = os.path.join(SRC, "dl_" + asset)
        cands.append((d, d))
    for d, s in cands:
        diff = [os.path.join(d, f) for f in os.listdir(d) if f.startswith(pre + "_diff") and f.endswith(".jpg")] \
            if os.path.isdir(d) else []
        if not diff:
            continue
        # Mansion names may differ in case (Armchair_01_diff vs ArmChair_01): match case-insensitively
        def find(folder, tag):
            if not os.path.isdir(folder):
                return None
            for f in os.listdir(folder):
                fl = f.lower()
                if fl.startswith((pre + "_" + tag).lower()) and fl.endswith((".jpg", ".png")):
                    return os.path.join(folder, f)
            return None
        return diff[0], find(d, "nor_gl"), find(s, "arm") or find(d, "arm")
    # case-insensitive fallback for the Mansion files (e.g. Armchair_01_diff_1k.jpg)
    for d, s in cands:
        if not os.path.isdir(d):
            continue
        fs = {f.lower(): os.path.join(d, f) for f in os.listdir(d)}
        ss = {f.lower(): os.path.join(s, f) for f in os.listdir(s)} if os.path.isdir(s) else {}
        p = pre.lower()
        diff = fs.get(p + "_diff_1k.jpg")
        if diff:
            return diff, fs.get(p + "_nor_gl_1k.jpg"), ss.get(p + "_arm_1k.jpg") or fs.get(p + "_arm_1k.jpg")
    raise FileNotFoundError(f"no Poly Haven maps for {asset} {sub}")


def grade(base, kind, seed):
    """Per-model grading towards the abandoned look (linear-space ops on sRGB input). Returns sRGB."""
    h, w, _ = base.shape
    lin = to_lin(base)
    L = lum(lin)
    sat = base.max(2) - base.min(2)
    dust = noise(h, w, 6, seed, 4)
    if kind in ("fabric_dark", "fabric_brown"):
        # the light upholstery of Sofa_01 / ArmChair_01 -> dark worn fabric (ref 2); the wood stays
        fab = smooth(L, 0.18, 0.35) * (1 - smooth(sat, 0.10, 0.25))
        tint = np.array([0.040, 0.030, 0.024] if kind == "fabric_dark" else [0.062, 0.036, 0.022], np.float32)
        detail = L / (L.mean() + 1e-6)
        new = tint * detail[..., None] * (0.75 + 0.5 * dust[..., None])
        lin = lin * (1 - fab[..., None]) + new * fab[..., None]
        lin *= 0.85
    elif kind == "bed":
        # mattress / sheet areas -> stained, yellowed linen
        fab = smooth(L, 0.25, 0.45) * (1 - smooth(sat, 0.08, 0.2))
        stain = smooth(noise(h, w, 5, seed + 3, 4), 0.55, 0.8)
        new = np.array([0.52, 0.45, 0.33], np.float32) * (L / (L.mean() + 1e-6))[..., None] * 0.3
        new *= (1 - 0.45 * stain)[..., None] * np.array([1.0, 0.92, 0.75], np.float32)
        lin = lin * (1 - fab[..., None]) + new * fab[..., None]
        lin *= 0.9
    elif kind == "aged":
        lin *= (0.78 + 0.15 * dust)[..., None]
        g = lum(lin)[..., None]
        lin = lin * 0.85 + g * 0.15
    elif kind == "darken":
        lin *= (0.55 + 0.15 * dust)[..., None]
        g = lum(lin)[..., None]
        lin = lin * 0.8 + g * 0.2
    elif kind == "dusty":
        g = lum(lin)[..., None]
        lin = (lin * 0.8 + g * 0.2) * (0.8 + 0.12 * dust)[..., None]
        lin = lin * 0.9 + np.array([0.06, 0.055, 0.045], np.float32) * smooth(dust, 0.55, 0.85)[..., None]
    elif kind == "grimy":
        grime = smooth(noise(h, w, 8, seed + 5, 5), 0.5, 0.85)
        lin *= (1 - 0.35 * grime)[..., None] * np.array([1.0, 0.97, 0.9], np.float32)
    elif kind == "kitchen":
        # painted kitchen furniture: desaturate towards a dirty blue-white
        g = lum(lin)[..., None]
        lin = lin * 0.6 + g * np.array([0.38, 0.42, 0.44], np.float32) * 1.0
        grime = smooth(noise(h, w, 8, seed + 5, 5), 0.55, 0.85)
        lin *= (0.92 - 0.3 * grime)[..., None]
    elif kind == "generator":
        grime = smooth(noise(h, w, 8, seed + 5, 5), 0.55, 0.9)
        lin *= (0.95 - 0.3 * grime)[..., None]
    elif kind == "plant":
        g = lum(lin)[..., None]
        lin = (lin * 0.75 + g * 0.25) * np.array([0.85, 0.85, 0.7], np.float32)
    elif kind == "old_photo":
        g = lum(lin)[..., None]
        lin = g * np.array([1.05, 0.9, 0.65], np.float32) * 0.85
    return to_srgb(np.clip(lin, 0.004, 0.85))


def cell_ph(c, w, h):
    asset, sub, gk = c["args"]
    diff, nor, arm = ph_paths(asset, sub)
    base = load(diff, (w, h))
    alpha = None
    if c.get("alpha"):
        ap = os.path.join(os.path.dirname(diff), os.path.basename(diff).replace("_diff_", "_alpha_")
                          .replace(".jpg", ".png"))
        if os.path.exists(ap):
            alpha = load(ap, (w, h), "L")
        else:
            rgba = Image.open(diff)
            alpha = np.ones((h, w), np.float32)
    base = grade(base, gk, zlib.crc32(c["key"].encode()) % 9999)
    if nor:
        n = load(nor, (w, h)) * 2 - 1
        n /= np.linalg.norm(n, axis=2, keepdims=True) + 1e-6
        n = n * 0.5 + 0.5
    else:
        n = flat_normal(h, w)
    if arm:
        a = load(arm, (w, h))
        m = mask(h, w, metal=a[..., 2], ao=a[..., 0], smooth_=1 - a[..., 1])
    else:
        m = mask(h, w, 0, 1, 0.25)
    if gk in ("aged", "darken", "dusty", "grimy", "kitchen", "fabric_dark", "fabric_brown", "bed"):
        m[..., 3] *= 0.8   # dust dulls the scans a little
    return base, n, m, alpha


def cell_trim(c, w, h):
    k = c["args"][0]
    y0 = RES - (k + 1) * 128 + 4
    box = (0, y0, 256, y0 + 120)       # the strip without its own 4 px insets: 0.5 m x 0.234 m

    def crop(name, mode):
        im = Image.open(os.path.join(TRIM, name)).convert(mode).crop(box).resize((w, h), Image.LANCZOS)
        return np.asarray(im, np.float32) / 255.0
    return crop("Trim_Albedo.png", "RGB"), crop("Trim_Normal.png", "RGB"), crop("Trim_Mask.png", "RGBA"), None


# ------------------------------------------------------------------ generators: return (base sRGB, normal, mask, alpha)
GEN = {}


def gen(fn):
    GEN[fn.__name__[2:]] = fn
    return fn


def carpet_detail(h, w, seed):
    """Pile detail from Poly Haven dirty_carpet (luminance ratio + its normal), tiled to the cell."""
    d = os.path.join(SRC, "dl_dirty_carpet")
    dif = load(os.path.join(d, "dirty_carpet_diff_1k.jpg"), (512, 512))
    nor = load(os.path.join(d, "dirty_carpet_nor_gl_1k.jpg"), (512, 512))
    L = lum(to_lin(dif))
    det = L / L.mean()
    r = rng(seed)
    oy, ox = r.integers(0, 256), r.integers(0, 256)
    det = np.roll(np.roll(det, oy, 0), ox, 1)[:h, :w] if h <= 512 and w <= 512 else np.resize(det, (h, w))
    nor = np.roll(np.roll(nor, oy, 0), ox, 1)[:h, :w]
    return np.clip(det, 0.6, 1.5), nor


def rug_finish(idx, cols, h, w, seed, wear=0.35):
    col = palette(idx, cols)
    det, nor = carpet_detail(h, w, seed)
    lin = to_lin(col) * det[..., None]
    worn = smooth(noise(h, w, 3, seed + 1, 4), 0.55, 0.85) * wear
    lin = lin * (1 - worn[..., None]) + to_lin(np.array([0.3, 0.26, 0.2], np.float32)) * worn[..., None] * det[..., None]
    dirt = smooth(noise(h, w, 6, seed + 2, 5), 0.6, 0.9)
    lin *= (1 - 0.4 * dirt)[..., None]
    return to_srgb(lin), nor, mask(h, w, 0, 0.9 + 0.1 * det / 1.5, 0.08), None


def _uv(h, w):
    v, u = np.mgrid[0:h, 0:w].astype(np.float32)
    return (u + 0.5) / w, (v + 0.5) / h


@gen
def g_rug_or_field(w, h):
    u, v = _uv(h, w)
    fu, fv = np.abs(u - 0.5) * 2, np.abs(v - 0.5) * 2         # mirrored: tiles seamlessly in every flip
    r = np.hypot(fu, fv)
    th = np.arctan2(fv, fu)
    idx = np.zeros((h, w), int)                               # 0 maroon ground
    petals = 0.55 + 0.12 * np.cos(8 * th)
    idx[r < petals] = 2                                       # navy medallion
    idx[(r < petals) & (r > petals - 0.07)] = 3               # cream outline
    idx[r < 0.32 + 0.06 * np.cos(4 * th)] = 1                 # red heart
    idx[r < 0.14] = 4                                         # gold centre
    dia = fu + fv
    idx[(dia > 1.55) & (dia < 1.68)] = 3                      # lattice between medallions
    idx[(np.abs(fu - fv) < 0.05) & (r > 0.75)] = 4
    sprig = (np.sin(fu * 40) * np.sin(fv * 40) > 0.75) & (idx == 0)
    idx[sprig] = 5
    return rug_finish(idx, [(92, 22, 24), (140, 36, 30), (30, 32, 52), (196, 176, 136), (176, 132, 60), (40, 18, 16)],
                      h, w, 11)


@gen
def g_rug_or_border(w, h):
    u, v = _uv(h, w)
    idx = np.full((h, w), 2, int)                             # navy band
    idx[(v < 0.12) | (v > 0.88)] = 0
    idx[((v > 0.12) & (v < 0.18)) | ((v > 0.82) & (v < 0.88))] = 3
    ph = (u * 2) % 1.0                                        # 2 motifs per cell, seamless along U
    wave = 0.5 + 0.22 * np.sin(ph * 2 * np.pi)
    idx[(np.abs(v - wave) < 0.07) & (v > 0.2) & (v < 0.8)] = 4
    blob = np.hypot((ph - 0.5) * 2.2, (v - 0.5) * 3.2) < 0.45
    idx[blob] = 1
    idx[np.hypot((ph - 0.5) * 2.2, (v - 0.5) * 3.2) < 0.18] = 3
    return rug_finish(idx, [(92, 22, 24), (140, 36, 30), (30, 32, 52), (196, 176, 136), (176, 132, 60)], h, w, 12)


@gen
def g_rug_or_corner(w, h):
    u, v = _uv(h, w)
    r = np.hypot(u - 0.5, v - 0.5) * 2
    idx = np.full((h, w), 2, int)
    idx[r < 0.8] = 1
    idx[r < 0.45] = 3
    idx[r < 0.25] = 4
    return rug_finish(idx, [(92, 22, 24), (140, 36, 30), (30, 32, 52), (196, 176, 136), (176, 132, 60)], h, w, 13)


@gen
def g_rug_geo_field(w, h):
    # ref 3: black and off-white stepped diamonds (Navajo-like)
    u, v = _uv(h, w)
    fu, fv = np.abs(u - 0.5) * 2, np.abs(v - 0.5) * 2
    q = np.floor((fu + fv) * 6) / 6                           # stepped
    idx = np.where((q * 6).astype(int) % 2 == 0, 0, 1)
    idx[(fu + fv) < 0.2] = 2
    return rug_finish(idx, [(208, 200, 180), (34, 32, 30), (120, 40, 30)], h, w, 21, wear=0.25)


@gen
def g_rug_geo_border(w, h):
    u, v = _uv(h, w)
    zz = np.abs(((u * 4) % 1.0) - 0.5) * 2                    # zig-zag, 4 teeth per cell
    idx = np.where(v > 0.25 + 0.5 * zz, 0, 1)
    idx[v < 0.12] = 1
    idx[v > 0.9] = 0
    return rug_finish(idx, [(208, 200, 180), (34, 32, 30)], h, w, 22, wear=0.25)


@gen
def g_rug_geo_corner(w, h):
    u, v = _uv(h, w)
    idx = np.where((np.floor(u * 4) + np.floor(v * 4)) % 2 == 0, 1, 0)
    return rug_finish(idx, [(208, 200, 180), (34, 32, 30)], h, w, 23, wear=0.25)


@gen
def g_runner_field(w, h):
    # ref 1: dark red runner with a small repeating motif; repeats along U, mirrored across V
    u, v = _uv(h, w)
    fu = np.abs(((u * 2) % 1.0) - 0.5) * 2
    fv = np.abs(v - 0.5) * 2
    idx = np.zeros((h, w), int)
    d = fu + fv * 1.3
    idx[d < 0.9] = 1
    idx[d < 0.55] = 2
    idx[d < 0.25] = 3
    idx[(np.abs(fu - 0.0) < 0.06) & (fv > 0.7)] = 2
    return rug_finish(idx, [(98, 24, 22), (64, 18, 18), (182, 150, 104), (32, 30, 44)], h, w, 31)


@gen
def g_runner_border(w, h):
    u, v = _uv(h, w)
    idx = np.zeros((h, w), int)
    idx[(v > 0.35) & (v < 0.65)] = 2
    idx[((u * 6) % 1.0 < 0.5) & (v > 0.42) & (v < 0.58)] = 1
    return rug_finish(idx, [(40, 20, 20), (182, 150, 104), (98, 24, 22)], h, w, 32)


@gen
def g_plaid(w, h):
    # ref 3 settee: dark grey-green tartan woven on the fabric_pattern_05 base
    u, v = _uv(h, w)

    def sett(t):
        x = (t * 2) % 1.0
        s = np.zeros_like(t)
        s += (x < 0.18) * 1.0                                 # wide dark band
        s += ((x > 0.45) & (x < 0.52)) * 0.6                  # mid stripe
        s += ((x > 0.74) & (x < 0.76)) * 2.0                  # thin bright line
        return s
    a, b = sett(u), sett(v)
    base = np.array([0.20, 0.22, 0.19], np.float32)
    dark = np.array([0.08, 0.10, 0.09], np.float32)
    line = np.array([0.42, 0.36, 0.22], np.float32)
    lin = np.empty((h, w, 3), np.float32)
    lin[:] = to_lin(base)
    k = np.clip((a == 1) * 0.5 + (b == 1) * 0.5, 0, 1)
    lin = lin * (1 - k[..., None]) + to_lin(dark) * k[..., None]
    m = np.clip(((a == 0.6) | (b == 0.6)) * 0.6, 0, 1)
    lin = lin * (1 - m[..., None]) + to_lin(np.array([0.17, 0.12, 0.10], np.float32)) * m[..., None]
    l2 = ((a == 2) | (b == 2)) * 1.0
    lin = lin * (1 - l2[..., None]) + to_lin(line) * l2[..., None]
    d = os.path.join(SRC, "dl_fabric_pattern_05")
    weave = lum(to_lin(load(os.path.join(d, "fabric_pattern_05_col_03_1k.jpg"), (w * 2, h * 2))))
    weave = np.asarray(Image.fromarray((weave / weave.max() * 255).astype(np.uint8)).resize((w, h)), np.float32) / 255
    lin *= (weave / weave.mean())[..., None] * (0.85 + 0.25 * noise(h, w, 3, 41, 3))[..., None]
    nor = load(os.path.join(d, "fabric_pattern_05_nor_gl_1k.jpg"), (w, h))
    return to_srgb(lin), nor, mask(h, w, 0, 1, 0.12), None


@gen
def g_curtain(w, h):
    u, v = _uv(h, w)
    folds = 0.5 + 0.5 * np.sin(u * 2 * np.pi * 2)
    flor = (np.sin(u * 2 * np.pi * 3 + np.sin(v * 2 * np.pi * 4) * 1.5) * np.sin(v * 2 * np.pi * 6) > 0.6) * 1.0
    col = np.array([0.30, 0.33, 0.24], np.float32)
    lin = to_lin(col) * (0.7 + 0.45 * folds)[..., None]
    lin = lin * (1 - 0.25 * flor[..., None])
    stain = smooth(noise(h, w, 3, 51, 4), 0.5, 0.8) * smooth(1 - v, 0.0, 0.5)
    lin *= (1 - 0.35 * stain)[..., None]
    hm = folds * 0.5 + noise(h, w, 16, 52, 2) * 0.1
    return to_srgb(lin), normal_from_height(hm, 3.0), mask(h, w, 0, 0.8 + 0.2 * folds, 0.1), None


@gen
def g_blanket(w, h):
    u, v = _uv(h, w)
    knit = 0.5 + 0.5 * np.sin(v * 2 * np.pi * 48) * np.sin(u * 2 * np.pi * 36)
    band = ((v * 4) % 1.0 < 0.12) * 1.0
    lin = to_lin(np.array([0.36, 0.14, 0.10], np.float32)) * (0.8 + 0.3 * knit)[..., None]
    lin = lin * (1 - band[..., None]) + to_lin(np.array([0.55, 0.48, 0.36], np.float32)) * band[..., None]
    lin *= (0.75 + 0.3 * noise(h, w, 4, 61, 4))[..., None]
    return to_srgb(lin), normal_from_height(knit * 0.3, 2.0), mask(h, w, 0, 1, 0.08), None


@gen
def g_linen(w, h):
    n = noise(h, w, 4, 71, 5)
    weave = noise(h, w, 128, 72, 1)
    lin = to_lin(np.array([0.62, 0.57, 0.47], np.float32)) * (0.8 + 0.25 * n)[..., None]
    stain = smooth(noise(h, w, 3, 73, 4), 0.55, 0.8)
    lin *= (1 - 0.4 * stain)[..., None] * np.array([1, 0.95, 0.82], np.float32)
    return to_srgb(lin), normal_from_height(weave * 0.2, 2.0), mask(h, w, 0, 1, 0.1), None


@gen
def g_towel(w, h):
    u, v = _uv(h, w)
    terry = noise(h, w, 64, 81, 2)
    stripe = ((v > 0.15) & (v < 0.2)) | ((v > 0.8) & (v < 0.85))
    lin = to_lin(np.array([0.62, 0.6, 0.52], np.float32)) * (0.75 + 0.35 * terry)[..., None]
    lin[stripe] = to_lin(np.array([0.2, 0.32, 0.38], np.float32))
    lin *= (0.8 + 0.2 * noise(h, w, 3, 82, 3))[..., None]
    return to_srgb(lin), normal_from_height(terry * 0.6, 2.0), mask(h, w, 0, 1, 0.05), None


def painterly(img, seed, crack=True):
    """Oil-paint finish for the generated canvases: brush noise, yellowed varnish, craquelure, dark edges."""
    h, w, _ = img.shape
    lin = to_lin(img)
    brush = noise(h, w, 40, seed, 2)
    lin *= (0.85 + 0.3 * brush)[..., None]
    lin *= np.array([1.0, 0.92, 0.7], np.float32)              # varnish
    u, v = _uv(h, w)
    vig = 1 - 0.55 * smooth(np.hypot(u - 0.5, v - 0.5), 0.3, 0.75)
    lin *= vig[..., None]
    hm = brush * 0.3
    if crack:
        c = noise(h, w, 18, seed + 1, 3)
        cr = (np.abs(c - 0.5) < 0.012) * 1.0
        lin *= (1 - 0.5 * cr)[..., None]
        hm -= cr * 0.3
    return to_srgb(lin), normal_from_height(hm, 1.5)


@gen
def g_landscape(w, h):
    # ref 2: meadow with trees under a grey sky, purple wildflowers, a pale path
    u, v = _uv(h, w)
    img = np.zeros((h, w, 3), np.float32)
    sky = np.array([0.66, 0.7, 0.68]) * (1 - v[..., None] * 0.3) + noise(h, w, 4, 91, 4)[..., None] * 0.12
    img[:] = sky
    hills = 0.42 + 0.06 * np.sin(u * 7 + 1) + 0.05 * noise(h, w, 3, 92, 3)
    img[v > hills] = np.array([0.36, 0.42, 0.3])
    trees = (noise(h, w, 10, 93, 4) > 0.55) & (v > hills - 0.18) & (v < hills + 0.08)
    img[trees] = np.array([0.18, 0.26, 0.17]) * (0.8 + 0.4 * noise(h, w, 20, 94, 2)[trees, None])
    meadow = v > 0.62
    img[meadow] = np.array([0.4, 0.46, 0.26]) * (0.8 + 0.4 * noise(h, w, 30, 95, 2)[meadow, None])
    flowers = meadow & (noise(h, w, 50, 96, 2) > 0.62)
    img[flowers] = np.array([0.5, 0.38, 0.55])
    path = meadow & (np.abs(u - (0.55 + (v - 0.62) * 0.9)) < (v - 0.6) * 0.3)
    img[path] = np.array([0.66, 0.6, 0.46])
    base, nor = painterly(img, 97)
    return base, nor, mask(h, w, 0, 1, 0.35), None


@gen
def g_portrait(w, h):
    # oval portrait (ref 2, decision 9 generated): a dim Victorian sitter on a dark olive ground
    u, v = _uv(h, w)
    img = np.zeros((h, w, 3), np.float32)
    img[:] = np.array([0.16, 0.15, 0.1]) * (0.7 + 0.6 * noise(h, w, 3, 101, 4))[..., None]
    shoulders = ((u - 0.5) / 0.48) ** 2 + ((v - 1.05) / 0.42) ** 2 < 1
    img[shoulders] = np.array([0.07, 0.065, 0.06])
    collar = shoulders & (((u - 0.5) / 0.14) ** 2 + ((v - 0.66) / 0.1) ** 2 < 1)
    img[collar] = np.array([0.6, 0.57, 0.5])
    neck = (np.abs(u - 0.5) < 0.07) & (v > 0.52) & (v < 0.66)
    img[neck] = np.array([0.55, 0.43, 0.34])
    face = ((u - 0.5) / 0.16) ** 2 + ((v - 0.43) / 0.21) ** 2 < 1
    shade = smooth(u, 0.35, 0.62)
    img[face] = (np.array([0.66, 0.53, 0.43]) * (1 - 0.35 * shade[face, None]))
    hair = (((u - 0.5) / 0.19) ** 2 + ((v - 0.36) / 0.22) ** 2 < 1) & ~(face & (v > 0.3))
    img[hair] = np.array([0.1, 0.07, 0.05])
    for ex in (0.44, 0.56):                                   # dark, slightly too-large eyes
        eye = ((u - ex) / 0.03) ** 2 + ((v - 0.42) / 0.016) ** 2 < 1
        img[eye] = np.array([0.05, 0.04, 0.035])
    mouth = (np.abs(u - 0.5) < 0.035) & (np.abs(v - 0.53) < 0.006)
    img[mouth] = np.array([0.32, 0.2, 0.18])
    img = np.asarray(Image.fromarray((img * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2.2)),
                     np.float32) / 255
    base, nor = painterly(img, 103)
    return base, nor, mask(h, w, 0, 1, 0.35), None


@gen
def g_seascape(w, h):
    # bedroom picture (ref 3's wide framed picture): grey sea, pale sky, a dark headland
    u, v = _uv(h, w)
    img = np.zeros((h, w, 3), np.float32)
    img[:] = np.array([0.62, 0.64, 0.6]) + noise(h, w, 3, 111, 4)[..., None] * 0.1
    sea = v > 0.55
    img[sea] = np.array([0.28, 0.33, 0.33]) * (0.8 + 0.4 * noise(h, w, 30, 112, 2)[sea, None])
    land = (v > 0.4 + 0.25 * u) & (u < 0.4)
    img[land] = np.array([0.15, 0.16, 0.12])
    base, nor = painterly(img, 113)
    return base, nor, mask(h, w, 0, 1, 0.35), None


@gen
def g_magazines(w, h):
    r = rng(121)
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    cols = [(150, 40, 36), (40, 70, 120), (200, 170, 60), (70, 110, 70)]
    for i in range(4):
        x0, y0 = (i % 2) * w // 2, (i // 2) * h // 2
        cw, ch = w // 2, h // 2
        d.rectangle([x0, y0, x0 + cw, y0 + ch], fill=cols[i])
        d.rectangle([x0 + 4, y0 + 4, x0 + cw - 5, y0 + 22], fill=(235, 230, 215))
        for k in range(5):
            d.rectangle([x0 + 8 + k * 22, y0 + 8, x0 + 22 + k * 22, y0 + 18], fill=tuple(int(c * 0.4) for c in cols[i]))
        px, py = x0 + 10 + r.integers(0, 20), y0 + 32
        d.ellipse([px, py, px + 70, py + 70], fill=tuple(int(min(255, c * 1.4)) for c in cols[(i + 1) % 4]))
        for k in range(4):
            yy = y0 + ch - 30 + k * 6
            d.rectangle([x0 + 8, yy, x0 + 8 + r.integers(30, cw - 16), yy + 3], fill=(240, 236, 220))
    a = np.asarray(img, np.float32) / 255
    lin = to_lin(a) * (0.75 + 0.2 * noise(h, w, 6, 122, 3))[..., None]
    return to_srgb(lin), flat_normal(h, w), mask(h, w, 0, 1, 0.45), None


@gen
def g_vcr(w, h):
    img = Image.new("RGB", (w, h), (34, 34, 36))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, h // 2], fill=(150, 150, 150))           # silver top half (lid)
    d.rectangle([8, h // 2 + 12, w - 50, h // 2 + 26], fill=(14, 14, 14))   # tape slot
    d.rectangle([w - 44, h // 2 + 10, w - 10, h // 2 + 22], fill=(60, 90, 70))   # display
    for k in range(5):
        d.rectangle([10 + k * 14, h - 16, 20 + k * 14, h - 10], fill=(180, 180, 180))
    a = np.asarray(img, np.float32) / 255
    a *= (0.8 + 0.2 * noise(h, w, 6, 131, 3))[..., None]
    sm = np.where(np.arange(h)[:, None] < h // 2, 0.55, 0.35) * np.ones((1, w))
    return a, flat_normal(h, w), mask(h, w, np.where(np.arange(h)[:, None] < h // 2, 0.6, 0.0) * np.ones((1, w)),
                                       1, sm.astype(np.float32)), None


@gen
def g_speaker(w, h):
    u, v = _uv(h, w)
    img = np.zeros((h, w, 3), np.float32)
    img[:] = np.array([0.12, 0.08, 0.06])                      # dark veneer
    for cx, cy, rr in ((0.5, 0.68, 0.27), (0.5, 0.25, 0.12)):
        r = np.hypot(u - cx, v - cy)
        img[r < rr] = np.array([0.05, 0.05, 0.05])
        img[(r < rr * 0.55)] = np.array([0.16, 0.16, 0.15])
        img[(r < rr * 0.2)] = np.array([0.08, 0.08, 0.08])
        img[(np.abs(r - rr) < 0.012)] = np.array([0.45, 0.44, 0.4])
    hm = np.zeros((h, w), np.float32)
    for cx, cy, rr in ((0.5, 0.68, 0.27), (0.5, 0.25, 0.12)):
        r = np.hypot(u - cx, v - cy)
        hm -= np.clip(1 - r / rr, 0, 1) * 0.6
    return img, normal_from_height(hm, 4.0), mask(h, w, 0, 1, 0.25), None


@gen
def g_doily(w, h):
    u, v = _uv(h, w)
    r = np.hypot(u - 0.5, v - 0.5) * 2
    th = np.arctan2(v - 0.5, u - 0.5)
    edge = 0.92 + 0.06 * np.cos(th * 18)
    lace = (np.sin(r * 40) * np.sin(th * 24) > -0.2) | (np.abs(np.sin(r * 20)) < 0.25)
    alpha = ((r < edge) & lace).astype(np.float32)
    img = np.ones((h, w, 3), np.float32) * np.array([0.78, 0.74, 0.64]) * (0.8 + 0.2 * noise(h, w, 4, 141, 3))[..., None]
    return img, flat_normal(h, w), mask(h, w, 0, 1, 0.1), alpha


@gen
def g_paper(w, h):
    u, v = _uv(h, w)
    lin = to_lin(np.array([0.72, 0.68, 0.58], np.float32)) * (0.85 + 0.2 * noise(h, w, 4, 151, 4))[..., None]
    lines = ((v * 18) % 1.0 < 0.08) & (u > 0.1) & (u < 0.9)
    lin[lines] *= 0.6
    stain = smooth(noise(h, w, 3, 152, 4), 0.6, 0.85)
    lin *= (1 - 0.4 * stain)[..., None] * np.array([1, 0.93, 0.78], np.float32)
    crumple = noise(h, w, 8, 153, 3)
    return to_srgb(lin), normal_from_height(crumple, 1.2), mask(h, w, 0, 1, 0.1), None


@gen
def g_newspaper(w, h):
    u, v = _uv(h, w)
    lin = to_lin(np.array([0.6, 0.58, 0.52], np.float32)) * (0.85 + 0.2 * noise(h, w, 4, 161, 4))[..., None]
    cols = (u * 4) % 1.0
    text = ((v * 40) % 1.0 < 0.45) & (cols > 0.08) & (cols < 0.92) & (v > 0.22)
    lin[text] *= 0.55
    head = (v < 0.16) & (v > 0.04) & (u > 0.08) & (u < 0.92)
    lin[head] *= 0.25
    photo = (u > 0.3) & (u < 0.7) & (v > 0.3) & (v < 0.55)
    lin[photo] = to_lin(np.array([0.3, 0.3, 0.3], np.float32)) * (0.6 + 0.8 * noise(h, w, 10, 162, 2))[photo, None]
    return to_srgb(lin), normal_from_height(noise(h, w, 8, 163, 3), 1.2), mask(h, w, 0, 1, 0.08), None


def _flat(col, h, w, seed, var=0.15, sm=0.5, metal=0.0, rough_noise=0.2):
    n = noise(h, w, 4, seed, 4)
    lin = to_lin(np.array(col, np.float32)) * (1 - var + 2 * var * n)[..., None]
    return to_srgb(lin), normal_from_height(noise(h, w, 16, seed + 1, 2) * 0.1, 1.0), \
        mask(h, w, metal, 1, np.clip(sm - rough_noise * n, 0, 1)), None


@gen
def g_glass_green(w, h):
    return _flat((0.1, 0.2, 0.12), h, w, 171, 0.2, 0.85)


@gen
def g_glass_brown(w, h):
    return _flat((0.22, 0.11, 0.04), h, w, 172, 0.2, 0.85)


@gen
def g_glass_jar(w, h):
    return _flat((0.36, 0.34, 0.24), h, w, 173, 0.3, 0.7)


@gen
def g_label(w, h):
    b, n, m, a = _flat((0.7, 0.62, 0.45), h, w, 174, 0.2, 0.15)
    b[h // 3:h // 3 + 6, 6:w - 6] *= 0.4
    b[h // 2:h // 2 + 3, 10:w - 10] *= 0.5
    return b, n, m, a


@gen
def g_ceramic(w, h):
    return _flat((0.78, 0.76, 0.7), h, w, 175, 0.08, 0.8)


@gen
def g_plaster_chunk(w, h):
    p = os.path.join(TEX_OUT, "M_Plaster", "M_Plaster_Base.png")
    if os.path.exists(p):
        b = load(p, (w, h))
        n = load(os.path.join(TEX_OUT, "M_Plaster", "M_Plaster_Normal.png"), (w, h))
        return b, n, mask(h, w, 0, 1, 0.1), None
    return _flat((0.7, 0.67, 0.6), h, w, 176, 0.15, 0.1)


@gen
def g_leaf(w, h):
    return _flat((0.18, 0.2, 0.1), h, w, 177, 0.3, 0.2)


@gen
def g_iron(w, h):
    b, n, m, a = _flat((0.1, 0.095, 0.09), h, w, 178, 0.3, 0.45, metal=0.85)
    rust = smooth(noise(h, w, 6, 179, 4), 0.6, 0.85)
    b = to_srgb(to_lin(b) * (1 - rust[..., None]) + to_lin(np.array([0.3, 0.14, 0.06], np.float32)) * rust[..., None])
    m[..., 0] *= 1 - rust
    m[..., 3] *= 1 - 0.7 * rust
    return b, n, m, a


@gen
def g_black_plastic(w, h):
    return _flat((0.05, 0.05, 0.05), h, w, 180, 0.2, 0.4)


@gen
def g_cardboard(w, h):
    b, n, m, a = _flat((0.5, 0.38, 0.24), h, w, 181, 0.15, 0.12)
    u, v = _uv(h, w)
    tape = np.abs(v - 0.5) < 0.06
    b[tape] = to_srgb(to_lin(b[tape]) * np.array([1.1, 1.0, 0.85]))
    return b, n, m, a


@gen
def g_steel(w, h):
    return _flat((0.55, 0.55, 0.53), h, w, 182, 0.15, 0.7, metal=1.0, rough_noise=0.4)


def _enamel(col, seed, w, h, chips=0.35):
    n = noise(h, w, 5, seed, 5)
    lin = to_lin(np.array(col, np.float32)) * (0.9 + 0.12 * n)[..., None]
    grime = smooth(noise(h, w, 4, seed + 1, 5), 0.5, 0.85)
    lin *= (1 - 0.45 * grime)[..., None] * np.array([1.0, 0.96, 0.86], np.float32)
    chip = smooth(noise(h, w, 24, seed + 2, 2), 1 - chips * 0.25, 1 - chips * 0.2)
    lin = lin * (1 - chip[..., None]) + to_lin(np.array([0.12, 0.1, 0.09], np.float32)) * chip[..., None]
    rust = smooth(noise(h, w, 10, seed + 3, 3), 0.72, 0.85)
    lin = lin * (1 - rust[..., None]) + to_lin(np.array([0.36, 0.17, 0.07], np.float32)) * rust[..., None]
    sm = 0.75 * (1 - 0.6 * grime) * (1 - chip) * (1 - rust) + 0.2
    return to_srgb(lin), normal_from_height(-chip * 0.5, 2.0), mask(h, w, 0, 1, sm.astype(np.float32)), None


@gen
def g_enamel_white(w, h):
    return _enamel((0.82, 0.82, 0.78), 191, w, h)


@gen
def g_enamel_cream(w, h):
    return _enamel((0.78, 0.74, 0.6), 192, w, h)


@gen
def g_castiron(w, h):
    b, n, m, a = _flat((0.07, 0.075, 0.07), h, w, 193, 0.3, 0.35)
    flake = smooth(noise(h, w, 12, 194, 3), 0.6, 0.7)
    b = to_srgb(to_lin(b) * (1 - flake[..., None]) + to_lin(np.array([0.32, 0.16, 0.08], np.float32)) * flake[..., None])
    return b, n, m, a


def cell_gen(c, w, h):
    return GEN[c["args"][0]](w, h)


# ------------------------------------------------------------------ packing and output
def pack(cells, size=RES):
    """Guillotine packer, largest first. Returns {key: (x, y)} (pixels, y down) or raises."""
    free = [(0, 0, size, size)]
    out = {}
    for c in sorted(cells, key=lambda c: (-c["h"], -c["w"])):
        best = None
        for i, (x, y, fw, fh) in enumerate(free):
            if c["w"] <= fw and c["h"] <= fh:
                score = (fw * fh - c["w"] * c["h"], y, x)
                if best is None or score < best[0]:
                    best = (score, i)
        if best is None:
            raise RuntimeError(f"atlas full at {c['key']}")
        x, y, fw, fh = free.pop(best[1])
        out[c["key"]] = (x, y)
        rw, rh = fw - c["w"], fh - c["h"]
        # split along the longer leftover
        if rw > rh:
            free += [(x + c["w"], y, rw, fh), (x, y + c["h"], c["w"], rh)]
        else:
            free += [(x, y + c["h"], fw, rh), (x + c["w"], y, rw, c["h"])]
        free = [f for f in free if f[2] > 0 and f[3] > 0]
    return out


def pad(a, p):
    return np.pad(a, [(p, p), (p, p)] + [(0, 0)] * (a.ndim - 2), mode="edge")


def build_atlas(name, cells):
    pos = pack(cells)
    base = np.zeros((RES, RES, 3), np.float32)
    alph = np.ones((RES, RES), np.float32)
    nrm = np.zeros((RES, RES, 3), np.float32)
    nrm[:] = (0.5, 0.5, 1.0)
    msk = np.zeros((RES, RES, 4), np.float32)
    msk[..., 1] = 1
    rects = {}
    used = 0
    for c in cells:
        x, y = pos[c["key"]]
        p = 4 if min(c["w"], c["h"]) >= 128 else 2
        iw, ih = c["w"] - 2 * p, c["h"] - 2 * p
        fn = {"ph": cell_ph, "trim": cell_trim, "gen": cell_gen}[c["kind"]]
        b, n, m, a = fn(c, iw, ih)
        base[y:y + c["h"], x:x + c["w"]] = pad(np.asarray(b, np.float32)[..., :3], p)
        nrm[y:y + c["h"], x:x + c["w"]] = pad(np.asarray(n, np.float32)[..., :3], p)
        msk[y:y + c["h"], x:x + c["w"]] = pad(np.asarray(m, np.float32), p)
        if a is not None:
            alph[y:y + c["h"], x:x + c["w"]] = pad(np.asarray(a, np.float32), p)
        used += c["w"] * c["h"]
        # UV rect of the inner area, V = 0 at the bottom of the image
        rects[c["key"]] = dict(uv=[(x + p) / RES, 1 - (y + p + ih) / RES, (x + p + iw) / RES, 1 - (y + p) / RES],
                               px=[x, y, c["w"], c["h"]], m=list(c.get("m", (0, 0))), kind=c["kind"])
    d = os.path.join(TEX_OUT, "Atlas_" + name)
    os.makedirs(d, exist_ok=True)
    u8 = lambda a: (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)  # noqa: E731
    if name in ALPHA_ATLASES:
        Image.fromarray(u8(np.dstack([base, alph])), "RGBA").save(os.path.join(d, f"Atlas_{name}_Base.png"))
    else:
        Image.fromarray(u8(base), "RGB").save(os.path.join(d, f"Atlas_{name}_Base.png"))
    Image.fromarray(u8(nrm), "RGB").save(os.path.join(d, f"Atlas_{name}_Normal.png"))
    Image.fromarray(u8(msk), "RGBA").save(os.path.join(d, f"Atlas_{name}_Mask.png"))
    fill = used / (RES * RES)
    print(f"Atlas_{name}: {len(cells)} cells, fill {fill:.1%}")
    return dict(cells=rects, fill=round(fill, 4), res=RES)


def main():
    global SRC
    a = sys.argv[1:]
    SRC = a[a.index("--src") + 1] if "--src" in a else None
    only = [x for x in a if not x.startswith("--") and x != SRC]
    data = {}
    if os.path.exists(ATLAS_JSON):
        with open(ATLAS_JSON, encoding="utf-8") as f:
            data = json.load(f).get("atlases", {})
    for name, cells in ATLASES.items():
        if only and name not in only:
            continue
        data[name] = build_atlas(name, cells)
    os.makedirs(os.path.dirname(ATLAS_JSON), exist_ok=True)
    with open(ATLAS_JSON, "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(source="tools/blender/bungalow_prop_textures.py", res=RES, atlases=data), f, indent=1)
    print("wrote", ATLAS_JSON)


if __name__ == "__main__":
    main()
