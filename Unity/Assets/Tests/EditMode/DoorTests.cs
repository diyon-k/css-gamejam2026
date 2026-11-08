using Blink.World;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class DoorTests
    {
        static DoorModel Run(DoorModel m, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.02f) m.Step(0.02f);
            return m;
        }

        [Test]
        public void Opens_AwayFromThePlayer()
        {
            var front = new DoorModel(false, 0);
            Assert.AreEqual(DoorResult.Opened, front.Interact(+1f, 0, null));
            Assert.AreEqual(-90f, Run(front, 1f).Angle, 1e-3f); // player in front: swings to the back

            var back = new DoorModel(false, 0);
            back.Interact(-1f, 0, null);
            Assert.AreEqual(90f, Run(back, 1f).Angle, 1e-3f);
        }

        [Test]
        public void Slam_ShutsFastAndLocks_UnlockOpensAgain()
        {
            var m = new DoorModel(false, 0, seconds: 0.5f);
            m.Snap(90f);
            Assert.IsTrue(m.Slam(0.12f));
            Assert.IsTrue(m.Locked);
            Assert.IsFalse(m.Open(-1f)); // locked from the first frame
            Run(m, 0.14f);
            Assert.AreEqual(0f, m.Angle, 1e-3f); // shut in the slam time, not the door's 0.5 s
            Assert.AreEqual(DoorResult.Rattled, m.Interact(1f, 0, null));
            Assert.AreEqual("Locked", m.Prompt(0));

            m.Unlock();
            Assert.IsTrue(m.Open(1f));
            Run(m, 0.1f);
            Assert.Less(Mathf.Abs(m.Angle), 89f); // a normal open is back to the door's own speed
            Assert.IsFalse(new DoorModel(false, 0).Slam(0.12f)); // already shut: nothing moves, still locks
        }

        [Test]
        public void Toggle_OpensThenCloses()
        {
            var m = new DoorModel(false, 0);
            m.Interact(1f, 0, null);
            Run(m, 1f);
            Assert.IsTrue(m.IsOpen);
            Assert.AreEqual("Close door", m.Prompt(0));
            Assert.AreEqual(DoorResult.Closed, m.Interact(-1f, 0, null)); // from either side
            Run(m, 1f);
            Assert.AreEqual(0f, m.Angle, 1e-3f);
            Assert.IsFalse(m.IsOpen);
            Assert.AreEqual("Open door", m.Prompt(0));
        }

        [Test]
        public void Close_RefusedWhileTheSweepIsBlocked()
        {
            var m = new DoorModel(false, 0);
            m.Interact(1f, 0, null);
            Run(m, 1f);
            Assert.AreEqual(DoorResult.Blocked, m.Interact(1f, 0, t => true));
            Assert.IsTrue(m.IsOpen);
        }

        [Test]
        public void Open_GoesTheOtherWayWhenTheAwaySideIsBlocked()
        {
            var m = new DoorModel(false, 0);
            // player in front: away = -90, but something stands in that arc
            Assert.AreEqual(DoorResult.Opened, m.Interact(1f, 0, t => t < 0f));
            Assert.AreEqual(90f, Run(m, 1f).Angle, 1e-3f);
        }

        [Test]
        public void BackSide_OpensOnlyAsFarAsItsOwnAngle()
        {
            var m = new DoorModel(false, 0, 90f, 0.5f, backAngle: 75f);
            m.Interact(1f, 0, null); // player in front: away is the back
            Assert.AreEqual(-75f, Run(m, 1f).Angle, 1e-3f);
            m.Interact(1f, 0, null);
            Run(m, 1f);
            m.Interact(-1f, 0, null); // player behind: away is the front
            Assert.AreEqual(90f, Run(m, 1f).Angle, 1e-3f);
        }

        [Test]
        public void BlockedAway_FallsBackToTheOtherSidesOwnAngle()
        {
            var m = new DoorModel(false, 0, 90f, 0.5f, backAngle: 75f);
            Assert.AreEqual(DoorResult.Opened, m.Interact(-1f, 0, t => t > 0f)); // front arc occupied
            Assert.AreEqual(-75f, Run(m, 1f).Angle, 1e-3f);
        }

        [Test]
        public void Open_RefusedWhenBothArcsAreBlocked()
        {
            var m = new DoorModel(false, 0);
            Assert.AreEqual(DoorResult.Blocked, m.Interact(1f, 0, t => t != 0f));
            Assert.IsFalse(m.IsOpen);
            Assert.IsFalse(m.Moving);
        }

        [Test]
        public void Delay_HoldsTheLeafThenSwings()
        {
            var m = new DoorModel(false, 0, 90f, 0.5f);
            m.Interact(-1f, 0, null, delay: 0.1f);
            m.Step(0.08f);
            Assert.AreEqual(0f, m.Angle);
            Assert.IsTrue(m.Moving);
            Run(m, 0.7f);
            Assert.AreEqual(90f, m.Angle, 1e-4f);
            Assert.IsFalse(m.Moving);
        }

        [Test]
        public void Animation_TakesAboutHalfASecondAndSettlesExactly()
        {
            var m = new DoorModel(false, 0, 90f, 0.5f);
            m.Interact(-1f, 0, null);
            Run(m, 0.2f);
            Assert.That(m.Angle, Is.GreaterThan(5f).And.LessThan(90f));
            Run(m, 0.4f);
            Assert.AreEqual(90f, m.Angle, 1e-4f);
            Assert.IsFalse(m.Moving);
        }

        [Test]
        public void Locked_RattlesAndDoesNotMove()
        {
            var m = new DoorModel(true, 0);
            Assert.AreEqual(DoorResult.Rattled, m.Interact(1f, 99, null));
            Assert.IsTrue(m.Locked);
            Assert.AreEqual("Locked", m.Prompt(99));
            m.Step(0.03f);
            Assert.AreNotEqual(m.Angle, m.PoseAngle); // the knock
            Assert.AreEqual(0f, m.Angle);
            Run(m, 0.5f);
            Assert.AreEqual(0f, m.PoseAngle, 1e-4f);
            Assert.IsFalse(m.Open(1f));
        }

        [TestCase(0, "Locked · 0/3 keys")]
        [TestCase(1, "Use key · 0/3 keys")]
        [TestCase(2, "Use key · 0/3 keys")]
        [TestCase(3, "Unlock door")]
        [TestCase(5, "Unlock door")]
        public void KeyPrompt_ShowsTheCount(int held, string expected)
        {
            Assert.AreEqual(expected, new DoorModel(true, 3).Prompt(held));
        }

        [Test]
        public void ThreeKeys_OneAtATime_TheLastUnlocks_ThenItOpensLikeAnyDoor()
        {
            var m = new DoorModel(true, 3);
            Assert.AreEqual(DoorResult.Rattled, m.Interact(1f, 0, null));
            Assert.AreEqual(0, m.LastKeysUsed);

            Assert.AreEqual(DoorResult.KeyUsed, m.Interact(1f, 1, null));
            Assert.AreEqual(1, m.LastKeysUsed);
            Assert.AreEqual("Locked · 1/3 keys", m.Prompt(0));
            Assert.AreEqual("Use key · 1/3 keys", m.Prompt(1));

            Assert.AreEqual(DoorResult.KeyUsed, m.Interact(1f, 1, null));
            Assert.AreEqual("Unlock door", m.Prompt(1)); // the next key finishes it
            Assert.IsTrue(m.Locked);

            Assert.AreEqual(DoorResult.Unlocked, m.Interact(1f, 1, null));
            Assert.AreEqual(1, m.LastKeysUsed);
            Assert.AreEqual(3, m.KeysUsed);
            Assert.IsFalse(m.Locked);
            Assert.IsFalse(m.IsOpen); // unlocking is its own beat
            Assert.AreEqual("Open door", m.Prompt(0));
            Assert.AreEqual(DoorResult.Opened, m.Interact(1f, 0, null));
            Assert.AreEqual(0, m.LastKeysUsed);
        }

        [Test]
        public void Keys_OnlyAsManyAsTheLockNeedsAreUsed()
        {
            var m = new DoorModel(true, 3);
            m.Interact(1f, 1, null);
            Assert.AreEqual(DoorResult.Unlocked, m.Interact(1f, 5, null)); // a pocketful (no one-hand rule)
            Assert.AreEqual(2, m.LastKeysUsed);
        }

        [Test]
        public void OneKeyDoor_SaysLocked_ThenUnlock()
        {
            var m = new DoorModel(true, 1);
            Assert.AreEqual("Locked", m.Prompt(0));
            Assert.AreEqual("Unlock door", m.Prompt(1));
            Assert.AreEqual(DoorResult.Unlocked, m.Interact(-1f, 1, null));
        }

        [Test]
        public void StoryLock_NeverUnlocksWithKeys()
        {
            var m = new DoorModel(true, 0);
            Assert.IsFalse(m.CanUnlock(10));
            Assert.AreEqual(DoorResult.Rattled, m.Interact(1f, 10, null));
            Assert.AreEqual(0, m.LastKeysUsed); // nobody's keys go into a lock that has none
        }

        [Test]
        public void Snap_PosesWithoutAnimating()
        {
            var m = new DoorModel(false, 0);
            m.Snap(90f);
            Assert.IsTrue(m.IsOpen);
            Assert.IsFalse(m.Moving);
            Assert.AreEqual(90f, m.PoseAngle);
        }

        [Test]
        public void Sweep_HitsAPlayerInTheArc_NotBehindOrBeyond()
        {
            var hinge = Vector2.zero;
            // leaf 1 m long swinging from heading 0 (+z) to 90 (+x)
            Assert.IsTrue(DoorModel.SweepHits(hinge, 1f, 0f, 90f, new Vector2(0.5f, 0.5f), 0.3f));
            Assert.IsFalse(DoorModel.SweepHits(hinge, 1f, 0f, 90f, new Vector2(-0.8f, 0.2f), 0.3f)); // behind the hinge
            Assert.IsFalse(DoorModel.SweepHits(hinge, 1f, 0f, 90f, new Vector2(0.5f, 2f), 0.3f));    // too far
            Assert.IsFalse(DoorModel.SweepHits(hinge, 1f, 0f, 90f, new Vector2(0.5f, -0.8f), 0.3f)); // other quadrant
            // reverse (closing) sweep and wrap-around
            Assert.IsTrue(DoorModel.SweepHits(hinge, 1f, 90f, 0f, new Vector2(0.7f, 0.7f), 0.3f));
            Assert.IsTrue(DoorModel.SweepHits(hinge, 1f, 170f, -170f, new Vector2(0f, -0.8f), 0.3f));
        }

        [Test]
        public void Sweep_IgnoresSomeoneLeaningOnTheSideItLeaves()
        {
            // closed leaf along +z from the hinge, swinging toward +x; the player touches its -x face
            Assert.IsFalse(DoorModel.SweepHits(Vector2.zero, 1f, 0f, 90f, new Vector2(-0.38f, 0.6f), 0.4f));
            // the same player is hit if the leaf swings toward them
            Assert.IsTrue(DoorModel.SweepHits(Vector2.zero, 1f, 0f, -90f, new Vector2(-0.38f, 0.6f), 0.4f));
        }

        [Test]
        public void Heading_IsAYaw()
        {
            Assert.AreEqual(0f, DoorModel.Heading(new Vector2(0f, 1f)), 1e-4f);
            Assert.AreEqual(90f, DoorModel.Heading(new Vector2(1f, 0f)), 1e-4f);
        }
    }
}
