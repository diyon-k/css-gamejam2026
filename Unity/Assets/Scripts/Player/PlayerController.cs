using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Player
{
    /// <summary>
    /// First-person walk/look on a CharacterController, driven by the project-wide input actions
    /// (Player/Move, Player/Look, Player/Jump, Player/Crouch). Crouch is held to crawl: the capsule and
    /// camera drop, and the player stays down while something is overhead. No sprinting by design.
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

        [Header("Crawl")]
        [SerializeField] float crawlSpeed = 1.5f;
        [SerializeField] float crawlHeight = 0.8f;
        [Tooltip("Metres per second the capsule shrinks or grows when going down or getting up.")]
        [SerializeField] float crawlTransitionSpeed = 4f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.1f;
        [SerializeField] float gamepadSensitivity = 180f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] bool lockCursor = true;

        public Vector3 Velocity => _velocity;
        public bool InputEnabled { get; set; } = true;
        public bool IsCrawling { get; private set; }

        CharacterController _controller;
        Health _health;
        InputAction _move, _look, _jump, _crouch;
        Vector3 _velocity;
        float _pitch;
        float _standHeight, _feetY, _standPivotY;

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
        }

        void OnEnable()
        {
            _move?.Enable();
            _look?.Enable();
            _jump?.Enable();
            _crouch?.Enable();
            if (_health != null) _health.Died += OnDied;
        }

        void OnDisable()
        {
            if (_health != null) _health.Died -= OnDied;
        }

        void Start()
        {
            if (lockCursor) SetCursorLocked(true);
        }

        void Update()
        {
            HandleCursor();
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
            delta *= isGamepad ? gamepadSensitivity * Time.unscaledDeltaTime : mouseSensitivity;

            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -maxPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            // Real time, not world time: the player must not gain ground from time skipping.
            float dt = RealDeltaTime(Time.deltaTime, Time.timeScale);

            Crawl(dt);

            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * (IsCrawling ? crawlSpeed : walkSpeed);
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), wish, acceleration * dt);

            float vertical = _controller.isGrounded ? -2f : _velocity.y + gravity * dt;
            if (_controller.isGrounded && !IsCrawling && _jump != null && _jump.WasPressedThisFrame())
                vertical = Mathf.Sqrt(-2f * gravity * jumpHeight);
            _velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            var flags = _controller.Move(_velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f) _velocity.y = 0f; // bonk the ceiling
        }

        /// <summary>Holding Crouch shrinks the capsule (feet stay put) and drops the camera with it.</summary>
        void Crawl(float dt)
        {
            bool held = _crouch != null && _crouch.IsPressed();
            IsCrawling = held || (IsCrawling && !HeadroomToStand());

            float height = Mathf.MoveTowards(_controller.height, IsCrawling ? crawlHeight : _standHeight, crawlTransitionSpeed * dt);
            if (Mathf.Approximately(height, _controller.height)) return;
            _controller.height = height;
            _controller.center = new Vector3(_controller.center.x, _feetY + height * 0.5f, _controller.center.z);
            if (cameraPivot != null)
            {
                Vector3 p = cameraPivot.localPosition;
                cameraPivot.localPosition = new Vector3(p.x, _standPivotY - (_standHeight - height), p.z);
            }
        }

        bool HeadroomToStand()
        {
            float r = _controller.radius * 0.9f;
            // Starts inside our own capsule, which casts ignore, so only real ceilings count.
            Vector3 origin = transform.TransformPoint(new Vector3(0f, _feetY + _controller.height - _controller.radius, 0f));
            float distance = _standHeight - _controller.height;
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
        }

        void OnDied()
        {
            InputEnabled = false;
            _velocity = Vector3.zero;
        }
    }
}
