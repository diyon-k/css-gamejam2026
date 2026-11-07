// Shared contract between the three HouseBlockout partial files. Owned by the orchestrator:
// change it only by agreement, the other files build against it.
//
//   HouseBlockout.cs              Layout: shell, floors, stairs, rooms, ladder geometry, markers, player spawn.
//   HouseBlockout.Interactables.cs Interactivity: cupboards, drawers, dust sheets, candles, ladder climbing.
//   HouseBlockout.Dressing.cs     Visuals: materials, lighting, furniture, props.
//
// Build order: layout registers Rooms + Ladders -> AddInteractables(house) -> DressRooms(house).
// Anything solid you place, Reserve() its footprint so later passes keep clear of it.
using System.Collections.Generic;
using UnityEngine;

public static partial class HouseBlockout
{
    public enum RoomKind
    {
        Bedroom, StartBedroom, Corridor, Hall, Stairwell, Kitchen, Dining, Library, Study, Bathroom,
        Ballroom, Conservatory, Storage, Servants, Attic, Basement, PuzzleRoom
    }

    public struct Room
    {
        public string name;
        public RoomKind kind;
        public int floor;        // -1 basement, 0 ground, 1, 2, 3 attic
        public Rect rect;        // x/z footprint in world metres (inside of walls)
        public float floorY;     // top of the floor slab
        public float height;     // floor to ceiling
        public Transform root;   // parent for anything placed in this room
    }

    public struct Ladder
    {
        public string name;
        public Vector3 bottom;   // floor point at the foot of the ladder
        public Vector3 top;      // floor point where you step off at the top
        public Vector3 facing;   // horizontal direction the climber faces (towards the wall/ladder)
    }

    static readonly List<Room> Rooms = new List<Room>();
    static readonly List<Ladder> Ladders = new List<Ladder>();
    static readonly List<Bounds> Reserved = new List<Bounds>();

    static Room AddRoom(string name, RoomKind kind, int floor, Rect rect, float floorY, float height, Transform parent)
    {
        var r = new Room { name = name, kind = kind, floor = floor, rect = rect, floorY = floorY, height = height, root = Group(name, parent) };
        Rooms.Add(r);
        return r;
    }

    static void Reserve(Bounds b) => Reserved.Add(b);

    static bool IsFree(Bounds b)
    {
        foreach (var r in Reserved) if (r.Intersects(b)) return false;
        return true;
    }

    static void ResetContract()
    {
        Rooms.Clear();
        Ladders.Clear();
        Reserved.Clear();
    }
}
