using System;

namespace Blink.DayNight
{
    /// <summary>
    /// Pure time-of-day maths behind <see cref="GameClock"/>, with no Unity calls so it can be unit-tested.
    /// Time is stored as game minutes since day 1, 00:00. A big jump (a blink skip, SetTime) raises
    /// each event at most once, with the final value: HourChanged doesn't fire for every hour skipped over.
    /// </summary>
    public sealed class GameTime
    {
        public const double MinutesPerDay = 1440.0;

        double _minutes;
        readonly double _startMinutes;

        public float SunriseHour { get; set; }
        public float SunsetHour { get; set; }

        /// <summary>The whole minute changed (HH:MM shows something new).</summary>
        public event Action MinuteChanged;
        /// <summary>The hour changed. Argument: new hour (0-23).</summary>
        public event Action<int> HourChanged;
        /// <summary>Midnight passed. Argument: new day number (starts at 1).</summary>
        public event Action<int> DayChanged;
        public event Action NightFell;
        public event Action DayBroke;

        public GameTime(float startHour, float sunriseHour = 6f, float sunsetHour = 20f)
        {
            _minutes = _startMinutes = WrapHour(startHour) * 60.0;
            SunriseHour = sunriseHour;
            SunsetHour = sunsetHour;
        }

        public int Day => (int)Math.Floor(_minutes / MinutesPerDay) + 1;
        double MinuteOfDay => _minutes - (Day - 1) * MinutesPerDay;
        /// <summary>Fractional hour of the day, 0 to 24 (21.5 = 21:30).</summary>
        public float HourOfDay => (float)(MinuteOfDay / 60.0);
        public int Hour => (int)(MinuteOfDay / 60.0);
        public int Minute => (int)MinuteOfDay % 60;
        /// <summary>0 at midnight, 0.5 at noon.</summary>
        public float TimeOfDay01 => (float)(MinuteOfDay / MinutesPerDay);
        /// <summary>Game minutes elapsed since the start time.</summary>
        public float TotalMinutes => (float)(_minutes - _startMinutes);
        public bool IsNight => !InRange(HourOfDay, SunriseHour, SunsetHour);

        /// <summary>True from <paramref name="fromHour"/> (inclusive) to <paramref name="toHour"/> (exclusive). Wraps past midnight: IsBetween(22, 2).</summary>
        public bool IsBetween(float fromHour, float toHour) => InRange(HourOfDay, fromHour, toHour);

        public static bool InRange(float hour, float fromHour, float toHour)
        {
            float from = WrapHour(fromHour), to = toHour >= 24f ? 24f : WrapHour(toHour); // 0-24 = all day
            return from <= to ? hour >= from && hour < to : hour >= from || hour < to;
        }

        public static float WrapHour(float hour) => ((hour % 24f) + 24f) % 24f;

        public void Advance(float minutes)
        {
            if (minutes > 0f) SetAbsolute(_minutes + minutes);
        }

        /// <summary>Sets the clock to HH:MM on the current day (can go backwards; Day is unchanged).</summary>
        public void SetTime(int hour, int minute)
        {
            double minuteOfDay = WrapHour((hour * 60f + minute) / 60f) * 60.0;
            SetAbsolute((Day - 1) * MinutesPerDay + Math.Round(minuteOfDay));
        }

        void SetAbsolute(double minutes)
        {
            long oldWhole = (long)Math.Floor(_minutes);
            int oldHour = Hour, oldDay = Day;
            bool wasNight = IsNight;

            _minutes = minutes;

            if ((long)Math.Floor(_minutes) != oldWhole) MinuteChanged?.Invoke();
            if (Hour != oldHour || Day != oldDay) HourChanged?.Invoke(Hour);
            if (Day != oldDay) DayChanged?.Invoke(Day);
            if (IsNight != wasNight)
            {
                if (IsNight) NightFell?.Invoke();
                else DayBroke?.Invoke();
            }
        }
    }
}
