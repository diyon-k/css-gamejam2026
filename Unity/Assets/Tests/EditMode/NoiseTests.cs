using Blink.World;
using NUnit.Framework;

namespace Blink.Tests
{
    public class NoiseTests
    {
        [Test]
        public void CanHear_InsideOrAtRange() => Assert.IsTrue(Noise.CanHear(10f, 10f, 1f));

        [Test]
        public void CanHear_FalseBeyondRange() => Assert.IsFalse(Noise.CanHear(10f, 10.5f, 1f));

        [Test]
        public void CanHear_ScalesWithHearing()
        {
            Assert.IsTrue(Noise.CanHear(10f, 15f, 2f));
            Assert.IsFalse(Noise.CanHear(10f, 15f, 1f));
        }
    }
}
