# Her memory as a dated log of observations (2026-10-11)

Decided with the player on 2026-10-11, after measuring her memory on LongMemEval and LoCoMo
([../research/2026-10-11-memory-benchmark.md](../research/2026-10-11-memory-benchmark.md); issues in
[../memory-issues.md](../memory-issues.md)).

## Why

- **What she can see of the past is a keyhole.** Beyond the window she reads word for word, she has two notes, one
  summary of at most 3,200 characters and 450 tokens of recalled memories: about 2,000 tokens. On LoCoMo at the 8k
  window, 31 of her 40 wrong answers on old evidence were evidence that never reached her (M6); when it did reach her
  she was as right as with the whole conversation in front of her.
- **The summary is rewritten whole at every fold** and cut at its cap, so the newest part is what is lost (M1), and
  each rewrite blurs what came before.
- **The same talk is digested three times** (the summary, the day's moments, the facts): most of why upkeep costs
  about three times the conversation (M3).
- **A dated log works.** Mastra's observational memory keeps a dated, prioritised log of observations in the prompt,
  written by one request (the observer) and condensed by another (the reflector), with no search at all; on
  LongMemEval it scores 84% with GPT-4o and 95% with gpt-5-mini, where GPT-4o reading the whole history scores about
  60% ([Mastra](https://mastra.ai/research/observational-memory)).

## The design

- **The log.** Rows, not prose: each observation has the day it was said, a priority (high, medium, low), its text,
  and the lines it came from. Nothing is ever rewritten in place.
- **Observing** takes the place of folding the window into the summary, at the same moments (the window full, or three
  quarters full while the talk is quiet): the lines leaving the window are read a few thousand tokens at a time, with
  her latest observations for continuity, and the model answers with JSON, one observation per thing worth keeping,
  dates written out in full. A narrow request that sees one batch, never the whole history.
- **What she sees:** her newest observations, as many as fit three eighths of her window (12k tokens at 32k), at
  the start of her prompt, where providers cache it; it only grows at its end, between conversations. The code lays
  them out (a heading per day with how long ago it was, `!` before what matters most), so the format, the dates and
  the size are never the model's to keep.
- **Nothing is lost:** observations older than what fits stay in the log and are recalled like moments (by shared
  words and meaning, with their date); the lines they came from stay searchable word for word.
- **An existing summary** becomes the log's first observation.

## Phases

1. **The log in place of the summary** (observing, what she sees, older ones recalled, the chat history showing it).
   Measured against the runs of 2026-10-11 on the old build: LoCoMo at the 8k window, question by question, and the
   LongMemEval sample on knowledge that changed, time and combining sittings.
2. **Condensing:** when the log outgrows what she sees, a second narrow request condenses its oldest part (days into
   weeks, weeks into months), keeping what is marked high nearly word for word; the originals stay, recallable.
3. **One digestion:** facts, plans and notes drawn from the observations instead of from the raw lines again, and the
   day's moments retired (the memory book shows the log). Facts get dates of validity: an update closes the old fact
   instead of deleting it.
4. **What fits a companion:** a day's entry from the cabinet's own records (solves, focus, play, activities, her
   texts), written by code, not by the model; open threads to follow up; what she has said about herself; which
   memories a reply used, to see and correct.
