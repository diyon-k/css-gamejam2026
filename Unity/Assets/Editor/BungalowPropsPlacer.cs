using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Blink.EditorTools
{
    /// <summary>
    /// Bungalow Phase 5: props and light-fixture meshes.
    ///   BLINK/Bungalow/Build Prop Assets          importer settings for Props/*.fbx and Textures/Atlas_*, the four
    ///                                               M_Atlas_* materials, and one prefab per model in Props/Prefabs/.
    ///   BLINK/Bungalow/Place Props                 instantiates Props/bungalow_props.json into the OPEN scene under a
    ///                                               "Props" root (replaced on every run; parented under the scene's
    ///                                               NavMeshSurface when it collects children, so a bake carves round
    ///                                               the furniture). Fixtures go under Props/Fixtures as FX_&lt;id&gt;.
    ///   BLINK/Bungalow/Build Props Preview Scene   Scenes/House_Bungalow_PropsPreview.unity: the shell scene + props +
    ///                                               provisional point lights at the fixture emitters + its own navmesh.
    /// Data: tools/blender/bungalow_props.py (placements), bungalow_prop_build.py (models), bungalow_prop_textures.py.
    /// Never opens or saves House_Bungalow.unity.
    /// </summary>
    public static class BungalowPropsPlacer
    {
        const string Root = "Assets/Art/Environment/Bungalow/";
        const string PropsDir = Root + "Props/";
        const string PrefabDir = PropsDir + "Prefabs/";
        const string ModelsJson = PropsDir + "bungalow_prop_models.json";
        const string PropsJson = PropsDir + "bungalow_props.json";
        const string FixturesJson = PropsDir + "fixtures.json";
        const string LayoutJson = Root + "bungalow_layout.json";
        const string MatDir = Root + "Materials/";
        const string TexDir = Root + "Textures/";
        public const string PreviewScene = "Assets/Scenes/House_Bungalow_PropsPreview.unity";
        const string PreviewNavFolder = "Assets/Scenes/House_Bungalow_PropsPreview";
        const string PreviewNavPath = PreviewNavFolder + "/NavMesh-Props.asset";
        const string RootName = "Props";
        static readonly string[] Atlases = { "Soft", "Wood", "Small", "Utility" };
        const float PropLightmapScale = 0.4f;   // SPEC 5: 8 texels/m on props vs 20 on the kit

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class Model { public string name; public int tris; public string pivot; public string collider; public string kind; public float[] lo; public float[] hi; public float[] emitter; public string emissive; }
        [Serializable] class Prop { public string id; public string prefab; public string room; public float[] pos; public float[] rot; public float[] scale; public string kind; public int tris; public string fixture; public string note; }
        [Serializable] class PropFile { public Prop[] props; }
        [Serializable] class Fixture { public string id; public string instance; public string prefab; public float[] emitterWorld; public bool off; public string mode; }
        [Serializable] class FixtureFile { public Fixture[] fixtures; }
        [Serializable] class LFix { public string id; public string kind; public float[] pos; public string color; public float intensity; public float range; public string mode; }
        [Serializable] class Layout { public LFix[] fixtures; }
#pragma warning restore 0649

        // ------------------------------------------------------------------ assets
        [MenuItem("BLINK/Bungalow/Build Prop Assets")]
        static void BuildAssetsMenu() => Debug.Log(BuildAssets());

        /// <summary>Importer settings, M_Atlas_* materials and the prefabs. Safe to re-run (assets keep their GUIDs).</summary>
        public static string BuildAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            var log = new System.Text.StringBuilder();
            log.AppendLine(SetupTextures());
            var mats = SetupMaterials();
            log.AppendLine("materials: " + string.Join(", ", mats.Keys.ToArray()));
            log.AppendLine(SetupModels(mats));
            log.AppendLine(BuildPrefabs());
            return log.ToString();
        }

        static string SetupTextures()
        {
            int n = 0;
            foreach (var a in Atlases)
            {
                foreach (var suffix in new[] { "Base", "Normal", "Mask" })
                {
                    string p = $"{TexDir}Atlas_{a}/Atlas_{a}_{suffix}.png";
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (ti == null) continue;
                    bool normal = suffix == "Normal", mask = suffix == "Mask", cutout = suffix == "Base" && a == "Small";
                    ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = suffix == "Base";
                    ti.alphaSource = mask || cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                    ti.alphaIsTransparency = false;
                    ti.mipmapEnabled = true;
                    ti.mipMapsPreserveCoverage = cutout;
                    ti.alphaTestReferenceValue = 0.5f;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.anisoLevel = 8;
                    ti.maxTextureSize = 1024;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    var ps = ti.GetPlatformTextureSettings("Standalone");
                    ps.overridden = true;
                    ps.maxTextureSize = 1024;
                    ps.format = TextureImporterFormat.BC7;
                    ti.SetPlatformTextureSettings(ps);
                    ti.SaveAndReimport();
                    n++;
                }
            }
            return $"atlas textures configured: {n} (1024, mips, BC7 Standalone, Base sRGB, Normal map, Mask linear)";
        }

        static Dictionary<string, Material> SetupMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mats = new Dictionary<string, Material>();
            foreach (var a in Atlases)
            {
                string path = $"{MatDir}M_Atlas_{a}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = m == null;
                if (create) m = new Material(lit) { name = "M_Atlas_" + a };
                m.shader = lit;
                var b = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}Atlas_{a}/Atlas_{a}_Base.png");
                var nm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}Atlas_{a}/Atlas_{a}_Normal.png");
                var mk = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}Atlas_{a}/Atlas_{a}_Mask.png");
                m.SetTexture("_BaseMap", b);
                m.SetTexture("_MainTex", b);
                m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", nm);
                m.SetFloat("_BumpScale", 1f);
                m.SetTexture("_MetallicGlossMap", mk);   // R metallic, A smoothness
                m.SetTexture("_OcclusionMap", mk);       // URP reads G
                m.SetFloat("_OcclusionStrength", 1f);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.SetFloat("_WorkflowMode", 1f);
                m.SetFloat("_Surface", 0f);
                m.EnableKeyword("_NORMALMAP");
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.EnableKeyword("_OCCLUSIONMAP");
                bool clip = a == "Small";   // plant leaves and doily lace are alpha-tested
                m.SetFloat("_AlphaClip", clip ? 1f : 0f);
                m.SetFloat("_Cutoff", 0.5f);
                if (clip) { m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout"); }
                else { m.DisableKeyword("_ALPHATEST_ON"); m.renderQueue = -1; m.SetOverrideTag("RenderType", "Opaque"); }
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                m.enableInstancing = false;
                if (create) AssetDatabase.CreateAsset(m, path);
                else EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                mats[m.name] = m;
            }
            foreach (var shared in new[] { "M_Shade", "M_Glass" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + shared + ".mat");
                if (m != null) mats[shared] = m;
            }
            return mats;
        }

        static string SetupModels(Dictionary<string, Material> mats)
        {
            int n = 0;
            foreach (var path in Directory.GetFiles(PropsDir, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                mi.importCameras = false;
                mi.importLights = false;
                mi.importVisibility = false;
                mi.importBlendShapes = false;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.addCollider = false;
                mi.isReadable = false;
                mi.generateSecondaryUV = false;          // UV2 comes from Blender (lib.uv_lightmap)
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var kv in mats)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                mi.SaveAndReimport();
                n++;
            }
            return $"models configured: {n} (no cameras / lights, Generate Lightmap UVs off, materials remapped)";
        }

        static Dictionary<string, Model> LoadModels()
        {
            // JsonUtility can't read a dictionary: pull "name": {...} blocks with a tiny scanner
            var text = File.ReadAllText(ModelsJson);
            var res = new Dictionary<string, Model>();
            int i = text.IndexOf("\"models\"", StringComparison.Ordinal);
            i = text.IndexOf('{', i) + 1;
            while (true)
            {
                int q0 = text.IndexOf('"', i);
                if (q0 < 0) break;
                int q1 = text.IndexOf('"', q0 + 1);
                string key = text.Substring(q0 + 1, q1 - q0 - 1);
                int b0 = text.IndexOf('{', q1);
                int depth = 0, b1 = b0;
                for (; b1 < text.Length; b1++)
                {
                    if (text[b1] == '{') depth++;
                    else if (text[b1] == '}' && --depth == 0) break;
                }
                var m = JsonUtility.FromJson<Model>(text.Substring(b0, b1 - b0 + 1));
                m.name = key;
                res[key] = m;
                i = b1 + 1;
                int next = text.IndexOf('"', i), end = text.IndexOf('}', i);
                if (next < 0 || (end >= 0 && end < next)) break;
            }
            return res;
        }

        static StaticEditorFlags FlagsFor(string kind)
        {
            var common = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            switch (kind)
            {
                case "furniture":
                case "rug":
                case "wall":
                case "fixture":
                    return common | StaticEditorFlags.ContributeGI;   // lightmapped (UV2 from Blender)
                default:
                    return common;                                      // small props / debris: light probes
            }
        }

        // models whose overall bounds are much bigger than what should block the player (leaves, coats)
        static readonly Dictionary<string, Vector3[]> ColliderOverride = new Dictionary<string, Vector3[]>
        {
            { "PlantPot", new[] { new Vector3(-0.19f, 0f, -0.19f), new Vector3(0.19f, 0.95f, 0.19f) } },   // stand + pot
            { "CoatStand", new[] { new Vector3(-0.2f, 0f, -0.2f), new Vector3(0.2f, 1.8f, 0.2f) } },
            { "UmbrellaStand", new[] { new Vector3(-0.12f, 0f, -0.12f), new Vector3(0.12f, 0.55f, 0.12f) } },
        };

        static string BuildPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir.TrimEnd('/'))) AssetDatabase.CreateFolder(PropsDir.TrimEnd('/'), "Prefabs");
            var models = LoadModels();
            int n = 0, boxes = 0, drawers = 0;
            var missing = new List<string>();
            foreach (var m in models.Values)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(PropsDir + m.name + ".fbx");
                if (src == null) { missing.Add(m.name); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                go.name = m.name;
                var flags = FlagsFor(m.kind);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var so = new SerializedObject(r);
                    so.FindProperty("m_ScaleInLightmap").floatValue = PropLightmapScale;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    r.receiveGI = (flags & StaticEditorFlags.ContributeGI) != 0 ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                    // the emissive shade must not block its own light
                    if (r.gameObject.name.EndsWith("_Emissive")) r.shadowCastingMode = ShadowCastingMode.Off;
                }
                if (m.collider == "box")
                {
                    // simple box from the model's bounds (Unity model space, written by the Blender build)
                    var bc = go.AddComponent<BoxCollider>();
                    var lo = new Vector3(m.lo[0], m.lo[1], m.lo[2]);
                    var hi = new Vector3(m.hi[0], m.hi[1], m.hi[2]);
                    if (ColliderOverride.TryGetValue(m.name, out var ov)) { lo = ov[0]; hi = ov[1]; }
                    bc.center = (lo + hi) * 0.5f;
                    bc.size = hi - lo;
                    boxes++;
                }
                drawers += SetupDrawers(go, m);
                PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + m.name + ".prefab");
                UnityEngine.Object.DestroyImmediate(go);
                n++;
            }
            return $"prefabs: {n} in {PrefabDir} ({boxes} with a box collider, {drawers} drawers)" + (missing.Count > 0 ? "; missing fbx: " + string.Join(", ", missing.ToArray()) : "");
        }

        static readonly System.Text.RegularExpressions.Regex DrawerName = new System.Text.RegularExpressions.Regex(@"_Drawer\d+$");
        const float DrawerPull = 0.30f;    // metres out of the front (+Z): about half of a 0.54 m deep drawer
        const float DrawerSeconds = 0.3f;
        const float DrawerProud = 0.01f;   // the drawer collider sticks out past the body box, so the look ray finds it

        /// <summary>
        /// Every "&lt;Model&gt;_Drawer&lt;N&gt;" child (split out by the Blender build, ph(split=...)) becomes a sliding
        /// <see cref="Blink.Interaction.Openable"/> with its own box collider. Drawers aren't static and are lit by light
        /// probes, so they can move. Returns how many it set up.
        /// </summary>
        static int SetupDrawers(GameObject go, Model m)
        {
            int n = 0;
            float bodyFront = m.hi != null && m.hi.Length == 3 ? m.hi[2] : float.NegativeInfinity;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!DrawerName.IsMatch(t.name)) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var r = t.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.receiveGI = ReceiveGI.LightProbes;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                var mf = t.GetComponent<MeshFilter>();
                var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.2f);
                var bc = t.gameObject.AddComponent<BoxCollider>();
                float front = t.localPosition.z + b.max.z;
                float extra = Mathf.Max(0f, bodyFront + DrawerProud - front);
                bc.center = b.center + new Vector3(0f, 0f, extra * 0.5f);
                bc.size = b.size + new Vector3(0f, 0f, extra);
                var o = t.gameObject.AddComponent<Blink.Interaction.Openable>();
                o.motion = Blink.Interaction.Openable.Motion.Sliding;
                o.noun = "drawer";
                o.slide = new Vector3(0f, 0f, DrawerPull);
                o.seconds = DrawerSeconds;
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ placement
        [MenuItem("BLINK/Bungalow/Place Props")]
        static void PlaceMenu() => Debug.Log(Place(SceneManager.GetActiveScene()));

        /// <summary>Instantiates every entry of bungalow_props.json into <paramref name="scene"/> under a fresh
        /// "Props" root (an existing root of that name is replaced). Does not save the scene.</summary>
        public static string Place(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            var data = JsonUtility.FromJson<PropFile>(File.ReadAllText(PropsJson));
            // replace our own root (scene root or under the navmesh surface)
            foreach (var g in scene.GetRootGameObjects())
            {
                if (g.name == RootName) { UnityEngine.Object.DestroyImmediate(g); continue; }
                // ... or directly under a NavMeshSurface (where this placer parents it)
                foreach (var s in g.GetComponentsInChildren<NavMeshSurface>(true))
                    foreach (var t in s.transform.Cast<Transform>().Where(t => t.name == RootName).ToArray())
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
            Transform parent = null;
            foreach (var g in scene.GetRootGameObjects())
            {
                var s = g.GetComponentInChildren<NavMeshSurface>(true);
                if (s != null && s.collectObjects == CollectObjects.Children) { parent = s.transform; break; }
            }
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            if (parent != null) root.transform.SetParent(parent, false);
            var groups = new Dictionary<string, Transform>();
            int placed = 0, tris = 0;
            var missing = new List<string>();
            foreach (var p in data.props)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + p.prefab + ".prefab");
                if (prefab == null) { missing.Add(p.id + "(" + p.prefab + ")"); continue; }
                string g = string.IsNullOrEmpty(p.fixture) ? (string.IsNullOrEmpty(p.room) ? "Misc" : p.room) : "Fixtures";
                if (!groups.TryGetValue(g, out var gt))
                {
                    gt = new GameObject(g).transform;
                    gt.SetParent(root.transform, false);
                    GameObjectUtility.SetStaticEditorFlags(gt.gameObject, FlagsFor("furniture"));
                    groups[g] = gt;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = p.id;
                go.transform.SetParent(gt, false);
                go.transform.SetPositionAndRotation(V(p.pos), Quaternion.Euler(V(p.rot)));
                if (p.scale != null && p.scale.Length == 3) go.transform.localScale = V(p.scale);
                placed++;
                tris += p.tris;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return $"placed {placed} props ({tris} tris) under {(parent != null ? parent.name + "/" : "")}{RootName} in {scene.path}" +
                   (missing.Count > 0 ? "; missing: " + string.Join(", ", missing.ToArray()) : "");
        }

        // ------------------------------------------------------------------ preview scene
        [MenuItem("BLINK/Bungalow/Build Props Preview Scene")]
        static void PreviewMenu() => Debug.Log(BuildPreviewScene(true));

        /// <summary>Builds Scenes/House_Bungalow_PropsPreview.unity (shell + props + provisional lights + navmesh),
        /// then reopens the scene that was open before.</summary>
        public static string BuildPreviewScene(bool restore)
        {
            var before = SceneManager.GetActiveScene().path;
            var log = new System.Text.StringBuilder();
            log.AppendLine(BungalowGreyboxBuilder.Build(false, true, PreviewScene));
            var scene = EditorSceneManager.OpenScene(PreviewScene, OpenSceneMode.Single);
            log.AppendLine(Place(scene));
            log.AppendLine(PreviewLights(scene));
            var surface = UnityEngine.Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface != null) log.AppendLine(BakePreviewNav(surface));
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("saved " + PreviewScene);
            if (restore && !string.IsNullOrEmpty(before) && before != PreviewScene)
                EditorSceneManager.OpenScene(before, OpenSceneMode.Single);
            return log.ToString();
        }

        static string BakePreviewNav(NavMeshSurface surface)
        {
            surface.BuildNavMesh();
            var built = surface.navMeshData;
            if (!AssetDatabase.IsValidFolder(PreviewNavFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "House_Bungalow_PropsPreview");
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(PreviewNavPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(built, existing);
                surface.RemoveData();
                surface.navMeshData = existing;
                surface.AddData();
            }
            else AssetDatabase.CreateAsset(built, PreviewNavPath);
            AssetDatabase.SaveAssetIfDirty(surface.navMeshData);
            var tri = NavMesh.CalculateTriangulation();
            return $"preview navmesh: {tri.indices.Length / 3} tris -> {PreviewNavPath}";
        }

        /// <summary>Provisional realtime point lights at the fixture emitters (colour / range from the layout), a dim
        /// ambient and no directional light: enough to judge the props. The real lighting is Phase 6's.</summary>
        static string PreviewLights(Scene scene)
        {
            foreach (var g in scene.GetRootGameObjects())
                if (g.name == "TEMP_GreyboxLight" || g.name == "PREVIEW_Lights") UnityEngine.Object.DestroyImmediate(g);
            var fx = JsonUtility.FromJson<FixtureFile>(File.ReadAllText(FixturesJson));
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutJson));
            var byId = layout.fixtures.ToDictionary(f => f.id);
            var root = new GameObject("PREVIEW_Lights");
            SceneManager.MoveGameObjectToScene(root, scene);
            int n = 0;
            foreach (var f in fx.fixtures)
            {
                if (f.off || !byId.TryGetValue(f.id, out var lf) || lf.intensity <= 0) continue;
                var go = new GameObject("PL_" + f.id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = V(f.emitterWorld);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                ColorUtility.TryParseHtmlString(lf.color, out var c);
                l.color = c;
                l.intensity = lf.intensity * 2.2f;
                l.range = lf.range * 1.25f;
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.7f;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                n++;
            }
            foreach (var w in layout.fixtures.Where(f => f.kind == "area"))
            {
                var go = new GameObject("PL_" + w.id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = V(w.pos) + new Vector3(w.pos[0] < 1 ? 0.6f : w.pos[0] > 19 ? -0.6f : 0, 0, w.pos[2] < 1 ? 0.6f : w.pos[2] > 14 ? -0.6f : 0);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                ColorUtility.TryParseHtmlString(w.color, out var c);
                l.color = c;
                l.intensity = w.intensity * 1.5f;
                l.range = 4f;
                l.shadows = LightShadows.None;
                n++;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.065f);
            RenderSettings.fog = false;
            return $"preview lights: {n}";
        }

        // ------------------------------------------------------------------ screenshots (edit mode)
        /// <summary>Renders a camera view of the open scene to a PNG (no post). pitch: positive looks down.</summary>
        public static string Shot(string file, Vector3 pos, float yaw, float pitch, float fovV, int w, int h)
        {
            var go = new GameObject("TEMP_ShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0f));
            cam.fieldOfView = fovV;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(go);
            return file;
        }

        static Vector3 V(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);
    }
}
