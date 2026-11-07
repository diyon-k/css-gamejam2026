using System;
using UnityEngine;

namespace Blink.Combat
{
    /// <summary>Hit points for the player and zombies. Other scripts react through the events.</summary>
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;

        /// <summary>Args: damage amount, hit point.</summary>
        public event Action<float, Vector3> Damaged;
        public event Action<float> Healed;
        public event Action Died;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Fraction => Current / maxHealth;
        public bool IsDead => Current <= 0f;

        void Awake() => Current = maxHealth;

        public void TakeDamage(float amount, Vector3 point, Vector3 direction)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount, point);
            if (IsDead) Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            float before = Current;
            Current = Mathf.Min(maxHealth, Current + amount);
            if (Current > before) Healed?.Invoke(Current - before);
        }

        /// <summary>Back to full health (e.g. on restart).</summary>
        public void ResetHealth() => Current = maxHealth;
    }
}
