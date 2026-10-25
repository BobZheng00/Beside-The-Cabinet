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
  instead of as facts; she can also add one in the conversation (`[plan: …]`). (She also kept facts as she replied, `[note: …]`, until
  2026-10-11: she said she'd remember after everything she was told, and upkeep keeps what lasts.)

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
   *Second round, 2026-10-05:* a reminder some time before its item (`[plan: … | 1 hour before]`) and repeats
   (daily, weekdays, weekly, monthly; a repeating item moves on to its next time); the player's optional reminder
   before every timed item; Snooze and OK on the ticket, which can stay until closed; she passes reminders on in her
   own words (a cabinet note answered like a message, logged as the cabinet's), queued until she has finished
   answering, speaking or listening, with the plain line when her mind is not connected; a chime that can sound
   while the cabinet is muted (its own bus); the memory book in tabs. A reminder whose time had passed before it was
   added goes off only while its item is ahead, as does one from the default lead.
   *Third round, 2026-10-14 (#17):* she calls off or changes an item in the reply where she is asked, not at the next
   upkeep: the agenda in her turn numbers the items she may change, and `[cancel: #12]` or `[change: #12 | new time |
   options]` reach that same item. The code takes only a number from that turn's list, and never a calendar's item
   (listed without a number, "from their calendar"): the calendar decides those.
  *Fourth round, 2026-10-16 (#32):* a change the code turns down (no number, a number not in that turn's list, a
  calendar's item, a time not written as one even beside an option it reads, such as "tomorrow 3pm" or "daily at
  9:00", or an option it can't do yet, such as removing a reminder or stopping a repeat) changes nothing, not even the
  parts it could read, and is logged after her reply as a cabinet line saying why (the time format, or what a change
  can do), so her next turn tells the player and puts it right or asks. A change works on the item as it is when the
  tag is read (a reminder may have gone off mid-reply and moved a repeat on). An item called off is done with for that
  reply: a `[plan]` that reuses its number is never changed by it, and calling it off again isn't reported as failing.
  *Fifth round, 2026-10-18 (#52):* a change's options are split on ";" and full-width punctuation (，、；) as well as
  commas, and words left over once the reminder and repeat are read ("every Monday" after "1 hour before", "no
  reminder" after "weekly", Chinese or Japanese words) make it an option the cabinet can't do, so the whole change is
  turned down rather than half made, and what is applied is the options read together, never fewer than their pieces
  said. Leads are read whole: "half an hour before" is 30 minutes, "three quarters of an hour" 45, "an hour and a
  half" or "1 hour 30 min" 90, "1 day and 2 hours" 26 hours, never the last part inside them; what still can't be read
  whole ("1.5 hours", "half a day") isn't read as a lead at all.
  *Changed 2026-10-22 (#114, from spike #105): the agenda is a tool.* Where the provider takes tools, the tags above
  are no longer offered, in her instructions or her voice's example (they stay for a provider without tools,
  `Toolbox.Refused`; a `[plan]` or `[remind]` she writes anyway is applied at the end of a reply with no agenda call in
  it, or where such a reply is stopped part-way, and ignored in one with a call, where it could win on time or reminder or add a second item in other words; one
  the cabinet can't read is told to her after her reply, pointing her to the tool): `BuiltIn.Agenda`
  adds, changes or cancels, by the same numbers and with the same refusals. On a real model the tags set the wrong
  thing silently in 5 of 20 replies (both cancels missed, three items put at their reminder's time); the tool got
  every cancel, change and "10 min before" right, and its only false claims were about reminders, so the reminder is
  a required choice (`remind`: none, at_the_time, before with `minutes_before`, or keep on a change) and every result
  names it ("no reminder"; the player's own lead when that gives one); "reminds them by itself" is gone from what she
  is told. An add that lands on the same thing already on the agenda (same day, same words) sets its reminder as asked,
  none taking one away (a calendar's item keeps its own reminder and repeat, and the same thing at another time that
  day is left as it is); unless the item is now just as she asked, time, reminder and repeat (once when the add gives
  none), the result says what was there and what is there now, and goes back to her rather than ending the reply. A change can now also rename, remove a reminder and stop a repeat; a repeat outside the list ("every other
  week") or a time it can't read is turned down with its reason. *Said as written* (flow C), in code
  (`Conversation.Reply`, `Ends`): she says what she is putting down and calls the tool in the same reply; when every
  call of the round was an agenda call that went through and words came before them, the reply ends there (one
  request; `FunctionInvocationContext.Terminate`, set only on the round's last call, as an earlier one would skip the
  rest unmade). A failure, a call with no words before it, or a round with another tool goes back to her, so she
  speaks once. Upkeep's plans take the same fields (`remind`, `minutes_before`, a `repeat` from the list), and "when"
  is always the thing's own time. Her body: the note-taking gesture (pad and pencil) as the call goes out, not the
  working pose of other tools. `tools/bench/agenda.cs` compares it with the tags on a stand-in or a real provider.

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

*Built 2026-10-04* (tray → "Reminders…"; `Couriers`, `WindowsNotice`). The Windows notification is a balloon from
the cabinet's own tray icon (Windows 10 and 11 show it as a toast and keep it in the notification centre): Godot
registers its tray icon on the main window with the indicator's number as its id, so `Shell_NotifyIcon` can ask
that icon for a balloon; no app registration or installer needed. ntfy is sent as JSON (any language in the title
and text); Discord never pings anyone; every body is sent with its length (small webhook receivers cannot read a
chunked one). Checked against a local receiver; the Windows notification was seen working by the player on 2026-10-05
(a reminder at 01:30). Not yet tried for real: ntfy, Discord and Telegram on a phone.

### 11. Check-ins, usage and calendars (decided 2026-10-05, built)

- **Check-ins.** She asks how something went once it has been over 15 minutes, only for things the player told her
  about (not reminders to do something, repeats or calendar entries: a busy calendar would have her asking all
  day), once, and not if they have talked since it ended (her turn had it under "Just past"). Only when the player
  is back and free: settled at the PC (two minutes of use since they last left it for a minute or more; *since
  2026-10-09*: it counted from their last five-minute break, so after a shorter one she spoke as they sat back down)
  and active now; the once-a-day word on rest waits the same way; not in a call (another program has the microphone
  or camera, from Windows' own record of their use: a start with no stop counts only within 12 hours and while
  that program runs, as programs that end mid-call never record the stop); not when Windows holds notifications
  back (`SHQueryUserNotificationState`: locked, full screen, presenting, quiet time); not in quiet hours
  (22:00–08:00); not while she is busy. Gentle (default): one every three hours at most; Chatty; Off. A meeting
  that runs over keeps its call going, so she waits. During a call her reminders are bubble-only.
- **Usage.** Every request is counted from the provider's own report into a row a day per model and purpose
  (replies, upkeep): input in all, cache reads, cache writes (Claude reports them; the Anthropic library passes
  them as `CacheCreationInputTokens`, its input total including both), output and thinking. The cost needs the
  player's prices for the model (from their provider; a proxy charges its own): input not from the cache at the
  input price, cache reads and writes at theirs. No built-in price table: it would go stale.
- **Calendars.** A calendar is a source (`ICalendarSource`); its events go on the agenda through one sync
  (`CalendarSync`: added, changed, taken off; marked with the calendar's name; the player's reminder on one kept).
  The first source is a private iCalendar link (Google's secret address, Outlook's published ICS), read every
  15 minutes for two weeks ahead, by our own reader (zones by IANA or Windows name, whole days, repeats daily,
  weekly on days, monthly on a day or "second Tuesday", yearly, left-out dates, moved and cancelled occurrences).
  Signing in with Google (Calendar API; a sensitive scope: 7-day sign-ins while in Testing, a 100-user cap until
  verified) or Microsoft (Graph, `Calendars.Read`, no review for personal accounts) would be another source.
- **Dates in the player's zone.** Times she writes down take the offset of their own date, and repeats keep their
  time of day across a clock change (this PC is on US Eastern time; the tests found an hour's slip after 1 November).

### 12. The memory book saves as you go (decided 2026-10-14, built)

The book had Apply and "Save and close", and saving wrote both notes and every fact back whole, so an Apply could put
back what upkeep had changed meanwhile, and a book left open showed old lists (issue #18). The user chose to save as
you go, as Settings does: each field is saved when the player leaves it (Enter, or a click elsewhere, the book losing
focus included), a tick or a choice at once; every forget asks twice (the forget buttons, and a fact's, a diary
note's or an agenda item's ×: the first click says "Forget?", the second within a few seconds forgets; the retired
list's × stays one click); only Close is left.
*Built 2026-10-14* (`MemoryEdits`, tests):
- **Only what was edited is written**, onto the item as it is now (an agenda item's other parts as she left them),
  and only if it is still what the book showed when the player began. Otherwise she changed it meanwhile: the row
  shows hers and asks which to keep ("Keep yours", "Keep <name>'s"); a fact she retired or merged meanwhile says why,
  with "Keep yours" (brought back, in the player's words) and "Let it go"; an agenda item gone meanwhile asks once
  however many of its fields changed, and is put back once. Whatever keeps the player's (a button, closing, a pin or
  a choice) on an item that has gone since, even after a clash question was showing, puts it back or asks; it never
  says kept while writing nothing. Items are matched by when they were learned or added as
  well as their id (SQLite gives a forgotten item's id to the next one).
- **Closing never drops an edit.** A field typed in and not left is saved; a question left unanswered keeps the
  player's, and hers is kept too: a fact's as what it used to say (retired, "changed to: " the player's words), a
  note's or an agenda item's in the log (a note is the player's own and rarely changed by anyone else; a second agenda
  item would remind twice). A time that can't be read can't be saved: Close stops and says which, and a second Close
  within a few seconds closes without that one change; the app quitting drops it with a log line naming it.
- **It follows her memory in place** every two seconds: agenda, threads, facts (new ones on top while it is open),
  retired facts, diary, memos and the two notes, except a field the player has changed and not left.
- The line beside Close says what was saved last, and when, after how many changes wait for the player to choose.

### 13. The chat history leaves out what the app tells her (decided 2026-10-14, built)

Some cabinet lines are the app's instructions to her rather than what happened: the note of a reminder going off,
the bedtime ask, a check-in, a memo to think over (every turn the cabinet starts, `Conversation.Announce`), a refused
agenda change ("Your [cancel: #99] didn't go through: … Tell the player briefly"), the note that the player cut her
off. The player read them in the chat history as if part of the talk (issue #20). The user (2026-10-14): off by
default, with a toggle for players who know how AI agents work and want to look.
*Built 2026-10-17* (`Line.Instruction`, tests):
- **Marked where they are written**, not told apart by their words: a column on the line (store version 17). They
  reach her exactly as before; only the chat history reads the mark. What happened at the cabinet (a solve, a game
  started, her tools' records, texting begun) is not an instruction and still shows.
- **Chat history** has "Show what the app tells <name>", off by default and remembered (a mark in the store). Ticked,
  each such line shows in a box of its own, marked "App to <name>". Hidden lines never count as "New lines below";
  it reads on from the newest line shown, so a number a hidden line leaves free when taken back is the next line's,
  and a stretch of only hidden lines is read past (never "Nothing yet." above "Show earlier"). After the chat is
  cleared while it is open, it is read afresh (#73).
- Lines from before the mark existed can't be told apart and always show.

