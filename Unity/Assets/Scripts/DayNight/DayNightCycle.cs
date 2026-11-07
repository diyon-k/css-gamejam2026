using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.DayNight
{
    /// <summary>
    /// Drives the sky, the directional light, ambient light and fog from <see cref="GameClock"/>.
    /// One directional light (RenderSettings.sun) is the sun by day and the moon by night; it fades out
    /// near the horizon so the swap at dawn/dusk is invisible. The scene's ambient, fog and light values
    /// at startup are the NIGHT look; day and dusk colours are set here. Sun and moon arcs peak in the
    /// direction the scene's light came from, so moonlight still slants through the same windows.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Tooltip("Defaults to GameClock.Instance.")]
        [SerializeField] GameClock clock;
        [Tooltip("Defaults to RenderSettings.sun, else the first directional light.")]
        [SerializeField] Light celestialLight;
        [Tooltip("Art/Sky/BlinkSky.mat. An instance is made at runtime, the asset isn't changed.")]
        [SerializeField] Material skyMaterial;

        [Header("Sky path")]
        [SerializeField, Range(10f, 90f)] float maxElevation = 55f;

        [Header("Day (muted overcast)")]
        [SerializeField] Color sunColor = new(0.95f, 0.92f, 0.85f);
        [SerializeField, Min(0f)] float sunIntensity = 1.1f;
        [SerializeField] Color dayAmbient = new(0.36f, 0.37f, 0.39f);
        [SerializeField] Color dayFog = new(0.44f, 0.46f, 0.48f);
        [SerializeField, Min(0f)] float dayFogDensity = 0.02f;
        [SerializeField] Color dayZenith = new(0.34f, 0.39f, 0.46f);
        [SerializeField] Color dayHorizon = new(0.55f, 0.57f, 0.59f);

        [Header("Dawn / dusk")]
        [SerializeField] Color duskSun = new(1f, 0.5f, 0.25f);
        [SerializeField] Color duskAmbient = new(0.3f, 0.2f, 0.16f);
        [SerializeField] Color duskFog = new(0.35f, 0.22f, 0.16f);
        [SerializeField] Color duskHorizon = new(0.75f, 0.36f, 0.16f);

        [Header("Night (ambient/fog/moonlight come from the scene)")]
        [SerializeField] Color nightZenith = new(0.008f, 0.012f, 0.03f);
        [SerializeField] Color nightHorizon = new(0.045f, 0.06f, 0.095f);
        [SerializeField] Color moonDiscColor = new(0.85f, 0.9f, 1f);

        Color _nightAmbient, _nightFog, _moonColor;
        float _nightFogDensity, _moonIntensity, _yaw;
        Material _sky;
        Camera _cam;

        static readonly int ZenithId = Shader.PropertyToID("_ZenithColor"), HorizonId = Shader.PropertyToID("_HorizonColor"),
            GroundId = Shader.PropertyToID("_GroundColor"), SunDirId = Shader.PropertyToID("_SunDir"),
            SunColorId = Shader.PropertyToID("_SunColor"), MoonDirId = Shader.PropertyToID("_MoonDir"),
            MoonColorId = Shader.PropertyToID("_MoonColor"), StarsId = Shader.PropertyToID("_StarIntensity");

        void Start()
        {
            if (clock == null) clock = GameClock.Instance;
            if (clock == null)
            {
                Debug.LogError("[DayNightCycle] No GameClock found; disabling.");
                enabled = false;
                return;
            }

            if (celestialLight == null) celestialLight = RenderSettings.sun;
            if (celestialLight == null)
                foreach (var l in FindObjectsByType<Light>())
                    if (l.type == LightType.Directional) { celestialLight = l; break; }

            // Night baseline = whatever the level set up.
            RenderSettings.ambientMode = AmbientMode.Flat; // ambientLight is ignored in Skybox mode
            _nightAmbient = RenderSettings.ambientLight;
            _nightFog = RenderSettings.fogColor;
            _nightFogDensity = RenderSettings.fogDensity;
            _moonColor = celestialLight != null ? celestialLight.color : new Color(0.55f, 0.62f, 0.8f);
            _moonIntensity = celestialLight != null ? celestialLight.intensity : 0.3f;
            if (celestialLight != null)
            {
                Vector3 towards = -celestialLight.transform.forward;
                _yaw = Mathf.Atan2(towards.x, towards.z) * Mathf.Rad2Deg;
                RenderSettings.sun = celestialLight;
            }

            if (skyMaterial == null)
            {
                var shader = Shader.Find("Blink/Sky");
                if (shader != null) skyMaterial = new Material(shader);
            }
            if (skyMaterial != null)
            {
                _sky = new Material(skyMaterial);
                RenderSettings.skybox = _sky;
            }
            else Debug.LogWarning("[DayNightCycle] No sky material; assign Art/Sky/BlinkSky.mat.");
        }

        void OnDestroy()
        {
            if (_sky != null) Destroy(_sky);
        }

        void LateUpdate()
        {
            EnsureSkyCamera();

            float hour = clock.HourOfDay, rise = clock.SunriseHour, set = clock.SunsetHour;
            float dayLength = Mathf.Repeat(set - rise, 24f);
            float sinceRise = Mathf.Repeat(hour - rise, 24f);
            bool isDay = sinceRise < dayLength;
            float p = isDay ? sinceRise / dayLength : (sinceRise - dayLength) / (24f - dayLength);

            // The body that is up follows an east-to-west arc; the other continues its arc under the ground.
            Vector3 up = Arc(p);
            Vector3 down = Arc(1f + p);
            Vector3 sunDir = isDay ? up : down, moonDir = isDay ? down : up;
            float sunY = sunDir.y;

            float dayness = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.15f, sunY));
            float twilight = 1f - Mathf.Clamp01(Mathf.Abs(sunY) / 0.25f);

            if (celestialLight != null)
            {
                Vector3 lit = isDay ? sunDir : moonDir;
                celestialLight.transform.rotation = Quaternion.LookRotation(-lit);
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, lit.y));
                if (isDay)
                {
                    celestialLight.color = Color.Lerp(duskSun, sunColor, Mathf.InverseLerp(0f, 0.35f, sunY));
                    celestialLight.intensity = sunIntensity * fade;
                }
                else
                {
                    celestialLight.color = _moonColor;
                    celestialLight.intensity = _moonIntensity * fade;
                }
            }

            RenderSettings.ambientLight = Color.Lerp(Color.Lerp(_nightAmbient, dayAmbient, dayness), duskAmbient, twilight * 0.6f);
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(_nightFog, dayFog, dayness), duskFog, twilight * 0.6f);
            RenderSettings.fogDensity = Mathf.Lerp(_nightFogDensity, dayFogDensity, dayness);

            if (_sky == null) return;
            Color horizon = Color.Lerp(Color.Lerp(nightHorizon, dayHorizon, dayness), duskHorizon, twilight * 0.7f);
            _sky.SetColor(ZenithId, Color.Lerp(nightZenith, dayZenith, dayness));
            _sky.SetColor(HorizonId, horizon);
            _sky.SetColor(GroundId, RenderSettings.fogColor);
            _sky.SetVector(SunDirId, sunDir);
            var sun = Color.Lerp(duskSun, sunColor, dayness);
            sun.a = 0.6f * Mathf.InverseLerp(-0.1f, 0.05f, sunY); // overcast: a pale sun
            _sky.SetColor(SunColorId, sun);
            _sky.SetVector(MoonDirId, moonDir);
            var moon = moonDiscColor;
            moon.a = 1f - dayness;
            _sky.SetColor(MoonColorId, moon);
            _sky.SetFloat(StarsId, (1f - dayness) * (1f - dayness));
        }

        // Direction towards a body p (0..1) of the way across the sky: rises at -x, sets at +x,
        // peaks at maxElevation in the scene light's original direction.
        Vector3 Arc(float p)
        {
            float a = p * Mathf.PI, e = maxElevation * Mathf.Deg2Rad;
            var local = new Vector3(-Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
            return Quaternion.Euler(0f, _yaw, 0f) * local;
        }

        void EnsureSkyCamera()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null && _cam.clearFlags != CameraClearFlags.Skybox) _cam.clearFlags = CameraClearFlags.Skybox;
        }
    }
}
