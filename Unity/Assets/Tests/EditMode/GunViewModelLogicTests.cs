using Blink.Combat;
using NUnit.Framework;

namespace Blink.Tests
{
    public class GunViewModelLogicTests
    {
        [Test]
        public void ShotThatEmptiesMagazine_PlaysFireLast_OthersPlayFire()
        {
            Assert.AreEqual(GunViewClip.Fire, GunViewModelLogic.FireClip(7));
            Assert.AreEqual(GunViewClip.Fire, GunViewModelLogic.FireClip(1));
            Assert.AreEqual(GunViewClip.FireLast, GunViewModelLogic.FireClip(0));
        }

        [Test]
        public void ReloadFromEmpty_PlaysReloadEmpty_PartialPlaysReload()
        {
            Assert.AreEqual(GunViewClip.Reload, GunViewModelLogic.ReloadClip(fromEmpty: false));
            Assert.AreEqual(GunViewClip.ReloadEmpty, GunViewModelLogic.ReloadClip(fromEmpty: true));
        }

        [Test]
        public void WithoutLeadIn_ClipTimeEqualsProgress()
        {
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(0f, 2.333f, 0f, out float t));
            Assert.AreEqual(0f, t, 1e-5f);
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(0.4f, 2.333f, 0f, out t));
            Assert.AreEqual(0.4f, t, 1e-5f);
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(1f, 2.333f, 0f, out t));
            Assert.AreEqual(1f, t, 1e-5f);
        }

        [Test]
        public void DuringLeadIn_ReportsNotYet()
        {
            // 3 s reload whose first 0.5 s belongs to FireLast.
            Assert.IsFalse(GunViewModelLogic.TryGetReloadClipTime(0f, 3f, 0.5f, out _));
            Assert.IsFalse(GunViewModelLogic.TryGetReloadClipTime(0.1f, 3f, 0.5f, out _)); // 0.3 s in
        }

        [Test]
        public void AfterLeadIn_ClipCoversTheRestOfTheReload()
        {
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(0.5f / 3f, 3f, 0.5f, out float t));
            Assert.AreEqual(0f, t, 1e-5f);
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(0.5f, 3f, 0.5f, out t)); // 1.5 s in: 1 of 2.5 s
            Assert.AreEqual(0.4f, t, 1e-5f);
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(1f, 3f, 0.5f, out t));
            Assert.AreEqual(1f, t, 1e-5f);
        }

        [Test]
        public void SkipPastLeadIn_JumpsStraightIntoTheClip()
        {
            // A 0.5 s blink right after the last shot lands 0.5 s into a 3 s reload with a 0.37 s lead-in.
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(0.5f / 3f, 3f, 0.37f, out float t));
            Assert.AreEqual(0.13f / 2.63f, t, 1e-4f);
        }

        [Test]
        public void ProgressOutOfRange_IsClamped()
        {
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(1.5f, 2f, 0f, out float t));
            Assert.AreEqual(1f, t, 1e-5f);
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(-1f, 2f, 0f, out t));
            Assert.AreEqual(0f, t, 1e-5f);
        }

        [Test]
        public void DegenerateDurations_FinishTheClip()
        {
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(0.5f, 0f, 0f, out float t));
            Assert.AreEqual(1f, t);
            // Lead-in as long as the whole reload: nothing left for the clip, so it shows the end once done.
            Assert.IsFalse(GunViewModelLogic.TryGetReloadClipTime(0.5f, 1f, 2f, out _));
            Assert.IsTrue(GunViewModelLogic.TryGetReloadClipTime(1f, 1f, 2f, out t));
            Assert.AreEqual(1f, t);
        }
    }
}
