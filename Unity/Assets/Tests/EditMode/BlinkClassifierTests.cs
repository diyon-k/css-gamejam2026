using Blink.Detection;
using NUnit.Framework;

namespace Blink.Tests
{
    public class BlinkClassifierTests
    {
        BlinkClassifier _classifier;

        [SetUp]
        public void SetUp() => _classifier = new BlinkClassifier(holdThreshold: 0.35f, minClosedTime: 0.03f);

        [Test]
        public void QuickCloseAndOpen_IsABlink_ReportedOnReopen()
        {
            Assert.AreEqual(BlinkEventType.None, _classifier.Update(true, 1.00f).Type);
            Assert.AreEqual(BlinkEventType.None, _classifier.Update(true, 1.10f).Type);

            var e = _classifier.Update(false, 1.15f);

            Assert.AreEqual(BlinkEventType.Blink, e.Type);
            Assert.AreEqual(0.15f, e.ClosedDuration, 1e-4f);
        }

        [Test]
        public void ClosureShorterThanMinimum_IsIgnoredAsNoise()
        {
            _classifier.Update(true, 1.00f);
            Assert.AreEqual(BlinkEventType.None, _classifier.Update(false, 1.01f).Type);
        }

        [Test]
        public void HoldingPastThreshold_BeginsShut_ThenEndsOnReopen()
        {
            _classifier.Update(true, 1.00f);
            Assert.AreEqual(BlinkEventType.None, _classifier.Update(true, 1.30f).Type);

            Assert.AreEqual(BlinkEventType.ShutBegan, _classifier.Update(true, 1.40f).Type);
            Assert.IsTrue(_classifier.IsShut);
            Assert.AreEqual(BlinkEventType.None, _classifier.Update(true, 2.00f).Type);

            var end = _classifier.Update(false, 3.00f);
            Assert.AreEqual(BlinkEventType.ShutEnded, end.Type);
            Assert.AreEqual(2.00f, end.ClosedDuration, 1e-4f);
            Assert.IsFalse(_classifier.IsClosed);
        }

        [Test]
        public void HoldDoesNotAlsoCountAsBlink()
        {
            _classifier.Update(true, 0f);
            _classifier.Update(true, 0.5f);
            Assert.AreNotEqual(BlinkEventType.Blink, _classifier.Update(false, 0.6f).Type);
        }

        [Test]
        public void StayingOpen_ProducesNoEvents()
        {
            for (float t = 0f; t < 1f; t += 0.1f)
                Assert.AreEqual(BlinkEventType.None, _classifier.Update(false, t).Type);
        }

        [Test]
        public void ConsecutiveBlinks_AreEachReported()
        {
            _classifier.Update(true, 0.0f);
            Assert.AreEqual(BlinkEventType.Blink, _classifier.Update(false, 0.1f).Type);
            _classifier.Update(true, 0.5f);
            Assert.AreEqual(BlinkEventType.Blink, _classifier.Update(false, 0.6f).Type);
        }
    }
}
