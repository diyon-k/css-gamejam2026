using Blink.TimeSkip;
using NUnit.Framework;

namespace Blink.Tests
{
    public class WorldTimerTests
    {
        [Test]
        public void DoesNothingUntilStarted()
        {
            var timer = new WorldTimer(2f);
            Assert.IsFalse(timer.Advance(5f));
            Assert.AreEqual(0f, timer.Progress01);
        }

        [Test]
        public void CompletesOnceWhenDurationReached_AndFiresEvent()
        {
            var timer = new WorldTimer(2f);
            int completions = 0;
            timer.Completed += () => completions++;
            timer.Start();

            Assert.IsFalse(timer.Advance(1.5f));
            Assert.AreEqual(0.75f, timer.Progress01, 1e-4f);
            Assert.IsTrue(timer.Advance(0.5f));
            Assert.IsFalse(timer.Advance(1f));

            Assert.AreEqual(1, completions);
            Assert.IsTrue(timer.IsComplete);
            Assert.AreEqual(0f, timer.Remaining);
        }

        [Test]
        public void SkipLargerThanRemaining_ClampsAndCompletes()
        {
            var timer = new WorldTimer(2f);
            timer.Start();
            Assert.IsTrue(timer.Advance(10f));
            Assert.AreEqual(2f, timer.Elapsed);
        }

        [Test]
        public void PauseKeepsProgress_ResumeContinues()
        {
            var timer = new WorldTimer(4f);
            timer.Start();
            timer.Advance(1f);
            timer.Pause();
            Assert.IsFalse(timer.Advance(10f));
            Assert.AreEqual(1f, timer.Elapsed);

            timer.Resume();
            Assert.IsTrue(timer.Advance(3f));
        }

        [Test]
        public void CancelResets()
        {
            var timer = new WorldTimer(4f);
            timer.Start();
            timer.Advance(2f);
            timer.Cancel();
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0f, timer.Elapsed);
        }
    }
}
