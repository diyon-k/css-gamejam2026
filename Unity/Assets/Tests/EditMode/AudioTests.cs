using Blink.Audio;
using NUnit.Framework;

namespace Blink.Tests
{
    public class CuePickerTests
    {
        [Test]
        public void NoClips_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, new CuePicker().Next(0, ClipOrder.Random, 0.5f));
        }

        [Test]
        public void SingleClip_AlwaysZero_EvenWithNoRepeat()
        {
            var picker = new CuePicker();
            for (int i = 0; i < 5; i++) Assert.AreEqual(0, picker.Next(1, ClipOrder.RandomNoRepeat, 0.9f));
        }

        [Test]
        public void RoundRobin_CyclesInOrder()
        {
            var picker = new CuePicker();
            int[] got = new int[7];
            for (int i = 0; i < got.Length; i++) got[i] = picker.Next(3, ClipOrder.RoundRobin, 0.5f);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 2, 0 }, got);
        }

        [Test]
        public void RandomNoRepeat_NeverPicksTheSameClipTwiceInARow()
        {
            var picker = new CuePicker();
            var rng = new System.Random(7);
            int last = picker.Next(4, ClipOrder.RandomNoRepeat, 0.5f);
            for (int i = 0; i < 500; i++)
            {
                int next = picker.Next(4, ClipOrder.RandomNoRepeat, (float)rng.NextDouble());
                Assert.AreNotEqual(last, next);
                Assert.That(next, Is.InRange(0, 3));
                last = next;
            }
        }

        [Test]
        public void RandomNoRepeat_StillReachesEveryOtherClip()
        {
            var picker = new CuePicker();
            picker.Next(3, ClipOrder.RoundRobin, 0f); // last = 0
            Assert.AreEqual(1, picker.Next(3, ClipOrder.RandomNoRepeat, 0f));
            picker.Reset();
            picker.Next(3, ClipOrder.RoundRobin, 0f); // last = 0
            Assert.AreEqual(2, picker.Next(3, ClipOrder.RandomNoRepeat, 0.99f));
        }

        [Test]
        public void Random_HandlesTheTopOfTheRange()
        {
            Assert.AreEqual(3, new CuePicker().Next(4, ClipOrder.Random, 1f));
        }
    }

    public class CueCooldownTests
    {
        [Test]
        public void BlocksUntilTheCooldownHasPassed()
        {
            var cd = new CueCooldown();
            Assert.IsTrue(cd.TryConsume(10f, 1f));
            Assert.IsFalse(cd.TryConsume(10.5f, 1f));
            Assert.IsTrue(cd.TryConsume(11f, 1f));
        }

        [Test]
        public void ZeroCooldown_NeverBlocks()
        {
            var cd = new CueCooldown();
            Assert.IsTrue(cd.TryConsume(1f, 0f));
            Assert.IsTrue(cd.TryConsume(1f, 0f));
        }

        [Test]
        public void ClockGoingBackwards_CountsAsReady()
        {
            // A new play session with domain reload off restarts Time.unscaledTime.
            var cd = new CueCooldown();
            Assert.IsTrue(cd.TryConsume(100f, 5f));
            Assert.IsTrue(cd.TryConsume(0.1f, 5f));
        }

        [Test]
        public void IsReady_DoesNotStartTheCooldown()
        {
            var cd = new CueCooldown();
            Assert.IsTrue(cd.IsReady(1f, 1f));
            Assert.IsTrue(cd.TryConsume(1f, 1f));
        }
    }

    public class StepCadenceTests
    {
        [Test]
        public void StepsEveryStepLength()
        {
            var steps = new StepCadence(0f);
            int count = 0;
            for (int i = 0; i < 100; i++) if (steps.Advance(0.1f, 1f)) count++; // 10 m
            Assert.AreEqual(10, count, 1);
        }

        [Test]
        public void HeadStart_MakesTheFirstStepComeEarly()
        {
            var steps = new StepCadence(0.5f);
            Assert.IsFalse(steps.Advance(0.4f, 1f));
            Assert.IsTrue(steps.Advance(0.15f, 1f));
        }

        [Test]
        public void BigJump_GivesOneStepNotABurst()
        {
            var steps = new StepCadence(0f);
            Assert.IsTrue(steps.Advance(10f, 1f));
            Assert.IsFalse(steps.Advance(0.1f, 1f));
        }

        [Test]
        public void ThreeTimesTheDistance_GivesThreeTimesTheSteps()
        {
            // Zombies feed scaled-time distance: fast-forward (3x) => 3x the footsteps per real second.
            var normal = new StepCadence(0f);
            var fast = new StepCadence(0f);
            int n = 0, f = 0;
            for (int i = 0; i < 600; i++)
            {
                if (normal.Advance(1.6f / 60f, 0.8f)) n++;
                if (fast.Advance(3f * 1.6f / 60f, 0.8f)) f++;
            }
            Assert.AreEqual(20, n, 1); // 16 m at 0.8 m per step
            Assert.AreEqual(60, f, 1); // 48 m

        }

        [Test]
        public void Reset_RestoresTheHeadStart()
        {
            var steps = new StepCadence(0.5f);
            steps.Advance(0.3f, 1f);
            steps.Reset();
            Assert.IsTrue(steps.Advance(0.5f, 1f));
        }
    }

    public class HeartbeatTests
    {
        [Test]
        public void Intensity_ZeroOutOfRange_OneWhenClose()
        {
            Assert.AreEqual(0f, HeartbeatMath.Intensity(20f, 2f, 15f));
            Assert.AreEqual(0f, HeartbeatMath.Intensity(float.PositiveInfinity, 2f, 15f));
            Assert.AreEqual(1f, HeartbeatMath.Intensity(1f, 2f, 15f));
            Assert.AreEqual(0.5f, HeartbeatMath.Intensity(8.5f, 2f, 15f), 1e-4f);
        }

        [Test]
        public void Intensity_RisesAsTheZombieGetsCloser()
        {
            float prev = -1f;
            for (float d = 16f; d >= 0f; d -= 0.5f)
            {
                float i = HeartbeatMath.Intensity(d, 2f, 15f);
                Assert.GreaterOrEqual(i, prev);
                prev = i;
            }
        }

        [Test]
        public void RateAndVolume_MapIntensity()
        {
            Assert.AreEqual(65f, HeartbeatMath.Bpm(0f, 65f, 170f));
            Assert.AreEqual(170f, HeartbeatMath.Bpm(1f, 65f, 170f));
            Assert.AreEqual(0f, HeartbeatMath.Volume(0f, 0.25f, 1f));
            Assert.AreEqual(0.25f, HeartbeatMath.Volume(0.0001f, 0.25f, 1f), 1e-3f);
            Assert.AreEqual(1f, HeartbeatMath.Volume(1f, 0.25f, 1f));
        }

        [Test]
        public void Clock_BeatsAtTheRequestedRate()
        {
            var clock = new HeartbeatClock();
            int beats = 0;
            for (int i = 0; i < 600; i++) if (clock.Tick(1f / 60f, 120f)) beats++; // 10 s at 120 bpm
            Assert.AreEqual(20, beats, 1);
        }

        [Test]
        public void Clock_LongHitch_GivesOneBeat()
        {
            var clock = new HeartbeatClock();
            Assert.IsTrue(clock.Tick(5f, 120f));
            Assert.IsFalse(clock.Tick(0.01f, 120f));
        }
    }

    public class ReloadFoleyTrackTests
    {
        static readonly float[] Marks = { 0.2f, 0.55f, 0.82f };

        [Test]
        public void PlaysEachMarkOnceAsProgressPasses()
        {
            var track = new ReloadFoleyTrack();
            track.Begin(Marks);
            var hits = new System.Collections.Generic.List<int>();
            for (int i = 1; i <= 100; i++)
            {
                int m = track.Advance(i / 100f);
                if (m >= 0) hits.Add(m);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, hits);
        }

        [Test]
        public void SkipOverSeveralMarks_PlaysOnlyTheLatest()
        {
            var track = new ReloadFoleyTrack();
            track.Begin(Marks);
            Assert.AreEqual(1, track.Advance(0.6f));
            Assert.AreEqual(-1, track.Advance(0.7f));
            Assert.AreEqual(2, track.Advance(1f));
        }

        [Test]
        public void NegativeMarks_AreSkipped()
        {
            var track = new ReloadFoleyTrack();
            track.Begin(new[] { 0.2f, 0.5f, -1f });
            Assert.AreEqual(1, track.Advance(1f));
        }

        [Test]
        public void StartingMidway_DoesNotReplayEarlierMarks()
        {
            var track = new ReloadFoleyTrack();
            track.Begin(Marks, 0.3f);
            Assert.AreEqual(1, track.Advance(0.6f));
        }
    }

    public class EyesMixStateTests
    {
        [Test]
        public void ShutFadesIn_OpenFadesOut_GrabSnapsBack()
        {
            var mix = new EyesMixState();
            Assert.AreEqual(0.15f, mix.Request(true, false, 0.15f, 0.25f));
            Assert.IsTrue(mix.Shut);
            Assert.AreEqual(0f, mix.Request(false, true, 0.15f, 0.25f));
            Assert.IsFalse(mix.Shut);
            mix.Request(true, false, 0.15f, 0.25f);
            Assert.AreEqual(0.25f, mix.Request(false, false, 0.15f, 0.25f));
        }

        [Test]
        public void RepeatedRequests_AreIgnored()
        {
            var mix = new EyesMixState();
            Assert.Less(mix.Request(false, false, 0.15f, 0.25f), 0f);
            mix.Request(true, false, 0.15f, 0.25f);
            Assert.Less(mix.Request(true, false, 0.15f, 0.25f), 0f);
        }
    }

    public class ZombieVocalRulesTests
    {
        [TestCase("Idle", "Chase", ZombieVocal.Alert)]
        [TestCase("Patrol", "Chase", ZombieVocal.Alert)]
        [TestCase("Investigate", "Chase", ZombieVocal.Alert)]
        [TestCase("Idle", "Attack", ZombieVocal.Alert)]
        [TestCase("Attack", "Chase", ZombieVocal.None)]
        [TestCase("Chase", "Attack", ZombieVocal.None)]
        [TestCase("Idle", "Investigate", ZombieVocal.Groan)]
        [TestCase("Chase", "Idle", ZombieVocal.None)]
        [TestCase("Idle", "Patrol", ZombieVocal.None)]
        [TestCase("Chase", "Dead", ZombieVocal.None)]
        [TestCase("Idle", "SomeFutureState", ZombieVocal.None)]
        [TestCase(null, "Chase", ZombieVocal.Alert)]
        public void MapsStateChanges(string previous, string next, ZombieVocal expected)
        {
            Assert.AreEqual(expected, ZombieVocalRules.OnStateChanged(previous, next));
        }

        [Test]
        public void EveryMobStateIsHandled()
        {
            foreach (var state in System.Enum.GetNames(typeof(Blink.Mobs.MobController.State)))
                Assert.DoesNotThrow(() => ZombieVocalRules.OnStateChanged("Idle", state));
        }
    }
}
