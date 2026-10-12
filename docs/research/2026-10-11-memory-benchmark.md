# Measuring her memory: benchmarks, and whether we need a vector database (2026-10-11)

The player's worry: as the conversation grows, does her long-term memory start getting facts wrong, above all in its
summaries and in facts that change? Does her search find what has left the window, and does she stay herself deep
into a long conversation? And would ChromaDB, a vector database, help? Issues found are tracked in
[../memory-issues.md](../memory-issues.md).

## Which benchmarks

| | What it is | Fit for her |
|---|---|---|
| [LongMemEval](https://github.com/xiaowu0162/LongMemEval) (Wu et al., ICLR 2025) | 500 questions, each with its own chat history between a user and an assistant: the S set about 115k tokens and 40 to 60 sittings, the M set about 1.5M. Five abilities: pulling out a detail, combining sittings, time, **knowledge that changed**, and **saying "you never told me"** (30 unanswerable questions) | **The one to use.** It is our shape (the player talking to her over many sittings), and two of its abilities are exactly the factual errors to fear. We use the cleaned release ([Hugging Face](https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned), MIT), which drops noisy sittings |
| [LoCoMo](https://github.com/snap-research/locomo) (Maharana et al., 2024) | 10 conversations between two people over weeks, 11k–22k tokens each, 1,986 questions (446 of them unanswerable) | The one everyone reports (Mem0, Zep, Letta), so the one to compare on. Two people talking, not a player and her: the first is taken as the player, the second as her, and her facts track only the player. At her 32k window a whole conversation stays word for word, so it tests her search only at the 8k window. Its answer key has errors and its scoring is disputed ([Zep](https://blog.getzep.com/lies-damn-lies-statistics-is-mem0-really-sota-in-agent-memory/), [an audit](https://penfieldlabs.substack.com/p/we-audited-locomo-64-of-the-answer)); CC BY-NC, never committed |
| [HaluMem](https://github.com/MemTensor/HaluMem) (2025) | User–assistant histories of about 160k (Medium) and 1M tokens (Long) with every memory point annotated: what should be extracted, which are updates | **The next one**: it grades each step on its own (what was extracted, whether updates were applied, the answer) with a hallucination rate for each, which is the player's worry about facts and updates. Scores published for Mem0, Zep, MemOS and others. CC BY-NC-ND |
| [BEAM](https://github.com/mohammadtavakoli78/BEAM) (ICLR 2026) | 100 conversations of 128k to 10M tokens, ten abilities | Years of use; later, it is costly |
| [MemoryAgentBench](https://arxiv.org/html/2507.05257v1) (2025) | Memory agents fed documents in pieces; FactConsolidation checks a newer fact overwrites an older one | Its inputs are documents, not talk |
| [LongMemEval-V2](https://arxiv.org/html/2605.12493v1) (ICML 2026) | Not a newer LongMemEval: web agents remembering their past trajectories through sites | Not ours |
| [MemoryCD](https://arxiv.org/html/2603.25973) (2026) | Years of Amazon reviews per user; rating prediction, ranking, review writing (ROUGE, BLEU) | Not ours: it measures predicting tastes, not remembering a conversation |

The three most cited for chat memory in 2026 are LoCoMo, LongMemEval and BEAM
([Mem0's guide](https://mem0.ai/blog/ai-memory-benchmarks-in-2026)). Vendor scores run from about 50% to 96% on
LongMemEval and are disputed, set up differently and answered by different models.

## How it is run

`tools/bench/memory.cs` (see its header). A history goes into a fresh memory as lines between the player and her, a
turn every 30 seconds from each sitting's date, and in the quiet between sittings her upkeep runs as `Chat.Upkeep`
does (moments, facts, plans, vectors, folding the window into the summary) until it has nothing left to do. Then the
question is put to her through `Conversation.Answer`, her real prompt and recall, and graded. Everything runs on the
player's own model (a GPT model through their provider), the judge too.

- **LongMemEval**, each question at its date: **memory** (her memory as the app runs it, the 32k window), **window**
  (the whole history word for word, about 106k tokens, no upkeep), **oracle** (only the sittings holding the answer:
  the ceiling), and **audit** (for what memory mode answered: where the answer's sittings ended up, whether her
  search finds them, and a judge's reading of what her notes, summary, facts and moments kept). Graded with
  LongMemEval's own prompts (`evaluate_qa.py`).
- **LoCoMo** (`--set locomo`): each conversation goes in once, then each question is asked on a copy of that memory a
  day after its last sitting, with where its evidence is and whether her search and recall bring it back. Graded
  twice: LongMemEval's strict prompt, and Mem0's generous one (their paper's appendix A), the grade behind the
  published scores.
- **Staying in character** (`--set persona`): her window filled to 0, 8k, 32k and 100k tokens with the newest lines
  of a LongMemEval history, someone asking an assistant about everything, her side of it written as a plain
  assistant would (the hard case, and a fair picture of hours of help with problems); eight everyday messages, twice
  each, her Japanese voice on as in the demo; a judge with her card scores each reply 1 to 5 for her manner, for
  nothing contradicting the card, and for reading as her rather than an assistant.

## Results so far (2026-10-11)

### LongMemEval, one question of each of its ten kinds

| | Right | Tokens a question |
|---|---|---|
| Oracle (only the sittings that hold the answer) | 10 of 10 | 7k |
| Window (the whole history word for word) | 10 of 10 | 106k |
| Memory (32k window, summaries, facts, recall) | 9 of 9 (one lost to the provider, to ask again) | 360k, 94% of it upkeep |

The audit of the nine: every answer's sittings had been folded out of the window, so they were answered from memory;
her search had them in its best four for 8 of 9; her notes, summary and facts held the answer for 7, and the other
two were answered from the conversation her search brought back. The one "outdated" thing the judge found was a
dated moment, true on its date; the fact itself had been updated and the old one retired.

Ten questions can't tell these apart; a 44-question sample of knowledge that changed, time and combining sittings
is running.

### LoCoMo, conversation 26 (of 10), her 8k window

| Kind | Right (strict) |
|---|---|
| single-hop | 55 of 64 |
| temporal | 25 of 36 |
| multi-hop | 17 of 31 |
| open-domain | 10 of 12 |
| **all answerable** | **107 of 143 (75%)** |
| unanswerable | 9 of 41 (22%) |

Evidence still in the window: 84% right; folded away: 73%, with her search finding it for 56% and recall passing it
to her for 48% (issue M6). The full run, all ten conversations at 8k and at 32k (everything word for word, our own
baseline), both grades, is running.

### Staying in character (Hana's card, written for the demo and not committed)

| History in her window | Replies | Her manner | Consistent | Herself, not an assistant | Words | Lists |
|---|---|---|---|---|---|---|
| none | 12 | 4.9 | 5.0 | 4.8 | 19 | 0 |
| 8k | 16 | 5.0 | 5.0 | 5.0 | 21 | 0 |
| 32k | 15 | 4.9 | 5.0 | 4.9 | 20 | 0 |
| 100k | 14 | 5.0 | 5.0 | 5.0 | 19 | 0 |

No drift at 100k tokens of assistant-style talk: she turned down nothing of her card, kept to about twenty words and
never fell into lists. The reminder of who she is at the end of every turn (spec memory v2, §6) is the likely reason.
The judge is her own model and was generous, and these messages don't ask for long help; harder probes (an
explanation, a week's plan) are the next step for this test.

### The log against the old memory (LoCoMo conversation 26, the 8k window, question by question)

The old memory's two runs differ by about 8 points on the same questions, so a change is judged question by question
(`ref\paired.py` in the benchmark folder), never on one run's total.

| Answerable questions | Old memory | Phase 1, the log |
|---|---|---|
| against the old memory's first run (150) | 75% | 76% (16 right only with the log, 15 only with the old) |
| against its second run (137), strict | 69% | 77% (18 against 7) |
| the same, Mem0's grade | 78% | 88% |
| temporal | 21 | 26 |
| evidence folded out of the window | 70 | 80 |

Unanswerable questions marked right fell from 23% to 13% (10% against the second run): nearly all are LoCoMo's name-swap traps, which she
answers about the right event (issue M5); not a memory she made up.

### The old memory over more of LoCoMo

At the 8k window over all ten conversations (1,482 answerable questions): 69% right on the strict grade, 76% on
Mem0's. At 32k, the whole conversation word for word or nearly (42 to 50 run past it), nine conversations and 1,286
questions: 82% and 86%. So the old memory lost about 13 points against having everything in view; on conversation
26 the new memory closes that gap (84% at 8k, against 81% for everything in view on the same questions). Not yet
run: the new memory on the other nine. LongMemEval's 44-question sample on the old memory was paused at 22 (20
right), at the player's word: each question took about 14 minutes, its 115k tokens of history all taken in.

### Retrieval, measured without a model

`--set locomo --mode reach` replays her search and recall on the memories already taken in (9 conversations, 1,376
answerable questions with evidence) and counts where the evidence ends up. Brought back into her prompt by search:
31% as it was; 35% with six passages in 2,000 tokens; 38% with ten found instead of four; **54% with whole passages
instead of the best line and the next**; 56% with old talk ranked by relevance whatever its age (multi-hop 4% to 22%).
Eight passages in 2,500 tokens gave 58%, not worth a thousand more tokens on every message. Built on the branch
`retrieval`, and confirmed with answers (below).

### What upkeep costs

LoCoMo conversation 26 at the 8k window: the old memory 66 upkeep requests and about 115k tokens; phase 1 about the
same; phase 3 92 requests and 179k (facts inflated to 116 from 55, and the always-present notes rewritten after every
batch). The fix (always-present notes at most once a day, facts only what lasts) brought it to 81 requests and 134k:
two-thirds of the rise taken back, still about a fifth over the old memory.

### Phase 3 and retrieval, answered (LoCoMo conversation 26, the 8k window)

All 199 questions answered (dropped replies asked again); strict grade, answerable questions, question by question:

| Against | Theirs | Phase 3 with the fixes and retrieval |
|---|---|---|
| the old memory's first run (152) | 75% | 84% (22 right only now, 9 only then) |
| its second run (139) | 69% | 83% (26 against 6) |
| the same, Mem0's grade | 78% | 87% |
| phase 3 alone (152) | 78% | 84% (15 against 7) |

Phase 3 alone: 119 of 152, time questions 31 of 37 (the old memory 21, phase 1 26). Retrieval added the multi-hop
questions (15 to 21 of 32): of 108 questions whose evidence had left the window, search found it for 87% and it
reached her turn for 82% (43% before). Answering reads about 7% more a question for the larger recall, 78% of it
from the provider's cache. Never-said questions declined: 15% (M5).

### Her profile from her facts (LoCoMo conversation 26, the 8k window)

What she always has in mind listed from her facts by code instead of rewritten by the model, facts asked for in at
most 20 words, long merges refused (M8, M9). Answered again on the last version's memory: 125 of 151 against 127
(7 right only then, 5 only now), no difference. From scratch, every question answered: **127 of 152 (84%)**, the
same as the last version (7 flipped each way); Mem0's grade 137 (90%); never-said declined 7 of 47, as before.
Against the old memory's runs: 84% against 75%, 83% against 69%. Upkeep 56 requests and 116k tokens (the last
version 81 and 134k, the old memory 66 and 112k), taking the conversation in in 15 minutes instead of about 30.
Facts: median 16 words, the longest 27, 3 over 25 (the last version: median 23, longest 68, 25 over).

### The final build on more of the benchmarks (2026-10-11)

The merged memory (phases 1 to 3, retrieval, the profile from facts, the three small fixes), at the 8k window:

| | Old memory, 8k | Old memory, whole conversation in view (32k) | New memory, 8k |
|---|---|---|---|
| LoCoMo conversation 42 (199 questions) | 67% | 81% | **80%** (17 against 14 against 32k) |
| LoCoMo conversation 44 (122 questions) | 75% | 84% | **84%** (6 flipped each way against 32k) |
| LongMemEval, 9 of the old sample's questions | 7 of 9 | | **9 of 9** |

Conversation 44 (28 sittings, 675 turns): time questions 19 of 24 against 16 for both old runs; every question
answered, 103 of 123 (84%), 87% on Mem0's grade. Upkeep 207k tokens against the old memory's 174k (+20%; on
conversation 26 it was +4%). LongMemEval: upkeep read about 30% less a question (196k to 265k against 316k to 363k),
each question's 115k tokens of history taken in, in about 12 minutes. Conversation 42 (29 sittings, 629 turns), every
question answered: 80% (84% on Mem0's grade); multi-hop 11 to 21 of 37 and time 24 to 34 of 40 against the old
memory at 8k, both a little above the whole conversation in view (19 and 32); upkeep 210k tokens against 184k (+14%).
So on all three conversations run, the memory at an 8k window answers as well as the whole conversation in view, 9 to
13 points above the old memory at 8k; upkeep costs 4 to 19% more than the old memory's on LoCoMo, 30% less on
LongMemEval's longer histories.

**Never-said questions** (LoCoMo's adversarial category) are declined about as rarely with the whole conversation in
view (5 of 39 and 7 of 35) as by the memory (6 and 6): not a memory gap. They swap the two speakers' names, and she
answers about the one it happened to, at times correcting the premise and still marked wrong. Mem0 left the category
out "because ground truth answers were unavailable"; our totals leave it out too. What matters for the app is
mixing up her life and the player's ("Eagles mesmerize you most", her own interest): her own story (phase 4) is for
that. Genuinely never said, LongMemEval's unanswerable questions: 3 of 3.

### Her own story and open threads (phase 4, conversation 26)

From scratch with both: 121 of 152 (80%) against 127, the questions lost about her own life, answered from facts about
her listed in full though some were kept wrong (a dog named Oliver, a cat). With her list taking the player's rule
(importance 6 and up), answered again on the same memory: 128 of 152 (84%), never-said 7 of 47 as before. Upkeep
133k tokens against 116k (+15%). Of 10 threads opened, 9 lapsed in LoCoMo's weeks between sittings; LoCoMo has no
question that a follow-up would answer, so threads are for the smoke test and daily use to judge.

### Facts checked against the conversation (2026-10-11)

Every fact kept in three LoCoMo memories (380: conversation 26 with her own story, 42 and 44 on the final build;
set-aside ones included) went to a model with the whole conversation in view, in batches, for a verdict and the turns
that show it; each fact not judged right was then read by hand. 330 right (87%), 39 partly, 4 wrong, 7 out of date
or not said, none about the wrong person (three say "her" of a man: our prompts call the companion "she"). Most
"partly" verdicts are a little more than was said ("annual" camping trips) or a fact rewritten or merged and judged
at its oldest part's day; the judge is lenient (it passed "a dog named Oliver"). Of the 4 wrong, two were rewritten
in place and kept their first day, one is an old wish rightly set aside, one merged two of the player's books into
one. The two kinds of real error were both in code, not in what the model kept: the sameness check took a changed
name for a rewording and wrote over it (M11), and the weekly review's merges dropped a name or number in 35 of 499
(M12). Both fixed (da4f101). What did go missing is upstream: things said in passing with a date reach no note with
it (M13).

### Prompt caching, reviewed (2026-10-11)

Her prompt runs from steadiest to least steady: her character, rules, the lists always in mind and the diary notes in
view (changed by upkeep while the player is quiet, at a fold every few days, and at midnight by the day headings);
then the conversation word for word (only grows until a fold); last the newest message with the turn's blocks (time,
agenda, recall, her character's reminder). Her tools are the same list every request. That is the shape prefix
caching wants. Measured: 35 to 37% of her reply input from cache in the player's daily use (10 and 11 Oct), 78 to 83%
in the benchmarks' back-to-back questions; the difference is the cache's life, minutes after last use by default.
The lever is a longer life: GPT-5.6 models take `prompt_cache_options.ttl` (up to 24 h), but on them cache writes are
billed besides the cheaper reads; a `prompt_cache_key` per character is a routing hint. Not tried: the player saw no
need for a probe yet (whether their provider passes either on). Anthropic's path already marks the prompt for caching.
Upkeep is not cached and is small (about 4k tokens a day).

## Comparing with Mem0, Zep and Letta

- **LoCoMo** is where they publish: all ten conversations, the 1,540 answerable questions, Mem0's generous grade,
  answered by GPT-4o-mini (full context about 73%, Mem0 about 67%, Zep about 75%, Letta's plain files 74%). With
  1,540 questions a score is good to about ±2 points. Her answers come from a different model, so the comparison that
  holds is each one's gap to its own full context: Mem0 about −6 points, Letta about +1; hers is her 8k run against
  her 32k run.
- **LongMemEval**: vendors report all 500 (about ±4 points). For tracking our own changes, about 100 questions across
  its kinds (±9 points) is enough, and a before/after on the same questions needs fewer, as a change shows question
  by question.

## A vector database?

**No.** What decides whether she finds the right memory is the embedding model and how matches are scored, not where
the vectors are kept, and our scale is small:

- She keeps vectors (multilingual-e5-small, 384 numbers each) in her SQLite file and scores them in memory, every
  one, exactly. Measured: 7 ms a search at 344 passages, 20–27 ms at 10,000 (about a year of heavy talk), 80 ms at
  50,000. What grows is the first search after start and the memory it takes (issue M4), mostly from the words, not
  the vectors.
- [ChromaDB](https://www.trychroma.com) runs inside the program only from Python or JavaScript; from .NET it is a
  separate server to install and keep running beside the cabinet ([the Rust client's notes](https://crates-io.ganymede-static.progval.net/crates/chromadb/0.4.2),
  [the .NET client](https://feed.nuget.org/packages/ChromaDB.Client/0.9.0) is HTTP only). Its index (HNSW) is
  approximate, a trade worth making at millions of vectors, not thousands.
- [sqlite-vec](https://github.com/asg017/sqlite-vec) (0.1.9, before 1.0) does the same exact scan in C inside SQLite;
  it would take over only the vectors, the smaller part. If her memory ever reaches hundreds of thousands of passages,
  it is the step to take, not a second server.
