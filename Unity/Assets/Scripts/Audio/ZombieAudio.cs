using Blink.Combat;
using Blink.Mobs;
using UnityEngine;
using UnityEngine.AI;

namespace Blink.Audio
{
    /// <summary>
    /// A zombie's voice and feet. Vocals play from one 3D AudioSource on the zombie's head, so a new vocal
    /// cuts off the previous one and death silences the rest: occasional groans, a growl or scream when it
    /// starts hunting you (<see cref="MobController.StateChanged"/>), a grunt on each attack, pain when shot
    /// (sometimes) and a death cry. Footsteps go through <see cref="AudioService"/> at the feet, timed by
    /// distance walked.
    ///
    /// Timers run on <b>scaled</b> time on purpose: with your eyes shut the world runs 3x faster, so you hear
    /// 3x the footsteps and groans as they close in. Pitch stays put; only the rate changes. The pain
    /// cooldown is real time. Lives on the Zombie prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZombieAudio : MonoBehaviour
    {
        [Tooltip("Defaults to the MobController on this object.")]
        [SerializeField] MobController mob;
        [Tooltip("Defaults to the Health on this object.")]
        [SerializeField] Health health;
        [Tooltip("Where vocals come from, relative to the zombie's root.")]
        [SerializeField] Vector3 mouthOffset = new(0f, 1.6f, 0f);

        [Header("Vocals")]
        [Tooltip("Ambient groans while it's alive and not attacking.")]
        [SerializeField] SoundCue groanCue;
        [Tooltip("World seconds between groans (random in range). Scaled time: 3x as often with eyes shut.")]
        [SerializeField] Vector2 groanInterval = new(5f, 12f);
        [Tooltip("Growl or scream when it starts hunting you (e.g. Idle/Patrol to Chase).")]
        [SerializeField] SoundCue alertCue;
        [Tooltip("Plays when it moves to Investigate or another searching state. Optional; defaults to a groan.")]
        [SerializeField] SoundCue searchCue;
        [SerializeField] SoundCue attackCue;
        [SerializeField] SoundCue painCue;
        [Tooltip("Chance a hit makes it cry out.")]
        [SerializeField, Range(0f, 1f)] float painChance = 0.5f;
        [Tooltip("Real seconds before this zombie can cry out in pain again.")]
        [SerializeField, Min(0f)] float painCooldown = 0.8f;
        [SerializeField] SoundCue deathCue;

        [Header("Footsteps")]
        [SerializeField] SoundCue footstepCue;
        [Tooltip("Metres per footstep (a slow shamble takes short steps).")]
        [SerializeField, Min(0.1f)] float stepLength = 0.8f;
        [Tooltip("Slower than this (m/s) counts as standing still.")]
        [SerializeField, Min(0f)] float minStepSpeed = 0.2f;

        readonly StepCadence _steps = new(0.3f);
        readonly CueCooldown _painCooldown = new();
        NavMeshAgent _agent;
        AudioSource _voice;
        MobController.State _state;
        float _groanTimer;
        bool _dead;

        void Awake()
        {
            if (mob == null) mob = GetComponent<MobController>();
            if (health == null) health = GetComponent<Health>();
            _agent = GetComponent<NavMeshAgent>();
            _voice = CreateVoice();
            if (mob != null) _state = mob.CurrentState;
            ScheduleGroan(0.3f);
        }

        void OnEnable()
        {
            if (mob != null)
            {
                mob.StateChanged += OnStateChanged;
                mob.Attacked += OnAttacked;
            }
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
            }
        }

        void OnDisable()
        {
            if (mob != null)
            {
                mob.StateChanged -= OnStateChanged;
                mob.Attacked -= OnAttacked;
            }
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }
        }

        void Update()
        {
            if (_dead) return;
            float dt = Time.deltaTime; // scaled: fast-forward speeds both up
            TickGroan(dt);
            TickFootsteps(dt);
        }

        void TickGroan(float dt)
        {
            if (groanCue == null || _state == MobController.State.Attack) return;
            _groanTimer -= dt;
            if (_groanTimer > 0f) return;
            if (!_voice.isPlaying) Say(groanCue); // don't talk over itself; try again next interval
            ScheduleGroan(1f);
        }

        void TickFootsteps(float dt)
        {
            if (footstepCue == null || _agent == null || !_agent.enabled) return;
            Vector3 v = _agent.velocity;
            v.y = 0f;
            float speed = v.magnitude;
            if (speed < minStepSpeed)
            {
                _steps.Reset();
                return;
            }
            if (_steps.Advance(speed * dt, stepLength) && AudioService.Instance != null)
                AudioService.Instance.PlayAt(footstepCue, transform.position);
        }

        void OnStateChanged(MobController.State next)
        {
            var previous = _state;
            _state = next;
            if (_dead) return;
            switch (ZombieVocalRules.OnStateChanged(previous.ToString(), next.ToString()))
            {
                case ZombieVocal.Alert:
                    Say(alertCue);
                    ScheduleGroan(1f);
                    break;
                case ZombieVocal.Groan:
                    Say(searchCue != null ? searchCue : groanCue);
                    ScheduleGroan(1f);
                    break;
            }
        }

        void OnAttacked()
        {
            if (!_dead) Say(attackCue);
        }

        void OnDamaged(float amount, Vector3 point)
        {
            if (_dead || health == null || health.IsDead) return; // the killing shot gets the death cry instead
            if (Random.value > painChance) return;
            if (!_painCooldown.TryConsume(Time.unscaledTime, painCooldown)) return;
            Say(painCue);
        }

        void OnDied()
        {
            if (_dead) return;
            _dead = true;
            _voice.Stop();
            Say(deathCue, force: true);
        }

        /// <summary>Plays a vocal on this zombie's own voice, replacing whatever it was saying.</summary>
        void Say(SoundCue cue, bool force = false)
        {
            if (cue == null || (_dead && !force)) return;
            if (!cue.TryPick(out var clip, out float volume, out float pitch)) return;
            cue.ApplySettings(_voice, force2D: false);
            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = volume;
            _voice.pitch = pitch;
            _voice.Play();
        }

        void ScheduleGroan(float scale) =>
            _groanTimer = Random.Range(groanInterval.x, groanInterval.y) * Mathf.Max(0.05f, scale);

        AudioSource CreateVoice()
        {
            var go = new GameObject("Voice");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = mouthOffset;
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
