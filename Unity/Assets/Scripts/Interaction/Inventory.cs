using System;
using System.Collections.Generic;
using Blink.UI;
using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>
    /// On the Player. What you carry, by item id (duplicates allowed: two candles = two entries).
    /// Puzzles check <see cref="Has"/> / <see cref="Count"/> and can <see cref="Remove"/> to use items up.
    /// Carrying a candle lights a small warm light on the camera.
    /// The list shows top-right, under the clock, for a few seconds after it changes, then fades away.
    /// </summary>
    public sealed class Inventory : MonoBehaviour
    {
        [SerializeField] string lightItemId = "candle";
        [Tooltip("Real seconds the carried-items list stays up after it changes.")]
        [SerializeField, Min(0f)] float listLinger = 5f;

        public event Action<string> ItemAdded, ItemRemoved;
        public IReadOnlyList<string> Items => _items;

        readonly List<string> _items = new List<string>();
        readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        Light _candle;
        float _changedAt = float.NegativeInfinity, _listAlpha;
        readonly List<string> _lines = new List<string>();

        public void Add(string id, string displayName = null)
        {
            _items.Add(id);
            if (!string.IsNullOrEmpty(displayName)) _names[id] = displayName;
            UpdateCandle();
            _changedAt = Time.unscaledTime;
            ItemAdded?.Invoke(id);
        }

        public bool Has(string id) => _items.Contains(id);
        public int Count(string id) => _items.FindAll(i => i == id).Count;
        public string DisplayName(string id) => _names.TryGetValue(id, out var n) ? n : id;

        /// <summary>Removes one of that item. False if none was carried.</summary>
        public bool Remove(string id)
        {
            if (!_items.Remove(id)) return false;
            UpdateCandle();
            _changedAt = Time.unscaledTime;
            ItemRemoved?.Invoke(id);
            return true;
        }

        void UpdateCandle()
        {
            if (!Application.isPlaying) return;
            if (_candle == null && Has(lightItemId))
            {
                var parent = GetComponentInChildren<Camera>() is { } cam ? cam.transform : transform;
                _candle = new GameObject("CandleLight").AddComponent<Light>();
                _candle.transform.SetParent(parent, false);
                _candle.transform.localPosition = new Vector3(0.25f, -0.3f, 0.3f);
                _candle.type = LightType.Point;
                _candle.color = new Color(1f, 0.7f, 0.4f);
                _candle.intensity = 1.5f;
                _candle.range = 4f;
            }
            if (_candle != null) _candle.enabled = Has(lightItemId);
        }

        void Update()
        {
            bool show = _items.Count > 0 && Time.unscaledTime - _changedAt < listLinger;
            _listAlpha = BlinkTheme.StepFade(_listAlpha, show, -1f, BlinkTheme.Asset.fadeOut * 2f);
        }

        void OnGUI()
        {
            if (_items.Count == 0 || _listAlpha <= 0f || Event.current.type != EventType.Repaint) return;
            _lines.Clear();
            foreach (var id in new HashSet<string>(_items))
            {
                int n = Count(id);
                _lines.Add(n > 1 ? $"{DisplayName(id)} ×{n}" : DisplayName(id));
            }
            BlinkWidgets.Inventory(_lines, BlinkTheme.EaseOut(_listAlpha));
        }
    }
}
