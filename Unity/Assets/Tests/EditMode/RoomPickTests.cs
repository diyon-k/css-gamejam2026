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
        public void PickRoom_NoneAllowed_ReturnsMinusOne() =>
            Assert.AreEqual(-1, MobController.PickRoom(4, _ => false, (a, b) => a));
    }
}
