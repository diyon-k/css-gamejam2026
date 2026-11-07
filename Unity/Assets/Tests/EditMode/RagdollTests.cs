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

        [Test]
        public void HeightShare_FeetGetTheirShare_HeadGetsAll_InBetweenIsLinear()
        {
            Assert.AreEqual(0.25f, RagdollMath.HeightShare(0f, 0f, 2f, 0.25f), 1e-5f);
            Assert.AreEqual(1f, RagdollMath.HeightShare(2f, 0f, 2f, 0.25f), 1e-5f);
            Assert.AreEqual(0.625f, RagdollMath.HeightShare(1f, 0f, 2f, 0.25f), 1e-5f);
            Assert.AreEqual(0.25f, RagdollMath.HeightShare(-1f, 0f, 2f, 0.25f), 1e-5f); // clamped
            Assert.AreEqual(1f, RagdollMath.HeightShare(5f, 0f, 2f, 0.25f), 1e-5f);
        }

        [Test]
        public void HeightShare_FlatBody_GetsAllOfIt()
        {
            Assert.AreEqual(1f, RagdollMath.HeightShare(1f, 1f, 1f, 0.25f), 1e-5f);
        }

        [Test]
        public void LaunchVelocity_FlattensTheBlow_AndAddsLift()
        {
            // A chop from eye height angled down and to the right.
            var v = RagdollMath.LaunchVelocity(new Vector3(1f, -1f, 0f), Vector3.up, 8f, 3f);
            Assert.AreEqual(8f, v.x, 1e-4f);
            Assert.AreEqual(3f, v.y, 1e-4f);
            Assert.AreEqual(0f, v.z, 1e-4f);
        }

        [Test]
        public void LaunchVelocity_IgnoresDirectionLength()
        {
            var a = RagdollMath.LaunchVelocity(new Vector3(0f, 0.2f, 0.01f), Vector3.up, 9f, 2f);
            var b = RagdollMath.LaunchVelocity(new Vector3(0f, 0.2f, 50f), Vector3.up, 9f, 2f);
            Assert.AreEqual(9f, a.z, 1e-4f);
            Assert.AreEqual(2f, a.y, 1e-4f);
            Assert.AreEqual(0f, Vector3.Distance(a, b), 1e-4f);
        }

        [Test]
        public void LaunchVelocity_StraightDown_IsLiftOnly_AndNegativesAreZero()
        {
            Assert.AreEqual(new Vector3(0f, 3f, 0f), RagdollMath.LaunchVelocity(Vector3.down, Vector3.up, 8f, 3f));
            Assert.AreEqual(Vector3.zero, RagdollMath.LaunchVelocity(Vector3.forward, Vector3.up, -8f, -3f));
            Assert.AreEqual(Vector3.zero, RagdollMath.LaunchVelocity(Vector3.zero, Vector3.up, 8f, 0f));
        }

        [Test]
        public void LaunchVelocity_ZeroUp_FallsBackToWorldUp()
        {
            var v = RagdollMath.LaunchVelocity(Vector3.right, Vector3.zero, 5f, 1f);
            Assert.AreEqual(new Vector3(5f, 1f, 0f), v);
        }

        [Test]
        public void LaunchEnded_WhenSlowOrAfterMaxTime()
        {
            Assert.IsFalse(RagdollMath.LaunchEnded(0.2f, 3f, 8f, 2f));  // flying
            Assert.IsTrue(RagdollMath.LaunchEnded(0.2f, 3f, 1.5f, 2f));  // slowed down (hit a wall, landed)
            Assert.IsTrue(RagdollMath.LaunchEnded(3f, 3f, 8f, 2f));      // flying too long (fell off the world)
        }

        // ---- Flight (shared by axe kills, axe hits on corpses and knockdowns) ----

        const float Dt = 0.02f;

        [Test]
        public void Flight_EndsOnceSlow_AndReportsItOnce()
        {
            var flight = new RagdollFlight(3f, 2f);
            Assert.IsFalse(flight.IsFlying);
            Assert.IsFalse(flight.Advance(Dt, 0f)); // never launched: nothing to end
            flight.Begin();
            Assert.IsFalse(flight.Advance(Dt, 8f));
            Assert.IsTrue(flight.IsFlying);
            Assert.IsTrue(flight.Advance(Dt, 1.5f));
            Assert.IsFalse(flight.IsFlying);
            Assert.IsFalse(flight.Advance(Dt, 0f)); // already over
        }

        [Test]
        public void Flight_EndsAfterMaxSeconds_EvenWhileFast()
        {
            var flight = new RagdollFlight(1f, 2f);
            flight.Begin();
            for (int i = 0; i < 3; i++) Assert.IsFalse(flight.Advance(0.25f, 9f));
            Assert.IsTrue(flight.Advance(0.25f, 9f));
            Assert.AreEqual(1f, flight.Elapsed, 1e-4f);
        }

        [Test]
        public void Flight_NoTimePassed_NeverEndsIt()
        {
            var flight = new RagdollFlight(3f, 2f);
            flight.Begin();
            Assert.IsFalse(flight.Advance(0f, 0f));  // a frozen-world frame (grab QTE) with the body still
            Assert.IsFalse(flight.Advance(-1f, 0f));
            Assert.IsTrue(flight.IsFlying);
            Assert.AreEqual(0f, flight.Elapsed);
        }

        [Test]
        public void Flight_BeginAgain_RestartsIt_AndEndStopsIt()
        {
            var flight = new RagdollFlight(1f, 2f);
            flight.Begin();
            for (int i = 0; i < 40; i++) flight.Advance(Dt, 9f);
            flight.Begin(); // a second blow mid-flight
            Assert.AreEqual(0f, flight.Elapsed);
            for (int i = 0; i < 40; i++) Assert.IsFalse(flight.Advance(Dt, 9f));
            flight.End();
            Assert.IsFalse(flight.IsFlying);
            Assert.IsFalse(flight.Advance(Dt, 0f));
        }

        [Test]
        public void MayFreeze_OnlyASettledDeadBodyOnTheGround()
        {
            Assert.IsTrue(RagdollMath.MayFreeze(true, false, false));
            Assert.IsFalse(RagdollMath.MayFreeze(false, false, false)); // still moving
            Assert.IsFalse(RagdollMath.MayFreeze(true, true, false));   // knocked down: gets up instead
            Assert.IsFalse(RagdollMath.MayFreeze(true, false, true));   // mid-flight
        }

        // Steps a limp body like ZombieRagdoll.Update: speed(t) is its fastest limb at world time t. Returns the time
        // it froze (or -1), and when its flight ended (or -1).
        static float RunLimp(RagdollFlight flight, RagdollSettleClock settle, bool knockedDown, float seconds,
                             System.Func<float, float> speed, out float flightEnd, out int endedCount)
        {
            flightEnd = -1f;
            endedCount = 0;
            for (float t = Dt; t <= seconds + 1e-4f; t += Dt)
            {
                bool freeze = RagdollMath.StepLimp(flight, settle, knockedDown, Dt, speed(t), out bool ended);
                if (ended)
                {
                    endedCount++;
                    if (flightEnd < 0f) flightEnd = t;
                }
                if (freeze) return t;
            }
            return -1f;
        }

        [Test]
        public void StepLimp_KnockedDown_NeverFreezes()
        {
            var flight = new RagdollFlight(3f, 2f);
            var settle = new RagdollSettleClock(3f, 0.08f, 0.5f);
            flight.Begin();
            // Flies, lands at 1.2 s, lies still for a long time.
            float frozeAt = RunLimp(flight, settle, true, 20f, t => t < 1.2f ? 8f : 0f, out float end, out int ended);
            Assert.AreEqual(-1f, frozeAt);
            Assert.AreEqual(1, ended);
            Assert.AreEqual(1.22f, end, 0.03f);  // the flight still ends, so continuous collision goes off
            Assert.AreEqual(0f, settle.Elapsed); // and the settle clock never runs
        }

        [Test]
        public void StepLimp_DeadLaunch_FreezesOnlyAfterItLands()
        {
            var flight = new RagdollFlight(3f, 2f);
            var settle = new RagdollSettleClock(3f, 0.08f, 0.5f);
            flight.Begin();
            // Flies until 1.3 s, tumbles slowly until 1.6 s, then lies still.
            float frozeAt = RunLimp(flight, settle, false, 10f, t => t < 1.3f ? 8f : t < 1.6f ? 1f : 0f, out float end, out _);
            Assert.AreEqual(1.3f, end, 0.03f);
            Assert.AreEqual(2.1f, frozeAt, 0.03f); // 0.5 s at rest after 1.6 s
        }

        [Test]
        public void StepLimp_SettleTimeRunsOutMidFlight_WaitsForTheLanding()
        {
            var flight = new RagdollFlight(3f, 2f);
            var settle = new RagdollSettleClock(1f, 0.08f, 0.5f); // would freeze at 1 s on its own
            flight.Begin();
            float frozeAt = RunLimp(flight, settle, false, 10f, t => t < 1.5f ? 8f : 0f, out float end, out _);
            Assert.AreEqual(1.5f, end, 0.03f);
            Assert.AreEqual(end, frozeAt, 1e-4f); // the step the flight ends, not before
        }

        [Test]
        public void StepLimp_NeverSlowsDown_FreezesWhenTheFlightTimesOut()
        {
            var flight = new RagdollFlight(3f, 2f);
            var settle = new RagdollSettleClock(3f, 0.08f, 0.5f);
            flight.Begin();
            float frozeAt = RunLimp(flight, settle, false, 10f, t => 9f, out float end, out _);
            Assert.AreEqual(3f, end, 0.03f);
            Assert.AreEqual(end, frozeAt, 1e-4f);
        }

        [Test]
        public void StepLimp_KnockedDownThenKilledMidFlight_FreezesAfterTheLanding()
        {
            var flight = new RagdollFlight(3f, 2f);
            flight.Begin();
            // Knocked down and flying for 0.4 s...
            Assert.AreEqual(-1f, RunLimp(flight, new RagdollSettleClock(3f, 0.08f, 0.5f), true, 0.4f, t => 8f, out _, out _));
            Assert.IsTrue(flight.IsFlying);
            // ...then the prop's damage kills it: dead from here on, with a fresh settle clock (ZombieRagdoll.Activate).
            var settle = new RagdollSettleClock(3f, 0.08f, 0.5f);
            float frozeAt = RunLimp(flight, settle, false, 10f, t => t < 0.8f ? 8f : 0f, out float end, out _);
            Assert.AreEqual(0.8f, end, 0.03f);
            Assert.Greater(frozeAt, end);
            Assert.AreEqual(1.3f, frozeAt, 0.03f);
        }

        [Test]
        public void StepLimp_NeverLaunched_FreezesOnTheSettleClockAlone()
        {
            var settle = new RagdollSettleClock(3f, 0.08f, 0.5f);
            float frozeAt = RunLimp(null, settle, false, 10f, t => t < 0.7f ? 4f : 0f, out float end, out int ended);
            Assert.AreEqual(-1f, end);
            Assert.AreEqual(0, ended);
            Assert.AreEqual(1.2f, frozeAt, 0.03f);
        }

        [Test]
        public void StepLimp_AlreadyFrozenBody_OnlyRunsItsFlight()
        {
            // A frozen corpse woken and flung by CorpseShotReaction: that re-freezes it, the ragdoll only ends the flight.
            var flight = new RagdollFlight(3f, 2f);
            flight.Begin();
            float frozeAt = RunLimp(flight, null, false, 10f, t => t < 0.9f ? 6f : 0f, out float end, out _);
            Assert.AreEqual(-1f, frozeAt);
            Assert.AreEqual(0.9f, end, 0.03f);
        }
    }
}
