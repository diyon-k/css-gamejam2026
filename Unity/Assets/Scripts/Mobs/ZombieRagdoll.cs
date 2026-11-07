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
    /// Layers: bone colliders sit on a layer that collides with nothing while alive (the gun still
    /// raycasts them) and move to <see cref="corpseLayer"/> on death, which collides with the level and
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
        [Tooltip("Damage multiplier for shots to the head bone.")]
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

        /// <summary>Ragdolls that are limp and still being simulated (not yet frozen).</summary>
        public static int ActiveCount { get; private set; }

        public bool IsActive { get; private set; }
        public bool IsFrozen { get; private set; }
        public IReadOnlyList<Rigidbody> Bodies => _bodies;

        Rigidbody[] _bodies = new Rigidbody[0];
        readonly List<Collider> _colliders = new();
        readonly List<Vector3> _bodyPositions = new();
        RagdollSettleClock _settle;

        // The last shot: recorded by a Hitbox just before the damage that may kill.
        Rigidbody _hitBody;
        Vector3 _hitPoint, _hitDirection;
        int _hitFrame = -1;

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
                }

                var hitbox = body.GetComponent<Hitbox>();
                if (hitbox == null)
                {
                    hitbox = body.gameObject.AddComponent<Hitbox>();
                    hitbox.DamageMultiplier = body.transform == head ? headDamageMultiplier : 1f;
                }
                hitbox.Struck += OnStruck;
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
            if (!IsActive || IsFrozen) return;
            float fastest = 0f;
            foreach (var body in _bodies)
                if (body != null) fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            if (_settle.Advance(Time.deltaTime, fastest)) Freeze();
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
        /// </summary>
        public void Activate(Vector3 inheritedVelocity)
        {
            if (IsActive || _bodies.Length == 0) return;
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

            ApplyHitImpulse();
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
