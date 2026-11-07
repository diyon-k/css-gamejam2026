using System;
using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// Timing for the grab QTE's closing ring. A ring appears, shrinks onto the target and meets it
    /// <see cref="MeetSeconds"/> after appearing; press within <see cref="CurrentWindow"/> of that moment to hit.
    /// Each ring asks for one of four <see cref="GrabKey"/>s (never the same twice running): the right key inside the
    /// window hits. <see cref="Beats"/> hits escape. The wrong key, or the right one outside the window, misses, and
    /// letting the ring pass times out; either way that ring is gone and a fresh one follows after the gap (what a
    /// miss costs is up to the caller).
    /// Pure logic so it's unit tested; <see cref="GrabQte"/> feeds it unscaled time and draws it.
    ///
    /// Presses are ignored while no ring is showing (the intro, and the short gap after each ring), so Space still
    /// held from the blink, or a double tap, doesn't count as a miss.
    /// </summary>
    /// <summary>The four grab keys, by position: W/A/S/D on the keyboard, Y/X/A/B on the gamepad's face buttons.</summary>
    public enum GrabKey { Up, Left, Down, Right }

    public sealed class GrabRing
    {
        public enum Result { None, Hit, Escaped, Miss, TimedOut }

        /// <summary>Ring radius as a multiple of the target's when it appears.</summary>
        public const float StartScale = 4f;
        const float MinScale = 0.2f;

        // Spot picking, in normalised screen units (0..1, y down). Distances are in screen heights.
        public const float EdgeMargin = 0.18f;
        /// <summary>Keeps rings off the zombie's face in the middle of the screen.</summary>
        public const float CentreClearance = 0.22f;
        /// <summary>Each ring lands at least this far from the last, so it visibly jumps.</summary>
        public const float MinJump = 0.3f;

        public int Beats { get; }
        public float MeetSeconds { get; }
        public float WindowSeconds { get; }
        public float WindowShrinkPerBeat { get; }
        public float GapSeconds { get; }

        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public bool Finished { get; private set; }
        public bool Escaped { get; private set; }

        /// <summary>Seconds since the current ring appeared (0 while it hasn't yet).</summary>
        public float RingSeconds { get; private set; }

        public bool RingVisible => !Finished && _wait <= 0f;

        /// <summary>The key the current ring wants.</summary>
        public GrabKey Key { get; private set; }

        /// <summary>Half-width of the hit window for the current ring, in seconds.</summary>
        public float CurrentWindow => Mathf.Max(0.01f, WindowSeconds - WindowShrinkPerBeat * Hits);

        /// <summary>Ring radius over target radius: <see cref="StartScale"/> on appearing, 1 on the beat, then smaller.</summary>
        public float RingScale => Mathf.Max(MinScale, 1f + (StartScale - 1f) * (1f - RingSeconds / MeetSeconds));

        float _wait;
        readonly Func<float> _random01;

        /// <param name="random01">Picks each ring's key; returns 0..1. Defaults to a seeded-by-clock System.Random.</param>
        public GrabRing(int beats, float meetSeconds, float windowSeconds, float windowShrinkPerBeat, float gapSeconds,
            float introSeconds = 0f, Func<float> random01 = null)
        {
            if (random01 == null)
            {
                var rng = new System.Random();
                random01 = () => (float)rng.NextDouble();
            }
            _random01 = random01;
            Key = PickKey(_random01, null);
            Beats = Mathf.Max(1, beats);
            MeetSeconds = Mathf.Max(0.05f, meetSeconds);
            WindowSeconds = windowSeconds;
            WindowShrinkPerBeat = windowShrinkPerBeat;
            GapSeconds = Mathf.Max(0f, gapSeconds);
            _wait = Mathf.Max(0f, introSeconds);
        }

        /// <summary>Advances by <paramref name="dt"/> real seconds. Returns <see cref="Result.TimedOut"/> when a ring passes unanswered.</summary>
        public Result Tick(float dt)
        {
            if (Finished || dt <= 0f) return Result.None;
            if (_wait > 0f)
            {
                _wait -= dt;
                if (_wait > 0f) return Result.None;
                dt = -_wait; // carry the overshoot into the ring
                _wait = 0f;
            }
            RingSeconds += dt;
            if (RingSeconds <= MeetSeconds + CurrentWindow) return Result.None;
            NextRing(missed: true);
            return Result.TimedOut;
        }

        /// <summary>The player pressed <paramref name="key"/>. Hit, Escaped (last hit) or Miss; None if no ring is showing or it's over.</summary>
        public Result Press(GrabKey key)
        {
            if (!RingVisible) return Result.None;
            if (key != Key || Mathf.Abs(RingSeconds - MeetSeconds) > CurrentWindow)
            {
                NextRing(missed: true);
                return Result.Miss;
            }
            if (++Hits >= Beats)
            {
                Finished = Escaped = true;
                return Result.Escaped;
            }
            NextRing(missed: false);
            return Result.Hit;
        }

        /// <summary>Ends the struggle early (the player died). Presses and ticks do nothing after this.</summary>
        public void Stop() => Finished = true;

        void NextRing(bool missed)
        {
            if (missed) Misses++;
            RingSeconds = 0f;
            _wait = GapSeconds;
            Key = PickKey(_random01, Key);
        }

        /// <summary>A random key, never <paramref name="previous"/>, so every new ring visibly asks for something new.</summary>
        public static GrabKey PickKey(Func<float> random01, GrabKey? previous)
        {
            int choices = previous.HasValue ? 3 : 4;
            int i = Mathf.Min((int)(Mathf.Clamp01(random01()) * choices), choices - 1);
            if (previous.HasValue && i >= (int)previous.Value) i++; // skip over the last one
            return (GrabKey)i;
        }

        /// <summary>
        /// A random spot for the next ring in normalised screen space (0..1, y down): inside the margins, clear of the
        /// middle and at least <see cref="MinJump"/> from <paramref name="previous"/>. <paramref name="aspect"/> is width / height.
        /// </summary>
        public static Vector2 PickSpot(Func<float> random01, Vector2? previous, float aspect)
        {
            var centre = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < 30; i++)
            {
                var spot = new Vector2(
                    Mathf.Lerp(EdgeMargin, 1f - EdgeMargin, random01()),
                    Mathf.Lerp(EdgeMargin, 1f - EdgeMargin, random01()));
                if (HeightDistance(spot, centre, aspect) < CentreClearance) continue;
                if (previous.HasValue && HeightDistance(spot, previous.Value, aspect) < MinJump) continue;
                return spot;
            }
            // Nothing fit (only with a broken random source): beside the face, on the other side from last time.
            bool left = previous.HasValue && previous.Value.x > 0.5f;
            float dx = (CentreClearance + 0.08f) / Mathf.Max(aspect, 0.01f);
            float x = Mathf.Clamp(0.5f + (left ? -dx : dx), EdgeMargin, 1f - EdgeMargin);
            return new Vector2(x, 0.5f);
        }

        /// <summary>Distance between two normalised screen points, in screen heights.</summary>
        public static float HeightDistance(Vector2 a, Vector2 b, float aspect) =>
            new Vector2((a.x - b.x) * aspect, a.y - b.y).magnitude;
    }
}
