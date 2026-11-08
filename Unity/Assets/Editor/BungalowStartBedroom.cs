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
    ///   - On its top: a folded note (Readable). In its top drawer: the bedroom key (KeyPickup, item id
    ///     <see cref="KeyId"/>), parented to the drawer so it slides out with it (<see cref="PlaceKeyInDrawer"/>).
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
        // The bungalow's own paper (same values as Art/Story's Paper.mat, which a sparse checkout without Art/Story lacks:
        // the note came out pink there). Created on first use.
        const string PaperMaterial = "Assets/Art/Environment/Bungalow/Materials/M_NotePaper.mat";
        const int NotPlayer = ~(1 << 9); // the Player capsule's layer

        // Chest of drawers (1.20 x 0.58 m, top 1.21 m) moved up against the nightstand (x 4.31-4.89), beside the bed.
        // Keep in step with tools/blender/bungalow_props.py (Bed_Dresser, Bed_DresserVase).
        static readonly Vector3 DresserPos = new(5.59f, 0f, 9.13f);
        static readonly Vector3 VasePos = new(6.02f, 1.21f, 9.15f); // far end of the top, out of the way

        // On the top, at the bed end: the note.
        static readonly Vector2 NoteSpot = new(5.26f, 9.08f);
        const float NoteYaw = 14f;

        // The key: in the top drawer, in the drawer's own space (+Z = out of the front). 0.22 m behind the front panel, so
        // it lies clear of the carcass once the drawer is pulled out and the look ray clears the panel from close up.
        const string KeyDrawer = "_Drawer1";
        const float KeyBehindFront = 0.22f, KeyAcross = -0.18f, KeyYaw = -28f;

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
            report.Append(PlaceKeyInDrawer(scene));

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
            go.GetComponent<Renderer>().sharedMaterial = Paper();
            go.AddComponent<Readable>().Setup("Read", NoteTitle, NoteText);
        }

        /// <summary>The note paper (URP Lit, aged off-white, matte), created if it isn't there yet.</summary>
        public static Material Paper()
        {
            var paper = AssetDatabase.LoadAssetAtPath<Material>(PaperMaterial);
            if (paper != null) return paper;
            paper = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_NotePaper" };
            paper.SetColor("_BaseColor", new Color(0.82f, 0.78f, 0.66f, 1f));
            paper.SetFloat("_Smoothness", 0.15f);
            paper.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(paper, PaperMaterial);
            return paper;
        }

        /// <summary>Gives the start-bedroom note the bungalow paper (it was pink where Art/Story isn't checked out).</summary>
        public static string FixNotePaper(Scene scene)
        {
            var note = Find(scene, "Note_" + NoteTitle.Replace(' ', '_'));
            if (note == null) return "no note; ";
            var r = note.GetComponent<Renderer>();
            Undo.RecordObject(r, "Note paper");
            r.sharedMaterial = Paper();
            return "note paper set; ";
        }

        /// <summary>
        /// Puts the bedroom key in Bed_Dresser's top drawer (replacing any earlier Key_Bedroom), lying on the drawer's
        /// bottom and parented to the drawer, so it slides out when the drawer opens. Doesn't save the scene.
        /// </summary>
        public static string PlaceKeyInDrawer(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            var dresser = Find(scene, "Bed_Dresser");
            if (dresser == null) throw new InvalidOperationException("No Bed_Dresser in " + scene.path);
            Transform drawer = null;
            foreach (var t in dresser.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith(KeyDrawer, StringComparison.Ordinal)) drawer = t;
            if (drawer == null) throw new InvalidOperationException("Bed_Dresser has no " + KeyDrawer + ": rebuild the prop prefabs.");

            for (var old = Find(scene, "Key_Bedroom"); old != null; old = Find(scene, "Key_Bedroom"))
                Undo.DestroyObjectImmediate(old.gameObject);

            var mesh = drawer.GetComponent<MeshFilter>().sharedMesh;
            var b = mesh.bounds; // drawer space; the drawer's pivot is its centre
            var local = new Vector3(KeyAcross, b.center.y, b.max.z - KeyBehindFront);
            if (!DrawerFloor(drawer, mesh, drawer.TransformPoint(local), out var floor))
                return "NO DRAWER FLOOR for the key; ";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPrefab);
            if (prefab == null) throw new InvalidOperationException("Missing " + KeyPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(go, "Bedroom key in drawer");
            go.name = "Key_Bedroom";
            go.transform.SetParent(drawer, true);
            go.transform.SetPositionAndRotation(floor, drawer.rotation * Quaternion.Euler(0f, KeyYaw, 0f));
            // Rest it on the drawer bottom: lift by however far the mesh reaches below its pivot.
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var rb = renderers[0].bounds;
                foreach (var r in renderers) rb.Encapsulate(r.bounds);
                go.transform.position += Vector3.up * (floor.y - rb.min.y + 0.001f);
            }
            ConfigureKey(go);
            return $"key in {dresser.name}/{drawer.name} at {go.transform.position}; ";
        }

        /// <summary>The drawer's inside bottom below <paramref name="inside"/>: a ray down against the drawer mesh only.</summary>
        static bool DrawerFloor(Transform drawer, Mesh mesh, Vector3 inside, out Vector3 point)
        {
            var probe = drawer.gameObject.AddComponent<MeshCollider>();
            try
            {
                probe.sharedMesh = mesh;
                Physics.SyncTransforms();
                bool hit = probe.Raycast(new Ray(inside, Vector3.down), out var h, 1f);
                point = h.point;
                return hit;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        static void ConfigureKey(GameObject go)
        {

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
