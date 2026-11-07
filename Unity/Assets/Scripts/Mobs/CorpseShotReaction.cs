using System;
using System.Collections.Generic;
using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// Makes dead bodies react to being shot or hit with the axe. The weapons ignore the <c>Corpse</c> layer (bullets
    /// fly over bodies), so this re-casts each shot against corpses only: the first corpse limb the ray meets before
    /// it stopped (its hit point, or its full range on a miss) gets an impulse at that point, along the shot. A frozen
    /// body is woken first (all its bodies dynamic again) and re-frozen once it settles, on its own clock. The bullet
    /// itself still carries on to whatever it hit.
    ///
    /// The axe: at the apex of each swing (<see cref="AxeViewModel.StrikeApex"/>) a short sphere sweep along the view
    /// shoves the nearest corpse limb within reach (and nearer than whatever the blade connected with) and launches
    /// the whole body along the swing (<see cref="ZombieRagdoll.Fling"/>), so it flies a few metres.
    ///
    /// Listens to <see cref="PlayerWeapons.RayFired"/> (every gunshot), <see cref="PlayerWeapons.MeleeHit"/> and the
    /// axe's apex. Without a hub it falls back to every <see cref="Gun"/>'s <see cref="Gun.Fired"/>. The killing
    /// shot is skipped (<see cref="ZombieRagdoll"/> already pushes the body it kills).
    ///
    /// One per scene: creates itself after the first scene loads if none was placed, so it needs no prefab. Place one
    /// (e.g. on GameSystems) to tune it in the Inspector; the bootstrap then does nothing.
    ///
    /// Time: woken bodies simulate with normal (scaled) physics, so fast-forward speeds them up. On a blink skip it
    /// steps physics for them like <see cref="RagdollSkipper"/>, unless a still-falling ragdoll means that one already will.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after ZombieRagdoll, so a freeze it does this frame is seen (and undone) before physics runs
    public sealed class CorpseShotReaction : MonoBehaviour, ISkippable
    {
        const int MaxHits = 32;
        const float SearchInterval = 1f;

        public static CorpseShotReaction Instance { get; private set; }

        [Tooltip("Weapon hub to listen to. If empty, the player's (PlayerWeapons.Instance) is used, searched again if it disappears.")]
        [SerializeField] PlayerWeapons weapons;
        [SerializeField] string corpseLayer = "Corpse";

        [Header("Push (impulse in N·s at the hit point; a limb weighs 4-9 kg)")]
        [SerializeField, Min(0f)] float pistolImpulse = 35f;
        [SerializeField, Min(0f)] float axeImpulse = 80f;
        [Tooltip("Any other weapon.")]
        [SerializeField, Min(0f)] float otherImpulse = 35f;
        [Tooltip("Share of a downward shot's downward part taken out, so shooting a body on the floor moves it instead of pressing it down.")]
        [SerializeField, Range(0f, 1f)] float floorDeflect = 0.75f;
        [Tooltip("Upward share added to the push.")]
        [SerializeField, Range(0f, 1f)] float lift = 0.2f;

        [Header("Axe launch (corpses fly from an axe hit; guns only shove)")]
        [Tooltip("Speed (m/s) an axe hit adds to the whole corpse, flat along the swing, on top of the axe impulse.")]
        [SerializeField, Min(0f)] float axeLaunchSpeed = 4.5f;
        [Tooltip("Upward speed (m/s) an axe hit adds to the whole corpse, so it lifts off the floor instead of dragging.")]
        [SerializeField, Min(0f)] float axeLaunchLift = 2f;

        [Header("Axe sweep")]
        [Tooltip("Radius of the sweep against corpses, metres.")]
        [SerializeField, Min(0.01f)] float axeRadius = 0.35f;
        [Tooltip("Reach when the hub has no axe to read it from.")]
        [SerializeField, Min(0.1f)] float axeReach = 1.8f;

        [Header("Re-freezing (world seconds)")]
        [Tooltip("A woken body is frozen again after this long, even if still moving.")]
        [SerializeField, Min(0.1f)] float settleSeconds = 2.5f;
        [Tooltip("Freeze earlier once every body is slower than this (m/s)...")]
        [SerializeField, Min(0f)] float restSpeed = 0.08f;
        [Tooltip("...for this many world seconds.")]
        [SerializeField, Min(0f)] float restSeconds = 0.4f;
        [Tooltip("Most world seconds of physics simulated for woken bodies on one blink skip.")]
        [SerializeField, Min(0f)] float maxSimulatedSeconds = 0.5f;

        /// <summary>A corpse limb was pushed. Args: the body, world hit point, impulse (N·s, world).</summary>
        public event Action<Rigidbody, Vector3, Vector3> Pushed;

        /// <summary>Bodies woken by a hit and not yet frozen again.</summary>
        public int WokenCount => _woken.Count;

        sealed class Woken
        {
            public ZombieRagdoll Ragdoll;
            public Rigidbody[] Bodies;
            public Vector3[] Linear, Angular;
            public RagdollSettleClock Clock;
            /// <summary>True if the body was already frozen when hit (so we froze it again ourselves).</summary>
            public bool WasFrozen;
        }

        readonly RaycastHit[] _hits = new RaycastHit[MaxHits];
        readonly List<Woken> _woken = new();
        readonly HashSet<ZombieRagdoll> _deadSeen = new();
        readonly List<Gun> _guns = new();
        readonly Dictionary<Gun, Action<bool, RaycastHit>> _gunHandlers = new();
        int _deadSeenFrame = -1;
        int _corpseMask;
        PlayerWeapons _subscribed;
        AxeViewModel _axeView;
        Camera _aimCamera;
        float _nextSearchTime;

        // The axe's apex is handled in LateUpdate, after its own hit check (in any order) has reported what it struck.
        bool _swingPending;
        Ray _swingRay;
        float _swingReach;
        Vector3 _bladeStop;
        int _bladeStopFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || FindAnyObjectByType<CorpseShotReaction>() != null) return;
            var go = new GameObject(nameof(CorpseShotReaction));
            DontDestroyOnLoad(go);
            go.AddComponent<CorpseShotReaction>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _corpseMask = LayerMask.GetMask(corpseLayer);
            if (_corpseMask == 0) Debug.LogWarning($"[CorpseShotReaction] No \"{corpseLayer}\" layer: corpses won't react to hits.", this);
        }

        void OnEnable()
        {
            if (Instance != this) return; // a duplicate, being destroyed
            TimeSkipController.Register(this);
            if (weapons != null) Subscribe(weapons);
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            Unsubscribe();
            UnsubscribeGuns();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindWeapons();
            Settle(Time.deltaTime);
        }

        void LateUpdate()
        {
            if (!_swingPending) return;
            _swingPending = false;
            float reach = _swingReach;
            // The blade stopped where it connected (a living zombie or a wall): no corpse behind that gets hit.
            if (_bladeStopFrame == Time.frameCount)
                reach = Mathf.Min(reach, Vector3.Distance(_swingRay.origin, _bladeStop) + 0.05f);
            SweepAxe(_swingRay, reach);
        }

        public void Skip(float seconds)
        {
            if (_woken.Count == 0 || ZombieRagdoll.ActiveCount > 0) return; // RagdollSkipper steps physics for those
            float step = Time.fixedDeltaTime;
            int steps = RagdollMath.SkipSteps(seconds, maxSimulatedSeconds, step);
            if (steps == 0) return;
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                for (int i = 0; i < steps; i++) Physics.Simulate(step);
            }
            finally
            {
                Physics.simulationMode = mode;
            }
        }

        /// <summary>
        /// Handles one shot ray: pushes the first corpse limb along <paramref name="ray"/> up to where the shot stopped.
        /// Public so other weapons (and editor tests) can feed rays in. Returns the body pushed, or null.
        /// </summary>
        /// <param name="hit">Did the shot hit something (with <paramref name="info"/>)? Otherwise it ran its full <paramref name="range"/>.</param>
        /// <param name="impulse">N·s given to the limb.</param>
        public Rigidbody HandleShot(Ray ray, bool hit, RaycastHit info, float range, float impulse)
        {
            if (_corpseMask == 0 || impulse <= 0f) return null;
            var killed = KilledBy(hit ? info.collider : null);
            // A shot that hits a body freshly turned corpse (it died earlier this frame) still reaches its own limb.
            float reach = hit ? info.distance + 0.05f : range;
            int count = Physics.RaycastNonAlloc(ray, _hits, reach, _corpseMask, QueryTriggerInteraction.Ignore);
            int nearest = NearestCorpseHit(count);
            if (nearest < 0) return null;
            var limb = _hits[nearest];
            var ragdoll = limb.rigidbody.GetComponentInParent<ZombieRagdoll>();
            if (ragdoll == null || ragdoll == killed) return null; // the killing shot: the ragdoll already pushed it
            return Push(ragdoll, limb.rigidbody, limb.point, ray.direction, impulse);
        }

        /// <summary>
        /// Pushes <paramref name="body"/> (a limb of <paramref name="ragdoll"/>) at <paramref name="point"/>, waking the
        /// ragdoll if it was frozen; it freezes again once it settles. Does nothing to a ragdoll that isn't dead.
        /// </summary>
        public Rigidbody Push(ZombieRagdoll ragdoll, Rigidbody body, Vector3 point, Vector3 direction, float impulse)
        {
            if (ragdoll == null || body == null || !ragdoll.IsActive) return null;
            var entry = Track(ragdoll);
            foreach (var b in entry.Bodies)
            {
                if (b == null || !b.isKinematic) continue;
                b.isKinematic = false;
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
            }
            foreach (var b in entry.Bodies)
                if (b != null) b.WakeUp();

            Vector3 push = HitReactionMath.PushDirection(direction, Vector3.up, floorDeflect, lift) * impulse;
            body.AddForceAtPosition(push, point, ForceMode.Impulse);
            Pushed?.Invoke(body, point, push);
            return body;
        }

        /// <summary>
        /// Steps the re-freeze clocks on by <paramref name="deltaTime"/> world seconds (Update does this). Bodies that
        /// have come to rest are frozen again. Public so it can be driven in edit mode.
        /// </summary>
        public void Settle(float deltaTime)
        {
            for (int i = _woken.Count - 1; i >= 0; i--)
            {
                var entry = _woken[i];
                if (entry.Ragdoll == null)
                {
                    _woken.RemoveAt(i); // the corpse was removed
                    continue;
                }
                if (!entry.Ragdoll.IsActive)
                {
                    _woken.RemoveAt(i); // knocked down, then got back up: the Animator owns the bones again
                    continue;
                }

                float fastest = 0f;
                for (int b = 0; b < entry.Bodies.Length; b++)
                {
                    var body = entry.Bodies[b];
                    if (body == null) continue;
                    if (body.isKinematic)
                    {
                        // Its own ragdoll froze it mid-push (its settle time ran out): carry on until ours has.
                        body.isKinematic = false;
                        body.linearVelocity = entry.Linear[b];
                        body.angularVelocity = entry.Angular[b];
                    }
                    entry.Linear[b] = body.linearVelocity;
                    entry.Angular[b] = body.angularVelocity;
                    fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
                }

                if (!entry.Clock.Advance(deltaTime, fastest)) continue;
                if (entry.Ragdoll.IsFlying) continue; // never re-frozen mid-air: wait for its flight to end
                _woken.RemoveAt(i);
                // Freeze it again. A body still falling from its death is left to its ragdoll, which freezes it itself.
                if (entry.WasFrozen || entry.Ragdoll.IsFrozen)
                {
                    entry.Ragdoll.EndFlight(); // continuous collision off before going kinematic
                    foreach (var body in entry.Bodies)
                        if (body != null) body.isKinematic = true;
                }
            }
        }

        Woken Track(ZombieRagdoll ragdoll)
        {
            var clock = new RagdollSettleClock(settleSeconds, restSpeed, restSeconds);
            foreach (var existing in _woken)
            {
                if (existing.Ragdoll != ragdoll) continue;
                existing.Clock = clock; // a new hit: start settling again
                return existing;
            }
            var bodies = new Rigidbody[ragdoll.Bodies.Count];
            for (int i = 0; i < bodies.Length; i++) bodies[i] = ragdoll.Bodies[i];
            var entry = new Woken
            {
                Ragdoll = ragdoll,
                Bodies = bodies,
                Linear = new Vector3[bodies.Length],
                Angular = new Vector3[bodies.Length],
                Clock = clock,
                WasFrozen = ragdoll.IsFrozen,
            };
            _woken.Add(entry);
            return entry;
        }

        /// <summary>
        /// The ragdoll this hit killed, if any. Weapons can't hit corpses, so a weapon hit landing on a dead ragdoll
        /// means it died during this frame's shots; the first report of it is the killing blow.
        /// </summary>
        ZombieRagdoll KilledBy(Collider struck)
        {
            if (_deadSeenFrame != Time.frameCount)
            {
                _deadSeenFrame = Time.frameCount;
                _deadSeen.Clear();
            }
            if (struck == null) return null;
            var ragdoll = struck.GetComponentInParent<ZombieRagdoll>();
            return ragdoll != null && ragdoll.IsActive && _deadSeen.Add(ragdoll) ? ragdoll : null;
        }

        int NearestCorpseHit(int count)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.rigidbody == null) continue;
                if (hit.distance <= 0f && hit.point == Vector3.zero) continue; // started inside it: no point to push at
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = i;
                }
            }
            return best;
        }

        // ---- Axe ----

        void OnStrikeApex()
        {
            var cam = _aimCamera != null ? _aimCamera : Camera.main;
            if (cam == null) return;
            _swingRay = new Ray(cam.transform.position, cam.transform.forward);
            _swingReach = _subscribed != null && _subscribed.Axe != null ? _subscribed.Axe.Reach : axeReach;
            _swingPending = true;
        }

        void OnMeleeHit(MeleeHitInfo hit)
        {
            _bladeStop = hit.Point;
            _bladeStopFrame = Time.frameCount;
            if (hit.Killed) KilledBy(hit.Collider); // the blow that killed it was pushed by its ragdoll
        }

        void SweepAxe(Ray ray, float reach)
        {
            if (_corpseMask == 0 || reach <= 0f) return;
            KilledBy(null); // reset the per-frame record if it's stale
            int count = Physics.SphereCastNonAlloc(ray, axeRadius, _hits, reach, _corpseMask, QueryTriggerInteraction.Ignore);
            int nearest = NearestCorpseHit(count);
            if (nearest < 0) return;
            var limb = _hits[nearest];
            var ragdoll = limb.rigidbody.GetComponentInParent<ZombieRagdoll>();
            if (ragdoll == null || _deadSeen.Contains(ragdoll)) return;
            LaunchCorpse(ragdoll, limb.rigidbody, limb.point, ray.direction);
        }

        /// <summary>
        /// An axe hit on a corpse: <see cref="Push"/> with <see cref="axeImpulse"/> at <paramref name="point"/>, then the
        /// whole body is launched (<see cref="ZombieRagdoll.Fling"/>, the same flight as an axe kill or a knockdown) with
        /// <see cref="axeLaunchSpeed"/> along the swing and <see cref="axeLaunchLift"/> up. It isn't re-frozen until
        /// that flight ends. Public so editor tests can drive it. Returns the body pushed, or null.
        /// </summary>
        public Rigidbody LaunchCorpse(ZombieRagdoll ragdoll, Rigidbody body, Vector3 point, Vector3 direction)
        {
            var pushed = Push(ragdoll, body, point, direction, axeImpulse);
            if (pushed != null) ragdoll.Fling(RagdollMath.LaunchVelocity(direction, Vector3.up, axeLaunchSpeed, axeLaunchLift));
            return pushed;
        }

        // ---- Listening ----

        float ImpulseFor(WeaponKind weapon) => weapon switch
        {
            WeaponKind.Pistol => pistolImpulse,
            WeaponKind.Axe => axeImpulse,
            _ => otherImpulse,
        };

        void OnRay(ShotRay shot) => HandleShot(shot.Ray, shot.Hit, shot.Info, shot.Range, ImpulseFor(shot.Weapon));

        void FindWeapons()
        {
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            if (weapons == null) weapons = PlayerWeapons.Instance;
            if (weapons != null && isActiveAndEnabled)
            {
                UnsubscribeGuns();
                Subscribe(weapons);
                return;
            }
            SubscribeGuns(); // no hub: listen to loose guns instead
        }

        void Subscribe(PlayerWeapons hub)
        {
            if (_subscribed == hub) return;
            Unsubscribe();
            _subscribed = hub;
            hub.RayFired += OnRay;
            hub.MeleeHit += OnMeleeHit;
            _axeView = hub.Axe != null ? hub.Axe.ViewModel : null;
            if (_axeView != null) _axeView.StrikeApex += OnStrikeApex;
            _aimCamera = hub.GetComponentInChildren<Camera>(true);
        }

        void Unsubscribe()
        {
            if (_subscribed != null)
            {
                _subscribed.RayFired -= OnRay;
                _subscribed.MeleeHit -= OnMeleeHit;
            }
            if (_axeView != null) _axeView.StrikeApex -= OnStrikeApex;
            _subscribed = null;
            _axeView = null;
        }

        void SubscribeGuns()
        {
            foreach (var gun in FindObjectsByType<Gun>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (_gunHandlers.ContainsKey(gun)) continue;
                var cam = gun.GetComponentInParent<Camera>();
                Action<bool, RaycastHit> handler = (hit, info) =>
                {
                    var aim = cam != null ? cam : Camera.main;
                    if (aim == null) return;
                    // Gun.Fired is raised in the same frame as its raycast, so the camera's centre ray is the shot's ray.
                    var ray = aim.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    HandleShot(ray, hit, info, gun.Range, pistolImpulse);
                };
                gun.Fired += handler;
                _gunHandlers.Add(gun, handler);
                _guns.Add(gun);
            }
        }

        void UnsubscribeGuns()
        {
            foreach (var gun in _guns)
                if (gun != null && _gunHandlers.TryGetValue(gun, out var handler)) gun.Fired -= handler;
            _guns.Clear();
            _gunHandlers.Clear();
        }
    }
}
