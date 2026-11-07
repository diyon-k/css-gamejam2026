#!/bin/sh
# One-time setup for the webcam sidecar on macOS / Linux: sh blink_server/setup.sh
# (Windows: blink_server/setup.ps1.) Creates blink_server/.venv and installs requirements.txt.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
venv="$here/.venv"

if [ ! -x "$venv/bin/python" ]; then
    python=""
    for candidate in python3.13 python3.12; do
        if command -v "$candidate" >/dev/null 2>&1; then python="$candidate"; break; fi
    done
    if [ -z "$python" ]; then
        echo "Python 3.13 or 3.12 not found. Install one (python.org, 'brew install python@3.12' or 'uv python install 3.12') and re-run." >&2
        exit 1
    fi
    echo "Creating $venv with $("$python" --version)"
    "$python" -m venv "$venv"
fi

"$venv/bin/python" -m pip install --upgrade pip >/dev/null
"$venv/bin/python" -m pip install -r "$here/requirements.txt"

echo
echo "Done. Check your blinks are seen (q to quit):"
echo "  blink_server/.venv/bin/python blink_server/blink_server.py --preview"
echo "After that, pressing Play in Unity starts the sidecar automatically."
if [ "$(uname)" = "Darwin" ]; then
    echo "macOS asks for camera access the first time: allow it for your terminal app, then for Unity."
fi
