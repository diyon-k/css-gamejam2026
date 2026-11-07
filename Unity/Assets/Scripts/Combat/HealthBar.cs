using UnityEngine;

namespace Blink.Combat
{
    /// <summary>
    /// A small health bar floating above a <see cref="Health"/> (put it on zombies). It only shows
    /// once the target is hurt and still alive, and only when the camera can see it.
    /// Drawn with IMGUI so it needs no Canvas or prefab; swap for a proper UI later if wanted.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class HealthBar : MonoBehaviour
    {
        [Tooltip("Height of the bar above this object's pivot (the feet).")]
        [SerializeField] float heightOffset = 2.1f;
        [Tooltip("Bar size in pixels at referenceDistance metres; it shrinks with distance.")]
        [SerializeField] Vector2 size = new(70f, 8f);
        [SerializeField] float referenceDistance = 6f;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] bool onlyWhenDamaged = true;
        [SerializeField] Color fillColor = new(0.85f, 0.12f, 0.1f, 0.95f);
        [SerializeField] Color backColor = new(0f, 0f, 0f, 0.6f);

        Health _health;
        Camera _camera;
        bool _visible;
        Vector3 _screen;
        float _scale;

        void Awake() => _health = GetComponent<Health>();

        void LateUpdate()
        {
            _visible = false;
            if (_health.IsDead || (onlyWhenDamaged && _health.Current >= _health.Max)) return;

            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null) return;

            Vector3 world = transform.position + Vector3.up * heightOffset;
            Vector3 camPos = _camera.transform.position;
            float distance = Vector3.Distance(camPos, world);
            if (distance > maxDistance) return;

            _screen = _camera.WorldToScreenPoint(world);
            if (_screen.z <= 0f) return; // behind the camera

            // Hide behind walls: anything hit on the way that isn't this zombie blocks the bar.
            if (Physics.Linecast(camPos, world, out var hit, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<Health>() != _health)
                return;

            _scale = Mathf.Clamp(referenceDistance / Mathf.Max(distance, 0.01f), 0.4f, 1.5f);
            _visible = true;
        }

        void OnGUI()
        {
            if (!_visible || Event.current.type != EventType.Repaint) return;

            float w = size.x * _scale, h = Mathf.Max(3f, size.y * _scale);
            // Screen space has y up; GUI space has y down.
            var back = new Rect(_screen.x - w * 0.5f, Screen.height - _screen.y - h * 0.5f, w, h);
            var fill = new Rect(back.x + 1f, back.y + 1f, (w - 2f) * _health.Fraction, h - 2f);

            var old = GUI.color;
            GUI.color = backColor;
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = fillColor;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
