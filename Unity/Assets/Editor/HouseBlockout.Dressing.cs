// Visuals: materials, lighting, furniture and props. Owned by the Visuals workstream.
// Runs after layout and interactables. Every solid prop checks IsFree() + existing colliders before it is
// placed and Reserve()s its footprint afterwards. Furniture = CC0 Poly Haven models in Assets/Art/Mansion/PolyHaven
// (Blender hero models in Assets/Art/Mansion/Blender win when present); fireplaces, lamps, sconces, piano, tub,
// stove, rugs and books are built from primitives here.
// Convention: every model/builder faces +Z (its front), so yaw DYaw(side) turns it to face into the room.
using System.IO;
using System.Linq;
using Blink.Visuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class HouseBlockout
{
    const string PackDir = "Assets/Art/Mansion/PolyHaven";
    const string HeroDir = "Assets/Art/Mansion/Blender";
    const string PostProfilePath = "Assets/Art/Mansion/MansionPost.asset";

    // Night baseline; the DayNightCycle reads these at startup. Tune here.
    static readonly Color NightAmbient = new Color(0.16f, 0.16f, 0.2f);
    static readonly Color FogColor = new Color(0.05f, 0.055f, 0.07f);
    const float FogDensity = 0.015f;

    static readonly Color Warm = new Color(1f, 0.72f, 0.45f);
    static readonly Color CandleCol = new Color(1f, 0.6f, 0.3f);
    static readonly Color FireCol = new Color(1f, 0.45f, 0.15f);
    static readonly Color Cold = new Color(0.55f, 0.65f, 0.9f);

    enum DSide { S, N, W, E }

    static Material mBrass, mStone, mSoot, mIron, mPorcelain, mFlame, mShade, mEmber, mBulb, mWax, mLacquer, mGrime,
                    mGlassDark, mGlassLit, mRugBorder;
    static Material[] mRugs, mBooks, mCanvases;
    static int shadowBudget;

    // ================================================================ Entry
    static void DressRooms(Transform house)
    {
        shadowBudget = 8; // shadow-casting point lights are expensive (6 atlas slices each); only key lights get one
        foreach (var r in Rooms) DressRoom(r);
        BuildLighting(Group("Lighting", house));
    }

    static void DressRoom(Room r)
    {
        switch (r.kind)
        {
            case RoomKind.Bedroom:
            case RoomKind.StartBedroom: DressBedroom(r); break;
            case RoomKind.Corridor: DressCorridor(r); break;
            case RoomKind.Hall: DressHall(r); break;
            case RoomKind.Stairwell: DressStairwell(r); break;
            case RoomKind.Kitchen: DressKitchen(r); break;
            case RoomKind.Dining: DressDining(r); break;
            case RoomKind.Library: DressLibrary(r); break;
            case RoomKind.Study: DressStudy(r); break;
            case RoomKind.Bathroom: DressBathroom(r); break;
            case RoomKind.Ballroom: DressBallroom(r); break;
            case RoomKind.Conservatory: DressConservatory(r); break;
            case RoomKind.Storage: DressStorage(r); break;
            case RoomKind.Servants: DressServants(r); break;
            case RoomKind.Attic: DressAttic(r); break;
            case RoomKind.Basement: DressBasement(r); break;
            case RoomKind.PuzzleRoom: DLight(r.root, DCeiling(r, 0.3f), new Color(0.9f, 0.85f, 0.75f), 2.2f, DSpan(r) * 0.9f); break;
        }
    }

    // ================================================================ Rooms
    static void DressBedroom(Room r)
    {
        bool start = r.kind == RoomKind.StartBedroom;
        var wall = DLongSide(r, rng.NextDouble() < 0.5);
        var bed = DWall(r, P("GothicBed_01", 1f, "Bed"), wall, 0.5f) ?? DWall(r, P("GothicBed_01", 1f, "Bed"), DOpp(wall), 0.5f);
        if (bed != null)
        {
            var bedWall = DSideOf(r, bed);
            for (int dir = -1; dir <= 1; dir += 2)
            {
                var ns = DNextTo(r, bed, P("ClassicNightstand_01"), bedWall, dir);
                if (ns != null && dir == 1) DTableLamp(ns, start && dir == 1);
            }
            var bb = DBounds(bed);
            var foot = bb.center + DIn(bedWall) * (DDepth(bb, bedWall) / 2 + 0.7f);
            DRug(r.root, new Vector3(foot.x, r.floorY, foot.z), DAlong(bedWall) ? new Vector2(2.2f, 1.4f) : new Vector2(1.4f, 2.2f));
        }
        var other = DOpp(wall);
        var commode = DWall(r, P("GothicCommode_01"), other, 0.3f);
        if (commode != null) DOnTop(commode, P("vintage_oil_lamp"), 0.25f, l => DGlow(l, 1.3f, 4.5f, 0.08f, start));
        DHang(r, P("ornate_mirror_01", 1.2f), other, 0.3f, 1.75f);
        DWall(r, P("ArmChair_01"), DSideTurn(wall), 0.8f);
        if (r.height > 2.5f) DWall(r, P("GothicCabinet_01", 0.95f), DSideTurn(other), 0.3f);
        DPaintings(r, 1);
        Scatter(r.root, 6, r.floorY, DInset(r, 0.4f));
    }

    static void DressCorridor(Room r)
    {
        bool alongX = r.rect.width >= r.rect.height;
        float len = alongX ? r.rect.width : r.rect.height, width = alongX ? r.rect.height : r.rect.width;
        DRug(r.root, new Vector3(r.rect.center.x, r.floorY, r.rect.center.y),
             alongX ? new Vector2(len - 0.8f, Mathf.Min(1.1f, width - 0.6f)) : new Vector2(Mathf.Min(1.1f, width - 0.6f), len - 0.8f));
        DSide a = alongX ? DSide.S : DSide.W, b = alongX ? DSide.N : DSide.E;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 4f));
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            var s = i % 2 == 0 ? a : b;
            DSconce(r, s, t);
            DHangPainting(r, i % 2 == 0 ? b : a, t, Rand(1.2f, 1.7f));
            if (width >= 2.2f && i % 2 == 1)
            {
                var st = DWall(r, P("side_table_01"), s, Mathf.Clamp01(t + 0.08f));
                if (st != null) DTableLamp(st, false);
            }
        }
        if (width >= 2.4f && len > 8f) DWall(r, P("vintage_grandfather_clock_01", 1f, "Clock"), b, 0.15f);
        Scatter(r.root, Mathf.RoundToInt(len), r.floorY, DInset(r, 0.3f));
    }

    static void DressHall(Room r)
    {
        var c = new Vector3(r.rect.center.x, r.floorY, r.rect.center.y);
        DRug(r.root, c, new Vector2(r.rect.width * 0.55f, r.rect.height * 0.55f));
        DChandeliers(r, 1.3f, true);
        var walls = DSides(r);
        DWall(r, P("vintage_grandfather_clock_01", 1f, "Clock"), walls[0], 0.5f);
        foreach (var s in walls)
        {
            var st = DWall(r, P("side_table_01"), s, 0.2f);
            if (st != null) DTableLamp(st, false);
            DHangPainting(r, s, 0.5f, 2.2f);
            DSconce(r, s, 0.85f);
        }
        var cab = DWall(r, P("GothicCommode_01"), walls[1], 0.75f);
        if (cab != null) DOnTop(cab, P("marble_bust_01", 1.2f), 0f, null);
        var sofa = DWall(r, P("Sofa_01"), walls[2], 0.5f);
        if (sofa != null) { DNextTo(r, sofa, P("ArmChair_01"), DSideOf(r, sofa), -1); DNextTo(r, sofa, P("ArmChair_01"), DSideOf(r, sofa), 1); }
        if (DSpan(r) > 9f) DWall(r, P("WoodenChair_01"), walls[3], 0.5f); // gothic throne chair
        DFireplace(r, walls[3], 0.25f, false);
    }

    static void DressStairwell(Room r)
    {
        foreach (var s in DSides(r))
        {
            DHangPainting(r, s, 0.5f, 1.8f);
            DSconce(r, s, 0.2f);
        }
        DWall(r, P("vintage_grandfather_clock_01", 1f, "Clock"), DSides(r)[0], 0.85f);
        DLight(r.root, DCeiling(r, 0.5f), Warm, 1.2f, DSpan(r) * 0.8f);
    }

    static void DressKitchen(Room r)
    {
        var w = DLongSide(r, true);
        for (int i = 0; i < 3; i++) DWall(r, DCounter, w, 0.15f + i * 0.25f, retry: false, pad: 0.03f);
        var stove = DWall(r, DStove, DOpp(w), 0.5f);
        if (stove != null) DLight(stove, stove.Find("Firebox").position + DIn(DSideOf(r, stove)) * 0.3f, FireCol, 1.2f, 3.5f, false, 0.3f);
        var shelf = DWall(r, P("wooden_bookshelf_worn"), DSideTurn(w), 0.5f);
        if (shelf != null) DFillShelf(shelf, true);
        var table = DCentre(r, P("WoodenTable_01", 1f), 0f, 1.0f, new Vector3(1f, 1.4f, 1f));
        if (table != null)
        {
            DChairsAround(r, table, 0.55f);
            DOnTop(table, P("wooden_candlestick"), 0.3f, l => DCandleOn(l, 0.9f, 3.5f));
        }
        DWall(r, P("wine_barrel_01"), DSideTurn(DOpp(w)), 0.2f);
        DLight(r.root, DCeiling(r, 0.6f), Warm, 1.6f, DSpan(r) * 0.8f, false, 0.25f, true); // dying bulb
        DHanging(r, DCeiling(r, 0.6f));
        Scatter(r.root, 10, r.floorY, DInset(r, 0.4f));
    }

    static void DressDining(Room r)
    {
        bool alongX = r.rect.width >= r.rect.height;
        float len = alongX ? r.rect.width : r.rect.height;
        int tables = len > 7.5f ? 2 : 1;
        DRug(r.root, new Vector3(r.rect.center.x, r.floorY, r.rect.center.y),
             alongX ? new Vector2(Mathf.Min(len - 2f, 2.4f * tables + 1.2f), 2.8f) : new Vector2(2.8f, Mathf.Min(len - 2f, 2.4f * tables + 1.2f)));
        for (int i = 0; i < tables; i++)
        {
            float off = tables == 1 ? 0 : (i == 0 ? -1.15f : 1.15f);
            var p = new Vector3(r.rect.center.x + (alongX ? off : 0), r.floorY, r.rect.center.y + (alongX ? 0 : off));
            var t = DAt(r.root, P("dining_table"), p, Quaternion.Euler(0, alongX ? 0 : 90, 0), pad: 0.05f);
            if (t == null) continue;
            DChairsAround(r, t, 0.62f);
            var hero = DModel(null, "Candelabra");
            if (hero != null) DOnTop(t, P(null, 1f, "Candelabra"), 0f, l => DCandleOn(l, 1.4f, 5f));
            else
                for (int k = -1; k <= 1; k++) DOnTop(t, P("wooden_candlestick"), k * 0.6f, l => DCandleOn(l, k == 0 ? 1.2f : 0f, 5f));
        }
        DChandeliers(r, 1.1f, true);
        var walls = DSides(r);
        DWall(r, P("GothicCabinet_01", 1f), walls[2], 0.5f);
        DWall(r, P("GothicCommode_01"), walls[3], 0.5f);
        DPaintings(r, 3);
        foreach (var s in walls) DSconce(r, s, 0.15f);
    }

    static void DressLibrary(Room r)
    {
        var walls = DSides(r);
        DFireplace(r, walls[0], 0.5f, true);
        DCornerReading(r);
        foreach (var s in walls) DShelfRow(r, s);
        var c = new Vector3(r.rect.center.x, r.floorY, r.rect.center.y);
        DRug(r.root, c, new Vector2(Mathf.Min(3.2f, r.rect.width - 2.5f), Mathf.Min(2.4f, r.rect.height - 2.5f)));
        var desk = DCentre(r, DDesk, 0f, 1.2f);
        if (desk != null)
        {
            DOnTop(desk, P("vintage_oil_lamp"), 0.4f, l => DGlow(l, 1.4f, 5f, 0.08f, false));
            DOnTop(desk, P("marble_bust_01"), -0.45f, null);
            DAt(r.root, P("dining_chair_02"), DBounds(desk).center - desk.forward * 0.9f, desk.rotation, pad: 0.02f);
        }
        DChandeliers(r, 1f, false);
    }

    static void DressStudy(Room r)
    {
        var walls = DSides(r);
        var desk = DWall(r, DDesk, walls[0], 0.5f);
        if (desk != null)
        {
            DOnTop(desk, P("vintage_oil_lamp"), 0.45f, l => DGlow(l, 1.3f, 4.5f, 0.08f, true));
            DAt(r.root, P("dining_chair_02"), DBounds(desk).center + DIn(walls[0]) * 0.75f, Quaternion.Euler(0, DYaw(walls[0]) + 180, 0), pad: 0.02f);
        }
        DShelfRow(r, walls[1]);
        DCornerReading(r);
        DPaintings(r, 1);
        Scatter(r.root, 6, r.floorY, DInset(r, 0.4f));
    }

    static void DressBathroom(Room r)
    {
        var w = DLongSide(r, true);
        DWall(r, DBathtub, w, 0.5f);
        var sink = DWall(r, DSink, DOpp(w), 0.5f);
        if (sink != null)
        {
            var s = DSideOf(r, sink);
            DHang(r, P("ornate_mirror_01", 1.1f), s, DAlongT(r, s, DBounds(sink).center), 1.65f);
            DOnTop(sink, P("wooden_candlestick"), 0.18f, l => DCandleOn(l, 0.9f, 3.5f));
        }
        DSconce(r, DSideTurn(w), 0.5f);
    }

    static void DressBallroom(Room r)
    {
        var walls = DSides(r);
        DChandeliers(r, 1.5f, true);
        DFireplace(r, walls[0], 0.5f, true);
        DWall(r, DPiano, walls[1], 0.3f);
        DWall(r, P("Sofa_01"), walls[2], 0.3f);
        DWall(r, P("Sofa_01"), walls[3], 0.7f);
        DWall(r, P("ArmChair_01"), walls[2], 0.75f);
        DWall(r, P("WoodenChair_01"), walls[1], 0.8f);
        foreach (var s in walls) { DHangPainting(r, s, 0.25f, 2.2f); DSconce(r, s, 0.5f); DHangPainting(r, s, 0.75f, 1.6f); }
        // derelict: a couple of chairs knocked over mid-floor
        for (int i = 0; i < 2; i++)
            DAt(r.root, P("dining_chair_02"), DRandIn(r, 1.5f), Quaternion.Euler(Rand(70, 90), Rand(0, 360), 0), pad: 0.4f);
        Scatter(r.root, 12, r.floorY, DInset(r, 0.5f));
    }

    static void DressConservatory(Room r)
    {
        var table = DCentre(r, P("CoffeeTable_01"), 0f, 1.2f);
        if (table != null)
        {
            DChairsAround(r, table, 0.7f, "ArmChair_01");
            DOnTop(table, P("wooden_lantern_01"), 0f, l => DGlow(l, 1.4f, 5f, 0.12f, false));
        }
        var walls = DSides(r);
        DWall(r, P("Sofa_01"), walls[0], 0.5f);
        var st = DWall(r, P("side_table_01"), walls[1], 0.3f);
        if (st != null) DOnTop(st, P("marble_bust_01"), 0f, null);
        // cold moon spill through the glass
        DLight(r.root, DCeiling(r, 0.4f), Cold, 1.6f, DSpan(r));
    }

    static void DressStorage(Room r)
    {
        var walls = DSides(r);
        DShelfRow(r, walls[0], "steel_frame_shelves_01", false);
        DCrates(r, walls[1], 3);
        DCrates(r, walls[2], 2);
        DWall(r, P("vintage_suitcase"), walls[3], 0.3f);
        var lantern = DAt(r.root, P("wooden_lantern_01"), DRandIn(r, 1f), Quaternion.Euler(0, Rand(0, 360), 0), solid: false);
        if (lantern != null) DGlow(lantern, 1.0f, 4f, 0.15f, false);
        Scatter(r.root, 8, r.floorY, DInset(r, 0.4f));
    }

    static void DressServants(Room r)
    {
        var w = DLongSide(r, true);
        DWall(r, DCot, w, 0.25f);
        DWall(r, DCot, w, 0.75f);
        var ns = DWall(r, P("ClassicNightstand_01"), DOpp(w), 0.5f);
        if (ns != null) DOnTop(ns, P("wooden_candlestick"), 0f, l => DCandleOn(l, 1.0f, 4f));
        DWall(r, P("vintage_suitcase"), DSideTurn(w), 0.5f);
        DWall(r, P("dining_chair_02"), DOpp(w), 0.15f);
        Scatter(r.root, 6, r.floorY, DInset(r, 0.4f));
    }

    static void DressAttic(Room r)
    {
        var walls = DSides(r);
        DCrates(r, walls[0], 3);
        DWall(r, P("vintage_suitcase"), walls[1], 0.3f);
        DWall(r, P("GothicCommode_01"), walls[1], 0.75f);
        DCrates(r, walls[2], 2);
        DWall(r, P("wine_barrel_01"), walls[3], 0.2f);
        // the rocking chair in the middle of the room, facing the door
        DCentre(r, P("Rockingchair_01"), Rand(0, 360), 0.8f);
        DAt(r.root, P("ArmChair_01"), DRandIn(r, 1.2f), Quaternion.Euler(0, Rand(0, 360), 80), pad: 0.4f);
        var lantern = DAt(r.root, P("wooden_lantern_01"), DRandIn(r, 1f), Quaternion.Euler(0, Rand(0, 360), 0), solid: false);
        if (lantern != null) DGlow(lantern, 0.8f, 3.5f, 0.2f, false);
        DLight(r.root, DCeiling(r, 0.4f), Cold, 0.7f, DSpan(r) * 0.9f); // moonlight through the roof gaps
        Scatter(r.root, 16, r.floorY, DInset(r, 0.3f));
    }

    static void DressBasement(Room r)
    {
        var walls = DSides(r);
        DShelfRow(r, walls[0], "steel_frame_shelves_01", false);
        for (int i = 0; i < 4; i++) DWall(r, P("wine_barrel_01"), walls[1], 0.1f + i * 0.27f, retry: false, pad: 0.03f);
        DCrates(r, walls[2], 3);
        DCrates(r, walls[3], 2);
        float cy = r.floorY + r.height - 0.35f;
        bool alongX = r.rect.width >= r.rect.height;
        var a = alongX ? new Vector3(r.rect.xMin + 0.2f, cy, r.rect.yMax - 0.35f) : new Vector3(r.rect.xMax - 0.35f, cy, r.rect.yMin + 0.2f);
        var b = alongX ? new Vector3(r.rect.xMax - 0.2f, cy, r.rect.yMax - 0.35f) : new Vector3(r.rect.xMax - 0.35f, cy, r.rect.yMax - 0.2f);
        Pipe("Pipe_Run", r.root, a, b, 0.06f);
        Pipe("Pipe_Run2", r.root, a + Vector3.down * 0.15f + (alongX ? Vector3.back : Vector3.left) * 0.15f,
             b + Vector3.down * 0.15f + (alongX ? Vector3.back : Vector3.left) * 0.15f, 0.04f);
        DHanging(r, DCeiling(r, 0.6f));
        DLight(r.root, DCeiling(r, 0.65f), new Color(1f, 0.8f, 0.55f), 1.4f, DSpan(r) * 0.7f, false, 0.3f, true);
        var lantern = DAt(r.root, P("wooden_lantern_01"), DRandIn(r, 1f), Quaternion.identity, solid: false);
        if (lantern != null) DGlow(lantern, 0.7f, 3f, 0.2f, false);
        Scatter(r.root, 14, r.floorY, DInset(r, 0.3f));
    }

    // ================================================================ Room building blocks
    static void DChandeliers(Room r, float scale, bool keyShadow)
    {
        bool alongX = r.rect.width >= r.rect.height;
        float len = alongX ? r.rect.width : r.rect.height;
        int n = Mathf.Clamp(Mathf.RoundToInt(len / 8f), 1, 3);
        for (int i = 0; i < n; i++)
        {
            float f = (i + 0.5f) / n;
            var xz = alongX ? new Vector2(Mathf.Lerp(r.rect.xMin, r.rect.xMax, f), r.rect.center.y)
                            : new Vector2(r.rect.center.x, Mathf.Lerp(r.rect.yMin, r.rect.yMax, f));
            var h = P("Chandelier_02", scale, "Chandelier")(r.root);
            if (h == null) return;
            float ceil = r.floorY + r.height;
            var b = DBounds(h);
            float drop = Mathf.Clamp(r.height - 2.3f - b.size.y, 0.1f, 1.2f); // keep the bottom above head height
            h.position += new Vector3(xz.x - b.center.x, ceil - drop - b.max.y, xz.y - b.center.y);
            DStatic(h);
            if (drop > 0.12f) DCyl("Chain", h, h.InverseTransformPoint(new Vector3(xz.x, ceil - drop / 2, xz.y)), 0.012f, drop, mIron);
            b = DBounds(h);
            DLight(h, new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.center.z), Warm, 3.2f * scale, Mathf.Clamp(DSpan(r) * 0.75f, 6f, 14f),
                   keyShadow && i == 0, 0.05f);
        }
    }

    static void DFireplace(Room r, DSide s, float t, bool keyShadow)
    {
        var h = DWall(r, DModel(null, "Fireplace") != null ? P(null, 1f, "Fireplace") : DFireplaceBuild, s, t);
        if (h == null) return;
        var b = DBounds(h);
        var embers = h.Find("Embers");
        var p = embers != null ? embers.position : new Vector3(b.center.x, b.min.y + 0.35f, b.center.z);
        DLight(h, p + DIn(s) * 0.45f + Vector3.up * 0.3f, FireCol, 3f, 7f, keyShadow, 0.3f);
        if (embers == null) return; // hero model brings its own mantel decor
        foreach (var x in new[] { -0.75f, 0.75f })
        {
            var cs = P("wooden_candlestick")(h);
            if (cs == null) break;
            cs.rotation = h.rotation;
            DMoveTo(cs, h.Find("Mantel").position + h.right * x + Vector3.up * 0.03f);
            DStatic(cs);
            DCandleOn(cs, 0f, 0f);
        }
        DRug(r.root, b.center + DIn(s) * (DDepth(b, s) / 2 + 0.9f) - Vector3.up * (b.center.y - r.floorY), DAlong(s) ? new Vector2(2.2f, 1.2f) : new Vector2(1.2f, 2.2f));
    }

    static void DCornerReading(Room r)
    {
        var walls = DSides(r);
        var chair = DWall(r, P("ArmChair_01"), walls[1], 0.12f);
        if (chair == null) return;
        var st = DNextTo(r, chair, P("side_table_01"), DSideOf(r, chair), 1);
        if (st != null) DTableLamp(st, false);
    }

    static void DShelfRow(Room r, DSide s, string model = "wooden_bookshelf_worn", bool books = true)
    {
        float width = model == "wooden_bookshelf_worn" ? 1.37f : 1.1f;
        float len = DAlong(s) ? r.rect.width : r.rect.height;
        int n = Mathf.Max(1, Mathf.FloorToInt((len - 0.4f) / (width + 0.06f)));
        for (int i = 0; i < n; i++)
        {
            var h = DWall(r, P(model), s, n == 1 ? 0.5f : (float)i / (n - 1), retry: false, pad: 0.03f);
            if (h != null && books) DFillShelf(h, false);
        }
    }

    static void DCrates(Room r, DSide s, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var c = DWall(r, P("wooden_crate_01"), s, (i + 0.5f) / n + Rand(-0.05f, 0.05f), retry: false, pad: 0.05f);
            if (c != null && rng.NextDouble() < 0.6) DOnTop(c, P("wooden_crate_01"), Rand(-0.1f, 0.1f), null, Rand(-20, 20), true);
        }
    }

    // Dining-style chairs around a table, all facing it; one is knocked over.
    static void DChairsAround(Room r, Transform table, float spacing, string model = "dining_chair_02")
    {
        var b = DBounds(table);
        bool alongX = b.size.x >= b.size.z;
        float len = alongX ? b.size.x : b.size.z, wid = alongX ? b.size.z : b.size.x;
        int per = Mathf.Max(1, Mathf.FloorToInt((len - 0.2f) / spacing));
        Vector3 axis = alongX ? Vector3.right : Vector3.forward, side = alongX ? Vector3.forward : Vector3.right;
        int k = 0;
        for (int sgn = -1; sgn <= 1; sgn += 2)
            for (int i = 0; i < per; i++)
            {
                float a = (i + 0.5f) / per * len - len / 2;
                var pos = b.center + axis * a + side * sgn * (wid / 2 + 0.38f);
                pos.y = r.floorY;
                bool tipped = k++ == 2;
                var rot = Quaternion.LookRotation(-side * sgn) * (tipped ? Quaternion.Euler(-85, Rand(-30, 30), 0) : Quaternion.identity);
                DAt(r.root, P(model), pos + (tipped ? side * sgn * 0.5f : Vector3.zero), rot, pad: 0.02f);
            }
        for (int sgn = -1; sgn <= 1; sgn += 2) // heads of the table
        {
            var pos = b.center + axis * sgn * (len / 2 + 0.38f);
            pos.y = r.floorY;
            DAt(r.root, P(model), pos, Quaternion.LookRotation(-axis * sgn), pad: 0.02f);
        }
    }

    static void DPaintings(Room r, int n)
    {
        var walls = DSides(r);
        for (int i = 0; i < n; i++) DHangPainting(r, walls[(i + 1) % 4], Rand(0.25f, 0.75f), Rand(1.4f, 2.0f));
    }

    static Transform DHangPainting(Room r, DSide s, float t, float scale, float y = 1.7f)
    {
        var h = DHang(r, P("fancy_picture_frame_01", scale), s, t, y);
        if (h == null) return null;
        var canvas = mCanvases[rng.Next(mCanvases.Length)];
        foreach (var rend in h.GetComponentsInChildren<Renderer>())
            if (rend.sharedMaterial != null && rend.sharedMaterial.name.Contains("Canvas")) rend.sharedMaterial = canvas;
        return h;
    }

    static void DSconce(Room r, DSide s, float t)
    {
        var h = DHang(r, DSconceBuild, s, t, 1.85f);
        if (h != null) DFlameLight(h, CandleCol, 1.1f, 4.5f, 0.12f);
    }

    static void DTableLamp(Transform table, bool keyShadow)
    {
        var lamp = DOnTop(table, DLampBuild, 0f, null);
        if (lamp == null) return;
        DLight(lamp, lamp.Find("Shade").position, Warm, 1.6f, 5f, keyShadow, 0.03f);
    }

    // Bare bulb hanging from a flex at `p` (decorative only; add the light separately).
    static void DHanging(Room r, Vector3 p)
    {
        float ceil = r.floorY + r.height;
        var g = Group("HangingBulb", r.root);
        DCyl("Flex", g, g.InverseTransformPoint(new Vector3(p.x, (ceil + p.y) / 2 + 0.05f, p.z)), 0.006f, ceil - p.y - 0.1f, mIron);
        DSphere("Bulb", g, g.InverseTransformPoint(p), new Vector3(0.07f, 0.09f, 0.07f), mBulb);
        DStatic(g);
    }

    // Puts a model's own flame/lit glass to work: a light at its upper middle.
    static void DGlow(Transform h, float intensity, float range, float flicker, bool keyShadow)
    {
        var b = DBounds(h);
        DLight(h, new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z), CandleCol, intensity, range, keyShadow, flicker);
    }

    // Adds a flame on top of a candlestick model and optionally a light (intensity 0 = flame only).
    static void DCandleOn(Transform holder, float intensity, float range)
    {
        var b = DBounds(holder);
        if (DModel(null, "Candelabra") == null || !holder.name.Contains("andelabra"))
            DSphere("Flame", holder, holder.InverseTransformPoint(new Vector3(b.center.x, b.max.y + 0.025f, b.center.z)), new Vector3(0.022f, 0.05f, 0.022f), mFlame);
        if (intensity > 0) DFlameLight(holder, CandleCol, intensity, range, 0.15f);
    }

    static void DFlameLight(Transform h, Color c, float intensity, float range, float flicker)
    {
        var flames = h.GetComponentsInChildren<Transform>().Where(t => t.name == "Flame").ToList();
        var p = flames.Count > 0 ? flames.Aggregate(Vector3.zero, (s, t) => s + t.position) / flames.Count : DBounds(h).center;
        DLight(h, p + Vector3.up * 0.08f, c, intensity, range, false, flicker);
    }

    // Leather-bound books along every shelf board of a bookcase (finds the boards with raycasts), or jars.
    static void DFillShelf(Transform h, bool jars)
    {
        var mf = h.GetComponentInChildren<MeshFilter>();
        if (mf == null) return;
        var mc = mf.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;
        Physics.SyncTransforms();
        var b = DBounds(h);
        float width = Mathf.Abs(Vector3.Dot(b.size, h.right)) * 0.82f;
        var g = Group(jars ? "Jars" : "Books", h);
        float y = b.max.y - 0.03f;
        for (int guard = 0; guard < 10 && y > b.min.y + 0.1f; guard++)
        {
            var o = new Vector3(b.center.x, y, b.center.z);
            if (!mc.Raycast(new Ray(o, Vector3.down), out var hit, y - b.min.y + 0.01f)) break;
            float head = mc.Raycast(new Ray(hit.point + Vector3.up * 0.01f, Vector3.up), out var up, 1f) ? up.distance : 0.45f;
            if (head > 0.17f) DBookRow(g, hit.point, h, width, Mathf.Min(head - 0.03f, 0.34f), jars);
            y = hit.point.y - 0.04f;
        }
        Object.DestroyImmediate(mc);
        DStatic(g);
    }

    static void DBookRow(Transform g, Vector3 shelf, Transform h, float width, float maxH, bool jars)
    {
        float x = -width / 2;
        while (x < width / 2 - 0.05f)
        {
            if (rng.NextDouble() < 0.08) { x += Rand(0.08f, 0.2f); continue; } // gap
            if (jars)
            {
                float jr = Rand(0.04f, 0.07f), jh = Mathf.Min(maxH, Rand(0.12f, 0.25f));
                DCyl("Jar", g, g.InverseTransformPoint(shelf + h.right * (x + jr) + Vector3.up * jh / 2), jr, jh, rng.NextDouble() < 0.5 ? mGlassDark : mPorcelain);
                x += jr * 2 + 0.03f;
                continue;
            }
            float th = Rand(0.025f, 0.06f), bh = maxH * Rand(0.62f, 0.95f), bd = Rand(0.15f, 0.21f);
            bool lean = rng.NextDouble() < 0.05;
            var rot = h.rotation * Quaternion.Euler(0, 0, lean ? -18 : 0);
            var c = shelf + h.right * (x + th / 2 + (lean ? bh * 0.15f : 0)) + Vector3.up * (bh / 2 - (lean ? bh * 0.02f : 0));
            var book = Box("Book", g, Vector3.zero, new Vector3(th, bh, bd), mBooks[rng.Next(mBooks.Length)], default, false);
            book.transform.SetPositionAndRotation(c, rot);
            x += th + (lean ? bh * 0.3f : 0.002f);
        }
    }

    // ================================================================ Primitive builders (front = +Z, origin at floor)
    static Transform DFireplaceBuild(Transform parent)
    {
        var g = Group("Fireplace", parent);
        Box("JambL", g, new Vector3(-0.75f, 0.55f, 0), new Vector3(0.36f, 1.1f, 0.5f), mStone);
        Box("JambR", g, new Vector3(0.75f, 0.55f, 0), new Vector3(0.36f, 1.1f, 0.5f), mStone);
        Box("Lintel", g, new Vector3(0, 1.2f, 0), new Vector3(1.86f, 0.22f, 0.5f), mStone);
        Box("Mantel", g, new Vector3(0, 1.34f, 0.04f), new Vector3(2.05f, 0.07f, 0.6f), wood);
        Box("Hearth", g, new Vector3(0, 0.03f, 0.2f), new Vector3(2.05f, 0.06f, 0.9f), mStone);
        Box("Back", g, new Vector3(0, 0.55f, -0.21f), new Vector3(1.14f, 1.1f, 0.08f), mSoot);
        Box("Chimney", g, new Vector3(0, 1.85f, -0.05f), new Vector3(1.4f, 1.1f, 0.4f), mStone);
        Box("Embers", g, new Vector3(0, 0.08f, 0), new Vector3(0.7f, 0.04f, 0.3f), mEmber, default, false);
        for (int i = 0; i < 3; i++)
        {
            var log = DCyl("Log", g, new Vector3(Rand(-0.12f, 0.12f), 0.15f + (i == 2 ? 0.1f : 0), (i - 1) * 0.08f), 0.06f, 0.65f, wood);
            log.transform.localRotation = Quaternion.Euler(Rand(-8, 8), Rand(-15, 15), 90);
        }
        return g;
    }

    static Transform DLampBuild(Transform parent)
    {
        var g = Group("TableLamp", parent);
        DCyl("Foot", g, new Vector3(0, 0.015f, 0), 0.08f, 0.03f, mBrass);
        DCyl("Stem", g, new Vector3(0, 0.2f, 0), 0.015f, 0.36f, mBrass);
        var shade = DCyl("Shade", g, new Vector3(0, 0.42f, 0), 0.17f, 0.22f, mShade);
        shade.transform.localScale = new Vector3(0.34f, 0.11f, 0.34f);
        return g;
    }

    static Transform DSconceBuild(Transform parent)
    {
        var g = Group("Sconce", parent);
        Box("Plate", g, new Vector3(0, 0, 0.01f), new Vector3(0.09f, 0.22f, 0.02f), mBrass, default, false);
        Box("Arm", g, new Vector3(0, -0.04f, 0.08f), new Vector3(0.025f, 0.025f, 0.14f), mBrass, default, false);
        DCyl("Cup", g, new Vector3(0, -0.02f, 0.15f), 0.035f, 0.02f, mBrass);
        DCyl("Candle", g, new Vector3(0, 0.05f, 0.15f), 0.018f, 0.12f, mWax);
        DSphere("Flame", g, new Vector3(0, 0.13f, 0.15f), new Vector3(0.022f, 0.05f, 0.022f), mFlame);
        return g;
    }

    static Transform DPiano(Transform parent)
    {
        var g = Group("Piano", parent);
        Box("Body", g, new Vector3(0, 0.65f, 0), new Vector3(1.5f, 1.3f, 0.6f), mLacquer);
        Box("KeyBed", g, new Vector3(0, 0.72f, 0.42f), new Vector3(1.5f, 0.08f, 0.26f), mLacquer);
        Box("Keys", g, new Vector3(0, 0.77f, 0.43f), new Vector3(1.38f, 0.02f, 0.16f), mPorcelain, default, false);
        Box("LegL", g, new Vector3(-0.68f, 0.34f, 0.48f), new Vector3(0.08f, 0.68f, 0.08f), mLacquer, default, false);
        Box("LegR", g, new Vector3(0.68f, 0.34f, 0.48f), new Vector3(0.08f, 0.68f, 0.08f), mLacquer, default, false);
        DOnTopLocal(g, P("wooden_candlestick"), new Vector3(-0.55f, 1.3f, 0));
        return g;
    }

    static Transform DBathtub(Transform parent)
    {
        var g = Group("Bathtub", parent);
        Box("Tub", g, new Vector3(0, 0.42f, 0), new Vector3(1.7f, 0.56f, 0.76f), mPorcelain);
        Box("Water", g, new Vector3(0, 0.66f, 0), new Vector3(1.5f, 0.06f, 0.58f), mGrime, default, false);
        Box("RimL", g, new Vector3(0, 0.71f, 0.35f), new Vector3(1.7f, 0.04f, 0.06f), mPorcelain, default, false);
        Box("RimR", g, new Vector3(0, 0.71f, -0.35f), new Vector3(1.7f, 0.04f, 0.06f), mPorcelain, default, false);
        Box("RimA", g, new Vector3(0.82f, 0.71f, 0), new Vector3(0.06f, 0.04f, 0.76f), mPorcelain, default, false);
        Box("RimB", g, new Vector3(-0.82f, 0.71f, 0), new Vector3(0.06f, 0.04f, 0.76f), mPorcelain, default, false);
        for (int i = 0; i < 4; i++)
            DSphere("Foot", g, new Vector3(i % 2 == 0 ? -0.7f : 0.7f, 0.07f, i < 2 ? -0.28f : 0.28f), Vector3.one * 0.14f, mBrass);
        return g;
    }

    static Transform DSink(Transform parent)
    {
        var g = Group("Sink", parent);
        DCyl("Pedestal", g, new Vector3(0, 0.38f, 0), 0.09f, 0.76f, mPorcelain);
        Box("Basin", g, new Vector3(0, 0.82f, 0.02f), new Vector3(0.6f, 0.14f, 0.45f), mPorcelain);
        Box("Grime", g, new Vector3(0, 0.895f, 0.02f), new Vector3(0.48f, 0.01f, 0.32f), mGrime, default, false);
        DCyl("Tap", g, new Vector3(0, 0.95f, -0.17f), 0.015f, 0.12f, mBrass);
        return g;
    }

    static Transform DStove(Transform parent)
    {
        var g = Group("Range", parent);
        Box("Body", g, new Vector3(0, 0.42f, 0), new Vector3(1.3f, 0.84f, 0.65f), mIron);
        Box("Top", g, new Vector3(0, 0.86f, 0), new Vector3(1.36f, 0.04f, 0.7f), rust);
        Box("OvenDoor", g, new Vector3(-0.32f, 0.45f, 0.33f), new Vector3(0.5f, 0.45f, 0.03f), mIron, default, false);
        Box("Handle", g, new Vector3(-0.32f, 0.66f, 0.36f), new Vector3(0.36f, 0.02f, 0.02f), mBrass, default, false);
        var fb = Box("Firebox", g, new Vector3(0.32f, 0.3f, 0.335f), new Vector3(0.36f, 0.14f, 0.02f), mEmber, default, false);
        DCyl("Flue", g, new Vector3(0.4f, 1.9f, -0.2f), 0.09f, 2.0f, mIron);
        Box("Kettle", g, new Vector3(-0.3f, 0.95f, 0.05f), new Vector3(0.22f, 0.14f, 0.18f), mIron, default, false);
        return g;
    }

    static Transform DCounter(Transform parent)
    {
        var g = Group("Counter", parent);
        Box("Base", g, new Vector3(0, 0.44f, 0), new Vector3(1.2f, 0.88f, 0.6f), wood);
        Box("Top", g, new Vector3(0, 0.9f, 0.02f), new Vector3(1.24f, 0.05f, 0.64f), mStone);
        Box("DoorL", g, new Vector3(-0.3f, 0.45f, 0.305f), new Vector3(0.54f, 0.7f, 0.02f), wood, default, false);
        Box("DoorR", g, new Vector3(0.3f, 0.45f, 0.305f), new Vector3(0.54f, 0.7f, 0.02f), wood, default, false);
        for (int i = 0; i < 3; i++)
            DCyl("Jar", g, new Vector3(Rand(-0.5f, 0.5f), 0.92f + 0.08f, Rand(-0.15f, 0.1f)), Rand(0.04f, 0.07f), 0.16f, rng.NextDouble() < 0.5 ? mGlassDark : mPorcelain);
        return g;
    }

    static Transform DDesk(Transform parent)
    {
        var g = Group("Desk", parent);
        Box("Top", g, new Vector3(0, 0.76f, 0), new Vector3(1.5f, 0.05f, 0.75f), wood);
        Box("PedL", g, new Vector3(-0.55f, 0.37f, 0), new Vector3(0.4f, 0.74f, 0.7f), wood);
        Box("PedR", g, new Vector3(0.55f, 0.37f, 0), new Vector3(0.4f, 0.74f, 0.7f), wood);
        Box("Blotter", g, new Vector3(0, 0.79f, 0.05f), new Vector3(0.6f, 0.01f, 0.4f), mRugs[1], default, false);
        Box("Papers", g, new Vector3(0.2f, 0.795f, 0.1f), new Vector3(0.21f, 0.01f, 0.3f), mPorcelain, new Vector3(0, 12, 0), false);
        return g;
    }

    static Transform DCot(Transform parent)
    {
        var g = Group("Cot", parent);
        Box("Frame", g, new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.1f, 1.95f), mIron);
        for (int i = 0; i < 4; i++)
            Box("Leg", g, new Vector3(i % 2 == 0 ? -0.42f : 0.42f, 0.12f, i < 2 ? -0.95f : 0.95f), new Vector3(0.04f, 0.25f, 0.04f), mIron, default, false);
        Box("Head", g, new Vector3(0, 0.55f, -0.96f), new Vector3(0.9f, 0.7f, 0.04f), mIron, default, false);
        Box("Mattress", g, new Vector3(0, 0.36f, 0.02f), new Vector3(0.84f, 0.14f, 1.85f), cloth, new Vector3(0, 0, 2), false);
        Box("Pillow", g, new Vector3(0, 0.46f, -0.75f), new Vector3(0.5f, 0.08f, 0.3f), cloth, new Vector3(0, 6, 0), false);
        return g;
    }

    // ================================================================ Placement core
    // A builder makes an unplaced holder (identity yaw, front +Z) under `parent`.
    static System.Func<Transform, Transform> P(string ph, float scale = 1f, string hero = null) => parent =>
    {
        var asset = DModel(ph, hero);
        if (asset == null) return null;
        var h = Group(asset.name, parent);
        var m = (GameObject)PrefabUtility.InstantiatePrefab(asset, h);
        m.transform.localScale *= scale;
        return h;
    };

    // Blender hero model whose file name contains `hero`, else the Poly Haven model (null if neither).
    static GameObject DModel(string ph, string hero)
    {
        if (hero != null && AssetDatabase.IsValidFolder(HeroDir))
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { HeroDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(path).IndexOf(hero, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        return ph == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>($"{PackDir}/{ph}/{ph}_1k.fbx");
    }

    // Against wall s at fraction t along it; tries nearby spots unless retry is false.
    static Transform DWall(Room r, System.Func<Transform, Transform> make, DSide s, float t, bool retry = true, float pad = 0.25f)
    {
        foreach (var dt in retry ? new[] { 0f, 0.15f, -0.15f, 0.3f, -0.3f } : new[] { 0f })
        {
            var h = make(r.root);
            if (h == null) return null;
            h.rotation = Quaternion.Euler(0, DYaw(s), 0);
            var b = DBounds(h);
            var p = DWallPoint(r, s, Mathf.Clamp01(t + dt), b.extents, 0.04f);
            b = DMoveTo(h, p);
            float halfW = (DAlong(s) ? b.extents.x : b.extents.z) * 0.8f;
            bool wallOk = DWallClear(r, s, p, r.floorY + 0.5f, halfW, h) && (b.size.y < 1.1f || DWallClear(r, s, p, r.floorY + 1.7f, halfW, h));
            if (wallOk && DCommit(h, true, true, pad)) return h;
            if (h != null) Object.DestroyImmediate(h.gameObject);
        }
        return null;
    }

    // Hung on wall s with its centre `y` above the floor (paintings, mirrors, sconces). No collider.
    static Transform DHang(Room r, System.Func<Transform, Transform> make, DSide s, float t, float y)
    {
        var h = make(r.root);
        if (h == null) return null;
        h.rotation = Quaternion.Euler(0, DYaw(s), 0);
        var b = DBounds(h);
        var p = DWallPoint(r, s, t, b.extents, 0.01f);
        h.position += new Vector3(p.x - b.center.x, r.floorY + y - b.center.y, p.z - b.center.z);
        float halfW = DAlong(s) ? b.extents.x : b.extents.z;
        if (DWallClear(r, s, p, r.floorY + y, halfW, h) && DCommit(h, false, true, 0.15f)) return h;
        if (h != null) Object.DestroyImmediate(h.gameObject);
        return null;
    }

    // Free-standing at a floor point with a rotation.
    static Transform DAt(Transform parent, System.Func<Transform, Transform> make, Vector3 floorPoint, Quaternion rot, bool solid = true, float pad = 0.1f)
    {
        var h = make(parent);
        if (h == null) return null;
        h.rotation = rot;
        DMoveTo(h, floorPoint);
        return DCommit(h, solid, true, pad) ? h : null;
    }

    // In the middle of the room, only if it leaves `clear` metres all round (walking route).
    static Transform DCentre(Room r, System.Func<Transform, Transform> make, float yaw, float clear, Vector3? stretch = null)
    {
        var h = make(r.root);
        if (h == null) return null;
        if (stretch.HasValue) h.localScale = stretch.Value;
        h.rotation = Quaternion.Euler(0, yaw, 0);
        var b = DMoveTo(h, new Vector3(r.rect.center.x, r.floorY, r.rect.center.y));
        bool fits = b.size.x + 2 * (clear + 0.8f) < r.rect.width && b.size.z + 2 * (clear + 0.8f) < r.rect.height;
        if (fits && DCommit(h, true, true, clear)) return h;
        if (h != null) Object.DestroyImmediate(h.gameObject);
        return null;
    }

    // On top of a placed prop, offset along its right axis. No collision checks (it sits on the prop).
    static Transform DOnTop(Transform under, System.Func<Transform, Transform> make, float along, System.Action<Transform> then,
                            float yawJitter = 0f, bool solid = false)
    {
        var h = make(under.parent); // not under `under`: it may be non-uniformly scaled
        if (h == null) return null;
        h.rotation = under.rotation * Quaternion.Euler(0, yawJitter, 0);
        var ub = DBounds(under);
        var top = DTopAt(under, ub.center + under.right * along);
        DMoveTo(h, top);
        if (solid) { Reserve(DBounds(h)); DAddCollider(h); }
        DStatic(h);
        then?.Invoke(h);
        return h;
    }

    static void DOnTopLocal(Transform g, System.Func<Transform, Transform> make, Vector3 local)
    {
        var h = make(g);
        if (h == null) return;
        h.localRotation = Quaternion.identity;
        h.localPosition = local;
    }

    // Highest surface of a prop above point p (raycast on its meshes, falls back to the bounds top).
    static Vector3 DTopAt(Transform prop, Vector3 p)
    {
        var b = DBounds(prop);
        var cols = prop.GetComponentsInChildren<MeshFilter>().Select(mf =>
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            return mc;
        }).ToList();
        Physics.SyncTransforms();
        float y = b.max.y;
        float best = float.MinValue;
        var ray = new Ray(new Vector3(p.x, b.max.y + 0.5f, p.z), Vector3.down);
        foreach (var c in cols)
            if (c.Raycast(ray, out var hit, 5f) && hit.point.y > best) best = hit.point.y;
        foreach (var c in cols) Object.DestroyImmediate(c);
        return new Vector3(p.x, best > float.MinValue ? best : y, p.z);
    }

    // Free of reserved footprints and of existing colliders (walls, stairs, interactables, other props)?
    static bool DCommit(Transform h, bool solid, bool check, float pad)
    {
        var b = DBounds(h);
        if (check)
        {
            var test = b;
            test.Expand(new Vector3(pad * 2, -0.1f, pad * 2));
            Physics.SyncTransforms();
            var ext = Vector3.Max(b.extents - new Vector3(0.03f, 0.03f, 0.03f), Vector3.one * 0.005f);
            bool hitsCollider = Physics.OverlapBox(b.center + Vector3.up * 0.03f, ext, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                                       .Any(c => !c.transform.IsChildOf(h));
            if (!IsFree(test) || hitsCollider)
            {
                Object.DestroyImmediate(h.gameObject);
                return false;
            }
        }
        if (solid)
        {
            Reserve(b);
            DAddCollider(h);
        }
        DStatic(h);
        return true;
    }

    static void DAddCollider(Transform h)
    {
        var b = DBounds(h);
        var c = h.gameObject.AddComponent<BoxCollider>();
        c.center = h.InverseTransformPoint(b.center);
        var s = h.InverseTransformVector(b.size);
        c.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        foreach (var child in h.GetComponentsInChildren<Collider>()) if (child != c) Object.DestroyImmediate(child); // one box is enough
    }

    static void DStatic(Transform h)
    {
        foreach (var t in h.GetComponentsInChildren<Transform>()) if (t.GetComponent<Light>() == null) t.gameObject.isStatic = true;
    }

    static Bounds DBounds(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Moves h so its bounds' bottom-centre sits at p.
    static Bounds DMoveTo(Transform h, Vector3 p)
    {
        var b = DBounds(h);
        h.position += p - new Vector3(b.center.x, b.min.y, b.center.z);
        return DBounds(h);
    }

    // Is there solid wall (not a doorway/window or a prop) behind point p at height y, across +-halfW?
    static bool DWallClear(Room r, DSide s, Vector3 p, float y, float halfW, Transform ignore)
    {
        Physics.SyncTransforms();
        var dir = -DIn(s);
        var tangent = DAlong(s) ? Vector3.right : Vector3.forward;
        var edge = DEdge(r, s, p);
        foreach (var o in new[] { -halfW, 0f, halfW })
        {
            var origin = new Vector3(edge.x, y, edge.z) - dir * 0.5f + tangent * o;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(origin, dir, 0.8f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(ignore)) nearest = Mathf.Min(nearest, hit.distance);
            if (nearest < 0.4f || nearest > 0.8f) return false;
        }
        return true;
    }

    static Vector3 DEdge(Room r, DSide s, Vector3 p)
    {
        switch (s)
        {
            case DSide.S: return new Vector3(p.x, p.y, r.rect.yMin);
            case DSide.N: return new Vector3(p.x, p.y, r.rect.yMax);
            case DSide.W: return new Vector3(r.rect.xMin, p.y, p.z);
            default: return new Vector3(r.rect.xMax, p.y, p.z);
        }
    }

    static Vector3 DWallPoint(Room r, DSide s, float t, Vector3 ext, float gap)
    {
        var R = r.rect;
        bool alongX = DAlong(s);
        float half = alongX ? ext.x : ext.z, depth = alongX ? ext.z : ext.x;
        float a0 = (alongX ? R.xMin : R.yMin) + half + 0.1f, a1 = (alongX ? R.xMax : R.yMax) - half - 0.1f;
        float a = a1 < a0 ? (a0 + a1) / 2 : Mathf.Lerp(a0, a1, t);
        switch (s)
        {
            case DSide.S: return new Vector3(a, r.floorY, R.yMin + gap + depth);
            case DSide.N: return new Vector3(a, r.floorY, R.yMax - gap - depth);
            case DSide.W: return new Vector3(R.xMin + gap + depth, r.floorY, a);
            default: return new Vector3(R.xMax - gap - depth, r.floorY, a);
        }
    }

    // Next to `anchor` along the same wall, on the -1 or +1 side.
    static Transform DNextTo(Room r, Transform anchor, System.Func<Transform, Transform> make, DSide s, int dir)
    {
        var a = DBounds(anchor);
        var h = make(r.root);
        if (h == null) return null;
        h.rotation = Quaternion.Euler(0, DYaw(s), 0);
        var b = DBounds(h);
        var p = DWallPoint(r, s, 0.5f, b.extents, 0.04f);
        if (DAlong(s)) p.x = dir < 0 ? a.min.x - 0.08f - b.extents.x : a.max.x + 0.08f + b.extents.x;
        else p.z = dir < 0 ? a.min.z - 0.08f - b.extents.z : a.max.z + 0.08f + b.extents.z;
        DMoveTo(h, p);
        return DCommit(h, true, true, 0.03f) ? h : null;
    }

    static void DRug(Transform parent, Vector3 floorCentre, Vector2 size)
    {
        if (size.x < 0.8f || size.y < 0.8f) return;
        var g = Group("Rug", parent);
        g.SetPositionAndRotation(floorCentre, Quaternion.Euler(0, Rand(-2, 2), 0));
        var m = mRugs[rng.Next(mRugs.Length)];
        Box("Border", g, Vector3.up * 0.006f, new Vector3(size.x, 0.012f, size.y), mRugBorder, default, false);
        Box("Field", g, Vector3.up * 0.008f, new Vector3(size.x - 0.24f, 0.012f, size.y - 0.24f), m, default, false);
        Box("Medallion", g, Vector3.up * 0.0095f, new Vector3((size.x - 0.24f) * 0.4f, 0.012f, (size.y - 0.24f) * 0.4f), mRugBorder,
            new Vector3(0, 45 * (size.x > size.y ? 0 : 1), 0), false);
        DStatic(g);
    }

    static Light DLight(Transform p, Vector3 worldPos, Color c, float intensity, float range, bool keyShadow = false, float flicker = 0f, bool dying = false)
    {
        var l = NewLight("PracticalLight", p, LightType.Point, c, intensity, Vector3.zero);
        l.transform.position = worldPos;
        l.range = range;
        l.shadows = LightShadows.None;
        if (keyShadow && shadowBudget > 0)
        {
            shadowBudget--;
            l.shadows = LightShadows.Soft;
            l.shadowNearPlane = 0.25f;
            // Low tier (256 px per cube face) so 8 point lights x 6 faces fit the 2048 shadow atlas.
            // The property setter throws outside Play mode, so write the serialized field.
            var so = new SerializedObject(l.GetUniversalAdditionalLightData());
            var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
            if (tier != null) { tier.intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
        if (flicker > 0)
        {
            var f = l.gameObject.AddComponent<LightFlicker>();
            f.amount = flicker;
            f.dying = dying;
        }
        return l;
    }

    static GameObject DCyl(string name, Transform parent, Vector3 pos, float radius, float height, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(radius * 2, height / 2, radius * 2);
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    static GameObject DSphere(string name, Transform parent, Vector3 localPos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = m;
        if (m == mFlame || m == mBulb) rend.shadowCastingMode = ShadowCastingMode.Off;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    // ---------------------------------------------------------------- Room geometry helpers
    static float DYaw(DSide s) => s == DSide.S ? 0 : s == DSide.N ? 180 : s == DSide.W ? 90 : 270;
    static Vector3 DIn(DSide s) => s == DSide.S ? Vector3.forward : s == DSide.N ? Vector3.back : s == DSide.W ? Vector3.right : Vector3.left;
    static bool DAlong(DSide s) => s == DSide.S || s == DSide.N;
    static DSide DOpp(DSide s) => s == DSide.S ? DSide.N : s == DSide.N ? DSide.S : s == DSide.W ? DSide.E : DSide.W;
    static DSide DSideTurn(DSide s) => s == DSide.S ? DSide.W : s == DSide.W ? DSide.N : s == DSide.N ? DSide.E : DSide.S;
    static DSide DLongSide(Room r, bool first) => r.rect.width >= r.rect.height ? (first ? DSide.S : DSide.N) : (first ? DSide.W : DSide.E);
    static float DDepth(Bounds b, DSide s) => DAlong(s) ? b.size.z : b.size.x;
    static float DSpan(Room r) => Mathf.Max(r.rect.width, r.rect.height);
    static Vector3 DCeiling(Room r, float below) => new Vector3(r.rect.center.x, r.floorY + r.height - below, r.rect.center.y);
    static Rect DInset(Room r, float m) => new Rect(r.rect.xMin + m, r.rect.yMin + m, Mathf.Max(0.1f, r.rect.width - 2 * m), Mathf.Max(0.1f, r.rect.height - 2 * m));
    static Vector3 DRandIn(Room r, float margin) { var i = DInset(r, margin); return new Vector3(Rand(i.xMin, i.xMax), r.floorY, Rand(i.yMin, i.yMax)); }

    // Walls in a seeded random order.
    static DSide[] DSides(Room r) => new[] { DSide.S, DSide.N, DSide.W, DSide.E }.OrderBy(_ => rng.Next()).ToArray();

    // Which wall a prop was placed against (nearest rect edge to its bounds centre).
    static DSide DSideOf(Room r, Transform h)
    {
        var c = DBounds(h).center;
        float s = c.z - r.rect.yMin, n = r.rect.yMax - c.z, w = c.x - r.rect.xMin, e = r.rect.xMax - c.x;
        float m = Mathf.Min(Mathf.Min(s, n), Mathf.Min(w, e));
        return m == s ? DSide.S : m == n ? DSide.N : m == w ? DSide.W : DSide.E;
    }

    // Fraction t along wall s that puts an item centred at world point p.
    static float DAlongT(Room r, DSide s, Vector3 p) =>
        DAlong(s) ? Mathf.InverseLerp(r.rect.xMin, r.rect.xMax, p.x) : Mathf.InverseLerp(r.rect.yMin, r.rect.yMax, p.z);

    // URP shades with RenderSettings.ambientProbe, which Unity only regenerates on a lighting bake,
    // so a freshly built scene would keep the previous scene's (near-black) probe.
    static void SetAmbientProbe(Color c)
    {
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(c);
        RenderSettings.ambientProbe = sh;
    }

    // ================================================================ Lighting + render settings
    // One directional moon (the DayNightCycle drives it at runtime); ambient/fog here are the NIGHT baseline.
    static void BuildLighting(Transform p)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = NightAmbient;
        SetAmbientProbe(NightAmbient);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogColor = FogColor;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; // no sky reflections indoors

        var moon = NewLight("Moonlight", p, LightType.Directional, Cold, 0.35f, Vector3.zero);
        moon.transform.rotation = Quaternion.Euler(28, 70, 0);
        RenderSettings.sun = moon;

        var vol = new GameObject("PostProcess").AddComponent<Volume>();
        vol.transform.SetParent(p, false);
        vol.isGlobal = true;
        vol.priority = 10;
        vol.sharedProfile = PostProfile();

        // Player camera must render post-processing (layout spawns the player before DressRooms).
        foreach (var cam in Object.FindObjectsByType<Camera>())
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    }

    static VolumeProfile PostProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);
        }
        T Get<T>() where T : VolumeComponent
        {
            if (!profile.TryGet(out T c))
            {
                c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            c.active = true;
            return c;
        }
        Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
        var bloom = Get<Bloom>();
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.6f);
        bloom.scatter.Override(0.72f);
        var vig = Get<Vignette>();
        vig.intensity.Override(0.32f);
        vig.smoothness.Override(0.45f);
        var grain = Get<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Medium1);
        grain.intensity.Override(0.22f);
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.45f); // ACES darkens mid-tones; keep it readable
        ca.contrast.Override(12f);
        ca.saturation.Override(-22f);
        ca.colorFilter.Override(new Color(0.93f, 0.97f, 1f));
        Get<WhiteBalance>().temperature.Override(-12f);
        EditorUtility.SetDirty(profile);
        return profile;
    }

    // ================================================================ Materials
    static void MakeMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "House");
        // Surfaces: solid colours only (the Overgrowth pass textures them). Names/GUIDs must stay stable.
        wood = Mat("DarkWood", new Color(0.16f, 0.11f, 0.075f), 0.15f, 0f);
        plaster = Mat("GrimyPlaster", new Color(0.30f, 0.29f, 0.25f), 0.05f, 0f);
        concrete = Mat("Concrete", new Color(0.20f, 0.20f, 0.19f), 0.1f, 0f);
        rust = Mat("Rust", new Color(0.26f, 0.12f, 0.06f), 0.3f, 0.5f);
        water = Mat("FloodWater", new Color(0.015f, 0.02f, 0.02f), 0.95f, 0f);
        cloth = Mat("StainedCloth", new Color(0.36f, 0.33f, 0.27f), 0.0f, 0f);

        // Props.
        mBrass = Mat("Brass", new Color(0.55f, 0.42f, 0.2f), 0.55f, 0.85f);
        mStone = Mat("FireplaceStone", new Color(0.33f, 0.31f, 0.29f), 0.12f, 0f);
        mSoot = Mat("Soot", new Color(0.03f, 0.025f, 0.02f), 0.05f, 0f);
        mIron = Mat("CastIron", new Color(0.07f, 0.07f, 0.075f), 0.35f, 0.7f);
        mPorcelain = Mat("Porcelain", new Color(0.72f, 0.7f, 0.64f), 0.75f, 0f);
        mLacquer = Mat("PianoLacquer", new Color(0.03f, 0.02f, 0.02f), 0.85f, 0f);
        mGrime = Mat("GrimeWater", new Color(0.09f, 0.08f, 0.05f), 0.9f, 0f);
        mWax = Mat("CandleWax", new Color(0.8f, 0.76f, 0.62f), 0.3f, 0f);
        mGlassDark = Mat("GlassDark", new Color(0.06f, 0.08f, 0.07f), 0.95f, 0f);
        mRugBorder = Mat("RugBorder", new Color(0.18f, 0.12f, 0.06f), 0f, 0f);
        mFlame = DEmissive("Flame", new Color(1f, 0.7f, 0.35f), new Color(1f, 0.55f, 0.2f) * 6f);
        mBulb = DEmissive("Bulb", new Color(1f, 0.85f, 0.6f), new Color(1f, 0.75f, 0.45f) * 4f);
        mEmber = DEmissive("Embers", new Color(0.3f, 0.06f, 0.01f), new Color(1f, 0.3f, 0.05f) * 3f);
        mShade = DEmissive("LampShade", new Color(0.75f, 0.62f, 0.42f), new Color(1f, 0.7f, 0.4f) * 0.9f);
        mGlassLit = DEmissive("GlassLit", new Color(0.5f, 0.4f, 0.25f), new Color(1f, 0.65f, 0.3f) * 1.5f);
        mRugs = new[]
        {
            Mat("Rug_Oxblood", new Color(0.28f, 0.05f, 0.05f), 0f, 0f),
            Mat("Rug_Green", new Color(0.06f, 0.16f, 0.1f), 0f, 0f),
            Mat("Rug_Navy", new Color(0.06f, 0.07f, 0.18f), 0f, 0f),
        };
        mBooks = new[]
        {
            Mat("Book_Red", new Color(0.3f, 0.06f, 0.05f), 0.25f, 0f), Mat("Book_Green", new Color(0.07f, 0.17f, 0.09f), 0.25f, 0f),
            Mat("Book_Blue", new Color(0.07f, 0.09f, 0.22f), 0.25f, 0f), Mat("Book_Brown", new Color(0.24f, 0.14f, 0.07f), 0.25f, 0f),
            Mat("Book_Black", new Color(0.04f, 0.035f, 0.03f), 0.3f, 0f), Mat("Book_Tan", new Color(0.45f, 0.36f, 0.22f), 0.2f, 0f),
        };
        mCanvases = new[]
        {
            Mat("Canvas_Umber", new Color(0.16f, 0.1f, 0.06f), 0.35f, 0f), Mat("Canvas_Moss", new Color(0.1f, 0.13f, 0.08f), 0.35f, 0f),
            Mat("Canvas_Dusk", new Color(0.12f, 0.1f, 0.16f), 0.35f, 0f), Mat("Canvas_Blood", new Color(0.2f, 0.05f, 0.04f), 0.35f, 0f),
        };
        SetupPackMaterials();
    }

    static Material DEmissive(string name, Color baseColor, Color emission)
    {
        var m = Mat(name, baseColor, 0.3f, 0f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    // Reuses the existing asset if present so re-runs keep GUIDs stable.
    static Material Mat(string name, Color c, float smooth, float metal)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(m);
        return m;
    }

    // Gives every pack FBX full-PBR URP Lit materials (<model>/<material>_URP.mat) and remaps the FBX onto them:
    //   _BaseMap      diffuse (BC7, sRGB)
    //   _BumpMap      OpenGL normal (BC5)
    //   _MetallicGlossMap + _OcclusionMap  one packed linear mask PNG: R metallic, G AO, A smoothness (1 - roughness),
    //                 built once from Poly Haven's ARM map in <model>/Source~ (that folder is not imported).
    // Glass/flame/bulb/canvas get the shared prop materials. Idempotent: only reimports what changed.
    static void SetupPackMaterials()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PackDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var map = imp.GetExternalObjectMap();
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name)
                .Concat(map.Keys.Where(k => k.type == typeof(Material)).Select(k => k.name)).Distinct().ToList();
            bool changed = false;
            foreach (var name in names)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                var mat = PackMat(dir, name); // refreshes already-created materials too
                if (map.TryGetValue(id, out var cur) && cur == mat) continue;
                imp.AddRemap(id, mat);
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
    }

    static Material PackMat(string dir, string matName)
    {
        string n = matName.ToLowerInvariant();
        if (n.EndsWith("_flame") || n.EndsWith("_bulb")) return mFlame;
        if (n.EndsWith("_glass")) return n.Contains("clock") ? mGlassDark : mGlassLit;
        if (n.EndsWith("_canvas")) return Mat("PaintingCanvas", new Color(0.14f, 0.1f, 0.07f), 0.35f, 0f);

        string path = $"{dir}/{matName}_URP.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        var diff = PackTex(dir, matName + "_diff", false, false);
        var nor = PackTex(dir, matName + "_nor_gl", true, true);
        var mask = PackMaskMap(dir, matName);
        m.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.78f)); // a little grime
        if (diff != null) m.SetTexture("_BaseMap", diff);
        if (nor != null)
        {
            m.SetTexture("_BumpMap", nor);
            m.SetFloat("_BumpScale", 1.2f); // lean on the normals: detail without geometry
            m.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            m.SetTexture("_MetallicGlossMap", mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_SmoothnessTextureChannel", 0); // smoothness from the mask's alpha
            m.SetFloat("_Smoothness", 0.9f);            // scale on the map; slightly duller for dust
            m.SetTexture("_OcclusionMap", mask);        // URP reads occlusion from G
            m.EnableKeyword("_OCCLUSIONMAP");
            m.SetFloat("_OcclusionStrength", 1f);
        }
        else
        {
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Metallic", 0f);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // Packs <model>/Source~/<mat>_arm_*.jpg (R AO, G roughness, B metal) into <mat>_MetalSmoothAO.png.
    static Texture2D PackMaskMap(string dir, string matName)
    {
        string outPath = $"{dir}/{matName}_MetalSmoothAO.png";
        if (!File.Exists(outPath))
        {
            var src = Path.Combine(dir, "Source~");
            var arm = Directory.Exists(src)
                ? Directory.GetFiles(src, "*.jpg").FirstOrDefault(f => Path.GetFileName(f).StartsWith(matName + "_arm", System.StringComparison.OrdinalIgnoreCase))
                : null;
            if (arm == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(arm));
            // Half resolution (2x2 box filter): masks are low-frequency and this keeps the repo ~30 MB lighter.
            int w = tex.width / 2, h = tex.height / 2;
            var s = tex.GetPixels32();
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        var c = s[(2 * y + k / 2) * tex.width + 2 * x + k % 2];
                        r += c.r; g += c.g; b += c.b;
                    }
                    px[y * w + x] = new Color32((byte)(b / 4), (byte)(r / 4), 0, (byte)(255 - g / 4));
                }
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            outTex.SetPixels32(px);
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(outTex);
            AssetDatabase.ImportAsset(outPath);
        }
        return PackTex(dir, Path.GetFileNameWithoutExtension(outPath), false, true);
    }

    // Finds <dir>/<prefix>* (jpg/png) and enforces import settings: mipmaps, max 1024, high-quality compression
    // (BC7 colour/mask, BC5 normal), masks/normals linear.
    static Texture2D PackTex(string dir, string prefix, bool normal, bool linear)
    {
        var f = Directory.GetFiles(dir).FirstOrDefault(x => !x.EndsWith(".meta") && Path.GetFileName(x).StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase));
        if (f == null) return null;
        var p = $"{dir}/{Path.GetFileName(f)}";
        if (AssetImporter.GetAtPath(p) is TextureImporter ti)
        {
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.sRGBTexture == linear || !ti.mipmapEnabled || ti.maxTextureSize != 1024 ||
                ti.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                ti.textureType = type;
                ti.sRGBTexture = !linear;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }
}
