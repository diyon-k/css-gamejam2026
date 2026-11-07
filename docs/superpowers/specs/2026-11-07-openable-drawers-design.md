# Openable drawers: design

Date: 2026-11-07. Scope: the bungalow's chest of drawers only.

## Goal

The player can open and close the three drawers of the bungalow's chest of drawers (`GothicCommode_01`). The model is used twice: `Bed_Dresser` in the bedroom and `Liv_Sideboard` in the living room, so 6 drawers in all. The drawers are empty.

Out of scope: nightstands, the hall console and the kitchen cabinets (their drawers aren't modelled separately); drawer contents; sounds; locks; zombies reacting to drawers.

## Why this model

The Poly Haven source (`Art/Mansion/PolyHaven/GothicCommode_01/GothicCommode_01_1k.fbx`) already has the drawers as separate objects. Each of `GothicCommode_01_door01`–`door03` is a full drawer box, about 1.00 × 0.26 m with a depth of 0.54 m. The bungalow prop build joins them into the body today (`bungalow_prop_models.ph`). No mesh cutting is needed.

## Approach: in the prop pipeline

The change lives in the data-driven prop pipeline, so a future prop rebuild keeps it.

### 1. Blender: `tools/blender/bungalow_prop_models.py` and `bungalow_prop_build.py`

- `GothicCommode_01` keeps its body joined and decimated as now. The three `door0N` objects become child meshes named `GothicCommode_01_Drawer1`–`Drawer3`, top to bottom, under the body object. This follows the fixtures' `<Model>_Emissive` child convention.
- Each drawer is decimated in proportion to its share of the model's triangle budget. It is atlas-mapped to the same cell as the body and gets its own UV2 (`lib.uv_lightmap`).
- Each drawer's pivot is at its own bounds centre, so moving it along local +Z slides it straight out.
- Unity finds the drawers by child name (`<Model>_Drawer<N>`), so `Props/bungalow_prop_models.json` doesn't change shape.

### 2. Unity: `Editor/BungalowPropsPlacer.cs` (prefab build)

For each child whose name matches `<Model>_Drawer<N>`:

- **Static flags:** none, so it can move. `receiveGI = LightProbes`, shadows on. The body keeps its furniture flags and stays lightmapped.
- **Collider:** a `BoxCollider` fitted to the drawer's mesh bounds and extended 0.01 m out of the front. The body's box collider covers the whole commode, so without the extension the interact ray would hit the body and never reach the drawer.
- **`Openable`** (`Scripts/Interaction/Openable.cs`, unchanged):
  - `motion = Sliding`, `noun = "drawer"`;
  - `slide = (0, 0, 0.30)` in parent space, i.e. along the commode's front (+Z). That's just over half the 0.54 m depth, so the drawer never leaves the carcass;
  - `seconds = 0.3`; the default hand delay and ease, with no overshoot for sliding.
- The prompt reads "Open drawer" / "Close drawer" through `Openable.Prompt`.

The scene instances (`Bed_Dresser`, `Liv_Sideboard`) are prefab instances, so they pick up the drawers when the prefab is rebuilt. `Editor/BungalowStartBedroom.cs` (key and note on top of `Bed_Dresser`) is unaffected.

### 3. Lighting

The drawer fronts leave the static set, so the bungalow is rebaked after the change: BLINK/Bungalow/Build Lighting, then a synchronous bake. Before baking, check that no other session is editing the lighting or baking.

## Testing

- **Edit-mode test** (`Tests/EditMode/DrawerPrefabTests.cs`): the `GothicCommode_01` prefab has exactly 3 `_Drawer` children. Each has:
  - a sliding `Openable` whose `slide` points along +Z with length 0.30;
  - a `BoxCollider` whose front face is at least 0.005 m in front of the body collider's front face;
  - no `ContributeGI` static flag.
- **In the editor:** player-eye renders of `Bed_Dresser` closed, with one drawer open and with all three open, checking alignment, gaps and that nothing pokes through. Plus a raycast from eye height at each drawer front, checking that it resolves to that drawer's `Openable`.

## Risks

- **Decimation seams:** decimating the body and drawers separately could leave small gaps round the drawer fronts. Mitigation: check them in the closed render; give the drawers a higher triangle share if needed.
- **Stale baked shadow:** a drawer that is open during play leaves a small baked shadow in the old spot. This is accepted; drawers are small and the shadow sits under the top.
