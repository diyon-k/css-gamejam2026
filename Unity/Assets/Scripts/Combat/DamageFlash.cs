using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// Flashes every renderer under this object toward <see cref="flashColor"/> when the
    /// <see cref="Health"/> on this object takes damage, so you can see a shot land.
    /// Uses a MaterialPropertyBlock on the URP "_BaseColor", so materials aren't duplicated.
    /// Runs on unscaled time (it's an effect, not part of the world).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class DamageFlash : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Color flashColor = new(1f, 0.12f, 0.08f, 1f);
        [Tooltip("Real seconds the flash takes to fade.")]
        [SerializeField, Min(0.01f)] float duration = 0.15f;

        Health _health;
        Renderer[] _renderers;
        Color[][] _originals;
        MaterialPropertyBlock _block;
        float _remaining;

        void Awake()
        {
            _health = GetComponent<Health>();
            _block = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originals = new Color[_renderers.Length][];
            for (int r = 0; r < _renderers.Length; r++)
            {
                var materials = _renderers[r].sharedMaterials;
                _originals[r] = new Color[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    _originals[r][i] = m != null && m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : Color.white;
                }
            }
        }

        void OnEnable() => _health.Damaged += OnDamaged;

        void OnDisable()
        {
            _health.Damaged -= OnDamaged;
            if (_remaining > 0f) Apply(0f);
            _remaining = 0f;
        }

        void Update()
        {
            if (_remaining <= 0f) return;
            _remaining -= Time.unscaledDeltaTime;
            Apply(Mathf.Clamp01(_remaining / duration));
        }

        void OnDamaged(float amount, Vector3 point)
        {
            _remaining = duration;
            Apply(1f);
        }

        void Apply(float strength)
        {
            for (int r = 0; r < _renderers.Length; r++)
            {
                var renderer = _renderers[r];
                if (renderer == null) continue;
                for (int i = 0; i < _originals[r].Length; i++)
                {
                    renderer.GetPropertyBlock(_block, i);
                    _block.SetColor(BaseColor, Color.Lerp(_originals[r][i], flashColor, strength));
                    renderer.SetPropertyBlock(_block, i);
                }
            }
        }
    }
}
