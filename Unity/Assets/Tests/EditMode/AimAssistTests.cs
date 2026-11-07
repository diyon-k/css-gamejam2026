using Blink.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class AimAssistTests
    {
        [Test]
        public void AimErrorTurnsTowardTheTarget()
        {
            Assert.AreEqual(Vector2.zero, AimDownSights.AimError(0f, 0f, Vector3.forward));
            Assert.AreEqual(90f, AimDownSights.AimError(0f, 0f, Vector3.right).x, 1e-3f);
            Assert.AreEqual(-45f, AimDownSights.AimError(0f, 0f, new Vector3(0f, 1f, 1f)).y, 1e-3f); // up = negative pitch
            Assert.AreEqual(-10f, AimDownSights.AimError(10f, 0f, Vector3.forward).x, 1e-3f);
            Assert.AreEqual(20f, AimDownSights.AimError(350f, 0f, new Vector3(Mathf.Sin(10f * Mathf.Deg2Rad), 0f, Mathf.Cos(10f * Mathf.Deg2Rad))).x, 1e-3f); // wraps
        }
    }
}
