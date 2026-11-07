using System;
using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// Procedural motion for the first-person axe: a subtle idle sway and a diagonal chop from the top
    /// right of the screen to the bottom left. Visual only: it never checks for hits or deals damage. Melee
    /// gameplay calls <see cref="PlaySwing"/> and does its hit check on <see cref="StrikeApex"/>.
    ///
    /// The axe hangs off <see cref="grip"/> (the point where the right hand holds the haft), which hangs off
    /// <see cref="swingPivot"/> (at the right shoulder). Both hands are Animation Rigging two-bone IK
    /// targets parented under the grip, so moving the grip moves the arms. A swing blends four key poses
    /// (Idle, WindUp, Apex, FollowThrough) with the curves in <see cref="AxeSwingTimeline"/>.
    ///
    /// Time: the swing is world time. It advances on scaled time, so eyes-shut fast-forward speeds it up,
    /// and it is an <see cref="ISkippable"/>, so a blink jumps an in-progress swing forward and still
    /// raises <see cref="StrikeApex"/> if the jump passes it. The pose depends only on how far into the
    /// swing it is, so a jump lands on the right pose. The idle sway is the player's body, so it runs on
    /// unscaled time like the camera.
    ///
    /// Lives on the view model root (<c>Prefabs/Weapons/ViewModel_Axe</c>), next to the Animator and RigBuilder.
    /// Parent that root to the camera at local position (0, 0, 0) with no rotation: every pose is authored
    /// in camera space. It never moves its own root, so a parent may bob it (as CameraFeel does the gun).
    /// It poses the pivot in Update, before the animation pass, so the IK reads this frame's targets. The
    /// pivot and grip carry a RigTransform so Animation Rigging syncs them into the animation stream.
    /// </summary>
    public sealed class AxeViewModel : MonoBehaviour, ISkippable
    {
        [Header("Rig")]
        [Tooltip("Rotates the whole axe and both hand targets. Sits at the right shoulder so the hands arc around it.")]
        [SerializeField] Transform swingPivot;
        [Tooltip("Child of the swing pivot at the right hand's grip on the haft. Holds the axe mesh and the hand IK targets.")]
        [SerializeField] Transform grip;

        [Header("Key poses (pivot rotation, grip local position and rotation)")]
        [SerializeField] AxePose idlePose = new AxePose(Vector3.zero, new Vector3(0f, 0f, 0.5f), Vector3.zero);
        [Tooltip("Axe pulled back over the right shoulder, head up and out of view at the top right.")]
        [SerializeField] AxePose windUpPose = new AxePose(Vector3.zero, new Vector3(0f, 0f, 0.5f), Vector3.zero);
        [Tooltip("Mid-chop: the blade crosses the middle of the screen. StrikeApex fires here.")]
        [SerializeField] AxePose apexPose = new AxePose(Vector3.zero, new Vector3(0f, 0f, 0.5f), Vector3.zero);
        [Tooltip("End of the chop, head low on the left.")]
        [SerializeField] AxePose followThroughPose = new AxePose(Vector3.zero, new Vector3(0f, 0f, 0.5f), Vector3.zero);

        [Header("Timing (world seconds)")]
        [SerializeField] AxeSwingTimeline timeline = new AxeSwingTimeline();

        [Header("Idle sway (unscaled time)")]
        [Tooltip("Peak grip offset in metres (x right, y up, z forward).")]
        [SerializeField] Vector3 swayPosition = new Vector3(0.004f, 0.006f, 0.002f);
        [Tooltip("Peak grip rotation in degrees.")]
        [SerializeField] Vector3 swayRotation = new Vector3(1.2f, 0.8f, 1.5f);
        [Tooltip("Breaths per second.")]
        [SerializeField, Min(0f)] float swayFrequency = 0.35f;
        [Tooltip("Seconds to fade the sway out when a swing starts, and back in after it.")]
        [SerializeField, Min(0.01f)] float swayFadeSeconds = 0.2f;

        [Header("Time skips")]
        [Tooltip("Register with the TimeSkipController so blinks jump a swing forward. Untick if a gameplay " +
                 "component forwards skips to Skip itself, so they aren't applied twice.")]
        [SerializeField] bool registerForTimeSkips = true;

        readonly AxeSwingState _swing = new AxeSwingState();
        float _swayTime;
        float _swayWeight = 1f;

        /// <summary>A swing started (<see cref="PlaySwing"/> returned true).</summary>
        public event Action SwingStarted;

        /// <summary>
        /// The blade is at the apex of the strike: do the melee hit check now. Raised once per swing, also
        /// when a skip or a long frame jumps past it (the pose is already past the apex by then).
        /// </summary>
        public event Action StrikeApex;

        /// <summary>The swing ended and the axe is back at rest. Not raised by <see cref="CancelSwing"/>.</summary>
        public event Action SwingFinished;

        public bool IsSwinging => _swing.IsSwinging;

        /// <summary>Normalized progress through the current swing (0 when idle).</summary>
        public float SwingProgress => _swing.Progress01(timeline);

        public AxeSwingPhase Phase => _swing.IsSwinging ? timeline.PhaseAt(_swing.Elapsed) : AxeSwingPhase.Idle;

        /// <summary>True once the current swing has passed its apex (and raised <see cref="StrikeApex"/>).</summary>
        public bool ApexReached => _swing.IsSwinging && _swing.ApexReached;

        /// <summary>Wind-up, strike and recovery in world seconds.</summary>
        public float SwingDuration => timeline.TotalSeconds;

        /// <summary>World seconds from <see cref="PlaySwing"/> to <see cref="StrikeApex"/>.</summary>
        public float SecondsToApex => timeline.ApexSeconds;

        public AxeSwingTimeline Timeline => timeline;

        /// <summary>The right hand's grip on the haft. The melee item in hand (the axe mesh, or a plank) hangs off it.</summary>
        public Transform Grip => grip;

        void OnEnable()
        {
            if (registerForTimeSkips) TimeSkipController.Register(this);
            ApplyPose();
        }

        void OnDisable()
        {
            if (registerForTimeSkips) TimeSkipController.Unregister(this);
            CancelSwing(); // put away mid-swing: no apex, back to rest for next time
        }

        void Update()
        {
            Advance(Time.deltaTime);
            UpdateSway(Time.unscaledDeltaTime);
            ApplyPose();
        }

        /// <summary>Starts a swing. Returns false if one is already running.</summary>
        public bool PlaySwing()
        {
            if (!_swing.TryStart()) return false;
            SwingStarted?.Invoke();
            ApplyPose();
            return true;
        }

        /// <summary>Stops the swing and snaps back to the idle pose, without raising any events.</summary>
        public void CancelSwing()
        {
            _swing.Cancel();
            ApplyPose();
        }

        /// <summary>Jumps the swing forward by <paramref name="seconds"/> of world time (a blink).</summary>
        public void Skip(float seconds)
        {
            Advance(seconds);
            ApplyPose();
        }

        void Advance(float seconds)
        {
            var step = _swing.Advance(seconds, timeline);
            if (step.CrossedApex) StrikeApex?.Invoke();
            if (step.Finished) SwingFinished?.Invoke();
        }

        void UpdateSway(float dt)
        {
            _swayTime += dt;
            _swayWeight = Mathf.MoveTowards(_swayWeight, _swing.IsSwinging ? 0f : 1f, dt / swayFadeSeconds);
        }

        void ApplyPose()
        {
            if (swingPivot == null || grip == null) return;

            var sample = _swing.IsSwinging ? timeline.Sample(_swing.Elapsed) : AxeSwingSample.Rest;
            AxePose.Blend(KeyPose(sample.From), KeyPose(sample.To), sample.Weight,
                out var pivotRotation, out var gripPosition, out var gripRotation);

            if (_swayWeight > 0f)
            {
                // Two incommensurate sines per axis read as breathing rather than a loop.
                float w = Mathf.PI * 2f * swayFrequency * _swayTime;
                var wave = new Vector3(Mathf.Sin(w), Mathf.Sin(w * 0.5f + 1.3f), Mathf.Sin(w * 0.77f + 2.1f));
                gripPosition += Vector3.Scale(swayPosition, wave) * _swayWeight;
                gripRotation *= Quaternion.Euler(Vector3.Scale(swayRotation, new Vector3(wave.y, wave.z, wave.x)) * _swayWeight);
            }

            swingPivot.localRotation = pivotRotation;
            grip.localPosition = gripPosition;
            grip.localRotation = gripRotation;
        }

        AxePose KeyPose(AxeKeyPose key) => key switch
        {
            AxeKeyPose.WindUp => windUpPose,
            AxeKeyPose.Apex => apexPose,
            AxeKeyPose.FollowThrough => followThroughPose,
            _ => idlePose,
        };
    }
}
