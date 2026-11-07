using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Interaction
{
    /// <summary>
    /// A loose physics prop the player can pick up (Interact), carry in front of the camera, drop (Interact again)
    /// or throw (Attack). Held by velocity, not parenting, so it still bumps into walls; it drops if it snags
    /// too far behind. Weapons don't fire while something is carried (<see cref="Held"/>).
    /// With a <see cref="SwingableProp"/> it's held by the handle and Attack swings it instead of throwing.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(100)] // after PlayerWeapons, so the throw click doesn't also fire the gun
    public sealed class Carryable : MonoBehaviour, IInteractable
    {
        /// <summary>The prop in the player's hands, or null.</summary>
        public static Carryable Held { get; private set; }

        [SerializeField] string displayName = "box";
        [SerializeField] float holdDistance = 1.3f;
        [Tooltip("How hard it's pulled to the hold point. Higher is stiffer.")]
        [SerializeField] float followStrength = 15f;
        [Tooltip("Drops if it gets this far from the hold point (stuck behind something).")]
        [SerializeField] float breakDistance = 1.5f;
        [SerializeField] float throwSpeed = 9f;

        Rigidbody _body;
        Collider[] _colliders;
        Collider _holder;
        Transform _eye;
        InputAction _throw;
        SwingableProp _swing; // optional: held like a club and swung on Attack instead of thrown

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Held = null;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
            _throw = InputSystem.actions?.FindAction("Player/Attack");
            _swing = GetComponent<SwingableProp>();
        }

        public string Prompt(Interactor who) => Held == this ? "Drop" : "Pick up " + displayName;
        public bool CanInteract(Interactor who) => Held == null || Held == this;

        public void Interact(Interactor who)
        {
            if (Held == this) Release(Vector3.zero);
            else if (Held == null) Grab(who);
        }

        void Grab(Interactor who)
        {
            Held = this;
            _eye = who.Eye;
            _holder = who.GetComponent<Collider>();
            SetIgnoreHolder(true); // don't shove the player around
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void Release(Vector3 velocity)
        {
            if (Held == this) Held = null;
            SetIgnoreHolder(false);
            _body.useGravity = true;
            _body.linearVelocity = velocity;
            _eye = null;
            if (_swing != null) _swing.Cancel();
        }

        void Update()
        {
            if (Held == this && _throw != null && _throw.WasPressedThisFrame())
            {
                if (_swing != null)
                {
                    _swing.TrySwing(_eye);
                    return;
                }
                Release(_eye.forward * throwSpeed);
            }
        }

        void FixedUpdate()
        {
            if (Held != this) return;
            if (_swing != null)
            {
                float grabEase = Mathf.SmoothStep(0.1f, 1f, (Time.unscaledTime - _grabbedAt) / grabSeconds);
                if (!_swing.Follow(_body, _eye, grabEase, breakDistance)) Release(Vector3.zero);
                return;
            }
            var target = _eye.position + _eye.forward * holdDistance;
            var toTarget = target - _body.position;
            if (toTarget.magnitude > breakDistance) { Release(Vector3.zero); return; }
            _body.linearVelocity = toTarget * followStrength;
            _body.angularVelocity *= 0.8f;
        }

        void OnDisable()
        {
            if (Held == this) Release(Vector3.zero);
        }

        void SetIgnoreHolder(bool ignore)
        {
            if (_holder == null) return;
            foreach (var c in _colliders) Physics.IgnoreCollision(_holder, c, ignore);
        }
    }
}
