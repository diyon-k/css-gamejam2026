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

        // Mixing: GamepadHaptics is the only thing driving the motors; each layer and pulse joins by per-motor max.

        [Test]
        public void Mix_ATickOnTheOtherMotor_DoesNotCutAGapInTheDrone()
        {
            var drone = new Rumble(0.08f, 0f);
            var dryFire = new Rumble(0f, 0.3f);
            Assert.AreEqual(new Rumble(0.08f, 0.3f), Rumble.Max(drone, dryFire));
        }

        [Test]
        public void Mix_ABiggerPulseWins_ThenTheDroneIsBack()
        {
            var r = new EyesShutRumble();
            r.Begin(0f);
            var gunshot = new Rumble(0.45f, 0.9f);
            Assert.AreEqual(gunshot, Rumble.Max(r.Sample(1f, 0f), gunshot));
            Assert.AreEqual(r.Sample(1f, 0f), Rumble.Max(r.Sample(1f, 0f), Rumble.Off), "pulse over: drone continues");
        }

        [Test]
        public void Mix_ALubOverDoorStrain_TakesTheStrongerOfEach()
        {
            var lub = new Rumble(1f, 0.3f);
            var strain = new Rumble(0.5f, 0.25f);
            Assert.AreEqual(new Rumble(1f, 0.3f), Rumble.Max(lub, strain));
        }

        [Test]
        public void Strength_ScalesThenCapsAtOne_AndRoundsToTwentieths()
        {
            var sent = new Rumble(0.7f, 0.2f).Scaled(1.6f).Quantized();
            Assert.AreEqual(1f, sent.Low, 1e-5f);
            Assert.AreEqual(0.3f, sent.High, 1e-5f); // 0.32 -> 0.3
            Assert.AreEqual(Rumble.Off, new Rumble(-1f, 0.01f).Quantized());
            Assert.AreEqual(Rumble.Off, new Rumble(0.5f, 0.5f).Scaled(0f).Quantized(), "strength 0 = silent");
        }

        [Test]
        public void Drone_StaysFeltAtDefaultStrength()
        {
            var r = new EyesShutRumble();
            r.Begin(0f);
            Assert.Greater(r.Sample(1f, 0f).Scaled(1.6f).Quantized().Low, 0f, "the quietest drone survives rounding");
        }
    }
}
