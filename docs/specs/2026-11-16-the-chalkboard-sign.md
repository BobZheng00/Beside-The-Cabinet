# The chalkboard sign: missions in sight, then what earns play time

*Decided with the user in one design session, 2026-11-16 (#273), after they tried the missions (#344). Nothing is built
yet. It changes where the missions show in `2026-11-08-a-new-player-gets-it.md` ("Where they show"). The order is at
the end.*

## Why

The user, 2026-11-16, trying the first run with missions: "mission is kind of hard to find. it works but user may not
notice it at all and curious about where does the credit come from when they do something. i would say we need to
brainstorm a more obvious way for user to understand the system (maybe have some Frame Chalkboard Sign like the one for
cafe?)". And of Hide missions: "did not find where i can hide and show missions (is mission on cabinet? what do we mean
by hide?)".

Today the missions are a list at the top of Settings and Missions… in the tray, with Hide and Show buttons. The plan was
a MISSIONS page among the screen's rotating pages, which is easy to miss while the screen shows the time. What earns play
time, and how much, is written only on Settings → Play time and on the first run's page that asks it.

## What it does

### The sign

- A small A-frame chalkboard stands on the desk beside the cabinet, on its other side from the companion's stool, so
  neither covers the other. It's a window of its own, like a memo sheet, and never takes the keyboard. It moves with
  the cabinet, and the player can slide it along the bottom, nearer to the cabinet or further off. With no room on its
  side (the cabinet against the screen's edge), it stands on the stool's side, beyond the stool.
- It's drawn in the cabinet's style (`tools/art`), with the words in chalk in the app's language, and reads at every
  cabinet size.
- It stands up after the first run. A profile set up before Preview 3 gets it on its first start on this version, as a
  price list (below).

### Missions on it

- Each mission not yet done is chalked on it with the minutes it pays. Under FREE PLAY, no minutes and no EARN A CREDIT.
  HEAR THEIR VOICE, an extra where the voice takes a download, is chalked as EXTRA, and the list is done without it.
- As one pays, the screen flashes its name and what it paid ("SAY HELLO +15 MIN"; under FREE PLAY only that it
  cleared), and the sign gets a chalk tick with the minutes beside it. Several paying at once (switching to CREDITS)
  flash one after another. Earnings outside the missions keep today's flashes.
- Clicking a mission opens where to do it, as the Settings list's buttons do (the AI page, Memory and so on); SAY HELLO
  opens the chat slip, and EARN A CREDIT opens Settings → Play time.
- The list at the top of Settings stays until they're done, with how to do each.

### Then, the price list

- With CREDITS, once the last mission is ticked, the board is wiped and shows what earns play time and how much, like a
  café's board: only what's on (focus, activities, LeetCode), at the rates the app pays, and today's limit. It follows
  Settings → Play time as it changes. The rates are read from the rules in Core, by the sign and by Settings → Play
  time alike, so they're written once.
- Clicking a line opens Settings → Play time.
- Under FREE PLAY there's no price list: once the missions are done the sign folds away. Switching to CREDITS stands it
  up with what's left there (EARN A CREDIT, which FREE PLAY can't tick, comes back if it isn't done), then the price
  list once that's ticked, or straight as the price list if nothing is left. Switching to FREE PLAY shows the missions
  left there without minutes, or folds the sign if none are.

### Putting it away

- Right-click → put away, or lift it off the desk (drag it up, where sliding it along the bottom moves it), as a memo
  is pulled off the cabinet. The tray stands it up again (Missions… while there are missions, then an entry named for
  the price list). With the sign already up, Missions… opens the full list in Settings, as today.
- Putting it away wins. The app folds the sign and stands it up (the missions done, a mode switched) only while the
  player hasn't put it away. Put away, it stays away, whatever it would show, until the tray brings it back. That's
  kept in the profile in place of today's hidden missions, and a profile that had hidden them starts with the sign put
  away.
- That replaces the Hide missions and Show missions buttons, which go. Put away, the missions leave the companion's
  mentions too.

## Decided

- **A chalkboard sign by the cabinet** (the user, 2026-11-16), over the screen's rotating pages as planned, and over
  both.
- **A payout is a flash on the screen and a chalk tick** (the user, 2026-11-16), over a line from the companion.
- **Once the missions are done it becomes the price list** (the user, 2026-11-16), over folding away or staying as it
  is.
- **Put away by hand, brought back from the tray** (the user, 2026-11-16), over no hiding and over keeping Hide and
  Show.
- **Opposite the stool, movable** (the user, 2026-11-16), over a fixed place and over leaning on the cabinet's side.
- **Under FREE PLAY, the missions without minutes, then it folds away** (the user, 2026-11-16).
- **Profiles from before Preview 3 get it as a price list** (the user, 2026-11-16).
- **Clicking a line opens how to do it** (the user, 2026-11-16), over a sign that's only to look at and over the
  companion explaining.

## Not built, and why

- **The MISSIONS page among the screen's rotating pages:** the sign takes its place.
- **A line from the companion at each payout:** the flash and the tick say it without talking over them. They still
  point the sign out once after the hello (#273).
- **New missions or other amounts:** not this session's question.

## Open

- The board's size at each cabinet size, and how many lines it holds before a price list is cut short: settled with
  the art in hand.
- How memo sheets and Settings, which open beside the cabinet, sit with the sign there: settled with the build, so
  neither opens over it.

## Order

1. **The sign with the missions** (#361): the board, the payout's flash and tick, clicking a mission, putting it away
   and back, FREE PLAY; Hide and Show missions go.
2. **The price list** (#362): after the missions, and for profiles from before Preview 3.
3. **The companion points the sign out once, and three suggestions over the empty slip** (#273).
