using System;
using Blink.Player;
using Blink.TimeSkip;
using Blink.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Combat
{
    /// <summary>
    /// Hitscan pistol fired from the centre of the camera. The reload runs on a <see cref="WorldTimer"/>,
    /// so blinking or shutting your eyes finishes it faster: the reference example of a skip that helps.
    /// Effects (muzzle flash, sound, recoil) belong in separate scripts listening to the events.
    /// On the player it is the pistol slot of <see cref="PlayerWeapons"/>, which turns <see cref="ReadsInput"/>
    /// off and calls <see cref="TryFire"/> / <see cref="TryStartReload"/> itself while the pistol is out.
    /// </summary>
    public sealed class Gun : MonoBehaviour, ISkippable
    {
        [SerializeField] Camera aimCamera;
        [Tooltip("Where effects spawn. Optional.")]
        [SerializeField] Transform muzzle;
        [Tooltip("Read the fire and reload input itself. PlayerWeapons turns this off and drives the gun instead.")]
        [SerializeField] bool readsInput = true;

        [Header("Shooting")]
        [SerializeField] float damage = 34f;
        [SerializeField] float range = 60f;
        [Tooltip("Minimum world seconds between shots.")]
        [SerializeField] float fireInterval = 0.25f;
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Noise (audible range in metres)")]
        [SerializeField, Min(0f)] float shotLoudness = 35f;
        [SerializeField, Min(0f)] float dryFireLoudness = 4f;

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
        public int MagazineSize => magazineSize;
        public bool IsReloading => _reloadTimer.IsRunning;
        public float ReloadProgress01 => _reloadTimer.Progress01;
        /// <summary>World seconds the current (or last) reload takes in total.</summary>
        public float ReloadDuration => _reloadTimer.Duration;
        /// <summary>True if the current (or last) reload started with an empty magazine.</summary>
        public bool IsEmptyReload { get; private set; }
        public Transform Muzzle => muzzle;
        /// <summary>Furthest a shot reaches, in metres.</summary>
        public float Range => range;

        /// <summary>When false, Update ignores the fire and reload input (something else calls TryFire).</summary>
        public bool ReadsInput
        {
            get => readsInput;
            set => readsInput = value;
        }

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

            if (!readsInput || GrabQte.BlocksInput) return; // clicks belong to the grab QTE
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
                Noise.Make(transform.position, dryFireLoudness);
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
            Noise.Make(transform.position, shotLoudness);
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

        /// <summary>
        /// Abandons a reload in progress without loading anything (the weapon was put away). Raises no event.
        /// </summary>
        public void CancelReload() => _reloadTimer.Cancel();

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
