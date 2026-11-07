using System;
using UnityEngine;

namespace Blink.World
{
    /// <summary>
    /// Global noise bus. Anything that makes a sound zombies should react to calls <see cref="Make"/>;
    /// every <c>MobController</c> listens and decides whether it heard it.
    /// Loudness is the audible range in metres, measured along the navmesh, so walls muffle sound.
    /// </summary>
    public static class Noise
    {
        /// <summary>Args: where the noise happened, loudness (audible range in metres).</summary>
        public static event Action<Vector3, float> Made;

        public static void Make(Vector3 position, float loudness)
        {
            if (loudness > 0f) Made?.Invoke(position, loudness);
        }

        /// <param name="pathDistance">Walking distance from listener to noise.</param>
        /// <param name="hearing">Listener's ear: 1 = normal, 2 = hears twice as far.</param>
        public static bool CanHear(float loudness, float pathDistance, float hearing) =>
            pathDistance <= loudness * hearing;
    }
}
