using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.UI
{
    /// <summary>
    /// One place for the look of every piece of IMGUI in the game: palette, fonts, a screen-height UI scale,
    /// cached text styles and drawing helpers (soft dot, anti-aliased rings and arcs, letterspaced text with a soft
    /// shadow, thin rules, rounded key caps, the aged-paper card). Values come from <see cref="BlinkThemeAsset"/>
    /// (<c>Resources/BlinkTheme</c>); composite pieces built from these live in <see cref="BlinkWidgets"/>.
    ///
    /// Design language: restraint. Off-white "bone" text with a soft dark shadow, muted grey for secondary text, one
    /// deep red accent, short eased fades on unscaled time. Sizes are authored for 1080p: wrap them in <see cref="Px"/>.
    /// Shapes are drawn with GL in GUI pixel space, so only call the drawing helpers from OnGUI on Repaint.
    /// </summary>
    public static class BlinkTheme
    {
        public const string ResourcePath = "BlinkTheme";

        static BlinkThemeAsset _asset;
        static float _stylesScale = -1f;
        static BlinkThemeAsset _stylesAsset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _asset = null;
            ScreenOverride = null;
            Invalidate();
        }

        /// <summary>The live theme. Loaded from Resources; falls back to defaults if the asset is missing.</summary>
        public static BlinkThemeAsset Asset
        {
            get
            {
                if (_asset != null) return _asset;
                _asset = Resources.Load<BlinkThemeAsset>(ResourcePath);
                if (_asset == null)
                {
                    _asset = ScriptableObject.CreateInstance<BlinkThemeAsset>();
                    _asset.hideFlags = HideFlags.HideAndDontSave;
                }
                return _asset;
            }
            set { _asset = value; Invalidate(); }
        }

        /// <summary>Drop cached styles and textures so edits to the asset show up (called automatically on resize).</summary>
        public static void Invalidate()
        {
            _stylesScale = -1f;
            _stylesAsset = null;
            _paperKey = default;
            _widths.Clear();
            _shadeFrame = -1;
        }

        // ---------------------------------------------------------------- screen and scale

        /// <summary>Set to render into an off-screen target of a given size (previews); null = the real screen.</summary>
        public static Vector2? ScreenOverride;
        public static float ScreenWidth => ScreenOverride?.x ?? Screen.width;
        public static float ScreenHeight => ScreenOverride?.y ?? Screen.height;
        public static Vector2 ScreenCentre => new(ScreenWidth * 0.5f, ScreenHeight * 0.5f);

        /// <summary>UI scale: screen height relative to 1080p, times the asset's <c>uiScale</c>.</summary>
        public static float Scale => Mathf.Clamp(ScreenHeight / 1080f * Asset.uiScale, 0.35f, 4f);

        /// <summary>A size authored at 1080p, in pixels on this screen.</summary>
        public static float Px(float px1080) => px1080 * Scale;

        /// <summary>
        /// Scale for type: follows <see cref="Scale"/> but never drops below the asset's <c>minTextScale</c>, so small caps
        /// stay legible at 720p while the layout still shrinks.
        /// </summary>
        public static float TextScale => Mathf.Max(Scale, Asset.minTextScale * Asset.uiScale);

        /// <summary>A font size authored at 1080p, on this screen (see <see cref="TextScale"/>).</summary>
        public static float PxText(float px1080) => px1080 * TextScale;

        public static float Margin => Px(Asset.margin);

        // ---------------------------------------------------------------- palette

        public static Color Bone => Asset.bone;
        public static Color Muted => Asset.muted;
        public static Color Accent => Asset.accent;
        public static Color Ink => Asset.ink;
        public static Color ShadowColor => Asset.shadow;
        public static Color Paper => Asset.paper;
        public static Color PaperEdge => Asset.paperEdge;
        public static Color PaperInk => Asset.paperInk;

        /// <summary>The accent, lifted a touch so a thin stroke still reads over a dark scene.</summary>
        public static Color AccentStroke => Color.Lerp(Asset.accent, new Color(0.86f, 0.2f, 0.2f, Asset.accent.a), 0.35f);

        public static Color Fade(Color c, float alpha) => new(c.r, c.g, c.b, c.a * Mathf.Clamp01(alpha));

        // ---------------------------------------------------------------- motion (all unscaled)

        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
        public static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Moves a 0..1 visibility toward on/off with the theme's fade times, on unscaled time.</summary>
        public static float StepFade(float current, bool on, float inSeconds = -1f, float outSeconds = -1f)
        {
            float s = on ? (inSeconds > 0f ? inSeconds : Asset.fadeIn) : (outSeconds > 0f ? outSeconds : Asset.fadeOut);
            return Mathf.MoveTowards(current, on ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, s));
        }

        /// <summary>
        /// Visibility of something shown at <paramref name="shownAt"/> (unscaled time) that lingers, then fades out:
        /// fades in over fadeIn, holds for <paramref name="linger"/>, fades out over fadeOut.
        /// </summary>
        public static float Linger(float shownAt, float linger = -1f)
        {
            float t = Time.unscaledTime - shownAt;
            if (t < 0f) return 0f;
            if (linger < 0f) linger = Asset.linger;
            float fin = EaseOut(t / Mathf.Max(0.01f, Asset.fadeIn));
            float fout = 1f - EaseInOut((t - linger) / Mathf.Max(0.01f, Asset.fadeOut * 2f));
            return Mathf.Min(fin, fout);
        }

        // ---------------------------------------------------------------- fonts and styles

        static Font _fallbackSerif, _monoFont;

        static Font Fallback => _fallbackSerif != null ? _fallbackSerif
            : _fallbackSerif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Palatino Linotype", "Times New Roman", "DejaVu Serif" }, 32);

        public static Font DisplayFont => Asset.display != null ? Asset.display : Fallback;
        public static Font CapsFont => Asset.caps != null ? Asset.caps : DisplayFont;
        public static Font BodyFont => Asset.body != null ? Asset.body : Fallback;
        public static Font NumberFont => Asset.numbers != null ? Asset.numbers : BodyFont;
        public static Font MonoFont => _monoFont != null ? _monoFont
            : _monoFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Menlo", "DejaVu Sans Mono", "Courier New" }, 14);

        static GUIStyle _title, _heading, _subtitle, _prompt, _smallCaps, _body, _number, _numberSmall, _key, _mono;

        /// <summary>Huge letterspaced title ("BLINK").</summary>
        public static GUIStyle Title { get { EnsureStyles(); return _title; } }
        /// <summary>Letterspaced heading (death screen, note titles).</summary>
        public static GUIStyle Heading { get { EnsureStyles(); return _heading; } }
        /// <summary>Small caps under a title ("blink to begin").</summary>
        public static GUIStyle Subtitle { get { EnsureStyles(); return _subtitle; } }
        /// <summary>Prompt labels next to a key cap.</summary>
        public static GUIStyle Prompt { get { EnsureStyles(); return _prompt; } }
        /// <summary>Small HUD labels: weapon name, the day, inventory.</summary>
        public static GUIStyle SmallCaps { get { EnsureStyles(); return _smallCaps; } }
        /// <summary>Readable body text, word-wrapped.</summary>
        public static GUIStyle Body { get { EnsureStyles(); return _body; } }
        /// <summary>Big HUD numbers (ammo, clock).</summary>
        public static GUIStyle Number { get { EnsureStyles(); return _number; } }
        /// <summary>Secondary numbers (reserve ammo).</summary>
        public static GUIStyle NumberSmall { get { EnsureStyles(); return _numberSmall; } }
        /// <summary>The letter inside a key cap.</summary>
        public static GUIStyle Key { get { EnsureStyles(); return _key; } }
        /// <summary>Debug readouts only.</summary>
        public static GUIStyle MonoText { get { EnsureStyles(); return _mono; } }

        static GUIStyle MakeStyle(Font font, TextAnchor anchor, bool wrap = false) => new()
        {
            font = font, alignment = anchor, wordWrap = wrap, richText = false, clipping = TextClipping.Overflow,
            padding = new RectOffset(), margin = new RectOffset(), border = new RectOffset(),
        };

        static void EnsureStyles()
        {
            float scale = Scale;
            var a = Asset;
            if (_title != null && _title.font != null && Mathf.Approximately(scale, _stylesScale) && ReferenceEquals(a, _stylesAsset)) return;
            if (_title == null || _title.font == null || !ReferenceEquals(a, _stylesAsset))
            {
                _title = MakeStyle(DisplayFont, TextAnchor.MiddleCenter);
                _heading = MakeStyle(DisplayFont, TextAnchor.MiddleCenter);
                _subtitle = MakeStyle(CapsFont, TextAnchor.MiddleCenter);
                _prompt = MakeStyle(CapsFont, TextAnchor.MiddleRight);
                _smallCaps = MakeStyle(CapsFont, TextAnchor.MiddleRight);
                _body = MakeStyle(BodyFont, TextAnchor.UpperLeft, wrap: true);
                _number = MakeStyle(NumberFont, TextAnchor.LowerRight);
                _numberSmall = MakeStyle(NumberFont, TextAnchor.LowerRight);
                _key = MakeStyle(NumberFont, TextAnchor.MiddleCenter);
                _mono = MakeStyle(MonoFont, TextAnchor.UpperLeft);
                _widths.Clear();
            }
            // Titles and note text scale with the layout; HUD type is floored for legibility.
            _title.fontSize = Mathf.RoundToInt(Px(a.titleSize));
            _heading.fontSize = Mathf.RoundToInt(Px(a.headingSize));
            _subtitle.fontSize = Mathf.RoundToInt(PxText(a.subtitleSize));
            _prompt.fontSize = Mathf.RoundToInt(PxText(a.promptSize));
            _smallCaps.fontSize = Mathf.RoundToInt(PxText(a.smallCapsSize));
            _body.fontSize = Mathf.RoundToInt(PxText(a.bodySize));
            _number.fontSize = Mathf.RoundToInt(PxText(a.numberSize));
            _numberSmall.fontSize = Mathf.RoundToInt(PxText(a.numberSize * 0.55f));
            _key.fontSize = Mathf.RoundToInt(PxText(a.keySize));
            _mono.fontSize = Mathf.Max(11, Mathf.RoundToInt(PxText(15f)));
            _stylesScale = scale;
            _stylesAsset = a;
        }

        // ---------------------------------------------------------------- text

        static readonly GUIContent Content = new();
        static readonly Dictionary<(Font, int, FontStyle, char), float> _widths = new();

        /// <summary>Width of <paramref name="text"/> in <paramref name="style"/>, without letterspacing.</summary>
        public static float Measure(string text, GUIStyle style)
        {
            Content.text = text;
            return style.CalcSize(Content).x;
        }

        static readonly Dictionary<char, string> _chars = new();

        /// <summary>A one-character string, cached so letterspaced text doesn't allocate every frame.</summary>
        static string CharString(char ch)
        {
            if (!_chars.TryGetValue(ch, out var str)) _chars[ch] = str = ch.ToString();
            return str;
        }

        static float CharWidth(char ch, GUIStyle style)
        {
            var key = (style.font, style.fontSize, style.fontStyle, ch);
            if (_widths.TryGetValue(key, out float w)) return w;
            Content.text = CharString(ch);
            w = style.CalcSize(Content).x;
            if (ch == ' ' && w <= 0f) w = style.fontSize * 0.28f;
            _widths[key] = w;
            return w;
        }

        /// <summary>Width of letterspaced text: <paramref name="tracking"/> is extra space per gap, as a fraction of the font size.</summary>
        public static float MeasureSpaced(string text, GUIStyle style, float tracking)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float gap = tracking * style.fontSize, w = 0f;
            foreach (char ch in text) w += CharWidth(ch, style);
            return w + gap * (text.Length - 1);
        }

        /// <summary>
        /// The soft dark shadow under all HUD text: a few faint offset copies, so it reads over bright scenes without a
        /// hard outline. <paramref name="strength"/> 0..1.
        /// </summary>
        static readonly Vector2[] ShadowTaps = { new(0f, 1f), new(0.9f, 1.1f), new(-0.9f, 1.1f), new(0f, 1.9f) };

        static void DrawShadowed(Rect r, string text, GUIStyle style, Color color, float shadow)
        {
            if (shadow > 0f)
            {
                var sc = ShadowColor;
                float o = Mathf.Max(0.75f, Px(1f));
                style.normal.textColor = new Color(sc.r, sc.g, sc.b, sc.a * color.a * shadow * 0.32f);
                foreach (var tap in ShadowTaps)
                    GUI.Label(new Rect(r.x + tap.x * o, r.y + tap.y * o, r.width, r.height), text, style);
            }
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        /// <summary>A label in the theme's way: <paramref name="color"/> (alpha included) with a soft shadow.</summary>
        public static void Text(Rect r, string text, GUIStyle style, Color color, float shadow = 1f)
        {
            if (string.IsNullOrEmpty(text) || color.a <= 0.003f) return;
            var old = GUI.color;
            GUI.color = Color.white;
            DrawShadowed(r, text, style, color, shadow);
            GUI.color = old;
        }

        /// <summary>
        /// Letterspaced text, laid out a character at a time inside <paramref name="r"/> using the style's alignment.
        /// Returns the drawn width. Use for titles, headings and small caps.
        /// </summary>
        public static float SpacedText(Rect r, string text, GUIStyle style, Color color, float tracking = -1f, float shadow = 1f)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            if (tracking < 0f) tracking = Asset.tracking;
            float width = MeasureSpaced(text, style, tracking);
            if (color.a <= 0.003f) return width;

            var anchor = style.alignment;
            int col = (int)anchor % 3; // 0 left, 1 centre, 2 right
            float x = col == 0 ? r.x : col == 1 ? r.x + (r.width - width) * 0.5f : r.xMax - width;
            var leftAnchor = (TextAnchor)((int)anchor - col);
            float gap = tracking * style.fontSize;

            var old = GUI.color;
            GUI.color = Color.white;
            style.alignment = leftAnchor;
            foreach (char ch in text)
            {
                float cw = CharWidth(ch, style);
                if (ch != ' ') DrawShadowed(new Rect(x, r.y, cw + 2f, r.height), CharString(ch), style, color, shadow);
                x += cw + gap;
            }
            style.alignment = anchor;
            GUI.color = old;
            return width;
        }

        // ---------------------------------------------------------------- flat shapes (IMGUI)

        public static void Fill(Rect r, Color color)
        {
            if (color.a <= 0.003f) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Anti-aliased rounded rectangle, filled (<paramref name="border"/> 0) or outlined.</summary>
        public static void RoundedRect(Rect r, Color color, float radius, float border = 0f)
        {
            if (color.a <= 0.003f) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color,
                new Vector4(border, border, border, border), new Vector4(radius, radius, radius, radius));
        }

        /// <summary>
        /// A thin horizontal rule that fades out at both ends. <paramref name="thickness"/> in screen pixels.
        /// </summary>
        public static void Rule(Vector2 centre, float width, float thickness, Color color)
        {
            if (color.a <= 0.003f || width <= 0f) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(centre.x - width * 0.5f, centre.y - thickness * 0.5f, width, thickness), RuleTexture);
            GUI.color = old;
        }

        /// <summary>A soft dark elliptical gradient behind text, for contrast over bright scenes.</summary>
        public static void Smudge(Rect r, float alpha)
        {
            if (alpha <= 0.003f) return;
            var old = GUI.color;
            var sc = ShadowColor;
            GUI.color = new Color(sc.r, sc.g, sc.b, Mathf.Clamp01(alpha));
            GUI.DrawTexture(r, SmudgeTexture);
            GUI.color = old;
        }

        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        static int _shadeFrame = -1;
        static readonly float[] _shadeDrawn = new float[4];

        /// <summary>
        /// A large, soft darkening from a screen corner, so HUD text there reads over bright scenes. Several elements in
        /// the same corner in one frame don't stack: the corner ends up as dark as the darkest request.
        /// </summary>
        public static void CornerShade(Corner corner, float alpha, float scale = 1f)
        {
            if (alpha <= 0.003f) return;
            if (_shadeFrame != Time.frameCount) { _shadeFrame = Time.frameCount; System.Array.Clear(_shadeDrawn, 0, 4); }
            int i = (int)corner;
            float drawn = _shadeDrawn[i];
            if (alpha <= drawn) return;
            float extra = (alpha - drawn) / Mathf.Max(0.001f, 1f - drawn); // composite up to exactly `alpha`
            _shadeDrawn[i] = alpha;
            float rx = Px(640f) * scale, ry = Px(400f) * scale;
            float x = corner is Corner.TopLeft or Corner.BottomLeft ? 0f : ScreenWidth;
            float y = corner is Corner.TopLeft or Corner.TopRight ? 0f : ScreenHeight;
            Smudge(new Rect(x - rx, y - ry, rx * 2f, ry * 2f), extra);
        }

        /// <summary>Darkened screen edges (multiplied by <paramref name="alpha"/>), for backdrops behind cards and titles.</summary>
        public static void Vignette(Rect r, Color color)
        {
            if (color.a <= 0.003f) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, VignetteTexture);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- round shapes (GL, anti-aliased)

        static Material _gl;

        static Material GlMaterial
        {
            get
            {
                if (_gl != null) return _gl;
                _gl = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                _gl.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _gl.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _gl.SetInt("_Cull", (int)CullMode.Off);
                _gl.SetInt("_ZWrite", 0);
                _gl.SetInt("_ZTest", (int)CompareFunction.Always);
                return _gl;
            }
        }

        static bool BeginGl(int mode)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return false;
            GlMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, ScreenWidth, ScreenHeight, 0f);
            GL.MultMatrix(GUI.matrix);
            GL.Begin(mode);
            return true;
        }

        static void EndGl()
        {
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>GL takes linear colours in a linear project; IMGUI colours are authored in gamma, like the palette.</summary>
        static Color ToGl(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(
            Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a) : c;

        static Vector2 OnCircle(Vector2 c, float radius, float turns)
        {
            float a = turns * Mathf.PI * 2f; // 0 = 12 o'clock, clockwise on screen (GUI y is down)
            return new Vector2(c.x + Mathf.Sin(a) * radius, c.y - Mathf.Cos(a) * radius);
        }

        static void Vertex(Vector2 p, Color c)
        {
            GL.Color(c);
            GL.Vertex3(p.x, p.y, 0f);
        }

        /// <summary>
        /// Anti-aliased arc of a ring, from <paramref name="from"/> to <paramref name="to"/> in turns (0 = top,
        /// clockwise; 0..1 = a full ring). Thickness in screen pixels; strokes thinner than a pixel fade instead.
        /// </summary>
        public static void Arc(Vector2 centre, float radius, float thickness, float from, float to, Color color)
        {
            if (color.a <= 0.003f || to <= from || radius <= 0f) return;
            if (thickness < 1f) { color.a *= thickness; thickness = 1f; }
            float span = Mathf.Min(1f, to - from);
            int segments = Mathf.Clamp(Mathf.CeilToInt(radius * Mathf.PI * 2f * span / 2.5f), 6, 360);
            const float feather = 0.85f;
            float r0 = Mathf.Max(0f, radius - thickness * 0.5f), r1 = radius + thickness * 0.5f;
            var solid = ToGl(color);
            var clear = new Color(solid.r, solid.g, solid.b, 0f);
            if (!BeginGl(GL.QUADS)) return;
            for (int i = 0; i < segments; i++)
            {
                float a0 = from + span * i / segments, a1 = from + span * (i + 1) / segments;
                // Inner feather, solid band, outer feather.
                Band(centre, Mathf.Max(0f, r0 - feather), r0, a0, a1, clear, solid);
                Band(centre, r0, r1, a0, a1, solid, solid);
                Band(centre, r1, r1 + feather, a0, a1, solid, clear);
            }
            EndGl();
        }

        static void Band(Vector2 c, float rIn, float rOut, float a0, float a1, Color inner, Color outer)
        {
            Vertex(OnCircle(c, rIn, a0), inner);
            Vertex(OnCircle(c, rOut, a0), outer);
            Vertex(OnCircle(c, rOut, a1), outer);
            Vertex(OnCircle(c, rIn, a1), inner);
        }

        /// <summary>A full anti-aliased ring.</summary>
        public static void Ring(Vector2 centre, float radius, float thickness, Color color) =>
            Arc(centre, radius, thickness, 0f, 1f, color);

        /// <summary>
        /// A filled disc with an anti-aliased edge. <paramref name="softness"/> 0 = crisp (one-pixel feather),
        /// 1 = fades all the way from the centre (a glow).
        /// </summary>
        public static void Disc(Vector2 centre, float radius, Color color, float softness = 0f)
        {
            if (color.a <= 0.003f || radius <= 0f) return;
            int segments = Mathf.Clamp(Mathf.CeilToInt(radius * Mathf.PI * 2f / 2.5f), 12, 256);
            float soft = Mathf.Clamp01(softness);
            float solidR = Mathf.Max(0f, radius * (1f - soft) - 0.5f), edgeR = radius + 0.6f;
            var solid = ToGl(color);
            var clear = new Color(solid.r, solid.g, solid.b, 0f);
            if (!BeginGl(GL.TRIANGLES)) return;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments, a1 = (float)(i + 1) / segments;
                if (solidR > 0f)
                {
                    Vertex(centre, solid);
                    Vertex(OnCircle(centre, solidR, a0), solid);
                    Vertex(OnCircle(centre, solidR, a1), solid);
                }
                var p0 = OnCircle(centre, solidR, a0);
                var p1 = OnCircle(centre, solidR, a1);
                var q0 = OnCircle(centre, edgeR, a0);
                var q1 = OnCircle(centre, edgeR, a1);
                Vertex(p0, solid); Vertex(q0, clear); Vertex(q1, clear);
                Vertex(p0, solid); Vertex(q1, clear); Vertex(p1, solid);
            }
            EndGl();
        }

        // ---------------------------------------------------------------- procedural textures

        static Texture2D _rule, _smudge, _vignette, _paper;
        static (Color, Color) _paperKey;

        static Texture2D NewTexture(int w, int h) => new(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
        };

        /// <summary>White, alpha fading in from both ends: tint with GUI.color.</summary>
        static Texture2D RuleTexture
        {
            get
            {
                if (_rule != null) return _rule;
                const int w = 256;
                _rule = NewTexture(w, 1);
                var px = new Color32[w];
                for (int x = 0; x < w; x++)
                {
                    float t = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    px[x] = new Color32(255, 255, 255, (byte)(255f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t)))));
                }
                _rule.SetPixels32(px);
                _rule.Apply();
                return _rule;
            }
        }

        /// <summary>White with a soft elliptical (gaussian-ish) alpha: tint with GUI.color.</summary>
        static Texture2D SmudgeTexture
        {
            get
            {
                if (_smudge != null) return _smudge;
                const int s = 64;
                _smudge = NewTexture(s, s);
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float a = Mathf.Exp(-(dx * dx + dy * dy) * 3.2f) - Mathf.Exp(-3.2f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(a / (1f - Mathf.Exp(-3.2f)))));
                }
                _smudge.SetPixels32(px);
                _smudge.Apply();
                return _smudge;
            }
        }

        /// <summary>White, clear in the middle and opaque at the corners: tint with GUI.color.</summary>
        static Texture2D VignetteTexture
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int s = 128;
                _vignette = NewTexture(s, s);
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1.45f, Mathf.Sqrt(dx * dx + dy * dy)));
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
                _vignette.SetPixels32(px);
                _vignette.Apply();
                return _vignette;
            }
        }

        /// <summary>
        /// Aged paper: warm cream, darkening and browning toward the edges with a faint, low-frequency mottle.
        /// Opaque; fade it with GUI.color's alpha.
        /// </summary>
        public static Texture2D PaperTexture
        {
            get
            {
                var key = (Paper, PaperEdge);
                if (_paper != null && _paperKey == key) return _paper;
                if (_paper != null) Object.DestroyImmediate(_paper);
                const int w = 180, h = 256;
                _paper = NewTexture(w, h);
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    // Rounded-square distance so the browning hugs the edges and corners, not a circle.
                    float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 6f) + Mathf.Pow(Mathf.Abs(v), 6f), 1f / 6f);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1.04f, d));
                    float mottle = (Mathf.PerlinNoise(x * 0.03f + 3.1f, y * 0.03f + 7.7f) - 0.5f) * 0.024f
                                   + (Mathf.PerlinNoise(x * 0.11f + 11f, y * 0.11f + 1.3f) - 0.5f) * 0.008f;
                    var c = Color.Lerp(Paper, PaperEdge, edge * 0.85f + Mathf.Max(0f, mottle) * 2f);
                    c.r += mottle; c.g += mottle; c.b += mottle * 0.8f;
                    c.a = 1f;
                    px[y * w + x] = c;
                }
                _paper.SetPixels(px);
                _paper.Apply();
                _paperKey = key;
                return _paper;
            }
        }
    }
}
