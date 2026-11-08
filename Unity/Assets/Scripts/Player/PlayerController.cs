using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Player
{
    /// <summary>
    /// First-person walk/look on a CharacterController, driven by the project-wide input actions
    /// (Player/Move, Player/Look, Player/Jump, Player/Crouch, Player/Crawl). Crouch and Crawl are held: the
    /// capsule and camera drop, and the player stays down while something is overhead. No sprinting by design.
    ///
    /// The player stands outside world time: it is deliberately NOT an <see cref="ISkippable"/>, so a
    /// blink skip leaves it where it is while the zombies jump, and movement runs on real time, so
    /// eyes-shut fast-forward doesn't speed it up either.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] Transform cameraPivot;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 3.5f;
        [SerializeField] float acceleration = 20f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float jumpHeight = 1f;
        [Tooltip("Noise range (metres) of landing from a jump or fall.")]
        [SerializeField, Min(0f)] float landLoudness = 6f;
        [Tooltip("Landing slower than this (m/s down) is silent, so stepping off a stair doesn't thump. A 1 m jump lands at ~6.")]
        [SerializeField, Min(0f)] float landNoiseSpeed = 4f;

        [Header("Crouch / Crawl")]
        [SerializeField] float crouchSpeed = 2.2f;
        [SerializeField] float crouchHeight = 1.2f;
        [SerializeField] float crawlSpeed = 1f;
        [Tooltip("Can't go below 2x the capsule radius.")]
        [SerializeField] float crawlHeight = 0.7f;
        [Tooltip("Metres per second the capsule shrinks or grows when going down or getting up.")]
        [SerializeField] float crawlTransitionSpeed = 4f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.1f;
        [SerializeField] float gamepadSensitivity = 180f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] bool lockCursor = true;

        [Header("Health regen")]
        [Tooltip("Seconds without taking damage before regen starts.")]
        [SerializeField, Min(0f)] float regenDelay = 15f;
        [SerializeField, Min(0.1f)] float regenInterval = 5f;
        [SerializeField, Min(0f)] float regenAmount = 25f;

        public Vector3 Velocity => _velocity;
        public bool InputEnabled { get; set; } = true;
        public bool IsCrawling => _controller.height < (crouchHeight + crawlHeight) * 0.5f;
        public bool IsCrouching => !IsCrawling && _controller.height < _standHeight - 0.01f;
        /// <summary>Camera pitch in degrees (positive looks down).</summary>
        public float Pitch => _pitch;
        /// <summary>Gamepad look multiplier; aim assist lowers it over a target (Fortnite-style slowdown).</summary>
        public float GamepadLookScale { get; set; } = 1f;

        CharacterController _controller;
        Health _health;
        InputAction _move, _look, _jump, _crouch, _crawl;
        Vector3 _velocity;
        float _pitch;
        float _standHeight, _feetY, _standPivotY;
        float _nextRegenAt;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            if (cameraPivot == null && GetComponentInChildren<Camera>() is { } cam) cameraPivot = cam.transform;
            _standHeight = _controller.height;
            _feetY = _controller.center.y - _standHeight * 0.5f;
            if (cameraPivot != null) _standPivotY = cameraPivot.localPosition.y;

            var actions = InputSystem.actions;
            if (actions == null)
            {
                Debug.LogError("[Player] No project-wide input actions set (Project Settings > Input System Package).");
                return;
            }
            _move = actions.FindAction("Player/Move");
            _look = actions.FindAction("Player/Look");
            _jump = actions.FindAction("Player/Jump");
            _crouch = actions.FindAction("Player/Crouch");
            _crawl = actions.FindAction("Player/Crawl");
        }

        void OnEnable()
        {
            _move?.Enable();
            _look?.Enable();
            _jump?.Enable();
            _crouch?.Enable();
            _crawl?.Enable();
            if (_health != null) { _health.Died += OnDied; _health.Damaged += OnDamaged; }
        }

        void OnDisable()
        {
            if (_health != null) { _health.Died -= OnDied; _health.Damaged -= OnDamaged; }
        }

        void OnDamaged(float amount, Vector3 point) => _nextRegenAt = Time.time + regenDelay;

        void Regen()
        {
            if (_health == null || _health.Current >= _health.Max || Time.time < _nextRegenAt) return;
            _health.Heal(regenAmount);
            _nextRegenAt = Time.time + regenInterval;
        }

        void Start()
        {
            if (lockCursor) SetCursorLocked(true);
        }

        void Update()
        {
            HandleCursor();
            Regen();
            if (!InputEnabled) return;
            Look();
            Move();
        }

        /// <summary>
        /// Converts a scaled frame delta back to real seconds, so fast-forward (timeScale &gt; 1)
        /// doesn't speed the player up. Unlike Time.unscaledDeltaTime it keeps Unity's
        /// maximumDeltaTime clamp after a hitch and stays 0 when the game is paused (timeScale 0).
        /// </summary>
        public static float RealDeltaTime(float scaledDeltaTime, float timeScale) =>
            timeScale > 0f ? scaledDeltaTime / timeScale : 0f;

        void Look()
        {
            if (_look == null || cameraPivot == null) return;
            Vector2 delta = _look.ReadValue<Vector2>();
            // Mouse reports pixels per frame; sticks report a direction that needs time scaling.
            bool isGamepad = _look.activeControl?.device is Gamepad;
            delta *= isGamepad ? gamepadSensitivity * GamepadLookScale * Time.unscaledDeltaTime : mouseSensitivity;
            AddLook(delta.x, -delta.y);
        }

        /// <summary>Turns the view by <paramref name="yaw"/> and <paramref name="pitch"/> degrees (pitch positive = down).</summary>
        public void AddLook(float yaw, float pitch)
        {
            if (cameraPivot == null) return;
            transform.Rotate(0f, yaw, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch + pitch, -maxPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>
        /// Turns the view toward a world point, easing in at <paramref name="sharpness"/> (higher snaps faster).
        /// For when something else has the camera while input is off, e.g. a zombie grabbing you.
        /// </summary>
        public void TurnTowards(Vector3 worldPoint, float sharpness, float deltaTime)
        {
            if (cameraPivot == null) return;
            Vector3 to = worldPoint - cameraPivot.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 1e-6f) return;

            float t = 1f - Mathf.Exp(-sharpness * deltaTime);
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), flat, Vector3.up);
            transform.Rotate(0f, yaw * t, 0f, Space.Self);

            float pitch = -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg; // looking up is negative pitch
            _pitch = Mathf.Clamp(Mathf.Lerp(_pitch, pitch, t), -maxPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            // Real time, not world time: the player must not gain ground from time skipping.
            float dt = RealDeltaTime(Time.deltaTime, Time.timeScale);

            Stance(dt);

            float speed = IsCrawling ? crawlSpeed : IsCrouching ? crouchSpeed : walkSpeed;
            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), wish, acceleration * dt);

            float vertical = _controller.isGrounded ? -2f : _velocity.y + gravity * dt;
            if (_controller.isGrounded && !IsCrawling && !IsCrouching && _jump != null && _jump.WasPressedThisFrame())
                vertical = Mathf.Sqrt(-2f * gravity * jumpHeight);
            _velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            bool airborne = !_controller.isGrounded;
            var flags = _controller.Move(_velocity * dt);
            if (airborne && _controller.isGrounded && -_velocity.y >= landNoiseSpeed)
                World.Noise.Make(transform.position, landLoudness);
            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f) _velocity.y = 0f; // bonk the ceiling
        }

        /// <summary>
        /// Holding Crouch or Crawl shrinks the capsule (feet stay put) and drops the camera with it.
        /// Crawl wins if both are held. Won't rise into a ceiling: stays low until there's room.
        /// </summary>
        void Stance(float dt)
        {
            float height = _controller.height;
            // Pad B also puts a note down; that press isn't a crouch.
            bool crouchHeld = _crouch != null && _crouch.IsPressed() && !Interaction.Readable.ClaimsCrouch;
            float target = _crawl != null && _crawl.IsPressed() ? crawlHeight
                : crouchHeld ? crouchHeight
                : _standHeight;
            if (target > height && !Headroom(target - height)) target = height;

            SetHeight(Mathf.MoveTowards(height, target, crawlTransitionSpeed * dt));
        }

        void SetHeight(float height)
        {
            if (Mathf.Approximately(height, _controller.height)) return;
            _controller.height = height;
            _controller.center = new Vector3(_controller.center.x, _feetY + height * 0.5f, _controller.center.z);
            if (cameraPivot != null)
            {
                Vector3 p = cameraPivot.localPosition;
                cameraPivot.localPosition = new Vector3(p.x, _standPivotY - (_standHeight - height), p.z);
            }
        }

        bool Headroom(float distance)
        {
            float r = _controller.radius * 0.9f;
            // Starts inside our own capsule, which casts ignore, so only real ceilings count.
            Vector3 origin = transform.TransformPoint(new Vector3(0f, _feetY + _controller.height - _controller.radius, 0f));
            return !Physics.SphereCast(origin, r, Vector3.up, out _, distance, ~0, QueryTriggerInteraction.Ignore);
        }

        void HandleCursor()
        {
            if (!lockCursor) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Moves the player (e.g. back to bed on respawn), facing level ahead and standing still.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _controller.enabled = false; // a CharacterController overrides transform moves while enabled
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
            _velocity = Vector3.zero;
            _pitch = 0f;
            SetHeight(_standHeight);
        }

        void OnDied()
        {
            InputEnabled = false;
            _velocity = Vector3.zero;
        }
    }
}
