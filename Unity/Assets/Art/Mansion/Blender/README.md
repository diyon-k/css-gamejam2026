# Mansion hero assets (Blender, procedural)

Built by `tools/blender/build_all.py` (`blender -b --factory-startup --python tools/blender/build_all.py -- [Name ...] [--preview DIR]`).
Re-running it overwrites the FBX files. The importer settings and material remaps live in the `.meta` files, so they survive a rebuild.

**Conventions (all assets):** metres, import scale 1, root rotation 0. The front faces local **+Z**, up is +Y.
Origin is at the base centre (floor contact, centre of the footprint) unless a line below says otherwise.
Size is W x H x D in Unity X/Y/Z. Materials are the shared URP Lit materials in `Materials/`. Slot names: Wood_Dark, Wood_Light, Brass, Iron, Silver, Fabric_Red, Fabric_Linen, Wax (slightly emissive), Glass (transparent), Stone, Paper, Ceramic, Leather_Brown, Leather_Green.
Nothing has colliders; add them on the prefab or instance.

**Textures (PBR, URP Lit).** Each map is in `Textures/`. Mask = `_MetallicGlossMap` (R metallic, A smoothness). The same file is also used as `_OcclusionMap` (G = AO).
- **Shared tiling sets** (1K, 1 tile per metre): `Wood`, `Metal`, `Fabric`, `Stone`, `Leather`, `Smooth` (each has `_Albedo`, `_Normal`, `_Mask`). The 14 shared materials tint a family albedo with `_BaseColor`, and they are SRP-Batcher friendly.
  - Wood_*: Wood. Brass/Iron/Silver: Metal. Fabric_*: Fabric. Stone: Stone. Leather_*: Leather. Wax/Paper/Ceramic: Smooth. Glass has no texture.
  - UVs are per part, and wood grain follows each part's long axis.
- **Baked heroes** (decimated low-poly, Smart UV, Cycles bake of the full-detail build): FourPosterBed, Chandelier, Armchair, Candelabra (512), Bookshelf.
  - Each uses one `<Asset>_Baked` material with its own `<Asset>_Albedo/_Normal/_Mask`. Their Glass and Wax faces keep the shared Glass/Wax materials.

| File | Size W x H x D (m) | Origin / pivot | Named children | Materials |
|---|---|---|---|---|
| Chandelier.fbx | 0.98 x 1.04 x 0.86 | **Hang point at TOP centre** (everything hangs below y=0) | `FlameAnchor_0..5` (wick tips) | Chandelier_Baked, Wax, Glass |
| Fireplace.fbx | 2.0 x 1.18 x 0.93 | Base centre of the 0.42 m-deep surround. The hearth slab sticks out 0.5 m in front (+Z), and the back face is at z = -0.21 | `Logs` (pivot on the firebox floor, y=0.12) | Stone, Iron, Brass, Wood_Dark |
| GrandfatherClock.fbx | 0.54 x 2.13 x 0.31 | base centre | `HourHand`, `MinuteHand`: pivot at the dial centre (y 1.72). At rest they point to 12. Rotate about local **Z**: `localEulerAngles.z = +hours*30` / `+minutes*6` turns them clockwise as seen from the front. `Pendulum`: pivot at its top, swings about local X | Wood_Dark, Wood_Light, Brass, Ceramic, Iron, Glass |
| FourPosterBed.fbx | 1.70 x 2.23 x 2.23 | base centre. The foot of the bed faces +Z | none (one mesh: tattered canopy, curtains, mattress, pillows, blanket) | FourPosterBed_Baked |
| Wardrobe.fbx | 1.34 x 2.10 x 0.71 | base centre, hollow interior with a rail and a shelf | `Door_L` (x=+0.6, the viewer's left) opens with **local Y +90**. `Door_R` (x=-0.6) opens with **local Y -90**. The pivots are on the hinge edges | Wood_Dark, Wood_Light, Brass |
| ChestOfDrawers.fbx | 1.05 x 0.96 x 0.55 | base centre, hollow carcass | `Drawer_0` (bottom) to `Drawer_3` (top): pivot at the front-face centre. Real drawer boxes about 0.45 deep; slide along local **+Z** (≤0.4 m) | Wood_Dark, Wood_Light, Brass |
| TableLamp.fbx | 0.44 x 0.63 x 0.44 | base centre | `LightAnchor` (bulb centre, y 0.485) | Ceramic, Brass, Glass, Fabric_Linen, Fabric_Red |
| Candelabra.fbx | 0.25 x 0.54 x 0.25 | base centre | `FlameAnchor_0` (centre) to `_4` | Candelabra_Baked, Wax |
| Candle.fbx | 0.17 x 0.16 x 0.13 | base centre (brass chamberstick with a finger loop) | `FlameAnchor` (wick tip, y 0.158) | Brass, Wax, Iron |
| Bookshelf.fbx | 1.09 x 2.00 x 0.40 | base centre | none (about 110 books, stacks and leaners, merged) | Bookshelf_Baked |
| Armchair.fbx | 0.91 x 1.18 x 0.84 | base centre, wingback, seat at about 0.48 | none | Armchair_Baked |
| DiningChair.fbx | 0.49 x 1.05 x 0.58 | base centre, seat 0.45-0.47 | none | Wood_Dark, Fabric_Red |
| LongDiningTable.fbx | 3.0 x 0.76 x 1.02 | base centre, 6 turned legs | none | Wood_Dark |
| Piano.fbx | 1.48 x 1.26 x 0.64 | base centre. The keyboard faces +Z, and the back face is at z = -0.31 | none (88 keys, a few sunk or missing; candle sconces) | Wood_Dark, Ceramic, Iron, Brass, Wax |
| PanelDoor.fbx | 0.90 x 2.10 x 0.045 slab | **Hinge edge at the floor**, mid-thickness. The leaf spans local **-X**. Local Y +90 swings it toward +Z | none | Wood_Dark, Brass, Iron |
| PanelDoor_Tall.fbx | 0.70 x 2.29 x 0.045 | same as PanelDoor. Two of them (one turned 180°) fill a **1.4 x 2.3 doorway** | none | Wood_Dark, Brass, Iron |
| DoorFrame.fbx | 1.68 x 2.46 x 0.29 | Base centre of the **1.4 x 2.3 opening**, centred in a **0.2 m wall** (y=0 is floor). Jamb linings plus casings, plinth blocks and rosettes on both faces. Clear width 1.35 | none | Wood_Dark |
| WindowFrame.fbx | 1.68 x 1.50 x 0.33 | **Bottom centre of the 1.4 x 1.2 opening** (sill level, mid-wall in a 0.2 m wall). The interior side is +Z (stool and casing); the exterior stone sill is at -Z. Place it at the window hole's bottom (1.0 m above the floor in HouseBlockout) | `Sash_Lower`, `Sash_Upper` (pivot at the bottom centre of each sash; slide along local Y). Some panes are broken | Wood_Dark, Glass, Stone |
| Ladder.fbx | 0.48 x 3.0 x 0.07 | base centre, modelled upright (tilt it to lean). Rungs every 0.3 m | none | Wood_Light, Iron |
| Banister.fbx | 2.0 x 0.92 x 0.07 | base centre, flat 2 m run, handrail top at 0.92 | none | Wood_Dark |
| Banister_Stair.fbx | 2.0 x 2.35 x 0.07 | **Lower end** on the stair nosing line. Runs 2 m along local **-X** and rises 1.43 m (0.2/0.28 stair pitch) | none | Wood_Dark |
| NewelPost.fbx | 0.19 x 1.26 x 0.19 | base centre | none | Wood_Dark |
| Key.fbx | 0.114 x 0.009 x 0.031 | lies flat, base centre | none | Iron |
| Fork.fbx / Spoon.fbx / Knife.fbx | about 0.20-0.22 long | lie flat, base centre. Length along X | none | Silver (+ Ceramic handle on the knife) |
| Plate.fbx | Ø 0.26 x 0.024 | base centre | none | Ceramic |
| Book.fbx | 0.156 x 0.038 x 0.22 | lies flat, base centre | none | Leather_* or Fabric_Red, Paper |
