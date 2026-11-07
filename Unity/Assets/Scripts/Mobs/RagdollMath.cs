using System.Collections.Generic;
using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>Pure helpers for <see cref="ZombieRagdoll"/> and <see cref="RagdollSkipper"/>. No Unity objects, so they're unit tested.</summary>
    public static class RagdollMath
    {
        /// <summary>Index of the point closest to <paramref name="target"/>, or -1 if there are none.</summary>
        public static int NearestIndex(IReadOnlyList<Vector3> points, Vector3 target)
        {
            int best = -1;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float sqr = (points[i] - target).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Physics steps to run for a time skip: the skipped time, capped at <paramref name="maxSeconds"/>,
        /// in whole fixed steps (rounded up). 0 for an empty skip or a bad step length.
        /// </summary>
        public static int SkipSteps(float seconds, float maxSeconds, float fixedDeltaTime)
        {
            if (fixedDeltaTime <= 0f) return 0;
            float simulated = Mathf.Min(Mathf.Max(0f, seconds), Mathf.Max(0f, maxSeconds));
            if (simulated <= 0f) return 0;
            // The small slack stops float noise (0.5 / 0.02 = 25.0000004) adding a step.
            return Mathf.CeilToInt(simulated / fixedDeltaTime - 1e-4f);
        }
    }
}
