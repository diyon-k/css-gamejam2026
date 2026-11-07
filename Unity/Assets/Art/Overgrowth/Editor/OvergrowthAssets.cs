using UnityEditor;
using UnityEngine;

namespace Blink.Overgrowth.EditorTools
{
    public class OgMats
    {
        public Material wall, floor, wood, cellarWall, cellarFloor, roof, ground, path, bark, stone;
        public Material ivy, vine, grass, pine, leaves, litter, cobweb, hole, treeline, iron, glass;
    }

    /// <summary>Creates/updates the overgrowth materials, texture import settings and tree meshes (idempotent, GUIDs stable).</summary>
    public static class OvergrowthAssets
    {
        public const string Root = "Assets/Art/Overgrowth";
        const string SurfaceShader = "Blink/Overgrowth/Surface";
        const string FoliageShader = "Blink/Overgrowth/Foliage";

        public static Mesh[] Pines;     // submesh 0 trunk (bark), 1 branches (pine)
        public static Mesh Broadleaf;   // submesh 0 trunk (bark), 1 leaves

        [MenuItem("BLINK/Overgrowth/Rebuild Assets")]
        public static OgMats Ensure()
        {
            ConfigureTextures();
            var m = new OgMats();

            m.wall = Surface("M_OG_Wall", "decrepit_wallpaper", 1.8f, "damaged_plaster", 2.5f, "damaged_plaster", 2.5f, "mossy_stone_wall", 3f,
                moss: 0.02f, mossUp: 0.1f, damp: 0.7f, grime: 0.5f, smoothScale: 0.9f, sideTint: new Color(0.78f, 0.74f, 0.66f));
            m.floor = Surface("M_OG_Floor", "old_planks_02", 1.5f, "old_wood_floor", 2.2f, "damaged_plaster", 2.5f, "mossy_stone_wall", 3f,
                moss: 0.03f, mossUp: 0.12f, damp: 0.4f, grime: 0.45f, smoothScale: 1.1f, topTint: new Color(0.78f, 0.72f, 0.66f), bottomTint: new Color(0.8f, 0.78f, 0.74f));
            m.wood = Surface("M_OG_Wood", "old_planks_02", 1.2f, "old_planks_02", 1.2f, "old_planks_02", 1.2f, "old_planks_02", 1.2f,
                moss: 0.02f, mossUp: 0.12f, damp: 0.2f, grime: 0.4f, smoothScale: 1f, sideTint: new Color(0.62f, 0.55f, 0.5f), topTint: new Color(0.62f, 0.55f, 0.5f), bottomTint: new Color(0.62f, 0.55f, 0.5f));
            m.cellarWall = Surface("M_OG_CellarWall", "old_stone_wall", 2.5f, "concrete_floor_damaged_01", 3f, "concrete_floor_damaged_01", 3f, "old_stone_wall", 2.5f,
                moss: 0.1f, mossUp: 0.2f, damp: 1f, grime: 0.6f, smoothScale: 1f, sideTint: new Color(0.7f, 0.7f, 0.66f));
            m.cellarFloor = Surface("M_OG_CellarFloor", "old_stone_wall", 2.5f, "concrete_floor_damaged_01", 3f, "concrete_floor_damaged_01", 3f, "old_stone_wall", 2.5f,
                moss: 0.08f, mossUp: 0.1f, damp: 0.8f, grime: 0.6f, smoothScale: 1.2f);
            m.roof = Surface("M_OG_Roof", "old_planks_02", 1.5f, "roof_slates_02", 2f, "old_planks_02", 1.5f, "old_planks_02", 1.5f,
                moss: 0.12f, mossUp: 0.35f, damp: 0.3f, grime: 0.5f, smoothScale: 1f, sideTint: new Color(0.5f, 0.45f, 0.42f), bottomTint: new Color(0.5f, 0.45f, 0.42f));
            m.ground = Surface("M_OG_Ground", "stony_dirt_path", 3f, "forest_leaves_02", 3.5f, "stony_dirt_path", 3f, "stony_dirt_path", 3f,
                moss: 0.15f, mossUp: 0.15f, damp: 0f, grime: 0.5f, smoothScale: 0.8f, topTint: new Color(0.7f, 0.72f, 0.66f));
            m.path = Surface("M_OG_Path", "stony_dirt_path", 2.5f, "stony_dirt_path", 2.5f, "stony_dirt_path", 2.5f, "stony_dirt_path", 2.5f,
                moss: 0.05f, mossUp: 0.05f, damp: 0f, grime: 0.4f, smoothScale: 0.9f);
            m.bark = Surface("M_OG_Bark", "pine_bark", 1.2f, "pine_bark", 1.2f, "pine_bark", 1.2f, "pine_bark", 1.2f,
                moss: 0.08f, mossUp: 0.3f, damp: 0f, grime: 0.3f, smoothScale: 0.8f, sideTint: new Color(0.6f, 0.58f, 0.55f));
            m.stone = Surface("M_OG_Stone", "mossy_stone_wall", 2.5f, "mossy_stone_wall", 2.5f, "mossy_stone_wall", 2.5f, "mossy_stone_wall", 2.5f,
                moss: 0.15f, mossUp: 0.4f, damp: 0.5f, grime: 0.5f, smoothScale: 1f);

            m.ivy = Foliage("M_OG_Ivy", "T_IvyWall", wind: 0.015f, cutoff: 0.5f, translucency: 0.3f, hue: 0.25f, normalUp: 0f);
            m.vine = Foliage("M_OG_Vine", "T_Vine", wind: 0.09f, cutoff: 0.5f, translucency: 0.35f, hue: 0.25f, normalUp: 0.2f);
            m.grass = Foliage("M_OG_Grass", "T_Grass", wind: 0.06f, cutoff: 0.45f, translucency: 0.3f, hue: 0.3f, normalUp: 0.6f);
            m.pine = Foliage("M_OG_PineBranch", "T_PineBranch", wind: 0.12f, cutoff: 0.45f, translucency: 0.2f, hue: 0.2f, normalUp: 0.4f, tint: new Color(0.75f, 0.85f, 0.8f));
            m.leaves = Foliage("M_OG_Leaves", "T_LeafCluster", wind: 0.1f, cutoff: 0.5f, translucency: 0.4f, hue: 0.3f, normalUp: 0.5f);
            m.litter = Foliage("M_OG_LeafLitter", "T_LeafLitter", wind: 0f, cutoff: 0.5f, translucency: 0f, hue: 0.2f, normalUp: 0f, tint: new Color(0.8f, 0.78f, 0.75f));
            m.cobweb = Foliage("M_OG_Cobweb", "T_Cobweb", wind: 0.02f, cutoff: 0.35f, translucency: 0f, hue: 0f, normalUp: 0.5f, tint: new Color(0.55f, 0.55f, 0.55f));
            m.hole = Foliage("M_OG_FloorHole", "T_FloorHole", wind: 0f, cutoff: 0.5f, translucency: 0f, hue: 0f, normalUp: 0f);
            m.treeline = Foliage("M_OG_Treeline", "T_Treeline", wind: 0f, cutoff: 0.5f, translucency: 0f, hue: 0f, normalUp: 0f);
            m.treeline.SetFloat("_FogScale", 0.35f);
            m.treeline.SetFloat("_Unlit", 1f);

            m.iron = Lit("M_OG_Iron", new Color(0.07f, 0.06f, 0.055f), 0.35f, 0.7f);
            m.glass = Lit("M_OG_Glass", new Color(0.35f, 0.42f, 0.42f), 0.95f, 0.2f);

            EnsureTrees();
            if (SafeMode)
            {
                // Swap every custom-shader material for a plain URP/Lit twin (M_OGL_*). Loses triplanar blending,
                // moss/damp masks and wind; keeps albedo + normal + tiling (generated meshes have metre UVs).
                foreach (var f in typeof(OgMats).GetFields())
                {
                    var mat = (Material)f.GetValue(m);
                    if (mat.shader.name.StartsWith("Blink/")) f.SetValue(m, LitTwin(mat));
                }
                Debug.LogWarning("[Overgrowth] Safe mode: using plain URP/Lit materials (BLINK > Overgrowth > Safe Mode to turn off).");
            }
            AssetDatabase.SaveAssets();
            return m;
        }

        // ---------------------------------------------------------------- safe mode (fallback if the custom shaders misbehave)
        const string SafeKey = "Blink.Overgrowth.SafeMode";
        const string SafeMenu = "BLINK/Overgrowth/Safe Mode (plain URP Lit)";

        /// <summary>When on, the builder uses plain URP/Lit materials instead of Blink/Overgrowth/* shaders. Per-machine (EditorPrefs).</summary>
        public static bool SafeMode
        {
            get => EditorPrefs.GetBool(SafeKey, false);
            set => EditorPrefs.SetBool(SafeKey, value);
        }

        [MenuItem(SafeMenu)]
        static void ToggleSafeMode() => SafeMode = !SafeMode;

        [MenuItem(SafeMenu, true)]
        static bool ToggleSafeModeValidate()
        {
            Menu.SetChecked(SafeMenu, SafeMode);
            return true;
        }

        static Material LitTwin(Material src)
        {
            var l = GetMat(src.name.Replace("M_OG_", "M_OGL_"), "Universal Render Pipeline/Lit");
            Texture albedo, normal;
            float tiling = 1f;
            if (src.shader.name == FoliageShader)
            {
                albedo = src.GetTexture("_BaseMap");
                normal = src.GetTexture("_BumpMap");
                l.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                l.SetFloat("_AlphaClip", 1);
                l.SetFloat("_Cutoff", src.GetFloat("_Cutoff"));
                l.SetFloat("_Cull", 0); // two-sided
                l.EnableKeyword("_ALPHATEST_ON");
                l.renderQueue = 2450;
            }
            else
            {
                string p = src.name.Contains("Wall") || src.name.Contains("Bark") || src.name.Contains("Stone") ? "_Side" : "_Top";
                albedo = src.GetTexture(p + "Map");
                normal = src.GetTexture(p + "Normal");
                l.SetColor("_BaseColor", src.GetColor(p + "Tint"));
                tiling = 1f / Mathf.Max(0.01f, src.GetFloat(p + "Scale"));
            }
            l.SetTexture("_BaseMap", albedo);
            l.SetTextureScale("_BaseMap", Vector2.one * tiling);
            l.SetTexture("_BumpMap", normal);
            if (normal != null) l.EnableKeyword("_NORMALMAP"); else l.DisableKeyword("_NORMALMAP");
            l.SetFloat("_Smoothness", 0.2f);
            l.SetFloat("_Metallic", 0f);
            return l;
        }

        // ---------------------------------------------------------------- textures
        static readonly string[] HeroMaps = { "decrepit_wallpaper", "old_wood_floor" }; // 2K sources

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                bool changed = false;
                void Set<T>(T cur, T want, System.Action<T> apply) { if (!Equals(cur, want)) { apply(want); changed = true; } }

                bool normal = file.EndsWith("_nor");
                bool arm = file.EndsWith("_arm");
                bool noise = file == "T_Noise";
                bool card = file.StartsWith("T_") && !normal && !noise;
                bool surface = !file.StartsWith("T_");
                bool hero = System.Array.Exists(HeroMaps, h => file.StartsWith(h));

                Set(ti.mipmapEnabled, true, x => ti.mipmapEnabled = x);
                if (normal) Set(ti.textureType, TextureImporterType.NormalMap, x => ti.textureType = x);
                if (arm || noise) Set(ti.sRGBTexture, false, x => ti.sRGBTexture = x);
                if (surface) Set(ti.anisoLevel, 4, x => ti.anisoLevel = x);
                if (noise)
                {
                    Set(ti.alphaSource, TextureImporterAlphaSource.FromInput, x => ti.alphaSource = x);
                    Set(ti.alphaIsTransparency, false, x => ti.alphaIsTransparency = x);
                }
                if (card)
                {
                    Set(ti.alphaIsTransparency, true, x => ti.alphaIsTransparency = x);
                    Set(ti.mipMapsPreserveCoverage, true, x => ti.mipMapsPreserveCoverage = x);
                    Set(ti.alphaTestReferenceValue, 0.5f, x => ti.alphaTestReferenceValue = x);
                }
                if (file.StartsWith("T_"))
                {
                    bool repeatU = file.StartsWith("T_Vine") || file.StartsWith("T_IvyWall") || file.StartsWith("T_Treeline") || noise;
                    bool repeatV = file.StartsWith("T_Vine") || file.StartsWith("T_IvyWall") || noise;
                    Set(ti.wrapModeU, repeatU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, x => ti.wrapModeU = x);
                    Set(ti.wrapModeV, repeatV ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, x => ti.wrapModeV = x);
                }
                int size = hero || file == "T_Treeline" ? 2048 : 1024;
                Set(ti.maxTextureSize, size, x => ti.maxTextureSize = x);

                // PC: BC5 normals, BC7 colour/masks, noise uncompressed (it drives thresholds).
                var ps = ti.GetPlatformTextureSettings("Standalone");
                var fmt = noise ? TextureImporterFormat.RGBA32 : normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
                if (!ps.overridden || ps.format != fmt || ps.maxTextureSize != size)
                {
                    ps.overridden = true; ps.format = fmt; ps.maxTextureSize = size;
                    ti.SetPlatformTextureSettings(ps);
                    changed = true;
                }
                if (changed) ti.SaveAndReimport();
            }
        }

        static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{file}");

        // ---------------------------------------------------------------- materials
        static Material GetMat(string name, string shader)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != sh) m.shader = sh;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Surface(string name, string side, float sideScale, string top, float topScale, string bottom, float bottomScale,
            string facade, float facadeScale, float moss, float mossUp, float damp, float grime, float smoothScale,
            Color? sideTint = null, Color? topTint = null, Color? bottomTint = null)
        {
            var m = GetMat(name, SurfaceShader);
            void Set(string prop, string file, float scale, Color tint)
            {
                m.SetTexture($"_{prop}Map", Tex(file + "_diff.jpg"));
                m.SetTexture($"_{prop}Normal", Tex(file + "_nor.jpg"));
                m.SetTexture($"_{prop}Arm", Tex(file + "_arm.jpg"));
                m.SetFloat($"_{prop}Scale", scale);
                m.SetColor($"_{prop}Tint", tint);
            }
            Set("Side", side, sideScale, sideTint ?? Color.white);
            Set("Top", top, topScale, topTint ?? Color.white);
            Set("Bottom", bottom, bottomScale, bottomTint ?? Color.white);
            Set("Facade", facade, facadeScale, new Color(0.8f, 0.8f, 0.78f));
            m.SetTexture("_MossMap", Tex("moss002_diff.jpg"));
            m.SetTexture("_MossNormal", Tex("moss002_nor.jpg"));
            m.SetTexture("_MossArm", Tex("moss002_arm.jpg"));
            m.SetTexture("_NoiseMap", Tex("T_Noise.png"));
            m.SetFloat("_MossAmount", moss);
            m.SetFloat("_MossUp", mossUp);
            m.SetFloat("_Damp", damp);
            m.SetFloat("_Grime", grime);
            m.SetFloat("_SmoothScale", smoothScale);
            m.SetFloat("_AOStrength", 1f);
            return m;
        }

        static Material Foliage(string name, string tex, float wind, float cutoff, float translucency, float hue, float normalUp, Color? tint = null)
        {
            var m = GetMat(name, FoliageShader);
            m.SetTexture("_BaseMap", Tex(tex + ".png"));
            m.SetTexture("_BumpMap", Tex(tex + "_nor.png")); // null -> flat "bump" default
            m.SetFloat("_BumpScale", 1f);
            m.SetFloat("_Smoothness", 0.18f);
            bool anchored = wind > 0; // anchored cards get darker toward their base; flat decals don't
            m.SetFloat("_BaseAO", anchored ? 0.5f : 1f);
            m.SetColor("_BaseColor", tint ?? Color.white);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_WindStrength", wind);
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_HueVariation", hue);
            m.SetFloat("_NormalUp", normalUp);
            m.SetFloat("_FogScale", 1f);
            m.SetFloat("_Unlit", 0f);
            m.renderQueue = 2450; // AlphaTest
            return m;
        }

        static Material Lit(string name, Color c, float smooth, float metal)
        {
            var m = GetMat(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        // ---------------------------------------------------------------- tree meshes (base at origin, +Y up)
        static void EnsureTrees()
        {
            Pines = new Mesh[3];
            for (int i = 0; i < 3; i++) Pines[i] = SaveMesh(BuildPine(new System.Random(100 + i), 15f + i * 2.5f), "Tree_Pine_" + i);
            Broadleaf = SaveMesh(BuildBroadleaf(new System.Random(200)), "Tree_Broadleaf");
        }

        /// <summary>Writes into an existing asset when present so references and GUIDs survive rebuilds.</summary>
        public static Mesh SaveMesh(Mesh mesh, string name, string folder = "Meshes")
        {
            string dir = $"{Root}/{folder}";
            string parent = Root;
            foreach (var part in folder.Split('/'))
            {
                if (!AssetDatabase.IsValidFolder(parent + "/" + part)) AssetDatabase.CreateFolder(parent, part);
                parent += "/" + part;
            }
            string path = $"{dir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            mesh.name = name;
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static Mesh BuildPine(System.Random r, float h)
        {
            var b = new OgMeshBuilder(2);
            var trunkCol = new Color(0, 0.25f, 0, 0);
            var trunkTop = new Color(0, 0, 0, 0);
            b.Tube(Vector3.zero, Vector3.up * h * 0.55f, 0.32f, 0.16f, 7, trunkCol, trunkTop);
            b.Tube(Vector3.up * h * 0.55f, Vector3.up * h, 0.16f, 0.03f, 5, trunkTop, trunkTop);

            int tiers = 15;
            float start = h * R(r, 0.18f, 0.28f);
            for (int t = 0; t < tiers; t++)
            {
                float f = (float)t / (tiers - 1);
                float y = Mathf.Lerp(start, h * 0.97f, f) + R(r, -0.2f, 0.2f);
                float len = Mathf.Lerp(3.6f, 0.5f, Mathf.Pow(f, 0.85f)) * R(r, 0.85f, 1.15f);
                float droop = Mathf.Lerp(0.5f, 0.1f, f);
                int count = f > 0.8f ? 5 : 7;
                float off = R(r, 0, 360);
                for (int k = 0; k < count; k++)
                {
                    float a = (off + 360f * k / count + R(r, -15, 15)) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), -droop * R(r, 0.7f, 1.3f), Mathf.Sin(a)).normalized;
                    var s = Vector3.up * y + dir * 0.1f;
                    var e = s + dir * len;
                    var side = Vector3.Cross(dir, Vector3.up).normalized * len * 0.32f;
                    var lift = Vector3.Cross(side, dir).normalized * len * 0.26f;
                    var lo = new Color(0, 0, 0, 0);
                    var hi = new Color(0, 0, 0, 1);
                    // flat-ish card and a crossed one; u runs along the branch.
                    b.Quad(s - side * 0.5f, e - side, e + side, s + side * 0.5f, Vector2.zero, Vector2.one, lo, hi, hi, lo, 1);
                    b.Quad(s - lift * 0.5f, e - lift, e + lift, s + lift * 0.5f, Vector2.zero, Vector2.one, lo, hi, hi, lo, 1);
                }
            }
            return b.ToMesh("pine");
        }

        static Mesh BuildBroadleaf(System.Random r)
        {
            var b = new OgMeshBuilder(2);
            var c0 = new Color(0, 0.3f, 0, 0);
            var c1 = new Color(0, 0.05f, 0, 0);
            var fork = new Vector3(R(r, -0.2f, 0.2f), 3.2f, R(r, -0.2f, 0.2f));
            b.Tube(Vector3.zero, fork, 0.28f, 0.2f, 8, c0, c1);
            // roots flaring at the base
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90 + R(r, -20, 20)) * Mathf.Deg2Rad;
                b.Tube(Vector3.up * 0.4f, new Vector3(Mathf.Cos(a) * 1.0f, -0.05f, Mathf.Sin(a) * 1.0f), 0.13f, 0.04f, 5, c0, c0);
            }
            void Branch(Vector3 from, Vector3 dir, float len, float rad, int depth)
            {
                var to = from + dir * len;
                b.Tube(from, to, rad, rad * 0.6f, 6, c1, c1);
                if (depth == 0)
                {
                    for (int k = 0; k < 3; k++) // crossed leaf cluster cards
                    {
                        var rot = Quaternion.Euler(R(r, -30, 30), k * 60 + R(r, -15, 15), R(r, -30, 30));
                        var right = rot * Vector3.right * R(r, 1.0f, 1.5f);
                        var up = rot * Vector3.up * R(r, 1.0f, 1.5f);
                        var lo = new Color(0, 0, 0, 0.6f);
                        var hi = new Color(0, 0, 0, 1);
                        b.Quad(to - right - up, to + right - up, to + right + up, to - right + up, Vector2.zero, Vector2.one, lo, lo, hi, hi, 1);
                    }
                    return;
                }
                int n = depth == 2 ? 4 : 2;
                for (int i = 0; i < n; i++)
                {
                    float yaw = (360f * i / n + R(r, -30, 30)) * Mathf.Deg2Rad;
                    var nd = (dir * 0.8f + new Vector3(Mathf.Cos(yaw), R(r, 0.3f, 0.9f), Mathf.Sin(yaw)) * 0.7f).normalized;
                    Branch(to, nd, len * R(r, 0.55f, 0.75f), rad * 0.6f, depth - 1);
                }
            }
            Branch(fork, Vector3.up, 1.6f, 0.2f, 2);
            return b.ToMesh("broadleaf");
        }
    }
}
