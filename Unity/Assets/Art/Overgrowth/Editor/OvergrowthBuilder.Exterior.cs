// Exterior: ground around the mansion, the sunken exit yard's retaining walls, an overgrown path leading
// out, an iron fence with a broken gate, garden weeds/bushes, facade ivy, and an "endless" forest:
//   near ring  = real tree GameObjects (static, trunk colliders, shadows)
//   far ring   = ForestInstancer (GPU instanced, no shadows)
//   horizon    = 3 cylindrical treeline-silhouette rings that fog/skybox take over from.
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Overgrowth.EditorTools
{
    public static partial class OvergrowthBuilder
    {
        const float FenceOffset = 14f;   // fence distance from the house walls
        const float NearForest = 40f;    // trees closer than this (from the walls) are GameObjects
        const float ForestRadius = 125f; // instanced trees out to here (from the house centre)
        const float GroundRadius = 215f;

        static Rect? yard;
        static float yardFloorY;
        static readonly List<Vector3> path = new List<Vector3>();
        static Rect fence;
        static float noiseX, noiseZ;

        static void BuildExterior(Transform house)
        {
            var ext = Group("Exterior", house);
            IgnoreNav(ext);
            noiseX = Rand(0, 1000); noiseZ = Rand(0, 1000);
            path.Clear();

            FindYard(house);
            var fr = foot;
            if (yard != null) fr = Encapsulate(fr, yard.Value);
            fence = Rect.MinMaxRect(fr.xMin - FenceOffset, fr.yMin - FenceOffset, fr.xMax + FenceOffset, fr.yMax + FenceOffset);

            BuildPath();
            BuildGround(ext);
            if (yard != null) BuildYardWalls(ext);
            BuildPathMesh(ext);
            BuildFence(ext);
            BuildGarden(ext);
            FacadeIvy(ext);
            BuildForest(ext);
            BuildTreeline(ext);
        }

        // ---------------------------------------------------------------- terrain height
        static float DistFromHouse(float x, float z)
        {
            float dx = Mathf.Max(foot.xMin - x, 0, x - foot.xMax);
            float dz = Mathf.Max(foot.yMin - z, 0, z - foot.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Ground surface height. Flat around the house and garden, rolling and rising further out.</summary>
        static float GroundH(float x, float z)
        {
            float d = DistFromHouse(x, z);
            float ramp = Mathf.SmoothStep(0, 1, (d - FenceOffset - 4f) / 30f);
            float roll = (Mathf.PerlinNoise(noiseX + x * 0.025f, noiseZ + z * 0.025f) * 2f - 0.7f) * 3f
                       + (Mathf.PerlinNoise(noiseX + x * 0.09f, noiseZ - z * 0.09f) - 0.5f) * 0.8f;
            float rise = Mathf.Max(0, d - 60f) * 0.06f; // hides the edge of the world behind higher ground
            return groundY - 0.05f + ramp * roll + rise;
        }

        // ---------------------------------------------------------------- yard
        static void FindYard(Transform house)
        {
            yard = null;
            foreach (var r in house.GetComponentsInChildren<Renderer>())
            {
                if (r.name.IndexOf("Yard", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var b = r.bounds;
                if (b.max.y > groundY - 0.8f || b.size.x < 1 || b.size.z < 1) continue;
                yard = yard == null ? Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z) : Encapsulate(yard.Value, Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
                yardFloorY = b.max.y;
            }
            if (yard != null) Debug.Log("[Overgrowth] Exit yard " + yard.Value + " floor " + yardFloorY);
        }

        static Rect Encapsulate(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        static void BuildYardWalls(Transform ext)
        {
            var y = yard.Value;
            float bottom = yardFloorY - 0.3f, top = groundY + 0.45f, h = top - bottom;
            var g = Group("ExitYard_Walls", ext);
            // Each yard edge that isn't against the house gets a mossy retaining wall just outside it.
            void Edge(Vector3 a, Vector3 b, Vector3 outward)
            {
                var mid = (a + b) / 2;
                if (InFoot(mid + outward * 0.5f, 0.3f)) return;
                float len = Vector3.Distance(a, b) + 0.8f;
                var size = Mathf.Abs(outward.x) > 0.5f ? new Vector3(0.4f, h, len) : new Vector3(len, h, 0.4f);
                StoneBox("RetainingWall", g, new Vector3(mid.x, bottom + h / 2, mid.z) + outward * 0.2f, size, Quaternion.identity, true);
            }
            Edge(new Vector3(y.xMin, 0, y.yMin), new Vector3(y.xMax, 0, y.yMin), Vector3.back);
            Edge(new Vector3(y.xMin, 0, y.yMax), new Vector3(y.xMax, 0, y.yMax), Vector3.forward);
            Edge(new Vector3(y.xMin, 0, y.yMin), new Vector3(y.xMin, 0, y.yMax), Vector3.left);
            Edge(new Vector3(y.xMax, 0, y.yMin), new Vector3(y.xMax, 0, y.yMax), Vector3.right);

            // Ivy over the yard walls, weeds in the corners.
            var gb = Cards(g, M.grass);
            for (int i = 0; i < 18; i++)
                gb.Tuft(new Vector3(Rand(y.xMin + 0.1f, y.xMax - 0.1f), yardFloorY, Rand(y.yMin + 0.1f, y.yMax - 0.1f)), Rand(0.3f, 0.7f), Rand(0.25f, 0.6f), Rand(0, 360));
            var lit = Cards(g, M.litter);
            for (int i = 0; i < 4; i++)
                lit.Flat(new Vector3(Rand(y.xMin + 0.5f, y.xMax - 0.5f), yardFloorY + 0.006f + i * 0.0015f, Rand(y.yMin + 0.5f, y.yMax - 0.5f)), Rand(0.8f, 1.5f), Rand(0.8f, 1.5f), Rand(0, 360));
        }

        static GameObject StoneBox(string name, Transform parent, Vector3 pos, Vector3 size, Quaternion rot, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var b = new OgMeshBuilder();
            foreach (var (n, u, w) in OgMeshBuilder.Faces)
            {
                Vector3 P(float su, float sw) => n * 0.5f + u * (su * 0.5f) + w * (sw * 0.5f);
                Color C(Vector3 local)
                {
                    float y = go.transform.TransformPoint(local).y;
                    return new Color(Mathf.Abs(n.y) < 0.5f ? 1 : 0, 0.2f, Mathf.Clamp01(1 - (y - groundY) / 1.5f), 0);
                }
                var (uvMin, uvMax) = MetreUV(go.transform, P(-1, -1), u, w);
                b.Quad(P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), uvMin, uvMax, C(P(-1, -1)), C(P(1, -1)), C(P(1, 1)), C(P(-1, 1)));
            }
            go.GetComponent<MeshFilter>().sharedMesh = b.ToMesh("OG_Stone_" + name);
            go.GetComponent<MeshRenderer>().sharedMaterial = M.stone;
            go.isStatic = true;
            return go;
        }

        // ---------------------------------------------------------------- path
        static void BuildPath()
        {
            Vector3 start, dir;
            if (yard != null)
            {
                // Leave from the yard edge facing away from the house.
                var y = yard.Value;
                var away = new Vector2(y.center.x - foot.center.x, y.center.y - foot.center.y);
                if (Mathf.Abs(away.x) > Mathf.Abs(away.y)) dir = new Vector3(Mathf.Sign(away.x), 0, 0);
                else dir = new Vector3(0, 0, Mathf.Sign(away.y));
                var c = new Vector3(y.center.x, 0, y.center.y);
                start = c + Vector3.Scale(dir, new Vector3(y.width / 2 + 0.9f, 0, y.height / 2 + 0.9f));
            }
            else
            {
                dir = Vector3.back;
                start = new Vector3(foot.center.x, 0, foot.yMin - 0.6f);
            }
            var heading = dir;
            var p = start;
            for (int i = 0; i < 48; i++)
            {
                path.Add(p);
                heading = Quaternion.Euler(0, Rand(-14, 14), 0) * heading;
                heading = Vector3.Lerp(heading, dir, 0.25f).normalized;
                p += heading * 2f;
            }
        }

        static float DistToPath(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector3 a = path[i], b = path[i + 1];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(new Vector3(p.x - a.x, 0, p.z - a.z), ab) / ab.sqrMagnitude);
                var q = a + ab * t;
                best = Mathf.Min(best, new Vector2(p.x - q.x, p.z - q.z).magnitude);
            }
            return best;
        }

        static void BuildPathMesh(Transform ext)
        {
            var b = new OgMeshBuilder();
            var col = new Color(0, 0.05f, 0, 0);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector3 a = path[i], c = path[i + 1];
                var fwd0 = (c - a).normalized;
                var fwd1 = i + 2 < path.Count ? (path[i + 2] - c).normalized : fwd0;
                var l0 = Vector3.Cross(fwd0, Vector3.up) * (0.8f + Mathf.PerlinNoise(i * 0.7f, 3) * 0.3f);
                var l1 = Vector3.Cross(fwd1, Vector3.up) * (0.8f + Mathf.PerlinNoise((i + 1) * 0.7f, 3) * 0.3f);
                Vector3 Y(Vector3 v) => new Vector3(v.x, GroundH(v.x, v.z) + 0.035f, v.z);
                // Normal up: a = left-bottom when looking down with fwd as "up".
                b.Quad(Y(a + l0), Y(a - l0), Y(c - l1), Y(c + l1), Vector2.zero, Vector2.one, col, col, col, col);
            }
            var go = new GameObject("Path");
            go.transform.SetParent(ext, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("OG_Path");
            go.AddComponent<MeshRenderer>().sharedMaterial = M.path;
            go.isStatic = true;

            // Weeds along its edges.
            var gb = Cards(ext, M.grass);
            for (int i = 0; i < path.Count * 5; i++)
            {
                var a = path[rng.Next(path.Count)];
                var side = Quaternion.Euler(0, Rand(0, 360), 0) * Vector3.forward;
                var p = a + side * Rand(0.8f, 1.6f);
                if (DistToPath(p) < 0.75f || InFoot(p, 0.4f) || InYard(p, 0.3f)) continue;
                gb.Tuft(new Vector3(p.x, GroundH(p.x, p.z), p.z), Rand(0.4f, 0.9f), Rand(0.35f, 0.8f), Rand(0, 360));
            }
        }

        static bool InYard(Vector3 p, float margin) =>
            yard != null && p.x > yard.Value.xMin - margin && p.x < yard.Value.xMax + margin && p.z > yard.Value.yMin - margin && p.z < yard.Value.yMax + margin;

        // ---------------------------------------------------------------- ground
        static List<float> AxisLines(float lo, float hi, float centre, params float[] exact)
        {
            var xs = new List<float>(exact);
            for (float x = lo - 20; x <= hi + 20; x += 1f) xs.Add(x);
            float step = 2f;
            for (float x = lo - 22; x > centre - GroundRadius; x -= step, step *= 1.18f) xs.Add(x);
            xs.Add(centre - GroundRadius);
            step = 2f;
            for (float x = hi + 22; x < centre + GroundRadius; x += step, step *= 1.18f) xs.Add(x);
            xs.Add(centre + GroundRadius);
            xs.Sort();
            var res = new List<float>();
            foreach (var x in xs) if (res.Count == 0 || x - res[res.Count - 1] > 0.05f) res.Add(x);
            return res;
        }

        static void BuildGround(Transform ext)
        {
            var yr = yard ?? new Rect(foot.center, Vector2.zero);
            var xs = AxisLines(foot.xMin, foot.xMax, foot.center.x, foot.xMin, foot.xMax, yr.xMin, yr.xMax);
            var zs = AxisLines(foot.yMin, foot.yMax, foot.center.y, foot.yMin, foot.yMax, yr.yMin, yr.yMax);
            var v = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector2>();
            foreach (var z in zs)
                foreach (var x in xs)
                {
                    v.Add(new Vector3(x, GroundH(x, z), z));
                    cols.Add(new Color(0, 0.25f, 0, 0));
                    uvs.Add(new Vector2(x, z));
                }
            var tris = new List<int>();
            int nx = xs.Count;
            for (int j = 0; j + 1 < zs.Count; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    var c = new Vector3((xs[i] + xs[i + 1]) / 2, 0, (zs[j] + zs[j + 1]) / 2);
                    if (InFoot(c, 0) || InYard(c, 0)) continue; // the house and the sunken yard punch through
                    int a = j * nx + i;
                    tris.AddRange(new[] { a, a + nx, a + 1, a + 1, a + nx, a + nx + 1 });
                }
            var mesh = new Mesh { name = "OG_Ground", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh = OvergrowthAssets.SaveMesh(mesh, "Ground", "Meshes/Generated");

            var go = new GameObject("Ground");
            go.transform.SetParent(ext, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = M.ground;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
        }

        // ---------------------------------------------------------------- fence + gate
        static void BuildFence(Transform ext)
        {
            var b = new OgMeshBuilder();
            var white = Color.white;
            // Gate where the path crosses the fence line.
            Vector3 gate = Vector3.zero; int gateSide = -1;
            for (int i = 0; i + 1 < path.Count && gateSide < 0; i++)
            {
                bool inA = fence.Contains(new Vector2(path[i].x, path[i].z)), inB = fence.Contains(new Vector2(path[i + 1].x, path[i + 1].z));
                if (inA == inB) continue;
                var m = (path[i] + path[i + 1]) / 2;
                float dl = Mathf.Abs(m.x - fence.xMin), dr = Mathf.Abs(m.x - fence.xMax), db = Mathf.Abs(m.z - fence.yMin), df = Mathf.Abs(m.z - fence.yMax);
                float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, df));
                gateSide = min == db ? 0 : min == df ? 1 : min == dl ? 2 : 3;
                gate = gateSide < 2 ? new Vector3(m.x, 0, gateSide == 0 ? fence.yMin : fence.yMax) : new Vector3(gateSide == 2 ? fence.xMin : fence.xMax, 0, m.z);
            }

            var corners = new[] { new Vector3(fence.xMin, 0, fence.yMin), new Vector3(fence.xMax, 0, fence.yMin), new Vector3(fence.xMin, 0, fence.yMax), new Vector3(fence.xMax, 0, fence.yMax) };
            var sides = new[] { (corners[0], corners[1]), (corners[2], corners[3]), (corners[0], corners[2]), (corners[1], corners[3]) };
            for (int s = 0; s < 4; s++)
            {
                var (a, c) = sides[s];
                float len = Vector3.Distance(a, c);
                var along = (c - a).normalized;
                // collapsed stretches
                var gaps = new List<(float, float)>();
                for (int k = 0; k < 2; k++) { float g0 = Rand(2, len - 6); gaps.Add((g0, g0 + Rand(2, 5))); }
                if (s == gateSide) { float t = Vector3.Dot(gate - a, along); gaps.Add((t - 1.7f, t + 1.7f)); }
                bool InGap(float t) { foreach (var (g0, g1) in gaps) if (t > g0 && t < g1) return true; return false; }

                float runStart = -1;
                for (float t = 0; t <= len + 0.01f; t += 0.16f)
                {
                    bool gap = InGap(t);
                    var p = a + along * t;
                    float gy = GroundH(p.x, p.z);
                    if (!gap)
                    {
                        if (runStart < 0) runStart = t;
                        bool post = Mathf.Repeat(t, 2.56f) < 0.15f;
                        float lean = rng.NextDouble() < 0.06 ? Rand(8, 25) : Rand(-2, 2);
                        var rot = Quaternion.AngleAxis(lean, along);
                        float bh = post ? 1.9f : 1.7f, bw = post ? 0.07f : 0.025f;
                        b.Box(Matrix4x4.TRS(p + Vector3.up * (gy + bh / 2 - 0.1f), rot, new Vector3(bw, bh, bw)), white);
                    }
                    if ((gap || t + 0.16f > len) && runStart >= 0)
                    {
                        float t1 = gap ? t - 0.16f : t;
                        var r0 = a + along * runStart; var r1 = a + along * t1;
                        var mid = (r0 + r1) / 2;
                        float gy0 = GroundH(mid.x, mid.z);
                        foreach (float h in new[] { 0.25f, 1.45f })
                            b.Box(Matrix4x4.TRS(mid + Vector3.up * (gy0 + h), Quaternion.LookRotation(along), new Vector3(0.03f, 0.04f, t1 - runStart + 0.03f)), white);
                        runStart = -1;
                    }
                    // a bar or two lying in the grass in collapsed stretches
                    if (gap && s != gateSide && rng.NextDouble() < 0.08)
                        b.Box(Matrix4x4.TRS(p + Vector3.up * (gy + 0.03f) + Vector3.Cross(along, Vector3.up) * Rand(-0.6f, 0.6f), Quaternion.Euler(0, Rand(0, 360), 90), new Vector3(0.03f, 1.6f, 0.03f)), white);
                }
            }

            if (gateSide >= 0)
            {
                var along = gateSide < 2 ? Vector3.right : Vector3.forward;
                var outward = gateSide == 0 ? Vector3.back : gateSide == 1 ? Vector3.forward : gateSide == 2 ? Vector3.left : Vector3.right;
                var g = Group("Gate", ext);
                float gy = GroundH(gate.x, gate.z);
                foreach (float sgn in new[] { -1f, 1f })
                {
                    StoneBox("GatePillar", g, gate + along * sgn * 1.75f + Vector3.up * (gy + 1.15f), new Vector3(0.55f, 2.4f, 0.55f), Quaternion.identity, false);
                    StoneBox("GatePillarCap", g, gate + along * sgn * 1.75f + Vector3.up * (gy + 2.42f), new Vector3(0.68f, 0.14f, 0.68f), Quaternion.identity, false);
                }
                // Left leaf hangs open outward; right leaf has come off and lies in the weeds.
                var hingeL = gate - along * 1.45f + Vector3.up * gy;
                var open = Quaternion.AngleAxis(sgnAngle(outward, along) * 68f, Vector3.up);
                Leaf(b, hingeL, open * along, 1.4f, 0f);
                var lying = gate + along * 0.6f + outward * 1.6f + Vector3.up * (gy + 0.05f);
                Leaf(b, lying - along * 0.7f, (along + outward * 0.2f).normalized, 1.4f, 88f);
            }

            var mesh = OvergrowthAssets.SaveMesh(b.ToMesh("OG_Fence", false), "Fence", "Meshes/Generated");
            var go = new GameObject("Fence");
            go.transform.SetParent(ext, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = M.iron;
            go.isStatic = true;
        }

        static float sgnAngle(Vector3 outward, Vector3 along) => Vector3.Dot(Vector3.Cross(along, outward), Vector3.up) > 0 ? -1 : 1;

        /// <summary>Gate leaf: bars along `dir` from `hinge`, optionally tipped over by `tip` degrees.</summary>
        static void Leaf(OgMeshBuilder b, Vector3 hinge, Vector3 dir, float width, float tip)
        {
            var rot = Quaternion.AngleAxis(tip, dir);
            var white = Color.white;
            for (float t = 0.05f; t < width; t += 0.14f)
                b.Box(Matrix4x4.TRS(hinge + dir * t + rot * Vector3.up * 1.0f, rot, new Vector3(0.025f, 1.9f, 0.025f)), white);
            foreach (float h in new[] { 0.2f, 1.0f, 1.8f })
                b.Box(Matrix4x4.TRS(hinge + dir * (width / 2) + rot * Vector3.up * (h + 0.05f), rot * Quaternion.LookRotation(dir), new Vector3(0.03f, 0.04f, width)), white);
        }

        // ---------------------------------------------------------------- garden + facade
        static void BuildGarden(Transform ext)
        {
            var g = Group("Garden", ext);
            var gb = Cards(g, M.grass);
            var lb = Cards(g, M.leaves);
            // Tall weeds, densest against the walls and along the fence.
            for (int i = 0; i < 2600; i++)
            {
                var p = new Vector3(Rand(fence.xMin - 20, fence.xMax + 20), 0, Rand(fence.yMin - 20, fence.yMax + 20));
                float d = DistFromHouse(p.x, p.z);
                if (d < 0.15f || InYard(p, 0.4f) || DistToPath(p) < 0.8f) continue;
                float keep = d < 2.5f ? 0.9f : d < FenceOffset ? 0.45f : 0.25f;
                if (rng.NextDouble() > keep) continue;
                float scale = d < FenceOffset + 1 ? 1f : 0.8f;
                gb.Tuft(new Vector3(p.x, GroundH(p.x, p.z), p.z), Rand(0.45f, 1.0f) * scale, Rand(0.35f, 1.0f) * scale, Rand(0, 360));
            }
            // Bushes: balls of crossed leaf cards.
            for (int i = 0; i < 34; i++)
            {
                var p = new Vector3(Rand(fence.xMin + 1, fence.xMax - 1), 0, Rand(fence.yMin + 1, fence.yMax - 1));
                float d = DistFromHouse(p.x, p.z);
                if (d < 0.6f || InYard(p, 1f) || DistToPath(p) < 1.6f) continue;
                Bush(lb, new Vector3(p.x, GroundH(p.x, p.z), p.z), Rand(0.6f, 1.3f));
            }
            // A few old garden trees, some dead.
            int placed = 0;
            for (int i = 0; i < 40 && placed < 5; i++)
            {
                var p = new Vector3(Rand(fence.xMin + 2, fence.xMax - 2), 0, Rand(fence.yMin + 2, fence.yMax - 2));
                float d = DistFromHouse(p.x, p.z);
                if (d < 5f || d > FenceOffset - 2 || InYard(p, 3) || DistToPath(p) < 3f) continue;
                p.y = GroundH(p.x, p.z) - 0.05f;
                TreeObject(g, OvergrowthAssets.Broadleaf, rng.NextDouble() < 0.5 ? new[] { M.bark } : new[] { M.bark, M.leaves }, p, Rand(0.8f, 1.2f), true);
                placed++;
            }
        }

        static void Bush(OgMeshBuilder b, Vector3 basePt, float r)
        {
            for (int k = 0; k < 5; k++)
            {
                var c = basePt + new Vector3(Rand(-0.3f, 0.3f) * r, r * Rand(0.5f, 0.9f), Rand(-0.3f, 0.3f) * r);
                var rot = Quaternion.Euler(Rand(-25, 25), k * 36 + Rand(-10, 10), Rand(-25, 25));
                var right = rot * Vector3.right * r;
                var up = rot * Vector3.up * r;
                b.Card(c - right - up, c + right - up, c + right + up, c - right + up, Vector2.zero, Vector2.one, 0.3f, 1f);
            }
        }

        static void FacadeIvy(Transform ext)
        {
            var ib = Cards(Group("FacadeIvy", ext), M.ivy);
            float maxH = roofTopY - groundY;
            for (int side = 0; side < 4; side++)
            {
                WallFrame(foot, side, out var origin, out var along, out var inward, out float len);
                // inward points into the house; we work from outside.
                for (float t = Rand(0.5f, 3f); t < len - 0.5f; t += Rand(3f, 6.5f))
                {
                    var from = origin + along * t - inward * 2f + Vector3.up * (groundY + 1.0f);
                    if (!phys.Raycast(from, inward, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float w = Rand(1.4f, 3.8f);
                    float h = Rand(2.2f, Mathf.Max(2.5f, maxH * 0.85f));
                    WallIvy(ib, hit.point - inward * 0.02f, along, -inward, w, groundY - 0.05f, h, false);
                    if (rng.NextDouble() < 0.4) // a second, narrower runner climbing higher
                        WallIvy(ib, hit.point - inward * 0.03f + along * Rand(-1f, 1f), along, -inward, w * 0.5f, groundY - 0.05f, Mathf.Min(maxH, h * 1.4f), false);
                }
            }
        }

        // ---------------------------------------------------------------- forest
        static GameObject TreeObject(Transform parent, Mesh mesh, Material[] mats, Vector3 pos, float scale, bool collider)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(Rand(-3, 3), Rand(0, 360), Rand(-3, 3)));
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collider)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.35f; cap.height = 5f; cap.center = new Vector3(0, 2.5f, 0);
            }
            go.isStatic = true;
            return go;
        }

        static void BuildForest(Transform ext)
        {
            var near = Group("Forest_Near", ext);
            var farGo = Group("Forest_Far", ext);
            var inst = farGo.gameObject.AddComponent<Blink.Overgrowth.ForestInstancer>();
            var batches = new Dictionary<(int, int, int, bool), Blink.Overgrowth.ForestInstancer.Batch>();
            var centre = new Vector3(foot.center.x, 0, foot.center.y);
            const float cell = 5.2f;
            int nearCount = 0, farCount = 0;
            for (float x = centre.x - ForestRadius; x < centre.x + ForestRadius; x += cell)
                for (float z = centre.z - ForestRadius; z < centre.z + ForestRadius; z += cell)
                {
                    var p = new Vector3(x + Rand(0, cell), 0, z + Rand(0, cell));
                    if (new Vector2(p.x - centre.x, p.z - centre.z).magnitude > ForestRadius) continue;
                    if (fence.Contains(new Vector2(p.x, p.z)) || DistToPath(p) < 3.2f) continue;
                    float d = DistFromHouse(p.x, p.z);
                    if (d < FenceOffset + 2.5f) continue;
                    if (rng.NextDouble() < 0.12) continue; // clearings
                    p.y = GroundH(p.x, p.z) - 0.1f;

                    double roll = rng.NextDouble();
                    int kind = roll < 0.82 ? rng.Next(3) : 3; // 0-2 pines, 3 broadleaf
                    bool dead = kind == 3 && rng.NextDouble() < 0.35;
                    var mesh = kind < 3 ? OvergrowthAssets.Pines[kind] : OvergrowthAssets.Broadleaf;
                    var mats = dead ? new[] { M.bark } : new[] { M.bark, kind < 3 ? M.pine : M.leaves };
                    float scale = kind < 3 ? Rand(0.7f, 1.3f) : Rand(0.9f, 1.4f);

                    if (d < NearForest)
                    {
                        TreeObject(near, mesh, mats, p, scale, true);
                        nearCount++;
                        continue;
                    }
                    var key = (Mathf.FloorToInt(p.x / 60f), Mathf.FloorToInt(p.z / 60f), kind, dead);
                    if (!batches.TryGetValue(key, out var bt) || bt.matrices.Count >= 500)
                    {
                        bt = new Blink.Overgrowth.ForestInstancer.Batch { mesh = mesh, materials = mats, castShadows = false, bounds = new Bounds(p, Vector3.zero) };
                        batches[key] = bt;
                        inst.batches.Add(bt);
                    }
                    bt.matrices.Add(Matrix4x4.TRS(p, Quaternion.Euler(Rand(-3, 3), Rand(0, 360), Rand(-3, 3)), Vector3.one * scale));
                    bt.bounds.Encapsulate(new Bounds(p + Vector3.up * 11f * scale, new Vector3(9, 24, 9) * scale));
                    farCount++;
                }
            Debug.Log($"[Overgrowth] Forest: {nearCount} near trees (GameObjects), {farCount} instanced in {inst.batches.Count} batches");
        }

        static void BuildTreeline(Transform ext)
        {
            var b = new OgMeshBuilder();
            var centre = new Vector3(foot.center.x, 0, foot.center.y);
            var radii = new[] { 128f, 158f, 195f };
            var heights = new[] { 26f, 34f, 44f };
            const int segs = 72;
            for (int r = 0; r < radii.Length; r++)
            {
                float rad = radii[r], h = heights[r], u0 = Rand(0, 1);
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2 / segs, a1 = (i + 1) * Mathf.PI * 2 / segs;
                    var p0 = centre + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * rad;
                    var p1 = centre + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * rad;
                    p0.y = GroundH(p0.x, p0.z) - 3f; p1.y = GroundH(p1.x, p1.z) - 3f;
                    float uA = u0 + rad * a0 / 70f, uB = u0 + rad * a1 / 70f; // ~70 m of treeline per texture repeat
                    // Front faces the house (normal toward the centre).
                    b.Card(p1, p0, p0 + Vector3.up * h, p1 + Vector3.up * h, new Vector2(uB, 0), new Vector2(uA, 1), 0, 0);
                }
            }
            var go = new GameObject("Treeline");
            go.transform.SetParent(ext, false);
            go.AddComponent<MeshFilter>().sharedMesh = OvergrowthAssets.SaveMesh(b.ToMesh("OG_Treeline"), "Treeline", "Meshes/Generated");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = M.treeline;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.isStatic = true;
        }
    }
}
