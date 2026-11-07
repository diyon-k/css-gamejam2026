using System.Collections.Generic;
using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>Geometry helpers for moving along a navmesh path's corners. Pure logic, unit tested.</summary>
    public static class NavPathUtility
    {
        public static float PathLength(IReadOnlyList<Vector3> corners)
        {
            float length = 0f;
            for (int i = 1; i < corners.Count; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        /// <summary>
        /// The point <paramref name="distance"/> metres along the path from its first corner.
        /// Clamps to the last corner if the path is shorter.
        /// </summary>
        public static Vector3 AdvanceAlongPath(IReadOnlyList<Vector3> corners, float distance)
        {
            if (corners == null || corners.Count == 0) return Vector3.zero;
            if (distance <= 0f) return corners[0];

            for (int i = 1; i < corners.Count; i++)
            {
                float segment = Vector3.Distance(corners[i - 1], corners[i]);
                if (distance <= segment)
                    return segment <= 0f ? corners[i] : Vector3.Lerp(corners[i - 1], corners[i], distance / segment);
                distance -= segment;
            }
            return corners[corners.Count - 1];
        }
    }
}
