// Interactivity: places cupboards, drawers, dust sheets, pickups and ladder climbers into Rooms / Ladders.
// Owned by the Interactivity workstream. Runtime behaviour lives in Assets/Scripts/Interaction/.
//
// Item ids the puzzles can check with Inventory.Has(id):
//   candle, fork, knife, spoon, key_brass (StartBedroom drawer), key_iron (under a dust sheet in the attic)
using Blink.Interaction;
using UnityEngine;

public static partial class HouseBlockout
{
    static Material linen, metal, brass;

    const float DrawerH = 0.8f / 3f; // chest of drawers: 3 drawers in 0.8 m

    static void AddInteractables(Transform house)
    {
        linen = Mat("DustSheet", new Color(0.78f, 0.77f, 0.72f), 0.05f, 0f);
        metal = Mat("Cutlery", new Color(0.6f, 0.6f, 0.62f), 0.7f, 1f);
        brass = Mat("Brass", new Color(0.6f, 0.45f, 0.15f), 0.6f, 1f);

        foreach (var r in Rooms)
        {
            switch (r.kind)
            {
                case RoomKind.StartBedroom:
                    Wardrobe(r);
                    InDrawer(Drawers(r), "key_brass");
                    if (Nightstand(r) is { } stand) Put(stand, new Vector3(0, 0.55f, 0), "candle");
                    break;
                case RoomKind.Bedroom:
                    Wardrobe(r);
                    InDrawer(Drawers(r), rng.NextDouble() < 0.5 ? "candle" : "spoon");
                    if (rng.NextDouble() < 0.5 && Nightstand(r) is { } nightstand) Put(nightstand, new Vector3(0, 0.55f, 0), "candle");
                    break;
                case RoomKind.Kitchen:
                    if (Cupboard(r) is { } cup) Put(cup, new Vector3(0.2f, 0.08f, 0), "candle");
                    Cupboard(r);
                    if (Drawers(r) is { } cutlery)
                        foreach (var id in new[] { "fork", "knife", "spoon" }) Put(cutlery[2], new Vector3(Rand(-0.3f, 0.3f), -DrawerH / 2 + 0.05f, 0.05f), id);
                    break;
                case RoomKind.Dining:
                    if (DiningTable(r) is { } table)
                    {
                        Put(table, new Vector3(-0.3f, 0.81f, 0.2f), "fork");
                        Put(table, new Vector3(0.3f, 0.81f, 0.2f), "knife");
                        Put(table, new Vector3(0f, 0.81f, -0.1f), "candle");
                    }
                    Sheeted(r, out _);
                    break;
                case RoomKind.Storage:
                case RoomKind.Servants:
                case RoomKind.Basement:
                    Cupboard(r);
                    Sheeted(r, out _);
                    FloorItem(r, "candle");
                    break;
                case RoomKind.Study:
                    InDrawer(Drawers(r), "candle");
                    Sheeted(r, out _);
                    break;
                case RoomKind.Library:
                case RoomKind.Ballroom:
                case RoomKind.Conservatory:
                    Sheeted(r, out _);
                    Sheeted(r, out _);
                    break;
                case RoomKind.Attic:
                    if (Sheeted(r, out float topY) is { } hidden) Put(hidden, new Vector3(0, topY, 0), "key_iron");
                    Sheeted(r, out _);
                    Sheeted(r, out _);
                    FloorItem(r, "candle");
                    break;
                // Corridor, Hall, Stairwell, Bathroom: nothing. PuzzleRoom: left empty for the puzzle designer.
            }
        }

        var ladders = Group("Ladders", house);
        foreach (var l in Ladders) BuildLadder(ladders, l);
    }

    // ---------------------------------------------------------------- Placement
    // Finds a free footprint w (along the wall) x d (deep) x h in the room, reserves it and returns a group at its
    // floor centre whose +z faces into the room. Against a wall it also reserves `clear` metres in front
    // (door swing / drawer travel / standing room); in the open it keeps `clear` free all round. Null if no room.
    static Transform Spot(Room r, string name, float w, float d, float h, bool wall, float clear)
    {
        for (int i = 0; i < 30; i++)
        {
            var rot = Quaternion.Euler(0, rng.Next(4) * 90f, 0);
            var front = rot * Vector3.forward;
            bool sideways = Mathf.Abs(front.x) > 0.5f;
            float ex = sideways ? d : w, ez = sideways ? w : d;
            float m = wall ? 0f : clear;
            if (r.rect.width < ex + 2 * m || r.rect.height < ez + 2 * m) continue;

            var c = new Vector3(Rand(r.rect.xMin + ex / 2 + m, r.rect.xMax - ex / 2 - m), r.floorY,
                                Rand(r.rect.yMin + ez / 2 + m, r.rect.yMax - ez / 2 - m));
            if (wall) // back against the wall behind it
            {
                if (front.z > 0.5f) c.z = r.rect.yMin + d / 2;
                else if (front.z < -0.5f) c.z = r.rect.yMax - d / 2;
                else if (front.x > 0.5f) c.x = r.rect.xMin + d / 2;
                else c.x = r.rect.xMax - d / 2;
            }

            var size = new Vector3(ex + 2 * m - 0.04f, h, ez + 2 * m - 0.04f);
            var centre = c + Vector3.up * (h / 2);
            if (wall)
            {
                size += new Vector3(Mathf.Abs(front.x), 0, Mathf.Abs(front.z)) * clear;
                centre += front * (clear / 2);
            }
            var b = new Bounds(centre, size);
            if (!IsFree(b)) continue;
            Reserve(b);

            var g = Group(name, r.root);
            g.SetPositionAndRotation(c, rot);
            return g;
        }
        return null;
    }

    // ---------------------------------------------------------------- Furniture
    static Transform Wardrobe(Room r) => Cabinet(r, "Wardrobe", 1.2f, 0.6f, 2.0f);
    static Transform Cupboard(Room r) => Cabinet(r, "Cupboard", 0.9f, 0.5f, 0.9f);

    // Hollow carcass with two hinged doors on the front. Returns the carcass; its floor is at local y 0.08.
    static Transform Cabinet(Room r, string name, float w, float d, float h)
    {
        var g = Spot(r, name, w, d, h, true, 0.8f);
        if (g == null) return null;
        Box("Back", g, new Vector3(0, h / 2, -d / 2 + 0.02f), new Vector3(w, h, 0.04f), wood);
        Box("Side_L", g, new Vector3(-w / 2 + 0.02f, h / 2, 0), new Vector3(0.04f, h, d), wood);
        Box("Side_R", g, new Vector3(w / 2 - 0.02f, h / 2, 0), new Vector3(0.04f, h, d), wood);
        Box("Top", g, new Vector3(0, h - 0.02f, 0), new Vector3(w, 0.04f, d), wood);
        Box("Base", g, new Vector3(0, 0.04f, 0), new Vector3(w, 0.08f, d), wood);
        if (h > 1.2f) Box("Shelf", g, new Vector3(0, h * 0.65f, 0), new Vector3(w - 0.08f, 0.03f, d - 0.06f), wood);

        for (int side = -1; side <= 1; side += 2)
        {
            var hinge = Group(side < 0 ? "Door_L" : "Door_R", g);
            hinge.localPosition = new Vector3(side * w / 2, 0, d / 2 + 0.015f);
            Box("Panel", hinge, new Vector3(-side * w / 4, h / 2, 0), new Vector3(w / 2 - 0.01f, h - 0.02f, 0.03f), wood).isStatic = false;
            var door = hinge.gameObject.AddComponent<Openable>();
            door.noun = name.ToLowerInvariant();
            door.openAngle = side * 100f; // both swing out towards the room
        }
        return g;
    }

    // Solid body with 3 sliding drawers (index 0 = bottom). Items go in a drawer at local y -DrawerH/2 + 0.05.
    static Transform[] Drawers(Room r)
    {
        const float w = 0.9f, d = 0.5f, h = 0.9f;
        var g = Spot(r, "ChestOfDrawers", w, d, h, true, 0.7f);
        if (g == null) return null;
        Box("Body", g, new Vector3(0, h / 2, -0.015f), new Vector3(w, h, d - 0.03f), wood);
        var drawers = new Transform[3];
        for (int i = 0; i < 3; i++)
        {
            var dr = Group("Drawer_" + i, g);
            dr.localPosition = new Vector3(0, 0.08f + DrawerH * (i + 0.5f), 0);
            Box("Front", dr, new Vector3(0, 0, d / 2 - 0.015f), new Vector3(w - 0.06f, DrawerH - 0.02f, 0.03f), wood).isStatic = false;
            Box("Bottom", dr, new Vector3(0, -DrawerH / 2 + 0.04f, 0), new Vector3(w - 0.1f, 0.015f, d - 0.08f), wood, default, false).isStatic = false;
            var o = dr.gameObject.AddComponent<Openable>();
            o.motion = Openable.Motion.Sliding;
            o.noun = "drawer";
            o.slide = new Vector3(0, 0, d * 0.7f);
            drawers[i] = dr;
        }
        return drawers;
    }

    static void InDrawer(Transform[] drawers, string id)
    {
        if (drawers != null) Put(drawers[rng.Next(drawers.Length)], new Vector3(Rand(-0.25f, 0.25f), -DrawerH / 2 + 0.05f, 0.05f), id);
    }

    // Small bedside table; its top is at local y 0.55.
    static Transform Nightstand(Room r)
    {
        var g = Spot(r, "Nightstand", 0.45f, 0.4f, 0.55f, true, 0.3f);
        if (g != null) Box("Body", g, new Vector3(0, 0.275f, 0), new Vector3(0.45f, 0.55f, 0.4f), wood);
        return g;
    }

    // Free-standing table; its top is at local y 0.81.
    static Transform DiningTable(Room r)
    {
        var g = Spot(r, "DiningTable", 1.4f, 0.8f, 0.8f, false, 0.6f);
        if (g != null) Table(g, "Table", Vector3.zero, new Vector3(1.4f, 0.78f, 0.8f), Vector3.zero);
        return g;
    }

    // A greybox piece of furniture under a pull-off dust sheet. topY = its top, for hiding something on it.
    static Transform Sheeted(Room r, out float topY)
    {
        var (name, size) = rng.Next(3) switch
        {
            0 => ("Armchair", new Vector3(0.9f, 0.95f, 0.85f)),
            1 => ("Sideboard", new Vector3(1.5f, 0.9f, 0.5f)),
            _ => ("Sofa", new Vector3(1.9f, 0.85f, 0.85f)),
        };
        topY = size.y;
        var g = Spot(r, "Sheeted" + name, size.x + 0.1f, size.z + 0.1f, size.y + 0.05f, true, 0.8f);
        if (g == null) return null;
        Box(name, g, new Vector3(0, size.y / 2, 0), size, wood);
        var sheetSize = size + new Vector3(0.08f, 0.03f, 0.08f);
        var sheet = Box("DustSheet", g, new Vector3(0, sheetSize.y / 2, 0), sheetSize, linen, new Vector3(0, Rand(-3, 3), 0));
        sheet.isStatic = false;
        sheet.AddComponent<DustSheet>();
        return g;
    }

    static void FloorItem(Room r, string id)
    {
        if (Spot(r, "Floor_" + id, 0.3f, 0.3f, 0.3f, false, 0.3f) is { } g) Put(g, Vector3.zero, id);
    }

    // ---------------------------------------------------------------- Pickups
    // Places a pickup standing on `at` (local to parent). Its trigger collider is padded so small things are easy to aim at.
    static void Put(Transform parent, Vector3 at, string id)
    {
        if (parent == null) return;
        var (name, size, mat) = id switch
        {
            "candle" => ("Candle", new Vector3(0.05f, 0.2f, 0.05f), linen),
            "fork" => ("Fork", new Vector3(0.025f, 0.01f, 0.19f), metal),
            "knife" => ("Knife", new Vector3(0.02f, 0.01f, 0.22f), metal),
            "spoon" => ("Spoon", new Vector3(0.035f, 0.012f, 0.17f), metal),
            "key_brass" => ("Brass key", new Vector3(0.035f, 0.01f, 0.09f), brass),
            "key_iron" => ("Iron key", new Vector3(0.04f, 0.012f, 0.11f), rust),
            _ => (id, new Vector3(0.1f, 0.1f, 0.1f), wood),
        };
        var go = Box(name, parent, at + Vector3.up * (size.y / 2), size, mat, new Vector3(0, Rand(0, 360), 0));
        go.isStatic = false;
        var col = go.GetComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(Mathf.Max(1, 0.15f / size.x), Mathf.Max(1, 0.12f / size.y), Mathf.Max(1, 0.15f / size.z));
        var p = go.AddComponent<Pickup>();
        p.itemId = id;
        p.displayName = name;
    }

    // ---------------------------------------------------------------- Ladders
    // Vertical ladder 0.45 m in front of the foot point (towards `facing`), reaching 1 m above the top floor so
    // you can see and grab it from up there. No solid colliders: a trigger box is what the Interactor looks at.
    static void BuildLadder(Transform parent, Ladder l)
    {
        var facing = new Vector3(l.facing.x, 0, l.facing.z).normalized;
        var foot = l.bottom + facing * 0.45f;
        float h = l.top.y - l.bottom.y + 1f;

        var g = Group(l.name, parent);
        g.SetPositionAndRotation(foot, Quaternion.LookRotation(facing));
        for (int s = -1; s <= 1; s += 2)
            Box("Rail", g, new Vector3(0.22f * s, h / 2, 0), new Vector3(0.05f, h, 0.05f), wood, default, false);
        for (float y = 0.3f; y < h - 0.1f; y += 0.3f)
            Box("Rung", g, new Vector3(0, y, 0), new Vector3(0.44f, 0.035f, 0.035f), wood, default, false);

        var trigger = g.gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0, h / 2, 0);
        trigger.size = new Vector3(0.6f, h, 0.3f);
        var climb = g.gameObject.AddComponent<Blink.Interaction.Ladder>();
        climb.bottom = l.bottom;
        climb.top = l.top;
        climb.facing = facing;

        // Keep both ends clear.
        Reserve(new Bounds(l.bottom + facing * 0.25f + Vector3.up, new Vector3(1.2f, 2f, 1.2f)));
        Reserve(new Bounds(l.top + Vector3.up, new Vector3(1.2f, 2f, 1.2f)));
    }
}
