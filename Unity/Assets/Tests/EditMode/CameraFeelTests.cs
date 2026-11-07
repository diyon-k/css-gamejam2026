using Blink.Player;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class CameraFeelTests
    {
        const float Frame = 1f / 60f;

        // ---- Spring3 ----

        [TestCase(3.5f, 0.7f)]
        [TestCase(5f, 1f)]
        [TestCase(2f, 0.3f)]
        public void Spring_KickedFromRest_ReturnsToRest(float hz, float zeta)
        {
            var s = new Spring3(hz, zeta);
            s.AddVelocity(new Vector3(-100f, 20f, -30f));
            for (int i = 0; i < 600; i++) s.Step(Frame); // 10 s
            Assert.Less(s.Value.magnitude, 1e-4f);
            Assert.Less(s.Velocity.magnitude, 1e-3f);
        }

        [Test]
        public void Spring_TwoHalfSteps_EqualOneFullStep()
        {
            var a = new Spring3(3.5f, 0.7f);
            var b = new Spring3(3.5f, 0.7f);
            a.AddVelocity(Vector3.one * 50f);
            b.AddVelocity(Vector3.one * 50f);

            a.Step(0.02f);
            b.Step(0.01f);
            b.Step(0.01f);

            Assert.AreEqual(a.Value.x, b.Value.x, 1e-4f);
            Assert.AreEqual(a.Velocity.x, b.Velocity.x, 1e-3f);
        }

        [TestCase(3.5f, 0.7f)]
        [TestCase(5f, 1f)]
        public void Spring_MatchesFineNumericalIntegration(float hz, float zeta)
        {
            var exact = new Spring3(hz, zeta);
            exact.AddVelocity(Vector3.right * 10f);
            exact.Step(0.1f);

            // Semi-implicit Euler with tiny steps on x'' = -w^2 x - 2 z w x'
            double w = 2.0 * Mathf.PI * hz, x = 0.0, v = 10.0;
            const int n = 100000;
            double h = 0.1 / n;
            for (int i = 0; i < n; i++)
            {
                v += (-w * w * x - 2.0 * zeta * w * v) * h;
                x += v * h;
            }
            Assert.AreEqual(x, exact.Value.x, 2e-3);
        }

        [Test]
        public void Spring_HugeStep_StaysFiniteAndSettles()
        {
            var s = new Spring3(3.5f, 0.7f);
            s.AddVelocity(Vector3.one * 1000f);
            s.Step(10f);
            Assert.IsFalse(float.IsNaN(s.Value.x) || float.IsInfinity(s.Value.x));
            Assert.Less(s.Value.magnitude, 1e-3f);
        }

        [TestCase(3.5f, 0.7f)]
        [TestCase(5f, 0.85f)]
        [TestCase(4f, 1f)]
        public void Spring_PeakPerUnitVelocity_MatchesSimulatedPeak(float hz, float zeta)
        {
            var s = new Spring3(hz, zeta);
            s.AddVelocity(Vector3.right);
            float peak = 0f;
            for (int i = 0; i < 20000; i++)
            {
                s.Step(0.0001f);
                peak = Mathf.Max(peak, s.Value.x);
            }
            Assert.AreEqual(Spring3.PeakPerUnitVelocity(hz, zeta), peak, peak * 1e-3f);
        }

        // ---- HeadBob ----

        [Test]
        public void Bob_StandingStill_IsExactlyZero()
        {
            var bob = new HeadBob();
            for (int i = 0; i < 60; i++) bob.Step(Frame, 3.5f, 1.6f, 3.5f, 0.25f);
            Assert.Greater(bob.Weight, 0.99f);

            for (int i = 0; i < 60; i++) bob.Step(Frame, 0f, 1.6f, 3.5f, 0.25f); // 1 s standing
            Assert.AreEqual(0f, bob.Weight);
            Assert.AreEqual(0f, bob.Lateral);
            Assert.AreEqual(0f, bob.Vertical);
        }

        [Test]
        public void Bob_PhaseFollowsDistance_NotTime()
        {
            // Two steps (one stride, 2 pi) per 2 x stepLength metres, whatever the speed.
            var slow = new HeadBob();
            var fast = new HeadBob();
            slow.Step(1f, 0.8f, 1.6f, 3.5f, 0f);   // 0.8 m
            fast.Step(0.25f, 3.2f, 1.6f, 3.5f, 0f); // 0.8 m
            Assert.AreEqual(Mathf.PI * 0.5f, slow.Phase, 1e-4f);
            Assert.AreEqual(slow.Phase, fast.Phase, 1e-4f);
        }

        [Test]
        public void Bob_WeightScalesWithSpeed_CappedAtOne()
        {
            var bob = new HeadBob();
            bob.Step(Frame, 1.75f, 1.6f, 3.5f, 0f);
            Assert.AreEqual(0.5f, bob.Weight, 1e-5f);
            bob.Step(Frame, 10f, 1.6f, 3.5f, 0f);
            Assert.AreEqual(1f, bob.Weight);
        }

        [Test]
        public void Bob_PhaseWraps_SoLongWalksKeepPrecision()
        {
            var bob = new HeadBob();
            for (int i = 0; i < 60 * 600; i++) bob.Step(Frame, 3.5f, 1.6f, 3.5f, 0.25f); // 10 minutes
            Assert.GreaterOrEqual(bob.Phase, 0f);
            Assert.Less(bob.Phase, 2f * Mathf.PI);
        }

        [Test]
        public void Bob_ZeroOrNegativeDelta_ChangesNothing()
        {
            var bob = new HeadBob();
            bob.Step(0f, 3.5f, 1.6f, 3.5f, 0.25f);
            bob.Step(-1f, 3.5f, 1.6f, 3.5f, 0.25f);
            Assert.AreEqual(0f, bob.Phase);
            Assert.AreEqual(0f, bob.Weight);
        }

        // ---- Reload envelope ----

        [Test]
        public void ReloadEnvelope_ZeroAtBothEnds_FullInTheMiddle()
        {
            Assert.AreEqual(0f, CameraFeelMath.ReloadEnvelope(0f, 0.15f, 0.25f));
            Assert.AreEqual(0f, CameraFeelMath.ReloadEnvelope(1f, 0.15f, 0.25f));
            Assert.AreEqual(1f, CameraFeelMath.ReloadEnvelope(0.5f, 0.15f, 0.25f));
        }

        [Test]
        public void ReloadEnvelope_RisesThenFallsSmoothly()
        {
            float prev = 0f;
            for (float p = 0f; p <= 0.5f; p += 0.01f)
            {
                float e = CameraFeelMath.ReloadEnvelope(p, 0.15f, 0.25f);
                Assert.GreaterOrEqual(e, prev - 1e-6f);
                prev = e;
            }
            for (float p = 0.5f; p <= 1f; p += 0.01f)
            {
                float e = CameraFeelMath.ReloadEnvelope(p, 0.15f, 0.25f);
                Assert.LessOrEqual(e, prev + 1e-6f);
                prev = e;
            }
        }

        [Test]
        public void ReloadEnvelope_ClampsOutOfRangeProgress()
        {
            Assert.AreEqual(0f, CameraFeelMath.ReloadEnvelope(-1f, 0.15f, 0.25f));
            Assert.AreEqual(0f, CameraFeelMath.ReloadEnvelope(2f, 0.15f, 0.25f));
        }

        // ---- Compose / soft cap ----

        [Test]
        public void Compose_ZeroIntensity_ReturnsBasePoseExactly()
        {
            var basePos = new Vector3(0.1f, 1.65f, -0.2f);
            var baseRot = Quaternion.Euler(3f, 4f, 5f);
            CameraFeelMath.Compose(basePos, baseRot, Vector3.one, new Vector3(10f, 10f, 10f), 0f,
                out var pos, out var rot);
            Assert.AreEqual(basePos, pos);
            Assert.AreEqual(baseRot, rot);
        }

        [Test]
        public void Compose_ZeroOffsets_ReturnsBasePose()
        {
            var basePos = new Vector3(0f, 1.65f, 0f);
            CameraFeelMath.Compose(basePos, Quaternion.identity, Vector3.zero, Vector3.zero, 1f,
                out var pos, out var rot);
            Assert.AreEqual(basePos, pos);
            Assert.AreEqual(1f, Quaternion.Dot(Quaternion.identity, rot), 1e-6f);
        }

        [Test]
        public void Compose_ScalesOffsetsByIntensity()
        {
            CameraFeelMath.Compose(Vector3.zero, Quaternion.identity, new Vector3(0f, 0.02f, 0f),
                new Vector3(-2f, 0f, 0f), 0.5f, out var pos, out var rot);
            Assert.AreEqual(0.01f, pos.y, 1e-6f);
            Assert.AreEqual(1f, Quaternion.Angle(Quaternion.identity, rot), 1e-3f);
        }

        [Test]
        public void SoftCap_ShrinksKickAsClimbBuilds()
        {
            Assert.AreEqual(1f, CameraFeelMath.SoftCapScale(0f, 5f));
            Assert.AreEqual(0.5f, CameraFeelMath.SoftCapScale(-2.5f, 5f), 1e-6f);
            Assert.AreEqual(0f, CameraFeelMath.SoftCapScale(-7f, 5f));
        }
    }
}
