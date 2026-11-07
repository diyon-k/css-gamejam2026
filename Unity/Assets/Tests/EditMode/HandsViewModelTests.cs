using Blink.Combat;
using NUnit.Framework;

namespace Blink.Tests
{
    public class HandsViewModelTests
    {
        [Test]
        public void PulseRisesToOneThenFallsBackToZero()
        {
            Assert.AreEqual(0f, HandsViewModel.Pulse(-0.1f, 0.1f, 0.3f));
            Assert.AreEqual(1f, HandsViewModel.Pulse(0.1f, 0.1f, 0.3f), 1e-4f);
            Assert.That(HandsViewModel.Pulse(0.25f, 0.1f, 0.3f), Is.InRange(0.1f, 0.9f));
            Assert.AreEqual(0f, HandsViewModel.Pulse(0.4f, 0.1f, 0.3f), 1e-4f);
            Assert.AreEqual(0f, HandsViewModel.Pulse(float.PositiveInfinity, 0.1f, 0.3f));
        }

        [Test]
        public void ClimbHandsAlternateAndStayContinuousAcrossRungs()
        {
            // Rung 0: left reaches up from low, right pulls down from high.
            Assert.AreEqual(-0.5f, HandsViewModel.ClimbHand(0f, 0, out float reach), 1e-5f);
            Assert.AreEqual(0f, reach);
            Assert.AreEqual(0.5f, HandsViewModel.ClimbHand(0f, 1, out reach), 1e-5f);
            Assert.AreEqual(0f, reach);
            Assert.AreEqual(0f, HandsViewModel.ClimbHand(0.5f, 0, out reach), 1e-5f, "left halfway up");
            Assert.Greater(reach, 0f);
            // Rung 1: roles swap, and each hand carries on from where it was.
            for (int hand = 0; hand < 2; hand++)
                Assert.AreEqual(HandsViewModel.ClimbHand(0.9999f, hand, out _), HandsViewModel.ClimbHand(1f, hand, out _), 1e-3f);
            HandsViewModel.ClimbHand(1.5f, 1, out reach);
            Assert.Greater(reach, 0f, "right reaches on odd rungs");
            HandsViewModel.ClimbHand(1.5f, 0, out reach);
            Assert.AreEqual(0f, reach);
        }
    }
}
