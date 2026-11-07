using System.Collections.Generic;
using UnityEngine;

namespace Blink.Visuals
{
    /// <summary>
    /// A lamp in the level: its light (may be none, e.g. a broken sconce) and the shade / bulb renderers that glow
    /// with it. The glow is a per-renderer emission override (MaterialPropertyBlock), so every fixture shares one
    /// shade material and lit lamps glow while broken or switched-off ones don't. With <see cref="followLight"/>
    /// the glow tracks the light's intensity, so a <see cref="LightFlicker"/> on the light flickers the bulb too.
    /// Story scripts switch fixtures by id: <c>PracticalLight.Find("C2")?.SetOn(true)</c> (the generator bulb).
    /// Set up by Editor/BungalowLightingBuilder.
    /// </summary>
    [ExecuteAlways]
    public sealed class PracticalLight : MonoBehaviour
    {
        static readonly List<PracticalLight> All = new List<PracticalLight>();
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Fixture id from the layout (SPEC 5.1), e.g. C2.")]
        [SerializeField] string id;
        [SerializeField] Light lightSource;
        [SerializeField] Renderer[] shades = new Renderer[0];
        [Tooltip("Shade glow when on at the light's base intensity (HDR: colour x strength).")]
        [SerializeField, ColorUsage(false, true)] Color emission = Color.black;
        [SerializeField] bool on = true;
        [Tooltip("Scale the glow by the light's current / base intensity (flicker, dimming).")]
        [SerializeField] bool followLight = true;
        [Tooltip("The light's intensity when fully on. Set by the builder.")]
        [SerializeField, Min(0f)] float baseIntensity = 1f;
        [Tooltip("Emission-only flicker (a fizzing shade on a baked light), 0 = steady.")]
        [SerializeField, Range(0f, 1f)] float emissionFlicker;

        MaterialPropertyBlock _block;
        float _seed;
        float _lastLevel = -1f;

        public string Id => id;
        public bool IsOn => on;
        public Light Light => lightSource;

        /// <summary>The fixture with this id among the enabled ones, or null.</summary>
        public static PracticalLight Find(string fixtureId)
        {
            foreach (var p in All)
                if (p != null && p.id == fixtureId) return p;
            return null;
        }

        public void SetOn(bool value)
        {
            on = value;
            if (lightSource != null)
            {
                lightSource.enabled = value;
                if (value && lightSource.intensity <= 0f) lightSource.intensity = baseIntensity;
            }
            _lastLevel = -1f;
            Apply(Level());
        }

        /// <summary>Editor setup (the lighting builder).</summary>
        public void Configure(string fixtureId, Light source, Renderer[] shadeRenderers, Color glow, bool isOn,
                              float intensity, bool follow, float fizz)
        {
            id = fixtureId;
            lightSource = source;
            shades = shadeRenderers ?? new Renderer[0];
            emission = glow;
            on = isOn;
            baseIntensity = intensity;
            followLight = follow;
            emissionFlicker = fizz;
            _lastLevel = -1f;
            Apply(Level());
        }

        void OnEnable()
        {
            All.Add(this);
            _seed = Random.value * 100f;
            _lastLevel = -1f;
            Apply(Level());
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        void OnValidate()
        {
            _lastLevel = -1f;
            if (isActiveAndEnabled) Apply(Level());
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            float level = Level();
            if (Mathf.Abs(level - _lastLevel) > 0.002f) Apply(level);
        }

        float Level()
        {
            if (!on) return 0f;
            float k = 1f;
            if (followLight && lightSource != null && baseIntensity > 0f)
                k = lightSource.enabled ? lightSource.intensity / baseIntensity : 0f;
            if (emissionFlicker > 0f && Application.isPlaying)
                k *= 1f - emissionFlicker * Mathf.PerlinNoise(_seed, Time.time * 9f);
            return Mathf.Max(0f, k);
        }

        void Apply(float level)
        {
            _lastLevel = level;
            if (shades == null) return;
            _block ??= new MaterialPropertyBlock();
            var c = emission * level;
            c.a = 1f;
            foreach (var r in shades)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(EmissionId, c);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
