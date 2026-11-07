using Blink.DayNight;
using NUnit.Framework;

namespace Blink.Tests
{
    public class GameTimeTests
    {
        [Test]
        public void StartsAtStartHour_OnDayOne()
        {
            var t = new GameTime(21f);
            Assert.AreEqual(21, t.Hour);
            Assert.AreEqual(0, t.Minute);
            Assert.AreEqual(1, t.Day);
            Assert.AreEqual(0f, t.TotalMinutes);
            Assert.IsTrue(t.IsNight);
        }

        [Test]
        public void SkipJump_AdvancesMinutesAndHour_FiresEachEventOnce()
        {
            var t = new GameTime(21f);
            int minuteEvents = 0, lastHour = -1, hourEvents = 0;
            t.MinuteChanged += () => minuteEvents++;
            t.HourChanged += h => { hourEvents++; lastHour = h; };

            t.Advance(15f);
            Assert.AreEqual(21, t.Hour);
            Assert.AreEqual(15, t.Minute);
            Assert.AreEqual(1, minuteEvents);
            Assert.AreEqual(0, hourEvents);

            t.Advance(150f); // 21:15 -> 23:45, skips over 22:00
            Assert.AreEqual(23, t.Hour);
            Assert.AreEqual(45, t.Minute);
            Assert.AreEqual(1, hourEvents);
            Assert.AreEqual(23, lastHour);
            Assert.AreEqual(165f, t.TotalMinutes, 1e-3f);
        }

        [Test]
        public void SmallSteps_OnlyFireMinuteChangedOnWholeMinutes()
        {
            var t = new GameTime(10f);
            int minuteEvents = 0;
            t.MinuteChanged += () => minuteEvents++;
            for (int i = 0; i < 10; i++) t.Advance(0.25f);
            Assert.AreEqual(2, minuteEvents);
            Assert.AreEqual(2, t.Minute);
        }

        [Test]
        public void WrapsPastMidnight_AndCountsDays()
        {
            var t = new GameTime(23.5f);
            int newDay = 0;
            t.DayChanged += d => newDay = d;

            t.Advance(45f);
            Assert.AreEqual(0, t.Hour);
            Assert.AreEqual(15, t.Minute);
            Assert.AreEqual(2, t.Day);
            Assert.AreEqual(2, newDay);

            t.Advance(24 * 60f);
            Assert.AreEqual(3, t.Day);
            Assert.AreEqual(0, t.Hour);
            Assert.AreEqual(15, t.Minute);
        }

        [Test]
        public void NightFellAndDayBroke_FireOnSunriseAndSunset()
        {
            var t = new GameTime(5.5f, sunriseHour: 6f, sunsetHour: 20f);
            int dawns = 0, dusks = 0;
            t.DayBroke += () => dawns++;
            t.NightFell += () => dusks++;

            t.Advance(30f);
            Assert.IsFalse(t.IsNight);
            Assert.AreEqual(1, dawns);

            t.Advance(14 * 60f);
            Assert.IsTrue(t.IsNight);
            Assert.AreEqual(1, dusks);
        }

        [Test]
        public void IsBetween_HandlesNormalAndWrappingRanges()
        {
            Assert.IsTrue(GameTime.InRange(12f, 9f, 17f));
            Assert.IsFalse(GameTime.InRange(17f, 9f, 17f)); // end exclusive
            Assert.IsTrue(GameTime.InRange(23f, 22f, 2f));
            Assert.IsTrue(GameTime.InRange(1.5f, 22f, 2f));
            Assert.IsFalse(GameTime.InRange(12f, 22f, 2f));
            Assert.IsTrue(GameTime.InRange(23.9f, 0f, 24f)); // 0-24 = all day
            Assert.IsTrue(GameTime.InRange(23f, 22f, 24f));
        }

        [Test]
        public void SetTime_StaysOnSameDay()
        {
            var t = new GameTime(21f);
            t.Advance(4 * 60f); // day 2, 01:00
            t.SetTime(23, 59);
            Assert.AreEqual(2, t.Day);
            Assert.AreEqual(23, t.Hour);
            Assert.AreEqual(59, t.Minute);
            Assert.IsTrue(t.IsBetween(23f, 0f));
        }
    }
}
