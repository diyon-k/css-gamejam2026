using Blink.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class AxeSwingTests
    {
        // 0.15 s wind-up, 0.12 s strike with the apex halfway, 0.3 s recovery.
        static AxeSwingTimeline Timeline(bool linear = false)
        {
            var timeline = new AxeSwingTimeline(0.15f, 0.12f, 0.5f, 0.3f);
            if (linear) timeline.SetCurves(null, null, null, null);
            return timeline;
        }

        [Test]
        public void Timeline_DerivesApexAndTotal()
        {
            var t = Timeline();
            Assert.AreEqual(0.21f, t.ApexSeconds, 1e-5f);
            Assert.AreEqual(0.27f, t.StrikeEndSeconds, 1e-5f);
            Assert.AreEqual(0.57f, t.TotalSeconds, 1e-5f);
        }

        [Test]
        public void Sample_WalksTheKeyPosesInOrder()
        {
            var t = Timeline(linear: true);

            AssertSample(t.Sample(0.075f), AxeSwingPhase.WindUp, AxeKeyPose.Idle, AxeKeyPose.WindUp, 0.5f);
            AssertSample(t.Sample(0.18f), AxeSwingPhase.Strike, AxeKeyPose.WindUp, AxeKeyPose.Apex, 0.5f);
            AssertSample(t.Sample(0.24f), AxeSwingPhase.Strike, AxeKeyPose.Apex, AxeKeyPose.FollowThrough, 0.5f);
            AssertSample(t.Sample(0.42f), AxeSwingPhase.Recover, AxeKeyPose.FollowThrough, AxeKeyPose.Idle, 0.5f);
        }

        [Test]
        public void Sample_IsAtTheApexPoseExactlyAtApexTime()
        {
            var t = Timeline(); // default curves
            var justBefore = t.Sample(t.ApexSeconds - 1e-4f);
            var atApex = t.Sample(t.ApexSeconds);

            Assert.AreEqual(AxeKeyPose.Apex, justBefore.To);
            Assert.AreEqual(1f, justBefore.Weight, 0.01f);
            Assert.AreEqual(AxeKeyPose.Apex, atApex.From);
            Assert.AreEqual(0f, atApex.Weight, 1e-5f);
        }

        [Test]
        public void Sample_IsAtRestOutsideTheSwing()
        {
            var t = Timeline();
            AssertSample(t.Sample(-0.1f), AxeSwingPhase.Idle, AxeKeyPose.Idle, AxeKeyPose.Idle, 0f);
            AssertSample(t.Sample(0f), AxeSwingPhase.WindUp, AxeKeyPose.Idle, AxeKeyPose.WindUp, 0f);
            AssertSample(t.Sample(t.TotalSeconds), AxeSwingPhase.Idle, AxeKeyPose.Idle, AxeKeyPose.Idle, 0f);
            AssertSample(t.Sample(5f), AxeSwingPhase.Idle, AxeKeyPose.Idle, AxeKeyPose.Idle, 0f);
        }

        [Test]
        public void Sample_IsContinuousAcrossPhaseBoundaries()
        {
            var t = Timeline(); // default curves end each segment at weight 1 on the next key pose
            float e = 1e-4f;
            var endWindUp = t.Sample(t.WindUpSeconds - e);
            Assert.AreEqual(AxeKeyPose.WindUp, endWindUp.To);
            Assert.AreEqual(1f, endWindUp.Weight, 0.01f);
            var startStrike = t.Sample(t.WindUpSeconds + e);
            Assert.AreEqual(AxeKeyPose.WindUp, startStrike.From);
            Assert.AreEqual(0f, startStrike.Weight, 0.01f);

            var endStrike = t.Sample(t.StrikeEndSeconds - e);
            Assert.AreEqual(AxeKeyPose.FollowThrough, endStrike.To);
            Assert.AreEqual(1f, endStrike.Weight, 0.01f);

            var endRecover = t.Sample(t.TotalSeconds - e);
            Assert.AreEqual(AxeKeyPose.Idle, endRecover.To);
            Assert.AreEqual(1f, endRecover.Weight, 0.01f);
        }

        [Test]
        public void StrikeCurves_AccelerateIntoAndOutOfTheApex()
        {
            var t = Timeline();
            float early = t.Sample(t.WindUpSeconds + 0.25f * (t.ApexSeconds - t.WindUpSeconds)).Weight;
            float late = t.Sample(t.ApexSeconds + 0.25f * (t.StrikeEndSeconds - t.ApexSeconds)).Weight;
            Assert.Less(early, 0.25f, "the strike starts slow and speeds up into the apex");
            Assert.Greater(late, 0.25f, "and leaves the apex fast before slowing into the follow-through");
        }

        [Test]
        public void Ease_MissingCurveIsLinear_AndInputIsClamped()
        {
            Assert.AreEqual(0.3f, AxeSwingTimeline.Ease(null, 0.3f), 1e-6f);
            Assert.AreEqual(0.3f, AxeSwingTimeline.Ease(new AnimationCurve(), 0.3f), 1e-6f);
            Assert.AreEqual(1f, AxeSwingTimeline.Ease(null, 3f), 1e-6f);
            Assert.AreEqual(0f, AxeSwingTimeline.Ease(null, -1f), 1e-6f);
        }

        [Test]
        public void PhaseAt_MatchesTheTimeline()
        {
            var t = Timeline();
            Assert.AreEqual(AxeSwingPhase.WindUp, t.PhaseAt(0f));
            Assert.AreEqual(AxeSwingPhase.Strike, t.PhaseAt(0.2f));
            Assert.AreEqual(AxeSwingPhase.Recover, t.PhaseAt(0.3f));
            Assert.AreEqual(AxeSwingPhase.Idle, t.PhaseAt(0.6f));
        }

        [Test]
        public void State_DoesNothingUntilStarted()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            var step = s.Advance(1f, t);
            Assert.IsFalse(step.CrossedApex);
            Assert.IsFalse(step.Finished);
            Assert.IsFalse(s.IsSwinging);
            Assert.AreEqual(0f, s.Progress01(t));
        }

        [Test]
        public void State_FrameByFrame_CrossesApexOnceThenFinishes()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            Assert.IsTrue(s.TryStart());
            Assert.IsFalse(s.TryStart(), "can't start a second swing mid-swing");

            int apexes = 0, finishes = 0, frames = 0;
            float apexAt = -1f;
            while (s.IsSwinging && frames < 1000)
            {
                var step = s.Advance(1f / 60f, t);
                frames++;
                if (step.CrossedApex) { apexes++; apexAt = frames / 60f; }
                if (step.Finished) finishes++;
            }

            Assert.AreEqual(1, apexes);
            Assert.AreEqual(1, finishes);
            Assert.AreEqual(t.ApexSeconds, apexAt, 1f / 60f + 1e-4f);
            Assert.AreEqual(Mathf.CeilToInt(t.TotalSeconds * 60f - 1e-3f), frames);
            Assert.IsTrue(s.TryStart(), "can swing again once finished");
        }

        [Test]
        public void State_SkipPastApex_StillCrossesApex_AndLandsOnTheRightPose()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            s.TryStart();
            Assert.IsFalse(s.Advance(0.05f, t).CrossedApex); // mid wind-up

            var step = s.Advance(0.3f, t); // a blink: 0.05 s -> 0.35 s, past the apex into the recovery
            Assert.IsTrue(step.CrossedApex);
            Assert.IsFalse(step.Finished);
            Assert.IsTrue(s.ApexReached);
            Assert.AreEqual(0.35f, s.Elapsed, 1e-5f);
            Assert.AreEqual(0.35f / 0.57f, s.Progress01(t), 1e-4f);
            Assert.AreEqual(AxeSwingPhase.Recover, t.Sample(s.Elapsed).Phase);

            Assert.IsFalse(s.Advance(0.05f, t).CrossedApex, "the apex fires once per swing");
        }

        [Test]
        public void State_SkipPastTheWholeSwing_CrossesApexAndFinishesInOneStep()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            s.TryStart();
            s.Advance(0.02f, t);

            var step = s.Advance(0.5f * 3f, t); // e.g. a long skip
            Assert.IsTrue(step.CrossedApex);
            Assert.IsTrue(step.Finished);
            Assert.IsFalse(s.IsSwinging);
            Assert.AreEqual(0f, s.Progress01(t));
        }

        [Test]
        public void State_SkipAfterApex_DoesNotCrossItAgain()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            s.TryStart();
            Assert.IsTrue(s.Advance(0.25f, t).CrossedApex);
            var step = s.Advance(0.5f, t);
            Assert.IsFalse(step.CrossedApex);
            Assert.IsTrue(step.Finished);
        }

        [Test]
        public void State_LandingExactlyOnTheApex_Crosses()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            s.TryStart();
            Assert.IsTrue(s.Advance(t.ApexSeconds, t).CrossedApex);
        }

        [Test]
        public void State_CancelStopsWithoutApex()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            s.TryStart();
            s.Advance(0.1f, t);
            s.Cancel();
            Assert.IsFalse(s.IsSwinging);
            Assert.IsFalse(s.Advance(1f, t).CrossedApex);
            Assert.IsTrue(s.TryStart());
            Assert.IsFalse(s.ApexReached);
        }

        [Test]
        public void State_IgnoresZeroAndNegativeSteps()
        {
            var t = Timeline();
            var s = new AxeSwingState();
            s.TryStart();
            s.Advance(0f, t);
            s.Advance(-1f, t);
            Assert.AreEqual(0f, s.Elapsed);
            Assert.IsTrue(s.IsSwinging);
        }

        [Test]
        public void PoseBlend_HitsBothEnds_AndSlerpsBetween()
        {
            var a = new AxePose(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.4f), Vector3.zero);
            var b = new AxePose(new Vector3(0f, 90f, 0f), new Vector3(0.2f, 0f, 0.4f), new Vector3(0f, 0f, 60f));

            AxePose.Blend(a, b, 0f, out var p0, out var g0, out var r0);
            Assert.Less(Quaternion.Angle(p0, a.PivotRotation), 0.01f);
            Assert.AreEqual(0f, (g0 - a.GripPosition).magnitude, 1e-6f);
            Assert.Less(Quaternion.Angle(r0, a.GripRotation), 0.01f);

            AxePose.Blend(a, b, 1f, out var p1, out var g1, out var r1);
            Assert.Less(Quaternion.Angle(p1, b.PivotRotation), 0.01f);
            Assert.AreEqual(0f, (g1 - b.GripPosition).magnitude, 1e-6f);
            Assert.Less(Quaternion.Angle(r1, b.GripRotation), 0.01f);

            AxePose.Blend(a, b, 0.5f, out var ph, out var gh, out var rh);
            Assert.AreEqual(45f, Quaternion.Angle(ph, a.PivotRotation), 0.01f);
            Assert.AreEqual(0.1f, gh.x, 1e-6f);
            Assert.AreEqual(30f, Quaternion.Angle(rh, a.GripRotation), 0.01f);
        }

        static void AssertSample(AxeSwingSample s, AxeSwingPhase phase, AxeKeyPose from, AxeKeyPose to, float weight)
        {
            Assert.AreEqual(phase, s.Phase);
            Assert.AreEqual(from, s.From);
            Assert.AreEqual(to, s.To);
            Assert.AreEqual(weight, s.Weight, 1e-4f);
        }
    }
}
