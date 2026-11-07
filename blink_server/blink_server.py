"""Webcam blink sidecar: MediaPipe Face Landmarker -> eye open/closed -> UDP packets for Unity.

Sends one JSON datagram per camera frame (~30 Hz) to 127.0.0.1:5065:
    {"t": <ms>, "present": bool, "closed": bool}

Usage:
    python blink_server.py                 # default camera
    python blink_server.py --preview       # show the camera with the blink score (q / Esc to quit)
    python blink_server.py --camera 1 --close 0.45 --open 0.3
"""

import argparse
import json
import os
import socket
import sys
import time
import urllib.request
from pathlib import Path

import cv2
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions, vision

HOST = "127.0.0.1"
PORT = 5065
MODEL_PATH = Path(__file__).with_name("face_landmarker.task")
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task"


class EyeHysteresis:
    """Closed once the blink score goes above `close_at`; open again only once it drops below `open_at`."""

    def __init__(self, close_at: float, open_at: float):
        self.close_at, self.open_at, self.closed = close_at, open_at, False

    def update(self, score: float) -> bool:
        if self.closed and score < self.open_at:
            self.closed = False
        elif not self.closed and score > self.close_at:
            self.closed = True
        return self.closed


def closest_face(face_landmarks) -> int | None:
    """Index of the face with the largest landmark bounding box (= nearest the camera), or None if no faces."""
    def area(lms):
        xs, ys = [p.x for p in lms], [p.y for p in lms]
        return (max(xs) - min(xs)) * (max(ys) - min(ys))
    return max(range(len(face_landmarks)), key=lambda i: area(face_landmarks[i]), default=None)


def blink_score(result) -> float | None:
    """Average of eyeBlinkLeft/Right for the closest face, or None if no face."""
    i = closest_face(result.face_landmarks)
    if i is None or not result.face_blendshapes:
        return None
    scores = {c.category_name: c.score for c in result.face_blendshapes[i]}
    return (scores["eyeBlinkLeft"] + scores["eyeBlinkRight"]) / 2


def process_alive(pid: int) -> bool:
    """Is process `pid` still running?"""
    if sys.platform == "win32":
        # os.kill(pid, 0) would send CTRL_C on Windows, so ask the kernel instead.
        import ctypes
        k32 = ctypes.windll.kernel32
        handle = k32.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE
        if not handle:
            return False
        alive = k32.WaitForSingleObject(handle, 0) == 0x102  # WAIT_TIMEOUT = still running
        k32.CloseHandle(handle)
        return alive
    try:
        os.kill(pid, 0)  # signal 0: existence check only, nothing is sent
    except ProcessLookupError:
        return False
    except PermissionError:
        return True  # exists, owned by someone else
    return True


def camera_backend(platform: str = sys.platform) -> int:
    """OpenCV capture API for this OS. DirectShow opens much faster than MSMF on Windows but doesn't exist elsewhere."""
    if platform == "win32":
        return cv2.CAP_DSHOW
    if platform == "darwin":
        return cv2.CAP_AVFOUNDATION
    return cv2.CAP_ANY


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--camera", type=int, default=0, help="camera index (default 0)")
    parser.add_argument("--close", type=float, default=0.5, help="blink score above this = closed")
    parser.add_argument("--open", type=float, default=0.35, help="blink score below this = open again")
    parser.add_argument("--preview", action="store_true", help="show a camera window with the score")
    parser.add_argument("--parent-pid", type=int, help="exit when this process dies (Unity passes its own pid)")
    args = parser.parse_args()

    if not MODEL_PATH.exists():
        print(f"downloading {MODEL_PATH.name} ...")
        part = MODEL_PATH.with_suffix(".part")  # an interrupted download must not leave a broken model behind
        urllib.request.urlretrieve(MODEL_URL, part)
        part.replace(MODEL_PATH)

    cap = cv2.VideoCapture(args.camera, camera_backend())
    if not cap.isOpened():
        hint = (" On macOS, allow camera access for the app that started this (Terminal, or Unity when launched on Play)"
                " in System Settings > Privacy & Security > Camera.") if sys.platform == "darwin" else ""
        raise SystemExit(f"could not open camera {args.camera}.{hint}")

    options = vision.FaceLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(MODEL_PATH)),
        running_mode=vision.RunningMode.VIDEO,
        num_faces=3,
        output_face_blendshapes=True,
    )
    eyes = EyeHysteresis(args.close, args.open)
    start = time.monotonic()
    last_ts = -1
    blinks = 0

    with vision.FaceLandmarker.create_from_options(options) as landmarker, \
            socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        print(f"sending to {HOST}:{PORT} from camera {args.camera} (Ctrl+C to stop)")
        frame_no = 0
        while True:
            frame_no += 1
            if args.parent_pid and frame_no % 30 == 0 and not process_alive(args.parent_pid):
                break  # Unity crashed or was killed: free the webcam
            ok, frame = cap.read()
            if not ok:
                time.sleep(0.05)  # camera unplugged / taken: don't spin a core
                continue
            ts = max(int((time.monotonic() - start) * 1000), last_ts + 1)  # VIDEO mode needs strictly increasing
            last_ts = ts
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)

            score = blink_score(result)
            present = score is not None
            was_closed = eyes.closed
            closed = eyes.update(score) if present else False
            eyes.closed = closed  # face lost resets to open, so a lost face isn't counted as a blink every frame
            if was_closed and not closed:
                blinks += 1
                print(f"BLINK #{blinks}", flush=True)
            packet = {"t": int(time.time() * 1000), "present": present, "closed": closed}
            sock.sendto(json.dumps(packet).encode("utf-8"), (HOST, PORT))

            if args.preview:
                label = "NO FACE" if not present else f"{'CLOSED' if closed else 'open'}  {score:.2f}"
                colour = (0, 0, 255) if closed or not present else (0, 200, 0)
                cv2.putText(frame, label, (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.1, colour, 2)
                cv2.putText(frame, f"blinks: {blinks}", (20, 80), cv2.FONT_HERSHEY_SIMPLEX, 1.1, (255, 255, 255), 2)
                cv2.imshow("blink_server", frame)
                if cv2.waitKey(1) in (ord("q"), 27):
                    break

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
