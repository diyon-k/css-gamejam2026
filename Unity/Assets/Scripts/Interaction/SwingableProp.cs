using System.Collections.Generic;
using Blink.Audio;
using Blink.Combat;
using Blink.Mobs;
using Blink.Player;
using Blink.World;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// Makes a <see cref="Carryable"/> a blunt melee weapon (golf club, plank, candlestick). While carried it's held
    /// by one end in the right hand instead of floating in front of you, and Attack swings it instead of throwing it.
    /// The swing reuses the axe's timing and pose blending (<see cref="AxeSwingTimeline"/>, <see cref="AxePose"/>,
    /// poses relative to the camera); at the apex a sphere sweep from the eye picks the nearest zombie
    /// (<see cref="MeleeTargeting"/>). A hit barely hurts: it knocks the zombie flying as a ragdoll and stuns it
    /// (<see cref="MobController.KnockDown"/>). A corpse just gets launched again. Damage skips the hitboxes, so a
    /// blow to the head doesn't count as a headshot kill.
    ///
    /// The prop is still a physics body pulled to its pose by velocity, so it bumps into the level, but during a
    /// swing it's pulled almost rigidly so it keeps up.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public sealed class SwingableProp : MonoBehaviour
    {
        const int MaxHits = 32;

        [Header("Grip (prop's local space)")]
        [Tooltip("The point on the prop held in the hand, e.g. the end of the handle.")]
        [SerializeField] Vector3 grip = new(0f, -0.4f, 0f);
        [Tooltip("Local direction from the grip to the striking end.")]
        [SerializeField] Vector3 lengthAxis = Vector3.up;

        [Header("Poses, relative to the camera (the striking end points along the grip's +Z)")]
        [SerializeField] AxePose idlePose = new(Vector3.zero, new Vector3(0.3f, -0.38f, 0.5f), new Vector3(-55f, -12f, 0f));
        [SerializeField] AxePose windUpPose = new(new Vector3(0f, 35f, 0f), new Vector3(0.3f, -0.2f, 0.3f), new Vector3(-95f, 40f, 0f));
        [SerializeField] AxePose apexPose = new(Vector3.zero, new Vector3(0.05f, -0.28f, 0.55f), new Vector3(-5f, -25f, 0f));
        [SerializeField] AxePose followThroughPose = new(new Vector3(0f, -45f, 0f), new Vector3(-0.2f, -0.3f, 0.4f), new Vector3(15f, -95f, 0f));
        [SerializeField] AxeSwingTimeline timeline = new(0.14f, 0.16f, 0.5f, 0.35f);
        [Tooltip("How hard it's pulled to its pose while not swinging (1/s). Higher is stiffer.")]
        [SerializeField, Min(1f)] float holdStiffness = 18f;

        [Header("Hit")]
        [Tooltip("Barely hurts: these knock zombies away rather than kill them.")]
        [SerializeField, Min(0f)] float damage = 8f;
        [SerializeField, Min(0.1f)] float reach = 2.1f;
        [Tooltip("Radius of the sweep, metres. Forgiving aim.")]
        [SerializeField, Min(0.01f)] float radius = 0.45f;
        [Tooltip("Speed (m/s) a hit gives every limb, flat along your view.")]
        [SerializeField, Min(0f)] float launchSpeed = 10f;
        [Tooltip("Upward speed (m/s) on top, so it flies instead of sliding.")]
        [SerializeField, Min(0f)] float launchLift = 4.5f;
        [Tooltip("Sideways speed (m/s) along the swing (it sweeps right to left).")]
        [SerializeField] float launchSideways = 2.5f;
        [Tooltip("World seconds a zombie lies stunned before getting back up (longer if it's still tumbling).")]
        [SerializeField, Min(0f)] float stunSeconds = 2.5f;
        [Tooltip("Impulse (N·s) given to a loose physics prop the swing hits.")]
        [SerializeField, Min(0f)] float propImpulse = 6f;
        [Tooltip("Audible range of a hit, metres.")]
        [SerializeField, Min(0f)] float hitLoudness = 6f;
        [Tooltip("Camera jolt on a hit (1 = a full axe hit).")]
        [SerializeField, Min(0f)] float cameraImpact = 1.2f;

        [Header("Sound (optional)")]
        [SerializeField] SoundCue swingCue;
        [SerializeField] SoundCue fleshHitCue;
        [SerializeField] SoundCue surfaceHitCue;

        /// <summary>True from the start of a swing until it's back at rest.</summary>
        public bool IsSwinging => _swing.IsSwinging;

        readonly AxeSwingState _swing = new();
        readonly RaycastHit[] _hits = new RaycastHit[MaxHits];
        readonly List<MeleeCandidate> _candidates = new(MaxHits);
        readonly List<RaycastHit> _candidateHits = new(MaxHits);
        readonly RaycastHit[] _blockers = new RaycastHit[8];
        Quaternion _align;
        Transform _eye, _player;
        CameraFeel _feel;

        void Awake()
        {
            // Turns the prop so its striking end points along +Z; the poses aim +Z.
            _align = lengthAxis.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(lengthAxis.normalized, Vector3.forward) : Quaternion.identity;
        }

        /// <summary>Starts a swing (Carryable calls this on Attack). False if one is running.</summary>
        public bool TrySwing(Transform eye)
        {
            Bind(eye);
            if (!_swing.TryStart()) return false;
            if (swingCue != null && AudioService.Instance != null) AudioService.Instance.Play(swingCue);
            if (FindAnyObjectByType<HandsViewModel>() is { } hands) hands.Reach();
            return true;
        }

        /// <summary>Stops a swing early (dropped mid-swing).</summary>
        public void Cancel() => _swing.Cancel();

        /// <summary>
        /// Pulls <paramref name="body"/> to where the hand holds it this physics step. <paramref name="ease"/> (0..1)
        /// softens the pull just after a grab. Returns false if the prop is snagged too far away and should drop.
        /// </summary>
        public bool Follow(Rigidbody body, Transform eye, float ease, float breakDistance)
        {
            Bind(eye);
            var s = timeline.Sample(_swing.Elapsed);
            AxePose.Blend(PoseOf(s.From), PoseOf(s.To), s.Weight, out var pivot, out var hand, out var gripRotation);

            Quaternion rotation = eye.rotation * pivot * gripRotation * _align;
            Vector3 handWorld = eye.TransformPoint(pivot * hand);
            Vector3 target = handWorld - rotation * Vector3.Scale(grip, transform.lossyScale);
            Vector3 toTarget = target - body.position;
            // Never snaps while swinging or still rising into the hand from the floor.
            if (!IsSwinging && ease >= 1f && toTarget.magnitude > breakDistance) return false;

            // Swinging: close nearly all the gap each step, or the prop lags behind the fast strike.
            float gain = IsSwinging ? 0.9f / Time.fixedDeltaTime : holdStiffness * ease;
            body.maxAngularVelocity = 60f;
            body.linearVelocity = toTarget * gain;
            (rotation * Quaternion.Inverse(body.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            body.angularVelocity = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad * gain) : Vector3.zero;
            return true;
        }

        void Update()
        {
            if (!_swing.IsSwinging) return;
            var step = _swing.Advance(Time.deltaTime, timeline);
            if (step.CrossedApex) Strike();
        }

        AxePose PoseOf(AxeKeyPose key) => key switch
        {
            AxeKeyPose.WindUp => windUpPose,
            AxeKeyPose.Apex => apexPose,
            AxeKeyPose.FollowThrough => followThroughPose,
            _ => idlePose,
        };

        void Bind(Transform eye)
        {
            if (_eye == eye) return;
            _eye = eye;
            _player = eye.root;
            _feel = eye.GetComponentInParent<CameraFeel>();
            if (_feel == null) _feel = FindAnyObjectByType<CameraFeel>();
        }

        /// <summary>The hit check at the apex: nearest zombie the eye can see, else the nearest surface (a thud).</summary>
        void Strike()
        {
            if (_eye == null) return;
            Vector3 origin = _eye.position, dir = _eye.forward;
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, reach, ~0, QueryTriggerInteraction.Ignore);

            _candidates.Clear();
            _candidateHits.Clear();
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                var col = hit.collider;
                if (col == null || col.transform.IsChildOf(_player) || col.transform.IsChildOf(transform)) continue;
                if (hit.distance <= 0f && hit.point == Vector3.zero) hit.point = col.bounds.ClosestPoint(origin); // started inside it
                var mob = col.GetComponentInParent<MobController>();
                // Corpses count as targets too (they get launched again), so "alive" here means "a body to hit".
                _candidates.Add(new MeleeCandidate(hit.distance, mob != null, true, mob == null || CanSee(origin, hit.point, mob.transform)));
                _candidateHits.Add(hit);
            }

            int pick = MeleeTargeting.Choose(_candidates, out bool isSurface);
            if (pick < 0) return;
            var chosen = _candidateHits[pick];
            Noise.Make(chosen.point, hitLoudness);

            if (isSurface)
            {
                var loose = chosen.rigidbody;
                if (loose != null && !loose.isKinematic) loose.AddForceAtPosition(dir * propImpulse, chosen.point, ForceMode.Impulse);
                Play(surfaceHitCue, chosen.point);
                if (_feel != null) _feel.AddImpact(cameraImpact * 0.5f);
                return;
            }

            var target = chosen.collider.GetComponentInParent<MobController>();
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            Vector3 side = Vector3.ProjectOnPlane(-_eye.right, Vector3.up).normalized;
            target.KnockDown(flat * launchSpeed + side * launchSideways + Vector3.up * launchLift, chosen.point, stunSeconds);
            // Straight to Health, not the hitbox: a knock on the head is not a headshot kill.
            if (target.TryGetComponent(out Health health)) health.TakeDamage(damage, chosen.point, dir);
            Play(fleshHitCue, chosen.point);
            if (_feel != null) _feel.AddImpact(cameraImpact);
        }

        // Nothing solid that isn't part of the target (or the player) between the eye and the hit point.
        bool CanSee(Vector3 origin, Vector3 point, Transform target)
        {
            Vector3 to = point - origin;
            float distance = to.magnitude;
            if (distance < 0.05f) return true;
            int n = Physics.RaycastNonAlloc(origin, to / distance, _blockers, distance - 0.02f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = _blockers[i].collider.transform;
                if (!t.IsChildOf(target) && !t.IsChildOf(_player) && !t.IsChildOf(transform)) return false;
            }
            return true;
        }

        static void Play(SoundCue cue, Vector3 at)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.PlayAt(cue, at);
        }
    }
}
