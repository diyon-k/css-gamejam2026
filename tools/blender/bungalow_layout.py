"""BLINK Bungalow: the single source of truth for the level's numbers (docs/bungalow/SPEC.md + floorplan.png).

Plain Python, no bpy, so the Blender builders, the floor-plan generator, the Unity assembly step and tests all
read the same data.

  python -I tools/blender/bungalow_layout.py   -> writes Unity/Assets/Art/Environment/Bungalow/bungalow_layout.json

Coordinates are Unity metres. Origin (0, 0, 0) = outer south-west corner of the building at ground-floor finished
floor level. +X east, +Z north, +Y up. Yaw 0 faces +Z (north), 90 faces +X (east), as Unity's Y rotation.
Ranges are (min, max).

Revision 2 (USER DECISION 11): rooms >= 3x the Phase 1 floor area (footprint 11.0 x 8.5 -> 20.0 x 15.0),
ground-floor ceiling 3.0, hallways >= 1.8 clear, interior doorways >= 1.2 wide, stair re-derived (15 x 0.19).
USER DIRECTION 12: doorways >= 1.2 x 2.2, long sightlines through aligned openings, and an expandable data model.

How to grow the house (no code changes needed in bungalow_greybox.py):
- a level: add it to LEVELS (floor / ceiling heights); rooms and walls name their level.
- a room, corridor or wing: add a ROOMS rectangle (it gets a floor, and a ceiling on levels that have one),
  WALLS entries for its new sides, and OPENINGS cut into those walls.
- a stair well or attic hatch: add a hole to the room's floor_holes / ceiling_holes.
- markers, lights and furniture: SPAWNS / ROUTES / FIXTURES / FURNITURE entries.
check() validates the decision-11/12 rules and that every room edge is closed by a wall or an opening.
"""
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_JSON = os.path.abspath(os.path.join(HERE, "..", "..", "Unity", "Assets", "Art", "Environment", "Bungalow",
                                        "bungalow_layout.json"))
REVISION = 3   # revision 3: north wing (Corridor, MasterBedroom, Parlour), see SPEC 8

# ------------------------------------------------------------------ building
GRID = 0.5
EXT = 0.30            # exterior wall thickness (outer faces on the grid)
INT = 0.15            # interior partitions, centred on grid lines
FOOTPRINT = (20.0, 26.0)   # rev 3: the north wing takes the building to z 26
CEILING = 3.00        # finished ceiling, whole ground floor
CEILING_TOP = 3.20    # top of the ceiling slab = top of every ground-floor wall
SLAB = 0.25           # ground-floor slab: -0.25 .. 0.00 (basement ceiling = slab underside)
GRADE = -0.30         # outside ground level
GROUND_BOTTOM = -1.00
BASEMENT = -2.85      # basement finished floor: 2.60 clear to the slab, 2.40 under the joists
BASEMENT_SLAB = 0.20  # basement floor slab thickness
JOIST = dict(depth=0.20, width=0.10, pitch=0.60, first_z=4.40)   # E-W joists under the slab, underside -0.45
TRIMMER_UNDERSIDE = -0.50
SILL, HEAD = 0.90, 2.40
DOOR_W, DOOR_H = 1.20, 2.20   # doorway minimum (decisions 11 / 12): >= 1.2 clear wide, >= 2.2 clear high
CASING = 0.12         # door / arch casing width (door casing top = 2.32)
HALL_MIN = 1.80       # minimum clear hallway width (decision 11)

# Floor levels. floor = finished floor y; ceiling = finished ceiling y; slab = structure under the floor.
# The attic is reserved (direction 12): its floor is the top of the ground-floor ceiling slab.
LEVELS = {
    "basement": dict(floor=BASEMENT, ceiling=-SLAB, slab=BASEMENT_SLAB, ceiling_piece=False),
    "ground": dict(floor=0.0, ceiling=CEILING, slab=SLAB, ceiling_piece=True, ceiling_top=CEILING_TOP),
    # "attic": dict(floor=CEILING_TOP, ceiling=None, slab=0.0, ceiling_piece=False),   # future, see SPEC 8
}

# Rooms: clear interior (inside finished walls). floor y; ref = the reference image it is based on.
# phase1 = the Phase 1 clear area in m2 (for the >= 3x check).
ROOMS = {
    "EntranceHall": dict(level="ground", x=(7.075, 11.425), z=(0.300, 3.925), ref="ref1", phase1=5.0),
    "CentralHall": dict(level="ground", x=(7.075, 11.425), z=(4.075, 14.700), ref="ref1", phase1=13.2,
                        floor_holes=[dict(id="StairWell", x=(7.075, 8.325), z=(6.0, 10.5))]),
    "Living": dict(level="ground", x=(11.575, 19.700), z=(0.300, 6.925), ref="ref2", phase1=17.0),
    "Kitchen": dict(level="ground", x=(11.575, 19.700), z=(7.075, 14.700), ref="ref2/ref3", phase1=15.0),
    "Bedroom": dict(level="ground", x=(0.300, 6.925), z=(0.300, 9.425), ref="ref3", phase1=18.6),
    "Bathroom": dict(level="ground", x=(0.300, 6.925), z=(9.575, 14.700), ref="", phase1=9.5),
    # B1's floor is cut where the stair flight stands and where the separate landing piece goes
    "B1": dict(level="basement", x=(7.075, 13.425), z=(4.075, 14.700), ref="", name="Cellar", phase1=21.7,
               floor_holes=[dict(id="StairFoot", x=(7.075, 8.325), z=(6.0, 14.7))]),
    "B2": dict(level="basement", x=(13.575, 19.700), z=(4.075, 14.700), ref="", name="Workshop", phase1=14.8),
    # revision 3 (no Phase 1 counterpart, phase1=0): the north wing copies the hall / bedroom / living finishes
    "Corridor": dict(level="ground", x=(0.300, 19.700), z=(15.000, 16.800), ref="ref1", phase1=0.0),
    "MasterBedroom": dict(level="ground", x=(0.300, 7.925), z=(16.950, 25.700), ref="ref3", phase1=0.0,
                          name="Master Bedroom"),
    "Parlour": dict(level="ground", x=(8.075, 19.700), z=(16.950, 25.700), ref="ref2", phase1=0.0),
}
for _r in ROOMS.values():
    _r["y"] = LEVELS[_r["level"]]["floor"]

_BW = BASEMENT - BASEMENT_SLAB   # basement walls start under the basement slab (D5 keeps a threshold)
# Walls: axis = the direction the wall runs ("x" -> thickness along z, "z" -> thickness along x).
# t = thickness range, a = along range, y = height range, openings = ids in OPENINGS cut through it.
WALLS = {
    # ground floor, exterior (S/N run the full length, W/E between them so corners don't overlap)
    "Wall_Ext_S": dict(axis="x", t=(0.0, 0.3), a=(0.0, 20.0), y=(GRADE, CEILING_TOP), openings=["D1", "WL1"]),
    # revision 3: Wall_Ext_N is now the corridor's south wall (WB1 / WK1 become borrowed lights into the corridor)
    "Wall_Ext_N": dict(axis="x", t=(14.7, 15.0), a=(0.0, 20.0), y=(GRADE, CEILING_TOP),
                       openings=["WB1", "WK1", "DC1", "DC4"]),
    "Wall_Ext_W": dict(axis="z", t=(0.0, 0.3), a=(0.3, 14.7), y=(GRADE, CEILING_TOP), openings=["WR1"]),
    "Wall_Ext_E": dict(axis="z", t=(19.7, 20.0), a=(0.3, 14.7), y=(GRADE, CEILING_TOP),
                       openings=["WL2", "WK2", "D4"]),
    # ground floor, interior partitions (0.15, centred on x = 7.0 / 11.5, z = 4.0 / 7.0 / 9.5)
    "Wall_X7p0": dict(axis="z", t=(6.925, 7.075), a=(0.3, 14.7), y=(-SLAB, CEILING_TOP), openings=["D2", "D3"]),
    "Wall_X11p5": dict(axis="z", t=(11.425, 11.575), a=(0.3, 14.7), y=(-SLAB, CEILING_TOP), openings=["A1", "A3"]),
    "Wall_Z4p0": dict(axis="x", t=(3.925, 4.075), a=(7.075, 11.425), y=(-SLAB, CEILING_TOP), openings=["A2"]),
    "Wall_Z7p0": dict(axis="x", t=(6.925, 7.075), a=(11.575, 19.7), y=(-SLAB, CEILING_TOP),
                      openings=["O1", "O1_Transom"]),
    "Wall_Z9p5": dict(axis="x", t=(9.425, 9.575), a=(0.3, 6.925), y=(-SLAB, CEILING_TOP), openings=[]),
    # basement retaining walls (0.30, inner faces x 7.075 / 19.7, z 4.075 / 14.7) and the x = 13.5 partition
    "Wall_B_S": dict(axis="x", t=(3.775, 4.075), a=(6.775, 19.7), y=(_BW, -SLAB), openings=[]),
    "Wall_B_N": dict(axis="x", t=(14.7, 15.0), a=(6.775, 19.7), y=(_BW, GRADE), openings=[]),   # BW1 under corridor
    # revision 3, north wing: exterior N / W / E, the corridor's north partition, the bedroom / parlour partition
    "Wall_Ext_N2": dict(axis="x", t=(25.7, 26.0), a=(0.0, 20.0), y=(GRADE, CEILING_TOP),
                        openings=["WM1", "WP1", "WP2"]),
    "Wall_Ext_W2": dict(axis="z", t=(0.0, 0.3), a=(15.0, 25.7), y=(GRADE, CEILING_TOP), openings=["WC1", "WM2"]),
    "Wall_Ext_E2": dict(axis="z", t=(19.7, 20.0), a=(15.0, 25.7), y=(GRADE, CEILING_TOP), openings=["WC2"]),
    "Wall_Z16p875": dict(axis="x", t=(16.8, 16.95), a=(0.3, 19.7), y=(-SLAB, CEILING_TOP), openings=["DC2", "DC3"]),
    "Wall_X8p0": dict(axis="z", t=(7.925, 8.075), a=(16.95, 25.7), y=(-SLAB, CEILING_TOP), openings=["AC1"]),
    "Wall_B_W": dict(axis="z", t=(6.775, 7.075), a=(4.075, 14.7), y=(_BW, -SLAB), openings=[]),
    "Wall_B_E": dict(axis="z", t=(19.7, 20.0), a=(3.775, 15.0), y=(_BW, GRADE), openings=["BW2"]),
    "Wall_B_X13p5": dict(axis="z", t=(13.425, 13.575), a=(4.075, 14.7), y=(_BW, -SLAB), openings=["D5"]),
}

# Openings: span = along the wall, y = clear height range. Arches: y top = crown, spring = springing height.
OPENINGS = {
    "D1": dict(kind="door", wall="Wall_Ext_S", span=(8.45, 10.05), y=(0.0, DOOR_H), hinge="both", swing="in", swing_dir=1,
               note="double front door, 2 x 0.8 leaves; on the D1-A2-hall sightline"),
    "D2": dict(kind="door", wall="Wall_X7p0", span=(1.50, 2.70), y=(0.0, DOOR_H), hinge="S", swing="bedroom",
               swing_dir=-1),
    "D3": dict(kind="door", wall="Wall_X7p0", span=(12.00, 13.20), y=(0.0, DOOR_H), hinge="N", swing="bathroom",
               swing_dir=-1),
    "D4": dict(kind="door", wall="Wall_Ext_E", span=(12.40, 13.60), y=(0.0, DOOR_H), hinge="N", swing="in",
               swing_dir=-1, note="kitchen back door, locked"),
    "D5": dict(kind="door", wall="Wall_B_X13p5", span=(12.40, 13.60), y=(BASEMENT, BASEMENT + DOOR_H), hinge="N",
               swing="B2", swing_dir=1),
    "A1": dict(kind="arch", wall="Wall_X11p5", span=(1.20, 3.20), y=(0.0, 2.65), spring=2.30),
    "A2": dict(kind="arch", wall="Wall_Z4p0", span=(7.60, 10.60), y=(0.0, 2.75), spring=2.30),
    "A3": dict(kind="arch", wall="Wall_X11p5", span=(8.40, 10.20), y=(0.0, 2.70), spring=2.30),
    "O1": dict(kind="cased", wall="Wall_Z7p0", span=(13.50, 16.50), y=(0.0, 2.40)),
    "O1_Transom": dict(kind="transom", wall="Wall_Z7p0", span=(13.50, 16.50), y=(2.50, 2.85), bars=5),
    "WR1": dict(kind="window", wall="Wall_Ext_W", span=(2.2, 3.8), y=(SILL, HEAD)),
    "WL1": dict(kind="window", wall="Wall_Ext_S", span=(16.6, 18.2), y=(SILL, HEAD), boarded=True),
    "WL2": dict(kind="window", wall="Wall_Ext_E", span=(3.0, 4.6), y=(SILL, HEAD)),
    "WK1": dict(kind="window", wall="Wall_Ext_N", span=(13.6, 15.4), y=(SILL, HEAD)),
    "WK2": dict(kind="window", wall="Wall_Ext_E", span=(9.4, 11.0), y=(SILL, HEAD)),
    "WB1": dict(kind="window", wall="Wall_Ext_N", span=(2.4, 3.4), y=(1.50, HEAD), frosted=True),
    "BW2": dict(kind="window", wall="Wall_B_E", span=(9.0, 10.4), y=(-0.75, -0.30), hopper=True,
                well=dict(x=(20.0, 20.6), z=(8.9, 10.5), floor=-0.90)),
    # revision 3. DC1 is centred on D1 (x 9.25) and DC2 on DC1: front door -> hall -> corridor -> parlour, ~25 m
    "DC1": dict(kind="door", wall="Wall_Ext_N", span=(8.65, 9.85), y=(0.0, DOOR_H), hinge="W", swing="corridor",
                swing_dir=1, note="hall north landing -> corridor, on the D1 sightline"),
    "DC2": dict(kind="door", wall="Wall_Z16p875", span=(8.65, 9.85), y=(0.0, DOOR_H), hinge="W", swing="parlour",
                swing_dir=1, note="corridor -> parlour, on the D1 sightline"),
    "DC3": dict(kind="door", wall="Wall_Z16p875", span=(3.40, 4.60), y=(0.0, DOOR_H), hinge="E", swing="bedroom",
                swing_dir=1),
    "DC4": dict(kind="door", wall="Wall_Ext_N", span=(18.00, 19.20), y=(0.0, DOOR_H), hinge="E", swing="corridor",
                swing_dir=1, note="kitchen -> corridor: the hall / corridor / kitchen / living chase loop"),
    "AC1": dict(kind="arch", wall="Wall_X8p0", span=(20.50, 22.50), y=(0.0, 2.70), spring=2.30),
    "WM1": dict(kind="window", wall="Wall_Ext_N2", span=(3.0, 4.6), y=(SILL, HEAD)),
    "WM2": dict(kind="window", wall="Wall_Ext_W2", span=(20.5, 22.1), y=(SILL, HEAD)),
    "WP1": dict(kind="window", wall="Wall_Ext_N2", span=(11.0, 12.6), y=(SILL, HEAD)),
    "WP2": dict(kind="window", wall="Wall_Ext_N2", span=(15.5, 17.1), y=(SILL, HEAD), boarded=True),
    "WC1": dict(kind="window", wall="Wall_Ext_W2", span=(15.3, 16.5), y=(SILL, HEAD)),
    "WC2": dict(kind="window", wall="Wall_Ext_E2", span=(15.3, 16.5), y=(SILL, HEAD)),
}
# A4: overhead hall arch spanning the hall at the well's north end (no wall below the springing)
A4 = dict(x=(7.075, 11.425), z=(10.425, 10.575), spring=2.40, crown=2.85)

# ------------------------------------------------------------------ stair (down, north), rise 0.19 / run 0.28
# Total drop = slab 0.25 + basement clear 2.60 = 2.85 = 15 x 0.19 exactly; 14 treads x 0.28 = 3.92 run.
STAIR = dict(x=(7.075, 8.325), z_top=6.0, rise=0.19, run=0.28, risers=15, treads=14,
             opening=ROOMS["CentralHall"]["floor_holes"][0],      # ground-floor well, 1.25 x 4.5
             rail_x=(8.285, 8.325),                              # stair-side rail inside the well edge
             handrail_above_pitch=0.90)
STAIR["z_bottom"] = round(STAIR["z_top"] + STAIR["treads"] * STAIR["run"], 4)          # 9.92
STAIR["landing"] = dict(x=STAIR["x"], z=(STAIR["z_bottom"], ROOMS["B1"]["z"][1]))     # 4.78 deep
STAIR["pitch_deg"] = math.degrees(math.atan2(STAIR["rise"], STAIR["run"]))              # 34.16
STAIR["clear_width"] = round(STAIR["rail_x"][0] - STAIR["x"][0], 3)                     # 1.21
# ground-floor balustrade around the well (ref 1): east run + north end, 0.95 high, newels 1.10
BALUSTRADE = dict(height=0.95, newel=0.08, newel_h=1.10, baluster=0.04, spacing=0.12,
                  east=dict(x=(8.325, 8.405), z=(5.96, 10.58)), north=dict(x=(7.075, 8.405), z=(10.5, 10.58)))
# G1 lockable gate at the stair top (decision 6). Built OPEN (leaf along the east balustrade, inside the well);
# closed = yaw -90 about the hinge.
GATE = dict(id="G1", hinge=(8.305, 0.0, 6.0), leaf=1.20, height=0.95, open_yaw=0.0, closed_yaw=-90.0)
# Basement beams around the well (undersides): trimmers -0.50, header along the well's east edge
BEAMS = {
    "Trimmer_N": dict(x=(7.075, 13.425), z=(10.5, 10.65), y=(TRIMMER_UNDERSIDE, -SLAB)),
    "Trimmer_S": dict(x=(7.075, 13.425), z=(5.85, 6.0), y=(TRIMMER_UNDERSIDE, -SLAB)),
    "Header_E": dict(x=(8.325, 8.475), z=(6.0, 10.5), y=(TRIMMER_UNDERSIDE, -SLAB)),
}

# ------------------------------------------------------------------ fixed kit pieces placed in Phase 2
FIREPLACE = dict(breast=dict(x=(2.4, 4.4), z=(0.3, 0.75)), firebox=dict(x=(3.0, 3.8), z=(0.3, 0.6), h=0.8),
                 hearth=dict(x=(2.25, 4.55), z=(0.75, 1.25), h=0.03),
                 mantel=dict(x=(2.25, 4.55), z=(0.65, 0.9), y=(1.25, 1.31)))
EXTERIOR = dict(ground=dict(x=(-4.0, 24.0), z=(-4.0, 30.0)),
                front_step=[dict(x=(8.0, 10.5), z=(-0.8, 0.0), y=(GRADE, -0.15)),
                            dict(x=(8.0, 10.5), z=(-0.4, 0.0), y=(-0.15, 0.0))],
                back_step=[dict(x=(20.0, 20.5), z=(12.3, 13.7), y=(GRADE, -0.15))])

# ------------------------------------------------------------------ gameplay markers
# P0: the player wakes on the bed (WakeUpSequence) beside its east side; yaw 200 = the CAM-3 / ref-3 direction.
SPAWNS = {
    "P0": dict(pos=(4.8, 0.0, 7.6), yaw=200.0, kind="player"),
    "Z1": dict(pos=(12.5, 0.0, 6.2), yaw=90.0, kind="zombie", route="R1", room="Living"),
    "Z2": dict(pos=(18.0, 0.0, 11.5), yaw=240.0, kind="zombie", route="R2", room="Kitchen"),
    "Z3": dict(pos=(11.0, BASEMENT, 6.0), yaw=0.0, kind="zombie", route="R3", room="B1"),
    "Z4": dict(pos=(2.5, 0.0, 12.5), yaw=90.0, kind="zombie", route=None, room="Bathroom", note="dormant"),
    "Z5": dict(pos=(2.0, 0.0, 15.9), yaw=90.0, kind="zombie", route="R5", room="Corridor"),
    "Z6": dict(pos=(16.0, 0.0, 23.0), yaw=270.0, kind="zombie", route="R6", room="Parlour"),
}
ROUTES = {  # closed loops, waypoint order = patrol order
    "R1": [(12.5, 0.0, 6.2), (18.8, 0.0, 6.2), (18.8, 0.0, 1.4), (16.0, 0.0, 1.4)],
    "R2": [(18.0, 0.0, 11.5), (13.0, 0.0, 9.3), (10.0, 0.0, 9.3), (10.0, 0.0, 13.5), (10.0, 0.0, 6.5),
           (13.0, 0.0, 9.3)],
    "R3": [(11.0, BASEMENT, 6.0), (11.0, BASEMENT, 13.0), (16.5, BASEMENT, 13.0), (17.0, BASEMENT, 6.0)],
    "R5": [(2.0, 0.0, 15.9), (18.6, 0.0, 15.9)],
    "R6": [(16.0, 0.0, 23.0), (10.5, 0.0, 23.0), (10.5, 0.0, 19.0), (16.0, 0.0, 19.0)],
}
CAMERAS = {  # reference-match cameras (vertical FOV); composition checks in SPEC 5.3
    "CAM-1": dict(pos=(9.4, 1.65, 4.1), yaw=352.0, pitch=-8.0, fov=60.0, ref="ref1_stair_landing.png",
                  size=(1192, 670)),
    "CAM-2": dict(pos=(19.2, 1.65, 0.8), yaw=297.0, pitch=-12.0, fov=60.0, ref="ref2_red_living_room.png",
                  size=(1920, 1080)),
    "CAM-3": dict(pos=(5.0, 1.50, 6.6), yaw=200.0, pitch=-10.0, fov=55.0, ref="ref3_green_farmhouse_room.png",
                  size=(1048, 486)),
}
PLAYER = dict(radius=0.35, height=1.8, step=0.3, slope=45.0, eye=1.65, fov=60.0)
NAV_AGENT = dict(name="BungalowZombie", radius=0.30, height=1.80, climb=0.30, slope=40.0, voxel=0.05,
                 min_region=0.5)

# ------------------------------------------------------------------ lighting plan (SPEC 5.1): id -> fixture
# kind: dome, chand, sconce, lamp, fan, bulb, area. mode: Baked / Mixed / Realtime. pos = light position.
FIXTURES = {
    "E1": dict(kind="dome", pos=(9.25, 2.92, 2.0), col="#F2DFA8", i=1.2, range=5.5, mode="Baked"),
    "H1": dict(kind="chand", pos=(7.70, 2.45, 9.6), col="#FFD6A0", i=1.0, range=6.5, mode="Baked"),
    # rev 3: H2 / H3 flank the new DC1 door (were x 8.45 / 10.05 round the removed hall radiator)
    "H2": dict(kind="sconce", pos=(8.20, 1.90, 14.62), col="#FFC680", i=0.5, range=5.0, mode="Baked"),
    "H3": dict(kind="sconce", pos=(10.30, 1.90, 14.62), col="#FFC680", i=0.5, range=5.0, mode="Baked"),
    "H4": dict(kind="lamp", pos=(11.0, 1.05, 13.8), col="#FFCB85", i=1.4, range=4.0, mode="Mixed", hero=True),
    "H5": dict(kind="sconce", pos=(7.13, 2.10, 8.0), col="", i=0.0, range=0.0, mode="off", off=True),
    "H6": dict(kind="sconce", pos=(11.37, 1.90, 6.6), col="#FFC680", i=0.45, range=5.0, mode="Baked"),
    "L1": dict(kind="fan", pos=(14.6, 2.55, 2.9), col="#FFD9A0", i=2.0, range=7.0, mode="Mixed"),
    "L2": dict(kind="lamp", pos=(15.6, 1.00, 5.3), col="#FFE6C2", i=1.6, range=4.5, mode="Mixed", hero=True),
    "L3": dict(kind="sconce", pos=(11.62, 1.90, 5.3), col="#FFC98A", i=0.7, range=5.0, mode="Baked"),
    "L4": dict(kind="sconce", pos=(16.75, 2.00, 6.85), col="#FFC98A", i=0.35, range=5.0, mode="Baked"),
    "K1": dict(kind="dome", pos=(14.0, 2.92, 10.9), col="#E9E6D2", i=1.1, range=6.0, mode="Mixed"),
    "K2": dict(kind="dome", pos=(17.6, 2.92, 10.9), col="#E9E6D2", i=0.9, range=5.5, mode="Baked"),
    "R1": dict(kind="lamp", pos=(0.8, 1.00, 1.0), col="#F2DDB0", i=1.2, range=5.0, mode="Mixed"),
    "R2": dict(kind="lamp", pos=(4.6, 0.95, 9.0), col="#F5E6A8", i=0.5, range=4.0, mode="Baked"),
    "W1": dict(kind="sconce", pos=(4.4, 2.00, 14.62), col="#E6EBC8", i=0.6, range=5.0, mode="Baked"),
    "C1": dict(kind="bulb", pos=(8.9, -0.65, 12.4), col="#F0DC9A", i=1.4, range=7.0, mode="Realtime", hero=True),
    "C2": dict(kind="bulb", pos=(16.6, -0.65, 9.4), col="#F0DC9A", i=0.0, range=7.0, mode="Realtime", off=True),
    "RW1": dict(kind="area", pos=(0.32, 1.65, 3.0), col="#A9B3A2", i=1.2, mode="Baked", size=(1.6, 1.5), face="+X"),
    "LW1": dict(kind="area", pos=(17.4, 1.65, 0.32), col="#8E9AA6", i=0.4, mode="Baked", size=(1.6, 1.5), face="+Z"),
    "LW2": dict(kind="area", pos=(19.68, 1.65, 3.8), col="#8E9AA6", i=0.4, mode="Baked", size=(1.6, 1.5), face="-X"),
    # rev 3: KW1 / WW1 now look into the dark corridor, not outside: dimmed borrowed light
    "KW1": dict(kind="area", pos=(14.5, 1.65, 14.68), col="#9AA6B0", i=0.2, mode="Baked", size=(1.8, 1.5), face="-Z"),
    "KW2": dict(kind="area", pos=(19.68, 1.65, 10.2), col="#9AA6B0", i=0.6, mode="Baked", size=(1.6, 1.5), face="-X"),
    "WW1": dict(kind="area", pos=(2.9, 1.95, 14.68), col="#A3AEA6", i=0.15, mode="Baked", size=(1.0, 0.9), face="-Z"),
    "CW2": dict(kind="area", pos=(19.68, -0.52, 9.7), col="#8A96A0", i=0.3, mode="Baked", size=(1.4, 0.45),
                face="-X"),
    # revision 3 (all Baked: shadows prebaked into the lightmaps). Lamp positions are refined by bungalow_props.py
    # to sit on their parent prop (see its FIX table / deviation report).
    "CO1": dict(kind="dome", pos=(4.50, 2.92, 15.9), col="#F2DFA8", i=0.9, range=5.5, mode="Baked"),
    "CO2": dict(kind="dome", pos=(15.00, 2.92, 15.9), col="#F2DFA8", i=0.6, range=5.5, mode="Baked"),
    "CO3": dict(kind="sconce", pos=(12.0, 2.00, 16.72), col="", i=0.0, range=0.0, mode="off", off=True),
    "P1": dict(kind="chand", pos=(13.9, 2.45, 21.3), col="#FFD6A0", i=1.1, range=7.5, mode="Baked"),
    "P2": dict(kind="lamp", pos=(17.2, 0.97, 25.45), col="#FFE6C2", i=1.4, range=4.5, mode="Baked"),
    "M1": dict(kind="fan", pos=(4.1, 2.55, 21.3), col="#FFD9A0", i=1.6, range=7.0, mode="Baked"),
    "M2": dict(kind="lamp", pos=(4.75, 0.93, 25.45), col="#F5E6A8", i=0.6, range=4.0, mode="Baked"),
    "MW1": dict(kind="area", pos=(3.8, 1.65, 25.68), col="#A9B3A2", i=0.9, mode="Baked", size=(1.6, 1.5), face="-Z"),
    "MW2": dict(kind="area", pos=(0.32, 1.65, 21.3), col="#A9B3A2", i=0.7, mode="Baked", size=(1.6, 1.5), face="+X"),
    "PW1": dict(kind="area", pos=(11.8, 1.65, 25.68), col="#8E9AA6", i=0.6, mode="Baked", size=(1.6, 1.5), face="-Z"),
    "CW3": dict(kind="area", pos=(0.32, 1.65, 15.9), col="#8E9AA6", i=0.4, mode="Baked", size=(1.2, 1.5), face="+X"),
    "CW4": dict(kind="area", pos=(19.68, 1.65, 15.9), col="#8E9AA6", i=0.4, mode="Baked", size=(1.2, 1.5), face="-X"),
}
# Reserved for later (direction 12), NOT built. Corridor_N was built in revision 3.
FUTURE = {
    "Wing_E": dict(kind="wing", level="ground", x=(20.3, 26.0), z=(9.0, 14.7),
                   attach=dict(wall="Wall_Ext_E", span=(12.4, 13.6), note="through D4"),
                   note="utility wing off the kitchen back door"),
    "AtticHatch": dict(kind="hatch", level="ground", room="CentralHall", x=(9.7, 10.9), z=(12.1, 12.8),
                       note="ceiling hole for a pull-down ladder (lands at z ~11.0 on the north landing); "
                            "attic floor y = CEILING_TOP 3.2"),
}

# Key furniture footprints (real-world scale) for the plan: (x0, z0, x1, z1) or rotated (cx, cz, w, d, yaw).
FURNITURE = {
    "bed": (2.6, 7.2, 4.2, 9.4), "nightstand": (4.35, 8.7, 4.85, 9.2), "plaid settee": (0.35, 2.2, 1.1, 3.8),
    "rocker": (3.9, 2.0, 0.7, 0.85, 20), "wardrobe": (0.35, 6.0, 0.95, 7.6), "dresser": (5.3, 9.0, 6.9, 9.4),
    "reading chair": (1.4, 5.0, 0.8, 0.8, 120),
    "console": (10.95, 13.0, 11.4, 14.6), "hall bench": (11.0, 4.6, 11.4, 6.0),
    "sideboard": (12.5, 0.32, 14.3, 0.88), "sofa": (15.2, 3.9, 2.0, 0.9, 297), "c.table": (14.0, 3.3, 1.0, 0.55, 297),
    "armchair": (12.8, 1.6, 0.85, 0.8, 117), "reading corner": (18.4, 5.4, 1.0, 1.0, 225),
    "counter": (12.0, 14.1, 16.0, 14.68), "fridge": (16.5, 14.05, 17.3, 14.68), "stove": (19.1, 7.4, 19.68, 8.2),
    "table": (13.8, 10.3, 15.6, 11.3), "dresser 2": (11.6, 11.5, 12.1, 13.0),
    "tub": (0.35, 9.65, 2.15, 10.45), "toilet": (0.35, 12.8, 0.95, 13.4), "basin": (4.0, 14.15, 4.8, 14.68),
}
FURNITURE_B = {"shelves": (9.0, 4.1, 11.0, 4.6), "crates": (12.2, 4.3, 13.0, 5.0), "crate": (12.4, 13.8, 13.3, 14.6),
               "gen": (16.0, 11.8, 17.2, 12.6), "bench": (19.1, 5.0, 19.68, 7.6), "shelves 2": (14.0, 4.1, 16.0, 4.6)}


# ------------------------------------------------------------------ Phase 3 modular kit (bungalow_kit.py)
# Everything the kit builds comes from the tables above plus these. A new room only needs a ROOM_FINISH entry; a new
# radiator, pipe run, door leaf or decay spot is a row here.
#
# UV0 contract: surface materials are box-mapped in world space at 1 UV = one texture tile of SURFACE_TILE_M metres
# (so their Unity materials use tiling 1). M_Trim pieces use the trim-sheet strips (bungalow_trim.STRIPS).
SURFACE_TILE_M = {"M_FloorPlank": 2.0, "M_Parquet": 3.4, "M_Lino": 2.0, "M_Concrete": 2.5, "M_Wall_Damask": 2.0,
                  "M_Wall_Pale": 2.0, "M_Wall_Floral": 2.0, "M_Plaster": 1.5, "M_Plaster_Kitchen": 1.5,
                  "M_BrickWhite": 2.0, "M_Tile": 1.5, "M_Glass": 1.0, "M_Water": 4.0,
                  # revision 3 (juliovii.itch.io scans, Poly Haven fallback; tile sizes confirmed by the texture pass)
                  "M_Wall_Parlour": 2.0}
# Revision 3 triangle cap for everything the north wing adds (shell pieces + props + fixtures): <= 100k.
REV3_TRI_CAP = 100_000
REV3_ROOMS = ("Corridor", "MasterBedroom", "Parlour")
# (exported to bungalow_layout.json as "surfaceTiles"; Materials/Editor/BungalowMaterialsBuilder reads it from there)
# Board seams of the M_FloorPlank scan (Poly Haven dark_wooden_planks 1K, image rows from the top, boards along U).
# The kit cuts its floor boards on these lines, so geometry seams sit on the texture's seams.
FLOOR_PLANK_SEAMS_PX = (57, 164, 274, 388, 512, 628, 742, 850, 966)
# Trim styles -> trim-sheet strip keys for casings / mouldings, door leaves, turned work
TRIM_STYLE = {
    "varnish": dict(trim="varnish", door="varnish_door", turned="varnish_turned", window="varnish"),
    "paint_dark": dict(trim="paint_dark", door="paint_dark", turned="paint_dark", window="paint_dark"),
    "paint_light": dict(trim="paint_light", door="paint_light", turned="paint_light", window="paint_light"),
    "raw": dict(trim="raw", door="raw", turned="raw", window="metal_paint"),
}
EXTERIOR_FINISH = "M_Plaster"     # outside faces of the exterior walls, wall tops, slab edges, reveals
# Per room: floor / wall / ceiling materials, board direction (plank floors), trim style, mouldings, wall bands
# (a band re-skins the wall faces of that room between y0..y1, optionally only on one side / along span).
ROOM_FINISH = {
    "EntranceHall": dict(floor="M_FloorPlank", boards="z", wall="M_Wall_Pale", ceiling="M_Plaster", trim="varnish",
                         mouldings=("skirting", "dado", "picture", "cornice")),
    "CentralHall": dict(floor="M_FloorPlank", boards="z", wall="M_Wall_Pale", ceiling="M_Plaster", trim="varnish",
                        mouldings=("skirting", "dado", "picture", "cornice")),
    "Living": dict(floor="M_Parquet", wall="M_Wall_Damask", ceiling="M_Plaster", trim="varnish",
                   mouldings=("skirting", "picture", "cornice")),
    "Kitchen": dict(floor="M_Lino", wall="M_Plaster_Kitchen", ceiling="M_Plaster", trim="paint_light",   # blue-white
                    mouldings=("skirting", "cornice"),
                    bands=[dict(mat="M_Tile", y=(0.90, 1.50), side="N", span=(11.575, 16.20))]),
    "Bedroom": dict(floor="M_FloorPlank", boards="x", wall="M_Wall_Floral", ceiling="M_Plaster", trim="paint_dark",
                    mouldings=("skirting", "picture", "cornice")),
    "Bathroom": dict(floor="M_Lino", wall="M_Plaster", ceiling="M_Plaster", trim="paint_light",
                     mouldings=("tilecap", "cornice"), bands=[dict(mat="M_Tile", y=(0.0, 1.20))]),
    "B1": dict(floor="M_Concrete", wall="M_BrickWhite", ceiling="M_Plaster", trim="raw", mouldings=()),
    "B2": dict(floor="M_Concrete", wall="M_BrickWhite", ceiling="M_Plaster", trim="raw", mouldings=()),
    # revision 3: copied from the hall / bedroom / living rows and altered
    "Corridor": dict(floor="M_FloorPlank", boards="x", wall="M_Wall_Pale", ceiling="M_Plaster", trim="varnish",
                     mouldings=("skirting", "dado", "picture", "cornice")),
    "MasterBedroom": dict(floor="M_FloorPlank", boards="z", wall="M_Wall_Damask", ceiling="M_Plaster",
                          trim="paint_dark", mouldings=("skirting", "picture", "cornice")),
    "Parlour": dict(floor="M_Parquet", wall="M_Wall_Parlour", ceiling="M_Plaster", trim="varnish",
                    mouldings=("skirting", "dado", "picture", "cornice")),
}
# Moulding heights (profiles are geometry, in bungalow_kit.PROFILES). y = bottom of the profile.
MOULDING_Y = {"skirting": 0.0, "dado": 0.88, "tilecap": 1.18, "picture": 2.56, "cornice": CEILING - 0.11}
# Door leaves: style, open angle (deg from closed, towards the swing side), sag = in-plane droop (deg) of a leaf
DOOR_LEAVES = {
    "D1": dict(style="double", open=(84.0, 18.0), sag=(0.0, 3.0), note="right leaf ajar and hung 3 deg off"),
    "D2": dict(style="panel4", open=78.0),
    "D3": dict(style="panel4", open=62.0),
    "D4": dict(style="panel4", open=0.0, note="locked, closed"),
    "D5": dict(style="ledged", open=96.0),
    "DC1": dict(style="panel4", open=170.0, note="folds flat on the corridor S wall: at 90 it leaves 0.6 m of corridor"),
    "DC2": dict(style="panel4", open=90.0),
    "DC3": dict(style="panel4", open=90.0),
    "DC4": dict(style="panel4", open=90.0),
}
# Radiators: wall run axis, wall face coordinate, inward direction (+1 / -1 across the face), span along the wall
RADIATORS = {   # rev 3: Radiator_Hall (and its pipes) removed, DC1 is cut where it hung
    "Radiator_Parlour": dict(room="Parlour", axis="z", face=19.7, inward=-1, span=(20.15, 21.45)),
    "Radiator_Master": dict(room="MasterBedroom", axis="z", face=0.3, inward=1, span=(18.2, 19.2)),
    "Radiator_Living": dict(room="Living", axis="z", face=19.7, inward=-1, span=(3.15, 4.45)),
    "Radiator_Kitchen": dict(room="Kitchen", axis="z", face=19.7, inward=-1, span=(9.55, 10.85)),
    "Radiator_Bedroom": dict(room="Bedroom", axis="z", face=0.3, inward=1, span=(4.35, 5.35)),
    "Radiator_Bathroom": dict(room="Bathroom", axis="x", face=9.575, inward=1, span=(3.40, 4.40)),
}
RADIATOR = dict(y=(0.14, 0.78), depth=0.13, gap=0.04, column=0.065)
# Basement heating pipes (polylines, Unity coordinates) under the joists, with drops to the radiators over B1 / B2
PIPE_R = 0.018
PIPES = {
    "Pipe_Main_N": [(7.35, -0.56, 14.45), (19.40, -0.56, 14.45), (19.40, -0.56, 4.35)],
    # drops start at floor level under the radiator tails (bungalow_kit.build_radiators: valve 0.045 outside the
    # span, 0.03 in front of the radiator back); the living flow valve is over solid ground, so it runs in the floor
    # rev 3: the hall radiator's drops are gone; the return main keeps its run along the north wall
    "Pipe_Return_N": [(7.35, -0.62, 14.30), (19.30, -0.62, 14.30), (19.30, -0.62, 4.35)],
    "Pipe_Kitchen_Flow": [(19.63, 0.0, 9.505), (19.63, -0.56, 9.505), (19.40, -0.56, 9.505)],
    "Pipe_Kitchen_Return": [(19.63, 0.0, 10.895), (19.63, -0.62, 10.895), (19.30, -0.62, 10.895)],
    "Pipe_Living_Flow": [(19.63, -0.25, 4.25), (19.63, -0.56, 4.25), (19.40, -0.56, 4.25)],
    "Pipe_Living_Return": [(19.63, 0.0, 4.495), (19.63, -0.40, 4.495), (19.30, -0.40, 4.495), (19.30, -0.62, 4.35)],
}
# Decay baked into the geometry (SPEC 2.2 / 2.1)
DECAY = dict(
    sag=[dict(room="CentralHall", x=(8.325, 9.9), z=(5.6, 11.2), depth=0.024),     # boards sagging by the well
         dict(room="Bedroom", x=(0.3, 2.2), z=(0.3, 1.8), depth=0.010)],           # damp corner under the window
    missing_balusters=[("east", 9), ("east", 23)],     # (balustrade run, index from the stair-top newel)
    cracked_newel="NE",                                  # the NE newel cap is split
    chip_rate=0.9,                                       # chipped board edges per metre of board, on average
    water_y=BASEMENT + 0.08,                             # 0.08 m standing water in the basement
)


def stair_nosings():
    """[(z, y)] of the 15 nosing / riser lines, top (hall floor edge) to bottom."""
    s = STAIR
    return [(round(s["z_top"] + k * s["run"], 4), round(-k * s["rise"], 4)) for k in range(s["risers"])]


def room_area(rid):
    r = ROOMS[rid]
    return (r["x"][1] - r["x"][0]) * (r["z"][1] - r["z"][0])


def to_json():
    """Unity-JsonUtility-friendly dump (lists of objects, flat float arrays)."""
    v3 = lambda p: [float(c) for c in p]  # noqa: E731
    return {
        "revision": REVISION,
        "rooms": [dict(id=k, min=[r["x"][0], r["y"], r["z"][0]], max=[r["x"][1], LEVELS[r["level"]]["ceiling"],
                                                                      r["z"][1]], ref=r["ref"]) for k, r in ROOMS.items()],
        "openings": [dict(id=k, kind=o["kind"], wall=o["wall"], span=list(o["span"]), y=list(o["y"]))
                     for k, o in OPENINGS.items()],
        "spawns": [dict(id=k, kind=s["kind"], pos=v3(s["pos"]), yaw=s["yaw"], route=s.get("route") or "")
                   for k, s in SPAWNS.items()],
        "routes": [dict(id=k, pts=[c for p in pts for c in v3(p)]) for k, pts in ROUTES.items()],
        "cameras": [dict(id=k, pos=v3(c["pos"]), yaw=c["yaw"], pitch=c["pitch"], fov=c["fov"], ref=c["ref"],
                         w=c["size"][0], h=c["size"][1]) for k, c in CAMERAS.items()],
        "fixtures": [dict(id=k, kind=f["kind"], pos=v3(f["pos"]), color=f["col"], intensity=f["i"],
                          range=f.get("range", 0.0), mode=f["mode"]) for k, f in FIXTURES.items()],
        "gate": dict(id=GATE["id"], hinge=v3(GATE["hinge"]), openYaw=GATE["open_yaw"],
                     closedYaw=GATE["closed_yaw"]),
        "surfaceTiles": [dict(material=k, tileM=float(v)) for k, v in SURFACE_TILE_M.items()],
        "agent": dict(NAV_AGENT),
        "player": dict(PLAYER),
    }


def write_json(path=OUT_JSON):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(to_json(), f, indent=1)
    return path


def check():
    """Decision-11 checks: room areas >= 3x Phase 1, doorways >= 1.2, hall walkway >= 1.8, stair numbers."""
    out = []
    for k, r in ROOMS.items():
        a = room_area(k)
        ratio = f"({a / r['phase1']:.2f}x)" if r["phase1"] else "(new in revision 3)"
        out.append((f"room {k} {r['x'][1] - r['x'][0]:.3f} x {r['z'][1] - r['z'][0]:.3f} = {a:.1f} m2 {ratio}",
                    a >= 3 * r["phase1"]))
    for k, o in OPENINGS.items():
        if o["kind"] in ("door", "arch", "cased"):
            w = o["span"][1] - o["span"][0]
            h = (o.get("spring") or o["y"][1]) - o["y"][0]
            out.append((f"opening {k} {w:.2f} wide x {h:.2f} clear high (arches: to the springing)",
                        w >= DOOR_W - 1e-6 and h >= DOOR_H - 1e-6))
    # every room side is closed by a wall face
    for k, r in ROOMS.items():
        lv = LEVELS[r["level"]]
        for side, (ax, val, span) in {"W": (0, r["x"][0], r["z"]), "E": (0, r["x"][1], r["z"]),
                                      "S": (2, r["z"][0], r["x"]), "N": (2, r["z"][1], r["x"])}.items():
            faces = [w for w in WALLS.values() if (0 if w["axis"] == "z" else 2) == ax
                     and abs(w["t"][0 if side in "EN" else 1] - val) < 1e-6
                     and w["y"][0] < lv["ceiling"] and w["y"][1] > lv["floor"]]

            def covered(rngs, lo, hi):
                pos = lo
                for a0, a1 in sorted(rngs):
                    if a0 > pos + 1e-6:
                        break
                    pos = max(pos, a1)
                return pos >= hi - 1e-6
            # along the side, then (for walls stacked across levels, e.g. retaining wall + exterior wall) in height
            ok = covered([w["a"] for w in faces], *span) and covered([w["y"] for w in faces], lv["floor"],
                                                                      lv["ceiling"])
            out.append((f"room {k} side {side} closed by walls", ok))
    walk = ROOMS["CentralHall"]["x"][1] - BALUSTRADE["east"]["x"][1]
    out.append((f"hall walkway {walk:.3f}", walk >= HALL_MIN))
    north = ROOMS["CentralHall"]["z"][1] - BALUSTRADE["north"]["z"][1]
    out.append((f"north landing {north:.3f}", north >= HALL_MIN))
    top = STAIR["z_top"] - ROOMS["CentralHall"]["z"][0]
    out.append((f"stair-top landing {top:.3f}", top >= HALL_MIN))
    s = STAIR
    drop = SLAB + (-SLAB - BASEMENT)
    out.append((f"stair {s['risers']} x {s['rise']} = {s['risers'] * s['rise']:.3f} (drop {drop:.3f}), "
                f"{s['treads']} x {s['run']} = {s['treads'] * s['run']:.2f}, pitch {s['pitch_deg']:.1f}, "
                f"2R+G {2 * s['rise'] + s['run']:.2f}, clear {s['clear_width']}",
                abs(s["risers"] * s["rise"] - drop) < 1e-6 and 0.18 <= s["rise"] <= 0.20))
    need = s["z_top"] + (2.2 - TRIMMER_UNDERSIDE) * s["run"] / s["rise"]
    out.append((f"well end {s['opening']['z'][1]} >= headroom line {need:.2f}", s["opening"]["z"][1] >= need))
    # Phase 3 kit tables
    out.append(("every room has a ROOM_FINISH", set(ROOMS) <= set(ROOM_FINISH)))
    out.append(("every door has a DOOR_LEAVES entry",
                {k for k, o in OPENINGS.items() if o["kind"] == "door"} <= set(DOOR_LEAVES)))
    for k, r in RADIATORS.items():
        rm = ROOMS[r["room"]]
        along = rm["x"] if r["axis"] == "x" else rm["z"]
        across = rm["z"] if r["axis"] == "x" else rm["x"]
        ok = along[0] <= r["span"][0] < r["span"][1] <= along[1] and abs(r["face"] - across[0 if r["inward"] > 0
                                                                                            else 1]) < 1e-6
        out.append((f"{k} on the {r['room']} wall", ok))
    low = min(p[1] for pts in PIPES.values() for p in pts if p[1] < -SLAB) - PIPE_R
    out.append((f"basement headroom under the pipes {low - BASEMENT:.2f}", low - BASEMENT >= 2.2 - 1e-6))
    return out


if __name__ == "__main__":
    for msg, ok in check():
        print("OK  " if ok else "FAIL", msg)
    print("wrote", write_json())
