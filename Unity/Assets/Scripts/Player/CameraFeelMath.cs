using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// A damped spring pulling a <see cref="Vector3"/> back to zero, stepped with the exact closed-form
    /// solution (Ryan Juckett's "damped springs"). Any delta time is stable, including a long hitch, and
    /// stepping 2 x 10 ms lands exactly where one 20 ms step does, so the motion doesn't depend on frame rate.
    ///
    /// Used for camera recoil: kick it with <see cref="AddVelocity"/> and it springs back to rest.
    /// Damping ratios above 1 are treated as 1 (critically damped).
    /// </summary>
    public sealed class Spring3
    {
        const float CriticalEpsilon = 1e-4f;

        float _frequencyHz;
        float _dampingRatio;

        public Spring3(float frequencyHz, float dampingRatio)
        {
            FrequencyHz = frequencyHz;
            DampingRatio = dampingRatio;
        }

        /// <summary>Undamped oscillation frequency. Higher springs back faster.</summary>
        public float FrequencyHz
        {
            get => _frequencyHz;
            set => _frequencyHz = Mathf.Max(0.01f, value);
        }

        /// <summary>1 settles without overshoot; lower values wobble past rest before settling.</summary>
        public float DampingRatio
        {
            get => _dampingRatio;
            set => _dampingRatio = Mathf.Clamp(value, 0.05f, 1f);
        }

        public Vector3 Value { get; set; }
        public Vector3 Velocity { get; set; }

        /// <summary>Instant change of velocity, e.g. a recoil kick. Displacement builds up over the next frames.</summary>
        public void AddVelocity(Vector3 impulse) => Velocity += impulse;

        public void Reset()
        {
            Value = Vector3.zero;
            Velocity = Vector3.zero;
        }

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Coefficients(_frequencyHz, _dampingRatio, dt, out float pp, out float pv, out float vp, out float vv);
            Vector3 x = Value, v = Velocity;
            Value = x * pp + v * pv;
            Velocity = x * vp + v * vv;
        }

        /// <summary>
        /// Peak displacement reached by a spring at rest kicked with a velocity of 1. Divide the peak you want
        /// by this to get the kick, so the inspector can talk in degrees and metres instead of impulses.
        /// </summary>
        public static float PeakPerUnitVelocity(float frequencyHz, float dampingRatio)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, frequencyHz);
            float zeta = Mathf.Clamp(dampingRatio, 0.05f, 1f);
            if (zeta >= 1f - CriticalEpsilon) return 1f / (omega * Mathf.Exp(1f)); // x(t) = t e^(-wt), peak at t = 1/w

            // x(t) = e^(-zwt) sin(at) / a, peaks where tan(at) = a / (zw)
            float alpha = omega * Mathf.Sqrt(1f - zeta * zeta);
            float tPeak = Mathf.Atan2(alpha, zeta * omega) / alpha;
            return Mathf.Exp(-zeta * omega * tPeak) * Mathf.Sin(alpha * tPeak) / alpha;
        }

        static void Coefficients(float frequencyHz, float zeta, float dt,
            out float pp, out float pv, out float vp, out float vv)
        {
            float omega = 2f * Mathf.PI * frequencyHz;
            if (zeta >= 1f - CriticalEpsilon)
            {
                float e = Mathf.Exp(-omega * dt);
                float te = dt * e;
                float tew = te * omega;
                pp = tew + e;
                pv = te;
                vp = -omega * tew;
                vv = -tew + e;
                return;
            }

            float omegaZeta = omega * zeta;
            float alpha = omega * Mathf.Sqrt(1f - zeta * zeta);
            float expTerm = Mathf.Exp(-omegaZeta * dt);
            float expSin = expTerm * Mathf.Sin(alpha * dt);
            float expCos = expTerm * Mathf.Cos(alpha * dt);
            float expOmegaZetaSinOverAlpha = expSin * omegaZeta / alpha;
            pp = expCos + expOmegaZetaSinOverAlpha;
            pv = expSin / alpha;
            vp = -expSin * alpha - omegaZeta * expOmegaZetaSinOverAlpha;
            vv = expCos - expOmegaZetaSinOverAlpha;
        }
    }

    /// <summary>
    /// Walking head bob driven by distance travelled, not by time: the stride phase advances with
    /// horizontal speed, so the bob matches the footsteps at any speed and stops when you stop. A weight
    /// fades the bob in and out with speed and reaches exactly 0 when standing still.
    /// One stride (left step + right step) is one full cycle of <see cref="Phase"/> (2 pi).
    /// </summary>
    public sealed class HeadBob
    {
        const float TwoPi = 2f * Mathf.PI;

        /// <summary>Stride phase in radians, kept in [0, 2 pi).</summary>
        public float Phase { get; private set; }

        /// <summary>0 standing still, 1 at or above the reference speed.</summary>
        public float Weight { get; private set; }

        /// <summary>Side-to-side motion in [-1, 1]: one cycle per stride (left foot, right foot).</summary>
        public float Lateral => Mathf.Sin(Phase) * Weight;

        /// <summary>Up-down motion in [-1, 1]: one cycle per step, lowest as each foot lands.</summary>
        public float Vertical => -Mathf.Cos(2f * Phase) * Weight;

        /// <param name="dt">Seconds since the last step (unscaled).</param>
        /// <param name="horizontalSpeed">Metres per second along the ground.</param>
        /// <param name="stepLength">Metres per step; a stride is two steps.</param>
        /// <param name="referenceSpeed">Speed at which the bob reaches full weight (the walk speed).</param>
        /// <param name="fadeSeconds">Time for the weight to go from 0 to 1 or back.</param>
        public void Step(float dt, float horizontalSpeed, float stepLength, float referenceSpeed, float fadeSeconds)
        {
            if (dt <= 0f) return;
            float speed = Mathf.Max(0f, horizontalSpeed);

            float target = referenceSpeed > 0f ? Mathf.Clamp01(speed / referenceSpeed) : 0f;
            Weight = fadeSeconds > 0f ? Mathf.MoveTowards(Weight, target, dt / fadeSeconds) : target;

            if (stepLength > 0f) Phase = Mathf.Repeat(Phase + Mathf.PI * speed * dt / stepLength, TwoPi);
        }

        public void Reset()
        {
            Phase = 0f;
            Weight = 0f;
        }
    }

    /// <summary>Stateless helpers for <see cref="CameraFeel"/>.</summary>
    public static class CameraFeelMath
    {
        /// <summary>
        /// How much of the reload pose to show at a point in the reload: eases in over the first
        /// <paramref name="rise"/> of the reload, holds, and eases out over the last <paramref name="fall"/>,
        /// so it is 0 at both ends and the camera settles exactly as the reload completes.
        /// </summary>
        public static float ReloadEnvelope(float progress01, float rise, float fall)
        {
            float p = Mathf.Clamp01(progress01);
            float inW = rise > 0f ? Smooth(p / rise) : (p > 0f ? 1f : 0f);
            float outW = fall > 0f ? Smooth((1f - p) / fall) : (p < 1f ? 1f : 0f);
            return Mathf.Min(inW, outW);
        }

        /// <summary>
        /// Final local pose: the base pose with the offsets scaled by <paramref name="intensity"/> layered on top.
        /// Always computed from the stored base pose, so nothing accumulates frame to frame; at intensity 0
        /// it returns the base pose exactly.
        /// </summary>
        public static void Compose(Vector3 basePosition, Quaternion baseRotation, Vector3 positionOffset,
            Vector3 eulerOffset, float intensity, out Vector3 position, out Quaternion rotation)
        {
            if (intensity <= 0f)
            {
                position = basePosition;
                rotation = baseRotation;
                return;
            }
            position = basePosition + positionOffset * intensity;
            rotation = baseRotation * Quaternion.Euler(eulerOffset * intensity);
        }

        /// <summary>Kick that keeps adding up to at most <paramref name="max"/>: scaled down by how far along it already is.</summary>
        public static float SoftCapScale(float current, float max) =>
            max > 0f ? Mathf.Clamp01(1f - Mathf.Abs(current) / max) : 1f;

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
