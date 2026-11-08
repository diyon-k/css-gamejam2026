using System;
using Blink.Combat;
using Blink.Player;
using Blink.Puzzles;
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
    /// - You hide (<see cref="Interaction.HidingSpot"/>) => blind to you, searches where it last saw you (the spot itself
    ///   if it watched you get in). It never pulls you out, grabs or hurts you while you're inside.
    /// - You're in a puzzle room (<see cref="PuzzleRooms"/>) => blind and deaf to you there; a chase gives up and patrols.
    /// - Search over => back to the nearest waypoint. No patrol route => stands guard (Idle).
    /// Attacks in range, dies when its Health runs out.
    /// On a blink skip it jumps along its current navmesh path by speed x seconds x skipDistanceMultiplier,
    /// keeps moving without a hitch, and can land a hit if it was already in range. Hitting the player while their eyes are shut interrupts fast-forward.
    /// Reaching you because you blinked grabs you instead (<see cref="GrabQte"/>): while the world is frozen this zombie
    /// slams in to <see cref="grabDistance"/> in front of you and lunges to bite on GrabQte's beat, on real time.
    /// Each ring you miss is a bite for <see cref="attackDamage"/>; break free and it's flung back a few metres
    /// (<see cref="grabEscapeThrow"/>) and lies stunned (<see cref="KnockDown"/>) before getting up to come for you again.
    /// On death it goes limp as a <see cref="ZombieRagdoll"/> if it has one (otherwise it plays the
    /// Animator's "Dead" clip, or falls over procedurally) and the body is removed after
    /// <see cref="corpseLifetime"/> world seconds, which skips also count towards.
    ///
    /// Animation is optional: if the Animator has a float "Speed", a trigger "Attack" or a bool
    /// "Dead", they're driven automatically.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class MobController : MonoBehaviour, ISkippable, IGrabber
    {
        /// <summary>Down = knocked flying by a melee prop: limp and stunned, then gets back up (see <see cref="KnockDown"/>).</summary>
        public enum State { Idle, Patrol, Investigate, Chase, Attack, Dead, Down }

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
        [Tooltip("Room patrol: world seconds (random between x and y) it lurks in a room's far corner, watching the doorway.")]
        [SerializeField] Vector2 lurkSeconds = new Vector2(6f, 12f);

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
        [Header("Grab (real seconds; the rest of the world is frozen)")]
        [Tooltip("Metres from the player it's pulled to when it grabs you: right in your face.")]
        [SerializeField] float grabDistance = 0.7f;
        [Tooltip("Seconds to slam in from where it was to grabDistance.")]
        [SerializeField, Min(0.01f)] float grabSlamSeconds = 0.12f;
        [Tooltip("Metres it lunges at your face on each bite.")]
        [SerializeField] float biteLunge = 0.22f;
        [Tooltip("Metres a good press shoves it back for a moment.")]
        [SerializeField] float shoveDistance = 0.3f;
        [Tooltip("Speed (m/s) its head and shoulders are shoved back, away from you, when you break free. Lower limbs get " +
                 "less (down to grabEscapeFeetShare at the feet), so it topples over backwards.")]
        [SerializeField, Min(0f)] float grabEscapeThrow = 3.5f;
        [Tooltip("Upward speed (m/s) added to that shove (scaled the same way), so its feet come off the floor.")]
        [SerializeField, Min(0f)] float grabEscapeLift = 1f;
        [Tooltip("Share of the shove the lowest limbs get (0 = feet stay planted, 1 = the whole body flies as one).")]
        [SerializeField, Range(0f, 1f)] float grabEscapeFeetShare = 0.3f;
        [Tooltip("World seconds it lies stunned after being flung off, before getting back up.")]
        [SerializeField, Min(0f)] float grabEscapeStun = 5f;
        [Tooltip("Metres above the head bone the camera looks at, for rigs without eye bones (with them it looks between the eyes).")]
        [SerializeField] float faceOffset = 0.08f;

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
        static readonly int AttackStateFull = Animator.StringToHash("Base Layer.Attack");
        static readonly int AttackStateShort = Animator.StringToHash("Attack");

        // Grab hold: GrabQte has frozen the world; this zombie is moved by hand on real time.
        bool _grabbing;
        float _grabClock, _biteAt, _shoveAt;
        Vector3 _grabFrom, _grabAnchor;
        Quaternion _grabFromRotation, _grabRotation;
        AnimatorUpdateMode _savedUpdateMode;
        Transform _head, _leftEye, _rightEye;

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
        // Room patrol (wins over the waypoint loop when set): wanders to a random reachable room, one no other mob is heading for.
        Bounds[] _rooms;
        int _room = -1;
        bool _claimed;
        NavMeshPath _roomPath;
        Vector3 _lurkWatch; // the doorway it came in by, which it faces while lurking
        /// <summary>Rooms mobs are heading for; a list, so two claims on one room (when nothing else is free) release cleanly.</summary>
        static readonly System.Collections.Generic.List<int> ClaimedRooms = new();
        bool _seesTarget;
        float _senseTimer;
        float _lostTimer;
        Vector3 _lastSeen;
        /// <summary>The hiding spot it watched you climb into, or null.</summary>
        Interaction.HidingSpot _hidingIn;
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

        // Knockdown: world seconds left stunned, and where it stood when hit (fallback get-up spot).
        float _downTimer;
        Vector3 _downFrom;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _agent.speed = moveSpeed;
            _agent.stoppingDistance = attackRange * 0.8f;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (ragdoll == null) ragdoll = GetComponent<ZombieRagdoll>();
            CacheAnimatorParameters();
            if (animator != null && animator.isHuman)
            {
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                _rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }

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
            ReleaseRoom();
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

        /// <summary>For spawned mobs, before Awake (the prefab can't reference scene objects).</summary>
        public void SetPatrolRoute(Transform route) => patrolRoute = route;

        /// <summary>Patrol by wandering between these rooms (world bounds, min y = floor) instead of a waypoint loop.</summary>
        public void SetPatrolRooms(Bounds[] rooms) => _rooms = rooms;

        /// <summary>Back to where it started, calm (used when the player respawns). Corpses stay put.</summary>
        public void ResetToSpawn()
        {
            if (CurrentState == State.Down) StandUp();
            if (CurrentState == State.Dead || !_agent.enabled) return;
            _agent.Warp(_spawnPos);
            transform.rotation = _spawnRot;
            if (_hasAttackParam) animator.ResetTrigger(AttackParam); // no leftover swing at home
            if (_agent.isOnNavMesh) StartPatrol();
        }

        void Update()
        {
            if (_grabbing)
            {
                TickGrab(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
                return;
            }
            if (CurrentState == State.Dead)
            {
                AdvanceDeath(Time.deltaTime);
                return;
            }
            if (CurrentState == State.Down)
            {
                TickDown(Time.deltaTime);
                return;
            }
            if (!EnsureOnNavMesh()) return;

            float dt = Time.deltaTime;
            _attackCooldown -= dt;
            bool targetAlive = TargetAlive();

            var hide = Interaction.HidingSpot.Occupant;
            // Mid-chase it saw you duck in, even if its sight check didn't tick that frame. Lose it for loseSightTime first.
            bool onYourTail = _seesTarget || (CurrentState is (State.Chase or State.Attack) && _lostTimer > 0f);
            _hidingIn = KnownHidingSpot(hide, _hidingIn, onYourTail && targetAlive);
            bool inPuzzleRoom = targetAlive && PuzzleRooms.Contains(target.position); // clowns don't follow you in
            if (!targetAlive) _seesTarget = false;
            else if (hide != null || Interaction.Vent.Inside != null || inPuzzleRoom)
            {
                // Hidden, in a vent or in a puzzle room: it can't get you. Watched you get in => it searches right at the spot.
                if (_hidingIn != null) _lastSeen = _hidingIn.ExitPoint;
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

            if (tracking && DistanceToPrey() <= attackRange)
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
                    _agent.SetDestination(PreyPosition);
                    _repathTimer = repathInterval;
                }
            }
            else if (hunting)
            {
                if (targetAlive && !inPuzzleRoom) Investigate(_lastSeen);
                else StartPatrol(); // killed you, or you're in a puzzle room: wander off
            }
            else if (CurrentState is State.Investigate or State.Patrol)
            {
                TickWander(dt);
            }

            // While chasing, animate at the intended pace: the measured velocity drops to zero for a
            // moment after a skip warps the agent, which would flick the Animator back to Idle.
            // Once it has run out of path (you're somewhere it can't reach) it stands instead of running on the spot.
            if (_hasSpeedParam)
            {
                bool running = CurrentState == State.Chase &&
                               (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance);
                animator.SetFloat(SpeedParam, running ? moveSpeed : _agent.velocity.magnitude);
            }
        }

        /// <summary>Walk to the patrol waypoint / investigated spot, then pause or search there.</summary>
        void TickWander(float dt)
        {
            if (!_agent.isStopped)
            {
                if (_agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance + 0.2f) return;
                _agent.isStopped = true;
                _waitTimer = CurrentState == State.Investigate ? searchTime
                           : _rooms is { Length: > 0 } ? UnityEngine.Random.Range(lurkSeconds.x, lurkSeconds.y)
                           : patrolWait;
            }

            if (CurrentState == State.Investigate) transform.Rotate(0f, searchTurnSpeed * dt, 0f);
            else if (_rooms is { Length: > 0 }) FacePoint(_lurkWatch, dt); // in its corner, staring at the doorway
            if ((_waitTimer -= dt) > 0f) return;

            if (CurrentState == State.Investigate) StartPatrol();
            else if (_rooms is { Length: > 0 }) { if (!GoToRandomRoom()) _waitTimer = patrolWait; } // nowhere reachable: try again later
            else GoToWaypoint((_waypointIndex + 1) % _waypoints.Length);
        }

        void StartPatrol()
        {
            if (_rooms is { Length: > 0 })
            {
                SetState(State.Patrol);
                if (GoToRandomRoom()) return;
                _agent.isStopped = true; // TickWander waits patrolWait, then tries again
                _waitTimer = patrolWait;
                return;
            }
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
            GoTo(_waypoints[index]);
        }

        /// <summary>
        /// Heads for the far corner of a random room it can walk to (a locked door cuts the navmesh, so rooms behind one
        /// drop out). Skips the room it's in and rooms other mobs are heading for, unless nothing else is reachable.
        /// </summary>
        bool GoToRandomRoom()
        {
            ReleaseRoom();
            int last = _room;
            var failed = new System.Collections.Generic.HashSet<int>();
            for (int pass = 0; pass < 2; pass++) // second pass allows rooms another mob has claimed
            {
                while (true)
                {
                    int room = PickRoom(_rooms.Length, i => i != last && !failed.Contains(i) && (pass == 1 || !ClaimedRooms.Contains(i)),
                                        UnityEngine.Random.Range);
                    if (room < 0) break;
                    if (!TryLurkCorner(_rooms[room], out var point, out _lurkWatch)) { failed.Add(room); continue; }
                    _room = room;
                    ClaimedRooms.Add(room);
                    _claimed = true;
                    GoTo(point);
                    return true;
                }
            }
            return false;
        }

        /// <summary>A random index in [0, count) that <paramref name="allowed"/> accepts, or -1 if none. random(min, maxExclusive).</summary>
        public static int PickRoom(int count, Func<int, bool> allowed, Func<int, int, int> random)
        {
            var options = new System.Collections.Generic.List<int>();
            for (int i = 0; i < count; i++) if (allowed(i)) options.Add(i);
            return options.Count == 0 ? -1 : options[random(0, options.Count)];
        }

        /// <summary>
        /// Where to lurk in the room: the corner farthest from where its path comes in (the doorway it'll use), with a
        /// complete path to it. Also gives that entry point, so it can stand there watching the door.
        /// </summary>
        bool TryLurkCorner(Bounds room, out Vector3 point, out Vector3 entry)
        {
            point = entry = default;
            _roomPath ??= new NavMeshPath();
            if (!TryRoomFloor(room, room.center, 2.5f, out var middle)) return false;
            if (!NavMesh.CalculatePath(transform.position, middle, _agent.areaMask, _roomPath) || _roomPath.status != NavMeshPathStatus.PathComplete) return false;
            entry = PathEntry(_roomPath.corners, room);
            foreach (var corner in FarthestCorners(room, entry, 0.7f))
            {
                if (!TryRoomFloor(room, corner, 1.2f, out var p)) continue; // furniture in that corner: next one
                if (!NavMesh.CalculatePath(transform.position, p, _agent.areaMask, _roomPath) || _roomPath.status != NavMeshPathStatus.PathComplete) continue;
                point = p;
                return true;
            }
            point = middle; // every corner blocked: the middle of the room will do
            return true;
        }

        /// <summary>Nearest navmesh point to <paramref name="guess"/> (at floor height) that lands inside this room, not through a wall or a storey down.</summary>
        bool TryRoomFloor(Bounds room, Vector3 guess, float radius, out Vector3 point)
        {
            guess.y = room.min.y;
            point = NavMesh.SamplePosition(guess, out var floor, radius, _agent.areaMask) ? floor.position : default;
            return point != default && InRoom(room, point);
        }

        static bool InRoom(Bounds room, Vector3 p) =>
            p.x >= room.min.x && p.x <= room.max.x && p.z >= room.min.z && p.z <= room.max.z && Mathf.Abs(p.y - room.min.y) <= 0.5f;

        /// <summary>Where a path first crosses into the room (its start if it begins inside).</summary>
        static Vector3 PathEntry(Vector3[] corners, Bounds room)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                if (!InRoom(room, corners[i])) continue;
                if (i == 0) return corners[0];
                Vector3 outside = corners[i - 1], inside = corners[i];
                for (int step = 0; step < 8; step++) // home in on the wall crossing
                {
                    var mid = (outside + inside) * 0.5f;
                    if (InRoom(room, mid)) inside = mid; else outside = mid;
                }
                return inside;
            }
            return corners.Length > 0 ? corners[^1] : room.center;
        }

        /// <summary>The room's four floor corners, <paramref name="inset"/> in from the walls, farthest from <paramref name="from"/> first.</summary>
        public static Vector3[] FarthestCorners(Bounds room, Vector3 from, float inset)
        {
            float x0 = room.min.x + inset, x1 = room.max.x - inset, z0 = room.min.z + inset, z1 = room.max.z - inset, y = room.min.y;
            var corners = new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1) };
            Vector2 f = new Vector2(from.x, from.z);
            Array.Sort(corners, (a, b) => Vector2.Distance(f, new Vector2(b.x, b.z)).CompareTo(Vector2.Distance(f, new Vector2(a.x, a.z))));
            return corners;
        }

        /// <summary>Drops its claim (once). _room stays as "the room it was last in" so the next pick skips it.</summary>
        void ReleaseRoom()
        {
            if (!_claimed) return;
            ClaimedRooms.Remove(_room);
            _claimed = false;
        }

        void GoTo(Vector3 point)
        {
            _agent.speed = patrolSpeed;
            _agent.isStopped = false;
            _agent.SetDestination(point);
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
            if (PuzzleRooms.Contains(position)) return; // noise from in a puzzle room doesn't lure them in
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
            if (CurrentState == State.Down)
            {
                _downTimer -= seconds; // the fall itself is stepped by RagdollSkipper
                return;
            }
            if (!_agent.isOnNavMesh) return;

            _attackCooldown -= seconds;
            _lostTimer -= seconds;
            AdvanceAnimation(seconds);
            bool hunting = CurrentState is State.Chase or State.Attack;
            if (hunting && !TargetAlive()) return;

            if (hunting && DistanceToPrey() <= attackRange)
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

            Vector3 destination = hunting ? PreyPosition : _agent.destination;
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
            if (hunting && _attackCooldown <= 0f && DistanceToPrey() <= attackRange) Attack(grabbed: true);
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
            if (Interaction.HidingSpot.Occupant != null) return; // hidden: no grab, no damage, no pulling you out

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

            if (grabbed && GrabQte.TryBegin(this))
            {
                StartGrabHold();
                return;
            }
            _targetHealth?.TakeDamage(attackDamage, target.position + Vector3.up, toTarget.normalized);
            Attacked?.Invoke();
        }

        // ---- Grab: in your face, biting, until GrabQte resolves ----

        Vector3 IGrabber.GrabFacePoint =>
            _leftEye != null && _rightEye != null ? (_leftEye.position + _rightEye.position) * 0.5f
            : _head != null ? _head.position + Vector3.up * faceOffset
            : transform.position + Vector3.up * eyeHeight;

        void IGrabber.GrabBite(bool hurts)
        {
            _biteAt = _grabClock;
            if (hurts && target != null)
                _targetHealth?.TakeDamage(attackDamage, target.position + Vector3.up, (target.position - transform.position).normalized);
            if (animator == null || !animator.isActiveAndEnabled) return;
            // Restart the attack clip on every bite, whatever transitions the controller has.
            if (animator.HasState(0, AttackStateFull)) animator.CrossFadeInFixedTime(AttackStateFull, 0.08f, 0, 0f);
            else if (animator.HasState(0, AttackStateShort)) animator.CrossFadeInFixedTime(AttackStateShort, 0.08f, 0, 0f);
            else if (_hasAttackParam) animator.SetTrigger(AttackParam);
        }

        void IGrabber.GrabShoved() => _shoveAt = _grabClock;

        // Pushed over backwards as a ragdoll (the chest harder than the feet, no spin); it gets up after the stun.
        void IGrabber.GrabThrown(Vector3 away)
        {
            EndGrabHold();
            if (CurrentState == State.Dead || !GoDown(grabEscapeStun)) return;
            Vector3 back = Vector3.ProjectOnPlane(away, Vector3.up);
            back = back.sqrMagnitude > 1e-4f ? back.normalized : _grabRotation * Vector3.back;
            ragdoll.Shove(back * grabEscapeThrow + Vector3.up * grabEscapeLift, grabEscapeFeetShare);
        }

        void IGrabber.GrabResolved(bool escaped)
        {
            EndGrabHold();
            Attacked?.Invoke();
        }

        /// <summary>
        /// Sends it flying as a ragdoll (a blunt melee prop): every limb gets <paramref name="velocity"/> and the one
        /// nearest <paramref name="point"/> spins. A living zombie then lies stunned for at least
        /// <paramref name="stunSeconds"/> of world time and gets back up; a corpse just flies. Needs a ZombieRagdoll.
        /// </summary>
        public void KnockDown(Vector3 velocity, Vector3 point, float stunSeconds)
        {
            if (GoDown(stunSeconds)) ragdoll.Launch(velocity, point);
        }

        /// <summary>Into <see cref="State.Down"/> (if alive) for at least <paramref name="stunSeconds"/>. False without a ragdoll.</summary>
        bool GoDown(float stunSeconds)
        {
            if (ragdoll == null) return false;
            if (CurrentState is not (State.Dead or State.Down))
            {
                _downFrom = transform.position;
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false; // the root stays put; the bones fly
                foreach (var col in GetComponents<Collider>()) col.enabled = false;
                SetState(State.Down);
            }
            if (CurrentState == State.Down) _downTimer = Mathf.Max(_downTimer, stunSeconds);
            return true;
        }

        void TickDown(float dt)
        {
            _downTimer -= dt;
            // Up once the stun is over and it has stopped tumbling (3 s more at most).
            if (_downTimer > 0f || (ragdoll.FastestSpeed > 0.6f && _downTimer > -3f)) return;
            StandUp();
        }

        /// <summary>Back on its feet where the body landed (the nearest navmesh), facing you, and straight back after you.</summary>
        void StandUp()
        {
            Vector3 hips = ragdoll.HipsPosition;
            ragdoll.GetUp(); // captures the lying pose before the root moves under it
            Vector3 spot = NavMesh.SamplePosition(hips, out var floor, 3f, _agent.areaMask) ? floor.position : _downFrom;
            Vector3 face = target != null ? target.position - spot : transform.forward;
            face.y = 0f;
            transform.SetPositionAndRotation(spot, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : transform.rotation);
            _agent.enabled = true;
            _agent.Warp(spot);
            foreach (var col in GetComponents<Collider>()) col.enabled = true;
            SetState(State.Idle);
            if (!_agent.isOnNavMesh) return;
            if (TargetAlive()) Investigate(target.position); // it knows who hit it
            else StartPatrol();
        }

        /// <summary>Takes the body off the agent and slams it in front of the player's face, facing them.</summary>
        void StartGrabHold()
        {
            _grabbing = true;
            _grabClock = 0f;
            _biteAt = _shoveAt = float.NegativeInfinity;

            _agent.isStopped = true;
            _agent.updatePosition = false;
            _agent.updateRotation = false;

            Vector3 away = transform.position - target.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = target.forward;
            away.Normalize();
            _grabFrom = transform.position;
            _grabFromRotation = transform.rotation;
            _grabAnchor = target.position + away * grabDistance;
            _grabAnchor.y = transform.position.y;
            _grabRotation = Quaternion.LookRotation(-away);

            if (animator != null)
            {
                // The world is frozen at timeScale 0; this one keeps moving.
                _savedUpdateMode = animator.updateMode;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                if (_hasSpeedParam) animator.SetFloat(SpeedParam, 0f);
            }
        }

        void TickGrab(float dt)
        {
            _grabClock += dt;
            float slam = Smooth01(_grabClock / grabSlamSeconds);
            float lunge = BiteCurve(_grabClock - _biteAt);
            float shove = ShoveCurve(_grabClock - _shoveAt);

            Vector3 toPlayer = _grabRotation * Vector3.forward;
            Vector3 position = Vector3.LerpUnclamped(_grabFrom, _grabAnchor, slam)
                               + toPlayer * (biteLunge * lunge - shoveDistance * shove);
            transform.SetPositionAndRotation(position, Quaternion.Slerp(_grabFromRotation, _grabRotation, slam));
        }

        /// <summary>Hands the body back to the agent where the grab left it. Safe to call twice.</summary>
        void EndGrabHold()
        {
            if (!_grabbing) return;
            _grabbing = false;
            if (animator != null) animator.updateMode = _savedUpdateMode;
            // Leave the body where the grab left it (no snap back to the anchor) and bring the agent to it.
            if (_agent.enabled) _agent.Warp(transform.position);
            _agent.updatePosition = true;
            _agent.updateRotation = true;
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>0 → 1 → 0: a quick lunge in (0.12 s), a slower pull back (0.33 s).</summary>
        static float BiteCurve(float t) =>
            t < 0f ? 0f : t < 0.12f ? Smooth01(t / 0.12f) : 1f - Smooth01((t - 0.12f) / 0.33f);

        /// <summary>0 → 1 → 0: knocked back fast (0.06 s), leans back in (0.3 s).</summary>
        static float ShoveCurve(float t) =>
            t < 0f ? 0f : t < 0.06f ? Smooth01(t / 0.06f) : 1f - Smooth01((t - 0.06f) / 0.3f);

        void OnDied()
        {
            EndGrabHold(); // let the ragdoll take over from where it stands
            ReleaseRoom();
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

        void FaceTarget(float deltaTime) => FacePoint(target.position, deltaTime);

        void FacePoint(Vector3 point, float deltaTime)
        {
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;
            var goal = Quaternion.LookRotation(flat);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, turnSpeed * deltaTime);
        }

        /// <summary>
        /// The hiding spot it knows you're in: the one it was watching you as you got into (its sight is frozen while
        /// you hide, so <paramref name="sawYou"/> stays true), else none. Cleared once you're out.
        /// </summary>
        public static Interaction.HidingSpot KnownHidingSpot(Interaction.HidingSpot occupant, Interaction.HidingSpot known, bool sawYou) =>
            occupant != null && (sawYou || known == occupant) ? occupant : null;

        /// <summary>Where it heads for you: the front of the hiding spot it saw you get into, else you.</summary>
        Vector3 PreyPosition => _hidingIn != null ? _hidingIn.ExitPoint : target.position;

        float DistanceToPrey()
        {
            if (_hidingIn == null) return FlatDistanceToTarget();
            Vector3 delta = _hidingIn.ExitPoint - transform.position;
            delta.y = 0f;
            return delta.magnitude;
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
