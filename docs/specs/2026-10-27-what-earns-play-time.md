# What earns your play time, chosen on the first run, or no play time at all

*Decided with the user, 2026-10-27 (#165). Nothing built yet; the order is at the end. Before Preview 2 (#157), at
the user's word.*

## Why

The user, 2026-10-26: "currently the character will by default react to the leetcode completion. we would like to have
a mroe complete on boarding session for user to select what contribute to their game time, or they do not want game
time system at all."

Today:
- **Three ways to earn** (`2026-10-07-balance.md`):
  - LeetCode solves, once the player has given their LeetCode name (a Getting started step since 2026-10-12);
  - focus, by the minute, always on;
  - the player's own activities, logged by her and sometimes proved with a photo, always on.
- **One way to spend:** Steam games (the registry's `Running` flag).
- **Around them:** the daily cap, the nightly fade, the bedtime price, credit with IOU tickets, and her nudges after
  a long game.
- **The first run** (`App/Welcome.cs`) asks for a name, who keeps the player company and her mind. Nothing chooses
  how play is earned, and nothing turns it off.

## What it does

- **One page on the first run: "What earns your play time?"** It comes after "Who keeps you company?", with three
  ticks and a choice to have none:
  - **Focus sessions**, ticked;
  - **Your own activities** ("the gym, chores, whatever you count"), ticked;
  - **LeetCode problems**, not ticked. Ticking it asks for the LeetCode name there and checks it, as Balance's
    Change… does today (`Onboarding`).
  - **"No play time, just keep me company"**, which clears the ticks above.

  Settings → Balance has the same choices, to change later. Getting started's LeetCode step goes, since the first run
  has asked.
- **A source that's off pays nothing and is never brought up.**
  - LeetCode off: the cabinet doesn't read the profile, and the screen never shows NO LEETCODE NAME.
  - Activities off: her `log_activity` and `judge_proof` tools aren't offered, and the DONE app isn't on the CRT.
  - Focus off: focus sessions still run, and they pay nothing.
  - Her base text's earning line, and the cabinet's "how play is earned", name only the sources that are on, in the
    plain words #166 settles.
- **No play time.** The cabinet keeps her company role and drops the balance:
  - **Stays:** chat, her memory, the agenda and reminders, texting, focus sessions (paying nothing), the bedtime
    words, and a break after a long game.
  - **Goes:** credits and coins, the cap and the fade, IOU tickets, the bedtime price, and her talk of earning. The
    CRT keeps its clock and its apps. Its attract screen stops saying "INSERT COIN / FOCUS OR SOLVE", while the
    marquee keeps the cabinet's name.
- **Programs you choose count as play.** On Settings → Balance there's a list of programs that count as play, as well
  as Steam games: a video player, a game launcher, whatever the player picks.
  - A program is picked from those running, or by its file.
  - While a picked program is the window in front, it spends play time as a Steam game does: the clock runs, she
    knows they're playing it, and the break after a long session applies.
  - Only the program is known, by its name from the file. Nothing is read from browser tabs or window titles for
    this, so a site is never watched.
  - No program is picked to start with.
- **Her reaction to a solve stays.** When LeetCode is on she notices each solve and says something in her own words,
  and the screen flashes the play time it earned. #166's plain words make that fit any character.
- **The word is "play time".** It's what the cabinet and her instructions already say ("Play time banked"), and it
  covers more than games. The new page and the docs keep to it.

## Decided

- **One page with ticks and defaults** (the user, 2026-10-27, choosing it over a page per source and over nothing on):
  focus and activities on, LeetCode off until ticked with a name, plus "No play time", and Balance to change it.
- **Its name** (the user, 2026-10-27): "instead of game time, can we name it as entertainment or something else since
  people may not always game, but have other entertainment similar to gaming"; then, choosing among names, "Play time".
- **With no play time, the companion, the agenda, focus and rest stay** (the user, 2026-10-27, choosing it over
  keeping only the companion and the agenda, and over tracking play without earning).
- **Her reaction to each solve stays as it is** (the user, 2026-10-27, over the first of the day only, and over none on
  her own).
- **Programs the player picks count as play** (the user, 2026-10-27, choosing it over renaming only, and over a
  "taking a break" the player says themselves). It's only programs, never browser tabs, so nothing private is read.

## Not built, and why

- **Browser sites as play** (YouTube, a streaming site): they'd need the tab titles read, which is a privacy choice of
  its own. Parked until a player asks.
- **A per-source amount on the first run** (how much a Hard problem or an hour of focus pays): Balance keeps the
  numbers, and the first run stays short.

## Open

- **The DONE app with activities off:** hidden, as above, or kept to show what was done that day without paying.
  The implementer looks at what the CRT's home screen holds, and the pull request says which.

## Order

1. **The first-run page and Balance's ticks** (#173). Sources on and off, her base text and the cabinet's ledger
   naming only those on, Getting started's LeetCode step gone.
2. **No play time** (#174). The balance hidden and her earning talk gone, with the rest kept.
3. **Programs that count as play** (#175). Picked on Balance, spent while in front.
