"""Run: python test_blink_server.py"""
from types import SimpleNamespace as P

import math
import os
import subprocess
import sys

import cv2

from blink_server import EyeHysteresis, camera_backend, closest_face, head_turn_deg, process_alive

eyes = EyeHysteresis(close_at=0.5, open_at=0.35)
assert eyes.update(0.2) is False
assert eyes.update(0.45) is False   # between thresholds, still open
assert eyes.update(0.6) is True     # closed
assert eyes.update(0.4) is True     # between thresholds, stays closed
assert eyes.update(0.3) is False    # reopened

small = [P(x=0.1, y=0.1), P(x=0.2, y=0.2)]       # 0.1 x 0.1
big = [P(x=0.4, y=0.3), P(x=0.9, y=0.8)]         # 0.5 x 0.5
wide_flat = [P(x=0.0, y=0.5), P(x=1.0, y=0.52)]  # 1.0 x 0.02: wide but small area
assert closest_face([]) is None
assert closest_face([small]) == 0
assert closest_face([small, big]) == 1
assert closest_face([big, small, wide_flat]) == 0
assert camera_backend("win32") == cv2.CAP_DSHOW
assert camera_backend("darwin") == cv2.CAP_AVFOUNDATION
assert camera_backend("linux") == cv2.CAP_ANY

def yaw(deg):  # rotation about the vertical axis (head turned left/right)
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [[c, 0, s, 0], [0, 1, 0, 0], [-s, 0, c, 0], [0, 0, 0, 1]]

assert abs(head_turn_deg(yaw(0))) < 1e-6
assert abs(head_turn_deg(yaw(45)) - 45) < 1e-6
assert abs(head_turn_deg(yaw(-60)) - 60) < 1e-6

assert process_alive(os.getpid()) is True
finished = subprocess.Popen([sys.executable, "-c", "pass"])
finished.wait()
assert process_alive(finished.pid) is False  # exited and reaped
print("ok")
