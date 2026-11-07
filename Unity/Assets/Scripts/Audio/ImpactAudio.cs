using Blink.Combat;
using UnityEngine;

namespace Blink.Audio
{
    /// <summary>
    /// Where the bullet lands: a wet flesh hit (3D, at the hit point) when a shot hits something
    /// <see cref="IDamageable"/>, plus a heavier crunch layered on top when that shot killed it, or a dull
    /// thud on walls and props. Listens to <see cref="Gun.Fired"/> (the gun applies damage before raising it,
    /// so the target's <see cref="IDamageable.IsDead"/> already tells us whether this shot was the kill).
    /// Lives on the Effects prefab; finds the gun at runtime like <c>BulletTracer</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ImpactAudio : MonoBehaviour
    {
        const float GunSearchInterval = 1f;

        [Tooltip("Gun to listen to. If empty, the first Gun in the scene is used (searched again if it disappears).")]
        [SerializeField] Gun gun;

        [SerializeField] SoundCue fleshHitCue;
        [Tooltip("Layered on the flesh hit when the shot killed the target.")]
        [SerializeField] SoundCue killCue;
        [Tooltip("Shot hit something that can't be hurt (wall, floor, prop). Optional.")]
        [SerializeField] SoundCue surfaceHitCue;

        Gun _subscribed;
        float _nextSearchTime;

        void Start() => FindGun();

        void OnEnable()
        {
            if (gun != null) Subscribe(gun);
        }

        void OnDisable() => Unsubscribe();

        void Update()
        {
            if (_subscribed == null && Time.unscaledTime >= _nextSearchTime) FindGun();
        }

        void OnFired(bool hit, RaycastHit info)
        {
            if (!hit || info.collider == null) return;
            var target = info.collider.GetComponentInParent<IDamageable>();
            PlayImpact(target != null, target != null && target.IsDead, info.point);
        }

        /// <summary>
        /// Plays the impact for one hit. Public so other weapons (or a test in the editor) can use it.
        /// </summary>
        public void PlayImpact(bool hitDamageable, bool killed, Vector3 point)
        {
            var audio = AudioService.Instance;
            if (audio == null) return;
            if (!hitDamageable)
            {
                if (surfaceHitCue != null) audio.PlayAt(surfaceHitCue, point);
                return;
            }
            if (fleshHitCue != null) audio.PlayAt(fleshHitCue, point);
            if (killed && killCue != null) audio.PlayAt(killCue, point);
        }

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
    }
}
