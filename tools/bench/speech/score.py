"""Scores ears.cs's runs (#346): word error rate for English, character error rate for Chinese and Japanese, whether
each name was heard (any fair spelling of it counts), and the timings. A line whose clean recording no model hears
within 35% is left out everywhere, as the synthesiser's slip rather than the ears'; how many is printed.

    python score.py --audio E:/Tools/speech-trial/audio --runs E:/Tools/speech-trial/runs [--md out.md]
"""
import argparse, glob, json, os, re, statistics, unicodedata
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = json.load(open(os.path.join(HERE, "corpus.json"), encoding="utf-8"))
WORDS = "zero one two three four five six seven eight nine ten eleven twelve".split()
CN = "零一二三四五六七八九"


def cn_number(n):
    n = int(n)
    if n < 10:
        return CN[n]
    if n < 100:
        return ("" if n // 10 == 1 else CN[n // 10]) + "十" + ("" if n % 10 == 0 else CN[n % 10])
    return str(n)


def canon_names(text):
    for canon, variants in CORPUS["names"].items():
        for v in sorted(variants, key=len, reverse=True):
            text = re.sub(re.escape(v), f" N{list(CORPUS['names']).index(canon)} ", text, flags=re.I)
    return text


def units(text, lang):
    text = unicodedata.normalize("NFKC", text)
    text = canon_names(text)
    if lang == "en":
        text = text.lower().replace("o'clock", "")
        text = re.sub(r"\b(\d{1,2}):(\d{2})\b", lambda m: f"{m.group(1)} {m.group(2)}", text)
        text = re.sub(r"\b\d{1,2}\b", lambda m: WORDS[int(m.group())] if int(m.group()) < len(WORDS) else m.group(), text)
        text = re.sub(r"[^\w' ]", " ", text)
        return text.split()
    text = re.sub(r"\d+", lambda m: cn_number(m.group()), text)
    tokens = re.findall(r"N\d|\w", text)
    return [t for t in tokens if t.strip()]


def distance(a, b):
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1]


def name_hit(name, hyp):
    return any(v.lower() in hyp.lower() for v in CORPUS["names"][name])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--audio", default="E:/Tools/speech-trial/audio")
    ap.add_argument("--runs", default="E:/Tools/speech-trial/runs")
    ap.add_argument("--md")
    a = ap.parse_args()
    manifest = {}
    for line in open(os.path.join(a.audio, "manifest.jsonl"), encoding="utf-8"):
        m = json.loads(line)
        manifest[(m["clip"], m["room"])] = m
    runs = {}
    for path in sorted(glob.glob(os.path.join(a.runs, "*.jsonl"))):
        rows = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]
        if rows and "model" in rows[0]:
            runs[os.path.basename(path)[:-6]] = rows

    def err(m, text):
        lang = m["lang"]
        ref, hyp = units(m["text"], lang), units(text, lang)
        return distance(ref, hyp), len(ref)

    # lines the synthesiser got wrong: no model hears the clean clip within 35%
    best = defaultdict(lambda: 9.0)
    for rows in runs.values():
        for r in rows:
            if r.get("room") == "clean" and (r["clip"], "clean") in manifest:
                e, n = err(manifest[(r["clip"], "clean")], r["text"])
                best[r["clip"]] = min(best[r["clip"]], e / max(n, 1))
    dropped = {c for c, b in best.items() if b > 0.35}

    out = []
    p = out.append
    p(f"Left out as the synthesiser's slips: {len(dropped)} of {len(best)} recordings: {', '.join(sorted(dropped)) or 'none'}\n")
    p("| run | load s | first turn s | memory MB (private) | GPU MB |")
    p("|---|---|---|---|---|")
    for name, rows in runs.items():
        h = rows[0]
        p(f"| {name} | {h['load_ms'] / 1000:.1f} | {h['first_turn_ms'] / 1000:.2f} | {h['private_mb']} | {h['gpu_used_mb']} |")

    groups = ["en", "accented", "zh", "ja"]
    rooms = ["clean", "near", "far"]
    p("\nError rate, % (WER for English and accented English, CER for Chinese and Japanese):\n")
    p("| run | " + " | ".join(f"{g} {r}" for g in groups for r in rooms) + " |")
    p("|---|" + "---|" * (len(groups) * len(rooms)))
    names = defaultdict(lambda: [0, 0])
    for name, rows in runs.items():
        tally = defaultdict(lambda: [0, 0])
        for r in rows[1:]:
            if "clip" not in r or r["clip"] in dropped:
                continue
            m = manifest[(r["clip"], r["room"])]
            e, n = err(m, r["text"])
            tally[(m["group"], m["room"])][0] += e
            tally[(m["group"], m["room"])][1] += n
            for nm in m["names"]:
                k = (name, m["group"], m["room"])
                names[k][0] += name_hit(nm, r["text"])
                names[k][1] += 1
        p(f"| {name} | " + " | ".join(
            f"{100 * tally[(g, r)][0] / tally[(g, r)][1]:.1f}" if tally[(g, r)][1] else "-" for g in groups for r in rooms) + " |")

    p("\nNames heard, % of names said (any fair spelling):\n")
    p("| run | " + " | ".join(f"{g} {r}" for g in groups for r in rooms) + " |")
    p("|---|" + "---|" * (len(groups) * len(rooms)))
    for name in runs:
        p(f"| {name} | " + " | ".join(
            f"{100 * names[(name, g, r)][0] / names[(name, g, r)][1]:.0f}" if names[(name, g, r)][1] else "-"
            for g in groups for r in rooms) + " |")

    p("\nFrom the end of the turn to the words, s: median [lowest, highest] of 5 tries on each clip of that length\n")
    p("| run | ~2 s | ~6 s | ~15 s |")
    p("|---|---|---|---|")
    for name, rows in runs.items():
        cells = []
        for length in "sml":
            t = [x / 1000 for r in rows if r.get("len") == length and "times" in r for x in r["times"]]
            cells.append(f"{statistics.median(t):.2f} [{min(t):.2f}, {max(t):.2f}]" if t else "-")
        p(f"| {name} | " + " | ".join(cells) + " |")

    streams = {n: rows for n, rows in runs.items() if any("first_word_s" in r for r in rows)}
    if streams:
        p("\nStreaming: when the first words showed (s into the clip), and the last landed after the clip ended (s), median [lowest, highest]:\n")
        p("| run | first words | last word | compute per second of speech |")
        p("|---|---|---|---|")
        for name, rows in streams.items():
            rs = [r for r in rows if "first_word_s" in r and r["room"] == "clean"]
            fw = [r["first_word_s"] for r in rs if r["first_word_s"] is not None]
            lw = [r["ms"] / 1000 for r in rs]
            per = [r["compute_ms"] / 1000 / manifest[(r["clip"], "clean")]["seconds"] for r in rs]
            p(f"| {name} | {statistics.median(fw):.2f} [{min(fw):.2f}, {max(fw):.2f}] | "
              f"{statistics.median(lw):.2f} [{min(lw):.2f}, {max(lw):.2f}] | {statistics.median(per):.3f} |")
    text = "\n".join(out)
    print(text)
    if a.md:
        open(a.md, "w", encoding="utf-8").write(text)


if __name__ == "__main__":
    main()
