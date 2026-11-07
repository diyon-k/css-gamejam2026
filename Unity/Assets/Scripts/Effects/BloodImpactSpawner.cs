using System.Collections.Generic;
using Blink.Combat;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.Pool;

namespace Blink.Effects
{
    /// <summary>
    /// Bursts blood out of anything the gun damages: a dark spray off the entry wound (bent back towards the shooter),
    /// an optional smaller spray out of the back, and an optional splat on the floor below. Listens to
    /// <see cref="Gun.Fired"/>, so it can live anywhere (it sits on the Effects prefab); if no gun is assigned it finds
    /// one at runtime.
    /// Effects come from small pools that are filled at startup, so nothing is instantiated per shot. When a pool is
    /// full the oldest effect is recycled.
    /// Blood is part of world time. Particles run on scaled time, so eyes-shut fast-forward speeds them up for free,
    /// and a blink skip (<see cref="ISkippable"/>) fast-forwards every live effect so droplets don't hang in mid-air
    /// while the zombie jumps ahead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BloodImpactSpawner : MonoBehaviour, ISkippable
    {
        const float GunSearchInterval = 1f;

        /// <summary>Layers the floor probe always ignores (bodies and the player), if they exist.</summary>
        static readonly string[] BodyLayers = { "Zombie", "ZombieHitbox", "Corpse", "Player" };

        [Tooltip("Gun to listen to. If empty, the first Gun in the scene is used (searched again if it disappears).")]
        [SerializeField] Gun gun;
        [Tooltip("Camera the gun aims from, used for the shot direction. If empty, the camera above the gun, then Camera.main.")]
        [SerializeField] Camera aimCamera;

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

        [Header("Pool & time skips")]
        [SerializeField, Min(1)] int impactPoolSize = 8;
        [SerializeField, Min(1)] int floorPoolSize = 6;
        [Tooltip("Longest single step when fast-forwarding through a blink, so droplets don't tunnel through the floor.")]
        [SerializeField, Min(0.01f)] float maxSkipStep = 0.1f;

        EffectPool _impacts;
        EffectPool _floor;
        Gun _subscribed;
        float _nextSearchTime;
        int _floorLayers;

        void Awake()
        {
            _floorLayers = floorMask & ~LayerMask.GetMask(BodyLayers);
            var root = new GameObject("BloodPool").transform;
            root.SetParent(transform, false);
            if (impactPrefab != null) _impacts = new EffectPool(impactPrefab, impactPoolSize, root);
            if (floorPrefab != null) _floor = new EffectPool(floorPrefab, floorPoolSize, root);
        }

        void Start() => FindGun();

        void OnEnable()
        {
            TimeSkipController.Register(this);
            if (gun != null) Subscribe(gun);
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
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindGun();
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
        /// Spawns blood for a bullet that hit <paramref name="info"/> travelling along <paramref name="rayDirection"/>.
        /// Public so other weapons (or editor tests) can use it; doesn't check that the target is damageable.
        /// </summary>
        public void SpawnHit(RaycastHit info, Vector3 rayDirection)
        {
            if (info.collider == null) return;
            Vector3 rayDir = rayDirection.sqrMagnitude > 1e-6f ? rayDirection.normalized : -info.normal;

            if (_impacts != null)
            {
                Vector3 spray = BloodSpray.SprayDirection(info.normal, rayDir, towardShooter);
                _impacts.Spawn(info.point + info.normal * surfaceOffset, BloodSpray.FacingRotation(spray),
                    RandomScale(scaleRange), BloodSpray.Lift(spray, dropletLift));

                if (exitSpray && exitScale > 0f) SpawnExit(info, rayDir);
            }

            if (_floor != null && Random.value < floorChance) SpawnFloor(info, rayDir);
        }

        void SpawnExit(RaycastHit info, Vector3 rayDir)
        {
            // Cast back along the shot from beyond the collider to find where the bullet would come out.
            var back = new Ray(info.point + rayDir * maxExitDepth, -rayDir);
            if (!info.collider.Raycast(back, out var exit, maxExitDepth)) return;
            // The quads are single-sided, so face them at the shooter: the burst shows round the body's silhouette.
            _impacts.Spawn(exit.point + rayDir * surfaceOffset, BloodSpray.FacingRotation(-rayDir),
                exitScale * RandomScale(scaleRange), BloodSpray.Lift(rayDir, dropletLift));
        }

        void SpawnFloor(RaycastHit info, Vector3 rayDir)
        {
            Vector3 carry = Vector3.ProjectOnPlane(rayDir, Vector3.up) * floorCarry;
            if (!Physics.Raycast(info.point + carry, Vector3.down, out var floor, maxFloorDrop, _floorLayers,
                    QueryTriggerInteraction.Ignore)) return;
            _floor.Spawn(floor.point + floor.normal * surfaceOffset, BloodSpray.DirectionalRotation(floor.normal, rayDir),
                RandomScale(floorScaleRange), Vector3.zero);
        }

        void OnFired(bool hit, RaycastHit info)
        {
            if (!hit || info.collider == null || info.collider.GetComponentInParent<IDamageable>() == null) return;
            var cam = ResolveCamera();
            Vector3 rayDir = cam != null ? (info.point - cam.transform.position).normalized : -info.normal;
            SpawnHit(info, rayDir);
        }

        static float RandomScale(Vector2 range) => Random.Range(range.x, range.y);

        void FindGun()
        {
            _nextSearchTime = Time.unscaledTime + GunSearchInterval;
            if (gun == null) gun = FindAnyObjectByType<Gun>();
            if (gun != null && isActiveAndEnabled) Subscribe(gun);
        }

        void Subscribe(Gun target)
        {
            if (_subscribed == target) return;
            Unsubscribe();
            _subscribed = target;
            _subscribed.Fired += OnFired;
        }

        void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.Fired -= OnFired;
            _subscribed = null;
        }

        Camera ResolveCamera()
        {
            if (aimCamera != null) return aimCamera;
            if (_subscribed != null) aimCamera = _subscribed.GetComponentInParent<Camera>();
            return aimCamera != null ? aimCamera : Camera.main;
        }

        /// <summary>
        /// Fixed-size pool of one effect prefab. Filled up front; when every instance is in use the oldest one is
        /// recycled, so it never instantiates after warm-up.
        /// </summary>
        sealed class EffectPool
        {
            readonly ObjectPool<BloodImpactEffect> _pool;
            readonly List<BloodImpactEffect> _active = new();
            readonly int _capacity;
            readonly GameObject _prefab;
            readonly Transform _parent;

            public EffectPool(GameObject prefab, int capacity, Transform parent)
            {
                _prefab = prefab;
                _parent = parent;
                _capacity = capacity;
                _pool = new ObjectPool<BloodImpactEffect>(Create, OnGet, OnRelease, OnDestroyEffect,
                    collectionCheck: false, defaultCapacity: capacity, maxSize: capacity);

                var warm = new BloodImpactEffect[capacity];
                for (int i = 0; i < capacity; i++) warm[i] = _pool.Get();
                foreach (var effect in warm) _pool.Release(effect);
            }

            public void Spawn(Vector3 position, Quaternion rotation, float scale, Vector3 dropletDirection)
            {
                if (_active.Count >= _capacity) Release(_active[0]);
                var effect = _pool.Get();
                effect.InUse = true;
                _active.Add(effect);
                effect.Play(position, rotation, scale, dropletDirection);
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
