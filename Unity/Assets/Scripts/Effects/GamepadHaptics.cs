using Blink.Audio;
using Blink.Combat;
using Blink.Detection;
using Blink.Player;
using Blink.TimeSkip;
using Blink.World;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blink.Effects
{
    /// <summary>
    /// Gamepad rumble for the player's events. Lives on the Player prefab, and is the only thing that drives the
    /// motors: it mixes the steady layers (the eyes-shut drone and heartbeat, door strain) with one-shot event
    /// pulses by taking the larger speed per motor. A pulse only replaces a running pulse of equal or lower priority.
    /// Runs on unscaled time because eyes-shut sets timeScale to 3 and the grab QTE sets it to 0. The motors are
    /// reset on disable, pause, focus loss, quit and pad change, because they hold their last speed forever.
    /// DualSense over Bluetooth gets no rumble (Input System limitation); use USB or an Xbox pad.
    /// </summary>
    public sealed class GamepadHaptics : MonoBehaviour
    {
        [Tooltip("Multiplies every pattern. Above 1 boosts weak pulses; motor output is capped at 1.")]
        [SerializeField, Range(0f, 2f)] float strength = 1.6f;
        [Tooltip("Continuous rumble while the eyes are shut (fast-forward): a drone plus a lub-dub on every heartbeat " +
                 "PlayerAudio plays, so you feel the beats you hear. Values are before Strength.")]
        [SerializeField] EyesShutRumble eyesShut = new();

        PlayerWeapons _weapons;
        Health _health;
        BlinkDetector _eyes;
        TimeSkipController _time;
        PlayerAudio _heart;
        JammedDoor[] _doors;
        Gamepad _pad;
        Rumble _sent = Unsent;

        static readonly Rumble Unsent = new(-1f, -1f);

        // Current one-shot pulse.
        float _pLow, _pHigh, _pDur, _pGap, _pStart = float.NegativeInfinity;
        int _pTaps, _pPrio;
        bool _pDecay;

        bool _wasGrabbed;

        void Start()
        {
            _weapons = GetComponent<PlayerWeapons>();
            _health = GetComponent<Health>();
            _eyes = BlinkDetector.Instance;
            _time = TimeSkipController.Instance;
            _heart = FindAnyObjectByType<PlayerAudio>(); // the one heartbeat; null = drone only
            // ponytail: doors cached once; re-find if doors ever spawn at runtime.
            _doors = FindObjectsByType<JammedDoor>();

            if (_eyes) _eyes.Blinked += OnBlink;
            if (_time) { _time.FastForwardStarted += OnFastForwardStarted; _time.FastForwardEnded += OnFastForwardEnded; }
            if (_heart) _heart.Heartbeat += OnHeartbeat;
            if (_weapons) { _weapons.ShotFired += OnFired; _weapons.DryFired += OnDryFire; _weapons.ReloadFinished += OnReloaded; _weapons.MeleeHit += OnMeleeHit; }
            if (_health) { _health.Damaged += OnDamaged; _health.Died += OnDied; }
            foreach (var d in _doors) d.Opened += OnDoorOpened;
        }

        void OnDestroy()
        {
            if (_eyes) _eyes.Blinked -= OnBlink;
            if (_time) { _time.FastForwardStarted -= OnFastForwardStarted; _time.FastForwardEnded -= OnFastForwardEnded; }
            if (_heart) _heart.Heartbeat -= OnHeartbeat;
            if (_weapons) { _weapons.ShotFired -= OnFired; _weapons.DryFired -= OnDryFire; _weapons.ReloadFinished -= OnReloaded; _weapons.MeleeHit -= OnMeleeHit; }
            if (_health) { _health.Damaged -= OnDamaged; _health.Died -= OnDied; }
            if (_doors != null) foreach (var d in _doors) if (d) d.Opened -= OnDoorOpened;
        }

        // Motors hold their last speed forever (even after leaving Play mode), so always stop them: on disable, when
        // the app is paused or loses focus (Update may not run then), on quit, and when the Editor pauses.
        void OnEnable()
        {
#if UNITY_EDITOR
            EditorApplication.pauseStateChanged += OnEditorPause;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.pauseStateChanged -= OnEditorPause;
#endif
            StopPad();
            _pad = null;
        }

        void OnApplicationPause(bool paused) { if (paused) StopPad(); }
        void OnApplicationFocus(bool focused) { if (!focused) StopPad(); }
        void OnApplicationQuit() => StopPad();
#if UNITY_EDITOR
        void OnEditorPause(PauseState state) { if (state == PauseState.Paused) StopPad(); }
#endif

        void StopPad()
        {
            if (_pad != null && _pad.added) _pad.ResetHaptics();
            _sent = Unsent; // resend whatever comes next
        }

        void OnBlink() => Pulse(0.1f, 0.6f, 0.06f, 0, decay: false);
        void OnFastForwardStarted() => eyesShut.Begin(Time.unscaledTime);
        void OnFastForwardEnded(bool interrupted) => eyesShut.End(Time.unscaledTime, interrupted);
        void OnHeartbeat(float intensity, float bpm) => eyesShut.Beat(Time.unscaledTime, intensity, bpm);
        void OnFired(WeaponKind kind) => Pulse(0.45f, 0.9f, 0.09f, 1);
        void OnDryFire(WeaponKind kind) => Pulse(0f, 0.3f, 0.03f, 0, decay: false);
        void OnReloaded(WeaponKind kind) => Pulse(0f, 0.45f, 0.04f, 0, decay: false, taps: 2, gap: 0.07f);
        void OnMeleeHit(MeleeHitInfo hit) => Pulse(hit.Target != null ? 0.7f : 0.4f, 0.4f, 0.1f, 1); // thunk
        void OnDoorOpened() => Pulse(0.8f, 0.5f, 0.25f, 1);
        void OnDied() => enabled = false;

        void OnDamaged(float amount, Vector3 point)
        {
            float f = Mathf.Clamp01(2f * amount / _health.Max);
            Pulse(0.5f + 0.5f * f, 0.3f + 0.5f * f, 0.15f + 0.25f * f, 2);
        }

        void Pulse(float low, float high, float dur, int prio, bool decay = true, int taps = 1, float gap = 0f)
        {
            bool running = Envelope(Time.unscaledTime - _pStart, _pDur, _pGap, _pTaps, _pDecay, out _);
            if (running && prio < _pPrio) return;
            (_pLow, _pHigh, _pDur, _pGap, _pTaps, _pPrio, _pDecay, _pStart) = (low, high, dur, gap, taps, prio, decay, Time.unscaledTime);
        }

        void Update()
        {
            if (Gamepad.current != _pad) { StopPad(); _pad = Gamepad.current; }
            if (_pad == null) return;
            if (!Application.isFocused) { Send(Rumble.Off); return; }
            Tick(Time.unscaledTime);
        }

        /// <summary>Works out the rumble at real time <paramref name="now"/> and sends it to the pad.</summary>
        void Tick(float now)
        {
            // Grab QTE: watch for it starting. Biggest jolt in the game.
            bool grabbed = GrabQte.Active;
            if (grabbed && !_wasGrabbed) Pulse(1f, 1f, 0.45f, 3);
            _wasGrabbed = grabbed;

            // Never stuck on: if fast-forward ended without telling us (controller gone), drop the eyes-shut layer.
            if (eyesShut.Active && (_time == null || !_time.IsFastForwarding)) eyesShut.End(now, false);

            Send(Mix(now).Scaled(strength));
        }

        /// <summary>Everything that should be on the motors at real time <paramref name="now"/>, before Strength.</summary>
        Rumble Mix(float now)
        {
            var mix = eyesShut.Sample(now, _heart ? _heart.HeartbeatIntensity : 0f);

            if (_doors != null)
                foreach (var d in _doors) // strain builds while forcing
                    if (d && d.IsForcing)
                        mix = Rumble.Max(mix, new Rumble(0.1f + 0.4f * d.Progress01, 0.25f * d.Progress01));

            if (Envelope(now - _pStart, _pDur, _pGap, _pTaps, _pDecay, out float env))
                mix = Rumble.Max(mix, new Rumble(_pLow, _pHigh).Scaled(env));
            return mix;
        }

        // Only send on change: a DS4 drops a command sent while another is in flight.
        void Send(Rumble rumble)
        {
            rumble = rumble.Quantized();
            if (rumble.Equals(_sent)) return;
            _pad.SetMotorSpeeds(rumble.Low, rumble.High);
            _sent = rumble;
        }

        /// <summary>
        /// Strength (0-1) of a pulse <paramref name="t"/> seconds after it started: <paramref name="taps"/> taps of
        /// <paramref name="dur"/> seconds separated by <paramref name="gap"/>, flat or fading out. False once it's over.
        /// </summary>
        public static bool Envelope(float t, float dur, float gap, int taps, bool decay, out float env)
        {
            env = 0f;
            if (t < 0f || dur <= 0f || t >= (dur + gap) * taps - gap) return false;
            float k = t % (dur + gap);
            if (k < dur) env = decay ? 1f - k / dur : 1f;
            return true;
        }
    }
}
