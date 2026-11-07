using Blink.Effects;
using NUnit.Framework;

namespace Blink.Tests
{
    public class EyelidTimelineTests
    {
        static EyelidTimeline Make() => new(closeSeconds: 0.1f, shutSeconds: 0.5f, openSeconds: 0.2f);

        [Test]
        public void StartsOpen()
        {
            var t = Make();
            Assert.AreEqual(EyelidTimeline.Phase.Open, t.Current);
            Assert.AreEqual(0f, t.Closed01);
            Assert.IsFalse(t.Tick(1f));
        }

        [Test]
        public void Trigger_ClosesThenReportsCoveredOnce()
        {
            var t = Make();
            Assert.IsFalse(t.Trigger());
            Assert.IsFalse(t.Tick(0.05f));
            Assert.AreEqual(0.5f, t.Linear01, 1e-4f);
            Assert.IsTrue(t.Tick(0.06f));
            Assert.IsTrue(t.IsCovered);
            Assert.AreEqual(1f, t.Closed01);
            Assert.IsFalse(t.Tick(0.01f)); // only the covering tick returns true
        }

        [Test]
        public void FullBlink_TakesCloseShutOpen()
        {
            var t = Make();
            t.Trigger();
            t.Tick(0.1f);  // shut
            t.Tick(0.5f);  // shut time over
            Assert.AreEqual(EyelidTimeline.Phase.Opening, t.Current);
            t.Tick(0.1f);
            Assert.AreEqual(0.5f, t.Linear01, 1e-4f);
            t.Tick(0.1f);
            Assert.AreEqual(EyelidTimeline.Phase.Open, t.Current);
            Assert.AreEqual(0f, t.Closed01);
        }

        [Test]
        public void Trigger_WhileShut_IsCoveredNow_AndExtendsShut()
        {
            var t = Make();
            t.Trigger();
            t.Tick(0.1f);
            t.Tick(0.4f);
            Assert.IsTrue(t.Trigger());
            t.Tick(0.4f);
            Assert.IsTrue(t.IsCovered); // shut timer restarted
            t.Tick(0.11f);
            Assert.AreEqual(EyelidTimeline.Phase.Opening, t.Current);
        }

        [Test]
        public void Trigger_WhileOpening_Recloses_FromWhereItWas()
        {
            var t = Make();
            t.Trigger();
            t.Tick(0.1f);
            t.Tick(0.5f);
            t.Tick(0.1f); // half open
            Assert.IsFalse(t.Trigger());
            Assert.AreEqual(EyelidTimeline.Phase.Closing, t.Current);
            Assert.AreEqual(0.5f, t.Linear01, 1e-4f);
            Assert.IsTrue(t.Tick(0.05f));
        }

        [Test]
        public void ZeroCloseTime_IsCoveredOnTrigger()
        {
            var t = new EyelidTimeline(0f, 0.5f, 0.2f);
            Assert.IsTrue(t.Trigger());
            Assert.IsTrue(t.IsCovered);
        }

        [Test]
        public void ZeroShutTime_OpensOnTheNextTick()
        {
            var t = new EyelidTimeline(0.07f, 0f, 0.1f);
            t.Trigger();
            Assert.IsTrue(t.Tick(0.07f));
            Assert.AreEqual(1f, t.Closed01); // one fully black frame for the skip
            t.Tick(0.016f);
            Assert.AreEqual(EyelidTimeline.Phase.Opening, t.Current);
            t.Tick(0.1f);
            Assert.AreEqual(EyelidTimeline.Phase.Open, t.Current);
        }

        [Test]
        public void Closed01_IsEasedButHitsEnds()
        {
            var t = Make();
            t.Trigger();
            t.Tick(0.025f); // linear 0.25
            Assert.Less(t.Closed01, t.Linear01); // eases in
            t.Tick(0.1f);
            Assert.AreEqual(1f, t.Closed01);
        }
    }
}
