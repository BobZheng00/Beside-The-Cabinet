# Settings and setup

*Decided with the user, 2026-10-10. Steps 1 to 3 built the same day, step 4 (Getting started) on 2026-10-12.*

## Why

The user, looking at the reminder window: "i start to feel that our config is too long and hard to navigate and there
is no clear on boarding guide. can we improve it?" What we found:

- **Grouped by how the code is built, not by what the player wants to do.** "Reminders…" also held check-ins, quiet
  hours, standing up and the phone. "Chat settings…" had six tabs, from the API key to what each reply cost.
- **Everything shown at once.** The phone part alone was four ticks and seven fields, used or not.
- **About 30 entries in the tray menu,** actions, switches and windows mixed.
- **Setup that happens outside the app was left to the player:** API keys, a Telegram bot and its chat id, a calendar's
  address, the voice engine. One line of hint each, at most.
- **The first run stopped at the initials and the LeetCode name.** Nothing led a new player to connecting her mind,
  without which she can't talk.

## One Settings window

Tray → Settings… opens one window: a list of pages on the left, in three groups, and the page chosen on the right.
Each page is short enough to read without scrolling much, and is named for what the player wants to do.

| Group | Page | What it holds | Was in |
|---|---|---|---|
| Her | Her mind | her provider (server, key, model; Test, Save); memory search; word for word | Chat settings → Connection |
| | Her voice | her voice on or off; GPT-SoVITS; the voice of whoever is on the stool; training | Voice settings…, tray → Voice |
| | Talking aloud | the talk key; listening without it | Chat settings → Listening |
| | Characters | who is on the stool, or nobody; speech bubbles; adding a character; the folder | tray |
| | Abilities | Auto; each tool's permission; sites allowed always | Chat settings → Abilities |
| | Privacy | what she may look at of yours; what she is told in the background | Chat settings → Privacy |
| | Add-ons | web search with SearXNG | Chat settings → Add-ons |
| Your day | Reminders | the ticket, her telling you, the chime, Windows; before agenda items; a try | Reminders… |
| | Check-ins | how did it go, and the quiet hours; standing up | Reminders… |
| | Your phone | ntfy, Discord, Telegram, any webhook; a test | Reminders… |
| | Calendars | linked calendars | Calendars… |
| | Balance | what a day can earn, bedtime, the LeetCode name | Balance…, tray → Change player… |
| The cabinet | Cabinet | size, sounds, in-game tickets, energy saver, always on top, start with Windows | tray |
| | Usage | what her requests used, and prices | Chat settings → Usage |

- **Saved as made.** There is no Save or Cancel for the window: a tick, a choice or a typed field is saved at once.
  Fields only show while what they belong to is ticked (an ntfy topic only with ntfy on).
- **A connection is the exception:** what connects her to something outside is used only when the player says so,
  since half of it is no use (and picking a provider to look at its server must not switch her over). Her provider has
  Test and Save; a calendar has Link it. The guides (below) take these over.
- **The tray and the pages agree:** Mute sounds and Voice are in both, and a change in one shows in the other.

## Guides for setup outside the app

Where setup happens elsewhere (a bot made in Telegram, a key from a provider's site, a calendar's address), a
connection shows as a card with its state ("Not set up", "Paired with you ✓") and a "Set up…" button. The guide goes a
step at a time: a labelled button that opens the right place ("Open BotFather"), a field only where something must be
pasted, and a tick once the cabinet has checked it worked. The connection is used once its test has passed.

Telegram's guide pairs by the `?start=` code (spec `2026-10-10-reaching-the-player.md`), so there is no chat id to
find: make a bot, paste its token (checked: "Found @…"), scan the code with the phone and press Start, and she says
hello. It says where her chat goes and that it isn't end-to-end encrypted. The same pattern serves her mind (provider,
key, model, test), her voice, calendars, ntfy and SearXNG.

## Getting started

After the initials and the LeetCode name on the cabinet, a checklist, first in Settings and in the tray until it is
done or hidden:

- connect her mind: needed to talk to her;
- her voice (optional; runs on the graphics card);
- talking to her aloud (optional);
- your phone (optional);
- your calendar (optional);
- balance: the usual limits and no bedtime, to change.

Each says what it gives, what it needs and how long it takes, with Set up… and Later. Later still: she mentions
something not set up when it would help, once.

**Built 2026-10-12** (`App/GettingStarted.cs`; the steps in `Main.StartSteps` and `Stool.StartSteps`):
- **Settings → Getting started** is listed first, above the groups, while anything is left and it isn't hidden; the
  tray has "Getting started…" under Settings… for as long. It opens by itself once the cabinet has said hello after
  the initials and the LeetCode name, on the first run only. It is made again each time it shows (`SettingsPage.Fresh`),
  so coming back from a page shows what changed there.
- **Your name first:** what she calls the player (`AppSettings.CallMe`), saved as typed and also on Settings →
  Characters. It fills a card's {{user}} and her prompt's "What you know about the player" ("The player goes by Bob.",
  `Persona.Player`); whoever is on the stool takes it up once the typing stops. Unset, she has no name for them: the
  LeetCode name is no longer used for it (the user, 2026-10-10: Hana said "BobSmith").
- **The steps:** Her mind (needed), Her voice, Talking aloud (shown only when the build has ears; ready from the start,
  it says how to talk), Your phone, Calendars; then Balance, what a day can earn and the bedtime, with Change…. Each has
  a word for how it stands (Done, Needed, Optional, Later), what it gives and needs and about how long it takes, and
  what it says now (her provider, her voice's state, where texts go, how many calendars). Set up… opens its page;
  Later puts an optional step off (`StartLater`), and it no longer counts as left. Done or not is read from the
  settings and her parts, not ticked by hand.
- **Hide Getting started** takes it out of Settings and the tray for good (`StartHidden`); everything in it stays on its
  own page. "All set" shows once nothing is left.
- **Her mind's guide** is the step it lacked: "Get a key from OpenAI…" (and the others; "Get Ollama…" for a server on
  this PC) opens the provider's page for keys (`Preset.Site`). The other guides were there already: GPT-SoVITS's
  download on Her voice, Telegram's steps on Your phone, where Google and Outlook keep a calendar's address on Calendars.
- **Not built:** her mentioning, once, something not set up when it would help.

## The first run

*Decided with the user, 2026-10-12, built the same day* ("should we have a onboarding flow for … user first open the
app to set up their name, llm key, other required and common settings, and our default character? and when ever the
user add a new character, they should go through the setup again?"). It revisits "a first-run wizard" (below): a
short flow for what she can't do without, the checklist for the rest.

- **Welcome** (`App/Welcome.cs`), a moment after the cabinet appears: three pages. Your name (`CallMe`; the initials
  come from it, its first three letters or digits, so the cabinet no longer asks); who keeps you company, the built-in
  characters with their persona's first sentence, seated on the stool as picked; her mind, the Her mind page without
  how she remembers (`MindSettings`, `brief`), made as the page comes up so it names whoever was picked. Finish once a
  provider is saved, Skip for now before; closing it keeps what was chosen. Then the screen says HI and she says
  hello in her own words (`Stool.Greet`), or, without a mind, that Getting started connects it.
- **LeetCode left the first run** for Getting started's optional steps (its Set up… opens Balance, whose Change…
  asks on the cabinet's screen, `Onboarding`, now the LeetCode name alone). The app is no longer LeetCode first.
- **A new character** gets "Meet …" (`App/MeetSheet.cs`) when added, not the whole setup again: the provider, the
  name and what the player has told any of them are shared. It says who they are (their card, a persona file, or none:
  a plain friendly companion, with Add a character card…), their voice (Add a voice…, and Set up voices… while
  GPT-SoVITS isn't), and the name they call the player (one for every character, the user's choice by default; a card
  may say otherwise). It fills in again as a card or a voice comes in.
- **How they sit** (the user, 2026-10-12: "we provide a boy and a girl default character but the motion is shaped to
  only apply to the girl character"): not two motion templates but a manner per character, chosen by the player and
  never guessed from the character (`Manner`, `AppSettings.Manners`): neat, today's (knees together, hands folded in
  the lap, the head tilting as she listens and reacts, a leg swung now and then), or relaxed (knees apart, a hand on
  each thigh, found from that character's own hips and thighs, head tilts a third as much, the legs mostly still).
  The gestures are shared. **Relaxed, reworked the same day** after the user's first run ("the knees are too split
  apart, especially on the girl model", the hands and arms strange, the side-to-side sway "very strange on a male
  model"), from how people sit at ease on a stool (men sit more open than women on average, but not sprawled; one foot
  on the rung and one down; asymmetry; a back a little rounded): knees 6° apart each instead of 12°, one foot down
  most of the time, the left hand past halfway to the knee and the right nearer its hip, each on its own thigh with the
  fingers curled over it and the elbows by the body (turned in across the lap, a hand read as covering it), the back a
  little rounded, less side-to-side balance, no legs swung, and a tune shown as a nod on its beat, not a sway. Neat is
  unchanged. Judged on clips of Fumiriya and Shino (`clips-body\relaxed-*2`). Fumiriya starts relaxed, everyone else neat. The choice is on the first run's character
  page, on "Meet …" and on Characters; whoever is on the stool moves into it as it is chosen, and "Show me" plays a
  few seconds of it (leaning in to listen, a nod, a tilt, re-settling, a leg swung if neat). The Welcome and "Meet …"
  open beside the cabinet, so the stool stays in view (`Paper.Open`, `besideCabinet`). Judged on clips of Shino,
  Fumiriya and Hana; Xbot, a mannequin with very thick thighs, gets its hands sunk into them and turned palm up.
- **`firstrun.cmd`**: a fresh profile every time, for trying the first run (the user, 2026-10-12); the user's smoke test,
  step 7.

## The tray

About ten entries: Show cabinet, Settings…, Getting started… (while unfinished), Memory book…, Chat history…, Write a
memo…, Check LeetCode now, the switches flipped often (Mute sounds, Voice, Conversation), Quit.

*Changed 2026-10-17* (the user): the same menu also opens from a right-click on her or the cabinet, and the CRT gets a
SETTINGS app and a row of the switches: `2026-10-17-on-the-cabinet.md`. The tray itself stays as it is.

## Order

1. The Settings window and the short tray, today's pages moved in as they are (*built 2026-10-10*: `SettingsWindow`,
   a page class each, `Form` for their parts; "Reminders & check-ins" became two pages, Reminders and Check-ins, to
   stay short).
2. Where the player is and the routing (reaching the player, step 2); its settings go on Your phone (*built
   2026-10-10*: the away time on a slider, and "Also while I'm at the PC").
3. Telegram both ways, with the first guide (*built 2026-10-10*: the steps on Your phone).
4. Getting started, with guides for her mind and the rest.

## Not chosen, and why

- **Save and Cancel per page:** with a list of pages, a change on one is easily lost moving to another, and it isn't
  clear what Save covers.
- **One long page with a search box:** fine for hundreds of settings; for about a dozen pages, a list to look down
  is quicker.
- **A first-run wizard that walks through everything:** a new player wants to see the cabinet first; a checklist can
  be left and come back to, and the optional parts stay optional.

## Dev

`settings` opens the window (tray → Settings…), `settings:<page>` at a page (`start`, `mind`, `voice`, `listening`, `characters`, `abilities`,
`privacy`, `addons`, `reminders`, `checkins`, `phone`, `calendars`, `balance`, `cabinet`, `usage`). `settingsdo:<label>` presses
the button or flips the tick with that text on the page showing; `settingsdo:<label>:<value>` sets the choice (by
number, from 0) or the field next to that label. Underscores for spaces. `settingsscroll:600` scrolls the page
showing; `settingswidths` logs any page made so far that is wider than its room (it would run off the window's
right edge, as the old voice window's row of buttons did). "Start with Windows" is Windows' own setting, the same in
a test run: never flip it in one. `trayitems` logs the tray menu's entries as they are now (Getting started… comes and goes). `welcome` opens the
first run's Welcome again; `welcomedo:<label>[:<value>]` and `meetdo:<label>[:<value>]` are `settingsdo` on the
Welcome and on "Meet …".
