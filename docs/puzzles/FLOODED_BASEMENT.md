# Puzzle: Flooded Basement

Build brief for Claude Code. Read `docs/ARCHITECTURE.md` (contracts, time rule) and `docs/STORY_PLAN.md` (P6) first. This design **replaces** the current P6 "The flood" entry in `STORY_PLAN.md` (wade to a key on a hook). Update that section to match once it's built.

**Blink rule: don't blink (each blink is a big jump in the water). Once the drain is open, blinking helps.**

---

## The fantasy in one paragraph

You step into a basement room and the door slams shut behind you. In a bath against one wall a tap drips, then runs, and water starts creeping across the floor. In the middle of the floor is a drain, but something heavy is wedged over it. Every time you blink, the water lurches up. Blink a few times and it's over your head. You tear through drawers and cupboards looking for something to lever the cover off, find a crowbar, wade to the drain and wrench it open. The water spirals away, and the door unlatches.

---

## Where it goes

- **Built (2026-11-07) in the bungalow `B2` workshop** (`House_Bungalow.unity`, x 13.575..19.7, z 4.075..14.7, floor y −2.85, 2.6 m to the slab). The doorway is D5 in the west wall (x 13.5, z 12.4..13.6, off the `B1` cellar). The drain is dead centre at (16.64, 9.39), 4.7 m from the doorway; the bath (the upstairs bathroom's `ClawfootTub` prefab, running from its own taps) is against the east wall facing D5; the drawers and tall cupboard are on the north wall, the tool locker and crate on the west wall. The clue card is on a stake by the drain. **The door is D5's own `Door`** (the placement script sets `FloodedBasement.door`): on `FloodStarted` it slams in 0.12 s (`Door.Slam`, the `Door_Burst` cue, a jolt and a thump; just locks if already shut) and locks as a story lock (rattles, "Locked", carves the navmesh so clowns can't path through); on `Solved` or `WasReset` it unlocks and swings open away from the player. Re-run **BLINK > Place Puzzle: Flooded Basement** after a house rebuild, then Setup Doors (it keeps D5's `Door`, so the link holds; on a scene with no D5 `Door` yet, run Setup Doors before the placement too). The rest of this section is the original brief.
- **Always `PuzzleRoom_5`** (basement, x 0..8, z 0..8, floor y −3.2, ceiling 3.0 m above the floor). Its only doorway is in the north wall at x ≈ 3.3..4.7, z 8, off `Basement_Passage`.
- Layout (inside the walls, about 7.8 × 7.8 m):
  - **Drain** dead centre at (4, −3.2, 4).
  - **Bath** with the tap against the south wall (z ≈ 0.6), centred around x 4, so it's the first thing you see from the door. Reuse `HouseBlockout.DBathtub` (Dressing.cs) or copy its shape; add a bigger tap with a running-water particle stream.
  - **Search spots** around the walls (see below), leaving a clear line from the door to the drain.

## Reward

- **`crowbar`** (new item id) — found during the search, used up on the drain.
- **`key_workshop`** (unchanged from STORY_PLAN; opens the `PuzzleRoom_6` door). It's revealed when the drain cover comes off: snagged in the drain's grate basket. It becomes a usable `Pickup` once the water's gone (pick-up prompt only after draining).
- Clue card (typed), pinned beside the drain cover, readable with `Readable`, only useful once you're already in trouble:
  > *"Drain blocked AGAIN. The cover's rusted solid, you'll need a bar to shift it. — Groundsman"*

---

## Rules (exact)

### States

```
Idle ──player fully inside──▶ Flooding ──cover levered off──▶ Draining ──water at 0──▶ Solved
  ▲                              │
  └── respawn (reset) ◀── Drowned ◀── water reaches eye height
```

- **Idle:** door open, bath tap dripping (a single drip sound every ~1.5 s), floor dry, crowbar not placed yet.
- **Flooding** starts when the player is `slamDepth` (1.5 m) inside the doorway:
  - The door **slams** shut and locks (see Door). Bang, dust, gamepad jolt.
  - The tap goes from a drip to running. The water starts rising.
  - The crowbar is placed in a **random search spot** (re-rolled on every attempt).
- **Drowned:** the water surface reaches the camera (eye) height → drowning death (below).
- **Draining:** the cover's off. The water drains, the tap is turned off (it splutters and stops), blinks now *help*.
- **Solved:** water at 0, door unlocks and swings open a little on its own (a creak), `key_workshop` can be picked up. Permanent: later deaths elsewhere don't reset it (`WakeUpSequence` keeps world progress).

### Water level

Level = metres of water above the room floor. Drive it from a pure `FloodLevel` class (unit tested):

| Source | Effect | Default |
|---|---|---|
| Base rise | `riseRate` × **world** delta time (scaled, so eyes-shut 3× speeds it too) | 0.012 m/s (≈ 2 min from dry to drowning with no blinks) |
| **Blink** | One jump of `blinkJump`. Hidden behind the eyelids: make the room an `ISkippable` and apply it in `Skip()`, so the player opens their eyes to a higher water line | 0.32 m |
| **Eyes held shut** | One `blinkJump` per `shutJumpInterval` of **real** time while `BlinkDetector.EyesShut` (shutting your eyes is a quick drowning) | every 0.5 s real |
| Draining | Falls at `drainRate` × world delta time; a blink now skips draining forward (`Skip(seconds)` advances the drain by `seconds`) instead of raising the water | 0.35 m/s |

- The jump isn't instant on screen: the level jumps, but the visible water surface eases up to it over ~0.25 s *after* the eyes open (unscaled), with a slosh sound and a camera bob, so the player sees and hears it surge. Death is decided on the real level, not the eased one.
- **Drown height** = the camera's world y at the moment of checking, minus the floor y, minus 0.05 m. Read it live, don't hard-code: crouching lowers your eyes and drowns you sooner (worth a line in the clue or just a nasty surprise).
- With the defaults, ~5 blinks from dry is death; with a 20 s search and the base rise, ~4. Natural blinks come every ~3-5 s, so the player has to actively hold their eyes open. That's the point.
- Before relying on `Skip()`, check that only blinks call `TimeSkipController.SkipWorld` (grep for callers). If anything else does, subscribe to `TimeSkipController.Skipped` instead.

### Door

- The blockout doorway is just a hole: add a real hinged door (an `Openable`, `noun = "door"`) in the `PuzzleRoom_5` doorway, built by the placement script. Starts open.
- Slam: temporarily set its `seconds` to ~0.12, `SetOpen(false)`, restore. Lock it by setting `requiredItemId` to an id nobody can get (e.g. `"flood_lock"`), so trying it rattles and shows "The door is locked". Better prompt if cheap: *"It won't budge."*
- Unlock on Solved: `requiredItemId = ""` and `SetOpen(true)`.
- Give the door a carving `NavMeshObstacle` while shut, so zombies in the passage can't path through. Better still, keep zombie routes out of the basement passage while this room is Flooding; a zombie can't get in anyway.

### Searching for the crowbar

- 6 search spots, all `Openable` (so the existing interact + open animation does the work): e.g. a chest of drawers (3 drawers = 3 spots), a tall cupboard, a wooden crate with a hinged lid, a metal tool locker. Reuse `HouseBlockout.Drawers()` / `Cupboard()` shapes or copy them.
- At the start of each attempt, pick one spot at random (don't pick the same one as the previous attempt) and **re-parent** the crowbar `Pickup` into it, so it slides out with the drawer. Close every spot.
- Fill the other spots with junk for flavour (rags, bottles, a rusty tin), non-interactive or as `Pickup`s with throwaway ids. Optional.
- Crowbar size ≈ 0.45 m so it fits a drawer (a short jemmy). `itemId = "crowbar"`, `displayName = "Crowbar"`.
- Spots must stay usable as the water rises: the water surface has **no solid collider** (a trigger on `Ignore Raycast` at most), so `Interactor`'s look ray passes through it. Low drawers underwater still open.

### Drain

- An interactable `DrainCover` (`IInteractable`):
  - Without the crowbar: prompt *"Something's wedged over the drain"*, `CanInteract` false (or rattles).
  - With the crowbar: prompt *"Lever the cover off"*, a single E press (the user's design: "press a button"). Keep it a tap, not a hold: the tension is the water, not a timer.
- On use: `Inventory.Remove("crowbar")`, the cover flips off onto the floor with a clang (physics or an animation), a whirlpool effect starts over the drain, a big gurgle loop plays, state → Draining.
- Since the drain is on the floor in the middle of the room, make sure it's in reach from standing height (`Interactor.reach` 2.2 m from eye height looking down is fine) and that nothing solid in the water blocks the ray.

### Drowning

1. As the water nears the eyes (within 0.25 m): low-pass/muffle the mix (BlinkMixer snapshot or a low-pass on the master), breathing gets panicky. Optional but cheap and it sells the danger.
2. When the level reaches eye height: green-brown underwater tint over the screen, bubbles, muffled heartbeat, `PlayerController.InputEnabled = false`, then after ~1.2 s `playerHealth.TakeDamage(playerHealth.Current, ...)`. `WakeUpSequence` takes over (slump, black, wake in bed).
3. Optional polish: respawn prompt *"You drowned\nBLINK to wake up"* via a small `WakeUpSequence.NextRespawnPrompt` hook (shared with the doll puzzle). Only if cheap.

### Reset (on respawn)

- Water back to 0, tap back to dripping, door open and unlocked, cover back on, whirlpool off, state Idle. The crowbar returns via `WakeUpSequence.DropItems` and is re-hidden on the next attempt.
- There's no respawn event today. Add one to `WakeUpSequence` (shared with Lily's Doll Room; whoever builds first adds it):
  ```csharp
  public static event Action Respawned; // raised in KnockOut() right after the player is teleported and healed
  ```
  Clear it in a `SubsystemRegistration` reset like the other statics.
- Reset while the screen is black, never in view.

### Respawn distance

Death in the basement (drowning, or clowns) respawns you in bed like any other death, not at a checkpoint outside the door. The room still resets on `WakeUpSequence.Respawned`.

---

## Code

New folder `Unity/Assets/Scripts/Puzzles/`, namespace `Blink.Puzzles`.

### `FloodLevel.cs` — pure logic (unit tested)

```csharp
public enum FloodState { Idle, Flooding, Draining, Solved, Drowned }

public sealed class FloodLevel
{
    public FloodState State { get; }
    public float Level { get; }                       // metres above the floor
    public void Start();                              // Idle -> Flooding
    public void Tick(float worldDt, float realDt, bool eyesShut, float drownHeight);
    public void Blink(float skipSeconds);             // Flooding: +blinkJump. Draining: drain by skipSeconds
    public void OpenDrain();                          // Flooding -> Draining
    public void Reset();                              // -> Idle, Level 0 (not from Solved)
}
```

Settings: `riseRate`, `blinkJump`, `shutJumpInterval`, `drainRate`. Draining reaching 0 → Solved. `Tick` checks `Level >= drownHeight` → Drowned (only while Flooding).

### `FloodedBasement.cs` — the MonoBehaviour (on the puzzle root)

- Implements `ISkippable` (register/unregister in `OnEnable`/`OnDisable`). `Skip(seconds)` → `flood.Blink(seconds)`.
- Each `Update`: feeds `Time.deltaTime` (world) and `Time.unscaledDeltaTime` (real) plus `BlinkDetector.Instance.EyesShut` and the live drown height into `flood.Tick`; eases the water plane's y towards `floorY + Level`; drives the tap/drain effects and audio; starts the slam when the player crosses `slamDepth`; runs the drowning sequence on Drowned.
- Owns the door (`Openable`), the search spots (`Openable[]`), the crowbar (`Pickup`), the `DrainCover`, the reward key, the water plane, the effects and the cues.
- Subscribes to `WakeUpSequence.Respawned` for the reset.

Inspector fields:

| Field | Default |
|---|---|
| `door` (`Openable`), `searchSpots` (`Openable[]`), `crowbar` (`Pickup`), `drain` (`DrainCover`), `rewardKey` (`Pickup`, `key_workshop`) | — |
| `water` (`Transform`, a plane/box filling the room inside the walls) | — |
| `slamDepth` | 1.5 m |
| `riseRate` | 0.012 m/s |
| `blinkJump` | 0.32 m |
| `shutJumpInterval` | 0.5 s (real) |
| `drainRate` | 0.35 m/s |
| `surgeSeconds` | 0.25 s (visual ease after a jump) |
| `drownDelay` | 1.2 s |
| cues: `dripCue`, `tapRunCue`, `slamCue`, `surgeCue`, `gurgleCue`, `coverClangCue`, `drownCue` | — |

### `DrainCover.cs` — `IInteractable`

Small: prompt / `CanInteract` from `Inventory.Has("crowbar")`, `Interact` raises an event the room listens to (or calls it directly). No state of its own beyond "off".

### Editor placement — `Editor/PuzzleProps.cs` (section "Flooded Basement")

Follow the `StoryProps.cs` pattern: menu **BLINK > Place Puzzle: Flooded Basement**, idempotent (replaces its own group), finds `PuzzleRoom_5` by name and raycasts for the real floor and walls. It builds the root with `FloodedBasement`, the door in the doorway, the bath + tap, the drain + cover + clue card, the 6 search spots (+ junk), the crowbar, the reward key in the drain basket (inactive / not interactable until Solved), and the water plane (starts at floor level, hidden at 0).

Dressing: damp concrete, a single bare bulb (flickers when the door slams, `LightFlicker` exists), pipes along the ceiling. Keep the middle of the room and the door-to-drain line clear. Leave the generic PuzzleRoom ceiling light dim.

### Water look

URP, no extra packages:
- A slightly transparent, murky green-brown plane with a scrolling normal or noise (simplest: a URP Lit transparent material with an animated offset). Must render from above and below (two-sided, or a second flipped plane), because the player's eyes end up just under it.
- Ripple rings under the tap stream, a whirlpool (rotating decal/mesh + particles) over the drain while Draining.
- Underwater screen tint when the camera is below the surface (only at the moment of drowning in practice).

### Movement in water (optional)

Slowing the player in deep water adds pressure (`walkSpeed` × ~0.7 above waist height). `PlayerController` has no speed multiplier today: add a small `public float SpeedMultiplier { get; set; } = 1f` applied in its move code. Only if cheap; tune it so the search is still possible.

### Audio

`SoundCue` assets in `Assets/Art/Audio/Cues/` (empty cue = silent placeholder): `Flood_Drip`, `Flood_TapRun` (loop), `Flood_DoorSlam`, `Flood_Surge` (on each blink jump, the slosh), `Flood_Gurgle` (drain loop), `Flood_CoverClang`, `Flood_Drown`. Use `AudioService.PlayLoop` / `PlayAt`. Third-party clips go in `CREDITS.md`.

---

## Tests (`Tests/EditMode/FloodLevelTests.cs`)

At least:
- Idle: ticks and blinks don't change the level.
- Flooding: level rises by `riseRate × worldDt`; each `Blink` adds `blinkJump`; eyes shut adds one jump per `shutJumpInterval` of real time (and not on world time).
- Reaching drown height → Drowned; nothing changes after that until `Reset`.
- `OpenDrain` → Draining: level falls by `drainRate × worldDt`; a blink now *lowers* it by `drainRate × skipSeconds`; reaching 0 → Solved.
- `Reset` from Drowned → Idle at 0; `Reset` does nothing once Solved.

---

## Tuning targets

- Search to drain-open should take **15-25 s** for a first-timer who blinks naturally a few times, and be survivable with **up to ~3 blinks**. If playtests show near-certain death, the levers in order: lower `blinkJump`, fewer search spots, lower `riseRate`. Don't remove the blink jump.
- Draining should take **3-5 s** with eyes open; a couple of blinks speed it up, which is the satisfying flip.

## Done when

- [ ] Walking in slams and locks the door; the tap runs; water rises slowly.
- [ ] Each blink visibly surges the water when the eyes reopen; holding eyes shut floods fast.
- [ ] The crowbar is in a different container each attempt; opening containers works at any water height.
- [ ] Water at eye height → drowning death → respawn → room fully reset (door open, dry, crowbar re-hidden next time).
- [ ] Crowbar + E on the drain → cover off, water drains (blinks speed it up), door unlocks and creaks open, `key_workshop` can be picked up.
- [ ] Solved state survives a later death elsewhere.
- [ ] Works with `blink_server/send_test_blinks.py blink` / `hold 2` and the real webcam.
- [ ] `FloodLevelTests` pass; no console errors.
- [ ] `STORY_PLAN.md` P6 section, the route table and the item-id table (`crowbar`) updated; `ARCHITECTURE.md` mentions `WakeUpSequence.Respawned` (and the checkpoint, if added).

## Out of scope

- Real water simulation or physics buoyancy (a moving plane is enough).
- Zombies in the room.
- Changing P5 (fire and pipes). The old "burst pipe floods the basement" link no longer applies; the bath tap is the source now. A line in the clue card or Note 5 can tie it back if wanted.
