# Settings and the switches, from the cabinet itself

*Decided with the user, 2026-10-17 (`/pm:brainstorm #64`). Nothing built yet; the order is at the end.*

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

## Open

- **The SETTINGS app's icon,** drawn in the style of the CRT's other icons, and its place among them (last, to start).
- **The switch row on a small cabinet:** whether three toggles fit at the smallest cabinet size, or show as icons
  only there.
- **A right-click while a card by her stool waits,** or while she speaks: the menu still opens; whether it should close
  the card is left to how the card already handles a click elsewhere.

## Order

Each step is usable on its own.

1. **The right-click menu** on her and the cabinet: the tray's menu, built once and shown in both places, at the pointer.
2. **The CRT:** the SETTINGS app, and the quick-switch row on the home screen.
3. **Settings → Memory:** the page with "Open the memory book…" and "Open Chat history…".

## Sources

- [Cat+Mouse desktop pet: "right-click the pet for settings"](https://vilverdemon.itch.io/catmouse-desktop-pet), one
  example of the habit desktop-pet apps teach.
