"""Scores ears.cs's runs (#346): word error rate for English, character error rate for Chinese and Japanese, whether
each name was heard (any fair spelling of it counts), and the timings. A line whose clean recording no model hears
within 35% (its names aside) is left out everywhere, as the synthesiser's slip rather than the ears'; how many is printed.

    python score.py --audio <trial folder>/audio --runs <runs folder> [--names <folder>] [--loop <file>] [--md out.md]

A name caught in an answer that is the hint list given back garbled (letters outside the players' scripts, as
Qwen3-ASR sometimes answers when given hints) doesn't count: the app would throw such an answer away.
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
        text = re.sub(r"(?<=[a-z])(?=\d)|(?<=\d)(?=[a-z])", " ", text)        # "at9"
        text = re.sub(r"\b(\d{1,2}):(\d{2})\b", lambda m: f"{m.group(1)} {m.group(2)}", text)
        text = re.sub(r"\b\d{1,2}\b", lambda m: WORDS[int(m.group())] if int(m.group()) < len(WORDS) else m.group(), text)
        text = re.sub(r"[^\w' ]", " ", text)
        return text.split()
    text = re.sub(r"\d+", lambda m: cn_number(m.group()), text)
    text = "".join(chr(ord(c) + 0x60) if "ぁ" <= c <= "ゖ" else c for c in text)  # ばたばた, バタバタ alike
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


# the scripts the players speak: an answer with letters outside them (or the replacement character) is the hint list
# given back garbled ("Shino ʫ nota͘ Hana ʫ…"), which the app would throw away; the same rule as ears.cs's
SCRIPTS = re.compile(r"[\u0000-\u024F\u3000-\u30FF\u4E00-\u9FFF\uAC00-\uD7AF\uFF00-\uFFEF]")


def hint_list(text):
    return "\ufffd" in text or any(c.isalpha() and not SCRIPTS.match(c) for c in text)


def name_hit(name, hyp):
    return not hint_list(hyp) and any(v.lower() in hyp.lower() for v in CORPUS["names"][name])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--audio", required=True, help="make_audio.py's output folder (its manifest.jsonl)")
    ap.add_argument("--runs", required=True, help="a folder of ears.cs runs")
    ap.add_argument("--md")
    ap.add_argument("--names", help="the folder of run_names.sh's runs")
    ap.add_argument("--loop", help="a jsonl of ears.cs --mode loop")
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

    def err(m, text, names=True):
        lang = m["lang"]
        ref, hyp = units(m["text"], lang), units(text, lang)
        if not names:
            ref, hyp = [u for u in ref if not re.fullmatch(r"[nN]\d", u)], [u for u in hyp if not re.fullmatch(r"[nN]\d", u)]
        return distance(ref, hyp), len(ref)

    # lines the synthesiser got wrong: no model hears the clean clip within 35%
    best = defaultdict(lambda: 9.0)
    for rows in runs.values():
        for r in rows:
            if r.get("room") == "clean" and (r["clip"], "clean") in manifest:
                e, n = err(manifest[(r["clip"], "clean")], r["text"], names=False)   # a misheard name is the ears' doing
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
    p("| run | " + " | ".join(f"{g} {r}" for g in groups for r in rooms) + " | ran on |")
    p("|---|" + "---|" * (len(groups) * len(rooms) + 1))
    names = defaultdict(lambda: [0, 0])
    for name, rows in ((n, r) for n, r in runs.items() if not n.endswith("+hints")):   # those are on the names only: below
        tally = defaultdict(lambda: [0, 0])
        ran_on = 0
        for r in rows[1:]:
            if "clip" not in r or r["clip"] in dropped:
                continue
            m = manifest[(r["clip"], r["room"])]
            e, n = err(m, r["text"])
            ran_on += bool(re.search(r"(.{1,6}?)\1{7,}", r["text"]))     # a decoder that repeats itself
            tally[(m["group"], m["room"])][0] += e
            tally[(m["group"], m["room"])][1] += n
            for nm in m["names"]:
                k = (name, m["group"], m["room"])
                names[k][0] += name_hit(nm, r["text"])
                names[k][1] += 1
        p(f"| {name} | " + " | ".join(
            f"{100 * tally[(g, r)][0] / tally[(g, r)][1]:.1f}" if tally[(g, r)][1] else "-" for g in groups for r in rooms) + f" | {ran_on} |")

    p("\nNames heard, % of names said (any fair spelling):\n")
    p("| run | " + " | ".join(f"{g} {r}" for g in groups for r in rooms) + " |")
    p("|---|" + "---|" * (len(groups) * len(rooms)))
    for name in (n for n in runs if not n.endswith("+hints")):
        p(f"| {name} | " + " | ".join(
            f"{100 * names[(name, g, r)][0] / names[(name, g, r)][1]:.0f}" if names[(name, g, r)][1] else "-"
            for g in groups for r in rooms) + " |")

    hinted = [n for n in runs if n.endswith("+hints") and n[:-6] in runs]
    if hinted:
        p("\nWith the names as hints, on the clips that hold a name, all rooms: names heard %, and the error rate % (names heard / error):\n")
        p("| model | " + " | ".join(f"{g} without | {g} with" for g in groups) + " |")
        p("|---|" + "---|" * (2 * len(groups)))
        for h in hinted:
            clips = {(r["clip"], r["room"]) for r in runs[h][1:] if "clip" in r}
            cells = []
            for g in groups:
                for name in (h[:-6], h):
                    said = heard = e = n = 0
                    for r in runs[name][1:]:
                        if "clip" not in r or (r["clip"], r["room"]) not in clips or r["clip"] in dropped:
                            continue
                        m = manifest[(r["clip"], r["room"])]
                        if m["group"] != g:
                            continue
                        de, dn = err(m, r["text"])
                        e, n = e + de, n + dn
                        said += len(m["names"])
                        heard += sum(name_hit(nm, r["text"]) for nm in m["names"])
                    cells.append(f"{100 * heard / said:.0f} / {100 * e / n:.1f}" if n else "-")
            p(f"| {h[:-6]} | " + " | ".join(cells) + " |")

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
        p("\nStreaming, clean clips: when the first words showed (s from the start of speech), and when the last landed (s after the end of the words), median [lowest, highest]:\n")
        p("| run | first words | last word | compute per second of speech |")
        p("|---|---|---|---|")
        for name, rows in streams.items():
            rs = [r for r in rows if "first_word_s" in r and r["room"] == "clean"]
            fw = [r["first_word_s"] - 0.25 for r in rs if r["first_word_s"] is not None]   # make_audio.py's lead-in
            lw = [r["ms"] / 1000 + 0.3 for r in rs]                                           # and its quiet after
            per = [r["compute_ms"] / 1000 / manifest[(r["clip"], "clean")]["seconds"] for r in rs]
            p(f"| {name} | {statistics.median(fw):.2f} [{min(fw):.2f}, {max(fw):.2f}] | "
              f"{statistics.median(lw):.2f} [{min(lw):.2f}, {max(lw):.2f}] | {statistics.median(per):.3f} |")
    if a.names:
        names_part(a.names, manifest, p)
    if a.loop:
        loop_part(a.loop, p)
    text = "\n".join(out)
    print(text)
    if a.md:
        open(a.md, "w", encoding="utf-8").write(text)


def names_part(folder, manifest, p):
    p("\nCatching their name: said first (caught / said), said later or not said (caught / clips), other speech (false starts, minutes, per hour), garbled hint lists not counted, and wall time spent per second of sound on the run's threads\n")
    p("| run | said first: caught | not first: caught | false starts | other speech, min | per hour | hint lists left out | threads | wall s per s of sound |")
    p("|---|---|---|---|---|---|---|---|---|")
    for path in sorted(glob.glob(os.path.join(folder, "*.jsonl"))):
        rows = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]
        garbled = [r for r in rows if (r.get("caught") or "false_start_at" in r) and hint_list(r.get("words") or "")]
        for r in garbled:
            r["caught"] = False
        clips = [r for r in rows if "starts_with_name" in r]
        first = [r for r in clips if r["starts_with_name"]]
        later = [r for r in clips if not r["starts_with_name"]]
        false = [r for r in rows if "false_start_at" in r and not hint_list(r.get("words") or "")]
        summary = next((r for r in rows if r.get("summary")), None)
        if not summary:
            continue
        minutes = summary["streams_minutes"]
        p(f"| {os.path.basename(path)[:-6]} | {sum(r['caught'] for r in first)} / {len(first)} | "
          f"{sum(r['caught'] for r in later)} / {len(later)} | {len(false)} | {minutes} | "
          f"{len(false) / minutes * 60 if minutes else 0:.1f} | {len(garbled)} | {rows[0].get('threads')} | "
          f"{summary['streams_compute_per_second']:.3f} |")


def loop_part(path, p):
    rows = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip() and '"loop"' in l]
    whole = [r for r in rows if r["turn_over_s"] > -0.3]
    cut = len(rows) - len(whole)
    p(f"\nThe whole loop, s after the end of the player's words: median [lowest, highest] ({len(whole)} turns; {cut} more were judged over at a pause in the middle)\n")
    p("| room, length | turn judged over | words heard | mind's first words | first sentence | voice's first sound |")
    p("|---|---|---|---|---|---|")
    keys = ["turn_over_s", "words_s", "mind_first_s", "first_sentence_s", "voice_first_s"]
    for room in ("clean", "near", "far"):
        for length in "sml":
            rs = [r for r in whole if r["room"] == room and r["len"] == length]
            if not rs:
                continue
            cells = []
            for k in keys:
                v = [r[k] for r in rs if r.get(k) is not None]
                cells.append(f"{statistics.median(v):.2f} [{min(v):.2f}, {max(v):.2f}]" if v else "-")
            p(f"| {room}, {dict(s='~2 s', m='~6 s', l='~15 s')[length]} | " + " | ".join(cells) + " |")
    ended = [r for r in whole if r["by_smart_turn"]]
    p(f"\nSmart Turn ended {len(ended)} of {len(whole)} turns; the rest waited for 2 s of quiet. Its time per ask: median "
      f"{statistics.median([t for r in rows for t in r['smart_turn_ms']]):.0f} ms.")


if __name__ == "__main__":
    main()
