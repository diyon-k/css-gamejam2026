using System;
using System.Collections.Generic;
using Blink.Interaction;
using Blink.Mobs;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace Blink.World
{
    /// <summary>One leaf of a door: the shell's leaf object, swung about a vertical hinge line (all in the leaf's parent space).</summary>
    [Serializable]
    public struct DoorLeafData
    {
        public Transform leaf;
        [Tooltip("Hinge line, parent space; y is the bottom of the leaf (the floor).")]
        public Vector3 hinge;
        [Tooltip("The leaf's modelled local pose (as imported).")]
        public Vector3 basePos;
        public Quaternion baseRot;
        [Tooltip("Pose yaw at angle 0 (closed), relative to the modelled pose.")]
        public float closedYaw;
        [Tooltip("+1 or -1: which way the leaf turns for a positive door angle.")]
        public float sign;
        public float length;
        [Tooltip("Heading (0 = +z, toward +x) of the closed leaf, hinge to free edge.")]
        public float closedHeading;
        [Tooltip("Carves the navmesh while the door is locked, or open and still.")]
        public NavMeshObstacle obstacle;
    }

    /// <summary>
    /// A hinged door (one or two leaves) the player opens and closes with Interact. Lives on a holder object; the leaves
    /// stay in the shell model and carry a <see cref="DoorLeaf"/> that forwards the look-and-press to it, so a re-import of
    /// the FBX keeps the door. The maths and state are <see cref="DoorModel"/>.
    ///
    /// Time: real (unscaled) seconds, like pickups and their glint, so it swings at full speed while eyes-shut fast-forward
    /// runs the world at 3x and while a grab QTE or the wake-up freezes it at 0.
    ///
    /// The player is never swept: a press opens away from them (or the other way if that arc is occupied), a close is
    /// refused while they stand in the arc, and a moving leaf holds still while they are in what is left of its swing.
    ///
    /// Locked doors rattle. One with <see cref="keysRequired"/> takes <see cref="keyItemId"/> keys as the player brings them
    /// (one hand, so one at a time); each is used up in the lock, and the last one unlocks it. 0 keys = a story lock.
    ///
    /// Zombies: they are NavMesh agents (colliders don't stop them), so every leaf is left out of the navmesh bake and
    /// carries a carving <see cref="NavMeshObstacle"/>: on while locked (the doorway is cut), on while open and still (the
    /// open leaf is cut, so they walk round it), off while closed and unlocked (they path through). A zombie whose path
    /// goes through a closed, unlocked door shoves it open as it reaches it; one that a blink skip carried through it
    /// leaves it open behind it.
    /// </summary>
    public sealed class Door : MonoBehaviour
    {
        public string doorId = "D1";
        [Tooltip("Direction (parent space, plan) the door is modelled to swing toward; the 'front' side.")]
        public Vector3 front = Vector3.forward;
        public float openAngle = 90f;
        [Range(0.3f, 0.8f)] public float seconds = 0.5f;
        [Tooltip("Real seconds between the press and the leaf moving: the hand reaching the handle.")]
        [SerializeField, Min(0f)] float handDelay = 0.1f;
        public bool startLocked;
        [Tooltip("0 = closed, 1 = open toward the front at start.")]
        [Range(0, 1)] public int startOpen;
        [Tooltip("Keys that unlock it (0 = never unlocks).")]
        [Min(0)] public int keysRequired;
        public string keyItemId = "key_front";
        [SerializeField] DoorLeafData[] leaves = Array.Empty<DoorLeafData>();

        [Header("Noise (audible range in metres; 0 = silent)")]
        [SerializeField, Min(0f)] float moveLoudness = 6f;
        [SerializeField, Min(0f)] float rattleLoudness = 8f;

        [Header("Zombies")]
        [Tooltip("A zombie whose path goes through the closed door shoves it open within this distance of the doorway.")]
        [SerializeField, Min(0f)] float zombieOpenRange = 1.9f;
        [Tooltip("Zombies this close are watched, so one a blink skip carries through the door leaves it open.")]
        [SerializeField, Min(0f)] float zombieTrackRange = 6f;
        [SerializeField, Min(0.02f)] float zombieScanSeconds = 0.1f;

        [Header("Events")]
        public UnityEvent onUnlocked = new();
        public UnityEvent onOpened = new();

        /// <summary>Fired once when keys (or code) unlock the door. Nothing in the game listens yet: the escape / ending can hook here.</summary>
        public event Action Unlocked;
        /// <summary>Fired each time it starts opening (true if the player did it, false for a zombie or code).</summary>
        public event Action<bool> Opened;
        public event Action Closed;
        public event Action Rattled;
        /// <summary>A key went into the lock: keys used so far, keys needed.</summary>
        public event Action<int, int> KeyUsed;
        /// <summary>Every door: a press by the player and what it did (for rumble and sound).</summary>
        public static event Action<Door, DoorResult> PlayerUsed;

        public DoorModel Model => _model ??= new DoorModel(startLocked, keysRequired, openAngle, seconds);
        public bool IsLocked => Model.Locked;
        public bool IsOpen => Model.IsOpen;
        public int LeafCount => leaves.Length;

        const float PlayerPad = 0.05f;

        DoorModel _model;
        Transform _player;
        float _playerRadius = 0.35f, _nextScan, _nextPlayerSearch;
        bool _carving, _carvingSet;
        TimeSkipController _skips;
        readonly List<(MobController mob, float side)> _tracked = new();
        static readonly Collider[] ZombieHits = new Collider[16];
        static NavMeshPath _path;
        static int _zombieMask = -2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PlayerUsed = null;
            _zombieMask = -2;
        }

        /// <summary>Editor setup: fills the data and poses the leaves in the starting state.</summary>
        public void Configure(string id, Vector3 frontDir, bool locked, bool open, int keys, string keyItem, DoorLeafData[] data)
        {
            doorId = id; front = frontDir; startLocked = locked; startOpen = open ? 1 : 0;
            keysRequired = keys; keyItemId = keyItem; leaves = data;
            SnapToStart();
        }

        public void SnapToStart()
        {
            _model = new DoorModel(startLocked, keysRequired, openAngle, seconds);
            _model.Snap(startOpen == 1 ? openAngle : 0f);
            ApplyPose();
            _carvingSet = false;
            UpdateObstacles();
        }

        /// <summary>Tools and checks: jumps the leaves to <paramref name="angle"/> (the door state follows). <see cref="SnapToStart"/> undoes it.</summary>
        public void PoseAt(float angle)
        {
            Model.Snap(angle);
            ApplyPose();
        }

        void Awake() => SnapToStart();

        void Start()
        {
            _skips = TimeSkipController.Instance;
            if (_skips != null) _skips.Skipped += OnSkipped;
        }

        void OnDestroy()
        {
            if (_skips != null) _skips.Skipped -= OnSkipped;
        }

        void ApplyPose()
        {
            float a = Model.PoseAngle;
            foreach (var l in leaves)
            {
                if (l.leaf == null) continue;
                var q = Quaternion.AngleAxis(l.closedYaw + l.sign * a, Vector3.up);
                l.leaf.SetLocalPositionAndRotation(l.hinge + q * (l.basePos - l.hinge), q * l.baseRot);
            }
        }

        void UpdateObstacles()
        {
            bool carve = Model.Locked || (Model.IsOpen && !Model.Moving);
            if (_carvingSet && carve == _carving) return;
            _carving = carve;
            _carvingSet = true;
            foreach (var l in leaves) if (l.obstacle != null) l.obstacle.enabled = carve;
        }

        // ---- geometry (world space)

        Transform Space(int i) => leaves[i].leaf != null ? leaves[i].leaf.parent : null;
        Vector3 HingeWorld(int i) => Space(i) is { } p ? p.TransformPoint(leaves[i].hinge) : leaves[i].hinge;
        float Yaw0(int i) => Space(i) is { } p ? p.eulerAngles.y : 0f;
        Vector3 FrontWorld => leaves.Length > 0 && Space(0) is { } p ? p.TransformDirection(front) : front;
        static Vector3 FromHeading(float h) => Quaternion.AngleAxis(h, Vector3.up) * Vector3.forward;

        /// <summary>The middle of the doorway at floor level.</summary>
        public Vector3 Centre
        {
            get
            {
                if (leaves.Length == 0) return transform.position;
                var c = Vector3.zero;
                for (int i = 0; i < leaves.Length; i++)
                    c += HingeWorld(i) + FromHeading(Yaw0(i) + leaves[i].closedHeading) * (leaves[i].length * 0.5f);
                return c / leaves.Length;
            }
        }

        /// <summary>The doorway as a plan segment (the closed leaves, jamb to jamb).</summary>
        void Doorway(out Vector2 a, out Vector2 b)
        {
            var c = Centre;
            float half = 0f;
            foreach (var l in leaves) half += l.length * 0.5f;
            var along = FromHeading(Yaw0(0) + leaves[0].closedHeading) * half;
            a = new Vector2(c.x - along.x, c.z - along.z);
            b = new Vector2(c.x + along.x, c.z + along.z);
        }

        float SideOf(Vector3 worldPos) => Vector3.Dot(worldPos - Centre, FrontWorld);
        bool SameStorey(Vector3 worldPos) => Mathf.Abs(worldPos.y - Centre.y) < 1.5f;

        // ---- from DoorLeaf (the Interactor)

        public string Prompt(Interactor who) => Model.Prompt(KeysHeld(who));

        int KeysHeld(Interactor who) => who != null && who.Inventory != null ? who.Inventory.Count(keyItemId) : 0;

        public void Interact(Interactor who)
        {
            if (leaves.Length == 0 || who == null) return;
            var pos = who.transform.position;
            if (who.Player != null && who.Player.TryGetComponent<CharacterController>(out var cc)) _playerRadius = cc.radius;
            var res = Model.Interact(SideOf(pos), KeysHeld(who), t => SweepHitsPlayer(Model.Angle, t, pos), handDelay);
            // Keys turned in the lock stay there: out of the hand for good, so a death can't put them back.
            for (int i = 0; i < Model.LastKeysUsed; i++)
                if (Hand.Of(who) is not { } hand || !hand.UseUp(keyItemId)) who.Inventory?.Remove(keyItemId);
            Raise(res, true);
            PlayerUsed?.Invoke(this, res);
        }

        /// <summary>
        /// Open or close from code (a puzzle, a trigger, a cutscene). Opens away from <paramref name="fromWorld"/> if given
        /// (else toward the front), never through the player. False if it didn't move.
        /// </summary>
        public bool SetOpen(bool open, Vector3? fromWorld = null, bool byPlayer = false)
        {
            var res = open
                ? Model.Open(fromWorld.HasValue ? SideOf(fromWorld.Value) : -1f, PlayerBlocks) ? DoorResult.Opened : DoorResult.None
                : !PlayerBlocks(0f) && Model.Close() ? DoorResult.Closed : DoorResult.None;
            Raise(res, byPlayer);
            return res != DoorResult.None;
        }

        /// <summary>Unlocks from code (e.g. a different key puzzle), with the same events as using the keys.</summary>
        public void Unlock()
        {
            if (!Model.Locked) return;
            Model.Unlock();
            Raise(DoorResult.Unlocked, false);
        }

        void Raise(DoorResult res, bool byPlayer)
        {
            switch (res)
            {
                case DoorResult.Opened:
                    _tracked.Clear();
                    if (byPlayer && moveLoudness > 0f) Noise.Make(Centre, moveLoudness);
                    Opened?.Invoke(byPlayer); onOpened.Invoke();
                    break;
                case DoorResult.Closed:
                    _tracked.Clear();
                    if (byPlayer && moveLoudness > 0f) Noise.Make(Centre, moveLoudness);
                    Closed?.Invoke();
                    break;
                case DoorResult.Rattled:
                    if (rattleLoudness > 0f) Noise.Make(Centre, rattleLoudness);
                    Rattled?.Invoke();
                    break;
                case DoorResult.KeyUsed:
                    KeyUsed?.Invoke(Model.KeysUsed, Model.KeysRequired);
                    break;
                case DoorResult.Unlocked:
                    if (Model.KeysRequired > 0) KeyUsed?.Invoke(Model.KeysUsed, Model.KeysRequired);
                    Unlocked?.Invoke(); onUnlocked.Invoke();
                    break;
            }
            UpdateObstacles();
        }

        // ---- the player must not be swept

        bool PlayerBlocks(float target) => FindPlayer() && SweepHitsPlayer(Model.Angle, target, _player.position);

        /// <summary>Would turning the leaves from <paramref name="fromAngle"/> to <paramref name="toAngle"/> touch the player?</summary>
        bool SweepHitsPlayer(float fromAngle, float toAngle, Vector3 playerWorld)
        {
            if (!SameStorey(playerWorld)) return false;
            if (fromAngle * toAngle < 0f) // through closed: check each half, so neither is mistaken for the long way round
                return SweepHitsPlayer(fromAngle, 0f, playerWorld) || SweepHitsPlayer(0f, toAngle, playerWorld);
            var p = new Vector2(playerWorld.x, playerWorld.z);
            for (int i = 0; i < leaves.Length; i++)
            {
                var l = leaves[i];
                if (l.leaf == null) continue;
                var h = HingeWorld(i);
                float h0 = Yaw0(i) + l.closedHeading;
                if (DoorModel.SweepHits(new Vector2(h.x, h.z), l.length + PlayerPad, h0 + l.sign * fromAngle,
                        h0 + l.sign * toAngle, p, _playerRadius + PlayerPad)) return true;
            }
            return false;
        }

        void Update()
        {
            if (Model.Animating)
            {
                // Someone stepped into what's left of the swing: hold it until they clear it.
                bool held = Model.Moving && PlayerBlocks(Model.Target);
                if (!held)
                {
                    Model.Step(Time.unscaledDeltaTime);
                    ApplyPose();
                }
            }
            UpdateObstacles();

            if (!Model.Locked && !Model.IsOpen && !Model.Moving && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + zombieScanSeconds;
                ScanZombies();
            }
        }

        bool FindPlayer()
        {
            if (_player != null) return true;
            if (Time.unscaledTime < _nextPlayerSearch) return false;
            _nextPlayerSearch = Time.unscaledTime + 1f;
            var who = FindAnyObjectByType<Interactor>();
            if (who == null) return false;
            _player = who.transform;
            if (who.TryGetComponent<CharacterController>(out var cc)) _playerRadius = cc.radius;
            return true;
        }

        // ---- zombies

        /// <summary>Watches nearby zombies; one heading through the doorway shoves it open.</summary>
        void ScanZombies()
        {
            _tracked.Clear();
            if (leaves.Length == 0 || zombieTrackRange <= 0f) return;
            if (_zombieMask == -2) _zombieMask = LayerMask.GetMask("Zombie");
            if (_zombieMask == 0) return;
            var centre = Centre;
            int n = Physics.OverlapSphereNonAlloc(centre + Vector3.up * 0.9f, Mathf.Max(zombieTrackRange, zombieOpenRange),
                ZombieHits, _zombieMask, QueryTriggerInteraction.Ignore);
            MobController shover = null;
            for (int i = 0; i < n; i++)
            {
                var mob = ZombieHits[i].GetComponentInParent<MobController>();
                if (mob == null || mob.CurrentState is MobController.State.Dead || Tracked(mob)) continue;
                var pos = mob.transform.position;
                if (!SameStorey(pos)) continue;
                _tracked.Add((mob, SideOf(pos)));
                if (shover == null && mob.CurrentState is not (MobController.State.Down or MobController.State.Idle)
                    && Flat(pos - centre) <= zombieOpenRange && HeadingThrough(mob)) shover = mob;
            }
            if (shover != null) ShoveOpen(SideOf(shover.transform.position), snap: false);
        }

        bool Tracked(MobController mob)
        {
            foreach (var t in _tracked) if (t.mob == mob) return true;
            return false;
        }

        /// <summary>Does the zombie's path cross the doorway in its next few metres?</summary>
        bool HeadingThrough(MobController mob)
        {
            if (!mob.TryGetComponent<NavMeshAgent>(out var agent) || !agent.isOnNavMesh || !agent.hasPath) return false;
            _path ??= new NavMeshPath();
            if (!agent.CalculatePath(agent.destination, _path)) return false;
            var corners = _path.corners;
            Doorway(out var a, out var b);
            float left = zombieOpenRange + 1.5f;
            for (int i = 1; i < corners.Length && left > 0f; i++)
            {
                var p = new Vector2(corners[i - 1].x, corners[i - 1].z);
                var q = new Vector2(corners[i].x, corners[i].z);
                if (SegmentsCross(p, q, a, b)) return true;
                left -= (q - p).magnitude;
            }
            return false;
        }

        /// <summary>After a blink skip: a watched zombie now on the other side went through, so it's left open.</summary>
        void OnSkipped(float seconds)
        {
            if (Model.Locked || Model.IsOpen) return;
            var centre = Centre;
            float? crossedFrom = null;
            foreach (var (mob, side) in _tracked)
            {
                if (mob == null || !mob.isActiveAndEnabled) continue;
                var pos = mob.transform.position;
                if (!SameStorey(pos) || Flat(pos - centre) > zombieTrackRange) continue;
                if (Mathf.Sign(SideOf(pos)) != Mathf.Sign(side)) { crossedFrom = side; break; }
            }
            _tracked.Clear();
            if (crossedFrom.HasValue) ShoveOpen(crossedFrom.Value, snap: true);
        }

        void ShoveOpen(float side, bool snap)
        {
            if (!Model.TryPickOpenTarget(side, PlayerBlocks, out float target)) return;
            if (snap)
            {
                Model.Snap(target);
                ApplyPose();
            }
            else Model.Open(side, PlayerBlocks);
            Raise(DoorResult.Opened, false);
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static bool SegmentsCross(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
        {
            static float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
            float d1 = Cross(a, b, p), d2 = Cross(a, b, q), d3 = Cross(p, q, a), d4 = Cross(p, q, b);
            return d1 * d2 <= 0f && d3 * d4 <= 0f && !(d1 == 0f && d2 == 0f);
        }
    }
}
