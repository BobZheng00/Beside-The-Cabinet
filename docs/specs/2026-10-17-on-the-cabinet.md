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
  lights a board of 75 by 15 round dots on it, in the marquee's perspective. Letters are five dots by seven, as on a
  dot-matrix sign; twelve fit. Dots too small to tell apart (the small cabinet) run together into solid letters.
- **INSERT COIN at rest; the name of the CRT's button under the pointer** (a quick switch, an app, SETTINGS) while
  there is one, and INSERT COIN again when it leaves. Only those buttons, not other hover tips (the PM's choice). A
  name too long for the board shows its start, then scrolls through a column at a time, round and round.
- **The time stays on the CRT's bar** whatever is under the pointer. The bulbs above (`MarqueeLights`) are unchanged.
- **It behaves as an LED sign does** (round 2, the user trying it: "the text appear and switch too fast, which does not
  really looks like pixel display (still more like some rgb display)"): one pink LED colour, hotter in each dot's
  middle, with unlit dots faintly there; each dot glows up over 0.06 s and fades over 0.22 s; a change fades the old
  text out (0.22 s), then draws the new one in a column at a time from the left (0.7 s across the board); a name shows
  once the pointer has stayed on its button 0.18 s, INSERT COIN once it has been off them 0.6 s (so crossing the gaps
  between buttons doesn't bring it back), and a change waits for the one under way. The timings are named constants
  at the top of `MarqueeDisplay`. A slide in from the side was tried first: at a pace that keeps up with the pointer
  the afterglow smeared it into a blur.

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
2. **The CRT:** the SETTINGS app, and the quick-switch row on the home screen.
3. **Settings → Memory:** the page with "Open the memory book…" and "Open Chat history…".

## Sources

- [Cat+Mouse desktop pet: "right-click the pet for settings"](https://vilverdemon.itch.io/catmouse-desktop-pet), one
  example of the habit desktop-pet apps teach.
