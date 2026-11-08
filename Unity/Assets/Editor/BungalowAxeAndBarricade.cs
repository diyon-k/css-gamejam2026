// The axe and the boarded-up master bedroom (House_Bungalow). Menu: BLINK > Bungalow > Place Axe And Barricade.
// The axe starts in the kitchen cabinet's drawer (Kit_Cabinet1 / Drawer_L): PlayerWeapons.axeStartsAt points at an
// "AxeStart" under the drawer, so the axe rides the drawer and is picked up when it's open.
// D8 (corridor -> MasterBedroom) gets planks nailed over its corridor side: axe only, PlankHits chops each, and the door
// stays locked (bungalow_layout.json D8 "locked": true) until the last plank breaks (Barricade).
// Re-running replaces both. Batch: Unity -batchmode -quit -projectPath Unity -executeMethod BungalowAxeAndBarricade.Batch
using System.Linq;
using Blink.Combat;
using Blink.Effects;
using Blink.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BungalowAxeAndBarricade
{
    const string Cabinet = "Kit_Cabinet1", Drawer = "Drawer_L", DoorId = "D8";
    const string PlankMat = "Assets/Art/Environment/Bungalow/Materials/M_FloorPlank.mat";
    const int PlankHits = 3;
    // Doorway D8: x 3.4..4.6 on the wall at z 16.875; the corridor is the -z side.
    const float DoorX = 4.0f, WallZ = 16.875f;
    // (height, tilt in degrees) per plank: lowest to highest, crossed a little like they went up in a hurry.
    static readonly (float y, float tilt)[] Planks = { (0.55f, 6f), (1.1f, -9f), (1.6f, 4f), (1.95f, -5f) };

    [MenuItem("BLINK/Bungalow/Place Axe And Barricade")]
    public static void Place()
    {
        PlaceAxe();
        PlaceBarricade();
    }

    public static void Batch()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/House_Bungalow.unity");
        Place();
        EditorSceneManager.SaveScene(scene);
    }

    static void PlaceAxe()
    {
        var cabinet = GameObject.Find(Cabinet);
        var drawer = cabinet != null ? cabinet.transform.Find(Drawer) : null;
        var weapons = Object.FindAnyObjectByType<PlayerWeapons>();
        if (drawer == null || weapons == null) { Debug.LogWarning($"[AxeAndBarricade] Need {Cabinet}/{Drawer} and a Player in the scene."); return; }
        var old = drawer.Find("AxeStart");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        // Lay the axe flat along the drawer's diagonal: its longest axis across the drawer, its thinnest one up.
        var grip = weapons.Axe != null && weapons.Axe.ViewModel != null ? weapons.Axe.ViewModel.Grip : null;
        var axe = grip != null ? grip.Cast<Transform>().FirstOrDefault(t => t.GetComponent<MeshRenderer>() != null) : null;
        var box = drawer.GetComponent<BoxCollider>();
        if (axe == null || box == null) { Debug.LogWarning("[AxeAndBarricade] No axe mesh under the axe grip, or no drawer collider."); return; }
        var b = axe.GetComponent<MeshFilter>().sharedMesh.bounds;
        var size = Vector3.Scale(b.size, axe.lossyScale);
        int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        int thinnest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        var axeFrame = Quaternion.LookRotation(Axis(longest), Axis(thinnest)); // z -> long, y -> thin
        var diag = new Vector3(box.size.x, 0f, box.size.z);
        var rot = Quaternion.LookRotation(diag, Vector3.up) * Quaternion.Inverse(axeFrame);
        var centre = box.center + Vector3.up * (-box.size.y * 0.5f + 0.03f + size[thinnest] * 0.5f);

        var start = new GameObject("AxeStart").transform;
        Undo.RegisterCreatedObjectUndo(start.gameObject, "Place Axe");
        start.SetParent(drawer, false);
        start.SetLocalPositionAndRotation(centre - rot * Vector3.Scale(b.center, axe.lossyScale), rot);

        var so = new SerializedObject(weapons);
        so.FindProperty("axeStartsAt").objectReferenceValue = start;
        so.ApplyModifiedProperties();
        Debug.Log($"[AxeAndBarricade] Axe ({size[longest]:F2} m) in {Cabinet}/{Drawer} (diagonal {diag.magnitude:F2} m).");
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    static void PlaceBarricade()
    {
        var door = Object.FindObjectsByType<Door>(FindObjectsSortMode.None).FirstOrDefault(d => d.doorId == DoorId);
        if (door == null) { Debug.LogWarning($"[AxeAndBarricade] No door {DoorId} in the scene."); return; }
        var old = GameObject.Find("Barricade_" + DoorId);
        if (old != null) Undo.DestroyObjectImmediate(old);

        Undo.RecordObject(door, "Lock " + DoorId);
        door.startLocked = true;

        // Nail them to the corridor face of the wall beside the doorway.
        Physics.SyncTransforms();
        float face = Physics.Raycast(new Vector3(3.2f, 1.2f, WallZ - 1f), Vector3.forward, out var hit, 1.5f) ? hit.point.z : WallZ - 0.1f;

        var root = new GameObject("Barricade_" + DoorId);
        Undo.RegisterCreatedObjectUndo(root, "Place Barricade");
        root.transform.SetParent(door.transform.parent, true);
        root.AddComponent<Barricade>().Configure(door);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(PlankMat);
        for (int i = 0; i < Planks.Length; i++)
        {
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "Plank_" + i;
            plank.transform.SetParent(root.transform, false);
            // alternate depths so crossing planks don't z-fight
            plank.transform.SetPositionAndRotation(new Vector3(DoorX, Planks[i].y, face - 0.02f - (i % 2) * 0.035f), Quaternion.Euler(0f, 0f, Planks[i].tilt));
            plank.transform.localScale = new Vector3(1.6f, 0.17f, 0.03f);
            if (mat != null) plank.GetComponent<MeshRenderer>().sharedMaterial = mat;
            plank.AddComponent<Breakable>().Configure(PlankHits, null, blade: true);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[AxeAndBarricade] {Planks.Length} planks over {DoorId} at z {face:F2}, {PlankHits} axe hits each; {DoorId} locked until they're gone.");
    }
}
