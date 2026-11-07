using UnityEngine;

namespace Blink.DayNight
{
    /// <summary>
    /// Turns objects and components (lights, colliders, scripts) on between two hours and off
    /// otherwise: lamps that light at night, a door that only opens at midnight. Wraps past midnight.
    /// Put it on an object that is not itself one of the targets.
    /// </summary>
    public sealed class TimeOfDayToggle : MonoBehaviour
    {
        [Tooltip("Active from this hour (inclusive). 19.5 = 19:30.")]
        [SerializeField, Range(0f, 24f)] float fromHour = 20f;
        [Tooltip("Until this hour (exclusive). Smaller than From wraps past midnight.")]
        [SerializeField, Range(0f, 24f)] float toHour = 6f;
        [Tooltip("Tick to do the opposite: off inside the window, on outside it.")]
        [SerializeField] bool invert;
        [SerializeField] GameObject[] objects;
        [SerializeField] Behaviour[] components;

        bool? _applied;

        public bool IsActive => _applied ?? false;

        void Update()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;
            bool on = clock.IsBetween(fromHour, toHour) != invert;
            if (_applied == on) return;
            _applied = on;
            foreach (var go in objects) if (go != null) go.SetActive(on);
            foreach (var c in components) if (c != null) c.enabled = on;
        }
    }
}
