using System;
using Blink.Combat;
using Blink.Effects;
using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Axe sounds from the <see cref="PlayerWeapons"/> hub: a whoosh when a swing starts (2D, it's your swing),
    /// and on contact either a flesh hit or a wall thud at the impact point (3D). Lives on the Effects prefab and
    /// finds the hub at runtime, like the other effect listeners. A melee item with its own cues (a plank's thud, see
    /// <see cref="Interaction.SwingableProp"/>) plays those instead, unless
    /// <c>surfaceCues</c> lists a cue for the surface hit (see <see cref="Effects.SurfaceTable"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeleeAudio : MonoBehaviour
    {
        const float HubSearchInterval = 1f;

        [SerializeField] SoundCue swingCue;
        [Tooltip("Blade into a zombie.")]
        [SerializeField] SoundCue fleshHitCue;
        [Tooltip("Blade into a wall, floor or prop.")]
        [SerializeField] SoundCue surfaceHitCue;
        [Tooltip("Per-surface cue for a blow on the world (wood thunk, soft thump on fabric...). Wins over the held item's own surface cue; a surface not listed uses the item's cue, then Surface Hit Cue.")]
        [SerializeField] SurfaceCue[] surfaceCues = Array.Empty<SurfaceCue>();

        PlayerWeapons _hub;
        float _nextSearchTime;

        void OnDisable() => Unsubscribe();

        void Update()
        {
            if (_hub != null || Time.unscaledTime < _nextSearchTime) return;
            _nextSearchTime = Time.unscaledTime + HubSearchInterval;
            var hub = PlayerWeapons.Instance != null ? PlayerWeapons.Instance : FindAnyObjectByType<PlayerWeapons>();
            if (hub == null) return;
            _hub = hub;
            _hub.MeleeSwing += OnSwing;
            _hub.MeleeHit += OnHit;
        }

        void OnSwing(WeaponKind weapon)
        {
            var cue = Item?.SwingCue != null ? Item.SwingCue : swingCue;
            if (cue != null && AudioService.Instance != null) AudioService.Instance.Play(cue);
        }

        void OnHit(MeleeHitInfo hit)
        {
            var cue = hit.Target != null
                ? (Item?.HitCue != null ? Item.HitCue : fleshHitCue)
                : SurfaceCue.Find(surfaceCues, SurfaceTable.Of(hit.Collider))
                  ?? (Item?.SurfaceHitCue != null ? Item.SurfaceHitCue : surfaceHitCue);
            if (cue != null && AudioService.Instance != null) AudioService.Instance.PlayAt(cue, hit.Point);
        }

        // Only the melee slot's swings carry an item; the grab QTE's headbutt reports hits with the axe slot empty or not out.
        Interaction.SwingableProp Item => _hub != null && _hub.Axe != null && _hub.ActiveKind == WeaponKind.Axe ? _hub.Axe.Equipped : null;

        void Unsubscribe()
        {
            if (_hub != null)
            {
                _hub.MeleeSwing -= OnSwing;
                _hub.MeleeHit -= OnHit;
            }
            _hub = null;
        }
    }
}
