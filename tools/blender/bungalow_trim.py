"""BLINK Bungalow M_Trim trim sheet (Phase 3): strip layout + compositor.

The strip table (STRIPS, v_range) is plain data with no imports, so the Blender kit builder (bungalow_kit.py) maps
every trim piece onto its strip from the same numbers. The compositor needs numpy + Pillow and runs outside Blender:

  python -I tools/blender/bungalow_trim.py --download DIR       fetch the CC0 1K sources, one fresh folder per asset
  python -I tools/blender/bungalow_trim.py --src DIR [--out OUT] composite Trim_Albedo / Trim_Normal / Trim_Mask

-> Unity/Assets/Art/Environment/Bungalow/Textures/Trim/Trim_{Albedo,Normal,Mask}.png (1024 x 1024)

Sheet: 8 horizontal strips of 128 px. Strip k covers V = k/8 .. (k+1)/8 (V = 0 is the bottom row of the image, as in
Blender and Unity UVs). Each strip tiles along U. Kit UVs stay MARGIN_PX inside their strip so mips don't bleed.
Texel scale: 1 UV = TRIM_UV_M metres along U (wood grain runs along U); one strip is 0.25 m tall.
Maps: Albedo sRGB; Normal OpenGL (Y+); Mask linear = URP MetallicSmoothness: R metallic, G AO, B 0, A smoothness.

Sources (all CC0):
  Poly Haven dark_wood (Dimitrios Savva, Dario Barresi, Rico Cilliers)  -> strips 0-2 (varnished wood)
  ambientCG PaintedWood008C                                              -> strips 3-4 (painted wood)
  Poly Haven worn_planks (Dimitrios Savva)                               -> strip 5 (raw lath / joist wood)
  Poly Haven rusty_metal_02 (Rob Tuytel)                                 -> strips 6-7 (painted metal, brass)
"""
import os
import sys

SIZE = 1024
NSTRIP = 8
STRIP_PX = SIZE // NSTRIP      # 128
MARGIN_PX = 4
TRIM_UV_M = 2.0                # metres per UV along U (and V): 1 strip = 128 px = 0.25 m

# id: key used by the kit, what it looks like, what goes on it, source
STRIPS = [
    dict(id=0, key="varnish", look="dark-stained varnished wood, long grain",
         use="skirting, dado / picture rails, cornice, door and window casings, arch and O1 casings + linings, "
             "stringers, window frames and sashes (varnished rooms)", src="dark_wood"),
    dict(id=1, key="varnish_door", look="varnished wood, warmer and more worn varnish",
         use="door leaves (varnished rooms), stair risers, mantel", src="dark_wood"),
    dict(id=2, key="varnish_turned", look="darkest varnish, tight grain",
         use="turned balusters, newel posts, handrails, G1 latch block", src="dark_wood"),
    dict(id=3, key="paint_dark", look="dark olive-brown paint over wood (ref 3)",
         use="bedroom trim, D2 leaf, bedroom window", src="PaintedWood008C"),
    dict(id=4, key="paint_light", look="off-white chipped paint over wood",
         use="kitchen and bathroom trim, D3 / D4 leaves, their windows, bathroom tile cap", src="PaintedWood008C"),
    dict(id=5, key="raw", look="raw weathered boards",
         use="basement joists, trimmers and header, lath, under-stair boarding, D5 ledged door, WL1 boards",
         src="worn_planks"),
    dict(id=6, key="metal_paint", look="cream paint on iron with rust",
         use="radiators, pipes and brackets, G1 gate, hopper frames", src="rusty_metal_02"),
    dict(id=7, key="brass", look="tarnished brass (metallic)",
         use="hinges, knobs, backplates, gate latch, radiator valves", src="rusty_metal_02"),
]
STRIP = {s["key"]: s["id"] for s in STRIPS}


def v_range(strip, margin=True):
    """(v0, v1) UV range of a strip (index or key), inset by MARGIN_PX when margin."""
    i = STRIP[strip] if isinstance(strip, str) else strip
    m = MARGIN_PX / SIZE if margin else 0.0
    return i / NSTRIP + m, (i + 1) / NSTRIP - m


SOURCES = {   # asset -> (provider, files {map: filename})
    "dark_wood": ("polyhaven", ["diff", "nor_gl", "arm"]),
    "worn_planks": ("polyhaven", ["diff", "nor_gl", "arm"]),
    "rusty_metal_02": ("polyhaven", ["diff", "nor_gl", "arm"]),
    "PaintedWood008C": ("ambientcg", ["Color", "NormalGL", "Roughness", "AmbientOcclusion"]),
}
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Environment", "Bungalow",
                                       "Textures", "Trim"))


# ------------------------------------------------------------------ download (stdlib only)
def download(root):
    """Fetch the 1K CC0 maps, each asset into its own new folder root/dl_<asset>. Only image files are kept."""
    import io
    import urllib.request
    import zipfile
    os.makedirs(root, exist_ok=True)
    for asset, (prov, maps) in SOURCES.items():
        d = os.path.join(root, "dl_" + asset)
        os.makedirs(d, exist_ok=True)
        if prov == "polyhaven":
            for m in maps:
                p = os.path.join(d, f"{asset}_{m}_1k.png")
                if not os.path.exists(p):
                    url = f"https://dl.polyhaven.org/file/ph-assets/Textures/png/1k/{asset}/{asset}_{m}_1k.png"
                    urllib.request.urlretrieve(url, p)
        else:
            want = [f"{asset}_1K-PNG_{m}.png" for m in maps]
            if not all(os.path.exists(os.path.join(d, w)) for w in want):
                data = urllib.request.urlopen(f"https://ambientcg.com/get?file={asset}_1K-PNG.zip").read()
                with zipfile.ZipFile(io.BytesIO(data)) as z:
                    for w in want:          # extract only the named PNGs, never the bundled .blend / .usdc
                        with open(os.path.join(d, w), "wb") as f:
                            f.write(z.read(w))
        print("source", asset, "->", d)


# ------------------------------------------------------------------ compositing (numpy + Pillow)
def _np():
    import site
    if site.getusersitepackages() not in sys.path:      # python -I drops the user site; numpy / Pillow live there
        sys.path.append(site.getusersitepackages())
    import numpy as np
    from PIL import Image
    return np, Image


def _load(src, asset):
    np, Image = _np()
    d = os.path.join(src, "dl_" + asset)

    def rd(name, mode):
        a = np.asarray(Image.open(os.path.join(d, name)).convert(mode), dtype=np.float32) / 255.0
        return a

    if SOURCES[asset][0] == "polyhaven":
        arm = rd(f"{asset}_arm_1k.png", "RGB")
        return dict(base=rd(f"{asset}_diff_1k.png", "RGB"), nrm=rd(f"{asset}_nor_gl_1k.png", "RGB"),
                    ao=arm[..., 0], rough=arm[..., 1], metal=arm[..., 2])
    p = f"{asset}_1K-PNG_"
    return dict(base=rd(p + "Color.png", "RGB"), nrm=rd(p + "NormalGL.png", "RGB"), ao=rd(p + "AmbientOcclusion.png", "L"),
                rough=rd(p + "Roughness.png", "L"), metal=None)


def _rows(m, r0, n=STRIP_PX):
    """n rows starting at r0 (wrapping: every source tiles) of every map."""
    np, _ = _np()
    idx = (np.arange(n) + r0) % m["base"].shape[0]
    return {k: (None if v is None else v[idx]) for k, v in m.items()}


def _rot90(m):
    """Rotate a source 90 deg CCW (vertical grain -> along U), rotating the tangent-space normals with it."""
    np, _ = _np()
    out = {k: (None if v is None else np.rot90(v, 1, axes=(0, 1)).copy()) for k, v in m.items()}
    n = out["nrm"]
    x, y = n[..., 0] * 2 - 1, n[..., 1] * 2 - 1
    out["nrm"] = np.dstack([(-y + 1) / 2, (x + 1) / 2, n[..., 2]])
    return out


def _tile_half(m):
    """Tile a 1 m source 2x2 and halve it, so it matches TRIM_UV_M = 2 m per UV."""
    np, Image = _np()
    out = {}
    for k, v in m.items():
        if v is None:
            out[k] = None
            continue
        t = np.concatenate([np.concatenate([v, v], 1)] * 2, 0)
        im = Image.fromarray((np.clip(t, 0, 1) * 255).astype(np.uint8))
        out[k] = np.asarray(im.resize((v.shape[1], v.shape[0]), Image.LANCZOS), dtype=np.float32) / 255.0
    return out


def _widest_board(m):
    """Row range of the widest board in a rotated plank scan (seams = strongest normal-Y slope rows)."""
    np, _ = _np()
    ny = np.abs(m["nrm"][..., 1] - 0.5).mean(axis=1)
    ny = ny - np.convolve(ny, np.ones(31) / 31, mode="same")
    h = len(ny)
    seams = []
    for r in np.argsort(-ny):
        if all(min(abs(r - s), h - abs(r - s)) > 40 for s in seams):
            seams.append(int(r))
        if len(seams) >= 12:
            break
    seams.sort()
    gaps = [((seams[(i + 1) % len(seams)] - s) % h, s) for i, s in enumerate(seams)]
    w, s = max(gaps)
    return s + 6, w - 12


def _resize_rows(m, r0, n):
    """Rows r0..r0+n stretched to STRIP_PX."""
    np, Image = _np()
    band = _rows(m, r0, n)
    out = {}
    for k, v in band.items():
        if v is None:
            out[k] = None
            continue
        im = Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8))
        out[k] = np.asarray(im.resize((v.shape[1], STRIP_PX), Image.LANCZOS), dtype=np.float32) / 255.0
    return out


def _lum(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def _flatten(nrm, k):
    """Blend a normal map towards flat by k (0 = unchanged, 1 = flat)."""
    np, _ = _np()
    v = nrm * 2 - 1
    v[..., :2] *= (1 - k)
    v /= np.linalg.norm(v, axis=-1, keepdims=True) + 1e-6
    return (v + 1) / 2


def strip_maps(src):
    """[(base, nrm, ao, rough, metal)] for strips 0..7, each 128 x 1024."""
    np, _ = _np()
    wood = _load(src, "dark_wood")
    paint = _load(src, "PaintedWood008C")
    raw = _rot90(_load(src, "worn_planks"))
    metal = _tile_half(_load(src, "rusty_metal_02"))
    out = []

    def varnish(r0, tint, gamma, r_lo, r_k, ao_k=1.0):
        s = _rows(wood, r0)
        c = s["base"] ** gamma
        c = c * 0.5 + _lum(c)[..., None] * 0.5            # take the mahogany red out: dark brown stain (refs 1, 2)
        base = np.clip(c * np.array(tint), 0, 1)
        return base, s["nrm"], 1 - ao_k * (1 - s["ao"]), np.clip(r_lo + r_k * s["rough"], 0, 1), 0.0 * s["ao"]

    out.append(varnish(96, (0.95, 0.72, 0.52), 1.15, 0.28, 0.40))          # 0 dark-stained varnish
    out.append(varnish(430, (1.10, 0.86, 0.62), 1.05, 0.36, 0.45))         # 1 warmer, more worn
    out.append(varnish(800, (0.80, 0.60, 0.45), 1.25, 0.25, 0.35))         # 2 darkest, turned work
    s = _rows(paint, 210)                                                    # 3 dark paint, as scanned
    out.append((np.clip(s["base"] * 0.85, 0, 1), s["nrm"], s["ao"], np.clip(s["rough"] * 0.95, 0, 1),
                0.0 * s["ao"]))
    s = _rows(paint, 610)                                                    # 4 off-white paint
    lum = _lum(s["base"])
    rel = np.clip(lum / (lum.mean() + 1e-6), 0.2, 1.8)[..., None]
    cream = np.array((0.80, 0.77, 0.68))
    dirt = np.array((0.42, 0.38, 0.30))
    t = np.clip((rel - 0.55) / 0.6, 0, 1)
    out.append((np.clip(dirt + (cream - dirt) * t, 0, 1), s["nrm"], s["ao"], np.clip(s["rough"] * 0.85 + 0.08, 0, 1),
                0.0 * s["ao"]))
    r0, n = _widest_board(raw)                                               # 5 raw boards: one board, grain along U
    s = _resize_rows(raw, r0, n)
    out.append((np.clip(s["base"] * 0.9, 0, 1), s["nrm"], s["ao"], np.clip(s["rough"], 0.55, 1), 0.0 * s["ao"]))
    s = _rows(metal, 300)                                                    # 6 painted iron (radiators, pipes)
    b = s["base"]
    sat = b.max(-1) - b.min(-1)
    q = np.quantile(sat, 0.78)                                              # keep ~20 % rust, the rest paint
    rust = np.clip((sat - q) / 0.08, 0, 1)[..., None]
    paint_c = np.array((0.74, 0.70, 0.58)) * (0.75 + 0.3 * _lum(b)[..., None])
    rust_c = b ** 1.3 * np.array((0.75, 0.6, 0.5))
    base = paint_c * (1 - rust) + rust_c * rust
    out.append((np.clip(base, 0, 1), s["nrm"], s["ao"], np.clip(0.45 + 0.45 * rust[..., 0] + 0.1 * s["rough"], 0, 1),
                0.0 * s["ao"]))
    s = _rows(metal, 700)                                                    # 7 tarnished brass
    b = s["base"]
    sat = b.max(-1) - b.min(-1)
    q = np.quantile(sat, 0.7)
    tarn = np.clip((sat - q) / 0.1, 0, 1)[..., None] * 0.8
    brass = np.array((0.80, 0.62, 0.33))
    dark = np.array((0.22, 0.17, 0.10))
    base = brass * (1 - 0.75 * tarn) + dark * 0.75 * tarn
    out.append((np.clip(base, 0, 1), _flatten(s["nrm"], 0.6), s["ao"],
                np.clip(0.28 + 0.5 * tarn[..., 0], 0, 1), np.clip(1 - 0.8 * tarn[..., 0], 0, 1)))
    return out


def build(src, out_dir=OUT_DIR):
    np, Image = _np()
    base = np.zeros((SIZE, SIZE, 3), np.float32)
    nrm = np.zeros((SIZE, SIZE, 3), np.float32)
    mask = np.zeros((SIZE, SIZE, 4), np.float32)
    for i, (b, n, ao, rough, metal) in enumerate(strip_maps(src)):
        r1 = SIZE - i * STRIP_PX            # image rows: strip 0 is at the bottom (V = 0)
        r0 = r1 - STRIP_PX
        base[r0:r1] = b
        nrm[r0:r1] = n
        mask[r0:r1, :, 0] = metal
        mask[r0:r1, :, 1] = ao
        mask[r0:r1, :, 3] = 1 - rough
    os.makedirs(out_dir, exist_ok=True)
    u8 = lambda a: (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)  # noqa: E731
    paths = []
    for name, arr in (("Trim_Albedo", base), ("Trim_Normal", nrm), ("Trim_Mask", mask)):
        p = os.path.join(out_dir, name + ".png")
        Image.fromarray(u8(arr)).save(p)
        paths.append(p)
    print("wrote", *paths)
    return paths


if __name__ == "__main__":
    a = sys.argv[1:]
    if "--download" in a:
        download(a[a.index("--download") + 1])
    if "--src" in a:
        build(a[a.index("--src") + 1], a[a.index("--out") + 1] if "--out" in a else OUT_DIR)
