"""Run: python test_blink_server.py"""
from blink_server import EyeHysteresis

eyes = EyeHysteresis(close_at=0.5, open_at=0.35)
assert eyes.update(0.2) is False
assert eyes.update(0.45) is False   # between thresholds, still open
assert eyes.update(0.6) is True     # closed
assert eyes.update(0.4) is True     # between thresholds, stays closed
assert eyes.update(0.3) is False    # reopened
print("ok")
