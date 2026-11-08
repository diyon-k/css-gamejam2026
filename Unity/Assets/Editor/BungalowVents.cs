using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Blink.Interaction;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Blink.EditorTools
{
    /// <summary>
    /// The crawl vents (bungalow_layout.VENTS) in the OPEN scene, after the shell (with its Vent_*_Duct pieces) and the
    /// FloorGrate prefab are imported:
    ///   1. props: instantiates bungalow_props.json entries the scene's Props root lacks (the grates) and moves the ones
    ///      whose position changed (the cellar crate), without re-placing the rest (that would drop the start-bedroom setup);
    ///   2. ducts: the shell's new Vent_*_Duct children get the shell's static flags (lightmapped like the rest of it);
    ///   3. a "Vents" root with one <see cref="Vent"/> per route and a trigger box per end (at the scene root, not under the
    ///      NavMeshSurface, so the boxes stay out of the navmesh);
    /// The duct light probes come from BungalowLightingBuilder (layout.ducts). Then bake the navmesh and the lighting as usual (BLINK/Bungalow/Build Lighting, bake, normalise, bake).
    /// </summary>
    public static class BungalowVents
    {
        const string Root = "Assets/Art/Environment/Bungalow/";
        const string LayoutJson = Root + "bungalow_layout.json";
        const string PropsJson = Root + "Props/bungalow_props.json";
        const string PrefabDir = Root + "Props/Prefabs/";
        const string ScenePath = "Assets/Scenes/House_Bungalow.unity";
        const string RootName = "Vents";

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class End { public string room; public float[] peek, exit, min, max; }
        [Serializable] class VentData { public string id; public float[] path; public End[] ends; }
        [Serializable] class Layout { public VentData[] vents; }
        [Serializable] class Prop { public string id, prefab, room, fixture; public float[] pos, rot, scale; }
        [Serializable] class PropFile { public Prop[] props; }
#pragma warning restore 0649

        [MenuItem("BLINK/Bungalow/Set Up Vents")]
        static void Menu()
        {
            var scene = SceneManager.GetActiveScene();
            Debug.Log(SetUp(scene));
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Batch: Unity -batchmode -quit -executeMethod Blink.EditorTools.BungalowVents.Batch. Builds the grate prefab,
        /// sets the vents up in House_Bungalow, rebakes the navmesh, rebuilds and bakes the lighting (twice, density normalised
        /// between, as SPEC 5.4), saves.</summary>
        public static void Batch()
        {
            var log = new StringBuilder();
            log.AppendLine(BungalowPropsPlacer.BuildAssets());
            var scene = EditorSceneManager.OpenScene(ScenePath);
            log.AppendLine(SetUp(scene));
            var surface = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None).FirstOrDefault();
            if (surface != null) log.AppendLine(BungalowGreyboxBuilder.Bake(surface));
            log.AppendLine(BungalowLightingBuilder.Build(scene, false));
            EditorSceneManager.SaveScene(scene);
            // ponytail: one bake, not SPEC 5.4's bake / normalise / bake; run NormalizeLightmapDensity + a second bake if the duct or grates look off-density.
            Lightmapping.Bake();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BungalowVents] " + log);
        }

        public static string SetUp(Scene scene)
        {
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutJson));
            var report = new StringBuilder();
            report.AppendLine(SyncProps(scene));

            // ducts: new FBX children come in without the static flags the shell scene build gave the rest
            var shell = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                             .FirstOrDefault(t => t.name == "Shell");
            int ducts = 0;
            if (shell != null)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(shell.gameObject);
                foreach (var t in shell.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_Duct", StringComparison.Ordinal)))
                {
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                    if (t.TryGetComponent<MeshRenderer>(out var r)) r.receiveGI = ReceiveGI.Lightmaps;
                    ducts++;
                }
            }
            report.AppendLine(shell == null ? "no Shell in the scene: ducts not flagged" : $"{ducts} duct pieces flagged static");

            foreach (var g in scene.GetRootGameObjects()) if (g.name == RootName) Object.DestroyImmediate(g);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var v in layout.vents)
            {
                var go = new GameObject(v.id);
                go.transform.SetParent(root.transform, false);
                var vent = go.AddComponent<Vent>();
                vent.path = Points(v.path);
                vent.peek = v.ends.Select(e => V(e.peek)).ToArray();
                vent.exit = v.ends.Select(e => V(e.exit)).ToArray();
                for (int k = 0; k < v.ends.Length; k++)
                {
                    var end = new GameObject($"End{k}_{v.ends[k].room}");
                    end.transform.SetParent(go.transform, false);
                    Vector3 mn = V(v.ends[k].min), mx = V(v.ends[k].max);
                    end.transform.position = (mn + mx) * 0.5f;
                    var box = end.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = mx - mn;
                }
            }
            report.AppendLine($"{layout.vents.Length} vents under {RootName}");

            EditorSceneManager.MarkSceneDirty(scene);
            return report.ToString();
        }

        static string SyncProps(Scene scene)
        {
            var data = JsonUtility.FromJson<PropFile>(File.ReadAllText(PropsJson));
            var propsRoot = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                                 .FirstOrDefault(t => t.name == "Props" && t.Find("Bedroom") != null);
            if (propsRoot == null) return "no Props root: run BLINK/Bungalow/Place Props first";
            int added = 0, moved = 0, removed = 0;
            // vent grates the layout no longer has (an end moved to another room or went away)
            var want = new HashSet<string>(data.props.Select(p => (string.IsNullOrEmpty(p.room) ? "Misc" : p.room) + "/" + p.id));
            foreach (var t in propsRoot.GetComponentsInChildren<Transform>(true).Where(t => t.parent != null && t.parent.parent == propsRoot
                         && t.name.StartsWith("Vent_", StringComparison.Ordinal) && !want.Contains(t.parent.name + "/" + t.name)).ToArray())
            {
                Object.DestroyImmediate(t.gameObject);
                removed++;
            }
            foreach (var p in data.props)
            {
                string group = string.IsNullOrEmpty(p.fixture) ? (string.IsNullOrEmpty(p.room) ? "Misc" : p.room) : "Fixtures";
                var parent = propsRoot.Find(group);
                if (parent == null) continue;
                var pos = V(p.pos);
                var rot = Quaternion.Euler(V(p.rot));
                var existing = parent.Find(p.id);
                if (existing != null)
                {
                    if ((existing.position - pos).sqrMagnitude > 1e-6f || Quaternion.Angle(existing.rotation, rot) > 0.1f)
                    {
                        existing.SetPositionAndRotation(pos, rot);
                        moved++;
                    }
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + p.prefab + ".prefab");
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = p.id;
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(pos, rot);
                if (p.scale != null && p.scale.Length == 3) go.transform.localScale = V(p.scale);
                added++;
            }
            return $"props: {added} added, {moved} moved, {removed} stale vent grates removed";
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        static Vector3[] Points(float[] flat) =>
            Enumerable.Range(0, flat.Length / 3).Select(i => new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2])).ToArray();
    }
}
