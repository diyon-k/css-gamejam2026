using System.Linq;
using System.Text;
using Blink.Combat;
using Blink.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// BLINK/Bungalow/Place Pickups: the weapons and ammo lying about House_Bungalow (open in the editor). The player starts
    /// empty-handed: this also turns the Player's <c>startsWithPistol</c> off.
    ///   - The pistol (PistolPickup) on the parlour sideboard, between the vase and the fallen box.
    ///   - A frying pan (FryingPan, a melee prop) on the kitchen table, clear of the dishes (the stove's hob is full).
    ///   - Four ammo boxes (AmmoBox, <see cref="AmmoRounds"/> rounds each): three in drawers, parented so they slide out
    ///     with them (Kit_Cabinet2's left drawer, the living-room sideboard's bottom drawer, the master-bedroom dresser's
    ///     top drawer behind the axe barricade), one on the cellar barrel.
    /// Loose ones are dropped onto whatever surface is under their spot. Safe to run again: the "Pickups" group and the
    /// "Ammo_*" boxes in drawers are replaced. Doesn't save the scene (Batch does).
    /// Batch: Unity -batchmode -quit -projectPath Unity -executeMethod Blink.EditorTools.BungalowPickups.Batch
    /// </summary>
    public static class BungalowPickups
    {
        const string RootName = "Pickups";
        const int NotPlayer = ~(1 << 9); // the Player capsule's layer
        const string AmmoPrefab = "Assets/Prefabs/Props/AmmoBox.prefab";
        const int AmmoRounds = 4; // half a magazine a box: 16 spare rounds in the house (the reserve holds 8)
        const float AmmoBehindFront = 0.22f; // as the bedroom key: clear of the carcass once the drawer is out

        // Loose, dropped onto the surface below (rayFrom: the height the ray down starts at).
        static readonly (string name, string prefab, float x, float rayFrom, float z, float yaw)[] Spots =
        {
            ("Pistol_Parlour", "Assets/Prefabs/Props/PistolPickup.prefab", 11.15f, 2.4f, 17.3f, 100f),
            ("FryingPan_Kitchen", "Assets/Prefabs/Props/FryingPan.prefab", 15.18f, 2.4f, 11.0f, 90f), // clear of the kitchen list note
            ("Ammo_Cellar", AmmoPrefab, 12.9f, -0.85f, 5.85f, 20f), // on B1_Barrel (cellar floor y -2.85)
        };

        // In a drawer of a bungalow_props.json instance: the drawer by name, or "top" / "bottom" (by height).
        // Kit_Cabinet1's Drawer_L holds the axe; Bed_Dresser's top drawer holds the bedroom key.
        static readonly (string name, string furniture, string drawer, float yaw)[] Drawers =
        {
            ("Ammo_Kitchen", "Kit_Cabinet2", "Drawer_L", 15f),
            ("Ammo_Living", "Liv_Sideboard", "bottom", -20f),
            ("Ammo_MasterBedroom", "Mbr_Dresser", "top", 30f),
        };

        [MenuItem("BLINK/Bungalow/Place Pickups")]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Place Pickups: leave Play mode first."); return; }
            Debug.Log("[Pickups] " + Place(SceneManager.GetActiveScene()));
        }

        public static void Batch()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/House_Bungalow.unity");
            Debug.Log("[Pickups] " + Place(scene));
            EditorSceneManager.SaveScene(scene);
        }

        public static string Place(Scene scene)
        {
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == RootName) Object.DestroyImmediate(r);
            foreach (var spot in Drawers)
                for (var old = BungalowStartBedroom.Find(scene, spot.name); old != null; old = BungalowStartBedroom.Find(scene, spot.name))
                    Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var sb = new StringBuilder();
            foreach (var (name, path, x, rayFrom, z, yaw) in Spots)
            {
                bool hitSomething = Physics.Raycast(new Vector3(x, rayFrom, z), Vector3.down, out var hit, 3f, NotPlayer, QueryTriggerInteraction.Ignore);
                float y = hitSomething ? hit.point.y : rayFrom - 2.4f;
                var go = Spawn(scene, path, name, sb);
                if (go == null) continue;
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
                SitOn(go, y);
                sb.Append($"{name} on {(hitSomething ? hit.collider.name : "nothing")} ({x:F2}, {y:F2}, {z:F2}); ");
            }
            foreach (var spot in Drawers) sb.Append(PlaceInDrawer(scene, spot));
            sb.Append(EmptyHanded(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return sb.ToString();
        }

        static GameObject Spawn(Scene scene, string path, string name, StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.Append($"{name}: no prefab at {path}; "); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.name = name;
            if (path == AmmoPrefab && go.TryGetComponent<Pickup>(out var pickup))
            {
                var so = new SerializedObject(pickup);
                so.FindProperty("pistolRounds").intValue = AmmoRounds;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return go;
        }

        /// <summary>The ammo box on the drawer's bottom, <see cref="AmmoBehindFront"/> behind its front, a child of the
        /// drawer so it slides out with it (the look ray finds it once the drawer is open, as with the bedroom key).</summary>
        static string PlaceInDrawer(Scene scene, (string name, string furniture, string drawer, float yaw) spot)
        {
            var furniture = BungalowStartBedroom.Find(scene, spot.furniture);
            var drawers = furniture == null ? new Openable[0]
                : furniture.GetComponentsInChildren<Openable>(true).Where(o => o.motion == Openable.Motion.Sliding)
                           .OrderBy(o => o.transform.position.y).ToArray();
            var drawer = spot.drawer == "bottom" ? drawers.FirstOrDefault()
                : spot.drawer == "top" ? drawers.LastOrDefault()
                : drawers.FirstOrDefault(o => o.name == spot.drawer);
            var mesh = drawer != null && drawer.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
            if (mesh == null) return $"{spot.name}: no {spot.drawer} drawer under {spot.furniture}; ";

            var t = drawer.transform;
            var b = mesh.bounds; // drawer space, +z out of the front
            float z = b.max.z - Mathf.Min(AmmoBehindFront, b.size.z * 0.5f);
            // a drawer cut from a solid model may have no bottom to hit: its bounds' floor instead, as the axe does
            if (!BungalowStartBedroom.DrawerFloor(t, mesh, t.TransformPoint(new Vector3(b.center.x, b.center.y, z)), out var floor))
                floor = t.TransformPoint(new Vector3(b.center.x, b.min.y + 0.03f, z));

            var sb = new StringBuilder();
            var go = Spawn(scene, AmmoPrefab, spot.name, sb);
            if (go == null) return sb.ToString();
            go.transform.SetParent(t, true);
            go.transform.SetPositionAndRotation(floor, t.rotation * Quaternion.Euler(0f, spot.yaw, 0f));
            SitOn(go, floor.y);
            return $"{spot.name} in {spot.furniture}/{drawer.name} at {go.transform.position}; ";
        }

        /// <summary>The Player starts with empty hands: the pistol is found (PistolPickup), not carried in.</summary>
        static string EmptyHanded(Scene scene)
        {
            var weapons = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerWeapons>(true)).FirstOrDefault();
            if (weapons == null) return "no PlayerWeapons in the scene; ";
            var so = new SerializedObject(weapons);
            so.FindProperty("startsWithPistol").boolValue = false;
            so.ApplyModifiedProperties();
            return $"{weapons.name} starts without the pistol; ";
        }

        /// <summary>Sits the mesh's underside on <paramref name="y"/> (the pan's pivot isn't at its base).</summary>
        static void SitOn(GameObject go, float y)
        {
            float? min = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                min = min is { } m ? Mathf.Min(m, r.bounds.min.y) : r.bounds.min.y;
            if (min is { } bottom) go.transform.position += Vector3.up * (y - bottom + 0.003f);
        }
    }
}
