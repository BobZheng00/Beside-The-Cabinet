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
- The full hint prints from the cabinet's printer as a hint ticket, which the player reads at their own pace (code in a fixed-width font) and tears off when done.
- Hints come in the player's language. The model handles Chinese and Japanese; leetcode.cn gives the Chinese statement.

### 2. Focus sessions (agreed)

**Pomodoro on the cabinet** (built-in, inside the app):
- 25 minutes of focus, then 5 off, a longer break every fourth; lengths in settings.
- The CRT counts down.
- She says when each part starts and ends, and can be asked "how long left?".

**Music** (*proposed*). Spotify's Web API is effectively closed to apps like this:
- since May 2025, beyond a handful of test users it needs an organisation with 250,000 monthly users;
- controlling playback needs Premium.

So, no API. She uses what works without it:
- the player's focus playlist link, saved in settings, opens in the Spotify app (or any player);
- the media keys play, pause and skip, which work for Spotify, YouTube Music in a browser, and others.

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
2. **Focus sessions** with break, stand-up and water nudges, then music (playlist link, media keys) and opening links and apps.
3. **LeetCode hints**, with the printed hint ticket.
4. **Web search:** the provider's own first, then the connectors window with Tavily and SearXNG.

## Sources

- [DuckDuckGo Search API: the official option and what to use instead](https://link.sc/blog/duckduckgo-search-api-guide)
- [Spotify: updating the criteria for Web API extended access (2025-04-15)](https://developer.spotify.com/blog/2025-04-15-updating-the-criteria-for-web-api-extended-access)
- [Spotify API changes 2026: quota and development mode](https://vorplabs.com/agent-tools/spotify-api-changes)
- [Brave Search API free tier changes (2026)](https://agentdeals.dev/vendor/brave-search-api)
- [Tavily: 1,000 free credits a month](https://docs.tavily.com/documentation/about)
- [SearXNG as a local search API](https://docs.litellm.ai/docs/search/searxng)
- [LeetCode GraphQL: question content and hints](https://github.com/akarsh1995/leetcode-graphql-queries/blob/main/README.md)
- [Hosted web search across OpenAI and Anthropic through Microsoft.Extensions.AI](https://github.com/ghostflyby/Maieutics/pull/100)
