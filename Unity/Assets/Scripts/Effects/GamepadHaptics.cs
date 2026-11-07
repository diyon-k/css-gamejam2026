using Blink.Combat;
using Blink.Detection;
using Blink.Mobs;
using Blink.Player;
using Blink.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blink.Effects
{
    /// <summary>
    /// Gamepad rumble for the player's events. Lives on the Player prefab. Runs on unscaled time because eyes-shut
    /// sets timeScale to 3 and the grab QTE sets it to 0. One-shot pulses override the background layer (heartbeat,
    /// door strain); a pulse only replaces a running pulse of equal or lower priority.
    /// DualSense over Bluetooth gets no rumble (Input System limitation); use USB or an Xbox pad.
    /// </summary>
    public sealed class GamepadHaptics : MonoBehaviour
    {
        [Tooltip("Multiplies every pattern. Above 1 boosts weak pulses; motor output is capped at 1.")]
        [SerializeField, Range(0f, 2f)] float strength = 1.6f;
        [Tooltip("Heartbeat period (s) with no zombie near / with one touching you.")]
        [SerializeField] float beatSlow = 0.9f, beatFast = 0.35f;
        [Tooltip("Zombies further than this don't speed the heartbeat up.")]
        [SerializeField] float beatRange = 15f;

        Gun _gun;
        Health _health;
        BlinkDetector _eyes;
        JammedDoor[] _doors;
        Gamepad _pad;
        float _sentLow = -1f, _sentHigh = -1f;

        // Current one-shot pulse.
        float _pLow, _pHigh, _pDur, _pGap, _pStart = float.NegativeInfinity;
        int _pTaps, _pPrio;
        bool _pDecay;

        bool _shut, _wasGrabbed;
        float _lastBeat = float.NegativeInfinity, _nextBeat;

        void Start()
        {
            _gun = GetComponentInChildren<Gun>();
            _health = GetComponent<Health>();
            _eyes = BlinkDetector.Instance;
            // ponytail: doors cached once; re-find if doors ever spawn at runtime.
            _doors = FindObjectsByType<JammedDoor>();

            if (_eyes) { _eyes.Blinked += OnBlink; _eyes.EyesShutBegan += OnShut; _eyes.EyesShutEnded += OnOpened; }
            if (_gun) { _gun.Fired += OnFired; _gun.DryFired += OnDryFire; _gun.ReloadFinished += OnReloaded; }
            if (_health) { _health.Damaged += OnDamaged; _health.Died += OnDied; }
            foreach (var d in _doors) d.Opened += OnDoorOpened;
        }

        void OnDestroy()
        {
            if (_eyes) { _eyes.Blinked -= OnBlink; _eyes.EyesShutBegan -= OnShut; _eyes.EyesShutEnded -= OnOpened; }
            if (_gun) { _gun.Fired -= OnFired; _gun.DryFired -= OnDryFire; _gun.ReloadFinished -= OnReloaded; }
            if (_health) { _health.Damaged -= OnDamaged; _health.Died -= OnDied; }
            if (_doors != null) foreach (var d in _doors) if (d) d.Opened -= OnDoorOpened;
        }

        // Motors hold their last speed forever (even after leaving Play mode), so always stop them.
        void OnDisable() { StopPad(); _pad = null; }

        void StopPad()
        {
            if (_pad != null && _pad.added) _pad.ResetHaptics();
            _sentLow = _sentHigh = -1f;
        }

        void OnBlink() => Pulse(0.1f, 0.6f, 0.06f, 0, decay: false);
        void OnShut() { _shut = true; _nextBeat = Time.unscaledTime; }
        void OnOpened(float closedSeconds) => _shut = false;
        void OnFired(bool hit, RaycastHit info) => Pulse(0.45f, 0.9f, 0.09f, 1);
        void OnDryFire() => Pulse(0f, 0.3f, 0.03f, 0, decay: false);
        void OnReloaded() => Pulse(0f, 0.45f, 0.04f, 0, decay: false, taps: 2, gap: 0.07f);
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
            if (!Application.isFocused) { Send(0f, 0f); return; }

            float now = Time.unscaledTime, low = 0f, high = 0f;

            // Grab QTE: GrabQte has no events, so watch for it starting. Biggest jolt in the game.
            bool grabbed = GrabQte.Active;
            if (grabbed && !_wasGrabbed) Pulse(1f, 1f, 0.45f, 3);
            _wasGrabbed = grabbed;

            if (_shut) // lub-dub, faster as zombies close in
            {
                if (now >= _nextBeat) { _lastBeat = now; _nextBeat = now + BeatPeriod(); }
                float b = now - _lastBeat;
                if (b < 0.08f) low = 0.7f;
                else if (b >= 0.16f && b < 0.22f) low = 0.4f;
            }

            foreach (var d in _doors) // strain builds while forcing
                if (d && d.IsForcing)
                {
                    low = Mathf.Max(low, 0.1f + 0.4f * d.Progress01);
                    high = Mathf.Max(high, 0.25f * d.Progress01);
                }

            if (Envelope(now - _pStart, _pDur, _pGap, _pTaps, _pDecay, out float env)) // one-shot overrides background
            {
                low = _pLow * env;
                high = _pHigh * env;
            }
            Send(low * strength, high * strength);
        }

        float BeatPeriod()
        {
            float nearest = beatRange;
            foreach (var m in FindObjectsByType<MobController>()) // once per beat, cheap
                if (m.CurrentState != MobController.State.Dead)
                    nearest = Mathf.Min(nearest, Vector3.Distance(m.transform.position, transform.position));
            return Mathf.Lerp(beatFast, beatSlow, nearest / beatRange);
        }

        // Only send on change: a DS4 drops a command sent while another is in flight.
        void Send(float low, float high)
        {
            low = Mathf.Round(Mathf.Clamp01(low) * 20f) / 20f;
            high = Mathf.Round(Mathf.Clamp01(high) * 20f) / 20f;
            if (low == _sentLow && high == _sentHigh) return;
            _pad.SetMotorSpeeds(low, high);
            (_sentLow, _sentHigh) = (low, high);
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
