using System.Collections.Generic;
using UnityEngine;

namespace Blink.Puzzles
{
    /// <summary>
    /// The puzzle rooms' bounds. Clowns don't follow you into one: a player inside counts as hidden, so a chase
    /// gives up and goes back to patrol (<see cref="Mobs.MobController"/>).
    /// </summary>
    public static class PuzzleRooms
    {
        static readonly List<BoxCollider> Rooms = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Rooms.Clear();

        public static void Add(BoxCollider room)
        {
            if (room != null && !Rooms.Contains(room)) Rooms.Add(room);
        }

        public static void Remove(BoxCollider room) => Rooms.Remove(room);

        /// <summary>Takes a feet position; checks half a metre up, like the rooms themselves do.</summary>
        public static bool Contains(Vector3 feet)
        {
            foreach (var room in Rooms)
                if (room != null && room.bounds.Contains(feet + Vector3.up * 0.5f)) return true;
            return false;
        }
    }
}
