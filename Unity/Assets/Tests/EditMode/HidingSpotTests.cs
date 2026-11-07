using Blink.Interaction;
using Blink.Mobs;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class HidingSpotTests
    {
        GameObject _a, _b;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_a);
            Object.DestroyImmediate(_b);
        }

        [Test]
        public void Zombie_OnlyKnowsTheSpotItWatchedYouGetInto()
        {
            var a = (_a = new GameObject("A")).AddComponent<HidingSpot>();
            var b = (_b = new GameObject("B")).AddComponent<HidingSpot>();

            Assert.IsNull(MobController.KnownHidingSpot(null, a, true), "not hiding: nothing to know");
            Assert.IsNull(MobController.KnownHidingSpot(a, null, false), "didn't see you get in: blind");
            Assert.AreSame(a, MobController.KnownHidingSpot(a, null, true), "watched you get in");
            Assert.AreSame(a, MobController.KnownHidingSpot(a, a, false), "keeps knowing while you stay");
            Assert.IsNull(MobController.KnownHidingSpot(b, a, false), "a different spot, unseen");
        }
    }
}
