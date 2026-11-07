using System;
using Blink.Player;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Combat
{
    /// <summary>
    /// Hitscan pistol fired from the centre of the camera. The reload runs on a <see cref="WorldTimer"/>,
    /// so blinking or shutting your eyes finishes it faster: the reference example of a skip that helps.
    /// Effects (muzzle flash, sound, recoil) belong in separate scripts listening to the events.
    /// </summary>
    public sealed class Gun : MonoBehaviour, ISkippable
    {
        [SerializeField] Camera aimCamera;
        [Tooltip("Where effects spawn. Optional.")]
        [SerializeField] Transform muzzle;

        [Header("Shooting")]
        [SerializeField] float damage = 34f;
        [SerializeField] float range = 60f;
        [Tooltip("Minimum world seconds between shots.")]
        [SerializeField] float fireInterval = 0.25f;
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Ammo")]
        [SerializeField, Min(1)] int magazineSize = 8;
        [SerializeField, Min(0)] int reserveAmmo = 32;
        [Tooltip("World seconds to reload with rounds still in the magazine. Matches the Reload clip (2.33 s).")]
        [SerializeField, Min(0f)] float reloadSeconds = 2.333f;
        [Tooltip("World seconds to reload from empty. Matches FireLast (0.37 s, the last shot starts the reload) " +
                 "plus ReloadEmpty (2.67 s), so both clips play at their natural speed.")]
        [SerializeField, Min(0f)] float emptyReloadSeconds = 3.033f;

        /// <summary>Args: hit something?, hit info (valid only if hit).</summary>
        public event Action<bool, RaycastHit> Fired;
        public event Action DryFired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;

        public int AmmoInMagazine { get; private set; }
        public int ReserveAmmo => reserveAmmo;
        public bool IsReloading => _reloadTimer.IsRunning;
        public float ReloadProgress01 => _reloadTimer.Progress01;
        /// <summary>World seconds the current (or last) reload takes in total.</summary>
        public float ReloadDuration => _reloadTimer.Duration;
        /// <summary>True if the current (or last) reload started with an empty magazine.</summary>
        public bool IsEmptyReload { get; private set; }
        public Transform Muzzle => muzzle;

        InputAction _fireAction;
        InputAction _reloadAction;
        float _nextShotTime;
        readonly WorldTimer _reloadTimer = new();
        IDamageable _owner;

        void Awake()
        {
            AmmoInMagazine = magazineSize;
            if (aimCamera == null) aimCamera = GetComponentInParent<Camera>();
            _owner = GetComponentInParent<IDamageable>();
            _reloadTimer.Completed += FinishReload;

            _fireAction = InputSystem.actions?.FindAction("Player/Attack");
            _reloadAction = new InputAction("Reload", InputActionType.Button);
            _reloadAction.AddBinding("<Keyboard>/r");
            _reloadAction.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable()
        {
            _fireAction?.Enable();
            _reloadAction.Enable();
            TimeSkipController.Register(this);
        }

        void OnDisable()
        {
            _reloadAction.Disable();
            TimeSkipController.Unregister(this);
        }

        void OnDestroy() => _reloadAction.Dispose();

        void Update()
        {
            _reloadTimer.Advance(Time.deltaTime);

            if (GrabQte.BlocksInput) return; // clicks belong to the grab QTE
            if (_fireAction != null && _fireAction.WasPressedThisFrame()) TryFire();
            if (_reloadAction.WasPressedThisFrame()) TryStartReload();
        }

        public void Skip(float seconds) => _reloadTimer.Advance(seconds);

        public void TryFire()
        {
            if (_owner != null && _owner.IsDead) return; // no shooting after you've been killed
            if (IsReloading || Time.time < _nextShotTime || aimCamera == null) return;
            if (AmmoInMagazine <= 0)
            {
                DryFired?.Invoke();
                TryStartReload();
                return;
            }

            AmmoInMagazine--;
            _nextShotTime = Time.time + fireInterval;

            var ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            bool hit = Physics.Raycast(ray, out var info, range, hitMask, QueryTriggerInteraction.Ignore);
            if (hit && info.collider.GetComponentInParent<IDamageable>() is { } target)
                target.TakeDamage(damage, info.point, ray.direction);

            Fired?.Invoke(hit, info);
            if (AmmoInMagazine == 0) TryStartReload();
        }

        public void TryStartReload()
        {
            if (IsReloading || AmmoInMagazine >= magazineSize || reserveAmmo <= 0) return;
            IsEmptyReload = AmmoInMagazine == 0;
            _reloadTimer.Duration = IsEmptyReload ? emptyReloadSeconds : reloadSeconds;
            _reloadTimer.Start();
            ReloadStarted?.Invoke();
        }

        public void AddAmmo(int amount) => reserveAmmo += Mathf.Max(0, amount);

        void FinishReload()
        {
            int needed = magazineSize - AmmoInMagazine;
            int taken = Mathf.Min(needed, reserveAmmo);
            AmmoInMagazine += taken;
            reserveAmmo -= taken;
            ReloadFinished?.Invoke();
        }
    }
}
