# What earns your play time, chosen on the first run, or no play time at all

*Decided with the user, 2026-10-27 (#165). Step 1 built 2026-10-28 (#173); the order is at the end. Before Preview 2
(#157), at the user's word.*

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
  has asked, and so do the Welcome's words pointing to it.
  - **Ticking LeetCode on the first run** asks for the name in a field on the page, checked as Balance's Change… checks
    it (`LeetCodeProfile.TryParse`, `PlayerExistsAsync`). If LeetCode doesn't answer, the name is kept and checked
    later: the first run never waits on it.
  - **No source ticked is no play time**, whether chosen by "No play time" or by unticking all three, so play time can
    never be spent without a way to earn it.
  - **A profile that already exists** (Preview 1's players, who never see the first run again) keeps what it had:
    focus and activities on, and LeetCode on when a LeetCode name is set.
- **A source that's off pays nothing and is never brought up.**
  - LeetCode off: the cabinet doesn't read the profile, and the screen never shows NO LEETCODE NAME.
  - Activities off: her `log_activity` and `judge_proof` tools aren't offered, and the DONE app isn't on the CRT.
  - A good day, for the streak and its bonus, counts only what's on.
  - Focus off: focus sessions still run, and they pay nothing.
  - Her base text's earning line, and the cabinet's "how play is earned", name only the sources that are on, in the
    plain words #166 settles. With LeetCode off, so does everything else that names it: the attract screen's "FOCUS OR
    SOLVE", an IOU ticket's "solve to pay", the ledger's "The player's LeetCode account" and the streak's "a solve".
- **No play time.** The cabinet keeps her company role and drops the balance:
  - **Stays:** chat, her memory, the agenda and reminders, texting, focus sessions (paying nothing), the bedtime
    words, and a break after a long game.
  - **Goes:** credits and coins, the cap and the fade, the streak, IOU tickets, the bedtime price, and her talk of
    earning, in her base text, the cabinet block and her notes about rest (`Rest.Note`, `Rest.AskBedtime`,
    `Rest.Notice`). The CRT keeps its clock and the apps still on. Its attract screen stops saying "INSERT COIN / FOCUS
    OR SOLVE", while the marquee keeps the cabinet's name.
  - **Games are still noticed.** Steam games and the programs picked as play still make a session, with nothing spent,
    faded or shown. The break after a long game, the bedtime the bedtime words read, and her keeping quiet while a game
    is on all need it.
  - **Turned off with a debt,** the debt is frozen and out of sight (the IOU ticket in the slot, the tray's "min
    owed"), and back as it was when play time is turned on again.
- **Programs you choose count as play.** On Settings → Balance there's a list of programs that count as play, as well
  as Steam games: a video player, a game launcher, whatever the player picks.
  - A program is picked from those running, or by its file.
  - While a picked program is the window in front, it spends play time as a Steam game does: the clock runs, she
    knows they're playing it, and the break after a long session applies.
  - **It doesn't pause when the player is idle**, since a film needs no typing. It stops when the screen locks or the
    player has left the PC, as the cabinet already knows (`Presence`). Steam games keep their pause after 10 minutes
    without input (`Rules.AfkAfter`).
  - Only the program is known, by its name from the file. The running programs to pick from are listed by process,
    and nothing is read from browser tabs or window titles for this, so a site is never watched.
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
  keeping only the companion and the agenda, and over tracking play without earning). The option turned down was the
  cabinet counting and showing play; games are still noticed in the background, for the rest it keeps.
- **Her reaction to each solve stays as it is** (the user, 2026-10-27, over the first of the day only, and over none on
  her own).
- **Programs the player picks count as play** (the user, 2026-10-27, choosing it over renaming only, and over a
  "taking a break" the player says themselves). It's only programs, never browser tabs, so nothing private is read.
- **A picked program counts while it's in front, and stops when the player is away** (the user, 2026-10-27, choosing
  it over pausing when idle, as games do, and over a choice per program).
- **Existing profiles keep what they had, and no source means no play time** (the PM, 2026-10-27, from the review of
  the spec: both follow from the choices above, and neither changes anything a player chose).

## Not built, and why

- **Browser sites as play** (YouTube, a streaming site): they'd need the tab titles read, which is a privacy choice of
  its own. Parked until a player asks.
- **A per-source amount on the first run** (how much a Hard problem or an hour of focus pays): Balance keeps the
  numbers, and the first run stays short.

## Order

1. **The first-run page and Balance's ticks** (#173). Sources on and off, her base text and the cabinet's ledger
   naming only those on, Getting started's LeetCode step gone. *Built 2026-10-28:* the Welcome's third page and
   Balance share the ticks (`BalanceSettings.Sources`); saved as `FocusEarns`, `ActivitiesEarn` and `LeetCodeEarns`
   (unset for a profile from before, read as "a LeetCode name earns"), read everywhere as `AppSettings.Earning`, a
   Core `Earning` that the rules carry (`Rules.Earns`; `Earning.Any` false is no play time, for #174). The attract
   screen's call names what earns: FOCUS OR SOLVE, FOCUS TO EARN, SOLVE TO EARN, GET THINGS DONE, or nothing. The
   cabinet's CHECK with LeetCode off says ALL UP TO DATE. A name kept on the first run while LeetCode didn't answer is
   checked when the cabinet first reads it, and dropped if LeetCode has no such player. A test run's LeetCode is a
   stand-in. Until #174, "No play time" only stops earning: a game still spends.
2. **No play time** (#174). The balance hidden and her earning talk gone, with the rest kept.
3. **Programs that count as play** (#175). Picked on Balance, spent while in front.
