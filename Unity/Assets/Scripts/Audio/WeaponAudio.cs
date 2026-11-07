using Blink.Combat;
using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Pistol sounds from <see cref="Gun"/> events: a layered gunshot (dry shot + room tail, round-robin with a
    /// little pitch spread), the dry-fire click, and reload foley timed to <see cref="Gun.ReloadProgress01"/>
    /// (mag out, mag in, and the slide on an empty reload). Because the foley follows the reload's progress,
    /// a blink that jumps the reload forward jumps the foley with it. Lives on the Effects prefab; if no gun
    /// is assigned it finds one at runtime, like <c>BulletTracer</c>. All gun sounds are 2D: it's your gun.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAudio : MonoBehaviour
    {
        const float GunSearchInterval = 1f;

        [Tooltip("Gun to listen to. If empty, the first Gun in the scene is used (searched again if it disappears).")]
        [SerializeField] Gun gun;

        [Header("Shot")]
        [Tooltip("The dry, close crack. Round-robin, +/-5% pitch.")]
        [SerializeField] SoundCue shotCue;
        [Tooltip("Room tail layered under each shot. Optional.")]
        [SerializeField] SoundCue tailCue;
        [Tooltip("Trigger pulled on an empty magazine.")]
        [SerializeField] SoundCue dryFireCue;

        [Header("Reload foley")]
        [Tooltip("Optional: plays the moment a reload starts (e.g. a cloth rustle).")]
        [SerializeField] SoundCue reloadStartCue;
        [SerializeField] SoundCue magOutCue;
        [SerializeField] SoundCue magInCue;
        [Tooltip("Slide release. Only on an empty reload (a tactical reload keeps a round chambered).")]
        [SerializeField] SoundCue slideCue;
        [Tooltip("Reload progress (0..1) of mag out, mag in and slide on a tactical reload. Negative = skip. " +
                 "Line these up with the Reload clip.")]
        [SerializeField] Vector3 reloadMarks = new(0.18f, 0.55f, -1f);
        [Tooltip("Reload progress (0..1) of mag out, mag in and slide on an empty reload (FireLast + ReloadEmpty).")]
        [SerializeField] Vector3 emptyReloadMarks = new(0.2f, 0.55f, 0.82f);

        readonly ReloadFoleyTrack _foley = new();
        Gun _subscribed;
        float _nextSearchTime;
        bool _tracking;

        void Start() => FindGun();

        void OnEnable()
        {
            if (gun != null) Subscribe(gun);
        }

        void OnDisable() => Unsubscribe();

        void Update()
        {
            if (_subscribed == null)
            {
                if (Time.unscaledTime >= _nextSearchTime) FindGun();
                return;
            }
            if (!_tracking) return;
            int mark = _foley.Advance(_subscribed.ReloadProgress01);
            if (mark >= 0) Play(MarkCue(mark));
            if (!_subscribed.IsReloading) _tracking = false;
        }

        void OnFired(bool hit, RaycastHit info)
        {
            Play(shotCue);
            Play(tailCue);
        }

        void OnDryFired() => Play(dryFireCue);

        void OnReloadStarted()
        {
            var marks = _subscribed.IsEmptyReload ? emptyReloadMarks : reloadMarks;
            _foley.Begin(new[] { marks.x, marks.y, marks.z }, _subscribed.ReloadProgress01);
            _tracking = true;
            Play(reloadStartCue);
        }

        void OnReloadFinished()
        {
            // A skip can finish the reload inside one frame: play the last mark it jumped over.
            if (!_tracking) return;
            int mark = _foley.Advance(1f);
            if (mark >= 0) Play(MarkCue(mark));
            _tracking = false;
        }

        SoundCue MarkCue(int index) => index switch
        {
            0 => magOutCue,
            1 => magInCue,
            _ => slideCue,
        };

        static void Play(SoundCue cue)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.Play(cue);
        }

        void FindGun()
        {
            _nextSearchTime = Time.unscaledTime + GunSearchInterval;
            if (gun == null) gun = FindAnyObjectByType<Gun>();
            if (gun != null && isActiveAndEnabled) Subscribe(gun);
        }

        void Subscribe(Gun target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _subscribed.Fired += OnFired;
            _subscribed.DryFired += OnDryFired;
            _subscribed.ReloadStarted += OnReloadStarted;
            _subscribed.ReloadFinished += OnReloadFinished;
        }

        void Unsubscribe()
        {
            if (_subscribed != null)
            {
                _subscribed.Fired -= OnFired;
                _subscribed.DryFired -= OnDryFired;
                _subscribed.ReloadStarted -= OnReloadStarted;
                _subscribed.ReloadFinished -= OnReloadFinished;
            }
            _subscribed = null;
            _tracking = false;
        }
    }
}
