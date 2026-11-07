using System;
using Blink.Combat;
using Blink.Player;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.AI;

namespace Blink.Mobs
{
    /// <summary>
    /// Zombie brain: chase the player on the navmesh, attack in range, die when its Health runs out.
    /// On a blink skip it jumps along its navmesh path by speed x seconds (and can land a hit if it
    /// was already in range). Hitting the player while their eyes are shut interrupts fast-forward.
    ///
    /// Animation is optional: if the Animator has a float "Speed", a trigger "Attack" or a bool
    /// "Dead", they're driven automatically.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class MobController : MonoBehaviour, ISkippable
    {
        public enum State { Idle, Chase, Attack, Dead }

        [Tooltip("Defaults to the PlayerController in the scene.")]
        [SerializeField] Transform target;

        [Header("Movement")]
        [SerializeField] float moveSpeed = 1.6f;
        [SerializeField] float turnSpeed = 360f;
        [SerializeField] float detectionRange = 40f;
        [SerializeField] float repathInterval = 0.25f;

        [Header("Attack")]
        [SerializeField] float attackRange = 1.6f;
        [SerializeField] float attackDamage = 25f;
        [Tooltip("World seconds between attacks.")]
        [SerializeField] float attackInterval = 1.4f;
        [Tooltip("World seconds between reaching the player and the first hit.")]
        [SerializeField] float attackWindup = 0.5f;

        [Header("Optional")]
        [SerializeField] Animator animator;

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
        NavMeshPath _skipPath;
        float _attackCooldown;
        float _repathTimer;
        bool _hasSpeedParam, _hasAttackParam, _hasDeadParam;
        bool _warnedOffMesh;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _agent.speed = moveSpeed;
            _agent.stoppingDistance = attackRange * 0.8f;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();
        }

        void OnEnable()
        {
            TimeSkipController.Register(this);
            if (_health != null) _health.Died += OnDied;
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            if (_health != null) _health.Died -= OnDied;
        }

        void Start()
        {
            if (target == null && FindAnyObjectByType<PlayerController>() is { } player) target = player.transform;
            if (target != null) _targetHealth = target.GetComponentInParent<IDamageable>();
        }

        void Update()
        {
            if (CurrentState == State.Dead) return;
            if (!EnsureOnNavMesh()) return;

            _attackCooldown -= Time.deltaTime;

            if (target == null || (_targetHealth != null && _targetHealth.IsDead))
            {
                SetState(State.Idle);
                _agent.isStopped = true;
            }
            else
            {
                float distance = FlatDistanceToTarget();
                if (distance <= attackRange)
                {
                    SetState(State.Attack);
                    _agent.isStopped = true;
                    FaceTarget(Time.deltaTime);
                    if (_attackCooldown <= 0f) Attack();
                }
                else if (distance <= detectionRange)
                {
                    SetState(State.Chase);
                    _agent.isStopped = false;
                    _repathTimer -= Time.deltaTime;
                    if (_repathTimer <= 0f)
                    {
                        _agent.SetDestination(target.position);
                        _repathTimer = repathInterval;
                    }
                }
                else
                {
                    SetState(State.Idle);
                    _agent.isStopped = true;
                }
            }

            if (_hasSpeedParam) animator.SetFloat(SpeedParam, _agent.velocity.magnitude);
        }

        public void Skip(float seconds)
        {
            if (CurrentState == State.Dead || target == null || !_agent.isOnNavMesh) return;
            if (_targetHealth != null && _targetHealth.IsDead) return;

            _attackCooldown -= seconds;
            float distance = FlatDistanceToTarget();

            if (distance <= attackRange)
            {
                if (_attackCooldown <= 0f) Attack();
                return;
            }
            if (distance > detectionRange) return;

            _skipPath ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, target.position, _agent.areaMask, _skipPath)) return;
            if (_skipPath.status == NavMeshPathStatus.PathInvalid) return;

            var corners = _skipPath.corners;
            float travel = Mathf.Min(moveSpeed * seconds, NavPathUtility.PathLength(corners) - attackRange * 0.9f);
            if (travel <= 0f) return;

            _agent.Warp(NavPathUtility.AdvanceAlongPath(corners, travel));
            FaceTarget(1f);
            _agent.SetDestination(target.position);
            _repathTimer = repathInterval;
        }

        void Attack()
        {
            _attackCooldown = attackInterval;
            if (_hasAttackParam) animator.SetTrigger(AttackParam);

            Vector3 toTarget = target.position - transform.position;
            _targetHealth?.TakeDamage(attackDamage, target.position + Vector3.up, toTarget.normalized);

            // A zombie reaching you while your eyes are shut snaps time back to normal.
            var timeSkip = TimeSkipController.Instance;
            if (timeSkip != null && timeSkip.IsFastForwarding) timeSkip.InterruptFastForward();

            Attacked?.Invoke();
        }

        void OnDied()
        {
            SetState(State.Dead);
            if (_agent.isOnNavMesh) _agent.isStopped = true;
            _agent.enabled = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            if (_hasDeadParam) animator.SetBool(DeadParam, true);
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
