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

        /// <summary>
        /// How much of a push a limb at height <paramref name="y"/> gets, for a body spanning
        /// <paramref name="minY"/>..<paramref name="maxY"/>: <paramref name="feetShare"/> at the bottom, all of it at
        /// the top, linear between (clamped). A flat body gets all of it.
        /// </summary>
        public static float HeightShare(float y, float minY, float maxY, float feetShare)
        {
            if (maxY - minY < 1e-4f) return 1f;
            return Mathf.Lerp(feetShare, 1f, Mathf.InverseLerp(minY, maxY, y));
        }

        /// <summary>
        /// Whole-body velocity for a launch from a blow (the axe): <paramref name="speed"/> m/s along
        /// <paramref name="direction"/> flattened onto the ground plane, plus <paramref name="lift"/> m/s along
        /// <paramref name="up"/>. Flattening means a blow from eye height down at the legs throws the body away,
        /// not into the floor. A straight-down blow gets the lift only. Negative speeds count as zero.
        /// </summary>
        public static Vector3 LaunchVelocity(Vector3 direction, Vector3 up, float speed, float lift)
        {
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
            up = up.normalized;
            Vector3 flat = direction - up * Vector3.Dot(direction, up);
            Vector3 v = up * Mathf.Max(0f, lift);
            if (flat.sqrMagnitude > 1e-8f) v += flat.normalized * Mathf.Max(0f, speed);
            return v;
        }

        /// <summary>
        /// True once a launched body no longer needs continuous collision: its fastest body has slowed below
        /// <paramref name="endSpeed"/> m/s (too slow to pass through a wall in one step), or it has been flying for
        /// <paramref name="maxSeconds"/> world seconds.
        /// </summary>
        public static bool LaunchEnded(float elapsed, float maxSeconds, float fastestBodySpeed, float endSpeed) =>
            elapsed >= maxSeconds || fastestBodySpeed < endSpeed;

        /// <summary>
        /// May a limp body be frozen now? Only a dead one (a knocked-down body gets up instead, so it never freezes)
        /// that has <paramref name="settled"/> and isn't still flying from a launch (never frozen mid-air).
        /// </summary>
        public static bool MayFreeze(bool settled, bool knockedDown, bool flying) => settled && !knockedDown && !flying;

        /// <summary>
        /// One world-time step of a limp body (<see cref="ZombieRagdoll"/>'s Update): moves its flight on, then its
        /// settle clock, and says whether to freeze it now. A knocked-down body's settle clock doesn't run (it gets up
        /// instead of freezing), and a flying body is never frozen (<see cref="MayFreeze"/>).
        /// </summary>
        /// <param name="flight">Its flight, or null if it was never launched.</param>
        /// <param name="settle">Its settle clock, or null if it's already frozen (woken by a hit, only its flight runs).</param>
        /// <param name="flightEnded">True on the step its flight ended: continuous collision can go off.</param>
        /// <returns>True to freeze it now.</returns>
        public static bool StepLimp(RagdollFlight flight, RagdollSettleClock settle, bool knockedDown, float deltaTime,
                                    float fastestBodySpeed, out bool flightEnded)
        {
            flightEnded = flight != null && flight.Advance(deltaTime, fastestBodySpeed);
            if (knockedDown || settle == null) return false;
            bool settled = settle.Advance(deltaTime, fastestBodySpeed);
            return MayFreeze(settled, knockedDown, flight != null && flight.IsFlying);
        }
    }
}
