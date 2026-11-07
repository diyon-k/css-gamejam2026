using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.AI;

namespace Blink.Mobs
{
    /// <summary>
    /// Makes a living zombie react to every hit (on the Zombie root, next to <see cref="ZombieRagdoll"/>).
    ///
    /// Flinch: a procedural pose layered on top of whatever the Animator plays (run, attack, idle). Each frame,
    /// after the Animator, the humanoid bones are rotated by a few critically damped springs (<see cref="FlinchSpring"/>):
    /// - torso (spine, chest, neck) tips back along the shot and twists the hit side away; the head whips a beat later;
    /// - an arm hit, or a chest hit near a shoulder, swings that upper arm back and pulls the shoulder round;
    /// - a leg hit buckles the knee, dips the hips and sags the body over onto that leg.
    /// The pose is rebuilt from the animated pose every frame, so nothing drifts even when the Animator skips a frame.
    ///
    /// Stagger: the NavMeshAgent slows at once and eases back (<see cref="StaggerState"/>), the run animation hitches,
    /// and the body slides back a little along the shot. Runs after <see cref="MobController"/> each frame and only
    /// scales the speed it set, so the two never fight.
    ///
    /// Size: every hit in a frame is summed before it lands, and the reaction scales with the damage
    /// (<see cref="HitReactionMath.Strength"/>): a pistol shot is 1. Melee hits (the axe, reported by
    /// <see cref="PlayerWeapons.MeleeHit"/>) are multiplied by <see cref="meleeStrength"/> on top, so an axe blow
    /// (60 damage) lands at about 2.2, over twice a pistol shot. A little randomness keeps repeated hits from looking the same.
    ///
    /// Death: stops at once (no more bone writes), so the ragdoll starts from the pose on screen. Time: reactions
    /// run on world time, so fast-forward speeds them up, and a blink skip advances them (mostly finishes them).
    /// Hits are read from every <see cref="Hitbox.Struck"/> under this object paired with <see cref="Health.Damaged"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)] // Update after MobController (sets the agent's speed), LateUpdate after the Animator
    public sealed class ZombieHitReaction : MonoBehaviour, ISkippable
    {
        const int Left = 0, Right = 1;

        enum Region { Torso, Head, Arm, Leg }

        [Tooltip("Defaults to the Animator under this object. Must be a Humanoid for the flinch.")]
        [SerializeField] Animator animator;

        [Header("Strength")]
        [Tooltip("Damage that gives a reaction of strength 1 (a pistol shot).")]
        [SerializeField, Min(1f)] float referenceDamage = 34f;
        [Tooltip("How strength grows with damage: 1 = linear, lower = bigger hits add less.")]
        [SerializeField, Range(0.2f, 1.5f)] float strengthExponent = 0.7f;
        [Tooltip("Cap on one frame's strength (several hits landing together).")]
        [SerializeField, Min(0.1f)] float maxStrength = 2.2f;
        [Tooltip("Extra multiplier on the strength of a melee hit (the axe), on top of its damage.")]
        [SerializeField, Min(1f)] float meleeStrength = 1.5f;
        [Tooltip("Random +/- share on each hit's size, so repeated hits don't look identical.")]
        [SerializeField, Range(0f, 0.6f)] float variation = 0.2f;
        [Tooltip("Random +/- degrees the push direction is turned about the vertical.")]
        [SerializeField, Range(0f, 45f)] float directionJitter = 12f;

        [Header("Torso and head (degrees at strength 1)")]
        [SerializeField, Min(0f)] float bendDegrees = 28f;
        [Tooltip("Twist from a hit off the centre line (a shoulder or flank).")]
        [SerializeField, Min(0f)] float twistDegrees = 30f;
        [Tooltip("Sideways metres from the spine for a full twist.")]
        [SerializeField, Min(0.01f)] float twistRadius = 0.2f;
        [SerializeField, Min(0f)] float headDegrees = 16f;
        [Tooltip("Seconds from a hit to the torso's furthest point. It settles in about 5x this.")]
        [SerializeField, Min(0.01f)] float torsoPeakSeconds = 0.09f;
        [Tooltip("Head peak, a little later than the torso for a whip.")]
        [SerializeField, Min(0.01f)] float headPeakSeconds = 0.12f;
        [SerializeField, Min(0f)] float maxTorsoDegrees = 60f;
        [SerializeField, Min(0f)] float maxHeadDegrees = 45f;

        [Header("Arms (degrees at strength 1)")]
        [Tooltip("Upper arm swinging back along the shot.")]
        [SerializeField, Min(0f)] float armDegrees = 65f;
        [Tooltip("Shoulder (clavicle) pulled back.")]
        [SerializeField, Min(0f)] float shoulderDegrees = 25f;
        [Tooltip("Share of the arm's swing the forearm adds on top (it flops).")]
        [SerializeField, Range(0f, 1f)] float forearmFollow = 0.35f;
        [Tooltip("Torso bend for an arm hit, relative to a body hit.")]
        [SerializeField, Range(0f, 1f)] float armHitBend = 0.5f;
        [Tooltip("A chest hit this close to a shoulder (metres) moves that arm fully; none beyond the second value.")]
        [SerializeField] Vector2 shoulderZone = new(0.08f, 0.24f);
        [SerializeField, Min(0.01f)] float armPeakSeconds = 0.08f;
        [SerializeField, Min(0f)] float maxArmDegrees = 95f;

        [Header("Legs (at strength 1)")]
        [Tooltip("Knee bend of the hit leg, degrees.")]
        [SerializeField, Min(0f)] float buckleDegrees = 45f;
        [Tooltip("Share of the knee bend the thigh swings forward (so the knee drops instead of the foot kicking up).")]
        [SerializeField, Range(0f, 1f)] float thighShare = 0.45f;
        [Tooltip("Metres the hips dip at a full buckle.")]
        [SerializeField, Min(0f)] float hipDip = 0.12f;
        [Tooltip("Degrees the body sags sideways onto the hit leg.")]
        [SerializeField, Min(0f)] float legSagDegrees = 16f;
        [Tooltip("Torso bend for a leg hit, relative to a body hit. Negative = folds forward over the leg.")]
        [SerializeField, Range(-1f, 1f)] float legHitBend = -0.45f;
        [SerializeField, Min(0.01f)] float legPeakSeconds = 0.11f;
        [SerializeField, Min(0f)] float maxBuckleDegrees = 75f;

        [Header("Stagger (world seconds)")]
        [Tooltip("Share of its speed a strength-1 hit takes away at once.")]
        [SerializeField, Range(0f, 1f)] float staggerDepth = 0.7f;
        [SerializeField, Range(0f, 1f)] float maxStaggerDepth = 0.85f;
        [Tooltip("Seconds to get back to full speed after a strength-1 hit.")]
        [SerializeField, Min(0f)] float staggerSeconds = 0.6f;
        [Tooltip("Extra stagger depth and length when the hit is on a leg (0.5 = +50%).")]
        [SerializeField, Min(0f)] float legStaggerBonus = 0.5f;
        [Tooltip("How much the animation slows with the stagger (0 = not at all, 1 = as much as the movement).")]
        [SerializeField, Range(0f, 1f)] float animationSlowdown = 0.5f;
        [Tooltip("Knock-back slide speed (m/s) at strength 1, along the shot.")]
        [SerializeField, Min(0f)] float knockbackSpeed = 2.4f;
        [Tooltip("Seconds for the slide to fade (it covers speed x this in total).")]
        [SerializeField, Min(0.01f)] float knockbackFade = 0.12f;

        // Springs. Rotation vectors in degrees, in this object's local space so they turn with the zombie.
        readonly FlinchSpring _torso = new(0.06f);
        readonly FlinchSpring _head = new(0.085f);
        readonly FlinchSpring[] _arm = { new(0.055f), new(0.055f) };
        readonly FlinchSpring[] _shoulder = { new(0.055f), new(0.055f) };
        readonly FlinchSpring[] _buckle = { new(0.08f), new(0.08f) }; // x = knee degrees
        readonly StaggerState _stagger = new();
        Vector3 _slideVelocity;

        // This frame's hits, summed until they land.
        float _hitDamage, _legDamage;
        Vector3 _torsoSum, _headSum, _slideSum;
        readonly Vector3[] _armSum = new Vector3[2], _shoulderSum = new Vector3[2];
        readonly float[] _buckleSum = new float[2];

        bool _meleeHit;

        // The weapon hub whose MeleeHit is listened to (one subscription shared by every zombie).
        static PlayerWeapons s_weapons;

        // The strike being damaged right now (Hitbox.Struck is raised just before Health.Damaged).
        Hitbox _strike;
        Vector3 _strikePoint, _strikeDirection;

        Health _health;
        NavMeshAgent _agent;
        ZombieRagdoll _ragdoll;
        Hitbox[] _hitboxes = new Hitbox[0];
        bool _subscribedHitboxes, _dead;

        // Bones (index 0 = left, 1 = right).
        bool _hasRig;
        Transform _hips, _neck, _headBone;
        Transform[] _spineChain = new Transform[0];
        float[] _spineWeights = new float[0];
        readonly Transform[] _shoulderBone = new Transform[2], _upperArm = new Transform[2], _lowerArm = new Transform[2];
        readonly Transform[] _upperLeg = new Transform[2], _lowerLeg = new Transform[2];

        // Pose bookkeeping: what the Animator wrote and what we turned it into, to undo a stale offset.
        Transform[] _posed = new Transform[0];
        Quaternion[] _animatedRotation = new Quaternion[0], _appliedRotation = new Quaternion[0];
        Vector3 _animatedHips, _appliedHips;
        bool _hasPose;

        // Speeds we scaled: restored only if nobody changed them since.
        bool _speedScaled, _animSpeedScaled;
        float _speedBase, _speedSet, _animSpeedBase, _animSpeedSet;

        /// <summary>True while any part of a flinch, stagger or slide is still playing.</summary>
        public bool IsReacting => !_torso.IsAtRest || !_head.IsAtRest || _stagger.IsActive || _slideVelocity != Vector3.zero ||
                                  !_arm[0].IsAtRest || !_arm[1].IsAtRest || !_shoulder[0].IsAtRest || !_shoulder[1].IsAtRest ||
                                  !_buckle[0].IsAtRest || !_buckle[1].IsAtRest;

        /// <summary>Current movement speed multiplier from the stagger (1 = none).</summary>
        public float SpeedFactor => _stagger.SpeedFactor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_weapons = null;

        void Awake()
        {
            _health = GetComponent<Health>();
            _agent = GetComponent<NavMeshAgent>();
            _ragdoll = GetComponent<ZombieRagdoll>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            ApplySpringTimes();
            FindBones();
        }

        void OnValidate() => ApplySpringTimes();

        void OnEnable()
        {
            TimeSkipController.Register(this);
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.Died += OnDied;
            }
            SubscribeHitboxes();
        }

        // Hitboxes may be added by ZombieRagdoll.Awake, which can run after our OnEnable.
        void Start() => SubscribeHitboxes();

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }
            UnsubscribeHitboxes();
            RestoreSpeeds();
        }

        void Update()
        {
            if (IsDead)
            {
                RestoreSpeeds();
                return;
            }
            WatchWeapons();
            Land();
            float dt = Time.deltaTime;
            _stagger.Advance(dt);
            ApplyStagger();
            Slide(dt);
        }

        void LateUpdate()
        {
            if (IsDead || !_hasRig || !animator.isActiveAndEnabled)
            {
                _hasPose = false; // never touch the bones again: the ragdoll (or the Dead clip) owns them
                return;
            }
            Land();
            AdvanceSprings(Time.deltaTime);
            UndoStalePose();
            if (FlinchAtRest()) return;
            ApplyPose();
        }

        /// <summary>A blink skip: the flinch, stagger and slide move on by the skipped time (a 0.5 s skip ends most of them).</summary>
        public void Skip(float seconds)
        {
            if (IsDead || seconds <= 0f) return;
            Land();
            AdvanceSprings(seconds);
            _stagger.Advance(seconds);
            ApplyStagger();
            Slide(seconds);
        }

        /// <summary>
        /// Queues a hit as if <paramref name="hitbox"/> (null = the body) took <paramref name="damage"/> at
        /// <paramref name="point"/> from a shot travelling along <paramref name="direction"/>. Hitboxes do this for you;
        /// public for damage that doesn't go through one, and for editor tests.
        /// </summary>
        public void React(Hitbox hitbox, Vector3 point, Vector3 direction, float damage)
        {
            if (IsDead || damage <= 0f) return;
            Vector3 up = transform.up;
            Vector3 flat = Vector3.ProjectOnPlane(direction, up);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : -transform.forward;
            if (directionJitter > 0f) flat = Quaternion.AngleAxis(Random.Range(-directionJitter, directionJitter), up) * flat;
            float weight = damage * (1f + Random.Range(-variation, variation));

            var region = Classify(hitbox, out int side);
            Vector3 centre = _spineChain.Length > 0 ? _spineChain[0].position : transform.position + up;
            Vector3 bendAxis = HitReactionMath.BendAxis(flat, up);
            float twist = HitReactionMath.TwistAmount(point - centre, flat, up, twistRadius);
            float bend = region switch { Region.Arm => armHitBend, Region.Leg => legHitBend, _ => 1f };
            float twistShare = region == Region.Leg ? 0.3f : 1f;

            Vector3 torso = bendAxis * (bendDegrees * bend) + up * (twistDegrees * twist * twistShare);
            if (region == Region.Leg)
                torso += transform.forward * (side == Right ? -legSagDegrees : legSagDegrees); // tip over onto the hit leg
            _torsoSum += weight * torso;
            _headSum += weight * bendAxis * (headDegrees * (region == Region.Head ? 1.6f : bend));

            if (region == Region.Arm) AddArm(side, weight, flat, 1f);
            else if (region == Region.Torso) AddShoulderHit(point, weight, flat);
            if (region == Region.Leg)
            {
                _buckleSum[side] += weight * buckleDegrees;
                _legDamage += damage;
            }

            _slideSum += weight * flat;
            _hitDamage += damage;
        }

        void AddShoulderHit(Vector3 point, float weight, Vector3 flat)
        {
            int nearest = -1;
            float best = float.PositiveInfinity;
            for (int side = 0; side < 2; side++)
            {
                if (_upperArm[side] == null) continue;
                float d = Vector3.Distance(point, _upperArm[side].position);
                if (d < best)
                {
                    best = d;
                    nearest = side;
                }
            }
            if (nearest < 0) return;
            float share = HitReactionMath.ShoulderShare(best, shoulderZone.x, shoulderZone.y);
            if (share > 0f) AddArm(nearest, weight, flat, share);
        }

        void AddArm(int side, float weight, Vector3 flat, float share)
        {
            var upper = _upperArm[side];
            if (upper == null) return;
            Vector3 fallback = HitReactionMath.BendAxis(flat, transform.up);
            Vector3 limb = _lowerArm[side] != null ? _lowerArm[side].position - upper.position : -transform.up;
            _armSum[side] += weight * share * armDegrees * HitReactionMath.SwingAxis(limb, flat, fallback);
            if (_shoulderBone[side] != null)
            {
                Vector3 clavicle = upper.position - _shoulderBone[side].position;
                _shoulderSum[side] += weight * share * shoulderDegrees * HitReactionMath.SwingAxis(clavicle, flat, fallback);
            }
        }

        /// <summary>Turns this frame's summed hits into kicks, a stagger and a slide.</summary>
        void Land()
        {
            if (_hitDamage <= 0f) return;
            float strength = HitReactionMath.Strength(_hitDamage, referenceDamage, strengthExponent, maxStrength);
            if (_meleeHit) strength *= meleeStrength;
            float scale = strength / _hitDamage;

            _torso.Kick(transform.InverseTransformDirection(_torsoSum * scale));
            _head.Kick(transform.InverseTransformDirection(_headSum * scale));
            for (int side = 0; side < 2; side++)
            {
                _arm[side].Kick(transform.InverseTransformDirection(_armSum[side] * scale));
                _shoulder[side].Kick(transform.InverseTransformDirection(_shoulderSum[side] * scale));
                _buckle[side].Kick(new Vector3(_buckleSum[side] * scale, 0f, 0f));
            }

            float legShare = _legDamage / _hitDamage;
            float bonus = 1f + legStaggerBonus * legShare;
            _stagger.Kick(staggerDepth * strength * bonus, staggerSeconds * (0.7f + 0.3f * strength) * bonus,
                          maxDepth: maxStaggerDepth);
            Vector3 slide = Vector3.ProjectOnPlane(_slideSum, Vector3.up);
            if (slide.sqrMagnitude > 1e-8f) _slideVelocity += slide.normalized * (knockbackSpeed * strength);

            // The hit stops it short, rather than waiting for the agent to brake.
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.velocity *= _stagger.SpeedFactor;

            _hitDamage = _legDamage = 0f;
            _meleeHit = false;
            _torsoSum = _headSum = _slideSum = Vector3.zero;
            for (int side = 0; side < 2; side++)
            {
                _armSum[side] = _shoulderSum[side] = Vector3.zero;
                _buckleSum[side] = 0f;
            }
        }

        void AdvanceSprings(float seconds)
        {
            _torso.Advance(seconds);
            _head.Advance(seconds);
            for (int side = 0; side < 2; side++)
            {
                _arm[side].Advance(seconds);
                _shoulder[side].Advance(seconds);
                _buckle[side].Advance(seconds);
            }
        }

        bool FlinchAtRest() =>
            _torso.IsAtRest && _head.IsAtRest && _arm[0].IsAtRest && _arm[1].IsAtRest &&
            _shoulder[0].IsAtRest && _shoulder[1].IsAtRest && _buckle[0].IsAtRest && _buckle[1].IsAtRest;

        void ApplyPose()
        {
            for (int i = 0; i < _posed.Length; i++) _animatedRotation[i] = _posed[i].localRotation;
            if (_hips != null) _animatedHips = _hips.localPosition;

            // Parents before children: each rotation is added in world space on top of what the parent already moved.
            float buckle = 0f;
            for (int side = 0; side < 2; side++) buckle += Mathf.Clamp(_buckle[side].Offset.x, 0f, maxBuckleDegrees);
            if (_hips != null && buckleDegrees > 0f)
                _hips.position -= transform.up * (hipDip * Mathf.Min(1f, buckle / buckleDegrees));

            Vector3 torso = Clamped(_torso, maxTorsoDegrees);
            for (int i = 0; i < _spineChain.Length; i++) Rotate(_spineChain[i], torso, _spineWeights[i]);
            if (_headBone != null) Rotate(_headBone, Clamped(_head, maxHeadDegrees), 1f);

            Vector3 right = transform.right;
            for (int side = 0; side < 2; side++)
            {
                if (_shoulderBone[side] != null) Rotate(_shoulderBone[side], Clamped(_shoulder[side], maxArmDegrees), 1f);
                Vector3 arm = Clamped(_arm[side], maxArmDegrees);
                if (_upperArm[side] != null) Rotate(_upperArm[side], arm, 1f);
                if (_lowerArm[side] != null) Rotate(_lowerArm[side], arm, forearmFollow);

                float knee = Mathf.Clamp(_buckle[side].Offset.x, 0f, maxBuckleDegrees);
                if (knee <= 0f) continue;
                if (_upperLeg[side] != null)
                    _upperLeg[side].rotation = Quaternion.AngleAxis(-knee * thighShare, right) * _upperLeg[side].rotation;
                if (_lowerLeg[side] != null)
                    _lowerLeg[side].rotation = Quaternion.AngleAxis(knee, right) * _lowerLeg[side].rotation;
            }

            for (int i = 0; i < _posed.Length; i++) _appliedRotation[i] = _posed[i].localRotation;
            if (_hips != null) _appliedHips = _hips.localPosition;
            _hasPose = true;
        }

        /// <summary>
        /// If the Animator didn't write a bone since our last offset (culled off screen, or between its updates),
        /// put the animated pose back first, so offsets never pile up.
        /// </summary>
        void UndoStalePose()
        {
            if (!_hasPose) return;
            for (int i = 0; i < _posed.Length; i++)
                if (_posed[i].localRotation.Equals(_appliedRotation[i])) _posed[i].localRotation = _animatedRotation[i];
            if (_hips != null && _hips.localPosition.Equals(_appliedHips)) _hips.localPosition = _animatedHips;
            _hasPose = false;
        }

        Vector3 Clamped(FlinchSpring spring, float maxDegrees) =>
            transform.TransformDirection(Vector3.ClampMagnitude(spring.Offset, maxDegrees));

        static void Rotate(Transform bone, Vector3 rotation, float weight)
        {
            float angle = rotation.magnitude * weight;
            if (angle < 1e-4f) return;
            bone.rotation = Quaternion.AngleAxis(angle, rotation / rotation.magnitude) * bone.rotation;
        }

        void ApplyStagger()
        {
            float factor = _stagger.SpeedFactor;
            if (_agent != null && _agent.enabled)
            {
                float speed = _agent.speed;
                if (!_speedScaled || speed != _speedSet) _speedBase = speed; // MobController set a new speed
                if (factor < 1f)
                {
                    _agent.speed = _speedBase * factor;
                    _speedSet = _agent.speed;
                    _speedScaled = true;
                }
                else if (_speedScaled)
                {
                    if (speed == _speedSet) _agent.speed = _speedBase;
                    _speedScaled = false;
                }
            }

            if (animator != null && animationSlowdown > 0f)
            {
                float animFactor = Mathf.Lerp(1f, factor, animationSlowdown);
                float speed = animator.speed;
                if (!_animSpeedScaled || speed != _animSpeedSet) _animSpeedBase = speed;
                if (animFactor < 1f)
                {
                    animator.speed = _animSpeedBase * animFactor;
                    _animSpeedSet = animator.speed;
                    _animSpeedScaled = true;
                }
                else if (_animSpeedScaled)
                {
                    if (speed == _animSpeedSet) animator.speed = _animSpeedBase;
                    _animSpeedScaled = false;
                }
            }
        }

        void RestoreSpeeds()
        {
            if (_speedScaled && _agent != null && _agent.speed == _speedSet) _agent.speed = _speedBase;
            if (_animSpeedScaled && animator != null && animator.speed == _animSpeedSet) animator.speed = _animSpeedBase;
            _speedScaled = _animSpeedScaled = false;
        }

        void Slide(float seconds)
        {
            Vector3 moved = HitReactionMath.DecaySlide(ref _slideVelocity, seconds, knockbackFade);
            if (moved != Vector3.zero && _agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Move(moved);
        }

        /// <summary>
        /// Marks the hits queued this frame as a melee blow, so they land <see cref="meleeStrength"/> times harder.
        /// Called for you from <see cref="PlayerWeapons.MeleeHit"/>; call it after <see cref="React"/> for other melee.
        /// </summary>
        public void MarkMelee()
        {
            if (_hitDamage > 0f) _meleeHit = true;
        }

        // One shared subscription to the player's weapon hub, picked up (again) whenever the hub changes.
        static void WatchWeapons()
        {
            var weapons = PlayerWeapons.Instance;
            if (ReferenceEquals(weapons, s_weapons)) return;
            if (s_weapons != null) s_weapons.MeleeHit -= OnMeleeHit;
            s_weapons = weapons;
            if (weapons != null) weapons.MeleeHit += OnMeleeHit;
        }

        // Raised after the damage, in the same call, so the blow's hit is already queued.
        static void OnMeleeHit(MeleeHitInfo hit)
        {
            if (hit.Target == null || hit.Collider == null) return;
            var reaction = hit.Collider.GetComponentInParent<ZombieHitReaction>();
            if (reaction != null) reaction.MarkMelee();
        }

        bool IsDead => _dead || (_health != null && _health.IsDead) || (_ragdoll != null && _ragdoll.IsActive);

        void OnStruck(Hitbox hitbox, Vector3 point, Vector3 direction)
        {
            _strike = hitbox;
            _strikePoint = point;
            _strikeDirection = direction;
        }

        void OnDamaged(float amount, Vector3 point)
        {
            if (_strike != null)
            {
                React(_strike, _strikePoint, _strikeDirection, amount);
                _strike = null;
                return;
            }
            // Damage that didn't come through a hitbox: push it away from where it landed.
            Vector3 centre = _spineChain.Length > 0 ? _spineChain[0].position : transform.position + transform.up;
            React(null, point, centre - point, amount);
        }

        void OnDied()
        {
            _dead = true;
            _hasPose = false;
            _hitDamage = 0f;
            _torso.Reset();
            _head.Reset();
            for (int side = 0; side < 2; side++)
            {
                _arm[side].Reset();
                _shoulder[side].Reset();
                _buckle[side].Reset();
            }
            _stagger.Reset();
            _slideVelocity = Vector3.zero;
            RestoreSpeeds();
        }

        Region Classify(Hitbox hitbox, out int side)
        {
            side = Left;
            if (hitbox == null || !_hasRig) return Region.Torso;
            var t = hitbox.transform;
            for (int s = 0; s < 2; s++)
            {
                side = s;
                if (Under(t, _upperArm[s]) || Under(t, _shoulderBone[s])) return Region.Arm;
                if (Under(t, _upperLeg[s])) return Region.Leg;
            }
            side = Left;
            if (Under(t, _headBone) || (hitbox.IsHead)) return Region.Head;
            return Region.Torso;
        }

        static bool Under(Transform t, Transform bone) => bone != null && t.IsChildOf(bone);

        void FindBones()
        {
            _hasRig = animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid;
            if (!_hasRig) return;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            _headBone = animator.GetBoneTransform(HumanBodyBones.Head);

            // The torso offset is shared out up the spine so it bends as a curve.
            var chain = new System.Collections.Generic.List<Transform>();
            var weights = new System.Collections.Generic.List<float>();
            void Add(HumanBodyBones bone, float weight)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) return;
                chain.Add(t);
                weights.Add(weight);
            }
            Add(HumanBodyBones.Spine, 0.35f);
            Add(HumanBodyBones.Chest, 0.3f);
            Add(HumanBodyBones.UpperChest, 0.2f);
            Add(HumanBodyBones.Neck, 0.15f);
            float total = 0f;
            foreach (float w in weights) total += w;
            for (int i = 0; i < weights.Count; i++) weights[i] /= total;
            _spineChain = chain.ToArray();
            _spineWeights = weights.ToArray();

            _shoulderBone[Left] = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _shoulderBone[Right] = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            _upperArm[Left] = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _upperArm[Right] = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lowerArm[Left] = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lowerArm[Right] = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _upperLeg[Left] = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _upperLeg[Right] = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _lowerLeg[Left] = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _lowerLeg[Right] = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);

            var posed = new System.Collections.Generic.List<Transform>(chain);
            foreach (var t in new[] { _headBone, _shoulderBone[0], _shoulderBone[1], _upperArm[0], _upperArm[1], _lowerArm[0],
                                      _lowerArm[1], _upperLeg[0], _upperLeg[1], _lowerLeg[0], _lowerLeg[1] })
                if (t != null && !posed.Contains(t)) posed.Add(t);
            _posed = posed.ToArray();
            _animatedRotation = new Quaternion[_posed.Length];
            _appliedRotation = new Quaternion[_posed.Length];
        }

        void SubscribeHitboxes()
        {
            if (_subscribedHitboxes) return;
            _hitboxes = GetComponentsInChildren<Hitbox>(true);
            if (_hitboxes.Length == 0) return;
            foreach (var hitbox in _hitboxes) hitbox.Struck += OnStruck;
            _subscribedHitboxes = true;
        }

        void UnsubscribeHitboxes()
        {
            if (!_subscribedHitboxes) return;
            foreach (var hitbox in _hitboxes)
                if (hitbox != null) hitbox.Struck -= OnStruck;
            _subscribedHitboxes = false;
        }

        void ApplySpringTimes()
        {
            _torso.PeakSeconds = torsoPeakSeconds;
            _head.PeakSeconds = headPeakSeconds;
            for (int side = 0; side < 2; side++)
            {
                _arm[side].PeakSeconds = armPeakSeconds;
                _shoulder[side].PeakSeconds = armPeakSeconds;
                _buckle[side].PeakSeconds = legPeakSeconds;
            }
        }
    }
}
