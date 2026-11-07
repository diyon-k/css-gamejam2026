using UnityEngine;

namespace Blink.Visuals
{
    // Candle / fire / failing-bulb flicker on a practical light. World thing, so it runs on scaled time:
    // fast-forward (eyes shut) makes flames dance faster.
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        [Range(0, 1)] public float amount = 0.15f; // +- fraction of the base intensity
        public float speed = 6f;
        [Tooltip("Occasionally drops almost to black, like a dying bulb.")]
        public bool dying;

        Light lightSource;
        float baseIntensity, seed;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            baseIntensity = lightSource.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float t = Time.time * speed;
            float i = baseIntensity * (1f - amount + 2f * amount * Mathf.PerlinNoise(seed, t));
            if (dying && Mathf.PerlinNoise(seed + 7f, Time.time * 1.3f) > 0.72f) i *= 0.08f;
            lightSource.intensity = i;
        }
    }
}
