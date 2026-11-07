using UnityEngine;

namespace Blink.DayNight
{
    /// <summary>
    /// Digital clock in the top-right corner: big "HH:MM" (colon blinks on real time) and "Day N".
    /// A blink skip flashes the digits and shows "+15 min" fading out. IMGUI placeholder like GunHud;
    /// all animation is on unscaled time.
    /// </summary>
    public sealed class ClockHud : MonoBehaviour
    {
        [Tooltip("Defaults to GameClock.Instance.")]
        [SerializeField] GameClock clock;
        [Tooltip("Optional digital font; defaults to an OS monospace font.")]
        [SerializeField] Font font;
        [SerializeField] Color dayColor = new(0.95f, 0.95f, 0.92f, 0.95f);
        [SerializeField] Color nightColor = new(0.7f, 0.82f, 1f, 0.95f);
        [SerializeField] Color jumpColor = new(1f, 0.85f, 0.4f, 1f);
        [SerializeField, Min(0.1f)] float jumpSeconds = 1.5f;

        float _jumpAt = -100f, _jumpMinutes;
        GUIStyle _big, _small, _smallLeft;

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
            EnsureStyles();

            const float w = 180f, margin = 24f;
            var box = new Rect(Screen.width - w - margin, margin, w, 78f);
            Fill(box, new Color(0f, 0f, 0f, 0.35f));

            float since = Time.unscaledTime - _jumpAt;
            float flash = Mathf.Clamp01(1f - since / 0.4f);
            var baseColor = clock.IsNight ? nightColor : dayColor;
            bool colon = Time.unscaledTime % 1f < 0.5f;
            string time = $"{clock.Hour:00}{(colon ? ":" : " ")}{clock.Minute:00}";

            var timeRect = new Rect(box.x, box.y + 2f, w - 12f, 52f);
            Label(timeRect, time, _big, Color.Lerp(baseColor, jumpColor, flash));

            var lineRect = new Rect(box.x + 12f, box.y + 52f, w - 24f, 22f);
            Label(lineRect, $"DAY {clock.Day}", _small, baseColor);

            if (since < jumpSeconds)
            {
                var c = jumpColor;
                c.a *= 1f - since / jumpSeconds;
                Label(lineRect, $"+{Mathf.RoundToInt(_jumpMinutes)} MIN", _smallLeft, c);
            }
        }

        static void Label(Rect r, string text, GUIStyle style, Color color)
        {
            var shadow = new Color(0f, 0f, 0f, color.a * 0.8f);
            style.normal.textColor = shadow;
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        void EnsureStyles()
        {
            if (_big != null) return;
            var f = font != null ? font : Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New", "DejaVu Sans Mono", "Menlo" }, 44);
            _big = new GUIStyle(GUI.skin.label)
            {
                font = f, fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight,
            };
            _small = new GUIStyle(_big) { fontSize = 15 };
            _smallLeft = new GUIStyle(_small) { alignment = TextAnchor.MiddleLeft };
        }

        static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
