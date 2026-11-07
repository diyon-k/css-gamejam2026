"""BLINK bungalow: atmosphere data for the Phase 6 lighting pass (plain Python, no bpy).

    python -I tools/blender/bungalow_decals.py
        -> writes Unity/Assets/Art/Environment/Bungalow/Lighting/bungalow_atmosphere.json

Reads the layout (bungalow_layout.py, read-only) and resolves:
- decals: mesh-decal quads (M_Decals atlas cells) placed on the room rectangles' wall / floor / ceiling planes, offset a
  few millimetres off the surface. Every wall decal is checked against the openings cut through that wall and against
  fixed obstructions (chimney breast, stair stringer), so a layout change that moves a door fails loudly here instead
  of leaving a stain floating in a doorway. Bands (tide-line, grime) are split around openings automatically.
- areaLights: size and facing of the window area lights (bungalow_layout.FIXTURES, not in bungalow_layout.json).
- stairWell: the ground-floor well rectangle (probe and reflection-probe layout).
- water: the basement flood plane.
Editor/BungalowLightingBuilder.cs (menu BLINK/Bungalow/Build Lighting) reads the JSON.

Placement table conventions (DECALS below):
- wall(room, side, along, y, w, h, cell): side N/S/E/W = the room rectangle's face; along = world x (N/S) or z (E/W) of
  the decal centre; y = centre height above the room floor. The decal's up is world up (leaks run downwards).
- ceil(room, x, z, w, h, cell, rot) / floor(room, x, z, w, h, cell, rot): world x / z of the centre; rot in degrees.
- band(room, side, y, h, cell, seg): a seamless band along the whole face, segments of `seg` metres, split around
  openings and obstructions.
"""
import json
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bungalow_layout as L  # noqa: E402  (read-only: owned by the kit / layout phase)

OUT_JSON = os.path.abspath(os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Environment", "Bungalow",
                                        "Lighting", "bungalow_atmosphere.json"))
REVISION = 1

OFFSET_WALL = 0.006    # metres off the wall plane (no z-fighting at 15 m with the 0.02 near plane)
OFFSET_FLOOR = 0.004
OFFSET_CEIL = 0.006
MARGIN = 0.05          # clearance from openings / obstructions
SKIRT_TOP = 0.22       # keep wall decals above the skirting...
CORNICE = 0.18         # ...and below the cornice
WATER_Y = -2.77        # SPEC 2.7: 0.08 m of standing water on the basement floor

# Fixed things standing against a wall face that decals must not cross: (room, side, along range, y range above floor)
OBSTRUCTIONS = [
    ("Bedroom", "S", (L.FIREPLACE["breast"]["x"][0], L.FIREPLACE["breast"]["x"][1]), (0.0, 3.0)),   # chimney breast
    ("B1", "W", (L.STAIR["z_top"] - 0.1, L.STAIR["z_bottom"] + 0.1), (0.0, 2.6)),                  # stair stringer
]

SIDES = {  # side -> (normal into the room, axis along, which room bound is the face)
    "N": ((0.0, 0.0, -1.0), "x", ("z", 1)),
    "S": ((0.0, 0.0, 1.0), "x", ("z", 0)),
    "E": ((-1.0, 0.0, 0.0), "z", ("x", 1)),
    "W": ((1.0, 0.0, 0.0), "z", ("x", 0)),
}


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def rnd(v):
    return [round(c, 4) for c in v]


def room(rid):
    r = L.ROOMS[rid]
    lv = L.LEVELS[r["level"]]
    return r, r["y"], lv["ceiling"]


def face_wall(rid, side):
    """The WALLS entry whose inner face is this room side (None if no wall, e.g. an open side)."""
    r, _, _ = room(rid)
    _, along_axis, (bound_axis, idx) = SIDES[side]
    plane = r[bound_axis][idx]
    wall_axis = "x" if bound_axis == "z" else "z"     # a wall facing +-z runs along x
    for wid, w in L.WALLS.items():
        if w["axis"] != wall_axis:
            continue
        if abs(w["t"][0] - plane) < 1e-3 or abs(w["t"][1] - plane) < 1e-3:
            lo, hi = r[along_axis]
            if w["a"][0] <= lo + 1e-3 and w["a"][1] >= hi - 1e-3:
                return wid, w
    return None, None


def blockers(rid, side):
    """Rectangles (along range, absolute y range) on this face that decals must avoid."""
    _, floor_y, _ = room(rid)
    out = []
    wid, w = face_wall(rid, side)
    if w is not None:
        for oid in w["openings"]:
            o = L.OPENINGS[oid]
            out.append((oid, o["span"], o["y"]))
    for orid, oside, along, yr in OBSTRUCTIONS:
        if orid == rid and oside == side:
            out.append(("obstruction", along, (floor_y + yr[0], floor_y + yr[1])))
    return out


def overlaps(a0, a1, b0, b1):
    return a0 < b1 + MARGIN and b0 - MARGIN < a1


DECALS = []
_errors = []


def _add(rid, surface, cell, c, n, v, w, h, sub=-1):
    r = cross(n, v)
    DECALS.append(dict(id=f"{rid}_{surface}_{cell}_{len(DECALS):03d}", room=rid, surface=surface, cell=cell,
                       sub=sub, c=rnd(c), n=rnd(n), r=rnd(r), v=rnd(v), size=[round(w, 3), round(h, 3)]))


def wall(rid, side, along, y, w, h, cell, sub=-1):
    r, floor_y, ceil_y = room(rid)
    n, along_axis, (bound_axis, idx) = SIDES[side]
    lo, hi = r[along_axis]
    a0, a1 = along - w / 2, along + w / 2
    y0, y1 = floor_y + y - h / 2, floor_y + y + h / 2
    tag = f"{rid}.{side} decal {cell} at {along:.2f}/{y:.2f}"
    if a0 < lo + MARGIN or a1 > hi - MARGIN:
        _errors.append(f"{tag}: runs off the face ({lo:.3f}..{hi:.3f})")
    if y0 < floor_y + SKIRT_TOP - 1e-6 or y1 > ceil_y - CORNICE + 1e-6:
        _errors.append(f"{tag}: crosses the skirting or the cornice")
    for oid, span, yr in blockers(rid, side):
        if overlaps(a0, a1, span[0], span[1]) and overlaps(y0, y1, yr[0], yr[1]):
            _errors.append(f"{tag}: overlaps {oid}")
    plane = r[bound_axis][idx]
    c = [0.0, floor_y + y, 0.0]
    if bound_axis == "z":
        c[0], c[2] = along, plane + n[2] * OFFSET_WALL
    else:
        c[0], c[2] = plane + n[0] * OFFSET_WALL, along
    _add(rid, side, cell, c, n, (0.0, 1.0, 0.0), w, h, sub)


def band(rid, side, y, h, cell, seg=2.0):
    """Seamless band (tide_line, grime_band) along the whole face, split around openings and obstructions."""
    r, floor_y, _ = room(rid)
    _, along_axis, _ = SIDES[side]
    lo, hi = r[along_axis][0] + 2 * MARGIN, r[along_axis][1] - 2 * MARGIN
    y0, y1 = floor_y + y - h / 2, floor_y + y + h / 2
    cuts = sorted((s[0] - 2 * MARGIN, s[1] + 2 * MARGIN) for _, s, yr in blockers(rid, side) if overlaps(y0, y1, yr[0], yr[1]))
    runs, cur = [], lo
    for c0, c1 in cuts:
        if c0 > cur:
            runs.append((cur, min(c0, hi)))
        cur = max(cur, c1)
    if cur < hi:
        runs.append((cur, hi))
    for a, b in runs:
        length = b - a
        if length < 0.3:
            continue
        k = max(1, round(length / seg))
        step = length / k
        for i in range(k):
            wall(rid, side, a + step * (i + 0.5), y, step, h, cell)


def _flat(rid, surface, x, z, w, h, cell, rot, sub):
    r, floor_y, ceil_y = room(rid)
    rad = math.radians(rot)
    d = (math.sin(rad), 0.0, math.cos(rad))           # yaw 0 = decal up points north
    reach = math.hypot(w, h) / 2
    if (x - reach < r["x"][0] or x + reach > r["x"][1] or z - reach < r["z"][0] or z + reach > r["z"][1]):
        _errors.append(f"{rid}.{surface} decal {cell} at {x:.2f},{z:.2f}: runs off the room rectangle")
    for hole in r.get("floor_holes", []) if surface == "floor" else []:
        if overlaps(x - reach, x + reach, *hole["x"]) and overlaps(z - reach, z + reach, *hole["z"]):
            _errors.append(f"{rid}.floor decal {cell} at {x:.2f},{z:.2f}: over the {hole['id']} hole")
    if surface == "floor":
        _add(rid, "floor", cell, (x, floor_y + OFFSET_FLOOR, z), (0.0, 1.0, 0.0), d, w, h, sub)
    else:
        _add(rid, "ceil", cell, (x, ceil_y - OFFSET_CEIL, z), (0.0, -1.0, 0.0), d, w, h, sub)


def floor(rid, x, z, w, h, cell, rot=0.0, sub=-1):
    _flat(rid, "floor", x, z, w, h, cell, rot, sub)


def ceil(rid, x, z, w, h, cell, rot=0.0):
    _flat(rid, "ceil", x, z, w, h, cell, rot, -1)


def scatter(rid, n, cells, area, size, seed):
    """Seeded floor debris (paper scraps, crumbs, small stains) inside area = ((x0, x1), (z0, z1))."""
    rng = random.Random(seed)
    for _ in range(n):
        cell = rng.choice(cells)
        s = rng.uniform(*size)
        sub = rng.randrange(4) if cell == "paper_scraps" else -1
        if sub >= 0:
            s *= 0.5          # one sheet, not the whole 2 x 2 cell
        floor(rid, rng.uniform(*area[0]), rng.uniform(*area[1]), s, s, cell, rng.uniform(0, 360), sub)


# ------------------------------------------------------------------ placements (SPEC 2: decay per room)
H = L.CEILING
# Entrance hall: water stain spreading from the front-door corner (SW of D1), scuffs inside the door
wall("EntranceHall", "S", 7.75, 1.55, 1.0, 2.2, "leak_streaks")
wall("EntranceHall", "W", 3.35, 2.5, 0.8, 0.5, "mould_bloom")
ceil("EntranceHall", 8.2, 1.4, 1.3, 1.3, "ceiling_stain", 20)
floor("EntranceHall", 9.25, 1.3, 1.2, 1.2, "scuffs", 90)
floor("EntranceHall", 7.8, 1.0, 0.9, 0.9, "floor_stain", 40)
scatter("EntranceHall", 4, ["paper_scraps", "plaster_crumbs"], ((7.5, 11.0), (0.8, 3.5)), (0.35, 0.6), 11)

# Central hall (ref 1): wallpaper lifting at the seams, damp at the ceiling line, a scuffed runner path
wall("CentralHall", "E", 7.4, 1.7, 0.7, 1.2, "peel_edge")
wall("CentralHall", "E", 11.6, 1.5, 0.7, 1.2, "peel_edge")
wall("CentralHall", "E", 12.6, 2.45, 0.9, 0.6, "mould_bloom")
wall("CentralHall", "W", 11.2, 1.6, 0.9, 2.0, "leak_streaks")
wall("CentralHall", "W", 13.9, 2.5, 0.8, 0.5, "mould_bloom")
wall("CentralHall", "N", 7.55, 2.5, 0.7, 0.5, "mould_bloom")
wall("CentralHall", "N", 11.0, 2.45, 0.6, 0.6, "mould_bloom")
wall("CentralHall", "S", 11.0, 1.6, 0.6, 1.8, "leak_streaks")
ceil("CentralHall", 10.2, 12.0, 1.5, 1.5, "ceiling_stain", 70)
ceil("CentralHall", 10.0, 5.2, 1.1, 1.1, "ceiling_stain", 200)
floor("CentralHall", 9.9, 7.5, 1.4, 1.4, "scuffs", 0)
floor("CentralHall", 9.9, 11.5, 1.4, 1.4, "scuffs", 180)
floor("CentralHall", 10.6, 13.8, 0.9, 0.9, "floor_stain", 15)
scatter("CentralHall", 6, ["paper_scraps", "plaster_crumbs"], ((8.9, 11.0), (4.6, 14.2)), (0.35, 0.6), 12)

# Living room (ref 2): damp tide-lines and leaks, brown ceiling stains, paper debris everywhere
wall("Living", "S", 12.05, 1.6, 0.8, 2.0, "leak_streaks")
wall("Living", "S", 15.4, 2.45, 1.0, 0.7, "mould_bloom")
wall("Living", "E", 1.6, 1.5, 1.0, 2.2, "leak_streaks")
wall("Living", "E", 5.7, 2.5, 0.9, 0.6, "mould_bloom")
wall("Living", "W", 4.4, 1.5, 0.8, 1.6, "peel_edge")
wall("Living", "W", 6.3, 1.6, 0.7, 2.0, "leak_streaks")
wall("Living", "N", 12.5, 1.6, 0.9, 2.2, "leak_streaks")
wall("Living", "N", 18.4, 2.5, 1.0, 0.6, "mould_bloom")
band("Living", "S", 0.45, 0.32, "grime_band", 2.0)
band("Living", "E", 0.45, 0.32, "grime_band", 2.0)
ceil("Living", 13.1, 1.6, 1.8, 1.8, "ceiling_stain", 30)
ceil("Living", 17.9, 5.2, 1.3, 1.3, "ceiling_stain", 110)
ceil("Living", 16.6, 2.0, 0.9, 0.9, "ceiling_stain", 250)
floor("Living", 12.2, 4.6, 0.7, 0.7, "plaster_crumbs", 30)
floor("Living", 15.0, 6.1, 1.1, 1.1, "floor_stain", 60)
floor("Living", 18.3, 1.3, 1.2, 1.2, "scuffs", 90)
scatter("Living", 14, ["paper_scraps", "paper_scraps", "plaster_crumbs"], ((12.0, 19.2), (0.8, 6.4)),
        (0.35, 0.65), 13)

# Kitchen: stained, debris-strewn lino; rust and leaks under the windows; mould at the ceiling line
wall("Kitchen", "N", 12.6, 1.6, 0.9, 2.2, "leak_streaks")
wall("Kitchen", "N", 16.6, 1.4, 0.7, 1.4, "rust_streaks")
wall("Kitchen", "N", 17.4, 2.5, 0.9, 0.6, "mould_bloom")   # rev 3: off DC4
wall("Kitchen", "E", 8.0, 1.5, 0.9, 2.0, "leak_streaks")
wall("Kitchen", "E", 14.2, 2.45, 0.9, 0.6, "mould_bloom")
wall("Kitchen", "W", 12.4, 1.6, 0.8, 1.8, "leak_streaks")
wall("Kitchen", "S", 18.8, 1.5, 0.8, 1.6, "peel_edge")
ceil("Kitchen", 15.8, 9.3, 1.8, 1.8, "ceiling_stain", 10)
ceil("Kitchen", 12.6, 13.6, 1.2, 1.2, "ceiling_stain", 95)
floor("Kitchen", 14.2, 9.2, 1.5, 1.5, "floor_stain", 20)
floor("Kitchen", 17.4, 12.6, 1.3, 1.3, "floor_stain", 140)
floor("Kitchen", 12.6, 11.9, 1.1, 1.1, "floor_stain", 250)
floor("Kitchen", 16.0, 8.0, 1.2, 1.2, "scuffs", 90)
scatter("Kitchen", 14, ["paper_scraps", "plaster_crumbs", "floor_stain"], ((12.1, 19.2), (7.6, 14.2)),
        (0.35, 0.7), 14)

# Bedroom (ref 3): stained green paper, mouldy at the ceiling line; damp under the window
for along, w in ((1.2, 1.0), (5.6, 1.1)):
    wall("Bedroom", "S", along, 2.5, w, 0.6, "mould_bloom")
for along, w in ((1.0, 1.0), (4.6, 1.0), (8.4, 1.0)):
    wall("Bedroom", "W", along, 2.5, w, 0.6, "mould_bloom")
for along, w in ((1.0, 0.9), (3.8, 1.2), (6.1, 0.9)):
    wall("Bedroom", "N", along, 2.5, w, 0.6, "mould_bloom")
wall("Bedroom", "E", 5.0, 2.5, 1.0, 0.6, "mould_bloom")
wall("Bedroom", "W", 4.4, 1.5, 0.8, 2.0, "leak_streaks")
wall("Bedroom", "E", 7.8, 1.6, 0.8, 2.0, "leak_streaks")
ceil("Bedroom", 1.6, 5.6, 1.6, 1.6, "ceiling_stain", 45)
ceil("Bedroom", 5.4, 1.7, 1.1, 1.1, "mould_bloom", 120)
floor("Bedroom", 5.6, 4.2, 1.2, 1.2, "scuffs", 30)
scatter("Bedroom", 5, ["paper_scraps", "plaster_crumbs"], ((0.9, 6.4), (1.6, 8.6)), (0.3, 0.55), 15)

# Bathroom: mould on the ceiling, rust streaks, damp
wall("Bathroom", "S", 2.0, 1.6, 0.6, 1.4, "rust_streaks")
wall("Bathroom", "S", 5.5, 2.5, 1.0, 0.6, "mould_bloom")
wall("Bathroom", "N", 1.2, 2.45, 0.9, 0.7, "mould_bloom")
wall("Bathroom", "N", 4.4, 1.3, 0.5, 1.0, "rust_streaks")
wall("Bathroom", "W", 12.2, 1.6, 0.8, 2.0, "leak_streaks")
ceil("Bathroom", 1.6, 13.4, 1.6, 1.6, "mould_bloom", 0)
ceil("Bathroom", 4.9, 11.0, 1.4, 1.4, "mould_bloom", 160)
ceil("Bathroom", 3.4, 12.0, 1.2, 1.2, "ceiling_stain", 80)
floor("Bathroom", 3.6, 11.6, 1.2, 1.2, "floor_stain", 0)

# Revision 3 north wing. Corridor: hall-like scuffs down the D1 sightline, damp on the long walls
wall("Corridor", "N", 6.0, 2.5, 0.8, 0.5, "mould_bloom")
wall("Corridor", "N", 13.4, 1.6, 0.7, 1.8, "leak_streaks")
wall("Corridor", "S", 6.0, 1.6, 0.8, 1.8, "peel_edge")
wall("Corridor", "S", 16.9, 2.45, 0.9, 0.6, "mould_bloom")
ceil("Corridor", 10.8, 15.9, 1.2, 1.2, "ceiling_stain", 40)
floor("Corridor", 9.25, 15.9, 0.9, 0.9, "scuffs", 90)
floor("Corridor", 4.0, 15.9, 0.9, 0.9, "scuffs", 270)
scatter("Corridor", 5, ["paper_scraps", "plaster_crumbs"], ((1.0, 19.0), (15.5, 16.3)), (0.3, 0.5), 16)

# Parlour (living-like): leaks round the north windows, grime band, ceiling stains, paper debris
wall("Parlour", "N", 9.5, 1.6, 0.8, 2.0, "leak_streaks")
wall("Parlour", "N", 14.0, 2.5, 1.0, 0.6, "mould_bloom")
wall("Parlour", "E", 24.0, 1.5, 0.9, 2.0, "leak_streaks")
wall("Parlour", "W", 18.8, 1.5, 0.8, 1.6, "peel_edge")
wall("Parlour", "S", 14.5, 2.45, 1.0, 0.6, "mould_bloom")
band("Parlour", "N", 0.45, 0.32, "grime_band", 2.0)
ceil("Parlour", 10.5, 24.0, 1.6, 1.6, "ceiling_stain", 30)
ceil("Parlour", 17.5, 19.0, 1.2, 1.2, "ceiling_stain", 200)
floor("Parlour", 16.2, 18.6, 1.1, 1.1, "floor_stain", 60)
floor("Parlour", 9.25, 18.2, 1.0, 1.0, "scuffs", 0)
scatter("Parlour", 10, ["paper_scraps", "paper_scraps", "plaster_crumbs"], ((8.6, 19.2), (17.4, 25.2)),
        (0.35, 0.6), 17)

# Master bedroom (bedroom-like): mould at the ceiling line, damp beside WM2
for side, along in (("N", 1.5), ("N", 6.5), ("W", 18.0), ("W", 24.0), ("E", 19.0), ("E", 24.2)):
    wall("MasterBedroom", side, along, 2.5, 1.0, 0.6, "mould_bloom")
wall("MasterBedroom", "W", 19.8, 1.5, 0.8, 2.0, "leak_streaks")
ceil("MasterBedroom", 2.0, 24.0, 1.6, 1.6, "ceiling_stain", 45)
ceil("MasterBedroom", 6.4, 18.4, 1.1, 1.1, "mould_bloom", 120)
floor("MasterBedroom", 4.0, 18.6, 1.0, 1.0, "scuffs", 0)
scatter("MasterBedroom", 5, ["paper_scraps", "plaster_crumbs"], ((0.9, 7.4), (17.6, 25.0)), (0.3, 0.55), 18)

# Basement: flood tide-line at ~0.5 m on every wall, grime under it, leaks and rust from the slab
for rid in ("B1", "B2"):
    for side in ("N", "S", "E", "W"):
        band(rid, side, 0.52, 0.26, "tide_line", 2.0)
        band(rid, side, 0.30, 0.16, "grime_band", 2.0)
wall("B1", "N", 8.9, 1.5, 0.9, 1.6, "leak_streaks")
wall("B1", "E", 6.0, 1.55, 0.7, 1.4, "rust_streaks")
wall("B1", "S", 11.5, 1.6, 0.9, 1.4, "leak_streaks")
wall("B2", "E", 6.4, 1.5, 0.9, 1.6, "leak_streaks")
wall("B2", "N", 17.5, 1.55, 0.7, 1.4, "rust_streaks")
wall("B2", "S", 15.0, 1.6, 0.9, 1.4, "leak_streaks")
wall("B2", "W", 7.5, 1.7, 0.8, 1.0, "mould_bloom")

# ------------------------------------------------------------------ lighting tuning (Phase 6 reference match)
# Per-fixture overrides on top of bungalow_layout.FIXTURES (which stays the SPEC 5.1 starting point):
# glow = shade emission strength (x the light colour, SPEC 5.1 "Emissive"); i / range / col replace the layout value.
# Phase 6 match: the SPEC colours are deep orange in linear light and pushed every frame orange-red under ACES, while
# the refs read as white-balanced tungsten (warm accents, near-neutral greys), so the colours are paler here and the
# intensities a little higher for the refs' soft fill.
# cone = a downward spot of that outer angle instead of a point light (ceiling domes and the fan's light kit): with the
# emitter 0.08-0.45 m under the ceiling a point light burnt a ring into it that blew out at the room exposures.
# Balance pass (SPEC 5.4): the lamps were cut where their pools / the wall next to them clipped (L2 3.0 -> 1.6,
# H4 1.8 -> 1.1, R1 2.2 -> 1.4), and KW1 raised 0.8 -> 1.2 as the moonlit backdrop of the living -> kitchen sightline.
FIXTURE_TUNING = {
    "E1": dict(glow=2.0, col="#F5E8CC", i=1.4, cone=150), "H1": dict(glow=1.2, col="#FFE4C0", i=1.6),
    "H2": dict(glow=1.5, col="#FFDDB0", i=0.45), "H3": dict(glow=1.5, col="#FFDDB0", i=0.45),
    "H4": dict(glow=1.8, col="#FFD9A8", i=1.1), "H5": dict(glow=0.0), "H6": dict(glow=1.5, col="#FFDDB0", i=0.35),
    "L1": dict(glow=3.0, col="#FFE6C4", i=1.5, cone=150), "L2": dict(glow=2.5, col="#FFEED8", i=1.6),
    "L3": dict(glow=2.0, col="#FFDDB4", i=0.6), "L4": dict(glow=1.2, col="#FFDDB4", i=0.4),
    "K1": dict(glow=2.0, cone=150), "K2": dict(glow=2.0, cone=150), "R1": dict(glow=1.6, col="#F5E6C8", i=1.4),
    "R2": dict(glow=1.0, i=0.8), "RW1": dict(col="#B8BCB0", i=3.0), "KW1": dict(i=1.2),
    "W1": dict(glow=1.5, fizz=0.35, i=1.0), "WW1": dict(i=0.9), "CW2": dict(i=0.8),
    "C1": dict(glow=6.0), "C2": dict(glow=6.0, i=1.4),
    # revision 3: copied from the matching ground-floor fixture (dome E1, chandelier H1, lamp L2 / R2, fan L1, RW1)
    "CO1": dict(glow=2.0, col="#F5E8CC"), "CO2": dict(glow=1.6, col="#F5E8CC"), "CO3": dict(glow=0.0),
    "P1": dict(glow=1.2, col="#FFE4C0", i=1.6), "P2": dict(glow=2.5, col="#FFEED8", i=2.4),
    "M1": dict(glow=3.0, col="#FFE6C4", i=1.3), "M2": dict(glow=1.0, i=0.8),
    "MW1": dict(col="#B8BCB0", i=2.2), "MW2": dict(col="#B8BCB0", i=1.7), "PW1": dict(i=0.9),
    "CW3": dict(i=0.8), "CW4": dict(i=0.8),
}
# Global look (SPEC 5.2). fog: Exponential / ExponentialSquared.
# fog 0.11 (SPEC start 0.15): a zombie must still read as a silhouette 10-15 m down the SPEC 1.6 sightlines
# sky = what the cameras clear to = what shows through the windows. NOT the fog colour: the room volumes expose the
# frame up 1.4-2.45 stops, which turned the olive fog colour into a bright mustard pane. Very dark cool blue instead.
GLOBAL = dict(ambient="#0A0B08", fog="#141408", fogMode="Exponential", fogDensity=0.11, sky="#04060B",
              # Blink/Sky colours (raw linear floats) NightSky applies at runtime, where the camera is Skybox, not
              # SolidColor. The prefab defaults (horizon .045/.06/.095, ground = fog colour, bright moon) turned into a
              # bright pale pane once the room volumes added 1.4-2.45 EV; these are ~10x darker and cool.
              skyZenith=(0.002, 0.004, 0.012), skyHorizon=(0.012, 0.022, 0.050),
              skyGround=(0.003, 0.005, 0.012), moon=(0.05, 0.065, 0.1), stars=0.35)


def post(rooms=(), exposure=0.0, filt="#FFFFFF", sat=0.0, contrast=0.0, temp=0.0, tint=0.0, vignette=-1.0,
         ca=-1.0, lift=None, split_s="", split_h="", bloom=-1.0, threshold=-1.0, scatter=-1.0, bloom_tint=""):
    """One volume's grade; -1 / "" / None = leave that effect to the global volume."""
    return dict(rooms=list(rooms), exposure=exposure, filter=filt, saturation=sat, contrast=contrast,
                temperature=temp, tint=tint, vignette=vignette, ca=ca, lift=list(lift) if lift else [],
                splitShadows=split_s, splitHighlights=split_h, bloom=bloom, threshold=threshold, scatter=scatter,
                bloomTint=bloom_tint)


POST_GLOBAL = post(exposure=1.5, contrast=10, sat=-18, vignette=0.30, ca=0.06, lift=(1.0, 0.99, 0.97, 0.0),
                   bloom=0.35, threshold=0.8, scatter=0.65, bloom_tint="#FFD9A0")
# SPEC 5.2 local room volumes (box = the room rectangles, blend 1 m, priority 1). Re-tuned in Phase 6 against the refs:
# ACES alone takes ~1.4 stops off these dark frames, so exposures are positive; the practicals are already warm, so the
# filters / white balance pull towards the refs' fairly neutral greys instead of adding more orange.
POST_ROOMS = {
    "Hall": post(("EntranceHall", "CentralHall", "Corridor"), 1.4, "#E8E8E4", -40, 8, temp=-16, vignette=0.34),
    "Living": post(("Living",), 2.3, "#E6E4DE", -38, 14, temp=-16, vignette=0.36, ca=0.12, split_s="#3A2418",
                   split_h="#FFF0DC"),
    "Bedroom": post(("Bedroom",), 2.45, "#ECE8DC", -30, -6, temp=4, tint=8, vignette=0.28, lift=(1.0, 1.0, 0.98, 0.0)),
    # revision 3: parlour = living grade a touch cooler; master = bedroom grade a touch darker
    "Parlour": post(("Parlour",), 2.2, "#E4E4E0", -40, 14, temp=-20, vignette=0.38, ca=0.12, split_s="#30241C",
                    split_h="#FFF0DC"),
    "MasterBedroom": post(("MasterBedroom",), 2.3, "#E8E6DC", -32, -4, temp=0, tint=6, vignette=0.32,
                          lift=(1.0, 1.0, 0.98, 0.0)),
    "Kitchen": post(("Kitchen",), 1.4, "#E4E9E6", -30, 0, temp=-10),
    "Bathroom": post(("Bathroom",), 1.7, "#D9E4D6", -35, 0, temp=-8, tint=-6),
    "Basement": post(("B1", "B2"), 1.6, "#D8D8C0", -40, 10, vignette=0.4),
}


def tuning():
    fx = []
    for fid, t in FIXTURE_TUNING.items():
        fx.append(dict(id=fid, glow=t.get("glow", 1.0), fizz=t.get("fizz", 0.0), i=t.get("i", -1.0),
                       range=t.get("range", -1.0), col=t.get("col", ""), cone=t.get("cone", 0.0)))
    vols = [dict(name=k, **v) for k, v in POST_ROOMS.items()]
    return dict(fixtures=fx, globals=GLOBAL, post=dict(name="Global", **POST_GLOBAL), rooms=vols)


# ------------------------------------------------------------------ other atmosphere data


def area_lights():
    faces = {"+X": (1, 0, 0), "-X": (-1, 0, 0), "+Z": (0, 0, 1), "-Z": (0, 0, -1)}
    out = []
    for fid, f in L.FIXTURES.items():
        if f["kind"] == "area":
            out.append(dict(id=fid, size=list(f.get("size", (1.6, 1.5))), face=list(faces[f.get("face", "+Z")])))
    return out


def stair_well():
    hole = L.ROOMS["CentralHall"]["floor_holes"][0]
    return dict(id=hole["id"], min=[hole["x"][0], L.BASEMENT, hole["z"][0]], max=[hole["x"][1], L.CEILING, hole["z"][1]])


def to_json():
    return dict(revision=REVISION, layoutRevision=L.REVISION, decals=DECALS, areaLights=area_lights(),
                stairWell=stair_well(), water=dict(y=WATER_Y, rooms=["B1", "B2"]), tuning=tuning())


if __name__ == "__main__":
    if _errors:
        print("\n".join(_errors))
        sys.exit("bungalow_decals: %d placement error(s)" % len(_errors))
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with open(OUT_JSON, "w", encoding="utf-8") as fh:
        json.dump(to_json(), fh, indent=1)
    counts = {}
    for d in DECALS:
        counts[d["cell"]] = counts.get(d["cell"], 0) + 1
    print("wrote", OUT_JSON, "decals", len(DECALS), counts)
