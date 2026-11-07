using Blink.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class TracerFlightTests
    {
        [Test]
        public void EndPoint_OnHit_IsHitPoint()
        {
            var ray = new Ray(Vector3.zero, Vector3.forward);
            var end = TracerFlight.EndPoint(true, new Vector3(1f, 2f, 3f), ray, 60f);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), end);
        }

        [Test]
        public void EndPoint_OnMiss_IsAlongAimRayAtMaxDistance()
        {
            var ray = new Ray(new Vector3(0f, 1.65f, 0f), new Vector3(0f, 0f, 2f));
            var end = TracerFlight.EndPoint(false, new Vector3(9f, 9f, 9f), ray, 60f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 1.65f, 60f), end), 1e-4f);
        }

        [Test]
        public void StreakLength_ScalesWithDistance_AndClamps()
        {
            Assert.AreEqual(3.5f, TracerFlight.StreakLengthFor(10f, 0.35f, 0.4f, 7f), 1e-4f);
            Assert.AreEqual(0.4f, TracerFlight.StreakLengthFor(0.5f, 0.35f, 0.4f, 7f), 1e-4f);
            Assert.AreEqual(7f, TracerFlight.StreakLengthFor(60f, 0.35f, 0.4f, 7f), 1e-4f);
        }

        [Test]
        public void Lifetime_IsTravelOverSpeed_ClampedToReadableRange()
        {
            Assert.AreEqual(0.07f, TracerFlight.LifetimeFor(42f, 7f, 700f, 0.05f, 0.09f), 1e-4f);
            Assert.AreEqual(0.05f, TracerFlight.LifetimeFor(2f, 0.7f, 700f, 0.05f, 0.09f), 1e-4f);
            Assert.AreEqual(0.09f, TracerFlight.LifetimeFor(200f, 7f, 700f, 0.05f, 0.09f), 1e-4f);
        }

        [Test]
        public void Segment_HeadLeadsTail_ThenDrainsIntoEnd()
        {
            var flight = new TracerFlight(10f, 4f, 0.1f);
            flight.Step(0.02f); // first step is capped at 25% of lifetime (0.025), 0.02 is under it
            flight.Segment(out float tail, out float head);
            Assert.AreEqual(2.8f, head, 1e-4f); // 14 m of travel * 0.2
            Assert.AreEqual(0f, tail, 1e-4f);

            flight.Step(0.04f); // age 0.06 -> 8.4 m travelled
            flight.Segment(out tail, out head);
            Assert.AreEqual(8.4f, head, 1e-4f);
            Assert.AreEqual(4.4f, tail, 1e-4f);

            flight.Step(0.03f); // age 0.09 -> 12.6 m: head parked at the end, tail catching up
            flight.Segment(out tail, out head);
            Assert.AreEqual(10f, head, 1e-4f);
            Assert.AreEqual(8.6f, tail, 1e-4f);
            Assert.IsFalse(flight.IsDone);

            flight.Step(0.02f);
            Assert.IsTrue(flight.IsDone);
            flight.Segment(out tail, out head);
            Assert.AreEqual(10f, tail, 1e-4f);
            Assert.AreEqual(10f, head, 1e-4f);
        }

        [Test]
        public void FirstStep_IsCapped_SoAHugeFrameStillShowsTheTracer()
        {
            var flight = new TracerFlight(20f, 5f, 0.05f);
            flight.Step(0.2f); // e.g. a long frame at 3x time scale
            Assert.IsFalse(flight.IsDone);
            Assert.AreEqual(0.05f * TracerFlight.FirstFrameMaxAge01, flight.Age, 1e-5f);

            flight.Step(0.2f); // later steps aren't capped
            Assert.IsTrue(flight.IsDone);
        }

        [Test]
        public void TimeScaleChangeMidFlight_OnlyChangesRate()
        {
            var a = new TracerFlight(30f, 6f, 0.09f);
            var b = new TracerFlight(30f, 6f, 0.09f);
            a.Step(0.01f); a.Step(0.01f); a.Step(0.03f);   // 1x, 1x, then 3x
            b.Step(0.01f); b.Step(0.04f);
            Assert.AreEqual(a.Age, b.Age, 1e-5f);

            a.Step(0f); // timeScale 0 holds it in place without breaking
            Assert.AreEqual(b.Age, a.Age, 1e-5f);
            a.Step(-1f); // negative deltas are ignored
            Assert.AreEqual(b.Age, a.Age, 1e-5f);
        }

        [Test]
        public void Fade_StartsFull_EndsAtZero_AndOnlyDecreases()
        {
            Assert.AreEqual(1f, TracerFlight.Fade(0f), 1e-5f);
            Assert.AreEqual(0f, TracerFlight.Fade(1f), 1e-5f);
            Assert.AreEqual(0.75f, TracerFlight.Fade(0.5f), 1e-5f);
            float previous = 1f;
            for (int i = 1; i <= 20; i++)
            {
                float f = TracerFlight.Fade(i / 20f);
                Assert.LessOrEqual(f, previous);
                previous = f;
            }
        }

        [Test]
        public void ScreenConstantWidth_IsProportionalToDistance_WithinLimits()
        {
            Assert.AreEqual(0.028f, TracerFlight.ScreenConstantWidth(10f, 0.0028f, 0.002f, 0.06f), 1e-5f);
            Assert.AreEqual(0.002f, TracerFlight.ScreenConstantWidth(0.3f, 0.0028f, 0.002f, 0.06f), 1e-5f);
            Assert.AreEqual(0.06f, TracerFlight.ScreenConstantWidth(100f, 0.0028f, 0.002f, 0.06f), 1e-5f);
        }
    }
}
