// BLINK > Place Puzzle: Flooded Basement (docs/puzzles/FLOODED_BASEMENT.md). Re-running deletes and rebuilds its own
// group. The room is found by its floor collider and its doorway by its Door object, so the puzzle follows the house if
// it's rebuilt. Everything sits in the room's own frame (x across from the doorway wall's corner, z along it), laid out
// for the bungalow's B2 workshop around the props already there.
//
// The door (D5) is the house's own Door (BLINK > Bungalow > Setup Doors first): the room slams and locks it itself.
using System.Collections.Generic;
using Blink.Interaction;
using Blink.Puzzles;
using Blink.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.EditorTools
{
    public static partial class PuzzleProps
    {
        /// <summary>Room: its floor collider is "Floor_&lt;FloodRoom&gt;", its props group "&lt;FloodRoom&gt;".</summary>
        const string FloodRoom = "B2";
        /// <summary>The doorway's Door object (its position is the doorway).</summary>
        const string FloodDoor = "D5";
        const string FloodGroup = "Puzzle_FloodedBasement";
        const string FloodKeyId = "key_workshop";
        const string FloodKeyName = "Workshop key";
        const string FloodClue = "Drain blocked AGAIN. The cover's rusted solid, you'll need a bar to shift it.\n\n— Groundsman";
        const string FloodMats = "Assets/Art/Puzzles/FloodedBasement/Materials/";
        const string FloodTex = "Assets/Art/Puzzles/FloodedBasement/Textures/";
        const string WaterSource = "Assets/Art/Environment/Bungalow/Materials/M_Water.mat";
        const string ParticleSource = "Assets/Art/VFX/BulletTracer/BulletSmoke.mat";
        const string KeyPrefab = "Assets/Prefabs/Props/KeyPickup.prefab";
        const float DrawerH = 0.26f;

        [MenuItem("BLINK/Place Puzzle: Flooded Basement")]
        public static void PlaceFloodedBasement()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PuzzleProps] Leave Play mode first.");
                return;
            }
            var old = GameObject.Find(FloodGroup);
            if (old != null) Undo.DestroyObjectImmediate(old);
            Physics.SyncTransforms();

            if (!FindFloodRoom(out var interior)) return;
            if (!FindFloodDoorway(interior, out var doorCentre, out var inward))
            {
                Debug.LogWarning($"[PuzzleProps] No doorway found into '{FloodRoom}'; nothing placed.");
                return;
            }
            float floor = interior.min.y;
            // The room frame: origin on the floor at the corner left of the doorway (looking in), x into the room, z along
            // the doorway wall (to the right). B2: origin (13.575, 14.705), x = east, z = south... see Frame().
            var frame = new Frame(interior, doorCentre, inward);

            var root = new GameObject(FloodGroup) { layer = 2 }; // Ignore Raycast: its trigger box mustn't catch rays
            Undo.RegisterCreatedObjectUndo(root, "Place Flooded Basement");
            root.transform.position = new Vector3(interior.center.x, floor, interior.center.z);
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, interior.size.y * 0.5f, 0f);
            box.size = new Vector3(interior.size.x, interior.size.y, interior.size.z);

            var doorway = new GameObject("Doorway").transform;
            doorway.SetParent(root.transform, false);
            doorway.SetPositionAndRotation(doorCentre, Quaternion.LookRotation(inward, Vector3.up));

            var mats = new FloodMaterials();
            var room = root.AddComponent<FloodedBasement>();
            room.roomBounds = box;
            room.doorway = doorway;
            room.door = GameObject.Find(FloodDoor) is { } doorGo ? doorGo.GetComponent<Blink.World.Door>() : null;
            if (room.door == null)
                Debug.LogWarning($"[PuzzleProps] No Door on '{FloodDoor}': the room won't slam its door. Run BLINK > Bungalow > Setup Doors, then this again.");

            // The drain in the middle, the clue on a stake beside it.
            Vector3 drainPos = new Vector3(interior.center.x, floor, interior.center.z);
            var (drain, basket) = BuildDrain(root.transform, drainPos, frame, mats);
            room.drain = drain;
            room.rewardKey = BuildRewardKey(basket);
            PlaceDrainClue(root.transform, drainPos, doorCentre, mats);

            // The bath against the wall facing the doorway, tap running into it, spilling over the rim.
            var bath = BuildBath(root.transform, frame, mats, out var tapMouth, out var running, out var drips);
            room.tap = tapMouth;
            room.runningWater = running;
            room.drips = drips;

            // Six places to look.
            var spots = new List<Openable>();
            var anchors = new List<Transform>();
            BuildChestOfDrawers(root.transform, frame, mats, spots, anchors);
            BuildTallCupboard(root.transform, frame, mats, spots, anchors);
            BuildToolLocker(root.transform, frame, mats, spots, anchors);
            BuildCrate(root.transform, frame, mats, spots, anchors);
            room.searchSpots = spots.ToArray();
            room.searchAnchors = anchors.ToArray();
            room.crowbar = BuildCrowbar(anchors[0], mats);

            room.water = BuildWaterPlane(root.transform, interior, mats);
            var (whirl, foam) = BuildWhirlpool(root.transform, drainPos, mats);
            room.whirlpool = whirl;
            room.whirlpoolFoam = foam;
            room.bulb = BuildBulb(root.transform, frame, interior, mats);
            room.floaters = FindFloaters(interior);

            var so = new SerializedObject(room);
            so.FindProperty("dripCue").objectReferenceValue = Cue("Flood_Drip", Clips + "Flood_Drip.wav", spatial: 1f, volume: 0.55f, maxDistance: 12f);
            so.FindProperty("tapRunCue").objectReferenceValue = Cue("Flood_TapRun", Clips + "Flood_TapRun.wav", spatial: 1f, volume: 0.7f, maxDistance: 16f);
            so.FindProperty("tapStopCue").objectReferenceValue = Cue("Flood_TapStop", Clips + "Flood_TapStop.wav", spatial: 1f, volume: 0.8f, maxDistance: 16f);
            so.FindProperty("surgeCue").objectReferenceValue = Cue("Flood_Surge", Clips + "Flood_Surge.wav", spatial: 0.6f, volume: 1f, maxDistance: 16f);
            so.FindProperty("gurgleCue").objectReferenceValue = Cue("Flood_Gurgle", Clips + "Flood_Gurgle.wav", spatial: 1f, volume: 0.9f, maxDistance: 20f);
            so.FindProperty("coverClangCue").objectReferenceValue = Cue("Flood_CoverClang", Clips + "Flood_CoverClang.wav", spatial: 1f, volume: 1f, maxDistance: 25f);
            so.FindProperty("drownCue").objectReferenceValue = Cue("Flood_Drown", Clips + "Flood_Drown.wav", spatial: 0f, volume: 1f, maxDistance: 30f);
            so.FindProperty("doorSlamCue").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Blink.Audio.SoundCue>("Assets/Art/Audio/Cues/Door_Burst.asset");
            so.ApplyModifiedPropertiesWithoutUndo();

            CheckFloodClearances(root.transform, interior);
            WarnAboutZombies(interior, "the flooded basement", "Zombies are out of scope in there and can't get in once the door shuts");

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[PuzzleProps] Flooded basement placed in '{FloodRoom}': doorway at ({doorCentre.x:0.00}, {doorCentre.z:0.00}) facing " +
                      $"({inward.x:0}, {inward.z:0}), drain at ({drainPos.x:0.00}, {drainPos.z:0.00}), {Vector3.Distance(Flat(doorCentre), Flat(drainPos)):0.0} m in; " +
                      $"bath at ({bath.position.x:0.00}, {bath.position.z:0.00}); {spots.Count} search spots; ceiling {interior.size.y:0.00} m. " +
                      "Door: " + (room.door != null ? room.door.doorId : "none") + ".");
        }

        // ------------------------------------------------------------------ the room

        /// <summary>The room's frame: x = into the room (away from the doorway wall), z = along that wall, to the right looking in.</summary>
        readonly struct Frame
        {
            readonly Vector3 _origin, _x, _z;
            public readonly float Depth, Width, DoorZ;

            public Frame(Bounds interior, Vector3 doorCentre, Vector3 inward)
            {
                _x = inward;
                _z = Vector3.Cross(Vector3.up, inward); // right, looking in
                float halfDepth = Mathf.Abs(Vector3.Dot(interior.extents, _x));
                float halfWidth = Mathf.Abs(Vector3.Dot(interior.extents, _z));
                Vector3 c = new Vector3(interior.center.x, interior.min.y, interior.center.z);
                _origin = c - _x * halfDepth - _z * halfWidth;
                Depth = 2f * halfDepth;
                Width = 2f * halfWidth;
                DoorZ = Vector3.Dot(doorCentre - _origin, _z);
            }

            /// <summary>World point at <paramref name="x"/> into the room, <paramref name="z"/> along the doorway wall, <paramref name="y"/> up.</summary>
            public Vector3 P(float x, float z, float y = 0f) => _origin + _x * x + _z * z + Vector3.up * y;

            /// <summary>A rotation whose forward (the front of a cabinet) points the given way in the room frame.</summary>
            public Quaternion Facing(float x, float z) => Quaternion.LookRotation(_x * x + _z * z, Vector3.up);
        }

        /// <summary>The interior box from the room's floor collider ("Floor_B2"): wall to wall, floor to the first thing overhead.</summary>
        static bool FindFloodRoom(out Bounds interior)
        {
            interior = default;
            var floorObj = GameObject.Find("Floor_" + FloodRoom);
            if (floorObj == null || floorObj.GetComponent<Collider>() is not { } floorCol)
            {
                Debug.LogWarning($"[PuzzleProps] No 'Floor_{FloodRoom}' collider in the scene; nothing placed. Open House_Bungalow.");
                return false;
            }
            var b = floorCol.bounds;
            float floor = b.max.y;
            var centre = new Vector3(b.center.x, floor + 0.1f, b.center.z);
            float ceiling = Physics.Raycast(centre, Vector3.up, out var up, 6f, Probe, QueryTriggerInteraction.Ignore) ? up.point.y : floor + 2.6f;
            interior = new Bounds(new Vector3(b.center.x, (floor + ceiling) * 0.5f, b.center.z), new Vector3(b.size.x, ceiling - floor, b.size.z));
            return true;
        }

        /// <summary>The doorway from the Door object's position, snapped onto the nearest wall of the room.</summary>
        static bool FindFloodDoorway(Bounds interior, out Vector3 centre, out Vector3 inward)
        {
            centre = default;
            inward = default;
            var door = GameObject.Find(FloodDoor);
            if (door == null)
            {
                if (!FindDoorway(interior, out centre, out var outward, out _)) return false;
                inward = -outward;
                return true;
            }
            Vector3 p = door.transform.position;
            float best = float.MaxValue;
            foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                float wall = Vector3.Dot(interior.center, dir) + Mathf.Abs(Vector3.Dot(interior.extents, dir));
                float gap = Mathf.Abs(Vector3.Dot(p, dir) - wall);
                if (gap >= best) continue;
                best = gap;
                inward = -dir;
                Vector3 onWall = p + dir * (wall - Vector3.Dot(p, dir));
                centre = new Vector3(onWall.x, interior.min.y, onWall.z);
            }
            return best < 1f;
        }

        /// <summary>
        /// Anything already floating in the room's props group (the bungalow's "B2_Float"). Batching-static is cleared on
        /// them, or the batched mesh would stay put while the water lifts the transform.
        /// </summary>
        static Transform[] FindFloaters(Bounds interior)
        {
            var list = new List<Transform>();
            if (GameObject.Find(FloodRoom) is { } group)
                foreach (Transform t in group.transform)
                {
                    if (!t.name.Contains("Float") || !interior.Contains(t.position + Vector3.up * 0.05f)) continue;
                    foreach (var r in t.GetComponentsInChildren<Transform>(true))
                    {
                        var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                        if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                        Undo.RecordObject(r.gameObject, "Let the flood lift " + t.name);
                        GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                    }
                    list.Add(t);
                }
            return list.ToArray();
        }

        static void CheckFloodClearances(Transform root, Bounds interior)
        {
            var mine = new HashSet<Collider>(root.GetComponentsInChildren<Collider>(true));
            foreach (var spot in root.GetComponentsInChildren<Openable>(true))
            {
                var body = spot.transform.parent;
                var rs = body.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                b.Expand(-0.04f);
                foreach (var c in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, Probe, QueryTriggerInteraction.Ignore))
                    if (!mine.Contains(c) && !c.name.StartsWith("Floor") && !c.name.StartsWith("Wall"))
                        Debug.LogWarning($"[PuzzleProps] Flooded basement: '{body.name}' overlaps '{c.name}'. Move one of them.", body);
            }
        }

        // ------------------------------------------------------------------ materials

        sealed class FloodMaterials
        {
            public readonly Material Water, Stream, Whirl, Drop, Wood, DarkWood, Porcelain, Brass, Iron, Rust, Locker, Hole,
                Card, Bulb, Cord, Rag, Glass, Tin;

            public FloodMaterials()
            {
                Water = WaterMat("M_FloodWater", new Color(0.15f, 0.17f, 0.1f, 0.9f), new Vector2(3f, 5f), null);
                Stream = WaterMat("M_FloodStream", new Color(0.5f, 0.55f, 0.45f, 0.55f), new Vector2(1f, 4f), null);
                Whirl = WaterMat("M_FloodWhirlpool", new Color(0.62f, 0.6f, 0.5f, 0.85f), Vector2.one, WhirlpoolTexture());
                Drop = DropMat();
                Wood = MatIn(FloodMats, "Wood", new Color(0.3f, 0.22f, 0.15f), 0.25f);
                DarkWood = MatIn(FloodMats, "DarkWood", new Color(0.17f, 0.12f, 0.08f), 0.2f);
                Porcelain = MatIn(FloodMats, "Porcelain", new Color(0.82f, 0.8f, 0.74f), 0.75f);
                Brass = MatIn(FloodMats, "Brass", new Color(0.6f, 0.45f, 0.22f), 0.6f, 0.7f);
                Iron = MatIn(FloodMats, "Iron", new Color(0.13f, 0.13f, 0.13f), 0.35f, 0.6f);
                Rust = MatIn(FloodMats, "Rust", new Color(0.33f, 0.17f, 0.09f), 0.15f, 0.3f);
                Locker = MatIn(FloodMats, "LockerGreen", new Color(0.2f, 0.27f, 0.21f), 0.35f, 0.4f);
                Hole = MatIn(FloodMats, "DrainHole", new Color(0.01f, 0.01f, 0.01f), 0.1f);
                Card = MatIn(FloodMats, "Card", new Color(0.82f, 0.78f, 0.66f), 0.1f);
                Bulb = MatIn(FloodMats, "Bulb", new Color(1f, 0.92f, 0.75f), 0.8f, 0f, new Color(2.2f, 1.6f, 0.9f));
                Cord = MatIn(FloodMats, "Cord", new Color(0.05f, 0.05f, 0.05f), 0.3f);
                Rag = MatIn(FloodMats, "Rag", new Color(0.4f, 0.37f, 0.3f), 0.05f);
                Glass = MatIn(FloodMats, "BottleGlass", new Color(0.12f, 0.2f, 0.12f), 0.85f);
                Tin = MatIn(FloodMats, "Tin", new Color(0.45f, 0.3f, 0.2f), 0.3f, 0.6f);
            }

            /// <summary>A copy of the bungalow's standing-water material (transparent URP Lit, ripple normal), two-sided.</summary>
            static Material WaterMat(string name, Color color, Vector2 tiling, Texture2D baseMap)
            {
                string path = FloodMats + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null) return m;
                System.IO.Directory.CreateDirectory(FloodMats);
                var src = AssetDatabase.LoadAssetAtPath<Material>(WaterSource);
                m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.name = name;
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Cull", (float)CullMode.Off); // the eyes end up just under it
                m.SetTextureScale("_BaseMap", tiling);
                if (baseMap != null) m.SetTexture("_BaseMap", baseMap);
                AssetDatabase.CreateAsset(m, path);
                return m;
            }

            /// <summary>Droplets and foam: the bullet smoke's particle material with a soft round dot.</summary>
            static Material DropMat()
            {
                string path = FloodMats + "M_FloodDrop.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null) return m;
                var src = AssetDatabase.LoadAssetAtPath<Material>(ParticleSource);
                m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                m.name = "M_FloodDrop";
                m.SetTexture("_BaseMap", DotTexture());
                m.SetColor("_BaseColor", new Color(0.78f, 0.82f, 0.74f, 0.85f));
                AssetDatabase.CreateAsset(m, path);
                return m;
            }

            static Texture2D DotTexture() => SaveTexture("T_FloodDrop", 32, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r));
            });

            /// <summary>Foamy spiral arms on clear water, darker in the eye.</summary>
            static Texture2D WhirlpoolTexture() => SaveTexture("T_Whirlpool", 256, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                if (r > 1f) return new Color(1f, 1f, 1f, 0f);
                float a = Mathf.Atan2(v, u);
                float arms = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(4f * a + 9f * Mathf.Sqrt(r)), 3f);
                float edge = Mathf.SmoothStep(1f, 0.6f, r);
                float eye = Mathf.SmoothStep(0f, 0.25f, r);
                float shade = Mathf.Lerp(0.15f, 1f, eye);
                return new Color(shade, shade, shade * 0.92f, Mathf.Clamp01(arms * edge * 0.9f + (1f - eye) * 0.9f));
            });

            /// <summary>Bakes a square texture from <paramref name="f"/>(u, v) with u, v in −1..1 and saves it as a PNG asset.</summary>
            static Texture2D SaveTexture(string name, int n, System.Func<float, float, Color> f)
            {
                string path = FloodTex + name + ".png";
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (existing != null) return existing;
                System.IO.Directory.CreateDirectory(FloodTex);
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = f((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                tex.SetPixels(px);
                tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter imp)
                {
                    imp.alphaIsTransparency = true;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.mipmapEnabled = true;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        // ------------------------------------------------------------------ the drain

        static (DrainCover cover, Transform basket) BuildDrain(Transform root, Vector3 at, Frame frame, FloodMaterials m)
        {
            var drain = new GameObject("Drain").transform;
            drain.SetParent(root, false);
            drain.SetPositionAndRotation(at, frame.Facing(1f, 0f));
            Part(drain, PrimitiveType.Cylinder, "Hole", new Vector3(0f, 0.002f, 0f), Vector3.zero, new Vector3(0.46f, 0.002f, 0.46f), m.Hole);
            Part(drain, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.004f, 0f), Vector3.zero, new Vector3(0.6f, 0.004f, 0.6f), m.Rust)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var basket = new GameObject("Basket").transform; // the key's snagged in here
            basket.SetParent(drain, false);
            basket.localPosition = new Vector3(0.05f, 0.006f, -0.04f);

            var cover = new GameObject("DrainCover");
            cover.transform.SetParent(drain, false);
            cover.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            Part(cover.transform, PrimitiveType.Cylinder, "Plate", Vector3.zero, Vector3.zero, new Vector3(0.52f, 0.012f, 0.52f), m.Rust);
            for (int i = -2; i <= 2; i++)
                Part(cover.transform, PrimitiveType.Cube, "Slot", new Vector3(i * 0.075f, 0.012f, 0f), Vector3.zero,
                    new Vector3(0.03f, 0.004f, 0.32f - Mathf.Abs(i) * 0.06f), m.Hole);
            Part(cover.transform, PrimitiveType.Cube, "Bar", new Vector3(0f, 0.016f, 0.2f), new Vector3(0f, 20f, 0f), new Vector3(0.18f, 0.012f, 0.03f), m.Iron);
            var hit = cover.AddComponent<BoxCollider>(); // generous: you're looking down at it through murky water
            hit.center = new Vector3(0f, 0.02f, 0f);
            hit.size = new Vector3(0.62f, 0.06f, 0.62f);
            var dc = cover.AddComponent<DrainCover>();
            dc.toolItemId = FloodedBasement.CrowbarId;
            return (dc, basket);
        }

        static Pickup BuildRewardKey(Transform basket)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPrefab);
            GameObject k;
            if (prefab != null) k = (GameObject)PrefabUtility.InstantiatePrefab(prefab, basket.gameObject.scene);
            else
            {
                k = new GameObject("Key");
                Part(k.transform, PrimitiveType.Cube, "Shaft", Vector3.zero, Vector3.zero, new Vector3(0.08f, 0.008f, 0.01f),
                    MatIn(FloodMats, "Brass", Color.yellow, 0.6f));
                var c = k.AddComponent<BoxCollider>();
                c.isTrigger = true;
                c.size = new Vector3(0.15f, 0.06f, 0.1f);
                k.AddComponent<Pickup>();
            }
            k.name = "Key_Workshop";
            k.transform.SetParent(basket, false);
            k.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 35f, 0f));
            var renderers = k.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0) // rest it in the basket
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                k.transform.position += Vector3.up * (basket.position.y - b.min.y + 0.001f);
            }
            var pickup = k.GetComponent<Pickup>();
            pickup.itemId = FloodKeyId;
            pickup.displayName = FloodKeyName;
            // Held up like the other keys (BungalowDoorSetup.PlaceKeys): face on to the view, tipped to the right.
            pickup.holdEuler = (Quaternion.AngleAxis(25f, Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f)).eulerAngles;
            pickup.holdScale = 0.8f;
            if (prefab != null) PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
            return pickup;
        }

        /// <summary>The groundsman's card on a stake beside the drain, on the far side from the door, facing it.</summary>
        static void PlaceDrainClue(Transform root, Vector3 drain, Vector3 doorCentre, FloodMaterials m)
        {
            Vector3 toDoor = Flat(doorCentre - drain).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toDoor);
            Vector3 at = drain - toDoor * 0.45f + side * 0.45f;
            var stake = new GameObject("ClueStake").transform;
            stake.SetParent(root, false);
            stake.SetPositionAndRotation(at, Quaternion.LookRotation(toDoor, Vector3.up));
            Part(stake, PrimitiveType.Cube, "Stake", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0f, 2f), new Vector3(0.05f, 1f, 0.05f), m.DarkWood, keepCollider: true);
            var card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            card.name = "ClueCard";
            card.transform.SetParent(stake, false);
            card.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.9f, 0.03f), Quaternion.Euler(0f, 180f, -5f));
            card.transform.localScale = new Vector3(0.2f, 0.13f, 0.004f);
            card.GetComponent<Renderer>().sharedMaterial = m.Card;
            card.AddComponent<Readable>().Setup("Read", "Card", FloodClue);
            Part(card.transform, PrimitiveType.Sphere, "Nail", new Vector3(0f, 0.38f, -0.6f), Vector3.zero, new Vector3(0.06f, 0.09f, 1.5f), m.Iron)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ the bath

        /// <summary>
        /// A clawfoot bath (HouseBlockout.DBathtub's shape) along the wall facing the doorway, brim full, with a big tap on
        /// the wall over it. Returns the bath; outs: the tap's mouth, the running water (stream, spill over the rim and
        /// their splashes) and the drip emitter.
        /// </summary>
        static Transform BuildBath(Transform root, Frame frame, FloodMaterials m, out Transform mouth, out GameObject running, out ParticleSystem drips)
        {
            const float Length = 1.7f, Width = 0.76f, Rim = 0.71f;
            float z = Mathf.Clamp(frame.DoorZ, Length * 0.5f + 0.4f, frame.Width - Length * 0.5f - 0.1f);
            var g = new GameObject("Bath").transform;
            g.SetParent(root, false);
            // Local x along the wall, local +z out into the room (towards the doorway).
            g.SetPositionAndRotation(frame.P(frame.Depth - Width * 0.5f - 0.03f, z), frame.Facing(-1f, 0f));
            Part(g, PrimitiveType.Cube, "Tub", new Vector3(0f, 0.42f, 0f), Vector3.zero, new Vector3(Length, 0.56f, Width), m.Porcelain, keepCollider: true);
            Part(g, PrimitiveType.Cube, "Water", new Vector3(0f, Rim - 0.012f, 0f), Vector3.zero, new Vector3(Length - 0.14f, 0.02f, Width - 0.14f), m.Water);
            Part(g, PrimitiveType.Cube, "RimF", new Vector3(0f, Rim, Width * 0.5f - 0.03f), Vector3.zero, new Vector3(Length, 0.04f, 0.06f), m.Porcelain);
            Part(g, PrimitiveType.Cube, "RimB", new Vector3(0f, Rim, -Width * 0.5f + 0.03f), Vector3.zero, new Vector3(Length, 0.04f, 0.06f), m.Porcelain);
            Part(g, PrimitiveType.Cube, "RimL", new Vector3(-Length * 0.5f + 0.03f, Rim, 0f), Vector3.zero, new Vector3(0.06f, 0.04f, Width), m.Porcelain);
            Part(g, PrimitiveType.Cube, "RimR", new Vector3(Length * 0.5f - 0.03f, Rim, 0f), Vector3.zero, new Vector3(0.06f, 0.04f, Width), m.Porcelain);
            for (int i = 0; i < 4; i++)
                Part(g, PrimitiveType.Sphere, "Foot", new Vector3(i % 2 == 0 ? -0.7f : 0.7f, 0.07f, i < 2 ? -0.28f : 0.28f), Vector3.zero, Vector3.one * 0.14f, m.Brass);

            // The tap: a big brass spout off the wall, turned down over the bath, a wheel handle above it.
            float wallZ = -Width * 0.5f - 0.03f;
            var tap = new GameObject("Tap").transform;
            tap.SetParent(g, false);
            tap.localPosition = new Vector3(0f, 1.0f, wallZ);
            Part(tap, PrimitiveType.Cylinder, "WallPlate", new Vector3(0f, 0f, 0.01f), new Vector3(90f, 0f, 0f), new Vector3(0.12f, 0.01f, 0.12f), m.Brass);
            Part(tap, PrimitiveType.Cylinder, "Spout", new Vector3(0f, 0f, 0.13f), new Vector3(90f, 0f, 0f), new Vector3(0.06f, 0.12f, 0.06f), m.Brass);
            Part(tap, PrimitiveType.Cylinder, "Nozzle", new Vector3(0f, -0.05f, 0.24f), Vector3.zero, new Vector3(0.06f, 0.06f, 0.06f), m.Brass);
            Part(tap, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.08f, 0.1f), Vector3.zero, new Vector3(0.025f, 0.05f, 0.025f), m.Brass);
            Part(tap, PrimitiveType.Cylinder, "Wheel", new Vector3(0f, 0.13f, 0.1f), Vector3.zero, new Vector3(0.12f, 0.008f, 0.12f), m.Iron);
            mouth = new GameObject("Mouth").transform;
            mouth.SetParent(tap, false);
            mouth.localPosition = new Vector3(0f, -0.11f, 0.24f);

            running = new GameObject("RunningWater");
            running.transform.SetParent(g, false);
            float drop = 1.0f - 0.11f - (Rim - 0.012f);
            Part(running.transform, PrimitiveType.Cylinder, "Stream", new Vector3(0f, Rim + drop * 0.5f, wallZ + 0.24f), Vector3.zero,
                new Vector3(0.035f, drop * 0.5f, 0.035f), m.Stream).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            // Spilling over the front rim onto the floor: a sheet down the outside of the tub.
            var sheet = Part(running.transform, PrimitiveType.Quad, "Spill", new Vector3(0f, Rim * 0.5f, Width * 0.5f + 0.015f), new Vector3(0f, 180f, 0f),
                new Vector3(Length - 0.3f, Rim, 1f), m.Stream);
            sheet.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Splash(running.transform, "TapSplash", new Vector3(0f, Rim, wallZ + 0.24f), new Vector3(0.05f, 0.01f, 0.05f), 70f, m.Drop);
            Splash(running.transform, "SpillSplash", new Vector3(0f, 0.02f, Width * 0.5f + 0.08f), new Vector3(Length - 0.3f, 0.01f, 0.05f), 90f, m.Drop);
            running.SetActive(false);

            drips = NewParticles(mouth, "Drips", m.Drop);
            var main = drips.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = Mathf.Sqrt(2f * 0.18f / 9.81f);
            main.startSpeed = 0f;
            main.startSize = 0.025f;
            main.gravityModifier = 1f;
            var em = drips.emission;
            em.rateOverTime = 0f;
            var shape = drips.shape;
            shape.enabled = false;
            return g;
        }

        static void Splash(Transform parent, string name, Vector3 local, Vector3 area, float rate, Material mat)
        {
            var ps = NewParticles(parent, name, mat);
            ps.transform.localPosition = local;
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.gravityModifier = 1f;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = area;
            shape.randomDirectionAmount = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // up
        }

        static ParticleSystem NewParticles(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 300;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------------ the search spots

        const string CommodePrefab = "Assets/Art/Environment/Bungalow/Props/Prefabs/GothicCommode_01.prefab";

        /// <summary>
        /// The bungalow's own chest of drawers (GothicCommode_01, drawers set up by BungalowPropsPlacer.SetupDrawers) against the
        /// far side wall, near the door. Each drawer gets a crowbar spot on its inside floor, toward the front, so a drawer
        /// pulled open shows it clear of the carcass collider. Falls back to a primitive chest if the prefab is missing.
        /// </summary>
        static void BuildChestOfDrawers(Transform root, Frame frame, FloodMaterials m, List<Openable> spots, List<Transform> anchors)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommodePrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[PuzzleProps] {CommodePrefab} not found; building a primitive chest of drawers instead.");
                BuildPrimitiveDrawers(root, frame, m, spots, anchors);
                return;
            }
            var chest = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            chest.name = "ChestOfDrawers";
            float depth = chest.GetComponent<BoxCollider>() is { } body ? body.size.z : 0.58f;
            chest.transform.SetPositionAndRotation(frame.P(2.4f, depth * 0.5f + 0.02f), frame.Facing(0f, 1f));
            const float insideFloor = -0.085f; // drawer meshes are pivoted at their centre; measured inside floor
            int n = 0;
            foreach (Transform dr in chest.transform)
            {
                var o = dr.GetComponent<Openable>();
                if (o == null) continue;
                spots.Add(o);
                anchors.Add(Anchor(dr, new Vector3(0f, insideFloor, 0.10f), 0f));
                if (n++ == 1) Junk(dr, new Vector3(-0.3f, insideFloor, -0.1f), m, rag: true);
            }
        }

        /// <summary>Three primitive drawers (HouseBlockout.Drawers' shape): the fallback for <see cref="BuildChestOfDrawers"/>.</summary>
        static void BuildPrimitiveDrawers(Transform root, Frame frame, FloodMaterials m, List<Openable> spots, List<Transform> anchors)
        {
            const float w = 0.9f, d = 0.5f;
            float h = 0.08f + DrawerH * 3f + 0.04f;
            var g = new GameObject("ChestOfDrawers").transform;
            g.SetParent(root, false);
            g.SetPositionAndRotation(frame.P(2.4f, d * 0.5f + 0.02f), frame.Facing(0f, 1f));
            Part(g, PrimitiveType.Cube, "Body", new Vector3(0f, h * 0.5f, -0.015f), Vector3.zero, new Vector3(w, h, d - 0.03f), m.Wood, keepCollider: true);
            for (int i = 0; i < 3; i++)
            {
                var dr = new GameObject("Drawer_" + i).transform;
                dr.SetParent(g, false);
                dr.localPosition = new Vector3(0f, 0.08f + DrawerH * (i + 0.5f), 0f);
                Part(dr, PrimitiveType.Cube, "Front", new Vector3(0f, 0f, d * 0.5f - 0.015f), Vector3.zero, new Vector3(w - 0.06f, DrawerH - 0.02f, 0.03f), m.DarkWood, keepCollider: true);
                Part(dr, PrimitiveType.Cube, "Knob", new Vector3(0f, 0f, d * 0.5f + 0.01f), Vector3.zero, new Vector3(0.1f, 0.025f, 0.025f), m.Brass);
                Part(dr, PrimitiveType.Cube, "Bottom", new Vector3(0f, -DrawerH * 0.5f + 0.04f, 0f), Vector3.zero, new Vector3(w - 0.1f, 0.015f, d - 0.08f), m.Wood, keepCollider: true);
                Part(dr, PrimitiveType.Cube, "SideL", new Vector3(-w * 0.5f + 0.06f, -0.03f, 0f), Vector3.zero, new Vector3(0.012f, DrawerH - 0.1f, d - 0.08f), m.Wood);
                Part(dr, PrimitiveType.Cube, "SideR", new Vector3(w * 0.5f - 0.06f, -0.03f, 0f), Vector3.zero, new Vector3(0.012f, DrawerH - 0.1f, d - 0.08f), m.Wood);
                var o = dr.gameObject.AddComponent<Openable>();
                o.motion = Openable.Motion.Sliding;
                o.noun = "drawer";
                o.slide = new Vector3(0f, 0f, d * 0.7f);
                spots.Add(o);
                anchors.Add(Anchor(dr, new Vector3(0f, -DrawerH * 0.5f + 0.06f, 0.06f), 0f));
                if (i == 1) Junk(dr, new Vector3(-0.25f, -DrawerH * 0.5f + 0.05f, -0.1f), m, rag: true);
            }
        }

        /// <summary>A tall single-door cupboard (HouseBlockout.Cabinet's carcass) beside the drawers. The crowbar would lie on its lower shelf.</summary>
        static void BuildTallCupboard(Transform root, Frame frame, FloodMaterials m, List<Openable> spots, List<Transform> anchors)
        {
            const float w = 0.7f, d = 0.5f, h = 1.9f;
            var g = Carcass(root, "TallCupboard", frame.P(4.0f, d * 0.5f + 0.02f), frame.Facing(0f, 1f), w, d, h, m.Wood, new[] { 0.62f, 1.25f });
            spots.Add(Door(g, "Door", w, d, h, m.DarkWood, m.Brass, "cupboard"));
            anchors.Add(Anchor(g, new Vector3(0f, 0.62f + 0.03f, 0.02f), 0f));
            Junk(g, new Vector3(0.18f, 1.25f + 0.02f, 0f), m, bottle: true);
            Junk(g, new Vector3(-0.2f, 0.1f, 0.02f), m, tin: true);
        }

        /// <summary>A green metal tool locker on the doorway wall, with a shelf.</summary>
        static void BuildToolLocker(Transform root, Frame frame, FloodMaterials m, List<Openable> spots, List<Transform> anchors)
        {
            const float w = 0.5f, d = 0.5f, h = 1.8f;
            float z = Mathf.Max(0.6f, frame.DoorZ - 2.3f);
            if (frame.DoorZ < frame.Width * 0.5f) z = frame.DoorZ + 2.3f;
            var g = Carcass(root, "ToolLocker", frame.P(d * 0.5f + 0.02f, z), frame.Facing(1f, 0f), w, d, h, m.Locker, new[] { 1.05f });
            var door = Door(g, "Door", w, d, h, m.Locker, m.Iron, "locker");
            for (int i = 0; i < 4; i++) // vents
                Part(door.transform, PrimitiveType.Cube, "Vent", new Vector3(w * 0.5f, h - 0.25f - i * 0.05f, 0.017f), Vector3.zero, new Vector3(w * 0.5f, 0.012f, 0.004f), m.Hole);
            spots.Add(door);
            anchors.Add(Anchor(g, new Vector3(0f, 1.05f + 0.03f, 0f), 90f));
            Junk(g, new Vector3(0.05f, 0.1f, 0f), m, rag: true);
        }

        /// <summary>A wooden crate with a hinged lid, low on the doorway wall: the first place to go under.</summary>
        static void BuildCrate(Transform root, Frame frame, FloodMaterials m, List<Openable> spots, List<Transform> anchors)
        {
            const float w = 0.8f, d = 0.5f, h = 0.5f;
            float z = Mathf.Max(0.6f, frame.DoorZ - 6.3f);
            if (frame.DoorZ < frame.Width * 0.5f) z = Mathf.Min(frame.Width - 0.6f, frame.DoorZ + 6.3f);
            var g = new GameObject("Crate").transform;
            g.SetParent(root, false);
            g.SetPositionAndRotation(frame.P(d * 0.5f + 0.02f, z), frame.Facing(1f, 0f));
            Part(g, PrimitiveType.Cube, "Base", new Vector3(0f, 0.03f, 0f), Vector3.zero, new Vector3(w, 0.06f, d), m.Wood, keepCollider: true);
            Part(g, PrimitiveType.Cube, "Front", new Vector3(0f, h * 0.5f, d * 0.5f - 0.015f), Vector3.zero, new Vector3(w, h, 0.03f), m.Wood, keepCollider: true);
            Part(g, PrimitiveType.Cube, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f + 0.015f), Vector3.zero, new Vector3(w, h, 0.03f), m.Wood, keepCollider: true);
            Part(g, PrimitiveType.Cube, "SideL", new Vector3(-w * 0.5f + 0.015f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.03f, h, d), m.Wood, keepCollider: true);
            Part(g, PrimitiveType.Cube, "SideR", new Vector3(w * 0.5f - 0.015f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.03f, h, d), m.Wood, keepCollider: true);
            foreach (float y in new[] { 0.12f, 0.38f })
                Part(g, PrimitiveType.Cube, "Slat", new Vector3(0f, y, d * 0.5f + 0.005f), Vector3.zero, new Vector3(w + 0.01f, 0.06f, 0.012f), m.DarkWood);

            // Openable turns about its own local up, so the hinge pivot's up is laid along the crate's width (local x).
            var hinge = new GameObject("Lid").transform;
            hinge.SetParent(g, false);
            hinge.SetLocalPositionAndRotation(new Vector3(0f, h, -d * 0.5f), Quaternion.Euler(0f, 0f, -90f));
            var lid = Part(hinge, PrimitiveType.Cube, "Boards", Vector3.zero, Vector3.zero, Vector3.one, m.Wood, keepCollider: true);
            // Placed in the crate's own axes (same rotation as the crate), so its scale reads as the crate's width/height/depth.
            lid.transform.SetPositionAndRotation(g.TransformPoint(new Vector3(0f, h + 0.015f, 0f)), g.rotation);
            lid.transform.localScale = new Vector3(w + 0.02f, 0.03f, d + 0.02f);
            var o = hinge.gameObject.AddComponent<Openable>();
            o.noun = "crate";
            o.openAngle = -85f; // swings up and back against the wall
            spots.Add(o);
            anchors.Add(Anchor(g, new Vector3(0f, 0.07f, 0.05f), 0f));
            Junk(g, new Vector3(0.25f, 0.07f, -0.1f), m, bottle: true);
        }

        /// <summary>A hollow carcass: back, sides, top, base and shelves. Local +z is the front, origin on the floor.</summary>
        static Transform Carcass(Transform root, string name, Vector3 pos, Quaternion facing, float w, float d, float h, Material mat, float[] shelves)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root, false);
            g.SetPositionAndRotation(pos, facing);
            Part(g, PrimitiveType.Cube, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f + 0.015f), Vector3.zero, new Vector3(w, h, 0.03f), mat, keepCollider: true);
            Part(g, PrimitiveType.Cube, "SideL", new Vector3(-w * 0.5f + 0.015f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.03f, h, d), mat, keepCollider: true);
            Part(g, PrimitiveType.Cube, "SideR", new Vector3(w * 0.5f - 0.015f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.03f, h, d), mat, keepCollider: true);
            Part(g, PrimitiveType.Cube, "Top", new Vector3(0f, h - 0.015f, 0f), Vector3.zero, new Vector3(w, 0.03f, d), mat, keepCollider: true);
            Part(g, PrimitiveType.Cube, "Base", new Vector3(0f, 0.04f, 0f), Vector3.zero, new Vector3(w, 0.08f, d), mat, keepCollider: true);
            foreach (float y in shelves)
                Part(g, PrimitiveType.Cube, "Shelf", new Vector3(0f, y, 0f), Vector3.zero, new Vector3(w - 0.06f, 0.025f, d - 0.06f), mat, keepCollider: true);
            return g;
        }

        /// <summary>One door across the carcass front, hinged on its left (looking at it), swinging out.</summary>
        static Openable Door(Transform carcass, string name, float w, float d, float h, Material panel, Material handle, string noun)
        {
            var hinge = new GameObject(name).transform;
            hinge.SetParent(carcass, false);
            hinge.localPosition = new Vector3(-w * 0.5f, 0f, d * 0.5f + 0.015f);
            Part(hinge, PrimitiveType.Cube, "Panel", new Vector3(w * 0.5f, h * 0.5f, 0f), Vector3.zero, new Vector3(w - 0.01f, h - 0.02f, 0.03f), panel, keepCollider: true);
            Part(hinge, PrimitiveType.Cube, "Handle", new Vector3(w - 0.07f, h * 0.5f, 0.025f), Vector3.zero, new Vector3(0.02f, 0.14f, 0.02f), handle);
            var o = hinge.gameObject.AddComponent<Openable>();
            o.noun = noun;
            o.openAngle = -100f;
            return o;
        }

        static Transform Anchor(Transform parent, Vector3 local, float yaw)
        {
            var a = new GameObject("CrowbarSpot").transform;
            a.SetParent(parent, false);
            a.SetLocalPositionAndRotation(local, Quaternion.Euler(0f, yaw, 0f));
            return a;
        }

        /// <summary>Flavour: a rag, a bottle or a rusty tin. No colliders, so they never hide the crowbar from the look ray.</summary>
        static void Junk(Transform parent, Vector3 local, FloodMaterials m, bool rag = false, bool bottle = false, bool tin = false)
        {
            if (rag) Part(parent, PrimitiveType.Cube, "Rag", local + Vector3.up * 0.012f, new Vector3(0f, 25f, 4f), new Vector3(0.2f, 0.025f, 0.14f), m.Rag);
            if (bottle)
            {
                Part(parent, PrimitiveType.Cylinder, "Bottle", local + Vector3.up * 0.1f, Vector3.zero, new Vector3(0.07f, 0.1f, 0.07f), m.Glass);
                Part(parent, PrimitiveType.Cylinder, "Neck", local + Vector3.up * 0.23f, Vector3.zero, new Vector3(0.025f, 0.035f, 0.025f), m.Glass);
            }
            if (tin) Part(parent, PrimitiveType.Cylinder, "Tin", local + Vector3.up * 0.05f, Vector3.zero, new Vector3(0.09f, 0.05f, 0.09f), m.Tin);
        }

        /// <summary>A short jemmy (0.45 m): a bar with a bent claw at one end and a flat chisel at the other.</summary>
        static Pickup BuildCrowbar(Transform anchor, FloodMaterials m)
        {
            var bar = new GameObject("Crowbar");
            bar.transform.SetParent(anchor, false);
            Part(bar.transform, PrimitiveType.Cube, "Shaft", new Vector3(0f, 0.011f, 0f), Vector3.zero, new Vector3(0.4f, 0.022f, 0.022f), m.Rust);
            Part(bar.transform, PrimitiveType.Cube, "Claw", new Vector3(-0.215f, 0.025f, 0f), new Vector3(0f, 0f, -40f), new Vector3(0.07f, 0.02f, 0.03f), m.Rust);
            Part(bar.transform, PrimitiveType.Cube, "Chisel", new Vector3(0.215f, 0.011f, 0f), Vector3.zero, new Vector3(0.04f, 0.01f, 0.034f), m.Iron);
            var hit = bar.AddComponent<BoxCollider>(); // generous: it's what you're hunting for
            hit.center = new Vector3(0f, 0.03f, 0f);
            hit.size = new Vector3(0.5f, 0.07f, 0.1f);
            var pickup = bar.AddComponent<Pickup>();
            pickup.itemId = FloodedBasement.CrowbarId;
            pickup.displayName = "Crowbar";
            pickup.holdEuler = new Vector3(0f, -70f, -35f);
            pickup.holdScale = 0.75f;
            bar.SetActive(false); // FloodedBasement puts it in a random spot when the flood starts
            return pickup;
        }

        // ------------------------------------------------------------------ water, whirlpool, light

        static Transform BuildWaterPlane(Transform root, Bounds interior, FloodMaterials m)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Quad);
            w.name = "FloodWater";
            Object.DestroyImmediate(w.GetComponent<Collider>()); // no collider: the look ray passes through
            w.transform.SetParent(root, false);
            w.transform.SetPositionAndRotation(new Vector3(interior.center.x, interior.min.y, interior.center.z), Quaternion.Euler(90f, 0f, 0f));
            w.transform.localScale = new Vector3(interior.size.x - 0.01f, interior.size.z - 0.01f, 1f);
            var r = w.GetComponent<Renderer>();
            r.sharedMaterial = m.Water;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(w, (StaticEditorFlags)0);
            w.SetActive(false); // shown once there's water
            return w.transform;
        }

        static (Transform whirl, ParticleSystem foam) BuildWhirlpool(Transform root, Vector3 drain, FloodMaterials m)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Quad);
            w.name = "Whirlpool";
            Object.DestroyImmediate(w.GetComponent<Collider>());
            w.transform.SetParent(root, false);
            w.transform.SetPositionAndRotation(drain + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
            w.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            var r = w.GetComponent<Renderer>();
            r.sharedMaterial = m.Whirl;
            r.shadowCastingMode = ShadowCastingMode.Off;
            w.SetActive(false);

            var foam = NewParticles(root, "WhirlpoolFoam", m.Drop);
            foam.transform.SetPositionAndRotation(drain + Vector3.up * 0.02f, Quaternion.identity);
            var main = foam.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            var em = foam.emission;
            em.rateOverTime = 45f;
            var shape = foam.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.7f;
            shape.rotation = new Vector3(90f, 0f, 0f); // flat on the water
            var vel = foam.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalX = vel.orbitalZ = 0f;
            vel.orbitalY = 3.5f;
            vel.radial = -0.35f;
            vel.x = vel.y = vel.z = 0f;
            return (w.transform, foam);
        }

        /// <summary>A bare bulb on a flex over the room, off to one side so the drain's lit but not under it.</summary>
        static Light BuildBulb(Transform root, Frame frame, Bounds interior, FloodMaterials m)
        {
            float ceiling = interior.max.y;
            Vector3 top = frame.P(frame.Depth * 0.42f, frame.Width * 0.58f, ceiling - interior.min.y);
            var g = new GameObject("BareBulb").transform;
            g.SetParent(root, false);
            g.position = top;
            const float flex = 0.45f;
            Part(g, PrimitiveType.Cylinder, "Flex", new Vector3(0f, -flex * 0.5f, 0f), Vector3.zero, new Vector3(0.008f, flex * 0.5f, 0.008f), m.Cord);
            Part(g, PrimitiveType.Cylinder, "Holder", new Vector3(0f, -flex - 0.02f, 0f), Vector3.zero, new Vector3(0.035f, 0.025f, 0.035f), m.Iron);
            Part(g, PrimitiveType.Sphere, "Glass", new Vector3(0f, -flex - 0.08f, 0f), Vector3.zero, new Vector3(0.065f, 0.08f, 0.065f), m.Bulb)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(g, false);
            light.transform.localPosition = new Vector3(0f, -flex - 0.14f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.52f);
            light.intensity = 1.6f;
            light.range = 7.5f;
            light.shadows = LightShadows.Soft;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            var flicker = light.gameObject.AddComponent<LightFlicker>();
            flicker.amount = 0.06f;
            flicker.speed = 9f;
            return light;
        }
    }
}
