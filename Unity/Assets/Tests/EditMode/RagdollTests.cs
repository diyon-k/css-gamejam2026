using Blink.Combat;
using Blink.Mobs;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class RagdollTests
    {
        [Test]
        public void ScaleDamage_AppliesMultiplier()
        {
            Assert.AreEqual(68f, Hitbox.ScaleDamage(34f, 2f), 1e-4f);
            Assert.AreEqual(34f, Hitbox.ScaleDamage(34f, 1f), 1e-4f);
            Assert.AreEqual(17f, Hitbox.ScaleDamage(34f, 0.5f), 1e-4f);
        }

        [Test]
        public void ScaleDamage_NegativeInputsCountAsZero()
        {
            Assert.AreEqual(0f, Hitbox.ScaleDamage(-10f, 2f));
            Assert.AreEqual(0f, Hitbox.ScaleDamage(34f, -1f));
        }

        [Test]
        public void NearestIndex_PicksClosestPoint()
        {
            var points = new[] { new Vector3(0f, 1f, 0f), new Vector3(0f, 1.5f, 0f), new Vector3(0.3f, 1.3f, 0f) };
            Assert.AreEqual(1, RagdollMath.NearestIndex(points, new Vector3(0f, 1.6f, 0.1f)));
            Assert.AreEqual(2, RagdollMath.NearestIndex(points, new Vector3(0.5f, 1.3f, 0f)));
            Assert.AreEqual(0, RagdollMath.NearestIndex(points, new Vector3(0f, -5f, 0f)));
        }

        [Test]
        public void NearestIndex_EmptyIsMinusOne()
        {
            Assert.AreEqual(-1, RagdollMath.NearestIndex(new Vector3[0], Vector3.zero));
        }

        [Test]
        public void SkipSteps_CoversTheSkip_CappedAndRoundedUp()
        {
            Assert.AreEqual(25, RagdollMath.SkipSteps(0.5f, 0.5f, 0.02f));
            Assert.AreEqual(25, RagdollMath.SkipSteps(3f, 0.5f, 0.02f));   // capped
            Assert.AreEqual(6, RagdollMath.SkipSteps(0.11f, 0.5f, 0.02f)); // rounded up
            Assert.AreEqual(0, RagdollMath.SkipSteps(0f, 0.5f, 0.02f));
            Assert.AreEqual(0, RagdollMath.SkipSteps(-1f, 0.5f, 0.02f));
            Assert.AreEqual(0, RagdollMath.SkipSteps(0.5f, 0.5f, 0f));
            Assert.AreEqual(0, RagdollMath.SkipSteps(0.5f, 0f, 0.02f));
        }

        [Test]
        public void SettleClock_FreezesAtMaxTime_EvenWhileMoving()
        {
            var clock = new RagdollSettleClock(3f, 0.1f, 0.5f);
            for (int i = 0; i < 11; i++) Assert.IsFalse(clock.Advance(0.25f, 5f));
            Assert.IsTrue(clock.Advance(0.25f, 5f));
            Assert.IsTrue(clock.IsSettled);
        }

        [Test]
        public void SettleClock_FreezesEarly_OnceAtRestLongEnough()
        {
            var clock = new RagdollSettleClock(3f, 0.1f, 0.5f);
            Assert.IsFalse(clock.Advance(0.5f, 2f));
            Assert.IsFalse(clock.Advance(0.25f, 0.05f));
            Assert.IsTrue(clock.Advance(0.25f, 0.05f));
            Assert.AreEqual(1f, clock.Elapsed, 1e-4f);
        }

        [Test]
        public void SettleClock_MovingAgainResetsRest()
        {
            var clock = new RagdollSettleClock(3f, 0.1f, 0.5f);
            clock.Advance(0.25f, 0.05f);
            clock.Advance(0.125f, 1f); // bounced
            Assert.AreEqual(0f, clock.RestElapsed);
            Assert.IsFalse(clock.Advance(0.25f, 0.05f));
            Assert.IsTrue(clock.Advance(0.25f, 0.05f));
        }

        [Test]
        public void SettleClock_IgnoresNegativeAndZeroTime_AndStaysSettled()
        {
            var clock = new RagdollSettleClock(1f, 0.1f, 0f);
            Assert.IsFalse(clock.Advance(0f, 0f));     // no time passed: a skip frame can't settle it
            Assert.IsFalse(clock.Advance(-1f, 0f));
            Assert.AreEqual(0f, clock.Elapsed);
            Assert.IsTrue(clock.Advance(0.01f, 0f));
            Assert.IsTrue(clock.Advance(0.01f, 9f));   // stays settled
        }
    }
}
