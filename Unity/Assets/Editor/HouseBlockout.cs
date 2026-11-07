// Greybox blockout of the BLINK house: upstairs bedroom -> landing -> broken stairs ->
// hallway -> kitchen -> basement generator -> exit gate.
// Regenerate: menu BLINK > Build House Blockout, or batch:
//   Unity.exe -batchmode -quit -nographics -projectPath <Unity> -executeMethod HouseBlockout.Build
// Screenshots (needs graphics, so no -nographics):
//   Unity.exe -batchmode -quit -projectPath <Unity> -executeMethod HouseBlockout.Screenshots -shotDir <folder>
//
// Coordinates in metres. House footprint x 0..15, z 0..10.
// Floor tops: basement y=-3, ground y=0, upstairs y=3. Slabs 0.2 thick, rooms 2.8 tall.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class HouseBlockout
{
    const string ScenePath = "Assets/Scenes/House.unity";
    const string MatDir = "Assets/Materials/House";
    const float T = 0.2f;      // wall / slab thickness
    const float H = 2.8f;      // room height
    const float DoorTop = 2.3f;

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
        rng = new System.Random(1337); // fixed seed: same debris every run
        MakeMaterials();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var house = new GameObject("House").transform;
        var a1 = Group("Area1_Bedroom", house);
        var a2 = Group("Area2_Middle", house);
        var a3 = Group("Area3_Basement", house);
        var lighting = Group("Lighting", house);
        var markers = Group("Markers", house);

        BuildUpstairs(a1, a2);
        BuildGround(a2);
        BuildBasement(a3);
        BuildLighting(lighting);
        BuildMarkers(markers);

        // Roof last so screenshots can hide it by name.
        Slab("Roof", house, 0, 15, 0, 10, 5.8f + T, plaster);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[HouseBlockout] Saved " + ScenePath);
    }

    // ---------------------------------------------------------------- Area 1 + landing (upstairs, y=3)
    static void BuildUpstairs(Transform a1, Transform a2)
    {
        const float y = 3f;

        // Slabs. Landing (x 0..9, z 2..5) is split into rects leaving two holes to route around.
        // Stairwell x 9..15, z 2..5 has no slab.
        Slab("Floor_North", a1, 0, 15, 5, 10, y, wood);
        Slab("Floor_SouthStrip", a2, 0, 15, 0, 2, y, wood);
        var landing = new[] { new Rect(0, 2, 3.5f, 3), new Rect(3.5f, 3.6f, 1.5f, 1.4f), new Rect(5, 2, 1.2f, 3),
                              new Rect(6.2f, 2, 1.4f, 1.4f), new Rect(7.6f, 2, 1.4f, 3) };
        for (int i = 0; i < landing.Length; i++)
            Slab("Landing_Floor_" + i, a2, landing[i].xMin, landing[i].xMax, landing[i].yMin, landing[i].yMax, y, wood);
        // Splintered board stubs poking into the holes.
        Box("HoleStub_A", a2, new Vector3(4.1f, y - 0.05f, 3.3f), new Vector3(0.15f, 0.05f, 0.7f), wood, new Vector3(12, 8, 0), false);
        Box("HoleStub_B", a2, new Vector3(6.9f, y - 0.08f, 3.7f), new Vector3(0.15f, 0.05f, 0.8f), wood, new Vector3(-15, -6, 0), false);

        // Exterior walls (upstairs storey).
        Wall("Ext_West", a1, new Vector3(0, y, -0.1f), new Vector3(0, y, 10.1f), H, plaster, new Hole(7, 8.6f, 1.0f, 2.2f));
        Wall("Ext_East", a1, new Vector3(15, y, -0.1f), new Vector3(15, y, 10.1f), H, plaster, Hole.Window(6.5f));
        Wall("Ext_North", a1, new Vector3(-0.1f, y, 10), new Vector3(15.1f, y, 10), H, plaster, Hole.Window(10f));
        Wall("Ext_South", a1, new Vector3(-0.1f, y, 0), new Vector3(15.1f, y, 0), H, plaster);

        // Bedroom: x 0..6, z 5..10. Jammed door in its south wall at x 2.6..4.0.
        // The same z=5 wall has the side-room door at x 7.2..8.6.
        Wall("Wall_BedroomSouth", a1, new Vector3(0, y, 5), new Vector3(15, y, 5), H, plaster, Hole.Door(2.6f), Hole.Door(7.2f));
        Wall("Wall_BedroomEast", a1, new Vector3(6, y, 5), new Vector3(6, y, 10), H, plaster);
        Wall("Wall_LandingSouth", a2, new Vector3(0, y, 2), new Vector3(15, y, 2), H, plaster);

        Box("Objective_JammedDoor", a1, new Vector3(3.3f, y + DoorTop / 2, 5), new Vector3(1.4f, DoorTop, 0.08f), wood, new Vector3(0, 0, 1.5f));

        // Boarded west window (opening at z 7..8.6, 1..2.2 above floor).
        for (int i = 0; i < 4; i++)
            Box("WindowBoard_" + i, a1, new Vector3(0.14f, y + 1.15f + i * 0.3f, 7.8f), new Vector3(0.04f, 0.18f, 1.9f), wood,
                new Vector3(Rand(-12, 12), 0, 0));

        // Bedroom props.
        Box("Mattress", a1, new Vector3(1.4f, y + 0.1f, 8.6f), new Vector3(1.4f, 0.2f, 2.0f), cloth, new Vector3(0, 14, 2));
        Box("Wardrobe_Collapsed", a1, new Vector3(5.1f, y + 0.55f, 7.6f), new Vector3(1.1f, 2.0f, 0.6f), wood, new Vector3(68, 80, 0));
        Box("Wardrobe_Door", a1, new Vector3(4.3f, y + 0.03f, 6.1f), new Vector3(0.5f, 0.03f, 1.8f), wood, new Vector3(0, 35, 0), false);
        Box("Nightstand", a1, new Vector3(0.45f, y + 0.3f, 6.2f), new Vector3(0.5f, 0.6f, 0.45f), wood, new Vector3(0, 8, 0));
        Chair(a1, new Vector3(1.0f, y, 5.8f), new Vector3(0, 40, 90)); // tipped over
        Scatter(a1, 14, y, new Rect(0.3f, 5.3f, 5.4f, 4.4f));

        // Landing / side room (x 6..15, z 5..10): a door ripped half off its hinges, zombies wander out.
        Box("SideRoom_DoorHanging", a2, new Vector3(8.3f, y + 1.05f, 4.4f), new Vector3(1.3f, 2.2f, 0.06f), wood, new Vector3(0, -60, 12));
        Box("SideRoom_Bedframe", a2, new Vector3(11.5f, y + 0.25f, 8.3f), new Vector3(1.8f, 0.5f, 2.1f), wood, new Vector3(0, -8, 4));
        Box("SideRoom_Cot", a2, new Vector3(8.4f, y + 0.4f, 8.8f), new Vector3(1.2f, 0.8f, 0.7f), wood, new Vector3(0, 20, -25));
        Scatter(a2, 10, y, new Rect(6.3f, 5.3f, 8.4f, 4.4f));
        Scatter(a2, 12, y, landing);
        // Fallen ceiling beam along the landing's south wall, clear of the walking line.
        Box("Beam_Landing", a2, new Vector3(1.8f, y + 1.3f, 2.35f), new Vector3(3.4f, 0.22f, 0.22f), wood, new Vector3(0, 0, -38));

        // Half-collapsed staircase: x 9 (top, y=3) -> 12.92 (bottom, y=0), z 2.1..4.9.
        // Steps 4..8 lost their south half; intact half z 3.4..4.9 (1.5 m) still fits the capsule.
        var stairs = Group("Stairs_Main", a2);
        Steps(stairs, 9f, +1, y, 0, 3, 2.1f, 4.9f, null);
        Steps(stairs, 9f, +1, y, 4, 8, 3.4f, 4.9f, null);
        Steps(stairs, 9f, +1, y, 9, 13, 2.1f, 4.9f, null);
        Box("Stairs_Rail", stairs, new Vector3(11f, 2.4f, 4.85f), new Vector3(4.3f, 0.08f, 0.08f), wood, new Vector3(0, 0, -35.5f));
        // Collapsed treads lying underneath.
        for (int i = 0; i < 4; i++)
            Box("FallenTread_" + i, stairs, new Vector3(10.1f + i * 0.5f, 0.1f + i * 0.05f, 2.7f), new Vector3(1.1f, 0.06f, 0.3f), wood,
                new Vector3(Rand(-20, 20), Rand(50, 130), Rand(-25, 25)), false);
    }

    // ---------------------------------------------------------------- Area 2 (ground floor, y=0)
    static void BuildGround(Transform a2)
    {
        const float y = 0f;

        // Slab with the basement-stair opening in the kitchen (x 1..5.6, z 8.3..10).
        Slab("Ground_Floor_Main", a2, 0, 15, 0, 8.3f, y, wood);
        Slab("Ground_Floor_NW", a2, 0, 1, 8.3f, 10, y, wood);
        Slab("Ground_Floor_NE", a2, 5.6f, 15, 8.3f, 10, y, wood);

        Wall("G_Ext_West", a2, new Vector3(0, y, -0.1f), new Vector3(0, y, 10.1f), H, plaster, Hole.Window(4f));
        Wall("G_Ext_East", a2, new Vector3(15, y, -0.1f), new Vector3(15, y, 10.1f), H, plaster);
        Wall("G_Ext_North", a2, new Vector3(-0.1f, y, 10), new Vector3(15.1f, y, 10), H, plaster, Hole.Window(10.5f));
        Wall("G_Ext_South", a2, new Vector3(-0.1f, y, 0), new Vector3(15.1f, y, 0), H, plaster, Hole.Window(2.1f), Hole.Door(12.1f));

        // Kitchen x 0..7, z 2.5..10; door from the hallway at x 5.2..6.6.
        Wall("Wall_KitchenSouth", a2, new Vector3(0, y, 2.5f), new Vector3(7, y, 2.5f), H, plaster, Hole.Door(5.2f));
        Wall("Wall_KitchenEast", a2, new Vector3(7, y, 2.5f), new Vector3(7, y, 10), H, plaster);
        // Parlour (x 7..15, z 5..10) is sealed off - only the stairwell nook is open.
        Wall("Wall_Parlour", a2, new Vector3(7, y, 5), new Vector3(15, y, 5), H, plaster);

        // Front door: barricaded, not an exit.
        Box("FrontDoor_Barricaded", a2, new Vector3(12.7f, y + DoorTop / 2, 0.06f), new Vector3(1.4f, DoorTop, 0.06f), wood);
        for (int i = 0; i < 3; i++)
            Box("FrontDoor_Plank_" + i, a2, new Vector3(12.7f, y + 0.6f + i * 0.6f, 0.14f), new Vector3(1.9f, 0.16f, 0.04f), wood,
                new Vector3(0, 0, Rand(-25, 25)));
        Box("Window_Boards_S", a2, new Vector3(2.8f, y + 1.6f, 0.13f), new Vector3(1.6f, 1.1f, 0.04f), wood, new Vector3(0, 0, 4));
        Box("Window_Boards_W", a2, new Vector3(0.13f, y + 1.6f, 4.7f), new Vector3(0.04f, 1.1f, 1.6f), wood, new Vector3(-5, 0, 0));

        // Hallway (z 0..2.5): beam down at a slant along the south wall, toppled side table.
        Box("Beam_Hallway", a2, new Vector3(8.4f, 1.35f, 0.45f), new Vector3(3.9f, 0.25f, 0.25f), wood, new Vector3(0, 0, 40));
        Table(a2, "HallTable", new Vector3(2.0f, y, 1.2f), new Vector3(1.0f, 0.75f, 0.5f), new Vector3(0, -20, 85));
        Scatter(a2, 16, y, new Rect(0.3f, 0.3f, 14.4f, 1.9f));

        // Kitchen props.
        Box("Kitchen_DoorOffHinges", a2, new Vector3(4.4f, y + 1.0f, 2.1f), new Vector3(1.3f, 2.2f, 0.06f), wood, new Vector3(14, 0, 6));
        Box("Counter_West", a2, new Vector3(0.4f, y + 0.45f, 5.3f), new Vector3(0.6f, 0.9f, 5.4f), wood);
        Box("Cabinet_Upper", a2, new Vector3(0.25f, y + 1.9f, 6.5f), new Vector3(0.35f, 0.7f, 2.4f), wood, new Vector3(6, 0, 0));
        Box("Cabinet_Fallen", a2, new Vector3(1.3f, y + 0.2f, 4.0f), new Vector3(0.7f, 0.35f, 1.1f), wood, new Vector3(5, 30, 10));
        Box("Fridge_Toppled", a2, new Vector3(1.6f, y + 0.4f, 7.4f), new Vector3(0.7f, 1.8f, 0.7f), plaster, new Vector3(0, 0, 66));
        Table(a2, "KitchenTable", new Vector3(3.6f, y, 5.6f), new Vector3(1.6f, 0.78f, 0.9f), new Vector3(0, 25, 72));
        Chair(a2, new Vector3(2.5f, y, 6.9f), new Vector3(0, 60, 0));
        Chair(a2, new Vector3(4.7f, y, 4.3f), new Vector3(85, 10, 0));
        Chair(a2, new Vector3(2.0f, y, 3.6f), new Vector3(0, -30, -88));
        Box("Beam_Kitchen", a2, new Vector3(4.6f, 1.45f, 7.3f), new Vector3(0.25f, 0.25f, 3.6f), wood, new Vector3(52, 8, 0));
        Box("Pickup_BandageStation", a2, new Vector3(6.6f, y + 0.6f, 6.0f), new Vector3(0.5f, 1.2f, 0.9f), cloth);
        Scatter(a2, 14, y, new Rect(0.8f, 2.8f, 6.0f, 5.4f));

        // Guard rails around the basement stair opening (enter from the east, heading west).
        Box("BasementRail_S", a2, new Vector3(3.3f, y + 0.5f, 8.3f), new Vector3(4.6f, 1.0f, 0.06f), wood);
        Box("BasementRail_W", a2, new Vector3(1.0f, y + 0.5f, 9.15f), new Vector3(0.06f, 1.0f, 1.7f), wood);
    }

    // ---------------------------------------------------------------- Area 3 (basement, y=-3)
    static void BuildBasement(Transform a3)
    {
        const float y = -3f;

        Slab("Basement_Floor", a3, 0, 15, 0, 10, y, concrete);
        Wall("B_Ext_West", a3, new Vector3(0, y, -0.1f), new Vector3(0, y, 10.1f), H, concrete, new Hole(1.3f, 3.3f, 0, 2.4f));
        Wall("B_Ext_East", a3, new Vector3(15, y, -0.1f), new Vector3(15, y, 10.1f), H, concrete);
        Wall("B_Ext_North", a3, new Vector3(-0.1f, y, 10), new Vector3(15.1f, y, 10), H, concrete);
        Wall("B_Ext_South", a3, new Vector3(-0.1f, y, 0), new Vector3(15.1f, y, 0), H, concrete);
        // Partition makes a U route: north half east -> gap at x 11..15 -> south half west.
        Wall("B_Partition", a3, new Vector3(0, y, 5), new Vector3(11, y, 5), H, concrete);

        // Stairs down from the kitchen: x 5.6 (top, y=0) -> 1.68 (bottom), z 8.4..9.9, solid.
        var stairs = Group("Stairs_Basement", a3);
        Steps(stairs, 5.6f, -1, 0f, 0, 13, 8.4f, 9.9f, y);

        // Pipes along the ceiling, a few drops.
        Pipe("Pipe_N1", a3, new Vector3(0.3f, -0.45f, 5.4f), new Vector3(14.7f, -0.45f, 5.4f), 0.07f);
        Pipe("Pipe_N2", a3, new Vector3(0.3f, -0.6f, 5.6f), new Vector3(14.7f, -0.6f, 5.6f), 0.05f);
        Pipe("Pipe_S1", a3, new Vector3(14.7f, -0.5f, 4.6f), new Vector3(0.3f, -0.5f, 4.6f), 0.08f);
        Pipe("Pipe_EastDrop", a3, new Vector3(14.7f, -0.45f, 5.4f), new Vector3(14.7f, -2.9f, 5.4f), 0.07f);
        Pipe("Pipe_Broken", a3, new Vector3(9.0f, -0.6f, 7.8f), new Vector3(10.5f, -2.6f, 8.6f), 0.06f);
        Pipe("Pipe_GenFeed", a3, new Vector3(4.3f, -2.3f, 0.3f), new Vector3(0.3f, -0.6f, 0.3f), 0.05f);

        // Flooded south-east corner (visual only).
        var flood = Box("Flood_Water", a3, new Vector3(13.2f, y + 0.03f, 1.9f), new Vector3(3.6f, 0.02f, 3.8f), water, Vector3.zero, false);
        flood.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // Clutter.
        Box("Shelf_North", a3, new Vector3(7.5f, y + 0.9f, 9.7f), new Vector3(3.0f, 1.8f, 0.45f), wood);
        Box("Shelf_Toppled", a3, new Vector3(8.0f, y + 0.25f, 3.6f), new Vector3(2.4f, 0.5f, 1.6f), wood, new Vector3(0, 12, 0));
        Box("Crate_A", a3, new Vector3(12.4f, y + 0.35f, 8.8f), new Vector3(0.7f, 0.7f, 0.7f), wood, new Vector3(0, 20, 0));
        Box("Crate_B", a3, new Vector3(13.3f, y + 0.3f, 9.1f), new Vector3(0.6f, 0.6f, 0.6f), wood, new Vector3(0, -15, 0));
        Box("Crate_C", a3, new Vector3(12.8f, y + 0.95f, 8.9f), new Vector3(0.5f, 0.5f, 0.5f), wood, new Vector3(8, 40, 6));
        Box("Boiler", a3, new Vector3(5.2f, y + 1.0f, 5.6f), new Vector3(1.0f, 2.0f, 1.0f), rust);
        Box("Basement_DoorOffHinges", a3, new Vector3(11.4f, y + 1.0f, 5.3f), new Vector3(1.2f, 2.1f, 0.06f), wood, new Vector3(-12, 70, 0));
        Scatter(a3, 14, y, new Rect(2.0f, 5.3f, 12.6f, 3.0f));
        Scatter(a3, 14, y, new Rect(0.5f, 0.3f, 10.5f, 4.4f));

        // Objectives.
        Box("Objective_Generator", a3, new Vector3(3.5f, y + 0.6f, 0.8f), new Vector3(1.6f, 1.2f, 0.9f), rust);
        Box("Exit_Gate", a3, new Vector3(0, y + 1.2f, 2.2f), new Vector3(0.1f, 2.4f, 2.0f), rust);
        Slab("ExitYard", a3, -4, 0, 0.7f, 3.7f, y, concrete);
    }

    // ---------------------------------------------------------------- Lighting + render settings
    static void BuildLighting(Transform p)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.025f, 0.025f, 0.03f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.12f;
        RenderSettings.fogColor = new Color(0.015f, 0.017f, 0.02f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

        var moon = NewLight("Moonlight", p, LightType.Directional, new Color(0.55f, 0.62f, 0.8f), 0.08f, Vector3.zero);
        moon.transform.rotation = Quaternion.Euler(28, 70, 0); // slants in through the west windows
        RenderSettings.sun = moon;

        var bulb = NewLight("Kitchen_DyingBulb", p, LightType.Point, new Color(1f, 0.72f, 0.45f), 1.2f, new Vector3(3.5f, 2.5f, 6f));
        bulb.range = 6;
        var stair = NewLight("Stairwell_Faint", p, LightType.Point, new Color(0.6f, 0.65f, 0.8f), 0.4f, new Vector3(12f, 4.5f, 3.5f));
        stair.range = 5;
        var gen = NewLight("Basement_WorkLamp", p, LightType.Point, new Color(1f, 0.35f, 0.2f), 1.5f, new Vector3(4.5f, -1.0f, 2.0f));
        gen.range = 7;
    }

    static void BuildMarkers(Transform p)
    {
        Marker("PlayerSpawn", p, new Vector3(2.8f, 3f, 8.0f));
        Marker("ZombieSpawn_01", p, new Vector3(12.5f, 3f, 7.0f));  // upstairs side room
        Marker("ZombieSpawn_02", p, new Vector3(0.8f, 0f, 1.2f));   // hallway dead end
        Marker("ZombieSpawn_03", p, new Vector3(14.0f, 0f, 3.5f));  // stair foot / front door
        Marker("ZombieSpawn_04", p, new Vector3(6.2f, 0f, 3.2f));   // kitchen doorway
        Marker("ZombieSpawn_05", p, new Vector3(9.0f, -3f, 7.0f));  // basement north run
        Marker("ZombieSpawn_06", p, new Vector3(13.8f, -3f, 8.6f)); // basement east end
        Marker("ZombieSpawn_07", p, new Vector3(13.0f, -3f, 1.8f)); // flooded corner
        Marker("ZombieSpawn_08", p, new Vector3(9.5f, -3f, 2.5f));  // generator approach

        // Placeholder player with flashlight, for previewing only. Faces the jammed door.
        var player = new GameObject("Player");
        player.transform.position = new Vector3(2.8f, 3f, 8.0f);
        player.transform.rotation = Quaternion.LookRotation(new Vector3(3.3f - 2.8f, 0, 5f - 8.0f));
        var camGo = new GameObject("Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.05f;
        camGo.AddComponent<AudioListener>();
        var flash = NewLight("Flashlight", camGo.transform, LightType.Spot, new Color(1f, 0.95f, 0.85f), 4f, new Vector3(0.2f, -0.15f, 0));
        flash.range = 18;
        flash.spotAngle = 50;
        flash.innerSpotAngle = 25;
    }

    // ---------------------------------------------------------------- Helpers
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

    static void MakeMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "House");
        wood = Mat("DarkWood", new Color(0.16f, 0.11f, 0.075f), 0.15f, 0f);
        plaster = Mat("GrimyPlaster", new Color(0.30f, 0.29f, 0.25f), 0.05f, 0f);
        concrete = Mat("Concrete", new Color(0.20f, 0.20f, 0.19f), 0.1f, 0f);
        rust = Mat("Rust", new Color(0.26f, 0.12f, 0.06f), 0.3f, 0.5f);
        water = Mat("FloodWater", new Color(0.015f, 0.02f, 0.02f), 0.95f, 0f);
        cloth = Mat("StainedCloth", new Color(0.36f, 0.33f, 0.27f), 0.0f, 0f);
    }

    // Reuses the existing asset if present so re-runs keep GUIDs stable.
    static Material Mat(string name, Color c, float smooth, float metal)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(m);
        return m;
    }

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
        Shot(player.GetComponentInChildren<Camera>(), Path.Combine(dir, "fp_bedroom.png"));
        player.transform.SetPositionAndRotation(new Vector3(6.0f, 0f, 1.3f), Quaternion.LookRotation(new Vector3(-0.2f, 0, 1)));
        Shot(player.GetComponentInChildren<Camera>(), Path.Combine(dir, "fp_kitchen_door.png"));
        player.transform.SetPositionAndRotation(new Vector3(12.5f, -3f, 3.5f), Quaternion.LookRotation(new Vector3(-1, 0, -0.3f)));
        Shot(player.GetComponentInChildren<Camera>(), Path.Combine(dir, "fp_basement.png"));

        // Top-down cutaways: no fog, flat bright ambient, everything above the cut hidden.
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
        var top = new GameObject("ShotCam").AddComponent<Camera>();
        top.orthographic = true;
        top.orthographicSize = 6.5f;
        top.clearFlags = CameraClearFlags.SolidColor;
        top.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        top.transform.SetPositionAndRotation(new Vector3(7.5f, 20f, 5f), Quaternion.Euler(90, 0, 0));
        var renderers = Object.FindObjectsByType<Renderer>();
        foreach (var (label, cut) in new[] { ("upstairs", 5.7f), ("ground", 2.7f), ("basement", -0.3f) })
        {
            foreach (var r in renderers) r.enabled = r.bounds.center.y < cut;
            Shot(top, Path.Combine(dir, $"overview_{label}.png"));
        }
        Debug.Log("[HouseBlockout] Screenshots written to " + dir);
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
