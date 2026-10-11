# Her memory: issues found

A living list of what goes wrong in her long-term memory, found by the benchmarks
([research/2026-10-11-memory-benchmark.md](research/2026-10-11-memory-benchmark.md), `tools/bench/memory.cs`) or in
use. Each says what the player would notice, the evidence, the cause where known, and what to do. Close an issue
only with a re-run that shows it fixed, and say which.

| # | Issue | Status |
|---|---|---|
| M1 | The summary is cut off at its newest end | open |
| M2 | Facts keep relative times, which go stale | open |
| M3 | Upkeep costs about three times the conversation | open, a cost |
| M4 | Search starts slowly and grows in memory with years of talk | open, not yet urgent |
| M5 | Asked about something never said, she often makes up an answer | open |
| M6 | What has left the window comes back only about half the time | open |
| R1 | The provider sometimes sends back an empty reply, and she says nothing | open (the player's provider, under load) |

## M1. The summary is cut off at its newest end

- **What the player would notice:** something from the stretch of talk folded most recently is gone from what she
  recalls of "before", while older things are still there.
- **Evidence:** LongMemEval S, memory mode at the 32k window (2026-10-11): every history long enough to fold more
  than a few times (5 of 5) ended with a summary of exactly 3,200 characters, cut mid-word ("…They plan to upgrade t").
- **Cause:** the fold (`Prompt.Fold`) merges the old summary and the new lines, oldest first, in "at most 400 words".
  The model keeps everything old, adds the new and overshoots; `Conversation.Fold` then keeps the first 3,200
  characters (`SummaryLimit`), so the newest part is what is lost.
- **To do:** ask for older parts to be shortened to make room for the new; on overflow drop the oldest, never the
  newest; consider a larger summary now that context is cheap (the player, 2026-10-11).

## M2. Facts keep relative times, which go stale

- **What the player would notice:** months later she says "you upgraded your RAM last month" or "your trip next month".
- **Evidence:** the same run: 3 to 7 facts in each history with "last month", "next month", "recently", "upcoming"
  and the like.
- **Cause:** upkeep's prompt (`Upkeep.Request`) asks for dates only for plans; facts are "short, standalone
  statements" with no word on time. A recalled fact is shown without when she learned it (`Recall.Find`), so nothing
  tells her it has aged.
- **To do:** have facts written with dates ("in September 2026"), and show a recalled fact's age as moments and old
  lines already show theirs.

## M3. Upkeep costs about three times the conversation

- **Evidence:** a 122k-token history took 91 upkeep requests and 340k input tokens to digest, fold and review
  (LongMemEval question c19f7a0b); answering read 22k.
- **Notes:** digests read 3,000 tokens of talk at a time with the related facts, and every digest that touches an
  important fact rewrites a note. For a player who chats a few thousand tokens a day it is small; it decides what a
  benchmark run costs.

## M4. Search starts slowly and grows in memory with years of talk

- **Evidence:** her search on one benchmark memory grown by copying (2026-10-11, `recall_speed.cs`):

  | Passages | Each search | First search after start | Memory |
  |---|---|---|---|
  | 344 | 7 ms | 0.1 s | 35 MB |
  | 10,000 (about a year of heavy talk) | 20–27 ms | 1.5 s | 141 MB |
  | 50,000 | ~80 ms | 7.3 s | 577 MB |

- **Cause:** `History` reads every passage's words and vector into memory on the first search, and that runs on the
  game's thread inside her reply. The words (a set per passage) cost more than the vectors.
- **To do, when passages reach a few thousand:** warm it up in the background at start; later, words in SQLite's own
  full-text index (our CJK pairs written out as words) and vectors in one compact array. A vector database
  (ChromaDB, sqlite-vec) would not help: the vectors are the smaller part.

## M5. Asked about something never said, she often makes up an answer

- **What the player would notice:** "What did I say about my grandma's country?" when they never said, and she
  answers anyway: a confident false memory.
- **Evidence:** LoCoMo conversation 26 at the 8k window (2026-10-11): 9 of 41 unanswerable questions answered as
  unanswerable (22%; empty replies left out, see R1). These questions often swap who did something (asking about the player what she herself did),
  so part of it is the benchmark's trickiness; LongMemEval's 4 unanswerable questions were all handled.
- **To do:** read the failures; whether her recalled memories should say plainly when nothing relevant was found.

## M6. What has left the window comes back only about half the time

- **What the player would notice:** something said a few weeks back, word for word gone from the window, isn't
  there when she answers.
- **Evidence:** LoCoMo conversation 26 at the 8k window: 101 answerable questions had their evidence folded away;
  her search had it in its best four for 56%, and recall passed it into her turn for 48%; those questions were 73%
  right against 84% for evidence still in the window (empty replies left out). On LongMemEval (32k window, 9 questions) search found it 8 of 9.
- **Cause, to check:** an excerpt is the best-matching line and the one after, so a passage can be found while the
  line that answers isn't shown; recall keeps two old lines at most within 450 tokens of memories; questions like
  "When did Caroline…" lean on names, which shared-word scoring takes out for her own.
- **To do:** compare against the 32k run of the same conversation (everything word for word), then try more excerpts
  or longer ones.

## R1. The provider sometimes sends back an empty reply, and she says nothing

- **Evidence:** 15 of 199 LoCoMo answers (2026-10-11) came back with no words and no usage, no error, while about ten
  requests ran at once on the player's provider; asked again alone they answered. LongMemEval's runs also saw
  "Service request failed" twice.
- **To do:** in the app, ask once more when a reply ends with no words at all; the benchmark now counts it as a
  failure to ask again.
