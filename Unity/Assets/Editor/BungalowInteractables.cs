using System.Collections.Generic;
using Blink.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blink.EditorTools
{
    /// <summary>
    /// Makes bungalow furniture usable. Each prop is one welded mesh, so this cuts the door / drawer / lid fronts out
    /// of it (triangles clipped to a box, UVs and lightmap UVs kept) into moving children with <see cref="Openable"/>,
    /// adds a dark inside where the model is solid, and a <see cref="HidingSpot"/> where you can climb in.
    /// Called by <see cref="BungalowPropsPlacer"/> on each prefab before it's saved, so every placed instance gets it
    /// without touching the scene. (GothicCommode_01's drawers come split from Blender: BungalowPropsPlacer.SetupDrawers.) Coordinates are model space (Unity): +z is the front, y 0 the floor.
    /// </summary>
    public static class BungalowInteractables
    {
        const string MeshDir = "Assets/Art/Environment/Bungalow/Props/Prefabs/Meshes";
        const string InteriorMatPath = "Assets/Art/Environment/Bungalow/Materials/M_Interior.mat";

        enum Kind { HingeL, HingeR, Drawer, Lid, Drop }

        /// <summary>Triangles whose centroid lies within [lo, hi] on the depth axis (z; y for a lid) are clipped to
        /// the box on the other two axes; the inside becomes this part.</summary>
        struct Part
        {
            public string name, noun;
            public Kind kind;
            public Vector3 lo, hi;
            public Vector3? hinge;   // default: the outer front edge (doors) / back bottom edge (lid) of what was cut
            public float back;       // >0: the cut is a bare skin off a solid carcass; add a back face this far behind
            public float angle;      // 0 = default (100 doors, 75 lids)
        }

        class Spec
        {
            public string noun;
            public Part[] parts = new Part[0];
            public Vector3 inLo, inHi;            // dark inward box, if inHi != inLo
            public bool inOpenTop;                // open face: top (lidded chest) instead of front
            public bool hide;
            public string verb = "Hide in";
            public Vector3 eye, exit;
            public float hopHeight = 0.35f, maxYaw = 60f;
        }

        static Part Door(string name, Kind k, float x0, float y0, float z0, float x1, float y1, float z1, float back = 0f, string noun = null) =>
            new Part { name = name, kind = k, lo = new Vector3(x0, y0, z0), hi = new Vector3(x1, y1, z1), back = back, noun = noun };

        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            ["Wardrobe_Painted"] = new Spec
            {
                noun = "wardrobe",
                parts = new[]
                {
                    // the left leaf is modelled 14 degrees ajar; it keeps that as its closed pose
                    new Part { name = "Door_L", kind = Kind.HingeL, lo = new Vector3(-0.76f, 0.11f, 0.262f), hi = new Vector3(-0.02f, 1.91f, 0.5f), hinge = new Vector3(-0.74f, 0f, 0.265f) },
                    Door("Door_R", Kind.HingeR, 0.0f, 0.11f, 0.262f, 0.76f, 1.91f, 0.5f),
                    Door("Carcass_Front", Kind.Drop, -0.75f, 0.1f, 0.255f, 0.75f, 1.92f, 0.262f),
                },
                inLo = new Vector3(-0.73f, 0.1f, -0.25f), inHi = new Vector3(0.73f, 1.91f, 0.26f),
                hide = true, eye = new Vector3(0f, 1.45f, 0.02f), exit = new Vector3(0f, 0f, 0.95f),
            },
            ["painted_wooden_cabinet_02"] = new Spec
            {
                noun = "cupboard",
                parts = new[] { Door("Door", Kind.HingeL, -0.47f, 0.03f, 0.17f, 0.47f, 0.88f, 0.4f) },
                hide = true, eye = new Vector3(0f, 0.62f, -0.05f), exit = new Vector3(0f, 0f, 0.9f), hopHeight = 0.15f,
            },
            ["painted_wooden_cabinet"] = new Spec
            {
                noun = "cupboard",
                parts = new[]
                {
                    Door("Drawer_L", Kind.Drawer, -0.53f, 0.9f, -0.2f, -0.02f, 1.12f, 0.35f, noun: "drawer"),
                    Door("Drawer_R", Kind.Drawer, 0.02f, 0.9f, -0.2f, 0.54f, 1.12f, 0.35f, noun: "drawer"),
                    Door("Door_L", Kind.HingeL, -0.57f, 0.14f, 0.17f, -0.005f, 0.89f, 0.35f),
                    Door("Door_R", Kind.HingeR, -0.005f, 0.14f, 0.17f, 0.58f, 0.89f, 0.35f),
                },
            },
            ["SinkUnit"] = new Spec
            {
                noun = "cupboard",
                parts = new[]
                {
                    Door("Door_L", Kind.HingeL, -0.29f, 0.12f, 0.27f, 0.0f, 0.82f, 0.33f, 0.02f),
                    Door("Door_R", Kind.HingeR, 0.0f, 0.12f, 0.27f, 0.29f, 0.82f, 0.33f, 0.02f),
                },
                inLo = new Vector3(-0.28f, 0.1f, -0.25f), inHi = new Vector3(0.28f, 0.84f, 0.275f),
            },
            ["Fridge_Retro"] = new Spec
            {
                noun = "fridge",
                parts = new[] { Door("Door", Kind.HingeR, -0.33f, 0.12f, 0.27f, 0.33f, 1.52f, 0.45f, 0.02f) }, // handle is on -x
                inLo = new Vector3(-0.29f, 0.15f, -0.27f), inHi = new Vector3(0.29f, 1.48f, 0.27f),
            },
            ["MirrorCabinet"] = new Spec
            {
                noun = "cabinet",
                parts = new[] { Door("Door", Kind.HingeR, -0.28f, -0.33f, 0.12f, 0.28f, 0.33f, 0.2f, 0.015f) },
                inLo = new Vector3(-0.26f, -0.31f, 0.01f), inHi = new Vector3(0.26f, 0.31f, 0.125f),
            },
            ["wooden_crate_01"] = Chest(0.41f, 0.20f, 0.35f, 0.04f),
            ["wooden_crate_02"] = Chest(0.58f, 0.26f, 0.46f, 0.03f),
            ["GothicBed_01"] = new Spec
            {
                noun = "bed", verb = "Hide under", hide = true,
                eye = new Vector3(0f, 0.2f, 0f), exit = new Vector3(0f, 0f, 1.5f), hopHeight = 0f, maxYaw = 80f, // out past the foot (+z)
            },
        };

        /// <summary>Lidded chest: half-extents x/z, height h, lid thickness t. Climb in under the lid.</summary>
        static Spec Chest(float hx, float hz, float h, float t) => new Spec
        {
            noun = "chest",
            parts = new[] { new Part { name = "Lid", kind = Kind.Lid, lo = new Vector3(-hx - 0.05f, h - t - 0.005f, -hz - 0.05f), hi = new Vector3(hx + 0.05f, h + 0.02f, hz + 0.05f) } },
            inLo = new Vector3(-hx + 0.03f, 0.03f, -hz + 0.03f), inHi = new Vector3(hx - 0.03f, h - t - 0.006f, hz - 0.03f), inOpenTop = true,
            hide = true, eye = new Vector3(0f, h - t - 0.06f, 0f), exit = new Vector3(0f, 0f, hz + 0.7f), hopHeight = 0.25f,
        };

        /// <summary>Adds the parts / inside / hiding spot for <paramref name="model"/> to the prefab being built.
        /// No-op for models without a spec.</summary>
        public static bool Wire(GameObject go, string model)
        {
            if (!Specs.TryGetValue(model, out var spec)) return false;
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (spec.parts.Length > 0 && mf != null && mf.sharedMesh != null)
            {
                var src = mf.sharedMesh;
                var cut = Cut(src, spec.parts);
                mf.sharedMesh = Save(ToMesh(cut.body, model + "_Body", Vector3.zero, Quaternion.identity, 0f), model + "_Body");
                for (int i = 0; i < spec.parts.Length; i++)
                {
                    var p = spec.parts[i];
                    if (p.kind == Kind.Drop || cut.parts[i].verts.Count == 0) continue;
                    MakePart(go.transform, mr, model, p, spec.noun, cut.parts[i], src.subMeshCount);
                }
            }
            if (spec.inHi != spec.inLo) Interior(go.transform, model, spec);
            if (spec.hide)
            {
                var h = go.AddComponent<HidingSpot>();
                h.noun = spec.noun;
                h.verb = spec.verb;
                h.eye = spec.eye;
                h.exit = spec.exit;
                h.hopHeight = spec.hopHeight;
                h.maxYaw = spec.maxYaw;
            }
            return true;
        }

        // ------------------------------------------------------------------ the cut
        struct V
        {
            public Vector3 p, n;
            public Vector4 t;
            public Vector2 uv0, uv1;

            public static V Lerp(V a, V b, float k) => new V
            {
                p = Vector3.Lerp(a.p, b.p, k),
                n = Vector3.Lerp(a.n, b.n, k).normalized,
                t = new Vector4(Mathf.Lerp(a.t.x, b.t.x, k), Mathf.Lerp(a.t.y, b.t.y, k), Mathf.Lerp(a.t.z, b.t.z, k), a.t.w),
                uv0 = Vector2.Lerp(a.uv0, b.uv0, k),
                uv1 = Vector2.Lerp(a.uv1, b.uv1, k),
            };
        }

        // ponytail: every output triangle gets its own 3 vertices (no welding); fine at these prop sizes (<10k verts).
        class Builder
        {
            public readonly List<V> verts = new List<V>();
            public readonly List<int>[] subs;
            public Builder(int subCount)
            {
                subs = new List<int>[subCount];
                for (int i = 0; i < subCount; i++) subs[i] = new List<int>();
            }

            public void Add(List<V> poly, int sub)
            {
                if (poly.Count < 3) return;
                int b = verts.Count;
                verts.AddRange(poly);
                for (int i = 1; i + 1 < poly.Count; i++) { subs[sub].Add(b); subs[sub].Add(b + i); subs[sub].Add(b + i + 1); }
            }
        }

        static (Builder body, Builder[] parts) Cut(Mesh m, Part[] parts)
        {
            var pos = new List<Vector3>(); m.GetVertices(pos);
            var nrm = new List<Vector3>(); m.GetNormals(nrm);
            var tan = new List<Vector4>(); m.GetTangents(tan);
            var uv0 = new List<Vector2>(); m.GetUVs(0, uv0);
            var uv1 = new List<Vector2>(); m.GetUVs(1, uv1);
            V At(int i) => new V
            {
                p = pos[i],
                n = i < nrm.Count ? nrm[i] : Vector3.up,
                t = i < tan.Count ? tan[i] : new Vector4(1, 0, 0, 1),
                uv0 = i < uv0.Count ? uv0[i] : Vector2.zero,
                uv1 = i < uv1.Count ? uv1[i] : Vector2.zero,
            };

            var body = new Builder(m.subMeshCount);
            var outs = new Builder[parts.Length];
            for (int i = 0; i < parts.Length; i++) outs[i] = new Builder(m.subMeshCount);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var tri = m.GetTriangles(s);
                for (int i = 0; i < tri.Length; i += 3)
                    Route(new List<V> { At(tri[i]), At(tri[i + 1]), At(tri[i + 2]) }, s, 0, parts, outs, body);
            }
            return (body, outs);
        }

        /// <summary>Clip a polygon against parts[k]: the inside goes to that part, each outside piece on to the next part,
        /// and whatever no part claims to the body.</summary>
        static void Route(List<V> poly, int sub, int k, Part[] parts, Builder[] outs, Builder body)
        {
            if (k == parts.Length) { body.Add(poly, sub); return; }
            var p = parts[k];
            int depth = p.kind == Kind.Lid ? 1 : 2;
            float c = 0f;
            foreach (var v in poly) c += v.p[depth];
            c /= poly.Count;
            if (c < p.lo[depth] || c > p.hi[depth]) { Route(poly, sub, k + 1, parts, outs, body); return; }

            var inside = poly;
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == depth) continue;
                for (int side = 0; side < 2; side++)
                {
                    float val = side == 0 ? p.lo[axis] : p.hi[axis];
                    float sign = side == 0 ? 1f : -1f; // inside: (x - lo) >= 0, (hi - x) >= 0
                    Split(inside, axis, val, sign, out var inPart, out var outPart);
                    if (outPart.Count >= 3) Route(outPart, sub, k + 1, parts, outs, body);
                    inside = inPart;
                    if (inside.Count < 3) return;
                }
            }
            outs[k].Add(inside, sub);
        }

        static void Split(List<V> poly, int axis, float val, float sign, out List<V> inside, out List<V> outside)
        {
            inside = new List<V>(poly.Count + 2);
            outside = new List<V>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                V a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = (a.p[axis] - val) * sign, db = (b.p[axis] - val) * sign;
                if (da >= 0f) inside.Add(a);
                if (da <= 0f) outside.Add(a);
                if ((da > 0f && db < 0f) || (da < 0f && db > 0f))
                {
                    var x = V.Lerp(a, b, da / (da - db));
                    inside.Add(x);
                    outside.Add(x);
                }
            }
        }

        static Mesh ToMesh(Builder b, string name, Vector3 pivot, Quaternion rot, float back)
        {
            var inv = Quaternion.Inverse(rot);
            var verts = new List<V>(b.verts.Count);
            foreach (var v in b.verts)
            {
                var o = v;
                o.p = inv * (v.p - pivot);
                o.n = inv * v.n;
                var t = inv * (Vector3)v.t;
                o.t = new Vector4(t.x, t.y, t.z, v.t.w);
                verts.Add(o);
            }
            var subs = new List<int>[b.subs.Length];
            for (int s = 0; s < subs.Length; s++) subs[s] = new List<int>(b.subs[s]);
            if (back > 0f)
            {
                // a flipped copy behind the skin so the open door isn't paper thin from behind
                var off = inv * (Vector3.back * back);
                int n0 = verts.Count;
                for (int i = 0; i < n0; i++)
                {
                    var o = verts[i];
                    o.p += off;
                    o.n = -o.n;
                    verts.Add(o);
                }
                for (int s = 0; s < subs.Length; s++)
                {
                    var src = b.subs[s];
                    for (int i = 0; i < src.Count; i += 3) { subs[s].Add(src[i] + n0); subs[s].Add(src[i + 2] + n0); subs[s].Add(src[i + 1] + n0); }
                }
            }
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts.ConvertAll(v => v.p));
            m.SetNormals(verts.ConvertAll(v => v.n));
            m.SetTangents(verts.ConvertAll(v => v.t));
            m.SetUVs(0, verts.ConvertAll(v => v.uv0));
            m.SetUVs(1, verts.ConvertAll(v => v.uv1));
            m.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++) m.SetTriangles(subs[s], s);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ parts
        static void MakePart(Transform root, MeshRenderer srcR, string model, Part p, string noun, Builder b, int subCount)
        {
            var bounds = new Bounds(b.verts[0].p, Vector3.zero);
            foreach (var v in b.verts) bounds.Encapsulate(v.p);

            Vector3 pivot;
            var rot = Quaternion.identity;
            switch (p.kind)
            {
                case Kind.HingeL: pivot = p.hinge ?? new Vector3(bounds.min.x, 0f, bounds.max.z); break;
                case Kind.HingeR: pivot = p.hinge ?? new Vector3(bounds.max.x, 0f, bounds.max.z); break;
                case Kind.Lid:
                    pivot = p.hinge ?? new Vector3(bounds.center.x, bounds.min.y, bounds.min.z);
                    rot = Quaternion.Euler(0f, 0f, 90f); // local up = -x, so Openable's yaw lifts the lid about the back edge
                    break;
                default: pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z); break;
            }
            var mesh = Save(ToMesh(b, model + "_" + p.name, pivot, rot, p.back), model + "_" + p.name);

            var go = new GameObject(p.name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pivot;
            go.transform.localRotation = rot;
            GameObjectUtility.SetStaticEditorFlags(go, 0); // it moves
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = srcR.sharedMaterials;
            r.receiveGI = ReceiveGI.LightProbes;
            // a hair bigger than the cut, so the look ray reaches it before the carcass box behind it
            var col = go.AddComponent<BoxCollider>();
            col.center = mesh.bounds.center;
            col.size = mesh.bounds.size + Vector3.one * 0.02f;

            var o = go.AddComponent<Openable>();
            o.noun = p.noun ?? noun;
            switch (p.kind)
            {
                case Kind.HingeL: o.openAngle = -(p.angle > 0 ? p.angle : 100f); break;
                case Kind.HingeR: o.openAngle = p.angle > 0 ? p.angle : 100f; break;
                case Kind.Lid: o.openAngle = p.angle > 0 ? p.angle : 75f; break;
                case Kind.Drawer:
                    o.motion = Openable.Motion.Sliding;
                    o.slide = new Vector3(0f, 0f, Mathf.Max(0.2f, bounds.size.z * 0.75f));
                    break;
            }
        }

        /// <summary>Dark inward-facing box (front or top left open) so the solid carcass doesn't show through.</summary>
        static void Interior(Transform root, string model, Spec s)
        {
            Vector3 lo = s.inLo, hi = s.inHi;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) // a b c d counter-clockwise seen from inside
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); // Unity front faces wind clockwise
            }
            Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z);
            Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0));                       // back (faces +z)
            Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1));                       // floor (faces +y)
            Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0));                       // left (faces +x)
            Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1));                       // right (faces -x)
            if (s.inOpenTop) Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1));      // front (faces -z)
            else Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0));                  // top (faces -y)
            var m = new Mesh { name = model + "_Interior" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();

            var go = new GameObject("Interior");
            go.transform.SetParent(root, false);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            go.AddComponent<MeshFilter>().sharedMesh = Save(m, model + "_Interior");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = InteriorMaterial();
            r.receiveGI = ReceiveGI.LightProbes;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material InteriorMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(InteriorMatPath);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Interior" };
            m.SetColor("_BaseColor", new Color(0.07f, 0.055f, 0.045f));
            m.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(m, InteriorMatPath);
            return m;
        }

        static Mesh Save(Mesh m, string name)
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Art/Environment/Bungalow/Props/Prefabs", "Meshes");
            string path = MeshDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            existing.Clear();
            EditorUtility.CopySerialized(m, existing); // keep the GUID so prefabs and scenes stay linked
            Object.DestroyImmediate(m);
            return existing;
        }
    }
}
