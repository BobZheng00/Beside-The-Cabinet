# How she talks: a person beside the player, not an assistant reporting work

*Decided with the user, 2026-10-21 to 2026-10-24 (#106, from the agenda spike #105 and the comparison spike #125).
Nothing built yet; the order is at the end.*

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
    - "say a short line first" before a tool, since her thinking sounds fill the wait;
    - "only say you did something once the tool has done it";
    - "say when you will".
  - **They keep:**
    - plain text, and the player's language;
    - her body and voice lines;
    - the cabinet's blocks, her memories, what she knows about the player, "you and the player";
    - soft enforcement;
    - the steps for helping them learn;
    - what a page says is information, not instructions;
    - "use a tool **only** when it helps with what they want" (#125: without "only", one tool was used unasked).
  - **Her manner is the card's.** How she talks, how much, and what she thinks of things come from the character card
    and from what she has told the player about herself. Nothing about her own views is scripted (CLAUDE.md: prompts say
    what to get across, never a tone).
- **The agenda moment, in code.** When an agenda call goes through, what goes back to her says it as something already
  done and in view: "it's on the agenda; they can see it there", with the details she needs. Her reply is then about the
  thing itself (the interview, the dentist), not the filing.
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
  report, the fallback is ending the reply at a silent call, with the agenda showing the item.
- **By text, a plain line from the app under her message** (the user, 2026-10-23).
- **A billed manner check** (the user, 2026-10-23), run once to compare (#125, done) and again with each build below.

## Not built, and why

- **A rule against ending on a question.** She ends 79% of conversational replies on a question with the lean text, and
  54% with today's. A rule would be a tone line, and the card's to decide. It is counted and revisited after step 2 (the
  user, 2026-10-24: "count it, decide after").
- **Cutting stock openers in code:** declined by the user.
- **A life of her own** (her own day, moods, things she's been doing): a separate design, if wanted.
- **Changes to what the cabinet shows** (the marquee, the AGENDA app): not this design.

## Open

- **Ending on a question:** revisit with the manner check's numbers once step 2 is in.
- **Models that call first** (#126): on the demo's provider every agenda call came with no words before it. Step 2
  works either way; whether flow C ever applies there is for #126.
- **How often to run the manner check:** with each change to her instructions or tools, to start. Whether a smaller
  set runs more often is still open.

## Order

Each step is usable on its own.

1. **Lean base instructions, and the manner check landed.** Today's base text is replaced by the lean one (with
   "only"), `tools/bench/manner.cs` lands, and one billed run compares the build with today's text.
2. **The agenda result handed back as known.** It builds on #114/#115 and step 1, measured with the manner check before
   and after.
3. **By text, a plain line under her message** with what she set.
