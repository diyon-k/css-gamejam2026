using Blink.Combat;
using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Axe sounds from the <see cref="PlayerWeapons"/> hub: a whoosh when a swing starts (2D, it's your swing),
    /// and on contact either a flesh hit or a wall thud at the impact point (3D). Lives on the Effects prefab and
    /// finds the hub at runtime, like the other effect listeners.
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
            if (swingCue != null && AudioService.Instance != null) AudioService.Instance.Play(swingCue);
        }

        void OnHit(MeleeHitInfo hit)
        {
            var cue = hit.Target != null ? fleshHitCue : surfaceHitCue;
            if (cue != null && AudioService.Instance != null) AudioService.Instance.PlayAt(cue, hit.Point);
        }

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
