// Overgrowth & exterior pass for the BLINK mansion. Called from HouseBlockout.AddDecay (after layout,
// interactables and dressing, before the navmesh bake). Deterministic: everything comes from the caller's rng.
//
//  1. Resurface: house cubes (DarkWood / GrimyPlaster / Concrete families + the Roof group) get
//     triplanar textured materials and a per-face vertex colour (R facade, G moss, B damp).
//  2. Roof holes: a few roof tiles removed, broken rafters + splinters, vines, a tree under one hole.
//  3. Interior: ivy creeping through windows, wall ivy, hanging vines, cobwebs, visual floor holes
//     with grass, leaf litter, fallen plaster, broken glass.
//  4. Exterior (OvergrowthBuilder.Exterior.cs): ground, path, garden, fence + gate, forest, treeline.
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Blink.Overgrowth.EditorTools
{
    public struct OgRoom
    {
        public string name, kind;   // kind = RoomKind.ToString()
        public int floor;           // -1 basement .. 3 attic
        public Rect rect;           // x/z inside of walls
        public float floorY, height;
        public Transform root;
    }

    public static partial class OvergrowthBuilder
    {
        static List<OgRoom> rooms;
        static Func<Bounds, bool> isFree;
        static Action<Bounds> reserve;
        static System.Random rng;
        static PhysicsScene phys;
        static OgMats M;
        static Dictionary<(Transform, Material), OgMeshBuilder> cards;
        static Rect foot;           // union of room rects (inside of the outer walls)
        static float groundY, roofTopY;
        static Transform ogRoot;
        static readonly List<Vector4> holes = new List<Vector4>();

        public static void Build(Transform house, List<OgRoom> roomList, Func<Bounds, bool> free, Action<Bounds> reserveFn, System.Random random)
        {
            rooms = roomList; isFree = free; reserve = reserveFn; rng = random;
            M = OvergrowthAssets.Ensure();
            cards = new Dictionary<(Transform, Material), OgMeshBuilder>();
            holes.Clear();
            phys = house.gameObject.scene.GetPhysicsScene();

            ComputeFootprint(house);
            ogRoot = Group("Overgrowth", house);
            IgnoreNav(ogRoot);

            Resurface(house);
            Physics.SyncTransforms();
            PunchRoofHoles(house);
            Physics.SyncTransforms();
            foreach (var room in rooms) DressRoom(room);
            BuildExterior(house);
            FlushCards();

            var globals = ogRoot.gameObject.AddComponent<OvergrowthGlobals>();
            globals.holes = holes.ToArray();
            Debug.Log($"[Overgrowth] rooms {rooms.Count}, roof holes {holes.Count}, footprint {foot}, ground y {groundY}");
        }

        // ---------------------------------------------------------------- footprint
        static void ComputeFootprint(Transform house)
        {
            groundY = 0;
            roofTopY = 0;
            bool any = false;
            float g = float.MaxValue;
            foreach (var r in rooms)
            {
                foot = any ? Rect.MinMaxRect(Mathf.Min(foot.xMin, r.rect.xMin), Mathf.Min(foot.yMin, r.rect.yMin),
                                             Mathf.Max(foot.xMax, r.rect.xMax), Mathf.Max(foot.yMax, r.rect.yMax)) : r.rect;
                any = true;
                if (r.floor == 0) g = Mathf.Min(g, r.floorY);
                roofTopY = Mathf.Max(roofTopY, r.floorY + r.height);
            }
            if (g < float.MaxValue) groundY = g;
            // Roof / fallback from renderers.
            var b = new Bounds();
            bool hasB = false;
            foreach (var r in house.GetComponentsInChildren<Renderer>())
            {
                if (!hasB) { b = r.bounds; hasB = true; } else b.Encapsulate(r.bounds);
            }
            if (!any && hasB) foot = Rect.MinMaxRect(b.min.x + 0.2f, b.min.z + 0.2f, b.max.x - 0.2f, b.max.z - 0.2f);
            if (hasB) roofTopY = Mathf.Max(roofTopY, b.max.y);
        }

        static bool InFoot(Vector3 p, float margin = 0) =>
            p.x > foot.xMin - margin && p.x < foot.xMax + margin && p.z > foot.yMin - margin && p.z < foot.yMax + margin;

        /// <summary>Floor height under p from the room list (highest floor at or below p).</summary>
        static float FloorAt(Vector3 p)
        {
            float best = float.MinValue;
            foreach (var r in rooms)
                if (r.rect.Contains(new Vector2(p.x, p.z)) && r.floorY <= p.y + 0.15f && r.floorY > best) best = r.floorY;
            return best > float.MinValue ? best : groundY;
        }

        // ---------------------------------------------------------------- 1. resurface
        enum Family { None, Plaster, Wood, Stone }

        static void Resurface(Transform house)
        {
            var cubeCache = new Dictionary<int, bool>();
            foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube" || r.sharedMaterial == null) continue;
                if (!r.gameObject.isStatic) continue; // moving things (doors, drawers) keep their material: world-space UVs would swim
                bool roof = UnderRoof(r.transform, house);
                string mn = r.sharedMaterial.name.ToLowerInvariant();
                var fam = mn.Contains("plaster") || mn.Contains("wall") || mn.Contains("paper") ? Family.Plaster
                        : mn.Contains("wood") || mn.Contains("plank") || mn.Contains("floor") ? Family.Wood
                        : mn.Contains("concrete") || mn.Contains("stone") || mn.Contains("brick") ? Family.Stone
                        : Family.None;
                if (fam == Family.None && !roof) continue;

                var s = r.transform.lossyScale;
                var up = r.transform.up;
                bool level = Mathf.Abs(up.y) > 0.99f;
                bool slab = level && Mathf.Abs(s.y) <= 0.45f && Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.z)) >= 0.9f;
                float cy = r.bounds.center.y;
                bool below = cy < groundY - 0.4f;

                Material mat = roof ? M.roof
                    : slab ? (below ? M.cellarFloor : M.floor)
                    : fam == Family.Stone || below && fam != Family.Wood ? M.cellarWall
                    : fam == Family.Plaster ? M.wall : M.wood;

                mf.sharedMesh = ColouredCube(r.transform, mat, below);
                r.sharedMaterial = mat;
            }
        }

        static bool UnderRoof(Transform t, Transform house)
        {
            for (; t != null && t != house; t = t.parent)
                if (t.name.StartsWith("Roof", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Unit cube (same local verts as Unity's) with vertex colour R facade, G moss, B damp.</summary>
        static Mesh ColouredCube(Transform t, Material mat, bool below)
        {
            var b = new OgMeshBuilder();
            bool roof = mat == M.roof;
            foreach (var (n, u, w) in OgMeshBuilder.Faces)
            {
                var nW = t.TransformDirection(n).normalized;
                var centreW = t.TransformPoint(n * 0.5f);
                bool side = Mathf.Abs(nW.y) < 0.5f;
                var probe = centreW + nW * 0.3f;
                bool ext = !roof && side && !InFoot(probe);
                float baseY = ext ? groundY : FloorAt(probe);
                Color Col(Vector3 local)
                {
                    var pw = t.TransformPoint(local);
                    float damp = side ? Mathf.Clamp01(1f - (pw.y - baseY) / 1.8f) : 0f;
                    if (ext && pw.y < groundY - 0.3f) damp = 1f; // buried / basement outside
                    if (below) damp = Mathf.Max(damp, 0.35f);
                    float moss = (ext ? 0.12f : 0f) + (below ? 0.06f : 0f);
                    return new Color(ext ? 1 : 0, moss, damp, 0);
                }
                Vector3 P(float su, float sw) => n * 0.5f + u * (su * 0.5f) + w * (sw * 0.5f);
                Vector3 a = P(-1, -1), bb = P(1, -1), c = P(1, 1), d = P(-1, 1);
                var (uvMin, uvMax) = MetreUV(t, a, u, w);
                b.Quad(a, bb, c, d, uvMin, uvMax, Col(a), Col(bb), Col(c), Col(d));
            }
            return b.ToMesh("OG_Cube_" + t.name, false);
        }

        /// <summary>World-scaled UVs (metres) for a cube face, so the plain URP/Lit safe-mode materials don't stretch.</summary>
        static (Vector2, Vector2) MetreUV(Transform t, Vector3 cornerLocal, Vector3 u, Vector3 w)
        {
            var uW = t.TransformVector(u); var wW = t.TransformVector(w);
            var cw = t.TransformPoint(cornerLocal);
            var o = new Vector2(Vector3.Dot(cw, uW.normalized), Vector3.Dot(cw, wW.normalized));
            return (o, o + new Vector2(uW.magnitude, wW.magnitude));
        }

        // ---------------------------------------------------------------- 2. roof holes
        static void PunchRoofHoles(Transform house)
        {
            var tiles = new List<Renderer>();
            foreach (var r in house.GetComponentsInChildren<Renderer>())
                if (UnderRoof(r.transform, house) && r.bounds.size.x < 7 && r.bounds.size.z < 7) tiles.Add(r);
            if (tiles.Count < 6) { Debug.LogWarning("[Overgrowth] Roof isn't split into tiles; no roof holes."); return; }

            // Candidate tiles above a room, shuffled deterministically.
            var picked = new List<Bounds>();
            var order = new List<Renderer>(tiles);
            Shuffle(order);
            int wanted = Mathf.Clamp(tiles.Count / 12, 2, 4);
            bool treeDone = false;
            foreach (var tile in order)
            {
                if (picked.Count >= wanted) break;
                var tb = tile.bounds;
                if (!InFoot(tb.center, -1.5f)) continue; // keep eaves intact
                bool farEnough = true;
                foreach (var p in picked) if (Vector3.Distance(p.center, tb.center) < 7f) farEnough = false;
                if (!farEnough) continue;
                if (!RoomBelow(tb.center, out var roomBelow)) continue;

                picked.Add(tb);
                var hole = Group("RoofHole_" + picked.Count, ogRoot);
                var tr = tile.transform;
                var tileRight = tr.right * tr.lossyScale.x * 0.5f;
                var tileFwd = tr.forward * tr.lossyScale.z * 0.5f;
                var tileUp = tr.up;
                var centre = tr.position;
                UnityEngine.Object.DestroyImmediate(tile.gameObject);

                holes.Add(new Vector4(centre.x, centre.y, centre.z, 5.5f));
                BrokenEdges(hole, centre, tileRight, tileFwd, tileUp);

                // Vines spilling down from the hole's edges.
                var vb = Cards(hole, M.vine);
                for (int i = 0; i < 7; i++)
                {
                    var edge = centre + tileRight * Rand(-1f, 1f) * 0.95f + tileFwd * (rng.Next(2) == 0 ? -0.95f : 0.95f);
                    HangingVine(vb, edge, Rand(1.2f, Mathf.Max(1.5f, edge.y - roomBelow.floorY - 0.6f)));
                }
                // Litter, moss and grass on the floor below.
                var below = new Vector3(centre.x, roomBelow.floorY, centre.z);
                var lit = Cards(roomBelow.root ? roomBelow.root : hole, M.litter);
                for (int i = 0; i < 6; i++)
                {
                    var p = below + new Vector3(Rand(-2f, 2f), 0, Rand(-2f, 2f));
                    if (FloorHit(p, roomBelow.floorY)) lit.Flat(p + Vector3.up * (0.006f + i * 0.0015f), Rand(0.8f, 1.6f), Rand(0.8f, 1.6f), Rand(0, 360));
                }
                var gb = Cards(roomBelow.root ? roomBelow.root : hole, M.grass);
                for (int i = 0; i < 14; i++)
                {
                    var p = below + new Vector3(Rand(-1.8f, 1.8f), 0, Rand(-1.8f, 1.8f));
                    if (FloorHit(p, roomBelow.floorY)) gb.Tuft(p, Rand(0.4f, 0.8f), Rand(0.3f, 0.7f), Rand(0, 360));
                }
                if (!treeDone) treeDone = TreeUnderHole(hole, below, centre.y, roomBelow);
            }
        }

        static bool RoomBelow(Vector3 p, out OgRoom room)
        {
            room = default;
            float best = float.MinValue;
            foreach (var r in rooms)
                if (r.rect.Contains(new Vector2(p.x, p.z)) && r.floorY < p.y && r.floorY > best) { best = r.floorY; room = r; }
            return best > float.MinValue;
        }

        static void BrokenEdges(Transform parent, Vector3 c, Vector3 right, Vector3 fwd, Vector3 up)
        {
            var wb = Cards(parent, M.wood);
            var col = new Color(0, 0.15f, 0.3f, 0);
            float halfR = right.magnitude, halfF = fwd.magnitude;
            var rN = right.normalized; var fN = fwd.normalized;
            // Rafters: one intact across the hole, one snapped and hanging.
            wb.Box(Matrix4x4.TRS(c - up * 0.12f + rN * halfR * Rand(-0.3f, 0.3f), Quaternion.LookRotation(fN, up), new Vector3(0.12f, 0.16f, halfF * 2.2f)), col);
            var snapPivot = c - up * 0.12f + fN * halfF + rN * halfR * Rand(-0.6f, 0.6f);
            var hang = Quaternion.AngleAxis(Rand(35, 60), rN) * -fN;
            wb.Box(Matrix4x4.TRS(snapPivot + hang * 0.7f, Quaternion.LookRotation(hang, up), new Vector3(0.11f, 0.14f, 1.4f)), col);
            // Splintered plank stubs along each edge, drooping into the hole.
            for (int e = 0; e < 4; e++)
            {
                var edgeN = e switch { 0 => rN, 1 => -rN, 2 => fN, _ => -fN };
                var along = e < 2 ? fN : rN;
                float half = e < 2 ? halfR : halfF, span = e < 2 ? halfF : halfR;
                for (int i = 0; i < 5; i++)
                {
                    var root = c + edgeN * half + along * Rand(-span, span) * 0.9f;
                    var inward = Quaternion.AngleAxis(Rand(-20, 40), Vector3.Cross(edgeN, up)) * -edgeN;
                    float len = Rand(0.2f, 0.6f);
                    wb.Box(Matrix4x4.TRS(root + inward * len * 0.5f - up * 0.03f, Quaternion.LookRotation(inward, up),
                        new Vector3(Rand(0.08f, 0.16f), 0.025f, len)), col);
                }
            }
        }

        static bool TreeUnderHole(Transform parent, Vector3 floor, float holeY, OgRoom room)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var p = floor + new Vector3(Rand(-1f, 1f), 0, Rand(-1f, 1f));
                var trunk = new Bounds(p + Vector3.up * 1f, new Vector3(0.9f, 2f, 0.9f));
                if (!room.rect.Contains(new Vector2(p.x, p.z)) || !isFree(trunk) || !FloorHit(p, room.floorY)) continue;
                reserve(trunk);
                var go = new GameObject("HoleTree");
                go.transform.SetParent(parent, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
                float scale = Mathf.Max(0.6f, (holeY + 2.5f - p.y) / 8f);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = OvergrowthAssets.Broadleaf;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { M.bark, M.leaves };
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f; cap.height = 3.2f; cap.center = new Vector3(0, 1.6f, 0);
                var obs = go.AddComponent<NavMeshObstacle>(); // whole tree is ignored by the bake; the trunk carves instead
                obs.shape = NavMeshObstacleShape.Capsule;
                obs.radius = 0.35f; obs.height = 3.2f; obs.center = new Vector3(0, 1.6f, 0);
                obs.carving = true; obs.carveOnlyStationary = true;
                go.isStatic = true;
                // Roots and grass around its foot.
                var gb = Cards(parent, M.grass);
                for (int i = 0; i < 10; i++)
                {
                    float a = Rand(0, Mathf.PI * 2), d = Rand(0.4f, 1.3f);
                    gb.Tuft(p + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d), Rand(0.4f, 0.7f), Rand(0.3f, 0.6f), Rand(0, 360));
                }
                var hb = Cards(parent, M.hole);
                hb.Flat(p + Vector3.up * 0.004f, 1.6f, 1.6f, Rand(0, 360));
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- 3. interior
        enum Seg { Wall, Window, Door }

        static void DressRoom(OgRoom room)
        {
            var parent = room.root ? room.root : ogRoot;
            float top = room.floorY + room.height;
            bool cellar = room.floor < 0;
            bool attic = room.kind == "Attic";
            var rc = room.rect;

            // Walls: scan each side for windows/doors.
            for (int side = 0; side < 4; side++)
            {
                WallFrame(rc, side, out var origin, out var along, out var inward, out float len);
                bool exterior = !InFoot(origin + along * len * 0.5f - inward * 0.6f);
                var segs = ScanWall(origin, along, inward, len, room.floorY, room.height);
                foreach (var (type, s0, s1) in segs)
                {
                    if (type == Seg.Window && exterior && !cellar) CreepThroughWindow(parent, room, origin, along, inward, s0, s1);
                    else if (type == Seg.Wall && s1 - s0 > 1.2f && rng.NextDouble() < (exterior ? 0.55 : 0.2) && !cellar)
                    {
                        float w = Mathf.Min(s1 - s0 - 0.2f, Rand(0.9f, 2.2f));
                        float at = Rand(s0 + w / 2 + 0.1f, s1 - w / 2 - 0.1f);
                        bool fromTop = rng.NextDouble() < 0.5;
                        float h = Rand(0.8f, room.height * 0.8f);
                        float y0 = fromTop ? top - h : room.floorY;
                        WallIvy(Cards(parent, M.ivy), origin + along * at + inward * 0.015f, along, inward, w, y0, h, fromTop);
                    }
                }
            }

            // Hanging vines (roots in the cellar) along the walls, high enough to walk under.
            int vines = attic ? rng.Next(4, 9) : cellar ? rng.Next(1, 4) : rng.Next(0, 4);
            var vb = Cards(parent, M.vine);
            for (int i = 0; i < vines; i++)
            {
                int side = rng.Next(4);
                WallFrame(rc, side, out var origin, out var along, out var inward, out float len);
                var p = origin + along * Rand(0.3f, len - 0.3f) + inward * Rand(0.08f, 0.5f);
                p.y = top - 0.02f;
                HangingVine(vb, p, Rand(0.6f, Mathf.Min(1.6f, room.height - 1.0f)));
            }

            // Cobwebs in the top corners.
            var cb = Cards(parent, M.cobweb);
            var corners = new[] { new Vector3(rc.xMin, top, rc.yMin), new Vector3(rc.xMax, top, rc.yMin), new Vector3(rc.xMax, top, rc.yMax), new Vector3(rc.xMin, top, rc.yMax) };
            for (int i = 0; i < 4; i++)
                if (rng.NextDouble() < (cellar || attic ? 0.75 : 0.45))
                {
                    var c = corners[i] + new Vector3(i == 0 || i == 3 ? 0.01f : -0.01f, -0.01f, i < 2 ? 0.01f : -0.01f);
                    var d1 = new Vector3(i == 0 || i == 3 ? 1 : -1, 0, 0);
                    var d2 = new Vector3(0, 0, i < 2 ? 1 : -1);
                    float s = Rand(0.5f, 1.0f);
                    // uv (0,1) at the corner, as drawn in T_Cobweb.
                    cb.Quad(c + d2 * s, c + (d1 + d2) * s * 0.55f + Vector3.down * s * 0.55f, c + d1 * s, c,
                        new Vector2(0, 0), new Vector2(1, 1), new Color(0, 0, 0, 1), new Color(0, 0, 0, 1), new Color(0, 0, 0, 0.3f), new Color(0, 0, 0, 0));
                }

            // Floor: visual holes with grass, leaf litter, plaster fallen from the ceiling.
            var area = rc.width * rc.height;
            if (!cellar && rng.NextDouble() < 0.2) // sparse: many read as black blobs
            {
                int n = 1;
                for (int i = 0; i < n; i++)
                {
                    var p = RandomFloorPoint(rc, room.floorY, 0.8f);
                    if (p == null) continue;
                    float sx = Rand(0.9f, 1.6f), sz = Rand(0.8f, 1.4f), yaw = rng.Next(4) * 90f + Rand(-4, 4);
                    Cards(parent, M.hole).Flat(p.Value + Vector3.up * 0.004f, sx, sz, yaw);
                    var gb = Cards(parent, M.grass);
                    for (int k = 0; k < 6; k++)
                        gb.Tuft(p.Value + new Vector3(Rand(-sx, sx) * 0.4f, 0, Rand(-sz, sz) * 0.4f), Rand(0.3f, 0.6f), Rand(0.25f, 0.6f), Rand(0, 360));
                }
            }
            int litter = (cellar ? 0 : 1) + (attic ? 3 : 0) + rng.Next(0, 3);
            for (int i = 0; i < litter; i++)
            {
                var p = RandomFloorPoint(rc, room.floorY, 0.4f);
                if (p != null) Cards(parent, M.litter).Flat(p.Value + Vector3.up * (0.006f + i * 0.0015f), Rand(0.7f, 1.5f), Rand(0.7f, 1.5f), Rand(0, 360));
            }
            if (!cellar)
            {
                var pb = Cards(parent, M.wall);
                int chunks = rng.Next(2, 6);
                var spot = RandomFloorPoint(rc, room.floorY, 0.6f);
                if (spot != null)
                {
                    for (int i = 0; i < chunks; i++)
                    {
                        var size = new Vector3(Rand(0.1f, 0.45f), Rand(0.02f, 0.05f), Rand(0.1f, 0.4f));
                        var p = spot.Value + new Vector3(Rand(-0.6f, 0.6f), size.y / 2, Rand(-0.6f, 0.6f));
                        pb.Box(Matrix4x4.TRS(p, Quaternion.Euler(Rand(-8, 8), Rand(0, 360), Rand(-8, 8)), size), new Color(0, 0.05f, 0.4f, 0));
                    }
                    // The patch of ceiling it fell from: dark lath showing through.
                    Cards(parent, M.hole).Flat(new Vector3(spot.Value.x, top - 0.004f, spot.Value.z), Rand(0.7f, 1.2f), Rand(0.6f, 1.0f), Rand(0, 360), down: true);
                }
            }
        }

        /// <summary>Wall of a room rect: origin at one end, `along` the wall, `inward` into the room.</summary>
        static void WallFrame(Rect rc, int side, out Vector3 origin, out Vector3 along, out Vector3 inward, out float len)
        {
            switch (side)
            {
                case 0: origin = new Vector3(rc.xMin, 0, rc.yMin); along = Vector3.right; inward = Vector3.forward; len = rc.width; break;
                case 1: origin = new Vector3(rc.xMax, 0, rc.yMax); along = Vector3.left; inward = Vector3.back; len = rc.width; break;
                case 2: origin = new Vector3(rc.xMin, 0, rc.yMax); along = Vector3.back; inward = Vector3.right; len = rc.height; break;
                default: origin = new Vector3(rc.xMax, 0, rc.yMin); along = Vector3.forward; inward = Vector3.left; len = rc.height; break;
            }
        }

        static List<(Seg, float, float)> ScanWall(Vector3 origin, Vector3 along, Vector3 inward, float len, float floorY, float height)
        {
            var res = new List<(Seg, float, float)>();
            const float step = 0.35f;
            for (float t = 0.25f; t < len - 0.2f; t += step)
            {
                var p = origin + along * t + inward * 0.4f;
                bool low = Hit(new Vector3(p.x, floorY + 0.6f, p.z), -inward, 0.7f);
                bool mid = Hit(new Vector3(p.x, floorY + Mathf.Min(1.6f, height - 0.4f), p.z), -inward, 0.7f);
                var type = low && mid ? Seg.Wall : low ? Seg.Window : Seg.Door;
                if (res.Count > 0 && res[res.Count - 1].Item1 == type) res[res.Count - 1] = (type, res[res.Count - 1].Item2, t + step / 2);
                else res.Add((type, t - step / 2, t + step / 2));
            }
            return res;
        }

        static bool Hit(Vector3 from, Vector3 dir, float dist) =>
            phys.Raycast(from, dir, out _, dist, ~0, QueryTriggerInteraction.Ignore);

        static bool FloorHit(Vector3 p, float floorY)
        {
            if (!phys.Raycast(new Vector3(p.x, floorY + 1.2f, p.z), Vector3.down, out var hit, 1.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return Mathf.Abs(hit.point.y - floorY) < 0.04f; // bare floor (no furniture, no hole)
        }

        static Vector3? RandomFloorPoint(Rect rc, float floorY, float margin)
        {
            if (rc.width < margin * 2 + 0.2f || rc.height < margin * 2 + 0.2f) return null;
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(Rand(rc.xMin + margin, rc.xMax - margin), floorY, Rand(rc.yMin + margin, rc.yMax - margin));
                if (FloorHit(p, floorY)) return p;
            }
            return null;
        }

        static void CreepThroughWindow(Transform parent, OgRoom room, Vector3 origin, Vector3 along, Vector3 inward, float s0, float s1)
        {
            float mid = (s0 + s1) / 2, w = s1 - s0;
            var basePt = origin + along * mid;
            // Find sill and lintel by probing up/down at the window centre.
            float sill = room.floorY + 0.6f, lintel = room.floorY + Mathf.Min(1.6f, room.height - 0.4f);
            var probe = basePt + inward * 0.4f;
            while (sill < lintel && Hit(new Vector3(probe.x, sill + 0.1f, probe.z), -inward, 0.7f)) sill += 0.1f; // climb until the ray clears the sill
            for (float y = lintel; y < room.floorY + room.height; y += 0.1f)
            {
                if (Hit(new Vector3(probe.x, y, probe.z), -inward, 0.7f)) break;
                lintel = y;
            }
            float open = lintel - sill;

            // Ivy hanging in from the lintel, and spilling over the sill down the inside wall.
            var ib = Cards(parent, M.ivy);
            float hang = Rand(0.4f, Mathf.Max(0.5f, open * 0.7f));
            WallIvy(ib, basePt - inward * 0.02f, along, inward, w + 0.4f, lintel + 0.2f - hang, hang, true);
            if (rng.NextDouble() < 0.7)
                WallIvy(ib, basePt + inward * 0.015f, along, inward, Rand(0.6f, w + 0.3f), Mathf.Max(room.floorY, sill - Rand(0.5f, 1.1f)), Mathf.Min(1.1f, sill - room.floorY) + 0.05f, true);
            var vb = Cards(parent, M.vine);
            for (int i = 0; i < 3; i++) HangingVine(vb, basePt + along * Rand(-w / 2, w / 2) + inward * Rand(0.05f, 0.25f) + Vector3.up * lintel, Rand(0.4f, open));
            // Grass on the sill.
            var gb = Cards(parent, M.grass);
            for (int i = 0; i < 3; i++)
                gb.Tuft(basePt + along * Rand(-w / 2, w / 2) + Vector3.up * (sill + 0.01f) + inward * 0.05f, Rand(0.25f, 0.4f), Rand(0.2f, 0.35f), Rand(0, 360));
            // Leaves blown in, glass on the floor.
            var floorP = basePt + inward * Rand(0.5f, 1.2f);
            floorP.y = room.floorY;
            if (FloorHit(floorP, room.floorY)) Cards(parent, M.litter).Flat(floorP + Vector3.up * 0.007f, Rand(0.9f, 1.6f), Rand(0.7f, 1.3f), Rand(0, 360));
            var glass = Cards(parent, M.glass);
            for (int i = 0; i < rng.Next(3, 8); i++)
            {
                var p = basePt + along * Rand(-w / 2, w / 2) + inward * Rand(0.15f, 0.9f);
                p.y = room.floorY;
                if (!FloorHit(p, room.floorY)) continue;
                float sx = Rand(0.03f, 0.12f), sz = Rand(0.03f, 0.1f);
                var rot = Quaternion.Euler(0, Rand(0, 360), 0);
                var a = p + rot * new Vector3(-sx, 0.003f, -sz);
                var b = p + rot * new Vector3(sx * Rand(0.3f, 1f), 0.003f, -sz * Rand(0.2f, 1f));
                var c = p + rot * new Vector3(sx, 0.003f, sz);
                var d = p + rot * new Vector3(-sx * Rand(0.2f, 1f), 0.003f, sz * Rand(0.3f, 1f));
                glass.Quad(a, b, c, d, Vector2.zero, Vector2.one, Color.white, Color.white, Color.white, Color.white);
            }
        }

        /// <summary>Ivy card flat on a wall. `wallPt` is on the wall surface at floor level; `inward` = the side it faces.</summary>
        static void WallIvy(OgMeshBuilder b, Vector3 wallPt, Vector3 along, Vector3 inward, float width, float y0, float height, bool hangsFromTop)
        {
            var p = new Vector3(wallPt.x, y0, wallPt.z);
            var right = Vector3.Cross(inward, Vector3.up).normalized * (width / 2); // Cross(up, right) == inward: front faces the room
            var up = Vector3.up * height;
            float u0 = Rand(0, 1), v0 = Rand(0, 1);
            var uvMax = new Vector2(u0 + width / 1.4f, v0 + height / 1.4f);
            if (hangsFromTop) b.Card(p - right, p + right, p + right + up, p - right + up, new Vector2(u0, v0), uvMax, 0.25f, 0f);
            else b.Card(p - right, p + right, p + right + up, p - right + up, new Vector2(u0, v0), uvMax, 0f, 0.25f);
        }

        /// <summary>Two crossed vine strips hanging from `top`; tip sways, anchor doesn't.</summary>
        static void HangingVine(OgMeshBuilder b, Vector3 top, float length)
        {
            float yaw = Rand(0, 180);
            float v0 = Rand(0, 1);
            for (int k = 0; k < 2; k++)
            {
                var f = Quaternion.Euler(0, yaw + 90 * k, 0) * Vector3.forward;
                var right = Vector3.Cross(f, Vector3.up).normalized * 0.17f;
                var bottom = top + Vector3.down * length;
                // texture v runs downward from the anchor (strip is 0.35 m wide, tile = 1.4 m tall)
                b.Card(bottom - right, bottom + right, top + right, top - right, new Vector2(0, v0), new Vector2(1, v0 + length / 1.4f), 1f, 0f);
            }
        }

        // ---------------------------------------------------------------- shared helpers
        static OgMeshBuilder Cards(Transform parent, Material m)
        {
            if (!cards.TryGetValue((parent, m), out var b)) cards[(parent, m)] = b = new OgMeshBuilder();
            return b;
        }

        static void FlushCards()
        {
            foreach (var kv in cards)
            {
                if (kv.Value.VertexCount == 0) continue;
                var (parent, mat) = kv.Key;
                var go = new GameObject("OG_" + mat.name.Replace("M_OG_", ""));
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one; // meshes are in world space; parents are unscaled
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh(go.name);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                bool flat = mat == M.litter || mat == M.hole || mat == M.glass || mat == M.cobweb;
                mr.shadowCastingMode = flat ? ShadowCastingMode.Off : ShadowCastingMode.On;
                go.isStatic = true;
                IgnoreNav(go.transform);
            }
            cards.Clear();
        }

        static void IgnoreNav(Transform t)
        {
            var mod = t.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
