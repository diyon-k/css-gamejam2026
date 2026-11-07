using Blink.Puzzles;
using NUnit.Framework;

namespace Blink.Tests
{
    /// <summary>The flooded basement's water level (docs/puzzles/FLOODED_BASEMENT.md, "Water level").</summary>
    public sealed class FloodLevelTests
    {
        const float Rise = 0.012f, Jump = 0.32f, ShutEvery = 0.5f, Drain = 0.35f, Eyes = 1.6f, Eps = 1e-4f;

        FloodLevel _flood;

        [SetUp]
        public void SetUp() => _flood = new FloodLevel(Rise, Jump, ShutEvery, Drain);

        void Tick(float worldDt, float realDt = -1f, bool eyesShut = false, float drownHeight = Eyes) =>
            _flood.Tick(worldDt, realDt < 0f ? worldDt : realDt, eyesShut, drownHeight);

        [Test]
        public void StartsIdleAndDry()
        {
            Assert.AreEqual(FloodState.Idle, _flood.State);
            Assert.AreEqual(0f, _flood.Level);
        }

        [Test]
        public void IdleIgnoresTicksBlinksAndShutEyes()
        {
            Tick(10f);
            Tick(1f, 1f, eyesShut: true);
            _flood.Blink(0.5f);
            _flood.OpenDrain();
            Assert.AreEqual(FloodState.Idle, _flood.State);
            Assert.AreEqual(0f, _flood.Level);
        }

        [Test]
        public void StartBeginsFlooding()
        {
            _flood.Start();
            Assert.AreEqual(FloodState.Flooding, _flood.State);
        }

        [Test]
        public void FloodingRisesWithWorldTime()
        {
            _flood.Start();
            Tick(10f, realDt: 1f);
            Assert.AreEqual(Rise * 10f, _flood.Level, Eps);
        }

        [Test]
        public void EachBlinkAddsOneJump()
        {
            _flood.Start();
            _flood.Blink(0.5f);
            _flood.Blink(0.5f);
            Assert.AreEqual(2f * Jump, _flood.Level, Eps);
            Assert.AreEqual(2, _flood.JumpCount);
        }

        [Test]
        public void EyesShutAddsAJumpPerIntervalOfRealTime()
        {
            _flood.Start();
            for (int i = 0; i < 50; i++) Tick(0f, realDt: 0.02f, eyesShut: true); // 1.0 s real, no world time
            Assert.AreEqual(2f * Jump, _flood.Level, Eps);
        }

        [Test]
        public void EyesShutDoesNotJumpOnWorldTime()
        {
            _flood.Start();
            Tick(3f, realDt: 0.1f, eyesShut: true); // fast-forward: lots of world time, little real time
            Assert.AreEqual(Rise * 3f, _flood.Level, Eps);
        }

        [Test]
        public void OpeningTheEyesRestartsTheShutCount()
        {
            _flood.Start();
            Tick(0f, realDt: 0.4f, eyesShut: true);
            Tick(0f, realDt: 0.1f, eyesShut: false);
            Tick(0f, realDt: 0.4f, eyesShut: true);
            Assert.AreEqual(0f, _flood.Level, Eps);
        }

        [Test]
        public void ReachingDrownHeightDrowns()
        {
            _flood.Start();
            for (int i = 0; i < 4; i++) _flood.Blink(0.5f); // 1.28
            Tick(0.01f);
            Assert.AreEqual(FloodState.Flooding, _flood.State);
            _flood.Blink(0.5f); // 1.60
            Tick(0.01f);
            Assert.AreEqual(FloodState.Drowned, _flood.State);
        }

        [Test]
        public void ALowerDrownHeightDrownsSooner()
        {
            _flood.Start();
            for (int i = 0; i < 4; i++) _flood.Blink(0.5f); // 1.28: fine standing, over a crouching head
            Tick(0.01f, drownHeight: 1.1f);
            Assert.AreEqual(FloodState.Drowned, _flood.State);
        }

        [Test]
        public void DrownedIgnoresEverythingUntilReset()
        {
            _flood.Start();
            for (int i = 0; i < 6; i++) _flood.Blink(0.5f);
            Tick(0.01f);
            float level = _flood.Level;
            Tick(10f, 10f, eyesShut: true);
            _flood.Blink(0.5f);
            _flood.OpenDrain();
            Assert.AreEqual(FloodState.Drowned, _flood.State);
            Assert.AreEqual(level, _flood.Level, Eps);
        }

        [Test]
        public void OpenDrainDrainsWithWorldTime()
        {
            _flood.Start();
            for (int i = 0; i < 3; i++) _flood.Blink(0.5f); // 0.96
            _flood.OpenDrain();
            Assert.AreEqual(FloodState.Draining, _flood.State);
            Tick(1f, realDt: 0.1f);
            Assert.AreEqual(3f * Jump - Drain, _flood.Level, Eps);
        }

        [Test]
        public void ABlinkWhileDrainingLowersTheWater()
        {
            _flood.Start();
            for (int i = 0; i < 3; i++) _flood.Blink(0.5f);
            _flood.OpenDrain();
            _flood.Blink(0.5f);
            Assert.AreEqual(3f * Jump - Drain * 0.5f, _flood.Level, Eps);
            Assert.AreEqual(3, _flood.JumpCount);
        }

        [Test]
        public void ShutEyesDoNotRaiseTheWaterWhileDraining()
        {
            _flood.Start();
            _flood.Blink(0.5f);
            _flood.OpenDrain();
            Tick(0f, realDt: 2f, eyesShut: true);
            Assert.AreEqual(Jump, _flood.Level, Eps);
        }

        [Test]
        public void DrainingToZeroSolves()
        {
            _flood.Start();
            _flood.Blink(0.5f);
            _flood.OpenDrain();
            Tick(Jump / Drain + 0.01f);
            Assert.AreEqual(FloodState.Solved, _flood.State);
            Assert.AreEqual(0f, _flood.Level);
        }

        [Test]
        public void ABlinkCanFinishTheDrain()
        {
            _flood.Start();
            Tick(5f); // 0.06
            _flood.OpenDrain();
            _flood.Blink(0.5f);
            Assert.AreEqual(FloodState.Solved, _flood.State);
            Assert.AreEqual(0f, _flood.Level);
        }

        [Test]
        public void DrainingNeverDrowns()
        {
            _flood.Start();
            for (int i = 0; i < 4; i++) _flood.Blink(0.5f);
            _flood.OpenDrain();
            Tick(0.01f, drownHeight: 0.5f);
            Assert.AreEqual(FloodState.Draining, _flood.State);
        }

        [Test]
        public void OpenDrainOnlyWorksWhileFlooding()
        {
            _flood.OpenDrain();
            Assert.AreEqual(FloodState.Idle, _flood.State);
        }

        [Test]
        public void LevelStopsAtTheCeiling()
        {
            _flood.MaxLevel = 2f;
            _flood.Start();
            for (int i = 0; i < 20; i++) _flood.Blink(0.5f);
            Tick(100f, drownHeight: float.PositiveInfinity);
            Assert.AreEqual(2f, _flood.Level, Eps);
        }

        [Test]
        public void ResetFromDrownedGoesBackToIdleAndDry()
        {
            _flood.Start();
            for (int i = 0; i < 6; i++) _flood.Blink(0.5f);
            Tick(0.01f);
            _flood.Reset();
            Assert.AreEqual(FloodState.Idle, _flood.State);
            Assert.AreEqual(0f, _flood.Level);
            Assert.AreEqual(0, _flood.JumpCount);
        }

        [Test]
        public void ResetClearsAPartialShutCount()
        {
            _flood.Start();
            Tick(0f, realDt: 0.4f, eyesShut: true);
            _flood.Reset();
            _flood.Start();
            Tick(0f, realDt: 0.2f, eyesShut: true);
            Assert.AreEqual(0f, _flood.Level, Eps);
        }

        [Test]
        public void ResetDoesNothingOnceSolved()
        {
            _flood.Start();
            _flood.OpenDrain();
            Tick(0.01f);
            Assert.AreEqual(FloodState.Solved, _flood.State);
            _flood.Reset();
            Assert.AreEqual(FloodState.Solved, _flood.State);
        }

        [Test]
        public void SolvedIgnoresEverything()
        {
            _flood.Start();
            _flood.OpenDrain();
            Tick(0.01f);
            _flood.Start();
            _flood.Blink(0.5f);
            Tick(10f, 10f, eyesShut: true);
            Assert.AreEqual(FloodState.Solved, _flood.State);
            Assert.AreEqual(0f, _flood.Level);
        }
    }
}
