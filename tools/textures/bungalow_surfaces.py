"""BLINK Bungalow surface materials (Phase 4): fetch the CC0 1K scans, author the procedural patterns, repack to URP.

Plain Python 3 + numpy + Pillow (no bpy). Downloads are untrusted data: run with isolated mode and keep the cache
outside the repo, one fresh folder per asset:

  python -I tools/textures/bungalow_surfaces.py fetch <cache_dir>
  python -I tools/textures/bungalow_surfaces.py build <cache_dir> [--unity <Unity/Assets/Art/Environment/Bungalow>]

`fetch` pulls the 1K maps listed in SOURCES (Poly Haven PNG via api.polyhaven.com/files, ambientCG 1K-PNG zips) and
writes <cache>/sources.json (authors, real-world tile size, URLs) for the credits and the tiling table.
`build` writes 1024x1024 PNGs into Textures/<MaterialName>/ following the URP Lit conventions:
  *_Base.png     sRGB albedo (alpha = coverage where the material is transparent / decal)
  *_Normal.png   OpenGL Y+ tangent normal (imported as Normal Map)
  *_Mask.png     linear MetallicSmoothness: R metallic, G AO, B 0, A smoothness (= 1 - roughness).
                 G doubles as the Occlusion map (URP reads occlusion from G).
Everything procedural is seeded and tiles seamlessly (periodic noise, wrapped stamping).
"""
import io
import json
import math
import os
import sys
import urllib.request
import zipfile

try:
    import numpy as np
except ImportError:  # `python -I` drops the user site-packages; add only that (never the cwd or a download dir)
    import site
    sys.path.append(site.getusersitepackages())
    import numpy as np
from PIL import Image, ImageDraw, ImageFilter

RES = 1024
UA = {"User-Agent": "blink-gamejam-bungalow/1.0 (CC0 asset fetch)"}

# material -> sources. ph = Poly Haven id (diff/nor_gl/arm), acg = ambientCG id (1K-PNG zip).
PH = ["dark_wooden_planks", "herringbone_parquet", "old_linoleum_flooring_01", "concrete_floor_damaged_01",
      "decrepit_wallpaper", "white_plaster_02", "whitewashed_brick"]
ACG = ["Wallpaper002C", "Tiles133D", "Fabric032", "Leaking013A", "Leaking017B"]
PH_MAPS = {"Diffuse": "diff", "nor_gl": "nor_gl", "arm": "arm"}


# ------------------------------------------------------------------ fetch
def _get(url, binary=True):
    data = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120).read()
    return data if binary else json.loads(data)


def fetch(cache):
    os.makedirs(cache, exist_ok=True)
    meta = {}
    for aid in PH:
        d = os.path.join(cache, aid)
        os.makedirs(d, exist_ok=True)
        info = _get(f"https://api.polyhaven.com/info/{aid}", False)
        files = _get(f"https://api.polyhaven.com/files/{aid}", False)
        urls = {}
        for key, short in PH_MAPS.items():
            u = files[key]["1k"]["png"]["url"]
            out = os.path.join(d, f"{aid}_{short}_1k.png")
            if not os.path.exists(out):
                open(out, "wb").write(_get(u))
            urls[short] = u
        meta[aid] = dict(site="Poly Haven", name=info.get("name"), authors=sorted(info.get("authors", {})),
                         dimensions_mm=info.get("dimensions"), page=f"https://polyhaven.com/a/{aid}", files=urls,
                         licence="CC0 1.0")
        print("ph", aid, meta[aid]["authors"], meta[aid]["dimensions_mm"])
    for aid in ACG:
        d = os.path.join(cache, aid)
        os.makedirs(d, exist_ok=True)
        info = _get(f"https://ambientcg.com/api/v2/full_json?id={aid}&include=downloadData,dimensionsData", False)
        a = info["foundAssets"][0]
        u = f"https://ambientcg.com/get?file={aid}_1K-PNG.zip"
        if not any(n.lower().endswith(".png") for n in os.listdir(d)):
            z = zipfile.ZipFile(io.BytesIO(_get(u)))
            for n in z.namelist():  # flat names only, PNGs only (never anything executable)
                base = os.path.basename(n)
                if base.lower().endswith(".png") and base == n:
                    open(os.path.join(d, base), "wb").write(z.read(n))
        dims = [a.get("dimensionX"), a.get("dimensionY"), a.get("dimensionZ")]
        meta[aid] = dict(site="ambientCG", name=a.get("displayName"), authors=["ambientCG"],
                         dimensions_cm=dims, page=f"https://ambientcg.com/view?id={aid}", files={"zip": u},
                         licence="CC0 1.0", files_in_zip=sorted(os.listdir(d)))
        print("acg", aid, dims, os.listdir(d))
    json.dump(meta, open(os.path.join(cache, "sources.json"), "w"), indent=1)


# ------------------------------------------------------------------ image helpers (float32 arrays, 0..1)
def load(path, mode="RGB"):
    im = Image.open(path)
    if im.size != (RES, RES):
        im = im.resize((RES, RES), Image.LANCZOS)
    return np.asarray(im.convert(mode), dtype=np.float32) / 255.0


def save(arr, path, mode=None):
    a = np.clip(arr * 255.0 + 0.5, 0, 255).astype(np.uint8)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(a, mode).save(path, optimize=True)
    return path


def s2l(c):
    c = np.asarray(c, dtype=np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(np.asarray(c, dtype=np.float32), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def lum(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def grade(srgb, target, sat=1.0, contrast=1.0, mix=1.0):
    """Shift an sRGB albedo so its mean lands on `target` (sRGB 0..255), keeping the scan's variation.
    Works in linear: per-channel gain, then saturation / contrast about the mean luminance."""
    lin = s2l(srgb)
    mean = lin.reshape(-1, 3).mean(0)
    out = lin * (s2l(np.array(target, np.float32) / 255.0) / np.maximum(mean, 1e-4))
    L = lum(out)[..., None]
    out = L + (out - L) * sat
    mL = lum(out).mean()
    out = mL * (out / mL) ** contrast if contrast != 1.0 else out
    out = lin + (out - lin) * mix
    return l2s(np.clip(out, 0.012, 0.86))   # keep albedo inside a sane PBR range (sRGB ~ 0.1 .. 0.94)


def mask_from_arm(arm):
    """Poly Haven ARM (R AO, G rough, B metal) -> URP MetallicSmoothness (R metal, G AO, B 0, A 1 - rough)."""
    m = np.zeros((RES, RES, 4), np.float32)
    m[..., 0], m[..., 1], m[..., 3] = arm[..., 2], arm[..., 0], 1.0 - arm[..., 1]
    return m


def mask_from(rough, ao=None, metal=None, smooth_scale=1.0):
    m = np.zeros((RES, RES, 4), np.float32)
    m[..., 0] = 0.0 if metal is None else metal
    m[..., 1] = 1.0 if ao is None else ao
    m[..., 3] = np.clip((1.0 - rough) * smooth_scale, 0, 1)
    return m


def pnoise(seed, beta=2.0, size=RES, lo_cut=1.0):
    """Seamlessly tiling fractal noise (random spectrum ~ f^-beta, inverse FFT), normalised to 0..1."""
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.rfftfreq(size)[None, :] * size
    f = np.sqrt(fx * fx + fy * fy)
    amp = np.where(f >= lo_cut, np.maximum(f, 1.0) ** (-beta / 1.0), 0.0)
    spec = amp * (r.normal(size=f.shape) + 1j * r.normal(size=f.shape))
    n = np.fft.irfft2(spec, s=(size, size))
    return ((n - n.min()) / (n.max() - n.min())).astype(np.float32)


def blur_wrap(a, radius):
    """Gaussian blur (sigma = radius px) by FFT, so it wraps around the edges and keeps tiling."""
    h, w = a.shape[:2]
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    g = np.exp(-2 * (math.pi * radius) ** 2 * (fx * fx + fy * fy))
    if a.ndim == 2:
        return np.fft.irfft2(np.fft.rfft2(a) * g, s=(h, w)).astype(np.float32)
    return np.stack([np.fft.irfft2(np.fft.rfft2(a[..., c]) * g, s=(h, w)) for c in range(a.shape[2])],
                    -1).astype(np.float32)


def max3(a):
    """3x3 max filter (wrapping)."""
    out = a.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            out = np.maximum(out, np.roll(np.roll(a, dy, 0), dx, 1))
    return out


def normal_from_height(h, strength, wrap=True):
    """OpenGL (Y+) tangent normal from a height field (0..1), central differences."""
    if wrap:
        dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
        dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    else:
        dy, dx = np.gradient(h)
    nx, ny = -dx * strength, dy * strength      # image rows go down; GL green points up
    nz = np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5, nz * inv * 0.5 + 0.5], -1)


def blend_normals(a, b):
    """Whiteout blend of two GL normal maps (0..1 encoded)."""
    a, b = a * 2 - 1, b * 2 - 1
    n = np.stack([a[..., 0] + b[..., 0], a[..., 1] + b[..., 1], a[..., 2] * b[..., 2]], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


# ------------------------------------------------------------------ procedural pattern drawing (supersampled, wrapped)
SS = 4                     # supersampling factor for the vector-drawn patterns
BIG = RES * SS


class Canvas:
    """L-mode canvas at RES*SS. Every primitive is drawn at the 9 wrap offsets, so the result tiles seamlessly."""

    def __init__(self):
        self.im = Image.new("L", (BIG, BIG), 0)
        self.d = ImageDraw.Draw(self.im)

    def _offs(self):
        return [(ox, oy) for ox in (-BIG, 0, BIG) for oy in (-BIG, 0, BIG)]

    def poly(self, pts, v=255):
        for ox, oy in self._offs():
            self.d.polygon([(x * SS + ox, y * SS + oy) for x, y in pts], fill=v)

    def ellipse(self, cx, cy, rx, ry, v=255):
        for ox, oy in self._offs():
            self.d.ellipse([(cx - rx) * SS + ox, (cy - ry) * SS + oy, (cx + rx) * SS + ox, (cy + ry) * SS + oy],
                           fill=v)

    def line(self, pts, w, v=255):
        for ox, oy in self._offs():
            q = [(x * SS + ox, y * SS + oy) for x, y in pts]
            self.d.line(q, fill=v, width=max(1, int(w * SS)), joint="curve")
            r = w * SS / 2
            for x, y in (q[0], q[-1]):
                self.d.ellipse([x - r, y - r, x + r, y + r], fill=v)

    def get(self):
        return np.asarray(self.im.resize((RES, RES), Image.LANCZOS), np.float32) / 255.0


def _xf(cx, cy, s, mirror=1.0, rot=0.0):
    c, sn = math.cos(rot), math.sin(rot)

    def f(x, y):
        x *= mirror
        return cx + (x * c - y * sn) * s, cy + (x * sn + y * c) * s
    return f


def leaf_pts(f, p0, p1, bend, width, n=24):
    """Leaf polygon along a quadratic curve p0 -> p1 bulging by `bend` (local units), width profile sin."""
    (x0, y0), (x1, y1) = p0, p1
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / L, dx / L
    cx, cy = mx + nx * bend, my + ny * bend
    left, right = [], []
    for i in range(n + 1):
        t = i / n
        px = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * cx + t * t * x1
        py = (1 - t) ** 2 * y0 + 2 * (1 - t) * t * cy + t * t * y1
        tx = 2 * (1 - t) * (cx - x0) + 2 * t * (x1 - cx)
        ty = 2 * (1 - t) * (cy - y0) + 2 * t * (y1 - cy)
        tl = math.hypot(tx, ty) or 1.0
        w = width * math.sin(math.pi * t) ** 0.8
        left.append(f(px - ty / tl * w, py + tx / tl * w))
        right.append(f(px + ty / tl * w, py - tx / tl * w))
    return left + right[::-1]


def curve_pts(f, pts, n=32):
    """Catmull-Rom through local points -> transformed polyline."""
    out = []
    P = [pts[0]] + list(pts) + [pts[-1]]
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            x = 0.5 * (2 * p1[0] + (-p0[0] + p2[0]) * t + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2 +
                       (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3)
            y = 0.5 * (2 * p1[1] + (-p0[1] + p2[1]) * t + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2 +
                       (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3)
            out.append(f(x, y))
    out.append(f(*pts[-1]))
    return out


def spiral_pts(f, cx, cy, r0, r1, turns, start, n=60):
    out = []
    for i in range(n + 1):
        t = i / n
        a = start + t * turns * 2 * math.pi
        r = r0 + (r1 - r0) * t
        out.append(f(cx + r * math.cos(a), cy + r * math.sin(a)))
    return out


def damask_motif(cv, cx, cy, s):
    """Mirror-symmetric damask medallion (palmette, leaves, scrolls, fleur) centred at cx, cy; s = cell size px."""
    for m in (1.0, -1.0):
        f = _xf(cx, cy, s, m)
        # palmette half (teardrop), drawn as a full polygon from the centre line out
        right = []
        for i in range(41):
            t = i / 40 * math.pi
            right.append((0.2 * math.sin(t) ** 1.25 * (1 + 0.45 * math.cos(t)), -0.30 * math.cos(t) + 0.02))
        cv.poly([f(x, y) for x, y in [(0.0, -0.28)] + right + [(0.0, 0.32)]], 255)
        # big leaves sweeping up and out from the base
        cv.poly(leaf_pts(f, (0.05, 0.30), (0.42, -0.06), -0.10, 0.075), 255)
        cv.poly(leaf_pts(f, (0.10, 0.34), (0.40, 0.30), 0.06, 0.045), 255)
        # scrolls
        cv.line(spiral_pts(f, 0.33, 0.12, 0.10, 0.015, 1.15, -0.2), 0.022 * s, 255)
        cv.line(curve_pts(f, [(0.02, -0.30), (0.10, -0.38), (0.22, -0.36), (0.26, -0.27)]), 0.018 * s, 255)
        cv.ellipse(*f(0.27, -0.25), 0.03 * s, 0.03 * s, 255)
        # side fleur petal on top
        cv.poly(leaf_pts(f, (0.0, -0.36), (0.12, -0.47), 0.03, 0.03), 255)
    f = _xf(cx, cy, s)
    cv.poly(leaf_pts(f, (0.0, -0.34), (0.0, -0.50), 0.0, 0.035), 255)
    # hollow the palmette: inner teardrop in ground, then a stem and three beads in motif
    inner = []
    for i in range(41):
        t = i / 40 * 2 * math.pi
        inner.append(f(0.11 * math.sin(t) * (1 + 0.4 * math.cos(t)) * (1 if t < math.pi else 1), -0.2 * math.cos(t) + 0.04))
    cv.poly(inner, 0)
    cv.line(curve_pts(f, [(0.0, -0.13), (0.0, 0.2)]), 0.02 * s, 255)
    for k, yy in enumerate((-0.06, 0.04, 0.14)):
        r = (0.03, 0.04, 0.05)[k]
        cv.ellipse(*f(0.0, yy), r * s * 0.9, r * s * 0.6, 255)


def damask_filler(cv, cx, cy, s):
    """Small four-petal diamond rosette between the medallions."""
    f = _xf(cx, cy, s)
    for a in range(4):
        g = _xf(cx, cy, s, 1.0, a * math.pi / 2)
        cv.poly(leaf_pts(g, (0.0, 0.0), (0.0, -0.15), 0.0, 0.035), 255)
    for a in range(4):
        g = _xf(cx, cy, s, 1.0, a * math.pi / 2 + math.pi / 4)
        cv.ellipse(*g(0.0, -0.1), 0.012 * s, 0.012 * s, 255)
    cv.ellipse(*f(0, 0), 0.03 * s, 0.03 * s, 0)


def ogee_vine(cv, cx, cy, W, H, rng):
    """Outline of one ogee (onion) cell of the lattice as a vine with paired leaves."""
    for side in (1.0, -1.0):
        pts = []
        for i in range(49):
            t = -0.5 + i / 48
            hw = 0.5 * W * math.cos(math.pi * t) ** 1.15 * (1 + 0.18 * math.sin(2 * math.pi * t))
            pts.append((cx + side * hw, cy + t * H))
        cv.line(pts, 0.016 * H, 255)
        for i in (8, 16, 32, 40):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            ang = math.atan2(y1 - y0, x1 - x0)
            for lr in (1.0, -1.0):
                g = _xf(x0, y0, H, 1.0, ang + lr * 0.9)
                cv.poly(leaf_pts(g, (0.0, 0.0), (0.075, 0.0), 0.0, 0.018), 255)


def pattern_damask(cols=4, rows=4):
    """Ref 2 damask: half-drop ogee lattice of leafy vines, a medallion in every cell, rosettes at the tips."""
    cv = Canvas()
    rng = np.random.default_rng(11)
    W, H = RES / cols, RES / rows
    for c in range(cols):
        for r in range(rows):
            dy = (c % 2) * H / 2
            cx, cy = c * W + W / 2, r * H + H / 2 + dy
            ogee_vine(cv, cx, cy, W, H, rng)
            damask_motif(cv, cx, cy + 0.02 * H, H * 0.66)
            damask_filler(cv, cx, cy - H / 2, H * 0.62)
    return cv.get()


def pattern_pale(cols=10, rows=10):
    """Ref 1 pale paper: small sprigs (four petals + two leaves) on a half-drop lattice, plus tiny dots."""
    cv = Canvas()
    W, H = RES / cols, RES / rows
    for c in range(cols):
        for r in range(rows):
            dy = (c % 2) * H / 2
            cx, cy = c * W + W / 2, r * H + H / 2 + dy
            flip = 1.0 if (c // 2 + r) % 2 == 0 else -1.0
            f = _xf(cx, cy, H, flip)
            for a in range(4):
                g = _xf(cx, cy - 0.06 * H, H, 1.0, a * math.pi / 2 + math.pi / 4)
                cv.ellipse(*g(0.0, -0.07), 0.045 * H, 0.045 * H, 255)
            cv.ellipse(*f(0.0, -0.06), 0.03 * H, 0.03 * H, 0)
            cv.line(curve_pts(f, [(0.0, -0.02), (0.03, 0.1), (0.0, 0.2)]), 0.025 * H, 255)
            cv.poly(leaf_pts(f, (0.02, 0.1), (0.17, 0.05), -0.03, 0.035), 255)
            cv.poly(leaf_pts(f, (0.01, 0.15), (-0.13, 0.2), 0.03, 0.03), 255)
            cv.ellipse(cx, cy - H / 2, 0.02 * H, 0.02 * H, 255)
    return cv.get()


def floral_motif(cv, cx, cy, s, rng):
    """Ref 3 trailing floral: stems with leaves (channel 'leaf') and blossoms (channel 'bloom')."""
    leaf, bloom = cv
    flip = 1.0 if rng.random() < 0.5 else -1.0
    f = _xf(cx, cy, s, flip, rng.uniform(-0.25, 0.25))
    stem = [(-0.35, 0.45), (-0.2, 0.2), (-0.05, 0.05), (0.05, -0.15), (0.2, -0.3), (0.35, -0.42)]
    leaf.line(curve_pts(f, stem), 0.022 * s, 255)
    branch = [(-0.05, 0.05), (-0.25, -0.05), (-0.38, -0.2)]
    leaf.line(curve_pts(f, branch), 0.016 * s, 255)
    branch2 = [(0.05, -0.15), (0.25, -0.05), (0.42, 0.08)]
    leaf.line(curve_pts(f, branch2), 0.014 * s, 255)
    for (px, py), (qx, qy), b in [((-0.2, 0.2), (-0.42, 0.12), 0.05), ((-0.12, 0.12), (0.08, 0.3), -0.05),
                                  ((0.05, -0.15), (-0.12, -0.32), -0.04), ((0.2, -0.3), (0.38, -0.18), 0.04),
                                  ((-0.25, -0.05), (-0.3, 0.12), 0.04), ((0.25, -0.05), (0.3, -0.25), -0.04),
                                  ((-0.32, 0.36), (-0.12, 0.42), 0.04)]:
        leaf.poly(leaf_pts(f, (px, py), (qx, qy), b, 0.055), 255)
    # main blossom (cabbage rose): a ring of overlapping outer petals, a cup, and dark petal edges / spiral heart
    gx, gy = f(0.0, -0.02)
    rot0 = rng.uniform(0, math.pi)
    for k in range(7):
        g = _xf(gx, gy, s, 1.0, rot0 + k * 2 * math.pi / 7)
        bloom.poly(leaf_pts(g, (0.0, -0.02), (0.0, -0.19), 0.0, 0.085), 255)
    for k in range(7):
        g = _xf(gx, gy, s, 1.0, rot0 + (k + 0.5) * 2 * math.pi / 7)
        bloom.line(curve_pts(g, [(-0.05, -0.13), (0.0, -0.165), (0.05, -0.13)], 8), 0.012 * s, 120)
    bloom.ellipse(gx, gy, 0.1 * s, 0.09 * s, 255)
    bloom.line(spiral_pts(_xf(gx, gy, s), 0.0, 0.0, 0.085, 0.01, 2.2, rot0), 0.012 * s, 110)
    # buds
    for (px, py) in [(-0.38, -0.2), (0.42, 0.08), (0.35, -0.42)]:
        g = f(px, py)
        bloom.ellipse(g[0], g[1], 0.05 * s, 0.05 * s, 255)
        bloom.ellipse(g[0], g[1], 0.022 * s, 0.022 * s, 110)


def pattern_floral(cols=4, rows=4, seed=31):
    rng = np.random.default_rng(seed)
    leaf, bloom = Canvas(), Canvas()
    W, H = RES / cols, RES / rows
    for c in range(cols):
        for r in range(rows):
            dy = (c % 2) * H / 2
            floral_motif((leaf, bloom), c * W + W / 2, r * H + H / 2 + dy, H * 0.95, rng)
    return leaf.get(), bloom.get()


# ------------------------------------------------------------------ materials
def P(cache, aid, short):
    return os.path.join(cache, aid, f"{aid}_{short}_1k.png")


def A(cache, aid, kind):
    return os.path.join(cache, aid, f"{aid}_1K-PNG_{kind}.png")


def scan_set(cache, out, name, aid, target, sat=1.0, contrast=1.0, smooth_scale=1.0, ao_strength=1.0):
    base = grade(load(P(cache, aid, "diff")), target, sat, contrast)
    mask = mask_from_arm(load(P(cache, aid, "arm")))
    mask[..., 3] = np.clip(mask[..., 3] * smooth_scale, 0, 1)
    mask[..., 1] = 1 - (1 - mask[..., 1]) * ao_strength
    d = os.path.join(out, name)
    save(base, os.path.join(d, f"{name}_Base.png"))
    save(load(P(cache, aid, "nor_gl")), os.path.join(d, f"{name}_Normal.png"))
    save(mask, os.path.join(d, f"{name}_Mask.png"), "RGBA")
    return base


def paper_variation(paper):
    """Relative stain/grime field from a paper scan (1 = clean average)."""
    L = lum(s2l(paper))
    rel = L / L.mean()
    return np.clip(rel, 0.35, 1.5)


def colorize(pattern_layers, ground, variation, paper, stain_mix=0.25, var_pow=0.9):
    """Linear-space wallpaper albedo: ground colour + pattern layers [(mask, colour)], times the paper's stains,
    pulled a little towards the paper's own (browned) hue where it is stained."""
    col = np.broadcast_to(s2l(np.array(ground, np.float32) / 255.0), (RES, RES, 3)).copy()
    for m, c in pattern_layers:
        col = col + (s2l(np.array(c, np.float32) / 255.0) - col) * m[..., None]
    v = variation[..., None] ** var_pow
    col = col * v
    paper_lin = s2l(paper)
    paper_lin = paper_lin * (lum(col).mean() / lum(paper_lin).mean())
    stained = np.clip(1.0 - variation, 0, 1)[..., None]
    col = col + (paper_lin - col) * stained * stain_mix
    return l2s(np.clip(col, 0.008, 0.86))


def build(cache, unity_root):
    out = os.path.join(unity_root, "Textures")
    rep = {}

    # --- floors, plaster, brick, concrete (Poly Haven scans, graded to the references)
    scan_set(cache, out, "M_FloorPlank", "dark_wooden_planks", (66, 52, 41), sat=1.05, contrast=1.05,
             smooth_scale=1.0)
    scan_set(cache, out, "M_Parquet", "herringbone_parquet", (76, 53, 38), sat=0.8, contrast=1.1, smooth_scale=0.95)
    scan_set(cache, out, "M_Lino", "old_linoleum_flooring_01", (128, 112, 86), sat=0.45, contrast=0.95)
    scan_set(cache, out, "M_Concrete", "concrete_floor_damaged_01", (92, 88, 80), sat=0.8)
    scan_set(cache, out, "M_Plaster", "white_plaster_02", (176, 170, 156), sat=0.7, contrast=0.9)
    brick = grade(load(P(cache, "whitewashed_brick", "diff")), (196, 190, 180), sat=0.55, contrast=0.85)
    d = os.path.join(out, "M_BrickWhite")
    save(brick, os.path.join(d, "M_BrickWhite_Base.png"))
    save(load(P(cache, "whitewashed_brick", "nor_gl")), os.path.join(d, "M_BrickWhite_Normal.png"))
    save(mask_from_arm(load(P(cache, "whitewashed_brick", "arm"))), os.path.join(d, "M_BrickWhite_Mask.png"), "RGBA")

    # --- tiles (ambientCG)
    d = os.path.join(out, "M_Tile")
    tile = grade(load(A(cache, "Tiles133D", "Color")), (172, 172, 162), sat=0.8)
    save(tile, os.path.join(d, "M_Tile_Base.png"))
    save(load(A(cache, "Tiles133D", "NormalGL")), os.path.join(d, "M_Tile_Normal.png"))
    save(mask_from(load(A(cache, "Tiles133D", "Roughness"), "L"), load(A(cache, "Tiles133D", "AmbientOcclusion"),
                                                                       "L")),
         os.path.join(d, "M_Tile_Mask.png"), "RGBA")

    # --- wallpapers: decrepit_wallpaper is the paper for Damask and Pale (shared normal + mask)
    paper = load(P(cache, "decrepit_wallpaper", "diff"))
    var = paper_variation(paper)
    d = os.path.join(out, "M_Wall_Shared")
    save(load(P(cache, "decrepit_wallpaper", "nor_gl")), os.path.join(d, "M_Wall_Shared_Normal.png"))
    wm = mask_from_arm(load(P(cache, "decrepit_wallpaper", "arm")))
    save(wm, os.path.join(d, "M_Wall_Shared_Mask.png"), "RGBA")

    dam = pattern_damask()
    dam_soft = np.clip(blur_wrap(dam, 0.8), 0, 1)
    damask = colorize([(dam_soft * 0.9, (96, 27, 24))], (66, 17, 17), var, paper, stain_mix=0.3, var_pow=0.6)
    save(damask, os.path.join(out, "M_Wall_Damask", "M_Wall_Damask_Base.png"))

    pale = pattern_pale()
    pale = colorize([(np.clip(blur_wrap(pale, 0.6), 0, 1) * 0.85, (138, 116, 92))], (194, 184, 160), var, paper,
                    stain_mix=0.3, var_pow=0.45)
    save(pale, os.path.join(out, "M_Wall_Pale", "M_Wall_Pale_Base.png"))

    # --- green floral on ambientCG Wallpaper002C (mouldy paper)
    mould_paper = load(A(cache, "Wallpaper002C", "Color"))
    mv = lum(s2l(mould_paper))
    mv = np.clip(mv / np.percentile(mv, 97), 0.25, 1.0)
    leaf, bloom = pattern_floral()
    leaf, bloom = np.clip(blur_wrap(leaf, 0.7), 0, 1), np.clip(blur_wrap(bloom, 0.7), 0, 1)
    flo = colorize([(leaf * 0.9, (56, 72, 48)), (np.clip(bloom * 1.0, 0, 1) * 0.8, (128, 130, 100))],
                   (98, 112, 84), mv ** 0.8, mould_paper, stain_mix=0.18, var_pow=1.0)
    d = os.path.join(out, "M_Wall_Floral")
    save(flo, os.path.join(d, "M_Wall_Floral_Base.png"))
    save(load(A(cache, "Wallpaper002C", "NormalGL")), os.path.join(d, "M_Wall_Floral_Normal.png"))
    disp = load(A(cache, "Wallpaper002C", "Displacement"), "L")
    ao = 0.78 + 0.22 * (disp - disp.min()) / max(1e-4, disp.max() - disp.min())
    save(mask_from(load(A(cache, "Wallpaper002C", "Roughness"), "L"), ao), os.path.join(d, "M_Wall_Floral_Mask.png"),
         "RGBA")

    # --- shade fabric (ambientCG Fabric032): aged parchment tint; Base doubles as the emission mask
    d = os.path.join(out, "M_Shade")
    fab = grade(load(A(cache, "Fabric032", "Color")), (226, 212, 186), sat=1.0)
    save(fab, os.path.join(d, "M_Shade_Base.png"))
    save(load(A(cache, "Fabric032", "NormalGL")), os.path.join(d, "M_Shade_Normal.png"))
    save(mask_from(load(A(cache, "Fabric032", "Roughness"), "L")), os.path.join(d, "M_Shade_Mask.png"), "RGBA")

    # --- glass smudge (self-authored): base RGBA (alpha = dirt cover), mask (smoothness drops on smudges)
    rng = np.random.default_rng(7)
    smudge = pnoise(101, 2.2) * 0.6 + pnoise(102, 1.4) * 0.4
    smudge = np.clip((smudge - 0.45) * 2.2, 0, 1)
    finger = Canvas()
    for _ in range(26):
        x, y = rng.uniform(0, RES, 2)
        a = rng.uniform(0, math.pi)
        for k in range(rng.integers(1, 4)):
            g = _xf(x + k * 22 * math.cos(a + 1.3), y + k * 22 * math.sin(a + 1.3), 1.0, 1.0, a)
            finger.poly(leaf_pts(g, (-18, 0), (18, 0), 0, 9), int(rng.integers(40, 110)))
    fing = blur_wrap(finger.get(), 3.0)
    specks = (pnoise(103, 0.3) > 0.83).astype(np.float32) * pnoise(104, 1.0)
    dirt = np.clip(smudge * 0.7 + fing * 0.9 + specks * 0.5, 0, 1)
    gb = np.zeros((RES, RES, 4), np.float32)
    gb[..., :3] = l2s(s2l(np.array([0.74, 0.76, 0.70])) * (1 - 0.45 * dirt[..., None]))
    gb[..., 3] = np.clip(0.10 + 0.55 * dirt, 0, 1)
    d = os.path.join(out, "M_Glass")
    save(gb, os.path.join(d, "M_Glass_Base.png"), "RGBA")
    save(mask_from(0.04 + 0.55 * dirt), os.path.join(d, "M_Glass_Mask.png"), "RGBA")

    # --- water ripple (self-authored): tiling height from mid-frequency noise + a few periodic wave trains
    h = pnoise(201, 3.2, lo_cut=3.0) * 0.6 + pnoise(202, 2.4, lo_cut=6.0) * 0.4
    yy, xx = np.mgrid[0:RES, 0:RES].astype(np.float32) / RES
    for kx, ky, ph in [(3, 1, 0.3), (-2, 4, 1.1), (5, -3, 2.0)]:
        h += 0.05 * np.sin(2 * math.pi * (kx * xx + ky * yy) + ph)
    h = (h - h.min()) / (h.max() - h.min())
    d = os.path.join(out, "M_Water")
    save(normal_from_height(h, 9.0), os.path.join(d, "M_Water_Normal.png"))
    scum = np.clip((pnoise(203, 2.0) - 0.62) * 4.0, 0, 1)
    save(mask_from(0.06 + 0.4 * scum), os.path.join(d, "M_Water_Mask.png"), "RGBA")

    # --- decal atlas
    rep["decals"] = decal_atlas(cache, out)
    return rep


# ------------------------------------------------------------------ decal atlas (alpha-blended mesh decals)
# Cells in pixels (x, y, w, h), origin top-left. UV rects in the JSON use Unity's bottom-left origin.
DECALS = {
    "leak_streaks":   (0, 0, 512, 512),      # wall water leak / damp streaks, fades out downward
    "ceiling_stain":  (512, 0, 256, 256),    # brown water ring stain (ceilings)
    "mould_bloom":    (768, 0, 256, 256),    # black-green mould patch (ceiling line, corners)
    "floor_stain":    (512, 256, 256, 256),  # dark dried puddle stain
    "scuffs":         (768, 256, 256, 256),  # dark scuffs / drag marks (skirting, floors)
    "rust_streaks":   (0, 512, 256, 256),    # thin orange-brown trickles (under radiators, pipes)
    "paper_scraps":   (256, 512, 256, 256),  # 4 sheets of yellowed paper / magazine pages (2 x 2)
    "plaster_crumbs": (512, 512, 256, 256),  # crumbs and chips of fallen plaster (floor under peels)
    "peel_edge":      (768, 512, 256, 256),  # torn wallpaper edge with exposed plaster rim
    "tide_line":      (0, 768, 1024, 128),   # flood tide-line, horizontal, seamless left-right
    "grime_band":     (0, 896, 1024, 128),   # dirty band along skirting / floor edges, seamless left-right
}


def _radial(w, h, cx=0.5, cy=0.5):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return np.sqrt(((xx + 0.5) / w - cx) ** 2 + ((yy + 0.5) / h - cy) ** 2)


def _cellnoise(seed, w, h, beta=2.0):
    n = pnoise(seed, beta)
    return np.asarray(Image.fromarray(n, "F").resize((w, h), Image.BILINEAR)) if (w, h) != (RES, RES) else n


def decal_atlas(cache, out):
    base = np.zeros((RES, RES, 4), np.float32)
    nrm = np.zeros((RES, RES, 3), np.float32)
    nrm[...] = (0.5, 0.5, 1.0)
    rough = np.ones((RES, RES), np.float32) * 0.85
    height = np.zeros((RES, RES), np.float32)

    def put(name, rgb, alpha, r=None, hgt=None):
        x, y, w, h = DECALS[name]
        base[y:y + h, x:x + w, :3] = rgb
        base[y:y + h, x:x + w, 3] = np.clip(alpha, 0, 1)
        if r is not None:
            rough[y:y + h, x:x + w] = r
        if hgt is not None:
            height[y:y + h, x:x + w] = hgt

    def edge_fade(w, h, m=0.1, vert=True, horiz=True):
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        f = np.ones((h, w), np.float32)
        if horiz:
            f *= np.clip(np.minimum(xx, w - 1 - xx) / (m * w), 0, 1)
        if vert:
            f *= np.clip(np.minimum(yy, h - 1 - yy) / (m * h), 0, 1)
        return f

    brown = np.array([0.36, 0.25, 0.14], np.float32)

    # leak streaks: ambientCG Leaking017B streak mask, top-heavy, brown with darker drip ends
    lk = load(A(cache, "Leaking017B", "Displacement"), "L")
    lk = np.asarray(Image.fromarray(lk, "F").resize((512, 512), Image.BILINEAR))
    yy = np.mgrid[0:512, 0:512][0].astype(np.float32) / 512
    n = _cellnoise(301, 512, 512)
    a = np.clip(lk * 0.55 + 0.35 * np.clip(1 - yy * 1.4, 0, 1) * n, 0, 1) * np.clip(1.1 - yy, 0, 1) ** 0.7
    a *= edge_fade(512, 512, 0.12, vert=False)
    a *= np.clip(yy * 12, 0, 1)
    col = brown[None, None] * (0.75 + 0.35 * n[..., None])
    put("leak_streaks", col, a * 0.85, r=0.7 - 0.2 * lk)

    # ceiling stain: ring with a darker tide edge
    r = _radial(256, 256) * 2
    n = _cellnoise(302, 256, 256, 2.4)
    rr = r + (n - 0.5) * 0.35
    body = np.clip((0.85 - rr) * 3, 0, 1) * 0.35
    ring = np.exp(-((rr - 0.8) / 0.05) ** 2) * 0.75
    inner = np.exp(-((rr - 0.45) / 0.06) ** 2) * 0.35
    put("ceiling_stain", np.broadcast_to(brown * 0.9, (256, 256, 3)), np.clip(body + ring + inner, 0, 1) *
        (rr < 0.95))

    # mould bloom: cut from Wallpaper002C's mould (dark areas), radial falloff, black-green
    mp = load(A(cache, "Wallpaper002C", "Color"))[256:512, 256:512]
    mL = 1 - lum(mp) / max(1e-4, lum(mp).max())
    r = _radial(256, 256) * 2 + (_cellnoise(303, 256, 256) - 0.5) * 0.5
    a = np.clip(mL * 2.2, 0, 1) * np.clip((1 - r) * 2.5, 0, 1)
    put("mould_bloom", np.broadcast_to(np.array([0.08, 0.1, 0.06], np.float32), (256, 256, 3)), a * 0.95, r=0.9)

    # floor stain: dried puddle (dark, slightly glossy rim)
    r = _radial(256, 256) * 2
    n = _cellnoise(304, 256, 256, 2.2)
    rr = r + (n - 0.5) * 0.6
    a = np.clip((0.8 - rr) * 4, 0, 1) * 0.55 + np.exp(-((rr - 0.78) / 0.04) ** 2) * 0.4
    put("floor_stain", np.broadcast_to(np.array([0.1, 0.075, 0.05], np.float32), (256, 256, 3)), a,
        r=0.55 + 0.3 * (rr > 0.75))

    # scuffs: short dark strokes
    rng = np.random.default_rng(305)
    sc_big = Canvas()
    for _ in range(36):
        x, y = rng.uniform(20, 236, 2)
        a0 = rng.normal(0, 0.2)
        L = rng.uniform(15, 80)
        sc_big.line([(x, y), (x + L * math.cos(a0), y + L * math.sin(a0) * 0.4)], rng.uniform(1.0, 4.0),
                    int(rng.integers(60, 200)))
    sc = sc_big.get()[:256, :256] * edge_fade(256, 256, 0.06)
    sc = blur_wrap(sc, 1.2)
    put("scuffs", np.broadcast_to(np.array([0.06, 0.05, 0.045], np.float32), (256, 256, 3)), sc * 0.8)

    # rust streaks: Leaking013A thin trickles, orange-brown
    lk = load(A(cache, "Leaking013A", "Displacement"), "L")[:, :512]
    lk = np.asarray(Image.fromarray(lk, "F").resize((256, 256), Image.BILINEAR))
    lk = max3(lk)
    yy = np.mgrid[0:256, 0:256][0].astype(np.float32) / 256
    a = np.clip(lk * 2.5, 0, 1) * np.clip(1.15 - yy, 0, 1) * edge_fade(256, 256, 0.05, vert=False)
    a = np.maximum(a, np.clip(0.25 - yy, 0, 1) * 2.0 * _cellnoise(306, 256, 256))
    put("rust_streaks", np.broadcast_to(np.array([0.36, 0.17, 0.07], np.float32), (256, 256, 3)), a * 0.9, r=0.8)

    # paper scraps: 2 x 2 sheets (yellowed paper, printed lines / a magazine block), torn edges
    x0, y0, w, h = DECALS["paper_scraps"]
    rgb = np.zeros((256, 256, 3), np.float32)
    al = np.zeros((256, 256), np.float32)
    hg = np.zeros((256, 256), np.float32)
    papers = [(0.86, 0.82, 0.70), (0.80, 0.78, 0.72), (0.88, 0.80, 0.62), (0.75, 0.70, 0.62)]
    rng = np.random.default_rng(307)
    for i in range(4):
        cx, cy = (i % 2) * 128, (i // 2) * 128
        sheet = Canvas()
        jag = []
        for k in range(28):
            t = k / 28 * 2 * math.pi
            rx, ry = 50 + rng.normal(0, 3), 58 + rng.normal(0, 3)
            jag.append((64 + rx * max(-1, min(1, 1.25 * math.cos(t))) + rng.normal(0, 1.5),
                        64 + ry * max(-1, min(1, 1.25 * math.sin(t))) + rng.normal(0, 1.5)))
        sheet.poly(jag, 255)
        m = sheet.get()[:128, :128]
        tint = np.array(papers[i], np.float32)
        lines = np.ones((128, 128), np.float32)
        if i in (0, 3):     # typed / printed lines
            for ly in range(22, 110, 7):
                lines[ly:ly + 2, 24:104 - (ly * 7) % 30] = 0.35
        else:               # magazine page: a picture block and a headline
            lines[20:30, 20:108] = 0.25
            blk = np.array([0.45, 0.25, 0.2], np.float32) if i == 1 else np.array([0.2, 0.3, 0.45], np.float32)
            lines3 = np.ones((128, 128, 3), np.float32)
            lines3[38:84, 22:106] = blk + 0.25 * _cellnoise(310 + i, 84, 46)[..., None]
            tint = tint[None, None] * lines3
        grime = 0.8 + 0.2 * _cellnoise(320 + i, 128, 128)
        col = (tint if tint.ndim == 3 else np.broadcast_to(tint, (128, 128, 3))) * (lines * grime)[..., None]
        rgb[cy:cy + 128, cx:cx + 128] = col
        al[cy:cy + 128, cx:cx + 128] = m
        hg[cy:cy + 128, cx:cx + 128] = m * (0.5 + 0.5 * _cellnoise(330 + i, 128, 128, 3.0))
    put("paper_scraps", rgb, al, r=0.9, hgt=hg)

    # plaster crumbs: irregular chips, light plaster with darker undersides
    cv = Canvas()
    rng = np.random.default_rng(308)
    for _ in range(120):
        x, y = rng.uniform(10, 246, 2)
        if math.hypot(x - 128, y - 128) > 118 * rng.uniform(0.5, 1.0):
            continue
        s = rng.uniform(1.5, 7.5)
        pts = [(x + s * rng.uniform(0.6, 1.2) * math.cos(t), y + s * rng.uniform(0.6, 1.2) * math.sin(t))
               for t in np.linspace(0, 2 * math.pi, int(rng.integers(5, 8)), endpoint=False)]
        cv.poly(pts, 255)
    cr = cv.get()[:256, :256]
    dust = np.clip((_cellnoise(309, 256, 256) - 0.55) * 2.5, 0, 1) * np.clip(1 - _radial(256, 256) * 2, 0, 1)
    col = np.broadcast_to(np.array([0.68, 0.64, 0.56], np.float32), (256, 256, 3)) * \
        (0.8 + 0.2 * _cellnoise(311, 256, 256))[..., None]
    put("plaster_crumbs", col, np.clip(cr + dust * 0.35, 0, 1), r=0.95, hgt=cr)

    # peel edge: torn wallpaper edge (paper-coloured lip, plaster rim, shadow line). Top = paper side.
    yy, xx = np.mgrid[0:256, 0:256].astype(np.float32)
    edge = 110 + 30 * (_cellnoise(312, 256, 256, 1.8)[128] - 0.5) * 2
    edge = edge[None, :] + 8 * (_cellnoise(313, 256, 256, 2.5) - 0.5)
    lip = np.clip(1 - np.abs(yy - edge) / 6, 0, 1)
    under = np.clip((yy - edge) / 40, 0, 1)
    rgb = np.broadcast_to(np.array([0.78, 0.72, 0.6], np.float32), (256, 256, 3)).copy()
    rgb *= (1 - 0.6 * np.clip(1 - np.abs(yy - edge - 9) / 6, 0, 1))[..., None]   # shadow under the curl
    a = np.clip(lip + np.clip(1 - np.abs(yy - edge - 9) / 6, 0, 1) * 0.8 + (1 - under) * (yy > edge) * 0.25, 0, 1)
    a *= edge_fade(256, 256, 0.1, vert=False)
    put("peel_edge", rgb, a, r=0.9, hgt=lip)

    # tide-line: horizontal band (tiles in U), darker line on top, staining fading downward
    x0, y0, w, h = DECALS["tide_line"]
    yy = np.mgrid[0:h, 0:w][0].astype(np.float32) / h
    n = pnoise(314, 2.0)[:h, :w]
    line = np.exp(-((yy - 0.2 - (n - 0.5) * 0.08) / 0.035) ** 2)
    below = np.clip((yy - 0.2) * 1.4, 0, 1) * (1 - yy) * 1.6 * (0.6 + 0.4 * n)
    put("tide_line", np.broadcast_to(np.array([0.30, 0.25, 0.16], np.float32), (h, w, 3)),
        np.clip(line * 0.85 + below * 0.5, 0, 1) * (yy > 0.12))

    # grime band: dirty fade (strongest at the bottom), tiles in U
    x0, y0, w, h = DECALS["grime_band"]
    yy = np.mgrid[0:h, 0:w][0].astype(np.float32) / h
    n = pnoise(315, 1.6)[:h, :w]
    put("grime_band", np.broadcast_to(np.array([0.12, 0.1, 0.08], np.float32), (h, w, 3)),
        np.clip(yy ** 1.6 * (0.5 + 0.7 * n), 0, 1) * 0.8)

    # premultiply-safe colour bleed: give transparent texels the cell's mean colour so mip edges don't go black
    for name, (x, y, w, h) in DECALS.items():
        c = base[y:y + h, x:x + w]
        wsum = c[..., 3].sum()
        if wsum > 0:
            mean = (c[..., :3] * c[..., 3:4]).reshape(-1, 3).sum(0) / wsum
            c[..., :3] = c[..., :3] * (c[..., 3:4] > 0.02) + mean * (c[..., 3:4] <= 0.02)
    base[..., :3] = l2s(base[..., :3])     # colours above were authored in linear
    nrm = normal_from_height(blur_wrap(height, 0.8), 6.0, wrap=False)
    d = os.path.join(out, "M_Decals")
    save(base, os.path.join(d, "M_Decals_Base.png"), "RGBA")
    save(nrm, os.path.join(d, "M_Decals_Normal.png"))
    save(mask_from(rough), os.path.join(d, "M_Decals_Mask.png"), "RGBA")
    uv = {k: {"px": list(v), "uv_rect_unity": [v[0] / RES, 1 - (v[1] + v[3]) / RES, v[2] / RES, v[3] / RES]}
          for k, v in DECALS.items()}
    json.dump({"atlas": "M_Decals_Base.png", "size": RES, "note": "uv_rect_unity = [u, v, w, h], bottom-left origin. tide_line and grime_band are seamless left-right: place quads end to end",
               "cells": uv}, open(os.path.join(d, "M_Decals_atlas.json"), "w"), indent=1)
    return uv


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "fetch":
        fetch(sys.argv[2])
    elif len(sys.argv) > 2 and sys.argv[1] == "build":
        root = sys.argv[sys.argv.index("--unity") + 1] if "--unity" in sys.argv else os.path.join(
            os.path.dirname(os.path.abspath(__file__)), "..", "..", "Unity", "Assets", "Art", "Environment", "Bungalow")
        build(sys.argv[2], os.path.normpath(root))
        print("built textures under", os.path.normpath(os.path.join(root, "Textures")))
    else:
        print(__doc__)
