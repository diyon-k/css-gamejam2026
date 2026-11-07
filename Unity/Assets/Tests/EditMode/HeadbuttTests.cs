using Blink.Player;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class HeadbuttTests
    {
        static Vector3 At(float t)
        {
            Headbutt.Sample(t, out var position, out _);
            return position;
        }

        [Test]
        public void RestsBeforeAndAfter()
        {
            foreach (float t in new[] { -1f, 0f, Headbutt.TotalSeconds, Headbutt.TotalSeconds + 1f })
            {
                Headbutt.Sample(t, out var position, out var euler);
                Assert.AreEqual(Vector3.zero, position, $"position at {t}");
                Assert.AreEqual(Vector3.zero, euler, $"rotation at {t}");
            }
        }

        [Test]
        public void DrawsBack_ThenSlamsForward_OnContact()
        {
            Assert.AreEqual(-Headbutt.PullBack, At(Headbutt.WindUpSeconds - 1e-4f).z, 1e-3f);
            Assert.AreEqual(Headbutt.Lunge, At(Headbutt.ContactSeconds).z, 1e-3f);

            // Contact is the furthest forward it ever goes.
            for (float t = 0f; t < Headbutt.TotalSeconds; t += 0.005f)
                Assert.LessOrEqual(At(t).z, Headbutt.Lunge + 1e-4f);
        }

        [Test]
        public void IsContinuous_NoJumps()
        {
            Vector3 last = At(0f);
            for (float t = 0.002f; t <= Headbutt.TotalSeconds + 0.01f; t += 0.002f)
            {
                Vector3 now = At(t);
                Assert.Less((now - last).magnitude, 0.03f, $"jump at {t:0.000}s");
                last = now;
            }
        }
    }
}
