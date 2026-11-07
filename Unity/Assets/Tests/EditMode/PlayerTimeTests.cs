using Blink.Player;
using Blink.TimeSkip;
using NUnit.Framework;

namespace Blink.Tests
{
    public class PlayerTimeTests
    {
        [Test]
        public void NormalSpeed_PassesDeltaThrough()
        {
            Assert.AreEqual(0.016f, PlayerController.RealDeltaTime(0.016f, 1f), 1e-6f);
        }

        [Test]
        public void FastForward_PlayerStillMovesAtRealTimeSpeed()
        {
            // Eyes shut: timeScale 3 makes a 16 ms frame report 48 ms of world time.
            Assert.AreEqual(0.016f, PlayerController.RealDeltaTime(0.048f, 3f), 1e-6f);
        }

        [Test]
        public void Paused_PlayerDoesNotMove()
        {
            Assert.AreEqual(0f, PlayerController.RealDeltaTime(0f, 0f));
        }

        [Test]
        public void Player_IsNotSkippable_SoBlinkSkipsDoNotMoveIt()
        {
            Assert.IsFalse(typeof(ISkippable).IsAssignableFrom(typeof(PlayerController)));
        }
    }
}
