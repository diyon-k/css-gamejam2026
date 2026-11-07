using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Player
{
    /// <summary>
    /// First-person walk/sprint/look on a CharacterController, driven by the project-wide input
    /// actions (Player/Move, Player/Look, Player/Sprint). No jumping by design.
    /// During a blink skip the player keeps moving with their current velocity, so blinking
    /// mid-sprint covers ground.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour, ISkippable
    {
        [SerializeField] Transform cameraPivot;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 3.5f;
        [SerializeField] float sprintSpeed = 5.5f;
        [SerializeField] float acceleration = 20f;
        [SerializeField] float gravity = -20f;
        [Tooltip("Keep moving on the current velocity during a blink skip.")]
        [SerializeField] bool moveDuringSkip = true;

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
            TimeSkipController.Register(this);
            if (_health != null) _health.Died += OnDied;
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
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

        public void Skip(float seconds)
        {
            if (!moveDuringSkip || !_controller.enabled) return;
            var horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            _controller.Move(horizontal * seconds);
        }

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

            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), wish, acceleration * Time.deltaTime);

            float vertical = _controller.isGrounded ? -2f : _velocity.y + gravity * Time.deltaTime;
            _velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            _controller.Move(_velocity * Time.deltaTime);
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
