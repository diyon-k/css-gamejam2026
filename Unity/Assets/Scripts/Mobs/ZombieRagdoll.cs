using System.Collections.Generic;
using Blink.Combat;
using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// Physics death for a zombie whose bones already carry Rigidbodies, colliders and CharacterJoints
    /// (built with Unity's Ragdoll wizard). While alive the bodies are kinematic and just follow the
    /// animation; each bone collider gets a <see cref="Hitbox"/> (the head does extra damage), so shots
    /// land on the limb they hit. <see cref="MobController"/> calls <see cref="Activate"/> on death: the
    /// Animator stops, the bodies go limp with the zombie's running speed, and the killing shot knocks
    /// the body back and flicks the part it hit. Once the body has come to rest (or after
    /// <see cref="settleSeconds"/> of world time) it's frozen in place to save physics work.
    ///
    /// Knockdown: a blunt melee prop (<see cref="Interaction.SwingableProp"/>) calls <see cref="Launch"/> on a living
    /// zombie: it goes limp and flies, and <see cref="GetUp"/> later hands the bones back to the Animator, blending
    /// out of the pose it landed in. A knocked-down body is never frozen: it gets up instead.
    ///
    /// Axe kills: the weapon hub's <see cref="PlayerWeapons.MeleeHit"/> (raised right after the damage, in the
    /// same call, so after <see cref="Activate"/>) reports a killing axe blow, and the body is launched on top of
    /// the normal death push (<see cref="LaunchFromBlow"/>): <see cref="axeLaunchSpeed"/> along the swing,
    /// <see cref="axeLaunchLift"/> up and a spin from <see cref="axeHitImpulse"/>.
    ///
    /// Flight: every launch (<see cref="Launch"/>, <see cref="LaunchFromBlow"/>, <see cref="Fling"/>) goes through
    /// one mechanism. The velocity goes on every simulated limb, the limbs use <see cref="launchCollision"/> (so they
    /// don't pass through walls) until the body slows below <see cref="launchEndSpeed"/>, and it's never frozen
    /// mid-flight (<see cref="IsFlying"/>). A dead body freezes once it has landed and settled.
    ///
    /// Grab shove: the grab QTE pushes a living zombie over backwards with <see cref="Shove"/> (no spin, the chest
    /// harder than the feet); it flies like any launch and gets up after the stun.
    ///
    /// Layers: bone colliders sit on a layer that collides with nothing while alive (the gun still
    /// raycasts them) and move to <see cref="corpseLayer"/> while limp, which collides with the level and
    /// other corpses only, so the player walks through bodies and bullets pass over them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZombieRagdoll : MonoBehaviour
    {
        [Tooltip("Defaults to the Animator under this object. Disabled when the ragdoll takes over.")]
        [SerializeField] Animator animator;

        [Header("Hits")]
        [Tooltip("Impulse (N·s) the killing shot gives the body part it hit. The whole body weighs ~60 kg.")]
        [SerializeField, Min(0f)] float hitImpulse = 2.5f;
        [Tooltip("Speed (m/s) the killing shot adds to the whole body, flat along the shot, so it falls away from " +
                 "the shooter. The hit impulse alone mostly just flicks the limb it hits.")]
        [SerializeField, Min(0f)] float knockbackSpeed = 0.1875f;
        [Tooltip("Damage multiplier for shots to the head bone. The head hitbox is also flagged IsHead, so a " +
                 "headshot kills outright whatever this is.")]
        [SerializeField, Min(0f)] float headDamageMultiplier = 2f;

        [Header("Axe kill launch (added on top of the death push)")]
        [Tooltip("Speed (m/s) a killing axe blow adds to the whole body, flat along the swing. Sends it several metres.")]
        [SerializeField, Min(0f)] float axeLaunchSpeed = 3f;
        [Tooltip("Upward speed (m/s) a killing axe blow adds to the whole body, so it flies instead of sliding.")]
        [SerializeField, Min(0f)] float axeLaunchLift = 1.25f;
        [Tooltip("Impulse (N·s) a killing axe blow gives the part it hit, at the hit point, for spin. Limbs weigh 4-9 kg.")]
        [SerializeField, Min(0f)] float axeHitImpulse = 15f;

        [Header("Settling")]
        [Tooltip("World seconds after death at which the body is frozen, even if it's still sliding.")]
        [SerializeField, Min(0.1f)] float settleSeconds = 3f;
        [Tooltip("Freeze earlier once every body is slower than this (m/s)...")]
        [SerializeField, Min(0f)] float restSpeed = 0.08f;
        [Tooltip("...for this many world seconds.")]
        [SerializeField, Min(0f)] float restSeconds = 0.5f;

        [Header("Physics")]
        [Tooltip("Layer the bone colliders move to on death. Should collide with the level and Corpse only.")]
        [SerializeField] string corpseLayer = "Corpse";
        [SerializeField, Range(1, 30)] int solverIterations = 12;
        [Tooltip("Caps how fast overlapping bodies are pushed apart, so a limb starting inside the floor doesn't launch the body.")]
        [SerializeField, Min(0.1f)] float maxDepenetrationVelocity = 2f;
        [Tooltip("Snap joints back when they drift too far apart (stops stretched limbs).")]
        [SerializeField] bool jointProjection = true;

        [Header("Flight (every launch: axe kills, axe hits on corpses, knockdowns)")]
        [Tooltip("Collision detection while flying, so a flying body can't pass through a wall. Back to Discrete " +
                 "once it slows down, and always before it's frozen or gets up. Speculative also catches limbs whipping " +
                 "round (ContinuousDynamic only sweeps straight-line motion, and let spinning forearms sink into the floor).")]
        [SerializeField] CollisionDetectionMode launchCollision = CollisionDetectionMode.ContinuousSpeculative;
        [Tooltip("A flight ends (continuous collision off, freezing allowed if dead) once every limb is slower than " +
                 "this, m/s, or after settleSeconds at most.")]
        [SerializeField, Min(0f)] float launchEndSpeed = 2f;

        [Header("Knockdown (melee props)")]
        [Tooltip("Spin impulse (N·s) at the struck point on a knockdown, so the body tumbles instead of sliding.")]
        [SerializeField, Min(0f)] float knockdownSpin = 40f;
        [Tooltip("Seconds to blend from the pose it lay in back to the animated one when it gets up.")]
        [SerializeField, Min(0.05f)] float getUpBlendSeconds = 0.9f;

        /// <summary>Ragdolls that are limp and still being simulated (not yet frozen).</summary>
        public static int ActiveCount { get; private set; }

        /// <summary>Limp (dead or knocked down).</summary>
        public bool IsActive { get; private set; }
        public bool IsFrozen { get; private set; }
        /// <summary>Limp from a knockdown, not death: <see cref="GetUp"/> hands it back to the Animator.</summary>
        public bool IsKnockedDown { get; private set; }

        /// <summary>
        /// True while the body flies from a launch: its limbs use continuous collision and it isn't frozen until it
        /// has slowed down.
        /// </summary>
        public bool IsFlying => _flight != null && _flight.IsFlying;

        public IReadOnlyList<Rigidbody> Bodies => _bodies;

        /// <summary>Where the body really is (the hips bone), which leaves the root behind while limp.</summary>
        public Vector3 HipsPosition => _hips != null ? _hips.position : transform.position;

        /// <summary>Speed of the fastest limb in m/s (0 while animated or frozen).</summary>
        public float FastestSpeed
        {
            get
            {
                if (!IsActive || IsFrozen) return 0f;
                return SimulatedSpeed();
            }
        }

        Rigidbody[] _bodies = new Rigidbody[0];
        readonly List<Collider> _colliders = new();
        readonly List<int> _aliveLayers = new();
        readonly List<Vector3> _bodyPositions = new();
        RagdollSettleClock _settle;

        // The last shot: recorded by a Hitbox just before the damage that may kill.
        Rigidbody _hitBody;
        Vector3 _hitPoint, _hitDirection;
        int _hitFrame = -1;

        // Get-up: the pose it lay in, blended out of over getUpBlendSeconds.
        Transform _hips;
        Transform[] _skeleton = new Transform[0];
        Quaternion[] _lyingRotations = new Quaternion[0];
        Vector3 _lyingHipsPosition;
        Quaternion _lyingHipsRotation;
        float _blendElapsed = -1f; // < 0: not blending

        RagdollFlight _flight;
        int _activatedFrame = -1; // the frame it died, so only the blow it died to launches it
        // The weapon hub whose MeleeHit is listened to (one subscription shared by every zombie).
        static PlayerWeapons s_weapons;

        // Clears the statics when entering play mode with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ActiveCount = 0;
            s_weapons = null;
        }

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _bodies = GetComponentsInChildren<Rigidbody>(true);

            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            foreach (var body in _bodies)
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                body.solverIterations = solverIterations;
                body.maxDepenetrationVelocity = maxDepenetrationVelocity;

                foreach (var col in body.GetComponents<Collider>())
                {
                    if (col.isTrigger) continue;
                    _colliders.Add(col);
                    _aliveLayers.Add(col.gameObject.layer);
                }

                var hitbox = body.GetComponent<Hitbox>();
                if (hitbox == null)
                {
                    hitbox = body.gameObject.AddComponent<Hitbox>();
                    hitbox.DamageMultiplier = body.transform == head ? headDamageMultiplier : 1f;
                }
                if (body.transform == head) hitbox.IsHead = true; // headshots kill outright
                hitbox.Struck += OnStruck;
            }

            _hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (_hips == null && _bodies.Length > 0) _hips = _bodies[0].transform;
            if (_hips != null)
            {
                _skeleton = _hips.GetComponentsInChildren<Transform>(true);
                _lyingRotations = new Quaternion[_skeleton.Length];
            }

            // Limbs of one body never collide with each other (joints alone only skip neighbours).
            for (int i = 0; i < _colliders.Count; i++)
                for (int j = i + 1; j < _colliders.Count; j++)
                    Physics.IgnoreCollision(_colliders[i], _colliders[j], true);
        }

        void OnDestroy()
        {
            if (IsActive && !IsFrozen) ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        void Update()
        {
            WatchWeapons();
            Tick(Time.deltaTime);
        }

        // World time in Update only (never a skip), so a blink can't end a flight or freeze a body mid-air.
        void Tick(float deltaTime)
        {
            // A frozen body still flying was woken by CorpseShotReaction, which re-freezes it; only its flight is ours.
            if (!IsActive || (IsFrozen && !IsFlying)) return;
            // A knocked-down body gets up instead of freezing; a flying one is never frozen.
            bool freeze = RagdollMath.StepLimp(_flight, IsFrozen ? null : _settle, IsKnockedDown, deltaTime,
                                               SimulatedSpeed(), out bool landed);
            if (landed) SetCollision(CollisionDetectionMode.Discrete);
            if (freeze) Freeze();
        }

        // Fastest simulated (non-kinematic) limb, m/s.
        float SimulatedSpeed()
        {
            float fastest = 0f;
            foreach (var body in _bodies)
                if (body != null && !body.isKinematic) fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            return fastest;
        }

        // After the Animator has posed the bones: ease them from the pose it lay in to the animated one.
        void LateUpdate()
        {
            if (_blendElapsed < 0f || IsActive) return;
            _blendElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, _blendElapsed / getUpBlendSeconds);
            if (t >= 1f)
            {
                _blendElapsed = -1f;
                return;
            }
            for (int i = 0; i < _skeleton.Length; i++)
            {
                var bone = _skeleton[i];
                if (bone == _hips)
                    bone.SetPositionAndRotation(Vector3.Lerp(_lyingHipsPosition, bone.position, t),
                                                Quaternion.Slerp(_lyingHipsRotation, bone.rotation, t));
                else
                    bone.localRotation = Quaternion.Slerp(_lyingRotations[i], bone.localRotation, t);
            }
        }

        /// <summary>
        /// Records a hit so the death push lands where the shot did. Hitboxes call this for you; call it
        /// yourself for damage that doesn't go through one (pass a null body to use the nearest bone).
        /// Only a hit recorded in the same frame as the death counts.
        /// </summary>
        public void RecordHit(Rigidbody body, Vector3 point, Vector3 direction)
        {
            _hitBody = body;
            _hitPoint = point;
            _hitDirection = direction;
            _hitFrame = Time.frameCount;
        }

        /// <summary>
        /// Goes limp: stops the Animator, hands the bones to physics with <paramref name="inheritedVelocity"/>
        /// (the zombie's speed when it died) and pushes the part the killing shot hit. Does nothing twice.
        /// Dying while knocked down just means it never gets up: a body still flying keeps flying and freezes once
        /// it has landed and settled.
        /// </summary>
        public void Activate(Vector3 inheritedVelocity)
        {
            if (IsKnockedDown)
            {
                IsKnockedDown = false;
                _activatedFrame = Time.frameCount;
                _settle = new RagdollSettleClock(settleSeconds, restSpeed, restSeconds);
                return;
            }
            if (IsActive || _bodies.Length == 0) return;
            _activatedFrame = Time.frameCount;
            GoLimp(inheritedVelocity);
            ApplyHitImpulse();
        }

        /// <summary>
        /// Knocks the body flying: goes limp if it wasn't (a living zombie stays down until <see cref="GetUp"/>;
        /// a frozen corpse wakes up), adds <paramref name="velocity"/> to every limb and spins the limb
        /// nearest <paramref name="point"/> so it tumbles. It flies like any launch (see <see cref="IsFlying"/>).
        /// </summary>
        public void Launch(Vector3 velocity, Vector3 point)
        {
            if (_bodies.Length == 0) return;
            LimpForKnockdown();

            Fly(velocity);

            if (knockdownSpin <= 0f || velocity.sqrMagnitude < 1e-6f) return;
            _bodyPositions.Clear();
            foreach (var body in _bodies) _bodyPositions.Add(body.worldCenterOfMass);
            int nearest = RagdollMath.NearestIndex(_bodyPositions, point);
            if (nearest >= 0) _bodies[nearest].AddForceAtPosition(velocity.normalized * knockdownSpin, point, ForceMode.Impulse);
        }

        /// <summary>
        /// Pushed over (the grab QTE's two-handed shove): goes limp like <see cref="Launch"/>, but with no spin, and
        /// each limb gets <paramref name="velocity"/> scaled by how high it is, <paramref name="feetShare"/> of it at
        /// the lowest limb up to all of it at the highest. The chest goes back faster than the feet, so it topples
        /// over backwards instead of tumbling through the air. It flies like any launch (see <see cref="IsFlying"/>).
        /// </summary>
        public void Shove(Vector3 velocity, float feetShare)
        {
            if (_bodies.Length == 0) return;
            LimpForKnockdown();
            foreach (var body in _bodies)
                if (body != null) body.angularVelocity = Vector3.zero;
            Fly(velocity, feetShare);
        }

        // Limp for a knockdown: a living body goes limp and will get up later; a frozen corpse wakes up.
        void LimpForKnockdown()
        {
            if (!IsActive)
            {
                GoLimp(Vector3.zero);
                IsKnockedDown = true;
            }
            else if (IsFrozen)
            {
                IsFrozen = false;
                ActiveCount++;
                _settle = new RagdollSettleClock(settleSeconds, restSpeed, restSeconds);
                foreach (var body in _bodies)
                    if (body != null) body.isKinematic = false;
            }
        }

        /// <summary>
        /// Sends a dead body flying from a killing blow: <see cref="axeLaunchSpeed"/> along
        /// <paramref name="direction"/> (flattened), <see cref="axeLaunchLift"/> up, and <see cref="axeHitImpulse"/>
        /// at <paramref name="point"/> on <paramref name="body"/> (the part hit; null = no spin). Called for you on a
        /// killing axe hit; call it after <see cref="Activate"/> for other heavy blows. Does nothing while alive.
        /// </summary>
        public void LaunchFromBlow(Rigidbody body, Vector3 point, Vector3 direction)
        {
            if (!IsActive || direction.sqrMagnitude < 1e-8f) return;
            Fling(RagdollMath.LaunchVelocity(direction, Vector3.up, axeLaunchSpeed, axeLaunchLift));
            if (axeHitImpulse > 0f && body != null && !body.isKinematic && IsOwnBody(body))
                body.AddForceAtPosition(direction.normalized * axeHitImpulse, point, ForceMode.Impulse);
        }

        /// <summary>
        /// Adds <paramref name="velocity"/> (m/s) to every simulated limb of a body that is already limp (dead or
        /// knocked down) and flies it (see <see cref="IsFlying"/>). Unlike <see cref="Launch"/> it never knocks a
        /// living zombie down, wakes a frozen body or adds spin. Kinematic (frozen) limbs are left alone: wake them
        /// first (<see cref="CorpseShotReaction"/> does, and re-freezes them itself).
        /// </summary>
        public void Fling(Vector3 velocity)
        {
            if (!IsActive || velocity.sqrMagnitude < 1e-6f) return;
            Fly(velocity);
        }

        /// <summary>
        /// Ends a flight now: back to discrete collision. Done for you once the body slows down, and before it's
        /// frozen or gets up. Call it before making the limbs kinematic yourself.
        /// </summary>
        public void EndFlight()
        {
            if (!IsFlying) return;
            _flight.End();
            SetCollision(CollisionDetectionMode.Discrete);
        }

        // The one flying-body mechanism behind every launch: the velocity goes straight onto every simulated limb
        // (not as a pending force, so this frame's Update already sees it flying) with continuous collision on.
        // feetShare below 1 scales each limb's share by its height (RagdollMath.HeightShare), for Shove.
        void Fly(Vector3 velocity, float feetShare = 1f)
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            if (feetShare < 1f)
                foreach (var body in _bodies)
                {
                    if (body == null || body.isKinematic) continue;
                    float y = body.worldCenterOfMass.y;
                    low = Mathf.Min(low, y);
                    high = Mathf.Max(high, y);
                }
            bool any = false;
            foreach (var body in _bodies)
            {
                if (body == null || body.isKinematic) continue;
                float share = feetShare < 1f ? RagdollMath.HeightShare(body.worldCenterOfMass.y, low, high, feetShare) : 1f;
                body.collisionDetectionMode = launchCollision;
                body.linearVelocity += velocity * share;
                body.WakeUp();
                any = true;
            }
            if (!any) return;
            _flight = new RagdollFlight(settleSeconds, launchEndSpeed);
            _flight.Begin();
        }

        void SetCollision(CollisionDetectionMode mode)
        {
            foreach (var body in _bodies)
                if (body != null) body.collisionDetectionMode = mode;
        }

        bool IsOwnBody(Rigidbody body)
        {
            foreach (var b in _bodies)
                if (b == body) return true;
            return false;
        }

        /// <summary>
        /// Ends a knockdown: the bones go back to kinematic and animated, blending out of the pose it lay in.
        /// The lying pose is captured here, so call it before moving the root under the hips.
        /// </summary>
        public void GetUp()
        {
            if (!IsKnockedDown) return;
            EndFlight(); // discrete collision again before the bones go kinematic and the Animator takes over
            IsKnockedDown = false;
            IsActive = false;
            if (!IsFrozen) ActiveCount = Mathf.Max(0, ActiveCount - 1);
            IsFrozen = false;

            if (_hips != null)
            {
                _lyingHipsPosition = _hips.position;
                _lyingHipsRotation = _hips.rotation;
                for (int i = 0; i < _skeleton.Length; i++) _lyingRotations[i] = _skeleton[i].localRotation;
                _blendElapsed = 0f;
            }

            foreach (var body in _bodies)
            {
                if (body == null) continue;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
            }
            for (int i = 0; i < _colliders.Count; i++) _colliders[i].gameObject.layer = _aliveLayers[i];
            if (animator != null) animator.enabled = true;
        }

        void GoLimp(Vector3 inheritedVelocity)
        {
            _blendElapsed = -1f;
            IsActive = true;
            ActiveCount++;
            _settle = new RagdollSettleClock(settleSeconds, restSpeed, restSeconds);

            if (animator != null) animator.enabled = false;

            int layer = LayerMask.NameToLayer(corpseLayer);
            foreach (var col in _colliders)
            {
                if (layer >= 0) col.gameObject.layer = layer;
                col.enabled = true;
            }

            foreach (var body in _bodies)
            {
                body.isKinematic = false;
                body.linearVelocity = inheritedVelocity;
                body.angularVelocity = Vector3.zero;
                var joint = body.GetComponent<CharacterJoint>();
                if (joint != null) joint.enableProjection = jointProjection;
                body.WakeUp();
            }
        }

        void ApplyHitImpulse()
        {
            if (_hitFrame != Time.frameCount || _hitDirection.sqrMagnitude < 1e-6f) return;

            // Flat, so a shot from eye height down at the legs doesn't drive the body into the floor.
            Vector3 flat = Vector3.ProjectOnPlane(_hitDirection, Vector3.up);
            if (knockbackSpeed > 0f && flat.sqrMagnitude > 1e-6f)
                foreach (var body in _bodies) body.AddForce(flat.normalized * knockbackSpeed, ForceMode.VelocityChange);

            if (hitImpulse <= 0f) return;
            Rigidbody target = _hitBody;
            if (target == null)
            {
                _bodyPositions.Clear();
                foreach (var body in _bodies) _bodyPositions.Add(body.worldCenterOfMass);
                int nearest = RagdollMath.NearestIndex(_bodyPositions, _hitPoint);
                if (nearest < 0) return;
                target = _bodies[nearest];
            }
            target.AddForceAtPosition(_hitDirection.normalized * hitImpulse, _hitPoint, ForceMode.Impulse);
        }

        /// <summary>Stops simulating the body where it lies. It stays solid for other corpses to land on.</summary>
        void Freeze()
        {
            if (IsFrozen) return;
            IsFrozen = true;
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            EndFlight(); // continuous collision off before going kinematic
            foreach (var body in _bodies)
                if (body != null) body.isKinematic = true;
        }

        void OnStruck(Hitbox hitbox, Vector3 point, Vector3 direction) =>
            RecordHit(hitbox.GetComponent<Rigidbody>(), point, direction);

        // One shared subscription to the player's weapon hub, picked up (again) whenever the hub changes.
        static void WatchWeapons()
        {
            var weapons = PlayerWeapons.Instance;
            if (ReferenceEquals(weapons, s_weapons)) return;
            if (s_weapons != null) s_weapons.MeleeHit -= OnMeleeHit;
            s_weapons = weapons;
            if (weapons != null) weapons.MeleeHit += OnMeleeHit;
        }

        // Raised after the damage, in the same call: a killing blow has already activated the ragdoll.
        static void OnMeleeHit(MeleeHitInfo hit)
        {
            if (!hit.Killed || hit.Weapon != WeaponKind.Axe || hit.Collider == null) return;
            var ragdoll = hit.Collider.GetComponentInParent<ZombieRagdoll>();
            if (ragdoll == null || ragdoll._activatedFrame != Time.frameCount) return; // only the blow it died to
            ragdoll.LaunchFromBlow(hit.Collider.attachedRigidbody, hit.Point, hit.Direction);
        }
    }
}
