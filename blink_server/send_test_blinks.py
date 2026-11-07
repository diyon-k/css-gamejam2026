"""Send fake eye-state packets to Unity, to test the UDP path without a webcam.

Speaks the same protocol as the real sidecar (blink_server.py, not written yet):
    {"t": <ms>, "present": bool, "closed": bool}  ->  UDP 127.0.0.1:5065, ~30 Hz

Usage:
    python send_test_blinks.py blink           # one quick blink (150 ms)
    python send_test_blinks.py hold 2          # eyes shut for 2 seconds (fast-forward)
    python send_test_blinks.py away 1          # face missing for 1 second
    python send_test_blinks.py open 5          # stream "eyes open" for 5 seconds
    python send_test_blinks.py demo            # open, blink, open, hold, open
"""

import argparse
import json
import socket
import time

HOST = "127.0.0.1"
PORT = 5065
RATE_HZ = 30


def stream(sock: socket.socket, seconds: float, *, closed: bool, present: bool = True) -> None:
    """Send the same eye state at RATE_HZ for `seconds`."""
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        packet = {"t": int(time.time() * 1000), "present": present, "closed": closed}
        sock.sendto(json.dumps(packet).encode("utf-8"), (HOST, PORT))
        time.sleep(1 / RATE_HZ)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mode", choices=["blink", "hold", "away", "open", "demo"])
    parser.add_argument("seconds", nargs="?", type=float, default=1.0, help="duration for hold/away/open")
    args = parser.parse_args()

    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        if args.mode == "blink":
            stream(sock, 0.3, closed=False)
            stream(sock, 0.15, closed=True)
            stream(sock, 0.5, closed=False)
        elif args.mode == "hold":
            stream(sock, 0.3, closed=False)
            stream(sock, args.seconds, closed=True)
            stream(sock, 0.5, closed=False)
        elif args.mode == "away":
            stream(sock, 0.3, closed=False)
            stream(sock, args.seconds, closed=False, present=False)
            stream(sock, 0.5, closed=False)
        elif args.mode == "open":
            stream(sock, args.seconds, closed=False)
        elif args.mode == "demo":
            stream(sock, 1.0, closed=False)
            stream(sock, 0.15, closed=True)
            stream(sock, 1.0, closed=False)
            stream(sock, 2.0, closed=True)
            stream(sock, 1.0, closed=False)
    print(f"sent '{args.mode}' to {HOST}:{PORT}")


if __name__ == "__main__":
    main()
