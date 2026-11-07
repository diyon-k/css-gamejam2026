"""Synthesised sounds for Lily's doll (docs/puzzles/LILYS_DOLL_ROOM.md). Original, made from sine waves and noise.

  Doll_MusicBox.wav   Brahms' "Wiegenlied" (Lullaby, 1868, public domain) on a slightly worn music box, a seamless loop.
  Doll_Scratch.wav    porcelain scraping on bone, a seamless loop for while she's on your face.

Run from the repo root (needs numpy; the webcam venv has it):
    blink_server/.venv/bin/python -I tools/audio/doll_sounds.py
"""
import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Unity", "Assets", "Art", "Audio", "World")

NOTE = {"C": -9, "D": -7, "E": -5, "F": -4, "G": -2, "A": 0, "B": 2}


def freq(name):
    """'E5' -> Hz (A4 = 440)."""
    return 440.0 * 2 ** ((NOTE[name[0]] + 12 * (int(name[1]) - 4)) / 12)


# Brahms' Lullaby in C, 3/4, durations in beats. Played two octaves up, like a small comb.
MELODY = [
    ("E4", .5), ("E4", .5), ("G4", 2),
    ("E4", .5), ("E4", .5), ("G4", 2),
    ("E4", .5), ("G4", .5), ("C5", 1), ("B4", 1.5), ("A4", .5), ("A4", 1), ("G4", 2),
    ("D4", .5), ("E4", .5), ("F4", 1), ("D4", 1), ("D4", .5), ("E4", .5), ("F4", 2),
    ("D4", .5), ("F4", .5), ("B4", 1), ("A4", 1), ("G4", 1), ("B4", 1), ("C5", 2),
    ("C4", .5), ("C4", .5), ("C5", 2), ("A4", .5), ("F4", .5), ("G4", 2),
    ("E4", .5), ("C4", .5), ("F4", 1), ("G4", 1), ("A4", 1), ("G4", 2),
    ("C4", .5), ("C4", .5), ("C5", 2), ("A4", .5), ("F4", .5), ("G4", 2),
    ("E4", .5), ("C4", .5), ("F4", 1), ("E4", 1), ("D4", 1), ("C4", 3),
]
# A few notes of the bass comb under the downbeats.
BASS = {0: "C3", 6: "C3", 12: "G2", 18: "G2", 24: "G2", 30: "C3", 36: "F2", 42: "C3", 48: "G2"}


def tine(f, seconds, rng, bright=1.0):
    """A plucked steel tine: inharmonic partials (free bar), fast click, long exponential ring."""
    t = np.arange(int(seconds * RATE)) / RATE
    partials = [(1.0, 1.0, 1.6), (2.0, 0.18 * bright, 0.7), (5.40, 0.10 * bright, 0.25), (8.93, 0.04 * bright, 0.12)]
    out = np.zeros_like(t)
    for mult, amp, decay in partials:
        detune = 1 + rng.normal(0, 0.0015)  # worn, not quite in tune
        out += amp * np.sin(2 * np.pi * f * mult * detune * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / decay)
    click = rng.normal(0, 1, len(t)) * np.exp(-t / 0.002) * 0.08
    attack = np.minimum(1, t / 0.0015)
    return (out + click) * attack


def music_box():
    rng = np.random.default_rng(1868)
    bpm = 84.0
    beat = 60.0 / bpm
    total_beats = sum(d for _, d in MELODY) + 3  # a bar of rest before it loops
    length = int(total_beats * beat * RATE)
    tail = int(3.0 * RATE)
    buf = np.zeros(length + tail)

    pos = 0.0
    for i, (name, dur) in enumerate(MELODY):
        # The spring is running down: the timing drags and rushes a little.
        wobble = 1 + 0.06 * np.sin(pos * 0.9) + rng.normal(0, 0.015)
        start = int(pos * beat * RATE)
        note = tine(freq(name) * 4, 2.5, rng) * 0.55
        buf[start:start + len(note)] += note
        beats_in = round(pos * 2) / 2
        if beats_in in BASS:
            b = tine(freq(BASS[beats_in]) * 4, 3.0, rng, bright=0.6) * 0.3
            buf[start:start + len(b)] += b
        pos += dur * wobble
    # Fold the ringing tail back onto the start so the loop is seamless.
    buf[:tail] += buf[length:length + tail]
    buf = buf[:length]
    # Mechanism: a faint, steady tick-whirr under it.
    t = np.arange(length) / RATE
    whirr = rng.normal(0, 1, length) * 0.004 * (1 + 0.5 * np.sin(2 * np.pi * 11 * t))
    buf += whirr
    return buf / np.max(np.abs(buf)) * 0.8


def scratch():
    rng = np.random.default_rng(6)
    seconds = 2.4
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    noise = rng.normal(0, 1, n)
    out = np.zeros(n)
    # Strokes: band-passed noise, each with a squealing resonance that sweeps, like porcelain dragged on bone.
    for k in range(9):
        start = rng.uniform(0, seconds)
        dur = rng.uniform(0.12, 0.35)
        f0, f1 = rng.uniform(1800, 3200), rng.uniform(900, 2600)
        idx = (np.arange(n) - int(start * RATE)) % n  # wraps round: seamless loop
        tt = idx / RATE
        env = np.where(tt < dur, np.sin(np.pi * np.clip(tt / dur, 0, 1)) ** 0.6, 0)
        sweep = f0 + (f1 - f0) * np.clip(tt / dur, 0, 1)
        phase = 2 * np.pi * np.cumsum(sweep) / RATE
        squeal = np.sin(phase) * (0.25 + 0.75 * np.abs(np.sin(phase * 0.013)))
        grit = np.convolve(np.roll(noise, k * 997), np.ones(6) / 6, mode="same")
        out += env * (0.6 * grit + 0.35 * squeal) * (1 + 0.6 * np.sign(np.sin(2 * np.pi * rng.uniform(25, 60) * t)))
    # A wet low thud under each stroke cluster.
    out += np.sin(2 * np.pi * 70 * t) * np.exp(-((t * 1.25) % 0.6) / 0.05) * 0.4
    return out / np.max(np.abs(out)) * 0.8


def write(name, samples):
    path = os.path.normpath(os.path.join(OUT, name))
    pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print(f"{path}  {len(samples) / RATE:.1f} s")


if __name__ == "__main__":
    write("Doll_MusicBox.wav", music_box())
    write("Doll_Scratch.wav", scratch())
