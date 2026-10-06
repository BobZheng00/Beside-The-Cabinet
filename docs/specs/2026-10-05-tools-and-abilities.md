# Tools and abilities (draft, 2026-10-05)

What she can do beyond talking, and how it is put together. Brainstormed with the user on 2026-10-05; background in `docs/research/2026-10-05-companion-agent.md` §6. **Draft:** the sections marked *proposed* wait for the user's word.

## How tools are organised (decided)

Modular, after Claude Code's tools, connectors and plugins:
- **Built-in tools** are the simple must-haves. They are registered directly and always there; nothing to set up.
- **Connectors** are the larger ones: anything needing a key, a sign-in or another app's access. The player chooses to connect each one, and its secrets are encrypted like the provider key.
- **Local MCP servers** the player installs count as connectors too.
- **Remote MCP is out for now.** We run no cloud service and hold nothing for the player, so we don't set one up for them.

Permission, as the companion spec already says:
- reading needs no permission;
- acting inside the app needs none;
- acting outside the app asks first: in her words, with a ticket offering Allow / Deny.

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

## Memos: how she hands over long text (agreed; details *proposed*)

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

## Order (*proposed*)

1. **Groundwork:**
   - tool calling in her conversation (Microsoft.Extensions.AI's loop);
   - the Allow / Deny ticket;
   - the bridging line;
   - a stable tool list.
2. **Memos:** printing, reading, pinning, the archive, the player's own, and her phone motion.
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
