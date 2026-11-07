using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.UI
{
    /// <summary>
    /// The bottom-right prompt stack: rows of [key] label, e.g. <c>[E] open</c>, <c>[R] reload</c>. Anything that wants a
    /// prompt calls <see cref="Show"/> every frame it applies, from Update or LateUpdate (not OnGUI); rows fade and slide
    /// in, and fade out a moment after they stop being shown. A hold shows its progress as a ring around the key cap.
    /// Drawn by a <see cref="BlinkPromptHud"/> that creates itself. Unscaled time.
    /// </summary>
    public static class BlinkPrompts
    {
        sealed class Row
        {
            public object Owner;
            public string Key, Label;
            public InputAction Action; // when set, the glyph follows the device in use
            public float Progress, Alpha, Y;
            public int Order, Frame, Seq;
            public bool Placed;
        }

        static readonly List<Row> Rows = new();
        static float _reserve, _reserveSmooth;
        static int _reserveFrame = -1, _seq;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Rows.Clear();
            _reserve = _reserveSmooth = 0f;
            _reserveFrame = -1;
        }

        /// <summary>
        /// Shows (or keeps showing) <paramref name="owner"/>'s prompt this frame. <paramref name="key"/> null or empty =
        /// a muted statement with no key cap. <paramref name="progress"/> 0..1 = a hold ring; negative = none.
        /// Lower <paramref name="order"/> sits lower in the stack.
        /// </summary>
        public static void Show(object owner, string key, string label, float progress = -1f, int order = 0) =>
            Show(owner, null, key, label, progress, order);

        /// <summary>
        /// As above, with the glyph taken from <paramref name="action"/>'s bindings for the device in use (a key cap on
        /// keyboard, the pad button on a gamepad, switching live). <paramref name="fallbackKey"/> is shown if the action is
        /// null or unbound; both null = a muted statement.
        /// </summary>
        public static void Show(object owner, InputAction action, string fallbackKey, string label, float progress = -1f, int order = 0)
        {
            if (owner == null || string.IsNullOrEmpty(label)) return;
            BlinkPromptHud.Ensure();
            var row = Rows.Find(r => ReferenceEquals(r.Owner, owner));
            if (row == null)
            {
                row = new Row { Owner = owner, Seq = ++_seq };
                Rows.Add(row);
            }
            row.Key = fallbackKey;
            row.Action = action;
            row.Label = label;
            row.Progress = progress;
            row.Order = order;
            row.Frame = Time.frameCount;
        }

        static InputGlyph? GlyphOf(Row r)
        {
            if (r.Action != null) return BlinkInput.GlyphFor(r.Action, r.Key);
            return string.IsNullOrEmpty(r.Key) ? null : InputGlyph.Key(r.Key);
        }

        /// <summary>Keeps the stack above something drawn in the bottom-right corner this frame (e.g. ammo), in pixels.</summary>
        public static void ReserveBottom(float pixels)
        {
            if (_reserveFrame != Time.frameCount) { _reserve = 0f; _reserveFrame = Time.frameCount; }
            _reserve = Mathf.Max(_reserve, pixels);
        }

        /// <summary>Removes every row at once (scene changes, previews).</summary>
        public static void Clear()
        {
            Rows.Clear();
            _reserve = _reserveSmooth = 0f;
        }

        /// <summary>True if any prompt row is on screen.</summary>
        public static bool AnyVisible
        {
            get
            {
                foreach (var r in Rows) if (r.Alpha > 0f) return true;
                return false;
            }
        }

        static bool Live(Row r) => r.Frame >= Time.frameCount - 1;

        /// <summary>
        /// Draws the stack (called by <see cref="BlinkPromptHud"/> on Repaint). <paramref name="instant"/> skips the fades
        /// and slides, for off-screen previews.
        /// </summary>
        public static void Draw(bool instant = false)
        {
            if (Rows.Count == 0) return;
            float dt = instant ? 1000f : Time.unscaledDeltaTime;
            float reserve = _reserveFrame >= Time.frameCount - 1 ? _reserve : 0f;
            _reserveSmooth = Mathf.Lerp(_reserveSmooth, reserve, 1f - Mathf.Exp(-dt * 12f));

            Rows.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));
            float rowH = BlinkTheme.PxText(48f);
            float right = BlinkTheme.ScreenWidth - BlinkTheme.Margin;
            float y = BlinkTheme.ScreenHeight - BlinkTheme.Margin - BlinkTheme.PxText(20f) - _reserveSmooth;

            float maxAlpha = 0f, top = y;
            for (int i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i];
                r.Alpha = instant ? (Live(r) ? 1f : 0f) : BlinkTheme.StepFade(r.Alpha, Live(r));
                if (!r.Placed || instant) { r.Y = y; r.Placed = true; }
                r.Y = Mathf.Lerp(r.Y, y, 1f - Mathf.Exp(-dt * 14f));
                maxAlpha = Mathf.Max(maxAlpha, r.Alpha);
                top = Mathf.Min(top, r.Y);
                // Rows that are fading out give their slot up smoothly.
                y -= rowH * BlinkTheme.EaseOut(r.Alpha * 1.5f);
            }
            BlinkTheme.CornerShade(BlinkTheme.Corner.BottomRight, maxAlpha * 0.5f,
                Mathf.Max(1f, (BlinkTheme.ScreenHeight - top) / BlinkTheme.Px(300f)));
            foreach (var r in Rows)
            {
                if (r.Alpha <= 0f) continue;
                float slide = (1f - BlinkTheme.EaseOut(r.Alpha)) * BlinkTheme.PxText(14f);
                BlinkWidgets.PromptRow(right + slide, r.Y, GlyphOf(r), r.Label, BlinkTheme.EaseOut(r.Alpha), r.Progress);
            }
            Rows.RemoveAll(r => r.Alpha <= 0f && !Live(r));
        }
    }
}
