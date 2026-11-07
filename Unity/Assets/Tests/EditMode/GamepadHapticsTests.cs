using Blink.Effects;
using NUnit.Framework;

namespace Blink.Tests
{
    public sealed class GamepadHapticsTests
    {
        [Test]
        public void DoubleTap_IsOnThenSilentThenOnThenOver()
        {
            Assert.IsTrue(GamepadHaptics.Envelope(0.02f, 0.04f, 0.07f, 2, false, out float a)); Assert.AreEqual(1f, a);
            Assert.IsTrue(GamepadHaptics.Envelope(0.08f, 0.04f, 0.07f, 2, false, out float b)); Assert.AreEqual(0f, b);
            Assert.IsTrue(GamepadHaptics.Envelope(0.13f, 0.04f, 0.07f, 2, false, out float c)); Assert.AreEqual(1f, c);
            Assert.IsFalse(GamepadHaptics.Envelope(0.16f, 0.04f, 0.07f, 2, false, out _));
        }

        [Test]
        public void Decay_FadesLinearly()
        {
            GamepadHaptics.Envelope(0.05f, 0.1f, 0f, 1, true, out float env);
            Assert.AreEqual(0.5f, env, 1e-4f);
        }

        [Test]
        public void NoPulseYet_IsNotRunning()
        {
            Assert.IsFalse(GamepadHaptics.Envelope(float.PositiveInfinity, 0f, 0f, 0, false, out _));
        }
    }
}
