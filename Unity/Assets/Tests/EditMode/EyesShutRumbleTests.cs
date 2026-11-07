using Blink.Audio;
using Blink.Effects;
using NUnit.Framework;

namespace Blink.Tests
{
    /// <summary>
    /// The eyes-shut rumble with its default tuning: drone 0.08 → 0.16, lub low 0.45 → 1 / high 0 → 0.3, dub 0.6×,
    /// lub 0.08 s, dub at 0.18 s for 0.07 s, jolt (1, 1) for 0.25 s. All times are real (unscaled) seconds.
    /// </summary>
    public sealed class EyesShutRumbleTests
    {
        const float Eps = 1e-4f;

        // PlayerAudio's defaults: the felt heartbeat uses the same curve as the heard one.
        const float PanicDistance = 2f, Range = 15f, RestBpm = 65f, PanicBpm = 170f;

        EyesShutRumble _r;

        [SetUp]
        public void SetUp() => _r = new EyesShutRumble();

        static void AssertRumble(float low, float high, Rumble actual, string message = null)
        {
            Assert.AreEqual(low, actual.Low, Eps, message);
            Assert.AreEqual(high, actual.High, Eps, message);
        }

        [Test]
        public void EyesOpen_IsOff()
        {
            for (float t = 0f; t < 5f; t += 0.1f) Assert.IsTrue(_r.Sample(t, 1f).IsOff);
        }

        [Test]
        public void Drone_SwellsIn_ThenKeepsGoingForTheWholeShut()
        {
            _r.Begin(10f);
            AssertRumble(0f, 0f, _r.Sample(10f, 0f));
            AssertRumble(0.04f, 0f, _r.Sample(10.15f, 0f)); // halfway through the 0.3 s swell
            for (float t = 10.3f; t < 70f; t += 0.25f) // a minute, no zombie in range, no beats: never drops out
                AssertRumble(0.08f, 0f, _r.Sample(t, 0f), $"t={t}");
        }

        [Test]
        public void Drone_RisesAsTheZombieGetsCloser()
        {
            _r.Begin(0f);
            AssertRumble(0.08f, 0f, _r.Sample(1f, 0f));
            AssertRumble(0.12f, 0f, _r.Sample(1f, 0.5f));
            AssertRumble(0.16f, 0f, _r.Sample(1f, 1f));
        }

        [Test]
        public void Beat_IsLub_Gap_Dub_ThenBackToTheDrone()
        {
            _r.Begin(0f);
            _r.Beat(1f, 0.5f, 120f);
            AssertRumble(0.725f, 0.15f, _r.Sample(1.04f, 0.5f), "lub");
            AssertRumble(0.12f, 0f, _r.Sample(1.12f, 0.5f), "gap: drone only");
            AssertRumble(0.435f, 0.09f, _r.Sample(1.21f, 0.5f), "dub = 0.6 lub");
            AssertRumble(0.12f, 0f, _r.Sample(1.3f, 0.5f), "after: drone only");
        }

        [Test]
        public void Beat_IsStrongerWhenTheZombieIsCloser()
        {
            _r.Begin(0f);
            _r.Beat(1f, 0f, RestBpm);
            var far = _r.Sample(1.01f, 0f);
            _r.Beat(2f, 1f, PanicBpm);
            var near = _r.Sample(2.01f, 1f);
            AssertRumble(0.45f, 0f, far);
            AssertRumble(1f, 0.3f, near);
        }

        [Test]
        public void FastRates_SqueezeTheBeat_SoTheDubEndsBeforeTheNextLub()
        {
            _r.Begin(0f);
            _r.Beat(1f, 1f, 400f); // 0.15 s period: lub-dub squeezed to 0.48×
            Assert.Greater(_r.Sample(1.09f, 1f).Low, 0.16f, "dub, early");
            AssertRumble(0.16f, 0f, _r.Sample(1.13f, 1f), "quiet before the next beat");
        }

        /// <summary>
        /// Feeds 10 s of real-time frames through the same HeartbeatMath + HeartbeatClock as PlayerAudio, beating the
        /// rumble on every heard beat. One lub and one dub per beat, rate from the bpm curve, stronger up close.
        /// </summary>
        [TestCase(14f)]
        [TestCase(8f)]
        [TestCase(2f)]
        public void PulseEnvelopeOverTime_FollowsTheHeardHeartbeat(float distance)
        {
            float intensity = HeartbeatMath.Intensity(distance, PanicDistance, Range);
            float bpm = HeartbeatMath.Bpm(intensity, RestBpm, PanicBpm);
            var clock = new HeartbeatClock();
            _r.Begin(0f);

            const float dt = 1f / 60f;
            int heard = 0, felt = 0;
            float peak = 0f;
            bool wasPulsing = false;
            float drone = _r.Sample(1f, intensity).Low;
            for (int i = 0; i < 660; i++) // 10 s of beats, then 1 s so the last dub plays out
            {
                float now = i * dt;
                if (i < 600 && clock.Tick(dt, bpm)) { heard++; _r.Beat(now, intensity, bpm); }
                var m = _r.Sample(now, intensity);
                Assert.GreaterOrEqual(m.Low, now >= 0.3f ? drone - Eps : 0f, "the drone never drops out");
                bool pulsing = m.Low > drone + Eps;
                if (pulsing && !wasPulsing) felt++;
                wasPulsing = pulsing;
                peak = UnityEngine.Mathf.Max(peak, m.Low);
            }

            Assert.AreEqual(bpm / 6f, heard, 1f, "beats in 10 s");
            Assert.AreEqual(2 * heard, felt, "a lub and a dub for every heard beat");
            Assert.AreEqual(UnityEngine.Mathf.Lerp(0.45f, 1f, intensity), peak, Eps);
        }

        [Test]
        public void NoZombieInRange_NoBeats_DroneOnly()
        {
            // PlayerAudio raises no beats at intensity 0 (silence = safe), so only the drone is felt.
            Assert.AreEqual(0f, HeartbeatMath.Intensity(20f, PanicDistance, Range));
            _r.Begin(0f);
            for (float t = 0.3f; t < 10f; t += 0.1f) AssertRumble(0.08f, 0f, _r.Sample(t, 0f));
        }

        [Test]
        public void OpeningTheEyes_StopsAtOnce_EvenMidBeat()
        {
            _r.Begin(0f);
            _r.Beat(1f, 1f, PanicBpm);
            Assert.IsFalse(_r.Sample(1.02f, 1f).IsOff);
            _r.End(1.03f, interrupted: false);
            Assert.IsFalse(_r.Active);
            Assert.IsTrue(_r.Sample(1.03f, 1f).IsOff);
            Assert.IsTrue(_r.Sample(1.05f, 1f).IsOff, "the rest of the lub is dropped");
        }

        [Test]
        public void BeatsAfterTheEyesOpen_AreIgnored()
        {
            _r.Begin(0f);
            _r.End(1f, false);
            _r.Beat(2f, 1f, PanicBpm);
            for (float t = 2f; t < 3f; t += 0.01f) Assert.IsTrue(_r.Sample(t, 1f).IsOff);
        }

        [Test]
        public void GrabInterrupt_JoltsHard_ThenStops()
        {
            _r.Begin(0f);
            _r.Beat(4.9f, 1f, PanicBpm);
            _r.End(5f, interrupted: true);
            AssertRumble(1f, 1f, _r.Sample(5f, 1f));
            AssertRumble(1f, 1f, _r.Sample(5.24f, 1f));
            Assert.IsTrue(_r.Sample(5.25f, 1f).IsOff);
            _r.Beat(5.5f, 1f, PanicBpm); // eyes still shut after the grab, heart still racing: nothing
            for (float t = 5.25f; t < 65f; t += 0.5f) Assert.IsTrue(_r.Sample(t, 1f).IsOff, $"t={t}");
        }

        [Test]
        public void Stop_CutsTheJoltToo()
        {
            _r.Begin(0f);
            _r.End(1f, true);
            _r.Stop();
            Assert.IsTrue(_r.Sample(1.01f, 1f).IsOff);
        }

        [Test]
        public void ShuttingAgainDuringAJolt_StartsAFreshDrone()
        {
            _r.Begin(0f);
            _r.End(1f, true);
            _r.Begin(1.1f);
            AssertRumble(0.08f, 0f, _r.Sample(1.4f, 0f));
        }

        [Test]
        public void NeverStuckOn_AfterAnySequence()
        {
            var rng = new System.Random(1234);
            for (int run = 0; run < 200; run++)
            {
                var r = new EyesShutRumble();
                float now = 0f;
                for (int step = 0; step < 30; step++)
                {
                    now += (float)rng.NextDouble() * 0.5f;
                    switch (rng.Next(3))
                    {
                        case 0: r.Begin(now); break;
                        case 1: r.End(now, rng.Next(2) == 0); break;
                        default: r.Beat(now, (float)rng.NextDouble(), 65f + 105f * (float)rng.NextDouble()); break;
                    }
                }
                r.End(now, rng.Next(2) == 0);
                for (float t = now + r.JoltSeconds + 0.001f; t < now + 30f; t += 0.37f)
                    Assert.IsTrue(r.Sample(t, 1f).IsOff, $"run {run}, t={t}");
            }
        }
    }
}
