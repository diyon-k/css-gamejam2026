using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using B = Blink.EditorTools.BungalowLightingBuilder;

namespace Blink.EditorTools
{
    /// <summary>
    /// Bungalow atmosphere (SPEC 2, 3.0, 5): the basement flood plane (M_Water at y -2.77, only if the level doesn't
    /// already ship one) and the mesh decals (M_Decals atlas cells; placements resolved on the layout's wall / floor /
    /// ceiling planes by tools/blender/bungalow_decals.py). Decals are merged into one mesh per room, lightmap-static
    /// with shadow casting off, so they bake with the same light as the surface under them and cost a draw per room.
    /// Called by <see cref="BungalowLightingBuilder.Build"/> under its "Lighting" root.
    /// </summary>
    public static class BungalowAtmosphereBuilder
    {
        const string DecalMatPath = B.ArtRoot + "Materials/M_Decals.mat";
        const string WaterMatPath = B.ArtRoot + "Materials/M_Water.mat";
        const string AtlasPath = B.ArtRoot + "Textures/M_Decals/M_Decals_atlas.json";
        const string MeshDir = B.LightingDir + "/Meshes";

        public static string Build(Scene scene, Transform root, B.Layout layout, B.Atmosphere atmo)
        {
            var parent = B.Child(root, "Atmosphere");
            var sb = new StringBuilder();
            sb.Append(BuildWater(scene, parent, layout, atmo));
            sb.Append("; ");
            sb.Append(BuildDecals(parent, atmo));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ water
        static string BuildWater(Scene scene, Transform parent, B.Layout layout, B.Atmosphere atmo)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
            if (mat == null || atmo.water == null) return "water: skipped (no M_Water / data)";

            // The kit shell ships its own flood plane: keep it, but it must not shadow the floor in the bake.
            int shipped = 0;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == B.RootName) continue;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                    if (Array.IndexOf(r.sharedMaterials, mat) >= 0)
                    {
                        MakeTransparentNonGI(r);
                        shipped++;
                    }
            }
            if (shipped > 0) return $"water: level ships {shipped} M_Water plane(s), set to probe-lit / no shadows";

            var rooms = new Dictionary<string, B.Room>();
            foreach (var r in layout.rooms) rooms[r.id] = r;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var id in atmo.water.rooms)
            {
                if (!rooms.TryGetValue(id, out var r)) continue;
                float y = atmo.water.y;
                Quad(verts, uvs, tris, new Vector3(r.min[0], y, r.min[2]), new Vector3(r.max[0], y, r.max[2]));
            }
            var mesh = MeshAsset("Water", verts, uvs, tris, null, null);
            var w = new GameObject("Water", typeof(MeshFilter), typeof(MeshRenderer));
            w.transform.SetParent(parent, false);
            w.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = w.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            MakeTransparentNonGI(mr);
            return $"water: plane at y {atmo.water.y} over {string.Join("+", atmo.water.rooms)}";
        }

        static void MakeTransparentNonGI(MeshRenderer r)
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags & ~StaticEditorFlags.ContributeGI);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveGI = ReceiveGI.LightProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        }

        static void Quad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 mn, Vector3 mx)
        {
            int i = v.Count;
            v.Add(new Vector3(mn.x, mn.y, mn.z));
            v.Add(new Vector3(mn.x, mn.y, mx.z));
            v.Add(new Vector3(mx.x, mn.y, mx.z));
            v.Add(new Vector3(mx.x, mn.y, mn.z));
            foreach (var p in v.GetRange(i, 4)) uv.Add(new Vector2(p.x, p.z)); // metric; the material tiles 4 m
            t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        // ------------------------------------------------------------------ decals
        static string BuildDecals(Transform parent, B.Atmosphere atmo)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(DecalMatPath);
            if (mat == null || atmo.decals == null || !File.Exists(AtlasPath)) return "decals: skipped (no M_Decals / atlas / data)";
            var cells = ReadAtlas(File.ReadAllText(AtlasPath));
            var root = B.Child(parent, "Decals");

            var byRoom = new Dictionary<string, List<B.Decal>>();
            int missing = 0;
            foreach (var d in atmo.decals)
            {
                if (!cells.ContainsKey(d.cell)) { missing++; continue; }
                if (!byRoom.TryGetValue(d.room, out var list)) byRoom[d.room] = list = new List<B.Decal>();
                list.Add(d);
            }

            int quads = 0;
            foreach (var kv in byRoom)
            {
                var v = new List<Vector3>();
                var uv = new List<Vector2>();
                var n = new List<Vector3>();
                var tg = new List<Vector4>();
                var t = new List<int>();
                foreach (var d in kv.Value)
                {
                    var rect = cells[d.cell];
                    if (d.sub >= 0 && d.sub < 4) // one quarter of a 2 x 2 cell (paper_scraps)
                        rect = new Rect(rect.x + (d.sub % 2) * rect.width * 0.5f, rect.y + (d.sub / 2) * rect.height * 0.5f,
                                        rect.width * 0.5f, rect.height * 0.5f);
                    Vector3 c = B.V(d.c), nn = B.V(d.n), r = B.V(d.r) * (d.size[0] * 0.5f), up = B.V(d.v) * (d.size[1] * 0.5f);
                    int i = v.Count;
                    v.Add(c - r - up); uv.Add(new Vector2(rect.xMin, rect.yMin));
                    v.Add(c - r + up); uv.Add(new Vector2(rect.xMin, rect.yMax));
                    v.Add(c + r + up); uv.Add(new Vector2(rect.xMax, rect.yMax));
                    v.Add(c + r - up); uv.Add(new Vector2(rect.xMax, rect.yMin));
                    for (int k = 0; k < 4; k++)
                    {
                        n.Add(nn);
                        tg.Add(new Vector4(d.r[0], d.r[1], d.r[2], -1f)); // bitangent = cross(n, r) * -1 = up
                    }
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                    quads++;
                }
                var mesh = MeshAsset("Decals_" + kv.Key, v, uv, t, n, tg);
                var go = new GameObject("Decals_" + kv.Key, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveGI = ReceiveGI.Lightmaps;
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(go, B.GIStatic);
            }
            return $"decals: {quads} quads in {byRoom.Count} room meshes" + (missing > 0 ? $", {missing} with unknown cells" : "");
        }

        /// <summary>Cell name -> Unity UV rect from M_Decals_atlas.json ("cells": { name: { "uv_rect_unity": [u, v, w, h] } }).</summary>
        static Dictionary<string, Rect> ReadAtlas(string json)
        {
            var d = new Dictionary<string, Rect>();
            var rx = new Regex("\"(\\w+)\"\\s*:\\s*\\{[^{}]*?\"uv_rect_unity\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.Singleline);
            foreach (Match m in rx.Matches(json))
            {
                var p = m.Groups[2].Value.Split(',');
                if (p.Length != 4) continue;
                float f(int i) => float.Parse(p[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                d[m.Groups[1].Value] = new Rect(f(0), f(1), f(2), f(3));
            }
            return d;
        }

        /// <summary>Creates or refills a mesh asset in Lighting/Meshes, with generated lightmap UVs (UV2).</summary>
        static Mesh MeshAsset(string name, List<Vector3> v, List<Vector2> uv, List<int> t, List<Vector3> n, List<Vector4> tg)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(B.LightingDir, "Meshes");
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            if (n != null) mesh.SetNormals(n); else mesh.RecalculateNormals();
            if (tg != null) mesh.SetTangents(tg); else mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var settings = new UnwrapParam();
            UnwrapParam.SetDefaults(out settings);
            settings.packMargin = 0.02f;
            Unwrapping.GenerateSecondaryUVSet(mesh, settings);
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }
    }
}
