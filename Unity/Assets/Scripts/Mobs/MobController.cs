using System;
using Blink.Combat;
using Blink.Player;
using Blink.TimeSkip;
using Blink.World;
using UnityEngine;
using UnityEngine.AI;

namespace Blink.Mobs
{
    /// <summary>
    /// Zombie brain. Patrols a loop of waypoints until it notices you:
    /// - Sees you (in range, inside its view cone, nothing in the way, or you're touching distance) => Chase.
    /// - Loses sight for <see cref="loseSightTime"/> => walks to where it last saw you and searches.
    /// - Hears a <see cref="Noise"/> within range (walking distance, so walls muffle it) while it can't
    ///   see you => walks over to the noise and searches. Noise can lure it off a chase.
    /// - Search over => back to the nearest waypoint. No patrol route => stands guard (Idle).
    /// Attacks in range, dies when its Health runs out.
    /// On a blink skip it jumps along its current navmesh path by speed x seconds x skipDistanceMultiplier,
    /// keeps moving without a hitch, and can land a hit if it was already in range. Hitting the player while their eyes are shut interrupts fast-forward.
    /// On death it goes limp as a <see cref="ZombieRagdoll"/> if it has one (otherwise it plays the
    /// Animator's "Dead" clip, or falls over procedurally) and the body is removed after
    /// <see cref="corpseLifetime"/> world seconds, which skips also count towards.
    ///
    /// Animation is optional: if the Animator has a float "Speed", a trigger "Attack" or a bool
    /// "Dead", they're driven automatically.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class MobController : MonoBehaviour, ISkippable
    {
        public enum State { Idle, Patrol, Investigate, Chase, Attack, Dead }

        [Tooltip("Defaults to the PlayerController in the scene.")]
        [SerializeField] Transform target;

        [Header("Movement")]
        [SerializeField] float moveSpeed = 1.6f;
        [SerializeField] float turnSpeed = 360f;
        [SerializeField] float repathInterval = 0.25f;
        [Tooltip("How much further than its normal pace a zombie lurches on a blink skip (2 = twice as far).")]
        [SerializeField, Min(0f)] float skipDistanceMultiplier = 2f;

        [Header("Patrol")]
        [Tooltip("Its children are the waypoints, walked in order and looped. Positions are read once at start, " +
                 "so the route can sit anywhere (even under the zombie). Empty = stand guard.")]
        [SerializeField] Transform patrolRoute;
        [SerializeField] float patrolSpeed = 0.8f;
        [Tooltip("World seconds to pause at each waypoint.")]
        [SerializeField, Min(0f)] float patrolWait = 2f;

        [Header("Senses")]
        [SerializeField] float sightRange = 15f;
        [Tooltip("Full view-cone angle in degrees.")]
        [SerializeField, Range(1f, 360f)] float fieldOfView = 120f;
        [Tooltip("Notices you this close whatever direction it faces.")]
        [SerializeField] float touchRange = 2f;
        [SerializeField] float eyeHeight = 1.6f;
        [Tooltip("What blocks its sight.")]
        [SerializeField] LayerMask sightBlockers = ~0;
        [Tooltip("World seconds it keeps tracking you after losing sight, before going to search.")]
        [SerializeField, Min(0f)] float loseSightTime = 3f;
        [Tooltip("Multiplies every noise's audible range for this zombie (2 = hears twice as far).")]
        [SerializeField, Min(0f)] float hearing = 1f;
        [Tooltip("World seconds spent looking around where it heard a noise or lost you.")]
        [SerializeField, Min(0f)] float searchTime = 3f;
        [Tooltip("Degrees per second it turns while searching.")]
        [SerializeField] float searchTurnSpeed = 90f;

        [Header("Attack")]
        [SerializeField] float attackRange = 1.6f;
        [SerializeField] float attackDamage = 25f;
        [Tooltip("World seconds between attacks.")]
        [SerializeField] float attackInterval = 1.4f;
        [Tooltip("World seconds between reaching the player and the first hit.")]
        [SerializeField] float attackWindup = 0.5f;
        [Tooltip("Metres the zombie is shoved back when you win the grab QTE.")]
        [SerializeField] float grabKnockback = 2.5f;

        [Header("Death")]
        [Tooltip("World seconds the body stays before it's removed (blinking speeds this up). 0 = keep forever.")]
        [SerializeField, Min(0f)] float corpseLifetime = 6f;
        [Tooltip("Seconds to fall over when there's no Animator \"Dead\" parameter to play a death clip.")]
        [SerializeField, Min(0.01f)] float toppleDuration = 0.45f;

        [Header("Optional")]
        [SerializeField] Animator animator;
        [Tooltip("Defaults to the ZombieRagdoll on this object. Without one, death plays the \"Dead\" clip or topples.")]
        [SerializeField] ZombieRagdoll ragdoll;

        public event Action<State> StateChanged;
        public event Action Attacked;

        public State CurrentState { get; private set; } = State.Idle;
        public Transform Target => target;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int AttackParam = Animator.StringToHash("Attack");
        static readonly int DeadParam = Animator.StringToHash("Dead");

        NavMeshAgent _agent;
        Health _health;
        IDamageable _targetHealth;
        Collider _targetCollider;
        NavMeshPath _skipPath, _noisePath;
        float _attackCooldown;
        float _repathTimer;
        bool _hasSpeedParam, _hasAttackParam, _hasDeadParam;
        bool _warnedOffMesh;

        // Senses and patrol.
        Vector3[] _waypoints = Array.Empty<Vector3>();
        int _waypointIndex;
        bool _seesTarget;
        float _senseTimer;
        float _lostTimer;
        Vector3 _lastSeen;
        /// <summary>Pause at a waypoint, or search time at an investigated spot. Counts while the agent is stopped.</summary>
        float _waitTimer;

        // Death: procedural fall-over (placeholder until there's a death clip) and corpse removal.
        WorldTimer _corpseTimer;
        bool _toppling;
        float _toppleElapsed;
        Quaternion _toppleFrom, _toppleTo;
        Vector3 _toppleFromPos, _toppleToPos;
        Vector3 _spawnPos;
        Quaternion _spawnRot;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _agent.speed = moveSpeed;
            _agent.stoppingDistance = attackRange * 0.8f;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (ragdoll == null) ragdoll = GetComponent<ZombieRagdoll>();
            CacheAnimatorParameters();

            if (patrolRoute != null)
            {
                _waypoints = new Vector3[patrolRoute.childCount];
                for (int i = 0; i < _waypoints.Length; i++) _waypoints[i] = patrolRoute.GetChild(i).position;
            }
        }

        void OnEnable()
        {
            TimeSkipController.Register(this);
            Noise.Made += OnNoise;
            if (_health != null) _health.Died += OnDied;
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            Noise.Made -= OnNoise;
            if (_health != null) _health.Died -= OnDied;
        }

        void Start()
        {
            if (target == null && FindAnyObjectByType<PlayerController>() is { } player) target = player.transform;
            if (target != null)
            {
                _targetHealth = target.GetComponentInParent<IDamageable>();
                _targetCollider = target.GetComponentInChildren<Collider>();
            }
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
            if (_agent.isOnNavMesh) StartPatrol();
        }

        /// <summary>Back to where it started, calm (used when the player respawns). Corpses stay put.</summary>
        public void ResetToSpawn()
        {
            if (CurrentState == State.Dead || !_agent.enabled) return;
            _agent.Warp(_spawnPos);
            transform.rotation = _spawnRot;
            if (_agent.isOnNavMesh) StartPatrol();
        }

        void Update()
        {
            if (CurrentState == State.Dead)
            {
                AdvanceDeath(Time.deltaTime);
                return;
            }
            if (!EnsureOnNavMesh()) return;

            float dt = Time.deltaTime;
            _attackCooldown -= dt;
            bool targetAlive = TargetAlive();

            if (!targetAlive) _seesTarget = false;
            else if (Interaction.HidingSpot.Occupant != null)
            {
                _seesTarget = false;
                _lostTimer = 0f; // stop homing on you: go search where it last saw you instead
            }
            else if ((_senseTimer -= dt) <= 0f)
            {
                _seesTarget = CanSeeTarget();
                _senseTimer = repathInterval;
            }

            if (_seesTarget)
            {
                _lastSeen = target.position;
                _lostTimer = loseSightTime;
            }
            else _lostTimer -= dt;

            bool hunting = CurrentState is State.Chase or State.Attack;
            bool tracking = targetAlive && (_seesTarget || (hunting && _lostTimer > 0f));

            if (tracking && FlatDistanceToTarget() <= attackRange)
            {
                SetState(State.Attack);
                _agent.isStopped = true;
                FaceTarget(dt);
                if (_attackCooldown <= 0f) Attack();
            }
            else if (tracking)
            {
                if (CurrentState != State.Chase) _repathTimer = 0f;
                SetState(State.Chase);
                _agent.speed = moveSpeed;
                _agent.isStopped = false;
                if ((_repathTimer -= dt) <= 0f)
                {
                    _agent.SetDestination(target.position);
                    _repathTimer = repathInterval;
                }
            }
            else if (hunting)
            {
                if (targetAlive) Investigate(_lastSeen);
                else StartPatrol(); // killed you: wander off
            }
            else if (CurrentState is State.Investigate or State.Patrol)
            {
                TickWander(dt);
            }

            // While chasing, animate at the intended pace: the measured velocity drops to zero for a
            // moment after a skip warps the agent, which would flick the Animator back to Idle.
            if (_hasSpeedParam)
                animator.SetFloat(SpeedParam, CurrentState == State.Chase ? moveSpeed : _agent.velocity.magnitude);
        }

        /// <summary>Walk to the patrol waypoint / investigated spot, then pause or search there.</summary>
        void TickWander(float dt)
        {
            if (!_agent.isStopped)
            {
                if (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance + 0.2f) return;
                _agent.isStopped = true;
                _waitTimer = CurrentState == State.Investigate ? searchTime : patrolWait;
            }

            if (CurrentState == State.Investigate) transform.Rotate(0f, searchTurnSpeed * dt, 0f);
            if ((_waitTimer -= dt) > 0f) return;

            if (CurrentState == State.Investigate) StartPatrol();
            else GoToWaypoint((_waypointIndex + 1) % _waypoints.Length);
        }

        void StartPatrol()
        {
            if (_waypoints.Length == 0)
            {
                SetState(State.Idle);
                _agent.isStopped = true;
                return;
            }
            int nearest = 0;
            for (int i = 1; i < _waypoints.Length; i++)
                if ((_waypoints[i] - transform.position).sqrMagnitude < (_waypoints[nearest] - transform.position).sqrMagnitude)
                    nearest = i;
            SetState(State.Patrol);
            GoToWaypoint(nearest);
        }

        void GoToWaypoint(int index)
        {
            _waypointIndex = index;
            _agent.speed = patrolSpeed;
            _agent.isStopped = false;
            _agent.SetDestination(_waypoints[index]);
        }

        void Investigate(Vector3 point)
        {
            SetState(State.Investigate);
            _agent.speed = moveSpeed;
            _agent.isStopped = false;
            _agent.SetDestination(point);
        }

        void OnNoise(Vector3 position, float loudness)
        {
            if (CurrentState is State.Dead or State.Attack || _seesTarget || !_agent.isOnNavMesh) return;
            // Cheap straight-line reject first; walking distance is never shorter.
            if (!Noise.CanHear(loudness, Vector3.Distance(transform.position, position), hearing)) return;
            // Noises happen in the air (a gun at hand height): snap to the floor before pathing.
            if (!NavMesh.SamplePosition(position, out var floor, 2.5f, _agent.areaMask)) return;
            _noisePath ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, floor.position, _agent.areaMask, _noisePath)) return;
            if (_noisePath.status != NavMeshPathStatus.PathComplete) return;
            if (!Noise.CanHear(loudness, NavPathUtility.PathLength(_noisePath.corners), hearing)) return;
            Investigate(floor.position);
        }

        bool CanSeeTarget()
        {
            if (FlatDistanceToTarget() <= touchRange) return true;

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 aim = _targetCollider != null ? _targetCollider.bounds.center : target.position + Vector3.up;
            Vector3 toTarget = aim - eye;
            float distance = toTarget.magnitude;
            if (distance > sightRange) return false;

            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (Vector3.Angle(transform.forward, flat) > fieldOfView * 0.5f) return false;
            return !Physics.Raycast(eye, toTarget / distance, out var hit, distance, sightBlockers, QueryTriggerInteraction.Ignore)
                   || hit.collider.transform.IsChildOf(target);
        }

        bool TargetAlive() => target != null && (_targetHealth == null || !_targetHealth.IsDead);

        public void Skip(float seconds)
        {
            if (CurrentState == State.Dead)
            {
                AdvanceAnimation(seconds);
                AdvanceDeath(seconds);
                return;
            }
            if (!_agent.isOnNavMesh) return;

            _attackCooldown -= seconds;
            _lostTimer -= seconds;
            AdvanceAnimation(seconds);
            bool hunting = CurrentState is State.Chase or State.Attack;
            if (hunting && !TargetAlive()) return;

            if (hunting && FlatDistanceToTarget() <= attackRange)
            {
                if (_attackCooldown <= 0f) Attack(grabbed: true);
                return;
            }
            if (CurrentState == State.Idle) return;
            if (!hunting && _agent.isStopped)
            {
                _waitTimer -= seconds; // pausing at a waypoint or searching: Update moves on when it runs out
                return;
            }

            Vector3 destination = hunting ? target.position : _agent.destination;
            _skipPath ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, destination, _agent.areaMask, _skipPath)) return;
            if (_skipPath.status == NavMeshPathStatus.PathInvalid) return;

            var corners = _skipPath.corners;
            float speed = hunting ? moveSpeed : _agent.speed;
            float stopShort = hunting ? attackRange * 0.9f : 0f;
            float travel = Mathf.Min(speed * seconds * skipDistanceMultiplier,
                                     NavPathUtility.PathLength(corners) - stopShort);
            if (travel <= 0f) return;

            Vector3 landing = NavPathUtility.AdvanceAlongPath(corners, travel);
            Vector3 heading = NavPathUtility.AdvanceAlongPath(corners, travel + 0.5f) - landing;
            heading.y = 0f;

            _agent.Warp(landing);
            if (heading.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(heading);
            _agent.SetDestination(destination);
            // Warp zeroes the agent's velocity; keep the momentum so it doesn't restart from a standstill.
            _agent.velocity = heading.normalized * speed;
            _repathTimer = repathInterval;

            // Closed the gap during your blink: it grabs you as your eyes open.
            if (hunting && _attackCooldown <= 0f && FlatDistanceToTarget() <= attackRange) Attack(grabbed: true);
        }

        /// <summary>Moves the animation on by the skipped time so the pose jumps with the body.</summary>
        void AdvanceAnimation(float seconds)
        {
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
                animator.Update(seconds);
        }

        /// <param name="grabbed">Reached you because you blinked: starts the grab QTE instead of plain damage.</param>
        void Attack(bool grabbed = false)
        {
            if (GrabQte.Active) return; // world is frozen mid-QTE; another zombie already has you

            // A zombie reaching you while your eyes are shut snaps time back to normal.
            var timeSkip = TimeSkipController.Instance;
            if (timeSkip != null && timeSkip.IsFastForwarding)
            {
                timeSkip.InterruptFastForward();
                grabbed = true;
            }

            _attackCooldown = attackInterval;
            if (_hasAttackParam) animator.SetTrigger(AttackParam);
            Vector3 toTarget = target.position - transform.position;

            if (grabbed && GrabQte.TryBegin(OnGrabResolved)) return;
            _targetHealth?.TakeDamage(attackDamage, target.position + Vector3.up, toTarget.normalized);
            Attacked?.Invoke();
        }

        void OnGrabResolved(bool escaped)
        {
            if (this == null || target == null) return;
            Vector3 away = transform.position - target.position;
            away.y = 0f;
            if (escaped)
            {
                // Shove it back so the same zombie doesn't re-grab on your next blink.
                if (_agent.isOnNavMesh) _agent.Move(away.normalized * grabKnockback);
            }
            else
            {
                _targetHealth?.TakeDamage(float.MaxValue, target.position + Vector3.up, -away.normalized);
            }
            Attacked?.Invoke();
        }

        void OnDied()
        {
            SetState(State.Dead);
            // Read before the agent is switched off, which zeroes it: the body keeps its running speed.
            Vector3 velocity = _agent.enabled ? _agent.velocity : Vector3.zero;
            if (_agent.isOnNavMesh) _agent.isStopped = true;
            _agent.enabled = false;
            foreach (var col in GetComponents<Collider>()) col.enabled = false;

            if (ragdoll != null)
            {
                // Physics owns the bones from here; the root must not move or rotate again.
                ragdoll.Activate(velocity);
            }
            else
            {
                foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
                if (_hasDeadParam) animator.SetBool(DeadParam, true);
                else StartTopple();
            }

            if (corpseLifetime > 0f)
            {
                _corpseTimer = new WorldTimer(corpseLifetime);
                _corpseTimer.Completed += () => Destroy(gameObject);
                _corpseTimer.Start();
            }
        }

        /// <summary>No death clip yet: tip over backwards around the feet, away from the player.</summary>
        void StartTopple()
        {
            if (target != null) FaceTarget(360f);
            _toppling = true;
            _toppleElapsed = 0f;
            _toppleFrom = transform.rotation;
            _toppleTo = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            // Lift a little so the lying body sits on the floor instead of half inside it.
            _toppleFromPos = transform.position;
            _toppleToPos = transform.position + Vector3.up * 0.2f;
        }

        void AdvanceDeath(float seconds)
        {
            if (_toppling)
            {
                _toppleElapsed += seconds;
                float t = Mathf.Clamp01(_toppleElapsed / toppleDuration);
                float eased = t * t; // accelerates like falling
                transform.SetPositionAndRotation(
                    Vector3.Lerp(_toppleFromPos, _toppleToPos, eased),
                    Quaternion.Slerp(_toppleFrom, _toppleTo, eased));
                if (t >= 1f) _toppling = false;
            }
            _corpseTimer?.Advance(seconds);
        }

        void SetState(State next)
        {
            if (next == CurrentState) return;
            if (next == State.Attack) _attackCooldown = Mathf.Max(_attackCooldown, attackWindup);
            CurrentState = next;
            StateChanged?.Invoke(next);
        }

        void FaceTarget(float deltaTime)
        {
            Vector3 flat = target.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;
            var goal = Quaternion.LookRotation(flat);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, turnSpeed * deltaTime);
        }

        float FlatDistanceToTarget()
        {
            Vector3 delta = target.position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Gizmos.DrawLine(eye, eye + Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * transform.forward * sightRange);
            Gizmos.DrawLine(eye, eye + Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * transform.forward * sightRange);
            Gizmos.DrawWireSphere(transform.position, touchRange);
            if (patrolRoute == null) return;
            Gizmos.color = Color.cyan;
            int n = patrolRoute.childCount;
            for (int i = 0; i < n; i++)
                Gizmos.DrawLine(patrolRoute.GetChild(i).position, patrolRoute.GetChild((i + 1) % n).position);
        }

        bool EnsureOnNavMesh()
        {
            if (_agent.isOnNavMesh) return true;
            if (!_warnedOffMesh)
            {
                Debug.LogWarning($"[Mob] {name} is not on a navmesh. Add a NavMeshSurface to the level and bake it.", this);
                _warnedOffMesh = true;
            }
            return false;
        }

        void CacheAnimatorParameters()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == SpeedParam && p.type == AnimatorControllerParameterType.Float) _hasSpeedParam = true;
                if (p.nameHash == AttackParam && p.type == AnimatorControllerParameterType.Trigger) _hasAttackParam = true;
                if (p.nameHash == DeadParam && p.type == AnimatorControllerParameterType.Bool) _hasDeadParam = true;
            }
        }
    }
}
