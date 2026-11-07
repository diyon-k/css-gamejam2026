using Blink.Combat;
using UnityEngine;

namespace Blink.Player
{
    /// <summary>
    /// First-person camera feel: recoil kick on each shot, a dip and tilt while reloading, and head bob plus
    /// strafe tilt while walking. Lives on the camera object, which sits under the pitch pivot that
    /// <see cref="PlayerController"/> rotates, so the two never write the same transform.
    ///
    /// Every frame the camera's local pose is set to its stored base pose plus this frame's offsets, so
    /// nothing accumulates: switching it off, or setting <see cref="Intensity"/> to 0, puts the camera exactly
    /// back where it started.
    ///
    /// Time: everything runs on unscaled time, so eyes-shut fast-forward (timeScale 3) and the grab QTE
    /// (timeScale 0) don't speed up or freeze the springs. Bob phase comes from the player's real-time speed,
    /// so it stays in step with the walking. The reload pose is read from <see cref="Gun.ReloadProgress01"/>
    /// rather than timed here, so a blink that jumps the reload forward jumps the camera with the hands.
    ///
    /// The gun is a child of the camera, so it rides along with all of this (crosshair, muzzle and hands stay
    /// aligned with where the shot goes). On top of that it gets a small bob of its own, against the head
    /// motion by default, so it reads as held in hands rather than painted on the screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraFeel : MonoBehaviour
    {
        [Tooltip("Transform to move. Defaults to this one (the camera).")]
        [SerializeField] Transform target;
        [Tooltip("Defaults to the PlayerController on a parent.")]
        [SerializeField] PlayerController player;
        [Tooltip("Defaults to the Gun under the target.")]
        [SerializeField] Gun gun;
        [Tooltip("Gets the extra view model bob below. Defaults to the gun's transform. Must not be animated.")]
        [SerializeField] Transform viewModel;

        [Tooltip("Master scale for every motion here. 0 turns camera feel off (for motion-sensitive players).")]
        [SerializeField, Range(0f, 2f)] float intensity = 1f;
        [Tooltip("Longest frame step the springs take, so a hitch doesn't jolt the camera.")]
        [SerializeField, Min(0.001f)] float maxDeltaTime = 0.1f;

        [Header("Recoil (peak per shot)")]
        [Tooltip("Degrees the view kicks up.")]
        [SerializeField] float recoilPitch = 1.4f;
        [Tooltip("Random sideways kick, up to this many degrees either way.")]
        [SerializeField] float recoilYaw = 0.4f;
        [Tooltip("Random roll, up to this many degrees either way.")]
        [SerializeField] float recoilRoll = 0.8f;
        [Tooltip("Metres the camera is pushed back.")]
        [SerializeField] float recoilPushBack = 0.015f;
        [Tooltip("Rapid shots stack up to roughly this many degrees of climb.")]
        [SerializeField, Min(0.1f)] float recoilMaxPitch = 5f;
        [Tooltip("How fast the view springs back. Higher is snappier.")]
        [SerializeField, Min(0.1f)] float recoilFrequency = 3.5f;
        [Tooltip("1 returns without overshoot; lower dips a little below the aim before settling.")]
        [SerializeField, Range(0.2f, 1f)] float recoilDamping = 0.7f;
        [SerializeField, Min(0.1f)] float pushBackFrequency = 5f;
        [SerializeField, Range(0.2f, 1f)] float pushBackDamping = 0.85f;

        [Header("Reload (follows the gun's reload progress)")]
        [Tooltip("Degrees the view dips down mid-reload.")]
        [SerializeField] float reloadPitch = 2.5f;
        [Tooltip("Degrees the head tilts mid-reload. Negative tilts right, towards the gun hand; positive tilts left.")]
        [SerializeField] float reloadRoll = -2f;
        [Tooltip("Metres the camera drops mid-reload.")]
        [SerializeField] float reloadDrop = 0.02f;
        [Tooltip("Fraction of the reload spent easing into the pose.")]
        [SerializeField, Range(0f, 0.5f)] float reloadEaseIn = 0.15f;
        [Tooltip("Fraction of the reload spent easing back out. The pose is gone exactly as the reload completes.")]
        [SerializeField, Range(0f, 0.5f)] float reloadEaseOut = 0.25f;

        [Header("Walking")]
        [Tooltip("Speed (m/s) at which the bob is at full strength. Match the player's walk speed.")]
        [SerializeField, Min(0.1f)] float bobReferenceSpeed = 3.5f;
        [Tooltip("Metres per footstep. The bob does one dip per step, so this sets its rhythm.")]
        [SerializeField, Min(0.1f)] float bobStepLength = 1.6f;
        [Tooltip("Seconds for the bob to fade in when you start walking and out when you stop.")]
        [SerializeField, Min(0f)] float bobFadeSeconds = 0.25f;
        [Tooltip("Metres up and down per step.")]
        [SerializeField] float bobVertical = 0.022f;
        [Tooltip("Metres side to side per stride.")]
        [SerializeField] float bobLateral = 0.015f;
        [Tooltip("Degrees the head nods down as each foot lands.")]
        [SerializeField] float bobPitch = 0.3f;
        [Tooltip("Degrees of roll side to side per stride.")]
        [SerializeField] float bobRoll = 0.5f;
        [Tooltip("Degrees the view leans into a strafe at full speed.")]
        [SerializeField] float strafeTilt = 1f;
        [Tooltip("How quickly the strafe lean follows the movement.")]
        [SerializeField, Min(0.1f)] float strafeTiltSharpness = 8f;

        [Header("View model (on top of the camera motion)")]
        [Tooltip("Metres the gun moves side to side per stride. Negative lags against the head.")]
        [SerializeField] float viewModelBobLateral = -0.004f;
        [Tooltip("Metres the gun moves up and down per step. Negative lags against the head.")]
        [SerializeField] float viewModelBobVertical = -0.004f;

        /// <summary>Master scale; 0 disables every motion and restores the base pose.</summary>
        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Clamp(value, 0f, 2f);
        }

        readonly Spring3 _recoilRotation = new(3.5f, 0.7f); // degrees: x pitch (negative is up), y yaw, z roll
        readonly Spring3 _recoilPosition = new(5f, 0.85f);  // metres, local
        readonly HeadBob _bob = new();
        float _strafe; // -1..1, smoothed

        Vector3 _basePosition, _viewModelBasePosition;
        Quaternion _baseRotation;
        bool _initialised, _subscribed;

        void Awake()
        {
            if (target == null) target = transform;
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (gun == null) gun = target.GetComponentInChildren<Gun>();
            if (viewModel == null && gun != null) viewModel = gun.transform;
            if (viewModel != null && !viewModel.IsChildOf(target)) viewModel = null; // must ride on the camera

            _basePosition = target.localPosition;
            _baseRotation = target.localRotation;
            if (viewModel != null) _viewModelBasePosition = viewModel.localPosition;
            _initialised = true;
        }

        void OnEnable()
        {
            if (gun == null || _subscribed) return;
            gun.Fired += OnFired;
            _subscribed = true;
        }

        void OnDisable()
        {
            if (_subscribed && gun != null) gun.Fired -= OnFired;
            _subscribed = false;
            _recoilRotation.Reset();
            _recoilPosition.Reset();
            _bob.Reset();
            _strafe = 0f;
            RestoreBasePose();
        }

        void LateUpdate() => Step(Mathf.Min(Time.unscaledDeltaTime, maxDeltaTime));

        /// <summary>
        /// Kicks the camera as if a shot was fired; <paramref name="scale"/> 1 is one pistol shot.
        /// The kick builds over the next frames, after the shot's raycast, so it never moves the shot that caused it.
        /// </summary>
        public void AddRecoil(float scale = 1f)
        {
            if (scale <= 0f) return;
            SyncSpringSettings();

            float rotPeak = Spring3.PeakPerUnitVelocity(recoilFrequency, recoilDamping);
            float posPeak = Spring3.PeakPerUnitVelocity(pushBackFrequency, pushBackDamping);
            float climb = CameraFeelMath.SoftCapScale(_recoilRotation.Value.x, recoilMaxPitch);

            var kickDegrees = new Vector3(
                -recoilPitch * climb, // negative x pitches up
                Random.Range(-recoilYaw, recoilYaw),
                Random.Range(-recoilRoll, recoilRoll));
            _recoilRotation.AddVelocity(kickDegrees * (scale / rotPeak));
            _recoilPosition.AddVelocity(new Vector3(0f, 0f, -recoilPushBack) * (scale / posPeak));
        }

        void OnFired(bool hit, RaycastHit info) => AddRecoil();

        /// <summary>Advances every motion by <paramref name="dt"/> real seconds and writes the pose.</summary>
        void Step(float dt)
        {
            if (!_initialised || target == null) return;
            SyncSpringSettings();
            _recoilRotation.Step(dt);
            _recoilPosition.Step(dt);

            // Walking: a stale velocity is left behind while input is off (grab QTE), so treat that as standing.
            Vector3 velocity = player != null && player.InputEnabled ? player.Velocity : Vector3.zero;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            _bob.Step(dt, speed, bobStepLength, bobReferenceSpeed, bobFadeSeconds);

            float lateral = player != null ? Vector3.Dot(velocity, player.transform.right) / bobReferenceSpeed : 0f;
            _strafe = Mathf.Lerp(_strafe, Mathf.Clamp(lateral, -1f, 1f), 1f - Mathf.Exp(-strafeTiltSharpness * dt));
            if (Mathf.Abs(_strafe) < 1e-5f && Mathf.Abs(lateral) < 1e-5f) _strafe = 0f;

            float reload = gun != null && gun.IsReloading
                ? CameraFeelMath.ReloadEnvelope(gun.ReloadProgress01, reloadEaseIn, reloadEaseOut)
                : 0f;

            Vector3 position = _recoilPosition.Value
                               + new Vector3(_bob.Lateral * bobLateral, _bob.Vertical * bobVertical, 0f)
                               + new Vector3(0f, -reloadDrop * reload, 0f);
            Vector3 euler = _recoilRotation.Value
                            + new Vector3(-_bob.Vertical * bobPitch, 0f, -_bob.Lateral * bobRoll - _strafe * strafeTilt)
                            + new Vector3(reloadPitch * reload, 0f, reloadRoll * reload);

            CameraFeelMath.Compose(_basePosition, _baseRotation, position, euler, intensity,
                out Vector3 pos, out Quaternion rot);
            target.SetLocalPositionAndRotation(pos, rot);

            if (viewModel != null)
            {
                var gunOffset = new Vector3(_bob.Lateral * viewModelBobLateral, _bob.Vertical * viewModelBobVertical, 0f);
                viewModel.localPosition = _viewModelBasePosition + gunOffset * intensity;
            }
        }

        void SyncSpringSettings()
        {
            _recoilRotation.FrequencyHz = recoilFrequency;
            _recoilRotation.DampingRatio = recoilDamping;
            _recoilPosition.FrequencyHz = pushBackFrequency;
            _recoilPosition.DampingRatio = pushBackDamping;
        }

        void RestoreBasePose()
        {
            if (!_initialised) return;
            if (target != null) target.SetLocalPositionAndRotation(_basePosition, _baseRotation);
            if (viewModel != null) viewModel.localPosition = _viewModelBasePosition;
        }
    }
}
