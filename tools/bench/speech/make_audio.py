"""The test speech for #346: corpus.json's lines said by Qwen TTS's preset speakers (on the processor) and Windows' own
voices, then each clip in three rooms: clean (as made), near (a room, about 1.5 m away, quiet) and far (across the
room, about 4 m: more echo, quieter, a fan and pink noise at 10 dB under the voice). Synthesised voices and a made-up
room are a weak stand-in for real people at a distance, and accents by speakers of other languages weaker still.

    python make_audio.py --out <trial folder>/audio --qwen <Qwen TTS native build folder> --sapi <sapi_say.dll> [--only-process]

Needs numpy, scipy and soundfile (the tools venv), Qwen TTS's native build (qwen-tts.exe, --qwen) and sapi_say.cs
built (--sapi). Writes <out>/raw/<voice>/<line>.wav, <out>/<room>/<clip>.wav at 16 kHz, and <out>/manifest.jsonl.
"""
import argparse, json, os, subprocess, sys
import numpy as np, soundfile as sf
from scipy.signal import resample_poly, fftconvolve, butter, sosfilt
from math import gcd

HERE = os.path.dirname(os.path.abspath(__file__))
LANG = {"en": "english", "zh": "chinese", "ja": "japanese"}
ROOMS = {  # rt60 s, direct-to-reverberant dB, speech level dBFS, noise dB under the speech (None: none)
    "clean": None,
    "near": (0.35, 4.0, -26.0, 25.0),
    "far": (0.6, -4.0, -32.0, 10.0),
}


def to16k(x, rate):
    if rate == 16000:
        return x
    g = gcd(rate, 16000)
    return resample_poly(x, 16000 // g, rate // g)


def trim(x, lead=0.25, tail=0.3, rate=16000):
    """The clip from just before the first sound to just after the last: an end of words that is the same for all."""
    env = np.convolve(np.abs(x), np.ones(160) / 160, mode="same")
    on = np.where(env > max(env.max() * 0.02, 1e-4))[0]
    if len(on) == 0:
        return x
    a, b = on[0], on[-1]
    return np.concatenate([np.zeros(int(lead * rate)), x[a:b + 1], np.zeros(int(tail * rate))])


def rir(rt60, drr_db, rng, rate=16000):
    n = int(rt60 * 1.2 * rate)
    t = np.arange(n) / rate
    tail = rng.standard_normal(n) * np.exp(-6.908 * t / rt60)        # 60 dB down at rt60
    tail[: int(0.003 * rate)] = 0                                     # the first reflections come a little later
    tail = sosfilt(butter(2, 5000, fs=rate, output="sos"), tail)     # air and walls take the highs out of the echo
    tail *= np.sqrt(10 ** (-drr_db / 10) / np.sum(tail ** 2))        # energy against the direct sound's 1
    tail[0] += 1.0
    return tail


def pink(n, rng):
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.arange(len(spec)); f[0] = 1
    return np.fft.irfft(spec / np.sqrt(f), n)


def db(x):
    return 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-12)


def room(x, kind, seed):
    if ROOMS[kind] is None:
        return x / (np.abs(x).max() + 1e-9) * 0.5
    rt60, drr, level, snr = ROOMS[kind]
    rng = np.random.default_rng(seed)
    y = fftconvolve(x, rir(rt60, drr, rng))[: len(x) + 4000]
    y *= 10 ** ((level - db(y[np.abs(y) > 1e-4])) / 20)
    t = np.arange(len(y)) / 16000
    noise = pink(len(y), rng) + 0.3 * np.sin(2 * np.pi * 120 * t) * pink(len(y), rng).std()   # a fan's hum
    speech_db = db(y[np.abs(y) > 10 ** ((level - 30) / 20)])
    noise *= 10 ** ((speech_db - snr - db(noise)) / 20)
    return np.clip(y + noise, -1, 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--qwen", required=True, help="qwentts.cpp's folder: build/Release/qwen-tts.exe and weights/")
    ap.add_argument("--sapi", required=True, help="sapi_say.cs built (dotnet build sapi_say.cs -o <folder>)")
    ap.add_argument("--only-process", action="store_true")
    a = ap.parse_args()
    corpus = json.load(open(os.path.join(HERE, "corpus.json"), encoding="utf-8"))
    lines = {l["id"]: l for l in corpus["lines"]}
    jobs = []  # (engine, voice, line, accented)
    for lang in ("en", "zh", "ja"):
        for engine, voice in corpus["voices"][lang]:
            jobs += [(engine, voice, l, False) for l in corpus["lines"] if l["lang"] == lang]
    for engine, voice in corpus["voices"]["accented"]:
        jobs += [(engine, voice, l, True) for l in corpus["lines"] if l["lang"] == "en"]

    def raw_path(engine, voice, line):
        return os.path.join(a.out, "raw", f"{engine}-{voice.replace(' ', '_')}", line["id"] + ".wav")

    if not a.only_process:
        sapi = [j for j in jobs if j[0] == "sapi" and not os.path.exists(raw_path(j[0], j[1], j[2]))]
        if sapi:
            for j in sapi:
                os.makedirs(os.path.dirname(raw_path(*j[:3])), exist_ok=True)
            tsv = "".join(f"{v}\t{raw_path(e, v, l)}\t{l['text']}\n" for e, v, l, _ in sapi)
            subprocess.run(["dotnet", a.sapi], input=tsv.encode("utf-8"), check=True)
        for engine, voice, line, accented in jobs:
            out = raw_path(engine, voice, line)
            if engine != "qwen" or os.path.exists(out):
                continue
            os.makedirs(os.path.dirname(out), exist_ok=True)
            cmd = [os.path.join(a.qwen, "build/Release/qwen-tts.exe"),
                   "--model", os.path.join(a.qwen, "weights/qwen-talker-0.6b-customvoice-Q8_0.gguf"),
                   "--codec", os.path.join(a.qwen, "weights/qwen-tokenizer-12hz-Q8_0.gguf"),
                   "--device", "CPU", "--speaker", voice, "--lang", LANG[line["lang"]], "--seed", "7", "-o", out]
            r = subprocess.run(cmd, input=line["text"].encode("utf-8"), capture_output=True)
            print(("made " if r.returncode == 0 else "FAILED ") + out, flush=True)

    manifest = []
    for i, (engine, voice, line, accented) in enumerate(jobs):
        src = raw_path(engine, voice, line)
        if not os.path.exists(src):
            continue
        x, rate = sf.read(src, dtype="float64")
        if x.ndim > 1:
            x = x.mean(axis=1)
        x = trim(to16k(x, rate))
        group = "accented" if accented else line["lang"]
        clip = f"{group}-{engine}-{voice.replace(' ', '_')}-{line['id']}"
        for kind in ROOMS:
            os.makedirs(os.path.join(a.out, kind), exist_ok=True)
            path = os.path.join(a.out, kind, clip + ".wav")
            sf.write(path, room(x, kind, seed=i * 7 + len(kind)).astype(np.float32), 16000, subtype="PCM_16")
            manifest.append({"clip": clip, "room": kind, "path": path, "group": group, "lang": line["lang"],
                             "line": line["id"], "len": line["len"], "voice": f"{engine}:{voice}",
                             "seconds": round(len(x) / 16000, 2), "text": line["text"], "names": line["names"]})
    with open(os.path.join(a.out, "manifest.jsonl"), "w", encoding="utf-8") as f:
        for m in manifest:
            f.write(json.dumps(m, ensure_ascii=False) + "\n")
    print(f"{len(manifest)} clips", file=sys.stderr)


if __name__ == "__main__":
    main()
