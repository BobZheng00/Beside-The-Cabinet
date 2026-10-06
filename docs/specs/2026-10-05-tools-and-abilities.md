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

**The reader** (`App/MemoReader`): the title, who it's from and when, the text (code in a fixed-width font), and Copy, Edit, Delete (pressed twice), Pin on the cabinet, Tear it off or Take it down. "Write a memo" opens it empty: the player's own memo prints out of the slot once saved.

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

### 2. Focus sessions (agreed)

**Pomodoro on the cabinet** (built-in, inside the app):
- 25 minutes of focus, then 5 off, a longer break every fourth; lengths in settings.
- The CRT counts down.
- She says when each part starts and ends, and can be asked "how long left?".

**Music** (*proposed*). Neither service gives us an API:
- **Spotify's Web API** is effectively closed to apps like this. Since May 2025, going beyond a handful of test users needs an organisation with 250,000 monthly users, and controlling playback needs Premium.
- **YouTube Music** has no playback API at all.

What works without one:
- **Opening the player's focus playlist link**, saved in settings. A Spotify link opens the playlist in the app but does not start it: Spotify doesn't support that. A `:play` ending on the link is reported to start it, unofficially. A YouTube Music `watch?list=` link starts the playlist in the browser if the browser allows autoplay there (usually so for a site used often).
- **Windows' media controls** (the system media sessions, the same as the volume flyout's) for playing, pausing, skipping and what is playing. Better than pressing media keys: they act on the music app alone, not a game or a video, and she can say what's on.

*To test on the player's PC:* which link starts by itself, with Spotify's `:play` trick, and opening then pressing play through the media controls.

Music starts with a focus session and pauses at a break, if the player wants that. Opening the link is outside the app: she asks the first time, then remembers the permission.

### 2.5. Stand up, drink water (agreed; details *proposed*)

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

## Order (*proposed*)

1. **Groundwork** (*built 2026-10-05*, above):
   - tool calling in her conversation (Microsoft.Extensions.AI's loop);
   - the Allow / Deny ticket;
   - the bridging line;
   - a stable tool list.
2. **Memos** (*built 2026-10-05*): printing, reading, pinning, the archive, the player's own, and her phone motion.
3. **Focus sessions** with break, stand-up and water nudges, then music (a link and Windows' media controls) and opening links and apps.
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
