using UnityEngine;

namespace Blink.Effects
{
    /// <summary>
    /// Pure maths for one bullet tracer: a short bright streak whose head flies from the muzzle to the
    /// impact point at a constant speed, then drains into the impact as its tail catches up.
    /// Distances are along the path (0 = muzzle, <see cref="Distance"/> = end). No Unity objects, so it's unit tested.
    /// </summary>
    public struct TracerFlight
    {
        /// <summary>On the first step the age is held below this fraction of the lifetime, so the first frame
        /// always shows the streak leaving the gun, even after a long frame or at 3x time scale.</summary>
        public const float FirstFrameMaxAge01 = 0.25f;

        public float Distance { get; }
        public float StreakLength { get; }
        public float Lifetime { get; }
        public float Age { get; private set; }
        public bool HasStepped { get; private set; }

        public float Age01 => Lifetime > 0f ? Mathf.Clamp01(Age / Lifetime) : 1f;
        public bool IsDone => Age >= Lifetime;

        /// <param name="distance">Muzzle-to-end distance in metres.</param>
        /// <param name="streakLength">Length of the glowing streak in metres.</param>
        /// <param name="lifetime">Seconds from leaving the muzzle until the tail reaches the end.</param>
        public TracerFlight(float distance, float streakLength, float lifetime)
        {
            Distance = Mathf.Max(0f, distance);
            StreakLength = Mathf.Max(0f, streakLength);
            Lifetime = Mathf.Max(1e-4f, lifetime);
            Age = 0f;
            HasStepped = false;
        }

        /// <summary>Builds a flight whose streak length and lifetime come from the distance.</summary>
        /// <param name="streakFraction">Streak length as a fraction of the distance, before clamping.</param>
        /// <param name="streakMin">Shortest streak in metres.</param>
        /// <param name="streakMax">Longest streak in metres.</param>
        /// <param name="speed">Nominal speed in m/s; the lifetime is clamped to [lifeMin, lifeMax] so it stays readable.</param>
        public static TracerFlight Create(float distance, float streakFraction, float streakMin, float streakMax,
            float speed, float lifeMin, float lifeMax)
        {
            float length = StreakLengthFor(distance, streakFraction, streakMin, streakMax);
            return new TracerFlight(distance, length, LifetimeFor(distance, length, speed, lifeMin, lifeMax));
        }

        /// <summary>Streak length for a shot of this distance, clamped to [min, max].</summary>
        public static float StreakLengthFor(float distance, float fraction, float min, float max) =>
            Mathf.Clamp(Mathf.Max(0f, distance) * fraction, min, Mathf.Max(min, max));

        /// <summary>Time for the whole streak to travel past the end, clamped to [min, max].</summary>
        public static float LifetimeFor(float distance, float streakLength, float speed, float min, float max)
        {
            float travel = Mathf.Max(0f, distance) + Mathf.Max(0f, streakLength);
            float raw = speed > 0f ? travel / speed : max;
            return Mathf.Clamp(raw, min, Mathf.Max(min, max));
        }

        /// <summary>
        /// Where the shot ends: the hit point, or the end of the aim ray at <paramref name="maxDistance"/> on a miss.
        /// </summary>
        public static Vector3 EndPoint(bool hit, Vector3 hitPoint, Ray aimRay, float maxDistance) =>
            hit ? hitPoint : aimRay.origin + aimRay.direction.normalized * Mathf.Max(0f, maxDistance);

        /// <summary>
        /// Advances by this frame's delta. Accumulates per frame rather than storing a start time,
        /// so a time-scale change mid-flight only changes the rate, never jumps or freezes it.
        /// </summary>
        public void Step(float deltaTime)
        {
            float next = Age + Mathf.Max(0f, deltaTime);
            if (!HasStepped) next = Mathf.Min(next, Lifetime * FirstFrameMaxAge01);
            Age = next;
            HasStepped = true;
        }

        /// <summary>Tail and head of the streak at the current age, as distances from the muzzle.</summary>
        public void Segment(out float tail, out float head)
        {
            float travelled = (Distance + StreakLength) * Age01;
            head = Mathf.Min(travelled, Distance);
            tail = Mathf.Clamp(travelled - StreakLength, 0f, Distance);
        }

        /// <summary>Brightness 0..1: holds near full for most of the flight, then drops off.</summary>
        public float Brightness => Fade(Age01);

        /// <summary>Fade curve used by <see cref="Brightness"/>: 1 - t².</summary>
        public static float Fade(float age01)
        {
            float t = Mathf.Clamp01(age01);
            return 1f - t * t;
        }

        /// <summary>
        /// World width that covers roughly the same angle on screen at any distance from the camera, so the
        /// streak isn't fat next to the lens and doesn't vanish far away.
        /// </summary>
        public static float ScreenConstantWidth(float distanceFromCamera, float angularWidth, float minWidth, float maxWidth) =>
            Mathf.Clamp(Mathf.Max(0f, distanceFromCamera) * angularWidth, minWidth, Mathf.Max(minWidth, maxWidth));
    }
}
