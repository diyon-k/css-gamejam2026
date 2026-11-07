using System;
using System.Collections.Generic;
using Blink.Detection;
using Blink.Player;
using UnityEngine;

namespace Blink.TimeSkip
{
    /// <summary>
    /// Owns world time. A blink skips the world forward by <see cref="blinkSkipSeconds"/>
    /// (every registered <see cref="ISkippable"/> jumps): instantly, or, when a <see cref="Transition"/>
    /// is set (the eyelids), at the moment the screen is covered. Eyes held shut run the world at
    /// <see cref="fastForwardScale"/> via Time.timeScale until they reopen.
    ///
    /// World systems (zombies, timers) use scaled time. The player is not part of world time: it
    /// isn't skipped and moves on real time. Camera, UI, effects and audio cues use unscaled time.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class TimeSkipController : MonoBehaviour
    {
        public static TimeSkipController Instance { get; private set; }

        static readonly List<ISkippable> Skippables = new();

        [Tooltip("World seconds skipped by one blink.")]
        [SerializeField, Min(0f)] float blinkSkipSeconds = 0.5f;
        [Tooltip("Time.timeScale while the eyes are held shut.")]
        [SerializeField, Min(1f)] float fastForwardScale = 3f;
        [SerializeField] BlinkDetector detector;

        /// <summary>After a blink skip was applied. Argument: world seconds skipped. Hook SkipFX here.</summary>
        public event Action<float> Skipped;

        /// <summary>Fast-forward started (eyes shut).</summary>
        public event Action FastForwardStarted;

        /// <summary>Fast-forward stopped. Argument: true if a zombie interrupted it (grab).</summary>
        public event Action<bool> FastForwardEnded;

        public bool IsFastForwarding { get; private set; }
        public float TotalSkippedSeconds { get; private set; }
        public float BlinkSkipSeconds => blinkSkipSeconds;

        /// <summary>
        /// Optional screen transition (the eyelids) that hides each blink skip. When set, a blink plays
        /// it and the world jumps once the screen is covered, instead of instantly.
        /// </summary>
        public static IBlinkTransition Transition { get; set; }

        /// <summary>While true, blinks and eyes-shut don't touch world time (e.g. the wake-up intro).</summary>
        public static bool Locked { get; set; }

        bool _interruptedWhileShut;

        // Clears the static registry when entering play mode with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Skippables.Clear();
            Instance = null;
            Transition = null;
            Locked = false;
        }

        public static void Register(ISkippable skippable)
        {
            if (skippable != null && !Skippables.Contains(skippable)) Skippables.Add(skippable);
        }

        public static void Unregister(ISkippable skippable) => Skippables.Remove(skippable);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[TimeSkip] More than one TimeSkipController in the scene; disabling this one.");
                enabled = false;
                return;
            }
            Instance = this;
        }

        void Start()
        {
            if (detector == null) detector = BlinkDetector.Instance;
            if (detector == null)
            {
                Debug.LogError("[TimeSkip] No BlinkDetector found; blinking won't affect time.");
                return;
            }
            detector.Blinked += OnBlinked;
            detector.EyesShutBegan += OnEyesShutBegan;
            detector.EyesShutEnded += OnEyesShutEnded;
        }

        void OnDestroy()
        {
            if (detector != null)
            {
                detector.Blinked -= OnBlinked;
                detector.EyesShutBegan -= OnEyesShutBegan;
                detector.EyesShutEnded -= OnEyesShutEnded;
            }
            Time.timeScale = 1f;
            if (Instance == this) Instance = null;
        }

        /// <summary>Instantly advances every registered ISkippable by <paramref name="seconds"/>.</summary>
        public void SkipWorld(float seconds)
        {
            if (seconds <= 0f) return;
            // Copy: a skip can kill or spawn things that (un)register themselves.
            foreach (var skippable in Skippables.ToArray())
            {
                if (skippable is UnityEngine.Object obj && obj == null) continue;
                skippable.Skip(seconds);
            }
            TotalSkippedSeconds += seconds;
            Skipped?.Invoke(seconds);
        }

        /// <summary>
        /// Stops fast-forward immediately, e.g. when a zombie grabs the player. The world stays at
        /// normal speed until the eyes reopen.
        /// </summary>
        public void InterruptFastForward()
        {
            if (!IsFastForwarding) return;
            _interruptedWhileShut = true;
            StopFastForward(interrupted: true);
        }

        // Space doubles as the keyboard blink key, so blinks during/just after a grab QTE are ignored.
        void OnBlinked()
        {
            if (GrabQte.BlocksInput || Locked) return;
            if (Transition is UnityEngine.Object obj && obj == null) Transition = null; // destroyed
            if (Transition == null)
            {
                SkipWorld(blinkSkipSeconds);
                return;
            }
            // Jump while the screen is black. A grab that started meanwhile has frozen the world: drop the skip.
            Transition.Play(() =>
            {
                if (this != null && !GrabQte.Active) SkipWorld(blinkSkipSeconds);
            });
        }

        void OnEyesShutBegan()
        {
            if (GrabQte.BlocksInput || Locked) return;
            _interruptedWhileShut = false;
            IsFastForwarding = true;
            Time.timeScale = fastForwardScale;
            FastForwardStarted?.Invoke();
        }

        void OnEyesShutEnded(float closedSeconds)
        {
            if (_interruptedWhileShut || !IsFastForwarding) // also: shut began during a QTE and was ignored
            {
                _interruptedWhileShut = false;
                return;
            }
            StopFastForward(interrupted: false);
        }

        void StopFastForward(bool interrupted)
        {
            IsFastForwarding = false;
            Time.timeScale = 1f;
            FastForwardEnded?.Invoke(interrupted);
        }
    }
}
