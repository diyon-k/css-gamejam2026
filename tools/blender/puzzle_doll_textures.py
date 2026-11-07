"""BLINK P2: the atlas for Lily's doll and her key (one 2048 x 2048 PBR set, procedural, no external sources).

Plain Python (numpy + Pillow, no bpy), like bungalow_prop_textures.py:

  python -I tools/blender/puzzle_doll_textures.py [--preview DIR]

Writes Unity/Assets/Art/Puzzles/LilysDoll/Textures/
  LilysDoll_Base.png     sRGB, opaque
  LilysDoll_Normal.png   OpenGL (Y+) tangent normal
  LilysDoll_Mask.png     linear: R metallic, G AO, B 0, A smoothness (the same layout as the prop atlases)

puzzle_doll.py imports this module for the layout (CELL / SUB / face_planar / eye_planar), so the geometry and the
painting cannot drift apart. Arrays are [row, col] with row 0 the BOTTOM of the image (V = 0), as in Blender.
Layout (px, x y w h from the bottom left):
  face   0 1024 1024 1024   planar front projection of the head: x -0.095..0.095 m, z 0..0.2 m (head-local)
  hair   0  512 1024  512   left half the scalp cap (u round the head, v crown -> hairline), right half the ringlets
  eye    0  256  256  256   brass 256 256   shoe 512 256   stocking 768 256 (each 256 x 256)
  porc   1024 0 1024 640    skull back, neck, palms, fingers
  cotton 1024 640 1024 384  pinafore bib (left) and apron
  lace   1024 1024 1024 512 band y 384..512 fine lace (32 px period), 256..384 coarse (64 px period)
  dress  1024 1536 1024 512 skirt (left), bodice, sleeves
"""
import os
import sys

try:
    import numpy as np
except ImportError:  # `python -I` drops user site-packages
    import site
    sys.path.append(site.getusersitepackages())
    import numpy as np

A = 2048
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Unity", "Assets", "Art", "Puzzles", "LilysDoll", "Textures")

CELL = {"face": (0, 1024, 1024, 1024), "hair": (0, 512, 1024, 512), "eye": (0, 256, 256, 256),
        "brass": (256, 256, 256, 256), "shoe": (512, 256, 256, 256), "stock": (768, 256, 256, 256),
        "porc": (1024, 0, 1024, 640), "cotton": (1024, 640, 1024, 384), "lace": (1024, 1024, 1024, 512),
        "dress": (1024, 1536, 1024, 512)}

# name: (cell, x0, y0, x1, y1) in px inside the cell
SUB = {"skirt": ("dress", 0, 0, 640, 512), "bodice": ("dress", 640, 256, 1024, 512), "sleeve": ("dress", 640, 0, 1024, 256),
       "collar": ("lace", 0, 384, 1024, 512), "cuff": ("lace", 0, 384, 320, 512), "frill": ("lace", 0, 256, 1024, 320),
       "frill_small": ("lace", 0, 256, 224, 320),
       "bib": ("cotton", 0, 0, 400, 384), "apron": ("cotton", 400, 0, 1024, 384), "strap": ("cotton", 400, 0, 520, 40),
       "head_back": ("porc", 0, 0, 640, 512), "neck": ("porc", 640, 0, 1024, 256),
       "palm": ("porc", 640, 256, 1024, 448), "finger": ("porc", 640, 448, 1024, 640),
       "hair_cap": ("hair", 0, 0, 512, 512), "ringlet": ("hair", 512, 0, 1024, 512),
       "stocking": ("stock", 0, 0, 256, 256), "shoe": ("shoe", 0, 0, 256, 256), "key": ("brass", 0, 0, 256, 256)}

FX0, FW, FZ0, FH = -0.095, 0.19, 0.0, 0.20   # the face cell's window onto the head
INSET = 3


def sub(name, repeats=None):
    """(u0, v0, u1, v1) in atlas UV for a named sub-rectangle, inset by a few px against mip bleed."""
    cell, x0, y0, x1, y1 = SUB[name]
    cx, cy, _, _ = CELL[cell]
    return ((cx + x0 + INSET) / A, (cy + y0 + INSET) / A, (cx + x1 - INSET) / A, (cy + y1 - INSET) / A)


def _cell_uv(cell, u, v):
    cx, cy, w, h = CELL[cell]
    u = min(max(u, 0.0), 1.0)
    v = min(max(v, 0.0), 1.0)
    return ((cx + INSET + (w - 2 * INSET) * u) / A, (cy + INSET + (h - 2 * INSET) * v) / A)


def face_planar(x, z):
    return _cell_uv("face", (x - FX0) / FW, (z - FZ0) / FH)


def eye_planar(x, z, R):
    return _cell_uv("eye", 0.5 + x / (2 * R), 0.5 + z / (2 * R))


# ------------------------------------------------------------------------------------------------ numpy toolbox

def fn(h, w, seed, beta=1.0, lo=1.0, hi=None, su=1.0, sv=1.0):
    """Fractal noise in [-1, 1] over an h x w patch (periodic). sv large => streaks that run along v (the rows axis)."""
    r = np.random.default_rng(seed)
    fu = np.fft.fftfreq(w) * w
    fv = np.fft.fftfreq(h) * h
    FU, FV = np.meshgrid(fu, fv)
    k = np.sqrt((FU * su) ** 2 + (FV * sv) ** 2)
    amp = np.where(k < lo, 0.0, 1.0 / np.maximum(k, 1e-6) ** beta)
    if hi:
        amp = amp * np.exp(-(k / hi) ** 2)
    out = np.real(np.fft.ifft2(np.fft.fft2(r.standard_normal((h, w))) * amp))
    return (out / (np.abs(out).max() + 1e-9)).astype(np.float32)


def blur(a, sigma):
    h, w = a.shape
    fu = np.fft.fftfreq(w) * w
    fv = np.fft.fftfreq(h) * h
    FU, FV = np.meshgrid(fu, fv)
    g = np.exp(-((FU / w) ** 2 + (FV / h) ** 2) * (2 * np.pi ** 2) * sigma ** 2)
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g)).astype(np.float32)


def sstep(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def worley(h, w, seed, cell_px, jit=0.85, warp=None):
    """F2 - F1 in px: small on the borders between cells (a crazing web)."""
    r = np.random.default_rng(seed)
    nx, ny = int(w / cell_px) + 2, int(h / cell_px) + 2
    jx = (r.random((ny, nx)) - 0.5) * jit + 0.5
    jy = (r.random((ny, nx)) - 0.5) * jit + 0.5
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    x = xx / cell_px
    y = yy / cell_px
    if warp is not None:
        x = x + warp[0]
        y = y + warp[1]
    cx = np.floor(x).astype(int)
    cy = np.floor(y).astype(int)
    f1 = np.full((h, w), 9.0, np.float32)
    f2 = np.full((h, w), 9.0, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            gx, gy = cx + dx, cy + dy
            px = gx + jx[np.mod(gy, ny), np.mod(gx, nx)]
            py = gy + jy[np.mod(gy, ny), np.mod(gx, nx)]
            d = np.sqrt((x - px) ** 2 + (y - py) ** 2)
            f2 = np.minimum(f2, np.maximum(f1, d))
            f1 = np.minimum(f1, d)
    return (f2 - f1) * cell_px


def normal_from(hf, strength):
    du = (np.roll(hf, -1, 1) - np.roll(hf, 1, 1)) * 0.5 * strength
    dv = (np.roll(hf, -1, 0) - np.roll(hf, 1, 0)) * 0.5 * strength
    n = np.stack([-du, -dv, np.ones_like(hf)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


class Atlas:
    def __init__(self):
        self.base = np.full((A, A, 3), 0.5, np.float32)
        self.h = np.zeros((A, A), np.float32)       # height, in "px of relief"
        self.hs = np.ones((A, A), np.float32)       # normal strength per texel
        self.smooth = np.full((A, A), 0.3, np.float32)
        self.metal = np.zeros((A, A), np.float32)
        self.ao = np.ones((A, A), np.float32)

    def region(self, cell, x0=0, y0=0, x1=None, y1=None):
        cx, cy, w, h = CELL[cell]
        x1 = w if x1 is None else x1
        y1 = h if y1 is None else y1
        return (slice(cy + y0, cy + y1), slice(cx + x0, cx + x1))


def mix(a, b, t):
    t = t[..., None] if t.ndim == a.ndim - 1 else t
    return a * (1 - t) + b * t


def col(r, g, b):
    return np.array([r, g, b], np.float32)


# ------------------------------------------------------------------------------------------------ painters

PORC = col(0.90, 0.865, 0.80)


def porcelain(base, h, smooth, ao, seed, crack_cell, density=1.0):
    """The glaze: warm, slightly uneven, with a hairline crazing web in patches. Returns the crack mask."""
    hh, ww = h.shape
    low = fn(hh, ww, seed, 1.6, lo=1, hi=24)
    mid = fn(hh, ww, seed + 1, 1.0, lo=4, hi=120)
    base[:] = PORC * (1.0 + 0.045 * low[..., None])
    base[:] = mix(base, col(0.80, 0.72, 0.58), np.clip(sstep(mid, 0.3, 0.9) * 0.35, 0, 1))   # age yellowing
    smooth[:] = 0.86 - 0.10 * sstep(mid, 0.1, 0.9)
    warp = (fn(hh, ww, seed + 2, 1.3, lo=2, hi=60) * 0.9, fn(hh, ww, seed + 3, 1.3, lo=2, hi=60) * 0.9)
    wob = worley(hh, ww, seed + 4, crack_cell, warp=warp)
    craze = 1.0 - sstep(wob, 0.4, 1.9)
    patch = sstep(fn(hh, ww, seed + 5, 1.4, lo=1, hi=10), -0.25, 0.25) * density
    # a few long cracks: boundaries of a much bigger web, rarely present
    big = worley(hh, ww, seed + 6, crack_cell * 5.0, jit=0.9, warp=(warp[0] * 0.25, warp[1] * 0.25))
    long = (1.0 - sstep(big, 0.5, 2.4)) * sstep(fn(hh, ww, seed + 7, 1.2, lo=1, hi=8), 0.15, 0.5)
    line = np.clip(craze * patch * 0.8 + long, 0, 1)
    base[:] = mix(base, col(0.36, 0.28, 0.20), np.clip(line * 0.55, 0, 1))
    smooth[:] = smooth * (1 - 0.55 * line)
    h[:] += -1.6 * line
    ao[:] = ao * (1 - 0.35 * line)
    return line


def paint_face(at):
    R = at.region("face")
    base, h, sm, ao = at.base[R].copy(), at.h[R].copy(), at.smooth[R].copy(), at.ao[R].copy()
    n = 1024
    line = porcelain(base, h, sm, ao, 100, 38.0, density=0.75)
    rr, cc = np.mgrid[0:n, 0:n].astype(np.float32)
    X = FX0 + (cc + 0.5) / n * FW
    Z = FZ0 + (rr + 0.5) / n * FH
    sg = lambda a, s: np.exp(-(a / s) ** 2)

    # rosy cheeks, a faint flush on the nose and chin
    blush = np.zeros((n, n), np.float32)
    for s in (-1, 1):
        blush += sg(np.hypot((X - 0.052 * s) / 0.021, (Z - 0.074) / 0.017), 1.0)
    blush += 0.55 * sg(np.hypot(X / 0.008, (Z - 0.083) / 0.009), 1.0) + 0.25 * sg(np.hypot(X / 0.012, (Z - 0.013) / 0.007), 1.0)
    base[:] = mix(base, col(0.78, 0.42, 0.38), np.clip(blush * 0.5, 0, 0.55))

    # tear track under the left eye, faded
    tx = -0.044 + 0.004 * np.sin((Z - 0.07) * 160)
    track = sg((X - tx) / 0.0028, 1.0) * sstep(Z, 0.052, 0.088) * (1 - sstep(Z, 0.088, 0.093)) * (0.6 + 0.4 * fn(n, n, 120, 1.2, lo=2, hi=60))
    base[:] = mix(base, col(0.42, 0.36, 0.30), np.clip(track * 0.55, 0, 0.6))
    sm[:] = sm * (1 - 0.3 * track)

    # eye sockets: lid shadow ring, upper lid crease, lash lines
    for s in (-1, 1):
        ex, ez, Re = 0.0375 * s, 0.1125, 0.0212
        d = np.hypot(X - ex, Z - ez)
        ring = sstep(d, Re * 0.9, Re * 1.05) * (1 - sstep(d, Re * 1.05, Re * 1.75))
        base[:] = mix(base, col(0.52, 0.40, 0.36), np.clip(ring * 0.55, 0, 0.6))
        ao[:] = ao * (1 - 0.5 * ring)
        ang = np.arctan2(Z - ez, X - ex)
        upper = sstep(np.sin(ang), 0.05, 0.4)
        crease = sg((d - Re * 1.45) / 0.0016, 1.0) * upper
        base[:] = mix(base, col(0.38, 0.26, 0.24), np.clip(crease * 0.7, 0, 0.8))
        h[:] += -1.8 * crease
        lash = sg((d - Re * 1.02) / 0.0013, 1.0) * sstep(np.sin(ang), -0.1, 0.35)
        ticks = (0.8 + 0.2 * np.cos(ang * 22)) ** 2
        base[:] = mix(base, col(0.03, 0.02, 0.02), np.clip(lash * (0.65 + 0.35 * ticks), 0, 0.97))
        low = sg((d - Re * 1.05) / 0.001, 1.0) * sstep(-np.sin(ang), 0.1, 0.5) * 0.4
        base[:] = mix(base, col(0.12, 0.08, 0.07), np.clip(low, 0, 0.5))

    # brows: tapering strokes, a little uneven
    for s, tilt in ((-1, 0.0035), (1, 0.0)):
        t = np.clip(((X - 0.0375 * s) * s + 0.001) / 0.021 * 1.0, -1.2, 1.2)
        zc = 0.1385 + 0.0075 * (1 - np.minimum(np.abs(t), 1.4) ** 2) - 0.0045 * np.maximum(t, 0) ** 2 + tilt
        thick = 0.0021 * np.clip(1.15 - np.abs(t) ** 2.0, 0, 1) ** 0.6
        stroke = sstep(thick - np.abs(Z - zc), 0.0, 0.0008) * (np.abs(t) < 1.1)
        hair_tex = 0.8 + 0.2 * fn(n, n, 130 + s, 0.4, lo=60)
        base[:] = mix(base, col(0.17, 0.10, 0.075), np.clip(stroke * hair_tex, 0, 0.92))

    # nostrils, philtrum dimple
    for s in (-1, 1):
        nn = sg(np.hypot((X - 0.0042 * s) / 0.0017, (Z - 0.0765) / 0.0013), 1.0)
        base[:] = mix(base, col(0.22, 0.10, 0.09), np.clip(nn * 0.85, 0, 0.9))
        ao[:] = ao * (1 - 0.5 * nn)
    nose_shadow = sg(np.hypot(X / 0.016, (Z - 0.0705) / 0.006), 1.0)
    ao[:] = ao * (1 - 0.35 * nose_shadow)

    # painted lips: upper with a cupid's bow, fuller lower; the paint has worn and flaked in places
    mx, mz = np.abs(X), Z - 0.0525
    upper_h = 0.0043 * np.clip(1 - (mx / 0.0185) ** 2, 0, 1) ** 0.65 * (1 - 0.35 * sg(mx / 0.004, 1.0))
    lower_h = 0.0058 * np.clip(1 - (mx / 0.0168) ** 2, 0, 1) ** 0.75
    lip = ((mz >= 0) & (mz < upper_h)) | ((mz < 0) & (mz > -lower_h))
    lip = lip.astype(np.float32)
    lip = blur(lip, 1.0)
    flake = sstep(fn(n, n, 140, 0.8, lo=30, hi=200), 0.35, 0.7)
    paint = np.clip(lip * (1 - 0.8 * flake), 0, 1)
    lipcol = col(0.50, 0.075, 0.09) * (0.85 + 0.2 * fn(n, n, 141, 0.5, lo=40)[..., None])
    base[:] = mix(base, lipcol, np.clip(paint, 0, 0.97))
    sm[:] = sm * (1 - paint) + 0.78 * paint
    seam = sg(mz / 0.0007, 1.0) * np.clip(1 - (mx / 0.0182) ** 2, 0, 1)
    base[:] = mix(base, col(0.18, 0.03, 0.04), np.clip(seam * 0.9, 0, 0.9))
    h[:] += -2.8 * seam + 0.9 * lip
    ao[:] = ao * (1 - 0.45 * seam)

    # chips: bisque body showing through the glaze
    for (cx, cz, rad, depth) in ((0.0015, 0.0802, 0.0036, 3.0), (-0.052, 0.1475, 0.0062, 2.4), (0.0675, 0.046, 0.0052, 2.6),
                                 (-0.0055, 0.0135, 0.0042, 2.2), (0.046, 0.0993, 0.0028, 1.6)):
        nz = fn(n, n, int(abs(cx * 9000)) + 150, 0.9, lo=5, hi=120)
        d = np.hypot(X - cx, Z - cz) / (rad * (1 + 0.55 * nz))
        inside = 1 - sstep(d, 0.92, 1.0)
        rim = sstep(d, 0.78, 0.98) * (1 - sstep(d, 0.98, 1.22))
        base[:] = mix(base, col(0.74, 0.69, 0.60), np.clip(inside, 0, 1))
        base[:] = mix(base, col(0.45, 0.38, 0.30), np.clip(rim * 0.55, 0, 0.6))
        sm[:] = sm * (1 - inside) + 0.12 * inside
        h[:] += -depth * inside + 0.8 * rim + 0.5 * inside * nz
        ao[:] = ao * (1 - 0.4 * rim)

    # grime: the hairline edge and the jaw, grey-brown, plus fingermarks
    grime = sstep(fn(n, n, 160, 1.5, lo=1, hi=25), -0.2, 0.9) * 0.35 + 0.5 * sstep(np.abs(X), 0.07, 0.095) + 0.35 * (1 - sstep(Z, 0.0, 0.03))
    base[:] = mix(base, col(0.40, 0.34, 0.27), np.clip(grime * 0.5, 0, 0.55))
    sm[:] = sm * (1 - 0.35 * np.clip(grime, 0, 1))
    at.base[R], at.h[R], at.smooth[R], at.ao[R] = base, h, sm, ao
    at.hs[R] = 0.9


def paint_porc(at):
    R = at.region("porc")
    base, h, sm, ao = at.base[R].copy(), at.h[R].copy(), at.smooth[R].copy(), at.ao[R].copy()
    porcelain(base, h, sm, ao, 200, 34.0, density=0.8)
    # fingers and palms: faint knuckle lines painted into the height
    hh, ww = h.shape
    knuckle = np.zeros_like(h)
    yy, xx = np.mgrid[0:hh, 0:ww]
    knuckle += (np.abs(((yy % 64) - 32)) < 2) * 1.0
    h[:] += -0.6 * blur(knuckle.astype(np.float32), 1.0) * (xx > 640)
    base[:] = mix(base, col(0.50, 0.42, 0.34), np.clip(0.18 * sstep(fn(hh, ww, 210, 1.4, lo=1, hi=18), 0.0, 0.8), 0, 0.3))
    at.base[R], at.h[R], at.smooth[R], at.ao[R] = base, h, sm, ao
    at.hs[R] = 0.9


def paint_dress(at):
    R = at.region("dress")
    n_h, n_w = 512, 1024
    pile = fn(n_h, n_w, 300, 0.5, lo=40)
    streak = fn(n_h, n_w, 301, 1.0, lo=3, hi=300, sv=14)
    worn = sstep(fn(n_h, n_w, 302, 1.5, lo=1, hi=14), 0.0, 0.7)
    stain = sstep(fn(n_h, n_w, 303, 1.6, lo=1, hi=12), 0.15, 0.8)
    base0 = col(0.29, 0.042, 0.054)
    faded = col(0.40, 0.11, 0.105)
    base = base0 * (1 + 0.18 * pile[..., None] + 0.12 * streak[..., None])
    base = mix(base, faded, np.clip(worn * 0.5, 0, 0.55))
    base = mix(base, col(0.17, 0.11, 0.08), np.clip(stain * 0.35, 0, 0.4))
    rr, cc = np.mgrid[0:n_h, 0:n_w].astype(np.float32)
    # the skirt (left 640 px): folds shade the cloth a little, grime at the hem (top of the rect)
    u = cc / 640.0
    fold = np.sin(2 * np.pi * u * 9 + 1.3) + 0.5 * np.sin(2 * np.pi * u * 17 + 0.4)
    inskirt = (cc < 640).astype(np.float32)
    v = rr / n_h
    shade = 1 - 0.22 * sstep(-fold, 0.2, 1.3) * sstep(v, 0.1, 0.9) * inskirt
    base = base * shade[..., None]
    hem = sstep(v, 0.55, 1.0) * inskirt
    dirt = np.clip(hem * (0.55 + 0.6 * fn(n_h, n_w, 304, 1.1, lo=2, hi=90)), 0, 1)
    base = mix(base, col(0.15, 0.115, 0.085), np.clip(dirt * 0.9, 0, 0.9))
    dust = sstep(fn(n_h, n_w, 305, 1.2, lo=2, hi=60), 0.35, 0.8) * hem
    base = mix(base, col(0.34, 0.31, 0.26), np.clip(dust * 0.55, 0, 0.6))
    sm = 0.26 + 0.12 * streak + 0.08 * worn - 0.14 * dirt
    hf = 0.35 * pile + 0.5 * streak + 1.2 * fold * 0.15 * inskirt * sstep(v, 0.1, 0.9)
    ao = 1 - 0.3 * sstep(-fold, 0.4, 1.3) * inskirt * sstep(v, 0.1, 0.9)
    at.base[R], at.h[R], at.smooth[R], at.ao[R] = base.astype(np.float32), hf.astype(np.float32), sm.astype(np.float32), ao.astype(np.float32)
    at.hs[R] = 1.0


def lace_pattern(h, w, P, seed):
    """Thread coverage (1 = thread) for an h x w patch tiled with period P: a net with a rosette in each diamond."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    x = (xx % P) / P
    y = (yy % P) / P
    q = 4.0
    d1 = np.abs(((x + y) * q) % 1.0 - 0.5) * 2    # 1 at the line centres' complement
    d2 = np.abs(((x - y) * q) % 1.0 - 0.5) * 2
    net = np.maximum(sstep(d1, 0.66, 0.84), sstep(d2, 0.66, 0.84))
    cx, cy = x - 0.5, y - 0.5
    r = np.hypot(cx, cy)
    th = np.arctan2(cy, cx)
    petals = (r < 0.17 * (0.55 + 0.45 * np.abs(np.cos(3 * th)))).astype(np.float32) * 1.0
    ringm = ((r > 0.30) & (r < 0.345)).astype(np.float32)
    dots = ((np.hypot(np.abs(cx) - 0.5, np.abs(cy) - 0.5) < 0.06)).astype(np.float32)
    hole_in = (r < 0.28).astype(np.float32)
    m = np.clip(net * (1 - hole_in * 0.0) + petals + ringm * 0.9 + dots, 0, 1)
    m = np.where(r < 0.28, np.clip(petals + 0.0 * net, 0, 1) * 1.0 + 0.0, m)
    m = np.clip(m, 0, 1)
    return blur(m.astype(np.float32), 0.6)


def paint_lace(at):
    R = at.region("lace")
    H, W = 512, 1024
    base = np.zeros((H, W, 3), np.float32)
    h = np.zeros((H, W), np.float32)
    sm = np.full((H, W), 0.12, np.float32)
    ao = np.ones((H, W), np.float32)
    thread = col(0.80, 0.765, 0.67)
    gap = col(0.10, 0.045, 0.045)
    for (y0, y1, P, seed) in ((384, 512, 32, 400), (256, 384, 64, 401)):
        m = lace_pattern(y1 - y0, W, P, seed)
        grime = sstep(fn(y1 - y0, W, seed + 1, 1.3, lo=1, hi=30), 0.0, 0.8)
        edge = sstep(np.mgrid[0:y1 - y0, 0:W][0] / (y1 - y0), 0.5, 1.0)     # the outer edge is dirtier
        tcol = mix(thread * np.ones((y1 - y0, W, 1), np.float32), col(0.46, 0.38, 0.27), np.clip(grime * 0.5 + edge * 0.35, 0, 0.7))
        base[y0:y1] = mix(gap * np.ones((y1 - y0, W, 1), np.float32), tcol, np.clip(m * 1.3, 0, 1))
        h[y0:y1] = m * 3.0
        sm[y0:y1] = 0.08 + 0.1 * m
        ao[y0:y1] = 0.55 + 0.45 * m
    # the rest of the cell: plain
    base[:256] = thread
    at.base[R], at.h[R], at.smooth[R], at.ao[R] = base, h, sm, ao
    at.hs[R] = 1.0


def paint_cotton(at):
    R = at.region("cotton")
    H, W = 384, 1024
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    T = 5.0
    warp = 0.5 + 0.5 * np.sin(2 * np.pi * xx / T)
    weft = 0.5 + 0.5 * np.sin(2 * np.pi * yy / T)
    over = (((xx // (T / 2)) + (yy // (T / 2))) % 2) < 1
    hf = np.where(over, warp * 0.8 + weft * 0.2, weft * 0.8 + warp * 0.2)
    irregular = fn(H, W, 500, 0.4, lo=20)
    base = col(0.68, 0.63, 0.53) * (1 - 0.05 * hf[..., None] + 0.04 * irregular[..., None])
    stain = sstep(fn(H, W, 501, 1.6, lo=1, hi=14), 0.1, 0.8)
    base = mix(base, col(0.52, 0.45, 0.33), np.clip(stain * 0.45, 0, 0.5))
    # the apron (right 624 px) is dirtiest at its hem (top of its rect)
    v = yy / H
    inapron = (xx >= 400).astype(np.float32)
    hem = sstep(v, 0.6, 1.0) * inapron
    dirt = np.clip(hem * (0.5 + 0.7 * fn(H, W, 502, 1.1, lo=2, hi=80)), 0, 1)
    base = mix(base, col(0.22, 0.17, 0.12), np.clip(dirt * 0.85, 0, 0.85))
    # one old tea stain, and a rust-brown smear
    for (px_, py_, rr_) in ((470, 120, 34), (780, 220, 70)):
        d = np.hypot(xx - px_, yy - py_) / rr_
        ring = sstep(d, 0.55, 0.92) * (1 - sstep(d, 0.92, 1.05))
        base = mix(base, col(0.40, 0.29, 0.17), np.clip(ring * 0.4 + (1 - sstep(d, 0.6, 0.9)) * 0.15, 0, 0.5))
    sm = 0.12 + 0.0 * hf
    at.base[R], at.h[R], at.smooth[R] = base.astype(np.float32), (hf * 0.5 + irregular * 0.3).astype(np.float32), sm
    at.ao[R] = (0.75 + 0.25 * hf).astype(np.float32)
    at.hs[R] = 0.35


def paint_hair(at):
    R = at.region("hair")
    H, W = 512, 1024
    streak = fn(H, W, 600, 0.7, lo=4, hi=400, su=1.0, sv=40)          # fine strands running along v
    fine = fn(H, W, 601, 0.5, lo=30, hi=500, sv=60)
    clump = fn(H, W, 602, 1.3, lo=2, hi=40, sv=10)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    ripple = np.sin(2 * np.pi * (xx / 512.0 * 11 + 0.25 * clump * 3))
    dark = col(0.032, 0.017, 0.011)
    warm = col(0.13, 0.07, 0.04)
    t = np.clip(0.5 + 0.55 * streak + 0.25 * fine, 0, 1)
    base = mix(dark * np.ones((H, W, 1), np.float32), warm, np.clip(sstep(t, 0.55, 1.0) * 0.8, 0, 0.8))
    base = base * (1 - 0.35 * sstep(-ripple, 0.2, 1.0))[..., None]
    v = (yy % 512) / 512.0
    base = base * (1 - 0.25 * sstep(v, 0.8, 1.0))[..., None]                 # darker towards the hairline / ends
    base = mix(base, col(0.20, 0.18, 0.15), np.clip(0.2 * sstep(fn(H, W, 603, 1.4, lo=1, hi=20), 0.3, 0.9), 0, 0.2))   # dust
    hf = streak * 1.2 + fine * 0.6 + ripple * 0.7 * 0.5 + clump * 1.0
    sm = 0.38 + 0.2 * np.clip(streak, 0, 1) - 0.12 * sstep(-ripple, 0.0, 1.0)
    ao = 1 - 0.45 * sstep(-ripple, 0.2, 1.0)
    at.base[R], at.h[R], at.smooth[R], at.ao[R] = base.astype(np.float32), hf.astype(np.float32), sm.astype(np.float32), ao.astype(np.float32)
    at.hs[R] = 1.2


def paint_eye(at):
    R = at.region("eye")
    n = 256
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    u, v = (xx + 0.5) / n - 0.5, (yy + 0.5) / n - 0.5
    rho = np.hypot(u, v) * 2          # 1 at the rim of the front cap
    th = np.arctan2(v, u)
    streak = fn(n, n, 700, 0.3, lo=20)
    radial = 0.5 + 0.5 * np.sin(th * 41 + streak * 3)
    iris = 1 - sstep(rho, 0.50, 0.56)
    pupil = 1 - sstep(rho, 0.20, 0.235)
    limbus = sstep(rho, 0.40, 0.52) * iris
    c_iris = mix(np.broadcast_to(col(0.10, 0.045, 0.025), (n, n, 3)).copy(), np.broadcast_to(col(0.20, 0.115, 0.055), (n, n, 3)).copy(), np.clip(radial * (1 - sstep(rho, 0.22, 0.46)), 0, 1))
    c_iris = c_iris * (1 - 0.8 * limbus)[..., None]
    base = np.broadcast_to(col(0.012, 0.011, 0.012), (n, n, 3)).copy()
    base = mix(base, np.broadcast_to(col(0.075, 0.062, 0.055), (n, n, 3)).copy(), np.clip(sstep(rho, 0.78, 1.0) * 0.8, 0, 0.8))
    base = mix(base, c_iris, np.clip(iris, 0, 1))
    base = base * (1 - pupil)[..., None] + col(0.004, 0.004, 0.004) * pupil[..., None]
    at.base[R] = base.astype(np.float32)
    at.smooth[R] = 0.985
    at.metal[R] = 0.0
    at.h[R] = 0.0
    at.ao[R] = 1.0
    at.hs[R] = 0.0


def paint_brass(at):
    R = at.region("brass")
    n = 256
    low = fn(n, n, 800, 1.5, lo=1, hi=16)
    mid = fn(n, n, 801, 1.0, lo=4, hi=90)
    pit = sstep(fn(n, n, 802, 0.3, lo=40), 0.55, 0.85)
    scr = np.clip(1 - np.abs(fn(n, n, 803, 0.9, lo=4, sv=22)) * 14, 0, 1) * 0.6
    tarnish = sstep(low + 0.3 * mid, 0.0, 0.9)
    bright = col(0.60, 0.43, 0.17)
    dull = col(0.25, 0.17, 0.075)
    base = mix(np.broadcast_to(bright, (n, n, 3)).copy(), np.broadcast_to(dull, (n, n, 3)).copy(), np.clip(tarnish * 0.75 + pit * 0.4, 0, 0.9))
    base = base * (1 - 0.15 * scr)[..., None]
    metal = np.clip(0.97 - 0.35 * tarnish - 0.4 * pit, 0.2, 1)
    sm = np.clip(0.55 - 0.30 * tarnish - 0.25 * pit + 0.12 * scr, 0.1, 0.62)
    at.base[R] = base.astype(np.float32)
    at.metal[R] = metal.astype(np.float32)
    at.smooth[R] = sm.astype(np.float32)
    at.h[R] = (-pit * 1.5 + mid * 0.4 - scr * 0.8).astype(np.float32)
    at.ao[R] = (1 - 0.35 * pit).astype(np.float32)
    at.hs[R] = 1.2


def paint_shoe(at):
    R = at.region("shoe")
    n = 256
    craze = 1 - sstep(worley(n, n, 900, 11.0, warp=(fn(n, n, 901, 1.2, lo=2, hi=30) * 0.6, fn(n, n, 902, 1.2, lo=2, hi=30) * 0.6)), 0.3, 1.3)
    craze *= sstep(fn(n, n, 903, 1.2, lo=1, hi=8), -0.3, 0.3)
    scuff = sstep(fn(n, n, 904, 1.2, lo=2, hi=40), 0.35, 0.8)
    base = np.broadcast_to(col(0.014, 0.014, 0.016), (n, n, 3)).copy()
    base = mix(base, np.broadcast_to(col(0.20, 0.18, 0.16), (n, n, 3)).copy(), np.clip(craze * 0.35 + scuff * 0.2, 0, 0.5))
    at.base[R] = base.astype(np.float32)
    at.smooth[R] = np.clip(0.93 - 0.55 * craze - 0.4 * scuff, 0.1, 0.95).astype(np.float32)
    at.h[R] = (-craze * 1.4 - scuff * 0.2).astype(np.float32)
    at.ao[R] = (1 - 0.3 * craze).astype(np.float32)
    at.hs[R] = 1.0


def paint_stocking(at):
    R = at.region("stock")
    n = 256
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    Tk = 5.0
    wale = np.abs(np.sin(np.pi * xx / Tk)) ** 0.7
    vee = np.abs(np.sin(np.pi * (yy / Tk * 1.0 + (np.floor(xx / Tk) % 2) * 0.5))) ** 0.8
    hf = wale * 0.7 + vee * 0.5
    base = col(0.80, 0.78, 0.71) * (1 - 0.12 * hf[..., None])
    grime = sstep(fn(n, n, 950, 1.3, lo=1, hi=20), 0.0, 0.8)
    base = mix(base, col(0.38, 0.32, 0.24), np.clip(grime * 0.4, 0, 0.5))
    at.base[R] = base.astype(np.float32)
    at.smooth[R] = 0.08
    at.h[R] = (hf * 1.6).astype(np.float32)
    at.ao[R] = (0.7 + 0.3 * hf).astype(np.float32)
    at.hs[R] = 1.0


# ------------------------------------------------------------------------------------------------ output

def build():
    at = Atlas()
    at.smooth[:] = 0.3
    for p in (paint_face, paint_porc, paint_dress, paint_lace, paint_cotton, paint_hair, paint_eye, paint_brass, paint_shoe,
              paint_stocking):
        p(at)
        print("painted", p.__name__)
    return at


def save(at, out_dir, preview=None):
    from PIL import Image
    os.makedirs(out_dir, exist_ok=True)
    nrm = normal_from(at.h * at.hs, 0.9)
    one = np.ones((A, A), np.float32)
    base = np.dstack([np.clip(at.base, 0, 1), one])
    nrmi = np.dstack([nrm, one])
    mask = np.dstack([np.clip(at.metal, 0, 1), np.clip(at.ao, 0, 1), np.zeros_like(one), np.clip(at.smooth, 0, 1)])
    for name, arr in (("Base", base), ("Normal", nrmi), ("Mask", mask)):
        img = Image.fromarray((np.flipud(arr) * 255 + 0.5).astype(np.uint8), "RGBA")
        img.save(os.path.join(out_dir, f"LilysDoll_{name}.png"), optimize=True)
        print("wrote", name)
    if preview:
        os.makedirs(preview, exist_ok=True)
        Image.fromarray((np.flipud(base[..., :3]) * 255).astype(np.uint8)).save(os.path.join(preview, "atlas_base.png"))
        Image.fromarray((np.flipud(nrm) * 255).astype(np.uint8)).save(os.path.join(preview, "atlas_normal.png"))


if __name__ == "__main__":
    prev = None
    if "--preview" in sys.argv:
        prev = sys.argv[sys.argv.index("--preview") + 1]
    save(build(), OUT, prev)
