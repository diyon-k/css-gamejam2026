using System;
using System.Text;
using Blink.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Blink.EditorTools
{
    /// <summary>
    /// BLINK/Bungalow/Set Up Front Door: the bungalow's way out (House_Bungalow, open in the editor).
    ///   - Both leaves of the double front door (D1_LeafL, D1_LeafR) swing shut about their hinges. They stop being
    ///     static, are left out of navmesh bakes and carve a NavMeshObstacle instead, so zombies can't path through.
    ///   - Three brass lock plates go on the inside face of the right leaf, by the meeting edge, one above the other.
    ///     Each has a keyhole and a hidden key (the KeyPickup model) that shows and turns when its key goes in.
    ///   - A "FrontDoor" object holds the <see cref="FrontDoor"/>, which needs <see cref="Keys"/> (one per lock, from the
    ///     puzzle rooms). The leaves and plates get a <see cref="FrontDoorPart"/> so looking anywhere at the door works.
    /// Safe to run again: the "FrontDoor" object and the plates are replaced and the leaves are re-shut. Doesn't save
    /// the scene. The leaves keep their old baked lighting until the next bake (they're lit by probes now).
    /// </summary>
    public static class BungalowFrontDoor
    {
        const string RootName = "FrontDoor";
        const string PlatesName = "FrontDoor_Locks";
        const string KeyPrefab = "Assets/Prefabs/Props/KeyPickup.prefab";
        const string BrassMaterial = "Assets/Materials/House/Brass.mat";
        const string IronMaterial = "Assets/Art/Props/Materials/House/CastIron.mat";

        /// <summary>The keys the front door takes, top lock first: item id and what the prompt calls it.</summary>
        public static readonly (string id, string name)[] Keys =
        {
            ("key_dorm", "Lily's key"),          // Lily's doll room (docs/puzzles/LILYS_DOLL_ROOM.md)
            ("key_workshop", "the workshop key"), // the flooded basement (docs/puzzles/FLOODED_BASEMENT.md)
            ("key_front_3", "the third key"),    // the third puzzle room: not built yet, give its reward Pickup this id
        };

        // D1 (SPEC 1.4): south exterior wall, opening x 8.45-10.05, two 0.8 leaves hinged at the jambs, swinging in (+z).
        const float JambL = 8.45f, JambR = 10.05f;
        static readonly Vector3 OpenDir = Vector3.forward; // into the house

        // Lock plates (on the right leaf's room face): centre this far in from the meeting edge, at these heights.
        const float PlateInset = 0.075f;
        static readonly float[] PlateHeights = { 1.38f, 1.12f, 0.86f };
        static readonly Vector3 PlateSize = new(0.07f, 0.15f, 0.008f); // width, height, depth off the leaf
        static readonly Vector3 KeyholeSize = new(0.011f, 0.028f, 0.002f);
        const float KeyholeRise = 0.02f; // above the plate centre
        const float KeyOutside = 0.45f; // share of the key's length left sticking out of the keyhole (the bow end)

        [MenuItem("BLINK/Bungalow/Set Up Front Door")]
        static void SetUpMenu() => Debug.Log("[FrontDoor] " + SetUp(SceneManager.GetActiveScene()));

        public static string SetUp(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            var report = new StringBuilder();

            var leafL = Find(scene, "D1_LeafL");
            var leafR = Find(scene, "D1_LeafR");
            if (leafL == null || leafR == null)
                throw new InvalidOperationException($"Open House_Bungalow first: no {(leafL == null ? "D1_LeafL" : "D1_LeafR")} in {scene.path}.");

            foreach (var g in scene.GetRootGameObjects())
                if (g.name == RootName) Undo.DestroyObjectImmediate(g);
            foreach (Transform child in leafR)
                if (child.name == PlatesName) { Undo.DestroyObjectImmediate(child.gameObject); break; }

            // ---- shut both leaves
            var left = ShutLeaf(leafL, JambL, Vector3.right, report);
            var right = ShutLeaf(leafR, JambR, Vector3.left, report);
            if (left == null || right == null) return report.Append("front door NOT set up.").ToString();
            Physics.SyncTransforms();

            // ---- the door
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Set Up Front Door");
            root.transform.position = new Vector3((JambL + JambR) * 0.5f, 1.1f, left.hinge.z);
            var door = root.AddComponent<FrontDoor>();
            door.noun = "front door";
            door.leaves = new[] { left, right };

            // ---- lock plates and keys on the right leaf
            door.locks = BuildLocks(leafR, door, report);

            foreach (var leaf in new[] { leafL, leafR })
            {
                var part = GetOrAdd<FrontDoorPart>(leaf.gameObject);
                Undo.RecordObject(part, "Set Up Front Door");
                part.door = door;
                EditorUtility.SetDirty(part);
            }
            EditorUtility.SetDirty(door);
            EditorSceneManager.MarkSceneDirty(scene);
            return report.ToString();
        }

        /// <summary>
        /// Swings <paramref name="leaf"/> shut about its hinge (the leaf box's vertical edge nearest the jamb at x =
        /// <paramref name="jambX"/>), so its free edge points along <paramref name="closedDir"/>. Null if it has no collider.
        /// </summary>
        static FrontDoor.Leaf ShutLeaf(Transform leaf, float jambX, Vector3 closedDir, StringBuilder report)
        {
            var box = leaf.GetComponentInChildren<BoxCollider>(true);
            if (box == null) { report.Append($"{leaf.name} has no collider; "); return null; }

            Vector3 centre = box.transform.TransformPoint(box.center);
            Vector3 half = box.transform.TransformVector(Vector3.right * (box.size.x * 0.5f));
            Vector3 e0 = centre - half, e1 = centre + half;
            Vector3 hinge = Mathf.Abs(e0.x - jambX) <= Mathf.Abs(e1.x - jambX) ? e0 : e1;
            Vector3 free = hinge == e0 ? e1 : e0;
            hinge.y = 0f;

            float toClosed = Vector3.SignedAngle(Flat(free - hinge), closedDir, Vector3.up);
            Undo.RecordObject(leaf, "Set Up Front Door");
            leaf.RotateAround(hinge, Vector3.up, toClosed);

            // It moves now: not static (static batching would freeze it), and left out of navmesh bakes (the obstacle carves instead).
            foreach (var t in leaf.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(t.gameObject, "Set Up Front Door");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
            var modifier = GetOrAdd<NavMeshModifier>(leaf.gameObject);
            Undo.RecordObject(modifier, "Set Up Front Door");
            modifier.ignoreFromBuild = true;

            var obstacle = GetOrAdd<NavMeshObstacle>(box.gameObject);
            Undo.RecordObject(obstacle, "Set Up Front Door");
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = new Vector3(box.size.x, box.size.y, Mathf.Max(box.size.z, 0.15f));
            obstacle.carving = true;

            float openAngle = Vector3.SignedAngle(closedDir, OpenDir, Vector3.up);
            report.Append($"{leaf.name} shut ({toClosed:0} deg about {hinge}), opens {openAngle:0} deg; ");
            return new FrontDoor.Leaf { leaf = leaf, hinge = hinge, openAngle = openAngle };
        }

        static FrontDoor.Lock[] BuildLocks(Transform leaf, FrontDoor door, StringBuilder report)
        {
            var box = leaf.GetComponentInChildren<BoxCollider>(true);
            var bt = box.transform;
            Vector3 centre = bt.TransformPoint(box.center);
            Vector3 across = bt.TransformVector(Vector3.right * (box.size.x * 0.5f)); // centre to one vertical edge
            // The meeting edge is the one away from the hinge (the right leaf hinges on the right jamb).
            Vector3 toMeeting = (centre + across).x < (centre - across).x ? across.normalized : -across.normalized;
            Vector3 toRoom = bt.forward.z >= 0f ? bt.forward : -bt.forward; // the house is on +z
            float halfWidth = across.magnitude, halfThick = box.size.z * 0.5f * bt.lossyScale.z;
            var faceRot = Quaternion.LookRotation(toRoom, Vector3.up); // +Z out of the door into the room

            var group = new GameObject(PlatesName).transform;
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Set Up Front Door");
            group.SetParent(leaf, false);
            group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            group.gameObject.AddComponent<FrontDoorPart>().door = door;

            var brass = AssetDatabase.LoadAssetAtPath<Material>(BrassMaterial);
            var iron = AssetDatabase.LoadAssetAtPath<Material>(IronMaterial);
            var keyModel = LoadKeyModel(out var keyMesh, out var keyMaterials, out var keyScale);
            if (keyModel == null) report.Append($"NO KEY MODEL in {KeyPrefab}: locks have no keys to show; ");

            var locks = new FrontDoor.Lock[Keys.Length];
            for (int i = 0; i < Keys.Length; i++)
            {
                float y = PlateHeights[Mathf.Min(i, PlateHeights.Length - 1)];
                Vector3 face = centre + toMeeting * (halfWidth - PlateInset) + toRoom * halfThick;
                face.y = y;

                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = $"Lock_{i + 1}_Plate";
                plate.transform.SetParent(group, false);
                plate.transform.SetPositionAndRotation(face + toRoom * (PlateSize.z * 0.5f), faceRot);
                plate.transform.localScale = PlateSize;
                if (brass != null) plate.GetComponent<Renderer>().sharedMaterial = brass;

                Vector3 hole = face + toRoom * PlateSize.z + Vector3.up * KeyholeRise;
                var keyhole = GameObject.CreatePrimitive(PrimitiveType.Cube);
                keyhole.name = $"Lock_{i + 1}_Keyhole";
                keyhole.transform.SetParent(group, false);
                keyhole.transform.SetPositionAndRotation(hole + toRoom * (KeyholeSize.z * 0.5f), faceRot);
                keyhole.transform.localScale = KeyholeSize;
                Object.DestroyImmediate(keyhole.GetComponent<Collider>());
                if (iron != null) keyhole.GetComponent<Renderer>().sharedMaterial = iron;

                var pivot = new GameObject($"Lock_{i + 1}_Key").transform;
                pivot.SetParent(group, false);
                pivot.SetPositionAndRotation(hole, faceRot);
                if (keyModel != null) AddKeyModel(pivot, keyMesh, keyMaterials, keyScale);
                pivot.gameObject.SetActive(false); // shows when its key goes in

                locks[i] = new FrontDoor.Lock { itemId = Keys[i].id, keyName = Keys[i].name, keyVisual = pivot };
            }
            report.Append($"{Keys.Length} locks on {leaf.name}: {string.Join(", ", Array.ConvertAll(Keys, k => k.id))}.");
            return locks;
        }

        static GameObject LoadKeyModel(out Mesh mesh, out Material[] materials, out Vector3 scale)
        {
            mesh = null;
            materials = null;
            scale = Vector3.one;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPrefab);
            var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
            if (filter == null || filter.sharedMesh == null) return null;
            mesh = filter.sharedMesh;
            materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
            scale = filter.transform.lossyScale;
            return filter.gameObject;
        }

        /// <summary>
        /// The key under its keyhole pivot: shaft along the pivot's Z with the bow end out (+Z), blade upright, pushed in
        /// so only the bow end shows. The key mesh runs along its X with the bow at one end; the bow ring has most of the
        /// vertices, so the vertex centroid says which end it is.
        /// </summary>
        static void AddKeyModel(Transform pivot, Mesh mesh, Material[] materials, Vector3 scale)
        {
            var b = mesh.bounds;
            float bowSign = -1f;
            var verts = mesh.vertices;
            if (verts.Length > 0)
            {
                float sum = 0f;
                foreach (var v in verts) sum += v.x;
                bowSign = sum / verts.Length >= b.center.x ? 1f : -1f;
            }
            float bowEnd = bowSign > 0f ? b.max.x : b.min.x;
            var atKeyhole = new Vector3(bowEnd - bowSign * b.size.x * KeyOutside, b.center.y, b.center.z);

            var model = new GameObject("Model").transform;
            model.SetParent(pivot, false);
            // mesh X (bow way) -> pivot +Z, mesh Y (flat face) -> pivot X (so the blade stands upright).
            model.localRotation = Quaternion.LookRotation(Vector3.up * bowSign, Vector3.right);
            model.localScale = scale;
            model.localPosition = -(model.localRotation * Vector3.Scale(atKeyhole, scale));
            model.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = model.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // a key's shadow on the door is just noise
        }

        static Transform Find(Scene scene, string name)
        {
            foreach (var g in scene.GetRootGameObjects())
                foreach (var t in g.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        // Not ??: a missing component is a Unity fake-null in the editor.
        static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent<T>(out var c) ? c : Undo.AddComponent<T>(go);
    }
}
