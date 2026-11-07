using UnityEngine;

namespace Blink.Combat
{
    /// <summary>Anything a bullet or a zombie can hurt. The gun looks for it with GetComponentInParent.</summary>
    public interface IDamageable
    {
        bool IsDead { get; }

        /// <param name="amount">Damage to apply.</param>
        /// <param name="point">World-space hit point (for blood effects).</param>
        /// <param name="direction">Direction the damage travelled (for knockback/effects).</param>
        void TakeDamage(float amount, Vector3 point, Vector3 direction);
    }
}
