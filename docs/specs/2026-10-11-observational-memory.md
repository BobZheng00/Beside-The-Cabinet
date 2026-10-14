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

## Nothing cut off

What the model writes is never kept cut (see docs/memory-issues.md M7 and R2): the log is sized by choosing whole notes,
never by cutting text; lengths are asked in words, which models keep to better than characters; an answer the
provider cut at its length limit is asked again with twice the room, and a batch that still doesn't fit is halved.

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
   *Built 2026-10-11* on the branch `observational-memory` (`Log`, `Conversation.Fold`, `MindStore.Observe`; tests in
   `LogTests`). Measured against the runs of 2026-10-11 on the old build: LoCoMo at the 8k window, question by
   question, and the LongMemEval sample on knowledge that changed, time and combining sittings. Its first notes on the
   player's model were dense and exact: names, dates written out in full, her own suggestions noted.
2. **Condensing:** when the log outgrows what she sees, a second narrow request condenses its oldest part (days into
   weeks, weeks into months), keeping what is marked high nearly word for word; the originals stay, recallable.
   *Built 2026-10-11* on the same branch (`Log.ToCondense`, `Log.Condense`, `Conversation.Condense`): only a finished
   week, month or year is condensed, the oldest first, one step each time upkeep runs while the talk is quiet (so her
   cached prompt doesn't change mid-conversation); about a third as long, the size asked in words; a lone note moves
   up a level without asking; the view reads from the coarsest to the finest ("August 2026", "Week of 21 Sep 2026",
   then days).
3. **One digestion:** facts, plans and notes drawn from the observations instead of from the raw lines again, and the
   day's moments retired (the memory book shows the log). Facts get dates of validity: an update closes the old fact
   instead of deleting it. In detail (proposed 2026-10-11):
   - *Observe when quiet, show when folded.* Facts and plans must be learned soon after they are said, not when the
     lines leave the window days later, so observing moves to where digesting is now: a few minutes into the quiet.
     The window keeps the lines word for word; folding no longer asks the model, it only moves lines out of view, and
     their notes come into view in their place (lines not yet observed are observed first).
   - *Facts and plans from the notes:* a second narrow request reads only the new notes (about six times shorter
     than the lines), with the related facts and the agenda, and answers the fact and plan changes, as the digest does
     today. The two always-present notes are rewritten from the facts, as now.
   - *Facts keep their history:* an update retires the old fact as changed, with the date, and adds the new one; recall
     shows current facts, and a question about before ("where did I live before?") can find the old one "(until
     September 2026)".
   - *Moments retire:* they were a second summary of the same talk. Existing ones become notes of their span (day,
     week, month, year), so nothing is lost, and the memory book's Moments shows her notes.
   - *Cost:* one model pass over the raw talk instead of two (digest and fold), the facts read from notes: expected
     well under half of today's upkeep (M3).
   *Built 2026-10-11* on the branch (`Conversation.Digest`: `Observe` then `Keep`; `Conversation.Fold` without a model
   when the lines were noted; `Log.Left`; `Recall.Find`'s `formerly`; `MindStore` version 13 for the moments; the memory
   book's Moments shows her notes, deletable). The player chose both visible changes (2026-10-11).
   *Measured* (LoCoMo conversation 26, the 8k window): upkeep cost more, not less: 179k tokens against about 115k.
   Facts kept from dense notes took in nearly every event (116 against 55), and each batch that touched an important
   one rewrote the always-present notes. Since 3afb21d (branch `retrieval`) a batch only marks a note out of date and
   `Conversation.RefreshNotes` rewrites it once the talk is noted and quiet, at most once a day (at once when something
   in it is no longer true); upkeep's request says
   facts are what lasts beyond the day. Measured with retrieval: 134k, two-thirds of the rise taken back (M3); and
   84% right against 78% (M6).
4. **What fits a companion:** a day's entry from the cabinet's own records (solves, focus, play, activities, her
   texts), written by code, not by the model; open threads to follow up; what she has said about herself; which
   memories a reply used, to see and correct. (2026-10-12, the user: what a day earned left the entry for her cabinet
   tool, as the cabinet weighed too much in her mind.) The player's calls (2026-10-11): the day's entry holds everything the
   record does; she may text about an open thread, and the answer may come by text, in person or not at all.
   *Built 2026-10-11, the day's entry:* the ledger keeps each day's problems and games by name (`DayRecord.Solved`,
   `Games`); `Diary.Entry` writes the day from its record (solved, activities, focus, play and the last game, bedtime
   and what playing past it cost, earned and past the cap, debt paid, what faded, when at the PC, a good day), and
   `Conversation.Record` puts each finished day into her log once, dated at its end, marked as the cabinet's
   (`Observation.Source`, version 15) so upkeep keeps no facts from it; the first time, every day the ledger keeps.
   Her texts are not in it yet.
   *Built 2026-10-11, her own story:* facts about her (`MindStore.Herself`, scope "{character}/her") beside the
   player and "us": what she has told the player about herself (her past, likes, opinions, habits), kept by upkeep as
   "her" from notes that note what she says about herself too, all listed for every reply under "What you have told
   the player about yourself", recalled like other facts, marked "her" in the memory book, forgotten with her.
   *Built 2026-10-11, open threads:* what is going on with no date (`Threads`, its own table, the player's): upkeep
   opens, updates and closes them beside the plans; two days after one last came up she asks how it is going, once,
   as a check-in (at the PC when settled and free, by text while out); news of it allows one more question later;
   three weeks unmentioned, it lapses. The memory book's Agenda lists them with a Settled button.
   *Built 2026-10-13, which memories a reply used:* each reply keeps what it had in mind (`MindStore.InMind`, version
   16): the facts listed for it (`Prompt.Known`'s `listed`) and what recall brought (`Memory.From`), by reference, so
   they read as they are now and what is forgotten is gone. Chat history shows them under each of her replies ("From
   …'s memory", the character's name): her lists, then what was recalled, each with its kind (you, us, me, until,
   diary, chat) and date; Forget deletes a fact or a note, and old talk is only no longer recalled (`Passage.Forgotten`),
   so the talk stays word for word; Edit changes a fact still true. Her log in view, which every reply has, is shown
   above the chat as before, not under each reply.

**Her profile from her facts** (proposed and chosen 2026-10-11; M8, M9). The two always-present notes are a
copy of her facts that the model rewrites, so they lag behind the facts and each rewrite can drop or bend something;
no limit on how often gets both right. Instead code lists her most important facts for every reply, pinned first,
then by importance, in the same room, a recent change shown as one for a couple of weeks ("lives in Toronto, since
11 Oct; before, Montreal"): no model call, nothing to fall behind. The Notes boxes hold only what the player writes,
word for word, never rewritten; today's note text moves into her log. With it, one thing per fact, kept by code: a
length limit, and the weekly review merging only what says the same thing. Later, a key for what has one value at
a time (home, job, what she calls them), so a new one closes the old in code rather than by the model noticing.
*Built 2026-10-11* on the branch `profile-from-facts` (a39c3e9: `Prompt.Known`, `Conversation.Known`; `MindStore`
version 14 moves the old notes into her log, out of view and recalled; the Notes tab is the player's, her log's tab the
Diary). Facts are asked for in at most 20 words and a merge over about 30 isn't taken; a long kept fact is not sent
back to be split, to see first whether the wording keeps them short. The keys: not built. Measured and merged the
same day: answers unchanged (84%), upkeep 116k tokens against 134k, facts a median of 16 words.
