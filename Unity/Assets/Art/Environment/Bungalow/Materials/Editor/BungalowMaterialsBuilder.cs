using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.EditorTools
{
    /// <summary>
    /// Bungalow Phase 4: texture import settings + the surface materials (SPEC section 3, #1-#15).
    /// Textures come from tools/textures/bungalow_surfaces.py (Textures/&lt;MaterialName&gt;/), the wear shader from
    /// tools/textures/bungalow_shadergraph.py (Shaders/SG_BungalowLit.shadergraph).
    /// Kit materials use SG_BungalowLit (URP Lit + UV2 wear atlas); glass, water, shade and decals use URP/Lit.
    /// UV0 contract (Phase 3, bungalow_layout.SURFACE_TILE_M): kit surfaces are box-mapped at 1 UV = one texture tile of
    /// SurfaceTileM metres, so kit materials use tiling 1. M_Shade (props, Phase 5) assumes metric UVs (tiling 2).
    /// M_Trim and the wear atlas are wired from Textures/Trim and Textures/KitWear when those exist (Phase 3);
    /// until then M_Trim is a flat dark-varnish placeholder and the wear slot stays empty (neutral).
    /// Re-runnable: existing materials are updated in place (GUIDs kept). Touches nothing outside its own folders.
    /// </summary>
    public static class BungalowMaterialsBuilder
    {
        const string Root = "Assets/Art/Environment/Bungalow/";
        const string TexRoot = Root + "Textures/";
        const string MatRoot = Root + "Materials/";
        const string GraphPath = Root + "Shaders/SG_BungalowLit.shadergraph";
        static readonly string[] NotMine = { "Trim", "KitWear" };   // Phase 3 owns their import settings

        /// <summary>Metres per texture tile, read from bungalow_layout.json "surfaceTiles" (written by
        /// tools/blender/bungalow_layout.py from SURFACE_TILE_M, the single source; the kit bakes it into UV0).
        /// The table below is only the fallback when the JSON is missing. Used only where UVs are metric: the grey-box
        /// preview and the neutral comparison quads.</summary>
        public static Dictionary<string, float> SurfaceTileM => tiles ?? (tiles = LoadTiles());
        static Dictionary<string, float> tiles;
        static readonly Dictionary<string, float> FallbackTileM = new Dictionary<string, float>
        {
            { "M_FloorPlank", 2.0f }, { "M_Parquet", 3.4f }, { "M_Lino", 2.0f }, { "M_Concrete", 2.5f },
            { "M_Wall_Damask", 2.0f }, { "M_Wall_Pale", 2.0f }, { "M_Wall_Floral", 2.0f },
            { "M_Plaster", 1.5f }, { "M_Plaster_Kitchen", 1.5f }, { "M_BrickWhite", 2.0f }, { "M_Tile", 1.5f },
            { "M_Glass", 1.0f }, { "M_Water", 4.0f },
        };

        [Serializable] class TileEntry { public string material; public float tileM; }
        [Serializable] class LayoutTiles { public TileEntry[] surfaceTiles; }

        static Dictionary<string, float> LoadTiles()
        {
            var d = new Dictionary<string, float>(FallbackTileM);
            var p = Root + "bungalow_layout.json";
            if (!File.Exists(p)) return d;
            var j = JsonUtility.FromJson<LayoutTiles>(File.ReadAllText(p));
            if (j != null && j.surfaceTiles != null)
                foreach (var t in j.surfaceTiles) d[t.material] = t.tileM;
            return d;
        }

        [MenuItem("BLINK/Bungalow/Build Surface Materials")]
        static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play mode first.");
            var log = new StringBuilder();
            ApplyImportSettings(log);
            BuildMaterials(log);
            return log.ToString();
        }

        // ------------------------------------------------------------------ textures
        public static void ApplyImportSettings(StringBuilder log)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexRoot.TrimEnd('/') });
            int changed = 0;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var rel = path.Substring(TexRoot.Length);
                if (NotMine.Any(n => rel.StartsWith(n + "/", StringComparison.Ordinal))) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                if (Configure(ti, Path.GetFileNameWithoutExtension(path), rel.StartsWith("M_Decals/")))
                {
                    ti.SaveAndReimport();
                    changed++;
                }
            }
            log.AppendLine($"textures: {guids.Length} found, {changed} reimported");
        }

        /// <summary>1024, mipmaps, BC7 (Standalone override), normal maps flagged, masks linear, alpha only where
        /// the base map carries coverage. Returns true when something changed.</summary>
        static bool Configure(TextureImporter ti, string name, bool atlas)
        {
            var before = EditorJsonUtility.ToJson(ti);
            bool normal = name.EndsWith("_Normal"), mask = name.EndsWith("_Mask");
            bool alphaBase = name == "M_Glass_Base" || name == "M_Decals_Base";
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || mask);
            ti.alphaSource = (mask || alphaBase) ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = alphaBase;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = false;
            ti.wrapMode = atlas ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = atlas ? 2 : 8;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.isReadable = false;
            var ps = ti.GetPlatformTextureSettings("Standalone");
            ps.overridden = true;
            ps.maxTextureSize = 1024;
            ps.format = TextureImporterFormat.BC7;
            ps.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(ps);
            return EditorJsonUtility.ToJson(ti) != before;
        }

        // ------------------------------------------------------------------ materials
        struct Wear
        {
            public Color edge; public float edgeLighten, edgeStrength; public Color dirt; public float dirtStrength,
                dirtRoughen, polishStrength, polishSmooth;
        }

        static Wear W(Color edge, float lighten, Color dirt, float polish, float polishSmooth = 0.72f,
                      float edgeStr = 1f, float dirtStr = 1f, float roughen = 0.6f) =>
            new Wear { edge = edge, edgeLighten = lighten, edgeStrength = edgeStr, dirt = dirt, dirtStrength = dirtStr,
                       dirtRoughen = roughen, polishStrength = polish, polishSmooth = polishSmooth };

        static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        public static void BuildMaterials(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(MatRoot.TrimEnd('/')))
                AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Materials");
            var sg = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (sg == null || lit == null) throw new Exception("missing shader: SG_BungalowLit or URP Lit");
            var wearTex = FindTex("KitWear", null);

            var woodDirt = C(104, 92, 76);
            // name, base tint (sRGB), normal scale, smoothness scale, wear
            Kit("M_FloorPlank", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(C(150, 116, 84, 0.45f), 0.35f, woodDirt, 1f, 0.68f), log);
            Kit("M_Parquet", sg, C(255, 255, 255), 1.0f, 0.7f, wearTex,
                W(C(150, 112, 80, 0.45f), 0.35f, woodDirt, 1f, 0.7f), log);
            Kit("M_Lino", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(C(120, 112, 98, 0.6f), 0.2f, C(98, 90, 76), 0.6f, 0.6f), log);
            Kit("M_Concrete", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(C(150, 146, 138, 0.5f), 0.25f, C(92, 86, 74), 0.2f, 0.5f), log);
            var paperEdge = C(176, 160, 132, 0.6f);
            Kit("M_Wall_Damask", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(paperEdge, 0.3f, C(110, 98, 84), 0f), log);
            Kit("M_Wall_Pale", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(paperEdge, 0.1f, C(120, 108, 90), 0f), log);
            Kit("M_Wall_Floral", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(paperEdge, 0.2f, C(100, 100, 82), 0f), log);
            Kit("M_Plaster", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(C(214, 208, 192, 0.5f), 0.2f, C(120, 110, 94), 0f), log);
            // 20th material (the spare): kitchen walls, M_Plaster's maps tinted blue-white (ceilings stay M_Plaster)
            Kit("M_Plaster_Kitchen", sg, C(204, 222, 242), 1.0f, 1.0f, wearTex,
                W(C(214, 214, 206, 0.5f), 0.2f, C(112, 110, 100), 0f), log, "M_Plaster");
            Kit("M_BrickWhite", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(C(150, 104, 86, 0.6f), 0f, C(112, 104, 90), 0f), log);   // chipped whitewash shows red brick
            Kit("M_Tile", sg, C(255, 255, 255), 1.0f, 1.0f, wearTex,
                W(C(128, 124, 116, 0.7f), 0f, C(110, 100, 84), 0.3f, 0.85f), log);
            Trim(sg, wearTex, W(C(132, 100, 72, 0.5f), 0.3f, woodDirt, 1f, 0.75f), log);

            Shade(lit, log);
            Glass(lit, log);
            Water(lit, log);
            Decals(lit, log);
            foreach (var m in Touched) AssetDatabase.SaveAssetIfDirty(m);   // never a blanket SaveAssets
            Touched.Clear();
        }

        static Texture2D T(string mat, string suffix) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexRoot}{mat}/{mat}_{suffix}.png");

        /// <summary>First texture in Textures/&lt;folder&gt;/ whose name contains one of the keys (null keys = any).</summary>
        static Texture2D FindTex(string folder, string[] keys)
        {
            var dir = TexRoot + folder;
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var n = Path.GetFileNameWithoutExtension(p);
                if (keys == null || keys.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        static readonly List<Material> Touched = new List<Material>();

        static Material Mat(string name, Shader shader, StringBuilder log)
        {
            var path = MatRoot + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
                log.Append("created ");
            }
            else
            {
                if (m.shader != shader) m.shader = shader;
                log.Append("updated ");
            }
            log.AppendLine(path);
            Touched.Add(m);
            return m;
        }

        static void SetWear(Material m, Texture2D wearTex, Wear w)
        {
            m.SetTexture("_WearMap", wearTex);
            m.SetFloat("_WearAOStrength", 1f);
            m.SetFloat("_EdgeWearStrength", w.edgeStrength);
            m.SetColor("_EdgeWearColor", w.edge);
            m.SetFloat("_EdgeWearLighten", w.edgeLighten);
            m.SetFloat("_DirtStrength", w.dirtStrength);
            m.SetColor("_DirtColor", w.dirt);
            m.SetFloat("_DirtRoughen", w.dirtRoughen);
            m.SetFloat("_PolishStrength", w.polishStrength);
            m.SetFloat("_PolishSmoothness", w.polishSmooth);
        }

        static void Kit(string name, Shader sg, Color tint, float bump, float smooth, Texture2D wearTex, Wear w,
                        StringBuilder log, string texFrom = null)
        {
            var m = Mat(name, sg, log);
            bool wall = name == "M_Wall_Damask" || name == "M_Wall_Pale";
            var src = texFrom ?? name;    // a tinted variant reuses another material's maps
            var baseTex = T(src, "Base");
            var nrm = wall ? T("M_Wall_Shared", "Normal") : T(src, "Normal");
            var mask = wall ? T("M_Wall_Shared", "Mask") : T(src, "Mask");
            m.SetTexture("_BaseMap", baseTex);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BumpMap", nrm);
            m.SetFloat("_BumpScale", bump);
            m.SetTexture("_MaskMap", mask);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetTextureScale("_BaseMap", Vector2.one);   // tile size is in the kit's UV0 (SurfaceTileM)
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            SetWear(m, wearTex, w);
            m.enableInstancing = false;    // SRP Batcher + static batching instead
            EditorUtility.SetDirty(m);
        }

        static void Trim(Shader sg, Texture2D wearTex, Wear w, StringBuilder log)
        {
            var m = Mat("M_Trim", sg, log);
            var b = FindTex("Trim", new[] { "Base", "Albedo", "Color", "Diffuse" });
            var n = FindTex("Trim", new[] { "Normal" });
            var k = FindTex("Trim", new[] { "Mask", "MetallicSmoothness" });
            m.SetTexture("_BaseMap", b);
            // placeholder until the trim sheet exists: flat dark varnish (ref 1/2 casings)
            m.SetColor("_BaseColor", b != null ? Color.white : C(62, 42, 30));
            m.SetTexture("_BumpMap", n);
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_MaskMap", k);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", k != null ? 1f : 0.42f);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetTextureScale("_BaseMap", Vector2.one);   // trim sheet: UV-unwrapped, not tiled
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            SetWear(m, wearTex, w);
            EditorUtility.SetDirty(m);
            log.AppendLine($"  M_Trim textures: base={(b ? b.name : "placeholder")} normal={(n ? n.name : "-")} " +
                           $"mask={(k ? k.name : "-")} wear={(wearTex ? wearTex.name : "neutral")}");
        }

        // ---- URP Lit materials
        static void LitMaps(Material m, string name, bool withNormal = true)
        {
            m.SetTexture("_BaseMap", T(name, "Base"));
            if (withNormal) m.SetTexture("_BumpMap", T(name, "Normal"));
            var mask = T(name, "Mask");
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask);       // AO lives in G, which is what URP Lit reads
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_Smoothness", 1f);              // scale on mask A
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_BumpScale", 1f);
            m.SetTextureScale("_BaseMap", name == "M_Shade" ? new Vector2(2f, 2f) : Vector2.one);
        }

        static void Transparent(Material m, bool preserveSpecular, int queueOffset)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);                   // alpha
            m.SetFloat("_BlendModePreserveSpecular", preserveSpecular ? 1f : 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_QueueOffset", queueOffset);
            m.SetFloat("_AlphaClip", 0f);
        }

        static void Validate(Material m)
        {
            // URP's own Lit inspector logic sets keywords, blend state and render queue from the properties.
            var t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader", false))
                .FirstOrDefault(x => x != null);
            if (t != null && Activator.CreateInstance(t, true) is ShaderGUI gui) gui.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }

        static void Shade(Shader lit, StringBuilder log)
        {
            var m = Mat("M_Shade", lit, log);
            LitMaps(m, "M_Shade");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);    // lamp shades are seen from inside too
            m.SetTexture("_EmissionMap", T("M_Shade", "Base"));
            // SPEC 5.1: emissive = the light's colour at low strength; default = L1/L3 tungsten x1.5.
            // Per-fixture strength/colour is a Phase 6 job (MaterialPropertyBlock or a few variants).
            m.SetColor("_EmissionColor", C(255, 217, 160) * 1.5f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;   // the point lights do the lighting
            Validate(m);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        static void Glass(Shader lit, StringBuilder log)
        {
            var m = Mat("M_Glass", lit, log);
            LitMaps(m, "M_Glass", withNormal: false);
            m.SetTexture("_BumpMap", null);
            m.SetTexture("_OcclusionMap", null);
            m.SetColor("_BaseColor", Color.white);        // tint + alpha (dirt cover) come from the base map
            Transparent(m, true, 0);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            Validate(m);
        }

        static void Water(Shader lit, StringBuilder log)
        {
            var m = Mat("M_Water", lit, log);
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", C(34, 32, 22, 0.86f));   // murky flood water over the concrete
            m.SetTexture("_BumpMap", T("M_Water", "Normal"));
            m.SetFloat("_BumpScale", 0.55f);
            var mask = T("M_Water", "Mask");
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", null);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetTextureScale("_BaseMap", Vector2.one);
            Transparent(m, true, 10);
            Validate(m);
        }

        static void Decals(Shader lit, StringBuilder log)
        {
            var m = Mat("M_Decals", lit, log);
            LitMaps(m, "M_Decals");
            m.SetTexture("_OcclusionMap", null);
            m.SetColor("_BaseColor", Color.white);
            m.SetTextureScale("_BaseMap", Vector2.one);
            Transparent(m, false, -20);                  // before glass and water in the transparent queue
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            Validate(m);
        }
    }
}
