using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Blink.UI
{
    /// <summary>Where a held note sits on screen: see <see cref="NotePaper.LiftPose"/>.</summary>
    public struct NotePose
    {
        /// <summary>0 = at the reading position, 1 = a screen-height below it (off the bottom); slightly negative while overshooting.</summary>
        public float Drop;
        /// <summary>Size multiplier (1 = reading size).</summary>
        public float Scale;
        /// <summary>Tilt in degrees, clockwise.</summary>
        public float Angle;
        /// <summary>Hand-held sway offset, in 1080p pixels.</summary>
        public Vector2 Sway;
    }

    /// <summary>
    /// Procedural aged paper for notes, baked once per seed and cached: an irregular sheet with one ripped side, softened
    /// and sometimes dog-eared corners, grime toward the edges, water and tea stains, fold creases, a faded border line
    /// that breaks up and is lost where the paper is torn, and uneven ink. Three textures: the <see cref="Sheet.Paper"/>
    /// under the text, a <see cref="Sheet.Wear"/> overlay drawn over the text (creases and patchy ink), and a short broken
    /// <see cref="Sheet.Rule"/> for under the title. The pose helpers (lift, drop, idle sway) are pure.
    /// </summary>
    public static class NotePaper
    {
        public const int Width = 360, Height = 512;
        /// <summary>The nominal sheet sits this far inside the texture on each side, leaving room for ragged edges.</summary>
        public const float Inset = 0.035f;

        public sealed class Sheet
        {
            public Texture2D Paper, Wear, Rule;
            /// <summary>Resting tilt in degrees (about 1 to 2, either way).</summary>
            public float Tilt;
        }

        /// <summary>A sheet's pixels, computed off the main thread; textures are made from it on the main thread.</summary>
        sealed class Baked
        {
            public Color32[] Paper, Wear, Rule;
            public float Tilt;
        }

        static readonly Dictionary<int, Sheet> Cache = new();
        static readonly Dictionary<int, Task<Baked>> Pending = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            Pending.Clear();
        }

        /// <summary>A stable seed from a note's text (FNV-1a), so the same note always looks the same.</summary>
        public static int SeedFor(string a, string b = null)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in a ?? "") h = (h ^ c) * 16777619;
                h = (h ^ '|') * 16777619;
                foreach (char c in b ?? "") h = (h ^ c) * 16777619;
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>
        /// Starts baking <paramref name="seed"/>'s sheet on a worker thread (main thread only), so it is ready before the note
        /// is first held up. Call early, e.g. from the note's Start. Does nothing if it is baked or baking.
        /// </summary>
        public static void Prewarm(int seed)
        {
            if (Cache.ContainsKey(seed) || Pending.ContainsKey(seed)) return;
            Color paper = BlinkTheme.Paper, edge = BlinkTheme.PaperEdge, ink = BlinkTheme.PaperInk;
#if UNITY_WEBGL && !UNITY_EDITOR
            Pending[seed] = Task.FromResult(Compute(seed, paper, edge, ink));
#else
            Pending[seed] = Task.Run(() => Compute(seed, paper, edge, ink));
#endif
        }

        /// <summary>
        /// The sheet for <paramref name="seed"/> (main thread only). Uses a prewarmed bake when there is one (waiting for it
        /// if it is still running), else bakes now (about a tenth of a second), then caches it.
        /// </summary>
        public static Sheet Get(int seed)
        {
            if (Cache.TryGetValue(seed, out var s) && s.Paper != null) return s;
            Baked baked;
            if (Pending.TryGetValue(seed, out var task))
            {
                Pending.Remove(seed);
                baked = task.Result;
            }
            else baked = Compute(seed, BlinkTheme.Paper, BlinkTheme.PaperEdge, BlinkTheme.PaperInk);
            s = Upload(baked);
            Cache[seed] = s;
            return s;
        }

        static Sheet Upload(Baked b)
        {
            var sheet = new Sheet { Paper = NewTexture(Width, Height), Wear = NewTexture(Width, Height), Rule = NewTexture(RuleWidth, RuleHeight), Tilt = b.Tilt };
            sheet.Paper.SetPixels32(b.Paper);
            sheet.Paper.Apply();
            sheet.Wear.SetPixels32(b.Wear);
            sheet.Wear.Apply();
            sheet.Rule.SetPixels32(b.Rule);
            sheet.Rule.Apply();
            return sheet;
        }

        // ------------------------------------------------------------ pose (pure)

        /// <summary>
        /// The note's pose at <paramref name="t"/> (0 = put away, 1 = held up). Raising: rises from below the screen with an
        /// ease-out and a slight overshoot, growing from 84% and untwisting from a steeper tilt to <paramref name="restTilt"/>.
        /// Lowering: drops away with an ease-in, tipping as it goes. Fully up, a small hand-held sway driven by
        /// <paramref name="time"/> (real seconds).
        /// </summary>
        public static NotePose LiftPose(float t, bool raising, float restTilt, float time)
        {
            t = Mathf.Clamp01(t);
            var pose = new NotePose();
            float side = restTilt >= 0f ? 1f : -1f;
            if (raising)
            {
                pose.Drop = 1f - EaseOutBack(t, 1.15f);
                pose.Scale = Mathf.Lerp(0.84f, 1f, BlinkTheme.EaseOut(t));
                pose.Angle = Mathf.LerpUnclamped(restTilt + 7f * side, restTilt, EaseOutBack(t, 1.7f));
            }
            else
            {
                float q = 1f - t;
                pose.Drop = q * q * q;
                pose.Scale = Mathf.Lerp(1f, 0.9f, q);
                pose.Angle = restTilt - 6f * side * q;
            }
            // Hand-held: two slow, out-of-step sines, faded in as it settles.
            float hold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, t));
            pose.Angle += 0.22f * Mathf.Sin(time * 1.1f) * hold;
            pose.Sway = new Vector2(1.6f * Mathf.Sin(time * 0.71f), 1.3f * Mathf.Sin(time * 0.93f + 1.3f)) * hold;
            return pose;
        }

        static float EaseOutBack(float t, float overshoot)
        {
            float u = t - 1f;
            return 1f + u * u * ((overshoot + 1f) * u + overshoot);
        }

        // ------------------------------------------------------------ baking

        static float Noise(float x, float y) => Perlin(x, y) - 0.5f;

        static readonly int[] Perm = BuildPermutation();

        static int[] BuildPermutation()
        {
            var rng = new System.Random(1337);
            var p = new int[512];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
            for (int i = 0; i < 256; i++) p[i + 256] = p[i];
            return p;
        }

        static float Grad(int h, float x, float y) => ((h & 1) == 0 ? x : -x) + ((h & 2) == 0 ? y : -y);

        /// <summary>Classic 2D Perlin noise, roughly 0..1 like <see cref="Mathf.PerlinNoise"/>, but managed and thread-safe.</summary>
        static float Perlin(float x, float y)
        {
            int xi = (int)System.Math.Floor(x), yi = (int)System.Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255;
            yi &= 255;
            float u = xf * xf * xf * (xf * (xf * 6f - 15f) + 10f), v = yf * yf * yf * (yf * (yf * 6f - 15f) + 10f);
            int a = Perm[xi] + yi, b = Perm[xi + 1] + yi;
            float x1 = Lerp(Grad(Perm[a], xf, yf), Grad(Perm[b], xf - 1f, yf), u);
            float x2 = Lerp(Grad(Perm[a + 1], xf, yf - 1f), Grad(Perm[b + 1], xf - 1f, yf - 1f), u);
            return (Lerp(x1, x2, v) + 1f) * 0.5f;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Fbm(float x, float y) => Noise(x, y) + 0.5f * Noise(x * 2.03f + 17f, y * 2.03f + 5f) + 0.25f * Noise(x * 4.1f + 3f, y * 4.1f + 29f);

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f - 0.5f;
            }
        }

        /// <summary>A smooth noise field sampled on a grid every few pixels and interpolated.</summary>
        sealed class CoarseField
        {
            const int Step = 4;
            readonly int _cols, _rows;
            readonly float[] _v;

            public CoarseField(int w, int h, System.Func<float, float, float> f)
            {
                _cols = w / Step + 2;
                _rows = h / Step + 2;
                _v = new float[_cols * _rows];
                for (int j = 0; j < _rows; j++)
                for (int i = 0; i < _cols; i++)
                    _v[j * _cols + i] = f(i * Step / (float)w, j * Step / (float)h);
            }

            public float Sample(float x, float y)
            {
                float fx = x / Step, fy = y / Step;
                int i = Mathf.Clamp((int)fx, 0, _cols - 2), j = Mathf.Clamp((int)fy, 0, _rows - 2);
                float tx = fx - i, ty = fy - j;
                float a = Mathf.Lerp(_v[j * _cols + i], _v[j * _cols + i + 1], tx);
                float b = Mathf.Lerp(_v[(j + 1) * _cols + i], _v[(j + 1) * _cols + i + 1], tx);
                return Mathf.Lerp(a, b, ty);
            }
        }

        static Texture2D NewTexture(int w, int h) => new(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
        };

        struct Stain
        {
            public Vector2 Centre;
            public float Radius, Ring, Fill, Phase;
        }

        /// <summary>Computes a sheet's pixels. Pure managed code, safe off the main thread.</summary>
        static Baked Compute(int seed, Color paper, Color edge, Color ink)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int W = Width, H = Height;
            float ox = R(0f, 500f), oy = R(0f, 500f);

            // Nominal sheet in texture pixels, y measured from the top.
            float left = Inset * W, right = W - Inset * W, top = Inset * H, bottom = H - Inset * H;
            // One ripped side (top most often), the others only slightly uneven.
            int torn = rng.NextDouble() < 0.5 ? 0 : rng.NextDouble() < 0.5 ? 1 : 3; // 0 top, 1 right, 2 bottom, 3 left
            var dTop = new float[W];
            var dBottom = new float[W];
            var dLeft = new float[H];
            var dRight = new float[H];
            for (int x = 0; x < W; x++)
            {
                dTop[x] = EdgeBite(x, 0, torn == 0, ox, oy);
                dBottom[x] = EdgeBite(x, 2, torn == 2, ox, oy);
            }
            for (int y = 0; y < H; y++)
            {
                dLeft[y] = EdgeBite(y, 3, torn == 3, ox, oy);
                dRight[y] = EdgeBite(y, 1, torn == 1, ox, oy);
            }

            // Corners: softened by a few pixels each; one may be dog-eared (folded over).
            var cornerRadius = new float[4];
            for (int i = 0; i < 4; i++) cornerRadius[i] = R(3f, 11f);
            // A small nick torn into one of the untorn sides.
            int nickSide = (torn + 1 + rng.Next(3)) % 4;
            float nickAt = R(0.2f, 0.8f), nickDepth = R(6f, 14f), nickWidth = R(5f, 11f);
            void Nick(float[] bites, int length)
            {
                float centre = nickAt * length;
                for (int i = 0; i < length; i++)
                {
                    float k = 1f - Mathf.Abs(i - centre) / nickWidth;
                    if (k > 0f) bites[i] = Mathf.Max(bites[i], nickDepth * k * (0.75f + 0.5f * (Noise(i * 0.4f, oy) + 0.5f)));
                }
            }
            Nick(nickSide == 0 ? dTop : nickSide == 1 ? dRight : nickSide == 2 ? dBottom : dLeft, nickSide % 2 == 0 ? W : H);
            int dogEar = rng.NextDouble() < 0.75 ? rng.Next(4) : -1; // 0 TL, 1 TR, 2 BR, 3 BL
            if (dogEar == 0 && torn == 0) dogEar = 2;
            float dogSize = R(0.07f, 0.11f) * W;

            // Folds: one across, sometimes one down.
            float creaseY = H * (rng.NextDouble() < 0.5 ? R(0.31f, 0.37f) : R(0.6f, 0.67f));
            bool hasVertical = rng.NextDouble() < 0.55;
            float creaseX = W * R(0.47f, 0.53f);

            // Stains: a ring or two and a soft blot.
            int stainCount = 1 + rng.Next(3);
            var stains = new Stain[stainCount];
            for (int i = 0; i < stainCount; i++)
            {
                bool ring = i == 0 || rng.NextDouble() < 0.4;
                stains[i] = new Stain
                {
                    Centre = new Vector2(R(0.12f, 0.88f) * W, R(0.1f, 0.9f) * H),
                    Radius = R(0.07f, 0.17f) * W,
                    Ring = ring ? R(0.1f, 0.18f) : 0f,
                    Fill = ring ? R(0.02f, 0.05f) : R(0.05f, 0.09f),
                    Phase = R(0f, 100f),
                };
            }
            var stainColor = new Color(0.55f, 0.4f, 0.22f);
            float borderInset = 0.05f * W;
            float yellowAngle = R(0f, Mathf.PI * 2f);

            // Slow noise fields on a coarse grid (sampled bilinearly) and per-column/row arrays: a bake costs a few
            // milliseconds instead of millions of Perlin calls.
            var mottleField = new CoarseField(W, H, (u, v) => Fbm(u * 3.2f + ox, v * 3.2f + oy));
            var grimeField = new CoarseField(W, H, (u, v) => Fbm(u * 7f + ox * 2f, v * 7f + oy));
            var fadeField = new CoarseField(W, H, (u, v) => Fbm(u * 9f + oy, v * 9f + ox));
            var creaseOffX = new float[W];
            var creaseKX = new float[W * 2];
            var jitterY = new float[W];
            for (int i = 0; i < W; i++)
            {
                creaseOffX[i] = 1.6f * Noise(i * 0.02f + ox, 1f);
                creaseKX[i * 2] = Perlin(i * 0.03f, 4f);
                creaseKX[i * 2 + 1] = Perlin(i * 0.03f, 8f);
                jitterY[i] = 0.8f * Noise(i * 0.045f + oy, 17f);
            }
            var creaseOffY = new float[H];
            var creaseKY = new float[H * 2];
            var jitterX = new float[H];
            for (int i = 0; i < H; i++)
            {
                creaseOffY[i] = 1.6f * Noise(i * 0.02f + oy, 2f);
                creaseKY[i * 2] = Perlin(i * 0.03f, 4f);
                creaseKY[i * 2 + 1] = Perlin(i * 0.03f, 8f);
                jitterX[i] = 0.8f * Noise(i * 0.045f + ox, 13f);
            }

            var paperPx = new Color32[W * H];
            var wearPx = new Color32[W * H];
            for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                float x = px + 0.5f, y = H - py - 0.5f; // y from the top
                int row = H - 1 - py;
                float iL = x - (left + dLeft[row]);
                float iR = (right - dRight[row]) - x;
                float iT = y - (top + dTop[px]);
                float iB = (bottom - dBottom[px]) - y;
                float dist = Mathf.Min(Mathf.Min(iL, iR), Mathf.Min(iT, iB));
                dist = RoundCorner(dist, iL, iT, cornerRadius[0]);
                dist = RoundCorner(dist, iR, iT, cornerRadius[1]);
                dist = RoundCorner(dist, iR, iB, cornerRadius[2]);
                dist = RoundCorner(dist, iL, iB, cornerRadius[3]);

                // Dog-ear: the corner triangle is folded over onto the sheet; we see its back as a flap.
                bool flap = false;
                float flapShade = 0f;
                if (dogEar >= 0)
                {
                    float a = dogEar == 0 || dogEar == 3 ? iL : iR;
                    float b = dogEar < 2 ? iT : iB;
                    if (a + b < dogSize) dist = Mathf.Min(dist, (a + b - dogSize) * 0.7071f);
                    else if (a <= dogSize && b <= dogSize)
                    {
                        flap = true;
                        flapShade = Mathf.InverseLerp(dogSize, dogSize * 2f, a + b); // darker toward the fold
                    }
                    else if (a < dogSize + 4f && b < dogSize + 4f && a + b < dogSize * 2f + 4f)
                        flapShade = -1f; // the flap's shadow on the sheet
                }

                float alpha = Mathf.Clamp01(dist / 1.3f + 0.5f);
                if (alpha <= 0f)
                {
                    paperPx[py * W + px] = new Color32(0, 0, 0, 0);
                    wearPx[py * W + px] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float u = x / W, v = y / H;
                // Base: cream with a slow mottle, yellowed more toward one side.
                var c = paper;
                float mottle = mottleField.Sample(x, y) * 0.05f;
                float yellow = 0.5f + 0.5f * Mathf.Cos(yellowAngle) * (u - 0.5f) * 2f + 0.5f * Mathf.Sin(yellowAngle) * (v - 0.5f) * 2f;
                c = Color.Lerp(c, edge, Mathf.Clamp01(yellow) * 0.18f);
                c.r += mottle; c.g += mottle; c.b += mottle * 0.7f;

                // Grime and browning toward the edges, patchy.
                float grimeNoise = 0.6f + 0.8f * (grimeField.Sample(x, y) + 0.5f);
                float near = 1f - Mathf.Clamp01(dist / 55f);
                float grime = near * Mathf.Sqrt(near) * grimeNoise * 1.15f;
                c = Color.Lerp(c, edge * 0.82f, Mathf.Clamp01(grime) * 0.75f);

                // Stains.
                for (int si = 0; si < stains.Length; si++)
                {
                    var st = stains[si];
                    float dx = x - st.Centre.x, dy = y - st.Centre.y;
                    float rr = st.Radius * 1.3f, d2 = dx * dx + dy * dy;
                    if (d2 > rr * rr) continue;
                    float len = Mathf.Sqrt(d2) + 1e-4f, cx = dx / len, cy = dy / len;
                    // The outline wobbles with direction; sampled on the unit circle, so no trig per pixel.
                    float dirNoise = Noise(cx * 1.5f + st.Phase, cy * 1.5f + st.Phase);
                    float d = len / (st.Radius * (1f + 0.16f * dirNoise));
                    float amount = 0f;
                    if (d < 1f) amount += st.Fill * (0.6f + 0.8f * (fadeField.Sample(si % 2 == 0 ? W - x : x, H - y) + 0.5f));
                    if (st.Ring > 0f)
                    {
                        float r01 = (d - 1f) / 0.045f;
                        if (r01 > -4f && r01 < 4f) amount += st.Ring * Mathf.Exp(-r01 * r01) * (0.7f + dirNoise * 2f);
                    }
                    if (amount > 0f) c = Color.Lerp(c, c * stainColor * 1.25f, Mathf.Clamp01(amount));
                }

                // Torn edge: a pale fibrous band where the paper's core shows.
                if (TornSideDistance(torn, iL, iR, iT, iB) < 2.6f && dist > -1f)
                    c = Color.Lerp(c, Color.Lerp(paper, Color.white, 0.35f), 0.55f * (0.6f + Hash(px, py, seed)));

                // Printed border: faded, broken, uneven, and gone where the paper is torn away.
                if (!flap)
                {
                    // The printed line wanders a little.
                    float jx = x + jitterX[row], jy = y + jitterY[px];
                    float bx = Mathf.Min(Mathf.Abs(jx - (left + borderInset)), Mathf.Abs(jx - (right - borderInset)));
                    float by = Mathf.Min(Mathf.Abs(jy - (top + borderInset)), Mathf.Abs(jy - (bottom - borderInset)));
                    bool inX = x > left + borderInset - 1f && x < right - borderInset + 1f;
                    bool inY = y > top + borderInset - 1f && y < bottom - borderInset + 1f;
                    float lineD = float.MaxValue;
                    float along = 0f;
                    if (inY && bx < lineD) { lineD = bx; along = y; }
                    if (inX && by < lineD) { lineD = by; along = x + 1000f; }
                    // Gaps are where the paper is torn near the line, or where the ink has simply worn away.
                    if (lineD < 2f && dist > borderInset * 0.75f)
                    {
                        float thick = 0.45f + 0.55f * (Noise(along * 0.06f + ox, 3f) + 0.5f);
                        float present = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.34f, Fbm(along * 0.009f + oy, 7f) + 0.5f));
                        float edgeLoss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(borderInset * 0.75f, borderInset * 1.3f, dist));
                        float weight = 0.2f + 0.36f * Mathf.Clamp01(Fbm(along * 0.025f, 11f) + 0.6f) + 0.06f * Hash(px, py, seed + 7);
                        float ink01 = Mathf.Clamp01(thick + 0.5f - lineD) * present * edgeLoss * weight;
                        c = Color.Lerp(c, ink, ink01);
                    }
                }

                // The dog-ear flap is the back of the sheet: a touch darker, with the fold casting shade.
                if (flap) c = Color.Lerp(c, edge * 0.9f, 0.22f + 0.25f * flapShade);
                else if (flapShade < 0f) c *= 0.88f;

                float grain = Hash(px, py, seed) * 0.022f;
                c.r += grain; c.g += grain; c.b += grain;
                c.a = alpha;
                paperPx[py * W + px] = c;

                // Wear overlay (drawn over the text): patchy ink, creases with light and shadow.
                float fade = Mathf.Clamp01((fadeField.Sample(x, y) + 0.5f) * 0.38f - 0.06f) + Mathf.Clamp01(grime) * 0.18f;
                float shadow = 0f, light = 0f;
                float sH = y - (creaseY + creaseOffX[px]);
                Crease(sH, creaseKX[px * 2 + (sH > 0f ? 0 : 1)], 1f, ref shadow, ref light, ref fade);
                if (hasVertical)
                {
                    float sV = x - (creaseX + creaseOffY[row]);
                    Crease(sV, creaseKY[row * 2 + (sV > 0f ? 0 : 1)], 0.7f, ref shadow, ref light, ref fade);
                }
                if (flap) fade = 1f; // the back of the sheet has no text on it
                // Premultiplied composite of: paper-coloured fade, then highlight, then shadow.
                Vector3 pm = new Vector3(c.r, c.g, c.b) * fade;
                float wa = fade;
                pm = Vector3.one * 0.98f * light + pm * (1f - light);
                wa = light + wa * (1f - light);
                pm *= 1f - shadow;
                wa = shadow + wa * (1f - shadow);
                var wc = wa > 0.001f ? pm / wa : Vector3.zero;
                wearPx[py * W + px] = new Color(wc.x, wc.y, wc.z, Mathf.Clamp01(wa) * alpha);
            }

            return new Baked
            {
                Paper = paperPx,
                Wear = wearPx,
                Rule = BakeRule(rng, ink),
                Tilt = R(0.9f, 1.9f) * (rng.NextDouble() < 0.5 ? -1f : 1f),
            };
        }

        /// <summary>How far an edge bites inward at <paramref name="t"/> pixels along it; a torn side bites deep and ragged.</summary>
        static float EdgeBite(float t, int side, bool tornSide, float ox, float oy)
        {
            float s = side * 31.7f;
            float gentle = 2.2f + 3f * Noise(t * 0.009f + ox + s, oy) + 1.2f * Noise(t * 0.04f + s, oy + 3f)
                           + 0.5f * Noise(t * 0.2f + s, oy + 5f);
            if (!tornSide) return gentle;
            float tear = 5f + 9f * Mathf.Abs(Fbm(t * 0.011f + ox + s, oy + 9f)) * 2f
                         + 3.2f * Noise(t * 0.07f + s, oy + 13f) + 1.4f * Noise(t * 0.32f + s, oy + 17f) + 0.7f * Noise(t * 1.1f, s);
            return Mathf.Max(gentle, tear);
        }

        static float TornSideDistance(int torn, float iL, float iR, float iT, float iB) => torn switch
        {
            0 => iT, 1 => iR, 2 => iB, _ => iL,
        };

        /// <summary>Rounds a corner: inside both edge bands, distance comes from a circle of radius <paramref name="r"/>.</summary>
        static float RoundCorner(float dist, float a, float b, float r)
        {
            if (a >= r || b >= r) return dist;
            float dx = r - a, dy = r - b;
            return Mathf.Min(dist, r - Mathf.Sqrt(dx * dx + dy * dy));
        }

        /// <summary>A fold line: a dark hairline, light on one side, shadow on the other, ink rubbed thin along it.</summary>
        static void Crease(float s, float noise01, float strength, ref float shadow, ref float light, ref float fade)
        {
            if (s > 14f || s < -14f) return; // too far from the fold to matter
            float k = strength * (0.65f + 0.35f * noise01);
            if (s >= 0f) shadow = Mathf.Max(shadow, k * 0.2f * Mathf.Exp(-s / 2.2f));
            else light = Mathf.Max(light, k * 0.32f * Mathf.Exp(s / 1.6f));
            shadow = Mathf.Max(shadow, k * 0.24f * Mathf.Exp(-(s * s) / 0.5f));
            fade = Mathf.Max(fade, k * 0.3f * Mathf.Exp(-(s * s) / 12f));
        }

        /// <summary>A short ink stroke that wavers in weight, breaks in places and frays at the ends.</summary>
        const int RuleWidth = 256, RuleHeight = 12;

        static Color32[] BakeRule(System.Random rng, Color ink)
        {
            const int w = RuleWidth, h = RuleHeight;
            float o = (float)rng.NextDouble() * 100f;
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float centre = h * 0.5f + 0.9f * Noise(u * 3f + o, 1f);
                float half = 0.7f + 0.8f * (Noise(u * 9f + o, 2f) + 0.5f);
                float present = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.32f, Noise(u * 2.6f + o, 3f) + 0.5f));
                float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.12f + Noise(u * 20f + o, 5f) * 0.6f);
                for (int y = 0; y < h; y++)
                {
                    float d = Mathf.Abs(y + 0.5f - centre);
                    float a = Mathf.Clamp01(half + 0.5f - d) * present * Mathf.Clamp01(ends) * (0.45f + 0.3f * (Noise(u * 40f, o) + 0.5f));
                    px[y * w + x] = new Color(ink.r, ink.g, ink.b, a);
                }
            }
            return px;
        }
    }
}
