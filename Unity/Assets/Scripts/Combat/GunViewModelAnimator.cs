using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// Plays the first-person arms and pistol animations from <see cref="Gun"/> events: Fire per shot,
    /// FireLast on the shot that empties the magazine, Reload or ReloadEmpty while reloading.
    ///
    /// The reload clip does not free-run. Its state uses the <c>ReloadTime</c> float as motion time, and
    /// this component sets it from <see cref="Gun.ReloadProgress01"/> every frame, so a blink that skips
    /// the reload timer forward skips the hands forward too. Fast-forward needs nothing extra: the reload
    /// timer and the Animator both run on scaled time.
    ///
    /// Lives on the view model (next to its Animator). Visual only: it never changes the gun's state.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(100)] // after Gun and the time-skip system, so the reload progress is this frame's
    public sealed class GunViewModelAnimator : MonoBehaviour
    {
        static readonly int IdleState = Animator.StringToHash("Idle");
        static readonly int FireState = Animator.StringToHash("Fire");
        static readonly int FireLastState = Animator.StringToHash("FireLast");
        static readonly int ReloadState = Animator.StringToHash("Reload");
        static readonly int ReloadEmptyState = Animator.StringToHash("ReloadEmpty");
        static readonly int ReloadTimeParam = Animator.StringToHash("ReloadTime");

        [Tooltip("Defaults to the Gun on a parent.")]
        [SerializeField] Gun gun;
        [Tooltip("Defaults to the Animator on this object.")]
        [SerializeField] Animator animator;
        [Tooltip("The FireLast clip. The last shot starts the reload on the same frame, so the reload " +
                 "animation waits this clip's length before ReloadEmpty takes over.")]
        [SerializeField] AnimationClip fireLastClip;

        [Header("Blends (seconds)")]
        [SerializeField, Min(0f)] float fireBlend = 0.02f;
        [SerializeField, Min(0f)] float reloadBlend = 0.08f;
        [SerializeField, Min(0f)] float returnToIdleBlend = 0.15f;

        bool _hasReloadTimeParam;
        int _lastRoundFrame = -1;
        bool _reloadActive;
        bool _inReloadState;
        int _reloadState;
        float _leadInSeconds;

        void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (gun == null) gun = GetComponentInParent<Gun>();

            foreach (var p in animator.parameters)
                if (p.nameHash == ReloadTimeParam && p.type == AnimatorControllerParameterType.Float)
                    _hasReloadTimeParam = true;
            if (!_hasReloadTimeParam)
                Debug.LogWarning($"{name}: Animator has no float 'ReloadTime'; reload animations won't follow the reload timer.", this);
        }

        void OnEnable()
        {
            if (gun == null) return;
            gun.Fired += OnFired;
            gun.ReloadStarted += OnReloadStarted;
            gun.ReloadFinished += OnReloadFinished;
        }

        void OnDisable()
        {
            if (gun == null) return;
            gun.Fired -= OnFired;
            gun.ReloadStarted -= OnReloadStarted;
            gun.ReloadFinished -= OnReloadFinished;
        }

        void Update()
        {
            if (_reloadActive && gun.IsReloading) SyncReload();
        }

        void OnFired(bool hit, RaycastHit info)
        {
            var clip = GunViewModelLogic.FireClip(gun.AmmoInMagazine); // Fired is raised after the round is used.
            if (clip == GunViewClip.FireLast) _lastRoundFrame = Time.frameCount;
            _inReloadState = false;
            animator.CrossFadeInFixedTime(StateHash(clip), fireBlend, 0, 0f);
        }

        void OnReloadStarted()
        {
            _reloadState = StateHash(GunViewModelLogic.ReloadClip(gun.IsEmptyReload));
            bool followsLastShot = gun.IsEmptyReload && _lastRoundFrame == Time.frameCount;
            _leadInSeconds = followsLastShot && fireLastClip != null ? fireLastClip.length : 0f;
            _reloadActive = true;
            _inReloadState = false;
            SyncReload();
        }

        void OnReloadFinished()
        {
            if (!_reloadActive) return;
            _reloadActive = false;
            _inReloadState = false;
            if (_hasReloadTimeParam) animator.SetFloat(ReloadTimeParam, 1f);
            animator.CrossFadeInFixedTime(IdleState, returnToIdleBlend, 0);
        }

        void SyncReload()
        {
            if (!GunViewModelLogic.TryGetReloadClipTime(gun.ReloadProgress01, gun.ReloadDuration, _leadInSeconds,
                    out float t))
                return; // FireLast is still playing

            if (_hasReloadTimeParam) animator.SetFloat(ReloadTimeParam, t);
            if (_inReloadState) return;
            _inReloadState = true;
            animator.CrossFadeInFixedTime(_reloadState, reloadBlend, 0); // time comes from ReloadTime
        }

        static int StateHash(GunViewClip clip) => clip switch
        {
            GunViewClip.Fire => FireState,
            GunViewClip.FireLast => FireLastState,
            GunViewClip.Reload => ReloadState,
            GunViewClip.ReloadEmpty => ReloadEmptyState,
            _ => IdleState,
        };
    }
}
