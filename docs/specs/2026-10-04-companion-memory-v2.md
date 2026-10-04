# Companion memory, second version: cache-aware, bounded, with a sense of time (2026-10-04)

The first version ([../research/2026-10-02-companion-memory.md](../research/2026-10-02-companion-memory.md)) works,
but its budgets were set as if every model had a small context window and no prompt cache. The player asked for a
design that suits a companion meant to last years: modern context sizes, good cache hits, memory that does not grow
without end, cost under control, and a sense of time.

## What is wrong with the first version

| | Now | Problem |
|---|---|---|
| Conversation kept word for word | the newest ~6,000 tokens; at 4,000 not yet summarised, all but the newest ~1,500 are folded | she summarises every 10–20 exchanges and forgets the detail of what was said half an hour ago, though today's models take 128k–1M tokens |
| Prompt cache | the order is right (rules, notes, summary, conversation, then this turn), but each fold rewrites the summary inside the system prompt | the cached prefix is thrown away every 10–20 exchanges, mid-conversation; and at 6–8k tokens the prompt is barely worth caching |
| Anthropic | no `cache_control` sent | Claude caches nothing |
| Moments | one per quiet gap (10 minutes, 2 in a game), even for "hi"; weeks are rolled up, nothing beyond | they pile up without end |
| Facts | upkeep sends every known fact to the model to reconcile against | after months, thousands of tokens per upkeep, and worse judgement |
| Time | the time now, and a marker when talk resumes after 2 hours | no "you last talked yesterday", no plans or deadlines that pass: "interview on Friday" stays a plain fact after Friday |

## How providers cache now (read 2026-10-04)

- **Anthropic:** `cache_control` marks; a top-level one turns on automatic caching, whose mark moves forward as
  the conversation grows. Reads cost 0.1× input, writes 1.25× (5-minute life) or 2× (1 hour); at least 1,024
  tokens. ([finout](https://www.finout.io/blog/anthropic-api-pricing), [respan](https://www.respan.ai/articles/claude-prompt-caching))
- **OpenAI:** automatic for prompts from 1,024 tokens, matched on the prefix; since May 2026 caches last up to 24
  hours by default on GPT-5 models; cached input about 0.1×. ([OpenAI](https://developers.openai.com/api/docs/guides/prompt-caching),
  [Effloow](https://effloow.com/articles/openai-prompt-cache-retention-24h-cost-proof-2026))
- **DeepSeek:** automatic, on disk; a hit costs about 2% of a miss. ([DeepSeek](https://api-docs.deepseek.com/news/news0802/))
- **Gemini:** implicit caching from 2,048 or 4,096 tokens by model. ([Google](https://ai.google.dev/gemini-api/docs/caching))

All of them reward one thing: **a long prefix that stays the same, and a conversation that only grows at its end.**

## Design

### 1. The prompt, from what never changes to what changes every turn

1. **Rules and persona** (system). Changes only with settings.
2. **Notes always in mind**: about the player, about the two of them (system). Rewritten only by upkeep, which runs
   when the conversation has gone quiet.
3. **The summary of what came before the window** (system). Rewritten only when the window is folded, and folding
   happens when the conversation is quiet, except in an emergency (below).
4. **The window**: the conversation since the summary, word for word, append-only, with a time mark when the date
   changes or talk resumes after half an hour.
5. **This turn** (never stored, so it never disturbs the prefix): the time now and how long since they last talked,
   what is coming up, the cabinet's numbers, the memories recalled for this message, and the message.

During a conversation everything up to the newest line is a cache hit; only this turn and the reply are new.

### 2. A large window, folded rarely and while quiet

- **Window size** is a setting (Chat settings → "Kept word for word"): 8k, 32k or 100k tokens. The default is 32k
  for cloud providers and 8k for local servers (Ollama, LM Studio), whose context is usually small.
- **Folding while quiet:** when upkeep runs (the conversation quiet for 10 minutes) and the window is past three
  quarters full, everything but its newest half is folded into the summary. A 5-minute cache has gone cold by then
  anyway, so the rebuilt prefix costs nothing extra; the next conversation starts with half a window of room.
- **Emergency fold:** a window that fills up mid-conversation is folded at once, to half. With 32k that is after
  about 16k tokens of talk without a 10-minute pause: rare.
- **The summary** may be longer (about 400 words): the window carries the recent detail, the summary the thread of
  what came before, facts, moments and the agenda what lasts.

Cost, for a sense of scale: at 32k, the first message after a long break reads up to ~35k tokens uncached (about
$0.10 on Claude Sonnet, $0.18 on GPT-5.5, $0.01 on DeepSeek V4.1 Flash); messages in a conversation read it from the
cache at a tenth of that or less.

### 3. Caching per provider

- **Anthropic:** automatic caching on (top-level `cache_control`, 5-minute life), through the SDK's
  `RawRepresentationFactory`.
- **OpenAI-compatible:** nothing to send; the prefix discipline above is what makes the hits.
- **Measured:** each reply logs input, cached input and output tokens, and Chat settings shows how much of the
  last replies' input was cached.

### 4. A sense of time

- **This turn says when "now" is**, and how long since they last talked ("yesterday evening, 17 hours ago").
- **Time marks in the window** when the day changes or talk resumes after half an hour.
- **Recalled memories carry their age** ("3 weeks ago, Fri 11 Sep: …").
- **An agenda** (phase 3): plans, deadlines, promises and reminders, each with its date. What is coming up in the
  next week is in every turn; what has just passed is too, so she can ask how it went; once past it becomes a moment
  and leaves the agenda. Reminders are agenda items that also notify: a bubble, her voice and a Windows notification
  at the time, and a word when the player comes back if they were away. Upkeep files dated things in the agenda
  instead of as facts; she can also add one in the conversation (`[plan: …]`, like `[note: …]`).

### 5. Memory that stops growing

| | Bound |
|---|---|
| Window | its size setting |
| Summary | about 400 words |
| Notes | 1,400 characters each (as now) |
| Moments | none for small talk; at most one a day (upkeep updates the day's moment instead of adding another); days roll into weeks after 14 days (as now), weeks into months after 90 days, months into years after two years |
| Facts | they fade unless they come up (as now); upkeep reconciles a stretch of conversation against the facts that relate to it (by meaning and shared words, plus the most important), not against all of them |
| Agenda | past items become moments and leave it |
| The conversation itself | kept in full for the chat history, on disk only (text: about a megabyte per 5,000 lines) |

## Phases

1. **The prompt and the window:** layout, window setting, folding while quiet, Anthropic caching, cached-token
   logging, "now" and "last talked", time marks.
2. **Bounded memory:** moments (small talk, one a day, month and year roll-ups) and facts reconciled against the
   related ones.
3. **The agenda and reminders.**
