using Blink.Interaction;
using Blink.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Combat
{
    /// <summary>
    /// On the Player, next to <see cref="PlayerWeapons"/>. Hold right mouse / left trigger with the pistol out to
    /// scope in: the camera zooms and the pistol slides to the centre of the screen. Not while switching, reloading,
    /// carrying a prop, dead or in the grab QTE. Runs on unscaled time (the player's hands, outside world time).
    /// </summary>
    [RequireComponent(typeof(PlayerWeapons))]
    public sealed class AimDownSights : MonoBehaviour
    {
        [Tooltip("Field of view while fully scoped in.")]
        [SerializeField, Range(10f, 90f)] float aimFov = 42f;
        [Tooltip("Where the pistol slot moves while scoped in (camera space). Lines the sights up with the centre.")]
        [SerializeField] Vector3 aimOffset = new(-0.11f, 0.04f, -0.02f);
        [Tooltip("Seconds to go fully in or out.")]
        [SerializeField, Min(0.01f)] float aimSeconds = 0.15f;

        /// <summary>0 = hip, 1 = fully scoped in.</summary>
        public float Aim01 { get; private set; }
        public bool IsAiming => Aim01 > 0.5f;

        PlayerWeapons _weapons;
        Camera _camera;
        IDamageable _owner;
        InputAction _aim;
        Vector3 _gunBase;
        float _baseFov;

        void Awake()
        {
            _weapons = GetComponent<PlayerWeapons>();
            _owner = GetComponent<IDamageable>();
            _camera = GetComponentInChildren<Camera>();
            if (_camera != null) _baseFov = _camera.fieldOfView;
            if (_weapons.Pistol != null) _gunBase = _weapons.Pistol.transform.localPosition;
            _aim = new InputAction("Aim", InputActionType.Button);
            _aim.AddBinding("<Mouse>/rightButton");
            _aim.AddBinding("<Gamepad>/leftTrigger");
        }

        void OnEnable() => _aim.Enable();
        void OnDisable() => _aim.Disable();
        void OnDestroy() => _aim.Dispose();

        void Update()
        {
            var gun = _weapons.Pistol;
            if (gun == null || _camera == null) return;

            bool want = _aim.IsPressed()
                        && _weapons.ActiveKind == WeaponKind.Pistol && !_weapons.IsSwitching && !gun.IsReloading
                        && Carryable.Held == null && !GrabQte.BlocksInput && (_owner == null || !_owner.IsDead);
            Aim01 = Mathf.MoveTowards(Aim01, want ? 1f : 0f, Mathf.Min(Time.unscaledDeltaTime, 0.1f) / aimSeconds);

            float t = Mathf.SmoothStep(0f, 1f, Aim01);
            _camera.fieldOfView = Mathf.Lerp(_baseFov, aimFov, t);
            gun.transform.localPosition = _gunBase + aimOffset * t;
        }
    }
}
