"""BLINK Bungalow (Phase 5): prop and light-fixture placement, the single table the Unity placer reads.

Plain Python (no bpy). Reads bungalow_layout (Phase 3, read-only) for rooms, fixtures and walls, and
Props/bungalow_prop_models.json (written by bungalow_prop_build.py) for each model's top / shelf heights and
fixture emitter offsets.

  python -I tools/blender/bungalow_props.py
    -> Unity/Assets/Art/Environment/Bungalow/Props/bungalow_props.json   every instance: prefab, room, pos, euler
    -> Unity/Assets/Art/Environment/Bungalow/Props/fixtures.json         every fixture: id, prefab, mount, yaw,
                                                                          emitter local offset + world position
Coordinates: Unity world metres (bungalow_layout). yaw = Unity Y rotation; a model's front faces +Z at yaw 0.
Pivots (see bungalow_prop_models): floor items at the footprint centre, wall items at the wall surface, ceiling
fixtures at the ceiling, "top" items at their base. on=(parent id, level) puts an item on a parent's surface
(level None = the parent's top; a number = one of its shelf heights).
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bungalow_layout as L  # noqa: E402  (read-only)

PROPS = os.path.join(os.path.dirname(L.OUT_JSON), "Props")
MODELS_JSON = os.path.join(PROPS, "bungalow_prop_models.json")
OUT = os.path.join(PROPS, "bungalow_props.json")
OUT_FIX = os.path.join(PROPS, "fixtures.json")
B = L.BASEMENT                     # basement floor y
WATER = L.DECAY["water_y"]         # basement water surface
CEIL = {"ground": L.CEILING, "basement": -L.SLAB}

# wall faces (room-side surface) used by the wall items
X_BED_W, X_HALL_W, X_HALL_E, X_LIV_W, X_E = 0.3, 7.075, 11.425, 11.575, 19.7
Z_S, Z_N, Z_LIV_N, Z_BATH_S = 0.3, 14.7, 6.925, 9.575
# revision 3: corridor S / N faces, the wing's S face (master + parlour), the wing's N face, master E / parlour W
Z_COR_S, Z_COR_N, Z_WING_S, Z_WING_N = 15.0, 16.8, 16.95, 25.7
X_MBR_E, X_PAR_W = 7.925, 8.075

# id: (model, room, (x, y, z), yaw, extra) ; extra: on=(parent, level), rot=(x, y, z) full euler, scale
P = {}


def put(i, model, room, x, z, yaw=0.0, y=None, on=None, rot=None, scale=None, note=""):
    P[i] = dict(model=model, room=room, x=x, y=y, z=z, yaw=yaw, on=on, rot=rot, scale=scale, note=note)


def wall(i, model, room, face, axis, a, y, note=""):
    """Wall item: axis 'x' = wall runs along x at z = face; 'z' = runs along z at x = face. Faces the room."""
    if axis == "x":
        put(i, model, room, a, face, 0.0 if face in (Z_S, Z_BATH_S, Z_COR_S, Z_WING_S) else 180.0, y=y, note=note)
    else:
        yaw = 90.0 if face in (X_BED_W, X_HALL_W, X_LIV_W, X_PAR_W) else 270.0
        put(i, model, room, face, a, yaw, y=y, note=note)


def copy_room(src, dst, prefix, skip=(), near=1.2):
    """Revision 3: clone room src's rows into room dst (then alter the copies by hand). Per axis, a coordinate within
    `near` of src's min / max wall keeps its offset from dst's min / max wall, anything else maps proportionally, so
    wall items stay on their walls. Ids swap their room prefix for `prefix`; an on=(parent, level) item follows its
    renamed parent at the same offset. yaw / rot / scale / y are kept."""
    s, d = L.ROOMS[src], L.ROOMS[dst]

    def m(v, ax):
        (a0, a1), (b0, b1) = s[ax], d[ax]
        if v - a0 <= near:
            return b0 + v - a0
        if a1 - v <= near:
            return b1 - (a1 - v)
        return b0 + (v - a0) / (a1 - a0) * (b1 - b0)

    def ren(i):
        return prefix + i.split("_", 1)[1]
    for i, p in list(P.items()):
        if p["room"] != src or i in skip:
            continue
        q = dict(p, room=dst, x=m(p["x"], "x"), z=m(p["z"], "z"))
        if p["on"]:
            par, new = P[p["on"][0]], P[ren(p["on"][0])]
            q.update(on=(ren(p["on"][0]), p["on"][1]), x=new["x"] + p["x"] - par["x"], z=new["z"] + p["z"] - par["z"])
        P[ren(i)] = q


def alter(i, **kw):
    P[i].update(kw)


# ------------------------------------------------------------------ BEDROOM (ref 3)
put("Bed_Bed", "GothicBed_01", "Bedroom", 3.4, 8.40, 180, note="hero; P0 wakes beside its east side")
put("Bed_Blanket", "Bed_Blanket", "Bedroom", 3.4, 7.92, 180, on=("Bed_Bed", 0.56))
put("Bed_Nightstand", "ClassicNightstand_01", "Bedroom", 4.6, 9.10, 180)
put("Bed_Dresser", "GothicCommode_01", "Bedroom", 5.59, 9.13, 180, note="chest of drawers beside the bed: key + note on top (Editor/BungalowStartBedroom.cs)")
put("Bed_DresserVase", "ceramic_vase_01", "Bedroom", 6.02, 9.15, 160, on=("Bed_Dresser", None))
put("Bed_Wardrobe", "Wardrobe_Painted", "Bedroom", 0.60, 6.80, 90, note="second grouping")
put("Bed_Suitcases", "vintage_suitcase", "Bedroom", 0.62, 6.80, 90, on=("Bed_Wardrobe", None))
put("Bed_Settee", "Settee_Plaid", "Bedroom", 0.72, 3.00, 90, note="under WR1 (ref 3)")
put("Bed_CoffeeTable", "WoodenTable_01", "Bedroom", 1.80, 3.00, 90, note="low wooden coffee table (ref 3)")
put("Bed_Rug", "Rug_Geo", "Bedroom", 1.95, 3.00, 90, note="black-and-white rug (ref 3)")
put("Bed_TablePaper", "Debris_Paper", "Bedroom", 1.85, 3.1, 30, on=("Bed_CoffeeTable", None), scale=(0.45, 1, 0.45))
put("Bed_Rocker", "Rockingchair_01", "Bedroom", 3.85, 2.05, 215, note="in front of the fireplace (ref 3)")
put("Bed_MantelClock", "mantel_clock_01", "Bedroom", 3.4, 0.80, 0, y=L.FIREPLACE["mantel"]["y"][1])
put("Bed_MantelOrnaments", "MantelClutter", "Bedroom", 3.4, 0.76, 0, y=L.FIREPLACE["mantel"]["y"][1])
put("Bed_MantelVase", "ceramic_vase_03", "Bedroom", 2.42, 0.78, 0, y=L.FIREPLACE["mantel"]["y"][1])
wall("Bed_Picture", "Picture_Wide", "Bedroom", Z_S, "x", 1.30, 1.75)
put("Bed_CornerTable", "side_table_tall_01", "Bedroom", 0.80, 1.00, 0, note="R1 lamp table")
wall("Bed_Curtains", "Curtains_Window", "Bedroom", X_BED_W, "z", 3.0, 2.48)
put("Bed_ReadingChair", "GreenChair_01", "Bedroom", 1.45, 5.00, 120, note="second grouping")
put("Bed_BedsideRug", "Rug_OrientalSmall", "Bedroom", 3.4, 6.55, 0)
put("Bed_Shoes", "Shoes_Pair", "Bedroom", 2.35, 7.55, 30)
put("Bed_Paper", "Debris_Paper", "Bedroom", 5.0, 4.4, 15)
put("Bed_Plaster", "Debris_Plaster", "Bedroom", 6.45, 6.2, 90)
put("Bed_Bookshelf", "wooden_bookshelf_worn", "Bedroom", 6.925 - 0.30, 4.6, 270, note="second grouping (E wall)")

# ------------------------------------------------------------------ LIVING (ref 2)
put("Liv_Rug", "Rug_Oriental", "Living", 14.35, 3.75, 297)
put("Liv_Sofa", "Sofa_01", "Living", 15.2, 3.9, 297, note="back to CAM-2")
put("Liv_Pillows", "throw_pillows_01", "Living", 15.22, 3.86, 297, on=("Liv_Sofa", 0.38))
put("Liv_CoffeeTable", "CoffeeTable_Glass", "Living", 14.30, 4.33, 297)
put("Liv_CoffeeClutter", "Clutter_CoffeeTable", "Living", 14.30, 4.33, 297, y=0.0)
put("Liv_Armchair", "ArmChair_01", "Living", 13.0, 2.55, 55)
put("Liv_EndTable", "ClassicNightstand_01", "Living", 15.78, 5.15, 297, note="L2 lamp")
put("Liv_Sideboard", "GothicCommode_01", "Living", 13.4, 0.60, 0)
put("Liv_SideboardVase", "ceramic_vase_01", "Living", 13.0, 0.58, 20, on=("Liv_Sideboard", None))
put("Liv_SideboardBox", "BoxFallen", "Living", 13.75, 0.6, 85, on=("Liv_Sideboard", None), scale=(0.7, 0.7, 0.7))
wall("Liv_Portrait", "Picture_Oval", "Living", Z_S, "x", 13.4, 1.88, note="oval portrait (ref 2)")
put("Liv_SideTable", "small_wooden_table_01", "Living", 17.40, 0.55, 0, note="ref 2 foreground table")
put("Liv_SideTableClutter", "Clutter_SideTable", "Living", 17.40, 0.55, 0, on=("Liv_SideTable", None))
put("Liv_Speaker", "Speaker", "Living", 14.6, 0.48, 8)
put("Liv_Boxes", "BoxStack", "Living", 15.6, 0.62, 4)
wall("Liv_Painting", "Picture_Landscape", "Living", X_LIV_W, "z", 6.0, 1.72, note="landscape (ref 2)")
put("Liv_ReadChair", "ArmChair_01", "Living", 18.30, 4.85, 250, note="reading corner (second grouping)")
put("Liv_ReadTable", "ClassicNightstand_01", "Living", 19.0, 5.50, 250)
put("Liv_ReadVase", "ceramic_vase_03", "Living", 19.0, 5.50, 0, on=("Liv_ReadTable", None))
put("Liv_Paper", "Debris_Paper", "Living", 16.9, 2.3, 30)
put("Liv_Paper2", "Debris_Paper", "Living", 12.6, 4.7, 200)
put("Liv_Plaster", "Debris_Plaster", "Living", 12.05, 0.75, 0)
put("Liv_Plaster2", "Debris_Plaster", "Living", 11.95, 4.4, 90)
put("Liv_FallenBox", "BoxFallen", "Living", 16.6, 5.75, 40)

# ------------------------------------------------------------------ ENTRANCE HALL and CENTRAL HALL (ref 1)
put("Ent_Runner", "Runner_Entrance", "EntranceHall", 9.25, 2.15, 90)
put("Ent_CoatStand", "CoatStand", "EntranceHall", 7.42, 3.55, 135)
put("Ent_Shoes", "Shoes_Pair", "EntranceHall", 7.38, 3.0, 80)
put("Ent_Umbrellas", "UmbrellaStand", "EntranceHall", 10.95, 0.62, 0)
put("Ent_FallenBox", "BoxFallen", "EntranceHall", 10.6, 3.35, 30, note="the fallen box")
put("Ent_Paper", "Debris_Paper", "EntranceHall", 8.1, 1.7, 0)
wall("Ent_WallClock", "wall_clock", "EntranceHall", X_HALL_E, "z", 0.75, 1.95)

put("Hal_Runner", "Runner_Hall", "CentralHall", 9.9, 7.4, 90)
put("Hal_Bench", "painted_wooden_bench", "CentralHall", 11.17, 5.30, 270, note="hall bench")
put("Hal_Plant", "PlantPot", "CentralHall", 10.95, 7.75, 20, note="potted plant (ref 1)")
put("Hal_Console", "ClassicConsole_01", "CentralHall", 11.13, 13.80, 270, note="H4 lamp")
put("Hal_ConsoleVase", "ceramic_vase_01", "CentralHall", 11.18, 13.2, 0, on=("Hal_Console", None))
put("Hal_Clock", "vintage_grandfather_clock_01", "CentralHall", 7.30, 11.35, 90, note="north landing")
put("Hal_Chair", "painted_wooden_chair_01", "CentralHall", 7.62, 14.12, 135, note="north landing chair")
wall("Hal_Picture", "Picture_Hall", "CentralHall", X_HALL_W, "z", 13.85, 1.72, note="over the chair (ref 1); DC1 took the N wall")
put("Hal_LandingRug", "Rug_OrientalSmall", "CentralHall", 9.6, 12.4, 0)
put("Hal_Plaster", "Debris_Plaster", "CentralHall", 10.8, 11.3, 30)
put("Hal_Paper", "Debris_Paper", "CentralHall", 10.6, 4.8, 120)

# ------------------------------------------------------------------ KITCHEN
put("Kit_Cabinet1", "painted_wooden_cabinet", "Kitchen", 12.40, Z_N - 0.31, 180)
put("Kit_Sink", "SinkUnit", "Kitchen", 14.50, Z_N - 0.31, 180, note="under WK1")
put("Kit_Cabinet2", "painted_wooden_cabinet", "Kitchen", 15.80, Z_N - 0.31, 180)
put("Kit_Fridge", "Fridge_Retro", "Kitchen", 16.95, Z_N - 0.38, 180)
put("Kit_Dishes", "Dishes_Stack", "Kitchen", 12.4, 14.4, 180, on=("Kit_Cabinet1", None))
put("Kit_Dishes2", "Dishes_Stack", "Kitchen", 15.8, 14.45, 170, on=("Kit_Cabinet2", None))
put("Kit_Stove", "electric_stove", "Kitchen", X_E - 0.33, 7.80, 270)
put("Kit_Pans", "Pans_Stove", "Kitchen", X_E - 0.35, 7.80, 270, on=("Kit_Stove", None))
put("Kit_Table", "KitchenTable", "Kitchen", 14.7, 10.8, 0)
put("Kit_Chair1", "painted_wooden_chair_01", "Kitchen", 14.3, 10.05, 0)
put("Kit_Chair2", "painted_wooden_chair_01", "Kitchen", 15.1, 11.58, 175)
put("Kit_Chair3", "painted_wooden_chair_01", "Kitchen", 13.58, 10.95, 80)
put("Kit_ChairDown", "KnockedChair", "Kitchen", 16.15, 11.3, 60, note="knocked over")
put("Kit_TableDishes", "Dishes_Stack", "Kitchen", 14.5, 10.9, 30, on=("Kit_Table", None))
put("Kit_Dresser", "painted_wooden_cabinet_02", "Kitchen", X_LIV_W + 0.37, 12.25, 90, note="dresser (W wall)")
put("Kit_DresserDishes", "Dishes_Stack", "Kitchen", X_LIV_W + 0.33, 12.25, 90, on=("Kit_Dresser", 0.92))
wall("Kit_WallClock", "wall_clock", "Kitchen", X_LIV_W, "z", 13.6, 2.05)
put("Kit_Bucket", "wooden_bucket_01", "Kitchen", 17.6, 14.25, 20, note="rev 3: clear of DC4")
put("Kit_Trash", "TrashBags", "Kitchen", 19.1, 8.95, 270)
put("Kit_DebrisDishes", "Debris_Dishes", "Kitchen", 15.6, 9.4, 0)
put("Kit_Paper", "Debris_Paper", "Kitchen", 17.4, 10.2, 70)
put("Kit_Plaster", "Debris_Plaster", "Kitchen", 12.6, 8.0, 0)

# ------------------------------------------------------------------ BATHROOM
put("Bath_Tub", "ClawfootTub", "Bathroom", 1.27, Z_BATH_S + 0.42, 0)
put("Bath_Toilet", "Toilet_Old", "Bathroom", X_BED_W + 0.32, 13.1, 90)
put("Bath_Sink", "PedestalSink", "Bathroom", 4.4, Z_N - 0.04, 180)
wall("Bath_Mirror", "MirrorCabinet", "Bathroom", Z_N, "x", 4.4, 1.5)
wall("Bath_Towel", "Towel_Rail", "Bathroom", Z_BATH_S, "x", 3.0, 1.1)
put("Bath_Bucket", "wooden_bucket_01", "Bathroom", 2.45, 10.05, 140)
put("Bath_Chair", "painted_wooden_chair_01", "Bathroom", X_BED_W + 0.30, 12.6, 90,
    note="P2 Lily's doll sits here, facing D3 (Editor/PuzzleProps.cs moves it here too)")
put("Bath_Plaster", "Debris_Plaster", "Bathroom", 3.8, 13.6, 0)

# ------------------------------------------------------------------ BASEMENT (B1 cellar, B2 workshop)
put("B1_Shelves", "steel_frame_shelves_01", "B1", 10.0, 4.075 + 0.27, 0, y=B)
for k, lv in enumerate((0.64, 1.65)):
    put(f"B1_Jars{k}", "Jars_Shelf", "B1", 10.0, 4.36, 0, on=("B1_Shelves", lv))
put("B1_Crate", "wooden_crate_01", "B1", 12.6, 4.6, 0, y=B)
put("B1_Crate2", "wooden_crate_01", "B1", 12.62, 4.6, 8, on=("B1_Crate", None))
put("B1_Barrel", "barrel_03", "B1", 12.9, 5.85, 0, y=B)
put("B1_CrateLong", "wooden_crate_02", "B1", 12.8, 14.2, 0, y=B)
put("B1_Trash", "TrashBags", "B1", 11.55, 14.3, 180, y=B)
put("B1_Float", "Debris_PaperFloat", "B1", 10.4, 9.0, 0, y=WATER + 0.002)
put("B1_Boxes", "BoxStack", "B1", 9.7, 14.25, 180, y=B)
put("B1_CrateLong2", "wooden_crate_02", "B1", 11.2, 4.4, 0, y=B)
put("B1_Barrel2", "barrel_03", "B1", 13.0, 9.2, 0, y=B)
put("B1_FallenBox", "BoxFallen", "B1", 9.6, 7.4, 110, y=B)
put("B1_Bucket", "wooden_bucket_01", "B1", 12.9, 10.0, 200, y=B)
put("B2_Generator", "portable_generator", "B2", 16.6, 12.2, 0, y=B, note="hero, story P7")
put("B2_Jerrycan", "metal_jerrycan_green", "B2", 17.35, 12.55, 30, y=B)
wall("B2_PowerBox", "power_box_01", "B2", X_E, "z", 12.2, B + 1.45)
put("B2_Workbench", "Workbench", "B2", X_E - 0.32, 6.3, 270, y=B)
put("B2_Vice", "bench_vice_01", "B2", X_E - 0.3, 5.55, 270, on=("B2_Workbench", 0.9))
put("B2_Shelves", "steel_frame_shelves_01", "B2", 15.0, 4.075 + 0.27, 0, y=B)
for k, lv in enumerate((0.13, 1.15)):
    put(f"B2_Jars{k}", "Jars_Shelf", "B2", 15.0, 4.36, 0, on=("B2_Shelves", lv))
put("B2_Boxes", "BoxStack", "B2", 17.8, 4.5, 0, y=B)
put("B2_Crate", "wooden_crate_01", "B2", 16.3, 4.35, 0, y=B)
put("B2_Crate2", "wooden_crate_01", "B2", 16.32, 4.35, -6, on=("B2_Crate", None))
put("B2_Barrel", "barrel_03", "B2", 19.2, 8.4, 0, y=B)
put("B2_Bucket", "wooden_bucket_01", "B2", 14.2, 8.0, 60, y=B)
put("B2_Trash", "TrashBags", "B2", 14.3, 14.2, 180, y=B)
put("B2_Float", "Debris_PaperFloat", "B2", 16.0, 8.4, 120, y=WATER + 0.002)

# ------------------------------------------------------------------ REVISION 3: NORTH WING (copied rooms, then altered)
# Parlour = the living room copied and re-arranged round P1; Master bedroom = the bedroom without its fireplace.
copy_room("Living", "Parlour", "Par_", skip=("Liv_Boxes", "Liv_ReadTable", "Liv_ReadVase"))
# sofa group turned to face south under P1, inside the R6 patrol loop (x 10.5-16, z 19-23)
for i, x, z, yaw in (("Par_Rug", 13.6, 21.0, 0), ("Par_Sofa", 13.6, 22.12, 180), ("Par_CoffeeTable", 13.6, 21.2, 0),
                     ("Par_CoffeeClutter", 13.6, 21.2, 0), ("Par_Armchair", 15.15, 21.0, 270)):
    alter(i, x=x, z=z, yaw=yaw)
alter("Par_Pillows", x=13.62, z=22.16, yaw=180)
put("Par_Chair2", "GreenChair_01", "Parlour", 12.0, 21.0, 90, note="swapped for the living's second armchair")
alter("Par_Sideboard", model="ClassicConsole_01", x=11.2, z=Z_WING_S + 0.30, note="console sideboard")
alter("Par_SideboardVase", x=10.8, z=17.25)
alter("Par_SideboardBox", x=11.6, z=17.27)
alter("Par_Portrait", x=11.2)
alter("Par_Speaker", x=12.6)
alter("Par_Painting", model="Picture_Wide", z=24.4)
# reading corner under the boarded WP2: end table (P2 lamp) + chair
alter("Par_EndTable", x=17.2, z=Z_WING_N - 0.25, yaw=180, note="P2 lamp")
alter("Par_ReadChair", model="painted_wooden_chair_01", x=18.15, z=24.75, yaw=225)
alter("Par_Plaster", x=8.6, z=19.4)
wall("Par_Curtains", "Curtains_Window", "Parlour", Z_WING_N, "x", 11.8, 2.48)
put("Par_ChairDown", "KnockedChair", "Parlour", 9.3, 24.3, 120, note="knocked over")

copy_room("Bedroom", "MasterBedroom", "Mbr_",
          skip=("Bed_MantelClock", "Bed_MantelOrnaments", "Bed_MantelVase", "Bed_Rocker", "Bed_Curtains",
                "Bed_TablePaper", "Bed_CornerTable"))
# bed moved east so WM1 lights its west side; dresser + bookshelf on the E wall either side of AC1
alter("Mbr_Bed", x=5.95)
alter("Mbr_Blanket", x=5.95)
alter("Mbr_Nightstand", x=4.75, z=Z_WING_N - 0.25, note="M2 lamp")
alter("Mbr_Dresser", x=X_MBR_E - 0.30, z=18.75, yaw=270)
alter("Mbr_DresserVase", x=X_MBR_E - 0.32, z=18.4)
alter("Mbr_Bookshelf", x=X_MBR_E - 0.30, z=24.75)
# settee group under WM2 (was under WR1), rug swapped for the big oriental
for i in ("Mbr_Settee", "Mbr_CoffeeTable", "Mbr_Rug"):
    alter(i, z=21.3)
alter("Mbr_Rug", model="Rug_Oriental", x=2.3)
alter("Mbr_ReadingChair", x=1.2, z=24.9, yaw=135)
alter("Mbr_BedsideRug", x=5.95, z=23.05)
alter("Mbr_Shoes", x=4.55, z=23.5)
alter("Mbr_Picture", model="Picture_Oval", x=1.3, y=1.85)
wall("Mbr_Curtains", "Curtains_Window", "MasterBedroom", Z_WING_N, "x", 3.8, 2.48)
wall("Mbr_Curtains2", "Curtains_Window", "MasterBedroom", X_BED_W, "z", 21.3, 2.48)
put("Mbr_FallenBox", "BoxFallen", "MasterBedroom", 2.6, 24.6, 140)

# ------------------------------------------------------------------ CORRIDOR (1.2+ m walk clear the whole length)
put("Cor_Runner", "Runner_Hall", "Corridor", 13.0, 15.9, 0)
put("Cor_Bench", "painted_wooden_bench", "Corridor", 6.6, Z_COR_N - 0.25, 180)
put("Cor_Shoes", "Shoes_Pair", "Corridor", 6.25, 16.35, 200)
wall("Cor_Picture", "Picture_Wide", "Corridor", Z_COR_N, "x", 6.6, 1.75)
wall("Cor_Portrait", "Picture_Oval", "Corridor", Z_COR_S, "x", 11.5, 1.85)
wall("Cor_Portrait2", "Picture_Oval", "Corridor", Z_COR_N, "x", 14.6, 1.85)
put("Cor_Boxes", "BoxStack", "Corridor", 16.75, Z_COR_S + 0.29, 0)
put("Cor_FallenBox", "BoxFallen", "Corridor", 11.0, 16.3, 30)
put("Cor_Paper", "Debris_Paper", "Corridor", 3.0, 15.6, 60)
put("Cor_Plaster", "Debris_Plaster", "Corridor", 12.9, 16.45, 0)
put("Cor_Paper2", "Debris_Paper", "Corridor", 17.6, 16.0, 200)

# ------------------------------------------------------------------ FIXTURES (SPEC 5.1 / bungalow_layout.FIXTURES)
# id: (model, mount kind, parent or wall, yaw). Ceiling: mount at the ceiling over the light. Wall: on the wall
# face behind the light. Lamp: on the parent's top, under the light.
FIX = {
    "E1": ("FX_Dome", "ceiling", "ground", 0), "K1": ("FX_Dome", "ceiling", "ground", 0),
    "K2": ("FX_Dome", "ceiling", "ground", 0),
    "H1": ("FX_Chandelier3", "ceiling", "ground", 0), "L1": ("FX_CeilingFan", "ceiling", "ground", 20),
    "C1": ("FX_BulbPendant", "ceiling", "basement", 0), "C2": ("FX_BulbPendant", "ceiling", "basement", 0),
    "H2": ("FX_SconceBrass", "wall", ("x", Z_N), 180), "H3": ("FX_SconceBrass", "wall", ("x", Z_N), 180),
    "W1": ("FX_SconceBrass", "wall", ("x", Z_N), 180),
    "H5": ("FX_SconceBroken", "wall", ("z", X_HALL_W), 90), "H6": ("FX_SconceBrass", "wall", ("z", X_HALL_E), 270),
    "L3": ("FX_SconceTulip", "wall", ("z", X_LIV_W), 90), "L4": ("FX_SconceTulip", "wall", ("x", Z_LIV_N), 180),
    "L2": ("FX_TableLamp", "lamp", "Liv_EndTable", 0), "H4": ("FX_TableLamp", "lamp", "Hal_Console", 0),
    "R1": ("FX_TableLamp", "lamp", "Bed_CornerTable", 0), "R2": ("FX_TableLampSmall", "lamp", "Bed_Nightstand", 0),
    # revision 3
    "CO1": ("FX_Dome", "ceiling", "ground", 0), "CO2": ("FX_Dome", "ceiling", "ground", 0),
    "CO3": ("FX_SconceBroken", "wall", ("x", Z_COR_N), 180),
    "P1": ("FX_Chandelier3", "ceiling", "ground", 0), "P2": ("FX_TableLamp", "lamp", "Par_EndTable", 0),
    "M1": ("FX_CeilingFan", "ceiling", "ground", 70), "M2": ("FX_TableLampSmall", "lamp", "Mbr_Nightstand", 0),
}


def rot_y(v, yaw):
    a = math.radians(yaw)
    x, y, z = v
    return (x * math.cos(a) + z * math.sin(a), y, -x * math.sin(a) + z * math.cos(a))


def resolve():
    with open(MODELS_JSON, encoding="utf-8") as f:
        models = json.load(f)["models"]
    out = []
    pos = {}

    def floor_y(room):
        return B if L.ROOMS[room]["level"] == "basement" else 0.0
    for i, p in P.items():
        m = models[p["model"]]
        if p["on"]:
            par, lv = p["on"]
            pm = models[P[par]["model"]]
            y = pos[par][1] + (lv if lv is not None else pm["top"])
        elif p["y"] is not None:
            y = p["y"]
        else:
            y = floor_y(p["room"])
        pos[i] = (p["x"], y, p["z"])
        e = dict(id=i, prefab=p["model"], room=p["room"], pos=[round(p["x"], 4), round(y, 4), round(p["z"], 4)],
                 rot=list(p["rot"]) if p["rot"] else [0.0, float(p["yaw"]), 0.0],
                 scale=list(p["scale"]) if p["scale"] else [1.0, 1.0, 1.0], kind=m["kind"], tris=m["tris"],
                 fixture="", note=p["note"])
        out.append(e)
    fixtures = []
    for fid, (model, mk, ref, yaw) in FIX.items():
        f = L.FIXTURES[fid]
        m = models[model]
        off = m["emitter"]
        lx, ly, lz = f["pos"]
        if mk == "ceiling":
            mount = (lx, CEIL[ref], lz)
        elif mk == "wall":
            axis, face = ref
            ro = rot_y(off, yaw)
            mount = (lx, ly - off[1], face) if axis == "x" else (face, ly - off[1], lz)
            del ro
        else:
            px, py, pz = pos[ref]
            mount = (px, py + models[P[ref]["model"]]["top"], pz)
        r = rot_y(off, yaw)
        world = [round(mount[0] + r[0], 4), round(mount[1] + r[1], 4), round(mount[2] + r[2], 4)]
        dev = math.dist(world, f["pos"])
        room = next((k for k, rm in L.ROOMS.items() if rm["x"][0] - 0.2 <= lx <= rm["x"][1] + 0.2 and
                     rm["z"][0] - 0.2 <= lz <= rm["z"][1] + 0.2 and
                     (rm["level"] == "basement") == (ly < -0.2)), "")
        inst = "FX_" + fid
        out.append(dict(id=inst, prefab=model, room=room, pos=[round(c, 4) for c in mount], rot=[0.0, float(yaw), 0.0],
                        scale=[1.0, 1.0, 1.0], kind="fixture", tris=m["tris"], fixture=fid, note=f["kind"]))
        fixtures.append(dict(id=fid, instance=inst, prefab=model, kind=f["kind"], mount=[round(c, 4) for c in mount],
                             yaw=float(yaw), emitterLocal=off, emitterWorld=world, layoutLight=list(f["pos"]),
                             deviation=round(dev, 3), emitterChild=model + "_Emitter",
                             emissiveChild=m.get("emissive", ""), off=bool(f.get("off")), mode=f["mode"]))
    return out, fixtures, models


def main():
    props, fixtures, models = resolve()
    os.makedirs(PROPS, exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(source="tools/blender/bungalow_props.py", units="m, Unity world", props=props), f, indent=1)
    with open(OUT_FIX, "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(source="tools/blender/bungalow_props.py",
                       note="emitterWorld = where the light goes (the <Prefab>_Emitter child of instance FX_<id>); "
                            "the shade / glass / bulb is the <Prefab>_Emissive child (M_Shade)", fixtures=fixtures),
                  f, indent=1)
    rooms = {}
    for p in props:
        rooms.setdefault(p["room"] or "?", [0, 0])
        rooms[p["room"] or "?"][0] += p["tris"]
        rooms[p["room"] or "?"][1] += 1
    tot = sum(v[0] for v in rooms.values())
    for k, (t, n) in sorted(rooms.items()):
        print(f"room {k:<13} {n:3d} instances {t:7d} tris")
    print(f"TOTAL {len(props)} instances, {tot} tris (fixtures {sum(p['tris'] for p in props if p['kind'] == 'fixture')})")
    for f in fixtures:
        print(f"fixture {f['id']:<3} {f['prefab']:<18} emitter {f['emitterWorld']} layout {f['layoutLight']} "
              f"dev {f['deviation']}")
    print("wrote", OUT, OUT_FIX)


if __name__ == "__main__":
    main()
