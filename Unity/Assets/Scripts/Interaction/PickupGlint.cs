using System.Collections.Generic;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// Added by the <see cref="Interactor"/>. Every few seconds each nearby thing you can take (<see cref="Pickup"/>,
    /// <see cref="SwingableProp"/> lying around, <see cref="Readable"/> notes) gives a small star-shaped twinkle, if it's
    /// in view and not behind a wall. Drawn under the HUD and the eyelids, so a blink hides it.
    /// </summary>
    public sealed class PickupGlint : MonoBehaviour
    {
        const float Range = 8f, Period = 2.2f, FlashSeconds = 0.5f, BasePixels = 64f, Rescan = 1f;
        static readonly Color Tint = new(1f, 0.93f, 0.75f);

        static Texture2D _star;
        readonly List<Component> _items = new();
        readonly List<(Vector2 pos, float size, float alpha)> _draw = new();
        Camera _cam;
        float _nextScan;

        void Start()
        {
            _cam = GetComponent<Interactor>().Eye.GetComponent<Camera>();
            if (_star == null) _star = MakeStar(32);
        }

        void Update()
        {
            _draw.Clear();
            if (_cam == null) return;
            if (Time.unscaledTime >= _nextScan)
            {
                // ponytail: rescans the scene once a second; swap for OnEnable/OnDisable registration if item counts get large.
                _nextScan = Time.unscaledTime + Rescan;
                _items.Clear();
                _items.AddRange(FindObjectsByType<Pickup>(FindObjectsSortMode.None));
                _items.AddRange(FindObjectsByType<SwingableProp>(FindObjectsSortMode.None));
                _items.AddRange(FindObjectsByType<Readable>(FindObjectsSortMode.None));
            }

            Vector3 eye = _cam.transform.position;
            foreach (var item in _items)
            {
                if (item == null || !item.gameObject.activeInHierarchy) continue;
                if (item is SwingableProp { IsHeld: true } || item is Pickup { Taken: true } || ReferenceEquals(item, Readable.Current)) continue;

                // Flash briefly once per period, each item at its own offset so they don't pulse in sync.
                float phase = Mathf.Repeat(Time.unscaledTime + (item.GetHashCode() & 1023) * 0.37f, Period);
                if (phase > FlashSeconds) continue;

                var rend = item.GetComponentInChildren<Renderer>();
                Vector3 p = rend != null ? rend.bounds.center : item.transform.position;
                float dist = Vector3.Distance(eye, p);
                if (dist > Range) continue;
                Vector3 sp = _cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                if (Physics.Linecast(eye, p, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(item.transform))
                    continue; // behind something

                float t = phase / FlashSeconds;
                float alpha = Mathf.Sin(t * Mathf.PI) * Mathf.InverseLerp(Range, Range * 0.6f, dist);
                float size = BasePixels * (Screen.height / 1080f) * Mathf.Lerp(1f, 0.5f, dist / Range) * (0.6f + 0.4f * Mathf.Sin(t * Mathf.PI));
                _draw.Add((new Vector2(sp.x, Screen.height - sp.y), size, alpha));
            }
        }

        void OnGUI()
        {
            if (_draw.Count == 0 || Event.current.type != EventType.Repaint) return;
            GUI.depth = 10; // under the HUD (0) and eyelids (-1000)
            var old = GUI.color;
            foreach (var (pos, size, alpha) in _draw)
            {
                GUI.color = new Color(Tint.r, Tint.g, Tint.b, alpha);
                GUI.DrawTexture(new Rect(pos.x - size / 2f, pos.y - size / 2f, size, size), _star);
            }
            GUI.color = old;
        }

        // A soft four-point star: two thin crossed streaks plus a bright core.
        static Texture2D MakeStar(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / n * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                float streak = Mathf.Max(Mathf.Pow(1f - dx, 3f) * Mathf.Exp(-dy * 18f), Mathf.Pow(1f - dy, 3f) * Mathf.Exp(-dx * 18f));
                float core = Mathf.Exp(-(dx * dx + dy * dy) * 25f);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(streak + core));
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
