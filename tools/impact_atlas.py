"""Procedural impact-mark atlas: 4x4 cells of 512px, albedo+alpha and a tangent-space normal map.

Cell order matches MarkCell in Unity/Assets/Scripts/Effects/ImpactMarks/Surfaces.cs (cell 0 = top-left, row-major).
Run from the repo root:  python tools/impact_atlas.py
"""
import os

import numpy as np
from PIL import Image, ImageDraw

C = 512          # cell size
SS = 2           # supersample factor for line drawing
MARGIN = 24      # fully transparent border per cell
OUT = os.path.join(os.path.dirname(__file__), "..", "Unity", "Assets", "Resources", "ImpactMarks")

_g = (np.arange(C) + 0.5) / C * 2 - 1
X, Y = np.meshgrid(_g, _g)          # x right, y DOWN (image rows), both in [-1, 1]
R = np.hypot(X, Y)
TH = np.arctan2(Y, X)


# ---------- helpers ----------
def blur(a, r):
    """Separable gaussian (PIL's GaussianBlur rejects float images)."""
    k = np.exp(-0.5 * (np.arange(-int(3 * r), int(3 * r) + 1) / r) ** 2)
    k /= k.sum()
    a = np.apply_along_axis(np.convolve, 1, a.astype(np.float32), k, "same")
    return np.apply_along_axis(np.convolve, 0, a, k, "same")


def noise(rng, cells, sx=1.0, sy=1.0):
    """Smooth value noise in [0,1]; `cells` grid points across the cell, stretched by sx/sy."""
    gx, gy = max(2, int(cells * sx)), max(2, int(cells * sy))
    g = rng.random((gy, gx)).astype(np.float32)
    return np.asarray(Image.fromarray(g, "F").resize((C, C), Image.BICUBIC)).clip(0, 1)


def fbm(rng, base=4, octaves=4, sx=1.0, sy=1.0):
    out, amp, tot = np.zeros((C, C), np.float32), 1.0, 0.0
    for o in range(octaves):
        out += noise(rng, base * 2 ** o, sx, sy) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def angular(rng, n=24, amp=1.0):
    """Periodic random function of angle, mean 0, ~[-amp, amp]."""
    k = np.arange(1, n + 1)
    ph = rng.random(n) * 2 * np.pi
    a = rng.normal(size=n) / k
    f = sum(a[i] * np.cos(k[i] * TH + ph[i]) for i in range(n))
    return f / np.abs(f).max() * amp


def canvas():
    img = Image.new("L", (C * SS, C * SS), 0)
    return img, ImageDraw.Draw(img)


def arr(img):
    return np.asarray(img.resize((C, C), Image.LANCZOS), np.float32) / 255


def P(x, y):
    """[-1,1] cell coords -> supersampled pixel coords."""
    return ((x + 1) / 2 * C * SS, (y + 1) / 2 * C * SS)


def jagged(rng, x, y, ang, length, steps, jitter):
    pts = [(x, y)]
    seg = length / steps
    for _ in range(steps):
        ang += rng.normal(0, jitter)
        x, y = x + np.cos(ang) * seg, y + np.sin(ang) * seg
        pts.append((x, y))
    return pts, ang


def line(draw, pts, w0, w1=None, val=255):
    w1 = w0 if w1 is None else w1
    n = len(pts) - 1
    for i in range(n):
        w = w0 + (w1 - w0) * i / max(1, n)
        draw.line([P(*pts[i]), P(*pts[i + 1])], fill=val, width=max(1, int(round(w * SS))))


def mix(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return np.asarray(a) * (1 - t) + np.asarray(b) * t


def col(c):
    return np.broadcast_to(np.array(c, np.float32), (C, C, 3)).copy()


# ---------- cells: each returns rgb (C,C,3), alpha (C,C), height (C,C) ----------
def bullet_wood(rng):
    rr = R * (1 + angular(rng, 16, 0.12))
    hole = 1 - ss(0.07, 0.10, rr)
    img, d = canvas()
    for _ in range(26):
        a = rng.random() * 2 * np.pi
        L = rng.uniform(0.12, 0.42)
        r0 = 0.07
        pts, _ = jagged(rng, np.cos(a) * r0, np.sin(a) * r0, a, L, 6, 0.12)
        line(d, pts, rng.uniform(3, 9), 1, int(rng.uniform(150, 255)))
    streak = arr(img)
    ring = 1 - ss(0.12, 0.24, rr)
    raw = np.maximum(ring, streak)
    g = fbm(rng, 6, 4, sx=0.3, sy=3)  # grain-ish
    rgb = mix(col((0.42, 0.29, 0.17)), col((0.70, 0.54, 0.34)), g * 0.6 + 0.4 * raw)
    rgb = mix(rgb, col((0.20, 0.12, 0.07)), ss(0.10, 0.14, rr) * (1 - ss(0.14, 0.2, rr)) * 0.7)  # scorched lip
    rgb = mix(rgb, col((0.02, 0.015, 0.01)), hole)
    alpha = np.clip(np.maximum(hole, raw * 0.95) + 0.25 * (1 - ss(0.2, 0.5, rr)) * fbm(rng, 8), 0, 1)
    h = -hole * 1.0 + 0.35 * ss(0.06, 0.12, rr) * (1 - ss(0.12, 0.22, rr)) + streak * 0.25
    return rgb, alpha, h


def bullet_plaster(rng):
    rr = R * (1 + angular(rng, 20, 0.22))
    crumble = fbm(rng, 10, 4)
    crater = 1 - ss(0.30, 0.42, rr + (crumble - 0.5) * 0.15)
    hole = 1 - ss(0.07, 0.10, R * (1 + angular(rng, 10, 0.1)))
    rgb = mix(col((0.62, 0.59, 0.53)), col((0.85, 0.82, 0.76)), crumble)
    rgb = mix(rgb, col((0.35, 0.32, 0.28)), (crumble > 0.62) * 0.6)          # grit pits
    rgb = mix(rgb, col((0.10, 0.09, 0.08)), (1 - ss(0.08, 0.2, R)) * 0.8)    # dark inner cone
    rgb = mix(rgb, col((0.015, 0.012, 0.01)), hole)
    dust = (1 - ss(0.35, 0.7, rr)) * 0.35 * fbm(rng, 12)
    rgb = np.where((crater < 0.5)[..., None], col((0.55, 0.53, 0.49)), rgb)
    alpha = np.clip(np.maximum(crater, dust), 0, 1)
    h = -crater * 0.45 * (1 - rr / 0.42).clip(0, 1) - hole * 0.8 + (crumble - 0.5) * 0.25 * crater
    return rgb, alpha, h


def bullet_stone(rng):
    rr = R * (1 + angular(rng, 12, 0.3))
    chip = 1 - ss(0.26, 0.32, rr)
    n = fbm(rng, 12, 4)
    rgb = mix(col((0.40, 0.40, 0.39)), col((0.66, 0.65, 0.62)), n)
    rgb = mix(rgb, col((0.12, 0.12, 0.12)), 1 - ss(0.03, 0.11, rr))
    rgb = mix(rgb, col((0.22, 0.22, 0.21)), ss(0.22, 0.29, rr) * chip)          # dark edge lip
    dust = (1 - ss(0.3, 0.5, rr)) * 0.3 * n
    alpha = np.maximum(chip, dust)
    h = -chip * (1 - (rr / 0.32) ** 2).clip(0, 1) * 0.6 + (n - 0.5) * 0.3 * chip
    return rgb, alpha, h


def bullet_metal(rng):
    rr = R * (1 + angular(rng, 8, 0.05))
    dent = 1 - ss(0.22, 0.27, rr)
    n = fbm(rng, 16, 3)
    shade = 0.5 + 0.5 * np.cos(TH + 2.3) * ss(0.05, 0.22, rr)  # fake lit side of bowl
    rgb = mix(col((0.28, 0.29, 0.31)), col((0.62, 0.63, 0.66)), shade * 0.8 + n * 0.2)
    rgb = mix(rgb, col((0.18, 0.18, 0.19)), ss(0.2, 0.24, rr) * (1 - ss(0.24, 0.28, rr)))  # edge ring
    rgb = mix(rgb, col((0.04, 0.04, 0.045)), 1 - ss(0.04, 0.07, rr))
    alpha = dent
    h = -(1 - (rr / 0.25) ** 2).clip(0, 1) * 0.7 + 0.35 * np.exp(-((rr - 0.26) / 0.03) ** 2)
    return rgb, alpha, h


def gash(rng, half_w, length):
    """Vertical groove shape: returns (inside 0..1, signed u across [-1..1], edge band 0..1)."""
    t = np.clip(1 - (Y / length) ** 2, 0, 1)
    w = half_w * np.sqrt(t) * (1 + (fbm(rng, 3, 3, sx=0.5, sy=6) - 0.5) * 0.6)
    w = np.maximum(w, 1e-4)
    u = X / w
    jag = (fbm(rng, 8, 4, sx=1, sy=12) - 0.5) * 0.5
    inside = 1 - ss(0.85, 1.0, np.abs(u) + jag)
    edge = (1 - ss(1.0, 2.2, np.abs(u) + jag * 2)) * (1 - inside) * (t > 0.02)
    return inside, u, edge


def gash_wood(rng):
    inside, u, edge = gash(rng, 0.085, 0.86)
    splint = fbm(rng, 6, 4, sx=0.6, sy=10)
    rgb = mix(col((0.50, 0.36, 0.22)), col((0.78, 0.62, 0.40)), splint)          # raw wood edges
    deep = (1 - np.abs(u).clip(0, 1)) ** 0.7
    rgb = mix(rgb, mix(col((0.22, 0.14, 0.08)), col((0.03, 0.02, 0.015)), deep), inside)
    img, d = canvas()
    for _ in range(18):  # splinters peeling off the edges, along the swing axis
        y0 = rng.uniform(-0.7, 0.7)
        side = rng.choice([-1, 1])
        x0 = side * 0.085 * np.sqrt(max(0, 1 - (y0 / 0.86) ** 2))
        pts, _ = jagged(rng, x0, y0, np.pi / 2 * rng.choice([-1, 1]) + side * rng.uniform(0.1, 0.35), rng.uniform(0.06, 0.2), 4, 0.1)
        line(d, pts, rng.uniform(3, 7), 1)
    sp = arr(img)
    rgb = mix(rgb, col((0.80, 0.66, 0.45)), sp * (1 - inside))
    alpha = np.clip(np.maximum.reduce([inside, edge * (0.6 + 0.4 * splint), sp]), 0, 1)
    h = -inside * (1 - u ** 2).clip(0, 1) ** 0.5 + 0.25 * edge + 0.3 * sp
    return rgb, alpha, h


def gash_plaster(rng):
    inside, u, edge = gash(rng, 0.13, 0.84)
    n = fbm(rng, 12, 4)
    rgb = mix(col((0.70, 0.67, 0.61)), col((0.90, 0.88, 0.83)), n)              # exposed plaster
    core = 1 - ss(0.0, 0.35, np.abs(u))
    rgb = mix(rgb, col((0.32, 0.30, 0.27)), core * inside * 0.85)              # deepest line
    rgb = mix(rgb, col((0.45, 0.43, 0.39)), (n > 0.62) * 0.5)
    crumbs = (fbm(rng, 24, 2) > 0.62) * (1 - ss(1.0, 2.8, np.abs(u))) * (np.abs(Y) < 0.8)
    alpha = np.clip(np.maximum.reduce([inside, edge * 0.55 * n * 2, crumbs * 0.8]), 0, 1)
    h = -inside * (1 - u ** 2).clip(0, 1) ** 0.6 * 0.9 + (n - 0.5) * 0.3 * inside
    return rgb, alpha, h


def dent(rng):
    rr = R * (1 + angular(rng, 6, 0.06))
    n = fbm(rng, 8, 3)
    rgb = mix(col((0.16, 0.15, 0.14)), col((0.34, 0.33, 0.31)), n * 0.3 + 0.7 * (0.5 + 0.5 * np.cos(TH + 2.3) * ss(0.05, 0.3, rr)))
    ring = np.exp(-((rr - 0.3) / 0.05) ** 2)
    rgb = mix(rgb, col((0.08, 0.075, 0.07)), ring * 0.8)
    alpha = (1 - ss(0.25, 0.36, rr)) * 0.4 + ring * 0.45
    h = -(1 - (rr / 0.34) ** 2).clip(0, 1) ** 1.5 * 0.6
    return rgb, np.clip(alpha, 0, 1), h


def scrape(rng):
    img, d = canvas()
    dimg, dd = canvas()
    for _ in range(16):
        x = rng.uniform(-0.14, 0.14)
        L = rng.uniform(0.6, 1.6)
        y0 = rng.uniform(-0.82, 0.82 - L * 0.7)
        y1 = min(0.84, y0 + L)
        drift = rng.normal(0, 0.02)
        pts = [(x + rng.normal(0, 0.0012) + (yy - y0) * drift, yy) for yy in np.linspace(y0, y1, 12)]
        w = rng.uniform(2, 6)
        line(d, pts, w, w * 0.4, int(rng.uniform(140, 255)))
        line(dd, [(p[0] + 0.006, p[1]) for p in pts], w * 1.6, w * 0.6, 200)
    lit, shadow = arr(img), blur(arr(dimg), 1)
    taper = ss(0.88, 0.7, np.abs(Y))
    rgb = mix(col((0.10, 0.09, 0.08)), col((0.78, 0.76, 0.72)), lit / max(lit.max(), 1e-3))
    alpha = np.clip(np.maximum(lit * 1.2, shadow * 0.6), 0, 1) * taper
    h = -(lit + shadow * 0.5) * 0.6
    return rgb, alpha, h


def branchy(rng, d, n_main, length, steps, jitter, w, sub_prob):
    for i in range(n_main):
        a = i / n_main * 2 * np.pi + rng.normal(0, 0.25)
        L = length * rng.uniform(0.55, 1.0)
        pts, _ = jagged(rng, 0, 0, a, L, steps, jitter)
        line(d, pts, w, max(1, w * 0.3))
        for j in range(2, len(pts) - 1):
            if rng.random() < sub_prob:
                ang = np.arctan2(pts[j + 1][1] - pts[j][1], pts[j + 1][0] - pts[j][0]) + rng.choice([-1, 1]) * rng.uniform(0.4, 1.0)
                sp, _ = jagged(rng, *pts[j], ang, L * rng.uniform(0.15, 0.35), 4, jitter)
                line(d, sp, w * 0.5, 1)


def crack(rng):
    img, d = canvas()
    branchy(rng, d, 7, 0.82, 16, 0.14, 5, 0.22)
    lines = arr(img)
    crush = (1 - ss(0.04, 0.12, R * (1 + angular(rng, 10, 0.3))))
    edge = blur(lines, 2.5) * 1.5
    rgb = mix(col((0.30, 0.28, 0.25)), col((0.02, 0.02, 0.02)), np.clip(lines + crush, 0, 1))
    alpha = np.clip(np.maximum.reduce([lines, crush, edge * 0.35]), 0, 1)
    h = -np.clip(lines + crush * 0.6, 0, 1) * 0.8
    return rgb, alpha, h


def glass_crack(rng):
    img, d = canvas()
    n = 13
    angs = np.sort(np.arange(n) / n * 2 * np.pi + rng.normal(0, 0.15, n))
    for a in angs:
        pts, _ = jagged(rng, 0, 0, a, rng.uniform(0.6, 0.86), 10, 0.08)
        line(d, pts, 3.5, 1.2)
    for rad in (0.16, 0.33, 0.52):
        for i in range(n):  # concentric segments between neighbouring radials, with gaps
            if rng.random() < 0.25 and rad > 0.2:
                continue
            a0, a1 = angs[i], angs[(i + 1) % n] + (2 * np.pi if i == n - 1 else 0)
            r0, r1 = rad * rng.uniform(0.85, 1.15), rad * rng.uniform(0.85, 1.15)
            mid = (a0 + a1) / 2
            rm = (r0 + r1) / 2 * rng.uniform(0.82, 0.95)
            line(d, [(np.cos(a0) * r0, np.sin(a0) * r0), (np.cos(mid) * rm, np.sin(mid) * rm), (np.cos(a1) * r1, np.sin(a1) * r1)], 2.2)
    lines = arr(img)
    core = 1 - ss(0.02, 0.07, R)
    rgb = mix(col((0.72, 0.76, 0.78)), col((0.95, 0.97, 1.0)), np.clip(lines + core, 0, 1))
    alpha = np.clip(np.maximum(lines * 0.95, core) + blur(lines, 3) * 0.15, 0, 1)
    h = -np.clip(lines + core, 0, 1) * 0.5
    return rgb, alpha, h


def fabric_tear(rng):
    # ragged slit, slightly diagonal
    xr, yr = X * np.cos(0.5) + Y * np.sin(0.5), -X * np.sin(0.5) + Y * np.cos(0.5)
    e = np.hypot(xr / 0.32, yr / 0.11) * (1 + angular(rng, 30, 0.22))
    hole = 1 - ss(0.85, 1.0, e)
    img, d = canvas()
    for _ in range(110):  # loose fibres around the rim
        a = rng.random() * 2 * np.pi
        bx, by = np.cos(a) * 0.32 * 0.95, np.sin(a) * 0.11 * 0.95
        x0, y0 = bx * np.cos(0.5) - by * np.sin(0.5), bx * np.sin(0.5) + by * np.cos(0.5)
        out = np.arctan2(y0, x0) + rng.normal(0, 0.5)
        pts, _ = jagged(rng, x0, y0, out, rng.uniform(0.03, 0.12), 4, 0.35)
        line(d, pts, rng.uniform(1.2, 2.5), 1, int(rng.uniform(120, 255)))
    fib = arr(img)
    frayed = (1 - ss(1.0, 1.4, e)) * (1 - hole)
    n = fbm(rng, 20, 3)
    rgb = mix(col((0.16, 0.14, 0.12)), col((0.40, 0.36, 0.30)), np.clip(fib + frayed * n * 0.5, 0, 1))
    rgb = mix(rgb, col((0.01, 0.01, 0.01)), hole)
    alpha = np.clip(np.maximum.reduce([hole, fib, frayed * 0.7]), 0, 1)
    h = -hole * 0.9 + fib * 0.3 + frayed * 0.2
    return rgb, alpha, h


def paper_hole(rng):
    rr = R * (1 + angular(rng, 24, 0.18))
    hole = 1 - ss(0.15, 0.17, rr)
    torn = (1 - ss(0.17, 0.32, R * (1 + angular(rng, 40, 0.35)))) * (1 - hole)
    n = fbm(rng, 16, 3)
    rgb = mix(col((0.70, 0.66, 0.56)), col((0.92, 0.89, 0.80)), n)
    rgb = mix(rgb, col((0.30, 0.26, 0.20)), (1 - ss(0.17, 0.22, rr)) * (1 - hole) * 0.6)  # curled shadow
    rgb = mix(rgb, col((0.01, 0.01, 0.01)), hole)
    alpha = np.clip(np.maximum(hole, torn), 0, 1)
    h = -hole + torn * 0.5
    return rgb, alpha, h


def blood(rng, blobs, drops, spray_dir=None, stretch=1.0, origin=(0, 0)):
    m = np.zeros((C, C), np.float32)
    n = fbm(rng, 6, 4)
    for (bx, by, br) in blobs:
        rr = np.hypot(X - bx, Y - by) / br * (1 + (n - 0.5) * 0.8)
        m = np.maximum(m, 1 - ss(0.8, 1.0, rr))
    img, d = canvas()
    for _ in range(drops):
        if spray_dir is None:
            a = rng.random() * 2 * np.pi
        else:
            a = spray_dir + rng.normal(0, 0.6)
        dist = rng.uniform(0.25, 0.8) if spray_dir is None else rng.uniform(0.15, 1.2)
        x, y = origin[0] + np.cos(a) * dist, origin[1] + np.sin(a) * dist
        if max(abs(x), abs(y)) > 0.84:
            continue
        r = rng.uniform(0.006, 0.035) * (1.2 - dist * 0.6)
        el = 1 + rng.uniform(0, 2.5) * stretch * dist
        cx, cy = P(x, y)
        rx, ry = r * C * SS / 2 * el, r * C * SS / 2
        # elongated drop pointing outward: draw a short line of shrinking dots
        for k in range(6):
            f = k / 5
            ox, oy = np.cos(a) * rx * f * 1.2, np.sin(a) * rx * f * 1.2
            rad = ry * (1 - 0.7 * f)
            d.ellipse([cx + ox - rad, cy + oy - rad, cx + ox + rad, cy + oy + rad], fill=255)
    m = np.maximum(m, arr(img))
    thick = blur(m, 6) * m
    rgb = mix(col((0.30, 0.03, 0.02)), col((0.10, 0.008, 0.008)), np.clip(thick * 1.3, 0, 1))
    rgb = mix(rgb, col((0.20, 0.02, 0.015)), n * 0.3)
    alpha = np.clip(m * (0.85 + 0.15 * thick), 0, 1)
    h = thick * 0.25
    return rgb, alpha, h


def blood1(rng):
    return blood(rng, [(0, 0, 0.28), (0.12, -0.1, 0.18), (-0.15, 0.12, 0.14)], 70)


def blood2(rng):
    return blood(rng, [(-0.4, 0.4, 0.2), (-0.25, 0.25, 0.13), (-0.1, 0.1, 0.07)], 140, spray_dir=-np.pi / 4, stretch=1.6, origin=(-0.4, 0.4))


def blood_pool(rng):
    rr = R * (1 + angular(rng, 18, 0.18)) * (1 + (fbm(rng, 5, 3) - 0.5) * 0.25)
    m = 1 - ss(0.62, 0.66, rr)
    n = fbm(rng, 8, 3)
    rgb = mix(col((0.26, 0.025, 0.02)), col((0.07, 0.006, 0.006)), (1 - ss(0.2, 0.6, rr)) * 0.8 + n * 0.2)
    rgb = mix(rgb, col((0.18, 0.02, 0.015)), np.exp(-((rr - 0.62) / 0.03) ** 2) * 0.6)  # drying rim
    h = m * 0.15 + 0.15 * np.exp(-((rr - 0.63) / 0.02) ** 2)
    return rgb, m, h


def scorch(rng):
    rr = R * (1 + angular(rng, 20, 0.2))
    n = fbm(rng, 8, 4)
    soot = (1 - ss(0.2, 0.7, rr + (n - 0.5) * 0.3))
    ring = np.exp(-((rr - 0.22) / 0.12) ** 2)
    rgb = mix(col((0.10, 0.09, 0.08)), col((0.015, 0.012, 0.01)), np.clip(ring + n * 0.4, 0, 1))
    alpha = np.clip(soot * (0.6 + 0.4 * n) * (0.65 + 0.35 * ring), 0, 1)
    h = (n - 0.5) * 0.1 * soot
    return rgb, alpha, h


CELLS = [bullet_wood, bullet_plaster, bullet_stone, bullet_metal,
         gash_wood, gash_plaster, dent, scrape,
         crack, glass_crack, fabric_tear, paper_hole,
         blood1, blood2, blood_pool, scorch]

NORMAL_STRENGTH = 24.0  # height units per cell -> slope; raise for deeper-looking holes


def main():
    edge_px = np.minimum.reduce([np.arange(C)[None, :].repeat(C, 0), np.arange(C)[:, None].repeat(C, 1),
                                 C - 1 - np.arange(C)[None, :].repeat(C, 0), C - 1 - np.arange(C)[:, None].repeat(C, 1)])
    border = ss(MARGIN, MARGIN + 16, edge_px)
    albedo = np.zeros((C * 4, C * 4, 4), np.float32)
    normal = np.zeros((C * 4, C * 4, 3), np.float32)
    for i, fn in enumerate(CELLS):
        rng = np.random.default_rng(1000 + i)
        rgb, a, h = fn(rng)
        a = np.clip(a, 0, 1) * border
        h = blur(h * border, 1.5)
        gy, gx = np.gradient(h)            # gy: d/drow (down), gx: d/dcol (right)
        n = np.dstack([-gx * NORMAL_STRENGTH, gy * NORMAL_STRENGTH, np.ones_like(h)])  # OpenGL / Unity: +Y = up
        n /= np.linalg.norm(n, axis=2, keepdims=True)
        r, c = divmod(i, 4)
        sl = (slice(r * C, (r + 1) * C), slice(c * C, (c + 1) * C))
        albedo[sl] = np.dstack([np.clip(rgb, 0, 1), a])
        normal[sl] = n * 0.5 + 0.5
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((albedo * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, "ImpactAtlas.png"))
    Image.fromarray((normal * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, "ImpactAtlas_Normal.png"))
    # self-check: margins transparent, flat normal there
    assert albedo[:, :, 3].reshape(4, C, 4, C)[:, :MARGIN].max() == 0
    flat = (normal[:MARGIN, :MARGIN] * 255 + 0.5).astype(int)
    assert (flat == [128, 128, 255]).all(), flat[0, 0]
    print("wrote", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
