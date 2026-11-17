"""An hour of other speech for counting false starts of a name catcher (#346): sentences from this repository's own
docs, said by Windows' English voices, joined into one-minute streams with pauses between sentences, at the near and
far rooms of make_audio.py (as a TV or another person in the room). Sentences that start with a character's name are
left out; those with a name later in them are kept, as the near misses they are. English only: there is no Chinese
or Japanese text on this PC to make the same from without a download.

    python make_other_speech.py --out <trial folder>/other --sapi <sapi_say.dll> [--minutes 60]

Writes <out>/<room>/stream-NN.wav and <out>/streams.json (each stream's sentences and where they start).
"""
import argparse, glob, json, os, re, subprocess, sys
import numpy as np, soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from make_audio import to16k, trim, room  # noqa: E402

NAMES = re.compile(r"^(shino|hana|fumiriya)\b", re.I)


def sentences(repo):
    out = []
    for path in sorted(glob.glob(os.path.join(repo, "docs", "**", "*.md"), recursive=True)):
        text = open(path, encoding="utf-8").read()
        text = re.sub(r"```.*?```", " ", text, flags=re.S)
        text = re.sub(r"`[^`]*`|\[([^\]]*)\]\([^)]*\)|https?://\S+|[#*_>|]", lambda m: m.group(1) or " ", text)
        for s in re.split(r"(?<=[.!?])\s+", re.sub(r"\s+", " ", text)):
            words = s.split()
            if 6 <= len(words) <= 28 and re.fullmatch(r"[A-Za-z0-9 ,.;:'\"!?()%-]+", s) and not NAMES.match(s):
                out.append(s.strip())
    return list(dict.fromkeys(out))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--minutes", type=float, default=60)
    ap.add_argument("--sapi", required=True, help="sapi_say.cs built (dotnet build sapi_say.cs -o <folder>)")
    a = ap.parse_args()
    repo = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
    rng = np.random.default_rng(3)
    lines = sentences(repo)
    rng.shuffle(lines)
    voices = ["Microsoft David", "Microsoft Zira", "Microsoft Mark"]
    raw = os.path.join(a.out, "raw")
    os.makedirs(raw, exist_ok=True)
    take = lines[: int(a.minutes * 60 / 5.5)]          # about 5.5 s a sentence
    jobs = [(voices[i % 3], os.path.join(raw, f"{i:04d}.wav"), s) for i, s in enumerate(take)]
    todo = [j for j in jobs if not os.path.exists(j[1])]
    if todo:
        subprocess.run(["dotnet", a.sapi], input="".join(f"{v}\t{p}\t{s}\n" for v, p, s in todo).encode("utf-8"),
                       check=True, stdout=subprocess.DEVNULL)
    streams, current, parts, at = [], [], [], 0.0
    for voice, path, text in jobs:
        x, rate = sf.read(path, dtype="float64")
        x = trim(to16k(x, rate), lead=0, tail=0)
        x = x / (np.abs(x).max() + 1e-9) * 0.5
        gap = rng.uniform(0.4, 2.0)
        current.append({"start": round(at, 2), "seconds": round(len(x) / 16000, 2), "text": text})
        parts += [x, np.zeros(int(gap * 16000))]
        at += len(x) / 16000 + gap
        if at > 60:
            streams.append((np.concatenate(parts), current))
            current, parts, at = [], [], 0.0
    if parts:
        streams.append((np.concatenate(parts), current))
    index = []
    for n, (audio, said) in enumerate(streams):
        for kind in ("near", "far"):
            os.makedirs(os.path.join(a.out, kind), exist_ok=True)
            sf.write(os.path.join(a.out, kind, f"stream-{n:02d}.wav"),
                     room(audio, kind, seed=100 + n).astype(np.float32), 16000, subtype="PCM_16")
        index.append({"stream": f"stream-{n:02d}.wav", "seconds": round(len(audio) / 16000, 1), "said": said})
    json.dump(index, open(os.path.join(a.out, "streams.json"), "w", encoding="utf-8"), indent=1)
    print(f"{len(streams)} streams, {sum(i['seconds'] for i in index) / 60:.1f} minutes, "
          f"{sum(len(i['said']) for i in index)} sentences", file=sys.stderr)


if __name__ == "__main__":
    main()
