# Her memory: issues found

A living list of what goes wrong in her long-term memory, found by the benchmarks
([research/2026-10-11-memory-benchmark.md](research/2026-10-11-memory-benchmark.md), `tools/bench/memory.cs`) or in
use. Each says what the player would notice, the evidence, the cause where known, and what to do. Close an issue
only with a re-run that shows it fixed, and say which.

| # | Issue | Status |
|---|---|---|
| M1 | The summary is cut off at its newest end | fixed on the branch `observational-memory`: no summary, a log of whole notes |
| M2 | Facts keep relative times, which go stale | open |
| M3 | Upkeep costs about three times the conversation | phase 3 made it worse; fixed on the branch `retrieval` (3afb21d), being re-measured |
| M4 | Search starts slowly and grows in memory with years of talk | open, not yet urgent |
| M5 | Asked about something never said, she often makes up an answer | open |
| M6 | What has left the window comes back only about half the time | fixed on the branch `retrieval` by the free score; a real run to confirm |
| R1 | The provider sometimes sends back an empty reply, and she says nothing | open (the player's provider, under load) |
| M7 | Her always-present notes were cut mid-word at 1,400 characters | fixed on the branch `observational-memory` |
| R2 | An answer cut at the provider's length limit could be kept cut, or lost | fixed on the branch `observational-memory` |

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
- **Phase 3 made it worse** (2026-10-11, LoCoMo conversation 26 at the 8k window): 92 upkeep requests, 113k tokens read
  and 66k written, against 66, 87k and 25k for the old memory; 179k in all against about 115k. Two causes feeding each
  other: kept from dense notes, nearly every event became a fact (116 against 55, 80 of them important), and every
  batch that touched an important fact rewrote the always-present notes from up to 40 facts.
- **Fix** (3afb21d, branch `retrieval`): a batch only marks a note out of date; `Conversation.RefreshNotes` rewrites it
  once the talk is noted and quiet, at most once a day each, at once while the note is empty or when something important
  in it is no longer true (a fact changed or ended: a note saying what isn't so is the error that matters); upkeep's request says
  facts are what lasts beyond the day (her notes keep the events). Being re-measured (`runs\p3fix-locomo`). LoCoMo's
  sittings are days apart, so it shows the fewer facts more than the once a day; daily use gains more from that.

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
- **Read 2026-10-11:** nearly all of LoCoMo's misses are its name-swap traps ("What did Caroline realize after her
  charity race?" when it was Melanie's): with the second person as her, she answers about her own race ("After the
  charity race, I realized…"), sometimes pointing the swap out ("If you mean your necklace, Caroline…"), which the
  grade counts as wrong. With her notes of observations more is in view, so she answers more of them (13% marked
  right against 22%). That is not a memory she made up; LongMemEval's never-said questions, things truly never
  mentioned, are the measure to watch for this (all 4 handled so far).
- **To do:** watch LongMemEval's unanswerable questions; whether her recalled memories should say plainly when nothing
  relevant was found.

## M6. What has left the window comes back only about half the time

- **What the player would notice:** something said a few weeks back, word for word gone from the window, isn't
  there when she answers.
- **Evidence:** LoCoMo conversation 26 at the 8k window: 101 answerable questions had their evidence folded away;
  her search had it in its best four for 56%, and recall passed it into her turn for 48%; those questions were 73%
  right against 84% for evidence still in the window (empty replies left out). On LongMemEval (32k window, 9 questions) search found it 8 of 9.
- **Cause, to check:** an excerpt is the best-matching line and the one after, so a passage can be found while the
  line that answers isn't shown; recall keeps two old lines at most within 450 tokens of memories; questions like
  "When did Caroline…" lean on names, which shared-word scoring takes out for her own.
- **Measured without a model** (2026-10-11, `--mode reach`: her real search and recall on the nine conversations
  taken in, 1,376 answerable questions): the evidence reached her prompt for 31%. No age penalty for old talk: 31%.
  Six passages in 2,000 tokens instead of two in 450: 35%. Ten found instead of four: 38%. **Whole passages instead of
  the best line and the next: 54%** (the line that answers is often not the one that matched best). Together, with
  old talk ranked by relevance alone: **56%**; eight passages in 2,500 tokens gave 58%, not worth a thousand more
  tokens on every message. Multi-hop, which needs every piece: 4% to 22%.
- **To do:** confirm with a real run that more of it reaching her means more right answers.

## R1. The provider sometimes sends back an empty reply, and she says nothing

- **Evidence:** 15 of 199 LoCoMo answers (2026-10-11) came back with no words and no usage, no error, while about ten
  requests ran at once on the player's provider; asked again alone they answered. LongMemEval's runs also saw
  "Service request failed" twice.
- **To do:** in the app, ask once more when a reply ends with no words at all; the benchmark now counts it as a
  failure to ask again.

## M7. Her always-present notes were cut mid-word at 1,400 characters

- **Evidence:** 23 of the 35 notes in the benchmark memories (2026-10-11) were exactly 1,400 characters, ending
  mid-word ("…especially flowers, painting"): asked for at most 1,300 characters, the model went over two times in three.
- **Fix:** the note is asked for in words (180), which models keep to far better than characters; one that comes
  back over its room is sent back once to be shortened; only then is it trimmed, at its last whole sentence
  (`Prompt.Fit`), never mid-word. Memos she prints are fitted the same way.

## R2. An answer cut at the provider's length limit could be kept cut, or lost

- **Evidence:** every request has a ceiling on what comes back (`MaxOutputTokens`), and a model that thinks first spends
  it on its thinking too: a GPT model thought 1,522 tokens for a short story. The player's provider ignores the ceiling
  and says "stop" (2026-10-11), but Anthropic always holds to it, and OpenAI does directly: there a reply of hers had
  600 tokens for thinking and words together, a cut note was kept cut, and a cut JSON answer failed three times and
  its stretch of conversation was skipped.
- **Fix:** a cut answer is recognised by how it ended (finish reason "length"), never kept, and asked again once
  with twice the room (`Conversation.Whole`); a batch that still doesn't fit is halved (observing at once, a digest
  or a review the next time). Her replies have 2,000 tokens of room, a ceiling and not a target, and the log says
  when one was cut short. Research agrees: check how an answer ended rather than its text, count thinking inside the
  limit, and don't trust length instructions; models miss them, more so for long targets
  ([the finish reason your code never inspects](https://tianpan.co/blog/2026/06/01/the-finish-reason-your-code-never-inspects),
  [Claude's max tokens and thinking budgets](https://fast.io/resources/claude-max-tokens.md),
  [LIFEBench, NeurIPS 2025](https://arxiv.org/abs/2505.16234)).
- **Not yet:** a character card over 8,000 characters is still cut at that length, mid-word (the player's own text).
