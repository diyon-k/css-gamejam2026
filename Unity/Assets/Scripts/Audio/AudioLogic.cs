using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Fires a footstep every <c>stepLength</c> metres travelled, so steps match the walking speed at any
    /// pace and stop when you stop. Feed it distance from whichever clock the walker runs on: the player
    /// passes real-time distance, zombies pass scaled-time distance (so fast-forward gives 3x the steps).
    /// </summary>
    public sealed class StepCadence
    {
        readonly float _headStart;
        float _travelled;
        bool _fresh = true;

        /// <param name="headStart">Fraction of a step already "walked" after a reset, so the first step comes quickly.</param>
        public StepCadence(float headStart = 0.5f) => _headStart = Mathf.Clamp01(headStart);

        /// <summary>Metres travelled since the last step.</summary>
        public float Travelled => _travelled;

        /// <summary>
        /// Adds distance; returns true when a step lands. At most one step per call, so a skip's warp
        /// doesn't machine-gun footsteps.
        /// </summary>
        public bool Advance(float distance, float stepLength)
        {
            if (stepLength <= 0f || distance <= 0f) return false;
            if (_fresh)
            {
                _travelled = _headStart * stepLength;
                _fresh = false;
            }
            _travelled += distance;
            if (_travelled < stepLength) return false;
            _travelled -= stepLength;
            if (_travelled >= stepLength) _travelled = 0f; // a big jump: restart the stride
            return true;
        }

        /// <summary>Call when the walker stops, so the next start gets the head start again.</summary>
        public void Reset()
        {
            _travelled = 0f;
            _fresh = true;
        }
    }

    /// <summary>Heartbeat rate and loudness from the distance to the nearest living zombie.</summary>
    public static class HeartbeatMath
    {
        /// <summary>0 when the nearest zombie is at or beyond <paramref name="far"/>, 1 at or inside <paramref name="near"/>.</summary>
        public static float Intensity(float distance, float near, float far)
        {
            if (float.IsNaN(distance) || distance >= far) return 0f;
            if (distance <= near || far <= near) return 1f;
            return 1f - (distance - near) / (far - near);
        }

        public static float Bpm(float intensity, float restBpm, float panicBpm) =>
            Mathf.Lerp(restBpm, panicBpm, Mathf.Clamp01(intensity));

        /// <summary>Silent at 0 intensity, otherwise between <paramref name="quietest"/> and <paramref name="loudest"/>.</summary>
        public static float Volume(float intensity, float quietest, float loudest) =>
            intensity <= 0f ? 0f : Mathf.Lerp(quietest, loudest, Mathf.Clamp01(intensity));
    }

    /// <summary>
    /// Schedules heartbeats at a changing rate. Each beat is a separate "lub-dub" one-shot, so the rate can
    /// change without pitching the sound up. Run it on unscaled time.
    /// </summary>
    public sealed class HeartbeatClock
    {
        float _phase;

        /// <summary>Progress towards the next beat, 0..1.</summary>
        public float Phase => _phase;

        /// <summary>Advances by <paramref name="dt"/> seconds at <paramref name="bpm"/>; true when a beat is due.</summary>
        public bool Tick(float dt, float bpm)
        {
            if (dt <= 0f || bpm <= 0f) return false;
            _phase += dt * bpm / 60f;
            if (_phase < 1f) return false;
            _phase -= 1f;
            if (_phase >= 1f) _phase = 0f; // a long hitch: one beat, not a burst
            return true;
        }

        /// <summary>Sets how far into a beat the clock is (e.g. 0.8 so the first beat comes soon).</summary>
        public void Reset(float phase = 0f) => _phase = Mathf.Clamp01(phase);
    }

    /// <summary>
    /// Plays reload foley at fixed points of <c>Gun.ReloadProgress01</c> (mag out, mag in, slide). Each call
    /// reports the latest point crossed since the previous call, so a blink that jumps the reload forward
    /// plays one sound for the jump instead of all of them on top of each other.
    /// </summary>
    public sealed class ReloadFoleyTrack
    {
        float[] _marks = System.Array.Empty<float>();
        float _progress;

        /// <summary>Starts a reload with these points (0..1, ascending; negative entries are skipped).</summary>
        public void Begin(float[] marks, float progress = 0f)
        {
            _marks = marks ?? System.Array.Empty<float>();
            _progress = progress;
        }

        /// <summary>Index of the latest mark crossed since the last call (previous &lt; mark &lt;= progress), or -1.</summary>
        public int Advance(float progress)
        {
            int crossed = -1;
            for (int i = 0; i < _marks.Length; i++)
            {
                float m = _marks[i];
                if (m < 0f) continue;
                if (m > _progress && m <= progress) crossed = i;
            }
            if (progress > _progress) _progress = progress;
            return crossed;
        }
    }

    /// <summary>
    /// Which mix the eyes-shut muffle should be in, and how fast to get there. Eyes shut fades in; opening
    /// your eyes snaps back quickly; a grab (interrupted fast-forward) snaps back instantly.
    /// </summary>
    public sealed class EyesMixState
    {
        public bool Shut { get; private set; }

        /// <summary>
        /// Requests a mix. Returns the transition time in real seconds, or a negative number if nothing changed.
        /// </summary>
        public float Request(bool shut, bool interrupted, float shutSeconds, float openSeconds)
        {
            if (shut == Shut) return -1f;
            Shut = shut;
            if (shut) return Mathf.Max(0f, shutSeconds);
            return interrupted ? 0f : Mathf.Max(0f, openSeconds);
        }
    }

    /// <summary>What a zombie says when its state changes.</summary>
    public enum ZombieVocal { None, Groan, Alert }

    /// <summary>
    /// Maps zombie state changes to vocals by state <b>name</b>, so states added later (Patrol, Investigate,
    /// ...) fall back gracefully instead of breaking a switch.
    /// </summary>
    public static class ZombieVocalRules
    {
        public static ZombieVocal OnStateChanged(string previous, string next)
        {
            if (IsAggressive(next)) return IsAggressive(previous) ? ZombieVocal.None : ZombieVocal.Alert;
            if (Is(next, "Investigate") || Is(next, "Search") || Is(next, "Alert")) return ZombieVocal.Groan;
            return ZombieVocal.None; // Idle, Patrol, Dead (Died handles death) and anything unknown
        }

        /// <summary>States where the zombie is hunting you: entering one from calm triggers the alert scream.</summary>
        public static bool IsAggressive(string state) =>
            Is(state, "Chase") || Is(state, "Attack") || Is(state, "Hunt") || Is(state, "Charge");

        static bool Is(string state, string name) =>
            state != null && state.Equals(name, System.StringComparison.OrdinalIgnoreCase);
    }
}
