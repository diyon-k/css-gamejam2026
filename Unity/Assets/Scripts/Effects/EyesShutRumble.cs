using System;
using UnityEngine;

namespace Blink.Effects
{
    /// <summary>Both gamepad motor speeds: <see cref="Low"/> is the heavy rumble, <see cref="High"/> the buzz.</summary>
    public readonly struct Rumble : IEquatable<Rumble>
    {
        public readonly float Low, High;

        public Rumble(float low, float high)
        {
            Low = low;
            High = high;
        }

        public static readonly Rumble Off = default;

        public bool IsOff => Low <= 0f && High <= 0f;

        /// <summary>Per-motor maximum. How layers mix: a short pulse never cuts a gap in a steady layer under it.</summary>
        public static Rumble Max(Rumble a, Rumble b) => new(Mathf.Max(a.Low, b.Low), Mathf.Max(a.High, b.High));

        public Rumble Scaled(float k) => new(Low * k, High * k);

        /// <summary>
        /// What actually goes to the pad: clamped to 0..1 and rounded to <paramref name="steps"/> levels, so the
        /// speeds only change (and get sent) when it matters. A DS4 drops a command sent while another is in flight.
        /// </summary>
        public Rumble Quantized(float steps = 20f) =>
            new(Mathf.Round(Mathf.Clamp01(Low) * steps) / steps, Mathf.Round(Mathf.Clamp01(High) * steps) / steps);

        public bool Equals(Rumble other) => Low == other.Low && High == other.High;
        public override bool Equals(object obj) => obj is Rumble other && Equals(other);
        public override int GetHashCode() => Low.GetHashCode() * 397 ^ High.GetHashCode();
        public override string ToString() => $"({Low:0.###}, {High:0.###})";
    }

    /// <summary>
    /// The rumble while the eyes are shut (fast-forward): a low steady drone for the whole time, plus a "lub-dub"
    /// on every heartbeat. The beats come from <c>PlayerAudio.Heartbeat</c>, so you feel exactly the beats you hear
    /// (65 → 170 bpm as the nearest zombie closes from 15 m), and get stronger with the heartbeat's intensity.
    /// Opening your eyes stops it at once; a grab interrupting fast-forward gives one hard jolt, then stops.
    ///
    /// Pure logic fed real (unscaled) seconds, so 3× fast-forward never speeds it up. Unit tested.
    /// Tuned in <see cref="GamepadHaptics"/>'s inspector; values are before its strength multiplier.
    /// </summary>
    [Serializable]
    public sealed class EyesShutRumble
    {
        [Tooltip("Low-motor drone the whole time your eyes are shut: no zombie in range / one right on top of you.")]
        [SerializeField] Vector2 drone = new(0.08f, 0.16f);
        [Tooltip("Real seconds the drone takes to swell in after your eyes shut.")]
        [SerializeField, Min(0f)] float droneFadeIn = 0.3f;
        [Tooltip("Low motor on the 'lub', with a zombie at the edge of the heartbeat range / right on top of you.")]
        [SerializeField] Vector2 lubLow = new(0.45f, 1f);
        [Tooltip("High motor on the 'lub' (a harder edge as they get close), edge of range / on top of you.")]
        [SerializeField] Vector2 lubHigh = new(0f, 0.3f);
        [Tooltip("The 'dub' is this fraction of the 'lub'.")]
        [SerializeField, Range(0f, 1f)] float dubScale = 0.6f;
        [Tooltip("Real seconds: 'lub' length, 'dub' start after the lub, 'dub' length. Squeezed at fast rates so " +
                 "the dub always ends before the next lub.")]
        [SerializeField, Min(0.01f)] float lubSeconds = 0.08f, dubDelay = 0.18f, dubSeconds = 0.07f;
        [Tooltip("A zombie grabbing you with your eyes shut: both motors, for this many real seconds, then nothing.")]
        [SerializeField] Vector2 jolt = new(1f, 1f);
        [SerializeField, Min(0f)] float joltSeconds = 0.25f;

        /// <summary>Longest part of a beat period the lub-dub may fill before it is squeezed.</summary>
        const float MaxBeatFill = 0.8f;

        [NonSerialized] bool _active;
        [NonSerialized] float _shutAt;
        [NonSerialized] float _beatAt = float.NegativeInfinity, _beatIntensity, _beatPeriod = 1f;
        [NonSerialized] float _joltAt = float.NegativeInfinity;

        /// <summary>The eyes are shut and the layer is running.</summary>
        public bool Active => _active;

        public float JoltSeconds => joltSeconds;

        /// <summary>The eyes shut (fast-forward started).</summary>
        public void Begin(float now)
        {
            _active = true;
            _shutAt = now;
            _beatAt = float.NegativeInfinity;
            _joltAt = float.NegativeInfinity;
        }

        /// <summary>The eyes opened, or a grab interrupted fast-forward (<paramref name="interrupted"/>: jolt, then off).</summary>
        public void End(float now, bool interrupted)
        {
            _active = false;
            _beatAt = float.NegativeInfinity;
            _joltAt = interrupted ? now : float.NegativeInfinity;
        }

        /// <summary>Silences everything at once, jolt included.</summary>
        public void Stop() => End(0f, false);

        /// <summary>A heartbeat landed. Ignored unless the eyes are shut. <paramref name="bpm"/> sets how much room the lub-dub has.</summary>
        public void Beat(float now, float intensity, float bpm)
        {
            if (!_active) return;
            _beatAt = now;
            _beatIntensity = Mathf.Clamp01(intensity);
            _beatPeriod = bpm > 0f ? 60f / bpm : 1f;
        }

        /// <summary>
        /// Motor speeds at real time <paramref name="now"/>, before the strength multiplier.
        /// <paramref name="intensity"/> is the heartbeat's current intensity (0 = no zombie in range, 1 = panic).
        /// </summary>
        public Rumble Sample(float now, float intensity)
        {
            if (!_active)
                return GamepadHaptics.Envelope(now - _joltAt, joltSeconds, 0f, 1, false, out float j)
                    ? new Rumble(jolt.x, jolt.y).Scaled(j)
                    : Rumble.Off;

            float swell = droneFadeIn > 0f ? Mathf.Clamp01((now - _shutAt) / droneFadeIn) : 1f;
            var steady = new Rumble(Mathf.Lerp(drone.x, drone.y, Mathf.Clamp01(intensity)) * swell, 0f);
            return Rumble.Max(steady, LubDub(now - _beatAt));
        }

        /// <summary>The heartbeat pulse <paramref name="t"/> real seconds after the last beat.</summary>
        Rumble LubDub(float t)
        {
            if (float.IsInfinity(t) || t < 0f) return Rumble.Off;
            float k = Mathf.Min(1f, MaxBeatFill * _beatPeriod / (dubDelay + dubSeconds)); // squeeze at fast rates
            float lub = GamepadHaptics.Envelope(t, lubSeconds * k, 0f, 1, false, out float a) ? a : 0f;
            float dub = GamepadHaptics.Envelope(t - dubDelay * k, dubSeconds * k, 0f, 1, false, out float b) ? b * dubScale : 0f;
            float env = Mathf.Max(lub, dub);
            if (env <= 0f) return Rumble.Off;
            float i = _beatIntensity;
            return new Rumble(Mathf.Lerp(lubLow.x, lubLow.y, i), Mathf.Lerp(lubHigh.x, lubHigh.y, i)).Scaled(env);
        }
    }
}
