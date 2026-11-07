using Blink.Player;
using NUnit.Framework;

namespace Blink.Tests
{
    /// <summary>The flashlight's switch curve: off is dark, on stutters for the switch time then holds full.</summary>
    public sealed class FlashlightTests
    {
        [Test]
        public void Off_IsDark()
        {
            Assert.AreEqual(0f, FlashlightMath.Level(false, 0f, 0.14f));
            Assert.AreEqual(0f, FlashlightMath.Level(false, 10f, 0.14f));
        }

        [Test]
        public void On_AfterTheSwitchTime_IsFull()
        {
            Assert.AreEqual(1f, FlashlightMath.Level(true, 0.14f, 0.14f));
            Assert.AreEqual(1f, FlashlightMath.Level(true, 5f, 0.14f));
        }

        [Test]
        public void On_AtStart_OrNeverSwitched_IsFull()
        {
            // Never switched: the time since is +infinity (Time - -infinity).
            Assert.AreEqual(1f, FlashlightMath.Level(true, float.PositiveInfinity, 0.14f));
            Assert.AreEqual(1f, FlashlightMath.Level(true, float.NaN, 0.14f));
        }

        [Test]
        public void ZeroDuration_IsInstant()
        {
            Assert.AreEqual(1f, FlashlightMath.Level(true, 0f, 0f));
        }

        [Test]
        public void SwitchingOn_Stutters_AndStaysInRange()
        {
            bool dipped = false;
            for (int i = 0; i < 100; i++)
            {
                float level = FlashlightMath.Level(true, 0.14f * i / 100f, 0.14f);
                Assert.That(level, Is.InRange(0f, 1f));
                if (level < 0.2f) dipped = true;
            }
            Assert.IsTrue(dipped, "the switch-on should drop near black once");
        }
    }
}
