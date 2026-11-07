using System.Collections.Generic;
using Blink.Combat;
using Blink.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class WeaponLogicTests
    {
        const float Lower = 0.12f, Raise = 0.14f;

        // ---------------- switching ----------------

        [Test]
        public void Switch_LowersSwapsAtTheBottomThenRaises()
        {
            var s = new WeaponSwitchState(1);
            Assert.IsTrue(s.Request(2));

            var first = s.Advance(0.06f, Lower, Raise, true);
            Assert.IsTrue(first.Started);
            Assert.IsFalse(first.Swapped);
            Assert.AreEqual(1, s.Active);
            Assert.AreEqual(0.5f, s.Lowered01, 1e-4f);

            var bottom = s.Advance(0.07f, Lower, Raise, true);
            Assert.IsTrue(bottom.Swapped);
            Assert.AreEqual(1, bottom.From);
            Assert.AreEqual(2, bottom.To);
            Assert.AreEqual(2, s.Active);
            Assert.IsTrue(s.IsSwitching, "still raising");

            s.Advance(Raise, Lower, Raise, true);
            Assert.AreEqual(0f, s.Lowered01);
            Assert.IsFalse(s.IsSwitching);
        }

        [Test]
        public void Switch_WaitsWhileTheWeaponCannotLeave()
        {
            var s = new WeaponSwitchState(3);
            s.Request(0);
            var step = s.Advance(0.1f, Lower, Raise, canLeave: false);
            Assert.IsFalse(step.Started);
            Assert.AreEqual(0f, s.Lowered01);
            Assert.IsTrue(s.IsSwitching, "the request is pending");

            Assert.IsTrue(s.Advance(0.01f, Lower, Raise, canLeave: true).Started);
        }

        [Test]
        public void Switch_AskingForTheSameSlotAgainDoesNothing()
        {
            var s = new WeaponSwitchState(1);
            Assert.IsFalse(s.Request(1));
            Assert.IsFalse(s.Advance(1f, Lower, Raise, true).Started);
            Assert.IsFalse(s.IsSwitching);
        }

        [Test]
        public void Switch_ChangingYourMindMidLowerRaisesTheSameWeapon()
        {
            var s = new WeaponSwitchState(1);
            s.Request(2);
            s.Advance(0.06f, Lower, Raise, true);
            s.Request(1);
            var step = s.Advance(0.07f, Lower, Raise, true);
            Assert.IsFalse(step.Swapped);
            Assert.AreEqual(1, s.Active);
            Assert.Less(s.Lowered01, 0.5f);
        }

        [Test]
        public void Switch_RetargetingWhileRaisingGoesDownAgainWithoutASnap()
        {
            var s = new WeaponSwitchState(0);
            s.Request(1);
            s.Advance(Lower, Lower, Raise, true); // swapped to 1, fully down
            s.Advance(Raise * 0.5f, Lower, Raise, true); // half up
            float height = s.Lowered01;

            s.Request(2);
            var step = s.Advance(0.001f, Lower, Raise, canLeave: false); // the weapon wasn't usable yet
            Assert.IsTrue(step.Started);
            Assert.AreEqual(height, s.Lowered01, 0.02f);
            s.Advance(1f, Lower, Raise, true);
            Assert.AreEqual(2, s.Active);
        }

        [TestCase(0, 1, 4, 1)]
        [TestCase(3, 1, 4, 0)]
        [TestCase(0, -1, 4, 3)]
        [TestCase(2, -5, 4, 1)]
        public void Cycle_WrapsAround(int current, int delta, int count, int expected) =>
            Assert.AreEqual(expected, WeaponSwitchState.Cycle(current, delta, count));

        // ---------------- melee ----------------

        [Test]
        public void Melee_PicksTheNearestLivingVisibleTarget()
        {
            var c = new List<MeleeCandidate>
            {
                new(0.4f, true, false, true), // dead
                new(0.6f, true, true, false), // behind a wall
                new(1.2f, true, true, true),
                new(0.9f, true, true, true),
                new(0.2f, false, false, true), // wall
            };
            Assert.AreEqual(3, MeleeTargeting.Choose(c, out bool surface));
            Assert.IsFalse(surface);
        }

        [Test]
        public void Melee_FallsBackToTheNearestSurface()
        {
            var c = new List<MeleeCandidate> { new(1.5f, false, false, true), new(0.7f, false, false, true) };
            Assert.AreEqual(1, MeleeTargeting.Choose(c, out bool surface));
            Assert.IsTrue(surface);
        }

        [Test]
        public void Melee_NothingInReach()
        {
            Assert.AreEqual(-1, MeleeTargeting.Choose(new List<MeleeCandidate>(), out bool surface));
            Assert.IsFalse(surface);
        }

        // ---------------- headshots ----------------

        [Test]
        public void Head_AnyDamagingHitTakesEverythingLeft()
        {
            Assert.AreEqual(100f, Hitbox.DamageFor(15f, 2f, true, 100f));
            Assert.AreEqual(100f, Hitbox.DamageFor(1f, 1f, true, 100f));
            Assert.AreEqual(150f, Hitbox.DamageFor(75f, 2f, true, 100f), "never less than the scaled damage");
        }

        [Test]
        public void Head_HarmlessHitStaysHarmless() => Assert.AreEqual(0f, Hitbox.DamageFor(0f, 2f, true, 100f));

        [Test]
        public void Body_DamageIsJustScaled() => Assert.AreEqual(30f, Hitbox.DamageFor(15f, 2f, false, 100f));

        [Test]
        public void Blood_BodyHitUsesTheSpawnerSettings()
        {
            var b = BloodBurst.For(false, 0.7f, 0.6f, HeadshotBlood.Default);
            Assert.AreEqual(1f, b.Scale);
            Assert.AreEqual(1f, b.CountMultiplier);
            Assert.AreEqual(0.7f, b.ExitScale, 1e-5f);
            Assert.AreEqual(0.6f, b.FloorChance, 1e-5f);
            Assert.AreEqual(0, b.ExtraBursts);
        }

        [Test]
        public void Blood_HeadshotIsStrongerEverywhereAndAlwaysSplatsTheFloor()
        {
            var body = BloodBurst.For(false, 0.7f, 0.6f, HeadshotBlood.Default);
            var head = BloodBurst.For(true, 0.7f, 0.6f, HeadshotBlood.Default);
            Assert.Greater(head.Scale, body.Scale);
            Assert.Greater(head.CountMultiplier, body.CountMultiplier);
            Assert.Greater(head.ExitScale, body.ExitScale);
            Assert.Greater(head.ExtraBursts, 0);
            Assert.AreEqual(1f, head.FloorChance);
        }

        [Test]
        public void Blood_HeadshotIsNeverWeakerEvenWithLowSettings()
        {
            var head = BloodBurst.For(true, 0.7f, 0.6f, new HeadshotBlood(0.5f, 0.2f, 0.1f, -1, 10f));
            Assert.AreEqual(1f, head.Scale);
            Assert.AreEqual(1f, head.CountMultiplier);
            Assert.AreEqual(0.7f, head.ExitScale, 1e-5f);
            Assert.AreEqual(0, head.ExtraBursts);
        }

        [TestCase(10, 2.5f, 25)]
        [TestCase(1, 1.2f, 1)]
        [TestCase(4, 0.5f, 4)]
        public void Blood_ScaleCountRoundsAndNeverShrinks(int count, float multiplier, int expected) =>
            Assert.AreEqual(expected, BloodBurst.ScaleCount(count, multiplier));
    }
}
