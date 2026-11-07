using UnityEngine;

namespace Blink.Effects
{
    /// <summary>
    /// Pure maths for blood impacts: which way a spray faces, how to turn the Vefects splat quads so they point
    /// where we want, and how to split a time skip into small steps. No scene access, so it's unit tested.
    /// </summary>
    public static class BloodSpray
    {
        /// <summary>
        /// The direction a Vefects directional splat streaks along, in the effect root's local space: its quad lies
        /// in the local XZ plane (facing +Y) and the spray runs corner to corner, from +X/-Z towards -X/+Z.
        /// </summary>
        public static readonly Vector3 DirectionalSplatAxis = new Vector3(-1f, 0f, 1f).normalized;

        /// <summary>
        /// Direction blood leaves an entry wound: the surface normal bent back towards the shooter.
        /// </summary>
        /// <param name="surfaceNormal">Normal of the surface that was hit.</param>
        /// <param name="rayDirection">Direction the bullet travelled (shooter to hit point).</param>
        /// <param name="towardShooter">0 = straight out along the normal, 1 = straight back along the ray.</param>
        public static Vector3 SprayDirection(Vector3 surfaceNormal, Vector3 rayDirection, float towardShooter)
        {
            Vector3 back = -rayDirection.normalized;
            Vector3 dir = Vector3.Lerp(surfaceNormal.normalized, back, Mathf.Clamp01(towardShooter));
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : back;
        }

        /// <summary>Tilts a direction upwards by <paramref name="lift"/> (0 = unchanged), so thrown droplets arc.</summary>
        public static Vector3 Lift(Vector3 direction, float lift)
        {
            Vector3 dir = direction.normalized + Vector3.up * Mathf.Max(0f, lift);
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
        }

        /// <summary>Rotation whose local +Y is <paramref name="facing"/>: the way the splat quads and droplet cone face.</summary>
        public static Quaternion FacingRotation(Vector3 facing)
        {
            Vector3 up = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.up;
            return Quaternion.LookRotation(AnyPerpendicular(up), up);
        }

        /// <summary>
        /// Rotation that lays a directional splat flat on a plane with its streak running along
        /// <paramref name="sprayDirection"/> (projected onto the plane). Local +Y is the plane normal.
        /// </summary>
        public static Quaternion DirectionalRotation(Vector3 planeNormal, Vector3 sprayDirection)
        {
            Vector3 up = planeNormal.sqrMagnitude > 1e-6f ? planeNormal.normalized : Vector3.up;
            Vector3 spray = Vector3.ProjectOnPlane(sprayDirection, up);
            spray = spray.sqrMagnitude > 1e-6f ? spray.normalized : AnyPerpendicular(up);
            // The splat axis sits 45 degrees round from local +Z, so turn the forward axis the same amount.
            Vector3 forward = Quaternion.AngleAxis(45f, up) * spray;
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// Splits <paramref name="seconds"/> into equal steps no longer than <paramref name="maxStep"/>, so a big skip
        /// doesn't let fast droplets tunnel through the floor. Returns the step count (0 for no time).
        /// </summary>
        public static int SkipSteps(float seconds, float maxStep, out float step)
        {
            if (seconds <= 0f)
            {
                step = 0f;
                return 0;
            }
            int count = maxStep > 0f ? Mathf.Max(1, Mathf.CeilToInt(seconds / maxStep - 1e-4f)) : 1;
            step = seconds / count;
            return count;
        }

        /// <summary>
        /// True if one particle system has nothing left to show: not looping, played to the end of its duration and
        /// no particles left. Needed because a system paused by <c>ParticleSystem.Simulate</c> still reports
        /// <c>IsAlive</c> even when it has finished.
        /// </summary>
        public static bool IsSpent(bool looping, float time, float duration, int particleCount)
        {
            return !looping && particleCount == 0 && time >= duration - 1e-3f;
        }

        static Vector3 AnyPerpendicular(Vector3 normal)
        {
            Vector3 p = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.ProjectOnPlane(Vector3.forward, normal);
            return p.normalized;
        }
    }
}
