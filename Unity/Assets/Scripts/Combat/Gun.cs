using System;
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
        [SerializeField] WorldTimer reloadTimer = new(2f);

        /// <summary>Args: hit something?, hit info (valid only if hit).</summary>
        public event Action<bool, RaycastHit> Fired;
        public event Action DryFired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;

        public int AmmoInMagazine { get; private set; }
        public int ReserveAmmo => reserveAmmo;
        public bool IsReloading => reloadTimer.IsRunning;
        public float ReloadProgress01 => reloadTimer.Progress01;
        public Transform Muzzle => muzzle;

        InputAction _fireAction;
        InputAction _reloadAction;
        float _nextShotTime;
        IDamageable _owner;

        void Awake()
        {
            AmmoInMagazine = magazineSize;
            if (aimCamera == null) aimCamera = GetComponentInParent<Camera>();
            _owner = GetComponentInParent<IDamageable>();
            reloadTimer.Completed += FinishReload;

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
            reloadTimer.Advance(Time.deltaTime);

            if (_fireAction != null && _fireAction.WasPressedThisFrame()) TryFire();
            if (_reloadAction.WasPressedThisFrame()) TryStartReload();
        }

        public void Skip(float seconds) => reloadTimer.Advance(seconds);

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
            reloadTimer.Start();
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
