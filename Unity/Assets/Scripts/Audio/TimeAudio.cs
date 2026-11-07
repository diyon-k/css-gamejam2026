using Blink.Player;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.Audio;

namespace Blink.Audio
{
    /// <summary>
    /// The sound of time: a sharp hit on every blink skip, the eyes-shut muffle while fast-forwarding, and a
    /// jump-scare sting when a zombie grabs you. Only listens to <see cref="TimeSkipController"/> and
    /// <see cref="GrabQte"/> events; lives on GameSystems.
    ///
    /// The muffle is the mixer's EyesShut snapshot (World group low-passed and turned down, Player and
    /// Stingers untouched). The mixer runs on unscaled time, so the 3x fast-forward doesn't speed up the
    /// fades, and nothing is pitched up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimeAudio : MonoBehaviour
    {
        const float SearchInterval = 1f;

        [Tooltip("Defaults to TimeSkipController.Instance.")]
        [SerializeField] TimeSkipController timeSkip;

        [Header("Blink skip")]
        [Tooltip("Sharp hit on every blink skip (2D, Stingers group).")]
        [SerializeField] SoundCue skipCue;
        [Tooltip("Optional low layer under the skip hit.")]
        [SerializeField] SoundCue skipLayerCue;

        [Header("Eyes shut")]
        [SerializeField] AudioMixerSnapshot eyesOpenSnapshot;
        [SerializeField] AudioMixerSnapshot eyesShutSnapshot;
        [Tooltip("Real seconds to fade into the muffle when the eyes shut.")]
        [SerializeField, Min(0f)] float shutFadeSeconds = 0.15f;
        [Tooltip("Real seconds to come back out when the eyes open. A grab snaps back instantly.")]
        [SerializeField, Min(0f)] float openFadeSeconds = 0.25f;
        [Tooltip("Optional: plays as the eyes shut (e.g. a breath in).")]
        [SerializeField] SoundCue eyesShutCue;
        [Tooltip("Optional: plays as the eyes open normally.")]
        [SerializeField] SoundCue eyesOpenCue;

        [Header("Grab")]
        [Tooltip("Jump-scare sting: a zombie interrupted fast-forward or started a grab. Give it a cooldown of ~1 s " +
                 "so the interrupt and the grab QTE starting don't both play it.")]
        [SerializeField] SoundCue grabStingCue;
        [Tooltip("Optional: you broke free of a grab.")]
        [SerializeField] SoundCue grabEscapedCue;

        readonly EyesMixState _mix = new();
        TimeSkipController _subscribed;
        float _nextSearchTime;
        bool _warnedNoSnapshots;

        void OnEnable()
        {
            GrabQte.Began += OnGrabBegan;
            GrabQte.Resolved += OnGrabResolved;
            if (_subscribed == null && timeSkip != null) Subscribe(timeSkip);
        }

        void OnDisable()
        {
            GrabQte.Began -= OnGrabBegan;
            GrabQte.Resolved -= OnGrabResolved;
            Unsubscribe();
            ApplyMix(false, interrupted: true); // never leave the world muffled
        }

        void Start()
        {
            if (eyesOpenSnapshot != null) eyesOpenSnapshot.TransitionTo(0f);
            FindTimeSkip();
        }

        void Update()
        {
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindTimeSkip();
        }

        void OnSkipped(float seconds)
        {
            Play(skipCue);
            Play(skipLayerCue);
        }

        void OnFastForwardStarted()
        {
            ApplyMix(true, interrupted: false);
            Play(eyesShutCue);
        }

        void OnFastForwardEnded(bool interrupted)
        {
            ApplyMix(false, interrupted);
            Play(interrupted ? grabStingCue : eyesOpenCue);
        }

        void OnGrabBegan() => Play(grabStingCue);

        void OnGrabResolved(bool escaped)
        {
            if (escaped) Play(grabEscapedCue);
        }

        void ApplyMix(bool shut, bool interrupted)
        {
            float seconds = _mix.Request(shut, interrupted, shutFadeSeconds, openFadeSeconds);
            if (seconds < 0f) return;
            var snapshot = shut ? eyesShutSnapshot : eyesOpenSnapshot;
            if (snapshot != null)
            {
                snapshot.TransitionTo(seconds);
                return;
            }
            if (!_warnedNoSnapshots && Application.isPlaying)
            {
                Debug.LogWarning("[Audio] TimeAudio has no EyesOpen/EyesShut snapshots; eyes-shut won't muffle the world.", this);
                _warnedNoSnapshots = true;
            }
        }

        static void Play(SoundCue cue)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.Play(cue);
        }

        void FindTimeSkip()
        {
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            if (timeSkip == null) timeSkip = TimeSkipController.Instance;
            if (timeSkip != null && isActiveAndEnabled) Subscribe(timeSkip);
        }

        void Subscribe(TimeSkipController target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _subscribed.Skipped += OnSkipped;
            _subscribed.FastForwardStarted += OnFastForwardStarted;
            _subscribed.FastForwardEnded += OnFastForwardEnded;
        }

        void Unsubscribe()
        {
            if (_subscribed == null) return;
            _subscribed.Skipped -= OnSkipped;
            _subscribed.FastForwardStarted -= OnFastForwardStarted;
            _subscribed.FastForwardEnded -= OnFastForwardEnded;
            _subscribed = null;
        }
    }
}
