using System;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// One body part's collider (a ragdoll bone) that bullets can hit. Forwards the damage, scaled by
    /// <see cref="DamageMultiplier"/>, to the <see cref="Health"/> on a parent, so the gun finds it with
    /// GetComponentInParent like any other <see cref="IDamageable"/>.
    /// <see cref="Struck"/> is raised before the damage is applied, so a listener (the ragdoll) knows
    /// which part the killing shot hit by the time <see cref="Health.Died"/> fires.
    /// </summary>
    public sealed class Hitbox : MonoBehaviour, IDamageable
    {
        [Tooltip("Damage multiplier for hits on this part (2 = head shots do double).")]
        [SerializeField, Min(0f)] float damageMultiplier = 1f;
        [Tooltip("Defaults to the Health on a parent.")]
        [SerializeField] Health health;

        /// <summary>Args: this hitbox, world hit point, direction the shot travelled. Raised before the damage.</summary>
        public event Action<Hitbox, Vector3, Vector3> Struck;

        public float DamageMultiplier
        {
            get => damageMultiplier;
            set => damageMultiplier = Mathf.Max(0f, value);
        }

        public Health Health => health;
        public bool IsDead => health != null && health.IsDead;

        void Awake()
        {
            if (health == null) health = GetComponentInParent<Health>();
        }

        public void TakeDamage(float amount, Vector3 point, Vector3 direction)
        {
            if (health == null || health.IsDead) return;
            Struck?.Invoke(this, point, direction);
            health.TakeDamage(ScaleDamage(amount, damageMultiplier), point, direction);
        }

        /// <summary>Damage after the part's multiplier. Negative inputs count as zero.</summary>
        public static float ScaleDamage(float amount, float multiplier) =>
            Mathf.Max(0f, amount) * Mathf.Max(0f, multiplier);
    }
}
