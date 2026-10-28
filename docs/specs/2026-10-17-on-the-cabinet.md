# Settings and the switches, from the cabinet itself

*Decided with the user, 2026-10-17 (`/pm:brainstorm #64`). Step 1, the right-click menu, is built (#68); step 2, the
CRT, is built (2026-10-18, #69); the order is at the end.*

## Why

The user, 2026-10-17, using the app daily: "it is now become a bit annoying to open settings and membook from the
tray. should we incorporate it in the crt os?"

Today the tray's menu is the only way into Settings, the memory book and Chat history
(`2026-10-10-settings-and-setup.md`, "The tray"). The cabinet's screen already has a small system of its own, the CRT
OS (`CrtOs`), with apps on a home screen: FOCUS, DONE, MUSIC, MEMOS, AGENDA. A right-click on the cabinet or on her
does nothing yet, and right-clicking the character for its menu is a habit desktop-pet apps teach.

## What it does

- **A right-click on her or the cabinet** shows the same menu as the tray, at the pointer: Settings…, the memory book,
  Chat history, the switches, Quit and the rest. It is the tray's menu, built once, so the two never differ.
- **A SETTINGS app on the CRT** opens the Settings window beside the cabinet, as the tray's Settings… does; the
  screen settles back into the cabinet. It sits on the home screen with the other apps and can be moved like them.
- **Quick switches on the CRT's home screen:** a row of three along its top, Mute sounds, Voice and Conversation
  (keeps listening), each showing whether it is on. They are the same switches as the tray's and Settings', so a
  change in one shows in the others.
- **A Memory page in Settings,** in the companion's group: a line on what each holds, and "Open the memory book…" and
  "Open Chat history…".
- **The tray stays as it is.**

## Decided

- **Both the CRT and a right-click** (the user, 2026-10-17, choosing "Both CRT and right-click"): the CRT is there to
  be found; the right-click is quick and works without opening the CRT.
- **A CRT app opens the usual window** (the user, 2026-10-17, choosing "Open the window"): one Settings to keep,
  readable at any cabinet size. Settings is never drawn inside the CRT.
- **On the CRT: Settings and the quick switches; the memory book and Chat history through Settings** (the user,
  2026-10-17: "Settings, Quick switches, maybe make mem book and chat history accessable from setting as well?", then
  choosing "A 'Memory' page"). The right-click menu, being the tray's, has the memory book and Chat history directly.
- **The switches as a row on the CRT's home screen** (the user, 2026-10-17, choosing "A row on the CRT home"), and in
  the right-click menu as in the tray.
- **The right-click works on her and on the cabinet** (the user, 2026-10-17, choosing "On her and the cabinet").
- **The tray is unchanged** (the user, 2026-10-17, choosing "Keep it as it is"): it is where Windows users look, and the
  only way in while the cabinet is hidden.
- **A test run never shows the menu or takes focus:** new dev actions open it (logging its entries, as `trayitems`
  does for the tray's) and press an entry. The tray has no such actions yet: `trayitems` only logs, and
  `tools/dev/README.md`'s `tray:x:y` lost its handler some time ago.

## Not built, and why

- **Settings drawn on the CRT:** a second Settings to build and keep in step, cramped on a small screen.
- **The memory book and Chat history as CRT apps:** the user chose to reach them through Settings (and the right-click
  menu), keeping the CRT's home screen to what is used most.
- **Slimming the tray:** the user kept it.

## Built: the CRT (2026-10-18, #69)

- **SETTINGS** (`App/Crt/SettingsApp.cs`): a cog on a pale orchid tile, last on the home screen (the default order
  has it; a player's own order gets it at the end) and movable like the others. Its icon opens Settings as the tray's
  Settings… does; the screen never lifts for it and stays on the home screen (an app that `OpensWindow`).
- **The switches** (`CrtOs.Switch`): three round toggles on the home screen's bar, between the time and the power
  mark: a speaker (crossed out while muted), a speech bubble (Voice) and a microphone (Conversation), filled pink when
  on. Icons only, at every cabinet size: three labelled toggles don't fit the bar beside the time and the power mark,
  and at the smallest cabinet their labels would be too small to read. The name of the one under the pointer showed
  in place of the time until #109 put it on the marquee (below). Each reads its state where the tray's comes from (the settings, the conversation) every frame
  the bar is drawn, and flips it through the same calls as the tray, so a change anywhere shows everywhere.

## Built: names on the marquee (2026-10-22, #109)

The user, trying the switches: "i can tell the icon apart, but not the text, i would suggest we display the hovered
button text on the insert coin bar above the crt. maybe make it a pixel screen", then choosing a lit pixel display
("let's try a lit pixel display and see how it goes"): a trial, judged by eye.

- **The marquee is a lit pixel display** (`Cabinet/MarqueeDisplay.cs`, `marquee_dots.gdshader`): the art draws a dark
  panel in the sign (`tools/art/cozy_cabinet.py`, `DISPLAY`; the skin's `marquee` anchor is its corners), and the game
  lights a board of 64 by 15 round dots on it, in the marquee's perspective (round 3: 75 before; fewer, bigger dots).
  Letters are five dots by seven, as on a dot-matrix sign, set as a sign sets them: each as wide as its dots (I and 1
  take three columns), a dark column between letters, three for a space; INSERT COIN and MUTE SOUNDS fit, CONVERSATION
  scrolls. Dots too small to draw apart (below about three screen pixels: the small and medium cabinets) are drawn as
  square blocks that meet, so letters stay crisp pixel letters, with a dot's edge eased over one screen pixel at most.
- **INSERT COIN at rest; the name of the CRT's button under the pointer** (a quick switch, an app, SETTINGS) while
  there is one, and INSERT COIN again when it leaves. Only those buttons, not other hover tips (the PM's choice). A
  name too long for the board shows its start for a second, then scrolls through a whole column at a time (about 17 a
  second), round and round.
- **The time stays on the CRT's bar** whatever is under the pointer (until #154, below: on a lifted home screen a
  switch's name takes its place). The bulbs above (`MarqueeLights`) are unchanged.
- **It behaves as an LED sign does** (round 2, the user trying it: "the text appear and switch too fast, which does not
  really looks like pixel display (still more like some rgb display)"): one pink LED colour, hotter in each dot's
  middle, with unlit dots faintly there; each dot glows up over 0.06 s and fades over 0.22 s; a change fades the old
  text out (0.22 s), then draws the new one in a column at a time from the left (0.7 s across the board); a name shows
  once the pointer has stayed on its button 0.18 s, INSERT COIN once it has been off them 0.6 s (so crossing the gaps
  between buttons doesn't bring it back), and a change waits for the one under way. The timings are named constants
  at the top of `MarqueeDisplay`. A slide in from the side was tried first: at a pace that keeps up with the pointer
  the afterglow smeared it into a blur.
- **Round 3: a real LED sign** (#148, the user: "do not like the current animation of the text. too smooth and the
  font are not pixel enough. lack the retro feeling"). The fades made it smooth, and the dots, under two screen
  pixels each on the medium cabinet, were drawn blurred together by the texture's linear filter. Now every dot is lit
  or dark, nothing in between (the glyphs are drawn in code, the 5×7 font above; a faint glow around lit dots stays),
  and changes go in steps: the board blanks for 0.2 s, then the new text is drawn in a whole column at a time, 30
  columns a second, from its own first column (VOICE in about a second, INSERT COIN in two). The same pauses before a
  name or INSERT COIN shows as round 2, but a change no longer waits for one under way: it cuts short a text still
  being drawn in (blank, then the new one), as the slower draw-in otherwise left a name up to 2.4 s behind the pointer
  when it came back to a button while INSERT COIN was coming in.
- **Round 4: names snap on, INSERT COIN animates** (#148, the user, 2026-10-26: "font looks better. the time for the
  marquee to display the full text is too slow. this cannot serve for the needs to help user understand what they are
  hover", then choosing "Names snap on, idle animates"). The name of the button under the pointer flips on whole in
  one step, as an LED sign turns to its next page, once the pointer has been on it 0.1 s (0.18 s before; a quick pass
  across the buttons, under 0.1 s on each, still shows none of them): from the hover to the whole name went from about
  1.2 s to 0.1 s. A name too long for the board shows its start at once and scrolls after a second, as before. INSERT
  COIN, which nobody waits for, keeps the blank and the column-by-column draw-in at 30 a second, after the pointer has
  been off the buttons 0.6 s. The font, the board and the dots are round 3's.

## Built: the home screen lifted on a small or medium cabinet (2026-10-26, #149)

The user, checking the switches on a small cabinet (2026-10-25): "the switch icon is almost not distinguishable also
the app text is hard to read (the menu is also hard to read in small!). consider in small and mediem, click on the crt
open the screen like today when open an app to enhance the readability. large is fine."

- **On Small and Medium, a click on the CRT lifts the screen out as the home screen opens,** the same lift an app
  makes (`ScreenView.Lift`, no second mechanism), so the switches and the app names are read at the lifted size. On
  Large it opens on the cabinet as before, and only an app lifts it.
- **It stays lifted until the system closes:** opening an app keeps it up, and so does going back home from one
  (the app's back mark). It settles back as the system closes: from the power mark, after a while untouched, or on a
  press away. On Small and Medium a press away from the lifted screen closes the system, app open or not (the PM's
  choice: the press is to put the screen away, and on those sizes there is no home screen on the cabinet to step back
  to); on Large it goes back to the home screen on the cabinet, as before.
- **The right-click menu** settles it as it does a lifted app (#83): an entry chosen off the cabinet's art is a press
  away; over the art it stays lifted.
- **SETTINGS** opens the Settings window as before; the lifted home screen stays until a press away or the time
  untouched closes it.

## Built: a switch's name on the lifted bar (2026-10-26, #154)

The lifted home screen covers the marquee, where the name of the switch under the pointer lights (#109), and the
switches are icons only. The user: "i think when hover the switch, replace the time with the switch name".

- **While the screen is lifted** (the home screen on Small and Medium), the switch under the pointer shows its name on
  the bar in place of the time, small and pink as it did before #109, and the time comes back as the pointer leaves
  it. The marquee still gets the name, behind the screen. App tiles carry their names already, so only switches.
- **On Large** the home screen stays on the cabinet: the name lights on the marquee and the bar keeps the time.

## Built: her speech bubble the same size by any cabinet (2026-10-27, #172, #170)

The user, trying a small cabinet: "at S, the msg bubble is also very small and a bit unreadable. i would suggest the
msg bubble to have the size like under L". QA on #169: a full page already covered the left of the marquee.

- **Her bubble is drawn in a window of its own,** a pixel per canvas unit: its text, page mark and answers are the size
  they are by a large cabinet on Small, Medium and Large. A small cabinet's window has no room for that, so the bubble
  reaches past it onto the desktop, over her head as before.
- **A full page sits beside the marquee, left of it,** its right edge short of the marquee's (on Large too, which
  covered its first letter). Only when the screen's left edge leaves no room there does it rise above the marquee
  instead. It keeps to the screen the cabinet is on, and no taller than above her head by a large cabinet.
- **Clicks pass through it** as before, except while she waits on her call: then a click on it is "yes?", and its
  answers take their own clicks. It is kept just above the cabinet, under the windows the player opened, and is put
  there as it shows, so it never opens over the player's game.
- **It gives way to the lifted screen and a memo's card,** which were drawn over it when it was in the cabinet's
  window: while either is over the bubble, the bubble's window hides (its pages go on turning, as they did unseen
  under them) and comes back as they go. On Small and Medium the lifted home screen covers her head, so the bubble
  waits behind it; trying to sit it beside or above the screen would leave its tail pointing into the screen.
- **A change of the cabinet's size** places it again over her head.

## Open
- **A right-click while a card by her stool waits,** or while she speaks: the menu still opens; whether it should close
  the card is left to how the card already handles a click elsewhere.

## Order

Each step is usable on its own.

1. **The right-click menu** on her and the cabinet: the tray's menu, built once and shown in both places, at the pointer.
   *Built (#68):* opened at the pointer when the cabinet window gets a right click (it only does on her or the
   cabinet: everywhere else clicks pass through); dev actions `menu`, `menuopen` and `menupress:`. It is a second `PopupMenu`
   made by the same code as the tray's and kept in step with it, drawn by the app: Windows' own menu, which the tray
   shows, holds the app still while it is open, and the user wanted her voice and motion to carry on (2026-10-18).
   Like every popup of the app's, it is kept above the cabinet and the app's windows while it shows, and on top of
   other apps when they are (Always on top): it opened under them before (the user, 2026-10-21).
   *Sized (#163):* the same size by a small, medium or large cabinet: the size it had by a large one with Windows at
   125%, the best fit to the user (2026-10-27; the medium one's was still a bit small), and as large in inches on
   screens at other scaling. A popup menu takes its scale
   from the window it belongs to, so it had shrunk and grown with the cabinet, and the screen's scaling set for it
   was overridden.
2. **The CRT:** the SETTINGS app, and the quick-switch row on the home screen.
3. **Settings → Memory:** the page with "Open the memory book…" and "Open Chat history…".

## Sources

- [Cat+Mouse desktop pet: "right-click the pet for settings"](https://vilverdemon.itch.io/catmouse-desktop-pet), one
  example of the habit desktop-pet apps teach.
