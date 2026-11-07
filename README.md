# BLINK

**Every time you blink, time skips forward. Use it, but only when it's safe.**

A first-person, single-player zombie escape game for the CSS Game Jam 2026 (University of Bristol), theme **TIME**. A webcam watches your eyes: each blink jumps the world forward, and keeping them shut fast-forwards it. Your reload finishes and the door gives way, but the zombies keep coming.

Unity 6 (URP), C#, plus a small Python webcam sidecar.

## Layout

| Path | What's there |
|---|---|
| `Unity/` | The game project (Unity 6000.6). Code in `Assets/Scripts/`, tests in `Assets/Tests/` |
| `blink_server/` | Python MediaPipe sidecar: sends eye state to Unity over UDP `127.0.0.1:5065` |
| `docs/GAME_PLAN.md` | The design: core loop, level, look and sound, timeline |
| `docs/ARCHITECTURE.md` | How the code fits together, the contracts, who owns what |
| `CREDITS.md` | Third-party assets and licences (add yours when you import one) |

## Quick start

1. Open `Unity/` in Unity 6000.6.
2. Once, for the real webcam, from the repo folder:
   - Windows: `powershell -ExecutionPolicy Bypass -File blink_server/setup.ps1`
   - macOS / Linux: `sh blink_server/setup.sh`
3. Open `Assets/Scenes/House.unity` and press Play. The sidecar starts automatically. No webcam? `Space` is the blink key.

Tuning and troubleshooting the sidecar: see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#try-it).

## Controls

| Input | Action |
|---|---|
| WASD / left stick | Move |
| Mouse / right stick | Look |
| Shift | Sprint |
| Left click / right trigger | Shoot |
| R / gamepad West | Reload |
| Hold E | Force the jammed door |
| **Blink** (or tap Space) | World skips 0.5 s |
| **Eyes shut** (or hold Space) | World runs at 3× until you open them |
| F1 | Blink debug overlay |
| Esc | Free the cursor |

## Working on it

- Branch per feature, PR into `main`, [Conventional Commits](https://www.conventionalcommits.org) (`feat(mobs): ...`).
- Scenes don't merge well. Build in your own scene from the shared prefabs; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#working-together-without-merge-pain).
- Turn on Unity's scene/prefab merge tool once per clone (`.gitattributes` already routes Unity files to it):
  ```sh
  # Windows (adjust the version folder); on macOS the tool is in Unity.app/Contents/Tools/UnityYAMLMerge
  git config merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
  git config merge.unityyamlmerge.recursive binary
  ```
