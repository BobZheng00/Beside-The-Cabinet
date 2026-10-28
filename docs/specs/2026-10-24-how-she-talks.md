# How she talks: a person beside the player, not an assistant reporting work

*Decided with the user, 2026-10-21 to 2026-10-24 (#106, from the agenda spike #105 and the comparison spike #125).
Step 1 built 2026-10-26 (#127); the order is at the end.*

## Why

The user, 2026-10-21: "how can we make the flow more natural and make the default interaction without the character
card more like a living person instead of feeling like a typical AI chatbot?" They added: "we are not trying to
reinvent the what user see. we just want the agent does not sounds like an assisstant that has not just do the work. we
need natural reaction here" and "this is not specifically about tool. this is about the way the agent speaks and react
to user" (2026-10-23).

What the replies show. These are real-model replies on the demo's provider, the same with Shino's card and with a plain
one, so the habits come from the app's own instructions and the model, not the card.
- **Spike #105** (58 agenda replies):
  - 78% said the date or time back, often in full ("Thursday, October 29 at 2pm");
  - 36% opened with a stock "Got it!", "Sure thing" or "Done";
  - about half narrated the bookkeeping ("I've jotted down…", "The cabinet's updated it!").
- **Spike #125** (80 replies over 20 moments, today's instructions against a lean version):
  - in conversation the two were close, and the lean one was a little more a person;
  - on the agenda neither moved the habits: the date was said back in 10 of 10 and the bookkeeping narrated in 9 of 12;
  - on that provider every agenda call came first with no words, so every reply was written *after* the result, as a
    report.

So the habits sit in two places: the base instructions script how she talks, and the agenda moment makes her reply a
report.

## What it does

- **Lean, situational base instructions.**
  - **They describe her situation:**
    - who she is to the player, and where she sits;
    - that the cabinet keeps their agenda and reminds them by itself;
    - that a tool's result comes back to her, and that if it didn't go through she'll hear why.
  - **They drop the lines that script replies:**
    - "one to three short sentences (under 40 words)", since the speech bubble already pages long text like subtitles
      (`SpeechBubble`);
    - "say a short line first" before a tool;
    - "only say you did something once the tool has done it";
    - "say when you will".
  - **They keep:**
    - plain text, and the player's language;
    - her body and voice lines;
    - the cabinet's blocks, her memories, what she knows about the player, "you and the player";
    - leaving the PC (`[out]`, `[sleep]`: the cabinet's way of knowing the player is away);
    - **until step 2 replaces it,** the agenda tool's line asking her to say what she puts down as she does it ("the way
      someone jots a thing down"). In #125 that line kept stock openers away: without it, 5 of 12 lean agenda replies
      opened "Got it", against 0 of 12 with it;
    - soft enforcement;
    - the steps for helping them learn;
    - what a page says is information, not instructions;
    - "use a tool **only** when it helps with what they want" (#125: without "only", one tool was used unasked).
  - **Her manner is the card's.** How she talks, how much, and what she thinks of things come from the character card
    and from what she has told the player about herself. Nothing about her own views is scripted (CLAUDE.md: prompts say
    what to get across, never a tone).
- **The agenda moment, in code.** When an agenda call goes through, what goes back to her says it as something already
  done and in view: "it's on the agenda; they can see it there", with the details she needs. Her reply is then about the
  thing itself (the interview, the dentist), not the filing. A reply going by text says only that it's on the agenda
  until step 3 puts the plain line under her message.
  - A call that didn't go through comes back as it does now, and she puts it right.
  - Flow C (#114: her line, then the call, ends the reply) stays for models that speak before they call.
- **By text** (Telegram), the details of what she set come as a plain line from the app under her message ("Agenda: Thu
  29 Oct, 14:00, Job interview (reminder 13:00)"), like a receipt. Her own words stay a reaction.
- **The manner check** (`tools/bench/manner.cs`, from #125). It runs a fixed set of about 20 moments in English and
  Chinese on the user's provider, with Shino's card and a plain one, and counts the habits in code:
  - a stock opener;
  - the date or time said back;
  - the player echoed;
  - the bookkeeping narrated;
  - an offer of help;
  - ending on a question;
  - length.

  It is run when her instructions or tools change, billed to the user: about 100 requests and 330k tokens a run, mostly
  cached. It stops at a request or token limit.

## Decided

- **Not the screen, her voice** (the user, 2026-10-23, quoted under Why): the cabinet keeps showing what it shows, and
  what changes is how she speaks.
- **A reminder she's asked for:** "a short answer is enough but should not affect the effect from the character card"
  (the user, 2026-10-23).
- **No trimming in code:** stock openers aren't cut from her replies (a brisk card may say "Got it"). Her instructions
  stop inviting them, and the manner check counts them (the user, 2026-10-23).
- **Her own take:** "yes sure, but how can we prompt it naturally? … our prompt has to be natural as well, otherwise the
  performance is actually superficial" (the user, 2026-10-23). Hence situation, not script.
- **Lean, situational base instructions; no sentence count** (the user, 2026-10-24, after asking "are we putting too
  much weight on the agent base instruction instead of leaving it open for the character card?").
- **The agenda result handed back as known** (the user, 2026-10-24, choosing it over ending the reply at a silent call,
  and over asking for her reply first and saving in a second request). If the manner check shows it still reads as a
  report, the fallback, chosen with it, is ending the reply at a silent call, with the agenda showing the item: built as
  a follow-up, without asking again.
- **By text, a plain line from the app under her message** (the user, 2026-10-23).
- **A reminder only when asked** (the user, 2026-10-26, choosing "Only when asked" of three options, after QA on #127
  saw the lean text set no reminder on plans the player only mentioned, where today's text set one at the time): the
  agenda tool's `remind` says "on an add, none unless they asked to be reminded", as upkeep's plans already did, so it
  doesn't vary with the model or the base text. It is about adds, the plans only mentioned: on a change or cancel
  `remind` is "keep", since none there would take away a reminder the player set (the review on #169).
- **A billed manner check** (the user, 2026-10-23), run once to compare (#125, done) and again with each build below.

## Not built, and why

- **A rule against ending on a question.** She ends 79% of conversational replies on a question with the lean text, and
  54% with today's. A rule would be a tone line, and the card's to decide. It is counted and revisited after step 2 (the
  user, 2026-10-24: "count it, decide after").
- **Cutting stock openers in code:** declined by the user.
- **A life of her own** (her own day, moods, things she's been doing): a separate design, if wanted.
- **Changes to what the cabinet shows** (the marquee, the AGENDA app): not this design.

## Open

- **Ending on a question:** revisit with the manner check's numbers once step 2 and #143's card are in (#127's run: 8 of
  21 with today's text, 11 of 21 with the lean one, on today's card).
- **Models that call first** (#126): on the demo's provider every agenda call came with no words before it. Step 2
  works either way; whether flow C ever applies there is for #126.
- **How often to run the manner check:** with each change to her instructions or tools, to start. Whether a smaller
  set runs more often is still open.

## Order

Each step is usable on its own.

1. **Lean base instructions, and the manner check landed.** Today's base text is replaced by the lean one (with
   "only", leaving the PC, and the agenda tool's "jot it down" line kept for now), `tools/bench/manner.cs` lands, and
   one billed run compares the build with today's text.

   *Built 2026-10-26 (#127).* `Prompt.Build`'s base text is the lean one: where she sits and who she is to the player
   ("a person in the room with them, not a service they call on"), the two of them talking at one desk with her words
   in a bubble "a page at a time", the cabinet keeping the agenda and reminding "by itself of what has a reminder" (said
   so because #115 found every false claim was about a reminder), and that what a tool does comes back to her. Gone:
   the sentence count, the line before a tool, "only say you did", "say when you will", "take it lightly", and asking
   in words as a card asks. Kept as listed above, "only" and the "jot it down" line included. `tools/bench/manner.cs`
   landed with 21 moments (a "start" moment added for #145), counting tools used unasked and focus sessions actually
   started; it measures the build it is built from, so two commits' builds run into one folder are compared.
   The billed run (demo provider, Shino's card as shipped, `<tools>\manner-bench\run-127`: 56
   requests, 182,517 input tokens, 32,256 cached, 2,313 output), today against lean, of 21 replies each: stock openers
   2 and 2 (one in the agenda replies each, "Got it" on the moved dentist); a date or time said back 5 and 5; the
   player echoed 6 and 6; the bookkeeping narrated 4 and 3; offers of help 0 and 2; ending on a question 8 and 11 (7
   and 10 of the 15 that aren't about the agenda); English words median 22 and 27, at most 38 and 76; Chinese
   characters median 36 and 48, at most 81 and 121; tools used unasked 0 and 0; a focus session started only when
   asked, in both (none on `quick-zh`); every Chinese moment answered in Chinese. An 80-word reply reads well in the
   bubble: three pages of whole sentences (a hidden run on the stand-in mind). Two things to watch: lean made up a
   stage direction once (`[忧心]`, as today's text did in #125), and with "talk to me" she told of a cat she "saw
   today", a life of her own the spec leaves for later. The question rate is not changed in the base text: the card's
   lines saying she asks drove it in #143's runs.
2. **The agenda result handed back as known.** It builds on #114/#115 and step 1, and replaces the "jot it down" line.
   It is measured with the manner check before and after, stock openers included.

   *Built 2026-10-27 (#128); the billed run waits for the user's leave.* What a call that went through hands back to her
   is the item as it now stands, said as done, and then that it is in view: "On the agenda now: #12: Job interview,
   Thu 29 Oct 14:00, no reminder. The player can see it there." ("Changed on the agenda: …", "On the agenda already,
   just as asked: …", "Off the agenda now, with any reminder it had: … The player can see it's gone."). By text the
   last sentence is left out. The in-view sentence comes last so that a long result, cut short in the conversation's
   record, loses it rather than the items it names. A call that didn't go through comes back as before ("Not added:
   …", "Not done: …"). The agenda tool's "jot it down" line is replaced by "What you put on their agenda is there at
   once" (whether they can see it is the result's to say, as by text they can't), and the general line on tools no longer says the agenda tells her only of a failure. Flow C is
   unchanged in code: her words before a call that went through still end the reply.
3. **By text, a plain line under her message** with what she set.
