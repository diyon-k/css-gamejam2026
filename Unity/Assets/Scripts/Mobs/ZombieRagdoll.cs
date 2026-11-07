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
    /// out of the pose it landed in.
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
        [SerializeField, Min(0f)] float hitImpulse = 10f;
        [Tooltip("Speed (m/s) the killing shot adds to the whole body, flat along the shot, so it falls away from " +
                 "the shooter. The hit impulse alone mostly just flicks the limb it hits.")]
        [SerializeField, Min(0f)] float knockbackSpeed = 1.5f;
        [Tooltip("Damage multiplier for shots to the head bone. The head hitbox is also flagged IsHead, so a " +
                 "headshot kills outright whatever this is.")]
        [SerializeField, Min(0f)] float headDamageMultiplier = 2f;

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
        public IReadOnlyList<Rigidbody> Bodies => _bodies;

        /// <summary>Where the body really is (the hips bone), which leaves the root behind while limp.</summary>
        public Vector3 HipsPosition => _hips != null ? _hips.position : transform.position;

        /// <summary>Speed of the fastest limb in m/s (0 while animated or frozen).</summary>
        public float FastestSpeed
        {
            get
            {
                if (!IsActive || IsFrozen) return 0f;
                float fastest = 0f;
                foreach (var body in _bodies)
                    if (body != null) fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
                return fastest;
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

        // Clears the counter when entering play mode with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ActiveCount = 0;

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
            if (!IsActive || IsFrozen || IsKnockedDown) return; // a knocked-down body gets up instead of freezing
            if (_settle.Advance(Time.deltaTime, FastestSpeed)) Freeze();
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
        /// Dying while knocked down just means it never gets up.
        /// </summary>
        public void Activate(Vector3 inheritedVelocity)
        {
            if (IsKnockedDown)
            {
                IsKnockedDown = false;
                _settle = new RagdollSettleClock(settleSeconds, restSpeed, restSeconds);
                return;
            }
            if (IsActive || _bodies.Length == 0) return;
            GoLimp(inheritedVelocity);
            ApplyHitImpulse();
        }

        /// <summary>
        /// Knocks the body flying: goes limp if it wasn't (a living zombie stays down until <see cref="GetUp"/>;
        /// a frozen corpse wakes up), adds <paramref name="velocity"/> to every limb and spins the limb
        /// nearest <paramref name="point"/> so it tumbles.
        /// </summary>
        public void Launch(Vector3 velocity, Vector3 point)
        {
            if (_bodies.Length == 0) return;
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

            foreach (var body in _bodies)
            {
                if (body == null) continue;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // fast limbs don't tunnel through walls
                body.AddForce(velocity, ForceMode.VelocityChange);
                body.WakeUp();
            }

            if (knockdownSpin <= 0f || velocity.sqrMagnitude < 1e-6f) return;
            _bodyPositions.Clear();
            foreach (var body in _bodies) _bodyPositions.Add(body.worldCenterOfMass);
            int nearest = RagdollMath.NearestIndex(_bodyPositions, point);
            if (nearest >= 0) _bodies[nearest].AddForceAtPosition(velocity.normalized * knockdownSpin, point, ForceMode.Impulse);
        }

        /// <summary>
        /// Ends a knockdown: the bones go back to kinematic and animated, blending out of the pose it lay in.
        /// The lying pose is captured here, so call it before moving the root under the hips.
        /// </summary>
        public void GetUp()
        {
            if (!IsKnockedDown) return;
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
            foreach (var body in _bodies)
                if (body != null) body.isKinematic = true;
        }

        void OnStruck(Hitbox hitbox, Vector3 point, Vector3 direction) =>
            RecordHit(hitbox.GetComponent<Rigidbody>(), point, direction);
    }
}
