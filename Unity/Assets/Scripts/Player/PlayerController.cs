using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Player
{
    /// <summary>
    /// First-person walk/sprint/look on a CharacterController, driven by the project-wide input
    /// actions (Player/Move, Player/Look, Player/Sprint). No jumping by design.
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
        [SerializeField] float sprintSpeed = 5.5f;
        [SerializeField] float acceleration = 20f;
        [SerializeField] float gravity = -20f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.1f;
        [SerializeField] float gamepadSensitivity = 180f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] bool lockCursor = true;

        public Vector3 Velocity => _velocity;
        public bool IsSprinting { get; private set; }
        public bool InputEnabled { get; set; } = true;

        CharacterController _controller;
        Health _health;
        InputAction _move, _look, _sprint;
        Vector3 _velocity;
        float _pitch;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            if (cameraPivot == null && GetComponentInChildren<Camera>() is { } cam) cameraPivot = cam.transform;

            var actions = InputSystem.actions;
            if (actions == null)
            {
                Debug.LogError("[Player] No project-wide input actions set (Project Settings > Input System Package).");
                return;
            }
            _move = actions.FindAction("Player/Move");
            _look = actions.FindAction("Player/Look");
            _sprint = actions.FindAction("Player/Sprint");
        }

        void OnEnable()
        {
            _move?.Enable();
            _look?.Enable();
            _sprint?.Enable();
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
            IsSprinting = _sprint != null && _sprint.IsPressed() && input.y > 0.1f;
            float speed = IsSprinting ? sprintSpeed : walkSpeed;

            // Real time, not world time: the player must not gain ground from time skipping.
            float dt = RealDeltaTime(Time.deltaTime, Time.timeScale);

            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), wish, acceleration * dt);

            float vertical = _controller.isGrounded ? -2f : _velocity.y + gravity * dt;
            _velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            _controller.Move(_velocity * dt);
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

        void OnDied()
        {
            InputEnabled = false;
            _velocity = Vector3.zero;
        }
    }
}
