using System;

namespace Blink.Puzzles
{
    public enum FloodState { Idle, Flooding, Draining, Solved, Drowned }

    /// <summary>
    /// The flooded basement's water (docs/puzzles/FLOODED_BASEMENT.md, "Water level"), with no Unity calls so it's unit
    /// tested. <see cref="Level"/> is metres of water above the room floor.
    /// <list type="bullet">
    /// <item><b>Flooding:</b> rises <see cref="RiseRate"/> per world second, one <see cref="BlinkJump"/> per blink and one
    /// per <see cref="ShutJumpInterval"/> of real time with the eyes held shut. At the drown height it's
    /// <see cref="FloodState.Drowned"/>.</item>
    /// <item><b>Draining</b> (drain opened): falls <see cref="DrainRate"/> per world second; a blink skips the drain forward
    /// instead of raising the water. Empty = <see cref="FloodState.Solved"/>, for good.</item>
    /// </list>
    /// Idle → Flooding → Draining → Solved, or Flooding → Drowned → (Reset) → Idle.
    /// </summary>
    public sealed class FloodLevel
    {
        /// <summary>Metres per world second while flooding.</summary>
        public float RiseRate { get; set; }

        /// <summary>Metres one blink (or one <see cref="ShutJumpInterval"/> with the eyes shut) adds.</summary>
        public float BlinkJump { get; set; }

        /// <summary>Real seconds of eyes held shut per jump.</summary>
        public float ShutJumpInterval { get; set; }

        /// <summary>Metres per world second while draining.</summary>
        public float DrainRate { get; set; }

        /// <summary>The water never goes above this (the ceiling).</summary>
        public float MaxLevel { get; set; } = float.PositiveInfinity;

        public FloodState State { get; private set; } = FloodState.Idle;
        public float Level { get; private set; }

        /// <summary>Jumps since the flood started (blinks and eyes-shut); goes up by one per surge, for the effects.</summary>
        public int JumpCount { get; private set; }

        float _shut; // real seconds the eyes have been shut since the last jump

        public FloodLevel(float riseRate, float blinkJump, float shutJumpInterval, float drainRate)
        {
            RiseRate = riseRate;
            BlinkJump = blinkJump;
            ShutJumpInterval = shutJumpInterval;
            DrainRate = drainRate;
        }

        /// <summary>Idle → Flooding (the player walked in).</summary>
        public void Start()
        {
            if (State != FloodState.Idle) return;
            State = FloodState.Flooding;
            Level = 0f;
            JumpCount = 0;
            _shut = 0f;
        }

        /// <summary>
        /// Every frame. <paramref name="worldDt"/> is scaled time (eyes-shut fast-forward speeds it up),
        /// <paramref name="realDt"/> unscaled. <paramref name="drownHeight"/> is the eyes' height above the floor.
        /// </summary>
        public void Tick(float worldDt, float realDt, bool eyesShut, float drownHeight)
        {
            switch (State)
            {
                case FloodState.Flooding:
                    Raise(RiseRate * Math.Max(0f, worldDt));
                    if (eyesShut && ShutJumpInterval > 0f)
                    {
                        _shut += Math.Max(0f, realDt);
                        while (_shut >= ShutJumpInterval)
                        {
                            _shut -= ShutJumpInterval;
                            Jump();
                        }
                    }
                    else _shut = 0f;
                    if (Level >= drownHeight) State = FloodState.Drowned;
                    break;
                case FloodState.Draining:
                    Lower(DrainRate * Math.Max(0f, worldDt));
                    break;
            }
        }

        /// <summary>A blink (world skipped by <paramref name="skipSeconds"/>). Flooding: one jump. Draining: drains that much sooner.</summary>
        public void Blink(float skipSeconds)
        {
            if (State == FloodState.Flooding) Jump();
            else if (State == FloodState.Draining) Lower(DrainRate * Math.Max(0f, skipSeconds));
        }

        /// <summary>Flooding → Draining (the cover's off).</summary>
        public void OpenDrain()
        {
            if (State != FloodState.Flooding) return;
            State = FloodState.Draining;
            _shut = 0f;
            if (Level <= 0f) Lower(0f);
        }

        /// <summary>Back to Idle and dry (after a respawn). Does nothing once solved.</summary>
        public void Reset()
        {
            if (State == FloodState.Solved) return;
            State = FloodState.Idle;
            Level = 0f;
            JumpCount = 0;
            _shut = 0f;
        }

        void Jump()
        {
            JumpCount++;
            Raise(BlinkJump);
        }

        void Raise(float metres) => Level = Math.Min(MaxLevel, Level + metres);

        void Lower(float metres)
        {
            Level = Math.Max(0f, Level - metres);
            if (Level <= 0f) State = FloodState.Solved;
        }
    }
}
