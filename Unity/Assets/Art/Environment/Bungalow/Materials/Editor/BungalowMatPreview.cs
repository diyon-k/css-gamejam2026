using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// Phase 4 material checkpoint helpers (temporary preview, not the shipping scene):
    ///  * BuildPreviewScene: Assets/Scenes/House_Bungalow_MatPreview.unity = a copy of the grey-box scene where every
    ///    grey-box face gets its SPEC section-2 material. Faces are split per room (a wall face takes the material of
    ///    the room it faces), plus preview point lights at the SPEC fixture positions (realtime, no GI) and a water
    ///    plane in the basement. House_Bungalow.unity itself is never opened for writing.
    ///  * RenderMatchShots / RenderSwatches / RenderCompare: PNGs for the checkpoint (neutral stage far from the house).
    /// Every entry point restores the scene that was open before, and refuses to run if it has unsaved changes.
    /// </summary>
    public static class BungalowMatPreview
    {
        const string SourceScene = "Assets/Scenes/House_Bungalow.unity";
        public const string PreviewScene = "Assets/Scenes/House_Bungalow_MatPreview.unity";
        const string Root = "Assets/Art/Environment/Bungalow/";
        const string MatRoot = Root + "Materials/";
        const string LayoutPath = Root + "bungalow_layout.json";
        const string FbxPath = Root + "Greybox/Bungalow_Greybox.fbx";
        static readonly Vector3 Stage = new Vector3(200f, 0f, 0f);

#pragma warning disable 0649
        [Serializable] class Room { public string id; public float[] min; public float[] max; }
        [Serializable] class Fixture { public string id; public string kind; public float[] pos; public string color; public float intensity; public float range; public string mode; }
        [Serializable] class Cam { public string id; public float[] pos; public float yaw; public float pitch; public float fov; public int w; public int h; }
        [Serializable] class Layout { public Room[] rooms; public Fixture[] fixtures; public Cam[] cameras; }
#pragma warning restore 0649

        static Layout LoadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(MatRoot + n + ".mat");

        // ------------------------------------------------------------------ scene switching (always restored)
        static T InScene<T>(string path, Func<Scene, T> body)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play mode");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("unsaved changes in " + SceneManager.GetSceneAt(i).path);
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;    // no cyan placeholders in the renders
                var sc = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return body(sc);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        // ------------------------------------------------------------------ preview scene
        [MenuItem("BLINK/Bungalow/Build Material Preview Scene")]
        static void BuildMenu() => Debug.Log(BuildPreviewScene());

        public static string BuildPreviewScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScene) == null)
                if (!AssetDatabase.CopyAsset(SourceScene, PreviewScene)) throw new Exception("copy failed");
            return InScene(PreviewScene, sc =>
            {
                var log = new StringBuilder();
                var layout = LoadLayout();
                AssignMaterials(layout, log);
                PreviewLights(layout, log);
                WaterPlane(log);
                foreach (var go in sc.GetRootGameObjects())
                    if (go.name == "TEMP_GreyboxLight") { UnityEngine.Object.DestroyImmediate(go); log.AppendLine("removed TEMP_GreyboxLight"); }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.039f, 0.043f, 0.031f);    // #0A0B08 (SPEC 5)
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = new Color(0.078f, 0.078f, 0.031f);        // #141408
                RenderSettings.fogDensity = 0.15f;
                RenderSettings.reflectionIntensity = 0.3f;
                EditorSceneManager.MarkSceneDirty(sc);
                EditorSceneManager.SaveScene(sc);
                return log.ToString();
            });
        }

        static string RoomAt(Layout L, Vector3 p)
        {
            foreach (var r in L.rooms)
                if (p.x > r.min[0] && p.x < r.max[0] && p.z > r.min[2] && p.z < r.max[2] &&
                    p.y > r.min[1] - 0.3f && p.y < r.max[1] + 0.3f) return r.id;
            return null;
        }

        static string WallMat(string room, Vector3 c)
        {
            switch (room)
            {
                case "EntranceHall": case "CentralHall": return "M_Wall_Pale";
                case "Living": return "M_Wall_Damask";
                case "Bedroom": return "M_Wall_Floral";
                case "Kitchen": return "M_Plaster";
                case "Bathroom": return c.y < 1.2f ? "M_Tile" : "M_Plaster";
                case "B1": case "B2": return "M_BrickWhite";
                default: return c.y < -0.3f ? "M_BrickWhite" : "M_Plaster";
            }
        }

        static string FloorMat(string obj)
        {
            if (obj.Contains("Living")) return "M_Parquet";
            if (obj.Contains("Kitchen") || obj.Contains("Bathroom")) return "M_Lino";
            if (obj.Contains("B1") || obj.Contains("B2")) return "M_Concrete";
            return "M_FloorPlank";
        }

        /// <summary>Material for one triangle (world centroid c, world normal n), or null to keep the original.</summary>
        static string Pick(Layout L, string obj, string orig, Vector3 c, Vector3 n)
        {
            switch (orig)
            {
                case "GB_Glass": return "M_Glass";
                case "GB_Trim": case "GB_Gate": return "M_Trim";
                case "GB_Stair": return Mathf.Abs(n.y) > 0.5f ? "M_FloorPlank" : "M_Trim";
                case "GB_Brick": return "M_BrickWhite";
                case "GB_Ceiling": return "M_Plaster";
                case "GB_Ground": return null;
            }
            if (obj.StartsWith("Floor_"))
                return n.y > 0.5f ? FloorMat(obj) : "M_Plaster";
            if (obj.StartsWith("Fireplace_Hearth") || obj.StartsWith("Ext_")) return "M_Concrete";
            if (Mathf.Abs(n.y) > 0.5f) return n.y > 0 && c.y < -2.5f ? "M_Concrete" : "M_Plaster";
            return WallMat(RoomAt(L, c + n * 0.25f), c);
        }

        static Mesh SourceMesh(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
                if (o is Mesh m && m.name == name) return m;
            return null;
        }

        static void AssignMaterials(Layout L, StringBuilder log)
        {
            var origMats = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Material>().ToDictionary(m => m.name);
            int objs = 0, tris = 0;
            var use = new Dictionary<string, int>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var srcName = mf.sharedMesh.name.Replace(" (preview)", "");
                var src = SourceMesh(srcName);
                if (src == null) continue;                       // not part of the grey-box
                // original GB_* material per submesh, from the renderer's import-time slots
                var slotNames = r.sharedMaterials.Select(m => m == null ? "" : m.name).ToArray();
                if (slotNames.Any(s => s.StartsWith("M_"))) slotNames = RestoreOrig(r, src, origMats);
                var verts = src.vertices; var tf = r.transform;
                var byMat = new Dictionary<string, List<int>>();
                var order = new List<string>();
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    var orig = s < slotNames.Length ? slotNames[s] : "";
                    var t = src.GetTriangles(s);
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        Vector3 a = tf.TransformPoint(verts[t[i]]), b = tf.TransformPoint(verts[t[i + 1]]),
                                cc = tf.TransformPoint(verts[t[i + 2]]);
                        var c = (a + b + cc) / 3f;
                        var n = Vector3.Cross(b - a, cc - a).normalized;
                        var key = Pick(L, r.name, orig, c, n) ?? ("keep:" + orig);
                        if (!byMat.TryGetValue(key, out var list)) { byMat[key] = list = new List<int>(); order.Add(key); }
                        list.Add(t[i]); list.Add(t[i + 1]); list.Add(t[i + 2]);
                        tris++;
                    }
                }
                // rebuild with per-material vertex copies; the grey-box UV0 is metric, the kit contract is
                // 1 UV = 1 tile (SurfaceTileM), so divide UV0 by the tile of each face's material
                var nrm = src.normals; var uv0 = src.uv; var uv1 = src.uv2;
                var V = new List<Vector3>(); var N = new List<Vector3>(); var T0 = new List<Vector2>(); var T1 = new List<Vector2>();
                var subs = new List<List<int>>();
                var mats = new Material[order.Count];
                for (int k = 0; k < order.Count; k++)
                {
                    bool keep = order[k].StartsWith("keep:");
                    mats[k] = keep ? (origMats.TryGetValue(order[k].Substring(5), out var om) ? om : null) : M(order[k]);
                    float tile = !keep && BungalowMaterialsBuilder.SurfaceTileM.TryGetValue(order[k], out var tt) ? tt : 1f;
                    var remap = new Dictionary<int, int>(); var idx = new List<int>();
                    foreach (var vi in byMat[order[k]])
                    {
                        if (!remap.TryGetValue(vi, out var ni))
                        {
                            ni = V.Count; remap[vi] = ni;
                            V.Add(verts[vi]); N.Add(nrm.Length > vi ? nrm[vi] : Vector3.up);
                            T0.Add(uv0.Length > vi ? uv0[vi] / tile : Vector2.zero);
                            T1.Add(uv1.Length > vi ? uv1[vi] : Vector2.zero);
                        }
                        idx.Add(ni);
                    }
                    subs.Add(idx);
                    use[order[k]] = (use.TryGetValue(order[k], out var u) ? u : 0) + idx.Count / 3;
                }
                var mesh = new Mesh { name = srcName + " (preview)", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, T0); mesh.SetUVs(1, T1);
                mesh.subMeshCount = subs.Count;
                for (int k = 0; k < subs.Count; k++) mesh.SetTriangles(subs[k], k);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                mf.sharedMesh = mesh;
                r.sharedMaterials = mats;
                objs++;
            }
            log.AppendLine($"preview materials on {objs} grey-box objects, {tris} tris");
            foreach (var kv in use.OrderBy(k => k.Key)) log.AppendLine($"  {kv.Key}: {kv.Value} tris");
        }

        static string[] RestoreOrig(MeshRenderer r, Mesh src, Dictionary<string, Material> origMats)
        {
            // re-run on an already converted scene: take the import-time slots from the model prefab
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            foreach (var mr in model.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.name == r.name) return mr.sharedMaterials.Select(m => m == null ? "" : m.name).ToArray();
            return new string[src.subMeshCount];
        }

        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.white;

        static void PreviewLights(Layout L, StringBuilder log)
        {
            var old = GameObject.Find("PREVIEW_Lights");
            if (old) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject("PREVIEW_Lights");
            int n = 0;
            foreach (var f in L.fixtures)
            {
                if (f.intensity <= 0f || string.IsNullOrEmpty(f.color)) continue;
                var go = new GameObject("PL_" + f.id);
                go.transform.SetParent(root.transform, false);
                var p = new Vector3(f.pos[0], f.pos[1], f.pos[2]);
                var lt = go.AddComponent<Light>();
                lt.color = Hex(f.color);
                if (f.kind == "area")
                {
                    // window light: realtime spot standing in for the baked area light, pointing into the room
                    var inward = p.x < 1f ? Vector3.right : p.x > 19f ? Vector3.left : p.z < 1f ? Vector3.forward : Vector3.back;
                    go.transform.position = p + inward * 0.3f;
                    go.transform.rotation = Quaternion.LookRotation(inward + Vector3.down * 0.35f);
                    lt.type = LightType.Spot; lt.spotAngle = 120f; lt.innerSpotAngle = 60f;
                    lt.range = 7f; lt.intensity = f.intensity * 2.5f;
                }
                else
                {
                    go.transform.position = p;
                    lt.type = LightType.Point; lt.range = f.range; lt.intensity = f.intensity;
                }
                lt.shadows = (f.id == "H4" || f.id == "L2" || f.id == "C1" || f.id == "L1" || f.id == "R1") ? LightShadows.Soft : LightShadows.None;
                lt.shadowStrength = 0.6f;
                lt.lightmapBakeType = LightmapBakeType.Realtime;
                n++;
            }
            log.AppendLine($"preview lights: {n} realtime (SPEC 5.1 positions/colours/intensities; no GI baked)");
        }

        static void WaterPlane(StringBuilder log)
        {
            var old = GameObject.Find("PREVIEW_Water");
            if (old) UnityEngine.Object.DestroyImmediate(old);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "PREVIEW_Water";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = new Vector3(13.4f, -2.77f, 9.4f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(12.6f, 10.6f, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = M("M_Water");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            log.AppendLine("water plane at y -2.77 over B1/B2");
        }

        // ------------------------------------------------------------------ rendering
        static Camera TempCam(Transform parent)
        {
            var go = new GameObject("TEMP_ShotCam");
            if (parent) go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 200f;
            cam.allowHDR = true;
            return cam;
        }

        static void Shoot(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }

        /// <summary>CAM-1/2/3 from the layout, in the preview scene. extraAmbient lifts the flat ambient so the
        /// materials can be judged without baked bounce light (0 = SPEC ambient only).</summary>
        public static string RenderMatchShots(string outDir, float extraAmbient)
        {
            return InScene(PreviewScene, sc =>
            {
                var L = LoadLayout();
                var amb = RenderSettings.ambientLight;
                RenderSettings.ambientLight = amb + new Color(extraAmbient, extraAmbient * 0.97f, extraAmbient * 0.9f);
                var cam = TempCam(null);
                cam.backgroundColor = Color.black;
                var sb = new StringBuilder();
                try
                {
                    var c0 = L.cameras[0];                       // warm-up: the first render after a scene open
                    cam.transform.SetPositionAndRotation(new Vector3(c0.pos[0], c0.pos[1], c0.pos[2]),
                                                         Quaternion.Euler(-c0.pitch, c0.yaw, 0f));
                    Shoot(cam, Path.Combine(outDir, "_warmup.png"), 64, 64);   // can come back black (URP init)
                    File.Delete(Path.Combine(outDir, "_warmup.png"));
                    foreach (var c in L.cameras)
                    {
                        cam.transform.SetPositionAndRotation(new Vector3(c.pos[0], c.pos[1], c.pos[2]),
                                                             Quaternion.Euler(-c.pitch, c.yaw, 0f));
                        cam.fieldOfView = c.fov;
                        var f = Path.Combine(outDir, $"{c.id}_matpreview{(extraAmbient > 0 ? "_lifted" : "")}.png");
                        Shoot(cam, f, c.w, c.h);
                        sb.AppendLine(f);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cam.gameObject);
                    RenderSettings.ambientLight = amb;
                }
                return sb.ToString();
            });
        }

        /// <summary>Neutral stage: one white directional light (intensity 1, no shadows) + flat ambient (linear
        /// 0.08), no fog, no environment reflections, no post. Mirrors tools/blender/bungalow_materials_compare.py.</summary>
        static T NeutralStage<T>(Func<Transform, Camera, T> body)
        {
            var fog = RenderSettings.fog; var amb = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
            var refl = RenderSettings.reflectionIntensity;
            var root = new GameObject("TEMP_NeutralStage");
            root.transform.position = Stage;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.08f, 0.08f, 0.08f).gamma;
                RenderSettings.reflectionIntensity = 0f;
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.transform.SetParent(root.transform, false);
                sun.type = LightType.Directional; sun.intensity = 1f; sun.color = Color.white;
                sun.shadows = LightShadows.None;
                sun.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
                // the house's own lights are far away (>180 m) but switch them off anyway
                var others = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Where(l => l != sun && l.enabled).ToList();
                foreach (var l in others) l.enabled = false;
                try
                {
                    var cam = TempCam(root.transform);
                    cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
                    return body(root.transform, cam);
                }
                finally { foreach (var l in others) l.enabled = true; }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                RenderSettings.fog = fog; RenderSettings.ambientLight = amb; RenderSettings.ambientMode = mode;
                RenderSettings.reflectionIntensity = refl;
            }
        }

        /// <summary>Axis-aligned quad, UV0 = metres / tile (the kit's UV contract), with tangents. axis: "floor" (XZ) or "wall" (XY, facing -Z).</summary>
        static float Tile(Material mat) =>
            mat != null && BungalowMaterialsBuilder.SurfaceTileM.TryGetValue(mat.name, out var t) ? t : 1f;

        static GameObject Quad(Transform parent, string name, Vector3 center, Vector2 size, bool floor, Material mat,
                               bool unitUV = false)
        {
            float tile = Tile(mat);
            var m = new Mesh { name = name };
            float hx = size.x / 2, hy = size.y / 2;
            Vector3[] v; Vector2[] uv;
            if (floor)
            {
                v = new[] { new Vector3(-hx, 0, -hy), new Vector3(hx, 0, -hy), new Vector3(hx, 0, hy), new Vector3(-hx, 0, hy) };
                uv = v.Select(p => new Vector2(p.x + center.x, p.z + center.z) / tile).ToArray();
            }
            else
            {
                v = new[] { new Vector3(-hx, -hy, 0), new Vector3(hx, -hy, 0), new Vector3(hx, hy, 0), new Vector3(-hx, hy, 0) };
                uv = v.Select(p => new Vector2(p.x + center.x, p.y + center.y) / tile).ToArray();
            }
            if (unitUV) uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.vertices = v; m.uv = uv;
            m.triangles = new[] { 0, 3, 2, 0, 2, 1 };   // clockwise = front: floor faces +Y, wall faces -Z
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Blender-vs-Unity sample: wall 2.4 x 1.8 (z = +1, facing the camera) on a 2.4 x 2.0 floor.</summary>
        public static string RenderCompare(string outDir, string[] materials)
        {
            return InScene(PreviewScene, sc => NeutralStage((root, cam) =>
            {
                var sb = new StringBuilder();
                foreach (var name in materials)
                {
                    var mat = M(name);
                    var set = new GameObject("set"); set.transform.SetParent(root, false);
                    Quad(set.transform, "wall", new Vector3(0f, 0.9f, 1.0f), new Vector2(2.4f, 1.8f), false, mat);
                    Quad(set.transform, "floor", new Vector3(0f, 0f, 0f), new Vector2(2.4f, 2.0f), true, mat);
                    cam.transform.localPosition = new Vector3(0f, 1.2f, -1.6f);
                    cam.transform.LookAt(root.TransformPoint(new Vector3(0f, 0.55f, 0.6f)));
                    cam.fieldOfView = 50f;
                    var f = Path.Combine(outDir, $"compare_unity_{name}.png");
                    Shoot(cam, f, 800, 600);
                    sb.AppendLine(f + " camEuler=" + cam.transform.eulerAngles.ToString("F3"));
                    UnityEngine.Object.DestroyImmediate(set);
                }
                return sb.ToString();
            }));
        }

        /// <summary>Checks the SG wear path: one floor panel per strip, uv2 (TEXCOORD1, the wear/lightmap layout) mapped across a synthetic
        /// in-memory 1024 wear atlas with 5 vertical bands: neutral | AO 0.25 | edge 1 | dirt 1 | polish 1.
        /// Rendered with the atlas assigned and with the slot empty (must be uniform = neutral).</summary>
        public static string RenderWearTest(string outDir, string material)
        {
            return InScene(PreviewScene, sc => NeutralStage((root, cam) =>
            {
                var atlas = new Texture2D(1024, 1024, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[1024 * 1024];
                for (int y = 0; y < 1024; y++)
                    for (int x = 0; x < 1024; x++)
                    {
                        int band = x * 5 / 1024;
                        px[y * 1024 + x] = band == 1 ? new Color32(64, 0, 0, 0) : band == 2 ? new Color32(255, 255, 0, 0)
                                         : band == 3 ? new Color32(255, 0, 255, 0) : band == 4 ? new Color32(255, 0, 0, 255)
                                         : new Color32(255, 0, 0, 0);
                    }
                atlas.SetPixels32(px); atlas.Apply(true);
                var mat = new Material(M(material));
                var sb = new StringBuilder();
                var q = Quad(root, "panel", new Vector3(0f, 0.75f, 0.6f), new Vector2(2.0f, 1.5f), false, mat);
                var mesh = q.GetComponent<MeshFilter>().sharedMesh;
                mesh.SetUVs(1, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                cam.transform.localPosition = new Vector3(0f, 0.75f, -1.3f);
                cam.transform.LookAt(root.TransformPoint(new Vector3(0f, 0.75f, 0.6f)));
                cam.fieldOfView = 50f;
                foreach (var on in new[] { true, false })
                {
                    mat.SetTexture("_WearMap", on ? atlas : null);
                    var f = Path.Combine(outDir, $"weartest_{material}_{(on ? "atlas" : "empty")}.png");
                    Shoot(cam, f, 800, 450);
                    sb.AppendLine(f);
                }
                UnityEngine.Object.DestroyImmediate(mat); UnityEngine.Object.DestroyImmediate(atlas);
                return sb.ToString();
            }));
        }

        /// <summary>One swatch per material: a 1.5 m panel (metric UVs) behind a sphere, under the neutral stage.</summary>
        public static string RenderSwatches(string outDir, string[] materials)
        {
            return InScene(PreviewScene, sc => NeutralStage((root, cam) =>
            {
                var sb = new StringBuilder();
                var backdrop = Quad(root, "backdrop", new Vector3(0f, 1.0f, 1.2f), new Vector2(6f, 4f), false, null);
                var checker = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                checker.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.35f));
                backdrop.GetComponent<MeshRenderer>().sharedMaterial = checker;
                foreach (var name in materials)
                {
                    var mat = M(name);
                    var set = new GameObject("set"); set.transform.SetParent(root, false);
                    bool atlas = name == "M_Decals" || name == "M_Trim";
                    var panel = Quad(set.transform, "panel", new Vector3(0f, 0.75f, 0.6f), new Vector2(1.5f, 1.5f), false, mat, atlas);
                    var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.DestroyImmediate(ball.GetComponent<Collider>());
                    ball.transform.SetParent(set.transform, false);
                    ball.transform.localPosition = new Vector3(0.35f, 0.42f, 0.1f);
                    ball.transform.localScale = Vector3.one * 0.55f;
                    ball.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    cam.transform.localPosition = new Vector3(0f, 0.8f, -1.55f);
                    cam.transform.LookAt(root.TransformPoint(new Vector3(0f, 0.72f, 0.5f)));
                    cam.fieldOfView = 45f;
                    var f = Path.Combine(outDir, $"swatch_{name}.png");
                    Shoot(cam, f, 512, 512);
                    sb.AppendLine(f);
                    UnityEngine.Object.DestroyImmediate(set);
                }
                UnityEngine.Object.DestroyImmediate(checker);
                return sb.ToString();
            }));
        }
    }
}
