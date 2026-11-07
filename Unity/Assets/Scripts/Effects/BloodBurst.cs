using System;
using UnityEngine;

namespace Blink.Effects
{
    /// <summary>How much stronger the blood is on a headshot (Inspector settings of <see cref="BloodImpactSpawner"/>).</summary>
    [Serializable]
    public struct HeadshotBlood
    {
        [Tooltip("Size of the entry burst and the floor splat, times the body-hit size.")]
        [SerializeField, Min(1f)] float scale;
        [Tooltip("Particles per burst (splats and droplets), times the body-hit count.")]
        [SerializeField, Min(1f)] float countMultiplier;
        [Tooltip("Size of the exit spray, times the body-hit exit size.")]
        [SerializeField, Min(1f)] float exitScaleMultiplier;
        [Tooltip("Extra entry bursts fanned out around the first one.")]
        [SerializeField, Range(0, 6)] int extraBursts;
        [Tooltip("Degrees the extra bursts fan out from the main spray.")]
        [SerializeField, Range(0f, 90f)] float extraBurstSpread;

        public HeadshotBlood(float scale, float countMultiplier, float exitScaleMultiplier, int extraBursts, float extraBurstSpread)
        {
            this.scale = scale;
            this.countMultiplier = countMultiplier;
            this.exitScaleMultiplier = exitScaleMultiplier;
            this.extraBursts = extraBursts;
            this.extraBurstSpread = extraBurstSpread;
        }

        public static HeadshotBlood Default => new(1.6f, 2.5f, 1.8f, 2, 35f);

        public float Scale => scale;
        public float CountMultiplier => countMultiplier;
        public float ExitScaleMultiplier => exitScaleMultiplier;
        public int ExtraBursts => extraBursts;
        public float ExtraBurstSpread => extraBurstSpread;
    }

    /// <summary>The intensity of one blood hit: what <see cref="BloodImpactSpawner"/> spawns for it.</summary>
    public readonly struct BloodBurst
    {
        /// <summary>Multiplier on the entry burst and floor splat size.</summary>
        public readonly float Scale;

        /// <summary>Multiplier on the particle count of every burst.</summary>
        public readonly float CountMultiplier;

        /// <summary>Exit spray size relative to the entry (0 = no exit spray).</summary>
        public readonly float ExitScale;

        /// <summary>Chance (0..1) of a floor splat.</summary>
        public readonly float FloorChance;

        /// <summary>Extra entry bursts around the main one, and how far (degrees) they fan out.</summary>
        public readonly int ExtraBursts;

        public readonly float ExtraBurstSpread;

        public BloodBurst(float scale, float countMultiplier, float exitScale, float floorChance, int extraBursts,
            float extraBurstSpread)
        {
            Scale = scale;
            CountMultiplier = countMultiplier;
            ExitScale = exitScale;
            FloorChance = floorChance;
            ExtraBursts = extraBursts;
            ExtraBurstSpread = extraBurstSpread;
        }

        /// <summary>
        /// Body hit: the spawner's own settings as they are. Headshot: bigger and denser bursts, extra bursts fanned
        /// around the first, a bigger exit spray, and always a floor splat. Never weaker than a body hit.
        /// </summary>
        public static BloodBurst For(bool headshot, float exitScale, float floorChance, HeadshotBlood head)
        {
            exitScale = Mathf.Max(0f, exitScale);
            floorChance = Mathf.Clamp01(floorChance);
            if (!headshot) return new BloodBurst(1f, 1f, exitScale, floorChance, 0, 0f);
            return new BloodBurst(
                Mathf.Max(1f, head.Scale),
                Mathf.Max(1f, head.CountMultiplier),
                exitScale * Mathf.Max(1f, head.ExitScaleMultiplier),
                1f,
                Mathf.Max(0, head.ExtraBursts),
                Mathf.Max(0f, head.ExtraBurstSpread));
        }

        /// <summary>
        /// A particle count scaled by <paramref name="multiplier"/>, rounded and never below the original
        /// (so a burst of 1 stays at least 1).
        /// </summary>
        public static int ScaleCount(int count, float multiplier) =>
            Mathf.Max(count, Mathf.RoundToInt(count * Mathf.Max(1f, multiplier)));
    }
}
