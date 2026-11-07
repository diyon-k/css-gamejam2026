# Puzzle: Lily's Doll Room

Build brief for Claude Code. Read `docs/ARCHITECTURE.md` (contracts, time rule) and `docs/STORY_PLAN.md` (P2) first. This design **replaces** the current P2 "Lily's doll" entry in `STORY_PLAN.md` (the hopping doll + grab QTE). Update that section to match once it's built.

**Blink rule: don't blink, don't look away.**

---

## The fantasy in one paragraph

A near-empty child's room. At the far wall, facing the door, sits Lily's porcelain doll with a small key in its lap. A music box nursery tune plays from it. You have to walk in looking straight at it, take the key, and back out of the room without ever taking your eyes off it. Look away and it launches off the floor onto your face. Blink once and it has moved closer when your eyes open, and it giggles. Blink again and it's on your face. You're only safe once you're fully out of the room with the key.

---

## Where it goes

- **The room isn't decided yet.** Build it room-agnostic: everything lives under one root object with a `LilysDollRoom` component, and the room's size comes from a box on that root. Moving the puzzle = moving that root and re-running the placement menu with a different room name.
- **Default placement for now:** `PuzzleRoom_1` (second floor, x 29..36, z 3..10, doorway in the north wall at x ≈ 32.5, z 10, off `Corridor_Second`). That's the existing P2 slot and it's right next to the start bedroom, so death → respawn → retry is quick.
  - Doll against the south wall at about (32.5, 6.4, 3.4), facing +z (towards the door). That's ~6.5 m from the doorway: about 2 s of walking each way at `walkSpeed` 3.5.
- Whatever room is chosen, the doll must sit on the **far side from the door**, in a straight line from it, so the player can walk in and back out without turning.

## Reward

- The key is a `Pickup` with `itemId = "key_dorm"`, `displayName = "Lily's key"` (unchanged from STORY_PLAN: it opens the `ServantsDorm` door / attic hatch route). Keep the id an Inspector field so it can change if the room moves.
- Clue card (typed, per STORY_PLAN's puzzle-note rule), on the corridor wall right beside the doorway so it's read *before* entering, using `Readable`:
  > *"Lily's doll hates to be ignored. Keep your eyes on her, and don't you dare blink."*

---

## Rules (exact)

### States

```
Dormant ──player enters room──▶ Armed ──blink──▶ Warned
   ▲                              │                 │
   │◀── player leaves, no key ────┤                 │
   │                              │  look away / blink while Warned / eyes held shut
   │                              ▼                 ▼
   │                           Leaping ─────▶ Killing ─▶ (player dies → respawn) ─▶ Dormant (reset)
   │
   └─ player leaves room WITH key ─▶ Done (forever: doll is just a prop, music stops)
```

- **Dormant:** player is outside the room. Doll sits still, music plays quietly (audible from the corridor as a lure).
- **Armed:** player is inside the room bounds. All the rules below apply. Taking the key doesn't change the state; it only changes what happens when you leave.
- **Warned:** the one warning blink has been used. Same as Armed, except the next blink kills.
- **Leaping / Killing:** the kill sequence (see below). Can't be interrupted.
- **Done:** player left the room bounds while carrying the key. The doll never moves again. Persists through later deaths (world progress stays, per `WakeUpSequence`).
- **Leaving without the key** goes back to Dormant and **resets the warning** (back to Armed next time). That's a harmless "exploit"; it's fine.

### Looking away (instant leap, no warning)

The doll is **watched** when all three are true, checked every frame on unscaled time:
1. The angle between the camera's forward and the direction from the camera to the doll's `face` anchor is ≤ `watchAngle` (30°).
2. The face anchor is inside the camera viewport (`WorldToViewportPoint`, z > 0, x/y in 0..1). Belt and braces for wide FOVs.
3. A linecast from the camera to the face isn't blocked (ignore the player's own colliders, the doll's colliders, triggers and `Ignore Raycast`).

If it's unwatched for longer than `lookAwayGrace` (0.15 s real time) → **Leaping**. The grace soaks up `CameraFeel` jolts and a twitchy mouse; it is not meant to forgive a real glance away.

Note: this is about the **in-game camera**, not the webcam. Turning your head away from the webcam counts as eyes open (by design, see ARCHITECTURE.md) and is irrelevant here; only where the game camera points matters.

### Blinking (one warning)

- **A quick blink:** make the doll an `ISkippable`. `Skip(seconds)` is called by `TimeSkipController` *while the eyelids are covering the screen* (`BlinkEyelids` transition), so whatever the doll does there is hidden, and the player opens their eyes to the result:
  - Armed → **Warned:** the doll lurches `warningLurch` (1.2 m) straight towards the player along the floor, its head tilted ~20° and turned to face the camera. A giggle sting plays. The music box skips a beat and goes slightly out of tune (pitch 0.94) from here on.
  - Warned → **Killing** directly: when the lids open the doll is already on your face (see kill sequence; skip the flight).
- **Eyes held shut** (`BlinkDetector.EyesShutBegan`, closed past 0.35 s, no `Blinked` follows): counts as a blink at that moment, with one difference: the lurch can't be hidden by the eyelid transition, but the player's eyes are physically closed so it doesn't matter. Armed → Warned; Warned → Killing. Also, if the eyes stay shut past `shutKillSeconds` (1.0 s total) even from Armed → Killing. You don't get to keep your eyes closed in here.
- Before relying on `Skip()`, check that only blinks call `TimeSkipController.SkipWorld` (grep for callers). If anything else does, subscribe to `TimeSkipController.Skipped` instead and filter.
- `TimeSkipController` already drops blinks while `Locked` or during `GrabQte`; the doll inherits that for free.

### Leaving

- "In the room" = the player's position is inside the root's `BoxCollider` bounds (`isTrigger`, sized to the room interior, full height). Use a plain `Bounds.Contains(player.position)` check each frame, not `OnTriggerExit`, to avoid layer/physics setup issues.
- Safe = position outside the bounds **and** at least `exitMargin` (0.4 m) past the doorway line, so a half-step back through the door doesn't count as "out".
- While Armed/Warned the rules apply right up to that point, including while walking backwards through the doorway.

---

## The kill sequence

Pure scare, then the normal death flow. All on unscaled time; the player is outside world time anyway.

1. **Lock:** `PlayerController.InputEnabled = false`, `TimeSkipController.Locked = true`, `Carryable.DropHeld()`, put the weapon away (`PlayerWeapons.Instance.Select(WeaponKind.Hands)` or equivalent; check the enum name).
2. **Leap (look-away case only, 0.25 s):** the doll flies on a short arc from where it is to a point ~0.25 m in front of the camera, rotating to face it. Turn the camera onto it with `PlayerController.TurnTowards(face, sharpness, dt)`. A short scream sting fires at take-off.
3. **On your face (0.6 s minimum):** doll face fills the screen (parent it to the camera at the end of the leap so it can't drift), heavy camera jolts each ~0.1 s via `CameraFeel.AddJolt`, gamepad hit pulse, music cuts dead, a wet porcelain-scratching sound loops.
   - **If the player's eyes are closed** (the blink-kill or eyes-shut case), hold this pose until `BlinkDetector.EyesClosed` goes false, then 0.6 s more (cap 3 s total), so the player opens their eyes onto it. That's the scare.
4. **Die:** `playerHealth.TakeDamage(playerHealth.Current, face.position, camera.forward)`. From here `WakeUpSequence` owns it: slump, fade to black, wake up in the start bedroom. The key goes back automatically (`WakeUpSequence.DropItems` re-activates the `Pickup`).
5. **Reset** once the screen is black (see below): doll back on its spot, unparented from the camera, Dormant, warning cleared, music back to normal.

Optional polish: a one-off respawn prompt like *"Lily wanted to play\nBLINK to wake up"* instead of "You were knocked out". That needs a small hook in `WakeUpSequence` (e.g. `public static string NextRespawnPrompt`, consumed once). Only add it if it's cheap.

---

## Code

New folder `Unity/Assets/Scripts/Puzzles/`, namespace `Blink.Puzzles` (compiles into `Blink.Runtime` automatically).

### `DollRule.cs` — pure logic (unit tested)

A plain C# class with no Unity calls in its decisions, so it can be tested in EditMode. Owns the state machine above.

```csharp
public enum DollState { Dormant, Armed, Warned, Killing, Done }
public enum DollEvent { None, Warned, Kill, Escaped, Disarmed }

public sealed class DollRule
{
    public DollState State { get; }
    public DollEvent Tick(bool inRoom, bool hasKey, bool watched, float realDt);   // look-away timer, enter/leave
    public DollEvent Blink();                                                     // quick blink
    public DollEvent EyesShut(float closedSeconds);                               // called while held shut
    public void Reset();                                                          // back to Dormant (not from Done)
}
```

Settings it needs: `lookAwayGrace`, `shutKillSeconds`. Leave geometry (angles, bounds) to the MonoBehaviour and pass booleans in.

### `LilysDollRoom.cs` — the MonoBehaviour (on the puzzle root)

- Implements `ISkippable` (register in `OnEnable`, unregister in `OnDisable`). `Skip()` → `rule.Blink()`.
- Subscribes to `BlinkDetector.Instance.EyesShutBegan` and polls `EyesShut` / closed time while shut.
- Each `Update`: computes `inRoom`, `hasKey` (`Inventory.Has(keyItemId)`), `watched` (the three checks), calls `rule.Tick`, acts on the returned event (lurch, start kill, mark done).
- Head tracking while Armed/Warned: the doll's head slowly turns to keep facing the camera (slerp, unscaled). Body stays put except for the lurch. Creepy, and it makes "facing you" true wherever you stand.
- Runs the kill sequence as a coroutine on unscaled time.
- **Reset hook:** subscribe to the player's `Health.Died` to know the kill landed, and reset when the respawn happens. There's no respawn event today; add a tiny one to `WakeUpSequence`:
  ```csharp
  public static event Action Respawned; // raised in KnockOut() right after the player is teleported and healed
  ```
  (Clear it in a `SubsystemRegistration` reset like the other statics.) The flooded basement puzzle needs the same event, so whoever builds first adds it.
- **Key position gotcha:** the key sits in the doll's lap as a child of a `lap` anchor, so the lurch carries it. But `Pickup` captures its home position *when picked up*, so a key taken after a lurch would later be restored to the lurched spot. On reset, explicitly re-parent the key to `lap` and zero its local pose after `WakeUpSequence` has re-activated it.

Inspector fields:

| Field | Default | Notes |
|---|---|---|
| `doll` | — | Root transform of the doll |
| `face` | — | Anchor between the eyes (look checks, camera lock) |
| `head` | — | Turns to track the player |
| `lap` | — | Key parent |
| `key` | — | The `Pickup` |
| `keyItemId` | `key_dorm` | |
| `roomBounds` | — | `BoxCollider` (trigger) on the root |
| `exitMargin` | 0.4 m | |
| `watchAngle` | 30° | Half-angle of the "looking at it" cone |
| `lookAwayGrace` | 0.15 s | Real time |
| `warningLurch` | 1.2 m | Never closer than 1.5 m to the player |
| `shutKillSeconds` | 1.0 s | Eyes held shut this long = kill |
| `leapSeconds` | 0.25 s | |
| `faceHoldSeconds` | 0.6 s | Min time on your face |
| `musicCue`, `giggleCue`, `screamCue`, `scratchCue` | — | `SoundCue` assets |

### Editor placement — `Editor/PuzzleProps.cs` (or a `LilysDoll` section in it)

Follow the `StoryProps.cs` pattern: menu **BLINK > Place Puzzle: Lily's Doll**, idempotent (deletes and rebuilds its own group), finds the target room's root by name (`const string Room = "PuzzleRoom_1"`), raycasts to find the real floor/walls so it survives a blockout rebuild. It builds:
- the root with `LilysDollRoom` and the room-sized trigger box,
- the doll (placeholder, below) on the far wall facing the doorway,
- the key in its lap,
- the clue card `Readable` on the corridor wall beside the doorway,
- light dressing that keeps the room **pretty empty**: bare floorboards, one small chair or rug under the doll, maybe a toy block or two near the walls. Nothing between the door and the doll, nothing tall enough to block the sight line.
- **Lighting:** one dim warm light near the doll so it reads clearly from the door (the player *must* be able to see it to stare at it), the rest dark. Leave the generic PuzzleRoom ceiling light off or very dim.

Don't put zombie patrol routes or spawns in or next to this room: a zombie forcing you to turn would be an unfair kill.

### Doll art

There's no doll model in the repo. Start with a primitive placeholder built by the editor script (white porcelain material: sphere head, tapered capsule body in a dress-coloured material, thin cylinder limbs, two black glossy sphere eyes, red mouth dot), sitting with legs out. Keep the `face` / `head` / `lap` anchors as named children so a real model can be dropped in later (Blender pipeline in `tools/blender/` or a CC0 asset, credited in `CREDITS.md`).

### Audio

`SoundCue` assets in `Assets/Art/Audio/Cues/` (an empty cue is a silent placeholder, so code can land before the clips):
- `Doll_MusicBox` — looping 3D nursery-tune music box via `AudioService.PlayLoop(cue, doll)`. Short range, audible in the corridor.
- `Doll_Giggle` — on the warning lurch.
- `Doll_Scream` — on the leap.
- `Doll_Scratch` — loop while on your face. `Grab_Sting` can stand in for the scream until there's a real one.
Any third-party clip goes in `CREDITS.md`. Use an out-of-copyright tune (e.g. a traditional nursery rhyme) for the music box.

---

## Tests (`Tests/EditMode/DollRuleTests.cs`)

At least:
- Dormant ignores blinks and look-away.
- Entering → Armed; first blink → Warned (event `Warned`); second blink → `Kill`.
- Unwatched for < grace → nothing; ≥ grace → `Kill` from Armed (no warning needed).
- Eyes shut from Armed → Warned; held past `shutKillSeconds` → `Kill`.
- Leave without key → Dormant, warning cleared; leave with key → `Escaped`, then Done ignores everything (blinks, look-away, re-entering).
- `Reset()` from Killing → Dormant; `Reset()` does nothing in Done.

---

## Tuning targets

- Door to doll and back should be about **4-6 s** with eyes open at walking speed. Natural blinks come every ~3-5 s, so most players will burn their warning; that's intended, and the giggle + lurch is the moment that teaches the rule.
- If playtests show near-certain death, the levers in order: shorten the room distance (move the doll closer to the door), widen `watchAngle`, then lengthen `lookAwayGrace` a little. Don't add a second warning.

## Done when

- [ ] Entering, looking away for a beat → doll leaps on your face, you die, respawn, doll and key are back, puzzle works again.
- [ ] One blink → doll is closer with a giggle when your eyes open; second blink → it's on your face when they open.
- [ ] Holding eyes shut in the room kills you.
- [ ] Walk in, take the key, back out staring at it → safe; the doll never moves again, even after a later death elsewhere.
- [ ] Leaving without the key and coming back gives you a fresh warning.
- [ ] Works with `blink_server/send_test_blinks.py blink` / `hold 2` (no webcam) and the real webcam.
- [ ] `DollRuleTests` pass; no console errors.
- [ ] `STORY_PLAN.md` P2 section, the route table and the build notes updated to this design; `ARCHITECTURE.md` mentions `WakeUpSequence.Respawned` if it was added.

## Out of scope

- Choosing the final room (default `PuzzleRoom_1` until decided).
- A real doll model and final audio (placeholders are fine).
- Zombies in this room.
