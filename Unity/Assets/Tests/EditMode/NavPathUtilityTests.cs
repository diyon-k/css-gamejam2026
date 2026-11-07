using Blink.Mobs;
using NUnit.Framework;
using UnityEngine;

namespace Blink.Tests
{
    public class NavPathUtilityTests
    {
        static readonly Vector3[] LShape =
        {
            new(0f, 0f, 0f),
            new(4f, 0f, 0f),
            new(4f, 0f, 3f),
        };

        [Test]
        public void PathLength_SumsSegments()
        {
            Assert.AreEqual(7f, NavPathUtility.PathLength(LShape), 1e-4f);
        }

        [Test]
        public void Advance_WithinFirstSegment()
        {
            AssertVector(new Vector3(2f, 0f, 0f), NavPathUtility.AdvanceAlongPath(LShape, 2f));
        }

        [Test]
        public void Advance_AcrossCorner()
        {
            AssertVector(new Vector3(4f, 0f, 1f), NavPathUtility.AdvanceAlongPath(LShape, 5f));
        }

        [Test]
        public void Advance_PastEnd_ClampsToLastCorner()
        {
            AssertVector(new Vector3(4f, 0f, 3f), NavPathUtility.AdvanceAlongPath(LShape, 100f));
        }

        [Test]
        public void Advance_ZeroOrNegative_StaysAtStart()
        {
            AssertVector(Vector3.zero, NavPathUtility.AdvanceAlongPath(LShape, 0f));
            AssertVector(Vector3.zero, NavPathUtility.AdvanceAlongPath(LShape, -1f));
        }

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), $"expected {expected}, got {actual}");
        }
    }
}
