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
   logging, "now" and "last talked", time marks. *Built 2026-10-04* (tests; the request checked against a mock
   provider; Anthropic's cache flag checked against a fake endpoint).
2. **Bounded memory:** moments (small talk, one a day, month and year roll-ups) and facts reconciled against the
   related ones. *Built 2026-10-04* (tests).
3. **The agenda and reminders.** *The agenda built 2026-10-04:* `[plan: …]` and `[remind: …]` in her replies,
   upkeep's "plans" (add, update, cancel), coming up and just past in every turn, past items leaving after two
   days, reminders on a ticket, in her bubble and voice and with an optional chime, missed ones going off at the
   next start, and the memory book's "Coming up" to read, add, edit and delete. Delivery elsewhere: section 10.

## Second pass (decided with the player, 2026-10-04)

### 6. Her character held steady in a long window

The card stays at the top of every request (never summarised, never pushed out), but with 32k–100k tokens of talk
after it, a model drifts towards the conversation's voice. Character-card apps (SillyTavern and its kin) answer this
with text placed *after* the history: the card's `post_history_instructions`, and its "character's note"
(`extensions.depth_prompt`), which they insert a few messages from the end. Here both go into the turn itself, with a
short reminder of our own when the card has neither: the turn is new every time anyway, so it never breaks the
cached prefix (a note a few messages back would, for those messages, every turn). It is read on every reply and
never stored; a few hundred characters at most.

### 7. Moments: a clear length, and busy days

- A day's moment: one to three sentences, at most 60 words. A day may have up to three, one per distinct thing that
  happened; upkeep is shown the day's moments and either rewrites the one the new talk continues, adds a new one
  (something else, while the day has fewer than three), or files nothing (small talk).
- Rolled up: a week at most 80 words, a month 100, a year 150.

*Built 2026-10-04* (`Upkeep.Request`'s "continues"; tests).

### 8. The whole conversation, searchable

Rolled-up moments lose detail by design; the conversation itself is kept, so she should be able to find it. It is
split into passages (lines from one sitting, about 250 tokens each), each with a vector from the local model when
one is loaded. Each reply searches the passages before the window by meaning and by shared words (Chinese and
Japanese as pairs of characters, as recall already does), and the best one or two come into the turn's memories
as an excerpt (the line that matches and the one after) with their date. Scored in memory rather than with SQLite's
full-text index: its trigram tokenizer cannot match two-character Chinese words, and a year of talk is a few
thousand passages, milliseconds to score. Passages are made as lines leave the window; their vectors come during
upkeep, the existing history a batch at a time. *Built 2026-10-04* (`History`; tests).

### 9. Memory that tends itself

ChatGPT has managed saved memories itself since October 2025: what matters stays "top of mind", the rest moves to
the background instead of being deleted, and the user can search, re-prioritise and restore
([OpenAI](https://x.com/OpenAI/status/1978608684088643709)). Claude (since July 2026) keeps memory as separate
entries that it reads and updates during conversations
([memorylake](https://www.memorylake.ai/en/blogs/claude-new-memory-entries)). Here, beside fading and upkeep's
updates and deletes:

- **A review while quiet**, about weekly or when she has gathered many new facts: the model reads her facts a batch
  at a time and merges duplicates, and retires what is obsolete (plans already past, things contradicted, trivia
  that never came up again).
- **Retired is not deleted:** a retired fact is no longer recalled or reconciled against, and shows in the memory
  book under "Retired", where it can be restored.

*Built 2026-10-04* (`Conversation.Review`): weekly, or after 30 new facts, 80 facts a request, carrying on from where
the last batch stopped; pinned facts are never touched, and a merge keeps the oldest learned date. Upkeep runs it only
when there is nothing to digest or roll up and the talk is quiet. A retired fact that upkeep would add again is
restored instead. The memory book's "Retired by her review" lists each with when and why, to bring back or forget for
good.

### 10. Reminders: where they reach the player

Every reminder shows in the cabinet: a printed ticket popup (as for low time), her bubble, and her voice. Chosen in
the reminder settings, it can also:

- play the reminder sound;
- raise a Windows notification;
- go to the phone or another app: an [ntfy](https://ntfy.sh) topic (free phone push, no account), a Discord
  webhook, a Telegram bot, or any webhook (a JSON POST). These are the usual choices for personal alerts
  ([Big Iron](https://www.bigiron.cc/guides/gotify-vs-ntfy-vs-apprise-vs-pushover)). Tokens and webhook addresses
  are stored encrypted, like the chat key.

A reminder that came due while the cabinet was closed is mentioned when it next starts.
