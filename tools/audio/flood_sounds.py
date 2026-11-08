"""Synthesised sounds for the flooded basement (docs/puzzles/FLOODED_BASEMENT.md). Original, made from sine waves and noise.

  Flood_Drip.wav        one drop into a full bath (a plink and a little ring).
  Flood_TapRun.wav      the bath tap running hard and spilling over, a seamless loop.
  Flood_TapStop.wav     the tap spluttering out: coughs of water and air.
  Flood_Surge.wav       the slosh of the water lurching up after a blink.
  Flood_Gurgle.wav      the drain swallowing it all, a seamless loop.
  Flood_CoverClang.wav  a rusted iron cover wrenched off and dropped on concrete.
  Flood_Drown.wav       going under: muffled bubbles rushing up and a slowing heartbeat.

Run from the repo root (needs numpy; the webcam venv has it):
    blink_server/.venv/bin/python -I tools/audio/flood_sounds.py
"""
import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Unity", "Assets", "Art", "Audio", "World")
rng = np.random.default_rng(6)


def t_axis(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def lowpass(x, cutoff):
    """One-pole low-pass (cutoff in Hz, scalar or per-sample array)."""
    a = np.exp(-2 * np.pi * np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape) / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def bandpass(x, lo, hi):
    return highpass(lowpass(x, hi), lo)


def bubble(f0, seconds, rise=1.6, decay=0.03):
    """A single bubble: a sine whose pitch rises as it pops (Minnaert resonance), decaying fast."""
    t = t_axis(seconds)
    f = f0 * (1 + rise * t / seconds)
    phase = 2 * np.pi * np.cumsum(f) / RATE
    return np.sin(phase) * np.exp(-t / decay) * np.minimum(1, t / 0.0008)


def scatter(out, sound, start):
    """Adds sound into out at sample start, wrapping round the end (so loops stay seamless)."""
    n = len(out)
    idx = (np.arange(len(sound)) + start) % n
    np.add.at(out, idx, sound)


def seamless(x, fade=0.25):
    """Folds the last `fade` seconds over the start with a crossfade so the loop has no click."""
    k = int(fade * RATE)
    head, tail = x[:k].copy(), x[-k:]
    ramp = np.linspace(0, 1, k)
    x = x[:-k].copy()
    x[:k] = head * ramp + tail * (1 - ramp)
    return x


def drip():
    out = np.zeros(int(0.7 * RATE))
    scatter(out, bubble(950, 0.09, rise=1.2, decay=0.018) * 0.9, 0)
    scatter(out, bubble(1900, 0.05, rise=0.6, decay=0.008) * 0.25, 40)
    # A soft ring in the bath, and a tiny tail of droplets.
    t = t_axis(0.7)
    out += np.sin(2 * np.pi * 620 * t) * np.exp(-t / 0.09) * 0.08
    for _ in range(3):
        scatter(out, bubble(rng.uniform(1400, 2600), 0.04, decay=0.006) * rng.uniform(0.05, 0.12),
                int(rng.uniform(0.04, 0.2) * RATE))
    return out


def tap_run():
    seconds = 6.0
    n = int(seconds * RATE)
    noise = rng.normal(0, 1, n)
    hiss = bandpass(noise, 900, 5200) * 0.35                       # the stream itself
    body = lowpass(rng.normal(0, 1, n), 400) * 0.9                  # the weight of it landing
    wobble = 1 + 0.18 * np.sin(2 * np.pi * 0.7 * t_axis(seconds)) + 0.1 * np.sin(2 * np.pi * 2.3 * t_axis(seconds))
    out = (hiss + body) * wobble
    for _ in range(int(seconds * 55)):                             # the splashing: lots of small bubbles
        scatter(out, bubble(rng.uniform(500, 2400), rng.uniform(0.02, 0.07), decay=rng.uniform(0.006, 0.02))
                * rng.uniform(0.05, 0.25), int(rng.uniform(0, n)))
    return seamless(out)


def tap_stop():
    seconds = 2.2
    out = np.zeros(int(seconds * RATE))
    t_ = 0.0
    for i, (length, amp) in enumerate([(0.32, 1.0), (0.12, 0.7), (0.22, 0.85), (0.08, 0.5), (0.18, 0.6), (0.05, 0.3)]):
        seg = int(length * RATE)
        env = np.sin(np.linspace(0, np.pi, seg)) ** 0.5
        cough = (bandpass(rng.normal(0, 1, seg), 500, 4000) * 0.5 + lowpass(rng.normal(0, 1, seg), 300)) * env * amp
        scatter(out, cough, int(t_ * RATE))
        # The pipe knocks as the air gets in.
        k = t_axis(0.08)
        scatter(out, np.sin(2 * np.pi * (140 + 30 * i) * k) * np.exp(-k / 0.02) * 0.5 * amp, int(t_ * RATE))
        t_ += length + rng.uniform(0.06, 0.16)
    for _ in range(6):                                              # last drops
        scatter(out, bubble(rng.uniform(800, 1500), 0.06, decay=0.012) * 0.3, int(rng.uniform(1.4, 2.1) * RATE))
    return out


def surge():
    seconds = 1.4
    t = t_axis(seconds)
    env = np.minimum(1, t / 0.12) * np.exp(-np.maximum(0, t - 0.12) / 0.35)
    cutoff = 300 + 1400 * np.exp(-t / 0.3)
    out = lowpass(rng.normal(0, 1, len(t)), cutoff) * env * 2.2
    out += bandpass(rng.normal(0, 1, len(t)), 700, 3000) * env * 0.25
    for _ in range(40):                                             # the slap against the walls
        scatter(out, bubble(rng.uniform(250, 900), rng.uniform(0.04, 0.12), decay=0.03) * rng.uniform(0.1, 0.35),
                int(rng.uniform(0.02, 0.7) * RATE))
    return out


def gurgle():
    seconds = 5.0
    n = int(seconds * RATE)
    t = t_axis(seconds)
    swirl = lowpass(rng.normal(0, 1, n), 250 + 120 * np.sin(2 * np.pi * 0.4 * t)) * 1.4
    out = swirl
    for _ in range(int(seconds * 22)):                             # big, low, glugging bubbles
        scatter(out, bubble(rng.uniform(110, 420), rng.uniform(0.06, 0.18), rise=2.2, decay=rng.uniform(0.03, 0.07))
                * rng.uniform(0.2, 0.6), int(rng.uniform(0, n)))
    # A rhythmic glug as air gets sucked down.
    for k in range(int(seconds * 3)):
        scatter(out, bubble(90, 0.22, rise=1.0, decay=0.08) * 0.7, int((k / 3 + rng.uniform(-0.03, 0.03)) * RATE) % n)
    return seamless(out)


def clang():
    seconds = 2.0
    t = t_axis(seconds)
    out = np.zeros(len(t))
    # The wrench: rust grinding for a moment.
    grind = int(0.25 * RATE)
    out[:grind] += bandpass(rng.normal(0, 1, grind), 1500, 6000) * np.linspace(0.2, 0.8, grind) * 0.4
    # Iron hitting concrete: inharmonic partials, a hard click, and a smaller bounce.
    for when, amp in [(0.32, 1.0), (0.52, 0.45)]:
        k = t_axis(seconds - when)
        ring = sum(a * np.sin(2 * np.pi * f * k + rng.uniform(0, 6.28)) * np.exp(-k / d)
                   for f, a, d in [(310, 1.0, 0.5), (787, 0.6, 0.3), (1453, 0.4, 0.18), (2240, 0.25, 0.1), (3410, 0.15, 0.06)])
        hit = rng.normal(0, 1, len(k)) * np.exp(-k / 0.004)
        scatter(out, (ring * 0.5 + hit * 0.6) * amp, int(when * RATE))
    return out


def drown():
    seconds = 3.0
    n = int(seconds * RATE)
    t = t_axis(seconds)
    out = lowpass(rng.normal(0, 1, n), 180) * 1.2 * np.minimum(1, t / 0.2)  # the pressure in your ears
    for _ in range(140):                                            # bubbles rushing up past your face
        when = rng.uniform(0, 1.6) ** 1.4
        scatter(out, bubble(rng.uniform(300, 1300), rng.uniform(0.03, 0.1), rise=2.0, decay=0.02) * rng.uniform(0.1, 0.4),
                int(when * RATE))
    beat = 0.0
    gap = 0.55
    while beat < seconds - 0.3:                                     # a heartbeat slowing down
        for off, amp in [(0.0, 1.0), (0.16, 0.6)]:
            k = t_axis(0.25)
            scatter(out, np.sin(2 * np.pi * 52 * k) * np.exp(-k / 0.06) * amp * 1.4, int((beat + off) * RATE))
        beat += gap
        gap *= 1.18
    return lowpass(out, 1400) * np.minimum(1, (seconds - t) / 0.4)  # fades out with the screen


def write(name, samples):
    path = os.path.normpath(os.path.join(OUT, name))
    peak = np.max(np.abs(samples)) or 1.0
    pcm = (samples / peak * 0.89 * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print(f"wrote {path} ({len(samples) / RATE:.2f} s)")


if __name__ == "__main__":
    write("Flood_Drip.wav", drip())
    write("Flood_TapRun.wav", tap_run())
    write("Flood_TapStop.wav", tap_stop())
    write("Flood_Surge.wav", surge())
    write("Flood_Gurgle.wav", gurgle())
    write("Flood_CoverClang.wav", clang())
    write("Flood_Drown.wav", drown())
