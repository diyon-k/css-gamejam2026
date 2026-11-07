# Openable Drawers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The three drawers of the bungalow's chest of drawers (`GothicCommode_01`, used by `Bed_Dresser` and `Liv_Sideboard`) open and close when the player interacts with them.

**Architecture:**
- **Blender:** the prop build splits the Poly Haven drawer objects out as child meshes `<Model>_Drawer<N>`, so the FBX carries them as separate transforms.
- **Unity prefab build:** `BungalowPropsPlacer.BuildPrefabs` turns every `*_Drawer<N>` child into a sliding `Openable` (existing component) with its own collider, non-static and probe-lit.
- **Scene:** both instances pick this up through the prefab; then the lighting is rebaked.

**Tech Stack:**
- Blender 5.1 (bpy, headless): `tools/blender/bungalow_prop_models.py`, `bungalow_prop_build.py`.
- Unity 6000.6 URP: editor script `Editor/BungalowPropsPlacer.cs`, NUnit EditMode tests, MCP for Unity (`execute_code`, `run_tests`).

## Global Constraints

- Scope: `GothicCommode_01` only, empty drawers, no sounds or locks.
- The drawers slide `(0, 0, 0.30)` m in parent space (out of the front, +Z), over `seconds = 0.3`, with noun `"drawer"`.
- Each drawer collider's front face is at least 0.01 m in front of the body's box collider front (`m.hi[2]`).
- Drawers have no static flags, `receiveGI = LightProbes` and shadows on; the body keeps its furniture flags.
- Unity MCP code: C# 6 CodeDom, fully-qualified names, no top-level `using`. Check `EditorApplication.isPlaying` first. No `AssetDatabase.DeleteAsset`, no `safety_checks:false`. Bake synchronously; save the scene in the call that changes it.
- Don't touch the door work (`Scripts/World`, `BungalowDoorSetup.cs`, `BungalowGreyboxBuilder.cs`, the navmesh). Before baking, check that no other session is editing the lighting or baking.
- Commits: conventional commits, no AI attribution lines.

---

### Task 1: Split the commode's drawers out in the Blender prop build

**Files:**
- Modify: `tools/blender/bungalow_prop_models.py`: `ph()` (around lines 120–219) and the `MODELS` entry for `GothicCommode_01`.
- Output (regenerated): `Unity/Assets/Art/Environment/Bungalow/Props/GothicCommode_01.fbx` and the `GothicCommode_01` row in `Props/bungalow_prop_models.json`.

**Interfaces:**
- Produces: the FBX root `GothicCommode_01` with three child meshes `GothicCommode_01_Drawer1` (top) to `GothicCommode_01_Drawer3` (bottom). Each child's pivot is at its own bounds centre; the axes are the same as the root's (Unity +Z = front).

- [ ] **Step 1: Refactor `ph()` so the per-mesh work (material cells, dropped faces, decimation, atlas UV remap) is a helper, and add `split`**

```python
def ph(asset, tris, keys=None, objects=None, drop=(), name=None, recentre=True, split=()):
    """Import a Poly Haven FBX, join it, decimate to `tris`, remap each material's UVs into its atlas cell.
    keys: {material name substring -> cell key | "GLASS" | "SHADE" | None (delete)}; default cell = the asset.
    objects: only keep objects whose name starts with one of these. drop: object names to delete.
    split: objects whose name starts with one of these stay separate as child meshes <name>_Drawer1.. (top first),
    each pivoted at its own centre (openable drawers); the triangle budget is shared by triangle count."""
    # ... import, filter and flatten exactly as before, into `meshes` ...
    parts = [m for m in meshes if any(m.name.startswith(s) for s in split)]
    body = [m for m in meshes if m not in parts]
    count = {m: sum(len(p.vertices) - 2 for p in m.data.polygons) for m in meshes}
    total = max(1, sum(count.values()))
    budget = (lambda objs: int(round(tris * sum(count[m] for m in objs) / total))) if tris else (lambda objs: 0)
    o = _ph_mesh(join(body, name or asset), asset, keys, budget(body))
    parts = [_ph_mesh(p, asset, keys, budget([p])) for p in parts]
    if recentre:
        lo, hi = bounds(o)
        for p in parts:
            plo, phi = bounds(p)
            lo = Vector([min(lo[i], plo[i]) for i in range(3)])
            hi = Vector([max(hi[i], phi[i]) for i in range(3)])
        t = Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
        for m in [o] + parts:
            m.data.transform(t)
    parts.sort(key=lambda p: -(bounds(p)[0].z + bounds(p)[1].z))
    for i, p in enumerate(parts, 1):
        plo, phi = bounds(p)
        c = (plo + phi) / 2
        p.data.transform(Matrix.Translation(-c))
        p.location = c
        p.name = p.data.name = f"{name or asset}_Drawer{i}"
        lib.child(p, o)
    return o
```

`_ph_mesh(o, asset, keys, tris)` is the old body of `ph()` from `me = o.data` through the material-slot rewrite, returning `o` (unchanged code; it only works on `o`). The old final `centre_floor(o)` becomes the union-bounds recentre above. It gives the same result when there's no split, so every other model is unchanged.

- [ ] **Step 2: Register the split for the commode**

```python
    "GothicCommode_01": (_ph("GothicCommode_01", 1600, split=("GothicCommode_01_door",)), "floor", "box", "furniture"),
```

- [ ] **Step 3: Build only that model**

Run: `blender -b --factory-startup --python tools/blender/bungalow_prop_build.py -- GothicCommode_01`
Expected: `MODEL GothicCommode_01: tris=~1600 ...` and `FAILED: []`.

- [ ] **Step 4: Verify the FBX in Unity** (MCP `execute_code` after a refresh). List the FBX's `MeshFilter`s: the root plus `GothicCommode_01_Drawer1..3`, each about 1.0 × 0.26 × 0.54 m, at heights about 0.99 / 0.67 / 0.36 m. Also check that the materials still map to `M_Atlas_*` (the importer keeps its remaps).

### Task 2: Drawers become sliding `Openable`s in the prefab (test first)

**Files:**
- Create: `Unity/Assets/Tests/EditMode/DrawerPrefabTests.cs`.
- Modify: `Unity/Assets/Editor/BungalowPropsPlacer.cs`: in `BuildPrefabs()`, after the box collider is added and before `SaveAsPrefabAsset`.

**Interfaces:**
- Consumes: the FBX children `<Model>_Drawer<N>` from Task 1.
- Produces: `Props/Prefabs/GothicCommode_01.prefab` with three drawer children, each carrying `Blink.Interaction.Openable` (Sliding) and a `BoxCollider`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Blink.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blink.Tests.EditMode
{
    public class DrawerPrefabTests
    {
        const string Path = "Assets/Art/Environment/Bungalow/Props/Prefabs/GothicCommode_01.prefab";

        static Transform[] Drawers(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("_Drawer")).OrderBy(t => t.name).ToArray();

        [Test]
        public void Commode_HasThreeSlidingDrawers()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            Assert.NotNull(root, Path);
            var drawers = Drawers(root);
            Assert.AreEqual(3, drawers.Length);
            foreach (var d in drawers)
            {
                var o = d.GetComponent<Openable>();
                Assert.NotNull(o, d.name + " has no Openable");
                Assert.AreEqual(Openable.Motion.Sliding, o.motion, d.name);
                Assert.AreEqual("drawer", o.noun, d.name);
                Assert.That(Vector3.Distance(o.slide, new Vector3(0f, 0f, 0.30f)), Is.LessThan(1e-4f), d.name);
            }
        }

        [Test]
        public void DrawerColliders_StandProudOfTheBody()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var body = root.GetComponent<BoxCollider>();
            Assert.NotNull(body);
            float bodyFront = body.center.z + body.size.z * 0.5f;
            foreach (var d in Drawers(root))
            {
                var c = d.GetComponent<BoxCollider>();
                Assert.NotNull(c, d.name + " has no BoxCollider");
                float front = d.localPosition.z + c.center.z + c.size.z * 0.5f;
                Assert.GreaterOrEqual(front, bodyFront + 0.005f, d.name);
            }
        }

        [Test]
        public void Drawers_AreNotLightmapStatic()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            foreach (var d in Drawers(root))
            {
                Assert.AreEqual(0, (int)(GameObjectUtility.GetStaticEditorFlags(d.gameObject) & StaticEditorFlags.ContributeGI), d.name);
                Assert.AreEqual(ReceiveGI.LightProbes, d.GetComponent<MeshRenderer>().receiveGI, d.name);
            }
        }
    }
}
```

- [ ] **Step 2: Run it and check it fails.** MCP `run_tests` (EditMode, filter `DrawerPrefabTests`). Expected: `Commode_HasThreeSlidingDrawers` fails, with 0 drawers or no `Openable`.

- [ ] **Step 3: Implement it in `BuildPrefabs()`** (after the `if (m.collider == "box") {...}` block):

```csharp
                drawers += SetupDrawers(go, m);
```

plus a new method in the class:

```csharp
        static readonly System.Text.RegularExpressions.Regex DrawerName =
            new System.Text.RegularExpressions.Regex(@"_Drawer\d+$");
        const float DrawerPull = 0.30f;     // metres out of the front (+Z), about half a 0.54 m deep drawer
        const float DrawerSeconds = 0.3f;
        const float DrawerProud = 0.01f;    // the drawer collider sticks out past the body box, so the look ray finds it

        /// <summary>
        /// Every "&lt;Model&gt;_Drawer&lt;N&gt;" child (split out by the Blender build) becomes a sliding Openable with its
        /// own box collider, not static and lit by light probes, so it can move. Returns the number of drawers.
        /// </summary>
        static int SetupDrawers(GameObject go, Model m)
        {
            int n = 0;
            float bodyFront = m.hi != null && m.hi.Length == 3 ? m.hi[2] : float.NegativeInfinity;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!DrawerName.IsMatch(t.name)) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var r = t.GetComponent<MeshRenderer>();
                if (r != null) { r.receiveGI = ReceiveGI.LightProbes; r.shadowCastingMode = ShadowCastingMode.On; }
                var mf = t.GetComponent<MeshFilter>();
                var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.2f);
                var bc = t.gameObject.AddComponent<BoxCollider>();
                float front = t.localPosition.z + b.max.z;
                float extra = Mathf.Max(0f, bodyFront + DrawerProud - front);
                bc.center = b.center + new Vector3(0f, 0f, extra * 0.5f);
                bc.size = b.size + new Vector3(0f, 0f, extra);
                var o = t.gameObject.AddComponent<Blink.Interaction.Openable>();
                o.motion = Blink.Interaction.Openable.Motion.Sliding;
                o.noun = "drawer";
                o.slide = new Vector3(0f, 0f, DrawerPull);
                o.seconds = DrawerSeconds;
                n++;
            }
            return n;
        }
```

Also declare `int drawers = 0;` next to `int n = 0, boxes = 0;`, and add `, {drawers} drawers` to the return string.

- [ ] **Step 4: Rebuild the prefabs.** MCP `execute_code`: call `BungalowPropsPlacer.BuildAssets()` through reflection if `BuildPrefabs` is private, or the public `BuildAssets()`. Expected: the report includes `3 drawers`.

- [ ] **Step 5: Run the tests and check they pass.** MCP `run_tests` (EditMode, `DrawerPrefabTests`). Expected: 3/3 pass.

### Task 3: Scene check, renders and rebake

**Files:**
- Scene: `Unity/Assets/Scenes/House_Bungalow.unity` (prefab instances update; no code).

- [ ] **Step 1:** Check that `Bed_Dresser` and `Liv_Sideboard` in the scene each have the three drawer children with an `Openable` (prefab instances; check for overrides that could hide them).
- [ ] **Step 2:** Raycast from a player-eye point 1.2 m in front of each drawer front of `Bed_Dresser`, using the `Interactor.FindTarget` logic (sorted hits, first non-trigger decides). Expected: it resolves to that drawer's `Openable`.
- [ ] **Step 3:** Render `Bed_Dresser` from 1.6 m eye height: closed, Drawer1 open, and all open. Set `localPosition = closed + slide` temporarily and restore it. Look for gaps, z-fighting and parts poking through.
- [ ] **Step 4:** Check the other sessions' lighting activity (timestamps of `LightingData.asset`, the builder, `bungalow_decals.py`). Then Build Lighting and a synchronous bake, saving the scene in the same call. Re-render the closed view.

### Task 4: Docs and commit

- [ ] **Step 1:** Add a "Drawers" line to `docs/bungalow/SPEC.md`, in the props section (`Bed_Dresser` and `Liv_Sideboard` drawers open; `split` in `ph()`; `SetupDrawers`).
- [ ] **Step 2:** Commit `bungalow_prop_models.py`, `GothicCommode_01.fbx`, `bungalow_prop_models.json`, the prefab, `BungalowPropsPlacer.cs`, `DrawerPrefabTests.cs` (+ .meta), the SPEC and this plan. Use `feat(bungalow): open and close the chest of drawers' drawers`, with no attribution, straight to `main`. Leave out the scene and bake: they carry other sessions' uncommitted work.
