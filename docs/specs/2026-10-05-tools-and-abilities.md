# Tools and abilities (draft, 2026-10-05)

What she can do beyond talking, and how it is put together. Brainstormed with the user on 2026-10-05; background in `docs/research/2026-10-05-companion-agent.md` §6. **Draft:** the sections marked *proposed* wait for the user's word.

## How tools are organised (decided)

Modular, framed like Claude Code's tools, connectors and plugins:
- **Built-in tools** are the simple must-haves. They are registered directly and always there; nothing to set up.
  - Memos, focus sessions and nudges, opening a link or an app, media controls, the LeetCode lookup, reading a page, and the provider's own web search.
- **Connectors** are the larger ones: anything needing a key, a sign-in or another app's access. The player chooses to connect each one, and its secrets are encrypted like the provider key.
  - A search connector (Tavily, SearXNG), writing to a calendar later.
  - **Local MCP servers** the player adds count as connectors too: the command that runs one on their PC. Its tools appear beside hers.
- **Remote MCP is out for now.** We run no cloud service and hold nothing for the player, so we don't set one up for them.
- **Plugins** are later, if at all: bundles of a connector, its prompt guidance and maybe her motions for it. Perhaps something a character brings with them at character import.

**Permission per tool**, set like Claude Code's permission rules (*built 2026-10-05, as proposed; open to change*):
- **Allowed** by default: reading, and acting inside the app.
- **Ask** by default: acting outside the app. She asks in her words, and a card by her stool offers Allow once, Always, or No.
  - "Always" covers what the tool says it should: for opening pages, the site (`open_link:leetcode.com`), like Claude Code's per-domain rules. Saying Always to one page doesn't let her open any page.
- **Off**: the tool isn't offered to the model at all.
- **Auto** (*built 2026-10-05*, the user's idea, like Claude Code's auto mode): one switch, off by default. Where she would ask, she goes ahead.
  - She still says what she's doing, and each use is in the chat history.
  - Tools switched Off stay off.
  - Nothing is remembered per tool: switching Auto off brings the asking back as it was.
  - *When web reading comes:* a page could try to talk her into opening things. Auto may then need to exclude what a page suggested, or local MCP servers' tools. Decide then.
- A local MCP server's tools all start at Ask, even those that say they only read: the server's word is a hint, not a guarantee.

**Where it's set:** Chat settings → Abilities.
- *Built:* the Auto switch, the built-in tools, each with its permission, and the sites answered Always, each with Forget.
- *Later:* the connectors, each with Connect and Disconnect, plus "Add a local MCP server…".

**Models without tool calling** (small local ones) keep stage directions as they work today. The abilities that need tools are simply not offered to them.

The tool list sits at the start of every request, the part providers cache. It changes only when a connector is connected or disconnected.

## Bridging a tool's wait (decided)

**A short line, written by the model itself** in the same reply, before it calls the tool. It fits what she is doing, in character and varied; it is not a canned "let me look". Examples: "Two Sum? Let me pull it up." or "Hmm, Tokyo, tomorrow… one sec." She says it at once, and her body shows her busy until the result comes. No extra request: it costs a few output tokens.

*To check:* some models call tools without writing anything first. For those, her body bridges the wait alone, or a line is asked for in the prompt more firmly.

## The first abilities

### 1. LeetCode hints (agreed; how, *proposed*)

**Which problem.** When the player asks for a hint, and only then, she reads the titles of their open windows. A browser tab on a problem is titled like "1. Two Sum - LeetCode" or "1. 两数之和 - 力扣（LeetCode）". From the title she finds the problem in LeetCode's public data: statement (in Chinese from leetcode.cn), difficulty, topics, and LeetCode's own hints.
- *Fallbacks:* a link, number or name said or typed in the slip; or the problem they last solved, which the cabinet already tracks.
- *Not:* reading the screen, or a browser extension.
- *Premium problems:* their text needs the player's sign-in; she can still hint from the title and topics.

**She can't see their code.** For feedback on code, they paste it into the slip. Reading the clipboard on request (asking first) could come later.

**Hints in tiers**, each only when asked for:
1. a nudge (what to notice);
2. the approach;
3. the outline in pseudo-code.

Never the full solution unless asked twice.

**Display.**
- She speaks and shows only the gist: a sentence or two. Code and long text don't speak well, and the bubble is small.
- The full hint comes as a memo (below).
- Hints come in the player's language. The model handles Chinese and Japanese; leetcode.cn gives the Chinese statement.

## Memos: how she hands over long text (agreed; *built 2026-10-05*, see "Memos: built" below)

The user's idea: the hint ticket is a general way for her to pass anything long. "Memo" keeps it apart from the memory book's notes and the cabinet's tickets.

**Where memos are used:**
- the full hint;
- what a search found;
- a plan or a list;
- anything she would otherwise say for a minute.

A built-in tool, `print_memo` (title and text), inside the app, so no asking.

**What happens to a memo:**
- **Printed:** it comes out of the cabinet's receipt slot (the one the game-over receipt uses), showing its title and first lines, and hangs there.
- **Read:** a click opens it whole, on paper in the app's style, scrolling, code in a fixed-width font, with a Copy button.
- **Pinned:** dragged (or its pin button) onto the side of the cabinet, it stays there, small and a little tilted, held by tape or a pin. Four or five fit. A click reads it; dragging it off unpins it.
- **Archived:** torn off, it goes to the archive: a Memos tab in the memory book, by date, where it can be read, pinned again or deleted. One left hanging is archived when the next comes.
- **The player's own memos:** "Write a memo" in that tab, or asking her ("make me a memo of this"). For notes to self.

**Her motion while she writes one** (the user's idea: she types on her phone and sends it to the cabinet). To be designed with care, in clips, as the clap was:
1. A phone in a pastel case comes into her hands, as the notebook does, held at lower-chest height in both hands, screen towards her. Her head bows to it, and her eyes are on it.
2. **Typing:** her thumbs take turns tapping, in uneven bursts with short stops. Now and then she glances up, thinking, then goes back to it. It lasts while the memo is written, scaled to its length.
3. **Sending:** a last firm tap and a small lift of the phone. She glances at the slot as the cabinet prints, with a small smile: there you go. The phone goes away.

The thumbs need finger poses like the pencil grip's, with the phone placed from the hands' joints, as the pad and pencil are.

### Memos: built (2026-10-05)

**In her mind** (`InsertCoin.Mind`, tested):
- `Memo` and the store's `memos` table: title, text, who printed it (a character, or none for the player's own), when, and where it is (hanging, pinned with its place, archived).
  - A new memo hangs from the slot; the one hanging there goes to the archive.
  - Pinning takes the first free place on the side; the side holds 5.
  - Titles are cut at 60 characters, a memo at 6,000.
  - Memos are the player's, not her memory: the memory book's forget buttons leave them be.
- `print_memo` (built-in, inside the app, so Allowed): its description tells her to use it for anything too long for her bubble, and to say only the gist.
- She knows what's on the cabinet: the titles of the memos hanging and pinned go with each request, in the cabinet block.
- **Seeing her start one:** `Provider.ToolStarting` reads the provider client's raw stream for the start of a tool call (OpenAI's first piece of a call, Anthropic's start of a tool-use block). So she takes her phone out as the model *begins* writing the memo, not once it's done.
- `Memo.ToBbcode` turns the little Markdown a model writes (headings, lists, bold, inline and fenced code) into the reader's rich text.

**On the cabinet** (`Cabinet/MemoBoard`, `MemoPaper`):
- **Printed:** the paper comes out of the printer's slot (the receipt's place) bottom edge first, with "MEMO", its title and its first words, and hangs there.
- **Pinned:** up to 5 on the right side panel, beside the screen, each a little tilted with pink tape across its top.
  - The places come from the art generator (`MEMO_PINS` in `tools/art/cozy_cabinet.py`), in true perspective. The side is steep, so a pinned memo is narrow (about 15 px wide at the default size): it reads as notes taped up the side, and a click reads one.
  - The paper is drawn once with mipmaps. Its outline and tape are drawn on the cabinet itself, so the ink line is as even as the art's.
- Clicking a memo opens it; the pointer over one brightens it.

**The sheet** (`App/MemoSheet`, 2026-10-06; it replaced a reader window the user found too much like a window): a memo clicked on the cabinet lifts off and grows into a sheet over the cabinet, with no frame or title bar: its own paper, sticker and torn edge, "MEMO", the title, who it's from and when, and the text, scrolling (code in a fixed-width font).
- Small drawn tools along its foot: copy, write on it, throw away (pressed twice); tear it off or take it down; pin it.
- Written on where it is: the title on a faint rule, the papers and stickers in a strip, ✓ to finish.
- A new one rises blank out of the slot (or the draft left last time), with "Let Shino read it"; printing shrinks it back into the slot, and out it prints.
- A click away keeps what was written and puts it back where it came from (to the side, the slot, or the archive below); Esc drops what was being written.

**The archive:** a Memos tab in the memory book: every memo, newest first, where it is, Read and Pin or Take down, and "+ Write a memo".

**The player's own memos, which she sees** (the user's request, 2026-10-06):
- Written without going through her: tray → "Write a memo…", or the Memos tab. Saved, it prints out of the slot like hers, and she glances at it as it comes out.
- **She thinks it over unseen** (`Chat.Consider`): no thinking dots, nothing shown while the reply comes. She is told what it says (the first 1,500 characters) and that it is the player's own (`Memo.Written`). Then either:
  - **she acts on it:** a reminder or a date on the agenda, through the usual stage directions, or a word of help, said in a line as a reply is;
  - **or she lets it be:** she answers only `[quiet]`, and nothing is shown or said.
- Either way the memo stays in the conversation as a cabinet line, so she knows it's there later; the titles of those still on the cabinet also go with every request.
- If she's busy (talking, listening, passing on a reminder), she reads it once she's free.

**Her phone** (`Puppet`, `Moment.Type` and `Moment.Send`; the phone is placed between her palms in `CompanionView`):
- **The phone** (the user found the first a plain slab, 2026-10-06): a mint case with rounded corners, and on its back, the side the viewer sees, a dark camera island in the top corner with two ringed lenses and a flash.
- **Holding it:** both hands at lower-chest height, the phone fairly upright (about 58° from flat), each hand cradling a lower corner with the fingers behind it.
  - Her head bows only a little; her eyes do most of the looking down. Bowed further, her hair hid her face from the cabinet's high camera.
- **Typing:** thumb taps a word at a time: bursts of 3 to 7, a fifth of a second apart, short gaps between words. Every few words she pauses for a second or so and glances up to think.
- **Sending:** a firmer last tap and a small lift of the phone. As it prints she glances at the slot with a small smile, and the phone goes away.
- If she wasn't seen starting it (a provider whose stream isn't read, or the dev action), she types for a moment before sending. If no memo comes within 40 s, she puts the phone away.
- *Judged on clips* (`<tools>\voice-trial\clips-body\phone-body.mp4`, `memo-flow.mp4`).

**Not yet:**
- dragging a memo onto the side or off it: the reader's buttons do that;
- her phone and the paper on another skin (the places come from each skin's generator).

### Memos, next: by hand, and readable (the user's notes, 2026-10-06)

The user wants memos more usable and complete before the next ability:
1. **Writing one from the cabinet itself.** The tray takes too long.
2. **Readable and told apart.** The paper is too blurry to read, hanging or pinned, and memos can't be told apart. It needs a way to customise them, and to separate one from another.
3. **Placing it by hand.** The player chooses where on the side it goes: taking the printed memo out of the machine with the mouse and sticking it to the side.
4. **Her reading the player's memo** is one ordinary request to their provider (a short reply's cost). "No action" means the model chose `[quiet]` for now; the memo stays in her conversation.

**Decided with the user (2026-10-06):** writing from the CRT, the start of a small OS on it ("we need some sort of small OS on the CRT anyway"); hover to read, paper colours and stickers; a tick to keep a memo from her.

**Built (2026-10-06):**
- **The CRT's small system:** first a text menu; replaced the same day by apps (below).
- **Reading on the cabinet:** pointing at a memo for a moment shows it flat beside the pointer (`MemoCard`): its paper, sticker, title and the first 400 or so characters, readable at the default size. The cabinet itself stays in true perspective.
- **Telling apart:** six papers (cream, yellow, pink, mint, lilac, blue) and six stickers (star, heart, coin, cat, flower, bolt), chosen when writing or in the reader, on the paper on the cabinet, the card and the reader's page. Her memos come on cream, the player's own on yellow.
- **By hand:** press on a memo (hanging or stuck on the side) and pull. It comes off into the hand as the flat card, a little smaller and tilted.
  - Over the side panel it shows where it will land, in perspective, at the pointer. Let go and it sticks there, a little tilted, taped; the latest lies on top. Up to 10, overlapping as they like.
  - Let go elsewhere on the cabinet: it goes back where it was. Off the cabinet: taken down, into the archive.
  - The reader's Pin button puts one at a free spot (the art generator's five spots first).
  - The art generator exports the side panel's plane (`MEMO_AREA`, `SIDE_SIZE`; its corners on the canvas give the homography), so any spot is in true perspective. The window carries a memo instead of moving itself when a pull starts on one (`DesktopWindow.Grabs`).
- **Her reading it:** the writer's "Let Shino read it" tick, on by default; unticked, no request is sent and she doesn't see it.
- The store keeps each memo's paper, sticker, spot and tilt (version 9; one pinned before spots waits in the archive).

### 2. Focus sessions (agreed)

**Pomodoro on the cabinet** (built-in, inside the app):
- 25 minutes of focus, then 5 off, a longer break every fourth; lengths in settings.
- The CRT counts down.
- She says when each part starts and ends, and can be asked "how long left?".

**Music** (*proposed*; the user has no Spotify Premium and uses Spotify desktop, a browser being acceptable, 2026-10-06). Neither service gives us an API:
- **Spotify's Web API** is effectively closed to apps like this. Since May 2025, going beyond a handful of test users needs an organisation with 250,000 monthly users, and controlling playback needs Premium.
- **YouTube Music** has no playback API at all.

What works without one:
- **Opening the player's focus playlist link**, saved in settings. A Spotify link opens the playlist in the app but does not start it: Spotify doesn't support that. A `:play` ending on the link is reported to start it, unofficially. A YouTube Music `watch?list=` link starts the playlist in the browser if the browser allows autoplay there (usually so for a site used often).
- **Windows' media controls** (the system media sessions, the same as the volume flyout's) for playing, pausing, skipping and what is playing. Better than pressing media keys: they act on the music app alone, not a game or a video, and she can say what's on.

*To test on the player's PC:* which link starts by itself, with Spotify's `:play` trick, and opening then pressing play through the media controls.
- *Ruled out:* a headless browser playing Spotify's web player (the user's suggestion): it needs DRM that automated browsers often lack, Spotify's user guidelines forbid automated access, and it would fight the desktop app for the one playback an account has. A Spotify connector through the official Web API needs Premium (Spotify's February 2026 rules for personal developer apps).

Music starts with a focus session and pauses at a break, if the player wants that. Opening the link is outside the app: she asks the first time, then remembers the permission.

### Focus, music and standing up: built (2026-10-06)

**Focus sessions** (`InsertCoin.Mind.FocusSession`, tested; `App/Focus`):
- Focus, a short break, a long one after every so many (25 / 5 / 15, every 4th by default), round after round until stopped; pause stops the clock, skip ends the part at once. Not kept across a restart.
- **FOCUS on the CRT** (`App/Crt/FocusApp`): before a session, the lengths, each with down and up beside it (the user's request, 2026-10-06, instead of a click cycling round: focus 5 minutes to 2 hours, breaks 1 to 30, stopping at either end), whether their music plays along, and START; during one, the part and round, the time left, a bar, the rounds up to the long break, and PAUSE / GO, SKIP, STOP.
- **The readout counts down** while a session runs and the screen is closed: the time left, "FOCUS - ROUND 2" or "BREAK - STAND UP", the bar showing how much is behind (`ScreenView.Focus`). A game's clock comes first.
- **She says each turn** in her own words (the start and stop when the player does them on the screen, and every part running out or skipped there), once she is free, as reminders are; stale after three minutes. Breaks come with standing up, stretching and water. In a call only her bubble; in a full-screen game or presenting only a ticket. Without her mind, canned lines (`Lines.Focus`).
- **Her tool** `focus_session` (start with optional lengths, pause, resume, skip, stop): inside the app, no permission. What she does with it she says in her reply, so it isn't announced again. Where a session stands is in the cabinet block, so she can say how long is left.

**Music** (`Desktop/Music`, Windows' media sessions; the game now targets `net10.0-windows10.0.19041.0` for them):
- The music app's session: Spotify's when it is open, else another music app's (Media Player, Apple Music, NetEase Cloud Music, QQ Music…), a browser's only when the saved playlist is a web one. Never "whatever Windows calls current": on the user's PC that was Sound Recorder.
- **Spotify starts by itself** (the user's request, 2026-10-06): when something should play (play, next, back, a focus part, her tool) and no music app is open, Spotify is found on this PC (its installer's copy in %APPDATA%\Spotify, the Store's, or whatever its `spotify:` links open) and started, asked to start minimised (it may not listen: no documented option), then told to play once it shows up among the sessions (up to 25 seconds). It carries on with what played last.
- **Keep playing, or start a playlist** (the user's request, 2026-10-06: the two are different, and playlists are chosen as wished): ▶ carries on with whatever was playing; START on a saved playlist plays it from its beginning.
- **Starting a playlist** (`Music.StartPlaylist`, `Desktop/SpotifyButtons`): its link opens it in Spotify (`spotify:playlist:…`; Spotify started for it if needed), then Spotify's own "Play <its name>" button is pressed through Windows' UI Automation: no keystrokes, no mouse, no focus taken. Spotify's links and Windows' media controls can't start a playlist (`:play` only works for albums and tracks, so those start by their link); its button can. Started for this, Spotify is minimised again once it plays. It waits up to 24 seconds for the page; if the button never shows, what played last carries on. Spotify names its buttons in its language: English here (checked on the user's PC, 2026-10-06: 280 buttons, "Play Daily Mix 2" among them). UI Automation is reached through its COM interface, declared by hand: Godot's .NET doesn't load the Windows desktop runtime that wraps it (tried: UIAutomationClient wouldn't load).
- **Saved playlists** (`AppSettings.Playlists`, each a link and the name Spotify gives it through its public link preview, `open.spotify.com/oembed`): as many as wished, added from the clipboard (Share, Copy link in Spotify), taken off with ×. One is starred: focus sessions start with it (the first added is, at first); none starred, what played last carries on. The single link kept before is moved in as "My playlist".
- **MUSIC on the CRT** (`App/Crt/MusicApp`), two views. NOW: what is playing ("Press play: Spotify starts" when nothing is), back / play-pause / next, MY PLAYLISTS (n), and the focus switch saying what focus does ("FOCUS STARTS Deep Focus", "FOCUS: MUSIC PLAYS ON", "NO MUSIC IN FOCUS"). PLAYLISTS: three to a page (swiped), each with its star, its name, START and ×; + ADD (PASTE LINK) and BACK.
- **With focus:** a session's first focus part starts the starred playlist from its beginning; after a break what was playing carries on; it pauses at breaks, while paused and at the end.
- **Her tool** `music` (start_playlist with a saved playlist's name, or the starred one; play, pause, next, previous): outside the app, so the card asks ("Start a playlist?" with its name; Always for music). What is playing and the saved playlists (which one starts focus) are in the cabinet block.
- A test run never touches the player's music or clipboard, nor presses Spotify's buttons: it says what it would do (`musicfake`, `musiclink` pretend). `spotifyprobe` only reads what Spotify's window shows.

**Stand up, have some water** (`Stool.MaybeStandUp`): after a stretch at the PC with no real break (50 minutes by default; tray → Reminders…; five minutes away resets it), she suggests standing up, stretching or water, and the count starts again. Never in a call or the quiet hours, nor in a focus session (its breaks do it); in a full-screen game or presenting only a ticket.

### 2.5. Stand up, drink water (agreed; *built 2026-10-06*, above)

The player sits for hours without a break or water. She notices continuous time at the PC (the cabinet already knows when they were last idle). After a while (50 minutes, say), she suggests standing up, getting some water, stretching; and says it's fine to leave the work for a bit. She does the same at Pomodoro breaks.
- Gentle and configurable, as check-ins are.
- Never in a call. In a full-screen game, only the ticket.
- The timer resets when they have actually been away.

### 3. Web search (agreed; providers *proposed*)

A general ability, not just for LeetCode. It reads, so it needs no permission.
- **Providers with their own search** (Anthropic, OpenAI): theirs, through Microsoft.Extensions.AI's hosted web search. *To check* for our Anthropic client.
- **Other providers and local models: a search connector.** DuckDuckGo has no web search API: its free API gives only instant answers (definitions, Wikipedia abstracts), and its pages fight scraping and forbid it. Instead:
  - **Tavily**, with the player's key: 1,000 free searches a month, made for agents;
  - **SearXNG**, for those who run it on their own PC: free and private.
  - Brave's API is now paid: a $5 monthly credit, about 1,000 searches.
- **Fetching a page:** a built-in tool reads one page's text.

### 4. Open a link or an app (agreed)

Built-in, outside the app: she asks first. Examples: today's daily problem, a game, a playlist.

## Later (recorded, to discuss)

- Weather.
- Writing to the calendar (Google or Microsoft sign-in, a connector).
- Reading the screen (only when asked).
- Files the player hands her.
- Reading the clipboard (asking first).
- Local MCP servers (connectors).
- A gesture per sentence while she speaks (the motion-file layer, §7 of the agent note).

## Groundwork: built (2026-10-05)

**In her mind** (`InsertCoin.Mind`, tested):
- `Toolbox` holds her tools, the player's permissions (by tool, or `tool:site` for Always), and runs each call. `BuiltIn` has the built-in tools.
- **The loop:** Microsoft.Extensions.AI's `FunctionInvokingChatClient`, with our own invoker for the permission check.
  - Up to 10 tool calls a reply (raised from 4 at the user's word: she says what she's doing as she goes); the last request has no tools, so she answers.
  - The same call twice in one reply (same tool, same arguments) isn't made again: a model going round in circles can't open one page ten times.
  - Calls run on the app's main thread, where the card and the memory store live.
- **The list is the same every request**, sorted by name. Only switching a tool off (or on) changes it; Allowed and Ask don't.
- **Every reply carries it:** answers, check-ins and reminders alike. Otherwise the cached start of the prompt would differ between them.
- **The bridging line:** the prompt asks for a short line of her own before each tool, fitting what she's doing, never a stock phrase. A paragraph "Your abilities" is in the prompt only while tools are offered.
- **What's remembered:** each tool use is a cabinet line in the conversation, between the player's line and hers: `You used open_link ({"url": …}): Opened in the player's browser.`, or `…; the player said no.` The full result stays in that reply's requests only, so a long one doesn't fill the conversation.
- **Her words around a tool** read as one reply: "Want me to open it? There you go, it's open." (a space between the two parts, none in Chinese or Japanese).
- **Models without tools:** if the provider turns tools down ("does not support tools", as Ollama says for such a model), she answers at once without them, and they aren't offered again until the provider changes.

**In the app:**
- **The permission card** (`PermissionCard`) stands by her stool, above the slip you type on.
  - It shows what and where, always the real address (a look-alike domain shows in its `xn--` spelling).
  - Clicks only: no key answers it.
  - It waits 2 minutes, then counts as no answer. Cutting her off (a click on her, a new message) counts as no answer too.
  - While it waits, the reply's 90-second clock is stopped, and each tool she uses starts it afresh, so a longer piece of work isn't cut short.
- **Her body:** she leans in with her eyes on you while you decide (the listening pose). Your answer gets a small "got it". While the tool runs and the model answers again, she looks away to think; then she carries on.
- **The first tool, `open_link`:** opens an `http` or `https` page in your browser, asking first. Nothing else opens: no files, programs or other links (`steam://` comes with apps, later). A test run (`--offscreen`) only logs what it would open.

**Measured** (*measured*, 2026-10-05, mock models, never a real key):
- OpenAI's and Anthropic's clients both carry the tools, the call and its result. Anthropic's automatic caching stays on with tools.
- In the app: asked, allowed once, allowed always (the next page on the same site opened without asking), said no, and a model refusing tools.
- Each tool call adds one more trip to the model: about 0.4–0.7 s with the mock.

**Not yet:**
- answering the card by voice ("yes");
- a real provider's model choosing tools well, and how varied its bridging lines are. Both are in `docs/to-try.md`.

## The CRT's small system (the user's idea, 2026-10-06; *built*)

"The apps will be listed as icons on the screen, four on each screen, and the user is able to explore them and reorganise them like phone apps", with animation between the regular screen and the apps: fun, natural and smooth.

**Built** (`App/Crt/CrtOs`, the apps `CrtApp`; `ScreenView` gives it a desk layer over the readout and redraws every frame while it is open):
- A click on the screen: the readout folds away like a CRT switching off (to a bright line), and the home screen's apps pop in one after another.
  - The home screen: the time and a power mark along the top, the apps as tiles in a 2×2 grid, four to a page, with page dots when there is more than one page.
  - A swipe across the screen turns the page.
  - Holding an app (about half a second) picks it up: it grows and wobbles, follows the pointer, and the others make room; carried to the screen's edge, it goes on to the next page. The order is kept in settings (`CrtApps`).
- An app opens out of its icon: its tile grows to fill the screen, then the app shows, with back and its name along the top. Back shrinks it into its icon again.
- The power mark, or 15 s untouched on the home screen (40 s in an app), folds the readout back.
- **Apps:**
  - **MEMOS:** the memos as little paper tiles (colour, sticker, title; taped ones are on the cabinet), the cabinet's first; the first tile writes a new one. A tile opens the memo's sheet over the cabinet. Four to a page.
  - **AGENDA:** what is coming up, four to a page: when (today, tomorrow, the day) and what. A click opens the memory book's Agenda tab.
  - Next: FOCUS and MUSIC, with focus sessions.

**Too small at the small cabinet size** (the user, 2026-10-06): at S (0.6 of the canvas) the CRT is about 90 px wide and the memo sheet's text about 10 px. The user suggested a view that brings the viewpoint in front of the cabinet. Options:
- **Lean in:** the whole picture zooms toward the screen. Cinematic, but at S the window is about 390×490 px, so even zoomed the screen stays small unless the window grows, and her figure and the art would need drawing at a higher resolution.
- **Lift out** (*chosen and built 2026-10-06*): as the memo sheet does, the screen's content comes out of the CRT flat and facing the player.
  - When the system opens, the screen's corners glide from the cabinet's perspective to a flat screen on a little bezel (dark, inked, with the CRT's pink glow), at least 340 of the window's own pixels wide whatever the cabinet's size (`ScreenView.Lift`, `LiftedWidth`). Then the readout folds away and the apps pop in; closing runs the other way and it settles back into the cabinet. It fits in the window at every size, so the window never needs to grow.
  - On the small cabinet it covers her while open; on the large one it sits over the cabinet's top, her face in view.
  - The memo sheet and the hover card are drawn larger on a smaller cabinet (`MemoBoard.Readable`), so their letters stay the size they are on the medium one.

**Two views, and memos on the desktop** (decided with the user and built, 2026-10-06; they like the on-cabinet view's retro feel):
- **Home on the cabinet, apps lifted.**
  - A click on the CRT opens the home screen right on the cabinet, in perspective. Opening an app lifts the screen out to read while the app grows out of its icon.
  - Going back, or a press anywhere away from the lifted screen, settles it back onto the cabinet, home still open; it closes to the readout after a while, or from the power mark. A press away on the desktop is seen by polling the mouse buttons (`DesktopWindow.PressedElsewhere`; never in a test run), so a very quick click can be missed.
  - Lifted, it looks like a CRT up close (`screen_quad.gdshader`'s `lifted`): the glass bulges a little, its corners round off, scanlines every third pixel and a darker rim.
- **Memos anywhere on the desktop.**
  - Each open memo is its own small transparent window with no frame (`MemoSheet` in a `Window`, transient to the cabinet's so it stays out of the taskbar), at a readable size (at least the medium cabinet's, whatever the cabinet's size).
  - It flies out of the cabinet to rest beside it: left of it while the screen has room, then right of it, each one after the first beside the last; with no room left, over the cabinet, a little lower each (`Memos.Home`).
  - Dragged by the paper anywhere on the screen; several open at once; opening one already open brings it to the front.
  - Its × puts it back, flying to where it belongs on the cabinet (keeping what was written). Esc does the same while reading; while editing, Esc goes back to reading it as it was.
  - The cabinet underneath a sheet isn't pointed at (`DesktopWindow.Covered`), so no hover card shows through.

## Order (*proposed*)

1. **Groundwork** (*built 2026-10-05*, above):
   - tool calling in her conversation (Microsoft.Extensions.AI's loop);
   - the Allow / Deny ticket;
   - the bridging line;
   - a stable tool list.
2. **Memos** (*built 2026-10-05*): printing, reading, pinning, the archive, the player's own, and her phone motion.
3. **Focus sessions** with break, stand-up and water nudges, then music (a link and Windows' media controls) (*built 2026-10-06*, above); opening apps still to come.
4. **LeetCode hints**, as memos.
5. **Web search:** the provider's own first, then the connectors window with Tavily and SearXNG.

## Sources

- [DuckDuckGo Search API: the official option and what to use instead](https://link.sc/blog/duckduckgo-search-api-guide)
- [Spotify: updating the criteria for Web API extended access (2025-04-15)](https://developer.spotify.com/blog/2025-04-15-updating-the-criteria-for-web-api-extended-access)
- [Spotify API changes 2026: quota and development mode](https://vorplabs.com/agent-tools/spotify-api-changes)
- [Brave Search API free tier changes (2026)](https://agentdeals.dev/vendor/brave-search-api)
- [Tavily: 1,000 free credits a month](https://docs.tavily.com/documentation/about)
- [SearXNG as a local search API](https://docs.litellm.ai/docs/search/searxng)
- [LeetCode GraphQL: question content and hints](https://github.com/akarsh1995/leetcode-graphql-queries/blob/main/README.md)
- [Hosted web search across OpenAI and Anthropic through Microsoft.Extensions.AI](https://github.com/ghostflyby/Maieutics/pull/100)
- [Spotify Community: links don't autoplay in the desktop app](https://community.spotify.com/t5/Other-Partners-Web-Player-etc/Spotify-link-does-not-autoplay-in-desktop-app-Windows/td-p/1336587)
- [AutoIt forum: starting a Spotify playlist (the `:play` ending)](https://www.autoitscript.com/forum/topic/193665-start-spotify-and-play-a-playlist/)
- [MDN: autoplay policy](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Feature-Policy/autoplay)
- [Observing and controlling Windows media sessions in C#](https://blog.pipe01.net/2021/01/gsmtc.html)
- [The Old New Thing: getting and controlling what media is playing](https://devblogs.microsoft.com/oldnewthing/20231108-00/?p=108980%2F)
