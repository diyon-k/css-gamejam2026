using System.Collections.Generic;
using UnityEngine;
using static Blink.UI.BlinkTheme;

namespace Blink.UI
{
    /// <summary>What the hit ring is reacting to.</summary>
    public enum HitKind { Hit, Kill, Headshot }

    /// <summary>
    /// The game's UI pieces, built from <see cref="BlinkTheme"/>: reticle and hit ring, key caps and prompt rows, ammo,
    /// health, clock, title and death cards, the note card, the grab QTE target and the debug panel. Each takes plain
    /// state (no game objects), so the HUD scripts stay presentation-free and the previews draw exactly what the game
    /// does. Call from OnGUI on Repaint.
    /// </summary>
    public static class BlinkWidgets
    {
        // ---------------------------------------------------------------- reticle

        /// <summary>The tiny soft centre dot, with a faint dark halo so it reads over bright walls.</summary>
        public static void Reticle(Vector2 c, float alpha) => Reticle(c, alpha, Fade(Bone, 0.9f));

        /// <summary>The centre dot in a given colour (alpha included).</summary>
        public static void Reticle(Vector2 c, float alpha, Color color)
        {
            if (alpha <= 0f) return;
            float r = Px(Asset.dotRadius);
            Disc(c, r * 2.6f, Fade(ShadowColor, alpha * 0.5f), 1f);
            Disc(c, r, Fade(color, alpha));
        }

        /// <summary>
        /// The hit ring around the dot: pops outward and fades over <paramref name="t01"/> (0 = the hit, 1 = gone).
        /// Bone on a hit, red on a kill; a headshot is red, a little larger and heavier.
        /// </summary>
        public static void HitRing(Vector2 c, float t01, HitKind kind) =>
            HitRing(c, t01, kind, kind == HitKind.Hit ? Bone : AccentStroke);

        /// <summary>The hit ring in a given colour.</summary>
        public static void HitRing(Vector2 c, float t01, HitKind kind, Color color)
        {
            if (t01 < 0f || t01 >= 1f) return;
            float big = kind == HitKind.Headshot ? Asset.headshotScale : 1f;
            float pop = EaseOut(t01 * 3f);
            float radius = Px(Asset.hitRingRadius) * big * Mathf.Lerp(0.7f, 1f, pop);
            float thick = Mathf.Max(1f, Px(Asset.hitRingThickness) * (kind == HitKind.Hit ? 1f : 1.25f) * big);
            float alpha = 1f - EaseIn(t01);
            Ring(c, radius, thick + Px(2.4f), Fade(ShadowColor, alpha * 0.45f));
            Ring(c, radius, thick, Fade(color, alpha));
        }

        // ---------------------------------------------------------------- key caps and prompts

        /// <summary>Size of a key cap for <paramref name="key"/>.</summary>
        public static Vector2 KeyCapSize(string key)
        {
            float h = PxText(32f);
            float w = Mathf.Max(h, Measure(key ?? "", Key) + PxText(16f));
            return new Vector2(w, h);
        }

        /// <summary>
        /// A small rounded key cap (thin outline, the key inside). <paramref name="progress"/> 0..1 draws a ring
        /// filling around it (holds); negative = no ring. Returns the cap's size.
        /// </summary>
        public static Vector2 KeyCap(Vector2 centre, string key, float alpha, float progress = -1f)
        {
            var size = KeyCapSize(key);
            if (alpha <= 0f) return size;
            var r = new Rect(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y);
            float corner = PxText(7f);
            RoundedRect(r, Fade(Ink, alpha * 0.55f), corner);
            RoundedRect(r, Fade(Bone, alpha * 0.85f), corner, Mathf.Max(1f, PxText(1.3f)));
            Text(new Rect(r.x, r.y - PxText(1.5f), r.width, r.height), key, Key, Fade(Bone, alpha), 0.6f);

            if (progress >= 0f)
            {
                float radius = HoldRingRadius(size);
                float thick = Mathf.Max(1f, PxText(2f));
                Ring(centre, radius, thick + PxText(2f), Fade(ShadowColor, alpha * 0.4f));
                Ring(centre, radius, thick, Fade(Bone, alpha * 0.18f));
                Arc(centre, radius, thick, 0f, Mathf.Clamp01(progress), Fade(Bone, alpha * 0.95f));
            }
            return size;
        }

        /// <summary>Radius of the hold ring that clears a key cap's corners.</summary>
        public static float HoldRingRadius(Vector2 capSize) => capSize.magnitude * 0.5f + Px(1f);

        static bool IsRound(GlyphShape s) => s is GlyphShape.FaceButton or GlyphShape.DPad or GlyphShape.Stick;

        /// <summary>Size of the glyph for one input (a key cap, a pad button circle or a bumper/trigger pill).</summary>
        public static Vector2 GlyphSize(InputGlyph glyph)
        {
            if (glyph.Shape == GlyphShape.Key) return KeyCapSize(glyph.Label);
            if (IsRound(glyph.Shape)) return Vector2.one * PxText(36f);
            float h = PxText(28f);
            return new Vector2(Mathf.Max(h * 1.5f, Measure(glyph.Label, Key) + PxText(20f)), h);
        }

        /// <summary>Radius of the hold ring around a glyph.</summary>
        public static float GlyphRingRadius(InputGlyph glyph, Vector2 size) =>
            IsRound(glyph.Shape) ? size.x * 0.5f + PxText(5f) : HoldRingRadius(size);

        /// <summary>
        /// One input drawn in the theme's look: a key cap on keyboard; on a pad, a circle with a four-dot face-button
        /// diagram (the button's position filled), a d-pad cross, an "L"/"R" stick, or a bumper/trigger pill.
        /// <paramref name="progress"/> 0..1 draws a hold ring filling around it; negative = none. <paramref name="scale"/> enlarges pad glyphs (the QTE). Returns its size.
        /// </summary>
        public static Vector2 Glyph(Vector2 centre, InputGlyph glyph, float alpha, float progress = -1f, float scale = 1f)
        {
            if (glyph.Shape == GlyphShape.Key) return KeyCap(centre, glyph.Label, alpha, progress);
            var size = GlyphSize(glyph) * scale;
            if (alpha <= 0f) return size;
            float line = Mathf.Max(1f, PxText(1.3f));
            var fill = Fade(Ink, alpha * 0.55f);
            var stroke = Fade(Bone, alpha * 0.85f);
            float r = size.x * 0.5f;
            switch (glyph.Shape)
            {
                case GlyphShape.FaceButton:
                {
                    Disc(centre, r, fill);
                    Ring(centre, r - line * 0.5f, line, stroke);
                    float off = r * 0.43f, dot = r * 0.16f;
                    for (int i = 1; i <= 4; i++)
                    {
                        var dir = (PadDirection)i;
                        var p = centre + DirectionVector(dir) * off;
                        if (dir == glyph.Direction) Disc(p, dot * 1.5f, Fade(Bone, alpha));
                        else Disc(p, dot * 0.8f, Fade(Bone, alpha * 0.38f));
                    }
                    break;
                }
                case GlyphShape.DPad:
                {
                    Disc(centre, r, fill);
                    Ring(centre, r - line * 0.5f, line, stroke);
                    float arm = r * 0.5f, w = r * 0.36f;
                    var faint = Fade(Bone, alpha * 0.45f);
                    float thin = Mathf.Max(1f, PxText(1f));
                    RoundedRect(new Rect(centre.x - w * 0.5f, centre.y - arm - w * 0.5f, w, (arm + w * 0.5f) * 2f), faint, w * 0.25f, thin);
                    RoundedRect(new Rect(centre.x - arm - w * 0.5f, centre.y - w * 0.5f, (arm + w * 0.5f) * 2f, w), faint, w * 0.25f, thin);
                    if (glyph.Direction != PadDirection.None)
                    {
                        var d = DirectionVector(glyph.Direction);
                        var c = centre + d * (arm * 0.62f);
                        float lx = Mathf.Abs(d.x) > 0f ? arm * 0.75f : w, ly = Mathf.Abs(d.y) > 0f ? arm * 0.75f : w;
                        RoundedRect(new Rect(c.x - lx * 0.5f, c.y - ly * 0.5f, lx, ly), Fade(Bone, alpha), w * 0.25f);
                    }
                    break;
                }
                case GlyphShape.Stick:
                    Disc(centre, r, fill);
                    Ring(centre, r - line * 0.5f, line, stroke);
                    Ring(centre, r * 0.8f, Mathf.Max(1f, PxText(1f)), Fade(Bone, alpha * 0.25f));
                    Text(new Rect(centre.x - r, centre.y - r - PxText(1.5f), r * 2f, r * 2f), glyph.Label, Key, Fade(Bone, alpha), 0.6f);
                    break;
                default:
                {
                    var rect = new Rect(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y);
                    float round = size.y * 0.5f;
                    // Triggers: rounded on top, squarer below; bumpers and the rest: a plain pill.
                    var radius = glyph.Shape == GlyphShape.Trigger
                        ? new Vector4(round, round, round * 0.3f, round * 0.3f)
                        : new Vector4(round, round, round, round);
                    if (fill.a > 0.003f)
                        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, Vector4.zero, radius);
                    GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, stroke, new Vector4(line, line, line, line), radius);
                    Text(new Rect(rect.x, rect.y - PxText(1.5f), rect.width, rect.height), glyph.Label, Key, Fade(Bone, alpha), 0.6f);
                    break;
                }
            }

            if (progress >= 0f)
            {
                float radius = GlyphRingRadius(glyph, size);
                float thick = Mathf.Max(1f, PxText(2f));
                Ring(centre, radius, thick + PxText(2f), Fade(ShadowColor, alpha * 0.4f));
                Ring(centre, radius, thick, Fade(Bone, alpha * 0.18f));
                Arc(centre, radius, thick, 0f, Mathf.Clamp01(progress), Fade(Bone, alpha * 0.95f));
            }
            return size;
        }

        /// <summary>Screen direction (GUI y down) of a pad position.</summary>
        public static Vector2 DirectionVector(PadDirection d) => d switch
        {
            PadDirection.Up => new Vector2(0f, -1f),
            PadDirection.Right => new Vector2(1f, 0f),
            PadDirection.Down => new Vector2(0f, 1f),
            PadDirection.Left => new Vector2(-1f, 0f),
            _ => Vector2.zero,
        };

        /// <summary>
        /// One prompt row, right-aligned at <paramref name="rightX"/>: [key] label. Without a key (null or empty) the label
        /// is drawn muted, as a statement ("the door is locked"). Returns the row's width.
        /// </summary>
        public static float PromptRow(float rightX, float centreY, string key, string label, float alpha, float progress = -1f) =>
            PromptRow(rightX, centreY, string.IsNullOrEmpty(key) ? null : InputGlyph.Key(key), label, alpha, progress);

        /// <summary>One prompt row with any glyph (keyboard or gamepad); null = a muted statement.</summary>
        public static float PromptRow(float rightX, float centreY, InputGlyph? glyph, string label, float alpha, float progress = -1f)
        {
            label = (label ?? "").ToLowerInvariant();
            bool hasKey = glyph.HasValue && (glyph.Value.IsGamepad || !string.IsNullOrEmpty(glyph.Value.Label));
            float h = PxText(40f);
            var row = new Rect(rightX - PxText(600f), centreY - h * 0.5f, PxText(600f), h);
            float labelW = SpacedText(row, label, BlinkTheme.Prompt, Fade(hasKey ? Bone : Muted, alpha), Asset.tracking * 0.6f);
            if (!hasKey) return labelW;
            var g = glyph.Value;
            var cap = GlyphSize(g);
            float gap = progress >= 0f ? GlyphRingRadius(g, cap) - cap.x * 0.5f + PxText(14f) : PxText(14f);
            Glyph(new Vector2(rightX - labelW - gap - cap.x * 0.5f, centreY), g, alpha, progress);
            return labelW + gap + cap.x;
        }

        // ---------------------------------------------------------------- ammo, weapon, health

        /// <summary>Height the ammo block takes above the bottom margin (prompts stack above it).</summary>
        public static float AmmoBlockHeight => PxText(98f);

        /// <summary>
        /// Weapon readout, bottom-right: a small-caps name (or "reloading" with a filling ring), and for guns the loaded
        /// count large with the reserve small and muted. "no ammo" in the accent when both are empty.
        /// </summary>
        public static void Ammo(string weaponName, bool usesAmmo, int loaded, int capacity, int reserve, bool reloading, float reloadProgress, float alpha)
        {
            if (alpha <= 0f) return;
            float right = ScreenWidth - Margin, bottom = ScreenHeight - Margin;
            CornerShade(Corner.BottomRight, alpha * 0.5f);

            float labelY = usesAmmo ? bottom - PxText(72f) : bottom - PxText(16f);
            var labelRect = new Rect(right - PxText(400f), labelY - PxText(15f), PxText(400f), PxText(30f));
            string label = reloading ? "reloading" : usesAmmo && loaded == 0 && reserve == 0 ? "no ammo" : (weaponName ?? "").ToLowerInvariant();
            var labelColor = label == "no ammo" ? AccentStroke : Color.Lerp(Muted, Bone, 0.3f);
            float lw = SpacedText(labelRect, label, SmallCaps, Fade(labelColor, alpha), Asset.tracking);
            if (reloading)
            {
                var c = new Vector2(right - lw - PxText(18f), labelY + PxText(1f));
                float rr = PxText(8f), th = Mathf.Max(1f, PxText(1.6f));
                Ring(c, rr, th, Fade(Bone, alpha * 0.2f));
                Arc(c, rr, th, 0f, reloadProgress, Fade(Bone, alpha));
            }
            if (!usesAmmo) return;

            string reserveText = "/ " + reserve;
            var numRect = new Rect(right - PxText(400f), bottom - PxText(62f), PxText(400f), PxText(62f));
            float reserveW = Measure(reserveText, NumberSmall);
            Text(new Rect(numRect.x, numRect.y - PxText(6f), numRect.width, numRect.height), reserveText, NumberSmall, Fade(Muted, alpha));
            var loadedRect = new Rect(numRect.x - reserveW - PxText(10f), numRect.y, numRect.width, numRect.height);
            Text(loadedRect, loaded.ToString(), Number, Fade(Bone, alpha * (reloading ? 0.55f : 1f)));

            // One bullet per magazine slot, left of the count: loaded ones solid, spent ones a faint outline.
            float bx = loadedRect.xMax - Measure(loaded.ToString(), Number) - PxText(18f);
            float bw = PxText(7f), bh = PxText(22f), step = bw + PxText(5f), by = bottom - PxText(30f) - bh * 0.5f;
            for (int i = 0; i < capacity; i++)
            {
                var r = new Rect(bx - (i + 1) * step + PxText(5f), by, bw, bh);
                bool full = capacity - 1 - i < loaded;
                var col = full ? Fade(Bone, alpha * (reloading ? 0.55f : 0.9f)) : Fade(Muted, alpha * 0.35f);
                float tip = bw * 0.5f, edge = full ? 0f : Mathf.Max(1f, PxText(1.2f));
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, col,
                    new Vector4(edge, edge, edge, edge), new Vector4(tip, tip, PxText(1f), PxText(1f)));
            }
        }

        /// <summary>
        /// Health, bottom-left, shown only when it matters: a thin ring that drains (bone, red when low) around a faint core.
        /// </summary>
        public static void HealthCore(float fraction, float alpha, float lowAt = 0.35f)
        {
            if (alpha <= 0f) return;
            var c = new Vector2(Margin + Px(18f), ScreenHeight - Margin - Px(18f));
            bool low = fraction <= lowAt;
            float r = Px(16f), th = Mathf.Max(1f, Px(2.2f));
            CornerShade(Corner.BottomLeft, alpha * 0.45f, 0.6f);
            Ring(c, r, th + Px(2f), Fade(ShadowColor, alpha * 0.4f));
            Ring(c, r, th, Fade(Bone, alpha * 0.16f));
            Arc(c, r, th, 0f, Mathf.Clamp01(fraction), Fade(low ? AccentStroke : Bone, alpha * 0.95f));
            Disc(c, Px(9f) * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(fraction)), Fade(low ? Accent : Bone, alpha * (low ? 0.7f : 0.22f)), 0.35f);
        }

        // ---------------------------------------------------------------- clock and inventory (top-right)

        /// <summary>Bottom of the clock block, for things listed under it.</summary>
        public static float ClockBottom => Margin + PxText(92f);

        /// <summary>
        /// Top-right clock: "HH:MM" in serif numerals (the colon fades with <paramref name="colonAlpha"/>), the day in small
        /// caps under it, and an optional "+15 min" left of the day that fades out after a skip.
        /// <paramref name="font"/> overrides the numeral font.
        /// </summary>
        public static void Clock(string hours, string minutes, float colonAlpha, string day, Color timeColor, Color dayColor,
            string jump, Color jumpColor, Font font = null)
        {
            float right = ScreenWidth - Margin, top = Margin;
            CornerShade(Corner.TopRight, Mathf.Max(timeColor.a, dayColor.a) * 0.4f);
            var style = Number;
            var oldFont = style.font;
            if (font != null) style.font = font;
            var timeRect = new Rect(right - PxText(300f), top - PxText(6f), PxText(300f), PxText(56f));
            float mw = Measure(minutes, style), cw = Measure(":", style);
            Text(timeRect, minutes, style, timeColor);
            Text(new Rect(timeRect.x - mw, timeRect.y - PxText(2f), timeRect.width, timeRect.height), ":", style, Fade(timeColor, colonAlpha));
            Text(new Rect(timeRect.x - mw - cw, timeRect.y, timeRect.width, timeRect.height), hours, style, timeColor);
            style.font = oldFont;

            var dayRect = new Rect(right - PxText(300f), top + PxText(58f), PxText(300f), PxText(28f));
            float dw = SpacedText(dayRect, day.ToLowerInvariant(), SmallCaps, dayColor, Asset.tracking);
            if (!string.IsNullOrEmpty(jump) && jumpColor.a > 0f)
                SpacedText(new Rect(dayRect.x, dayRect.y, dayRect.width - dw - PxText(34f), dayRect.height), jump.ToLowerInvariant(),
                    SmallCaps, jumpColor, Asset.tracking);
        }

        /// <summary>Carried items, right-aligned small caps under the clock.</summary>
        public static void Inventory(IList<string> lines, float alpha, float top = -1f)
        {
            if (alpha <= 0f || lines == null || lines.Count == 0) return;
            float right = ScreenWidth - Margin;
            if (top < 0f) top = ClockBottom + PxText(14f);
            float row = PxText(30f);
            CornerShade(Corner.TopRight, alpha * 0.45f, 1.1f);
            Rule(new Vector2(right - PxText(40f), top), PxText(80f), Mathf.Max(1f, PxText(1f)), Fade(Muted, alpha * 0.6f));
            for (int i = 0; i < lines.Count; i++)
                SpacedText(new Rect(right - PxText(400f), top + PxText(10f) + i * row, PxText(400f), row), lines[i].ToLowerInvariant(),
                    SmallCaps, Fade(Bone, alpha * 0.82f), Asset.tracking * 0.8f);
        }

        // ---------------------------------------------------------------- title and death cards

        /// <summary>
        /// Centred card on a dark screen: one large letterspaced serif line, a thin red rule, and a small-caps line under it.
        /// <paramref name="large"/> uses the title size (for "BLINK"); otherwise the heading size, shrunk to fit.
        /// </summary>
        public static void TitleCard(string heading, string subtitle, float alpha, float subtitleAlpha, bool large)
        {
            if (alpha <= 0f) return;
            float w = ScreenWidth, h = ScreenHeight;
            heading = (heading ?? "").ToUpperInvariant();
            var style = large ? Title : Heading;
            float tracking = large ? 0.32f : 0.2f;
            int baseSize = style.fontSize;
            float fit = MeasureSpaced(heading, style, tracking);
            if (fit > w * 0.86f) style.fontSize = Mathf.FloorToInt(baseSize * w * 0.86f / fit);

            float headH = style.fontSize * 1.4f;
            float cy = h * 0.47f;
            Smudge(new Rect(w * 0.5f - Px(560f), cy - Px(220f), Px(1120f), Px(440f)), alpha * 0.25f);
            var headRect = new Rect(0f, cy - headH * 0.5f - Px(large ? 34f : 22f), w, headH);
            float headW = SpacedText(headRect, heading, style, Fade(Bone, alpha), tracking);
            style.fontSize = baseSize;

            float ruleY = cy + Px(large ? 52f : 28f);
            float ruleW = Mathf.Clamp(headW * 0.55f, Px(140f), Px(360f));
            Rule(new Vector2(w * 0.5f, ruleY), ruleW, Mathf.Max(1f, Px(2f)), Fade(Accent, alpha));

            if (string.IsNullOrEmpty(subtitle)) return;
            var subRect = new Rect(0f, ruleY + Px(12f), w, Px(48f));
            SpacedText(subRect, subtitle.ToLowerInvariant(), Subtitle, Fade(Bone, alpha * subtitleAlpha * 0.9f), 0.22f);
        }

        // ---------------------------------------------------------------- note card

        static GUIStyle _noteTitle;

        /// <summary>The note card with a plain lift (no seed): see the full overload.</summary>
        public static void NoteCard(float shown, string title, string body, Texture2D image) =>
            NoteCard(shown, shown, true, 0, title, body, image);

        /// <summary>
        /// A note held up to the face: a light dim over the room (<paramref name="dim"/> 0..1) and a worn, torn sheet of
        /// procedural paper (<see cref="NotePaper"/>, looked up by <paramref name="seed"/>) posed by
        /// <see cref="NotePaper.LiftPose"/> at <paramref name="lift"/> 0..1, rising or dropping. Title in letterspaced small
        /// caps over a broken ink rule, an optional picture, the text in dark ink, then creases and patchy ink over it all.
        /// <paramref name="time"/> drives the idle sway (real seconds; negative = now).
        /// </summary>
        public static void NoteCard(float lift, float dim, bool raising, int seed, string title, string body, Texture2D image,
            float time = -1f)
        {
            if (lift <= 0f && dim <= 0f) return;
            float w = ScreenWidth, h = ScreenHeight;
            float e = EaseInOut(dim);
            Fill(new Rect(0f, 0f, w, h), Fade(Ink, 0.28f * e));
            Vignette(new Rect(0f, 0f, w, h), Fade(Ink, 0.45f * e));
            if (lift <= 0f) return;

            var sheet = NotePaper.Get(seed);
            var pose = NotePaper.LiftPose(lift, raising, sheet.Tilt, time < 0f ? Time.unscaledTime : time);
            float ph = h * 0.82f, pw = Mathf.Min(ph * 0.7f, w * 0.9f);
            var centre = new Vector2(w * 0.5f, h * 0.5f + pose.Drop * h * 0.98f) + pose.Sway * Scale;
            float alpha = Mathf.Clamp01(lift * 5f);

            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            GUIUtility.ScaleAroundPivot(Vector2.one * pose.Scale, centre);
            GUIUtility.RotateAroundPivot(pose.Angle, centre);

            var paper = new Rect(centre.x - pw * 0.5f, centre.y - ph * 0.5f, pw, ph);
            float tw = pw / (1f - 2f * NotePaper.Inset), th = ph / (1f - 2f * NotePaper.Inset);
            var texRect = new Rect(centre.x - tw * 0.5f, centre.y - th * 0.5f, tw, th);
            Smudge(new Rect(paper.x - pw * 0.1f, paper.y - ph * 0.04f + Px(18f), pw * 1.2f, ph * 1.1f), alpha * 0.75f);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(texRect, sheet.Paper);

            float fit = Mathf.Clamp(pw / Px(630f), 0.7f, 1.2f);
            var inner = new Rect(paper.x + pw * 0.12f, paper.y + ph * 0.085f, pw * 0.76f, ph * 0.83f);
            _noteTitle ??= new GUIStyle(Heading);
            _noteTitle.font = DisplayFont;
            _noteTitle.alignment = TextAnchor.MiddleCenter;
            _noteTitle.fontSize = Mathf.RoundToInt(Px(40f) * fit);
            const float titleTracking = 0.14f;
            float titleW = MeasureSpaced(title ?? "", _noteTitle, titleTracking);
            if (titleW > inner.width * 0.92f) _noteTitle.fontSize = Mathf.FloorToInt(_noteTitle.fontSize * inner.width * 0.92f / titleW);
            float titleH = _noteTitle.fontSize * 1.5f;
            SpacedText(new Rect(inner.x, inner.y, inner.width, titleH), title ?? "", _noteTitle, Fade(PaperInk, alpha * 0.9f), titleTracking, 0f);
            float y = inner.y + titleH + Px(4f);
            float ruleW = inner.width * 0.36f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(inner.center.x - ruleW * 0.5f, y - Px(5f) * fit, ruleW, Px(10f) * fit), sheet.Rule);
            y += Px(26f);

            if (image != null)
            {
                float ih = Mathf.Min(inner.height * 0.55f, inner.width * image.height / Mathf.Max(1, image.width));
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(new Rect(inner.x, y, inner.width, ih), image, ScaleMode.ScaleToFit);
                y += ih + Px(22f);
            }
            var bodyStyle = Body;
            int baseSize = bodyStyle.fontSize;
            // Note text follows the card, not the HUD's legibility floor.
            bodyStyle.fontSize = Mathf.RoundToInt(Px(Asset.bodySize) * fit);
            Text(new Rect(inner.x, y, inner.width, inner.yMax - y), body ?? "", bodyStyle, Fade(PaperInk, alpha * 0.93f), 0f);
            bodyStyle.fontSize = baseSize;

            // Creases and rubbed, patchy ink over the text.
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(texRect, sheet.Wear);

            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }

        // ---------------------------------------------------------------- grab QTE

        /// <summary>
        /// The grab QTE target: a thin red circle with the key cap in its middle, and a bone ring closing onto it
        /// (<paramref name="ringScale"/> 1 = meeting the red circle). Radius in screen pixels.
        /// </summary>
        public static void QteTarget(Vector2 c, float targetRadius, float ringScale, string key, float alpha) =>
            QteTarget(c, targetRadius, ringScale, InputGlyph.Key(key), alpha);

        /// <summary>The grab QTE target with any glyph in its middle (a key cap, or the pad button by position).</summary>
        public static void QteTarget(Vector2 c, float targetRadius, float ringScale, InputGlyph key, float alpha)
        {
            if (alpha <= 0f) return;
            Disc(c, targetRadius * 1.9f, Fade(ShadowColor, alpha * 0.35f), 1f);
            float th = Mathf.Max(1f, Px(2f));
            Ring(c, targetRadius, th + Px(2.5f), Fade(ShadowColor, alpha * 0.5f));
            Ring(c, targetRadius, th, Fade(AccentStroke, alpha));
            Glyph(c, key, alpha, -1f, key.IsGamepad ? 1.3f : 1f);

            float r = targetRadius * ringScale;
            float rt = Mathf.Max(1f, Px(1.6f));
            Ring(c, r, rt + Px(2.5f), Fade(ShadowColor, alpha * 0.45f));
            Ring(c, r, rt, Fade(Bone, alpha * 0.95f));
        }

        /// <summary>Progress dots, bottom centre: filled red for each landed hit, a thin ring for each still to go.</summary>
        public static void QtePips(int hits, int total, float alpha)
        {
            if (alpha <= 0f) return;
            float r = Px(5.5f), gap = Px(22f);
            float x0 = ScreenWidth * 0.5f - gap * (total - 1) * 0.5f, y = ScreenHeight - Px(72f);
            for (int i = 0; i < total; i++)
            {
                var c = new Vector2(x0 + i * gap, y);
                Disc(c, r * 2.2f, Fade(ShadowColor, alpha * 0.4f), 1f);
                if (i < hits) Disc(c, r, Fade(AccentStroke, alpha));
                else Ring(c, r, Mathf.Max(1f, Px(1.3f)), Fade(Bone, alpha * 0.75f));
            }
        }

        /// <summary>A short letterspaced word across the upper screen ("bitten"), in the accent.</summary>
        public static void Banner(string text, float alpha, float yFraction = 0.22f)
        {
            if (alpha <= 0f) return;
            var r = new Rect(0f, ScreenHeight * yFraction - Px(40f), ScreenWidth, Px(80f));
            SpacedText(r, (text ?? "").ToUpperInvariant(),
                Heading, Fade(AccentStroke, alpha), 0.24f);
        }

        // ---------------------------------------------------------------- debug

        /// <summary>A plain dark panel of monospace text, top-left, clearly a debug readout.</summary>
        public static void DebugPanel(string header, string text)
        {
            float x = Px(16f), y = Px(16f);
            var mono = MonoText;
            Content.text = text;
            var size = mono.CalcSize(Content);
            header = (header ?? "").ToLowerInvariant();
            float headerW = MeasureSpaced(header, SmallCaps, 0.12f);
            var r = new Rect(x, y, Mathf.Max(Mathf.Max(size.x, headerW), Px(200f)) + Px(24f), size.y + Px(46f));
            RoundedRect(r, Fade(Ink, 0.72f), Px(6f));
            RoundedRect(r, Fade(Muted, 0.35f), Px(6f), 1f);
            var head = new Rect(r.x + Px(12f), r.y + Px(6f), r.width, Px(24f));
            var caps = SmallCaps;
            var oldAnchor = caps.alignment;
            caps.alignment = TextAnchor.MiddleLeft;
            SpacedText(head, header, caps, AccentStroke, 0.12f, 0f);
            caps.alignment = oldAnchor;
            Text(new Rect(r.x + Px(12f), r.y + Px(34f), r.width, size.y), text, mono, Fade(Bone, 0.92f), 0f);
        }

        static readonly GUIContent Content = new();
    }
}
