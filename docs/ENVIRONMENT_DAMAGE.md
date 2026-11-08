# Environment damage (plan)

Every hit on the house leaves something behind: bullet holes, axe gashes, crowbar dents, blood on the wall behind a zombie. Shot lamps go dark, shot glass breaks, loose props get knocked about. Nothing here changes a weapon or a brain script. Like `BloodImpactSpawner` and `CorpseShotReaction`, it all listens to events that already exist.

## What already exists (hook points)

- `PlayerWeapons.RayFired(ShotRay)`: every pistol ray, raised after damage. Gives `Hit`, `Info.point`, `Info.normal`, `Info.collider` and `Target` (null on a wall).
- `PlayerWeapons.MeleeHit(MeleeHitInfo)`: every melee blow (axe and held `SwingableProp`s: crowbar, wrench, pan, plank, candlestick). Gives `Point`, `Normal`, `Direction`, `Collider` and `Target` (null on a wall or prop).
- `PlayerWeapons.ActiveWeapon` and the held `SwingableProp` (`MeleeAudio` already reads `Item`): these tell a crowbar from a pan.
- `BloodImpactSpawner` returns early on `hit.Target == null`. The new listener handles exactly those hits.
- `ImpactAudio` / `MeleeAudio` already play one `surfaceHitCue`. Per-surface sounds come in phase 4.
- Not there yet: URP decals. `PC_Renderer` has only SSAO, so phase 1 adds the Decal renderer feature.

## Core piece: `ImpactMarks` (Effects, one per scene, creates itself like `CorpseShotReaction`)

- **Listens to** `RayFired` and `MeleeHit`. If `Target == null` and the collider isn't on the `Corpse` layer or a zombie, it is a world hit.
- **Surface:** finds the hit renderer's `sharedMaterial` name in one static table (`SurfaceTable`) and maps it to a `SurfaceKind`. There's no tagging and no component on every object. An optional `Surface` component overrides the table for odd cases. Unknown materials count as `Plaster`.
- **Mark:** a pooled URP `DecalProjector` placed at `point + normal * 0.01`, facing `-normal`, with a random roll and ±15% size. All marks share one **atlas** material, and each projector picks its cell through `uvScale`/`uvBias`, so the whole system uses one material and one draw setup.
- **Moving things:** if the collider has a non-kinematic `Rigidbody`, the decal is parented to it so it moves with the prop. Doors (`Openable`) get parented too.
- **Cap:** a fixed pool of 96 projectors, oldest reused first (`ponytail: FIFO cap, fade-out on reuse if the pop is visible`).
- **Blink skips:** marks don't change over time, so it isn't `ISkippable`.
- **Particles:** a one-shot burst per surface (dust puff, wood splinters, sparks, glass shards) from one pooled `ParticleSystem` per kind. Blood uses the existing `Free Blood VFX`.

## Surfaces x weapons (what each hit leaves)

| Surface (materials) | Pistol | Axe | Crowbar / wrench | Pan / plank / candlestick |
|---|---|---|---|---|
| **Wood** (DarkWood, Wood_*, PianoLacquer, *_Baked furniture, doors) | splintered hole + splinters | deep chop gash along the swing | long scrape + dent | shallow round dent |
| **Plaster** (GrimyPlaster, walls) | crater hole + white dust puff | gouge, chunks fall | crack spider + dust | small crack + dust |
| **Stone** (Stone, FireplaceStone, Concrete) | chip + grey dust + spark | chip mark, sparks | chip + sparks | scuff only |
| **Metal** (CastIron, Iron, Brass, Rust, Silver) | bright dent + sparks + ricochet *ping* | scratch + sparks | scratch + sparks | scuff + clang |
| **Glass** (GlassDark, GlassLit, Glass) | crack star; 2nd hit shatters (phase 3) | shatters | shatters | shatters |
| **Fabric** (Rug_*, Fabric_*, DustSheet, LampShade, StainedCloth, Leather_*) | small dark tear, fluff puff | slash tear | none (soft) | none (soft) |
| **Porcelain / ceramic** (Porcelain, Ceramic, Plate) | chip; small items break (phase 3) | breaks | breaks | breaks |
| **Paper / books** (Book_*, Paper, PaintingCanvas, Canvas_*) | hole + paper bits | slash | none | none |
| **Water** (FloodWater, GrimeWater) | splash ring (particles only, no decal) | splash | splash | splash |

- The melee mark is stretched along `Direction` projected onto the surface, so a crowbar scrape follows the swing.
- Hands: no marks.

## Blood on the environment (zombie hits)

- **Back-spray:** when a pistol ray hits a zombie, cast on along `Ray.direction` up to 2.5 m from `Info.point` against the world. On a hit, put a blood splatter decal there, bigger for a headshot (`ShotRay.IsHead`).
- **Melee splash:** axe hits on a zombie cast 1.5 m along `Direction`; splat on the wall or floor.
- **Pools:** when a corpse settles (`RagdollSettleClock` / `ZombieRagdoll` freeze), drop one blood-pool decal on the floor under the hips that grows over about 4 s.
- Blood uses the same pool and atlas (blood cells in the atlas).

## Interactive props (beyond marks)

- **Knock:** world hits on a non-kinematic `Rigidbody` get `AddForceAtPosition` (pistol 4 N·s, melee 6 N·s), so books, plates, candlesticks, crates and suitcases jump when hit. `Carryable` and `SwingableProp` items are already Rigidbodies, so this mostly comes free.
- **Lights:** shooting or hitting a lamp, candle or chandelier bulb (`Bulb`, `Flame` materials, or a `Breakable` with a `Light`) turns its `Light` off, with a glass burst and a spark. That fits the darkness theme: shooting a light makes the room more dangerous.
- **`Breakable` (one small component, phase 3):** `hitsToBreak`, a list of debris Rigidbody pieces or just a particle burst, an optional `Light` to kill, and a `SoundCue`. Used on windows, plates, bottles, the oil lamp, picture glass and the chandelier (shoot it to drop it, its own `Rigidbody` released).
- **Paintings:** a hit tilts the frame (a hinge at the top or a small random rotation), and the canvas gets a tear decal.
- **Dust sheets (`DustSheet`):** a hit puffs dust. A tear decal only if it reads well on the cloth.
- **Doors (`Openable`, `JammedDoor`):** marks parent to the door. An axe on a `JammedDoor` could chip it open (design call, not in this plan).
- **Noise:** every world impact already makes sound. Also raise a `Noise` at the point (gunshot and metal louder), so shooting walls draws zombies, consistent with the stealth loop.

## Textures (one atlas, 4x4 cells, 2048²)

- Albedo with alpha plus a normal map (the normal is what makes a hole look deep under the flashlight).
- Cells: bullet hole wood, plaster, stone, metal; axe gash wood, plaster; blunt dent, scrape; crack spider; glass crack star; fabric tear; paper hole; blood splatter x2; blood pool; scorch (spare).
- **Source:** CC0 textures (ambientCG / Poly Haven, matching the PolyHaven licence already in the project) where they have a fitting decal, or generate with Unity MCP `generate_image` then fix the alpha by hand. CC-BY ones go in the README credits.
- **Material:** `Shader Graphs/Decal` with the atlas, Base Color + Normal + alpha, `affectsNormal` on.

## Phases (each one shippable, commit after each)

1. **Bullet holes on walls.** Add the Decal renderer feature (DBuffer) to `PC_Renderer` (and `Mobile_Renderer`). `ImpactMarks` with pool, placement and parenting; pistol only; one plaster hole texture. *Check: shoot every wall/floor/furniture type in `House.unity`; no floating, z-fighting or marks on zombies.*
2. **Surfaces + melee.** `SurfaceTable`, the full atlas, per-surface particles, `MeleeHit` marks per weapon (axe / blunt / scrape stretched along the swing). *Check: the table in this doc.*
3. **Blood + props.** Back-spray, melee splash, corpse pools, rigidbody knock, `Breakable` (glass, plates, lamps, chandelier), light kill, painting tilt.
4. **Sound + noise.** Per-`SurfaceKind` impact cues in `ImpactAudio`/`MeleeAudio` (wood thunk, plaster crumble, metal ping, glass crash), ricochet ping on metal, `Noise` on impact.

## Tests

- EditMode (`Tests/EditMode/ImpactMarksTests.cs`): `SurfaceTable` lookups (known names, `(Instance)` suffix stripped, unknown → Plaster), the FIFO pool reuses the oldest, and the melee mark's rotation lies along the swing on the surface plane.
- Visual: a `Sandbox.unity` wall strip with one block of each surface to shoot and swing at.

## Risks / decisions

- **DBuffer vs Screen Space decals:** DBuffer gives normals (holes look deep) but costs a prepass; Screen Space is cheaper and has no normals. Start with DBuffer, and drop to Screen Space if the frame budget on the demo laptop says so.
- **Skinned meshes:** no decals on zombies or corpses (they swim on skinned meshes). Their blood stays the existing VFX.
- **Material lookup** uses the first material when the collider isn't a `MeshCollider` (no submesh info). Good enough for walls and furniture; use a `Surface` override where it's wrong.
- **Shared art:** the `.mat` files currently modified in `PolyHaven/` aren't touched by this.
