// Overgrowth & exterior: surface textures, plants, roof holes, decay, the forest and grounds outside.
// Owned by the Overgrowth workstream. Runs after DressRooms, before the navmesh bake.
// The work lives in Assets/Art/Overgrowth/Editor (Blink.Overgrowth.EditorTools.OvergrowthBuilder);
// this partial only hands it the contract data.
using System.Collections.Generic;
using Blink.Overgrowth.EditorTools;
using UnityEngine;

public static partial class HouseBlockout
{
    static void AddDecay(Transform house)
    {
        var rooms = new List<OgRoom>(Rooms.Count);
        foreach (var r in Rooms)
            rooms.Add(new OgRoom
            {
                name = r.name, kind = r.kind.ToString(), floor = r.floor,
                rect = r.rect, floorY = r.floorY, height = r.height, root = r.root,
            });
        OvergrowthBuilder.Build(house, rooms, IsFree, Reserve, rng);
    }
}
