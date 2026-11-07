"""Tileable PBR texture sets for the shared mansion materials (numpy spectral synthesis => seamless).

Each family writes <Family>_Albedo.png (sRGB, neutral-ish, tinted per material in Unity/Blender),
<Family>_Normal.png (OpenGL / Unity, green up) and <Family>_Mask.png (URP: R metallic, G AO, A smoothness).
Arrays are [v, u] with v=0 at the bottom row, matching Blender's pixel order.
"""
import os

import bpy
import numpy as np

N = 1024
FAMILIES = ("Wood", "Metal", "Fabric", "Stone", "Leather", "Smooth")


def _freq(n=N):
    f = np.fft.fftfreq(n) * n
    return np.meshgrid(f, f)  # fu, fv (cycles per tile)


def noise(seed, beta=1.0, su=1.0, sv=1.0, lo=1.0, hi=None):
    """Periodic fractal noise in [-1,1]. su/sv squash frequencies (sv<1 => streaks along v)."""
    r = np.random.default_rng(seed)
    fu, fv = _freq()
    k = np.sqrt((fu * su) ** 2 + (fv * sv) ** 2)
    amp = np.where(k < lo, 0.0, 1.0 / np.maximum(k, 1e-6) ** beta)
    if hi:
        amp *= np.exp(-(k / hi) ** 2)
    spec = np.fft.fft2(r.standard_normal((N, N))) * amp
    out = np.real(np.fft.ifft2(spec))
    return out / (np.abs(out).max() + 1e-9)


def blur(a, sigma):
    fu, fv = _freq()
    g = np.exp(-(fu ** 2 + fv ** 2) * (2 * np.pi ** 2) * (sigma / N) ** 2)
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def normal_from(h, strength):
    du = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dv = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-du, -dv, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def ao_from(h, k, sigma=6):
    return np.clip(1 - np.maximum(blur(h, sigma) - h, 0) * k, 0, 1)


def u_v():
    g = np.arange(N) / N
    return np.meshgrid(g, g)  # u, v


def wood():
    u, v = u_v()
    warp = noise(1, 1.2, 1, 6, lo=1, hi=60)
    rings = 0.5 + 0.5 * np.sin(2 * np.pi * (u * 18 + warp * 0.35))
    fibre = noise(2, 0.6, 1, 25, lo=2)            # long streaks along v
    pores = noise(3, 0.2, 1, 12, lo=40) * 0.5
    h = rings * 0.5 + fibre * 0.35 + pores * 0.15
    grime = blur(noise(4, 1.5, lo=1), 4)
    alb = 0.78 - 0.16 * rings - 0.12 * fibre + 0.08 * grime
    alb = np.stack([alb] * 3, -1)
    smooth = np.clip(0.55 - 0.15 * rings + 0.15 * grime, 0.2, 0.75)
    return alb, normal_from(h, 3.5), np.zeros_like(h), ao_from(h, 0.8), smooth


def metal():
    pits = noise(11, 0.3, lo=60)
    pit = np.clip((pits - 0.55) * 4, 0, 1)
    tarn = blur(noise(12, 1.6, lo=1), 3)
    scr = np.clip(np.abs(noise(13, 0.9, 1, 30, lo=4)) * -20 + 1, 0, 1) * 0.5   # fine streak scratches
    h = -pit * 0.8 + noise(14, 1.0, lo=4) * 0.25 - scr * 0.2
    t = np.clip(tarn * 1.0 + 0.1, 0, 1)
    alb = 0.88 - 0.35 * t - 0.25 * pit
    alb = np.stack([alb] * 3, -1)
    metallic = np.clip(1 - 0.45 * t - 0.5 * pit, 0, 1)
    smooth = np.clip(0.8 - 0.4 * t - 0.4 * pit + 0.15 * scr, 0.1, 0.9)
    return alb, normal_from(h, 2.5), metallic, ao_from(h, 1.0, 3), smooth


def fabric():
    u, v = u_v()
    T = 220
    cu, cv = (u * T) % 1, (v * T) % 1
    over = ((np.floor(u * T) + np.floor(v * T)) % 2).astype(bool)
    hw = np.sin(np.pi * cv) ** 0.6    # warp thread profile
    hf = np.sin(np.pi * cu) ** 0.6
    h = np.where(over, hw * 0.8 + hf * 0.2, hf * 0.8 + hw * 0.2)
    crease = blur(noise(21, 1.3, lo=2), 2)
    fuzz = noise(22, 0.0, lo=200) * 0.15
    h = h * 0.6 + crease * 1.5 + fuzz
    stain = np.clip(blur(noise(23, 1.8, lo=1), 6) * 1.6, 0, 1)
    alb = 0.8 - 0.12 * (1 - h * 0.5) - 0.25 * stain
    alb = np.stack([alb] * 3, -1)
    smooth = np.clip(0.18 + 0.05 * stain, 0, 1)
    return alb, normal_from(h, 3.0), np.zeros_like(h), ao_from(h, 0.6, 2), smooth


def stone():
    base = noise(31, 1.6, lo=1)
    grain = noise(32, 0.3, lo=60)
    chips = np.clip((noise(33, 1.1, lo=8) - 0.3) * 4, 0, 1)
    h = base * 0.6 + grain * 0.2 - chips * 0.6
    speck = np.clip(noise(34, 0.0, lo=300) * 2, -1, 1)
    alb = 0.7 + 0.14 * base + 0.08 * speck - 0.18 * chips - 0.1 * np.clip(blur(noise(35, 1.8), 5), 0, 1)
    alb = np.stack([alb * 1.0, alb * 0.98, alb * 0.94], -1)
    smooth = np.clip(0.22 - 0.1 * chips + 0.05 * grain, 0.05, 0.4)
    return alb, normal_from(h, 7.0), np.zeros_like(h), ao_from(h, 1.2), smooth


def leather():
    c = noise(41, 0.9, lo=80, hi=500)
    crease = np.clip(1 - np.abs(c) * 8, 0, 1)
    big = blur(noise(42, 1.4, lo=1), 3)
    h = -crease * 0.6 + noise(43, 0.2, lo=100) * 0.15 + big * 0.5
    alb = 0.78 - 0.2 * crease + 0.08 * big
    alb = np.stack([alb] * 3, -1)
    smooth = np.clip(0.5 - 0.3 * crease + 0.1 * big, 0.1, 0.7)
    return alb, normal_from(h, 2.0), np.zeros_like(h), ao_from(h, 0.8, 3), smooth


def smooth_():
    h = noise(51, 1.8, lo=1) * 0.6 + noise(52, 0.4, lo=80) * 0.08
    craze = np.clip(1 - np.abs(noise(53, 1.0, lo=6, hi=120)) * 30, 0, 1) * 0.3   # fine crazing / ripples
    h -= craze
    alb = 0.92 - 0.06 * np.clip(blur(noise(54, 1.8, lo=1), 4), 0, 1) - 0.1 * craze
    alb = np.stack([alb] * 3, -1)
    smooth = np.clip(0.9 - craze, 0, 1)
    return alb, normal_from(h, 2.0), np.zeros_like(h), ao_from(h, 0.5), smooth


GEN = {"Wood": wood, "Metal": metal, "Fabric": fabric, "Stone": stone, "Leather": leather, "Smooth": smooth_}


def save_png(path, rgba, srgb):
    img = bpy.data.images.new(os.path.basename(path), N if rgba.shape[1] == N else rgba.shape[1], rgba.shape[0],
                              alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def build(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for fam in FAMILIES:
        alb, nrm, metal_, ao, sm = GEN[fam]()
        one = np.ones(alb.shape[:2])
        save_png(os.path.join(out_dir, fam + "_Albedo.png"), np.dstack([np.clip(alb, 0, 1), one]), True)
        save_png(os.path.join(out_dir, fam + "_Normal.png"), np.dstack([nrm, one]), False)
        save_png(os.path.join(out_dir, fam + "_Mask.png"), np.dstack([metal_, ao, np.zeros_like(ao), sm]), False)
        print("TEX", fam)
