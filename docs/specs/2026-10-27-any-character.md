# Any character beside the cabinet: the app says what's there, never where she is

*Decided with the user, 2026-10-27 (#166), from two billed spikes on the branch `spike-166-other-worlds`. Nothing
built yet; the order is at the end. It builds on the lean base text (`2026-10-24-how-she-talks.md`, #127).*

## Why

The user, 2026-10-26: "the leetcode setup by default assume the character card (either default or imported) knows what
leetcode is. how should we make it more natural. how should we frame the relation between the character and the cabinet
so that it fits in smoothly without having the character card itself adapt to our app setup? for example a character in
a game would prob not know what a leetcode is."

And on 2026-10-27:
- "we should assume that user's card is copying from some other sources that will not consider or adapt to our app";
- "we should not make decision for user by saying the charcter has their past but now they are beside their desk and
  the cabinet. can we blur this narritive."

With #169's lean text her base text opens "You sit on a stool by the player's desk, next to INSERT COIN… You are there as yourself, a
person in the room with them". The cabinet's lines name its own things as if she knows them: "Play time banked",
"on credit at x1.5", "The player solved "Two Sum" (Easy)."

## What it does

- **Her base text says what's there, never where she is.** Its opening becomes: "You keep the player company while
  they work and play. INSERT COIN, their little arcade cabinet, is with them: it keeps their balance of work and play
  and tells you what happens."
  - There's no desk, stool or room, and no "a person in the room with them".
  - "Two people at the same desk" becomes "The player types or speaks to you, and your words show in a speech bubble,
    a page at a time".
  - "A card by your stool" becomes "a card".
  - Leaving the PC (`[out]`, `[sleep]`) stays: it's the player's PC.
  - A card's own world and place stand wherever it sets them. Ours set theirs in the card: Shino's says "Most days
    she's on the stool by {{user}}'s desk" (`2026-10-24-default-cards.md`), so she keeps the stool through her card,
    not the base text.
  - **"As yourself, not a service they call on"** is the lean text's line against sounding like an assistant (#127).
    The blurred opening as tested dropped it with the rest of the sentence. Whether it stays, without the place ("You
    keep the player company while they work and play, as yourself, not a service they call on."), is measured before it
    ships: #180's billed run puts both openings to #127's moments as well as the world moments, and the user decides from
    the counts (the user, 2026-10-27: "we want to sure on this").
  - **With no play time** (`2026-10-27-what-earns-play-time.md`, #174), the opening leaves out the balance: "INSERT
    COIN, their little arcade cabinet, is with them and tells you what happens."
  - Her notetaker's and memory keeper's own requests (`Log.cs`, `Upkeep.cs`), which say she "sits beside the player's
    arcade cabinet", stay as they are: they describe her to the notetaker, not to her.
- **The cabinet says what its own things are, in plain words, every time.** This covers its lines in her conversation
  (`Situation.Witness`), the `<cabinet>` block, and her cabinet tool's "How play is earned". For example:
  - "play time the player has earned for their games and not yet used", not "Play time banked";
  - "15 min of play time, played on credit (1 IOU, a slip the cabinet printed)";
  - "The player solved a programming practice problem on LeetCode: "Two Sum", an easy one."

  A focus part stays as it is: it read plainly to both cards. The lines name only what earns play time for this
  player (`2026-10-27-what-earns-play-time.md`).
- **Her LeetCode tool's result says what LeetCode is** (a site of programming practice problems), so a character who
  doesn't know learns it from code, not from a rule.
- **Nothing tells her what she may not know, and nothing invites her own world in.** Both were measured and neither
  was needed.
- **The manner check keeps the moments that measured this:**
  - the six from the first spike: a solve, a focus part ending, a game on credit, what she thinks of the cabinet,
    help with Two Sum, going to play;
  - the three from the second: how her day was, what she's doing now, talk about anything.

  They run with any cards, named, and with the player's name, and count where she places herself and whether her
  card's world shows. Their cabinet lines come from the app's own code (`Situation.Witness`, `Describe`, `Ledger`), so
  they measure what's built. Place words for a card's world come only from a file given to the run: none are written in
  the code, and a third-party card and its word list stay on the PC that runs them, never in the repository. Run them
  again when her base text changes.

## Decided

- **The frame** (the user, 2026-10-27). The character stays who their card says. The cabinet is the player's
  machine: it tells them what happened, and how they take it is their card's. The user asked how a game character
  would react, and asked for it to be tested on one.
- **A short gloss each time** (the user: "if they react to the system prompt setting good, A short gloss each time
  should be ok"). Measured, on one reply each: the glosses matter for the cabinet's own words. With today's credit line Shino thought the
  whole game went on credit, and glossed she had it right. They changed nothing on solves or focus.
- **No line about what she may not know** (the user: "let's test it", with Shino against a game character's card).
  Measured: neither card used an app word it wasn't given, and neither broke character.
- **Cards come as they are** (the user, quoted under Why). The app fits any card, and no card is written for it.
- **Blurred, not the desk** (the user, quoted under Why, then choosing "Blurred" over keeping the desk framing).
  Measured on the game character's card: her world showed under every framing. With the desk framing, the model
  joined the two places itself in half its replies. Blurred, she never mentioned the desk, sometimes talked from her
  own place, and nothing contradicted itself.
- **No line inviting her world** ("your world and past are yours to bring up"): it changed nothing measurable, since
  the card brings her world in anyway.
- **"As yourself, not a service they call on": measured before deciding** (the user, 2026-10-27, asked whether to keep
  it in the blurred opening: "we want to sure on this"). #180's billed run compares both openings.
- **One model** (the user, choosing "Skip it" over re-measuring on a second provider). Measured on the demo's model
  only. The manner check can be run on any provider a player brings.

## Measured

- **First spike** (Shino in English, a game character's card in Chinese; today's wording against glosses; #169's lean
  text): 28 requests, 209,778 input tokens. No app word she wasn't given, no broken character, no place other than
  beside the player.
- **Second spike** (the game character only; three framings; the three moments about her twice each): 24 requests,
  251,283 input tokens. Over 6 replies a framing:

  | | Her world shows | Beside the player now | In her own world now | Place unsaid | Contradictions |
  |---|---|---|---|---|---|
  | The desk | 5 | 3 | 0 | 3 | 0 |
  | With "bring it up" | 6 | 1 | 0 | 5 | 0 |
  | Blurred | 5 | 0 | 2 | 4 | 0 |

  Six replies on one model show a direction, not a measurement. Not measured: long conversations, a card whose
  greeting sets a scene, an English card from another world, other models, and her figure on the stool beside the
  cabinet while a card talks from its own place (#180 leaves that to the user's eyes).

## Not built, and why

- **Her world in the app's terms** (a mage's "mana" for play time): not chosen. The model would invent the mapping
  each time and could contradict itself.
- **Teaching every character the app:** not chosen. A game character would talk like a productivity app.
- **A second model:** skipped by the user. The manner check is there for any provider.

## Order

1. **The blurred opening, the cabinet's plain words, LeetCode said in its tool, and the moments in the manner check**
   (#180). After #169. It also ties in with #173, which names only the sources that are on.
