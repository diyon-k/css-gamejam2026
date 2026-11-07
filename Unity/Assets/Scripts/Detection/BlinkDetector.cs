using System;
using UnityEngine;

namespace Blink.Detection
{
    /// <summary>
    /// The one place the rest of the game asks "what are the player's eyes doing?".
    /// Merges every <see cref="IEyeStateSource"/> on this GameObject (eyes count as closed if ANY active
    /// source says so), classifies blinks vs. eyes-shut, and raises events.
    /// Everything here runs on unscaled time so fast-forward doesn't affect detection.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class BlinkDetector : MonoBehaviour
    {
        public static BlinkDetector Instance { get; private set; }

        [Tooltip("Eyes closed longer than this count as 'eyes shut' (fast-forward) instead of a blink.")]
        [SerializeField] float holdThreshold = 0.35f;
        [Tooltip("Closures shorter than this are ignored as noise.")]
        [SerializeField] float minClosedTime = 0.03f;
        [Tooltip("If the webcam can't see a face, treat the eyes as closed (looking away lets time pass).")]
        [SerializeField] bool treatMissingFaceAsClosed = true;
        [SerializeField] bool logEvents;

        /// <summary>A quick blink finished (fired when the eyes reopen).</summary>
        public event Action Blinked;

        /// <summary>The eyes have been closed past the hold threshold.</summary>
        public event Action EyesShutBegan;

        /// <summary>The eyes reopened after being shut. Argument: seconds they were closed.</summary>
        public event Action<float> EyesShutEnded;

        public bool EyesClosed => _classifier.IsClosed;
        public bool EyesShut => _classifier.IsShut;
        public int BlinkCount { get; private set; }

        /// <summary>Unscaled seconds since the eyes were last open-and-reopened. Drives stare effects.</summary>
        public float SecondsSinceLastBlink => EyesClosed ? 0f : Time.unscaledTime - _lastOpenedAt;

        /// <summary>Name of the source currently reporting closed eyes, or the first active one.</summary>
        public string ActiveSourceName { get; private set; } = "none";

        IEyeStateSource[] _sources = Array.Empty<IEyeStateSource>();
        BlinkClassifier _classifier;
        float _lastOpenedAt;

        // OnEnable, not Awake: a script recompile during Play wipes these fields and re-runs OnEnable but not Awake.
        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Blink] More than one BlinkDetector in the scene; disabling this one.");
                enabled = false;
                return;
            }
            Instance = this;
            if (_classifier == null)
            {
                _classifier = new BlinkClassifier(holdThreshold, minClosedTime);
                _lastOpenedAt = Time.unscaledTime;
            }
            _sources = GetComponents<IEyeStateSource>();
            if (_sources.Length == 0)
                Debug.LogWarning("[Blink] BlinkDetector has no IEyeStateSource components next to it.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnValidate()
        {
            if (_classifier == null) return;
            _classifier.HoldThreshold = holdThreshold;
            _classifier.MinClosedTime = minClosedTime;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            var e = _classifier.Update(ReadClosed(), now);

            switch (e.Type)
            {
                case BlinkEventType.Blink:
                    BlinkCount++;
                    _lastOpenedAt = now;
                    Log($"blink ({e.ClosedDuration * 1000f:0} ms)");
                    Blinked?.Invoke();
                    break;
                case BlinkEventType.ShutBegan:
                    Log("eyes shut");
                    EyesShutBegan?.Invoke();
                    break;
                case BlinkEventType.ShutEnded:
                    BlinkCount++;
                    _lastOpenedAt = now;
                    Log($"eyes open after {e.ClosedDuration:0.00} s");
                    EyesShutEnded?.Invoke(e.ClosedDuration);
                    break;
            }
        }

        bool ReadClosed()
        {
            string firstActive = null;
            foreach (var source in _sources)
            {
                if (!source.IsActive) continue;
                firstActive ??= source.SourceName;
                var state = source.Current;
                if (state.Closed || (treatMissingFaceAsClosed && !state.Present))
                {
                    ActiveSourceName = source.SourceName;
                    return true;
                }
            }
            ActiveSourceName = firstActive ?? "none";
            return false;
        }

        void Log(string message)
        {
            if (logEvents) Debug.Log($"[Blink] {message}");
        }
    }
}
