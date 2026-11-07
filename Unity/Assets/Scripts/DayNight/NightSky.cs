using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.DayNight
{
    /// <summary>
    /// Holds the world at night for the whole game: there is no day/night cycle. The scene's ambient, fog and
    /// directional light (RenderSettings.sun) at startup are the night look and are left as authored; the light
    /// is the moonlight. This adds the night sky: stars, and a moon disc in the direction that light comes from,
    /// so the moonlight slants through the windows the level was lit for. Independent of <see cref="GameClock"/>.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class NightSky : MonoBehaviour
    {
        [Tooltip("The moonlight. Defaults to RenderSettings.sun, else the first directional light.")]
        [SerializeField] Light celestialLight;
        [Tooltip("Art/Sky/BlinkSky.mat. An instance is made at runtime, the asset isn't changed.")]
        [SerializeField] Material skyMaterial;

        [SerializeField] Color nightZenith = new(0.008f, 0.012f, 0.03f);
        [SerializeField] Color nightHorizon = new(0.045f, 0.06f, 0.095f);
        [SerializeField] Color moonDiscColor = new(0.85f, 0.9f, 1f);
        [SerializeField, Range(0f, 3f)] float stars = 1f;

        Material _sky;
        Camera _cam;

        static readonly int ZenithId = Shader.PropertyToID("_ZenithColor"), HorizonId = Shader.PropertyToID("_HorizonColor"),
            GroundId = Shader.PropertyToID("_GroundColor"), SunDirId = Shader.PropertyToID("_SunDir"),
            SunColorId = Shader.PropertyToID("_SunColor"), MoonDirId = Shader.PropertyToID("_MoonDir"),
            MoonColorId = Shader.PropertyToID("_MoonColor"), StarsId = Shader.PropertyToID("_StarIntensity");

        void Start()
        {
            if (celestialLight == null) celestialLight = RenderSettings.sun;
            if (celestialLight == null)
                foreach (var l in FindObjectsByType<Light>())
                    if (l.type == LightType.Directional) { celestialLight = l; break; }
            if (celestialLight != null) RenderSettings.sun = celestialLight;

            // URP shades with the ambient probe, which isn't regenerated from ambientLight at runtime.
            RenderSettings.ambientMode = AmbientMode.Flat; // ambientLight is ignored in Skybox mode
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(RenderSettings.ambientLight);
            RenderSettings.ambientProbe = sh;

            if (skyMaterial == null)
            {
                var shader = Shader.Find("Blink/Sky");
                if (shader != null) skyMaterial = new Material(shader);
            }
            if (skyMaterial == null)
            {
                Debug.LogWarning("[NightSky] No sky material; assign Art/Sky/BlinkSky.mat.");
                return;
            }
            _sky = new Material(skyMaterial);
            RenderSettings.skybox = _sky;

            Vector3 moonDir = celestialLight != null ? -celestialLight.transform.forward : new Vector3(0f, 0.5f, 0.85f);
            if (moonDir.y < 0.1f) moonDir.y = 0.1f; // keep the disc above the horizon
            _sky.SetColor(ZenithId, nightZenith);
            _sky.SetColor(HorizonId, nightHorizon);
            _sky.SetColor(GroundId, RenderSettings.fogColor);
            _sky.SetVector(SunDirId, Vector3.down);
            _sky.SetColor(SunColorId, Color.clear);
            _sky.SetVector(MoonDirId, moonDir.normalized);
            _sky.SetColor(MoonColorId, moonDiscColor);
            _sky.SetFloat(StarsId, stars);
        }

        void OnDestroy()
        {
            if (_sky != null) Destroy(_sky);
        }

        void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null && _cam.clearFlags != CameraClearFlags.Skybox) _cam.clearFlags = CameraClearFlags.Skybox;
        }
    }
}
