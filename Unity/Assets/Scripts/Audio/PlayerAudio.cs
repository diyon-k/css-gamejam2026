using System;
using System.Collections.Generic;
using Blink.Combat;
using Blink.Detection;
using Blink.Mobs;
using Blink.Player;
using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// The player's body: footsteps from <see cref="PlayerController.Velocity"/>, a grunt and thud when hurt,
    /// and a heartbeat that beats faster and louder as the nearest living zombie gets closer. The heartbeat
    /// is loudest with your eyes shut (<see cref="BlinkDetector.EyesShut"/>, not fast-forward, since a grab
    /// ends fast-forward while your eyes are still closed) and routes to the Player group, so the eyes-shut
    /// muffle never touches it: it's how you read danger without looking.
    ///
    /// Everything runs on real time (the player is outside world time). Each heartbeat is its own
    /// "lub-dub" one-shot, so the rate changes without pitching anything. This is the game's one heart:
    /// every beat also raises <see cref="Heartbeat"/>, which the gamepad rumble follows while your eyes are
    /// shut, so what you feel lands on what you hear. Finds the player at runtime; lives on GameSystems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAudio : MonoBehaviour
    {
        const float SearchInterval = 1f;

        [Tooltip("Defaults to the PlayerController in the scene (searched again if it disappears).")]
        [SerializeField] PlayerController player;

        [Header("Footsteps")]
        [SerializeField] SoundCue footstepCue;
        [Tooltip("Metres per footstep. Match CameraFeel's bob step length so steps land on the head dips.")]
        [SerializeField, Min(0.1f)] float stepLength = 1.6f;
        [Tooltip("Slower than this (m/s) counts as standing still.")]
        [SerializeField, Min(0f)] float minStepSpeed = 0.5f;

        [Header("Hurt")]
        [SerializeField] SoundCue hurtCue;
        [Tooltip("Body-hit thud layered under the grunt. Optional.")]
        [SerializeField] SoundCue hitThudCue;
        [Tooltip("Optional: plays when the player dies.")]
        [SerializeField] SoundCue deathCue;

        [Header("Heartbeat")]
        [Tooltip("One lub-dub. Retriggered at the current rate.")]
        [SerializeField] SoundCue heartbeatCue;
        [Tooltip("Zombies further than this (metres) don't register: no heartbeat.")]
        [SerializeField, Min(0.1f)] float heartbeatRange = 15f;
        [Tooltip("At or inside this distance the heartbeat is at full panic.")]
        [SerializeField, Min(0f)] float panicDistance = 2f;
        [Tooltip("Beats per minute with a zombie at the edge of the range / right on top of you.")]
        [SerializeField] Vector2 bpm = new(65f, 170f);
        [Tooltip("Heartbeat volume with a zombie at the edge of the range / right on top of you.")]
        [SerializeField] Vector2 heartbeatVolume = new(0.25f, 1f);
        [Tooltip("Volume multiplier while your eyes are open. Eyes shut is always 1.")]
        [SerializeField, Range(0f, 1f)] float eyesOpenHeartbeat = 0.35f;
        [Tooltip("How fast (per real second) the heartbeat intensity follows the zombie distance.")]
        [SerializeField, Min(0.01f)] float intensityResponse = 1.5f;
        [Tooltip("Real seconds between scans for the nearest zombie.")]
        [SerializeField, Min(0.05f)] float scanInterval = 0.25f;

        readonly StepCadence _steps = new(0.5f);
        readonly HeartbeatClock _heart = new();
        readonly List<MobController> _mobs = new();
        PlayerController _subscribed;
        Health _health;
        CharacterController _body;
        float _nextSearchTime, _nextScanTime;
        float _nearest = float.PositiveInfinity;
        float _intensity;

        /// <summary>Current heartbeat intensity, 0 (no zombie in range) to 1 (panic). For debugging and tests.</summary>
        public float HeartbeatIntensity => _intensity;
        /// <summary>Distance to the nearest living zombie at the last scan.</summary>
        public float NearestZombieDistance => _nearest;

        /// <summary>
        /// A heartbeat landed (real time), whether or not a heartbeat sound is assigned. Arguments: intensity
        /// (0..1) and rate (bpm). None while no zombie is in range. <c>GamepadHaptics</c> beats on it.
        /// </summary>
        public event Action<float, float> Heartbeat;

        void OnEnable()
        {
            if (player != null) Subscribe(player);
        }

        void OnDisable() => Unsubscribe();

        void Start() => FindPlayer();

        void Update()
        {
            if (_subscribed == null)
            {
                if (Time.unscaledTime >= _nextSearchTime) FindPlayer();
                return;
            }
            float dt = Time.unscaledDeltaTime;
            TickFootsteps(dt);
            TickHeartbeat(dt);
        }

        void TickFootsteps(float dt)
        {
            if (footstepCue == null) return;
            Vector3 v = _subscribed.Velocity;
            v.y = 0f;
            float speed = v.magnitude;
            bool grounded = _body == null || _body.isGrounded;
            if (speed < minStepSpeed || !grounded || (_health != null && _health.IsDead))
            {
                _steps.Reset();
                return;
            }
            if (_steps.Advance(speed * dt, stepLength)) Play(footstepCue);
        }

        void TickHeartbeat(float dt)
        {
            if (Time.unscaledTime >= _nextScanTime) ScanZombies();
            float target = HeartbeatMath.Intensity(_nearest, panicDistance, heartbeatRange);
            if (_health != null && _health.IsDead) target = 0f;
            _intensity = Mathf.MoveTowards(_intensity, target, intensityResponse * dt);
            if (_intensity <= 0f)
            {
                _heart.Reset(0.7f); // the first beat comes quickly once a zombie shows up
                return;
            }
            float rate = HeartbeatMath.Bpm(_intensity, bpm.x, bpm.y);
            if (!_heart.Tick(dt, rate)) return;
            Heartbeat?.Invoke(_intensity, rate);
            if (heartbeatCue == null) return;
            var eyes = BlinkDetector.Instance;
            float eyesScale = eyes != null && eyes.EyesShut ? 1f : eyesOpenHeartbeat;
            Play(heartbeatCue, HeartbeatMath.Volume(_intensity, heartbeatVolume.x, heartbeatVolume.y) * eyesScale);
        }

        void ScanZombies()
        {
            _nextScanTime = Time.unscaledTime + scanInterval;
            _nearest = float.PositiveInfinity;
            _mobs.Clear();
            _mobs.AddRange(FindObjectsByType<MobController>());
            Vector3 here = _subscribed.transform.position;
            foreach (var mob in _mobs)
            {
                if (mob == null || mob.CurrentState == MobController.State.Dead) continue;
                float d = Vector3.Distance(here, mob.transform.position);
                if (d < _nearest) _nearest = d;
            }
        }

        void OnDamaged(float amount, Vector3 point)
        {
            Play(hitThudCue);
            if (_health == null || !_health.IsDead) Play(hurtCue);
        }

        void OnDied() => Play(deathCue);

        static void Play(SoundCue cue, float volumeScale = 1f)
        {
            if (cue != null && AudioService.Instance != null) AudioService.Instance.Play(cue, volumeScale);
        }

        void FindPlayer()
        {
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null && isActiveAndEnabled) Subscribe(player);
        }

        void Subscribe(PlayerController target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _body = target.GetComponent<CharacterController>();
            _health = target.GetComponent<Health>();
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.Died += OnDied;
            }
        }

        void Unsubscribe()
        {
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }
            _health = null;
            _body = null;
            _subscribed = null;
        }
    }
}
