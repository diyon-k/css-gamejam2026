using System;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>Where an axe swing is, as gameplay sees it.</summary>
    public enum AxeSwingPhase
    {
        /// <summary>Not swinging.</summary>
        Idle,
        /// <summary>Pulling the axe back over the right shoulder.</summary>
        WindUp,
        /// <summary>The fast diagonal chop. The strike apex is inside this phase.</summary>
        Strike,
        /// <summary>Bringing the axe back up to the ready pose.</summary>
        Recover,
    }

    /// <summary>The key poses a swing blends between.</summary>
    public enum AxeKeyPose
    {
        Idle,
        WindUp,
        Apex,
        FollowThrough,
    }

    /// <summary>One moment of a swing: blend <see cref="From"/> towards <see cref="To"/> by <see cref="Weight"/>.</summary>
    public readonly struct AxeSwingSample
    {
        public readonly AxeSwingPhase Phase;
        public readonly AxeKeyPose From;
        public readonly AxeKeyPose To;

        /// <summary>Eased blend weight. Usually 0..1, but a curve may overshoot on purpose.</summary>
        public readonly float Weight;

        public AxeSwingSample(AxeSwingPhase phase, AxeKeyPose from, AxeKeyPose to, float weight)
        {
            Phase = phase;
            From = from;
            To = to;
            Weight = weight;
        }

        public static AxeSwingSample Rest => new AxeSwingSample(AxeSwingPhase.Idle, AxeKeyPose.Idle, AxeKeyPose.Idle, 0f);
    }

    /// <summary>
    /// The timing of one axe swing: wind-up (Idle to WindUp), strike (WindUp to Apex to FollowThrough) and
    /// recover (FollowThrough back to Idle), each eased by its own <see cref="AnimationCurve"/>.
    ///
    /// The strike apex sits at a fixed fraction of the strike, and the strike is split into two segments
    /// that meet there, so the Apex key pose is exactly where the axe is when <see cref="ApexSeconds"/> is
    /// reached. Sampling depends only on elapsed time, so a skip lands on the right pose.
    /// </summary>
    [Serializable]
    public sealed class AxeSwingTimeline
    {
        [SerializeField, Min(0.01f)] float windUpSeconds = 0.15f;
        [SerializeField, Min(0.01f)] float strikeSeconds = 0.12f;
        [Tooltip("Where in the strike the blade passes the Apex pose and the hit check happens (0..1 of the strike).")]
        [SerializeField, Range(0.05f, 0.95f)] float apexInStrike = 0.5f;
        [SerializeField, Min(0.01f)] float recoverSeconds = 0.3f;

        [Tooltip("Idle to WindUp, over the wind-up (time and weight both 0..1).")]
        [SerializeField] AnimationCurve windUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("WindUp to Apex, over the first part of the strike. Accelerates into the apex.")]
        [SerializeField] AnimationCurve strikeInCurve = Accelerate();
        [Tooltip("Apex to FollowThrough, over the rest of the strike. Decelerates out of the apex.")]
        [SerializeField] AnimationCurve strikeOutCurve = Decelerate();
        [Tooltip("FollowThrough back to Idle, over the recovery.")]
        [SerializeField] AnimationCurve recoverCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public AxeSwingTimeline() { }

        public AxeSwingTimeline(float windUpSeconds, float strikeSeconds, float apexInStrike, float recoverSeconds)
        {
            this.windUpSeconds = Mathf.Max(0.01f, windUpSeconds);
            this.strikeSeconds = Mathf.Max(0.01f, strikeSeconds);
            this.apexInStrike = Mathf.Clamp(apexInStrike, 0.05f, 0.95f);
            this.recoverSeconds = Mathf.Max(0.01f, recoverSeconds);
        }

        public float WindUpSeconds => windUpSeconds;
        public float StrikeSeconds => strikeSeconds;
        public float RecoverSeconds => recoverSeconds;

        /// <summary>Seconds from the start of the swing to the strike apex.</summary>
        public float ApexSeconds => windUpSeconds + strikeSeconds * apexInStrike;

        /// <summary>Seconds from the start of the swing to the end of the strike (the follow-through).</summary>
        public float StrikeEndSeconds => windUpSeconds + strikeSeconds;

        public float TotalSeconds => windUpSeconds + strikeSeconds + recoverSeconds;

        /// <summary>Replaces the easing curves (null means linear).</summary>
        public void SetCurves(AnimationCurve windUp, AnimationCurve strikeIn, AnimationCurve strikeOut, AnimationCurve recover)
        {
            windUpCurve = windUp;
            strikeInCurve = strikeIn;
            strikeOutCurve = strikeOut;
            recoverCurve = recover;
        }

        /// <summary>The phase a swing is in after <paramref name="elapsed"/> seconds.</summary>
        public AxeSwingPhase PhaseAt(float elapsed)
        {
            if (elapsed < 0f || elapsed >= TotalSeconds) return AxeSwingPhase.Idle;
            if (elapsed < windUpSeconds) return AxeSwingPhase.WindUp;
            if (elapsed < StrikeEndSeconds) return AxeSwingPhase.Strike;
            return AxeSwingPhase.Recover;
        }

        /// <summary>The key poses to blend and how far, <paramref name="elapsed"/> seconds into a swing.</summary>
        public AxeSwingSample Sample(float elapsed)
        {
            if (elapsed < 0f || elapsed >= TotalSeconds) return AxeSwingSample.Rest;

            if (elapsed < windUpSeconds)
                return new AxeSwingSample(AxeSwingPhase.WindUp, AxeKeyPose.Idle, AxeKeyPose.WindUp,
                    Ease(windUpCurve, elapsed / windUpSeconds));

            if (elapsed < ApexSeconds)
            {
                float t = (elapsed - windUpSeconds) / (ApexSeconds - windUpSeconds);
                return new AxeSwingSample(AxeSwingPhase.Strike, AxeKeyPose.WindUp, AxeKeyPose.Apex, Ease(strikeInCurve, t));
            }

            if (elapsed < StrikeEndSeconds)
            {
                float t = (elapsed - ApexSeconds) / (StrikeEndSeconds - ApexSeconds);
                return new AxeSwingSample(AxeSwingPhase.Strike, AxeKeyPose.Apex, AxeKeyPose.FollowThrough, Ease(strikeOutCurve, t));
            }

            return new AxeSwingSample(AxeSwingPhase.Recover, AxeKeyPose.FollowThrough, AxeKeyPose.Idle,
                Ease(recoverCurve, (elapsed - StrikeEndSeconds) / recoverSeconds));
        }

        /// <summary>Evaluates a 0..1 easing curve; a missing or empty curve is linear.</summary>
        public static float Ease(AnimationCurve curve, float t01)
        {
            t01 = Mathf.Clamp01(t01);
            return curve == null || curve.length == 0 ? t01 : curve.Evaluate(t01);
        }

        /// <summary>Starts at rest and leaves at twice the average speed (ease-in).</summary>
        public static AnimationCurve Accelerate() =>
            new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));

        /// <summary>Enters at twice the average speed and settles at rest (ease-out).</summary>
        public static AnimationCurve Decelerate() =>
            new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
    }

    /// <summary>What one <see cref="AxeSwingState.Advance"/> step went through.</summary>
    public readonly struct AxeSwingStep
    {
        /// <summary>The step reached the strike apex (at most once per swing, even across a skip).</summary>
        public readonly bool CrossedApex;

        /// <summary>The step reached the end of the swing.</summary>
        public readonly bool Finished;

        public AxeSwingStep(bool crossedApex, bool finished)
        {
            CrossedApex = crossedApex;
            Finished = finished;
        }
    }

    /// <summary>
    /// The clock of one axe swing. Advance it by world seconds, either a frame's <c>Time.deltaTime</c> or a
    /// whole blink skip in one go: the result reports whether the apex was crossed, so a skip that jumps
    /// past the apex still triggers the hit check exactly once.
    /// </summary>
    public sealed class AxeSwingState
    {
        float _elapsed;
        bool _apexReached;

        public bool IsSwinging { get; private set; }

        /// <summary>Seconds since the swing started (0 when idle).</summary>
        public float Elapsed => _elapsed;

        /// <summary>True once the current swing has passed its apex.</summary>
        public bool ApexReached => _apexReached;

        /// <summary>Normalized progress through the whole swing (0 when idle).</summary>
        public float Progress01(AxeSwingTimeline timeline) =>
            IsSwinging ? Mathf.Clamp01(_elapsed / timeline.TotalSeconds) : 0f;

        /// <summary>Starts a swing. Returns false (and changes nothing) if one is already running.</summary>
        public bool TryStart()
        {
            if (IsSwinging) return false;
            IsSwinging = true;
            _elapsed = 0f;
            _apexReached = false;
            return true;
        }

        /// <summary>Stops the swing without reaching the apex or the end.</summary>
        public void Cancel()
        {
            IsSwinging = false;
            _elapsed = 0f;
            _apexReached = false;
        }

        /// <summary>Moves the swing forward by <paramref name="seconds"/> of world time.</summary>
        public AxeSwingStep Advance(float seconds, AxeSwingTimeline timeline)
        {
            if (!IsSwinging || seconds <= 0f) return default;

            _elapsed += seconds;
            bool crossedApex = !_apexReached && _elapsed >= timeline.ApexSeconds;
            if (crossedApex) _apexReached = true;

            bool finished = _elapsed >= timeline.TotalSeconds;
            if (finished)
            {
                IsSwinging = false;
                _elapsed = 0f;
            }

            return new AxeSwingStep(crossedApex, finished);
        }
    }

    /// <summary>
    /// One key pose of the axe view model: the rotation of the swing pivot (at the right shoulder) and the
    /// grip's local position and rotation under it. The grip is the point on the haft where the right hand
    /// holds it; the axe mesh and both hand IK targets hang off the grip.
    /// </summary>
    [Serializable]
    public struct AxePose
    {
        [SerializeField] Vector3 pivotEuler;
        [SerializeField] Vector3 gripPosition;
        [SerializeField] Vector3 gripEuler;

        public AxePose(Vector3 pivotEuler, Vector3 gripPosition, Vector3 gripEuler)
        {
            this.pivotEuler = pivotEuler;
            this.gripPosition = gripPosition;
            this.gripEuler = gripEuler;
        }

        public Quaternion PivotRotation => Quaternion.Euler(pivotEuler);
        public Vector3 GripPosition => gripPosition;
        public Quaternion GripRotation => Quaternion.Euler(gripEuler);

        /// <summary>
        /// Blends two poses. Rotations slerp, so the grip travels on an arc around the pivot and the axe
        /// turns about a fixed axis between keys. Unclamped, so an overshooting curve overshoots the pose.
        /// </summary>
        public static void Blend(AxePose a, AxePose b, float weight,
            out Quaternion pivotRotation, out Vector3 gripPosition, out Quaternion gripRotation)
        {
            pivotRotation = Quaternion.SlerpUnclamped(a.PivotRotation, b.PivotRotation, weight);
            gripPosition = Vector3.LerpUnclamped(a.gripPosition, b.gripPosition, weight);
            gripRotation = Quaternion.SlerpUnclamped(a.GripRotation, b.GripRotation, weight);
        }
    }
}
