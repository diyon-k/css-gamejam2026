using System;
using UnityEngine;

namespace Blink.Detection
{
    /// <summary>
    /// The one place the rest of the game asks "what are the player's eyes doing?".
    /// Merges every <see cref="IEyeStateSource"/> on this GameObject (eyes count as closed if ANY active
    /// source says so), classifies blinks vs. eyes-shut, and raises events. The only source in the game is the
    /// webcam (<see cref="UdpEyeStateSource"/>); there is no keyboard blink (Space is Jump). Without a webcam,
    /// blink with <c>blink_server/send_test_blinks.py</c>.
    /// Looking away from the webcam counts as eyes open: no blink, no eyes-shut. Losing the face mid-blink
    /// drops that blink; losing it while shut ends the eyes-shut (see <see cref="BlinkClassifier.LoseFace"/>).
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
        [Tooltip("Off (default): no face in view counts as eyes open, so looking away from the webcam does nothing. " +
                 "On: no face counts as closed eyes, so looking away blinks and fast-forwards time.")]
        [SerializeField] bool treatMissingFaceAsClosed;
        [SerializeField] bool logEvents;

        /// <summary>A quick blink finished (fired when the eyes reopen).</summary>
        public event Action Blinked;

        /// <summary>The eyes have been closed past the hold threshold.</summary>
        public event Action EyesShutBegan;

        /// <summary>The eyes reopened after being shut. Argument: seconds they were closed.</summary>
        public event Action<float> EyesShutEnded;

        public bool EyesClosed => _classifier.IsClosed;
        public bool EyesShut => _classifier.IsShut;

        /// <summary>Seconds closed before a closure counts as eyes shut (when <see cref="EyesShutBegan"/> fires).</summary>
        public float HoldThreshold => holdThreshold;
        public int BlinkCount { get; private set; }

        /// <summary>Unscaled seconds since the eyes were last open-and-reopened. Drives stare effects.</summary>
        public float SecondsSinceLastBlink => EyesClosed ? 0f : Time.unscaledTime - _lastOpenedAt;

        /// <summary>Name of the source currently reporting closed eyes, or the first active one, or "none" (no webcam packets).</summary>
        public string ActiveSourceName { get; private set; } = "none";

        /// <summary>At least one source is reporting (the webcam sidecar is up and sending). False while it starts.</summary>
        public bool CameraRunning { get; private set; }

        /// <summary>A reporting source can see a face, so blinks can register.</summary>
        public bool FaceInView { get; private set; }

        IEyeStateSource[] _sources = Array.Empty<IEyeStateSource>();
        BlinkClassifier _classifier;
        EyeStateMerger _merger;
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
            _merger ??= new EyeStateMerger(treatMissingFaceAsClosed);
            _merger.Reset();
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
            if (_merger != null) _merger.TreatMissingFaceAsClosed = treatMissingFaceAsClosed;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            bool running = false, face = false;
            foreach (var source in _sources)
            {
                if (source == null || !source.IsActive) continue;
                running = true;
                face |= source.Current.Present;
            }
            CameraRunning = running;
            FaceInView = face;

            var reading = _merger.Read(_sources);
            ActiveSourceName = reading.SourceName;
            var e = reading.FaceLost ? _classifier.LoseFace(now) : _classifier.Update(reading.Closed, now);
            if (reading.FaceLost && e.Type == BlinkEventType.None) Log("face lost mid-blink: no blink");

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
                    Log($"eyes open after {e.ClosedDuration:0.00} s{(reading.FaceLost ? " (face lost)" : "")}");
                    EyesShutEnded?.Invoke(e.ClosedDuration);
                    break;
            }
        }

        void Log(string message)
        {
            if (logEvents) Debug.Log($"[Blink] {message}");
        }
    }
}
