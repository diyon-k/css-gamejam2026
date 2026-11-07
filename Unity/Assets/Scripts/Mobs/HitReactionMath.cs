using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// A critically damped spring that rests at zero, for one flinch channel (a rotation vector in degrees).
    /// <see cref="Kick"/> adds velocity so the offset swings out and settles back by itself, with no overshoot.
    /// Kicks add up, so several hits in a row push it further. <see cref="Advance"/> is exact for any step
    /// (closed form), so a whole blink skip can be advanced in one call. Used by <see cref="ZombieHitReaction"/>.
    /// </summary>
    public sealed class FlinchSpring
    {
        /// <summary>Below this offset (degrees) and the matching speed the spring snaps to rest.</summary>
        public const float RestEpsilon = 0.01f;

        float _omega;

        public FlinchSpring(float peakSeconds) => PeakSeconds = peakSeconds;

        /// <summary>Seconds from a kick (from rest) to the biggest offset. About 5x this to settle.</summary>
        public float PeakSeconds
        {
            get => 1f / _omega;
            set => _omega = 1f / Mathf.Max(0.001f, value);
        }

        public Vector3 Offset { get; private set; }
        public Vector3 Velocity { get; private set; }
        public bool IsAtRest => Offset == Vector3.zero && Velocity == Vector3.zero;

        /// <summary>Adds a kick that, from rest, peaks at <paramref name="peak"/> after <see cref="PeakSeconds"/>.</summary>
        public void Kick(Vector3 peak) => Velocity += peak * (_omega * (float)System.Math.E);

        /// <summary>Moves the spring on by <paramref name="deltaTime"/> seconds and returns the new offset.</summary>
        public Vector3 Advance(float deltaTime)
        {
            if (deltaTime <= 0f || IsAtRest) return Offset;
            float w = _omega;
            float decay = Mathf.Exp(-w * deltaTime);
            Vector3 c = Velocity + w * Offset;
            Offset = (Offset + c * deltaTime) * decay;
            Velocity = (Velocity - w * deltaTime * c) * decay;
            if (Offset.sqrMagnitude < RestEpsilon * RestEpsilon &&
                Velocity.sqrMagnitude < RestEpsilon * w * (RestEpsilon * w))
                Reset();
            return Offset;
        }

        public void Reset()
        {
            Offset = Vector3.zero;
            Velocity = Vector3.zero;
        }
    }

    /// <summary>
    /// A short slow-down after a hit: the speed factor drops at once to 1 - depth and eases back to 1 over the
    /// duration. A hit during a stagger stacks on what is left of it.
    /// </summary>
    public sealed class StaggerState
    {
        public float Depth { get; private set; }
        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsActive => Depth > 0f && Elapsed < Duration;

        /// <summary>Multiplier for movement speed: 1 = unhurt, 0 = stopped dead.</summary>
        public float SpeedFactor => IsActive ? 1f - Depth * HitReactionMath.StaggerEnvelope(Elapsed / Duration) : 1f;

        /// <param name="depth">Speed taken away at the moment of the hit, 0..1.</param>
        /// <param name="duration">Seconds to recover.</param>
        /// <param name="stackShare">Share of a new hit's depth added on top of the stagger still left.</param>
        /// <param name="maxDepth">Cap on the stacked depth.</param>
        public void Kick(float depth, float duration, float stackShare = 0.35f, float maxDepth = 0.9f)
        {
            depth = Mathf.Clamp01(depth);
            if (depth <= 0f || duration <= 0f) return;
            float left = 1f - SpeedFactor;
            float remaining = IsActive ? Duration - Elapsed : 0f;
            Depth = Mathf.Min(Mathf.Clamp01(maxDepth), Mathf.Max(depth, left + depth * stackShare));
            Duration = Mathf.Max(duration, remaining);
            Elapsed = 0f;
        }

        public void Advance(float deltaTime)
        {
            if (!IsActive || deltaTime <= 0f) return;
            Elapsed += deltaTime;
            if (Elapsed >= Duration) Reset();
        }

        public void Reset()
        {
            Depth = 0f;
            Duration = 0f;
            Elapsed = 0f;
        }
    }

    /// <summary>Pure helpers for <see cref="ZombieHitReaction"/> and <see cref="CorpseShotReaction"/>. Unit tested.</summary>
    public static class HitReactionMath
    {
        /// <summary>
        /// How hard a hit (or one frame's worth of hits) lands, from the damage it did: 1 at
        /// <paramref name="referenceDamage"/> (a pistol shot), growing more slowly above it, capped at
        /// <paramref name="max"/>. 0 for no damage.
        /// </summary>
        public static float Strength(float damage, float referenceDamage, float exponent, float max)
        {
            if (damage <= 0f || referenceDamage <= 0f) return 0f;
            return Mathf.Min(Mathf.Max(0f, max), Mathf.Pow(damage / referenceDamage, Mathf.Max(0.01f, exponent)));
        }

        /// <summary>
        /// Unit axis that tips something standing along <paramref name="up"/> over towards <paramref name="direction"/>
        /// (flattened): rotating by a positive angle about it moves the top along the shot. Zero if the shot is vertical.
        /// </summary>
        public static Vector3 BendAxis(Vector3 direction, Vector3 up)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, up);
            if (flat.sqrMagnitude < 1e-8f) return Vector3.zero;
            return Vector3.Cross(up, flat).normalized;
        }

        /// <summary>
        /// Twist about <paramref name="up"/> from a hit off the body's centre line, -1..1: a positive angle about
        /// <paramref name="up"/> times this turns the side that was hit along the shot. <paramref name="offset"/> is the hit point minus the spine,
        /// <paramref name="radius"/> the sideways distance for a full twist.
        /// </summary>
        public static float TwistAmount(Vector3 offset, Vector3 direction, Vector3 up, float radius)
        {
            if (radius <= 0f) return 0f;
            float torque = Vector3.Dot(Vector3.Cross(offset, direction.normalized), up.normalized);
            return Mathf.Clamp(torque / radius, -1f, 1f);
        }

        /// <summary>
        /// Rotation axis that swings a limb pointing along <paramref name="limb"/> towards <paramref name="direction"/>,
        /// unit length; <paramref name="fallback"/> when they are (nearly) parallel.
        /// </summary>
        public static Vector3 SwingAxis(Vector3 limb, Vector3 direction, Vector3 fallback)
        {
            Vector3 axis = Vector3.Cross(limb, direction);
            return axis.sqrMagnitude > 1e-6f * limb.sqrMagnitude * direction.sqrMagnitude ? axis.normalized : fallback;
        }

        /// <summary>1 when the hit is within <paramref name="near"/> of the shoulder, fading to 0 at <paramref name="far"/>.</summary>
        public static float ShoulderShare(float distance, float near, float far)
        {
            if (far <= near) return distance <= near ? 1f : 0f;
            return 1f - Mathf.Clamp01((distance - near) / (far - near));
        }

        /// <summary>Stagger shape over its duration (t = 0..1): full at the hit, easing out to 0.</summary>
        public static float StaggerEnvelope(float t)
        {
            float left = 1f - Mathf.Clamp01(t);
            return left * left;
        }

        /// <summary>
        /// Moves an exponentially decaying slide on by <paramref name="deltaTime"/>: returns the distance covered and
        /// slows <paramref name="velocity"/> down. Exact for any step; a slide covers velocity x <paramref name="timeConstant"/> in total.
        /// </summary>
        public static Vector3 DecaySlide(ref Vector3 velocity, float deltaTime, float timeConstant)
        {
            if (deltaTime <= 0f || velocity == Vector3.zero) return Vector3.zero;
            if (timeConstant <= 0f)
            {
                velocity = Vector3.zero;
                return Vector3.zero;
            }
            float decay = Mathf.Exp(-deltaTime / timeConstant);
            Vector3 moved = velocity * (timeConstant * (1f - decay));
            velocity *= decay;
            if (velocity.sqrMagnitude < 1e-6f) velocity = Vector3.zero;
            return moved;
        }

        /// <summary>
        /// Direction to push a corpse's limb: along the shot, but with <paramref name="floorDeflect"/> of its downward
        /// part taken out (a shot from eye height would only press the body into the floor) and a little
        /// <paramref name="lift"/> added. Unit length; zero for a zero direction.
        /// </summary>
        public static Vector3 PushDirection(Vector3 direction, Vector3 up, float floorDeflect, float lift)
        {
            if (direction.sqrMagnitude < 1e-8f) return Vector3.zero;
            up = up.normalized;
            Vector3 v = direction.normalized;
            float down = -Vector3.Dot(v, up);
            if (down > 0f) v += up * (down * Mathf.Clamp01(floorDeflect));
            v += up * Mathf.Max(0f, lift);
            return v.sqrMagnitude > 1e-8f ? v.normalized : direction.normalized;
        }
    }
}
