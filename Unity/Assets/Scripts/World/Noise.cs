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

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        const float EarHeight = 1f;

        /// <summary>
        /// True if the walking route from listener to noise (<paramref name="corners"/>, a navmesh path) passes through a
        /// closed door, so it isn't heard. A door never muffles its own noise (rattle, slam): its doorway is the noise.
        /// </summary>
        // ponytail: only the shortest path is checked; a longer route round the shut door is ignored (it'd be quieter anyway).
        public static bool MuffledByDoor(Vector3 noise, Vector3[] corners)
        {
            var up = Vector3.up * EarHeight;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 a = corners[i - 1] + up, d = corners[i] - corners[i - 1];
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                int n = Physics.RaycastNonAlloc(a, d / len, Hits, len, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var leaf = Hits[h].collider.GetComponentInParent<DoorLeaf>();
                    if (leaf == null || leaf.door == null || leaf.door.IsOpen) continue;
                    if ((leaf.door.Centre - noise).sqrMagnitude < 0.01f) continue; // its own noise (made at Centre)
                    return true;
                }
            }
            return false;
        }
    }
}
