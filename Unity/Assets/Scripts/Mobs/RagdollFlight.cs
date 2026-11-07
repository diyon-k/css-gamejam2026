using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// Tracks one flight of a launched ragdoll (an axe kill, an axe hit on a corpse, a melee-prop knockdown): it
    /// flies until its fastest body is slower than <see cref="EndSpeed"/>, or for <see cref="MaxSeconds"/> at most
    /// (<see cref="RagdollMath.LaunchEnded"/>). While it flies the bodies use continuous collision and the body is
    /// never frozen. Advance it with world time (Update only, never a skip), so a blink can't end a flight mid-air.
    /// </summary>
    public sealed class RagdollFlight
    {
        public float MaxSeconds { get; }
        public float EndSpeed { get; }
        public bool IsFlying { get; private set; }
        /// <summary>World seconds since the flight began (or the last one, once it has ended).</summary>
        public float Elapsed { get; private set; }

        public RagdollFlight(float maxSeconds, float endSpeed)
        {
            MaxSeconds = Mathf.Max(0f, maxSeconds);
            EndSpeed = Mathf.Max(0f, endSpeed);
        }

        /// <summary>Starts a flight, or starts it over if it was already flying (a second blow).</summary>
        public void Begin()
        {
            IsFlying = true;
            Elapsed = 0f;
        }

        /// <summary>Ends the flight now (the body was frozen or got up).</summary>
        public void End() => IsFlying = false;

        /// <summary>
        /// Moves the flight on. Returns true on the one call that ends it. A call with no time passed (a paused or
        /// frozen-world frame) never ends it.
        /// </summary>
        /// <param name="deltaTime">World seconds since the last call.</param>
        /// <param name="fastestBodySpeed">Speed of the ragdoll's fastest simulated body this frame, in m/s.</param>
        public bool Advance(float deltaTime, float fastestBodySpeed)
        {
            if (!IsFlying || deltaTime <= 0f) return false;
            Elapsed += deltaTime;
            if (!RagdollMath.LaunchEnded(Elapsed, MaxSeconds, fastestBodySpeed, EndSpeed)) return false;
            IsFlying = false;
            return true;
        }
    }
}
