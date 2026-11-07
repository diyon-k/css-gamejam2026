using Blink.Mobs;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class HitReactionTests
    {
        [Test]
        public void Strength_IsOneAtTheReference_GrowsSlowerAbove_AndIsCapped()
        {
            Assert.AreEqual(1f, HitReactionMath.Strength(34f, 34f, 0.7f, 2.2f), 1e-4f);
            float axe = HitReactionMath.Strength(60f, 34f, 0.7f, 2.2f);
            Assert.Greater(axe, 1f);
            Assert.Less(axe, 60f / 34f); // sub-linear
            Assert.AreEqual(2.2f, HitReactionMath.Strength(500f, 34f, 0.7f, 2.2f), 1e-4f);
            Assert.Less(HitReactionMath.Strength(15f, 34f, 0.7f, 2.2f), 1f);
        }

        [Test]
        public void Strength_NoDamageIsZero()
        {
            Assert.AreEqual(0f, HitReactionMath.Strength(0f, 34f, 0.7f, 2.2f));
            Assert.AreEqual(0f, HitReactionMath.Strength(-5f, 34f, 0.7f, 2.2f));
        }

        [Test]
        public void Spring_PeaksAtTheKickAfterPeakSeconds()
        {
            var spring = new FlinchSpring(0.06f);
            spring.Kick(new Vector3(10f, 0f, 0f));
            float best = 0f, bestTime = 0f;
            for (int i = 1; i <= 300; i++)
            {
                float x = spring.Advance(0.001f).x;
                if (x > best)
                {
                    best = x;
                    bestTime = i * 0.001f;
                }
            }
            Assert.AreEqual(10f, best, 0.01f);
            Assert.AreEqual(0.06f, bestTime, 0.002f);
        }

        [Test]
        public void Spring_ReturnsExactlyToRest_WithoutOvershoot()
        {
            var spring = new FlinchSpring(0.06f);
            spring.Kick(new Vector3(0f, 20f, -5f));
            for (int i = 0; i < 200; i++)
            {
                var offset = spring.Advance(1f / 60f);
                Assert.GreaterOrEqual(offset.y, 0f); // critically damped: never swings past the rest pose
            }
            Assert.IsTrue(spring.IsAtRest);
            Assert.AreEqual(Vector3.zero, spring.Offset);
        }

        [Test]
        public void Spring_OneBigStepMatchesManySmallOnes()
        {
            var a = new FlinchSpring(0.07f);
            var b = new FlinchSpring(0.07f);
            a.Kick(new Vector3(12f, -3f, 4f));
            b.Kick(new Vector3(12f, -3f, 4f));
            a.Advance(0.1f);
            for (int i = 0; i < 10; i++) b.Advance(0.01f);
            Assert.AreEqual(a.Offset.x, b.Offset.x, 1e-3f);
            Assert.AreEqual(a.Offset.y, b.Offset.y, 1e-3f);
            Assert.AreEqual(a.Velocity.z, b.Velocity.z, 1e-2f);
        }

        [Test]
        public void Spring_KicksAddUp()
        {
            var one = new FlinchSpring(0.06f);
            var two = new FlinchSpring(0.06f);
            one.Kick(Vector3.right * 5f);
            two.Kick(Vector3.right * 5f);
            two.Kick(Vector3.right * 5f);
            one.Advance(0.06f);
            two.Advance(0.06f);
            Assert.AreEqual(one.Offset.x * 2f, two.Offset.x, 1e-3f);
        }

        [Test]
        public void Spring_ABlinkSkipSettlesAFlinch()
        {
            var spring = new FlinchSpring(0.06f);
            spring.Kick(Vector3.forward * 15f);
            spring.Advance(0.5f);
            Assert.Less(spring.Offset.magnitude, 0.2f);
        }

        [Test]
        public void BendAxis_TipsTheTopAlongTheShot()
        {
            Vector3 shot = new Vector3(0f, -0.3f, 1f); // from eye height, slightly down
            Vector3 axis = HitReactionMath.BendAxis(shot, Vector3.up);
            Vector3 top = Quaternion.AngleAxis(20f, axis) * Vector3.up;
            Assert.Greater(top.z, 0.3f);
            Assert.AreEqual(0f, top.x, 1e-4f);
            Assert.AreEqual(Vector3.zero, HitReactionMath.BendAxis(Vector3.down, Vector3.up));
        }

        [Test]
        public void Twist_TurnsTheHitSideAlongTheShot()
        {
            Vector3 shot = Vector3.forward;
            float twist = HitReactionMath.TwistAmount(new Vector3(0.1f, 0.3f, 0f), shot, Vector3.up, 0.2f);
            Vector3 shoulder = Quaternion.AngleAxis(30f * twist, Vector3.up) * Vector3.right;
            Assert.Greater(shoulder.z, 0f); // the right side was hit and goes back with the shot
            Assert.AreEqual(0f, HitReactionMath.TwistAmount(new Vector3(0f, 0.3f, 0f), shot, Vector3.up, 0.2f), 1e-5f);
            Assert.AreEqual(-1f, HitReactionMath.TwistAmount(new Vector3(2f, 0f, 0f), shot, Vector3.up, 0.2f), 1e-5f);
        }

        [Test]
        public void SwingAxis_SwingsTheLimbTowardsTheShot_OrFallsBack()
        {
            Vector3 axis = HitReactionMath.SwingAxis(Vector3.down, Vector3.forward, Vector3.right);
            Vector3 limb = Quaternion.AngleAxis(30f, axis) * Vector3.down;
            Assert.Greater(limb.z, 0f);
            Assert.AreEqual(Vector3.up, HitReactionMath.SwingAxis(Vector3.forward, Vector3.forward * 2f, Vector3.up));
        }

        [Test]
        public void ShoulderShare_FadesOutWithDistance()
        {
            Assert.AreEqual(1f, HitReactionMath.ShoulderShare(0.05f, 0.08f, 0.24f));
            Assert.AreEqual(0.5f, HitReactionMath.ShoulderShare(0.16f, 0.08f, 0.24f), 1e-4f);
            Assert.AreEqual(0f, HitReactionMath.ShoulderShare(0.3f, 0.08f, 0.24f));
        }

        [Test]
        public void Stagger_DropsAtOnce_AndRecoversFully()
        {
            var stagger = new StaggerState();
            Assert.AreEqual(1f, stagger.SpeedFactor);
            stagger.Kick(0.6f, 0.4f);
            Assert.AreEqual(0.4f, stagger.SpeedFactor, 1e-4f);
            stagger.Advance(0.2f);
            Assert.Greater(stagger.SpeedFactor, 0.4f);
            Assert.Less(stagger.SpeedFactor, 1f);
            stagger.Advance(0.25f);
            Assert.IsFalse(stagger.IsActive);
            Assert.AreEqual(1f, stagger.SpeedFactor);
        }

        [Test]
        public void Stagger_HitsStack_UpToTheCap()
        {
            var stagger = new StaggerState();
            stagger.Kick(0.6f, 0.4f, stackShare: 0.5f, maxDepth: 0.9f);
            stagger.Kick(0.6f, 0.4f, stackShare: 0.5f, maxDepth: 0.9f);
            Assert.AreEqual(0.1f, stagger.SpeedFactor, 1e-4f); // 0.6 + 0.3 = 0.9 (the cap)
            stagger.Kick(0f, 0.4f);
            Assert.AreEqual(0.1f, stagger.SpeedFactor, 1e-4f); // an empty kick changes nothing
        }

        [Test]
        public void DecaySlide_CoversSpeedTimesTimeConstant_SplitOrWhole()
        {
            Vector3 v1 = Vector3.forward * 2f, v2 = v1;
            Vector3 whole = HitReactionMath.DecaySlide(ref v1, 0.2f, 0.1f);
            Vector3 split = Vector3.zero;
            for (int i = 0; i < 20; i++) split += HitReactionMath.DecaySlide(ref v2, 0.01f, 0.1f);
            Assert.AreEqual(whole.z, split.z, 1e-4f);

            Vector3 v3 = Vector3.forward * 2f;
            Vector3 total = HitReactionMath.DecaySlide(ref v3, 10f, 0.1f);
            Assert.AreEqual(0.2f, total.z, 1e-3f);
            Assert.AreEqual(Vector3.zero, v3);
        }

        [Test]
        public void PushDirection_LiftsADownwardShotOffTheFloor()
        {
            Vector3 down45 = new Vector3(0f, -1f, 1f).normalized;
            Vector3 push = HitReactionMath.PushDirection(down45, Vector3.up, 0.75f, 0.2f);
            Assert.AreEqual(1f, push.magnitude, 1e-4f);
            Assert.Greater(push.y, down45.y);
            Assert.Greater(push.z, 0.9f);

            Vector3 flat = HitReactionMath.PushDirection(Vector3.forward, Vector3.up, 0.75f, 0f);
            Assert.AreEqual(Vector3.forward, flat);
            Assert.AreEqual(Vector3.zero, HitReactionMath.PushDirection(Vector3.zero, Vector3.up, 0.75f, 0.2f));
        }
    }
}
