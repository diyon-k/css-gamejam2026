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
    /// A head hitbox (<see cref="IsHead"/>) kills outright: any hit that does damage takes all the remaining
    /// health, so headshots are one-shot kills with every weapon (the pistol and the axe).
    /// </summary>
    public sealed class Hitbox : MonoBehaviour, IDamageable
    {
        [Tooltip("Damage multiplier for hits on this part (2 = head shots do double).")]
        [SerializeField, Min(0f)] float damageMultiplier = 1f;
        [Tooltip("The head: any damaging hit here kills outright. ZombieRagdoll sets it on the head bone.")]
        [SerializeField] bool isHead;
        [Tooltip("Defaults to the Health on a parent.")]
        [SerializeField] Health health;

        /// <summary>Args: this hitbox, world hit point, direction the shot travelled. Raised before the damage.</summary>
        public event Action<Hitbox, Vector3, Vector3> Struck;

        public float DamageMultiplier
        {
            get => damageMultiplier;
            set => damageMultiplier = Mathf.Max(0f, value);
        }

        /// <summary>True on the head: a damaging hit kills outright. Effects read it for headshot feedback.</summary>
        public bool IsHead
        {
            get => isHead;
            set => isHead = value;
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
            health.TakeDamage(DamageFor(amount, damageMultiplier, isHead, health.Current), point, direction);
        }

        /// <summary>True if <paramref name="collider"/> belongs to a head <see cref="Hitbox"/>.</summary>
        public static bool IsHeadCollider(Collider collider) =>
            collider != null && collider.TryGetComponent(out Hitbox hitbox) && hitbox.IsHead;

        /// <summary>
        /// Damage a hit on this part deals: the scaled damage, or on the head at least everything the target has
        /// left (<paramref name="remainingHealth"/>), so it dies. A hit that does no damage stays harmless.
        /// </summary>
        public static float DamageFor(float amount, float multiplier, bool isHead, float remainingHealth)
        {
            float scaled = ScaleDamage(amount, multiplier);
            return isHead && scaled > 0f ? Mathf.Max(scaled, remainingHealth) : scaled;
        }

        /// <summary>Damage after the part's multiplier. Negative inputs count as zero.</summary>
        public static float ScaleDamage(float amount, float multiplier) =>
            Mathf.Max(0f, amount) * Mathf.Max(0f, multiplier);
    }
}
