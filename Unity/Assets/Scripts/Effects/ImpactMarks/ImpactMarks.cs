using System;
using System.Collections.Generic;
using Blink.Combat;
using Blink.Interaction;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Blink.Effects
{
    /// <summary>One weapon hit on the world (not a zombie or corpse), raised by <see cref="ImpactMarks.WorldHit"/>.</summary>
    public readonly struct WorldImpact
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        /// <summary>Which way the bullet or blow travelled (unit length).</summary>
        public readonly Vector3 Direction;
        public readonly Collider Collider;
        public readonly SurfaceKind Surface;
        public readonly WeaponKind Weapon;
        public readonly bool Melee;
        /// <summary>The melee tool (only meaningful when <see cref="Melee"/>).</summary>
        public readonly MeleeTool Tool;

        public WorldImpact(Vector3 point, Vector3 normal, Vector3 direction, Collider collider, SurfaceKind surface,
            WeaponKind weapon, bool melee, MeleeTool tool)
        {
            Point = point;
            Normal = normal;
            Direction = direction;
            Collider = collider;
            Surface = surface;
            Weapon = weapon;
            Melee = melee;
            Tool = tool;
        }
    }

    /// <summary>
    /// Leaves marks on the house: bullet holes, axe gashes, crowbar scrapes, pan dents (URP decals from one 4x4 atlas,
    /// <see cref="MarkCell"/>). Listens to <see cref="PlayerWeapons.RayFired"/> and <see cref="PlayerWeapons.MeleeHit"/>,
    /// handles the hits <see cref="BloodImpactSpawner"/> ignores (no <see cref="IDamageable"/>), and re-raises them as
    /// <see cref="WorldHit"/> for particles, props and sound. <see cref="Place"/> is public for blood marks.
    /// Creates itself after the first scene loads, like <c>CorpseShotReaction</c>. Marks are static, so blink skips
    /// don't touch them. See docs/ENVIRONMENT_DAMAGE.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ImpactMarks : MonoBehaviour
    {
        const float HubSearchInterval = 1f;
        const string MaterialPath = "ImpactMarks/ImpactDecal";
        static readonly string[] BodyLayers = { "Zombie", "ZombieHitbox", "Corpse", "Player" };

        public static ImpactMarks Instance { get; private set; }

        /// <summary>Every weapon hit on the world, after its mark was placed.</summary>
        public static event Action<WorldImpact> WorldHit;

        [Tooltip("Decal material on the impact atlas. Empty = Resources/ImpactMarks/ImpactDecal.")]
        [SerializeField] Material decalMaterial;
        [Tooltip("Most marks at once; the oldest is reused first.")]
        [SerializeField, Min(1)] int maxMarks = 96;
        [Tooltip("How deep the projector box reaches into and out of the surface, in metres.")]
        [SerializeField, Min(0.01f)] float depth = 0.12f;
        [SerializeField] float bulletSize = 0.11f;
        [Tooltip("Length x width of an axe gash or a prying scrape, along the swing.")]
        [SerializeField] Vector2 gashSize = new(0.38f, 0.12f);
        [SerializeField] float dentSize = 0.16f;
        [SerializeField] float crackSize = 0.3f;
        [SerializeField, Range(0f, 0.5f)] float sizeJitter = 0.15f;
        [SerializeField] float drawDistance = 40f;

        readonly List<DecalProjector> _pool = new();
        int _next;
        int _bodyMask;
        PlayerWeapons _hub;
        float _nextSearchTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            WorldHit = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || FindAnyObjectByType<ImpactMarks>() != null) return;
            var go = new GameObject(nameof(ImpactMarks));
            DontDestroyOnLoad(go);
            go.AddComponent<ImpactMarks>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _bodyMask = LayerMask.GetMask(BodyLayers);
            if (decalMaterial == null) decalMaterial = Resources.Load<Material>(MaterialPath);
            if (decalMaterial == null) Debug.LogWarning($"[ImpactMarks] No decal material at Resources/{MaterialPath}: no marks.", this);
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_hub != null || Time.unscaledTime < _nextSearchTime) return;
            _nextSearchTime = Time.unscaledTime + HubSearchInterval;
            var hub = PlayerWeapons.Instance != null ? PlayerWeapons.Instance : FindAnyObjectByType<PlayerWeapons>();
            if (hub == null) return;
            _hub = hub;
            _hub.RayFired += OnRay;
            _hub.MeleeHit += OnMelee;
        }

        void Unsubscribe()
        {
            if (_hub == null) return;
            _hub.RayFired -= OnRay;
            _hub.MeleeHit -= OnMelee;
            _hub = null;
        }

        /// <summary>True if this collider is a body (zombie, corpse, player) rather than the world.</summary>
        public bool IsBody(Collider c) =>
            c == null || (_bodyMask & (1 << c.gameObject.layer)) != 0 || c.GetComponentInParent<IDamageable>() != null;

        void OnRay(ShotRay shot)
        {
            if (!shot.Hit || shot.Target != null || IsBody(shot.Info.collider)) return;
            var info = shot.Info;
            var surface = SurfaceTable.Of(info.collider);
            var dir = shot.Ray.direction.normalized;
            var cell = SurfaceTable.BulletMark(surface);
            if (cell.HasValue)
            {
                float s = bulletSize * (cell == MarkCell.GlassCrack ? 3f : 1f);
                Place(cell.Value, info.point, info.normal, RandomAlong(info.normal), new Vector2(s, s), info.collider.transform);
            }
            Raise(new WorldImpact(info.point, info.normal, dir, info.collider, surface, shot.Weapon, false, MeleeTool.Blade), 1f);
        }

        void OnMelee(MeleeHitInfo hit)
        {
            if (hit.Target != null || IsBody(hit.Collider)) return;
            var tool = CurrentTool();
            var surface = SurfaceTable.Of(hit.Collider);
            var cell = SurfaceTable.MeleeMark(surface, tool);
            if (cell.HasValue)
            {
                var size = cell.Value switch
                {
                    MarkCell.GashWood or MarkCell.GashPlaster or MarkCell.Scrape or MarkCell.FabricTear => gashSize,
                    MarkCell.Crack or MarkCell.GlassCrack => new Vector2(crackSize, crackSize),
                    _ => new Vector2(dentSize, dentSize),
                };
                Place(cell.Value, hit.Point, hit.Normal, hit.Direction, size, hit.Collider.transform);
            }
            Raise(new WorldImpact(hit.Point, hit.Normal, hit.Direction, hit.Collider, surface, hit.Weapon, true, tool), 1.6f);
        }

        static void Raise(WorldImpact impact, float strength)
        {
            ImpactParticles.Burst(impact.Surface, impact.Point, impact.Normal, strength);
            WorldHit?.Invoke(impact);
        }

        MeleeTool CurrentTool()
        {
            var item = _hub != null && _hub.Axe != null && _hub.ActiveKind == WeaponKind.Axe ? _hub.Axe.Equipped : null;
            return ToolFor(item);
        }

        /// <summary>The axe (or no item) is a blade; crowbar-like items pry; other blunt items dent.</summary>
        public static MeleeTool ToolFor(SwingableProp item)
        {
            if (item == null || !item.KnocksDown) return MeleeTool.Blade;
            var n = (item.DisplayName + " " + item.name).ToLowerInvariant();
            return n.Contains("crowbar") || n.Contains("wrench") || n.Contains("poker") || n.Contains("iron")
                ? MeleeTool.Prying
                : MeleeTool.Blunt;
        }

        static Vector3 RandomAlong(Vector3 normal) => Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, normal);

        /// <summary>
        /// Projects one atlas cell onto the surface at <paramref name="point"/>. <paramref name="along"/> is the mark's
        /// long axis (projected onto the surface); <paramref name="size"/> is length along it x width across it, in
        /// metres. The mark follows <paramref name="parent"/> if it moves (a door, a prop). Returns null if there's no
        /// material.
        /// </summary>
        public DecalProjector Place(MarkCell cell, Vector3 point, Vector3 normal, Vector3 along, Vector2 size, Transform parent)
        {
            if (decalMaterial == null || normal.sqrMagnitude < 1e-6f) return null;
            var d = Next();
            normal.Normalize();
            var up = Vector3.ProjectOnPlane(along, normal);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            float k = 1f + UnityEngine.Random.Range(-sizeJitter, sizeJitter);

            var t = d.transform;
            t.SetParent(parent != null ? parent : transform, true);
            t.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, up));
            d.size = new Vector3(size.y * k, size.x * k, depth);
            d.uvScale = new Vector2(0.25f, 0.25f);
            d.uvBias = SurfaceTable.UvBias(cell);
            d.fadeFactor = 1f;
            d.gameObject.SetActive(true);
            return d;
        }

        // ponytail: FIFO reuse, add a fade-out if the oldest mark popping away is ever visible.
        DecalProjector Next()
        {
            _pool.RemoveAll(p => p == null); // parent destroyed (a broken prop)
            if (_pool.Count < maxMarks)
            {
                var p = new GameObject("Mark").AddComponent<DecalProjector>();
                p.material = decalMaterial;
                p.drawDistance = drawDistance;
                p.pivot = Vector3.zero;
                _pool.Add(p);
                return p;
            }
            _next %= _pool.Count;
            return _pool[_next++];
        }
    }
}
