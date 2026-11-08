// Puzzle rooms. Menu: BLINK > Place Puzzle: Lily's Doll (docs/puzzles/LILYS_DOLL_ROOM.md) and
// BLINK > Place Puzzle: Flooded Basement (docs/puzzles/FLOODED_BASEMENT.md, in PuzzleProps.FloodedBasement.cs).
// Re-running deletes and rebuilds the puzzle's own group. The room is found by name, then raycasts find its real floor,
// walls and doorway, so the puzzle follows the house if it's rebuilt, and moving it to another room is changing Room.
using Blink.Audio;
using Blink.Interaction;
using Blink.Puzzles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.EditorTools
{
    public static partial class PuzzleProps
    {
        // ------------------------------------------------------------------ Lily's doll

        /// <summary>The room it goes in: found as "Volume_&lt;Room&gt;" (a box over the interior) or a "&lt;Room&gt;" props group.</summary>
        const string Room = "Bathroom";
        const string DollGroup = "Puzzle_LilysDoll";
        const string KeyItemId = "key_dorm";
        const string KeyName = "Lily's key";
        const string ClueText = "Lily's doll hates to be ignored. Keep your eyes on her, and don't you dare blink.";
        const string Art = "Assets/Art/Puzzles/LilysDoll/";
        const string Mats = Art + "Materials/";
        const string Cues = "Assets/Art/Audio/Cues/";
        const string Clips = "Assets/Art/Audio/World/";
        /// <summary>Seat height of painted_wooden_chair_01 (bungalow_prop_models.json "top") and of the fallback chair.</summary>
        const float SeatHeight = 0.45f;
        const float ChairHalfDepth = 0.27f;
        const int Probe = ~((1 << 2) | (1 << 9)); // not Ignore Raycast, not the Player

        [MenuItem("BLINK/Place Puzzle: Lily's Doll")]
        public static void PlaceLilysDoll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PuzzleProps] Leave Play mode first.");
                return;
            }
            var old = GameObject.Find(DollGroup);
            if (old != null) Undo.DestroyObjectImmediate(old);
            Physics.SyncTransforms();

            if (!FindRoom(Room, out var interior)) return;
            if (!FindDoorway(interior, out var doorCentre, out var outward, out float doorWidth))
            {
                Debug.LogWarning($"[PuzzleProps] No doorway found in '{Room}'; nothing placed.");
                return;
            }
            float floor = interior.min.y;

            // The far wall, straight across from the doorway (probed high, over the furniture).
            Vector3 probeFrom = new Vector3(doorCentre.x, floor + 2.3f, doorCentre.z) - outward * 0.3f;
            if (!Physics.Raycast(probeFrom, -outward, out var far, 30f, Probe, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning($"[PuzzleProps] No far wall across from the doorway in '{Room}'; nothing placed.");
                return;
            }
            Vector3 chairPos = new Vector3(far.point.x, floor, far.point.z) + outward * (ChairHalfDepth + 0.03f);
            var facing = Quaternion.LookRotation(outward, Vector3.up); // towards the doorway

            // Root: the room-sized trigger, the rules.
            var root = new GameObject(DollGroup) { layer = 2 }; // Ignore Raycast: its trigger box mustn't catch rays
            Undo.RegisterCreatedObjectUndo(root, "Place Lily's Doll");
            root.transform.position = new Vector3(interior.center.x, floor, interior.center.z);
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, interior.size.y * 0.5f - 0.1f, 0f);
            box.size = new Vector3(interior.size.x, interior.size.y + 0.2f, interior.size.z);

            var chair = PlaceChair(root.transform, chairPos, facing);
            var (doll, head, face, lap) = BuildDoll(root.transform);
            doll.SetPositionAndRotation(chairPos + Vector3.up * SeatHeight - outward * 0.1f, facing);
            var key = BuildKey(lap);

            var room = root.AddComponent<LilysDollRoom>();
            room.doll = doll;
            room.head = head;
            room.face = face;
            room.lap = lap;
            room.key = key;
            room.keyItemId = KeyItemId;
            room.roomBounds = box;
            var so = new SerializedObject(room);
            so.FindProperty("musicCue").objectReferenceValue = Cue("Doll_MusicBox", Clips + "Doll_MusicBox.wav", spatial: 1f, volume: 0.5f, maxDistance: 9f);
            so.FindProperty("giggleCue").objectReferenceValue = Cue("Doll_Giggle", null, spatial: 1f, volume: 1f, maxDistance: 12f);
            so.FindProperty("screamCue").objectReferenceValue = Cue("Doll_Scream", null, spatial: 0f, volume: 1f, maxDistance: 30f, copyClipsFrom: "Grab_Sting");
            so.FindProperty("scratchCue").objectReferenceValue = Cue("Doll_Scratch", Clips + "Doll_Scratch.wav", spatial: 0f, volume: 0.8f, maxDistance: 30f);
            so.ApplyModifiedPropertiesWithoutUndo();

            // A dim warm light in front of her, so she reads from the doorway. The room's own light is left as it is.
            var lamp = new GameObject("DollLight").AddComponent<Light>();
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.position = doll.position + outward * 0.9f + Vector3.up * 1.2f;
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.74f, 0.5f);
            lamp.intensity = 1.6f;
            lamp.range = 3f;
            lamp.shadows = LightShadows.Soft;
            lamp.lightmapBakeType = LightmapBakeType.Realtime;

            PlaceClueCard(root.transform, doorCentre, outward, doorWidth);
            PlaceBlocks(root.transform, interior, doorCentre, outward, far.point);
            WarnAboutZombies(interior, "Lily's doll's room", "A zombie making you turn is an unfair death");

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[PuzzleProps] Lily's doll placed in '{Room}': doorway at ({doorCentre.x:0.00}, {doorCentre.z:0.00}), " +
                      $"doll on {(chair != null ? chair.name : "a chair")} at ({chairPos.x:0.00}, {chairPos.z:0.00}), " +
                      $"{Vector3.Distance(Flat(doorCentre), Flat(chairPos)):0.0} m from the door.");
        }

        // ------------------------------------------------------------------ finding the room

        /// <summary>The room's interior box: floor to ceiling, wall to wall, found by raycasting from its centre.</summary>
        static bool FindRoom(string room, out Bounds interior)
        {
            interior = default;
            Vector3 centre;
            var volume = GameObject.Find("Volume_" + room);
            var group = GameObject.Find(room);
            if (volume != null && volume.GetComponent<Collider>() is { } col) centre = col.bounds.center;
            else if (group != null && group.GetComponentsInChildren<Renderer>() is { Length: > 0 } rs)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                centre = b.center;
            }
            else
            {
                Debug.LogWarning($"[PuzzleProps] No 'Volume_{room}' or '{room}' in the scene; nothing placed.");
                return false;
            }

            if (!Physics.Raycast(centre + Vector3.up * 0.5f, Vector3.down, out var floorHit, 5f, Probe, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning($"[PuzzleProps] No floor under '{room}'; nothing placed.");
                return false;
            }
            float floor = floorHit.point.y;
            float ceiling = Physics.Raycast(floorHit.point + Vector3.up * 0.1f, Vector3.up, out var up, 6f, Probe, QueryTriggerInteraction.Ignore)
                ? up.point.y : floor + 3f;
            Vector3 high = new Vector3(centre.x, floor + Mathf.Min(2.3f, ceiling - floor - 0.2f), centre.z);
            float Wall(Vector3 dir) => Physics.Raycast(high, dir, out var h, 30f, Probe, QueryTriggerInteraction.Ignore)
                ? Vector3.Dot(h.point - high, dir) : 2f;
            float west = Wall(Vector3.left), east = Wall(Vector3.right), south = Wall(Vector3.back), north = Wall(Vector3.forward);
            var min = new Vector3(high.x - west, floor, high.z - south);
            var max = new Vector3(high.x + east, ceiling, high.z + north);
            interior = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        /// <summary>
        /// The widest opening in the four walls: rays at knee and head height (under the door head) both get through the
        /// wall there. Two heights so a window (open high only) or furniture against a wall (rays start inside it) don't count.
        /// </summary>
        static bool FindDoorway(Bounds interior, out Vector3 centre, out Vector3 outward, out float width)
        {
            centre = default;
            outward = default;
            width = 0f;
            float floor = interior.min.y;
            foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                var side = Vector3.Cross(Vector3.up, dir);
                float half = Mathf.Abs(Vector3.Dot(interior.extents, side));
                float toWall = Mathf.Abs(Vector3.Dot(interior.extents, dir));
                float runStart = 0f, run = 0f;
                for (float s = -half + 0.05f; s <= half - 0.05f + 1e-4f; s += 0.05f)
                {
                    Vector3 basePos = interior.center + dir * (toWall - 0.3f) + side * s;
                    bool open = true;
                    foreach (float y in new[] { 1.0f, 2.0f })
                    {
                        var p = new Vector3(basePos.x, floor + y, basePos.z);
                        if (BlockedIgnoringDoors(p, dir, 0.9f)) open = false;
                    }
                    if (open)
                    {
                        if (run == 0f) runStart = s;
                        run += 0.05f;
                        if (run > width && run >= 0.7f)
                        {
                            width = run;
                            outward = dir;
                            var mid = interior.center + dir * toWall + side * (runStart + s) * 0.5f;
                            centre = new Vector3(mid.x, floor, mid.z);
                        }
                    }
                    else run = 0f;
                }
            }
            return width > 0f;
        }

        /// <summary>A ray blocked by anything but a door leaf: a closed door still leaves its doorway an opening.</summary>
        static bool BlockedIgnoringDoors(Vector3 from, Vector3 dir, float distance)
        {
            foreach (var hit in Physics.RaycastAll(from, dir, distance, Probe, QueryTriggerInteraction.Ignore))
                if (!IsDoor(hit.collider)) return true;
            return false;
        }

        /// <summary>Matched by component name (Door, DoorLeaf...) so this editor script needs no door types.</summary>
        static bool IsDoor(Collider c)
        {
            for (var t = c.transform; t != null; t = t.parent)
                foreach (var m in t.GetComponents<MonoBehaviour>())
                    if (m != null && m.GetType().Name.StartsWith("Door")) return true;
            return false;
        }

        // ------------------------------------------------------------------ the chair and the doll

        /// <summary>
        /// Moves the room's own chair (a child of the '&lt;Room&gt;' group named *Chair*) against the far wall, facing the
        /// doorway, or builds a plain one. Its collider is cut down to the seat so it can't hide her face or the key.
        /// </summary>
        static Transform PlaceChair(Transform root, Vector3 pos, Quaternion facing)
        {
            Transform chair = null;
            if (GameObject.Find(Room) is { } group)
                foreach (var t in group.GetComponentsInChildren<Transform>())
                    if (t != group.transform && t.name.Contains("Chair")) { chair = t; break; }

            if (chair != null)
            {
                Undo.RecordObject(chair, "Move chair for Lily's doll");
                chair.SetPositionAndRotation(pos, facing);
                foreach (var col in chair.GetComponents<BoxCollider>())
                {
                    Undo.RecordObject(col, "Trim chair collider");
                    col.center = new Vector3(col.center.x, SeatHeight * 0.5f, col.center.z);
                    col.size = new Vector3(col.size.x, SeatHeight, col.size.z);
                }
                return chair;
            }

            var built = new GameObject("DollChair").transform;
            built.SetParent(root, false);
            built.SetPositionAndRotation(pos, facing);
            var wood = Mat("ChairWood", new Color(0.32f, 0.22f, 0.14f), 0.3f);
            Part(built, PrimitiveType.Cube, "Seat", new Vector3(0f, SeatHeight - 0.02f, 0f), Vector3.zero, new Vector3(0.42f, 0.04f, 0.42f), wood, keepCollider: true);
            Part(built, PrimitiveType.Cube, "Back", new Vector3(0f, 0.7f, -0.19f), Vector3.zero, new Vector3(0.42f, 0.5f, 0.03f), wood);
            foreach (var (x, z) in new[] { (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) })
                Part(built, PrimitiveType.Cube, "Leg", new Vector3(x, (SeatHeight - 0.04f) * 0.5f, z), Vector3.zero, new Vector3(0.035f, SeatHeight - 0.04f, 0.035f), wood);
            return built;
        }

        /// <summary>
        /// The modelled porcelain doll (tools/blender/puzzle_doll.py), sitting with her legs out (+Z forward, origin on the
        /// seat). No colliders: the key is the only thing to hit. The FBX holds Head (the neck pivot), HeadMesh and Face
        /// as siblings; they are put under Head here so the head turns as one piece with its tilt baked into the mesh.
        /// </summary>
        static (Transform doll, Transform head, Transform face, Transform lap) BuildDoll(Transform root)
        {
            var doll = Spawn("LilysDoll.fbx").transform;
            doll.name = "Doll";
            doll.SetParent(root, false);
            var head = doll.Find("Head") ?? throw new System.InvalidOperationException("LilysDoll.fbx has no Head.");
            var face = doll.Find("Face") ?? throw new System.InvalidOperationException("LilysDoll.fbx has no Face.");
            var lap = doll.Find("Lap") ?? throw new System.InvalidOperationException("LilysDoll.fbx has no Lap.");
            var headMesh = doll.Find("HeadMesh") ?? throw new System.InvalidOperationException("LilysDoll.fbx has no HeadMesh.");
            headMesh.SetParent(head, true);
            face.SetParent(head, true);
            return (doll, head, face, lap);
        }

        static Pickup BuildKey(Transform lap)
        {
            var key = new GameObject("Key_Dorm");
            key.transform.SetParent(lap, false);
            key.transform.localPosition = new Vector3(0f, 0.008f, 0f);
            key.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            var model = Spawn("LilysKey.fbx").transform;
            model.name = "Model";
            model.SetParent(key.transform, false);
            var hit = key.AddComponent<BoxCollider>(); // generous: it's small and it's what you aim at
            hit.center = Vector3.zero;
            hit.size = new Vector3(0.17f, 0.07f, 0.12f);
            var pickup = key.AddComponent<Pickup>();
            pickup.itemId = KeyItemId;
            pickup.displayName = KeyName;
            return pickup;
        }

        /// <summary>An unlinked copy of a model from the doll's art folder, wearing the doll material.</summary>
        static GameObject Spawn(string file)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + file)
                         ?? throw new System.InvalidOperationException($"Missing {Art + file}; run tools/blender/puzzle_doll.py.");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Mats + "LilysDoll.mat")
                      ?? throw new System.InvalidOperationException($"Missing {Mats}LilysDoll.mat.");
            var go = Object.Instantiate(prefab);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------ dressing

        /// <summary>The clue card on the corridor wall beside the doorway, so it's read before going in.</summary>
        static void PlaceClueCard(Transform root, Vector3 doorCentre, Vector3 outward, float doorWidth)
        {
            var side = Vector3.Cross(Vector3.up, outward);
            // Either side of the doorway, on bare wall: nothing standing within a hand's width of the card.
            RaycastHit hit = default;
            bool found = false;
            foreach (bool needClear in new[] { true, false })
            foreach (float sign in new[] { 1f, -1f })
            {
                if (found) break;
                Vector3 corridor = doorCentre + outward * 0.7f + side * sign * (doorWidth * 0.5f + 0.35f) + Vector3.up * 1.5f;
                if (!Physics.Raycast(corridor, -outward, out hit, 0.8f, Probe, QueryTriggerInteraction.Ignore)) continue;
                if (Vector3.Dot(hit.normal, outward) < 0.9f) continue; // a wall facing into the corridor
                var clearance = new Vector3(0.2f, 0.3f, 0.12f);
                found = !needClear || !Physics.CheckBox(hit.point + hit.normal * 0.14f, clearance,
                    Quaternion.LookRotation(hit.normal), Probe, QueryTriggerInteraction.Ignore);
            }
            if (found)
            {
                var card = GameObject.CreatePrimitive(PrimitiveType.Cube);
                card.name = "ClueCard";
                card.transform.SetParent(root, false);
                card.layer = 0;
                card.transform.SetPositionAndRotation(hit.point + hit.normal * 0.003f,
                    Quaternion.LookRotation(-hit.normal, Vector3.up) * Quaternion.Euler(0f, 0f, 4f));
                card.transform.localScale = new Vector3(0.2f, 0.13f, 0.004f);
                card.GetComponent<Renderer>().sharedMaterial = Mat("Card", new Color(0.86f, 0.82f, 0.7f), 0.1f);
                card.AddComponent<Readable>().Setup("Read", "Card", ClueText);
                var pin = Part(card.transform, PrimitiveType.Sphere, "Pin", new Vector3(0f, 0.38f, -0.6f), Vector3.zero,
                    new Vector3(0.08f, 0.12f, 1.5f), Mat("Mouth", Color.red, 0.7f));
                pin.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                return;
            }
            Debug.LogWarning("[PuzzleProps] No corridor wall beside the doorway for the clue card; skipped.");
        }

        /// <summary>A couple of toy blocks by the side walls, well clear of the line from the door to the doll.</summary>
        static void PlaceBlocks(Transform root, Bounds interior, Vector3 doorCentre, Vector3 outward, Vector3 farWall)
        {
            var side = Vector3.Cross(Vector3.up, outward);
            var colours = new[] { new Color(0.55f, 0.16f, 0.12f), new Color(0.18f, 0.3f, 0.5f), new Color(0.7f, 0.58f, 0.2f) };
            float halfSide = Mathf.Abs(Vector3.Dot(interior.extents, side));
            float depth = Vector3.Dot(doorCentre - farWall, outward);
            float farAlong = Vector3.Dot(farWall - interior.center, outward);
            var spots = new[] { (0.4f, 1f, 15f), (0.47f, 1f, 50f), (0.3f, -1f, -20f) }; // (fraction from the far wall, side, yaw)
            for (int i = 0; i < spots.Length; i++)
            {
                var (along, s, yaw) = spots[i];
                Vector3 p = interior.center + outward * (farAlong + depth * along) + side * s * (halfSide - 0.35f);
                p = new Vector3(p.x, interior.max.y - 0.2f, p.z);
                if (!Physics.Raycast(p, Vector3.down, out var hit, interior.size.y, Probe, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y > interior.min.y + 0.05f) continue; // something's there; floor only
                var block = Part(root, PrimitiveType.Cube, "ToyBlock", Vector3.zero, Vector3.zero, Vector3.one * 0.06f,
                    Mat("Block" + i, colours[i], 0.3f));
                block.transform.SetPositionAndRotation(hit.point + Vector3.up * 0.03f, Quaternion.Euler(0f, yaw, 0f));
            }
        }

        static void WarnAboutZombies(Bounds interior, string room, string why)
        {
            var near = interior;
            near.Expand(new Vector3(3f, 0f, 3f));
            foreach (var t in Object.FindObjectsByType<Transform>())
            {
                bool zombie = t.GetComponent<Blink.Mobs.MobController>() != null || t.name.StartsWith("Spawn_Z");
                if (zombie && near.Contains(t.position))
                    Debug.LogWarning($"[PuzzleProps] '{t.name}' is in or next to {room}. {why}; move it.", t);
            }
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Loads or creates a URP Lit material asset in Lily's doll's folder, so the scene keeps its reference.</summary>
        static Material Mat(string name, Color color, float smoothness, float metallic = 0f) => MatIn(Mats, name, color, smoothness, metallic);

        /// <summary>Loads or creates a URP Lit material asset in <paramref name="folder"/> (settings only written on creation).</summary>
        static Material MatIn(string folder, string name, Color color, float smoothness, float metallic = 0f, Color? emission = null)
        {
            string path = folder + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            System.IO.Directory.CreateDirectory(folder);
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission is { } e)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", e);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>
        /// Loads the cue, or creates it (an empty cue is a silent placeholder). Settings are only written on creation, so
        /// tuning a cue in the Inspector survives a re-run.
        /// </summary>
        static SoundCue Cue(string name, string clipPath, float spatial, float volume, float maxDistance, string copyClipsFrom = null)
        {
            string path = Cues + name + ".asset";
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (cue != null) return cue;
            cue = ScriptableObject.CreateInstance<SoundCue>();
            AssetDatabase.CreateAsset(cue, path);
            var so = new SerializedObject(cue);
            var clips = so.FindProperty("clips");
            if (copyClipsFrom != null && AssetDatabase.LoadAssetAtPath<SoundCue>(Cues + copyClipsFrom + ".asset") is { } src)
            {
                clips.arraySize = src.Clips.Length;
                for (int i = 0; i < src.Clips.Length; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = src.Clips[i];
                so.FindProperty("output").objectReferenceValue = src.Output;
            }
            else if (clipPath != null && AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath) is { } clip)
            {
                clips.arraySize = 1;
                clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            }
            if (so.FindProperty("output").objectReferenceValue == null
                && AssetDatabase.LoadAssetAtPath<SoundCue>(Cues + "Door_ForcingLoop.asset") is { } world)
                so.FindProperty("output").objectReferenceValue = world.Output;
            so.FindProperty("volume").vector2Value = new Vector2(volume, volume);
            so.FindProperty("pitch").vector2Value = Vector2.one;
            so.FindProperty("spatialBlend").floatValue = spatial;
            so.FindProperty("minDistance").floatValue = 1.5f;
            so.FindProperty("maxDistance").floatValue = maxDistance;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(cue);
            return cue;
        }
    }
}
