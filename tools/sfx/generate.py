"""Generates INSERT COIN's sound effects as 16-bit mono WAVs (original, license-free).

A tiny chip-style synth: square / triangle / sine oscillators with pitch slides and decay envelopes, plus filtered
noise. Everything is deterministic (fixed random seed), so the files are reproducible.

    python tools/sfx/generate.py        # writes src/InsertCoin.Game/sounds/*.wav
"""
import math
import random
import struct
import wave
from pathlib import Path

SR = 44100
NOTE = {n: 440.0 * 2 ** ((i - 57) / 12) for i, n in enumerate(
    f"{name}{octave}" for octave in range(0, 9) for name in ("C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"))}


def osc(kind, phase, duty=0.5):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < duty else -1.0
    if kind == "triangle":
        return 4 * abs(p - 0.5) - 1
    return math.sin(2 * math.pi * p)


def tone(freq, dur, kind="square", vol=0.5, to=None, duty=0.5, attack=0.003, decay=4.0):
    """One note. `to` slides the pitch to another frequency; `decay` is the exponential fall-off rate."""
    n = int(SR * dur)
    out, phase = [], 0.0
    for i in range(n):
        t = i / n
        f = freq + ((to - freq) * t if to else 0)
        phase += f / SR
        env = min(1.0, (i / SR) / attack) * math.exp(-decay * t) * min(1.0, (n - i) / (SR * 0.004))
        out.append(osc(kind, phase, duty) * env * vol)
    return out


def noise(dur, vol=0.4, cutoff=0.5, decay=6.0, rng=random.Random(7)):
    """Noise through a one-pole low-pass (cutoff 0..1)."""
    n = int(SR * dur)
    out, y = [], 0.0
    for i in range(n):
        y += cutoff * (rng.uniform(-1, 1) - y)
        out.append(y * vol * math.exp(-decay * i / n))
    return out


def silence(dur):
    return [0.0] * int(SR * dur)


def seq(*parts):
    return [s for p in parts for s in p]


def mix(*parts):
    out = [0.0] * max(len(p) for p in parts)
    for p in parts:
        for i, s in enumerate(p):
            out[i] += s
    return out


def arp(notes, each, kind="square", vol=0.45, duty=0.5, decay=3.0):
    return seq(*[tone(NOTE[n], each, kind, vol, duty=duty, decay=decay) for n in notes])


SOUNDS = {
    # earning
    "coin": seq(tone(NOTE["B5"], 0.07, vol=0.42, decay=1.0), tone(NOTE["E6"], 0.32, vol=0.42, decay=5.0)),
    "jackpot": mix(seq(arp(["C6", "E6", "G6", "C7", "E7", "G7", "C8"], 0.065, decay=1.5), tone(NOTE["C8"], 0.5, "triangle", 0.4, decay=5)),
                   seq(silence(0.2), arp(["G5", "C6", "E6", "G6"], 0.11, "triangle", 0.3))),
    "token": mix(noise(0.03, 0.5, 0.9), seq(silence(0.012), tone(2300, 0.09, "triangle", 0.4, to=1700, decay=6)),
                 seq(silence(0.07), tone(190, 0.08, "sine", 0.5, to=120, decay=5))),
    "paid": seq(mix(noise(0.07, 0.6, 0.15, decay=5), tone(120, 0.09, "sine", 0.6, to=70, decay=4)), silence(0.05),
                tone(NOTE["G6"], 0.34, "triangle", 0.42, decay=5)),
    "cashbox": mix(noise(0.14, 0.35, 0.8, decay=3),
                   seq(silence(0.12), mix(tone(NOTE["C7"], 0.45, "triangle", 0.34, decay=5), tone(NOTE["E7"], 0.45, "triangle", 0.26, decay=5)))),
    # alerts (must cut through game audio)
    "warn5": seq(*[seq(tone(880, 0.12, duty=0.25, vol=0.45, decay=1), silence(0.05), tone(660, 0.2, duty=0.25, vol=0.45, decay=2), silence(0.14))
                   for _ in range(2)]),
    "warn1": seq(*[seq(tone(NOTE["C6"], 0.09, duty=0.25, vol=0.5, decay=1), silence(0.05)) for _ in range(3)],
                 tone(NOTE["F6"], 0.32, duty=0.25, vol=0.5, decay=3)),
    "continue_tick": tone(440, 0.1, vol=0.4, decay=3),
    "credits_out": seq(arp(["G5", "E5", "C5", "G4"], 0.15, decay=2.0), tone(NOTE["C4"], 0.5, "triangle", 0.5, decay=4)),
    "printer": mix(seq(*[seq(noise(0.018, 0.55, 0.6, decay=2), silence(0.016)) for _ in range(18)]),
                   tone(1250, 0.62, "square", 0.05, duty=0.3, decay=0.5)),
    # session
    "game_on": arp(["C5", "E5", "G5", "C6"], 0.085, decay=2.0),
    "game_over": seq(tone(660, 0.5, vol=0.42, to=200, decay=2.0), silence(0.05), tone(110, 0.3, "triangle", 0.5, decay=4)),
    # ui
    "check": seq(tone(1500, 0.04, vol=0.35, to=900, decay=3), silence(0.02), tone(1250, 0.07, vol=0.3, decay=5)),
    "key": tone(1800, 0.02, vol=0.25, decay=4),
    "error": tone(130, 0.26, vol=0.4, duty=0.3, decay=1.5),
    "hello": arp(["E5", "G5", "C6", "E6", "G6"], 0.08, "triangle", 0.45, decay=2.0),
}


def write(path, samples):
    peak = max(1e-9, max(abs(s) for s in samples))
    gain = min(1.0, 0.85 / peak)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * gain)) * 32767)) for s in samples))


if __name__ == "__main__":
    out = Path(__file__).resolve().parents[2] / "src" / "InsertCoin.Game" / "sounds"
    out.mkdir(parents=True, exist_ok=True)
    for name, samples in SOUNDS.items():
        write(out / f"{name}.wav", samples)
        print(f"{name}.wav  {len(samples) / SR:.2f}s")
