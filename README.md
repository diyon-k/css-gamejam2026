# BLINK

**Time only moves when you blink.**

CSS Game Jam 2026 (University of Bristol), theme: **TIME**. Unity (URP), C#, plus a Python webcam sidecar.

Two outlaws stand back to back in a saloon, surrounded by gunmen. A webcam watches both players' faces. While your eyes are open, the world is frozen. Blink, and time moves. In co-op, time moves when *either* of you blinks, so you have to talk.

## Layout (planned)
- `blink_server/`: Python MediaPipe blink detector that sends player eye state to Unity over UDP (`127.0.0.1:5065`)
- `Unity/`: the game project

## Webcam blink test
Run the setup once (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)), then from the repo root:

```powershell
cd blink_server
.venv\Scripts\python.exe blink_server.py --preview    # macOS/Linux: .venv/bin/python
```

- `q` / Esc closes the window.
- Turning your head more than 30° away from the camera counts as eyes open. Change it with `--max-turn 25`.
- Other options: `--camera 1` picks another webcam, and `--close` / `--open` set the blink thresholds.

## Controls (fallback)
- P1: mouse aim + LMB fire, `Space` = blink
- P2: gamepad right stick + RT fire, South button / `RightShift` = blink
