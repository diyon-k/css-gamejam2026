using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// Bungalow Phase 2 (grey-box) scene assembly. Builds Assets/Scenes/House_Bungalow.unity from the Blender
    /// grey-box (tools/blender/bungalow_greybox.py): the model, its box colliders and static flags, the Player /
    /// GameSystems / Effects prefabs, spawn / patrol / match-camera markers, a temporary neutral light, and a
    /// navmesh baked with the "BungalowZombie" agent type (created in the project's NavMesh settings if missing).
    /// Data comes from the two JSON files the Blender build writes, so re-running after a rebuild is safe.
    /// Never touches House.unity or the HouseBlockout scripts.
    /// </summary>
    public static class BungalowGreyboxBuilder
    {
        const string ScenePath = "Assets/Scenes/House_Bungalow.unity";
        const string ArtRoot = "Assets/Art/Environment/Bungalow/";
        const string ModelPath = ArtRoot + "Greybox/Bungalow_Greybox.fbx";
        const string CollidersPath = ArtRoot + "Greybox/Bungalow_Greybox_colliders.json";
        const string ShellModelPath = ArtRoot + "Kit/Bungalow_Shell.fbx";                 // Phase 3 modular-kit shell
        const string ShellCollidersPath = ArtRoot + "Kit/Bungalow_Shell_colliders.json";
        const string LayoutPath = ArtRoot + "bungalow_layout.json";
        const string NavDataFolder = "Assets/Scenes/House_Bungalow";
        const string NavDataPath = NavDataFolder + "/NavMesh-Greybox.asset";
        const string TempLightName = "TEMP_GreyboxLight";

        const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                              StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic |
                                              StaticEditorFlags.ReflectionProbeStatic;

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Box { public float[] c; public float[] s; public float[] r; }
        [Serializable] class Obj { public string name; public Box[] boxes; public bool navIgnore; public bool isStatic; public float[] pivot; public int tris; }
        [Serializable] class ColliderFile { public Obj[] objects; }
        [Serializable] class Spawn { public string id; public string kind; public string route; public float[] pos; public float yaw; }
        [Serializable] class Route { public string id; public float[] pts; }
        [Serializable] class Cam { public string id; public string @ref; public float[] pos; public float yaw; public float pitch; public float fov; public int w; public int h; }
        [Serializable] class Agent { public string name; public float radius; public float height; public float climb; public float slope; public float voxel; public float min_region; }
        [Serializable] class Opening { public string id; public string kind; public string wall; public float[] span; public float[] y; }
        [Serializable] class Layout { public Spawn[] spawns; public Route[] routes; public Cam[] cameras; public Agent agent; public Opening[] openings; }
#pragma warning restore 0649

        [MenuItem("BLINK/Bungalow/Build Grey-box Scene")]
        static void BuildMenu() => Debug.Log(Build(true));

        [MenuItem("BLINK/Bungalow/Build Shell Scene")]
        static void BuildShellMenu() => Debug.Log(Build(true, true));

        /// <summary>
        /// Creates (or replaces) House_Bungalow.unity (or <paramref name="scenePath"/>) from the grey-box, or from the
        /// Phase 3 kit shell when <paramref name="shell"/> is set (same object names and colliders, plus door leaves).
        /// The navmesh is only baked into Assets/Scenes/House_Bungalow/ for the main scene. Returns a short report.
        /// </summary>
        public static string Build(bool bakeNavMesh, bool shell = false, string scenePath = null)
        {
            if (string.IsNullOrEmpty(scenePath)) scenePath = ScenePath;
            string modelPath = shell ? ShellModelPath : ModelPath, collidersPath = shell ? ShellCollidersPath : CollidersPath;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode first.");
            var active = SceneManager.GetActiveScene();
            if (active.isDirty && active.path != scenePath)
                throw new InvalidOperationException($"Save or discard the changes in {active.path} first.");

            var cols = JsonUtility.FromJson<ColliderFile>(File.ReadAllText(collidersPath));
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException(modelPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var report = new System.Text.StringBuilder();

            // ---- level geometry
            var level = new GameObject("Bungalow");
            GameObjectUtility.SetStaticEditorFlags(level, StaticFlags);
            var grey = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            grey.name = shell ? "Shell" : "Greybox";
            grey.transform.SetParent(level.transform, false);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in grey.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            GameObjectUtility.SetStaticEditorFlags(grey, StaticFlags);
            foreach (var g in grey.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(g.gameObject, StaticFlags);

            int boxes = 0, missing = 0;
            foreach (var o in cols.objects)
            {
                if (!byName.TryGetValue(o.name, out var t)) { missing++; report.AppendLine("missing object " + o.name); continue; }
                var flags = o.isStatic ? StaticFlags : 0;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                var pivot = V(o.pivot);
                for (int i = 0; i < o.boxes.Length; i++)
                {
                    var b = o.boxes[i];
                    var rot = V(b.r);
                    BoxCollider bc;
                    if (rot == Vector3.zero)
                    {
                        bc = t.gameObject.AddComponent<BoxCollider>();
                        bc.center = V(b.c) - pivot;
                    }
                    else
                    {
                        var child = new GameObject($"{o.name}_Collider{i}");
                        child.transform.SetParent(t, false);
                        child.transform.localPosition = V(b.c) - pivot;
                        child.transform.localRotation = Quaternion.Euler(rot);
                        GameObjectUtility.SetStaticEditorFlags(child, flags);
                        bc = child.AddComponent<BoxCollider>();
                    }
                    bc.size = V(b.s);
                    boxes++;
                }
                // door leaves stay in the navmesh build (zombies must not path through a leaf); others as the JSON says
                bool leaf = shell && o.name.Contains("_Leaf");
                if (o.navIgnore && !leaf) t.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            }
            report.AppendLine($"objects {cols.objects.Length}, box colliders {boxes}, missing {missing}");
            if (shell) report.AppendLine(OpenLeaves(byName, cols, layout));

            // ---- markers: spawns, patrol routes, reference-match cameras
            var markers = new GameObject("Markers");
            var spawns = Child(markers.transform, "Spawns");
            Vector3 playerPos = Vector3.zero;
            float playerYaw = 0f;
            foreach (var s in layout.spawns)
            {
                var m = Child(spawns, "Spawn_" + s.id + (string.IsNullOrEmpty(s.route) ? "" : " (" + s.route + ")"));
                m.position = V(s.pos);
                m.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                if (s.kind == "player") { playerPos = m.position; playerYaw = s.yaw; }
            }
            var routes = Child(markers.transform, "Routes");
            foreach (var r in layout.routes)
            {
                var root = Child(routes, "Route_" + r.id);
                for (int i = 0; i * 3 + 2 < r.pts.Length; i++)
                    Child(root, $"WP_{i:00}").position = new Vector3(r.pts[i * 3], r.pts[i * 3 + 1], r.pts[i * 3 + 2]);
            }
            var cams = Child(markers.transform, "MatchCameras");
            foreach (var c in layout.cameras)
            {
                var m = Child(cams, c.id);
                m.position = V(c.pos);
                m.rotation = Quaternion.Euler(-c.pitch, c.yaw, 0f); // spec pitch is negative = looking down
            }

            // ---- gameplay prefabs
            var player = Prefab("Assets/Prefabs/Player.prefab", scene);
            player.transform.SetPositionAndRotation(playerPos, Quaternion.Euler(0f, playerYaw, 0f));
            Prefab("Assets/Prefabs/GameSystems.prefab", scene);
            Prefab("Assets/Prefabs/Effects.prefab", scene);
            var spawner = new SerializedObject(new GameObject("ClownSpawner").AddComponent<Blink.Mobs.ClownSpawner>());
            spawner.FindProperty("clownPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Zombie.prefab");
            spawner.ApplyModifiedPropertiesWithoutUndo();

            // ---- temporary neutral light (grey-box only; the lighting pass replaces it)
            var lightGo = new GameObject(TempLightName);
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.36f, 0.36f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            // ---- navmesh
            int agentId = EnsureAgentType(layout.agent);
            var surface = level.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agentId;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.overrideVoxelSize = true;
            surface.voxelSize = layout.agent.voxel;
            surface.minRegionArea = layout.agent.min_region;
            report.AppendLine($"agent {layout.agent.name} id {agentId}");
            if (bakeNavMesh && scenePath == ScenePath) report.AppendLine(Bake(surface));

            EditorSceneManager.SaveScene(scene, scenePath);
            report.AppendLine("saved " + scenePath);
            return report.ToString();
        }

        /// <summary>Bakes the surface and stores the data next to the scene (Assets/Scenes/House_Bungalow/).</summary>
        public static string Bake(NavMeshSurface surface)
        {
            surface.BuildNavMesh();
            var built = surface.navMeshData;
            if (!AssetDatabase.IsValidFolder(NavDataFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "House_Bungalow");
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavDataPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(built, existing);
                surface.RemoveData();
                surface.navMeshData = existing;
                surface.AddData();
            }
            else
            {
                AssetDatabase.CreateAsset(built, NavDataPath);
            }
            AssetDatabase.SaveAssetIfDirty(surface.navMeshData);
            var tri = NavMesh.CalculateTriangulation();
            return $"navmesh baked: {tri.indices.Length / 3} tris, {tri.vertices.Length} verts -> {NavDataPath}";
        }

        /// <summary>Adds (or updates) a NavMesh agent type in ProjectSettings/NavMeshAreas.asset. Returns its id.</summary>
        static int EnsureAgentType(Agent a)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0];
            var so = new SerializedObject(asset);
            var names = so.FindProperty("m_SettingNames");
            var settings = so.FindProperty("m_Settings");
            int idx = -1;
            for (int i = 0; i < names.arraySize; i++)
                if (names.GetArrayElementAtIndex(i).stringValue == a.name) idx = i;
            if (idx < 0)
            {
                NavMesh.CreateSettings(); // appends a new agent type with a fresh id
                so.Update();
                idx = settings.arraySize - 1;
                if (names.arraySize < settings.arraySize) names.arraySize = settings.arraySize;
                names.GetArrayElementAtIndex(idx).stringValue = a.name;
            }
            var s = settings.GetArrayElementAtIndex(idx);
            s.FindPropertyRelative("agentRadius").floatValue = a.radius;
            s.FindPropertyRelative("agentHeight").floatValue = a.height;
            s.FindPropertyRelative("agentClimb").floatValue = a.climb;
            s.FindPropertyRelative("agentSlope").floatValue = a.slope;
            s.FindPropertyRelative("manualCellSize").boolValue = true;
            s.FindPropertyRelative("cellSize").floatValue = a.voxel;
            s.FindPropertyRelative("minRegionArea").floatValue = a.min_region;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(asset); // only this settings file, not every dirty project setting
            return s.FindPropertyRelative("agentTypeID").intValue;
        }

        static GameObject Prefab(string path, Scene scene)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) throw new FileNotFoundException(path);
            return (GameObject)PrefabUtility.InstantiatePrefab(p, scene);
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // Doors that stay shut (story locks). Every other leaf hung less than fully open is swung to 90 degrees.
        static readonly HashSet<string> LockedDoors = new HashSet<string> { "D4" };
        const float FullyOpen = 90f;

        /// <summary>
        /// The kit shell hangs some leaves part-open (D1's right leaf 18 deg, D3 62 deg), where they catch the player
        /// capsule. Swings every unlocked leaf that is less than fully open to 90 degrees about its hinge, in the
        /// direction it already swings, so the doorway is clear; colliders (children) follow. Scene-only: the FBX is
        /// unchanged. The hinge is the leaf box's vertical edge nearest a jamb of its opening (layout span).
        /// </summary>
        static string OpenLeaves(Dictionary<string, Transform> byName, ColliderFile cols, Layout layout)
        {
            var sb = new System.Text.StringBuilder("door leaves:");
            foreach (var o in cols.objects)
            {
                int k = o.name.IndexOf("_Leaf", StringComparison.Ordinal);
                if (k < 0 || o.boxes == null || o.boxes.Length == 0 || !byName.TryGetValue(o.name, out var t)) continue;
                string door = o.name.Substring(0, k);
                Opening op = null;
                if (layout.openings != null) foreach (var x in layout.openings) if (x.id == door) op = x;
                if (op == null) continue;
                // walls named ..._S / _N / Z.. run along x; ..._E / _W / X.. run along z
                string w = op.wall;
                bool alongX = w.EndsWith("_S") || w.EndsWith("_N") || w.Contains("_Z");
                var b = o.boxes[0];
                var c = V(b.c);
                var axis = Quaternion.Euler(V(b.r)) * Vector3.right * (b.s[0] * 0.5f);
                Vector3 e0 = c - axis, e1 = c + axis;
                Func<Vector3, float> along = p => alongX ? p.x : p.z;
                Func<Vector3, float> jamb = p => Mathf.Min(Mathf.Abs(along(p) - op.span[0]), Mathf.Abs(along(p) - op.span[1]));
                Vector3 hinge = jamb(e0) <= jamb(e1) ? e0 : e1, free = hinge == e0 ? e1 : e0;
                hinge.y = free.y = 0f;
                var d = (free - hinge).normalized;
                float spanMid = (op.span[0] + op.span[1]) * 0.5f;
                var closedDir = (alongX ? Vector3.right : Vector3.forward) * Mathf.Sign(spanMid - along(hinge));
                float openAngle = Vector3.Angle(d, closedDir);
                if (LockedDoors.Contains(door)) { sb.Append($" {o.name} locked ({openAngle:0} deg)"); continue; }
                if (openAngle >= FullyOpen - 1f) { sb.Append($" {o.name} {openAngle:0} deg kept"); continue; }
                var n = alongX ? Vector3.forward : Vector3.right;
                if (Vector3.Dot(d, n) < 0f) n = -n;
                float delta = Vector3.SignedAngle(d, n, Vector3.up);
                t.RotateAround(hinge, Vector3.up, delta);
                sb.Append($" {o.name} {openAngle:0}->{FullyOpen:0} deg");
            }
            return sb.ToString();
        }

        static Vector3 V(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);
    }
}
