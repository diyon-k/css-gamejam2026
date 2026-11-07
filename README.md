# BLINK

**Time only moves when you blink.**

CSS Game Jam 2026 (University of Bristol), theme: **TIME**. Unity (URP), C#, plus a Python webcam sidecar.

Two outlaws stand back to back in a saloon, surrounded by gunmen. A webcam watches both players' faces. While your eyes are open, the world is frozen. Blink, and time moves. In co-op, time moves when *either* of you blinks, so you have to talk.

## Layout (planned)
- `blink_server/`: Python MediaPipe blink detector that sends player eye state to Unity over UDP (`127.0.0.1:5065`)
- `Unity/`: the game project

## Controls (fallback)
- P1: mouse aim + LMB fire, `Space` = blink
- P2: gamepad right stick + RT fire, South button / `RightShift` = blink
