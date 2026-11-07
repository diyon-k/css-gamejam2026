using Blink.Player;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class GrabRingTests
    {
        // Rings meet the target 1.2 s after appearing, ±0.15 s to hit, window shrinks 0.015 s per hit.
        static GrabRing Make(float introSeconds = 0f) =>
            new(beats: 3, meetSeconds: 1.2f, windowSeconds: 0.15f, windowShrinkPerBeat: 0.015f, gapSeconds: 0.2f, introSeconds: introSeconds);

        /// <summary>Presses the key the ring wants.</summary>
        static GrabRing.Result PressRight(GrabRing r) => r.Press(r.Key);

        static GrabKey Other(GrabKey k) => (GrabKey)(((int)k + 1) % 4);

        [Test]
        public void WrongKeyOnTheBeat_Misses()
        {
            var r = Make();
            r.Tick(1.2f);
            Assert.AreEqual(GrabRing.Result.Miss, r.Press(Other(r.Key)));
            Assert.AreEqual(1, r.Misses);
            Assert.AreEqual(0, r.Hits);
            Assert.IsFalse(r.Finished);
        }

        [Test]
        public void EachNewRing_AsksForADifferentKey()
        {
            var r = Make();
            for (int i = 0; i < 50; i++)
            {
                GrabKey before = r.Key;
                r.Tick(0.5f);
                r.Press(before); // too early: a miss, and a new ring
                Assert.AreNotEqual(before, r.Key);
            }
        }

        [Test]
        public void PickKey_CoversAllFour_AndNeverRepeats()
        {
            var rng = new System.Random(3);
            var seen = new System.Collections.Generic.HashSet<GrabKey>();
            GrabKey? last = null;
            for (int i = 0; i < 400; i++)
            {
                GrabKey k = GrabRing.PickKey(() => (float)rng.NextDouble(), last);
                if (last.HasValue) Assert.AreNotEqual(last.Value, k);
                seen.Add(k);
                last = k;
            }
            Assert.AreEqual(4, seen.Count);
            // Edge of the random range still lands on a valid key.
            Assert.AreNotEqual(GrabKey.Right, GrabRing.PickKey(() => 1f, GrabKey.Right));
            Assert.That((int)GrabRing.PickKey(() => 1f, null), Is.InRange(0, 3));
        }

        [Test]
        public void PressOnTheBeat_Hits()
        {
            var r = Make();
            r.Tick(1.2f);
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(r));
            Assert.AreEqual(1, r.Hits);
            Assert.IsFalse(r.Finished);
        }

        [Test]
        public void PressInsideTheWindowEitherSide_Hits()
        {
            var early = Make();
            early.Tick(1.06f);
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(early));

            var late = Make();
            late.Tick(1.34f);
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(late));
        }

        [Test]
        public void PressTooEarly_Misses_AndANewRingFollows()
        {
            var r = Make();
            r.Tick(0.9f);
            Assert.AreEqual(GrabRing.Result.Miss, PressRight(r));
            Assert.AreEqual(1, r.Misses);
            Assert.AreEqual(0, r.Hits);
            Assert.IsFalse(r.Finished); // a miss costs health, not the whole grab
            Assert.IsFalse(r.RingVisible);
            Assert.AreEqual(GrabRing.Result.None, PressRight(r)); // mashing in the gap isn't another miss

            r.Tick(0.2f + 1.2f);
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(r));
        }

        [Test]
        public void LettingTheRingPass_TimesOut_AndANewRingFollows()
        {
            var r = Make();
            Assert.AreEqual(GrabRing.Result.None, r.Tick(1.3f));
            Assert.AreEqual(GrabRing.Result.TimedOut, r.Tick(0.1f));
            Assert.AreEqual(1, r.Misses);
            Assert.IsFalse(r.Finished);
            Assert.IsFalse(r.RingVisible);
            r.Tick(0.2f);
            Assert.IsTrue(r.RingVisible);
        }

        [Test]
        public void Stop_EndsIt_AndIgnoresEverythingAfter()
        {
            var r = Make();
            r.Stop();
            Assert.IsTrue(r.Finished);
            Assert.IsFalse(r.Escaped);
            Assert.AreEqual(GrabRing.Result.None, r.Tick(5f));
            Assert.AreEqual(GrabRing.Result.None, PressRight(r));
        }

        [Test]
        public void AMiss_DoesNotShrinkTheWindow()
        {
            var r = Make();
            r.Tick(0.5f);
            PressRight(r); // miss
            Assert.AreEqual(0.15f, r.CurrentWindow, 1e-5f);
        }

        [Test]
        public void ThreeHits_Escape()
        {
            var r = Make();
            r.Tick(1.2f); // the first ring has no gap
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(r));
            r.Tick(0.2f + 1.2f); // gap, then the ring closes
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(r));
            r.Tick(0.2f + 1.2f);
            Assert.AreEqual(GrabRing.Result.Escaped, PressRight(r));
            Assert.IsTrue(r.Finished);
            Assert.IsTrue(r.Escaped);
        }

        [Test]
        public void FirstRing_HasNoGap()
        {
            var r = Make();
            Assert.IsTrue(r.RingVisible);
        }

        [Test]
        public void AfterAHit_NextRingWaitsForTheGap_AndIgnoresPresses()
        {
            var r = Make();
            r.Tick(1.2f);
            PressRight(r);
            Assert.IsFalse(r.RingVisible);
            Assert.AreEqual(GrabRing.Result.None, PressRight(r)); // a double tap doesn't fail you
            r.Tick(0.2f);
            Assert.IsTrue(r.RingVisible);
            Assert.AreEqual(0f, r.RingSeconds, 1e-5f);
        }

        [Test]
        public void Intro_HidesTheRing_AndIgnoresPresses()
        {
            var r = Make(introSeconds: 0.5f);
            Assert.IsFalse(r.RingVisible);
            Assert.AreEqual(GrabRing.Result.None, PressRight(r)); // Space still held from the blink
            r.Tick(0.5f + 1.2f);
            Assert.AreEqual(GrabRing.Result.Hit, PressRight(r));
        }

        [Test]
        public void Window_ShrinksEachHit()
        {
            var r = Make();
            Assert.AreEqual(0.15f, r.CurrentWindow, 1e-5f);
            r.Tick(1.2f);
            PressRight(r);
            Assert.AreEqual(0.135f, r.CurrentWindow, 1e-5f);

            r.Tick(0.2f + 1.2f - 0.14f); // 0.14 s early: inside the first window, outside this one
            Assert.AreEqual(GrabRing.Result.Miss, PressRight(r));
        }

        [Test]
        public void RingScale_IsOneWhenItMeetsTheTarget_AndBiggerBefore()
        {
            var r = Make();
            Assert.AreEqual(GrabRing.StartScale, r.RingScale, 1e-4f);
            r.Tick(0.6f);
            Assert.Greater(r.RingScale, 1f);
            Assert.Less(r.RingScale, GrabRing.StartScale);
            r.Tick(0.6f);
            Assert.AreEqual(1f, r.RingScale, 1e-4f);
            r.Tick(0.1f);
            Assert.Less(r.RingScale, 1f); // keeps closing past the target
            Assert.Greater(r.RingScale, 0f);
        }

        [Test]
        public void PickSpot_StaysOnScreen_OutOfTheMiddle_AndAwayFromTheLastSpot()
        {
            var rng = new System.Random(7);
            Vector2? last = null;
            for (int i = 0; i < 500; i++)
            {
                Vector2 spot = GrabRing.PickSpot(() => (float)rng.NextDouble(), last, aspect: 16f / 9f);
                Assert.That(spot.x, Is.InRange(GrabRing.EdgeMargin, 1f - GrabRing.EdgeMargin));
                Assert.That(spot.y, Is.InRange(GrabRing.EdgeMargin, 1f - GrabRing.EdgeMargin));
                Assert.GreaterOrEqual(GrabRing.HeightDistance(spot, new Vector2(0.5f, 0.5f), 16f / 9f), GrabRing.CentreClearance - 1e-4f);
                if (last.HasValue)
                    Assert.GreaterOrEqual(GrabRing.HeightDistance(spot, last.Value, 16f / 9f), GrabRing.MinJump - 1e-4f);
                last = spot;
            }
        }

        [Test]
        public void PickSpot_AlwaysReturns_EvenWithABadRandomSource()
        {
            // Always 0.5: every candidate is dead centre. It must still give a usable spot.
            Vector2 spot = GrabRing.PickSpot(() => 0.5f, null, aspect: 16f / 9f);
            Assert.GreaterOrEqual(GrabRing.HeightDistance(spot, new Vector2(0.5f, 0.5f), 16f / 9f), GrabRing.CentreClearance - 1e-4f);
        }
    }
}
