using UnityEngine;

namespace Blink.World
{
    /// <summary>
    /// Makes an object noisy. Call <see cref="Emit"/> from code or a UnityEvent (cupboard opening,
    /// item placed), and with a Rigidbody it also makes noise when it hits something hard enough
    /// (dropped or thrown), louder the harder the hit.
    /// </summary>
    public sealed class NoiseSource : MonoBehaviour
    {
        [Tooltip("Audible range in metres.")]
        [SerializeField, Min(0f)] float loudness = 6f;

        [Header("Impacts (needs a Rigidbody)")]
        [Tooltip("Hits slower than this are silent.")]
        [SerializeField, Min(0f)] float minImpactSpeed = 1.5f;
        [Tooltip("Impact speed that gives full loudness; harder hits are capped there.")]
        [SerializeField, Min(0.01f)] float fullImpactSpeed = 6f;

        public void Emit() => Noise.Make(transform.position, loudness);

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;
            Noise.Make(transform.position, loudness * Mathf.Clamp01(speed / fullImpactSpeed));
        }
    }
}
