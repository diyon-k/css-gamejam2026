using System;
using Blink.TimeSkip;
using UnityEngine;

namespace Blink.DayNight
{
    /// <summary>
    /// The in-game time of day. Runs on world (scaled) time, so eyes-shut fast-forward speeds it up,
    /// and jumps on every blink skip by skipped seconds x rate plus <see cref="skipBonusMinutes"/>.
    /// Puzzles: read <see cref="Instance"/> (Hour, IsNight, IsBetween...) or subscribe to its events.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class GameClock : MonoBehaviour, ISkippable
    {
        public static GameClock Instance { get; private set; }

        [SerializeField, Range(0f, 24f)] float startHour = 21f;
        [Tooltip("Game minutes per world second. Fast-forward multiplies this through Time.timeScale.")]
        [SerializeField, Min(0f)] float minutesPerSecond = 1f;
        [Tooltip("Extra game minutes added on every blink skip, on top of skipped seconds x rate.")]
        [SerializeField, Min(0f)] float skipBonusMinutes = 15f;
        [SerializeField, Range(0f, 24f)] float sunriseHour = 6f;
        [SerializeField, Range(0f, 24f)] float sunsetHour = 20f;

        GameTime _time;
        GameTime T => _time ??= new GameTime(startHour, sunriseHour, sunsetHour);

        /// <summary>A blink skip jumped the clock. Argument: game minutes jumped.</summary>
        public event Action<float> Jumped;
        public event Action MinuteChanged { add => T.MinuteChanged += value; remove => T.MinuteChanged -= value; }
        public event Action<int> HourChanged { add => T.HourChanged += value; remove => T.HourChanged -= value; }
        public event Action<int> DayChanged { add => T.DayChanged += value; remove => T.DayChanged -= value; }
        public event Action NightFell { add => T.NightFell += value; remove => T.NightFell -= value; }
        public event Action DayBroke { add => T.DayBroke += value; remove => T.DayBroke -= value; }

        public int Hour => T.Hour;
        public int Minute => T.Minute;
        public int Day => T.Day;
        /// <summary>Fractional hour, 0 to 24.</summary>
        public float HourOfDay => T.HourOfDay;
        public float TimeOfDay01 => T.TimeOfDay01;
        /// <summary>Game minutes since the start time.</summary>
        public float TotalMinutes => T.TotalMinutes;
        public bool IsNight => T.IsNight;
        public float SunriseHour => sunriseHour;
        public float SunsetHour => sunsetHour;

        /// <summary>From fromHour (inclusive) to toHour (exclusive); wraps past midnight, e.g. IsBetween(23, 1).</summary>
        public bool IsBetween(float fromHour, float toHour) => T.IsBetween(fromHour, toHour);
        /// <summary>Sets HH:MM on the current day (puzzles/debug).</summary>
        public void SetTime(int hour, int minute) => T.SetTime(hour, minute);
        public void AdvanceMinutes(float minutes) => T.Advance(minutes);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameClock] More than one GameClock in the scene; disabling this one.");
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnEnable() => TimeSkipController.Register(this);
        void OnDisable() => TimeSkipController.Unregister(this);

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnValidate()
        {
            if (_time == null) return;
            _time.SunriseHour = sunriseHour;
            _time.SunsetHour = sunsetHour;
        }

        void Update() => T.Advance(Time.deltaTime * minutesPerSecond);

        public void Skip(float seconds)
        {
            float minutes = seconds * minutesPerSecond + skipBonusMinutes;
            T.Advance(minutes);
            Jumped?.Invoke(minutes);
        }
    }
}
