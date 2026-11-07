using Blink.Player;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class GrabShovePoseTests
    {
        static Vector3 At(float t, out float pitch)
        {
            GrabShovePose.Sample(t, out var offset, out pitch);
            return offset;
        }

        static Vector3 At(float t) => At(t, out _);

        [Test]
        public void RestsBeforeAndAfter()
        {
            foreach (float t in new[] { -1f, 0f, GrabShovePose.EndSeconds, GrabShovePose.EndSeconds + 1f })
            {
                Assert.AreEqual(Vector3.zero, At(t, out float pitch), $"t={t}");
                Assert.AreEqual(0f, pitch, 1e-5f, $"t={t}");
            }
        }

        [Test]
        public void PullsIn_ThenThrustsOut_FurthestAtContact()
        {
            Assert.Less(At(GrabShovePose.WindUpSeconds).z, -0.05f); // elbows bend: hands come in to the chest
            float reach = At(GrabShovePose.ContactSeconds).z;
            Assert.Greater(reach, 0f);
            for (float t = 0f; t < GrabShovePose.EndSeconds; t += 0.005f)
                Assert.LessOrEqual(At(t).z, reach + 1e-5f);
        }

        [Test]
        public void HoldsTheReach_AfterContact()
        {
            float reach = At(GrabShovePose.ContactSeconds).z;
            Assert.Greater(At(GrabShovePose.HoldUntil).z, reach * 0.7f);
        }

        [Test]
        public void HandsComeTogether_OnTheWayOut()
        {
            GrabShovePose.Sample(GrabShovePose.ContactSeconds, out _, out _, out float together);
            Assert.Greater(together, 0f);
            GrabShovePose.Sample(GrabShovePose.EndSeconds, out _, out _, out together);
            Assert.AreEqual(0f, together, 1e-5f);
        }

        [Test]
        public void IsContinuous_NoJumps()
        {
            Vector3 last = At(0f, out float lastPitch);
            for (float t = 0.002f; t <= GrabShovePose.EndSeconds + 0.01f; t += 0.002f)
            {
                Vector3 now = At(t, out float pitch);
                Assert.Less((now - last).magnitude, 0.01f, $"t={t}");
                Assert.Less(Mathf.Abs(pitch - lastPitch), 1.5f, $"t={t}");
                last = now;
                lastPitch = pitch;
            }
        }
    }
}
