using Blink.Detection;
using NUnit.Framework;

namespace Blink.Tests
{
    public class BlinkPacketTests
    {
        [Test]
        public void ParsesSidecarMessage()
        {
            Assert.IsTrue(BlinkPacket.TryParse("{\"t\": 1731000000123, \"present\": true, \"closed\": true}", out var packet));
            Assert.IsTrue(packet.present);
            Assert.IsTrue(packet.closed);
            Assert.AreEqual(1731000000123d, packet.t);
        }

        [Test]
        public void MissingFields_DefaultToFalse()
        {
            Assert.IsTrue(BlinkPacket.TryParse("{\"closed\": true}", out var packet));
            Assert.IsFalse(packet.present);
            Assert.IsTrue(packet.closed);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase(null)]
        public void RejectsGarbage(string input)
        {
            Assert.IsFalse(BlinkPacket.TryParse(input, out _));
        }

        [Test]
        public void ConvertsToEyeState()
        {
            var state = new BlinkPacket { present = true, closed = false }.ToEyeState();
            Assert.IsTrue(state.Present);
            Assert.IsFalse(state.Closed);
        }
    }
}
