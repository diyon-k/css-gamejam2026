"""BLINK Bungalow floor plan, drawn from bungalow_layout (ground floor, basement, stair section, legend).

  python tools/blender/bungalow_floorplan.py [docs/bungalow/floorplan.png]

Plain Python + Pillow, no bpy. Top-down, metres, origin = outer SW corner at ground-floor level, X east (right),
Z north (up). Everything comes from the layout tables, so new rooms, walls, openings, fixtures and markers show up
here without code changes. FUTURE entries (reserved expansion) are drawn dashed grey.
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bungalow_layout as L  # noqa: E402

S = 56            # px per metre
M = 120           # panel margin
FX = L.FOOTPRINT[0]
# rev 3: the north wing runs past L.FOOTPRINT's z 15, so the plan's depth comes from the outermost x-axis wall
FZ = max(w["t"][1] for w in L.WALLS.values() if w["axis"] == "x")
EXTRA_N = 0.8     # margin above the building
EXTRA_E = 6.5     # and east of it

try:
    F = ImageFont.truetype("arial.ttf", 15)
    FB = ImageFont.truetype("arialbd.ttf", 17)
    FS = ImageFont.truetype("arial.ttf", 12)
    FT = ImageFont.truetype("arialbd.ttf", 26)
except OSError:
    F = FB = FS = FT = ImageFont.load_default()

PANEL_W = int((FX + EXTRA_E) * S + 2 * M)
PANEL_H = int((FZ + EXTRA_N) * S + 2 * M)
IMG = D = None   # created by draw()
COL = dict(wall=(48, 46, 44), grid=(226, 226, 220), grid5=(238, 238, 234), dim=(70, 70, 160), room=(30, 30, 30),
           door=(120, 70, 30), win=(40, 120, 200), stair=(110, 80, 50), bal=(90, 50, 20), water=(170, 205, 235),
           zomb=(200, 30, 30), player=(20, 150, 60), cam=(120, 0, 160), opening=(150, 150, 150),
           future=(150, 150, 150), sight=(230, 160, 0))
LCOL = {"fan": (255, 170, 0), "chand": (230, 120, 0), "sconce": (210, 150, 40), "lamp": (240, 90, 20),
        "dome": (200, 180, 60), "bulb": (150, 140, 40), "area": (90, 160, 200)}
TINT = {"EntranceHall": (236, 229, 210), "CentralHall": (236, 229, 210), "Living": (238, 214, 210),
        "Kitchen": (226, 232, 238), "Bedroom": (220, 232, 214), "Bathroom": (222, 234, 232),
        "Corridor": (236, 229, 210), "MasterBedroom": (220, 232, 214), "Parlour": (238, 214, 210)}


class P:
    def __init__(self, ox, oy):
        self.ox, self.oy = ox, oy

    def __call__(self, x, z):
        return (self.ox + M + x * S, self.oy + M + (FZ + EXTRA_N - z) * S)


def rect(p, x0, z0, x1, z1, **kw):
    a, b = p(x0, z1), p(x1, z0)
    D.rectangle([a, b], **kw)


def text_c(xy, s, font=F, fill=COL["room"]):
    l, t, r, b = D.textbbox((0, 0), s, font=font)
    D.text((xy[0] - (r - l) / 2, xy[1] - (b - t) / 2), s, font=font, fill=fill)


def dashed(p, pts, col, width=2, dash=9):
    for (a, b) in zip(pts, pts[1:]):
        pa, pb = p(*a), p(*b)
        n = max(1, int(math.dist(pa, pb) / dash))
        for i in range(0, n, 2):
            t0, t1 = i / n, min(1, (i + 1) / n)
            D.line([(pa[0] + (pb[0] - pa[0]) * t0, pa[1] + (pb[1] - pa[1]) * t0),
                    (pa[0] + (pb[0] - pa[0]) * t1, pa[1] + (pb[1] - pa[1]) * t1)], fill=col, width=width)


def drect(p, x0, z0, x1, z1, col, width=1, dash=6):
    dashed(p, [(x0, z0), (x1, z0), (x1, z1), (x0, z1), (x0, z0)], col, width, dash)


def arrow(p, a, b, col, width=2, head=10):
    pa, pb = p(*a), p(*b)
    D.line([pa, pb], fill=col, width=width)
    ang = math.atan2(pb[1] - pa[1], pb[0] - pa[0])
    for s in (-1, 1):
        D.line([pb, (pb[0] - head * math.cos(ang + s * 0.45), pb[1] - head * math.sin(ang + s * 0.45))],
               fill=col, width=width)


def grid(p):
    for i in range(int((FX + EXTRA_E) * 2) + 1):
        x = i * 0.5
        D.line([p(x, 0), p(x, FZ + EXTRA_N)], fill=COL["grid"] if i % 2 == 0 else COL["grid5"], width=1)
    for j in range(int((FZ + EXTRA_N) * 2) + 1):
        z = j * 0.5
        D.line([p(0, z), p(FX + EXTRA_E, z)], fill=COL["grid"] if j % 2 == 0 else COL["grid5"], width=1)
    for i in range(0, int(FX + EXTRA_E) + 1):
        text_c((p(i, 0)[0], p(i, 0)[1] + 14), str(i), FS, (140, 140, 140))
    for j in range(0, int(FZ + EXTRA_N) + 1):
        text_c((p(0, j)[0] - 16, p(0, j)[1]), str(j), FS, (140, 140, 140))


def wall_rect(w):
    if w["axis"] == "x":
        return w["a"][0], w["t"][0], w["a"][1], w["t"][1]
    return w["t"][0], w["a"][0], w["t"][1], w["a"][1]


def on_level(w, level):
    """A wall belongs to a level's plan when it spans at least 0.5 m of that level's storey height."""
    lv = L.LEVELS[level]
    return min(w["y"][1], lv["ceiling"]) - max(w["y"][0], lv["floor"]) > 0.5


def opening(p, oid, o):
    w = L.WALLS[o["wall"]]
    a0, a1 = o["span"]
    t0, t1 = w["t"]
    line = (t0 + t1) / 2
    along_x = w["axis"] == "x"

    def R(u0, u1, v0, v1, **kw):   # u along the wall, v across
        if along_x:
            rect(p, u0, v0, u1, v1, **kw)
        else:
            rect(p, v0, u0, v1, u1, **kw)

    def pt(u, v):
        return p(u, v) if along_x else p(v, u)

    kind = o["kind"]
    if kind == "transom":
        return
    if kind == "window":
        R(a0, a1, t0, t1, fill=(255, 255, 255), outline=COL["win"])
        for v in (line - 0.04, line + 0.04):
            D.line([pt(a0, v), pt(a1, v)], fill=COL["win"], width=2)
        mid = L.FOOTPRINT[1 if along_x else 0] / 2
        off = -0.45 if line < mid else 0.45
        c = pt((a0 + a1) / 2, line + off)
        text_c(c, oid + (" boarded" if o.get("boarded") else ""), FS, COL["win"])
        return
    R(a0, a1, t0 - 0.02, t1 + 0.02, fill=(250, 249, 245))
    if kind == "door":
        d = o.get("swing_dir", 1)
        hinges = [0, 1] if o.get("hinge") == "both" else [1 if o.get("hinge") in ("N", "E") else 0]
        leaf = (a1 - a0) / len(hinges)
        for hi in hinges:
            h = a0 if hi == 0 else a1
            other = h + leaf if hi == 0 else h - leaf
            D.line([pt(h, line), pt(h, line + d * leaf)], fill=COL["door"], width=3)
            pts = [pt(h + (other - h) * math.cos(k / 20 * math.pi / 2), line + d * leaf * math.sin(k / 20 * math.pi / 2))
                   for k in range(21)]
            D.line(pts, fill=COL["door"], width=1)
        text_c(pt((a0 + a1) / 2, line + d * leaf * 0.45), oid, FS, COL["door"])
    else:
        for v in (line - 0.08, line + 0.08):
            D.line([pt(a0, v), pt(a1, v)], fill=COL["opening"], width=1)
        R(a0 - 0.08, a0, line - 0.12, line + 0.12, fill=COL["door"])
        R(a1, a1 + 0.08, line - 0.12, line + 0.12, fill=COL["door"])
        w_ = a1 - a0
        label = f"{oid} {'arch' if kind == 'arch' else 'cased'} {w_:.1f}"
        if kind == "cased" and (oid + "_Transom") in L.OPENINGS:
            label += " + transom"
        mid = L.FOOTPRINT[1 if along_x else 0] / 2
        text_c(pt((a0 + a1) / 2, line + (0.4 if line < mid else -0.4)), label, FS, COL["door"])


def fixture(p, fid, f):
    c = p(f["pos"][0], f["pos"][2])
    r = 8
    col = LCOL[f["kind"]]
    if f["kind"] == "area":
        D.rectangle([c[0] - r, c[1] - 5, c[0] + r, c[1] + 5], fill=col, outline=(0, 0, 0))
    else:
        off = f.get("off")
        D.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=(255, 255, 255) if off else col, outline=(0, 0, 0))
        if f.get("hero"):
            D.ellipse([c[0] - r - 5, c[1] - r - 5, c[0] + r + 5, c[1] + r + 5], outline=(220, 0, 0), width=2)
    D.text((c[0] + 10, c[1] - 7), fid, font=FS, fill=(90, 40, 0))


def furniture(p, name, f):
    if len(f) == 4:
        x0, z0, x1, z1 = f
        rect(p, x0, z0, x1, z1, outline=(150, 150, 150), width=1)
        text_c(p((x0 + x1) / 2, (z0 + z1) / 2), name, FS, (130, 130, 130))
        return
    cx, cz, w, d, yaw = f
    a = math.radians(yaw)
    fwd, rt = (math.sin(a), math.cos(a)), (math.cos(a), -math.sin(a))
    pts = [p(cx + rt[0] * w / 2 * sx + fwd[0] * d / 2 * sz, cz + rt[1] * w / 2 * sx + fwd[1] * d / 2 * sz)
           for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    D.polygon(pts, outline=(150, 150, 150))
    text_c(p(cx, cz), name, FS, (130, 130, 130))


def route(p, pts, col):
    loop = [(q[0], q[2]) for q in pts]
    loop = loop + [loop[0]]
    dashed(p, loop, col, 2)
    for a, b in zip(loop, loop[1:]):
        mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        dx, dz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dz) or 1
        arrow(p, (mid[0] - dx / ln * 0.2, mid[1] - dz / ln * 0.2), (mid[0] + dx / ln * 0.2, mid[1] + dz / ln * 0.2),
              col, 2, 9)
    for q in loop[:-1]:
        c = p(*q)
        D.rectangle([c[0] - 3, c[1] - 3, c[0] + 3, c[1] + 3], fill=col)


def camera(p, cid, c):
    x, z = c["pos"][0], c["pos"][2]
    w, h = c["size"]
    hfov = math.degrees(2 * math.atan(math.tan(math.radians(c["fov"] / 2)) * w / h))
    q = p(x, z)
    D.rectangle([q[0] - 7, q[1] - 7, q[0] + 7, q[1] + 7], fill=COL["cam"])
    for s in (-1, 1):
        a = math.radians(c["yaw"] + s * hfov / 2)
        dashed(p, [(x, z), (x + math.sin(a) * 2.6, z + math.cos(a) * 2.6)], COL["cam"], 1, 5)
    a = math.radians(c["yaw"])
    arrow(p, (x, z), (x + math.sin(a) * 1.3, z + math.cos(a) * 1.3), COL["cam"], 2, 8)
    D.text((q[0] + 10, q[1] + 6), f"{cid} ({c['ref'][:4]})", font=FS, fill=COL["cam"])


def zombie(p, zid, s):
    c = p(s["pos"][0], s["pos"][2])
    D.ellipse([c[0] - 11, c[1] - 11, c[0] + 11, c[1] + 11], fill=COL["zomb"], outline=(0, 0, 0))
    text_c(c, zid, FS, (255, 255, 255))
    if s.get("note"):
        D.text((c[0] + 14, c[1] + 4), s["note"], font=FS, fill=COL["zomb"])


def dim(p, a, b, s, off_px, vertical=False):
    pa, pb = p(*a), p(*b)
    if vertical:
        x = pa[0] + off_px
        D.line([(x, pa[1]), (x, pb[1])], fill=COL["dim"], width=1)
        for y in (pa[1], pb[1]):
            D.line([(x - 6, y), (x + 6, y)], fill=COL["dim"], width=2)
        l, t, r, bb = D.textbbox((0, 0), s, font=F)
        tmp = Image.new("RGBA", (r - l + 6, 22), (0, 0, 0, 0))
        ImageDraw.Draw(tmp).text((3, 2), s, font=F, fill=COL["dim"])
        tmp = tmp.rotate(90, expand=True)
        IMG.paste(tmp, (int(x - 24), int((pa[1] + pb[1]) / 2 - tmp.size[1] / 2)), tmp)
    else:
        y = pa[1] + off_px
        D.line([(pa[0], y), (pb[0], y)], fill=COL["dim"], width=1)
        for x in (pa[0], pb[0]):
            D.line([(x, y - 6), (x, y + 6)], fill=COL["dim"], width=2)
        text_c(((pa[0] + pb[0]) / 2, y - 11), s, F, COL["dim"])


def room_label(p, rid, r):
    cx, cz = (r["x"][0] + r["x"][1]) / 2, (r["z"][0] + r["z"][1]) / 2
    if rid == "CentralHall":
        cx = (L.BALUSTRADE["east"]["x"][1] + r["x"][1]) / 2
    if rid == "B1":
        cx += 0.5
    w, d = r["x"][1] - r["x"][0], r["z"][1] - r["z"][0]
    c = p(cx, cz)
    name = (r.get("name") and (rid + " " if rid != r["name"].replace(" ", "") else "") + r["name"].upper()) or {"EntranceHall": "ENTRANCE HALL",
                                                                 "CentralHall": "CENTRAL HALL"}.get(rid, rid.upper())
    text_c((c[0], c[1] - 18), name, FB)
    text_c((c[0], c[1] + 2), f"{w:.2f} x {d:.2f} = {w * d:.1f} m2", F)
    text_c((c[0], c[1] + 20), f"{w * d / r['phase1']:.1f}x Phase 1" if r["phase1"] else "new in rev 3", FS,
           (90, 90, 90))


def stair_plan(p, basement):
    st = L.STAIR
    x0, x1 = st["x"]
    rect(p, x0, st["z_top"], x1, st["z_bottom"], fill=(214, 200, 180))
    for k in range(st["risers"]):
        z = st["z_top"] + k * st["run"]
        D.line([p(x0, z), p(x1, z)], fill=COL["stair"], width=1)
    xm = (x0 + x1) / 2
    if basement:
        arrow(p, (xm, st["z_bottom"] - 0.15), (xm, st["z_top"] + 0.15), COL["stair"], 3, 12)
        text_c(p(xm, (st["z_top"] + st["z_bottom"]) / 2), "UP", FB, COL["stair"])
        rect(p, st["rail_x"][0], st["z_top"], st["rail_x"][1], st["z_bottom"], fill=COL["bal"])
        text_c(p(xm, st["z_bottom"] + 0.8), "landing", FS, COL["stair"])
        dashed(p, [(x0, st["opening"]["z"][1]), (x1 + 0.1, st["opening"]["z"][1])], (0, 0, 0), 1, 5)
        text_c(p(xm + 0.1, st["opening"]["z"][1] + 0.2), "slab edge", FS, (0, 0, 0))
        return
    o = st["opening"]
    drect(p, o["x"][0], o["z"][0], o["x"][1], o["z"][1], (0, 0, 0))
    arrow(p, (xm, st["z_top"] + 0.15), (xm, st["z_bottom"] - 0.15), COL["stair"], 3, 12)
    text_c(p(xm, st["z_top"] + 1.4), "DN", FB, COL["stair"])
    text_c(p(xm, st["z_top"] + 1.1), f"{st['risers']}R x {st['rise']}", FS, COL["stair"])
    text_c(p(xm, st["z_top"] + 0.85), f"{st['treads']}T x {st['run']}", FS, COL["stair"])
    b = L.BALUSTRADE
    rect(p, *[b["east"]["x"][0], b["east"]["z"][0], b["east"]["x"][1], b["east"]["z"][1]], fill=COL["bal"])
    rect(p, *[b["north"]["x"][0], b["north"]["z"][0], b["north"]["x"][1], b["north"]["z"][1]], fill=COL["bal"])
    g = L.GATE
    dashed(p, [(o["x"][0], o["z"][0] - 0.02), (o["x"][1], o["z"][0] - 0.02)], (0, 120, 0), 2, 4)
    text_c(p(xm, o["z"][0] - 0.3), "G1 gate", FS, (0, 120, 0))
    text_c(p(b["east"]["x"][1] + 0.9, o["z"][0] + 0.4), f"balustrade {b['height']} h", FS, COL["bal"])
    _ = g


def draw():
    """Draws the whole sheet from the layout tables and returns the PIL image."""
    global IMG, D
    IMG = Image.new("RGB", (PANEL_W * 2, PANEL_H + 560), (250, 249, 245))
    D = ImageDraw.Draw(IMG)
    # ======================================================================= GROUND FLOOR
    g = P(0, 40)
    D.text((M, 14), f"GROUND FLOOR  (floor y = 0.00, ceiling {L.CEILING:.2f})  rev {L.REVISION}: decision 11 + 12",
           font=FT, fill=(0, 0, 0))
    grid(g)
    for rid, r in L.ROOMS.items():
        if r["level"] == "ground":
            rect(g, r["x"][0], r["z"][0], r["x"][1], r["z"][1], fill=TINT.get(rid, (235, 235, 235)))
    grid(g)
    for wid, w in L.WALLS.items():
        if on_level(w, "ground"):
            rect(g, *wall_rect(w), fill=COL["wall"])
    fp = L.FIREPLACE
    rect(g, fp["breast"]["x"][0], fp["breast"]["z"][0], fp["breast"]["x"][1], fp["breast"]["z"][1], fill=(205, 200, 190),
         outline=COL["wall"], width=2)
    rect(g, fp["firebox"]["x"][0], fp["firebox"]["z"][0], fp["firebox"]["x"][1], fp["firebox"]["z"][1], fill=(60, 40, 30))
    text_c(g((fp["breast"]["x"][0] + fp["breast"]["x"][1]) / 2, fp["hearth"]["z"][1] + 0.25), "FP whitewashed brick",
           FS, (90, 70, 50))
    for oid, o in L.OPENINGS.items():
        if on_level(L.WALLS[o["wall"]], "ground"):
            opening(g, oid, o)
    a4 = L.A4
    dashed(g, [(a4["x"][0], sum(a4["z"]) / 2), (a4["x"][1], sum(a4["z"]) / 2)], COL["opening"], 1, 6)
    text_c(g(a4["x"][1] - 1.3, a4["z"][1] + 0.18), "A4 hall arch (overhead)", FS, COL["opening"])
    stair_plan(g, False)
    for n, f in L.FURNITURE.items():
        furniture(g, n, f)
    for rid, r in L.ROOMS.items():
        if r["level"] == "ground":
            room_label(g, rid, r)
    # sightlines (direction 12): long views through aligned openings
    SIGHT = [((9.25, -0.2), (9.25, 25.6), "D1-A2-DC1-DC2 25.8 m"), ((1.0, 2.1), (19.6, 2.1), "D2-A1 18.6 m"),
             ((15.0, 0.4), (15.0, 14.6), "O1 14.2 m")]
    for a, b, lab in SIGHT:
        dashed(g, [a, b], COL["sight"], 2, 14)
        D.text(g(a[0] + 0.15, a[1] + 0.25), lab, font=FS, fill=(180, 110, 0))
    for fid, f in L.FIXTURES.items():
        if f["pos"][1] >= -0.1:
            fixture(g, fid, f)
    for rid, pts in L.ROUTES.items():
        if pts[0][1] >= -0.1:
            route(g, pts, (220, 60, 60) if rid == "R1" else (190, 20, 120))
    for zid, s in L.SPAWNS.items():
        if s["kind"] == "zombie" and s["pos"][1] >= -0.1:
            zombie(g, zid, s)
    s0 = L.SPAWNS["P0"]
    pc = g(s0["pos"][0], s0["pos"][2])
    D.polygon([(pc[0], pc[1] + 14), (pc[0] - 10, pc[1] - 8), (pc[0] + 10, pc[1] - 8)], fill=COL["player"])
    D.text((pc[0] + 12, pc[1] - 4), "P0 (wakes on bed)", font=FS, fill=COL["player"])
    for cid, c in L.CAMERAS.items():
        camera(g, cid, c)
    # FUTURE (reserved, not built)
    for fid, f in L.FUTURE.items():
        if f["level"] != "ground":
            continue
        drect(g, f["x"][0], f["z"][0], f["x"][1], f["z"][1], COL["future"], 2, 8)
        D.text(g(f["x"][0] + 0.15, f["z"][1] - 0.1), f"FUTURE {fid}", font=FS, fill=(110, 110, 110))
        if f.get("attach"):
            w = L.WALLS[f["attach"]["wall"]]
            a0, a1 = f["attach"]["span"]
            t = sum(w["t"]) / 2
            if w["axis"] == "x":
                D.line([g(a0, t), g(a1, t)], fill=(0, 170, 0), width=5)
            else:
                D.line([g(t, a0), g(t, a1)], fill=(0, 170, 0), width=5)
    dim(g, (0, 0), (FX, 0), f"{FX:.2f} m (outer)", 48)
    dim(g, (0, 0), (0, FZ), f"{FZ:.2f} m (outer)", -70, vertical=True)
    for rid, off in (("Bedroom", -46), ("CentralHall", -46), ("Kitchen", -46)):
        r = L.ROOMS[rid]
        dim(g, (r["x"][0], FZ + EXTRA_N - 0.1), (r["x"][1], FZ + EXTRA_N - 0.1), f"{r['x'][1] - r['x'][0]:.3f}", 0)
    for rid in ("Living", "Kitchen"):
        r = L.ROOMS[rid]
        dim(g, (FX, r["z"][0]), (FX, r["z"][1]), f"{r['z'][1] - r['z'][0]:.3f}", 34, vertical=True)
    for rid in ("Bedroom", "Bathroom"):
        r = L.ROOMS[rid]
        dim(g, (0, r["z"][0]), (0, r["z"][1]), f"{r['z'][1] - r['z'][0]:.3f}", -30, vertical=True)
    na = (PANEL_W - 60, 70)
    D.polygon([(na[0], na[1] - 30), (na[0] - 12, na[1] + 6), (na[0] + 12, na[1] + 6)], fill=(0, 0, 0))
    text_c((na[0], na[1] + 22), "N (+Z)", F)

    # ======================================================================= BASEMENT
    b = P(PANEL_W, 40)
    lvb = L.LEVELS["basement"]
    D.text((PANEL_W + M, 14), f"BASEMENT  (floor y = {lvb['floor']:.2f}, slab underside {lvb['ceiling']:.2f}, "
           f"joists {-L.SLAB - L.JOIST['depth']:.2f})", font=FT, fill=(0, 0, 0))
    grid(b)
    rect(b, 0, 0, FX, FZ, outline=(200, 200, 200), width=1)
    D.text(b(0.6, 7.5), "(no basement under the bedroom,", font=FS, fill=(160, 160, 160))
    D.text(b(0.6, 7.1), " bathroom, entrance hall or", font=FS, fill=(160, 160, 160))
    D.text(b(0.6, 6.7), " south of the living room: solid ground)", font=FS, fill=(160, 160, 160))
    for rid, r in L.ROOMS.items():
        if r["level"] == "basement":
            rect(b, r["x"][0], r["z"][0], r["x"][1], r["z"][1], fill=COL["water"])
    for wid, w in L.WALLS.items():
        if on_level(w, "basement"):
            rect(b, *wall_rect(w), fill=COL["wall"])
    for oid, o in L.OPENINGS.items():
        if on_level(L.WALLS[o["wall"]], "basement"):
            opening(b, oid, o)
    st = L.STAIR
    j = L.JOIST
    z = j["first_z"]
    while z < L.ROOMS["B1"]["z"][1]:
        x0 = L.BEAMS["Header_E"]["x"][1] if st["opening"]["z"][0] <= z <= st["opening"]["z"][1] else L.ROOMS["B1"]["x"][0]
        dashed(b, [(x0, z), (L.ROOMS["B2"]["x"][1], z)], (120, 120, 160), 1, 6)
        z += j["pitch"]
    stair_plan(b, True)
    for n, f in L.FURNITURE_B.items():
        furniture(b, n, f)
    for rid, r in L.ROOMS.items():
        if r["level"] == "basement":
            room_label(b, rid, r)
    for fid, f in L.FIXTURES.items():
        if f["pos"][1] < -0.1:
            fixture(b, fid, f)
    for rid, pts in L.ROUTES.items():
        if pts[0][1] < -0.1:
            route(b, pts, (220, 60, 60))
    for zid, s in L.SPAWNS.items():
        if s["kind"] == "zombie" and s["pos"][1] < -0.1:
            zombie(b, zid, s)
    for oid, o in L.OPENINGS.items():
        if o.get("well"):
            wl = o["well"]
            drect(b, wl["x"][0], wl["z"][0], wl["x"][1], wl["z"][1], (90, 90, 90))
    r1, r2 = L.ROOMS["B1"], L.ROOMS["B2"]
    dim(b, (r1["x"][0], FZ + EXTRA_N - 0.1), (r1["x"][1], FZ + EXTRA_N - 0.1), f"{r1['x'][1] - r1['x'][0]:.3f}", 0)
    dim(b, (r2["x"][0], FZ + EXTRA_N - 0.1), (r2["x"][1], FZ + EXTRA_N - 0.1), f"{r2['x'][1] - r2['x'][0]:.3f}", 0)
    dim(b, (FX, r1["z"][0]), (FX, r1["z"][1]), f"{r1['z'][1] - r1['z'][0]:.3f} clear", 34, vertical=True)

    # ======================================================================= STAIR SECTION
    oy = PANEL_H + 20
    D.text((M, oy), f"STAIR SECTION (looking west through the stair, x = {sum(st['x']) / 2:.2f}; 0.75 x plan scale)",
           font=FB, fill=(0, 0, 0))
    SC = 0.75
    zA, zB = st["z_top"] - 1.5, L.ROOMS["CentralHall"]["z"][1]


    def sct(z, y):
        return (M + 40 + (z - zA) * S * SC, oy + 60 + (L.CEILING + 0.3 - y) * S * SC)


    for (z0, z1) in [(zA, st["opening"]["z"][0]), (st["opening"]["z"][1], zB)]:
        D.rectangle([sct(z0, 0), sct(z1, -L.SLAB)], fill=(120, 110, 100))
    D.rectangle([sct(L.BEAMS["Trimmer_N"]["z"][0], -L.SLAB), sct(L.BEAMS["Trimmer_N"]["z"][1], L.TRIMMER_UNDERSIDE)],
                fill=(120, 90, 60))
    D.line([sct(zA, L.CEILING), sct(zB, L.CEILING)], fill=(0, 0, 0), width=2)
    D.rectangle([sct(zA, L.BASEMENT), sct(zB, L.BASEMENT - 0.15)], fill=(120, 110, 100))
    pts = [sct(st["z_top"], 0)]
    y, zz = 0.0, st["z_top"]
    for i in range(st["risers"]):
        y -= st["rise"]
        pts.append(sct(zz, y))
        if i < st["treads"]:
            zz += st["run"]
            pts.append(sct(zz, y))
    pts.append(sct(zB, L.BASEMENT))
    D.line(pts, fill=COL["stair"], width=3)
    D.line([sct(st["z_top"], 0), sct(st["z_bottom"], -st["treads"] * st["rise"])], fill=(0, 150, 0), width=1)
    D.line([sct(st["z_top"], 0), sct(st["z_top"], L.CEILING)], fill=(0, 150, 0), width=1)
    D.text(sct(st["z_top"] + 0.05, L.CEILING / 2), f"{L.CEILING:.2f}", font=FS, fill=(0, 120, 0))
    zt = st["opening"]["z"][1]
    D.line([sct(zt, L.TRIMMER_UNDERSIDE), sct(zt, L.BASEMENT)], fill=(200, 0, 0), width=1)
    D.text(sct(zt + 0.05, -1.6), f"{L.TRIMMER_UNDERSIDE - L.BASEMENT:.2f} clear under trimmer", font=FS, fill=(200, 0, 0))
    D.text(sct(st["z_top"] + 1.6, 0.8), f"headroom over pitch line >= {L.CEILING:.2f} (whole flight under the open well)",
           font=FS, fill=(0, 120, 0))
    D.text(sct(zt + 0.6, -0.9), f"basement {-L.SLAB - L.BASEMENT:.2f} to slab / "
           f"{-L.SLAB - L.JOIST['depth'] - L.BASEMENT:.2f} under joists", font=FS, fill=(0, 0, 0))
    D.text(sct(zA + 0.05, 0.15), "GF 0.00", font=FS, fill=(0, 0, 0))
    D.text(sct(zA + 0.05, L.BASEMENT + 0.15), f"B {L.BASEMENT:.2f}", font=FS, fill=(0, 0, 0))
    D.text(sct(st["z_top"] - 0.1, -0.7), f"z={st['z_top']:.2f}", font=FS, fill=(0, 0, 0))
    D.text(sct(st["z_bottom"] - 0.3, L.BASEMENT + 0.35), f"z={st['z_bottom']:.2f}", font=FS, fill=(0, 0, 0))
    D.text(sct(zA + 0.05, -1.4), f"{st['risers']} x {st['rise']} rise, {st['treads']} x {st['run']} run, "
           f"pitch {st['pitch_deg']:.1f} deg", font=FS, fill=(0, 0, 0))

    # legend
    lx, ly = PANEL_W + M - 40, oy + 10
    D.text((lx, ly), "LEGEND", font=FB, fill=(0, 0, 0))
    items = [("wall: exterior 0.30, interior partitions 0.15 (centrelines on the 0.5 m grid)", COL["wall"]),
             (f"door (>= {L.DOOR_W} x {L.DOOR_H} clear) with swing arc; D1 is a double door", COL["door"]),
             ("arch / cased opening (A1-A4, O1); window (W..)", COL["win"]),
             ("light fixture with ID; red ring = realtime-shadow hero; white = off/broken", LCOL["lamp"]),
             ("baked area light at a window (rectangle)", LCOL["area"]),
             ("zombie spawn Z1-Z6 + patrol route (dashed, arrows = direction, squares = waypoints)", COL["zomb"]),
             ("player spawn P0 (green triangle)", COL["player"]),
             ("CAM-1/2/3 = reference match cameras (yaw arrow + FOV wedge)", COL["cam"]),
             ("long sightline through aligned openings (blink mechanic)", COL["sight"]),
             ("FUTURE (dashed grey, not built): attic hatch, east wing; green = attach point",
              COL["future"]),
             ("flood water in basement (light blue)", COL["water"])]
    for i, (s_, c) in enumerate(items):
        yy = ly + 30 + i * 24
        D.rectangle([lx, yy, lx + 18, yy + 14], fill=c)
        D.text((lx + 28, yy - 1), s_, font=F, fill=(0, 0, 0))
    bx, by = lx, ly + 30 + len(items) * 24 + 20
    for i in range(5):
        D.rectangle([bx + i * S, by, bx + (i + 1) * S, by + 10], fill=(0, 0, 0) if i % 2 == 0 else (255, 255, 255),
                    outline=(0, 0, 0))
        text_c((bx + i * S, by + 22), str(i), FS)
    text_c((bx + 5 * S, by + 22), "5 m", FS)
    D.text((bx, by + 38), "Origin (0,0,0) = outer SW corner at ground-floor finished level. +X east, +Z north, +Y up.",
           font=F, fill=(0, 0, 0))
    D.text((bx, by + 60), f"Grid 1 m (dark) / 0.5 m (light). Ceiling {L.CEILING:.2f} on the ground floor. "
           "Generated by tools/blender/bungalow_floorplan.py from bungalow_layout.py.", font=F, fill=(0, 0, 0))
    return IMG


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "docs", "bungalow", "floorplan.png")
    img = draw()
    img.save(out)
    print("saved", os.path.abspath(out), img.size)
