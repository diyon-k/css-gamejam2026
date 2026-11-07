using Blink.Audio;
using Blink.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Blink.Effects
{
    /// <summary>
    /// Bloody screen-edge vignette for the player, in four stages, one per 25% of health lost:
    /// any damage = stage 1 (light), under 75% = 2, under 50% = 3, under 25% = 4 (heaviest).
    /// Healing walks it back down. Stages fade into each other instead of popping.
    /// Each heartbeat (<see cref="PlayerAudio.Heartbeat"/>) throbs the red; at stages 3-4 the body beats on its
    /// own too, faster at 4. The world also drains of colour as health falls (a runtime URP volume).
    ///
    /// Textures are generated at startup (no assets). Drawn with IMGUI, so no Canvas; sits under the
    /// HUD and the blink eyelids. Put it on the Player next to <see cref="Health"/>.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class DamageVignette : MonoBehaviour
    {
        [Tooltip("Optional overlay image per stage 1..4 (transparent centre, blood at the edges). Empty = generated.")]
        [SerializeField] Texture2D[] stageImages = new Texture2D[4];
        [Tooltip("Per stage 1..4: how far in from the edge the blood reaches (0 = edge only, 1 = centre).")]
        [SerializeField] float[] reach = { 0.12f, 0.24f, 0.36f, 0.5f };
        [Tooltip("Per stage 1..4: opacity of the blood.")]
        [SerializeField] float[] opacity = { 0.45f, 0.6f, 0.75f, 0.92f };
        [SerializeField] Color bloodColor = new(0.45f, 0.01f, 0.01f, 1f);
        [Tooltip("Stages per second when fading between stages.")]
        [SerializeField, Min(0.1f)] float fadeSpeed = 3f;
        [Tooltip("IMGUI depth: lower draws on top. HUD is 0, eyelids -1000.")]
        [SerializeField] int guiDepth = 10;

        [Header("Heartbeat")]
        [Tooltip("Per stage 1..4: how much the red dims between beats (0 = steady).")]
        [SerializeField] float[] pulseDepth = { 0f, 0.15f, 0.3f, 0.45f };
        [Tooltip("Per stage 1..4: the body's own beat when no zombie heartbeat is playing (0 = none).")]
        [SerializeField] float[] restingBpm = { 0f, 0f, 70f, 100f };
        [Tooltip("How long one throb takes to fade, in seconds.")]
        [SerializeField, Min(0.05f)] float throbSeconds = 0.35f;

        [Header("Colour drain")]
        [Tooltip("Saturation at stage 4 (URP Color Adjustments, -100 = grey). Scales in from 0 with the stage.")]
        [SerializeField, Range(-100f, 0f)] float lowHealthSaturation = -65f;

        const int TexSize = 256;

        Health _health;
        Texture2D[] _stages;
        float _shown; // 0 = clean .. 4 = heaviest, eased toward the target stage
        float _throb, _sinceBeat;
        PlayerAudio _heart;
        Volume _volume;
        VolumeProfile _profile;

        void Awake()
        {
            _health = GetComponent<Health>();
            _heart = FindAnyObjectByType<PlayerAudio>(); // the one heartbeat; null = own beat only
            if (_heart) _heart.Heartbeat += OnHeartbeat;
            BuildVolume();
            _stages = new Texture2D[4];
            for (int i = 0; i < 4; i++)
                _stages[i] = stageImages != null && i < stageImages.Length && stageImages[i] != null
                    ? stageImages[i]
                    : Build(reach[i]);
        }

        void OnDestroy()
        {
            if (_heart) _heart.Heartbeat -= OnHeartbeat;
            if (_volume) Destroy(_volume.gameObject);
            if (_profile) Destroy(_profile);
            if (_stages == null) return;
            for (int i = 0; i < _stages.Length; i++)
                if (_stages[i] != null && _stages[i].hideFlags == HideFlags.HideAndDontSave) Destroy(_stages[i]); // only the generated ones
        }

        /// <summary>0 at full health, then 1..4 for each 25% lost.</summary>
        public static int StageFor(float fraction) =>
            fraction >= 1f ? 0 : Mathf.Clamp(4 - Mathf.FloorToInt(Mathf.Max(fraction, 0f) * 4f), 1, 4);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            int stage = _health.IsDead ? 0 : StageFor(_health.Fraction);
            _shown = Mathf.MoveTowards(_shown, stage, fadeSpeed * dt);

            // The body's own beat; a zombie heartbeat resets the clock, so the two never double up.
            _sinceBeat += dt;
            float bpm = stage > 0 ? restingBpm[stage - 1] : 0f;
            if (bpm > 0f && _sinceBeat >= 60f / bpm) OnHeartbeat(0f, bpm);
            _throb = Mathf.MoveTowards(_throb, 0f, dt / throbSeconds);

            if (_volume) _volume.weight = _shown / 4f;
        }

        void OnHeartbeat(float intensity, float bpm)
        {
            _throb = 1f;
            _sinceBeat = 0f;
        }

        /// <summary>Global volume that only desaturates; its weight follows the stage.</summary>
        void BuildVolume()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var grade = _profile.Add<ColorAdjustments>();
            grade.saturation.Override(lowHealthSaturation);
            var go = new GameObject("DamageVignette Volume");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f; // over the scene's own grading
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
        }

        void OnGUI()
        {
            if (_shown <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.depth = guiDepth;

            // Crossfade the two stages either side of _shown (fading from nothing below stage 1).
            int lower = Mathf.FloorToInt(_shown);
            float t = _shown - lower;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var old = GUI.color;
            if (lower >= 1) Draw(screen, lower, 1f - (lower < 4 ? t : 0f));
            if (lower < 4 && t > 0f) Draw(screen, lower + 1, t);
            GUI.color = old;
        }

        void Draw(Rect screen, int stage, float weight)
        {
            var tex = _stages[stage - 1];
            // Images carry their own blood colour; only the generated white masks get tinted.
            var c = tex.hideFlags == HideFlags.HideAndDontSave ? bloodColor : Color.white;
            // Dims between beats and swells back on each one (squared, so it snaps in then eases out).
            float throb = 1f - pulseDepth[stage - 1] * (1f - _throb * _throb);
            c.a = opacity[stage - 1] * weight * throb;
            GUI.color = c;
            GUI.DrawTexture(screen, tex, ScaleMode.StretchToFill);
        }

        /// <summary>White texture whose alpha is a ragged, blotchy ring around the edge.</summary>
        static Texture2D Build(float reach)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (TexSize - 1f) * 2f - 1f, v = y / (TexSize - 1f) * 2f - 1f;
                // Rounded-square distance so the corners fill first, like real screen-blood.
                float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 3f) + Mathf.Pow(Mathf.Abs(v), 3f), 1f / 3f);
                // Same noise for every stage, so the blood grows inward from the same blotches.
                float a = Mathf.Atan2(v, u);
                // Mostly a soft glow; just enough wobble that the edge doesn't read as a perfect shape.
                float ragged = (Mathf.PerlinNoise(Mathf.Cos(a) * 3f + 10f, Mathf.Sin(a) * 3f + 10f) - 0.5f) * 0.14f
                             + (Mathf.PerlinNoise(u * 9f + 50f, v * 9f + 50f) - 0.5f) * 0.04f;
                float edge = 1f - reach;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 0.3f, edge + 0.3f, d + ragged));
                // Darker, thicker toward the very edge.
                byte shade = (byte)(255f * Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(edge, 1.2f, d)));
                px[y * TexSize + x] = new Color32(shade, shade, shade, (byte)(alpha * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
