using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// Decides when a ragdoll has finished falling and can be frozen: once its fastest body has stayed
    /// below <see cref="RestSpeed"/> for <see cref="RestSeconds"/>, or after <see cref="MaxSeconds"/>
    /// at the latest. Advance it with world time (Update only, never a skip), so a blink can't freeze
    /// a body mid-air.
    /// </summary>
    public sealed class RagdollSettleClock
    {
        public float MaxSeconds { get; }
        public float RestSpeed { get; }
        public float RestSeconds { get; }
        public float Elapsed { get; private set; }
        public float RestElapsed { get; private set; }
        public bool IsSettled { get; private set; }

        public RagdollSettleClock(float maxSeconds, float restSpeed, float restSeconds)
        {
            MaxSeconds = Mathf.Max(0f, maxSeconds);
            RestSpeed = Mathf.Max(0f, restSpeed);
            RestSeconds = Mathf.Max(0f, restSeconds);
        }

        /// <summary>Moves the clock on. Returns true once the ragdoll counts as settled (and stays true).</summary>
        /// <param name="deltaTime">World seconds since the last call; negative values are ignored.</param>
        /// <param name="fastestBodySpeed">Speed of the ragdoll's fastest body this frame, in m/s.</param>
        public bool Advance(float deltaTime, float fastestBodySpeed)
        {
            if (IsSettled) return true;
            float dt = Mathf.Max(0f, deltaTime);
            Elapsed += dt;
            RestElapsed = fastestBodySpeed <= RestSpeed ? RestElapsed + dt : 0f;
            IsSettled = Elapsed >= MaxSeconds || (dt > 0f && RestElapsed >= RestSeconds);
            return IsSettled;
        }
    }
}
