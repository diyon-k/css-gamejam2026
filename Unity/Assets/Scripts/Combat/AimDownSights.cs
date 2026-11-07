using Blink.Interaction;
using Blink.Mobs;
using Blink.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Combat
{
    /// <summary>
    /// On the Player, next to <see cref="PlayerWeapons"/>. Hold right mouse / left trigger with the pistol out to
    /// scope in: the camera zooms and the pistol slides to the centre of the screen. Not while switching, reloading,
    /// carrying a prop, dead or in the grab QTE. Runs on unscaled time (the player's hands, outside world time).
    /// Subtle aim assist (Minecraft-like, no snap): while scoped, a visible zombie near the crosshair gently pulls
    /// the view toward it and the gamepad stick slows slightly over it.
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

        [Header("Aim assist")]
        [SerializeField] bool aimAssist = true;
        [Tooltip("Only assist when scoping in with a gamepad (Fortnite does). Off = mouse gets it too.")]
        [SerializeField] bool gamepadOnly;
        [Tooltip("Degrees off-centre a zombie can be and still pull the view.")]
        [SerializeField, Range(0f, 45f)] float assistCone = 5f;
        [SerializeField, Min(0f)] float assistRange = 30f;
        [Tooltip("Height above the zombie's feet to aim at (chest).")]
        [SerializeField] float aimHeight = 1.3f;
        [Tooltip("How hard the view follows a tracked zombie (per second).")]
        [SerializeField, Min(0f)] float pullStrength = 1.5f;
        [Tooltip("Gamepad look speed while over a target.")]
        [SerializeField, Range(0.1f, 1f)] float slowdown = 0.8f;

        /// <summary>0 = hip, 1 = fully scoped in.</summary>
        public float Aim01 { get; private set; }
        public bool IsAiming => Aim01 > 0.5f;

        PlayerWeapons _weapons;
        Camera _camera;
        IDamageable _owner;
        PlayerController _player;
        MobController[] _mobs = System.Array.Empty<MobController>();
        bool _wanted;
        InputAction _aim;
        Vector3 _gunBase;
        float _baseFov;

        void Awake()
        {
            _weapons = GetComponent<PlayerWeapons>();
            _owner = GetComponent<IDamageable>();
            _player = GetComponent<PlayerController>();
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

            Assist(want);
        }

        void Assist(bool want)
        {
            bool justAimed = want && !_wanted;
            _wanted = want;
            if (_player != null) _player.GamepadLookScale = 1f;
            if (!aimAssist || _player == null || !want) return;
            if (gamepadOnly && _aim.activeControl?.device is not Gamepad) return;

            // ponytail: rescans zombies on each scope-in, fine for a house full; register in MobController if it gets big
            if (justAimed) _mobs = FindObjectsByType<MobController>(FindObjectsSortMode.None);
            if (!BestTarget(assistCone, out Vector3 point)) return;

            Vector2 err = AimError(transform.eulerAngles.y, _player.Pitch, point - _camera.transform.position);
            float k = 1f - Mathf.Exp(-pullStrength * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            _player.AddLook(err.x * k, err.y * k);
            _player.GamepadLookScale = slowdown;
        }

        bool BestTarget(float cone, out Vector3 best)
        {
            best = default;
            float bestAngle = cone;
            Vector3 eye = _camera.transform.position, fwd = _camera.transform.forward;
            foreach (var mob in _mobs)
            {
                if (mob == null || mob.CurrentState == MobController.State.Dead) continue;
                Vector3 p = mob.transform.position + Vector3.up * aimHeight;
                Vector3 to = p - eye;
                float angle = Vector3.Angle(fwd, to);
                if (angle > bestAngle || to.sqrMagnitude > assistRange * assistRange) continue;
                // No assist through walls: the first thing on the line must be this zombie.
                if (Physics.Linecast(eye, p, out var hit, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<MobController>() != mob
                    && !hit.collider.transform.IsChildOf(transform)) continue;
                bestAngle = angle;
                best = p;
            }
            return bestAngle < cone;
        }

        /// <summary>
        /// Degrees to turn (yaw, pitch; pitch positive = down) so a view at <paramref name="yaw"/>/<paramref name="pitch"/>
        /// looks along <paramref name="dir"/>.
        /// </summary>
        public static Vector2 AimError(float yaw, float pitch, Vector3 dir)
        {
            float wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(Mathf.DeltaAngle(yaw, wantYaw), Mathf.DeltaAngle(pitch, wantPitch));
        }
    }
}
