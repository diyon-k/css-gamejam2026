using System.Collections.Generic;
using Blink.Detection;
using NUnit.Framework;

namespace Blink.Tests
{
    /// <summary>
    /// Looking away from the webcam (no face) counts as eyes open. Runs frames through the same
    /// <see cref="EyeStateMerger"/> + <see cref="BlinkClassifier"/> pair that <see cref="BlinkDetector"/> uses.
    /// </summary>
    public sealed class LookAwayTests
    {
        sealed class FakeSource : IEyeStateSource
        {
            public bool IsActive { get; set; } = true;
            public EyeState Current { get; set; } = EyeState.Open;
            public string SourceName { get; set; } = "fake";
        }

        FakeSource _cam;
        List<IEyeStateSource> _sources;
        EyeStateMerger _merger;
        BlinkClassifier _classifier;
        List<BlinkEventType> _events;

        [SetUp]
        public void SetUp()
        {
            _cam = new FakeSource { SourceName = "webcam" };
            _sources = new List<IEyeStateSource> { _cam };
            _merger = new EyeStateMerger();
            _classifier = new BlinkClassifier(holdThreshold: 0.35f, minClosedTime: 0.03f);
            _events = new List<BlinkEventType>();
        }

        /// <summary>One detector frame, exactly as BlinkDetector.Update does it.</summary>
        BlinkEventType Frame(float now)
        {
            var reading = _merger.Read(_sources);
            var e = reading.FaceLost ? _classifier.LoseFace(now) : _classifier.Update(reading.Closed, now);
            if (e.Type != BlinkEventType.None) _events.Add(e.Type);
            return e.Type;
        }

        /// <summary>Holds the webcam at one state for [from, to) at 30 fps.</summary>
        void Run(float from, float to, bool present, bool closed)
        {
            _cam.Current = new EyeState(present, closed);
            for (float t = from; t < to - 1e-4f; t += 1f / 30f) Frame(t);
        }

        [Test]
        public void MissingFace_IsOpenByDefault()
        {
            Assert.IsFalse(new EyeStateMerger().TreatMissingFaceAsClosed);
        }

        [Test]
        public void LookingAwayWhileOpen_DoesNothing()
        {
            Run(0f, 1f, present: true, closed: false);
            Run(1f, 4f, present: false, closed: false); // longer than the hold threshold
            Run(4f, 5f, present: true, closed: false);
            CollectionAssert.IsEmpty(_events);
            Assert.IsFalse(_classifier.IsClosed);
        }

        [Test]
        public void NoFace_CountsAsOpen_EvenIfThePacketSaysClosed()
        {
            Run(0f, 3f, present: false, closed: true);
            CollectionAssert.IsEmpty(_events);
        }

        [Test]
        public void FaceLostMidBlink_FiresNoBlink_AndFaceReturningOpenFiresNothing()
        {
            Run(0f, 0.5f, present: true, closed: false);
            Run(0.5f, 0.6f, present: true, closed: true); // closing, under the hold threshold
            Assert.IsTrue(_classifier.IsClosed);
            Run(0.6f, 2f, present: false, closed: false); // looked away
            Assert.IsFalse(_classifier.IsClosed);
            Run(2f, 3f, present: true, closed: false); // back, eyes open
            CollectionAssert.IsEmpty(_events);
        }

        [Test]
        public void FaceLostWhileShut_EndsTheShutOnce_ThenNothing()
        {
            Run(0f, 0.2f, present: true, closed: false);
            Run(0.2f, 1.5f, present: true, closed: true);
            CollectionAssert.AreEqual(new[] { BlinkEventType.ShutBegan }, _events);

            _cam.Current = new EyeState(false, false);
            Assert.AreEqual(BlinkEventType.ShutEnded, Frame(1.5f)); // fast-forward stops straight away
            Run(1.55f, 4f, present: false, closed: false);
            Run(4f, 5f, present: true, closed: false);

            CollectionAssert.AreEqual(new[] { BlinkEventType.ShutBegan, BlinkEventType.ShutEnded }, _events);
            Assert.IsFalse(_classifier.IsClosed);
        }

        [Test]
        public void FaceLostWhileShut_ReportsHowLongTheyWereShut()
        {
            Run(0f, 1f, present: true, closed: true);
            _cam.Current = new EyeState(false, false);
            var reading = _merger.Read(_sources);
            Assert.IsTrue(reading.FaceLost);
            Assert.AreEqual(1f, _classifier.LoseFace(1f).ClosedDuration, 0.05f);
        }

        [Test]
        public void FaceReturnsWithEyesClosed_StartsAFreshClosure()
        {
            Run(0f, 0.1f, present: true, closed: true);
            Run(0.1f, 1f, present: false, closed: false);
            Run(1f, 1.5f, present: true, closed: true); // the hold threshold counts from the face's return
            Run(1.5f, 2f, present: true, closed: false);
            CollectionAssert.AreEqual(new[] { BlinkEventType.ShutBegan, BlinkEventType.ShutEnded }, _events);
        }

        [Test]
        public void FaceReturnsAndBlinks_IsANormalBlink()
        {
            Run(0f, 1f, present: false, closed: false);
            Run(1f, 1.15f, present: true, closed: true);
            Run(1.15f, 1.5f, present: true, closed: false);
            CollectionAssert.AreEqual(new[] { BlinkEventType.Blink }, _events);
        }

        [Test]
        public void NormalBlinkAndHold_StillWork()
        {
            Run(0f, 0.15f, present: true, closed: true);
            Run(0.15f, 0.5f, present: true, closed: false);
            Run(0.5f, 1.5f, present: true, closed: true);
            Run(1.5f, 2f, present: true, closed: false);
            CollectionAssert.AreEqual(
                new[] { BlinkEventType.Blink, BlinkEventType.ShutBegan, BlinkEventType.ShutEnded }, _events);
        }

        [Test]
        public void WebcamGoingQuietMidBlink_FiresNoBlink()
        {
            Run(0f, 0.1f, present: true, closed: true);
            _cam.IsActive = false; // sidecar stopped sending
            Frame(0.15f);
            Frame(0.2f);
            CollectionAssert.IsEmpty(_events);
            Assert.IsFalse(_classifier.IsClosed);
        }

        [Test]
        public void AnotherSourceLosingTheFace_DoesNotCancelABlink()
        {
            var other = new FakeSource { SourceName = "other", Current = new EyeState(false, false) };
            _sources.Insert(0, other);
            Run(0f, 0.15f, present: true, closed: true);
            Run(0.15f, 0.5f, present: true, closed: false);
            CollectionAssert.AreEqual(new[] { BlinkEventType.Blink }, _events);
        }

        [Test]
        public void SourceName_IsTheClosingSource_ElseFirstActive_ElseNone()
        {
            Assert.AreEqual("webcam", _merger.Read(_sources).SourceName);
            _cam.IsActive = false;
            Assert.AreEqual("none", _merger.Read(_sources).SourceName);
        }

        [Test]
        public void OldBehaviour_MissingFaceAsClosed_StillAvailable()
        {
            _merger.TreatMissingFaceAsClosed = true;
            Run(0f, 1f, present: false, closed: false);
            Run(1f, 1.5f, present: true, closed: false);
            CollectionAssert.AreEqual(new[] { BlinkEventType.ShutBegan, BlinkEventType.ShutEnded }, _events);
        }

        [Test]
        public void LoseFace_WhileOpen_IsNothing()
        {
            Assert.AreEqual(BlinkEventType.None, _classifier.LoseFace(1f).Type);
        }
    }
}
