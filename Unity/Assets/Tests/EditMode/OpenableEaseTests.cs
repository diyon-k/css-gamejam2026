using Blink.Interaction;
using NUnit.Framework;

namespace Blink.Tests
{
    public class OpenableEaseTests
    {
        [TestCase(0f), TestCase(1f), TestCase(1.6f)]
        public void Ease_StartsAtZeroAndEndsAtOne(float overshoot)
        {
            Assert.AreEqual(0f, Openable.Ease(0f, overshoot), 1e-5f);
            Assert.AreEqual(1f, Openable.Ease(1f, overshoot), 1e-5f);
        }

        [Test]
        public void Ease_OvershootSwingsPastThenSettles()
        {
            float peak = 0f;
            for (int i = 0; i <= 100; i++) peak = UnityEngine.Mathf.Max(peak, Openable.Ease(i / 100f, 1f));
            Assert.Greater(peak, 1.02f);
            Assert.Less(peak, 1.06f);
        }

        [Test]
        public void Ease_NoOvershootStaysInRange()
        {
            for (int i = 0; i <= 100; i++)
            {
                float e = Openable.Ease(i / 100f, 0f);
                Assert.That(e, Is.InRange(0f, 1f));
            }
        }
    }
}
