using System;
using System.Collections.Generic;
using Blink.World;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// Axe gameplay: a swing plays <see cref="AxeViewModel"/>, and when the blade reaches the apex of the chop
    /// (<see cref="AxeViewModel.StrikeApex"/>, raised even if a blink skips past it) a short sphere sweep in front of
    /// the camera picks one target: the nearest living hitbox the eye can see, or failing that the nearest wall
    /// (a thud). The target takes <see cref="damage"/> once per swing; a head hitbox kills outright (see
    /// <see cref="Hitbox"/>). No ammo.
    ///
    /// No input of its own: <see cref="PlayerWeapons"/> calls <see cref="TrySwing"/> while the axe is out. The swing
    /// is world time (the view model handles skips and fast-forward).
    /// </summary>
    public sealed class MeleeWeapon : MonoBehaviour, IWeaponStatus
    {
        const int MaxHits = 16;

        [SerializeField] Camera aimCamera;
        [Tooltip("Defaults to the AxeViewModel under this object.")]
        [SerializeField] AxeViewModel viewModel;

        [Header("Strike")]
        [SerializeField, Min(0f)] float damage = 60f;
        [Tooltip("How far in front of the eye the blade reaches, metres.")]
        [SerializeField, Min(0.1f)] float reach = 1.8f;
        [Tooltip("Radius of the sweep, metres. Forgiving aim without hitting things off to the side.")]
        [SerializeField, Min(0.01f)] float radius = 0.35f;
        [Tooltip("Same layers as the guns: hitboxes and the level, not the Zombie root, corpses or the player.")]
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Noise (audible range in metres)")]
        [Tooltip("When the blade hits something. A swing through the air is silent.")]
        [SerializeField, Min(0f)] float hitLoudness = 6f;

        /// <summary>A swing started.</summary>
        public event Action Swung;

        /// <summary>The swing connected (after the damage). <see cref="MeleeHitInfo.Target"/> is null for a wall.</summary>
        public event Action<MeleeHitInfo> Hit;

        public WeaponKind Kind => WeaponKind.Axe;
        public bool UsesAmmo => false;
        public int AmmoLoaded => 0;
        public int AmmoCapacity => 0;
        public int AmmoReserve => 0;
        public bool IsReloading => false;
        public float ReloadProgress01 => 0f;
        public float Damage => damage;
        public float Reach => reach;
        public AxeViewModel ViewModel => viewModel;

        /// <summary>True from the start of a swing until the axe is back at rest.</summary>
        public bool IsSwinging => viewModel != null && viewModel.IsSwinging;

        readonly RaycastHit[] _hits = new RaycastHit[MaxHits];
        readonly List<MeleeCandidate> _candidates = new(MaxHits);
        readonly List<RaycastHit> _candidateHits = new(MaxHits);
        IDamageable _owner;
        Collider _forcedTarget;

        void Awake()
        {
            if (aimCamera == null) aimCamera = GetComponentInParent<Camera>();
            if (viewModel == null) viewModel = GetComponentInChildren<AxeViewModel>(true);
            _owner = GetComponentInParent<IDamageable>();
        }

        void OnEnable()
        {
            if (viewModel != null) viewModel.StrikeApex += Strike;
        }

        void OnDisable()
        {
            if (viewModel != null) viewModel.StrikeApex -= Strike;
        }

        /// <summary>Starts a swing. False if one is already running (or the player is dead).</summary>
        public bool TrySwing()
        {
            _forcedTarget = null;
            if (_owner != null && _owner.IsDead) return false;
            if (viewModel == null || !viewModel.PlaySwing()) return false;
            Swung?.Invoke();
            return true;
        }

        /// <summary>
        /// Starts a swing whose strike lands on <paramref name="target"/> whatever the sweep would pick: for scripted
        /// kills (the grab QTE's chop goes for the head, past the arms reaching for you). Same damage, sound and events.
        /// </summary>
        public bool TrySwingAt(Collider target)
        {
            if (!TrySwing()) return false;
            _forcedTarget = target;
            return true;
        }

        /// <summary>
        /// The hit check: sweeps a sphere from the eye along the view, picks one target and damages it.
        /// Called on the view model's strike apex; public so editor tests can call it directly.
        /// </summary>
        public void Strike()
        {
            if (aimCamera == null) return;
            Vector3 origin = aimCamera.transform.position;
            Vector3 dir = aimCamera.transform.forward;

            var forced = _forcedTarget;
            _forcedTarget = null;
            if (forced != null && forced.enabled && forced.GetComponentInParent<IDamageable>() is { } forcedTarget)
            {
                Vector3 point = ClosestPoint(forced, origin);
                Vector3 away = origin - point;
                Vector3 normal = away.sqrMagnitude > 1e-8f ? away.normalized : -dir;
                Land(forcedTarget, point, normal, (point - origin).sqrMagnitude > 1e-8f ? (point - origin).normalized : dir, forced);
                return;
            }

            int count = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, reach, hitMask, QueryTriggerInteraction.Ignore);

            _candidates.Clear();
            _candidateHits.Clear();
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null) continue;
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null && ReferenceEquals(damageable, _owner)) continue;

                if (hit.distance <= 0f && hit.point == Vector3.zero) // overlapping at the start: no point given
                {
                    Vector3 closest = ClosestPoint(hit.collider, origin);
                    Vector3 away = origin - closest;
                    hit.point = closest;
                    hit.normal = away.sqrMagnitude > 1e-8f ? away.normalized : -dir;
                    hit.distance = 0f;
                }

                bool visible = damageable == null || CanSee(origin, hit.point, hit.collider);
                _candidates.Add(new MeleeCandidate(hit.distance, damageable != null, damageable is { IsDead: false }, visible));
                _candidateHits.Add(hit);
            }

            int pick = MeleeTargeting.Choose(_candidates, out bool isSurface);
            if (pick < 0) return;

            var chosen = _candidateHits[pick];
            Land(isSurface ? null : chosen.collider.GetComponentInParent<IDamageable>(), chosen.point, chosen.normal, dir, chosen.collider);
        }

        /// <summary>Damages <paramref name="target"/> (null for a wall), makes the noise and raises <see cref="Hit"/>.</summary>
        void Land(IDamageable target, Vector3 point, Vector3 normal, Vector3 dir, Collider collider)
        {
            bool killed = false;
            if (target != null)
            {
                target.TakeDamage(damage, point, dir);
                killed = target.IsDead;
            }

            Noise.Make(point, hitLoudness);
            Hit?.Invoke(new MeleeHitInfo(WeaponKind.Axe, point, normal, dir, collider, target, killed));
        }

        // Nothing solid (that isn't part of the target) between the eye and the hit point.
        bool CanSee(Vector3 origin, Vector3 point, Collider target)
        {
            Vector3 to = point - origin;
            float distance = to.magnitude;
            if (distance < 0.05f) return true;
            if (!Physics.Raycast(origin, to / distance, out var block, distance - 0.02f, hitMask, QueryTriggerInteraction.Ignore))
                return true;
            if (block.collider == target) return true;
            var blocker = block.collider.GetComponentInParent<IDamageable>();
            return blocker != null; // another hitbox (same or another zombie) is in the way, not a wall
        }

        static Vector3 ClosestPoint(Collider collider, Vector3 point) =>
            collider is MeshCollider { convex: false } ? collider.bounds.ClosestPoint(point) : collider.ClosestPoint(point);
    }
}
