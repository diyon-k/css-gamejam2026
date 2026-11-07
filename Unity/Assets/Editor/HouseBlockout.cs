// Greybox blockout of the BLINK mansion (layout only: shell, floors, stairs, rooms, holes, markers).
// Regenerate: menu BLINK > Build House Blockout, or batch:
//   Unity.exe -batchmode -quit -nographics -projectPath <Unity> -executeMethod HouseBlockout.Build
// Screenshots (needs graphics, so no -nographics):
//   Unity.exe -batchmode -quit -projectPath <Unity> -executeMethod HouseBlockout.Screenshots -shotDir <folder>
//
// Metres. Footprint x 0..40 (west->east), z 0..24 (south/front->north). Wall lines below are wall centres;
// registered Room rects are the inside of the walls. Storey 3.2 m: slab 0.2 + room 3.0.
// Floor tops: basement -3.2, ground 0, first 3.2, second 6.4, attic 9.6. Doorways 1.4 x 2.3.
// Stairs rise 0.2 / run 0.28 (Player: stepOffset 0.3, slopeLimit 45). PR = PuzzleRoom_N (empty shells).
//
// SECOND (6.4) - start here
//  z24 +---------+---------+-----------------+--------+
//      | Bedroom | Old     | ServantsDorm    | Servants
//      | _North  | Nursery |   [H] attic     | Store  |
//      |         |         |   hatch+ladder  +--D-----+ z16
//      |         |         |                 |Stairwell (top of servants stair, hole x33.6-37.8)
//  12.5+--D------+--D------+--------D--------+------D-+
//      | Corridor_Second  (beam leaning on north wall)        |
//  10  +-J--+-D--+---D----+---D----+--D----+---D---+
//      |Start|Bath| Bedroom| Bedroom| PR_1  |Servants
//      |Bed- +----+ _Rose  | _Blue  +-------+ Room  |
//      |room D Closet      |        D BoxRoom|      |
//  0   +-----+----+--------+--------+-------+-------+
//      0     8    13       21       29      36     40      J = Objective_JammedDoor (x4, z10)
//
// ATTIC (9.6) - pitched roof, ridge along x at z12 (5 m above floor), knee walls z5 / z19, gable windows
//      Attic_Main x0..33 z5..19 (hatch arrives x25-26.2 z15-16.2) | PuzzleRoom_2 x33..40 z8.5..15.5 (door x33,z12, flat ceiling)
//
// FIRST (3.2)
//  z24 +-----------+--------+--------+--------+--------+
//      | Drawing   | Guest  | Bedroom| Study  | Linen  |
//      | Room      | Bed_2  | _Green | _First | Store  |
//      |           |        |        |        +---D----+ z16
//      |           |        |        |        |Stairwell (servants stair up, bottom at x33.6)
//  12.5+----D------+---D----+---D----+---D----+D-------+
//      | Corridor_First / gallery (rail over hall x15-21.2) |
//  10  +----D-+--D-+ rail......+landing+--D-----+---D---+
//      |Master|Bath|  (void:   |  ^    | Guest  | PR_3  |
//      |Bed-  +----+  hall is  | grand | Bed_1  +-------+ z3
//      |room  D Linen double h)| stair | D      | WC    |
//  0   +------+----+-----------+-------+--------+-------+
//      0      10   15          21.2    25       33     40
//
// GROUND (0)
//  z24 +---------+-----+---------+-------+--------+
//      | Kitchen |Pan- | Dining  | Study | Servants
//      | [stair  |try  |         |       | Hall [drop
//      |  down]  D     D         |       |  ladder]
//  12.5+--D------+--D--+----D----+---D---+---D----+
//      | Corridor_Ground (open to the hall) |
//  10  +-----D-------+ grand stair  +---D----+--D---+
//      | Ballroom    D EntranceHall D Library| Cloak |
//      |             |  (6.2 high)  |        +---D---+ z7
//      |             |              |        | PR_4  |
//  0   +-------------+--FrontDoor---+--------+-------+   front door boarded up
//      0             15             25       34     40
//
// BASEMENT (-3.2)
//  z24 +-------------+--------------+---------+
//      | Cellar      | Workshop     | Basement_East
//      | (stair from |              | (drop ladder from ServantsHall)
//      |  kitchen)   |              |         |
//  10.5+------D------+------D-------+----D----+
//      | Basement_Passage                     |
//  8   +--D--+--D----+------D-------+--D---+--+-----+
//      | PR_5|CoalStore| BoilerRoom | PR_6  D Generator  Exit_Gate (x40,z4) -> ExitYard
//  0   +-----+-------+--------------+------+--------+
//      0     8       15             25     32      40
//
// Critical path: StartBedroom -> Corridor_Second -> PR_1 -> attic ladder -> PR_2 -> servants stair (east)
// -> PR_3 -> gallery -> grand stair -> hall -> Cloakroom -> PR_4 -> Kitchen stair -> PR_5 -> PR_6
// -> GeneratorRoom -> Exit_Gate. Shortcut: ServantsHall floor collapse, ladder down to Basement_East.
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using static HouseBlockout.RoomKind;

public static partial class HouseBlockout
{
    const string ScenePath = "Assets/Scenes/House.unity";
    const string NavPath = "Assets/Scenes/House_NavMesh.asset";
    const string MatDir = "Assets/Materials/House";
    const float T = 0.2f;        // wall / slab thickness
    const float H = 3.0f;        // room height
    const float Storey = 3.2f;   // floor to floor
    const float DoorTop = 2.3f;
    const float W = 40f, D = 24f;                // footprint
    const float Eave = 0.4f, Ridge = 5f;         // roof height above the attic floor at the walls / at z = D/2
    static float FloorY(int f) => f * Storey;
    static float RoofAt(float d) => Eave + d * (Ridge - Eave) / (D / 2); // roof height above attic floor, d m in from the eave

    static Material wood, plaster, concrete, rust, water, cloth;
    static System.Random rng;

    // A doorway/window cut in a wall: distance along the wall, and height range above the wall base.
    struct Hole
    {
        public float from, to, bottom, top;
        public Hole(float from, float to, float bottom, float top) { this.from = from; this.to = to; this.bottom = bottom; this.top = top; }
        public static Hole Door(float from) => new Hole(from, from + 1.4f, 0f, DoorTop);
        public static Hole Window(float from, float w = 1.4f) => new Hole(from, from + w, 1.0f, 2.2f);
    }

    [MenuItem("BLINK/Build House Blockout")]
    public static void Build()
    {
        rng = new System.Random(1337); // fixed seed: same output every run
        ResetContract();
        MakeMaterials();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var house = new GameObject("House").transform;
        BuildBasement(Group("Floor_Basement", house));
        BuildGround(Group("Floor_Ground", house));
        BuildFirst(Group("Floor_First", house));
        BuildSecond(Group("Floor_Second", house));
        BuildAttic(Group("Floor_Attic", house));
        BuildRoof(Group("Roof", house));
        BuildMarkers(Group("Markers", house));

        // Other partial files (see HouseBlockout.Contract.cs for the order and who owns what).
        AddInteractables(house);
        DressRooms(house);
        AddDecay(house);
        BakeNavMesh(house);
        StoryProps.Place(); // photos + notes (story workstream), placed by raycast against the built house

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[HouseBlockout] Saved " + ScenePath);
    }

    // ---------------------------------------------------------------- Basement (-3.2)
    static void BuildBasement(Transform p)
    {
        const int f = -1;
        float y = FloorY(f);
        Slab("Slab_Basement", p, 0, W, 0, D, y, concrete);

        R(p, "PuzzleRoom_5", PuzzleRoom, f, 0, 0, 8, 8);
        R(p, "CoalStore", Storage, f, 8, 0, 15, 8);
        R(p, "BoilerRoom", Basement, f, 15, 0, 25, 8);
        R(p, "PuzzleRoom_6", PuzzleRoom, f, 25, 0, 32, 8);
        R(p, "GeneratorRoom", Basement, f, 32, 0, 40, 8);
        R(p, "Basement_Passage", Corridor, f, 0, 8, 40, 10.5f);
        R(p, "Cellar", Basement, f, 0, 10.5f, 15, 24);
        R(p, "Workshop", Storage, f, 15, 10.5f, 30, 24);
        R(p, "Basement_East", Basement, f, 30, 10.5f, 40, 24);

        WX(p, y, 8, 0, W, 4, 11.5f, 20, 28.5f);
        WX(p, y, 10.5f, 0, W, 12, 22.5f, 35);
        WZ(p, y, 8, 0, 8);
        WZ(p, y, 15, 0, 8);
        WZ(p, y, 25, 0, 8);
        WZ(p, y, 32, 0, 8, 4); // PR_6 -> GeneratorRoom: the only way to the generator
        WZ(p, y, 15, 10.5f, D);
        WZ(p, y, 30, 10.5f, D);
        Exterior(p, f, false, (3, new Hole(3f + T / 2, 5f + T / 2, 0, 2.4f))); // exit gate opening, east wall z 3..5

        // Stairs down from the kitchen: top x 6.4 (y 0) -> bottom x 2.2, z 22.1..23.8, solid.
        Steps(Group("Stairs_Basement", p), 6.4f, -1, 0f, 0, 14, 22.1f, 23.8f, y);
        ReserveRect(0.1f, 21.5f, 6.4f, 23.9f, y, 5.5f);

        var gen = Box("Objective_Generator", p, new Vector3(36f, y + 0.6f, 1.2f), new Vector3(1.6f, 1.2f, 0.9f), rust);
        Reserve(gen.GetComponent<Renderer>().bounds);
        Box("Exit_Gate", p, new Vector3(W, y + 1.2f, 4f), new Vector3(0.1f, 2.4f, 2f), rust);
        ReserveRect(W - 1.6f, 3f, W, 5f, y);
        Slab("ExitYard", p, W, W + 4, 2.5f, 5.5f, y, concrete);
    }

    // ---------------------------------------------------------------- Ground (0)
    static void BuildGround(Transform p)
    {
        const int f = 0;
        float y = FloorY(f);
        SlabHole("Slab_Ground_W", p, 0, 20, 0, D, y, wood, Rect.MinMaxRect(2.0f, 21.8f, 6.4f, 23.9f)); // basement stair
        SlabHole("Slab_Ground_E", p, 20, W, 0, D, y, wood, new Rect(37.4f, 20f, 1.4f, 1.4f));          // collapsed floor (ladder)

        R(p, "Ballroom", Ballroom, f, 0, 0, 15, 10);
        R(p, "EntranceHall", Hall, f, 15, 0, 25, 10, 2 * Storey - T); // double height
        R(p, "Library", Library, f, 25, 0, 34, 10);
        R(p, "PuzzleRoom_4", PuzzleRoom, f, 34, 0, 40, 7);
        R(p, "Cloakroom", Storage, f, 34, 7, 40, 10);
        R(p, "Corridor_Ground", Corridor, f, 0, 10, 40, 12.5f);
        R(p, "Kitchen", Kitchen, f, 0, 12.5f, 10, 24);
        R(p, "Pantry", Storage, f, 10, 12.5f, 15, 24);
        R(p, "Dining", Dining, f, 15, 12.5f, 25, 24);
        R(p, "Study", Study, f, 25, 12.5f, 32, 24);
        R(p, "ServantsHall", Servants, f, 32, 12.5f, 40, 24);

        WX(p, y, 10, 0, 15, 7.5f);
        WX(p, y, 10, 25, W, 29.5f, 37);
        WX(p, y, 12.5f, 0, W, 5, 12.5f, 20, 28.5f, 36);
        DoorWall(p, new Vector3(15, y, 0), new Vector3(15, y, 10), Storey, 5); // hall walls reach the first-floor slab
        DoorWall(p, new Vector3(25, y, 0), new Vector3(25, y, 10), Storey, 5);
        WZ(p, y, 34, 0, 10);
        WX(p, y, 7, 34, W, 37);
        WZ(p, y, 10, 12.5f, D, 20);
        WZ(p, y, 15, 12.5f, D, 20);
        WZ(p, y, 25, 12.5f, D);
        WZ(p, y, 32, 12.5f, D);
        Exterior(p, f, true, (0, Hole.Door(20 - 0.7f + T / 2)));

        // Front door boarded shut: not an exit.
        Box("FrontDoor_Barricaded", p, new Vector3(20, y + DoorTop / 2, 0.06f), new Vector3(1.4f, DoorTop, 0.06f), wood);
        for (int i = 0; i < 3; i++)
            Box("FrontDoor_Plank_" + i, p, new Vector3(20, y + 0.6f + i * 0.6f, 0.14f), new Vector3(1.9f, 0.16f, 0.04f), wood,
                new Vector3(0, 0, Rand(-25, 25)));

        // Grand staircase: bottom x 17 (hall floor) -> top x 21.2 (first-floor landing), z 7.5..9.8, solid.
        Steps(Group("Stairs_Grand", p), 21.2f, -1, Storey, 0, 14, 7.5f, 9.8f, y);
        ReserveRect(15.1f, 7.4f, 21.2f, 9.9f, y, Storey + DoorTop);

        // Rails round the basement stair opening (enter from the east).
        Rail(p, 2.0f, 21.8f, 6.4f, 21.8f, y);
        Rail(p, 2.0f, 21.8f, 2.0f, 23.9f, y);
        ReserveRect(6.4f, 21.6f, 8.0f, 23.9f, y); // approach

        AddLadder(p, "Ladder_BasementDrop", new Vector3(38.1f, FloorY(-1), 19.4f), new Vector3(38.1f, y, 22.0f), Vector3.forward);

        Derelict(Box("Beam_Ballroom", p, new Vector3(2.2f, y + 1.3f, 8.9f), new Vector3(4f, 0.25f, 0.25f), wood, new Vector3(0, 15, 35)));
    }

    // ---------------------------------------------------------------- First (3.2)
    static void BuildFirst(Transform p)
    {
        const int f = 1;
        float y = FloorY(f);
        // Hall void x 15..25, z 0..10 apart from the stair landing.
        Slab("Slab_First_SW", p, 0, 15, 0, 10, y, wood);
        Slab("Slab_First_SE", p, 25, W, 0, 10, y, wood);
        Slab("Slab_First_Landing", p, 21.2f, 25, 7, 10, y, wood);
        Slab("Slab_First_N", p, 0, W, 10, D, y, wood);

        R(p, "MasterBedroom", Bedroom, f, 0, 0, 10, 10);
        R(p, "LinenRoom", Storage, f, 10, 0, 15, 5);
        R(p, "Bathroom_First", Bathroom, f, 10, 5, 15, 10);
        var landing = R(p, "GrandStair_Landing", Stairwell, f, 21.2f, 7, 25, 10);
        R(p, "GuestBedroom_1", Bedroom, f, 25, 0, 33, 10);
        R(p, "WC_First", Bathroom, f, 33, 0, 40, 3);
        R(p, "PuzzleRoom_3", PuzzleRoom, f, 33, 3, 40, 10);
        R(p, "Corridor_First", Corridor, f, 0, 10, 40, 12.5f);
        R(p, "DrawingRoom", Ballroom, f, 0, 12.5f, 12, 24);
        R(p, "GuestBedroom_2", Bedroom, f, 12, 12.5f, 20, 24);
        R(p, "Bedroom_Green", Bedroom, f, 20, 12.5f, 26, 24);
        R(p, "Study_First", Study, f, 26, 12.5f, 32, 24);
        R(p, "Stairwell_First", Stairwell, f, 32, 12.5f, 40, 16);
        R(p, "LinenStore", Storage, f, 32, 16, 40, 24);

        WX(p, y, 10, 0, 15, 5, 12.5f);
        WX(p, y, 10, 25, W, 29, 36.5f);
        WX(p, y, 12.5f, 0, W, 6, 16, 23, 29, 33);
        WX(p, y, 5, 10, 15);
        WZ(p, y, 10, 0, 10, 2.5f);
        WZ(p, y, 15, 0, 10);
        WZ(p, y, 25, 0, 10);
        WZ(p, y, 33, 0, 10, 1.5f);
        WX(p, y, 3, 33, W);
        WZ(p, y, 12, 12.5f, D);
        WZ(p, y, 20, 12.5f, D);
        WZ(p, y, 26, 12.5f, D);
        WZ(p, y, 32, 12.5f, D);
        WX(p, y, 16, 32, W, 39);
        Exterior(p, f, true);

        // Gallery rails round the hall void.
        Rail(p, 15, 10.04f, 21.2f, 10.04f, y);
        Rail(p, 21.2f, 7.04f, 25, 7.04f, y);
        Rail(p, 21.24f, 7, 21.24f, 7.5f, y);
        ReserveRect(landing.rect.xMin, landing.rect.yMin, landing.rect.xMax, landing.rect.yMax, y);

        // Servants' stair (first -> second): bottom x 33.6 -> top x 37.8, z 14.3..15.9, solid.
        Steps(Group("Stairs_Servants", p), 37.8f, -1, FloorY(2), 0, 14, 14.3f, 15.9f, y);
        ReserveRect(32.1f, 12.6f, 39.9f, 15.9f, y, Storey + DoorTop); // whole stairwell, both floors
    }

    // ---------------------------------------------------------------- Second (6.4) - start floor
    static void BuildSecond(Transform p)
    {
        const int f = 2;
        float y = FloorY(f);
        SlabHole("Slab_Second", p, 0, W, 0, D, y, wood, Rect.MinMaxRect(33.6f, 13.9f, 37.8f, 16f)); // servants' stair

        var start = R(p, "StartBedroom", StartBedroom, f, 0, 0, 8, 10);
        R(p, "Closet", Storage, f, 8, 0, 13, 4);
        R(p, "Bathroom_Second", Bathroom, f, 8, 4, 13, 10);
        R(p, "Bedroom_Rose", Bedroom, f, 13, 0, 21, 10);
        R(p, "Bedroom_Blue", Bedroom, f, 21, 0, 29, 10);
        R(p, "BoxRoom", Storage, f, 29, 0, 36, 3);
        R(p, "PuzzleRoom_1", PuzzleRoom, f, 29, 3, 36, 10);
        R(p, "ServantsRoom", Servants, f, 36, 0, 40, 10);
        R(p, "Corridor_Second", Corridor, f, 0, 10, 40, 12.5f);
        R(p, "Bedroom_North", Bedroom, f, 0, 12.5f, 10, 24);
        R(p, "OldNursery", Bedroom, f, 10, 12.5f, 20, 24);
        R(p, "ServantsDorm", Servants, f, 20, 12.5f, 32, 24);
        R(p, "Stairwell_Second", Stairwell, f, 32, 12.5f, 40, 16);
        R(p, "ServantsStore", Storage, f, 32, 16, 40, 24);

        WX(p, y, 10, 0, W, 4, 10.5f, 17, 25, 32.5f, 38);
        WX(p, y, 12.5f, 0, W, 5, 15, 26, 39);
        WZ(p, y, 8, 0, 10, 2);
        WX(p, y, 4, 8, 13);
        WZ(p, y, 13, 0, 10);
        WZ(p, y, 21, 0, 10);
        WZ(p, y, 29, 0, 10, 1.5f);
        WX(p, y, 3, 29, 36);
        WZ(p, y, 36, 0, 10);
        WZ(p, y, 10, 12.5f, D);
        WZ(p, y, 20, 12.5f, D);
        WZ(p, y, 32, 12.5f, D);
        WX(p, y, 16, 32, W, 39);
        Exterior(p, f, true);

        Rail(p, 33.6f, 13.94f, 37.8f, 13.94f, y);
        Rail(p, 33.64f, 13.9f, 33.64f, 16, y);

        // The way out of the start bedroom: jammed door filling its corridor doorway (x 3.3..4.7, z 10).
        var jammed = Box("Objective_JammedDoor", start.root, new Vector3(4f, y + DoorTop / 2, 10), new Vector3(1.4f, DoorTop, 0.08f), wood, new Vector3(0, 0, 1.5f));
        jammed.isStatic = false; // it swings open
        jammed.AddComponent<Blink.World.JammedDoor>();

        AddLadder(p, "Ladder_Attic", new Vector3(25.6f, y, 16.8f), new Vector3(25.6f, FloorY(3), 14.4f), Vector3.back);

        // Fallen beam leaning along the corridor's north wall, clear of the walking line.
        Derelict(Box("Beam_Corridor", p, new Vector3(14f, y + 1.3f, 12.2f), new Vector3(3.4f, 0.22f, 0.22f), wood, new Vector3(0, 0, -38)));
    }

    // ---------------------------------------------------------------- Attic (9.6) under the pitched roof
    static void BuildAttic(Transform p)
    {
        const int f = 3;
        float y = FloorY(f), knee = RoofAt(5);
        SlabHole("Slab_Attic", p, 0, W, 0, D, y, wood, new Rect(25f, 15f, 1.2f, 1.2f)); // ladder hatch

        R(p, "Attic_Main", Attic, f, 0, 5, 33, 19, knee);
        R(p, "PuzzleRoom_2", PuzzleRoom, f, 33, 8.5f, 40, 15.5f);

        Wall("Knee_S", p, new Vector3(0, y, 5), new Vector3(W, y, 5), knee, plaster);
        Wall("Knee_N", p, new Vector3(0, y, 19), new Vector3(W, y, 19), knee, plaster);
        Wall("Eave_S", p, new Vector3(-T / 2, y, 0), new Vector3(W + T / 2, y, 0), Eave + 0.1f, plaster);
        Wall("Eave_N", p, new Vector3(-T / 2, y, D), new Vector3(W + T / 2, y, D), Eave + 0.1f, plaster);
        WZ(p, y, 33, 8.5f, 15.5f, 12);
        WX(p, y, 8.5f, 33, W);
        WX(p, y, 15.5f, 33, W);
        Slab("PuzzleRoom_2_Ceiling", p, 33, W, 8.5f, 15.5f, y + H + T, plaster);

        // Gable ends: 0.5 m bands stepped under the roof line, with a window gap (z 11.3..12.7, 1..2 m up).
        for (int k = 0; k < 10; k++)
        {
            float h0 = k * 0.5f, d = Mathf.Max(0, (h0 - Eave) * (D / 2) / (Ridge - Eave));
            foreach (float x in new[] { 0f, W })
            {
                void Band(float z0, float z1) =>
                    Box($"Gable_{x:0}_{k}", p, new Vector3(x, y + h0 + 0.25f, (z0 + z1) / 2), new Vector3(T, 0.5f, z1 - z0), plaster);
                if (h0 >= 1f && h0 < 2f) { Band(d, 11.3f); Band(12.7f, D - d); }
                else Band(d, D - d);
            }
        }
    }

    // Roof in ~4 x 4.3 m tiles (Roof_S/N_row_col) so later passes can punch holes by removing tiles.
    static void BuildRoof(Transform p)
    {
        float y = FloorY(3), rise = Ridge - Eave;
        float a = Mathf.Atan2(rise, D / 2) * Mathf.Rad2Deg, len = Mathf.Sqrt(rise * rise + D * D / 4);
        const int cols = 10, rows = 3;
        float tw = (W + 0.6f) / cols;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float t = (r + 0.5f) / rows, x = -0.3f + tw * (c + 0.5f), yc = y + Eave + t * rise + 0.12f;
            var size = new Vector3(tw, T, len / rows + 0.05f);
            Box($"Roof_S_{r}_{c:00}", p, new Vector3(x, yc, t * D / 2), size, wood, new Vector3(-a, 0, 0));
            Box($"Roof_N_{r}_{c:00}", p, new Vector3(x, yc, D - t * D / 2), size, wood, new Vector3(a, 0, 0));
        }
    }

    static void BuildMarkers(Transform p)
    {
        Marker("PlayerSpawn", p, new Vector3(4f, FloorY(2), 6.5f));
        var spawns = new[]
        {
            new Vector3(37f, FloorY(2), 11.2f),   // second corridor, east end
            new Vector3(23f, FloorY(2), 21f),     // servants' dorm
            new Vector3(8f, FloorY(3), 12f),      // attic west
            new Vector3(28f, FloorY(3), 8f),      // attic near PR_2
            new Vector3(2f, FloorY(1), 11.2f),    // first corridor, west end
            new Vector3(6f, FloorY(1), 20f),      // drawing room
            new Vector3(29f, FloorY(1), 5f),      // guest bedroom 1
            new Vector3(5f, FloorY(0), 4f),       // ballroom
            new Vector3(20f, FloorY(0), 4f),      // entrance hall
            new Vector3(7f, FloorY(0), 16f),      // kitchen
            new Vector3(35f, FloorY(0), 16f),     // servants' hall
            new Vector3(8f, FloorY(-1), 18f),     // cellar
            new Vector3(22f, FloorY(-1), 18f),    // workshop
            new Vector3(20f, FloorY(-1), 4f),     // boiler room
            new Vector3(34f, FloorY(-1), 14f),    // basement east (drop landing)
        };
        for (int i = 0; i < spawns.Length; i++) Marker($"ZombieSpawn_{i + 1:00}", p, spawns[i]);

        // One zombie per spawn. Kept outside House so the navmesh bake (House children only) ignores their colliders.
        var zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie.prefab");
        var zombies = new GameObject("Zombies").transform;
        for (int i = 0; i < spawns.Length; i++)
        {
            var z = (GameObject)PrefabUtility.InstantiatePrefab(zombiePrefab, zombies);
            z.name = $"Zombie_{i + 1:00}";
            z.transform.position = spawns[i];
            z.transform.rotation = Quaternion.Euler(0f, i * 137f, 0f); // not rng: would reshuffle the dressing/decay rolls after this
        }

        // Real Player + GameSystems prefabs so the house is walkable in Play mode. Faces the jammed door.
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameSystems.prefab"));
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effects.prefab")); // gun/melee/impact audio + blood
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
        player.transform.position = new Vector3(4f, FloorY(2) + 0.05f, 6.5f);
        player.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        var cam = player.GetComponentInChildren<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox; // windows show the sky (driven by the Day/Night pass)
        cam.farClipPlane = 300f;                  // forest runs out to ~215 m, treeline rings to ~195 m
        var flash = NewLight("Flashlight", cam.transform, LightType.Spot, new Color(1f, 0.95f, 0.85f), 12f, new Vector3(0.2f, -0.15f, 0));
        flash.range = 25;
        flash.spotAngle = 50;
        flash.innerSpotAngle = 25;
    }

    static void BakeNavMesh(Transform house)
    {
        var roof = house.Find("Roof");
        if (roof) roof.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var s = house.gameObject.AddComponent<NavMeshSurface>();
        s.collectObjects = CollectObjects.Children;
        s.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        s.BuildNavMesh();
        AssetDatabase.DeleteAsset(NavPath);
        AssetDatabase.CreateAsset(s.navMeshData, NavPath); // persist the bake with the scene
    }

    // ---------------------------------------------------------------- Layout helpers
    // Registers a room from wall-centre lines; the stored rect is the inside of the walls.
    static Room R(Transform p, string name, RoomKind k, int f, float x0, float z0, float x1, float z1, float h = H) =>
        AddRoom(name, k, f, Rect.MinMaxRect(x0 + T / 2, z0 + T / 2, x1 - T / 2, z1 - T / 2), FloorY(f), h, p);

    // Interior walls along x (at z) / along z (at x), with 1.4 m doorways centred on `doors`.
    static void WX(Transform p, float y, float z, float x0, float x1, params float[] doors) =>
        DoorWall(p, new Vector3(x0, y, z), new Vector3(x1, y, z), H, doors);
    static void WZ(Transform p, float y, float x, float z0, float z1, params float[] doors) =>
        DoorWall(p, new Vector3(x, y, z0), new Vector3(x, y, z1), H, doors);

    static void DoorWall(Transform p, Vector3 a, Vector3 b, float h, params float[] doors)
    {
        bool alongX = b.x != a.x;
        var holes = new Hole[doors.Length];
        for (int i = 0; i < doors.Length; i++)
        {
            holes[i] = Hole.Door(doors[i] - 0.7f - (alongX ? a.x : a.z));
            var c = alongX ? new Vector3(doors[i], a.y + DoorTop / 2, a.z) : new Vector3(a.x, a.y + DoorTop / 2, doors[i]);
            Reserve(new Bounds(c, alongX ? new Vector3(1.4f, DoorTop, 3.2f) : new Vector3(3.2f, DoorTop, 1.4f)));
        }
        string name = alongX ? $"Wall_z{a.z:0.#}_x{a.x:0.#}-{b.x:0.#}" : $"Wall_x{a.x:0.#}_z{a.z:0.#}-{b.z:0.#}";
        Wall(name, p, a, b, h, a.y < 0 ? concrete : plaster, holes);
    }

    // Point `s` metres along exterior side 0 south, 1 north, 2 west, 3 east (walls start T/2 outside the corner).
    static Vector3 SidePoint(int side, float s, float y) => side switch
    {
        0 => new Vector3(s - T / 2, y, 0),
        1 => new Vector3(s - T / 2, y, D),
        2 => new Vector3(0, y, s - T / 2),
        _ => new Vector3(W, y, s - T / 2),
    };

    // One storey of exterior wall (full storey high, so it hides the slab edge). With `windows`, every room on this
    // floor (or a double-height room from below) gets windows on each exterior side it touches. Ground windows are boarded.
    static void Exterior(Transform p, int f, bool windows, params (int side, Hole hole)[] extra)
    {
        float y = FloorY(f);
        var holes = new List<Hole>[4];
        for (int i = 0; i < 4; i++) holes[i] = new List<Hole>();
        if (windows)
            foreach (var r in Rooms)
            {
                if (r.floor != f && !(r.floor == f - 1 && r.height > H + 1)) continue;
                var rc = r.rect;
                if (rc.yMin < 0.2f) AddWindows(holes[0], rc.xMin, rc.xMax);
                if (rc.yMax > D - 0.2f) AddWindows(holes[1], rc.xMin, rc.xMax);
                if (rc.xMin < 0.2f) AddWindows(holes[2], rc.yMin, rc.yMax);
                if (rc.xMax > W - 0.2f) AddWindows(holes[3], rc.yMin, rc.yMax);
            }
        foreach (var (side, hole) in extra) holes[side].Add(hole);

        var boards = Group("WindowBoards", p);
        for (int side = 0; side < 4; side++)
        {
            float len = (side < 2 ? W : D) + T;
            Wall("Ext_" + "SNWE"[side], p, SidePoint(side, 0, y), SidePoint(side, len, y), Storey, f < 0 ? concrete : plaster, holes[side].ToArray());
            if (f != 0) continue;
            foreach (var h in holes[side])
            {
                if (h.bottom < 0.5f) continue; // doors
                for (int k = -1; k <= 1; k += 2) // two planks in an X: barricaded, still see-through
                    Box("Board", boards, SidePoint(side, (h.from + h.to) / 2, y + 1.6f),
                        side < 2 ? new Vector3(1.8f, 0.16f, 0.04f) : new Vector3(0.04f, 0.16f, 1.8f), wood,
                        side < 2 ? new Vector3(0, 0, 30 * k + Rand(-6, 6)) : new Vector3(30 * k + Rand(-6, 6), 0, 0));
            }
        }
    }

    // One window centred on the span, or two at the quarter points if it's long (keeps clear of a central front door).
    static void AddWindows(List<Hole> l, float s0, float s1)
    {
        float len = s1 - s0, mid = (s0 + s1) / 2;
        if (len > 7) { l.Add(Hole.Window(mid - len / 4 - 0.7f + T / 2)); l.Add(Hole.Window(mid + len / 4 - 0.7f + T / 2)); }
        else if (len > 2) l.Add(Hole.Window(mid - 0.7f + T / 2));
    }

    // Slab over x0..x1, z0..z1 with one rectangular hole (stair opening / hatch).
    static void SlabHole(string name, Transform p, float x0, float x1, float z0, float z1, float top, Material m, Rect hole)
    {
        void S(string n, float a0, float a1, float b0, float b1) { if (a1 - a0 > 0.01f && b1 - b0 > 0.01f) Slab(name + n, p, a0, a1, b0, b1, top, m); }
        S("_S", x0, x1, z0, hole.yMin);
        S("_N", x0, x1, hole.yMax, z1);
        S("_W", x0, hole.xMin, hole.yMin, hole.yMax);
        S("_E", hole.xMax, x1, hole.yMin, hole.yMax);
    }

    // 1 m guard rail from (x0,z0) to (x1,z1) on the floor at y.
    static void Rail(Transform p, float x0, float z0, float x1, float z1, float y)
    {
        var c = new Vector3((x0 + x1) / 2, y + 0.5f, (z0 + z1) / 2);
        var s = new Vector3(Mathf.Max(x1 - x0, 0.08f), 1f, Mathf.Max(z1 - z0, 0.08f));
        Box("Rail", p, c, s, wood);
        Reserve(new Bounds(c, s + new Vector3(0.3f, 0, 0.3f)));
    }

    // Registers a ladder for the Interactivity pass (it builds the mesh + climbing) and keeps both ends clear.
    static void AddLadder(Transform p, string name, Vector3 bottom, Vector3 top, Vector3 facing)
    {
        Ladders.Add(new Ladder { name = name, bottom = bottom, top = top, facing = facing });
        foreach (var e in new[] { bottom, top }) Reserve(new Bounds(e + Vector3.up * 1.2f, new Vector3(2f, 2.4f, 2f)));
        Reserve(new Bounds((bottom + top) / 2 + Vector3.up * 1f, new Vector3(1.6f, top.y - bottom.y + 2.4f, 1.6f))); // shaft
    }

    static void ReserveRect(float x0, float z0, float x1, float z1, float y, float h = DoorTop) =>
        Reserve(new Bounds(new Vector3((x0 + x1) / 2, y + h / 2, (z0 + z1) / 2), new Vector3(x1 - x0, h, z1 - z0)));

    static void Derelict(GameObject go) => Reserve(go.GetComponent<Renderer>().bounds);

    // ---------------------------------------------------------------- Primitives
    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3 rotEuler = default, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(rotEuler);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    static GameObject Slab(string name, Transform p, float x0, float x1, float z0, float z1, float top, Material m) =>
        Box(name, p, new Vector3((x0 + x1) / 2, top - T / 2, (z0 + z1) / 2), new Vector3(x1 - x0, T, z1 - z0), m);

    // Axis-aligned wall from a to b (a.y = base), cut into pieces around holes. Hole distances are measured from a.
    static void Wall(string name, Transform p, Vector3 a, Vector3 b, float h, Material m, params Hole[] holes)
    {
        var dir = (b - a).normalized;
        float len = Vector3.Distance(a, b);
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        var g = Group(name, p);
        int n = 0;
        void Piece(float s0, float s1, float y0, float y1)
        {
            if (s1 - s0 < 0.01f || y1 - y0 < 0.01f) return;
            var c = a + dir * ((s0 + s1) / 2) + Vector3.up * ((y0 + y1) / 2);
            var size = alongX ? new Vector3(s1 - s0, y1 - y0, T) : new Vector3(T, y1 - y0, s1 - s0);
            Box(name + "_" + n++, g, c, size, m);
        }
        System.Array.Sort(holes, (x, y) => x.from.CompareTo(y.from));
        float cur = 0;
        foreach (var hl in holes)
        {
            Piece(cur, hl.from, 0, h);
            Piece(hl.from, hl.to, 0, hl.bottom);
            Piece(hl.from, hl.to, hl.top, h);
            cur = hl.to;
        }
        Piece(cur, len, 0, h);
    }

    // Steps `from..to` of a straight stair along x. Step i spans x xTop+dir*0.28*i .. +0.28, top yTop-0.2*(i+1).
    // solidBase: fill down to this y; null = 0.2 m thick treads with open space underneath.
    static void Steps(Transform p, float xTop, int dir, float yTop, int from, int to, float z0, float z1, float? solidBase)
    {
        const float rise = 0.2f, run = 0.28f;
        for (int i = from; i <= to; i++)
        {
            float top = yTop - rise * (i + 1);
            float bottom = solidBase ?? top - rise;
            float xc = xTop + dir * run * (i + 0.5f);
            Box($"Step_{i:00}" + (z1 - z0 < 2 && solidBase == null ? "_Half" : ""), p,
                new Vector3(xc, (top + bottom) / 2, (z0 + z1) / 2), new Vector3(run, top - bottom, z1 - z0), wood);
        }
    }

    // Unused by the layout; kept as shared helpers for the Dressing / Decay passes.
    static void Pipe(string name, Transform p, Vector3 from, Vector3 to, float r)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(p, false);
        go.transform.localPosition = (from + to) / 2;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        go.transform.localScale = new Vector3(2 * r, Vector3.Distance(from, to) / 2, 2 * r);
        go.GetComponent<Renderer>().sharedMaterial = rust;
        go.isStatic = true;
    }

    static void Table(Transform p, string name, Vector3 pos, Vector3 size, Vector3 rotEuler)
    {
        var g = Group(name, p);
        g.localPosition = pos + Vector3.up * 0.45f; // lift so a tipped table rests roughly on the floor
        g.localRotation = Quaternion.Euler(rotEuler);
        Box("Top", g, new Vector3(0, size.y - 0.45f, 0), new Vector3(size.x, 0.06f, size.z), wood);
        for (int i = 0; i < 4; i++)
        {
            float lx = (i % 2 == 0 ? -1 : 1) * (size.x / 2 - 0.06f), lz = (i < 2 ? -1 : 1) * (size.z / 2 - 0.06f);
            Box("Leg_" + i, g, new Vector3(lx, size.y / 2 - 0.45f, lz), new Vector3(0.06f, size.y, 0.06f), wood);
        }
    }

    static void Chair(Transform p, Vector3 pos, Vector3 rotEuler)
    {
        var g = Group("Chair", p);
        g.localPosition = pos + Vector3.up * 0.25f;
        g.localRotation = Quaternion.Euler(rotEuler);
        Box("Seat", g, new Vector3(0, 0.2f, 0), new Vector3(0.45f, 0.05f, 0.45f), wood);
        Box("Back", g, new Vector3(0, 0.45f, -0.2f), new Vector3(0.45f, 0.5f, 0.05f), wood);
        for (int i = 0; i < 4; i++)
            Box("Leg_" + i, g, new Vector3(i % 2 == 0 ? -0.19f : 0.19f, -0.03f, i < 2 ? -0.19f : 0.19f), new Vector3(0.04f, 0.45f, 0.04f), wood);
    }

    // Small collider-less junk (planks, plaster chunks) inside one of the given floor rects.
    static void Scatter(Transform p, int count, float y, params Rect[] rects)
    {
        var g = Group("Debris", p);
        for (int i = 0; i < count; i++)
        {
            var r = rects[rng.Next(rects.Length)];
            bool plank = rng.NextDouble() < 0.4;
            var size = plank ? new Vector3(Rand(0.6f, 1.4f), 0.03f, Rand(0.08f, 0.16f))
                             : new Vector3(Rand(0.08f, 0.4f), Rand(0.04f, 0.18f), Rand(0.08f, 0.4f));
            var pos = new Vector3(Rand(r.xMin, r.xMax), y + size.y / 2, Rand(r.yMin, r.yMax));
            Box("Debris_" + i, g, pos, size, plank ? wood : (rng.NextDouble() < 0.5 ? plaster : concrete),
                new Vector3(Rand(-12, 12), Rand(0, 360), Rand(-12, 12)), false);
        }
    }

    static void Marker(string name, Transform p, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
    }

    static Light NewLight(string name, Transform p, LightType type, Color c, float intensity, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.color = c;
        l.intensity = intensity;
        l.shadows = LightShadows.Soft;
        return l;
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ---------------------------------------------------------------- Preview screenshots
    public static void Screenshots()
    {
        string dir = Path.Combine(Path.GetTempPath(), "blink_shots");
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotDir") dir = args[i + 1];
        Directory.CreateDirectory(dir);

        EditorSceneManager.OpenScene(ScenePath);

        // First-person views with the real (dark) lighting.
        var player = GameObject.Find("Player");
        var cam = player.GetComponentInChildren<Camera>();
        Shot(cam, Path.Combine(dir, "fp_start_bedroom.png"));
        foreach (var (name, pos, look) in new[]
        {
            ("fp_grand_hall", new Vector3(18f, 0f, 2f), new Vector3(0.3f, 0, 1)),
            ("fp_attic", new Vector3(10f, FloorY(3), 12f), new Vector3(1, 0, 0)),
            ("fp_generator", new Vector3(34f, FloorY(-1), 6.5f), new Vector3(0.4f, 0, -1)),
        })
        {
            player.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look));
            Shot(cam, Path.Combine(dir, name + ".png"));
        }

        // Top-down cutaways per floor: no fog, flat bright ambient, everything above the cut (and the roof) hidden.
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
        SetAmbientProbe(RenderSettings.ambientLight);
        var top = new GameObject("ShotCam").AddComponent<Camera>();
        top.orthographic = true;
        top.orthographicSize = 14f;
        top.clearFlags = CameraClearFlags.SolidColor;
        top.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        top.transform.SetPositionAndRotation(new Vector3(21f, 40f, D / 2), Quaternion.Euler(90, 0, 0));
        var roof = GameObject.Find("House/Roof")?.transform;
        var renderers = Object.FindObjectsByType<Renderer>();
        foreach (var (label, cut) in new[] { ("basement", -0.4f), ("ground", 2.9f), ("first", 6.1f), ("second", 9.3f), ("attic", 12.5f) })
        {
            foreach (var r in renderers) r.enabled = r.bounds.center.y < cut && !(roof != null && r.transform.IsChildOf(roof));
            Shot(top, Path.Combine(dir, $"overview_{label}.png"));
        }
        Debug.Log("[HouseBlockout] Screenshots written to " + dir);
        // The shots move the player, hide upper floors and brighten the lighting: throw all of that away.
        EditorSceneManager.OpenScene(ScenePath);
    }

    static void Shot(Camera cam, string path, int w = 1600, int h = 1000)
    {
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
