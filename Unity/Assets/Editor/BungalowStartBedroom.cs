using System;
using System.Text;
using Blink.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Blink.EditorTools
{
    /// <summary>
    /// BLINK/Bungalow/Set Up Start Bedroom: the room the player wakes in (House_Bungalow, open in the editor).
    ///   - The chest of drawers (Bed_Dresser, GothicCommode_01) stands against the nightstand, right beside the bed.
    ///   - On its top: a folded note (Readable) and the bedroom key (KeyPickup, item id <see cref="KeyId"/>).
    ///   - The bedroom door (D2) is shut and locked: a <see cref="Blink.World.Door"/> that takes the key (layout DOOR_STATE:
    ///     D2 locked, 1 x <see cref="KeyId"/>), set up by <see cref="BungalowDoorSetup"/> once the key is in the scene. The
    ///     Door turns the kit's D2_Leaf about its hinge and carves the navmesh while it's locked.
    /// Safe to run again: positions are absolute, the "StartBedroom" group is replaced and the door is re-closed.
    /// Doesn't save the scene. Moving the drawers leaves their old baked shadow until the next lighting bake.
    /// </summary>
    public static class BungalowStartBedroom
    {
        const string RootName = "StartBedroom";
        public const string KeyId = "key_bedroom";
        const string KeyName = "Bedroom key";
        const string KeyPrefab = "Assets/Prefabs/Props/KeyPickup.prefab";
        const string PaperMaterial = "Assets/Art/Story/Materials/Paper.mat";
        const int NotPlayer = ~(1 << 9); // the Player capsule's layer

        // Chest of drawers (1.20 x 0.58 m, top 1.21 m) moved up against the nightstand (x 4.31-4.89), beside the bed.
        // Keep in step with tools/blender/bungalow_props.py (Bed_Dresser, Bed_DresserVase).
        static readonly Vector3 DresserPos = new(5.59f, 0f, 9.13f);
        static readonly Vector3 VasePos = new(6.02f, 1.21f, 9.15f); // far end of the top, out of the way

        // On the top, at the bed end: the note, then the key.
        static readonly Vector2 NoteSpot = new(5.26f, 9.08f), KeySpot = new(5.66f, 9.03f);
        const float NoteYaw = 14f, KeyYaw = -28f;

        const string NoteTitle = "Folded note";
        const string NoteText =
            "If you're reading this, you're awake. Stay that way.\n\n" +
            "Time doesn't run properly in this house. It holds still while you watch it, and the moment you look away it hurries on without you.\n\n" +
            "So don't close your eyes. Not for long. Every blink, the house gets a little further ahead of you, and the things in it get a little closer.\n\n" +
            "The key opens the door. I locked it to keep them out, not to keep you in.\n\n" +
            "Find a way out. Don't wait for morning. It doesn't come here.\n\n" +
            "— A.";

        [MenuItem("BLINK/Bungalow/Set Up Start Bedroom")]
        static void SetUpMenu() => Debug.Log("[StartBedroom] " + SetUp(SceneManager.GetActiveScene()));

        public static string SetUp(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            var report = new StringBuilder();

            var dresser = Find(scene, "Bed_Dresser");
            var vase = Find(scene, "Bed_DresserVase");
            var leaf = Find(scene, "D2_Leaf");
            if (dresser == null || leaf == null)
                throw new InvalidOperationException($"Open House_Bungalow first: no {(dresser == null ? "Bed_Dresser" : "D2_Leaf")} in {scene.path}.");

            // ---- chest of drawers beside the bed
            Undo.RecordObject(dresser, "Set Up Start Bedroom");
            dresser.position = DresserPos;
            if (vase != null)
            {
                Undo.RecordObject(vase, "Set Up Start Bedroom");
                vase.position = VasePos;
            }
            Physics.SyncTransforms();
            report.Append($"drawers at {DresserPos}; ");

            // ---- key and note on top
            foreach (var g in scene.GetRootGameObjects())
                if (g.name == RootName) Undo.DestroyObjectImmediate(g);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Set Up Start Bedroom");

            if (TopAt(NoteSpot, out var noteAt)) PlaceNote(root.transform, noteAt);
            else report.Append("NO SURFACE for the note; ");
            if (TopAt(KeySpot, out var keyAt)) PlaceKey(root.transform, keyAt);
            else report.Append("NO SURFACE for the key; ");
            report.Append("key and note placed; ");

            // ---- the door: shut and locked (Setup Doors locks D2 now that its key is in the scene)
            report.Append(BungalowDoorSetup.Setup(scene, false));

            EditorSceneManager.MarkSceneDirty(scene);
            return report.ToString();
        }

        static Transform Find(Scene scene, string name)
        {
            foreach (var g in scene.GetRootGameObjects())
                foreach (var t in g.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }

        /// <summary>The top surface at (x, z), found from above.</summary>
        static bool TopAt(Vector2 xz, out Vector3 point)
        {
            bool hit = Physics.Raycast(new Vector3(xz.x, 1.6f, xz.y), Vector3.down, out var h, 1.6f, NotPlayer, QueryTriggerInteraction.Ignore);
            point = h.point;
            return hit;
        }

        static void PlaceNote(Transform root, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Note_" + NoteTitle.Replace(' ', '_');
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.002f, Quaternion.Euler(0f, NoteYaw, 0f));
            go.transform.localScale = new Vector3(0.21f, 0.004f, 0.28f);
            var paper = AssetDatabase.LoadAssetAtPath<Material>(PaperMaterial);
            if (paper != null) go.GetComponent<Renderer>().sharedMaterial = paper;
            go.AddComponent<Readable>().Setup("Read", NoteTitle, NoteText);
        }

        static void PlaceKey(Transform root, Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPrefab);
            if (prefab == null) throw new InvalidOperationException("Missing " + KeyPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
            go.name = "Key_Bedroom";
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, KeyYaw, 0f));
            // Rest it on the top: lift by however far the mesh reaches below its pivot.
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                go.transform.position += Vector3.up * (at.y - b.min.y + 0.001f);
            }

            var pickup = go.GetComponent<Pickup>();
            pickup.itemId = KeyId;
            pickup.displayName = KeyName;
            // Held up, face on to the view, tipped to the right.
            pickup.holdEuler = (Quaternion.AngleAxis(25f, Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f)).eulerAngles;
            pickup.holdScale = 0.8f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
        }
    }
}
