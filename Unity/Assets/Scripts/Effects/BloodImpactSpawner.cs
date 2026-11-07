using System.Collections.Generic;
using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.Pool;

namespace Blink.Effects
{
    /// <summary>
    /// Bursts blood out of anything the player's weapons damage: a dark spray off the entry wound (bent back towards
    /// the shooter), an optional smaller spray out of the back, and an optional splat on the floor below. Listens to
    /// <see cref="PlayerWeapons.RayFired"/> (every pistol shot) and <see cref="PlayerWeapons.MeleeHit"/>
    /// (the axe), so it can live anywhere (it sits on the Effects prefab); if no hub is assigned it finds the player's
    /// at runtime.
    /// Headshots (a head <see cref="Hitbox"/>) get a much stronger burst: bigger and denser sprays, extra bursts fanned
    /// around the first, a bigger exit spray and always a floor splat (<see cref="HeadshotBlood"/>, <see cref="BloodBurst"/>).
    /// Effects come from pools that are filled at startup. A full pool grows (up to 4x, for stacked headshot bursts) before
    /// it starts recycling the oldest effect.
    /// Blood is part of world time. Particles run on scaled time, so eyes-shut fast-forward speeds them up for free,
    /// and a blink skip (<see cref="ISkippable"/>) fast-forwards every live effect so droplets don't hang in mid-air
    /// while the zombie jumps ahead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BloodImpactSpawner : MonoBehaviour, ISkippable
    {
        const float SearchInterval = 1f;
        const int PoolGrowth = 4;

        /// <summary>Layers the floor probe always ignores (bodies and the player), if they exist.</summary>
        static readonly string[] BodyLayers = { "Zombie", "ZombieHitbox", "Corpse", "Player" };

        [Tooltip("Weapon hub to listen to. If empty, the player's (PlayerWeapons.Instance) is used, searched again if it disappears.")]
        [SerializeField] PlayerWeapons weapons;

        [Header("Entry spray")]
        [Tooltip("BloodImpact prefab: root particle system with Stop Action = Callback, a Splat child and an optional Droplets child.")]
        [SerializeField] GameObject impactPrefab;
        [Tooltip("How far the spray bends from the surface normal back towards the shooter (0 = along the normal).")]
        [SerializeField, Range(0f, 1f)] float towardShooter = 0.4f;
        [Tooltip("Upward tilt of the droplet cone, so drops arc before they fall.")]
        [SerializeField, Min(0f)] float dropletLift = 0.25f;
        [Tooltip("Spawn this far off the surface, in metres, so the quads don't clip into the body.")]
        [SerializeField, Min(0f)] float surfaceOffset = 0.01f;
        [Tooltip("Random scale range applied to each burst.")]
        [SerializeField] Vector2 scaleRange = new(0.85f, 1.15f);

        [Header("Exit spray")]
        [SerializeField] bool exitSpray = true;
        [Tooltip("Exit burst size relative to the entry burst.")]
        [SerializeField, Range(0f, 2f)] float exitScale = 0.7f;
        [Tooltip("Deepest collider (in metres) the bullet is treated as passing through.")]
        [SerializeField, Min(0.05f)] float maxExitDepth = 1.2f;

        [Header("Floor splat")]
        [Tooltip("BloodFloorSplat prefab. Leave empty to turn floor splats off.")]
        [SerializeField] GameObject floorPrefab;
        [Tooltip("Chance a hit leaves a splat on the floor.")]
        [SerializeField, Range(0f, 1f)] float floorChance = 0.6f;
        [Tooltip("Floor layers. Zombie, ZombieHitbox, Corpse and Player are always excluded.")]
        [SerializeField] LayerMask floorMask = ~0;
        [Tooltip("Furthest drop below the hit point that still gets a splat, in metres.")]
        [SerializeField, Min(0.1f)] float maxFloorDrop = 2.5f;
        [Tooltip("Splat lands this far past the hit, along the shot, in metres (blood carries on with the bullet).")]
        [SerializeField, Min(0f)] float floorCarry = 0.3f;
        [SerializeField] Vector2 floorScaleRange = new(0.8f, 1.2f);

        [Header("Headshots")]
        [Tooltip("How much stronger the blood is when a hit lands on a head hitbox.")]
        [SerializeField] HeadshotBlood headshotBlood = HeadshotBlood.Default;

        [Header("Pool & time skips")]
        [SerializeField, Min(1)] int impactPoolSize = 8;
        [SerializeField, Min(1)] int floorPoolSize = 6;
        [Tooltip("Longest single step when fast-forwarding through a blink, so droplets don't tunnel through the floor.")]
        [SerializeField, Min(0.01f)] float maxSkipStep = 0.1f;

        EffectPool _impacts;
        EffectPool _floor;
        PlayerWeapons _subscribed;
        float _nextSearchTime;
        int _floorLayers;

        void Awake()
        {
            _floorLayers = floorMask & ~LayerMask.GetMask(BodyLayers);
            var root = new GameObject("BloodPool").transform;
            root.SetParent(transform, false);
            if (impactPrefab != null) _impacts = new EffectPool(impactPrefab, impactPoolSize, impactPoolSize * PoolGrowth, root);
            if (floorPrefab != null) _floor = new EffectPool(floorPrefab, floorPoolSize, floorPoolSize * PoolGrowth, root);
        }

        void Start() => FindWeapons();

        void OnEnable()
        {
            TimeSkipController.Register(this);
            if (weapons != null) Subscribe(weapons);
        }

        void OnDisable()
        {
            TimeSkipController.Unregister(this);
            Unsubscribe();
        }

        void OnDestroy()
        {
            _impacts?.Dispose();
            _floor?.Dispose();
        }

        void Update()
        {
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindWeapons();
            // Safety net for the stop callback (e.g. an effect that ended while paused).
            _impacts?.ReleaseFinished();
            _floor?.ReleaseFinished();
        }

        /// <summary>Fast-forwards every live blood effect by <paramref name="seconds"/> of world time.</summary>
        public void Skip(float seconds)
        {
            _impacts?.Advance(seconds, maxSkipStep);
            _floor?.Advance(seconds, maxSkipStep);
        }

        /// <summary>
        /// Spawns blood for a bullet that hit <paramref name="info"/> travelling along <paramref name="rayDirection"/>,
        /// headshot-strength if it hit a head hitbox. Public so other weapons (or editor tests) can use it; doesn't
        /// check that the target is damageable.
        /// </summary>
        public void SpawnHit(RaycastHit info, Vector3 rayDirection) =>
            SpawnAt(info.point, info.normal, info.collider, rayDirection, Hitbox.IsHeadCollider(info.collider));

        /// <summary>
        /// Spawns blood at a wound: <paramref name="point"/> on <paramref name="collider"/> with surface
        /// <paramref name="normal"/>, from a blow travelling along <paramref name="direction"/>.
        /// <paramref name="headshot"/> picks the stronger burst.
        /// </summary>
        public void SpawnAt(Vector3 point, Vector3 normal, Collider collider, Vector3 direction, bool headshot)
        {
            if (collider == null) return;
            Vector3 rayDir = direction.sqrMagnitude > 1e-6f ? direction.normalized : -normal;
            var burst = BloodBurst.For(headshot, exitSpray ? exitScale : 0f, floorChance, headshot ? headshotBlood : default);

            if (_impacts != null)
            {
                Vector3 spray = BloodSpray.SprayDirection(normal, rayDir, towardShooter);
                Vector3 at = point + normal * surfaceOffset;
                _impacts.Spawn(at, BloodSpray.FacingRotation(spray), burst.Scale * RandomScale(scaleRange),
                    BloodSpray.Lift(spray, dropletLift), burst.CountMultiplier);

                for (int i = 0; i < burst.ExtraBursts; i++) // headshot: fan more bursts around the first
                {
                    Vector3 axis = Vector3.Cross(spray, Random.onUnitSphere);
                    if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(spray, Vector3.up);
                    Vector3 fan = Quaternion.AngleAxis(burst.ExtraBurstSpread * Random.Range(0.5f, 1f), axis.normalized) * spray;
                    _impacts.Spawn(at, BloodSpray.FacingRotation(fan), 0.8f * burst.Scale * RandomScale(scaleRange),
                        BloodSpray.Lift(fan, dropletLift), burst.CountMultiplier);
                }

                if (burst.ExitScale > 0f) SpawnExit(point, collider, rayDir, burst);
            }

            if (_floor != null && Random.value < burst.FloorChance) SpawnFloor(point, rayDir, burst.Scale);
        }

        void SpawnExit(Vector3 point, Collider collider, Vector3 rayDir, BloodBurst burst)
        {
            // Cast back along the shot from beyond the collider to find where the bullet would come out.
            var back = new Ray(point + rayDir * maxExitDepth, -rayDir);
            if (!collider.Raycast(back, out var exit, maxExitDepth)) return;
            // The quads are single-sided, so face them at the shooter: the burst shows round the body's silhouette.
            _impacts.Spawn(exit.point + rayDir * surfaceOffset, BloodSpray.FacingRotation(-rayDir),
                burst.ExitScale * RandomScale(scaleRange), BloodSpray.Lift(rayDir, dropletLift), burst.CountMultiplier);
        }

        void SpawnFloor(Vector3 point, Vector3 rayDir, float scale)
        {
            Vector3 carry = Vector3.ProjectOnPlane(rayDir, Vector3.up) * floorCarry;
            if (!Physics.Raycast(point + carry, Vector3.down, out var floor, maxFloorDrop, _floorLayers,
                    QueryTriggerInteraction.Ignore)) return;
            _floor.Spawn(floor.point + floor.normal * surfaceOffset, BloodSpray.DirectionalRotation(floor.normal, rayDir),
                scale * RandomScale(floorScaleRange), Vector3.zero);
        }

        void OnRayFired(ShotRay shot)
        {
            if (!shot.Hit || shot.Info.collider == null || shot.Info.collider.GetComponentInParent<IDamageable>() == null) return;
            SpawnAt(shot.Info.point, shot.Info.normal, shot.Info.collider, shot.Ray.direction, shot.IsHead);
        }

        void OnMeleeHit(MeleeHitInfo hit)
        {
            if (hit.Target == null) return; // a wall: no blood
            SpawnAt(hit.Point, hit.Normal, hit.Collider, hit.Direction, hit.IsHead);
        }

        static float RandomScale(Vector2 range) => Random.Range(range.x, range.y);

        void FindWeapons()
        {
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            if (weapons == null) weapons = PlayerWeapons.Instance != null ? PlayerWeapons.Instance : FindAnyObjectByType<PlayerWeapons>();
            if (weapons != null && isActiveAndEnabled) Subscribe(weapons);
        }

        void Subscribe(PlayerWeapons target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _subscribed.RayFired += OnRayFired;
            _subscribed.MeleeHit += OnMeleeHit;
        }

        void Unsubscribe()
        {
            if (_subscribed != null)
            {
                _subscribed.RayFired -= OnRayFired;
                _subscribed.MeleeHit -= OnMeleeHit;
            }
            _subscribed = null;
        }

        /// <summary>
        /// Pool of one effect prefab. Filled up front with <c>capacity</c> instances; when every instance is in use it
        /// grows one at a time up to <c>maxCapacity</c> (headshots spawn several at once), then recycles the oldest.
        /// </summary>
        sealed class EffectPool
        {
            readonly ObjectPool<BloodImpactEffect> _pool;
            readonly List<BloodImpactEffect> _active = new();
            readonly int _maxCapacity;
            readonly GameObject _prefab;
            readonly Transform _parent;
            int _capacity;

            public EffectPool(GameObject prefab, int capacity, int maxCapacity, Transform parent)
            {
                _prefab = prefab;
                _parent = parent;
                _capacity = capacity;
                _maxCapacity = Mathf.Max(capacity, maxCapacity);
                _pool = new ObjectPool<BloodImpactEffect>(Create, OnGet, OnRelease, OnDestroyEffect,
                    collectionCheck: false, defaultCapacity: capacity, maxSize: _maxCapacity);

                var warm = new BloodImpactEffect[capacity];
                for (int i = 0; i < capacity; i++) warm[i] = _pool.Get();
                foreach (var effect in warm) _pool.Release(effect);
            }

            public void Spawn(Vector3 position, Quaternion rotation, float scale, Vector3 dropletDirection,
                float countMultiplier = 1f)
            {
                if (_active.Count >= _capacity)
                {
                    if (_capacity < _maxCapacity) _capacity++;
                    else Release(_active[0]);
                }
                var effect = _pool.Get();
                effect.InUse = true;
                _active.Add(effect);
                effect.Play(position, rotation, scale, dropletDirection, countMultiplier);
            }

            public void Advance(float seconds, float maxStep)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var effect = _active[i];
                    if (!effect.Advance(seconds, maxStep)) Release(effect);
                }
            }

            public void ReleaseFinished()
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_active[i].IsFinished) Release(_active[i]);
            }

            public void Dispose()
            {
                _active.Clear();
                _pool.Clear();
            }

            void Release(BloodImpactEffect effect)
            {
                if (!effect.InUse) return;
                effect.InUse = false;
                _active.Remove(effect);
                _pool.Release(effect);
            }

            BloodImpactEffect Create()
            {
                var go = Object.Instantiate(_prefab, _parent);
                go.name = _prefab.name;
                if (!go.TryGetComponent(out BloodImpactEffect effect)) effect = go.AddComponent<BloodImpactEffect>();
                effect.Stopped += OnStopped;
                go.SetActive(false);
                return effect;
            }

            void OnStopped(BloodImpactEffect effect)
            {
                // A recycled effect may report the end of its previous run after it was replayed; ignore that.
                if (effect.InUse && effect.IsFinished) Release(effect);
            }

            static void OnGet(BloodImpactEffect effect) => effect.gameObject.SetActive(true);

            static void OnRelease(BloodImpactEffect effect)
            {
                effect.Clear();
                effect.gameObject.SetActive(false);
            }

            static void OnDestroyEffect(BloodImpactEffect effect)
            {
                if (effect != null) Object.Destroy(effect.gameObject);
            }
        }
    }
}
