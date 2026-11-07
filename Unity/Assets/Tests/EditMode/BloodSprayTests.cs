using Blink.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class BloodSprayTests
    {
        const float Eps = 1e-4f;

        static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = Eps)
        {
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"expected {expected:F4} but was {actual:F4}");
        }

        [Test]
        public void SprayDirection_HeadOnShot_PointsBackAtShooter()
        {
            var dir = BloodSpray.SprayDirection(Vector3.back, Vector3.forward, 0.4f);
            AssertVector(Vector3.back, dir);
        }

        [Test]
        public void SprayDirection_ObliqueShot_LiesBetweenNormalAndShooter()
        {
            var normal = Vector3.right;
            var ray = new Vector3(-1f, 0f, 1f).normalized; // glancing hit from the side
            var dir = BloodSpray.SprayDirection(normal, ray, 0.4f);

            Assert.AreEqual(1f, dir.magnitude, Eps);
            float toNormal = Vector3.Angle(dir, normal);
            float toShooter = Vector3.Angle(dir, -ray);
            float total = Vector3.Angle(normal, -ray);
            Assert.AreEqual(total, toNormal + toShooter, 0.01f, "should lie in the plane between the two");
            Assert.Less(toNormal, toShooter, "40% towards the shooter stays closer to the normal");
        }

        [Test]
        public void SprayDirection_BlendEnds_AreNormalAndBackAlongRay()
        {
            var normal = new Vector3(0f, 1f, 1f).normalized;
            var ray = new Vector3(0.3f, -0.2f, -1f);
            AssertVector(normal, BloodSpray.SprayDirection(normal, ray, 0f));
            AssertVector(-ray.normalized, BloodSpray.SprayDirection(normal, ray, 1f));
        }

        [Test]
        public void SprayDirection_DegenerateBlend_FallsBackToShooter()
        {
            // Normal exactly along the ray (can't happen from a real raycast) cancels out at 50%.
            var dir = BloodSpray.SprayDirection(Vector3.forward, Vector3.forward, 0.5f);
            AssertVector(Vector3.back, dir);
        }

        [Test]
        public void Lift_TiltsUpwardsAndStaysNormalised()
        {
            var dir = BloodSpray.Lift(Vector3.forward, 0.25f);
            Assert.AreEqual(1f, dir.magnitude, Eps);
            Assert.Greater(dir.y, 0f);
            Assert.Greater(dir.z, dir.y);
            AssertVector(Vector3.forward, BloodSpray.Lift(Vector3.forward, 0f));
        }

        [TestCase(0f, 0f, -1f)]
        [TestCase(0.3f, 0.2f, 1f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, -1f, 0f)]
        public void FacingRotation_PutsLocalUpAlongFacing(float x, float y, float z)
        {
            var facing = new Vector3(x, y, z).normalized;
            AssertVector(facing, BloodSpray.FacingRotation(facing) * Vector3.up);
        }

        [TestCase(0f, 0f, 1f)]
        [TestCase(1f, 0f, 0f)]
        [TestCase(-0.6f, 0f, -0.8f)]
        public void DirectionalRotation_OnFloor_RunsSplatAlongSpray(float x, float y, float z)
        {
            var spray = new Vector3(x, y, z).normalized;
            var rotation = BloodSpray.DirectionalRotation(Vector3.up, spray);
            AssertVector(Vector3.up, rotation * Vector3.up);
            AssertVector(spray, rotation * BloodSpray.DirectionalSplatAxis);
        }

        [Test]
        public void DirectionalRotation_ProjectsSprayOntoThePlane()
        {
            // A shot travelling down and away still streaks flat along the floor, away from the shooter.
            var rotation = BloodSpray.DirectionalRotation(Vector3.up, new Vector3(0f, -0.5f, 1f));
            AssertVector(Vector3.forward, rotation * BloodSpray.DirectionalSplatAxis);
        }

        [Test]
        public void DirectionalRotation_OnSlope_KeepsNormalAndStaysInPlane()
        {
            var normal = new Vector3(0.2f, 1f, 0.1f).normalized;
            var rotation = BloodSpray.DirectionalRotation(normal, Vector3.forward);
            AssertVector(normal, rotation * Vector3.up);
            var axis = rotation * BloodSpray.DirectionalSplatAxis;
            Assert.AreEqual(0f, Vector3.Dot(axis, normal), Eps);
            Assert.Greater(Vector3.Dot(axis, Vector3.forward), 0.9f);
        }

        [Test]
        public void SkipSteps_BlinkSkip_IsFiveTenthSecondSteps()
        {
            int steps = BloodSpray.SkipSteps(0.5f, 0.1f, out float step);
            Assert.AreEqual(5, steps);
            Assert.AreEqual(0.1f, step, Eps);
        }

        [Test]
        public void SkipSteps_ShortSkip_IsOneStep()
        {
            int steps = BloodSpray.SkipSteps(0.04f, 0.1f, out float step);
            Assert.AreEqual(1, steps);
            Assert.AreEqual(0.04f, step, Eps);
        }

        [Test]
        public void SkipSteps_UnevenSkip_StepsCoverItExactlyWithoutExceedingMax()
        {
            int steps = BloodSpray.SkipSteps(0.73f, 0.1f, out float step);
            Assert.AreEqual(8, steps);
            Assert.LessOrEqual(step, 0.1f + Eps);
            Assert.AreEqual(0.73f, steps * step, Eps);
        }

        [Test]
        public void SkipSteps_NoTime_IsNoSteps()
        {
            Assert.AreEqual(0, BloodSpray.SkipSteps(0f, 0.1f, out float step));
            Assert.AreEqual(0f, step);
            Assert.AreEqual(0, BloodSpray.SkipSteps(-1f, 0.1f, out _));
        }

        [Test]
        public void IsSpent_OnlyWhenPlayedOutWithNoParticles()
        {
            Assert.IsTrue(BloodSpray.IsSpent(false, 0.5f, 0.5f, 0), "at the end, empty");
            Assert.IsFalse(BloodSpray.IsSpent(false, 0.5f, 0.5f, 3), "particles still flying");
            Assert.IsFalse(BloodSpray.IsSpent(false, 0.2f, 0.5f, 0), "still emitting (or waiting out a start delay)");
            Assert.IsFalse(BloodSpray.IsSpent(true, 0.5f, 0.5f, 0), "looping systems never finish");
        }

        [Test]
        public void SkipSteps_NoMaxStep_IsOneWholeStep()
        {
            Assert.AreEqual(1, BloodSpray.SkipSteps(0.5f, 0f, out float step));
            Assert.AreEqual(0.5f, step, Eps);
        }
    }
}
