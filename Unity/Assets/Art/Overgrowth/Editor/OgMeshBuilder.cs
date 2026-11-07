using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.Overgrowth.EditorTools
{
    /// <summary>Accumulates quads/boxes (world space) into one mesh with optional submeshes.</summary>
    public class OgMeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Color> c = new List<Color>();
        readonly List<int>[] tris;

        public OgMeshBuilder(int submeshes = 1)
        {
            tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) tris[i] = new List<int>();
        }

        public int VertexCount => v.Count;

        /// <summary>a,b,c,d = bottom-left, bottom-right, top-right, top-left as seen from the front (front = normal side).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector2 uvMin, Vector2 uvMax,
                         Color ca, Color cb, Color ccol, Color cd, int sub = 0)
        {
            var nrm = Vector3.Cross(d - a, b - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            n.Add(nrm); n.Add(nrm); n.Add(nrm); n.Add(nrm);
            uv.Add(uvMin); uv.Add(new Vector2(uvMax.x, uvMin.y)); uv.Add(uvMax); uv.Add(new Vector2(uvMin.x, uvMax.y));
            c.Add(ca); c.Add(cb); c.Add(ccol); c.Add(cd);
            tris[sub].AddRange(new[] { i, i + 2, i + 1, i + 2, i, i + 3 });
        }

        /// <summary>Card with wind weight 0 on the bottom edge and `topWeight` on the top edge (vertex colour A).</summary>
        public void Card(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector2 uvMin, Vector2 uvMax, float bottomWeight = 0, float topWeight = 1, int sub = 0)
        {
            var lo = new Color(0, 0, 0, bottomWeight);
            var hi = new Color(0, 0, 0, topWeight);
            Quad(a, b, cc, d, uvMin, uvMax, lo, lo, hi, hi, sub);
        }

        /// <summary>Upright card standing on `bottomCentre`, facing `facing` (horizontal).</summary>
        public void Upright(Vector3 bottomCentre, Vector3 facing, float width, float height, Vector2 uvMin, Vector2 uvMax, float topWeight = 1, int sub = 0)
        {
            var right = Vector3.Cross(facing, Vector3.up).normalized * (width / 2); // so Cross(up, right) == facing
            var up = Vector3.up * height;
            Card(bottomCentre - right, bottomCentre + right, bottomCentre + right + up, bottomCentre - right + up, uvMin, uvMax, 0, topWeight, sub);
        }

        /// <summary>Flat decal lying on a horizontal plane, normal up (or down when `down`).</summary>
        public void Flat(Vector3 centre, float sizeX, float sizeZ, float yawDeg, bool down = false, int sub = 0, float weight = 0)
        {
            var rot = Quaternion.Euler(0, yawDeg, 0);
            var ax = rot * Vector3.right * (sizeX / 2);
            var az = rot * Vector3.forward * (sizeZ / 2);
            var col = new Color(0, 0, 0, weight);
            if (!down) Quad(centre - ax - az, centre + ax - az, centre + ax + az, centre - ax + az, Vector2.zero, Vector2.one, col, col, col, col, sub);
            else Quad(centre - ax + az, centre + ax + az, centre + ax - az, centre - ax - az, Vector2.zero, Vector2.one, col, col, col, col, sub);
        }

        /// <summary>Tuft of `count` crossed upright cards around a vertical axis.</summary>
        public void Tuft(Vector3 bottom, float width, float height, float yawDeg, int count = 3, float topWeight = 1, int sub = 0)
        {
            for (int i = 0; i < count; i++)
            {
                var f = Quaternion.Euler(0, yawDeg + 180f * i / count, 0) * Vector3.forward;
                Upright(bottom, f, width, height, Vector2.zero, Vector2.one, topWeight, sub);
            }
        }

        /// <summary>Unit cube transformed by `m`, one vertex colour.</summary>
        public void Box(Matrix4x4 m, Color col, int sub = 0)
        {
            foreach (var face in Faces)
            {
                var (nn, u, w) = face;
                Vector3 P(float su, float sw) => m.MultiplyPoint3x4(nn * 0.5f + u * (su * 0.5f) + w * (sw * 0.5f));
                Quad(P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), Vector2.zero, Vector2.one, col, col, col, col, sub);
            }
        }

        /// <summary>Tapered tube between two points (tree trunks/branches).</summary>
        public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides, Color ca, Color cb, int sub = 0)
        {
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var fwd = Vector3.Cross(side, axis);
            float len = (b - a).magnitude;
            for (int i = 0; i < sides; i++)
            {
                float t0 = (float)i / sides * Mathf.PI * 2, t1 = (float)(i + 1) / sides * Mathf.PI * 2;
                Vector3 d0 = side * Mathf.Cos(t0) + fwd * Mathf.Sin(t0), d1 = side * Mathf.Cos(t1) + fwd * Mathf.Sin(t1);
                // Cross(axis, tangent) points outward, so going d0 -> d1 along the bottom edge faces out.
                Quad(a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb,
                     new Vector2((float)i / sides, 0), new Vector2((float)(i + 1) / sides, len), ca, ca, cb, cb, sub);
            }
        }

        public static readonly (Vector3 n, Vector3 u, Vector3 w)[] Faces = MakeFaces();

        static (Vector3, Vector3, Vector3)[] MakeFaces()
        {
            var dirs = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var res = new (Vector3, Vector3, Vector3)[6];
            for (int i = 0; i < 6; i++)
            {
                var nn = dirs[i];
                var w = Mathf.Abs(nn.y) > 0.5f ? Vector3.forward : Vector3.up;
                var u = Vector3.Cross(nn, w); // so that Cross(w, u) == n -> front faces outward
                res[i] = (nn, u, w);
            }
            return res;
        }

        public Mesh ToMesh(string name, bool tangents = true)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetColors(c);
            mesh.subMeshCount = tris.Length;
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
            mesh.RecalculateBounds();
            if (tangents) mesh.RecalculateTangents(); // foliage cards are normal-mapped; triplanar/Lit-untextured meshes skip it
            return mesh;
        }
    }
}
