using Blink.Mobs;
using NUnit.Framework;

namespace Blink.Tests
{
    public class RoomPickTests
    {
        [Test]
        public void PickRoom_OnlyReturnsAllowedRooms()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var rng = new System.Random(seed);
                int room = MobController.PickRoom(8, i => i != 3 && i != 5, (a, b) => rng.Next(a, b));
                Assert.That(room, Is.InRange(0, 7));
                Assert.That(room, Is.Not.EqualTo(3).And.Not.EqualTo(5));
            }
        }

        [Test]
        public void FarthestCorners_StartsOppositeTheDoor()
        {
            var room = new UnityEngine.Bounds();
            room.SetMinMax(UnityEngine.Vector3.zero, new UnityEngine.Vector3(10f, 3f, 6f));
            // door in the west wall near its south end: the far corner is north-east, the near one south-west
            var corners = MobController.FarthestCorners(room, new UnityEngine.Vector3(0f, 0f, 1f), 0.5f);
            Assert.AreEqual(new UnityEngine.Vector3(9.5f, 0f, 5.5f), corners[0]);
            Assert.AreEqual(new UnityEngine.Vector3(0.5f, 0f, 0.5f), corners[3]);
        }

        [Test]
        public void PickRoom_NoneAllowed_ReturnsMinusOne() =>
            Assert.AreEqual(-1, MobController.PickRoom(4, _ => false, (a, b) => a));
    }
}
