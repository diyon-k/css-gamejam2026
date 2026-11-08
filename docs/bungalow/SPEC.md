# BLINK: Bungalow level spec (revision 2)

Target scene `Assets/Scenes/House_Bungalow.unity`, art in `Assets/Art/Environment/Bungalow/`. The floor plan is [floorplan.png](floorplan.png): ground floor, basement, a stair section and a legend.

**Revision 2** applies **USER DECISION 11** (rooms ≥ 3× the Phase 1 floor area, footprint 11.0 × 8.5 → **20 × 15 m**, ceiling **3.00**, hallways ≥ 1.8 m, interior doorways ≥ 1.2 m) and **USER DIRECTION 12** (doorways ≥ 1.2 × 2.2, long sightlines for the blink mechanic, an expandable data-driven house; see §8).
- Every number in §1, §5.1 and §5.3 is generated from `tools/blender/bungalow_layout.py`, the single source of truth. The plan is regenerated from the same data by `tools/blender/bungalow_floorplan.py`.
- Props keep real-world scale. The extra space is dressed with more furniture groupings, not bigger furniture.

**Coordinates (used everywhere below):**
- Unity metres. Origin (0, 0, 0) is the **outer south-west corner of the building at ground-floor finished floor level**.
- +X is east, +Z is north, +Y is up.
- Yaw 0° faces +Z (north) and 90° faces +X (east), the same as Unity's Y rotation.
- Exterior walls are 0.30 m thick. Their outer faces sit on the 0.5 m grid, so the inner faces are x = 0.3 / 19.7 and z = 0.3 / 14.7.
- Interior partitions are 0.15 m thick and centred on grid lines: x = 7.0, x = 11.5, z = 4.0, z = 7.0 and z = 9.5. The basement partition is x = 13.5.

**References (the images were followed, per the image correction):**
- Ref 1 `ref1_stair_landing.png`: stairwell with a balustrade, arches, pale wallpaper, 3-arm chandelier, sconces.
- Ref 2 `ref2_red_living_room.png`: red damask wallpaper, herringbone parquet, ceiling fan, shaded lamp, tulip sconce.
- Ref 3 `ref3_green_farmhouse_room.png`: green floral wallpaper, whitewashed brick fireplace, rocking chair, plaid sofa.

---

## 1. Constraints check

### 1.1 Building

| Requirement | Spec (revision 2) | OK |
|---|---|---|
| Footprint (decision 11: ~20 × 15, supersedes the brief's 9–11 × 7–9) | **20 × 15 m** outer (19.4 × 14.4 inner) | ✓ |
| Every room ≥ 3× its Phase 1 area | 3.11× to 4.40× (table 1.2) | ✓ |
| Ceiling 3.0 (decision 11) | **3.00** on the whole ground floor (slab to 3.20); arch springings 2.30–2.40 | ✓ |
| Rooms | Entrance hall, long central hall, living room, kitchen, bedroom, bathroom, plus the basement (cellar B1 and workshop B2) | ✓ |
| Stair **down** to the basement, off the hall or kitchen | In the central hall: a 1.25 × 4.50 m open well with a ref-1 balustrade on two sides | ✓ |
| Doorways ≥ 1.2 wide × 2.2 high (decisions 11/12, supersede the brief's 0.9 × 2.0) | D1 **double, 1.60 × 2.20**; D2–D5 **1.20 × 2.20** clear, casing top 2.32; arches 1.80–3.00 wide, springing ≥ 2.30; O1 3.00 × 2.40 | ✓ |
| Hallways ≥ 1.8 clear | Hall walkway **3.02**, north landing **4.12**, stair-top landing **1.925**, entrance hall 4.35 | ✓ |
| Basement headroom ≥ 2.2 | 2.60 to the slab, 2.40 under the joists, 2.35 under the trimmer | ✓ |
| Modular grid 0.5 | All wall centrelines, openings and the stair opening snap to 0.5 m. Openings are cut to their own widths inside 0.5 m-snapped kit pieces. | ✓ |

### 1.2 Room sizes (clear, inside finished walls)

| Room | x range | z range | Size (m) | Area (m²) | Phase 1 (m²) | × |
|---|---|---|---|---|---|---|
| Entrance hall | 7.075–11.425 | 0.3–3.925 | 4.350 × 3.625 | 15.8 | 5.0 | **3.15×** |
| Central hall | 7.075–11.425 | 4.075–14.7 | 4.350 × 10.625 | 46.2 (4.5 of it is the stair well) | 13.2 | **3.50×** |
| Living | 11.575–19.7 | 0.3–6.925 | 8.125 × 6.625 | 53.8 | 17.0 | **3.17×** |
| Kitchen | 11.575–19.7 | 7.075–14.7 | 8.125 × 7.625 | 62.0 | 15.0 | **4.13×** |
| Bedroom | 0.3–6.925 | 0.3–9.425 | 6.625 × 9.125 | 60.5 | 18.6 | **3.25×** |
| Bathroom | 0.3–6.925 | 9.575–14.7 | 6.625 × 5.125 | 34.0 | 9.5 | **3.57×** |
| B1 Cellar | 7.075–13.425 | 4.075–14.7 | 6.350 × 10.625 | 67.5 (stair included) | 21.7 | **3.11×** |
| B2 Workshop | 13.575–19.7 | 4.075–14.7 | 6.125 × 10.625 | 65.1 | 14.8 | **4.40×** |

There is no basement under the bedroom, bathroom, entrance hall or the south part of the living room (solid ground). The basement retaining walls are 0.30 m thick, with their inner faces on x = 7.075, x = 19.7, z = 4.075 and z = 14.7.

### 1.3 Openings

| ID | Where | Clear size | Notes |
|---|---|---|---|
| D1 front door | S exterior wall, x 8.45–10.05 | 1.60 × 2.20 | **Double door** (2 × 0.80 leaves), modelled swinging in. On the D1–A2–hall sightline. **Starts shut and locked: one key per puzzle room, any order** (`key_dorm` from Lily's doll, `key_workshop` from the flooded basement, `key_front_3` from the third room; the last two are placeholders until their rooms exist), put in one at a time ("Locked · n/3 keys"), then an ordinary door. Unlocking or opening it ends nothing yet (`Door.Unlocked` / `Opened` are the hooks) |
| D2 bedroom | Wall x = 7.0, z 1.5–2.7 | 1.20 × 2.20 | Hinge S, modelled swinging into the bedroom. Visible through A1 (ref 2's door through the arch); on the D2–A1 sightline. **Starts shut and locked: 1 × `key_bedroom`**, in the top drawer of the chest of drawers beside the bed; the folded note lies on top (Set Up Start Bedroom) |
| D3 bathroom | Wall x = 7.0, z 12–13.2 | 1.20 × 2.20 | Hinge N, modelled swinging into the bathroom. Reached from the north landing. **Starts shut, unlocked** |
| D4 kitchen back door | E exterior wall, z 12.4–13.6 | 1.20 × 2.20 | Hinge N, modelled swinging in. **Shut and locked for good (story lock: no key)**, rattles; attach point for a future east wing |
| D5 basement | Partition x = 13.5, z 12.4–13.6 | 1.20 × 2.20 | Hinge N, modelled swinging into B2. In line with the B1 landing (long basement view). **Starts shut, unlocked** |
| A1 arch | Entrance hall ↔ living, x = 11.5, z 1.2–3.2 | 2.00, springs 2.30, crown 2.65 | Ref 2's arched doorway |
| A2 arch | Entrance hall ↔ central hall, z = 4.0, x 7.6–10.6 | 3.00, springs 2.30, crown 2.75 | Frames the CAM-1 view (the big arch at the top of ref 1) |
| A3 arch | Central hall ↔ kitchen, x = 11.5, z 8.4–10.2 | 1.80, springs 2.30, crown 2.70 | Ref 1's arched doorway on the right |
| O1 cased opening | Living ↔ kitchen, z = 7.0, x 13.5–16.5 | 3.00 × 2.40 | Transom grid above (2.50–2.85, 5 bars). Ref 2's wide opening into the debris room |
| A4 hall arch | Spans the hall at z = 10.5 (the well's north end) | 4.35, springs 2.40, crown 2.85 | Overhead only (no wall). Frames the radiator wall as in ref 1 |
| G1 gate | Stair top, z = 6 | 1.21 | Lockable balustrade gate (decision 6). Built open in the grey-box; closed = yaw −90° about the hinge |

Windows have sills at 0.90 and heads at 2.40:
- WR1: bedroom, W wall, z 2.2–3.8.
- WL1: living, S wall, x 16.6–18.2, boarded.
- WL2: living, E wall, z 3–4.6.
- WK1: kitchen, N wall, x 13.6–15.4.
- WK2: kitchen, E wall, z 9.4–11.
- WB1: bathroom, N wall, x 2.4–3.4, frosted, sill 1.50.
- Basement hoppers, at y −0.75 to −0.30 with light wells: BW1 (N wall, x 10–11.4) and BW2 (E wall, z 9–10.4).

### 1.4 Stair maths (rise 0.19 / run 0.28, re-derived for revision 2)

| Quantity | Value |
|---|---|
| Storey | Ground-floor slab 0.25 (−0.25..0) + basement clear 2.60 → basement floor y = **−2.85** (2.40 under the 0.20 joists) |
| Total drop | 2.85 m, so **15 risers × 0.19** (exact, no odd riser; inside the 0.18–0.20 band) |
| Treads | **14 × 0.28**, so **total run 3.92 m** (z 6 → 9.92) |
| Pitch | atan(0.19 / 0.28) = **34.2°** (CharacterController slope 45° ✓; step 0.19 < stepOffset 0.30 ✓) |
| Comfort 2R + G | 0.66 m (0.55–0.70 band ✓) |
| Width | **1.21 m** clear between the wall (x 7.075) and the stair rail (x 8.285) |
| Ground-floor opening | 1.25 × **4.50 m**, x 7.075–8.325, z 6–10.5 |
| Landings | Top: the hall floor z 4.075–6 (**1.925**, plus the entrance hall through the 3.0 m A2). Bottom: z 9.92–14.7 = **4.78** |

**Headroom** (vertical from the pitch line through the nosings, y_p(z) = −0.6786 (z − 6) on 6 ≤ z ≤ 9.92):
- **Whole flight:** the flight lies entirely under the open well, so the ceiling above it is the ground-floor ceiling at +3.00. Headroom is 3.00 at the top nosing and grows down the flight (5.66 at the last nosing, measured). ✓ ≥ 2.2
- **Ground-floor slab edge:** the well ends at z = 10.5, 0.58 m past the last riser. The trimmer underside is at −0.5, the floor below at −2.85, so the clearance there is **2.35** ✓.
  - The pitch-line rule needs the slab edge at z ≥ 6 + (2.20 + 0.50) / 0.6786 = **9.98**. The actual edge is at 10.5, a 0.52 m margin.
- **Basement:** 2.60 to the slab and 2.40 under the joists, so 2.35 (under the trimmer) is the lowest walkable clearance anywhere in the basement ✓.

### 1.5 Player and zombie clearances

| Item | Value |
|---|---|
| Player (`Player.prefab` CharacterController) | Radius **0.35**, height **1.8**, stepOffset 0.3, slope 45. Eye at **1.65** (CameraPivot), vertical FOV **60** |
| Zombie (`Zombie.prefab` NavMeshAgent) | Radius **0.35**, height **1.8**, agentTypeID **0** (Humanoid) |
| Bake agent (decision 1, added in Phase 2) | **"BungalowZombie": radius 0.30, height 1.80, climb 0.30, max slope 40°, voxel 0.05**, min region 0.5 m². The Humanoid agent is unchanged. Zombie agents keep radius 0.35 for avoidance |
| Measured in Phase 2 (Unity, revision 2) | Navmesh strip 0.56 m through every 1.2 m door (0.96 through D1), 1.27–2.39 m through the arches and O1, 0.61 m down the stair. Player capsule spare: doors +0.25, stair +0.14, arches/halls ≥ +0.55 |
| Furniture rule | Every walking route between furniture is ≥ 1.2 m (doorway width) and hallways stay ≥ 1.8 m clear of furniture. Groupings sit against walls or float with ≥ 1.2 m around them |

### 1.6 Sightlines (direction 12: the blink mechanic needs long views)

Only 1–3 zombies are active at a time, and the player must see one jump forward after a blink, so the plan keeps three long straight views through aligned openings (drawn orange on the plan):
- **D1 → A2 → hall walkway → radiator wall** (x ≈ 9.25): **14.8 m** from the front door to the north wall, past the stairwell. CAM-1 looks down it.
- **Bedroom → D2 → entrance hall → A1 → living room** (z ≈ 2.1): **18.6 m** wall to wall across three rooms.
- **Living → O1 → kitchen** (x ≈ 15.0): **14.2 m**, S wall to the kitchen window WK1.
- Basement: B1 landing → D5 → B2 east wall (z ≈ 13.0), about 12 m.
- Fog visibility is ~10 m, so the far ends of these views fade into fog: a zombie is visible as a silhouette before it is lit.

## 2. Room-by-room

Each room lists the ref it is based on and why. Material names are defined in §3.

### 2.1 Entrance hall (4.35 × 3.63), from ref 1
- **Why ref 1:** it shares ref 1's pale wallpaper and dark arches. CAM-1 stands here (in A2) looking into the central hall. Seen from the living room, A1 shows this space with D2 beyond, which is exactly the "arch, short passage, closed panel door" in ref 2.
- **Walls:** `M_Wall_Pale` above a 0.9 m dado line, with `M_Trim` skirting, dado and picture rail. **Floor:** `M_FloorPlank` with a runner rug (`M_Atlas_Soft`). **Ceiling:** `M_Plaster`.
- **Fixtures:** E1 flush glass dome.
- **Props:** coat stand, umbrella stand, shoes, a fallen box, paper debris.
- **Decay:** water stain spreading from the front-door corner, the D1 leaf hung 3° off.

### 2.2 Central hall and stairwell (4.35 × 10.63), from ref 1
- **Why ref 1:** an open stairwell on the west side wrapped by a **dark-stained turned-baluster balustrade with newel posts** (east side 4.5 m plus the north end 1.33 m). The walkway (3.02 m) runs on the east side past the A3 arch; the north landing (4.12 m deep) is big enough for a second grouping (hall bench / chair by the radiator).
  - At the north end, the A4 arch frames the radiator wall: pale patterned wallpaper, a radiator, two shaded brass sconces either side of a framed picture.
  - The 3-arm chandelier hangs over the far end of the well and a potted plant stands on the right, all as in ref 1.
- **Walls:** `M_Wall_Pale`, `M_Trim` (skirting, picture rail, arch casings).
  - Stairwell walls below floor level are `M_Plaster`, with `M_BrickWhite` in the basement part.
- **Floor:** `M_FloorPlank` with a patterned runner on the walkway.
- **Stair:** treads in `M_FloorPlank` with chipped nosings cut into the mesh; risers, stringers, balusters, handrail and newels in `M_Trim`.
- **Fixtures:** H1 3-arm chandelier (fabric drum shades), H2/H3 brass sconces, H4 shaded table lamp on the console (the brief's ref-1 lamp), H5 sconce over the well (broken, off), H6 sconce on the walkway E wall (revision 2).
- **Props:** console table (WoodenTable_01), potted plant, radiator (kit), framed picture, runner.
- **Decay:** sagging boards near the well, two missing balusters, one cracked newel cap, wallpaper lifting at the seams.

### 2.3 Living room (8.13 × 6.63), from ref 2 (primary)
- **Why ref 2:** ref 2 is the primary for this room, its materials, its lighting and its post. CAM-2 stands in the SE corner (behind it the boarded WL1 and the WL2 window) looking WNW. The frame then shows:
  - **left (S) wall:** oval portrait over the sideboard, wallpaper torn to plaster and lath;
  - **far (W) wall:** A1 arch on the left, then the tulip sconce L3 and the landscape painting;
  - **right (N) wall:** the wide cased opening O1 with its transom into the debris-strewn kitchen;
  - **ceiling:** the fan L1 near the middle of the frame.
- **Walls:** `M_Wall_Damask`, deep red. Peels show `M_Plaster`, with lath strips (`M_Trim`) behind the larger holes; damp tide-lines are `M_Decals`.
- **Floor:** `M_Parquet` (herringbone).
- **Ceiling:** `M_Plaster` with brown water stains (decals).
- **Trim:** `M_Trim`: dark varnished skirting, arch casing, O1 casing with transom bars.
- **Fixtures:**
  - L1: 4-blade ceiling fan with a 3-tulip glass light kit and a pull chain.
  - L2: shaded table lamp with a white shade on the end table (realtime-shadow hero).
  - L3: tulip-shade wall sconce.
  - L4: small sconce on the O1 pilaster (dim).
- **Props:** Sofa_01 and ArmChair_01 (both re-tinted to dark worn fabric); glass-top coffee table with bottles, cups and magazines; ornate oriental rug; GothicCommode_01 sideboard; side table with a doily and a VCR (foreground left in ref 2); speaker cabinet; 4 cardboard boxes; magazines and paper debris on the floor; landscape painting (fancy_picture_frame_01) and an oval portrait.
- **Layout:**
  - main grouping: rug and sofa at about (15.2, 3.9), turned to yaw 297 so the sofa back faces CAM-2; glass-top coffee table at (14.0, 3.3);
  - armchair at (12.8, 1.6) facing it; L2 on its end table at (15.6, 5.3);
  - second grouping (revision 2): a reading corner in the NE corner at (18.4, 5.4) (armchair instance, side table, magazines);
  - the sideboard on the S wall at x 12.5–14.3; the landscape painting and L3 on the W wall around z 5.3–6.0 (CAM-2 places them right of the fan).

### 2.4 Bedroom (6.63 × 9.13), from ref 3 (primary)
- **Why ref 3:** ref 3's green farmhouse look goes here, with the **whitewashed brick fireplace** on the south (front) wall. The chimney breast is 2.0 × 0.45 m (x 2.4–4.4) with a 0.8 × 0.8 firebox, cold, and a mantel at 1.25 (architecture grows a little with the room; furniture does not).
  - The player **wakes up here**: `WakeUpSequence` (on GameSystems) poses the player lying on a bed. Placing the spawn so the first view after waking is the ref-3 frame makes the opening shot the match shot.
  - From CAM-3 (beside the bed foot, looking SSW): door D2 on the **left** wall opens to the paler entrance hall (ref 3's lighter room through the doorway). The fireplace sits at centre-left, the rocking chair in front of it, and the curtained window WR1 with grey daylight on the right, above the plaid settee.
- **Walls:** `M_Wall_Floral` (stained green floral, mouldy at the ceiling line). **Floor:** `M_FloorPlank` (dark worn). **Ceiling:** `M_Plaster`.
- **Trim:** `M_Trim`, painted dark (the painted-wood strip of the trim sheet). **Fireplace:** `M_BrickWhite` with a `M_Concrete` hearth and an `M_Trim` mantel.
- **Fixtures:** R1 shaded table lamp on a small table in the SW corner by the window (lit), R2 bedside lamp (dim), RW1 baked window light (grey-green daylight).
- **Props:**
  - GothicBed_01 (hero) against the N wall (z = 9.5 partition) at x 2.6–4.2 and ClassicNightstand_01 east of it; P0 is beside the bed's east side.
  - Rockingchair_01 at (3.9, 2.0) in front of the fireplace and the plaid settee under WR1.
  - Second grouping (revision 2): wardrobe on the W wall (z 6.0–7.6), dresser on the N wall, a reading chair at (1.4, 5.0).
  - Low wooden coffee table, patterned black-and-white rug, mantel clock and ornaments.
  - Framed picture, curtains.

### 2.5 Kitchen (8.13 × 7.63), extrapolated in the ref-2/ref-3 style
- **Why:** ref 2's adjacent room (light, stained, debris-strewn floor) and ref 3's "lighter blue-white room" both read as a kitchen. A3 gives ref 1's arched doorway on the right, and K1 is the ceiling light seen far through it.
- **Walls:** `M_Plaster`, tinted blue-white, with a `M_Tile` splashback. **Floor:** `M_Lino` (old linoleum), heavily stained (decals) with debris. **Ceiling:** `M_Plaster`.
- **Fixtures:** K1 and K2 flush glass domes (K2 added in revision 2 for the bigger room); KW1/KW2 baked window light.
- **Props:** painted_wooden_cabinet (white, base unit), painted_wooden_cabinet_02 (dresser), sink unit under WK1, electric_stove, fridge, table and 4 painted chairs (one knocked over) in the middle at (14.7, 10.8), a second dresser on the W wall, dishes, pans, debris.

### 2.6 Bathroom (6.63 × 5.13), extrapolated
- **Walls:** `M_Tile` (dirty, broken white tiles) to 1.2 m, `M_Plaster` above. **Floor:** `M_Lino`. **Ceiling:** `M_Plaster` with mould.
- **Fixtures:** W1 shaded vanity sconce over the mirror; WW1 frosted window light.
- **Props:** clawfoot tub along the S wall, toilet on the W wall, pedestal sink and mirror cabinet on the N wall, towel, rust streaks. Zombie Z4 stands here, dormant.
- **Puzzle P2, Lily's doll** ([LILYS_DOLL_ROOM.md](../puzzles/LILYS_DOLL_ROOM.md)): the chair (`Bath_Chair`) stands against the W wall at (0.6, 12.6), facing D3, with the doll on it. Keep the line from D3 to the chair clear. **Don't spawn Z4 here** while the puzzle is in this room (the `Spawn_Z4` marker is still at (2.5, 12.5)): a zombie making you turn away is an unfair death.

### 2.7 Basement: B1 Cellar (6.35 × 10.63) and B2 Workshop (6.13 × 10.63) (y −2.85), flood-damaged
- **Story fit:** `STORY_PLAN.md` puts the flood puzzle (P6) and the generator finale (P7) here.
- **Walls:** `M_BrickWhite` (limewashed cellar brick) with a flood tide-line at about 0.5 m and efflorescence (decals). **Floor:** `M_Concrete` under **0.08 m of standing water** (`M_Water` plane at y −2.77).
- **Ceiling:** slab underside in `M_Plaster`, joists in `M_Trim` (raw wood strip), pipes and conduit in `M_Trim` (painted metal strip).
- **Fixtures:**
  - C1: bare-bulb pendant at the stair foot, flickering, realtime shadows (hero).
  - C2: bare bulb in the workshop, off until the generator runs.
  - CW1/CW2: baked hopper-window light.
- **Props:** 2 × steel_frame_shelves_01 with jars and boxes, 3 wooden crates, workbench, portable_generator (hero, P7), floating paper and boxes, trash bags.
- **Puzzle P6, the flooded basement** ([FLOODED_BASEMENT.md](../puzzles/FLOODED_BASEMENT.md)), in B2 behind D5: drain in the middle (16.64, 9.39), bath with a running tap against the E wall facing D5, chest of drawers and tall cupboard on the N wall, tool locker and crate on the W wall. Keep the line from D5 to the drain clear. The puzzle's bare bulb lights it; C2 stays off for P7.

### 2.8 Vents (crawl routes out of rooms a clown can trap you in)
Data: `bungalow_layout.VENTS` (checked by `check()`); runtime `Blink.Interaction.Vent`; set up by `BLINK/Bungalow/Set Up Vents` (`Editor/BungalowVents.cs`).

| Vent | End 0 | End 1 | How |
|---|---|---|---|
| **Vent_A** | Bedroom grate (6.3, 7.85) | Bathroom grate (6.3, 10.40) | Crawl duct x 6.0–6.6, y −0.80 to −0.30, z 7.55–10.70, under `Wall_Z9p5`. Both rooms have one door (D2 / D3), so this is the way out when a clown blocks it. |
| **Vent_B** | B1 cellar wall grate at the stair foot (9.0, 14.7) | Parlour floor grate (9.0, 18.7) | `VB1` opening low in `Wall_B_N`, a duct x 8.7–9.3, 0.5 high, north under the corridor to z 19.0, then a shaft up to the parlour floor (grate clear of D7's swing). **Not B2**: B2 is the flooded-basement puzzle room. |

- **Geometry:** a floor end is a 0.6 m `floor_holes` entry cut through the slab; a wall end is a `kind="vent"` opening with the grate stood upright on the wall. A duct is a list of boxes, built by `build_ducts` (`M_Concrete`, inside faces only, open where boxes, openings and holes meet, no collider). `FloorGrate` is a cast-iron prop (`castiron` cell, 0.68 m square, 0.025 m high, box collider), so the floor stays walkable and the navmesh is unchanged.
- **Lighting:** the duct is lightmapped with the shell, and the lighting builder lays light probes down `layout.ducts`. Light only gets in through the grates; inside, the flashlight is the light.
- **Play:** interacting at an end dips the screen to black and puts you in the duct. Forward / back crawls along the path. At an end, Interact peeks out through the grate and again climbs out. Getting in or out clanks the grate (`Noise`, 8 m). While you're inside, zombies lose sight of you and nobody follows.

---

## 3. Materials (21: the cap is raised to 21 for revision 3's `M_Wall_Parlour`, §8.4)

**Conventions:**
- Every texture is **1024²** (CC0 1K downloads, used as is or repacked by script).
- Maps:
  - Base Map: sRGB.
  - Normal: OpenGL Y+ (`nor_gl` / `NormalGL`), imported as Normal Map.
  - MetallicSmoothness: linear. R = metallic, A = 1 − roughness (from `arm`: R = AO, G = rough, B = metal). G = AO, so the same texture is wired as the Occlusion map too. URP reads occlusion from G.
- Import settings: mipmaps on, BC7, max size 1024.
- All Poly Haven and ambientCG assets are CC0. Both licence pages were opened on 7 Nov 2026:
  - Poly Haven: "Our assets are all licensed as CC0".
  - ambientCG: "Creative Commons CC0 1.0 Universal License", no credit needed.
- Every ID below was checked against the live APIs (`api.polyhaven.com/info`, `api.polyhaven.com/files`, `ambientcg.com/api/v2`). Each exists and has 1K maps.

| # | Material | Surfaces | CC0 source(s) (1K) | Maps used |
|---|---|---|---|---|
| 1 | `M_FloorPlank` | Entrance hall, hall, bedroom floors; stair treads | Poly Haven [dark_wooden_planks](https://polyhaven.com/a/dark_wooden_planks) (Amal Kumar, 2 × 2 m tile) | diff, nor_gl, arm |
| 2 | `M_Parquet` | Living-room floor | Poly Haven [herringbone_parquet](https://polyhaven.com/a/herringbone_parquet) (Jenelle van Heerden, Sergej Majboroda, 3.4 m tile), tinted dark | diff, nor_gl, arm |
| 3 | `M_Lino` | Kitchen and bathroom floors | Poly Haven [old_linoleum_flooring_01](https://polyhaven.com/a/old_linoleum_flooring_01) (Charlotte Baglioni) | diff, nor_gl, arm |
| 4 | `M_Concrete` | Basement floor, hearth, light wells | Poly Haven [concrete_floor_damaged_01](https://polyhaven.com/a/concrete_floor_damaged_01) (Rob Tuytel) | diff, nor_gl, arm |
| 5 | `M_Wall_Damask` | Living walls (ref 2) | Base: Poly Haven [decrepit_wallpaper](https://polyhaven.com/a/decrepit_wallpaper) (Rob Tuytel). Damask pattern self-authored (numpy), multiplied into the albedo in deep red | diff (+ pattern), nor_gl, arm. **Shares** its normal and mask with #6 |
| 6 | `M_Wall_Pale` | Entrance and hall walls (ref 1) | Same decrepit_wallpaper base + a self-authored pale stripe or small-motif pattern | diff (+ pattern); shares nor/mask with #5 |
| 7 | `M_Wall_Floral` | Bedroom walls (ref 3) | ambientCG [Wallpaper002C](https://ambientcg.com/view?id=Wallpaper002C) (mouldy paper) + a self-authored green floral pattern | Color (+ pattern), NormalGL, Roughness, AO |
| 8 | `M_Plaster` | All ceilings, kitchen walls, bathroom upper walls, exposed plaster in peels, basement slab | Poly Haven [white_plaster_02](https://polyhaven.com/a/white_plaster_02) (Rob Tuytel) | diff, nor_gl, arm |
| 9 | `M_BrickWhite` | Fireplace and chimney breast, basement walls | Poly Haven [whitewashed_brick](https://polyhaven.com/a/whitewashed_brick) (Charlotte Baglioni) | diff, nor_gl, arm |
| 10 | `M_Tile` | Bathroom dado, kitchen splashback | ambientCG [Tiles133D](https://ambientcg.com/view?id=Tiles133D) (old, broken, dirty white tiles) | Color, NormalGL, Roughness, AO |
| 11 | `M_Trim` (**trim sheet**, 1K, 8 horizontal strips) | Skirting, dado and picture rails, cornice, architraves, arch casings, O1 transom, door leaves, window sashes, stair stringers, risers, balusters, handrails, newels, joists, lath, radiators, pipes, fixture arms | Varnished-wood strips: ambientCG [PaintedWood008C](https://ambientcg.com/view?id=PaintedWood008C). Raw lath/joist strip: Poly Haven [worn_planks](https://polyhaven.com/a/worn_planks) (Dimitrios Savva). Painted-metal and brass strip: Poly Haven [rusty_metal_02](https://polyhaven.com/a/rusty_metal_02) (Rob Tuytel) | Composited by script into one Base, Normal and Mask |
| 12 | `M_Shade` (emissive) | Lamp shades, glass tulips, domes, bulbs, chandelier drums | ambientCG [Fabric032](https://ambientcg.com/view?id=Fabric032) (white weave) | Color (also the emission mask), NormalGL |
| 13 | `M_Glass` | Window panes, picture glass, mirror (transparent) | Self-authored smudge and dirt mask (numpy, textures.py style) | Base (alpha), Mask |
| 14 | `M_Water` | Basement flood plane | Self-authored tiling ripple normal (numpy) | Normal, Mask |
| 15 | `M_Decals` (alpha atlas, mesh decals) | Leaks, ceiling stains, mould blooms, flood tide-line, floor stains, paper scraps, plaster crumbs, peel edges, scuffs | ambientCG decals [Leaking013A](https://ambientcg.com/view?id=Leaking013A) and [Leaking017B](https://ambientcg.com/view?id=Leaking017B); mould cut from Wallpaper002C; paper and crumbs self-authored | Base (alpha), Normal, Mask |
| 16 | `M_Atlas_Soft` (prop atlas, as built: 18 cells, 98 % fill) | Sofa, 2 armchairs, reading chair, sofa cushions, plaid settee, curtains, bed blanket, towel, 3 rugs and 2 runners | Re-baked Poly Haven Sofa_01, ArmChair_01 (both re-tinted dark), GreenChair_01, throw_pillows_01. Plaid: self-authored tartan woven on Poly Haven [fabric_pattern_05](https://polyhaven.com/a/fabric_pattern_05) (Rob Tuytel). Rugs: self-authored oriental / geometric / runner patterns (seeded numpy) on the pile of Poly Haven [dirty_carpet](https://polyhaven.com/a/dirty_carpet) (Rohit Seervi). Curtain, blanket, linen, towel: self-authored. A rod and leg wood swatch cropped from `M_Trim` | Base (sRGB), Normal (GL), Mask (R metal, G AO, A smooth) |
| 17 | `M_Atlas_Wood` (prop atlas, 15 cells, 92 %) | Bed, rocking chair, sideboard / dresser, nightstands, hall console, low coffee table, bookshelf, grandfather clock, corner table, wardrobe, glass coffee-table frame, coat stand, fan blades | Re-baked Poly Haven GothicBed_01, GothicCommode_01, Rockingchair_01, ClassicConsole_01, ClassicNightstand_01, WoodenTable_01, wooden_bookshelf_worn, vintage_grandfather_clock_01, side_table_tall_01. Swatches cropped from the `M_Trim` sheet (varnish ×2, dark paint, raw, brass) + iron | as #16 |
| 18 | `M_Atlas_Small` (prop atlas, 37 cells, 80 %; **alpha-tested**, cutoff 0.5) | Pictures and canvases, oval portrait, plant, mantel clock, vases, suitcases, side table, hall bench, boxes, pans, wall clock, magazines, VCR, speaker, doily, bottles, cups, dishes, paper, plaster chunks, all fixture metalwork (brass, iron, bakelite) | Re-baked Poly Haven anthurium_botany_01 (alpha leaves), fancy_picture_frame_01, hanging_picture_frame_02 (+ its artwork), mantel_clock_01, vintage_suitcase, small_wooden_table_01, painted_wooden_bench, cardboard_box_01, ceramic_vase_01/03, wall_clock, pot_enamel_01, brass_pan_01. Self-authored: landscape, portrait (decision 9) and seascape canvases, magazine covers, VCR, speaker, doily lace (alpha), paper, newspaper, glass, ceramic, labels. Also a plaster crop of `M_Plaster` and trim swatches | as #16; Base alpha = cutout |
| 19 | `M_Atlas_Utility` (prop atlas, 24 cells, 95 %) | Generator, stove, kitchen cabinets / dresser / chairs, steel shelves, crates, barrel, jerrycan, fuse box, vice, buckets, trash bags, sink unit, fridge, tub, toilet, pedestal sink, mirror cabinet, workbench, kitchen table, jars | Re-baked Poly Haven portable_generator, electric_stove, steel_frame_shelves_01, painted_wooden_cabinet, painted_wooden_cabinet_02, painted_wooden_chair_01, wooden_crate_01/02, trashbag, barrel_03, metal_jerrycan_green, power_box_01, bench_vice_01, wooden_bucket_01. Self-authored enamel (white and cream, chipped and rusted), cast iron, steel, jar glass, paper. Trim swatches (light paint, raw, painted metal) | as #16 |
| (20) | `M_Plaster_Kitchen` | The 20th slot went to the kitchen plaster (Phase 3/4). The rugs did **not** need it: they tile inside `M_Atlas_Soft` (§3.5) | n/a | n/a |

**Shared textures and atlases:**
- `M_Wall_Damask` and `M_Wall_Pale` share one normal and one mask.
- The whole modular kit shares the `M_Trim` trim sheet.
- All modular pieces share one 1K **kit wear atlas** on UV2 (Unity `mesh.uv2` = TEXCOORD1, shared with the lightmap layout): R = baked AO, G = edge wear, B = dirt (cavity × upward-facing), A = polish (nosings, handrail tops, door edges). URP reads it through `SG_BungalowLit` (§3.0.2; Q2 answered by decision 2).
- Props are packed into 4 atlases (§3.5).

### 3.5 Prop atlases (Phase 5, as built)

`python -I tools/blender/bungalow_prop_textures.py --src <download root>` writes two things:
- `Textures/Atlas_<Soft|Wood|Small|Utility>/Atlas_*_Base / _Normal / _Mask.png`, all **1024²**. No 2K atlas was needed.
- `Props/bungalow_prop_atlas.json`, the cell UV rects.

**How each kind of cell is made:**
- **Poly Haven models are re-baked in image space.** A model keeps its own 0..1 UV layout, and the build remaps the decimated mesh's UV0 into its cell. This is exact, fast and re-runnable, and needs no Cycles bake.
  - Its 1K maps are resampled into the cell: diff, nor_gl, and arm repacked to R metal, G AO, A 1 − rough.
  - The maps are graded towards the abandoned look: dust and desaturation. For ref 2, the light sofa and armchair fabric is re-tinted to dark worn upholstery; the mattress becomes stained linen.
- **Self-built props use swatch cells:** 0.25–1 m real-world crops of trim-sheet strips or seeded procedural materials. Each part's metric box or cylinder UVs are scaled into the cell, and compressed (never stretched) when the part is bigger than the swatch.
- **Rugs and runners are 9-slice meshes:** corner, border and field quads. Field tiles are mirrored in alternation, so the repeat has no seams. A 256 px field tile covers 0.5 m, about 5 px/cm, so the 20th material slot was not needed.

**Finishing:**
- Each cell has a 4 px edge-extended gutter (2 px on 64 px cells), so mips don't bleed between cells.
- `M_Atlas_Small` is alpha-tested (plant leaves, doily lace) and preserves mip coverage. The other three are opaque URP Lit.
- Wear is baked into the atlas albedo (dust, grime, chips), so the props don't need `SG_BungalowLit`.
- **Fill:** Soft 98.4 %, Wood 92.2 %, Small 80.5 %, Utility 95.3 %.
- **Importers** (`BungalowPropsPlacer.BuildAssets`):
  - 1024, mipmaps, trilinear, aniso 8, clamp, Standalone **BC7**.
  - Base sRGB, Normal as Normal Map, Mask linear with alpha.

### 3.0 As built (Phase 4: surface materials #1–#15)

Built by script, re-runnable:
1. `python -I tools/textures/bungalow_surfaces.py fetch <cache>` downloads the 1K PNG maps (one new folder per asset, outside the repo) and writes `<cache>/sources.json` with the authors and real-world sizes from the live APIs.
2. `python -I tools/textures/bungalow_surfaces.py build <cache>` repacks, grades and authors everything into `Art/Environment/Bungalow/Textures/<MaterialName>/` (1024² PNG).
3. `python -I tools/textures/bungalow_shadergraph.py` writes `Shaders/SG_BungalowLit.shadergraph`.
4. Unity menu **BLINK/Bungalow/Build Surface Materials** (`Materials/Editor/BungalowMaterialsBuilder.cs`) applies the import settings and creates or updates `Materials/M_*.mat` in place. It also wires `Textures/Trim/` and `Textures/KitWear/` as soon as Phase 3 delivers them.
5. **BLINK/Bungalow/Build Material Preview Scene** (`BungalowMatPreview.cs`) builds the throwaway `Scenes/House_Bungalow_MatPreview.unity`.

#### 3.0.1 Material table (as built)

**UV0 contract** (Phase 3, `bungalow_layout.SURFACE_TILE_M`): kit surfaces are box-mapped at **1 UV = one texture tile**, so every kit material uses **tiling 1**, and the tile size column below is the real-world size of one texture repeat. The grey-box is metric (1 UV = 1 m); the preview scene divides its UV0 by the same table. `BungalowMaterialsBuilder.SurfaceTileM` mirrors the table; keep them in sync.

| # | Material | Shader | Textures (all 1024², BC7, mipmaps) | Tile (m), material tiling | Source (CC0) and processing |
|---|---|---|---|---|---|
| 1 | `M_FloorPlank` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 2.0, 1 | Poly Haven `dark_wooden_planks` (Amal Kumar). Graded to mean sRGB (66, 52, 41), warm dark brown (refs 1, 3) |
| 2 | `M_Parquet` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 3.4, 1 | Poly Haven `herringbone_parquet` (Jenelle van Heerden, Sergej Majboroda). Tinted dark, mean (76, 53, 38), sat 0.8. Smoothness ×0.7 (the scan is too glossy for ref 2) |
| 3 | `M_Lino` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 2.0, 1 | Poly Haven `old_linoleum_flooring_01` (Charlotte Baglioni). Desaturated (0.45) and dulled to (128, 112, 86) |
| 4 | `M_Concrete` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 2.5, 1 | Poly Haven `concrete_floor_damaged_01` (Rob Tuytel). Real size 5 m, shown at 2.5 m for crisper cracks |
| 5 | `M_Wall_Damask` | SG_BungalowLit | `M_Wall_Damask_Base` + **shared** `M_Wall_Shared_Normal` / `_Mask` | 2.0, 1 | Paper: Poly Haven `decrepit_wallpaper` (Rob Tuytel); stains carried by its luminance. Pattern self-authored: half-drop **ogee lattice** of leafy vines, a mirrored palmette medallion per cell, rosettes at the tips, 4 × 4 per tile (50 cm repeat), deep maroon tone-on-tone: ground (66, 17, 17), motif (96, 27, 24) |
| 6 | `M_Wall_Pale` | SG_BungalowLit | `M_Wall_Pale_Base` + shared normal and mask | 2.0, 1 | Same paper. Self-authored small sprig motif (4 petals, stem, 2 leaves, alternate flips), half-drop 10 × 10 (20 cm repeat). Cream ground (194, 184, 160), motif (138, 116, 92) |
| 7 | `M_Wall_Floral` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 2.0, 1 | ambientCG `Wallpaper002C` (mouldy paper: mould = multiply, normal, roughness; AO from its displacement). Self-authored trailing floral (cabbage roses, buds, stems, leaves), 4 × 4 half-drop (50 cm), seeded flips/rotations. Sage ground (98, 112, 84), leaves (56, 72, 48), roses (128, 130, 100) |
| 8 | `M_Plaster` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 1.5, 1 | Poly Haven `white_plaster_02` (Rob Tuytel). Real size 1 m, shown at 1.5 m to hide repeats on the big ceilings. Warm off-white (176, 170, 156) |
| 9 | `M_BrickWhite` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 2.0, 1 | Poly Haven `whitewashed_brick` (Charlotte Baglioni). Whitened (196, 190, 180), sat 0.55, contrast 0.85 (ref 3's painted fireplace) |
| 10 | `M_Tile` | SG_BungalowLit | `_Base`, `_Normal`, `_Mask` | 1.5, 1 | ambientCG `Tiles133D` (Color, NormalGL, Roughness, AO). Shown at 1.5 m per tile repeat: ~11.5 cm period tiles (changed from 1.0 in Phase 3) |
| 11 | `M_Trim` | SG_BungalowLit | from `Textures/Trim/` (Phase 3) | sheet, 1 | Material asset built. Until the trim sheet lands it is a flat dark-varnish placeholder: base (62, 42, 30), smoothness 0.42 |
| 12 | `M_Shade` | URP Lit, opaque, both faces, emission | `_Base` (also the emission map), `_Normal`, `_Mask` | 0.5, 2 (props: metric UVs assumed) | ambientCG `Fabric032`, tinted aged parchment (226, 212, 186). Emission #FFD9A0 × 1.5, GI flag None (the point lights carry the light). Per-fixture colour and strength (§5.1) is a Phase 6 job |
| 13 | `M_Glass` | URP Lit, transparent alpha, preserve specular, both faces; base colour tinted cool (0.38, 0.48, 0.62) so panes read blue at night | `_Base` (RGB tint, A = dirt cover 0.10–0.65), `_Mask` | 1.0, 1 | Self-authored: tiling fBm smudge, finger smears, specks. Smoothness 0.96 drops to 0.4 on the smudges |
| 14 | `M_Water` | URP Lit, transparent alpha, preserve specular | `_Normal`, `_Mask` | 4.0, 1 | Self-authored tiling ripple (periodic spectral noise + 3 wave trains → GL normal), scum patches in the mask. Base colour (34, 32, 22, α 0.86), normal 0.55 |
| 15 | `M_Decals` | URP Lit, transparent alpha (queue −20, before glass and water) | `_Base` (RGBA), `_Normal`, `_Mask`, plus `M_Decals_atlas.json` (cell UV rects) | atlas, 1 | Cells: `leak_streaks` (ambientCG `Leaking017B` displacement as the streak mask), `ceiling_stain`, `mould_bloom` (cut from `Wallpaper002C`), `floor_stain`, `scuffs`, `rust_streaks` (`Leaking013A` displacement), `paper_scraps` (4 sheets), `plaster_crumbs`, `peel_edge`, `tide_line` and `grime_band` (both seamless left to right). All self-authored except the noted scan cuts |

**Map conventions as built:**
- `_Base`: sRGB. Alpha only on M_Glass and M_Decals.
- `_Normal`: OpenGL Y+, imported as Normal Map.
- `_Mask`: linear, R metallic, G AO, B 0, A smoothness. Poly Haven `arm` gives R = arm.B, G = arm.R, A = 1 − arm.G; ambientCG gives A = 1 − Roughness, G = AO.
- **Import:** max 1024, mipmaps on, trilinear, aniso 8 (decals 2), Standalone override **BC7**, CompressedHQ. Repeat wrap; the decal atlas uses Clamp.
- **Grading:** done in linear (per-channel gain to a target mean, then saturation and contrast about the mean). Albedo is clamped to sRGB ≈ 0.1–0.94.

**Sources (all CC0 1.0; Poly Haven and ambientCG need no credit, so they are not listed):**
- Poly Haven: dark_wooden_planks (Amal Kumar), herringbone_parquet (Jenelle van Heerden, Sergej Majboroda), old_linoleum_flooring_01 (Charlotte Baglioni), concrete_floor_damaged_01 (Rob Tuytel), decrepit_wallpaper (Rob Tuytel), white_plaster_02 (Rob Tuytel), whitewashed_brick (Charlotte Baglioni).
- ambientCG: Wallpaper002C, Tiles133D, Fabric032, Leaking013A, Leaking017B.
- The decal atlas, the three wallpaper patterns, the glass smudge and the water ripple are self-authored (seeded numpy and PIL).

#### 3.0.2 `SG_BungalowLit` (kit wear shader)

`Shaders/SG_BungalowLit.shadergraph` is a **URP Lit** Shader Graph: metallic workflow, opaque, front faces.
- The URP target supplies the ForwardLit, GBuffer, ShadowCaster, DepthOnly, DepthNormals, MotionVectors and **Meta** passes.
- It therefore works with lightmaps, light probes, **shadowmask**, Forward+ and the SRP Batcher.
- The surface maths lives in `Shaders/BungalowWear.hlsl`, a Custom Function node in file mode, so the graph stays small: properties, UV0 and UV2 feed one node, whose outputs go to Base Color, Normal (TS), Metallic, Smoothness and Occlusion.

**Properties:**
- **URP Lit names:** `_BaseMap` (tiling and offset drive Base, Normal and Mask), `_BaseColor`, `_BumpMap`, `_BumpScale`, `_MaskMap` (R metallic, G AO, A smoothness), `_Metallic` and `_Smoothness` (scales), `_OcclusionStrength`.
- **Wear:** `_WearMap`, the kit wear atlas on **"UV2" = Unity `mesh.uv2` = TEXCOORD1** (Shader Graph channel UV1). This is the same non-overlapping layout the lightmapper uses: Blender's `UVLightmap` layer, packed by `bungalow_wear.py`. The atlas reads the raw mesh UV, not the lightmap scale and offset.
- **Wear strengths and tints:** `_WearAOStrength`, `_EdgeWearStrength`, `_EdgeWearColor` (A = tint amount), `_EdgeWearLighten`, `_DirtStrength`, `_DirtColor` (multiply), `_DirtRoughen`, `_PolishStrength`, `_PolishSmoothness`.

**Wear maths:**
- **Edge wear (G):** lerps the base towards `saturate(base × (1 + Lighten))`, tinted by `_EdgeWearColor`, i.e. exposed lighter wood, backing paper or chipped whitewash.
- **Dirt (B):** multiplies the albedo towards `_DirtColor` and scales smoothness by (1 − `_DirtRoughen`).
- **Polish (A):** raises smoothness to at least `_PolishSmoothness` and darkens the albedo by 10%.
- **Atlas AO (R):** multiplies into Occlusion.

**Neutral when unassigned:** an empty `_WearMap` or `_MaskMap` falls back to Unity's 4 × 4 default texture. The node detects that through the texel size (< 8 px) and treats the slot as neutral: no wear, AO 1, metallic and smoothness from the scalars. Kit materials therefore work before the atlas exists.

**Per-material wear presets** (in `BungalowMaterialsBuilder`):
- Wood: lighter raw-wood edges, polish on.
- Wallpapers: backing-paper edges, no polish.
- Brick: chips show red brick.
- Tile: grey chips.
- Plaster: pale chips.

#### 3.0.3 Checkpoint renders (Phase 4)

The renders are in the session scratch folder `bungalow/phase4/`.
- **Blender 5.1 Cycles vs Unity URP** on the same wall-and-floor sample under matched neutral light (`tools/blender/bungalow_materials_compare.py` vs `BungalowMatPreview.RenderCompare`). Mean sRGB deltas: wall / floor −6 / −9 (Damask), −2 / −5 (FloorPlank), −11 / −12 (BrickWhite). Unity is slightly darker because URP applies the mask AO to ambient light; Cycles adds wall-to-floor bounce.
- **Swatch sheet** of all 15 materials.
- **CAM-1/2/3 grey-box previews** with these materials (preview point lights at the §5.1 positions, no GI, no post) next to refs 1–3.

**Atlas texel density (1K):**
- `M_Atlas_Soft` holds about 6 m² of visible upholstery, so roughly 4 px/cm. That's adequate at night under fog; if it isn't, use the spare slot.
- `M_Atlas_Small` holds about 40 small items.

#### 3.0.4 `M_Trim` trim sheet and the kit UV contract (Phase 3)

**Build:** `python -I tools/blender/bungalow_trim.py --download <dir>` (one new folder per source) then `python -I tools/blender/bungalow_trim.py --src <dir>` composites `Textures/Trim/Trim_Albedo.png` (sRGB), `Trim_Normal.png` (OpenGL Y+) and `Trim_Mask.png` (linear: R metallic, G AO, B 0, A smoothness), all 1024². The strip table is `bungalow_trim.STRIPS`; the kit reads it, so the geometry and the texture can't drift apart.

**Layout:** 8 horizontal strips of 128 px. Strip *k* covers V = *k*/8 .. (*k*+1)/8, with V = 0 the bottom row of the image (Blender and Unity UVs). Kit UVs stay **4 px inside** their strip (the V ranges below), so mips don't bleed. Every strip tiles along U. **1 UV = 2 m** in both directions, so the wood grain runs along U at its real scale and a strip is 0.25 m tall. A profile or face wider than 0.25 m is compressed across the grain to fit.

| Strip | V range (inset) | Key | Look | Used on | Source (CC0) |
|---|---|---|---|---|---|
| 0 | 0.0039–0.1211 | `varnish` | Dark-stained varnished wood, long grain, smoothness 0.3–0.7 | Skirting, dado and picture rails, cornice, door and window casings, linings, A1–A4 and O1 casings, stringers, sashes (varnished rooms) | Poly Haven `dark_wood` (Dimitrios Savva, Dario Barresi, Rico Cilliers), de-reddened |
| 1 | 0.1289–0.2461 | `varnish_door` | Warmer varnish, more worn | Door leaves in varnished rooms (D1), risers, thresholds | `dark_wood`, another band |
| 2 | 0.2539–0.3711 | `varnish_turned` | Darkest varnish, tight grain | Turned balusters, newels, handrails, O1 transom spindles | `dark_wood`, another band |
| 3 | 0.3789–0.4961 | `paint_dark` | Dark olive-brown paint over wood (ref 3) | Bedroom trim, D2 leaf, WR1, the mantel | ambientCG `PaintedWood008C` |
| 4 | 0.5039–0.6211 | `paint_light` | Off-white chipped paint | Kitchen and bathroom trim, D3 / D4 leaves, their windows, the bathroom tile cap | `PaintedWood008C`, recoloured |
| 5 | 0.6289–0.7461 | `raw` | Raw weathered board (one board, grain along U) | Joists, trimmers, header, under-stair boarding, D5 ledged door and its casings, WL1 boards | Poly Haven `worn_planks` (Dimitrios Savva), rotated 90° |
| 6 | 0.7539–0.8711 | `metal_paint` | Cream paint on iron, ~20 % rust | Radiators, pipes and hangers, G1 gate, firebox lintel, hopper sashes | Poly Haven `rusty_metal_02` (Rob Tuytel), tiled to 2 m |
| 7 | 0.8789–0.9961 | `brass` | Tarnished brass, **metallic** (R 1, 0.2 where tarnished) | Knobs, backplates, hinges, gate pins and latch, radiator valves | `rusty_metal_02` normals / tarnish mask, self-authored brass |

**Which strip a piece uses** comes from the room it faces: `ROOM_FINISH[room].trim` → `TRIM_STYLE` (halls and living = varnish 0/1/2, bedroom = 3, kitchen and bathroom = 4, basement = 5). Casings are styled per wall face, so a door between the hall and the bedroom is varnished on the hall side and painted on the bedroom side. The arches A1–A4 and O1 are always varnished (refs 1 and 2).

**UV0 contract** (all kit meshes): surface materials are world box-mapped at **1 UV = one tile** of `bungalow_layout.SURFACE_TILE_M` metres, so their materials use tiling 1. The table is exported to `bungalow_layout.json` (`surfaceTiles`), and `BungalowMaterialsBuilder.SurfaceTileM` reads it from there. `M_FloorPlank` boards are cut on the scan's own seams (`FLOOR_PLANK_SEAMS_PX`), so the material must keep the scan unrotated with offset 0. Stair treads each map one scan board.

**UV2 contract** (Blender layer `UVLightmap` = Unity `mesh.uv2` / TEXCOORD1): one non-overlapping layout for the whole shell, shared by the lightmapper and the wear atlas.
- Each mesh is Smart-UV-unwrapped on its own and placed as **one compact, pixel-snapped rectangle** in the atlas, so its UV2 bounds are tight. Islands are at least 3 px apart.
- Texel density is weighted per mesh: trim and detail pieces ~37 px/m, plank floors ~29, walls ~19, joists ~19, ceilings ~12 (AO in the corners only), outside faces ~7, the ground ~4.
- `Textures/KitWear/KitWear.png` (1024², RGBA, **linear**, clamp, BC7) holds the wear:
  - **R**: AO (Cycles, 0.35 m). 1 = open.
  - **G**: edge wear. Convex edges (bevel normal vs face normal, 3 cm, gated by local AO, noise-broken), plus foot-traffic wear on up-facing surfaces away from walls (worn paths on the floors, tread middles).
  - **B**: dirt. Cavity × up-facing, grime along the skirting and in floor corners, and a 0.5 m kick / mop band up vertical faces.
  - **A**: polish. From the kit's `polish` face attribute: nosings and tread fronts, handrail tops, newel caps, latch stiles, knobs, thresholds, sills.
  - Outside every island: R = 1, G = B = A = 0.
- Regenerate with `build_all.py --target bungalow Bungalow_Shell`. The bake takes ~8 min on the GPU (OptiX). The atlas is only valid with the UV2 of the same build.

**20th material:** `M_Plaster_Kitchen` (kitchen walls only; ceilings stay `M_Plaster`) reuses M_Plaster's maps tinted blue-white (#CCDEF2). It is in `ROOM_FINISH`, `SURFACE_TILE_M` (1.5) and `BungalowMaterialsBuilder`, and uses the spare slot, so the count is now **20 of ≤ 20**.

### 3.1 Reuse from `Unity/Assets/Art/Mansion/PolyHaven/`

Triangle counts were read by importing every FBX in headless Blender 5.1.2. The folder's LICENSE.txt confirms CC0 with Poly Haven links; these assets need no credit.

| Asset | Tris (file) | Use | Target tris |
|---|---|---|---|
| Sofa_01 | 4,101 | Living sofa, re-tinted dark | 2,500 |
| ArmChair_01 | 5,626 | Living armchair | 2,500 |
| GothicCommode_01 | 3,897 | Living sideboard | 2,000 |
| ClassicNightstand_01 | 2,002 | Living end table + bedroom nightstand (2 instances) | 1,200 |
| fancy_picture_frame_01 | 938 | Ref-2 landscape painting (its canvas is a landscape) | 938 |
| GothicBed_01 | 18,741 | Bedroom bed (**hero**) | 5,000 |
| Rockingchair_01 | 11,866 | Bedroom rocker (ref 3) | 3,000 (spindles decimate badly; may need 4k, see risks) |
| WoodenTable_01 | 952 | Hall console | 952 |
| steel_frame_shelves_01 | 4,348 | Basement shelves | 1,500 |
| wooden_crate_01 | 6,576 | Basement crates (×2) | 600 each |

**Not reused:**

| Asset | Tris | Why not |
|---|---|---|
| CoffeeTable_01 | 10,404 | Turquoise ornate, wrong look |
| Chandelier_02 | 21,514 | Crystal multi-arm, not ref 1's 3-arm drum |
| GothicCabinet_01 | 12,170 | Too big for these rooms |
| WoodenChair_01 | 19,992 | Too heavy |
| dining_chair_02 | 22,013 | Too heavy |
| dining_table | 1,048 | 2.26 m, too big for the kitchen |
| marble_bust_01 | 17,456 | Not needed |
| ornate_mirror_01 | 14,246 | Not needed |
| side_table_01 | 2,756 | Pale modern pine |
| vintage_grandfather_clock_01 | 8,582 | Not needed |
| vintage_oil_lamp | 7,208 | Not needed |
| vintage_suitcase | 16,474 | Not needed |
| wine_barrel_01 | 10,820 | Not needed |
| wooden_bookshelf_worn | 10,106 | Not needed |
| wooden_lantern_01 | 8,321 | Not needed |
| wooden_candlestick | **212,992** in the local FBX (Poly Haven lists 3,328) | Different LOD; don't use |

### 3.2 New CC0 models (Poly Haven)

All were checked via `api.polyhaven.com/info`.

| Asset | Author(s) | Tris | Use | Target |
|---|---|---|---|---|
| [anthurium_botany_01](https://polyhaven.com/a/anthurium_botany_01) | Rob Tuytel, Rico Cilliers | 4,134 | Hall potted plant (ref 1) | 1,500 |
| [painted_wooden_cabinet](https://polyhaven.com/a/painted_wooden_cabinet) | Kirill Sannikov | 2,227 | Kitchen base unit | 2,227 |
| [painted_wooden_cabinet_02](https://polyhaven.com/a/painted_wooden_cabinet_02) | Kirill Sannikov | 966 | Kitchen dresser | 966 |
| [electric_stove](https://polyhaven.com/a/electric_stove) | Kuutti Siitonen | 13,914 | Kitchen stove | 2,000 |
| [painted_wooden_chair_01](https://polyhaven.com/a/painted_wooden_chair_01) | Kuutti Siitonen | 724 | Kitchen chairs ×2 | 724 each |
| [portable_generator](https://polyhaven.com/a/portable_generator) | James Ray Cock | 26,419 | Basement generator (story P7, **hero**) | 4,000 |

Everything else is self-built with `lib.py` primitives: fixtures, boxes, bottles, the tub and toilet, the settee, tables, the oval frame, rugs and curtains.

---

## 4. Props and triangle budget (≤ 80k total; props ≤ 3k, hero ≤ 8k)

> Revision 2: tables 4.1–4.4 are the Phase 1 baseline. **§4.5 re-checks the budget at 3× floor area and flags that 80k no longer holds** (Q11).

Sources: **R** = reuse Mansion/PolyHaven, **N** = new Poly Haven CC0, **S** = self-built in Blender. Tris are after decimation.

### 4.1 Modular kit (shell), as built in Phase 3

`tools/blender/bungalow_kit.py` builds the whole shell from the layout tables (`Kit/Bungalow_Shell.fbx`, 97 meshes). The Phase 1 estimate was 26.4k and the revision-2 estimate 39.5k. Decision 13 raised the level cap to ~200k, so the kit carries real bevels, turned work and per-board floors.

| Piece (all generated from `bungalow_layout`) | Tris |
|---|---|
| Walls, inside faces: thickness, openings cut from `OPENINGS`, faces split at room edges and wall bands (bathroom tile to 1.20, kitchen splashback 0.90–1.50), arch spandrels | 2,440 |
| Walls, outside faces (`<wall>_Outside`, low UV2 density) | 870 |
| Floors: plank boards cut on the scan's seams (chamfered V-grooves, ±1.5 mm per-board height, sag by the well and in the bedroom's damp corner, ~0.9 chipped edges per board-metre), parquet / lino / concrete slabs, basement-ceiling undersides, well edges | 12,112 |
| Ceilings (undersides only) | 12 |
| Mouldings: skirting, dado (halls), tile cap (bathroom), picture rail, coved cornice; broken at openings, the well and A4, wrapped round the chimney breast | 3,806 |
| Door frames D1–D5: linings, stop beads, moulded casings on both faces (styled per room), plinth blocks, thresholds | 2,290 |
| Door leaves: D1 double (right leaf ajar 18° and **hung 3° off**, latch corner dropped onto the floor), D2–D4 four-panel raised-and-fielded with beads, brass knobs, backplates and hinges, D5 ledged-and-braced raw boards with strap hinges | 9,700 |
| Arches A1–A3 (lining, wide casings, keystones, plinths), A4 overhead arch (soffit, casings, corbels), O1 cased opening (transom rail and 5 turned spindles) | 4,754 |
| Windows ×6 + 2 hoppers: linings, casings, stools, aprons, outside sills, double-hung sashes (lower sash lifted on some), parting beads, hoppers tilted 14°, WL1 boards | 4,920 |
| Window glass (`<id>_Glass`, panes) | 336 |
| Stair: 14 bullnosed treads + landing nosing with **chipped nosings** cut into the profile, 15 risers, wall and outer stringers on the pitch, under-stair boarding | 4,986 |
| Stair rail: 21 turned balusters on the outer string (the top ones stop under the header), handrail on the pitch, foot newel | 5,580 |
| Well balustrade: 3 panelled newels (**NE cap cracked**: split, one half dropped and tilted, finial knocked askew), shoe rails, handrails, 45 turned balusters with **2 missing** (stubs) | 12,096 |
| G1 gate: painted iron frame, spear-top bars, brass pins and latch, pivot at the hinge | 636 |
| Basement joists (each sagging 2–9 mm), trimmers, header | 1,716 |
| Radiators ×5 (column type, valves and tails) | 15,048 |
| Basement heating pipes: mains, drops to the radiators, elbows, hangers | 2,476 |
| Fireplace: whitewashed brick breast and firebox, steel lintel, concrete hearth, moulded mantel with corbels | 192 |
| Flood water plane (y −2.77, cut round the flight) | 8 |
| Exterior: ground, steps, light wells | 218 |
| **Shell total** | **84,196** |

- `Kit/Bungalow_Kit.fbx` is the piece library: one copy of each type (walls, floors, ceiling, mouldings, door frame, three leaf styles, arch, A4, O1, window, glass, hopper, stair, rail, balustrade, gate, joists, radiator, pipes, fireplace, mantel), 44.6k tris, re-centred at the origin and laid out along +X. The pieces keep the shell's UV2, so they share the wear atlas. The library is not placed in the level.
- The heaviest items are the radiators (15k) and the balustrade (12k). Both can be halved (6-sided columns, 6-segment balusters) if the batch / tris budget needs it in Phase 7.
- Colliders: `Kit/Bungalow_Shell_colliders.json` carries the **grey-box boxes unchanged** (146 boxes, taken from `bungalow_greybox` at build time, keyed to the same object names), so the navmesh sources are identical. The only additions are one nav-ignored box per door leaf. `check()` passes, including the new kit checks: every room has a finish, every door has a leaf, the radiators are on their walls, and the basement headroom under the pipes is 2.21 m.
- Visual clearances vs the grey-box: the stringers stand 3–5 cm above the treads (clear width between them 1.18 m, capsule 0.70), the radiators stand 0.17 m off their walls, and the casings stand 2.4 cm proud of the walls. Board sag is ≤ 24 mm below the collider top (by the well).
- Not in the kit yet: wallpaper peel holes, lath strips and plaster chunks (they need the decal pass, §3 #15, and the room dressing, Phase 5/6).

### 4.2 Fixtures (all S, as built in Phase 5)

Self-built in `tools/blender/bungalow_prop_models.py`. Metalwork is on `M_Atlas_Small`; every shade, glass tulip, dome and bulb is **`M_Shade`** (emissive).
- **Naming:** each fixture prefab `FX_<Model>` has two children:
  - **`<Model>_Emissive`**: a child mesh with only the `M_Shade` part, shadow casting off.
  - **`<Model>_Emitter`**: an empty at the light point.
- **Instances:** the placer names them **`Props/Fixtures/FX_<id>`**, for example `FX_L1/FX_CeilingFan_Emitter`.
- **`Props/fixtures.json`** lists, per fixture id: prefab, mount point and yaw, `emitterLocal`, `emitterWorld` (where the light goes), the layout light position, and the deviation between the two.
- **Mounting:** ceiling fixtures hang from the ceiling (y 3.00; basement slab underside −0.25). Sconces sit on their wall face. Lamps stand on their table's top surface.

| Prefab | Fixtures | Tris each | Tris | Emitter (model space) | Emitter vs layout light |
|---|---|---|---|---|---|
| FX_CeilingFan (4 varnished blades, brass motor, 3 down-tilted glass tulips + bulbs, pull chain) | L1 | 2,076 | 2,076 | (0, −0.45, 0) | exact |
| FX_Chandelier3 (chain, brass body, 3 curved arms, fabric drum shades + bulbs) | H1 | 1,776 | 1,776 | (0, −0.55, 0) | exact |
| FX_Dome (brass rim, frosted dome, finial) | E1, K1, K2 | 384 | 1,152 | (0, −0.08, 0) | exact |
| FX_BulbPendant (rose, 0.33 m cord, bakelite socket, bare bulb) | C1, C2 | 248 | 496 | (0, −0.42, 0) | 2 cm lower |
| FX_SconceBrass (backplate, swan arm, small shade, bulb) | H2, H3, W1, H6 | 508 | 2,032 | (0, 0.08, 0.10) | 2–4.5 cm (wall offset) |
| FX_SconceBroken (shade knocked askew, no bulb; H5 stays off) | H5 | 428 | 428 | (0, 0.08, 0.10) | 4.5 cm |
| FX_SconceTulip (bronze plate, curved arm, upturned tulip) | L3, L4 | 532 | 1,064 | (0, 0.11, 0.12) | 4.5–7.5 cm |
| FX_TableLamp (turned ceramic base, brass harp, conical shade) | L2, H4, R1 | 688 | 2,064 | (0, 0.27, 0) | L2 0.24 m; **H4 0.21 m** (higher); R1 3 cm |
| FX_TableLampSmall (bedside, 0.85 scale) | R2 | 688 | 688 | (0, 0.23, 0) | 10 cm |
| **Fixtures subtotal** | **18** | | **11,776** | | |

The emitters in `fixtures.json` are authoritative for the lighting pass. The two largest deviations:
- **H4:** the hall console (ClassicConsole_01) is 0.95 m high, so the bulb sits at y 1.22 instead of 1.05.
- **L2:** the end table stands at the sofa's north end, (15.78, 5.15).

### 4.3 Props per room (as built in Phase 5)

**Sources:**
- Poly Haven models, either from `Art/Mansion/PolyHaven` or downloaded, all decimated and re-baked into the atlases (§3.5). **`Art/Mansion` was removed on 2026-11-08** with the rest of the mansion. Rebuilding a Poly Haven prop now needs its download: `bungalow_prop_build.py -- --src <root>` with `dl_<asset>/`. The CC0 authors are in §3.1, §3.2 and the Phase 5 report.
- Self-built models in `bungalow_prop_models.py`.

**Openable drawers:** the chest of drawers (`GothicCommode_01`: `Bed_Dresser` and `Liv_Sideboard`) has 3 drawers that open and close. (The kitchen cabinets', sink's, fridge's and mirror cabinet's doors and drawers are cut out of their welded meshes by `BungalowInteractables`, run from the same prefab build.)
- The Blender build keeps the Poly Haven drawer objects as child meshes `GothicCommode_01_Drawer1`–`3` (top first, each pivoted at its own centre): `ph(..., split=("GothicCommode_01_door",))`. The model's triangle budget is shared by triangle count.
- `BungalowPropsPlacer.SetupDrawers` (prefab build) gives every `<Model>_Drawer<N>` child:
  - a sliding `Openable` ("drawer", out of the front by 0.30 m or 60% of the drawer's depth, whichever is less, over 0.3 s);
  - a `DrawerCollider` that swaps its box when the drawer opens or shuts:
    - **shut:** the whole front plus 1 cm on every side, 5 cm deep, 1 cm proud of the body's box, so it's easy to aim at and the look ray finds the drawer rather than the body;
    - **open:** the whole drawer, so it's easy to close again;
  - no static flags and light-probe lighting, so it can move.
- **Looking at things** (`Interactor.FindTarget`):
  - Something lying inside the thing in focus (the key in an open drawer) wins the look, if nothing solid comes between.
  - A look that misses everything gets a 6 cm aim-assist sphere. It only finds things nearer than the solid surface the look landed on, and only where the eye can see them.
- **Highlight:** `InteractHighlight` (added by the `Interactor`) outlines the current target with a warm outline and rim, `Blink/InteractHighlight` (`Art/VFX/InteractHighlight`, material `Resources/InteractHighlight`). It draws an overlay copy of each of the target's meshes, leaving out things inside it. Nothing is outlined while a prop is carried or a note is held up.
- **The bedroom key (`key_bedroom`) is in `Bed_Dresser`'s top drawer.** It's parented to the drawer, 0.22 m behind the front, so it slides out with it; `BungalowStartBedroom.PlaceKeyInDrawer`, also run by Set Up Start Bedroom. While the drawer is shut, the body blocks the look ray; once it's open, the key is the target from 0.35–0.9 m and twinkles like other pickups.
- **The start-bedroom note** uses the bungalow's own `Materials/M_NotePaper` (created by Set Up Start Bedroom). `Art/Story/Materials/Paper.mat` isn't in every checkout, and without it the note rendered pink.
- Covered by `Tests/EditMode/DrawerPrefabTests.cs` and `InteractTargetingTests.cs`. Design: `docs/superpowers/specs/2026-11-07-openable-drawers-design.md`.

**Placement data:** `tools/blender/bungalow_props.py` → `Props/bungalow_props.json`, 143 instances of 84 models.
- Each instance has a room, position, euler and scale.
- Items that sit on something reference their parent's top or shelf height, measured from the built model.

**Placement rules (checked in the preview scene):**
- Every doorway keeps its 1.2 m clear width.
- The hall walkway stays at least 2.0 m clear.
- No collider stands within 0.5 m of a spawn or patrol waypoint.
- One deviation from §2: the living reading corner moved from the NE corner to (18.3, 4.85), because the R1 patrol waypoint (18.8, 6.2) sits in that corner.

**Revision-2 second groupings:**
- **Living:** the reading corner.
- **Bedroom:** wardrobe with suitcases, dresser, reading chair, bookshelf, bedside rug.
- **Central hall:** hall bench, grandfather clock, landing rug.
- **Kitchen:** dresser, 4 chairs, a second dish stack.
- **Basement:** extra crates, barrels, boxes and buckets.

**Living** (24 instances, 17,245 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| ArmChair_01 | Soft | 2 | 2,000 | 4,000 |
| Sofa_01 | Soft | 1 | 2,600 | 2,600 |
| ClassicNightstand_01 | Wood | 2 | 800 | 1,600 |
| GothicCommode_01 | Wood | 1 | 1,600 | 1,600 |
| small_wooden_table_01 | Small | 1 | 999 | 999 |
| BoxStack | Small | 1 | 900 | 900 |
| Picture_Landscape | Small | 1 | 900 | 900 |
| Clutter_CoffeeTable | Small | 1 | 768 | 768 |
| throw_pillows_01 | Soft | 1 | 700 | 700 |
| BoxFallen | Small | 2 | 300 | 600 |
| Picture_Oval | Small | 1 | 496 | 496 |
| Debris_Plaster | Small | 2 | 236 | 472 |
| CoffeeTable_Glass | Wood, M_Glass | 1 | 408 | 408 |
| ceramic_vase_01 | Small | 1 | 300 | 300 |
| ceramic_vase_03 | Small | 1 | 300 | 300 |
| Debris_Paper | Small | 2 | 120 | 240 |
| Clutter_SideTable | Small | 1 | 188 | 188 |
| Rug_Oriental | Soft | 1 | 118 | 118 |
| Speaker | Small | 1 | 56 | 56 |

**Bedroom** (24 instances, 22,834 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| GothicBed_01 | Wood | 1 | 4,600 | 4,600 |
| Rockingchair_01 | Wood | 1 | 2,999 | 2,999 |
| Settee_Plaid | Soft | 1 | 1,728 | 1,728 |
| GothicCommode_01 | Wood | 1 | 1,600 | 1,600 |
| GreenChair_01 | Soft | 1 | 1,500 | 1,500 |
| wooden_bookshelf_worn | Wood | 1 | 1,499 | 1,499 |
| mantel_clock_01 | Small, M_Glass | 1 | 1,000 | 1,000 |
| WoodenTable_01 | Wood | 1 | 952 | 952 |
| vintage_suitcase | Small | 1 | 900 | 900 |
| side_table_tall_01 | Wood | 1 | 900 | 900 |
| Curtains_Window | Soft | 1 | 848 | 848 |
| ClassicNightstand_01 | Wood | 1 | 800 | 800 |
| Bed_Blanket | Soft | 1 | 776 | 776 |
| Shoes_Pair | Small | 1 | 472 | 472 |
| Wardrobe_Painted | Wood | 1 | 460 | 460 |
| MantelClutter | Small | 1 | 388 | 388 |
| ceramic_vase_01 | Small | 1 | 300 | 300 |
| ceramic_vase_03 | Small | 1 | 300 | 300 |
| Debris_Paper | Small | 2 | 120 | 240 |
| Debris_Plaster | Small | 1 | 236 | 236 |
| Picture_Wide | Small | 1 | 172 | 172 |
| Rug_OrientalSmall | Soft | 1 | 88 | 88 |
| Rug_Geo | Soft | 1 | 76 | 76 |

**Entrance hall** (7 instances, 3,447 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| CoatStand | Wood | 1 | 1,100 | 1,100 |
| wall_clock | Small, M_Glass | 1 | 699 | 699 |
| UmbrellaStand | Small | 1 | 644 | 644 |
| Shoes_Pair | Small | 1 | 472 | 472 |
| BoxFallen | Small | 1 | 300 | 300 |
| Debris_Paper | Small | 1 | 120 | 120 |
| Runner_Entrance | Soft | 1 | 112 | 112 |

**Central hall** (11 instances, 8,466 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| vintage_grandfather_clock_01 | Wood, M_Glass | 1 | 2,000 | 2,000 |
| PlantPot | Small | 1 | 1,672 | 1,672 |
| ClassicConsole_01 | Wood | 1 | 1,600 | 1,600 |
| Picture_Hall | Small, M_Glass | 1 | 900 | 900 |
| painted_wooden_chair_01 | Utility | 1 | 724 | 724 |
| painted_wooden_bench | Small | 1 | 630 | 630 |
| ceramic_vase_01 | Small | 1 | 300 | 300 |
| Debris_Plaster | Small | 1 | 236 | 236 |
| Runner_Hall | Soft | 1 | 196 | 196 |
| Debris_Paper | Small | 1 | 120 | 120 |
| Rug_OrientalSmall | Soft | 1 | 88 | 88 |

**Kitchen** (22 instances, 16,831 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| painted_wooden_cabinet | Utility | 2 | 1,500 | 3,000 |
| Dishes_Stack | Small | 4 | 572 | 2,288 |
| painted_wooden_chair_01 | Utility | 3 | 724 | 2,172 |
| electric_stove | Utility, M_Glass | 1 | 1,799 | 1,799 |
| painted_wooden_cabinet_02 | Utility | 1 | 966 | 966 |
| Pans_Stove | Small | 1 | 949 | 949 |
| TrashBags | Utility | 1 | 900 | 900 |
| KnockedChair | Utility | 1 | 724 | 724 |
| Fridge_Retro | Utility | 1 | 716 | 716 |
| wall_clock | Small, M_Glass | 1 | 699 | 699 |
| SinkUnit | Utility | 1 | 624 | 624 |
| KitchenTable | Utility | 1 | 604 | 604 |
| Debris_Dishes | Small | 1 | 584 | 584 |
| wooden_bucket_01 | Utility | 1 | 450 | 450 |
| Debris_Plaster | Small | 1 | 236 | 236 |
| Debris_Paper | Small | 1 | 120 | 120 |

**Bathroom** (8 instances, 4,456 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| ClawfootTub | Utility | 1 | 1,124 | 1,124 |
| painted_wooden_chair_01 | Utility | 1 | 724 | 724 |
| Toilet_Old | Utility | 1 | 686 | 686 |
| PedestalSink | Utility | 1 | 636 | 636 |
| Towel_Rail | Soft | 1 | 456 | 456 |
| wooden_bucket_01 | Utility | 1 | 450 | 450 |
| Debris_Plaster | Small | 1 | 236 | 236 |
| MirrorCabinet | Utility, M_Glass | 1 | 144 | 144 |

**B1 cellar** (14 instances, 9,806 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| barrel_03 | Utility | 2 | 900 | 1,800 |
| wooden_crate_02 | Utility | 2 | 800 | 1,600 |
| wooden_crate_01 | Utility | 2 | 700 | 1,400 |
| steel_frame_shelves_01 | Utility | 1 | 1,200 | 1,200 |
| Jars_Shelf | Utility | 2 | 564 | 1,128 |
| TrashBags | Utility | 1 | 900 | 900 |
| BoxStack | Small | 1 | 900 | 900 |
| wooden_bucket_01 | Utility | 1 | 450 | 450 |
| BoxFallen | Small | 1 | 300 | 300 |
| Debris_PaperFloat | Small | 1 | 128 | 128 |

**B2 workshop** (15 instances, 15,111 tris)

| Model | Atlas / materials | × | Tris each | Tris |
|---|---|---|---|---|
| portable_generator | Utility, M_Glass | 1 | 4,598 | 4,598 |
| wooden_crate_01 | Utility | 2 | 700 | 1,400 |
| steel_frame_shelves_01 | Utility | 1 | 1,200 | 1,200 |
| power_box_01 | Utility | 1 | 1,199 | 1,199 |
| Jars_Shelf | Utility | 2 | 564 | 1,128 |
| metal_jerrycan_green | Utility | 1 | 900 | 900 |
| BoxStack | Small | 1 | 900 | 900 |
| barrel_03 | Utility | 1 | 900 | 900 |
| TrashBags | Utility | 1 | 900 | 900 |
| Workbench | Utility | 1 | 708 | 708 |
| bench_vice_01 | Utility | 1 | 700 | 700 |
| wooden_bucket_01 | Utility | 1 | 450 | 450 |
| Debris_PaperFloat | Small | 1 | 128 | 128 |

### 4.4 Running total (as built, Phase 5)

| Block | Tris | Running |
|---|---|---|
| Shell (Phase 3, §4.1) | 84,196 | 84,196 |
| Living | 17,245 | 101,441 |
| Bedroom | 22,834 | 124,275 |
| Entrance hall | 3,447 | 127,722 |
| Central hall | 8,466 | 136,188 |
| Kitchen | 16,831 | 153,019 |
| Bathroom | 4,456 | 157,475 |
| B1 cellar | 9,806 | 167,281 |
| B2 workshop | 15,111 | 182,392 |
| Fixtures | 11,776 | **194,168** |

**Props and fixtures come to 109,972 tris.** That fits inside the ~110k left by the 84.2k shell under the ~200k cap (decision 13). The level total is **194.2k**; Phase 6's mesh decals are still to come.
- **Heroes:** GothicBed_01 at 4,600 and portable_generator at 4,598. Every other model is ≤ 3k (Rockingchair_01 is 2,999).
- **If Phase 7 finds spare tris:** the heroes were decimated harder than first planned to fit 110k (bed 7k → 4.6k, generator 6k → 4.6k). Raise these first, through the `ph()` targets in `bungalow_prop_models.py`:
  - GothicBed_01 back to 7k;
  - Rockingchair_01 to 4.5k;
  - Sofa_01 to 3.2k.

**Materials:** the props use only the 4 atlases, `M_Shade` and `M_Glass`, so the level stays at 20 materials.
- `M_Glass` is on the glass coffee-table top, the mirror cabinet, the clock glass, the stove door and the hall picture.
- Most props have 1 material (one draw call). Fixtures have 2: atlas and shade.

**Static flags and colliders** (`BungalowPropsPlacer.BuildPrefabs`):
- **Furniture, rugs, wall items and fixtures:** Contribute GI, Batching, Occludee and Reflection Probe static. They are lightmapped through their Blender UV2 at scale in lightmap 0.4 (~8 texels/m).
- **Small props and debris:** the same flags without Contribute GI, so light probes light them.
- **Colliders:**
  - 43 of the 84 prefabs have one box collider from the model bounds. The plant, coat stand and umbrella stand use a trimmed box around the stand.
  - Rugs, debris, pictures, wall items, clutter and ceiling fixtures have no collider.
- **Navmesh:** the placer parents `Props` under the scene's `NavMeshSurface` when it collects children, so the bake carves around the furniture.

**Batches:** these are measured in Phase 7. Props add about 6 materials to the static batches in each view; with the SRP Batcher and static batching, a room view should add roughly 10–25 draw calls.

**Colliders across the level:** box colliders only.
- Walls: one box per wall run.
- Stairs: a single sloped box under the flight, plus step boxes for the navmesh source if needed.
- Props: one box each, or none.
- No mesh colliders.

---

### 4.5 Revision 2: budget re-check at 3× floor area (decision 11)  ⚠ the 80k cap no longer holds as planned

Tables 4.1–4.4 are the Phase 1 baseline (75.6k). At 3× floor area the shell and the dressing both grow:

| Block | Phase 1 | Revision 2 estimate | Why |
|---|---|---|---|
| Kit: floors | 2,600 | 6,000 | 3.2× floor area; sag strips only where they show, larger plank modules elsewhere |
| Kit: walls | 2,600 | 4,200 | ~1.8× wall length, taller (3.0) walls |
| Kit: ceilings, joists | 1,200 | 2,800 | 3× ceiling and joist area |
| Kit: mouldings | 2,000 | 3,600 | ~1.8× perimeter |
| Kit: doors, casings, windows | 6,400 | 7,200 | same count, double D1, bigger openings |
| Kit: stair + balustrades | 6,800 | 8,500 | 15 risers, ~85 balusters on the longer runs |
| Kit: fireplace, radiators (5), basement, peels, water | 4,800 | 7,200 | more radiators and pipe runs, more peel holes |
| **Kit subtotal** | **26,400** | **39,500** | |
| Fixtures (+K2, +H6) | 5,120 | 5,420 | |
| Props (Phase 1 list) | 43,131 | 43,131 | real-world scale, unchanged |
| Props: extra groupings | 0 | 18,500 | living reading corner, bedroom wardrobe / dresser / chair, hall bench, kitchen dresser + 2 chairs, basement shelves + crate |
| Decals and debris | 900 | 2,000 | 3× floor and wall area |
| **Total** | **75,551** | **≈ 108,600** | **over the 80k cap by ~29k** |

**This needs the user's call before Phase 3** (the brief says never quietly lower a requirement):
- **Option A, keep ≤ 80k:** a lighter kit (normal-mapped plank floors, geometric sag only near the camera spots: kit ≈ 30k), decimate the reused PolyHaven props harder, and keep the extra groupings sparse (+6k). That lands at ≈ 81–84k, so even this is tight, and the bigger rooms will look emptier.
- **Option B, raise the cap to ~110k** (recommended): the shell and dressing scale with the area the user asked for; 110k static, batched tris is still light for a URP Forward+ desktop scene.
- **Batches** (≤ 150 from any viewpoint): wide views (CAM-2 down the living room, the D1–A2 hall sightline) now see more props at once, so the estimate rises from 110–130 to about **130–160**. Static batching, the SRP Batcher, and keeping realtime shadows to the 3 hero lights should hold it, but it's the main risk to verify in Phase 7.
- **Materials** (≤ 20) and **textures** (1K) are unaffected. Atlas texel density is unaffected because props keep their size.
- **Lights:** 18 practicals + 8 windows (§5.1); the Mixed set is unchanged (5), and the rooms are now further apart, so Shadowmask overlap is easier than in Phase 1.

## 5. Lighting plan

**Global:**
- Ambient: Source = Color, **#0A0B08**. No skybox contribution (Environment Lighting = Color, reflections from baked probes).
- Lightmapping:
  - GPU Progressive, Mixed Lighting **Shadowmask**.
  - 20 texels/m on the kit, 8 on props.
  - Baked AO on (max distance 0.6, indirect 1.0).
  - 2 bounces, no Realtime GI.
- Light probes on a 1 m grid in every room, on the stairs and through the basement.
- One box-projected reflection probe per room plus one over the stairwell, baked.
- No directional light (see Q4, DayNightCycle).

**Why not "all Mixed":**
- In Shadowmask mode a texel can hold only 4 overlapping Mixed lights. With 5–7 m ranges in a 10 × 8 m house (Phase 1), most rooms would overlap more than 4; at 20 × 15 the overlap is lower, but the hall and the open plan through A1 / O1 still exceed 4, and Unity silently demotes the extras to Baked.
- In Baked-Indirect mode, unshadowed Mixed lights leak through walls.
- So the split is:
  - **Mixed:** L1, L2, K1, H4, R1. Their sphere-overlap clique is ≤ 4: H4 and R1 don't overlap, so they can share a channel.
  - **Realtime:** C1, C2 (the flickering and switched basement bulbs, with shadows, so they don't leak up through the slab).
  - **Baked:** the dim sconces, domes and window area lights.
- Zombies still get the baked lights through the probes.
- Window area lights are **Baked-only** in URP.

**Realtime shadows (hero lights): H4, L2, C1.** All soft, strength 0.6, resolution Medium.
- C2 also has shadows, but it only switches on in the P7 finale, when the player is in the basement and H4/L2 are out of range. So at most 3 are ever significant.

### 5.1 Fixture table

26 light sources: 18 practicals and 8 windows. Positions use the origin above; y is the light's position, not the mount. Intensities are URP linear starting values to tune at the Phase 7 match. Each emissive is listed by its colour; the multiplier is the HDR strength on `M_Shade`, using the light's colour. Revision 2 re-placed every fixture in the bigger rooms and added K2 and H6; ranges grew slightly (living fan 7.0, chandelier 6.5, basement bulbs 7.0). The same data is in `bungalow_layout.FIXTURES`, so the Phase 6 lighting pass reads it instead of re-typing this table.

**Entrance hall and central hall (ref 1)**

| ID | Type | Position (x, y, z) | Colour | Int. | Range | Mode | Shadows | Emissive |
|---|---|---|---|---|---|---|---|---|
| E1 | Flush glass dome, entrance | (9.25, 2.92, 2.00) | #F2DFA8 | 1.2 | 5.5 | Baked | n/a | ×2.0 |
| H1 | 3-arm chandelier, fabric drum shades, over the well's north end | (7.70, 2.45, 9.60) | #FFD6A0 | 1 | 6.5 | Baked | n/a | ×1.2 |
| H2 | Brass sconce, small shade, N wall | (8.45, 1.9, 14.62) | #FFC680 | 0.5 | 5 | Baked | n/a | ×1.5 |
| H3 | Brass sconce, small shade, N wall | (10.05, 1.9, 14.62) | #FFC680 | 0.5 | 5 | Baked | n/a | ×1.5 |
| H4 | Shaded table lamp on the console (the brief's ref-1 lamp) | (11.00, 1.05, 13.80) | **#FFCB85** | 1.4 | 4 | Mixed | **Realtime soft (hero)** | ×1.8 |
| H5 | Brass sconce over the well, W wall (broken, off) | (7.13, 2.1, 8.00) | n/a | 0 | n/a | n/a (broken, off) | n/a | 0 |
| H6 | Brass sconce on the walkway E wall (new in rev 2: the hall is 10.6 m long) | (11.37, 1.9, 6.60) | #FFC680 | 0.45 | 5 | Baked | n/a | ×1.5 |

**Living room (ref 2)**

| ID | Type | Position (x, y, z) | Colour | Int. | Range | Mode | Shadows | Emissive |
|---|---|---|---|---|---|---|---|---|
| L1 | Ceiling fan, 3 glass tulip light kit (SW of centre for the CAM-2 composition) | (14.60, 2.55, 2.90) | #FFD9A0 | 2 | 7 | Mixed | Shadowmask only | glass ×3.0 |
| L2 | Shaded table lamp, white shade | (15.60, 1, 5.30) | #FFE6C2 | 1.6 | 4.5 | Mixed | **Realtime soft (hero)** | ×2.5 |
| L3 | Tulip-shade wall sconce, W wall | (11.62, 1.9, 5.30) | #FFC98A | 0.7 | 5 | Baked | n/a | ×2.0 |
| L4 | Small sconce, O1 pilaster | (16.75, 2, 6.85) | #FFC98A | 0.35 | 5 | Baked | n/a | ×1.2 |

**Kitchen**

| ID | Type | Position (x, y, z) | Colour | Int. | Range | Mode | Shadows | Emissive |
|---|---|---|---|---|---|---|---|---|
| K1 | Flush glass dome, W half | (14.00, 2.92, 10.90) | #E9E6D2 | 1.1 | 6 | Mixed | Shadowmask only | ×2.0 |
| K2 | Flush glass dome, E half (new in rev 2) | (17.60, 2.92, 10.90) | #E9E6D2 | 0.9 | 5.5 | Baked | n/a | ×2.0 |

**Bedroom (ref 3)**

| ID | Type | Position (x, y, z) | Colour | Int. | Range | Mode | Shadows | Emissive |
|---|---|---|---|---|---|---|---|---|
| R1 | Shaded table lamp by the window | (0.80, 1, 1.00) | #F2DDB0 | 1.2 | 5 | Mixed | Shadowmask only | ×1.6 |
| R2 | Bedside lamp on the nightstand | (4.60, 0.95, 9.00) | #F5E6A8 | 0.5 | 4 | Baked | n/a | ×1.0 |

**Bathroom**

| ID | Type | Position (x, y, z) | Colour | Int. | Range | Mode | Shadows | Emissive |
|---|---|---|---|---|---|---|---|---|
| W1 | Shaded vanity sconce over the mirror | (4.40, 2, 14.62) | #E6EBC8 | 0.6 | 5 | Baked | n/a | ×1.5 (emission flicker only) |

**Basement**

| ID | Type | Position (x, y, z) | Colour | Int. | Range | Mode | Shadows | Emissive |
|---|---|---|---|---|---|---|---|---|
| C1 | Bare-bulb pendant at the stair foot, `LightFlicker` (dying) | (8.90, −0.65, 12.40) | #F0DC9A | 1.4 | 7 | Realtime | **Realtime soft (hero)** | bulb ×6 |
| C2 | Bare-bulb pendant, off until the generator runs | (16.60, −0.65, 9.40) | #F0DC9A | 0 → 1.4 | 7 | Realtime | Realtime (finale only) | 0 → ×6 |

**Window area lights** (rectangle, pointing into the room)

| ID | Room | Position (x, y, z), size, facing | Colour | Int. | Mode |
|---|---|---|---|---|---|
| RW1 | Bedroom | (0.32, 1.65, 3.00), 1.6 × 1.5, faces +X | #A9B3A2 | 1.2 | Baked |
| LW1 | Living | (17.40, 1.65, 0.32), 1.6 × 1.5, faces +Z | #8E9AA6 | 0.4 | Baked |
| LW2 | Living | (19.68, 1.65, 3.80), 1.6 × 1.5, faces -X | #8E9AA6 | 0.4 | Baked |
| KW1 | Kitchen | (14.50, 1.65, 14.68), 1.8 × 1.5, faces -Z | #9AA6B0 | 0.8 | Baked |
| KW2 | Kitchen | (19.68, 1.65, 10.20), 1.6 × 1.5, faces -X | #9AA6B0 | 0.6 | Baked |
| WW1 | Bathroom | (2.90, 1.95, 14.68), 1.0 × 0.9, faces -Z | #A3AEA6 | 0.5 | Baked |
| CW1 | B1 | (10.70, −0.52, 14.68), 1.4 × 0.45, faces -Z | #8A96A0 | 0.3 | Baked |
| CW2 | B2 | (19.68, −0.52, 9.70), 1.4 × 0.45, faces -X | #8A96A0 | 0.3 | Baked |

**Count:** 18 practicals. 16 are lit at start; H5 is broken and C2 waits for the generator. There are 8 window lights, so **26 light sources** in total.

**Tuning targets** (from the brief):
- The ceiling around each fixture is bright and the floor beneath is mid.
- The hall's far end falls to black within about 8 m (the hall is now 10.6 m long, so its north end relies on H2/H3/H4).
- Shades bloom softly but don't flare.

**Player flashlight:** added in Phase 6 (decision 5), see §5.4.

### 5.2 Fog and post

**Fog (global RenderSettings):**
- Exponential, colour **#141408**, density **0.15** to start. Transmittance is 0.64 at 3 m, 0.22 at 10 m and 0.05 at 20 m; in these light levels visibility ends at about 10 m.
- Alternative if a crisper near field is wanted: ExponentialSquared 0.17 (0.05 at 10 m).
- Unity fog is global, so a per-room change needs a small trigger script (Q8). Intent per room:
  - halls: 0.15
  - living: 0.17 (ref 2's visible haze)
  - bedroom: 0.16
  - kitchen and bathroom: 0.15
  - basement: 0.22 (damp)

**Global Volume** (on the player camera; requires enabling Post Processing on the camera, see Q4):
- Tonemapping ACES.
- Color Adjustments: post-exposure **−0.6**, contrast +12, saturation −18.
- Bloom: threshold **0.8**, intensity 0.35, scatter 0.65, tint #FFD9A0.
- Vignette **0.40**, smoothness 0.45.
- Chromatic Aberration 0.06.
- Lift: (1.0, 0.99, 0.96, −0.03).
- The project's **FilmGrain** (GameSystems) stays; URP Film Grain stays off.

**Local room Volumes** (box, blend distance 1.0 m, priority 1):

| Room | Exposure | Colour filter | Sat. | Contrast | Extra |
|---|---|---|---|---|---|
| Entrance and hall (ref 1) | −0.5 | #FFE6C8 | −25 | +8 | WB temp +8, vignette 0.42. Very dark, warm, soft |
| Living (ref 2) | −0.4 | #FFDCC0 | −12 | +18 | Vignette 0.45, CA 0.12, split-tone shadows #3A2418 / highlights #FFE0B0 |
| Bedroom (ref 3) | −0.2 | #DCE6CF | −35 | **−12** (low contrast) | WB temp −6, tint −8 (green); lift (0.97, 1.0, 0.96, +0.02) for greenish raised blacks; vignette 0.35 |
| Kitchen | −0.4 | #E4E9E6 | −30 | 0 | WB temp −10 (the blue-white room) |
| Bathroom | −0.5 | #D9E4D6 | −35 | 0 | WB temp −8, tint −10 |
| Basement | −0.6 | #CFD3B4 | −40 | +10 | Vignette 0.5. The brief's yellow-green grade lives here |

### 5.3 Reference-match cameras (re-placed in revision 2)

All three use the player camera's eye height and FOV (vertical 60; CAM-3 55), cropped to each ref's aspect. They are drawn as CAM-1/2/3 on the plan and placed as markers in the scene (`Markers/MatchCameras`). Revision 2 moved them so each still frames its reference **composition** in the bigger rooms (decision 11: composition matters more than coordinates). Screen x below is computed from the layout (0% = left edge).

| Cam | Ref | Stand at (x, y, z) | Yaw | Pitch | FOV (vertical) | Computed screen x | Target composition |
|---|---|---|---|---|---|---|---|
| CAM-1 | Ref1 (1192 × 670) | (9.40, 1.65, 4.10) | 352° | −8° | 60 (h 92°) | stairwell (centre) 34%; H1 chandelier 40%; radiator wall centre 58%; A3 83% | ref 1: well + balustrade lower left, chandelier upper left, radiator wall centre, A3 right |
| CAM-2 | Ref2 (1920 × 1080) | (19.20, 1.65, 0.80) | 297° | −12° | 60 (h 91°) | A1 arch 32%; L1 fan 47%; painting / L3 (W wall) 58%; O1 centre 81%; O1 east jamb 93% | ref 2: fan 47%, arch 35%, painting 62%, opening 85–100% |
| CAM-3 | Ref3 (1048 × 486) | (5.00, 1.50, 6.60) | 200° | −10° | 55 (h 97°) | D2 (left wall) 4%; fireplace 45%; WR1 window 84% | ref 3: doorway on the left, fireplace ~45%, window right |

- CAM-1 stands in the 3.0 m A2 arch looking up the 14.8 m hall sightline; the A4 arch frames the radiator wall as in ref 1.
- CAM-2 stands in the living room's SE corner. To keep ref 2's left-to-right order (arch, fan, painting, opening), L1 sits SW of the room centre and the painting / L3 sit towards the north end of the W wall.
- CAM-3 stands beside the bed foot looking SSW; it is also the player's first view after waking (P0 yaw 200).
- Phase 2 grey-box renders of all three: see the Phase 2 report. Final tweaks are made against `compare_screenshot` in Phase 7.

### 5.4 As built (Phase 6: lighting, atmosphere, Unity set-up)

**Tools (all re-runnable, data-driven):**
- `tools/blender/bungalow_decals.py` (plain Python; reads `bungalow_layout.py` read-only) writes `Art/Environment/Bungalow/Lighting/bungalow_atmosphere.json`:
  - `decals`: 185 mesh-decal quads placed on the wall, floor and ceiling planes of the room rectangles. They sit 6 mm off walls and ceilings and 4 mm off floors.
    - Each wall decal is checked against the openings cut through its wall, the skirting and cornice band, and fixed obstructions (chimney breast, stair stringer).
    - Tide-line and grime bands split themselves around openings.
    - If a layout change puts a door under a stain, the script fails.
  - `areaLights`: size and facing of the window lights, which aren't in `bungalow_layout.json`.
  - `stairWell`.
  - `water`: y −2.77, over B1 and B2.
  - `tuning`: the per-fixture overrides (colour, intensity, shade glow, emission fizz), the global and per-room grades below, and `globals.sky*`: the night-sky colours NightSky uses in this scene (see "Window view" below). **Tune here, not in the scene.**
- **BLINK/Bungalow/Build Lighting** (`Editor/BungalowLightingBuilder.cs` + `Editor/BungalowAtmosphereBuilder.cs`) builds a `Lighting` root in the active scene. Each run replaces its own output. The root holds:
  - **`Fixtures`:** one light per §5.1 row (25 lights; H5 is a dark fixture with no light).
    - Each light has a `PracticalLight` (`Scripts/Visuals/PracticalLight.cs`) that makes its shade glow through a per-renderer emission override. One `M_Shade` therefore serves every fixture: lit lamps glow; H5 doesn't, and C2 doesn't until it's switched on.
    - Lights are parented to Phase 5's fixture emitters (`FX_<id>` / `<Model>_Emitter`, from `Props/fixtures.json`) when those are in the scene. Otherwise they go at the `emitterWorld` from `fixtures.json` or at the layout position, with simple placeholder shades.
    - Sconce lights are kept at least **0.24 m** off the wall (`SconceWallGap`). Closer burns a hot spot into the paper: at 0.16 m the halos round H2, H3 and H6 clipped at the room exposures.
    - Fixtures with a `cone` in `tuning` are downward spot lights, not points: E1, K1, K2 (domes) and L1 (the fan's light kit), 150° outer and 110° inner. Their emitters sit 0.08–0.45 m under the ceiling, so as points they burnt a ring into it.
    - C1 has `LightFlicker` (dying).
    - **C2 starts off.** The generator beat calls `PracticalLight.Find("C2").SetOn(true)`, which switches on the light and the bulb glow.
  - **`Probes`** (1,616 light probes in 8 groups after the props went in, 136 positions inside geometry skipped; plus 9 reflection probes):
    - Light-probe groups on a ~1 m grid per room, in 3–4 layers kept 0.3 m off the floor and ceiling.
    - Columns over the stair well run down the flight to the basement.
    - Probes inside colliders are dropped.
    - Box-projected baked reflection probes, one per room plus the stairwell (128 px, HDR).
  - **`Volumes`:** a global Volume plus one local Volume per room group (box = the room rectangles, blend 1 m, priority 1). Profiles are `Lighting/VP_*.asset`.
  - **`Atmosphere`:**
    - Decals are merged into one mesh per room (`Lighting/Meshes/Decals_<room>.asset`). They are lightmap-static with shadow casting off, so they bake with the light of the surface under them, at one draw per room.
    - The shell's own `Basement_Water` plane is kept but made probe-lit and non-shadowing; otherwise it would shadow the flooded floor in the bake. If the shell has no water plane, the builder makes one.
- The builder also sets the scene settings:
  - ambient Color #0A0B08, no skybox, black default reflection, global fog;
  - lighting settings `Lighting/LS_Bungalow.lighting`;
  - the grey-box temporary light removed;
  - `NightSky` (GameSystems) tuned and **enabled** in this scene only (prefab-instance overrides): zenith (0.002, 0.004, 0.012), horizon (0.012, 0.022, 0.050), ground (0.003, 0.005, 0.012), moon (0.05, 0.065, 0.10), stars 0.35, all from `globals.sky*`. An earlier build had disabled the `DayNightCycle` that sat on this component; main replaced it with `NightSky`, and the stale `m_Enabled` override had kept the sky off in this scene (see "Window view" below).
  - the Player camera clears to `globals.sky` **#04060B** (a very dark cool blue, matching the sky's ground colour) until NightSky switches it to Skybox in play. Not the fog colour: the room volumes expose the frame up 1.4–2.45 stops, which turned #141408 into a mustard pane.
- Builder helpers:
  - `NormalizeLightmapDensity`: run after a bake. It raises `scaleInLightmap` on renderers whose measured texel density is under 60% of the target.
    - The shell's rect-per-mesh UV2 left mouldings, arches, radiators, balustrades and pipes at 1.2–5 texels/m instead of 10.
    - 40 shell renderers were rescaled (0 more after the November riser rebake: the scene is at the normalised state).
  - `RenderMatchCameras` / `Render`: player-eye renders with the Player camera's post, and optionally the flashlight.
  - `RefreshLook`: re-applies the grade and fog without a rebake.
- **BLINK/Bungalow/Build Shell Scene** (`BungalowGreyboxBuilder.Build(bake, shell: true)`) builds `House_Bungalow.unity` from `Kit/Bungalow_Shell.fbx` and its colliders JSON, and sets up the doors (BLINK/Bungalow/Setup Doors: every leaf a `Door`, shut at start, left out of the navmesh bake).

**Lightmapping (`LS_Bungalow`):**
- GPU Progressive, Shadowmask, no realtime GI.
- 20 texels/m (10 for provisional bakes), max size 2048, padding 4, directional.
- AO distance 0.6 m (indirect 1, direct 0).
- 2 bounces, indirect scale 1.4 for the refs' soft fill.
- Samples: 32 direct, 384 indirect, 128 environment.
- Bake time on this machine: 40–90 s, giving 3 × 1024² lightmaps.
- Mixed lights got these shadowmask channels: H4 1, R1 0, L1 1, K1 0, L2 2 (max overlap 3).

**Quality and pipeline settings (decision 7):**
- The PC quality level's `shadowmaskMode` went from Distance Shadowmask to **Shadowmask**; Mobile was already Shadowmask. Static shadows of the Mixed lights now come from the baked shadowmask, and only dynamic objects (player, zombies) are drawn into their realtime shadow maps.
- `PC_RPAsset` already had Mixed Lighting, additional-light shadows and soft shadows on. It was not changed.
- The URP Decal renderer feature is **not** enabled (decision 10).

**Light settings as built** (layout values from §5.1 unless overridden in `tuning`):
- **Shadows:**
  - All point and spot lights have soft shadows. Baked shadow radius: 0.03 m for bulbs, 0.08 m for sconces, domes and the fan, 0.12 m for lamps.
  - Mixed H4 and L2: strength 0.6, Medium tier.
  - L1, K1 and R1: Low tier; the shadowmask carries their static shadows.
  - Realtime C1 and C2: strength **1.0**, Medium tier. At 0.6, 40% of the bulb would come through the slab into the ground floor.
- **Re-tuned colours and intensities for the ref match.** The §5.1 colours are deep orange in linear light and turned every frame orange-red under ACES. The refs read as white-balanced tungsten, so the colours below are paler. Intensities in bold were cut or raised in the balance pass (below).

  | Light | Colour | Intensity | Shape |
  |---|---|---|---|
  | E1 | #F5E8CC | 1.4 | spot 150° down |
  | H1 | #FFE4C0 | 1.6 | point |
  | H2, H3 | #FFDDB0 | 0.45 | point |
  | H4 | #FFD9A8 | **1.1** (was 1.8) | point |
  | H6 | (layout) | 0.35 | point |
  | K1, K2 | (layout) | 1.1, 0.9 | spot 150° down |
  | L1 | #FFE6C4 | 1.5 | spot 150° down |
  | L2 | #FFEED8 | **1.6** (was 3.0) | point |
  | L3, L4 | #FFDDB4 | 0.6, 0.4 | point |
  | R1 | #F5E6C8 | **1.4** (was 2.2) | point |
  | R2 | (layout) | 0.8 | point |
  | W1 | (layout) | 1.0 | point |
  | RW1 | #B8BCB0 | 3.0 | window |
  | KW1 | (layout) | **1.2** (was 0.8) | window |
  | WW1 | (layout) | 0.9 | window |
  | CW1, CW2 | (layout) | 0.8 | window |

- Shade glow follows the §5.1 "Emissive" column. W1 fizzes (emission only).

**Balance pass (after the props and doors):** an audit of 14 player-eye views (1.65 m, arms and flashlight hidden) looking for surfaces that clip. ACES flattens out around sRGB luminance 229, so "clipping" here means a flat area at 200 or above, measured with the shades and bulbs masked out and each hot spot attributed to the light that dominates it.
- **Causes found:**
  - Sconces 0.16 m off the wall: halos on the hall's north wall (H2, H3) and on the wall behind H6.
  - H4 0.30 m from the wall at intensity 1.8: the hall corner.
  - L2 at 3.0: a pool on the living-room floor and sofa out to 1.8 m, under the living room's +3.8 EV total.
  - L1, E1, K1 and K2 as point lights 0.08–0.45 m under the ceiling: rings on the ceiling.
  - R1 at 2.2: the bedroom's west wall.
- **Not causes:** glossy materials and reflection probes; toggling them on the hot surfaces changed nothing. Room exposures were left alone, since lowering them darkens the corners and the sightline backdrops too.
- **The bathtub.** `ClawfootTub`'s Blender UV2 had collapsed: 532 of its 1,124 triangles were zero-area, covering 95% of its surface, and half the texels it used overlapped. Every bake gave it garbage lighting (one bake a glowing white tub, the next a black one).
  - The tub's importer now generates lightmap UVs in Unity (`UNITY_LIGHTMAP_UV` in `tools/blender/unity_setup.py`), and its `scaleInLightmap` is back at the prop default 0.4.
  - No other lightmapped mesh is affected; the rest have under 1% of their surface in sliver triangles.
- **Changes:** the four lamp and window values in the table above, sconce gap 0.24 m, and the ceiling spots.
- **Results**, as the share of the frame's walls and floors at 200 or above, before → after:

  | View | Before | After |
  |---|---|---|
  | Hall, looking north | 2.68% | 1.83% |
  | Hall CAM-1 | 0.18% | 0.13% |
  | Living CAM-2 | 0.76% | 0.12% |
  | Living lamp view | 0.88% | 0.05% |
  | Living → kitchen | 0.15% | 0.05% |
  | Kitchen | 0.08% | 0% |
  | Bedroom CAM-3 | 0.23% | 0.01% |
  | Bathroom, basement | 0% | 0% |

  The hall's north wall still clips round H2, H3 and H4 at the hall's +2.9 EV total.
- **Zombie readability:** a dark capsule 11 m down each §1.6 sightline, as the ratio of the background behind it to the capsule. Hall 3.6 → 3.5. Living → kitchen 2.3 → 2.1 (the cuts to L1 and L2 took out some of the fill that the KW1 raise added). Bedroom → living 1.26 → 1.24. Basement about 1.4 either way, so it still needs the flashlight.

**Fog and post as built** (the §5.2 starting values, re-tuned against the refs):
- **Fog:** Exponential, #141408, **density 0.11**. At 0.15 a zombie at 12 m was hidden; at 0.11 it reads as a silhouette down every §1.6 sightline (see the playability check below).
- **Global volume:**
  - ACES tonemapping.
  - Post-exposure **+1.5**. ACES alone takes ~1.4 stops off these dark frames, so the §5.2 value of −0.6 rendered them black. Room values were raised 0.3–0.4 once the props went in.
  - Contrast +10, saturation −18.
  - Vignette 0.30, chromatic aberration 0.06, lift (1, 0.99, 0.97, 0).
  - Bloom 0.35, threshold 0.8, scatter 0.65, tint #FFD9A0.
  - URP Film Grain off (the user also turned the project's grain off).
- **Room volumes:**

  | Room | Exposure | Filter | Saturation | Contrast | Extras |
  |---|---|---|---|---|---|
  | Hall | +1.4 | #E8E8E4 | −40 | +8 | temp −16, vignette 0.34 |
  | Living | +2.3 | #E6E4DE | −38 | +14 | temp −16, vignette 0.36, CA 0.12, split-tone #3A2418 / #FFF0DC |
  | Bedroom | +2.45 | #ECE8DC | −30 | −6 | temp +4, tint +8, vignette 0.28 |
  | Kitchen | +1.4 | #E4E9E6 | −30 | 0 | temp −10 |
  | Bathroom | +1.7 | #D9E4D6 | −35 | 0 | temp −8, tint −6 |
  | Basement | +1.6 | #D8D8C0 | −40 | +10 | vignette 0.4 |

**Player prefab (decisions 4 and 5):**
- The camera has `UniversalAdditionalCameraData` with Post Processing on (volume mask Default, dithering on).
- New child `Camera/Flashlight`, a spot light:
  - range 25 m, 55° outer / 20° inner, intensity 14, #FFF2DE, no cookie, starts on;
  - soft shadows, High tier, strength 0.9;
  - **shadow near plane 0.6 m**, so the view-model gun doesn't throw a shadow down the beam.
- `Scripts/Player/Flashlight.cs` toggles it with **F** or **d-pad up**. This is a code action, as in `PlayerWeapons`; the input asset is unchanged.
  - Switching on gives a short stutter, on unscaled time.
  - `FlashlightMath` is covered by `Tests/EditMode/FlashlightTests.cs`.

**Match and playability** (final, House_Bungalow.unity with the props; the flashlight off unless noted):
- Mean sRGB, ref vs Unity:

  | Camera | Ref | Unity | Unity, flashlight on |
  |---|---|---|---|
  | CAM-1 | (30, 26, 22) | (26, 20, 13) | (29, 22, 15) |
  | CAM-2 | (28, 26, 23) | (24, 18, 13) | (28, 21, 15) |
  | CAM-3 | (38, 34, 26) | (34, 31, 21) | (40, 36, 25) |

- After the props went in, every exposure went up 0.3–0.4 (furniture absorbs light): global +1.5, Hall +1.4, Living +2.3, Bedroom +2.45, Kitchen +1.4, Bathroom +1.7, Basement +1.6.
- A zombie placed 10.6–12.6 m down each §1.6 sightline (D1–A2 hall, bedroom–D2–A1–living, living–O1–kitchen, B1–D5–B2) reads as a silhouette with the flashlight in all four, and without it in the hall and the living→kitchen view; the basement needs the flashlight.

**Window view and the top stair riser (fixes after the first playable build, 7 Nov 2026):**
- **Windows read as bright mustard panes.** Two causes, both in what the camera clears to. (1) The builder set the Player camera to clear to the fog colour #141408; under the room volumes' +1.4 to +2.45 EV that olive became a bright pane. (2) `NightSky` (the whole game's night, on GameSystems) was disabled in this scene by a prefab override that originally disabled the `DayNightCycle` on the same component; the override survived main's swap to `NightSky`. With it enabled, the sky's prefab defaults (horizon 0.045/0.06/0.095, fog-coloured ground, a full moon) would have been brighter still.
  - Fix: `globals.sky*` in `bungalow_decals.py` (tuned colours about 10× darker and cool), `TuneNightSky` in the lighting builder applies them to the scene's `NightSky` and enables it, `NightCameras` clears to `globals.sky` #04060B, `NightSky.groundColor` (new, alpha 0 = fog colour) replaces the fog-coloured ground, and `M_Glass` is tinted (0.38, 0.48, 0.62). `BungalowLightingBuilder.PreviewSkyMode` renders the old clear (2) and the untuned sky (1) for comparisons; 0 is the sky as in play.
  - Pane centres (mean sRGB of the central 120 px of a player-eye render, before → after): bedroom WR1 (41, 39, 19) → (19, 20, 10); living WL2 (14, 8, 1) → (4, 2, 0); living WL1 (9, 5, 0) → (4, 2, 0); kitchen WK1 (19, 10, 2) → (15, 4, 2); kitchen WK2 (21, 14, 4) → (16, 5, 2); bathroom WB1 (16, 14, 3) → (7, 6, 2); basement BW1 (30, 22, 11) → (28, 21, 10). Interiors unchanged: hall sightline (28, 21, 14) → (28, 21, 14); living → kitchen (19, 13, 8) → (19, 13, 8).
  - Match cameras with the as-built lights, before → after: CAM-1 (26, 20, 13) → (27, 20, 14); CAM-2 (14, 9, 6) → (14, 9, 6); CAM-3 (30, 27, 18) → (30, 27, 18).
- **The committed scene predated the balance pass.** `House_Bungalow.unity` and the committed `bungalow_atmosphere.json` still had E1, K1, K2 and L1 as point lights and H4 1.8, L2 3.0, R1 2.2, KW1 0.8; `bungalow_decals.py` already had the table above. Regenerating the JSON and running Build Lighting applied the documented values, so against the committed scene CAM-2 drops from (24, 18, 13) to (14, 9, 6) and the living → kitchen view from (29, 23, 17) to (19, 13, 8); CAM-1 and the hall sightline are within 1 unit. Zombie readability on that sightline is the balance pass's 2.1 ratio above, not a new change.
- **Top riser showed tan plaster with a diagonal seam.** Riser 0's front face was coplanar with the hall slab's plaster hole edge (both at z = 6.0) and z-fought with it. `bungalow_kit.py` now builds riser 0 as a 12 mm box in front of that edge (§6.5); the other 14 risers sit under their tread's nose, and the bottom riser and the B1 landing edge share z = 9.92 but not a height band, so neither needed a change. Shell re-exported (door-leaf names, pivots and transforms unchanged), wear atlas rebaked, then bake → `NormalizeLightmapDensity` (0 rescaled) → bake: 3 × 1024² lightmaps, 1,616 light probes in 8 groups (136 inside geometry skipped), 9 reflection probes.

## 6. Pipeline plan (`tools/blender/`)

### 6.1 What exists and can be reused

| Piece | Reuse as is |
|---|---|
| `build_all.export(name)` | FBX in metres, forward −Z / up Y, baked transforms, tangents, so Unity imports at scale 1 / rotation 0. |
| `build_all.bake(name, hi, target, res, ext)` | Decimate, Smart UV, Cycles selected-to-active bakes of Normal, Albedo (through emission), Mask (through emission) and AO, packed into the URP mask (R metal, G AO, A smooth). The core of the prop atlases and the PolyHaven re-bakes. |
| `build_all.preview(name, path, lit)` | Headless Workbench/EEVEE renders, the stand-in for BlenderMCP screenshots. |
| `lib.py` primitives | `box`, `box_z`, `lathe` (balusters, bottles, lamp bases, newels, tub), `cyl`, `torus`, `tube`, `sweep_x` (skirting, rails, cornice), `extrude_poly` (arches, frames), `rounded_box` (cushions, settee, mattress), `solidify`, `grid`, `jitter` (sag), `join`, `finish`, `set_origin`, `empty` (light and socket anchors), `tri_count`, `bounds`, `bake_mode`. |
| `assets.py` builders | `make_door`/`PanelDoor` (door leaves), `casing`/`DoorFrame` (architraves), `WindowFrame`, `make_banister`/`Banister_Stair`/`NewelPost` (the ref-1 balustrade), `drape` (curtains), `TableLamp`, `turned_leg`. Each gets a parameter for size. |
| `unity_setup.py` | `remaps()` as is: FBX material names map onto `.mat`. `textures()` and `wire()` keep the same YAML approach. |

### 6.2 The `bungalow` target

1. **`build_all.py`:**
   - Move the module-level `main()` under `if __name__ == "__main__":`, so other scripts can import `export`/`bake`/`preview`. Blender's `--python` still runs it.
   - Add `--target {mansion,bungalow}`. `mansion` stays the default with identical behaviour.
   - The target supplies `OUT` (`Unity/Assets/Art/Environment/Bungalow`), `TEX`, the builder registry, the `BAKE` table and the texture step.
2. **New `bungalow_layout.py`** (plain Python, no bpy): the single source of truth for the numbers in this spec. It holds walls, openings, rooms, stair, fixtures, spawns, routes and match cameras.
   - It writes `bungalow_layout.json` for the Unity assembly step.
   - The floor-plan generator (now in scratch) moves here, so the plan regenerates from data.
3. **New `bungalow_kit.py`:**
   - Modular pieces on the 0.5 m grid: wall segments (plain, door cut, arch cut, window cut), floor tiles with plank strips (seeded rotation and sag through `jitter`), the stair flight (15 risers, nosing chips cut by boolean), balustrade runs (reusing `make_banister`), casings, arches, the radiator, the fireplace and joists.
   - Each piece gets UV0 box mapping scaled to the scan's real tile size. `lib._uv_box` is hard-coded to 1 tile/m and needs a scale argument.
4. **New `bungalow_props.py`:**
   - Self-built props and fixtures.
   - `import_polyhaven(fbx, target_tris)`: imports a Mansion/PolyHaven FBX and **rebuilds its material as a `lib`-style node tree** from its `diff`/`nor_gl` and `Source~/*_arm` maps (it needs the `MaskEmit`/`AlbedoEmit` nodes so `bake()` works), then decimates.
5. **New `scans.py`** (plain Python + Pillow/numpy, no bpy; run with `python -I`):
   - Downloads the 1K CC0 maps from §3 into a cache outside `Assets/` (one fresh folder per asset).
   - Repacks `arm` into URP MetallicSmoothness: R = arm.B, G = arm.R (AO), B = 0, A = 1 − arm.G.
   - Composites the **`M_Trim` trim sheet** strips.
   - Generates the three wallpaper patterns and the rug patterns and multiplies them into the base albedos.
   - Builds the decal atlas.
   - Writes the 1024² PNGs into `Art/Environment/Bungalow/Textures/`.
   - This replaces `textures.py`'s procedural families for this target; `textures.py` stays for the mansion.
6. **`lib.py`:**
   - `MATERIALS`/`mat()` assume `<Family>_Albedo/_Normal/_Mask.png` in one `TEX_DIR`. Add a `BUNGALOW_MATERIALS` table keyed to the scan files, plus `TEX_DIR` per target.
   - New UV helpers:
     - `uv_lightmap(o)`: second UV layer, `bpy.ops.uv.lightmap_pack`, no overlaps, 2-texel margin.
     - `uv_wear(objs)`: third UV layer packing every kit piece into one shared wear atlas.
7. **New bakes in `build_all.py`:**
   - `bake_atlas(objs, name, res=1024)`: several low-poly props share one UV layout and one baked set. Today `bake()` does one asset to one texture set.
   - `bake_wear(kit_objs)`: Cycles AO, an edge-wear mask (a Bevel-node vs. geometry normal difference baked through emission, via a new `lib.bake_mode("wear")`) and dirt (cavity × up-facing). These are packed into the RGBA wear atlas.
8. **`unity_setup.py`:**
   - Parameterise `ROOT`, `SHARED`, `BAKED` by target.
   - In `textures()`: force **1024 for every file** (drop the per-name 512 rule for this target) and add a Standalone platform override of **BC7** (format 25). Normals would default to BC5 under "HQ", and the brief says BC7.
   - In `materials()`: it copies the template `Materials/Wood_Dark.mat`, which is in the sparse-checkout-hidden mansion folder, so it isn't present here. Add an inline URP Lit `.mat` YAML template, or create the materials through Unity MCP `execute_code`.
   - Add ModelImporter edits: importLights/Cameras 0, generateSecondaryUV 0 (UV2 comes from Blender), readable 0, no import colliders.
9. **Unity assembly** (Phase 6, Unity MCP `execute_code` C# 6, or a new `Assets/Editor/BungalowBuilder.cs`; **never** `HouseBlockout*.cs`):
   - Reads `bungalow_layout.json`, places the kit and props, and sets Static flags and box colliders.
   - Creates the lights, probes and volumes, places the Player, GameSystems and Effects prefabs, the spawns and the routes (empties with waypoint children for `MobController.patrolRoute`).
   - Adds a NavMeshSurface (with the BungalowZombie agent) and bakes.

### 6.3 Conflicts with the existing project (brief vs reality)

1. **No Player flashlight.** The brief's "existing Player flashlight" isn't in `Player.prefab`; only the Inventory candle light exists (Q5).
2. **Player camera has no URP camera data**, so Post Processing is off and no Volume shows (FEEL_KIT "To do"). Turning it on means editing `Player.prefab` or a per-scene prefab-instance override (Q4).
3. **`DayNightCycle` + `GameClock` on GameSystems** set `RenderSettings` ambient (Flat), fog and the skybox every frame.
   - They start at 21:00 (night) and use the scene's own night values. But after 06:00 game time the house fades to day ambient (0.36 grey) and day fog.
   - The clock advances at 1 min/s plus **15 min per blink**, so about 540 s or about 36 blinks reach dawn.
   - Fix: disable it in this scene (instance override) or confirm the plan (Q4).
4. **`WakeUpSequence`** starts the game lying on a bed, so the player spawn is the bedroom (P0 by the bed). That's good, and it makes the ref-3 view the opening shot.
5. **NavMesh:** the Humanoid bake agent (radius 0.5, climb 0.75) closes 0.9 m doors. A new agent type is a project-settings change (Q1).
6. **URP Lit can't take a per-piece wear mask** on a shared tiling material: there is no third-UV input, and its detail maps use UV0.
   - "Baked AO + edge-wear masks per modular piece" with ≤ 20 materials therefore needs a **URP Lit Shader Graph**: tiling scan on UV0, wear atlas on UV2, SRP Batcher compatible.
   - The alternative is baking unique per-piece textures, which costs one material per piece type and breaks the 20 cap (Q2).
7. **"Every fixture Mixed"** collides with the Shadowmask 4-channel limit and with light leaking. §5 gives the proposed split.
8. **Ref-1 lighting rule vs image:** the brief says "one table lamp, no ceiling light lit", but the image shows the chandelier and both sconces lit. Defaulting to the image, with H4 kept as specified (Q3).
9. **Story plan** (`STORY_PLAN.md`) has the basement reached by a **locked door in the kitchen**. Ref 1 asks for an open balustraded stairwell in the hall (Q6).
10. **Existing pipeline textures are procedural** (`textures.py`), while the brief requires CC0 scans, so `scans.py` is added.
11. **Mansion PolyHaven props each carry their own URP material**. Reused as is, they'd cost one material each, so they get re-baked into the 4 atlases. Most exceed 3k tris and need decimation.
12. **`unity_setup.materials()` template and `Art/Mansion/Blender`** are hidden by sparse-checkout here, so there's no template `.mat` to copy.
13. **Fog is global** in Unity, so per-room values need a script (Q8).
14. **`docs/reference/`** (the three refs) is untracked in git. It should be committed with this spec.

### 6.4 BlenderMCP (blender_bridge) status

The add-on is **not installed or connected**; headless Blender 5.1.2 works.

| Phase | Needs blender_bridge? |
|---|---|
| 2 Grey-box | No. Headless build plus Unity walk-test screenshots through Unity MCP |
| 3 Kit, trim sheet, wear bakes | Optional. The before/after views can be headless `preview()` renders |
| 4 Materials | **Yes**: `compare_screenshot` Blender vs Unity on 3 materials |
| 5 Props | Optional: `import_model` checks. Headless `preview()` is the fallback |
| 6–7 Lighting match and verify | **Yes**: the brief's `compare_screenshot` against refs 1–3. Fallback if still missing: a Pillow side-by-side with luminance histograms and mean-colour deltas per region, but that isn't the tool the brief names |

---

### 6.5 Status

**Phase 2 (implemented)**

- `bungalow_layout.py`: the data model (levels, rooms with floor / ceiling holes, walls, openings, stair, beams, fixtures, furniture footprints, spawns, routes, cameras, reserved FUTURE entries) plus `check()` (≥ 3× areas, doorway sizes, hallway widths, stair maths, every room side closed by walls). Writes `Art/Environment/Bungalow/bungalow_layout.json`.
- `bungalow_greybox.py`: builds every piece from the tables (walls cut by their openings with no internal faces, per-room floor and ceiling slabs cut by their holes, arches, casings, windows, stair, balustrade, G1, beams, fireplace, exterior), UV0 box mapping, UV2 lightmap layout, and `Greybox/Bungalow_Greybox_colliders.json` (box colliders, Unity space).
- `bungalow_floorplan.py`: regenerates `docs/bungalow/floorplan.png` from the same data.
- `build_all.py --target bungalow`, `unity_setup.py --target bungalow` (importer settings), `lib.uv_lightmap` and the `GB_*` grey-box colours in `lib.MATERIALS`. The mansion target is unchanged.
- `Assets/Editor/BungalowGreyboxBuilder.cs` (menu **BLINK/Bungalow/Build Grey-box Scene**): builds `House_Bungalow.unity` from the FBX and the two JSON files and bakes the navmesh with the BungalowZombie agent.

**Phase 3 (implemented): modular kit, trim sheet, wear bakes**
- `bungalow_layout.py`: new kit tables. `SURFACE_TILE_M` (also in the JSON as `surfaceTiles`), `FLOOR_PLANK_SEAMS_PX`, `TRIM_STYLE`, `ROOM_FINISH` (floor / wall / ceiling materials, board direction, trim style, mouldings, wall bands), `MOULDING_Y`, `DOOR_LEAVES`, `RADIATORS`, `PIPES`, `DECAY` (sag zones, missing balusters, cracked newel, chip rate, water level). `check()` validates them. A new room needs a `ROOM_FINISH` row; new radiators, pipes, leaves and decay spots are rows too.
- `bungalow_kit.py`: the shell from those tables (`Bungalow_Shell`, see §4.1), the piece library (`Bungalow_Kit`), and `Kit/Bungalow_Shell_colliders.json` (grey-box boxes plus leaf boxes). Riser 0 (the top riser) is a 12 mm box standing **in front of** the hall slab's hole edge at z = 6.0 (faces at z = 6.000 and 6.012); the other risers sit 20 mm behind their tread's nose. Its face used to be coplanar with the plaster hole edge and z-fought with it (a tan, diagonally split quad in play).
- `bungalow_trim.py`: the trim sheet (§3.0.4).
- `bungalow_wear.py`: the UV2 atlas packing and the four-channel wear bake (§3.0.4), plus `preview_materials()` for Blender before / after renders.
- `build_all.py --target bungalow [Bungalow_Shell] [Bungalow_Kit] [--no-wear]`; the mansion target is unchanged.
- `unity_setup.py --target bungalow` adds `kit_textures()` (Trim / KitWear importers: 1024, mips, BC7 Standalone override, normal flagged, masks and the atlas linear, the atlas clamped) and `kit_remaps()` (Kit FBX material names → `Materials/M_*.mat`). The FBX importer has no cameras or lights, and Generate Lightmap UVs is off.
- Unity: `Scenes/House_Bungalow_KitPreview.unity` is the shell with preview copies of the Phase 4 materials (`Kit/Preview/PV_*.mat`, wear atlas wired) and preview point lights. `House_Bungalow.unity` is untouched; swapping the grey-box for `Bungalow_Shell.fbx` and its colliders JSON is a builder change (`ModelPath` / `CollidersPath`).
- Known limits: the 1K atlas spread over ~1,500 m² gives 2.5–5 cm wear texels, so the wear reads as broad AO, grime and traffic patterns rather than crisp chips; the chips themselves are geometry. The UV2 rect-per-mesh layout assumes Unity packs lightmaps by each mesh's UV2 bounds; verify the lightmap texel density at the Phase 6 bake.

**Phase 6 (done: Stage A + Stage B)**
- The lighting rig, probes, volumes, atmosphere, lightmapping, Player flashlight and post are described in §5.4.
- `House_Bungalow.unity` is the playable level: the textured Phase 3 shell, the 143 Phase 5 props, the Player, GameSystems, Effects, the markers, the BungalowZombie navmesh and the final bake.
- **Rebuild order** (each step is re-runnable):
  1. **BLINK/Bungalow/Build Shell Scene**: shell, colliders, doors (below), markers, prefabs.
  2. **BLINK/Bungalow/Place Props**, then rebake the navmesh (`BungalowGreyboxBuilder.Bake`). Props sit under the NavMeshSurface, so the furniture carves the navmesh.
  3. **BLINK/Bungalow/Build Lighting**: lights attach to the `FX_<id>` emitters (18 of 18); NightSky is tuned and enabled and the cameras clear to `globals.sky`.
  4. Bake, `NormalizeLightmapDensity`, bake again, save.
- **Doors** (BLINK/Bungalow/Setup Doors, `BungalowDoorSetup`; the FBX is unchanged; see ARCHITECTURE.md, Doors):
  - Every leaf (found by name) is a `Door`: non-static (light probes, Contribute GI off), turned about its hinge, **shut at start**. Interact opens it away from the player in about 0.5 s (real time), or closes it; it never sweeps the player. Swing measured against walls and jambs: 90° to the front (the modelled side), 70° to the back.
  - Locks (`DOOR_STATE`): D1 one key per puzzle room, D2 the bedroom key, D4 a story lock. Keys go in one at a time (one hand).
  - Navmesh: every leaf is left out of the bake and carves a `NavMeshObstacle` while locked (D1, D2, D4: no path) or open and still. Closed unlocked doors (D3, D5) keep their 0.56 m strip and zombies shove them open. Checked before locking: no patrol route or spawn path uses D1 or D4.
  - Player capsule spare with the leaves fully open: D1 +0.433 m, D2 / D3 / D5 +0.226 m (front); D1 +0.333 m, D2 / D3 / D5 +0.171 m (back, 70°).
- **Checks with the props in (doors pass):** the furniture-aware walk test gives 45 / 45 legs with 0 stuck with every leaf fully open to its front; with every leaf at its 70° back limit, 43 / 45 (the two misses are the walker's fixed waypoint beside D5 on the B1 side, where that open leaf stands; the doorway still fits the capsule). Navmesh with D1 and D4 locked and D2–D5 unlocked: 549 tris; D1 and D4 carved shut (0 m strip), D2, D3, D5 0.56 m; all three patrol routes complete; every spawn on the navmesh; P0 reaches every room. Unlocking D1 reopens its 0.96 m strip at runtime. Room coverage 66–96 %. Not re-checked since: D2 locked (the start bedroom, so P0 is shut in until the bedroom key) and Lily's doll placed in the bathroom; navmesh now 541 tris.
- **Totals:** 194.5k tris (shell 84.2k, props and fixtures 110.0k, decals 0.4k), 20 materials.

## 7. Risks and open questions

### Questions for the user

Q1–Q10 were answered by USER DECISIONS 1–10 (see BRIEF.md). Revision 2 adds:
- **Q11 Triangle budget** (§4.5): keep ≤ 80k with a lighter kit and sparse dressing, or raise the cap to ~110k?

Phase 1 questions, for the record:
- **Q1 NavMesh agent (project setting).** Add an agent type "BungalowZombie" (r 0.30, h 1.8, step 0.3, slope 40) and set it on this scene's zombie instances as an override (recommended). The alternative is changing the Humanoid agent to r 0.35 / climb 0.3, which affects the next Sandbox rebake.
- **Q2 Wear shader.** OK to add a URP **Lit Shader Graph** (same texture conventions, plus a UV2 wear atlas) for the modular kit? Without it, per-piece wear means unique per-piece materials and the 20-material cap can't hold. The fallback is baked unique kit atlases: wear stays, but texel density drops.
- **Q3 Ref-1 hall lighting.** Light the hall as the image shows (chandelier H1 + sconces H2/H3 dim + table lamp H4), or as the brief text says (only H4 + the player light)? The default here follows the image.
- **Q4 Camera post and DayNightCycle.** May I override the Player camera's post-processing and disable `DayNightCycle` in `House_Bungalow.unity`, both as scene-level prefab-instance overrides? Or should the fix go on the prefabs for every level?
- **Q5 Flashlight.** None exists. Add a spot light to the player (where: a new Player child via scene override, or a prefab change?) or rely on the Inventory candle? The ref-1 match assumes a flashlight.
- **Q6 Basement access vs story.** Keep the open ref-1 stairwell with an optional lockable gate G1 at the top (fits P5's `key_basement`)? Or move to a door at the stair foot or in the kitchen?
- **Q7 Lights not all Mixed.** Accept the Mixed / Realtime / Baked split in §5? Making all of them Mixed would lose lights to Shadowmask overflow or leak through walls.
- **Q8 Per-room fog.** Add a small `FogZone` trigger component that lerps `RenderSettings.fogDensity` per room? Or keep one global density?
- **Q9 Paintings and rug sources.**
  - The landscape is the CC0 Poly Haven canvas.
  - The oval portrait and the oriental-rug pattern: self-authored (default), or public-domain museum scans? The Met Open Access is CC0, but its policy page couldn't be loaded today (HTTP 429), so it is unverified.
- **Q10 URP Decal renderer feature** (the brief asks before enabling). This plan uses **mesh decals** (`M_Decals`), so no project change is needed unless you prefer the feature.

### Risks
- **Decimation:** Rockingchair_01 (spindles) and GothicBed_01 (carved posts) lose shape below about 3k/5k. The fallback is a self-built rocker on the `M_Atlas_Wood` budget.
- **Atlas density:** `M_Atlas_Soft` holds big upholstered surfaces, rugs and curtains at 1K. The spare 20th material is earmarked for the rugs.
- **Navmesh at doors:** a 0.23 m strip at 0.05 voxels is fine but fragile if door leaves are included in the bake. Open leaves get "Not Walkable" and are excluded, or carve only when closed.
- **Batches:** about 110–130 is estimated. Realtime shadow casters for three point lights (6 faces each) are the main risk. C1 and H4 are close vertically through the slab, so shadow distance and culling need checking.
- **Ref-3 colour:** ref 3 is a mobile game with flat, raised blacks. Matching it means lifting blacks only in the bedroom volume, so the transition into the hall must blend over 1 m.
- **Time:** Phases 3–5 (scans, trim sheet, wear shader, 4 atlases, about 60 props) are the bulk. If the deadline approaches, cut in this order:
  1. per-room fog
  2. kitchen and bathroom dressing
  3. decimated PolyHaven extras, replaced by simple self-built boxes

  The lighting match is never cut.

---

## 8. Future expansion (USER DIRECTION 12)

The user will later want a **long corridor**, an **attic** (pull-down ladder or narrow stair) and **more rooms or wings**, to grow the house and make the game scarier. Nothing below is built yet; the data model and the generator are set up so each is a matter of adding table entries.

### 8.1 How the house is generated
- **Levels** (`LEVELS`): basement (−2.85), ground (0.00, ceiling 3.00, slab top 3.20). An `attic` entry is reserved (commented) with its floor at y = 3.20, the top of the ground-floor ceiling slab.
- **Rooms** (`ROOMS`): a rectangle per room plus its level. Each gets a floor slab, and a ceiling slab on levels that have one, cut by the room's `floor_holes` / `ceiling_holes` (the stair well is a floor hole of the central hall).
- **Walls** (`WALLS`): a run with a thickness band, a length range, a height range and the openings cut through it. **Openings** (`OPENINGS`): doors, arches, cased openings and windows with their span and height on a wall.
- **Markers, lights, props**: `SPAWNS`, `ROUTES`, `CAMERAS`, `FIXTURES`, `FURNITURE` key off the same coordinates; the Unity builder reads the JSON, and the navmesh surface collects every generated collider, so new rooms are walkable after a rebuild.
- `check()` fails the build data if a room side is left open, a doorway is under 1.2 × 2.2, or a hallway is under 1.8.

### 8.2 Reserved attach points (drawn dashed grey on the plan, green = where it connects)
| ID | What | Where | Notes |
|---|---|---|---|
| Corridor_N | **Built in revision 3**, see §8.4 | | |
| AtticHatch | **Attic access**, pull-down ladder | Ceiling hole in the central hall north landing, x 9.7–10.9, z 12.1–12.8 | The ladder lands at z ≈ 11.0 on the 4.1 m-deep north landing, clear of A4 (z 10.5), H1 and the radiator wall. A narrow stair alternative fits along the landing's E wall. Add the `attic` level, attic rooms and a `ceiling_holes` entry |
| Wing_E | **Utility / mudroom wing** | x 20.3–26.0, z 9.0–14.7, through D4 (kitchen back door) | D4 is already locked for story use; unlocking it opens the wing |

Other natural extensions: a west wing off the bedroom's W wall (WR1 side), and extending the basement under the entrance hall (it is solid ground now).

### 8.3 Design rules to keep when expanding
- Doorways ≥ 1.2 × 2.2 (double doors or wide openings where they fit); hallways ≥ 1.8 clear; ceilings 3.00 on the ground floor.
- Keep long views through **aligned openings** (§1.6) for the blink mechanic, with 1–3 zombies at a time.
- New walls and floors snap to the 0.5 m grid; new rooms get a tint, a reference and lighting entries in the same tables.

### 8.4 Revision 3: north wing (as built)
The house grows north from 20 × 15 to **20 × 26** (`FOOTPRINT`; the builders take it from `WALLS`). Rooms are copies of existing ones, altered:

| Room | Clear size | Copied from | Finish |
|---|---|---|---|
| Corridor | 19.4 × 1.8 | Central hall (hand-placed props) | Plank (boards along x), `M_Wall_Pale`, varnish, skirting / dado / picture / cornice |
| MasterBedroom | 7.625 × 8.75 | Bedroom, without the fireplace items | Plank, `M_Wall_Damask`, paint_dark |
| Parlour | 11.625 × 8.75 | Living | Parquet, `M_Wall_Parlour` (new), varnish |

- **Openings.** D6 (hall N wall) and D7 (corridor → parlour) are both at x 8.65–9.85, centred on D1, so the **D1 → A2 → D6 → D7 sightline runs about 25.8 m** to the parlour's north wall. D8 is corridor → master bedroom. D9 (kitchen → corridor) and the AC1 arch (master ↔ parlour) add two chase loops. Windows: WM1, WM2, WP1, WP2 (boarded), WC1, WC2. WB1 and WK1 are now borrowed lights into the corridor, cased on both faces. BW1 and its light well are gone (they sat under the corridor).
- **Doors.** All four leaves are panel4. D6 opens to 170° so it folds flat on the corridor wall (at 90° it left 0.6 m of corridor). D9 stays at 90°, which only closes the dead end by WC2.
- **Hall changes.** `Radiator_Hall` and its drops are removed (D6 is cut there). H2 / H3 now flank D6 at x 8.20 / 10.30, and `Hal_Picture` moves to the hall W wall. Radiators are added in the parlour (E wall) and master bedroom (W wall).
- **Props.** `bungalow_props.copy_room(src, dst, prefix, skip)` clones a room's rows: coordinates within 1.2 m of a wall keep their offset from the matching wall, and the rest map proportionally. The copies are then altered by hand (other models, regrouped furniture, curtains on the new windows) and kept clear of door swings, AC1, the radiators and the R6 patrol loop.
- **Lights.** CO1 / CO2 (domes), CO3 (broken sconce, off), P1 (chandelier), P2 (table lamp on Par_EndTable), M1 (fan), M2 (bedside lamp on Mbr_Nightstand), and window area lights MW1, MW2, PW1, CW3, CW4. All are **Baked**. KW1 / WW1 now look into the corridor. Post: the corridor shares the Hall volume; Parlour and MasterBedroom get their own.
- **Zombies.** Z5 patrols the corridor (R5); Z6 loops the parlour (R6).
- **Material #21.** `M_Wall_Parlour`: a faded Regency stripe on the ambientCG `Wallpaper001C` paper scan (CC0), built like the other wallpapers (`bungalow_surfaces.py build <cache> --rev3`). No free juliovii.itch.io pack had a period wallpaper.
- **Triangles (cap 100k for revision 3).** Shell 115,746 (+31.6k: corridor +11.5k, master bedroom +14.0k, parlour +10.0k, minus 4.2k for the removed radiator and BW1). New props and fixtures 45.5k (62 instances). **About 77k in total.**
- **Rebuild.** `python -I tools/blender/bungalow_layout.py` → Blender `build_all.py -- --target bungalow Bungalow_Greybox Bungalow_Shell Bungalow_Kit` (the wear bake takes about 3 min on CPU with Blender 4.5) → `python tools/blender/unity_setup.py --target bungalow` (it also rewrites line endings on the Props and Trim metas; discard those) → `python -I tools/blender/bungalow_props.py` and `bungalow_decals.py` → in Unity: Build Surface Materials, then Build Shell Scene, Place Props, navmesh rebake, Build Lighting, bake (§6.5).
- **Attic.** Designed and then deferred by the user. The layout is in commit `fda6026`: a steep stair on the hall E wall rising south, a 19.4 × 7.6 m attic with a 2.4 m ceiling. AtticHatch above stays reserved.

