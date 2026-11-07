# BLINK: Story Plan (Act 1)

**You were sent to deliver a pocket watch to a mansion in the forest. You woke up in one of its beds.**

Scope: the intro video, the wake-up, and Act 1 only (the whole demo). The 7 puzzles are planned below in **Puzzles and keys**.

---

## The story in short

- **1946.** A fever reaches the Hale estate, deep in the forest. Eleanor Hale and the children, Tom (9) and Lily (6), fall sick.
- **Ambrose Hale**, a watchmaker, builds the **Great Clock** in the tower and stops time inside the walls at **11:58**, two minutes before the doctor said they'd die.
- It works, but only halfway. Nobody in the house dies and nobody lives. Eighty years of standing still rot the family and the staff into what you fight.
- Ambrose wants to restart the clock and let them go, but its heart, a **brass pocket watch**, was stolen by his apprentice, **Samuel Kerr**, who ran from the house.
- **2026.** Kerr's belongings are auctioned. Someone buys the watch and pays a courier to take it to the Hale estate. **That courier is you.**
- **Why blinking skips time:** inside the walls, time only moves while no one is looking. Shut your eyes and the house catches up. That's the rule the whole game runs on, and the intro shows it before you've touched a control.

---

## Opening: Blink to begin

No intro video. The game opens on black with **BLINK to begin**, and the world stays frozen.

- The first blink starts the wake-up: the eyelids open slowly with two heavy-eyed flutters, and the player sits up from lying on the bed (camera rises from the ceiling to level). About 6.5 seconds, then control and world time start.
- How you got here is never shown. The player pieces it together from the delivery docket on the nightstand and Ambrose's note on the floor.
- Code: `Scripts/Player/WakeUpSequence.cs` on GameSystems. Timing, lid curve and lying pose are all inspector fields.

---

## Act 1 route and what's in each room

This follows the current house blockout: bedroom → landing / side room → broken stairs → hallway → kitchen → basement generator → exit gate.

| Room | Story beat | Story notes | Photos / heirlooms | Puzzle slot |
|---|---|---|---|---|
| Bedroom (start) | Ambrose put you here. First zombie, first forced door. | Note 1 | Wedding photo, Eleanor's music box | P1 (tutorial) |
| Landing / side room (the nursery) | The children's room. Tom and Lily were here. | Note 2 | Lily's doll, Tom's tin soldier, children's photo | P2 |
| Broken stairs | A glimpse of the hall from the intro. The grandfather clock at the bottom. | | Family portrait (half fallen), grandfather clock | |
| Hallway | The servants' route. | Note 3 | Staff photo | P3 |
| Kitchen | The cook stayed to the end. | Note 4 | | P4 |
| Basement (generator) | Ambrose's old workshop below the house. He ran the clock's power from here. | Note 5 | Ambrose's empty watch case | P5 |
| Exit gate | End of Act 1. The tower light is on as you leave. | Note 6 | | |

---

## Story notes

Notes **hint** at the story; none of them explains it outright. Read with E, time keeps running while you read. The text lives in `Editor/StoryProps.cs` (menu **BLINK > Place Story Props**), which places them in the House scene.

| Note | Where | Hints at (cryptically) |
|---|---|---|
| Delivery docket | Bedroom nightstand | Why you came: a courier job, Kerr's pocket watch, "before midnight", the van clock stopping at the gate |
| Read this first (A.) | Bedroom floor | First note. You won't remember arriving; time moves strangely here; "keep your eyes open". P1: "if a thing will not move for you, stop watching it" |
| Child's drawing (Lily) | Nursery floor | P2: "Dolly only plays when nobody is looking... Tom blinked." |
| Torn diary page (E.) | Hallway floor | P4: faces turned to him. P3: "he keeps his cabinet for the moon... never slow while you're watching" |
| Kitchen list (Mrs Pell) | Kitchen floor | P5: "a watched pot never does"; valve: "left loosens, right binds" |
| Workshop scrap | On the generator | P6: "it comes up every time you close your eyes"; P7: "two short of midnight" (11:58) |
| Newspaper clipping | By the exit gate | Kerr's auction, the watch marked 'A. H.', an anonymous buyer |

**Puzzle-hint notes:** not written yet. They will use the same `Readable` component once the puzzles are decided.

---

## Family photos

Generated with Kie (Nano Banana 2), in `Unity/Assets/Art/Story/Photos/`, each with a clean and a `_decayed` version. They hang in frames and can be inspected (E) for a caption that hints at the story.

| Photo | Hung where | Version | Caption hints at |
|---|---|---|---|
| Wedding | Bedroom, above the nightstand | Clean | "Forever is a promise I intend to keep" |
| The children | Nursery, north wall | Decayed (faces run) | Tom and Lily |
| Family portrait | Stairwell wall | Decayed (faces scratched out) | Something went wrong with the Hales |
| The staff | Ground floor hallway | Clean | The apprentice, circled in ink: Samuel |

---

## Family heirlooms

Inspectable 3D objects: E to pick up and turn, with one line of text. Story only, no puzzle role unless we decide one later.

| Heirloom | Where | Text on inspect |
|---|---|---|
| Grandfather clock | Foot of the stairs | Stopped at 11:58. *"The pendulum is still. You can hear it ticking anyway."* |
| Eleanor's music box | Bedroom dresser | Plays a few notes, then stops. *"Engraved: 'For E., so you'll always have a song.'"* |
| Lily's porcelain doll | Nursery | *"Its eyes are closed. You're sure they were open a moment ago."* (They really were open; flip them on a blink.) |
| Tom's tin soldier | Nursery floor | *"One arm worn smooth from being held."* |
| Ambrose's watch case | Basement workbench | An empty velvet case shaped for a pocket watch. *"Whatever belonged here is what you were carrying."* |

---

## Puzzles and keys

Seven puzzles, one per room, placed in the blockout's empty puzzle rooms (`PuzzleRoom_1`-`_6` in `Editor/HouseBlockout.cs`). This replaces the P1-P5 slots in the route table above.

**The rule every puzzle follows:** blinking is the tool, and each puzzle uses it a different way. Some want you to speed time up, some punish you for blinking, one uses blinks as information.

- **Puzzle notes** (clues) are **typed on cards**. Story notes stay **handwritten**, so players can tell which ones matter for progress.
- **Nothing soft-locks.** Every fail resets the puzzle instead of losing it for good.
- **Keys** are `Pickup` items checked by an `Openable` door's `requiredItemId`. New item ids are listed per puzzle.

### Route

| # | Room (blockout) | Floor | Puzzle | Blink rule | Reward |
|---|---|---|---|---|---|
| P1 | `StartBedroom` | Second | The jammed door | Eyes shut gets work done | Out to `Corridor_Second`, plus the candle |
| P2 | Bungalow `Bathroom` (was `PuzzleRoom_1`) | Ground | Lily's doll | **Don't blink, don't look away** | `key_dorm`: opens `ServantsDorm` (attic hatch and ladder) |
| P3 | `PuzzleRoom_2` | Attic | Moonlight through the roof | **Speed time up** | `key_servants`: opens the servants' stair door down |
| P4 | `PuzzleRoom_3` | First | The watching portraits | **Blinks as information** | `wrench` |
| P5 | `PuzzleRoom_4` (dressed as the scullery) | Ground | Fire and pipes | Eyes shut gets work done | `key_basement`: opens the basement stair door in the `Kitchen` |
| P6 | Bungalow `B2` workshop (was `PuzzleRoom_5`) | Basement | The flooded basement | **Don't blink**, then blinks help | `key_workshop`: one of D1's three keys (was `PuzzleRoom_6`) |
| P7 | `PuzzleRoom_6` + `GeneratorRoom` | Basement | Burning note and generator | Don't blink, then eyes shut | Exit gate opens. End of Act 1 |

**Mix:** 3 reward closing your eyes (P1, P3, P5), 2 punish it (P2, P6), 1 reads blinks (P4), and the finale does both (P7).

**Playtime:** roughly 45-90 s per puzzle, so 6-9 minutes. That's longer than a judge's few minutes. **Cut order** if it runs long: P4 (portraits; leave the wrench on a table), then P3 (moonlight; plain locked door with the key nearby). Never cut P2 or P7: they're the best "don't blink" and "eyes shut" moments.

---

### P1. The jammed door (tutorial, `StartBedroom`)

- **Setup:** you wake in bed. The door (`Objective_JammedDoor`) is stuck. The candle is on the nightstand, `key_brass` is in a drawer.
- **Clue:** Note 1 (*"do not close your eyes for long"*) and the on-screen prompt *"Hold E to force the door"*.
- **Solve:**
  1. Pick up the candle.
  2. Hold E on the door. Progress pauses if you let go or walk away.
  3. Shut your eyes to speed it up (3×). The first zombie shambles in partway through.
- **Fail:** none. The zombie is slow and alone.
- **Teaches:** eyes shut gets work done, at the cost of not seeing what's coming.
- **Build:** done (`JammedDoor`, `Openable`, `Pickup`). Tuning: `forceTimer` duration (4 s now).

### P2. Lily's doll (bungalow `Bathroom`)

Full brief: [puzzles/LILYS_DOLL_ROOM.md](puzzles/LILYS_DOLL_ROOM.md). **Blink rule: don't blink, don't look away.**

- **Setup:** Lily's porcelain doll sits on a chair against the far wall, facing the door, with `key_dorm` (*"Lily's key"*) in her lap. A music box plays Brahms' Lullaby from her, audible from the hall.
- **Clue card** (on the hall wall beside the door, read before going in): *"Lily's doll hates to be ignored. Keep your eyes on her, and don't you dare blink."*
- **Solve:** walk in looking straight at her, take the key, and back out without taking your eyes off her. Safe once you're 0.4 m past the doorway with the key; from then on she's just a doll and the music stops.
- **Fail:**
  - **Look away** (more than 0.15 s outside a 30° cone, or behind something): she leaps onto your face.
  - **Blink once** (or shut your eyes): when they open she has lurched 1.2 m closer (never nearer than 1.5 m), head tilted, with a giggle, and the music box goes out of tune. **Blink again:** she's on your face when they open.
  - **Eyes held shut** past 1 s: on your face, and she's still there when you open them.
  - On your face: camera jolts, scratching, then you die (*"Lily wanted to play"*) and wake in bed. Doll and key reset; the warning is fresh.
  - Leaving without the key also forgives the warning.
- **Why it works:** blinks are involuntary, so the player fights their own eyes. The scariest use of the core rule.
- **Build:** done.
  - `Scripts/Puzzles/DollRule.cs` (pure rules, `Tests/EditMode/DollRuleTests.cs`) and `LilysDollRoom.cs` (looking, moving, kill, reset on `WakeUpSequence.Respawned`).
  - **BLINK > Place Puzzle: Lily's Doll** (`Editor/PuzzleProps.cs`) builds it in the room named by `Room`: trigger box, placeholder primitive doll, key, clue card, warm light, toy blocks. It moves the room's chair under her.
  - Cues `Doll_MusicBox`, `Doll_Giggle` (silent placeholder), `Doll_Scream` (`Grab_Sting` clips), `Doll_Scratch`. Clips from `tools/audio/doll_sounds.py`.
- **Tuning:** door to doll and back is 4-6 s at walking speed (6.3 m here). If it's near-certain death: move her closer, widen `watchAngle`, then lengthen `lookAwayGrace` a little. Don't add a second warning.

### P3. Moonlight through the roof (`PuzzleRoom_2`, attic)

- **Setup:**
  - Rotten roof with a hole straight through to the sky.
  - A brass plaque is set in the floor and a locked cabinet stands against the wall.
  - It's daylight or dusk when you arrive, so the plaque is dull and unreadable.
- **Clue:** carved over the cabinet: *"Only the moon may open me."*
- **Solve:**
  1. Blink or shut your eyes to push the clock into night (`GameClock` jumps on every blink, runs 3× with eyes shut).
  2. The moon shaft slides across the floor (`DayNightCycle` already moves the moon).
  3. Between about 23:00 and 01:00 the shaft is on the plaque. It glows, the cabinet unlocks, and `key_servants` is inside.
- **Fail:** none. Once the cabinet has opened it stays open, so overshooting the window can't lock you out.
- **Pressure:** zombies wander up through the hatch while you fast-forward.
- **Build:**
  - **Blockout:** `PuzzleRoom_2` has a flat ceiling (`PuzzleRoom_2_Ceiling`). Cut a hole in it and in the roof above the plaque. Ask the mansion session.
  - Plaque glow and cabinet unlock: `TimeOfDayToggle` (23:00-01:00) enables a light and calls `Openable.SetOpen(true)` once. That needs a ~10-line latch script.
  - Not real light-hits-plaque detection: the hours fake it. Aim the hole so it *looks* right at those hours.
- **Tuning:** the time you arrive. If it's 18:00, about 5 hours of game time is needed. Check how many blinks and eyes-shut seconds that is with `GameClock`'s rate.

### P4. The watching portraits (`PuzzleRoom_3`)

- **Setup:**
  - Five Hale family portraits around the room. Ambrose's hangs over a wall safe.
  - The staff photo is on a side table.
- **Clue card:** *"The family always faced the master."*
- **Solve:**
  1. Each blink, every face in the portraits turns a little.
  2. After 3-4 blinks, all of them look at the same portrait: Ambrose's.
  3. Interact with Ambrose's portrait: it swings open (`Openable`), and the safe behind it holds the `wrench`.
- **Fail:** none, it just costs blinks. The pressure is the zombies coming in while you watch.
- **Why it works:** "things change when you're not looking", used as a clue instead of a threat.
- **Build:**
  - `WatchingPortraits` (new, small). On each blink skip, advance every portrait one face stage: swap the texture, or rotate a face quad, towards the target.
  - Ambrose's portrait is an `Openable` with `requiredItemId` blank, but it only opens once the faces have finished turning (the script unlocks it).
- **Art:** 2-3 face stages per portrait (front → three-quarter → turned).

### P5. Fire and pipes (`PuzzleRoom_4`, dressed as the scullery)

- **Setup:**
  - Mrs. Pell's writing desk at the back with `key_basement` on it.
  - A fire from the range has spread across the floor in front of the desk.
  - A rusted water pipe runs along the ceiling to a valve joint by the door.
- **Clue:** Note 4 (the pot that never boils), plus a card on the valve: *"Mains. Anticlockwise. Mind the rust."*
- **Solve:**
  1. Bring the `wrench` from P4.
  2. Hold E on the valve joint. It's rusted, so it takes about 6 s. Shut your eyes to speed it up.
  3. The pipe bursts and water pours onto the fire. The fire dies over about 1 s.
  4. Walk to the desk and take the key.
- **Fail:** walking into the fire hurts (damage per second). Letting go of E pauses the valve, it doesn't reset.
- **Pressure:** zombies come in through the kitchen while you hold E with your eyes shut.
- **Build:**
  - The valve is a copy of `JammedDoor`'s hold logic, gated on `Inventory.Has("wrench")`. Better: give `JammedDoor` an optional `requiredItemId` and reuse it.
  - The fire is a particle system, a point light and a damage trigger, switched off when the valve completes.
  - The water is a particle system turned on at the same time.

### P6. The flooded basement (bungalow `B2` workshop)

Full brief: [puzzles/FLOODED_BASEMENT.md](puzzles/FLOODED_BASEMENT.md). **Blink rule: don't blink (each blink is a big jump in the water). Once the drain is open, blinking helps.**

- **Setup:** a bath stands against the far wall of the workshop, facing door D5, its tap dripping. A rusted cover sits over a drain in the middle of the floor. Around the walls: a chest of three drawers, a tall cupboard, a green tool locker and a crate with a hinged lid.
- **Clue card** (nailed to a stake by the drain, only useful once you're in trouble): *"Drain blocked AGAIN. The cover's rusted solid, you'll need a bar to shift it. — Groundsman"*
- **Solve:**
  1. Walk 1.5 m in: the tap runs and the bath spills over. The water creeps up (0.012 m/s, about 2 min to the eyes with no blinks).
  2. Search the six spots for the `crowbar` (a different one every attempt, never the last one).
  3. Crowbar in hand, E on the drain: the cover flips off with a clang, the tap splutters out and the water spirals away (0.35 m/s, 3-5 s). Blinks now drain it faster.
  4. `key_workshop` (*"Workshop key"*) was snagged in the drain basket. Take it once the water's gone.
- **Fail:**
  - **Each blink** is a 0.32 m surge (hidden behind the eyelids, eased up over 0.25 s as they open, with a slosh). **Eyes held shut:** one surge every 0.5 s. About 5 blinks from dry is death.
  - **Water at eye height** (read live, so crouching drowns you sooner): green-brown underwater tint, bubbles, 1.2 s, then *"You drowned"*. The world muffles as the water closes in on the eyes, and you wade at 70 % speed once it's past 0.9 m.
  - **Respawn** is at a checkpoint just outside the door (`WakeUpSequence.SetCheckpoint`), not the bed. The room resets: dry, tap dripping, cover back on, crowbar put away until the next attempt.
- **Solved** is permanent: a later death elsewhere doesn't reset it (the checkpoint is cleared).
- **Why it works:** it flips the rule twice: blinking is what drowns you, then once the drain is open it's what saves time.
- **Build:** done, except the door.
  - `Scripts/Puzzles/FloodLevel.cs` (pure rules, `Tests/EditMode/FloodLevelTests.cs`), `FloodedBasement.cs` (trigger, crowbar, surface and surge, tap, whirlpool, drowning, reset on `WakeUpSequence.Respawned`) and `DrainCover.cs`.
  - **The door slam and lock aren't built** (someone else has the door). `FloodedBasement` raises `FloodStarted` / `onFloodStarted` (slam and lock D5), `Solved` / `onSolved` (unlock and swing it open) and `WasReset` / `onReset` (open and unlocked again after a drowning). Until the door exists you can walk out; the water can't drown you outside the room.
  - **BLINK > Place Puzzle: Flooded Basement** (`Editor/PuzzleProps.FloodedBasement.cs`) builds it in the room named by `FloodRoom` (`B2`, found by `Floor_B2`), with the doorway from `FloodDoor` (`D5`). Run it before Setup Doors so D1 counts the real `key_workshop`.
  - Cues `Flood_Drip`, `Flood_TapRun`, `Flood_TapStop`, `Flood_Surge`, `Flood_Gurgle`, `Flood_CoverClang`, `Flood_Drown`. Clips from `tools/audio/flood_sounds.py`.
  - `PlayerController.SpeedMultiplier` (wading).
- **Tuning:** search to drain-open should take 15-25 s and survive about 3 blinks. If it's near-certain death: lower `blinkJump`, then fewer search spots, then lower `riseRate`. Don't remove the blink jump.

### P7. Burning note and generator (`PuzzleRoom_6` + `GeneratorRoom`, the finale)

- **Setup:**
  - `PuzzleRoom_6` is Ambrose's workshop: workbench, the empty watch case (heirloom) and Note 5.
  - Ambrose's gate-code note is smouldering in an ashtray on the bench.
  - The `GeneratorRoom` next door has the generator. The exit gate has 4 number dials and needs power.
- **Clue:** the note itself: *"The gate answers to the moment this house stopped. 11 : 58. — A. H."*
- **Solve:**
  1. Read the note before it burns. Once you're within 6 m, each 0.5 s of world time you don't watch burns it a stage: clear → partly burned → mostly burned → *"Only ash."*
  2. Start the generator (E). It takes 30 s of world time to charge, so shut your eyes to speed it up while the horde pours in.
  3. Turn the gate dials to **1158**. The gate opens once the dials match and the generator is charged.
- **Fail:**
  - **Note burned:** still solvable. 11:58 is the time the house stopped, shown on every clock and in the notes.
  - **Grabbed while charging:** the QTE interrupts the fast-forward. The generator keeps its charge.
- **Why it works:** both rules in one room, back to back: first *don't blink*, then *you have to*.
- **Build:**
  - `BurningNote`, `Generator`, `CodeDial` and `CodeLock` (drafted).
  - The gate is an `Openable` with an item id nobody carries (e.g. `gate-power`), so it only opens through `CodeLock`.
  - `Generator.poweredOn` lists the lamps and gate light to switch on.

---

### New item ids

| Id | Where it's found | What it opens |
|---|---|---|
| `key_dorm` | Doll's lap (P2) | `ServantsDorm` door (attic hatch) |
| `key_servants` | Moonlight cabinet (P3) | Servants' stair door |
| `wrench` | Portrait safe (P4) | The valve in P5 |
| `key_basement` | Mrs. Pell's desk (P5) | Basement stair door in the `Kitchen` |
| `crowbar` | A random drawer or cupboard in the flooded basement (P6), re-hidden each attempt | Levers the drain cover off (used up) |
| `key_workshop` | Drain basket, once the water's gone (P6) | Front door D1 (one of three keys; was `PuzzleRoom_6`) |

Existing ids (`candle`, `key_brass`, `key_iron`, cutlery) are unchanged. `key_brass` and `key_iron` aren't used by any puzzle yet: either give them a door or drop them.

### Build order

1. **P2 and P7** first: they're the strongest moments. P2 is built (bungalow bathroom); P7's scripts are drafted.
2. **P5 and P6** next. P6 is built (bungalow basement, all but its door); the bath tap is the flood's source now, so it no longer chains from P5's burst pipe.
3. **P3:** needs the ceiling and roof hole from the mansion session.
4. **P4** last: needs portrait art, and it's first on the cut list.

---

## Start screen

**Blink to begin** (see Opening). New Game / Continue menu is dropped for the jam: a judge plays one 2-3 minute run.

---

## What needs building

| Item | Status |
|---|---|
| Wake-up (`WakeUpSequence`) | Done, needs a play test |
| `Readable` (notes and photos) | Done, needs a play test. Depends on the uncommitted Interaction folder (`IInteractable`, `Interactor`) |
| Story props placement (`StoryProps` menu) | Done. Run **BLINK > Place Story Props** in House.unity and check positions |
| Heirlooms | Not started |
| Puzzles, puzzle notes, keys | P2 (Lily's doll) built in the bungalow bathroom: **BLINK > Place Puzzle: Lily's Doll**. P7 scripts drafted, not in the repo yet. The rest planned (see **Puzzles and keys**) |
