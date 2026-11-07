using Blink.UI;
using UnityEngine;

namespace Blink.DayNight
{
    /// <summary>
    /// Clock in the top-right corner, in the <see cref="BlinkTheme"/> style: "HH:MM" in serif numerals (the colon
    /// breathes on real time) over "day N" in small caps. A blink skip brightens the digits and shows "+15 min" fading
    /// out. IMGUI; all animation is on unscaled time.
    /// </summary>
    public sealed class ClockHud : MonoBehaviour
    {
        [Tooltip("Defaults to GameClock.Instance.")]
        [SerializeField] GameClock clock;
        [Tooltip("Optional font for the digits; defaults to the theme's numerals.")]
        [SerializeField] Font font;
        [Tooltip("Use BlinkTheme's palette. Untick to use the three colours below.")]
        [SerializeField] bool themeColors = true;
        [SerializeField] Color dayColor = new(0.95f, 0.95f, 0.92f, 0.95f);
        [SerializeField] Color nightColor = new(0.7f, 0.82f, 1f, 0.95f);
        [SerializeField] Color jumpColor = new(1f, 0.85f, 0.4f, 1f);
        [SerializeField, Min(0.1f)] float jumpSeconds = 1.5f;

        float _jumpAt = -100f, _jumpMinutes;

        void Start()
        {
            if (clock == null) clock = GameClock.Instance;
            if (clock != null) clock.Jumped += OnJumped;
            else Debug.LogWarning("[ClockHud] No GameClock found.");
        }

        void OnDestroy()
        {
            if (clock != null) clock.Jumped -= OnJumped;
        }

        void OnJumped(float minutes)
        {
            // Blinks in quick succession add up into one "+N min".
            bool stillShowing = Time.unscaledTime - _jumpAt < jumpSeconds;
            _jumpMinutes = stillShowing ? _jumpMinutes + minutes : minutes;
            _jumpAt = Time.unscaledTime;
        }

        void OnGUI()
        {
            if (clock == null || Event.current.type != EventType.Repaint) return;

            float since = Time.unscaledTime - _jumpAt;
            float flash = 1f - BlinkTheme.EaseOut(since / 0.5f);
            Color timeColor, dayLabel, jumpBase;
            if (themeColors)
            {
                var bone = BlinkTheme.Bone;
                // Night cools the bone a touch; a skip lifts it toward paper-white for a moment, never pure white.
                var baseTime = clock.IsNight ? Color.Lerp(bone, new Color(0.72f, 0.77f, 0.84f, 1f), 0.45f) : bone;
                timeColor = BlinkTheme.Fade(Color.Lerp(baseTime, new Color(0.97f, 0.95f, 0.9f, 1f), flash), 0.88f);
                dayLabel = BlinkTheme.Fade(BlinkTheme.Muted, 0.95f);
                jumpBase = bone;
            }
            else
            {
                var baseColor = clock.IsNight ? nightColor : dayColor;
                timeColor = Color.Lerp(baseColor, jumpColor, flash);
                dayLabel = baseColor;
                jumpBase = jumpColor;
            }
            // The colon breathes once a second instead of blinking hard.
            float colon = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f));
            string jump = since < jumpSeconds ? $"+{Mathf.RoundToInt(_jumpMinutes)} min" : null;
            var jumpColorNow = BlinkTheme.Fade(jumpBase, 1f - BlinkTheme.EaseIn(since / jumpSeconds));
            BlinkWidgets.Clock($"{clock.Hour:00}", $"{clock.Minute:00}", colon, $"Day {clock.Day}",
                timeColor, dayLabel, jump, jumpColorNow, font);
        }
    }
}
