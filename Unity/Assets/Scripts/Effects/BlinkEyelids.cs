using System;
using System.Collections.Generic;
using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Effects
{
    /// <summary>
    /// On every blink, black eyelids close in from the top and bottom of the screen, stay shut for a
    /// instant and open again straight away (about a sixth of a second in all). It's the <see cref="TimeSkipController.Transition"/>,
    /// so the world skip happens the moment the lids meet: when they open, the zombies have jumped.
    /// A blink while the lids are still shut keeps them shut longer and skips straight away.
    ///
    /// Drawn with IMGUI on top of the HUD, so it needs no Canvas. Runs on unscaled time, so
    /// fast-forward and the grab QTE's frozen world don't change its speed. Lives on GameSystems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlinkEyelids : MonoBehaviour, IBlinkTransition
    {
        [Header("Timing (real seconds)")]
        [Tooltip("Lids moving from open to meeting in the middle. The skip happens at the end of this.")]
        [SerializeField, Min(0f)] float closeSeconds = 0.07f;
        [Tooltip("How long the screen stays fully black before the lids open (0 = straight back out).")]
        [SerializeField, Min(0f)] float shutSeconds = 0f;
        [Tooltip("Lids moving from shut back to fully open.")]
        [SerializeField, Min(0f)] float openSeconds = 0.1f;

        [Header("Look")]
        [SerializeField] Color lidColor = Color.black;
        [Tooltip("How far the lid edge curves, as a fraction of screen height (0 = straight edge).")]
        [SerializeField, Range(0f, 0.5f)] float curveHeight = 0.15f;
        [Tooltip("Softness of the lid edge, as a fraction of the curve.")]
        [SerializeField, Range(0.01f, 0.5f)] float edgeSoftness = 0.2f;
        [Tooltip("IMGUI depth: lower draws on top. The HUD and QTE use the default 0.")]
        [SerializeField] int guiDepth = -1000;

        const int TexWidth = 256, TexHeight = 64;

        readonly List<Action> _pending = new();
        EyelidTimeline _timeline;
        Texture2D _lidEdge;
        float _builtCurve = -1f, _builtSoftness = -1f;

        public bool IsCovered => _timeline != null && _timeline.IsCovered;
        public float Closed01 => Hold01 ?? _timeline?.Closed01 ?? 0f;

        /// <summary>When set, draws the lids at this closure (0 open .. 1 shut) instead of the blink timeline, e.g. the wake-up.</summary>
        public float? Hold01 { get; set; }

        void Awake() => _timeline = new EyelidTimeline(closeSeconds, shutSeconds, openSeconds);

        void OnEnable()
        {
            _timeline ??= new EyelidTimeline(closeSeconds, shutSeconds, openSeconds);
            TimeSkipController.Transition = this;
        }

        void OnDisable()
        {
            if (ReferenceEquals(TimeSkipController.Transition, this)) TimeSkipController.Transition = null;
            FlushPending(); // never swallow a skip
        }

        void OnDestroy()
        {
            if (_lidEdge != null) Destroy(_lidEdge);
        }

        void OnValidate()
        {
            if (_timeline == null) return;
            _timeline.CloseSeconds = closeSeconds;
            _timeline.ShutSeconds = shutSeconds;
            _timeline.OpenSeconds = openSeconds;
        }

        public void Play(Action onCovered)
        {
            if (onCovered != null) _pending.Add(onCovered);
            if (!isActiveAndEnabled || _timeline.Trigger()) FlushPending();
        }

        void Update()
        {
            if (_timeline.Tick(Time.unscaledDeltaTime)) FlushPending();
        }

        void FlushPending()
        {
            if (_pending.Count == 0) return;
            var callbacks = _pending.ToArray(); // a callback can trigger another blink
            _pending.Clear();
            foreach (var cb in callbacks) cb();
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _timeline == null) return;
            float closed = Closed01;
            if (closed <= 0f) return;

            GUI.depth = guiDepth;
            EnsureTexture();

            float w = Screen.width, h = Screen.height;
            float curve = curveHeight * h;
            // Lid edge travels from just off-screen to past the middle, so the curved corners meet too.
            float edge = closed * (h * 0.5f + curve);
            float solid = edge - curve;

            var old = GUI.color;
            GUI.color = lidColor;
            if (solid > 0f)
            {
                GUI.DrawTexture(new Rect(0f, 0f, w, solid), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, h - solid, w, solid), Texture2D.whiteTexture);
            }
            if (curve > 0f)
            {
                GUI.DrawTexture(new Rect(0f, solid, w, curve), _lidEdge);
                // Same texture flipped vertically for the lower lid.
                GUI.DrawTextureWithTexCoords(new Rect(0f, h - edge, w, curve), _lidEdge, new Rect(0f, 1f, 1f, -1f));
            }
            GUI.color = old;
        }

        /// <summary>
        /// White texture whose alpha is the upper lid's rim: solid at the top, fading out along an arc
        /// that dips lowest in the middle, like a real eyelid.
        /// </summary>
        void EnsureTexture()
        {
            if (_lidEdge != null && Mathf.Approximately(_builtCurve, curveHeight) && Mathf.Approximately(_builtSoftness, edgeSoftness))
                return;
            if (_lidEdge == null)
            {
                _lidEdge = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            float soft = edgeSoftness;
            var pixels = new Color32[TexWidth * TexHeight];
            for (int y = 0; y < TexHeight; y++)
            {
                float v = y / (TexHeight - 1f); // 0 = bottom (open side), 1 = top (lid side)
                for (int x = 0; x < TexWidth; x++)
                {
                    float u = x / (TexWidth - 1f) * 2f - 1f; // -1..1 across the screen
                    float rim = soft + (1f - 2f * soft) * u * u; // lowest at the centre
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rim - soft, rim + soft, v));
                    pixels[y * TexWidth + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            _lidEdge.SetPixels32(pixels);
            _lidEdge.Apply();
            _builtCurve = curveHeight;
            _builtSoftness = edgeSoftness;
        }
    }
}
