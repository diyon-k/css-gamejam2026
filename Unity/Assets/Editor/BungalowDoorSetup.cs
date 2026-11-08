using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Blink.Interaction;
using Blink.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// BLINK/Bungalow/Setup Doors: turns the shell's door leaves (found by name, "D1_LeafL", "D2_Leaf"...) into
    /// <see cref="Door"/>s in the open scene. Idempotent. Hinges, swing sides and the open angle come from the leaf's
    /// collider box (Kit/Bungalow_Shell_colliders.json) and the layout's openings; locked / open / keys come from the
    /// layout's "doors" table (tools/blender/bungalow_layout.py DOOR_STATE).
    ///
    /// Per leaf: reset to the modelled (imported) pose; non-static (no lightmap, Contribute GI off, light probes);
    /// kinematic Rigidbody; a <see cref="DoorLeaf"/> that forwards the look-and-press; left out of the navmesh bake, with a
    /// carving NavMeshObstacle on its collider that the Door switches on while locked (cuts the doorway) or open and still
    /// (cuts the open leaf). The Door components sit under Bungalow/Doors, each at its doorway.
    /// The placeholder front-door keys are a separate menu item (BLINK/Bungalow/Place Front Door Keys).
    /// </summary>
    public static class BungalowDoorSetup
    {
        const string ArtRoot = "Assets/Art/Environment/Bungalow/";
        const string ShellCollidersPath = ArtRoot + "Kit/Bungalow_Shell_colliders.json";
        const string LayoutPath = ArtRoot + "bungalow_layout.json";
        const string ScenePath = "Assets/Scenes/House_Bungalow.unity";
        const string KeyPrefabPath = "Assets/Prefabs/Props/KeyPickup.prefab"; // Key.fbx + Pickup (also the bedroom key)
        const int NotPlayer = ~(1 << 9); // the Player capsule's layer
        static readonly Regex LeafName = new Regex(@"^(D\d+)_Leaf[A-Z]?$");

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Box { public float[] c; public float[] s; public float[] r; }
        [Serializable] class Obj { public string name; public Box[] boxes; }
        [Serializable] class ColliderFile { public Obj[] objects; }
        [Serializable] class Opening { public string id; public string wall; public float[] span; }
        [Serializable] class DoorRow { public string id; public bool locked; public int keys; public string key; public string[] also; public bool open; }
        [Serializable] class Layout { public Opening[] openings; public DoorRow[] doors; public string keyItem; }
#pragma warning restore 0649

        [MenuItem("BLINK/Bungalow/Setup Doors")]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Setup Doors: leave Play mode first."); return; }
            Debug.Log(Setup(SceneManager.GetActiveScene(), true));
        }

        [MenuItem("BLINK/Bungalow/Place Front Door Keys (placeholder)")]
        static void KeysMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Leave Play mode first."); return; }
            Debug.Log(PlaceKeys(SceneManager.GetActiveScene(), true));
        }

        /// <summary>
        /// Sets up every door in <paramref name="scene"/>. Optionally saves it. Returns a report.
        /// A keyed door is only locked if the scene holds enough <see cref="Pickup"/>s of its key (else nobody could open
        /// it): place the keys first (Place Front Door Keys, Set Up Start Bedroom), or run this again after.
        /// </summary>
        public static string Setup(Scene scene, bool save)
        {
            var cols = JsonUtility.FromJson<ColliderFile>(File.ReadAllText(ShellCollidersPath));
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            var sb = new StringBuilder("doors:");

            var leavesByDoor = new SortedDictionary<string, List<Transform>>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var m = LeafName.Match(t.name);
                    if (!m.Success) continue;
                    if (!leavesByDoor.TryGetValue(m.Groups[1].Value, out var list)) leavesByDoor[m.Groups[1].Value] = list = new List<Transform>();
                    list.Add(t);
                }
            if (leavesByDoor.Count == 0) return "no door leaves found in " + scene.path;

            var holder = FindOrCreate(scene, "Doors", FindRoot(scene, "Bungalow"));
            foreach (var kv in leavesByDoor)
            {
                string id = kv.Key;
                DoorRow row = null;
                foreach (var d in layout.doors ?? Array.Empty<DoorRow>()) if (d.id == id) row = d;
                row ??= new DoorRow { id = id };
                Opening op = null;
                foreach (var x in layout.openings) if (x.id == id) op = x;
                if (op == null) { sb.Append($" {id}: no opening in layout;"); continue; }

                kv.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                var data = new List<DoorLeafData>();
                Vector3? front = null; // the first leaf's swing side is the door's front; the other leaf (D1) follows it
                var centre = Vector3.zero;
                foreach (var leaf in kv.Value)
                {
                    Obj o = null;
                    foreach (var c in cols.objects) if (c.name == leaf.name) o = c;
                    if (o == null || o.boxes == null || o.boxes.Length == 0) { sb.Append($" {leaf.name}: no collider box;"); continue; }
                    var d = Geometry(leaf, o.boxes[0], op, ref front, out var mid);
                    d.obstacle = PrepareLeaf(leaf);
                    data.Add(d);
                    centre += mid;
                }
                if (data.Count == 0) continue;

                string key = !string.IsNullOrEmpty(row.key) ? row.key : !string.IsNullOrEmpty(layout.keyItem) ? layout.keyItem : "key_front";
                bool locked = row.locked;
                string note = "";
                if (locked && row.keys > 0)
                {
                    int placed = KeysInScene(scene, key, row.also);
                    if (placed < row.keys) { locked = false; note = $" UNLOCKED: {placed}/{row.keys} '{KeyList(key, row.also)}' pickups in the scene"; }
                }

                var go = FindOrCreate(scene, id, holder.transform);
                go.transform.position = centre / data.Count;
                var door = GetOrAdd<Door>(go);
                door.Configure(id, front.Value, locked, row.open, row.keys, key, data.ToArray(), row.also);
                // How far each side can swing before the leaf meets a wall, the jamb or furniture.
                door.openAngle = ClearSwing(door, data, +1f);
                door.backAngle = ClearSwing(door, data, -1f);
                door.SnapToStart();
                foreach (var leaf in kv.Value)
                {
                    // One interactable per leaf: an Openable from an older setup (e.g. the start bedroom's D2), or a
                    // FrontDoorPart from BLINK/Bungalow/Set Up Front Door (D1 is this Door's), would fight the Door.
                    if (leaf.TryGetComponent<Openable>(out var stale)) UnityEngine.Object.DestroyImmediate(stale);
                    if (leaf.TryGetComponent<FrontDoorPart>(out var part)) UnityEngine.Object.DestroyImmediate(part);
                    if (leaf.GetComponent<DoorLeaf>() is { } link) { link.door = door; EditorUtility.SetDirty(link); }
                }
                EditorUtility.SetDirty(door);
                sb.Append($" {id} {(locked ? (row.keys > 0 ? $"locked({row.keys} x {KeyList(key, row.also)})" : "locked") : row.open ? "open" : "closed")}{note} x{data.Count} swing +{door.openAngle:0}/-{door.backAngle:0};");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (save) EditorSceneManager.SaveScene(scene);
            return sb.ToString();
        }

        /// <summary>
        /// Hinge (leaf box vertical edge nearest a jamb, at the leaf's bottom), swing side and closed pose from the modelled
        /// leaf (the collider box is in the shell's space, as modelled). <paramref name="front"/>: the door's front; set
        /// from this leaf if still null. <paramref name="mid"/>: the closed leaf's middle at floor level.
        /// </summary>
        static DoorLeafData Geometry(Transform leaf, Box b, Opening op, ref Vector3? front, out Vector3 mid)
        {
            string w = op.wall; // walls named ..._S / _N / Z.. run along x; ..._E / _W / X.. run along z
            bool alongX = w.EndsWith("_S") || w.EndsWith("_N") || w.Contains("_Z");
            var c = V(b.c);
            var half = Quaternion.Euler(V(b.r)) * Vector3.right * (b.s[0] * 0.5f);
            Vector3 e0 = c - half, e1 = c + half;
            Func<Vector3, float> along = p => alongX ? p.x : p.z;
            Func<Vector3, float> jamb = p => Mathf.Min(Mathf.Abs(along(p) - op.span[0]), Mathf.Abs(along(p) - op.span[1]));
            Vector3 hinge = jamb(e0) <= jamb(e1) ? e0 : e1, free = hinge == e0 ? e1 : e0;
            hinge.y = free.y = c.y - b.s[1] * 0.5f; // the floor under the leaf (the basement door is at -2.85)
            var d = (free - hinge).normalized;
            float spanMid = (op.span[0] + op.span[1]) * 0.5f;
            var closedDir = (alongX ? Vector3.right : Vector3.forward) * Mathf.Sign(spanMid - along(hinge));
            float theta = Vector3.SignedAngle(closedDir, d, Vector3.up);
            var n = alongX ? Vector3.forward : Vector3.right;
            if (Mathf.Abs(theta) > 1f) { if (Vector3.Dot(d, n) < 0f) n = -n; }
            else if (Vector3.Dot(n, new Vector3(10f, 0f, 7.5f) - hinge) < 0f) n = -n; // closed in the model: swings into the house
            front ??= n;
            n = front.Value;
            float length = (free - hinge).magnitude;
            mid = hinge + closedDir * (length * 0.5f);
            var src = PrefabUtility.GetCorrespondingObjectFromSource(leaf);
            return new DoorLeafData
            {
                leaf = leaf,
                hinge = hinge,
                basePos = src != null ? src.localPosition : Vector3.zero,
                baseRot = src != null ? src.localRotation : Quaternion.identity,
                closedYaw = -theta,
                sign = Mathf.Sign(Vector3.SignedAngle(closedDir, n, Vector3.up)),
                length = length,
                closedHeading = Mathf.Atan2(closedDir.x, closedDir.z) * Mathf.Rad2Deg,
            };
        }

        /// <summary>Makes a shell leaf animatable. Returns the carving obstacle on its collider (null if it has none).</summary>
        static NavMeshObstacle PrepareLeaf(Transform leaf)
        {
            // Back to the modelled (imported) pose; the Door poses it from there.
            var src = PrefabUtility.GetCorrespondingObjectFromSource(leaf);
            leaf.SetLocalPositionAndRotation(src != null ? src.localPosition : Vector3.zero, src != null ? src.localRotation : Quaternion.identity);
            NavMeshObstacle obstacle = null;
            foreach (var t in leaf.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); // moves: no lightmap, no batching, Contribute GI off
                if (t.TryGetComponent<MeshRenderer>(out var mr))
                {
                    mr.receiveGI = ReceiveGI.LightProbes;
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                }
                if (obstacle == null && t.TryGetComponent<BoxCollider>(out var box))
                {
                    obstacle = GetOrAdd<NavMeshObstacle>(t.gameObject);
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.center = box.center;
                    obstacle.size = new Vector3(box.size.x, box.size.y, Mathf.Max(box.size.z, 0.06f));
                    obstacle.carving = true;
                    obstacle.carveOnlyStationary = true;
                    obstacle.enabled = false; // the Door switches it
                }
            }
            var rb = GetOrAdd<Rigidbody>(leaf.gameObject);
            rb.isKinematic = true;
            rb.useGravity = false;
            if (leaf.GetComponent<DoorLeaf>() == null) leaf.gameObject.AddComponent<DoorLeaf>();

            // Out of the bake whatever its state: the Door's carving obstacle blocks a locked doorway at runtime, so
            // unlocking D1 opens the route for zombies too.
            var mod = GetOrAdd<NavMeshModifier>(leaf.gameObject);
            mod.ignoreFromBuild = true;
            mod.applyToChildren = true;
            EditorUtility.SetDirty(leaf.gameObject);
            return obstacle;
        }

        // ---- placeholder keys

        /// <summary>
        /// Placeholder keys for the puzzle rooms not built yet: (name, x, floor y, z, item id, display name). Dropped onto
        /// whatever surface is there. Lily's doll room gives key_dorm (BLINK/Place Puzzle: Lily's Doll) and the flooded
        /// basement key_workshop (BLINK/Place Puzzle: Flooded Basement), so they have none; delete a row when its room is
        /// built and hands out the real key.
        /// </summary>
        static readonly (string name, float x, float y, float z, string itemId, string displayName)[] KeySpots =
        {
            ("Key_Front3_Placeholder", 17.0f, 0f, 11.5f, "key_front_3", "Front door key"),      // third puzzle room (kitchen spot)
        };

        /// <summary>
        /// The front door's placeholder keys (see <see cref="KeySpots"/>), under Keys_Placeholder: main's KeyPickup
        /// (Key.fbx) with each spot's item id, resting on whatever surface is at its spot. Idempotent.
        /// </summary>
        public static string PlaceKeys(Scene scene, bool save)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPrefabPath);
            if (prefab == null) return "placeholder keys: missing " + KeyPrefabPath;
            var old = FindRoot(scene, "Keys_Placeholder");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Keys_Placeholder");
            SceneManager.MoveGameObjectToScene(root, scene);
            Physics.SyncTransforms();
            var sb = new StringBuilder("placeholder keys:");
            for (int i = 0; i < KeySpots.Length; i++)
            {
                var (name, x, y, z, itemId, displayName) = KeySpots[i];
                var from = new Vector3(x, y + 1.6f, z); // above the floor, below the ceiling
                Vector3 at = new Vector3(x, y, z);
                string on = "floor (no hit)";
                if (Physics.Raycast(from, Vector3.down, out var hit, 1.7f, NotPlayer, QueryTriggerInteraction.Ignore)) { at = hit.point; on = hit.collider.name; }
                var k = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                k.name = name;
                k.transform.SetParent(root.transform, false);
                k.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 40f + 70f * i, 0f));
                // Rest it on the surface: lift by however far the mesh reaches below its pivot.
                var renderers = k.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    k.transform.position += Vector3.up * (at.y - b.min.y + 0.001f);
                }
                var pickup = k.GetComponent<Pickup>();
                pickup.itemId = itemId;
                pickup.displayName = displayName;
                // Held up like the bedroom key (BungalowStartBedroom): face on to the view, tipped to the right.
                pickup.holdEuler = (Quaternion.AngleAxis(25f, Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f)).eulerAngles;
                pickup.holdScale = 0.8f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
                sb.Append($" {name} {k.transform.position.ToString("F2")} on {on};");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (save) EditorSceneManager.SaveScene(scene);
            return sb.ToString();
        }

        /// <summary>
        /// The widest angle (5-degree steps, up to 90) the door swings toward <paramref name="sign"/> (+1 front, -1 back)
        /// with no leaf overlapping anything solid but itself. A leaf box is tested 6 cm short at each end and edge, so
        /// grazing the floor or its own frame doesn't count. Leaves the door posed at that angle (call SnapToStart).
        /// </summary>
        static float ClearSwing(Door door, List<DoorLeafData> data, float sign)
        {
            var own = new HashSet<Collider>();
            foreach (var l in data) foreach (var c in l.leaf.GetComponentsInChildren<Collider>(true)) own.Add(c);
            int mask = ~0;
            foreach (var layer in new[] { "Player", "Zombie", "ZombieHitbox", "Corpse", "Ignore Raycast" })
            {
                int li = LayerMask.NameToLayer(layer);
                if (li >= 0) mask &= ~(1 << li);
            }
            var hits = new Collider[16];
            float clear = 0f;
            for (float a = 5f; a <= 90.01f; a += 5f)
            {
                door.PoseAt(sign * a);
                Physics.SyncTransforms();
                foreach (var l in data)
                    foreach (var box in l.leaf.GetComponentsInChildren<BoxCollider>(true))
                    {
                        var half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f - new Vector3(0.06f, 0.06f, 0f);
                        int n = Physics.OverlapBoxNonAlloc(box.transform.TransformPoint(box.center), half, hits, box.transform.rotation,
                            mask, QueryTriggerInteraction.Ignore);
                        for (int i = 0; i < n; i++) if (!own.Contains(hits[i])) return clear;
                    }
                clear = a;
            }
            return clear;
        }

        static int KeysInScene(Scene scene, string itemId, string[] also)
        {
            int n = 0;
            foreach (var r in scene.GetRootGameObjects())
                foreach (var p in r.GetComponentsInChildren<Pickup>(true))
                    if (p.itemId == itemId || (also != null && Array.IndexOf(also, p.itemId) >= 0)) n++;
            return n;
        }

        static string KeyList(string key, string[] also) =>
            also == null || also.Length == 0 ? key : key + "/" + string.Join("/", also);

        // ---- helpers

        // Not `GetComponent() ?? AddComponent()`: a missing component is a fake null that `??` doesn't see.
        static T GetOrAdd<T>(GameObject go) where T : Component => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();

        static Transform FindRoot(Scene scene, string name)
        {
            foreach (var r in scene.GetRootGameObjects()) if (r.name == name) return r.transform;
            return null;
        }

        static GameObject FindOrCreate(Scene scene, string name, Transform parent)
        {
            if (parent != null)
            {
                var t = parent.Find(name);
                if (t != null) return t.gameObject;
            }
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static Vector3 V(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);
    }
}
