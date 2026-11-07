using System.Collections.Generic;
using Blink.Combat;
using Blink.Mobs;
using Blink.TimeSkip;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Blink.Effects
{
    /// <summary>
    /// Blood on the house from zombie hits, through <see cref="ImpactMarks.Place"/> (blood cells of the same atlas).
    /// A gunshot sprays on behind the zombie onto the wall or floor it was standing by; an axe or prop blow splashes
    /// along the swing and onto the floor; a dead body, once its ragdoll freezes, leaves one pool under its hips that
    /// grows over a few seconds. <see cref="BloodImpactSpawner"/> still does the particles and the floor splat under
    /// the hit. Creates itself after the first scene loads, like <c>CorpseShotReaction</c>. See
    /// docs/ENVIRONMENT_DAMAGE.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BloodMarks : MonoBehaviour, ISkippable
    {
        const float HubSearchInterval = 1f;
        const float CorpseScanInterval = 0.5f;
        static readonly string[] BodyLayers = { "Zombie", "ZombieHitbox", "Corpse", "Player" };

        public static BloodMarks Instance { get; private set; }

        [Header("Back-spray (gunshots)")]
        [SerializeField, Min(0f)] float sprayReach = 2.5f;
        [SerializeField, Range(0f, 1f)] float sprayChance = 0.8f;
        [Tooltip("Length along the shot x width, metres.")]
        [SerializeField] Vector2 spraySize = new(0.5f, 0.3f);
        [SerializeField, Min(1f)] float headshotScale = 1.6f;

        [Header("Melee splash")]
        [SerializeField, Min(0f)] float splashReach = 1.5f;
        [Tooltip("Reach of the second, down-biased cast that finds the floor.")]
        [SerializeField, Min(0f)] float splashFloorReach = 2.5f;
        [SerializeField] Vector2 splashSize = new(0.45f, 0.3f);

        [Header("Corpse pools")]
        [SerializeField] float poolStartSize = 0.2f;
        [SerializeField] float poolEndSize = 1.1f;
        [Tooltip("World seconds for a pool to grow to full size.")]
        [SerializeField, Min(0.01f)] float poolGrowSeconds = 4f;

        sealed class Pool
        {
            public DecalProjector Decal;
            public Vector3 Position;
            public float Elapsed, EndSize;
        }

        readonly HashSet<ZombieRagdoll> _pooled = new();
        readonly List<Pool> _growing = new();
        int _worldMask;
        PlayerWeapons _hub;
        float _nextSearchTime, _nextScanTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || FindAnyObjectByType<BloodMarks>() != null) return;
            var go = new GameObject(nameof(BloodMarks));
            DontDestroyOnLoad(go);
            go.AddComponent<BloodMarks>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _worldMask = ~LayerMask.GetMask(BodyLayers);
        }

        void OnEnable()
        {
            if (Instance == this) TimeSkipController.Register(this);
        }

        void OnDisable() => TimeSkipController.Unregister(this);

        void OnDestroy()
        {
            if (_hub != null)
            {
                _hub.RayFired -= OnRay;
                _hub.MeleeHit -= OnMelee;
            }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            FindHub();
            if (Time.time >= _nextScanTime) ScanCorpses();
            Grow(Time.deltaTime);
        }

        public void Skip(float seconds) => Grow(seconds);

        void FindHub()
        {
            if (_hub != null || Time.unscaledTime < _nextSearchTime) return;
            _nextSearchTime = Time.unscaledTime + HubSearchInterval;
            var hub = PlayerWeapons.Instance != null ? PlayerWeapons.Instance : FindAnyObjectByType<PlayerWeapons>();
            if (hub == null) return;
            _hub = hub;
            _hub.RayFired += OnRay;
            _hub.MeleeHit += OnMelee;
        }

        void OnRay(ShotRay shot)
        {
            if (shot.Target == null) return;
            bool head = shot.IsHead;
            if (!head && Random.value > sprayChance) return;
            var dir = shot.Ray.direction.normalized;
            Splat(shot.Info.point, dir, sprayReach, spraySize * (head ? headshotScale : 1f));
        }

        void OnMelee(MeleeHitInfo hit)
        {
            if (hit.Target == null) return;
            var dir = hit.Direction.normalized;
            Splat(hit.Point, dir, splashReach, splashSize);
            Splat(hit.Point, (dir + Vector3.down * 2f).normalized, splashFloorReach, splashSize);
        }

        /// <summary>Casts from <paramref name="from"/> against the world and leaves a splat stretched along the cast.</summary>
        void Splat(Vector3 from, Vector3 dir, float reach, Vector2 size)
        {
            var marks = ImpactMarks.Instance;
            if (marks == null || !Physics.Raycast(from, dir, out var hit, reach, _worldMask, QueryTriggerInteraction.Ignore)) return;
            if (marks.IsBody(hit.collider)) return; // ponytail: a body on a world layer blocks the spray, RaycastAll if it shows
            var cell = Random.value < 0.5f ? MarkCell.Blood1 : MarkCell.Blood2;
            marks.Place(cell, hit.point, hit.normal, dir, size, hit.collider.transform);
        }

        // ponytail: polls every ragdoll twice a second, a ZombieRagdoll.Froze event if zombie counts climb.
        void ScanCorpses()
        {
            _nextScanTime = Time.time + CorpseScanInterval;
            _pooled.RemoveWhere(r => r == null);
            foreach (var r in FindObjectsByType<ZombieRagdoll>())
            {
                // Knocked-down bodies get up instead of freezing, so frozen means dead. One pool per body, even if shoved.
                if (!r.IsActive || !r.IsFrozen || r.IsKnockedDown || !_pooled.Add(r)) continue;
                PlacePool(r.HipsPosition);
            }
        }

        void PlacePool(Vector3 hips)
        {
            var marks = ImpactMarks.Instance;
            if (marks == null || !Physics.Raycast(hips + Vector3.up * 0.3f, Vector3.down, out var hit, 2f, _worldMask,
                    QueryTriggerInteraction.Ignore)) return;
            if (marks.IsBody(hit.collider)) return;
            var decal = marks.Place(MarkCell.BloodPool, hit.point, hit.normal, Random.onUnitSphere,
                new Vector2(poolStartSize, poolStartSize), hit.collider.transform);
            if (decal == null) return;
            _growing.Add(new Pool
            {
                Decal = decal,
                Position = decal.transform.position,
                EndSize = poolEndSize * Random.Range(0.85f, 1.15f),
            });
        }

        void Grow(float deltaTime)
        {
            for (int i = _growing.Count - 1; i >= 0; i--)
            {
                var p = _growing[i];
                // ImpactMarks reuses its oldest projector: if this one moved, it's someone else's mark now.
                if (p.Decal == null || p.Decal.transform.position != p.Position)
                {
                    _growing.RemoveAt(i);
                    continue;
                }
                p.Elapsed += deltaTime;
                float t = Mathf.Clamp01(p.Elapsed / poolGrowSeconds);
                float s = Mathf.Lerp(poolStartSize, p.EndSize, 1f - (1f - t) * (1f - t)); // ease out: spreads fast, then slows
                p.Decal.size = new Vector3(s, s, p.Decal.size.z);
                if (t >= 1f) _growing.RemoveAt(i);
            }
        }
    }
}
