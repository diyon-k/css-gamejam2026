# Repaints the ZombieGirl atlas per variant: skin, hair, shirt and pants recoloured separately,
# plus procedural detail (bruises, burn cracks, veins, blood). Blood is kept red.
# Needs uv_labels.json from tools/blender_zombie_variants.py.
# Run: python tools/zombie_textures.py <dir containing uv_labels.json>
import json, sys
import numpy as np
from PIL import Image, ImageDraw

ART = "C:/Users/jackp/github/css-gamejam2026/Unity/Assets/Art/Zombies"
N = 2048
LABELS = {"skin": 1, "head": 2, "shirt": 3, "pants": 4}

def label_map(path):
    im = Image.new("L", (N, N), 0); d = ImageDraw.Draw(im)
    for lab, uvs in json.load(open(path)):
        d.polygon([(u * N, (1 - v) * N) for u, v in uvs], fill=LABELS[lab])
    return np.asarray(im)

def noise(seed, cells):
    g = np.random.default_rng(seed).random((cells, cells)).astype(np.float32)
    return np.asarray(Image.fromarray(g, "F").resize((N, N), Image.BICUBIC)).clip(0, 1)

def ridge(seed, cells, sharp):  # thin line network: cracks / veins
    return (1 - np.abs(noise(seed, cells) - 0.5) * 2) ** sharp

def recolor(rgb, tint, bright=1.0):
    lum = rgb @ np.array([0.3, 0.59, 0.11], np.float32)
    return (lum[..., None] * np.array(tint, np.float32) * 1.6 * bright).clip(0, 1)

def mix(a, b, m):
    return a * (1 - m[..., None]) + b * m[..., None]

BLOOD = np.array([0.35, 0.03, 0.03], np.float32)

def bloater(rgb, lab):
    skin = recolor(rgb, (0.55, 0.72, 0.32))
    skin = mix(skin, skin * np.array([0.75, 0.45, 0.8]), noise(1, 24) ** 3 * 1.6)       # purple bruising
    skin = mix(skin, np.array([0.75, 0.8, 0.25]), (noise(2, 140) - 0.8).clip(0, 1) * 1.75)          # pus spots
    return {1: skin, 2: mix(skin, recolor(rgb, (0.35, 0.25, 0.15)), hair_mask(rgb)),
            3: recolor(rgb, (0.85, 0.72, 0.35)), 4: recolor(rgb, (0.3, 0.38, 0.6))}

def husk(rgb, lab):
    char = recolor(rgb, (0.26, 0.22, 0.2), 0.8)
    char = mix(char, np.array([0.55, 0.52, 0.5]), noise(3, 30) ** 4 * 1.5)               # ash patches
    char = mix(char, np.array([1.0, 0.38, 0.08]), (ridge(4, 40, 30) > 0.5) * 0.9)         # ember cracks
    burnt = mix(recolor(rgb, (0.32, 0.3, 0.27)), np.zeros(3), noise(5, 50) ** 2)
    return {1: char, 2: mix(char, recolor(rgb, (0.15, 0.15, 0.15)), hair_mask(rgb)), 3: burnt, 4: burnt}

def brute(rgb, lab):
    skin = recolor(rgb, (0.55, 0.45, 0.62))
    skin = mix(skin, skin * 0.55, noise(6, 20) ** 2)                                       # rot shading
    blood = (noise(7, 120) - 0.7).clip(0, 1) * 2.4
    return {1: mix(skin, BLOOD, blood), 2: mix(skin, recolor(rgb, (0.55, 0.18, 0.08)), hair_mask(rgb)),
            3: mix(recolor(rgb, (0.55, 0.12, 0.1)), BLOOD, blood), 4: recolor(rgb, (0.22, 0.22, 0.24))}

def stalker(rgb, lab):
    skin = recolor(rgb, (0.82, 0.88, 1.0), 1.35)
    skin = mix(skin, np.array([0.25, 0.15, 0.4]), (ridge(8, 70, 40) > 0.45) * 0.8)        # dark veins
    return {1: skin, 2: mix(skin, recolor(rgb, (0.95, 0.95, 0.95), 1.4), hair_mask(rgb)),
            3: recolor(rgb, (0.75, 0.75, 0.72), 1.2), 4: recolor(rgb, (0.3, 0.3, 0.32))}

def hair_mask(rgb):  # blonde pixels: bright, warm, low blue
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return ((r > 0.45) & (r >= g) & (g > b * 1.15) & (r - b > 0.12)).astype(np.float32)

PAINT = {"ZombieBloater": bloater, "ZombieHusk": husk, "ZombieBrute": brute, "ZombieStalker": stalker}

if __name__ == "__main__":
    lab = label_map(sys.argv[1] + "/uv_labels.json")
    for src, suffix in (("zombie_diffuse", "_diffuse"), ("zombie_body_diffuse", "_body_diffuse")):
        im = np.asarray(Image.open(f"{ART}/ZombieGirl/Textures/{src}.png").convert("RGBA"), np.float32) / 255
        rgb, a = im[..., :3], im[..., 3:]
        blood = ((rgb[..., 0] - rgb[..., 1:].max(-1)) * 4 - 0.6).clip(0, 1)               # keep existing gore
        for name, fn in PAINT.items():
            out = rgb.copy()
            for k, layer in fn(rgb, lab).items():
                out = np.where((lab == k)[..., None], layer, out)
            out = mix(out, rgb * np.array([1.1, 0.6, 0.6]), blood * (lab > 0))
            import os; os.makedirs(f"{ART}/{name}/Textures", exist_ok=True)
            Image.fromarray((np.concatenate([out.clip(0, 1), a], -1) * 255).astype(np.uint8)).save(
                f"{ART}/{name}/Textures/{name}{suffix}.png")
            print("wrote", name, suffix)
