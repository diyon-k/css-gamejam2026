using Blink.Puzzles;
using NUnit.Framework;

namespace Blink.Tests
{
    /// <summary>Lily's doll state machine (docs/puzzles/LILYS_DOLL_ROOM.md, "Rules (exact)").</summary>
    public sealed class DollRuleTests
    {
        const float Grace = 0.15f, ShutKill = 1f, Frame = 0.02f;

        DollRule _rule;

        [SetUp]
        public void SetUp() => _rule = new DollRule(Grace, ShutKill);

        DollEvent Enter() => _rule.Tick(inRoom: true, hasKey: false, watched: true, realDt: Frame);

        [Test]
        public void StartsDormant() => Assert.AreEqual(DollState.Dormant, _rule.State);

        [Test]
        public void DormantIgnoresBlinksAndLookAway()
        {
            Assert.AreEqual(DollEvent.None, _rule.Blink());
            Assert.AreEqual(DollEvent.None, _rule.EyesShut(5f));
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(DollEvent.None, _rule.Tick(inRoom: false, hasKey: false, watched: false, realDt: Frame));
            Assert.AreEqual(DollState.Dormant, _rule.State);
        }

        [Test]
        public void EnteringArms()
        {
            Assert.AreEqual(DollEvent.None, Enter());
            Assert.AreEqual(DollState.Armed, _rule.State);
        }

        [Test]
        public void FirstBlinkWarnsSecondKills()
        {
            Enter();
            Assert.AreEqual(DollEvent.Warned, _rule.Blink());
            Assert.AreEqual(DollState.Warned, _rule.State);
            Assert.AreEqual(DollEvent.Kill, _rule.Blink());
            Assert.AreEqual(DollState.Killing, _rule.State);
        }

        [Test]
        public void LookingAwayShorterThanGraceDoesNothing()
        {
            Enter();
            Assert.AreEqual(DollEvent.None, _rule.Tick(true, false, watched: false, realDt: Grace * 0.9f));
            Assert.AreEqual(DollEvent.None, _rule.Tick(true, false, watched: true, realDt: Frame));
            // The timer starts over once watched again.
            Assert.AreEqual(DollEvent.None, _rule.Tick(true, false, watched: false, realDt: Grace * 0.9f));
            Assert.AreEqual(DollState.Armed, _rule.State);
        }

        [Test]
        public void LookingAwayForGraceKillsWithoutWarning()
        {
            Enter();
            Assert.AreEqual(DollEvent.None, _rule.Tick(true, false, watched: false, realDt: Grace * 0.6f));
            Assert.AreEqual(DollEvent.Kill, _rule.Tick(true, false, watched: false, realDt: Grace * 0.6f));
            Assert.AreEqual(DollState.Killing, _rule.State);
        }

        [Test]
        public void LookingAwayWhileWarnedKills()
        {
            Enter();
            _rule.Blink();
            Assert.AreEqual(DollEvent.Kill, _rule.Tick(true, false, watched: false, realDt: Grace));
        }

        [Test]
        public void EyesShutWarnsThenKillsPastShutKillSeconds()
        {
            Enter();
            Assert.AreEqual(DollEvent.Warned, _rule.EyesShut(0.35f));
            Assert.AreEqual(DollState.Warned, _rule.State);
            Assert.AreEqual(DollEvent.None, _rule.EyesShut(0.6f)); // same closure: the warning was this shut
            Assert.AreEqual(DollEvent.Kill, _rule.EyesShut(ShutKill));
            Assert.AreEqual(DollState.Killing, _rule.State);
        }

        [Test]
        public void EyesShutAfterAWarningBlinkKills()
        {
            Enter();
            _rule.Blink();
            Assert.AreEqual(DollEvent.Kill, _rule.EyesShut(0.35f));
        }

        [Test]
        public void SecondEyesShutKills()
        {
            Enter();
            _rule.EyesShut(0.35f);
            _rule.EyesShut(0.5f);
            // Eyes opened (no call), then a new closure starts: shorter closed time = a new shut.
            Assert.AreEqual(DollEvent.Kill, _rule.EyesShut(0.36f));
        }

        [Test]
        public void BlinkAfterEyesShutWarningKills()
        {
            Enter();
            _rule.EyesShut(0.4f);
            Assert.AreEqual(DollEvent.Kill, _rule.Blink());
        }

        [Test]
        public void LeavingWithoutKeyDisarmsAndClearsWarning()
        {
            Enter();
            _rule.Blink();
            Assert.AreEqual(DollEvent.Disarmed, _rule.Tick(inRoom: false, hasKey: false, watched: false, realDt: Frame));
            Assert.AreEqual(DollState.Dormant, _rule.State);
            Enter();
            Assert.AreEqual(DollState.Armed, _rule.State);
            Assert.AreEqual(DollEvent.Warned, _rule.Blink(), "a fresh warning");
        }

        [Test]
        public void LeavingWithKeyEscapesAndDoneIgnoresEverything()
        {
            Enter();
            _rule.Blink();
            Assert.AreEqual(DollEvent.None, _rule.Tick(inRoom: true, hasKey: true, watched: true, realDt: Frame));
            Assert.AreEqual(DollEvent.Escaped, _rule.Tick(inRoom: false, hasKey: true, watched: true, realDt: Frame));
            Assert.AreEqual(DollState.Done, _rule.State);

            Assert.AreEqual(DollEvent.None, _rule.Blink());
            Assert.AreEqual(DollEvent.None, _rule.EyesShut(3f));
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(DollEvent.None, _rule.Tick(inRoom: true, hasKey: false, watched: false, realDt: Frame));
            _rule.Reset();
            Assert.AreEqual(DollState.Done, _rule.State);
        }

        [Test]
        public void KillingIgnoresInputUntilReset()
        {
            Enter();
            _rule.Tick(true, false, watched: false, realDt: Grace);
            Assert.AreEqual(DollEvent.None, _rule.Blink());
            Assert.AreEqual(DollEvent.None, _rule.EyesShut(2f));
            Assert.AreEqual(DollEvent.None, _rule.Tick(inRoom: false, hasKey: true, watched: false, realDt: Frame));
            Assert.AreEqual(DollState.Killing, _rule.State);

            _rule.Reset();
            Assert.AreEqual(DollState.Dormant, _rule.State);
            Enter();
            Assert.AreEqual(DollEvent.Warned, _rule.Blink(), "warning cleared by the reset");
        }

        [Test]
        public void ResetClearsLookAwayTimer()
        {
            Enter();
            _rule.Tick(true, false, watched: false, realDt: Grace * 0.9f);
            _rule.Reset();
            Enter();
            Assert.AreEqual(DollEvent.None, _rule.Tick(true, false, watched: false, realDt: Grace * 0.5f));
        }
    }
}
