using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Blink.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Blink.EditorTools
{
    /// <summary>
    /// Bungalow Phase 6 lighting pass (SPEC 5). Builds, in the active scene, a "Lighting" root holding:
    /// - every fixture light from bungalow_layout.json (SPEC 5.1) with its mode, colour, intensity, range and shadows,
    ///   plus a <see cref="PracticalLight"/> that makes its shade glow (or not: H5 is broken, C2 waits for the generator);
    ///   lights sit on the Phase 5 fixture emitters when those are in the scene, else at the layout positions with
    ///   placeholder shades;
    /// - light-probe groups on a ~1 m grid per room and down the stairwell, and box-projected reflection probes per
    ///   room plus the stairwell, all laid out from the layout room rectangles;
    /// - a global post-processing Volume and one local Volume per room group (SPEC 5.2);
    /// - the atmosphere (<see cref="BungalowAtmosphereBuilder"/>): basement water and mesh decals.
    /// It also sets the scene's ambient / fog / reflection environment, assigns the LightingSettings asset
    /// (GPU Progressive, Shadowmask), removes the grey-box's temporary light and disables NightSky in this scene
    /// only (a prefab-instance override; GameSystems.prefab is untouched). Re-running replaces everything it made.
    /// Tuning lives in tools/blender/bungalow_decals.py -> Lighting/bungalow_atmosphere.json.
    /// </summary>
    public static class BungalowLightingBuilder
    {
        public const string ArtRoot = "Assets/Art/Environment/Bungalow/";
        public const string LightingDir = ArtRoot + "Lighting";
        const string LayoutPath = ArtRoot + "bungalow_layout.json";
        public const string AtmospherePath = LightingDir + "/bungalow_atmosphere.json";
        const string FixturesPath = ArtRoot + "Props/fixtures.json";
        const string SettingsPath = LightingDir + "/LS_Bungalow.lighting";
        const string ShadeMatPath = ArtRoot + "Materials/M_Shade.mat";
        public const string RootName = "Lighting";
        const string OutsidePrefix = "BL_"; // builder-made objects parented outside the root (on fixture emitters)

        public const StaticEditorFlags GIStatic = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic;

        // ------------------------------------------------------------------ data
#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] public class Fixture { public string id, kind, color, mode; public float[] pos; public float intensity, range; }
        [Serializable] public class Room { public string id, @ref; public float[] min, max; }
        [Serializable] public class Cam { public string id, @ref; public float[] pos; public float yaw, pitch, fov; public int w, h; }
        [Serializable] public class Layout { public Room[] rooms; public Fixture[] fixtures; public Cam[] cameras; }

        [Serializable] public class AreaLight { public string id; public float[] size, face; }
        [Serializable] public class Box { public string id; public float[] min, max; }
        [Serializable] public class Water { public float y; public string[] rooms; }
        [Serializable] public class FixtureTune { public string id, col; public float glow, fizz, i, range; }
        [Serializable] public class Globals { public string ambient, fog, fogMode; public float fogDensity; }
        [Serializable] public class Post
        {
            public string name, filter, splitShadows, splitHighlights, bloomTint;
            public string[] rooms;
            public float exposure, saturation, contrast, temperature, tint, vignette, ca, bloom, threshold, scatter;
            public float[] lift;
        }
        [Serializable] public class Tuning { public FixtureTune[] fixtures; public Globals globals; public Post post; public Post[] rooms; }
        [Serializable] public class Decal { public string id, room, surface, cell; public int sub; public float[] c, n, r, v, size; }
        [Serializable] public class Atmosphere { public Decal[] decals; public AreaLight[] areaLights; public Box stairWell; public Water water; public Tuning tuning; }

        [Serializable] class FixtureEntry { public string id, name, path, instance, emitterChild; public float[] emitter, emitterWorld, pos; }
        [Serializable] class FixtureFile { public FixtureEntry[] fixtures; }
#pragma warning restore 0649

        public static Layout LoadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
        public static Atmosphere LoadAtmosphere() => JsonUtility.FromJson<Atmosphere>(File.ReadAllText(AtmospherePath));

        // ------------------------------------------------------------------ menu
        [MenuItem("BLINK/Bungalow/Build Lighting")]
        static void BuildMenu()
        {
            var scene = SceneManager.GetActiveScene();
            Debug.Log(Build(scene, false));
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Builds (or rebuilds) the lighting rig in <paramref name="scene"/>. Doesn't save or bake.</summary>
        public static string Build(Scene scene, bool provisional)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play mode first.");
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            var layout = LoadLayout();
            var atmo = LoadAtmosphere();
            var report = new StringBuilder();

            // ---- clear what an earlier run made
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName || go.name == "TEMP_GreyboxLight") Object.DestroyImmediate(go);
            foreach (var t in AllTransforms(scene))
                if (t != null && t.name.StartsWith(OutsidePrefix, StringComparison.Ordinal)) Object.DestroyImmediate(t.gameObject);

            var root = new GameObject(RootName).transform;
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);

            report.AppendLine(BuildFixtures(scene, root, layout, atmo));
            Physics.SyncTransforms();
            report.AppendLine(BuildProbes(root, layout, atmo));
            report.AppendLine(BuildVolumes(root, layout, atmo));
            report.AppendLine(BungalowAtmosphereBuilder.Build(scene, root, layout, atmo));
            report.AppendLine(ApplyEnvironment(atmo, provisional));
            report.AppendLine(DisableDayNight(scene));
            report.AppendLine(NightCameras(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return report.ToString();
        }

        // ------------------------------------------------------------------ fixtures
        static string BuildFixtures(Scene scene, Transform root, Layout layout, Atmosphere atmo)
        {
            var parent = Child(root, "Fixtures");
            var placeholders = Child(root, "PlaceholderShades");
            var tune = new Dictionary<string, FixtureTune>();
            if (atmo.tuning?.fixtures != null) foreach (var t in atmo.tuning.fixtures) tune[t.id] = t;
            var areas = new Dictionary<string, AreaLight>();
            if (atmo.areaLights != null) foreach (var a in atmo.areaLights) areas[a.id] = a;
            var fixtureFile = File.Exists(FixturesPath) ? JsonUtility.FromJson<FixtureFile>(File.ReadAllText(FixturesPath)) : null;
            var shadeMat = AssetDatabase.LoadAssetAtPath<Material>(ShadeMatPath);

            int lights = 0, onEmitter = 0, placeholder = 0;
            var counts = new Dictionary<string, int>();
            var mixed = new List<Light>();
            foreach (var f in layout.fixtures)
            {
                tune.TryGetValue(f.id, out var t);
                bool area = f.kind == "area";
                bool broken = f.mode == "off";
                Color col = Hex(!string.IsNullOrEmpty(t?.col) ? t.col : f.color, Color.white);
                float intensity = t != null && t.i >= 0f ? t.i : f.intensity;
                float range = t != null && t.range >= 0f ? t.range : f.range;
                float glow = t?.glow ?? 1f;

                // where the light goes: the Phase 5 fixture's emitter if it is in the scene, else the layout position
                var fixtureRoot = FindFixture(scene, f.id, fixtureFile, out var emitter, out var filePos);
                Vector3 pos = filePos ?? V(f.pos);
                var go = new GameObject((emitter != null ? OutsidePrefix : "") + "Light_" + f.id + (broken ? " (broken)" : ""));
                if (emitter != null)
                {
                    go.transform.SetParent(emitter, false);
                    onEmitter++;
                }
                else
                {
                    go.transform.SetParent(parent, false);
                    go.transform.position = pos;
                }
                if (f.kind == "sconce") go.transform.position = OffWall(go.transform.position, layout, SconceWallGap);

                Light light = null;
                if (!broken)
                {
                    light = go.AddComponent<Light>();
                    light.color = col;
                    light.intensity = intensity;
                    if (area)
                    {
                        areas.TryGetValue(f.id, out var a);
                        light.type = LightType.Rectangle;
                        light.areaSize = a?.size != null && a.size.Length == 2 ? new Vector2(a.size[0], a.size[1]) : new Vector2(1.6f, 1.5f);
                        var face = a?.face != null ? V(a.face) : Vector3.forward;
                        go.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
                        light.lightmapBakeType = LightmapBakeType.Baked;
                        light.shadows = LightShadows.Soft;
                    }
                    else
                    {
                        light.type = LightType.Point;
                        light.range = range;
                        light.shadows = LightShadows.Soft;
                        light.shapeRadius = f.kind == "lamp" ? 0.12f : f.kind == "bulb" ? 0.03f : 0.08f; // baked softness
                        light.lightmapBakeType = f.mode == "Mixed" ? LightmapBakeType.Mixed
                            : f.mode == "Realtime" ? LightmapBakeType.Realtime : LightmapBakeType.Baked;
                        bool hero = f.id == "H4" || f.id == "L2" || f.id == "C1" || f.id == "C2";
                        SetShadowTier(light, hero ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium
                                                  : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow);
                        // Realtime basement bulbs: full strength, or they would leak 40% up through the slab.
                        light.shadowStrength = f.mode == "Realtime" ? 1f : 0.6f;
                        light.shadowNearPlane = 0.1f;
                        if (f.mode == "Mixed") mixed.Add(light);
                    }
                    lights++;
                    if (f.id == "C1")
                    {
                        var flicker = go.AddComponent<LightFlicker>();
                        flicker.amount = 0.18f;
                        flicker.speed = 7f;
                        flicker.dying = true;
                    }
                }
                counts[f.mode] = (counts.TryGetValue(f.mode, out var n) ? n : 0) + 1;

                if (area) continue; // windows: no shade
                Renderer[] shades = fixtureRoot != null ? ShadeRenderers(fixtureRoot) : new Renderer[0];
                if (shades.Length == 0 && shadeMat != null)
                {
                    shades = Placeholder(placeholders, f.id, f.kind, pos, shadeMat);
                    placeholder++;
                }
                bool startOn = !broken && f.id != "C2";
                if (light != null && !startOn) light.enabled = false;
                var practical = go.AddComponent<PracticalLight>();
                float baseIntensity = broken ? 0f : intensity;
                practical.Configure(f.id, light, shades, col * glow, startOn, baseIntensity, f.mode == "Realtime", t?.fizz ?? 0f);
            }

            var sb = new StringBuilder();
            sb.Append($"fixtures: {lights} lights ({Join(counts)}), on Phase 5 emitters {onEmitter}, placeholder shades {placeholder}");
            sb.Append($"; mixed overlap clique max {MaxOverlap(mixed)} (shadowmask channels <= 4)");
            return sb.ToString();
        }

        const float SconceWallGap = 0.16f; // a sconce bulb sits ~16 cm off the wall; closer burns a hot spot into it

        /// <summary>Pushes a wall-mounted light out of the nearest wall of the room it is in, to at least gap metres.</summary>
        static Vector3 OffWall(Vector3 p, Layout layout, float gap)
        {
            foreach (var r in layout.rooms)
            {
                Vector3 mn = V(r.min), mx = V(r.max);
                if (p.x < mn.x - 0.01f || p.x > mx.x + 0.01f || p.z < mn.z - 0.01f || p.z > mx.z + 0.01f || p.y < mn.y || p.y > mx.y) continue;
                p.x = Mathf.Clamp(p.x, mn.x + gap, mx.x - gap);
                p.z = Mathf.Clamp(p.z, mn.z + gap, mx.z - gap);
                return p;
            }
            return p;
        }

        /// <summary>
        /// Re-applies only the look (volume profiles, fog, ambient) from the atmosphere JSON, without touching lights or
        /// probes, so grading can be iterated without a rebake.
        /// </summary>
        public static string RefreshLook(Scene scene)
        {
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            var atmo = LoadAtmosphere();
            if (atmo.tuning?.post != null) Profile("VP_Bungalow_Global", atmo.tuning.post, true);
            if (atmo.tuning?.rooms != null) foreach (var p in atmo.tuning.rooms) Profile("VP_Room_" + p.name, p, false);
            Lightmapping.TryGetLightingSettings(out var ls);
            var r = ApplyEnvironment(atmo, ls != null && ls.lightmapResolution < 15f);
            EditorSceneManager.MarkSceneDirty(scene);
            return r;
        }

        /// <summary>URP per-light shadow resolution tier (the property setter only works in Play mode).</summary>
        public static void SetShadowTier(Light light, int tier)
        {
            var so = new SerializedObject(light.GetUniversalAdditionalLightData());
            so.FindProperty("m_UsePipelineSettings").boolValue = false;
            so.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = tier;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Largest set of Mixed lights whose ranges all overlap one point (checked on a 0.5 m grid).</summary>
        static int MaxOverlap(List<Light> mixed)
        {
            if (mixed.Count == 0) return 0;
            var b = new Bounds(mixed[0].transform.position, Vector3.zero);
            foreach (var l in mixed) b.Encapsulate(new Bounds(l.transform.position, Vector3.one * l.range * 2f));
            int best = 0;
            for (float x = b.min.x; x <= b.max.x; x += 0.5f)
            for (float y = b.min.y; y <= b.max.y; y += 0.5f)
            for (float z = b.min.z; z <= b.max.z; z += 0.5f)
            {
                var p = new Vector3(x, y, z);
                int c = 0;
                foreach (var l in mixed) if ((l.transform.position - p).sqrMagnitude < l.range * l.range) c++;
                best = Mathf.Max(best, c);
            }
            return best;
        }

        /// <summary>
        /// The Phase 5 fixture for this id (Editor/BungalowPropsPlacer: instance "FX_&lt;id&gt;" under Props/Fixtures, listed in
        /// Props/fixtures.json with its emitterWorld and emitterChild "&lt;Model&gt;_Emitter"). The light is parented to the
        /// emitter child; the fixture's M_Shade renderers glow. Without the instance, fixtures.json's emitterWorld is used.
        /// </summary>
        static Transform FindFixture(Scene scene, string id, FixtureFile file, out Transform emitter, out Vector3? filePos)
        {
            emitter = null;
            filePos = null;
            FixtureEntry entry = null;
            if (file?.fixtures != null) foreach (var e in file.fixtures) if (e.id == id) entry = e;
            if (entry != null)
            {
                if (entry.emitterWorld != null && entry.emitterWorld.Length == 3) filePos = V(entry.emitterWorld);
                else if (entry.emitter != null && entry.emitter.Length == 3) filePos = V(entry.emitter);
                else if (entry.pos != null && entry.pos.Length == 3) filePos = V(entry.pos);
            }
            Transform found = null;
            foreach (var t in AllTransforms(scene))
            {
                if (t == null || IsUnder(t, RootName)) continue;
                if (entry != null && (!string.IsNullOrEmpty(entry.name) && t.name == entry.name || !string.IsNullOrEmpty(entry.path) && PathOf(t).EndsWith(entry.path, StringComparison.Ordinal)))
                { found = t; break; }
                if (entry != null && !string.IsNullOrEmpty(entry.instance) && t.name == entry.instance) { found = t; break; }
                if (t.name == "FX_" + id || t.name == "Fixture_" + id) { found = t; break; }
            }
            if (found == null) return null;
            foreach (var c in found.GetComponentsInChildren<Transform>(true))
                if (entry != null && c.name == entry.emitterChild || c.name.EndsWith("Emitter", StringComparison.OrdinalIgnoreCase)) { emitter = c; break; }
            if (emitter == null) emitter = found;
            return found;
        }

        static Renderer[] ShadeRenderers(Transform fixture)
        {
            var list = new List<Renderer>();
            foreach (var r in fixture.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.StartsWith("M_Shade", StringComparison.Ordinal)) { list.Add(r); break; }
            return list.ToArray();
        }

        /// <summary>Simple glowing shade shapes until Phase 5's fixture meshes are in the scene. No shadows, no GI.</summary>
        static Renderer[] Placeholder(Transform parent, string id, string kind, Vector3 p, Material mat)
        {
            var list = new List<Renderer>();
            var root = Child(parent, "Shade_" + id);
            Action<PrimitiveType, Vector3, Vector3> part = (type, at, scale) =>
            {
                var go = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.position = at;
                go.transform.localScale = scale;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveGI = ReceiveGI.LightProbes;
                list.Add(r);
            };
            switch (kind)
            {
                case "dome": part(PrimitiveType.Sphere, p + Vector3.up * 0.05f, new Vector3(0.38f, 0.1f, 0.38f)); break;
                case "chand":
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI * 2f / 3f;
                        part(PrimitiveType.Cylinder, p + new Vector3(Mathf.Cos(a) * 0.3f, 0f, Mathf.Sin(a) * 0.3f), new Vector3(0.2f, 0.07f, 0.2f));
                    }
                    break;
                case "sconce": part(PrimitiveType.Cylinder, p + Vector3.up * 0.04f, new Vector3(0.13f, 0.06f, 0.13f)); break;
                case "lamp": part(PrimitiveType.Cylinder, p + Vector3.up * 0.04f, new Vector3(0.38f, 0.13f, 0.38f)); break;
                case "fan":
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI * 2f / 3f + 0.5f;
                        part(PrimitiveType.Sphere, p + new Vector3(Mathf.Cos(a) * 0.13f, -0.04f, Mathf.Sin(a) * 0.13f), Vector3.one * 0.11f);
                    }
                    break;
                default: part(PrimitiveType.Sphere, p, Vector3.one * 0.07f); break; // bare bulb
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ probes
        const float ProbeStep = 1.0f, ProbeInset = 0.4f, ProbeLift = 0.3f, ProbeMerge = 0.35f;

        static string BuildProbes(Transform root, Layout layout, Atmosphere atmo)
        {
            var parent = Child(root, "Probes");
            var lpParent = Child(parent, "LightProbes");
            var rpParent = Child(parent, "ReflectionProbes");
            var taken = new List<Vector3>();
            int probes = 0, skipped = 0, groups = 0;
            Box well = atmo.stairWell;

            foreach (var room in layout.rooms)
            {
                Vector3 mn = V(room.min), mx = V(room.max);
                var pts = new List<Vector3>();
                foreach (float x in Steps(mn.x + ProbeInset, mx.x - ProbeInset))
                foreach (float z in Steps(mn.z + ProbeInset, mx.z - ProbeInset))
                {
                    // the floor under this column: the room floor, or the stair / basement floor where the well is
                    float floorY = mn.y;
                    var origin = new Vector3(x, mx.y - 0.05f, z);
                    if (Physics.Raycast(origin, Vector3.down, out var hit, mx.y - mn.y + 4f, ~0, QueryTriggerInteraction.Ignore))
                        floorY = hit.point.y < mn.y - 0.2f ? hit.point.y : Mathf.Max(mn.y, Mathf.Min(hit.point.y, mn.y + 0.05f));
                    foreach (float y in Steps(floorY + ProbeLift, mx.y - ProbeLift))
                    {
                        var p = new Vector3(x, y, z);
                        if (Physics.CheckSphere(p, 0.08f, ~0, QueryTriggerInteraction.Ignore)) { skipped++; continue; }
                        if (Near(taken, p)) continue;
                        taken.Add(p);
                        pts.Add(p);
                    }
                }
                probes += AddGroup(lpParent, "LP_" + room.id, pts);
                groups++;

                // reflection probe: the room box
                AddReflection(rpParent, "RP_" + room.id, (mn + mx) * 0.5f, mx - mn, 1);
            }
            if (well != null && well.min != null)
            {
                Vector3 mn = V(well.min), mx = V(well.max);
                AddReflection(rpParent, "RP_" + well.id, (mn + mx) * 0.5f, mx - mn, 2);
            }
            return $"probes: {groups} light-probe groups, {probes} probes ({skipped} inside geometry skipped), {layout.rooms.Length + (well != null ? 1 : 0)} reflection probes";
        }

        static IEnumerable<float> Steps(float a, float b)
        {
            if (b <= a) { yield return (a + b) * 0.5f; yield break; }
            int n = Mathf.CeilToInt((b - a) / ProbeStep);
            for (int i = 0; i <= n; i++) yield return Mathf.Lerp(a, b, (float)i / n);
        }

        static bool Near(List<Vector3> pts, Vector3 p)
        {
            foreach (var q in pts) if ((q - p).sqrMagnitude < ProbeMerge * ProbeMerge) return true;
            return false;
        }

        static int AddGroup(Transform parent, string name, List<Vector3> pts)
        {
            var go = Child(parent, name).gameObject;
            var g = go.AddComponent<LightProbeGroup>();
            g.probePositions = pts.ToArray(); // the group sits at the origin, so local = world
            return pts.Count;
        }

        static void AddReflection(Transform parent, string name, Vector3 centre, Vector3 size, int importance)
        {
            var go = Child(parent, name).gameObject;
            go.transform.position = centre;
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = size;
            rp.center = Vector3.zero;
            rp.resolution = 128;
            rp.hdr = true;
            rp.importance = importance;
            rp.blendDistance = 0.5f;
            rp.clearFlags = ReflectionProbeClearFlags.SolidColor;
            rp.backgroundColor = Color.black;
            rp.nearClipPlane = 0.05f;
            rp.shadowDistance = 30f;
        }

        // ------------------------------------------------------------------ post-processing
        static string BuildVolumes(Transform root, Layout layout, Atmosphere atmo)
        {
            var parent = Child(root, "Volumes");
            var post = atmo.tuning?.post;
            int n = 0;
            if (post != null)
            {
                var g = Child(parent, "Volume_Global").gameObject;
                var v = g.AddComponent<Volume>();
                v.isGlobal = true;
                v.priority = 0;
                v.sharedProfile = Profile("VP_Bungalow_Global", post, true);
                n++;
            }
            var rects = new Dictionary<string, Room>();
            foreach (var r in layout.rooms) rects[r.id] = r;
            if (atmo.tuning?.rooms != null)
                foreach (var p in atmo.tuning.rooms)
                {
                    var go = Child(parent, "Volume_" + p.name).gameObject;
                    foreach (var id in p.rooms)
                    {
                        if (!rects.TryGetValue(id, out var r)) continue;
                        var box = go.AddComponent<BoxCollider>();
                        box.isTrigger = true;
                        box.center = (V(r.min) + V(r.max)) * 0.5f;
                        box.size = V(r.max) - V(r.min);
                    }
                    var v = go.AddComponent<Volume>();
                    v.isGlobal = false;
                    v.priority = 1;
                    v.blendDistance = 1f;
                    v.sharedProfile = Profile("VP_Room_" + p.name, p, false);
                    n++;
                }
            return $"volumes: {n} (global + room grades), profiles in {LightingDir}";
        }

        static VolumeProfile Profile(string name, Post p, bool global)
        {
            string path = $"{LightingDir}/{name}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (var c in profile.components.ToArray())
            {
                AssetDatabase.RemoveObjectFromAsset(c);
                Object.DestroyImmediate(c, true);
            }
            profile.components.Clear();

            if (global)
            {
                var tm = Add<Tonemapping>(profile);
                tm.mode.Override(TonemappingMode.ACES);
            }
            var ca = Add<ColorAdjustments>(profile);
            ca.postExposure.Override(p.exposure);
            ca.contrast.Override(p.contrast);
            ca.saturation.Override(p.saturation);
            ca.colorFilter.Override(Hex(p.filter, Color.white));
            if (p.temperature != 0f || p.tint != 0f)
            {
                var wb = Add<WhiteBalance>(profile);
                wb.temperature.Override(p.temperature);
                wb.tint.Override(p.tint);
            }
            if (p.vignette >= 0f)
            {
                var vg = Add<Vignette>(profile);
                vg.intensity.Override(p.vignette);
                vg.smoothness.Override(0.45f);
                vg.color.Override(Color.black);
            }
            if (p.ca >= 0f) Add<ChromaticAberration>(profile).intensity.Override(p.ca);
            if (p.lift != null && p.lift.Length == 4)
                Add<LiftGammaGain>(profile).lift.Override(new Vector4(p.lift[0], p.lift[1], p.lift[2], p.lift[3]));
            if (!string.IsNullOrEmpty(p.splitShadows))
            {
                var st = Add<SplitToning>(profile);
                st.shadows.Override(Hex(p.splitShadows, Color.grey));
                st.highlights.Override(Hex(p.splitHighlights, Color.grey));
            }
            if (p.bloom >= 0f)
            {
                var b = Add<Bloom>(profile);
                b.intensity.Override(p.bloom);
                if (p.threshold >= 0f) b.threshold.Override(p.threshold);
                if (p.scatter >= 0f) b.scatter.Override(p.scatter);
                if (!string.IsNullOrEmpty(p.bloomTint)) b.tint.Override(Hex(p.bloomTint, Color.white));
                b.highQualityFiltering.Override(true);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(false);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ------------------------------------------------------------------ environment, lightmapping, day/night
        static string ApplyEnvironment(Atmosphere atmo, bool provisional)
        {
            var g = atmo.tuning?.globals;
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Flat; // "Color": no skybox contribution
            RenderSettings.ambientLight = Hex(g?.ambient, new Color(0.04f, 0.043f, 0.031f));
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null; // black outside the baked reflection probes
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = g?.fogMode == "ExponentialSquared" ? FogMode.ExponentialSquared : FogMode.Exponential;
            RenderSettings.fogColor = Hex(g?.fog, new Color(0.078f, 0.078f, 0.031f));
            RenderSettings.fogDensity = g != null && g.fogDensity > 0f ? g.fogDensity : 0.15f;

            var ls = Settings(provisional);
            Lightmapping.lightingSettings = ls;
            return $"environment: ambient {ColorUtility.ToHtmlStringRGB(RenderSettings.ambientLight)}, fog {RenderSettings.fogMode} " +
                   $"{RenderSettings.fogDensity} #{ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor)}, lighting settings {SettingsPath} " +
                   $"({ls.lightmapResolution} texels/m, {ls.lightmapMaxSize}, {ls.mixedBakeMode})";
        }

        /// <summary>The scene's LightingSettings asset (SPEC 5): GPU Progressive, Shadowmask, 20 texels/m, AO, 2 bounces.</summary>
        public static LightingSettings Settings(bool provisional)
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "LS_Bungalow" };
                AssetDatabase.CreateAsset(ls, SettingsPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.mixedBakeMode = MixedLightingMode.Shadowmask;
            ls.lightmapResolution = provisional ? 10f : 20f;
            ls.lightmapPadding = 4;
            ls.lightmapMaxSize = provisional ? 1024 : 2048;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            ls.ao = true;
            ls.aoMaxDistance = 0.6f;
            ls.aoExponentIndirect = 1f;
            ls.aoExponentDirect = 0f;
            ls.maxBounces = 2;
            ls.directSampleCount = provisional ? 16 : 32;
            ls.indirectSampleCount = provisional ? 96 : 384;
            ls.environmentSampleCount = provisional ? 32 : 128;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            ls.albedoBoost = 1f;
            ls.indirectScale = 1.4f; // Phase 6 match: the refs have more soft bounce fill than 2 physical bounces give
            ls.autoGenerate = false;
            EditorUtility.SetDirty(ls);
            AssetDatabase.SaveAssetIfDirty(ls);
            return ls;
        }

        /// <summary>NightSky (formerly DayNightCycle) off in this scene only (prefab-instance override), keeping the night look.</summary>
        static string DisableDayNight(Scene scene)
        {
            int n = 0;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var c in go.GetComponentsInChildren<Blink.DayNight.NightSky>(true))
                {
                    if (c.enabled)
                    {
                        Undo.RecordObject(c, "Disable NightSky");
                        c.enabled = false;
                    }
                    if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    n++;
                }
            return $"NightSky disabled in this scene: {n}";
        }

        /// <summary>
        /// With NightSky off there is no sky: the scene's cameras (the Player instance) clear to the fog colour
        /// instead of Skybox, so windows show a dark night rather than the camera's default blue. Scene override only.
        /// </summary>
        static string NightCameras(Scene scene)
        {
            int n = 0;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var cam in go.GetComponentsInChildren<Camera>(true))
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = RenderSettings.fogColor;
                    if (PrefabUtility.IsPartOfPrefabInstance(cam)) PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
                    n++;
                }
            return $"cameras clearing to the fog colour: {n}";
        }

        /// <summary>
        /// Shadowmask (not Distance Shadowmask) on every quality level (decision 7): static shadows of the Mixed lights
        /// come from the baked shadowmask, so only dynamic objects (player, zombies) go into their realtime shadow maps.
        /// Writes only ProjectSettings/QualitySettings.asset. Returns what changed.
        /// </summary>
        public static string EnsureShadowmaskQuality()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(asset);
            var levels = so.FindProperty("m_QualitySettings");
            var sb = new StringBuilder();
            for (int i = 0; i < levels.arraySize; i++)
            {
                var l = levels.GetArrayElementAtIndex(i);
                var mode = l.FindPropertyRelative("shadowmaskMode");
                if (mode.intValue != (int)ShadowmaskMode.Shadowmask)
                {
                    sb.Append($"{l.FindPropertyRelative("name").stringValue}: shadowmaskMode {(ShadowmaskMode)mode.intValue} -> Shadowmask; ");
                    mode.intValue = (int)ShadowmaskMode.Shadowmask;
                }
            }
            if (so.ApplyModifiedPropertiesWithoutUndo())
            {
                AssetDatabase.SaveAssetIfDirty(asset);
                QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
            }
            return sb.Length == 0 ? "quality: already Shadowmask" : "quality: " + sb;
        }

        /// <summary>
        /// Measured lightmap texel density (texels per metre) of a baked renderer: sqrt(lightmap texels its UV2 covers /
        /// world surface area). -1 when it has no lightmap.
        /// </summary>
        public static float LightmapDensity(MeshRenderer r)
        {
            var lms = LightmapSettings.lightmaps;
            if (r.lightmapIndex < 0 || r.lightmapIndex >= lms.Length) return -1f;
            var mf = r.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            if (m == null || !m.isReadable && !Application.isEditor) return -1f;
            var uv2 = m.uv2;
            if (uv2 == null || uv2.Length == 0) uv2 = m.uv;
            var v = m.vertices;
            var t = m.triangles;
            var tr = r.transform;
            double world = 0, uv = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
                world += Vector3.Cross(b - a, c - a).magnitude * 0.5;
                Vector2 p = uv2[t[i]], q = uv2[t[i + 1]], s = uv2[t[i + 2]];
                uv += Math.Abs((q.x - p.x) * (s.y - p.y) - (s.x - p.x) * (q.y - p.y)) * 0.5;
            }
            var tex = lms[r.lightmapIndex].lightmapColor;
            float size = tex != null ? tex.width : 1024f;
            var so = r.lightmapScaleOffset;
            return world > 0 ? (float)Math.Sqrt(uv * so.x * so.y * size * size / world) : -1f;
        }

        /// <summary>
        /// After a bake: rescales scaleInLightmap on renderers whose measured texel density is far from the scene's
        /// lightmap resolution (compact or repeated UV2 charts, e.g. radiators, balustrades), so the next bake is even.
        /// </summary>
        public static string NormalizeLightmapDensity(Scene scene)
        {
            Lightmapping.TryGetLightingSettings(out var ls);
            float target = ls != null ? ls.lightmapResolution : 20f;
            int changed = 0, checkedN = 0;
            var sb = new StringBuilder();
            foreach (var go in scene.GetRootGameObjects())
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    float d = LightmapDensity(r);
                    if (d <= 0f) continue;
                    checkedN++;
                    // density scales linearly with scaleInLightmap; only lift the under-resolved ones (props keep their
                    // deliberate lower scale as long as they reach 60% of the target)
                    if (d >= 0.6f * target * Mathf.Min(1f, r.scaleInLightmap)) continue;
                    float before = r.scaleInLightmap;
                    r.scaleInLightmap = Mathf.Clamp(r.scaleInLightmap * Mathf.Clamp(target * Mathf.Min(1f, before) / d, 1f, 6f), 0.05f, 8f);
                    EditorUtility.SetDirty(r);
                    changed++;
                    if (changed <= 12) sb.Append($"{r.name} {d:0.0}/m x{before:0.##}->{r.scaleInLightmap:0.##}; ");
                }
            return $"lightmap density: {checkedN} renderers checked against {target}/m, {changed} rescaled. {sb}";
        }

        // ------------------------------------------------------------------ match renders (Phase 6/7 checks)
        /// <summary>
        /// Renders the layout's reference-match cameras (SPEC 5.3) with the Player camera's settings and post, at each
        /// reference's size, to outDir/&lt;CAM&gt;.png. Edit mode: uses the baked lighting in the open scene.
        /// </summary>
        public static string RenderMatchCameras(string outDir, bool flashlight)
        {
            var layout = LoadLayout();
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            foreach (var c in layout.cameras)
            {
                string file = Path.Combine(outDir, c.id + (flashlight ? "_torch" : "") + ".png");
                Render(V(c.pos), Quaternion.Euler(-c.pitch, c.yaw, 0f), c.fov, c.w, c.h, flashlight, file);
                sb.AppendLine(file);
            }
            return sb.ToString();
        }

        /// <summary>One player-eye render (Player camera settings + post, optional flashlight) to a PNG.</summary>
        public static void Render(Vector3 pos, Quaternion rot, float fov, int w, int h, bool flashlight, string file)
        {
            var go = new GameObject("BL_RenderCam") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                go.transform.SetPositionAndRotation(pos, rot);
                var cam = go.AddComponent<Camera>();
                var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
                var src = player != null ? player.GetComponentInChildren<Camera>(true) : null;
                if (src != null) cam.CopyFrom(src);
                cam.transform.SetPositionAndRotation(pos, rot);
                cam.fieldOfView = fov;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.dithering = true;
                if (flashlight && player != null)
                {
                    var torch = player.GetComponentInChildren<Blink.Player.Flashlight>(true);
                    if (torch != null)
                    {
                        var l = Object.Instantiate(torch.gameObject, go.transform, false);
                        l.hideFlags = HideFlags.HideAndDontSave;
                        l.transform.localPosition = torch.transform.localPosition;
                        l.transform.localRotation = torch.transform.localRotation;
                        Object.DestroyImmediate(l.GetComponent<Blink.Player.Flashlight>());
                    }
                }
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                cam.Render(); // the first render after a scene change misses lights / volumes: warm up, then keep the second
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------ helpers
        public static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        public static IEnumerable<Transform> AllTransforms(Scene scene)
        {
            var list = new List<Transform>();
            foreach (var go in scene.GetRootGameObjects()) list.AddRange(go.GetComponentsInChildren<Transform>(true));
            return list;
        }

        static bool IsUnder(Transform t, string rootName)
        {
            for (var p = t; p != null; p = p.parent) if (p.parent == null && p.name == rootName) return true;
            return false;
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        public static Vector3 V(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);

        public static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c) ? c : fallback;
        }

        static string Join(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add($"{kv.Key} {kv.Value}");
            return string.Join(", ", parts.ToArray());
        }
    }
}
