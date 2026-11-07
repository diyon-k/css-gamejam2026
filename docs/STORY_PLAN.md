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

## Opening: Blink to begin

No intro video. The game opens on black with **BLINK to begin**, and the world stays frozen.

- The first blink (or Space) starts the wake-up: the eyelids open slowly with two heavy-eyed flutters, and the player sits up from lying on the bed (camera rises from the ceiling to level). About 6.5 seconds, then control and world time start.
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

| Note | Where | Hints at |
|---|---|---|
| Delivery docket | Bedroom nightstand | Why you came: a courier job, a pocket watch from Kerr's estate, "before midnight" |
| Note in shaky handwriting (A.) | Bedroom floor | Who knocked you out, that you brought something back, the blink rule |
| Child's drawing (Lily) | Nursery floor | Staying still to beat the sickness; a man with a clock for a face |
| Torn diary page (E.) | Hallway floor | Ambrose not sleeping, clocks going wrong, something in the tower |
| Kitchen list (Mrs Pell) | Kitchen floor | Time has stopped: bread won't stale, kettle won't boil |
| Workshop scrap | On the generator | The "heart" was stolen by S.; someone will bring it home |
| Newspaper clipping | By the exit gate | Kerr's auction, the watch with Hale's mark, an anonymous buyer |

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

## Puzzles and keys (to be decided)

- **Puzzle objects:** general house objects the player can use (to be listed once the puzzles are decided).
- **Puzzle notes:** a separate set of notes that hold clues (codes, hints, orders). Keep them visually different from story notes, e.g. **typed on cards** where story notes are **handwritten**, so players know which ones matter for progress.
- **Keys:** finishing puzzles gives keys that open the way forward. Which puzzle gives which key, and which door each key opens, is still open.
- Puzzle slots P1-P5 above mark where they would sit on the route. P1 should be trivial, since it teaches the interaction.

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
| Puzzles, puzzle notes, keys | To be decided |
