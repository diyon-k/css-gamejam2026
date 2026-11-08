using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// BLINK/Bungalow/Place Pickups: the weapons lying about House_Bungalow (open in the editor). The player starts empty-handed.
    ///   - The pistol (PistolPickup) on the parlour sideboard, between the vase and the fallen box.
    ///   - A frying pan (FryingPan, a melee prop) on the kitchen table, clear of the dishes (the stove's hob is full).
    /// Each is dropped onto whatever surface is under its spot. Safe to run again: the "Pickups" group is replaced.
    /// Doesn't save the scene.
    /// </summary>
    public static class BungalowPickups
    {
        const string RootName = "Pickups";
        const int NotPlayer = ~(1 << 9); // the Player capsule's layer

        static readonly (string name, string prefab, float x, float z, float yaw)[] Spots =
        {
            ("Pistol_Parlour", "Assets/Prefabs/Props/PistolPickup.prefab", 11.15f, 17.3f, 100f),
            ("FryingPan_Kitchen", "Assets/Prefabs/Props/FryingPan.prefab", 15.18f, 11.0f, 90f), // clear of the kitchen list note
        };

        [MenuItem("BLINK/Bungalow/Place Pickups")]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Place Pickups: leave Play mode first."); return; }
            Debug.Log("[Pickups] " + Place(SceneManager.GetActiveScene()));
        }

        public static string Place(Scene scene)
        {
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == RootName) Object.DestroyImmediate(r);
            Physics.SyncTransforms();

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var sb = new StringBuilder();
            foreach (var (name, path, x, z, yaw) in Spots)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.Append($"{name}: no prefab at {path}; "); continue; }
                var top = new Vector3(x, 2.4f, z);
                float y = Physics.Raycast(top, Vector3.down, out var hit, 3f, NotPlayer, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
                // Sit the mesh's underside on the surface (the pan's pivot isn't at its base).
                if (Bottom(go) is { } bottom) go.transform.position += Vector3.up * (y - bottom + 0.003f);
                sb.Append($"{name} on {(hit.collider != null ? hit.collider.name : "nothing")} ({x:F2}, {y:F2}, {z:F2}); ");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return sb.ToString();
        }

        static float? Bottom(GameObject go)
        {
            float? min = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                min = min is { } m ? Mathf.Min(m, r.bounds.min.y) : r.bounds.min.y;
            return min;
        }
    }
}
