# BLINK: Story Plan (Act 1)

**You were sent to deliver a pocket watch to a mansion in the forest. You woke up in one of its beds.**

Scope: the intro video, the wake-up, and Act 1 only (the whole demo, 2-3 minutes of play). Puzzles and keys are placeholders until we decide them.

---

## The story in short

- **1946.** A fever reaches the Hale estate, deep in the forest. Eleanor Hale and the children, Tom (9) and Lily (6), fall sick.
- **Ambrose Hale**, a watchmaker, builds the **Great Clock** in the tower and stops time inside the walls at **11:58**, two minutes before the doctor said they'd die.
- It works, but only halfway. Nobody in the house dies and nobody lives. Eighty years of standing still rot the family and the staff into what you fight.
- Ambrose wants to restart the clock and let them go, but its heart, a **brass pocket watch**, was stolen by his apprentice, **Samuel Kerr**, who ran from the house.
- **2026.** Kerr's belongings are auctioned. Someone buys the watch and pays a courier to take it to the Hale estate. **That courier is you.**
- **Why blinking skips time:** inside the walls, time only moves while no one is looking. Shut your eyes and the house catches up. That's the rule the whole game runs on, and the intro shows it before you've touched a control.

---

## Intro video (about 56 seconds, one continuous shot)

**Look:** first-person, handheld, like a horror film. Night, fog, a torch beam as the main light, muted colours, film grain. The camera never cuts. The only "cuts" are the courier's blinks, which also hide the seams between generated clips.

**Making it with Kie:** video models give us about 8 seconds per clip, so we generate 7 clips and chain them. The last frame of each clip becomes the start image of the next. Seams sit on blinks or on dark frames so the join doesn't show. For clip 5, use a screenshot of our in-game entrance hall as the start image so the video matches the level.

**Style prefix for every prompt:**
> First-person POV, handheld camera, cinematic horror film, night, thick fog, cold blue moonlight, warm torch beam, film grain, muted desaturated colours, 35mm, slow deliberate movement, no text, no on-screen captions.

| # | Time | Shot (prompt body) | Sound |
|---|---|---|---|
| 1 | 0-8 s | Inside a delivery van on a narrow forest road at night. Headlights on fog and pine trunks. A small brown parcel on the passenger seat. The dashboard clock flickers to **11:58** and freezes. The engine sputters and dies and the headlights fade. | Engine, radio static, then silence. One clock tick. |
| 2 | 8-16 s | The driver's door opens. Walking into the forest with a torch in one hand and the parcel in the other. Pale human figures stand perfectly still between the trees at the edge of the beam. | Footsteps on leaves, breathing. The ticking speeds up. |
| 3 | 16-24 s | Rusted iron gates hang open. Beyond them, a decaying Victorian mansion swallowed by ivy, and one warm lit window at the top of a clock tower. Walking up the overgrown path. | Wind, a gate creaking, a distant bell. |
| 4 | 24-32 s | The porch. A gloved hand knocks three times on a heavy wooden door. The door creaks open on its own, showing a dark hall. Stepping inside. | Three knocks. Long creak. The door slams shut behind. |
| 5 | 32-40 s | The entrance hall: dust sheets over furniture, a grandfather clock stopped at 11:58. The torch drifts up the grand staircase. Four figures stand at the top: a woman, two children and a servant, perfectly still, faces in shadow. | Total silence. Then a ticking sound coming from the parcel. |
| 6 | 40-48 s | A blink (one black frame): the figures are halfway down the stairs, still frozen. Another blink: they're at the foot of the stairs, a few metres away. The torch shakes. | A sharp skip sound on each blink, a heartbeat rising. |
| 7 | 48-56 s | Spinning back towards the front door. A tall, gaunt old man in a waistcoat stands inches away, holding a heavy brass clock weight. He swings it. The view jolts, tilts to the floor, the torch rolls away, and everything fades to black. | Impact, the torch clattering, a ringing in the ears. A whisper: *"Forgive me."* Ticking in the dark. |

**Clip 6 teaches the mechanic.** The family only moves while the courier blinks. Players have already seen the rule before they play.

---

## Waking up (in-engine, about 10 seconds)

- Black screen with ticking. The eyes open slowly, blurry, and the vision blinks clear twice. (Use the existing eyelid effect from `feat/blink-eyelids`.)
- The player is lying in the bed in the **upstairs bedroom** (Area 1 in the blockout) and sits up. Control starts.
- The parcel is gone. A note on the bedside table is the first thing in view (**Note 1**).
- The jammed bedroom door works as before: force it with eyes shut.

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

Pick up with E to read full screen. Time **keeps running while you read**: reading is an eyes-open action, so it's a risk, not a pause.

**Note 1 (bedside, handwritten, shaky)**
> Courier,
> Forgive the blow. You would have run, and I need what you carried.
> Whatever you see in this house, do not close your eyes for long. They only move when you aren't looking.
> Come down to me. I will explain everything.
> — A. H.

**Note 2 (nursery, Eleanor's diary page)**
> 3rd March 1946
> Lily's fever broke at noon and came back by supper. Tom keeps asking why Father is in the tower all night. I told him Father is making time. I do not know what else to call it.

**Note 3 (hallway, Butler's ledger)**
> 11th March. Dr. Ashby has given the mistress and the children until midnight. The master has asked all staff to remain in the house tonight and to stop every clock but his own.
> 11:58. The house has gone very quiet.

**Note 4 (kitchen, a note from the cook, Mrs. Pell)**
> I have been stirring this pot for what feels like a hundred years and it has not boiled. The bread will not go stale. My hands will not stop being cold.
> If anyone reads this, do not let him wind it again.

**Note 5 (basement, Ambrose's workshop notes)**
> The clock holds. They do not die. But they do not wake either, and when I shut my eyes they move. Closer each time.
> Samuel has taken the heart and run. Without it I cannot start it again and I cannot let them go.
> I will wait. Someone will bring it back.

**Note 6 (by the exit gate, newspaper clipping, 2026)**
> **AUCTION: Estate of the late Samuel Kerr.** Lot 14: Victorian brass pocket watch, maker's mark "A. Hale." Note from the seller: *the watch has never stopped ticking, though it has no spring.*

---

## Family photos

Framed photos the player can inspect. Each one has two versions: **clean** (1946) and **decayed** (water-damaged, faces scratched out or blurred). Optional extra: the photo swaps to the decayed version after enough blinks, using the existing decay system.

**Style prefix for image prompts:**
> Authentic 1940s black and white photograph, silver gelatin print, slight sepia, soft focus, vignette, film grain, worn edges, English country house.

| Photo | Where | Prompt body | Text on inspect |
|---|---|---|---|
| Wedding photo | Bedroom | Formal wedding portrait of a tall thin man with a neat moustache and a smiling woman with pinned dark hair, standing in front of a stone doorway. | *"Ambrose & Eleanor, June 1934."* |
| The children | Nursery | A boy of nine in shorts holding a toy soldier and a girl of six holding a porcelain doll, sitting on garden steps, squinting at the sun. | *"Tom and Lily, summer 1945."* |
| Family portrait | Stairs (hanging crooked) | Formal family portrait of a father, mother, son and daughter in a wood-panelled hall, with a tall grandfather clock behind them. | *"The Hales."* Decayed version: the four faces scratched out. |
| The staff | Hallway | Five household servants in uniform lined up outside a large country house: butler, cook, two maids, a young apprentice in a leather apron. | *"Staff, 1945. Samuel (far right) joined in the spring."* |

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

## Puzzles and keys (to be decided)

- **Puzzle objects:** general house objects the player can use (to be listed once the puzzles are decided).
- **Puzzle notes:** a separate set of notes that hold clues (codes, hints, orders). Keep them visually different from story notes, e.g. **typed on cards** where story notes are **handwritten**, so players know which ones matter for progress.
- **Keys:** finishing puzzles gives keys that open the way forward. Which puzzle gives which key, and which door each key opens, is still open.
- Puzzle slots P1-P5 above mark where they would sit on the route. P1 should be trivial, since it teaches the interaction.

---

## Start screen

- **Background:** a looping video of the mansion in fog with the tower window lit and a slow push-in (one more Kie clip, using the clip 3 look).
- **Buttons:** **New Game** and **Continue**. Continue only appears if a save exists.
- **New Game:** plays the intro video (any key skips), then loads the House scene with the player in bed and the wake-up sequence playing.
- **Continue:** loads the House scene at the last checkpoint, skipping the intro.

---

## What needs building

| Item | Notes |
|---|---|
| `Readable` (note) | A new `IInteractable`. Shows the note text full screen until E / Esc. Time keeps running. |
| `Inspectable` (photo / heirloom) | A new `IInteractable`. Holds the object up to the camera so you can rotate it, with a caption. Could share code with `Readable`. |
| Intro scene | A `VideoPlayer` on a black canvas, skip on any key, then load House. |
| Wake-up | A short camera move plus eyelid effect at the start of House. |
| Start menu scene | Background video, New Game / Continue. Save = last checkpoint in `PlayerPrefs`. |
| Art | 7 intro clips + 1 menu loop (Kie), 4 photos × 2 versions (image gen), note paper textures. |
