# Companion helper — decisions so far (2026-10-02)

The companion on the stool gets a language model behind her. Nothing of this is built yet. This note records what
has been decided, what those decisions mean for the design, and what is still open. Background on the character
itself: [../research/2026-10-02-companion-character.md](../research/2026-10-02-companion-character.md).

## Decided

1. **First version:** she knows the cabinet's state (time left, credits, debt, streak, the week's numbers, the
   running session) and remarks on it or answers questions about it.
2. **Later, and the design must leave room for both:**
   - *Personal agent:* calendar access, reminders, notifications. She acts on things outside the cabinet.
   - *Companion and role play:* people will import characters they like and want to talk to them as those
     characters, as a friend or a partner. Who she is belongs to the character, not to the app.
3. **Conversation:** two-way. Clicking her opens a text box, and her reply appears in the speech bubble. She is
   modelled as a real companion with **long-term memory**, which needs a proper design (open question 1).
4. **How chatty:** quiet. She speaks on cabinet events and when clicked, plus an occasional unprompted remark,
   and a tray switch mutes her (built: tray "Speech bubbles", setting `SpeechBubbles`). Be ready for
   **text-to-speech** later, and for **voice-to-voice** conversation.
5. **Models and keys:** the user brings their own API key and provider URL, so third-party inference providers
   work too. Nothing in the app depends on one model; the user picks it.

## What follows for the design

- **Canned lines stay.** With no key, offline, or when the provider fails, she says the lines from `App/Lines.cs`.
  The cabinet never waits on the network.
- **One provider interface:** send messages, get text back as a stream. First an OpenAI-compatible
  chat-completions client, since most third-party providers and local servers speak that protocol; native
  clients (Anthropic and others) behind the same interface. Settings: base URL, key, model name. The key is
  stored encrypted for the Windows user (DPAPI), never in plain text in `settings.json`, never logged.
- **Streamed replies from the start:** the bubble types as text arrives, and text-to-speech can later start on the
  first sentence before the reply is finished. Voice-to-voice puts speech-to-text in front of the same
  conversation loop and speech behind it; providers' realtime speech-to-speech APIs would be a separate path.
- **Replies carry stage directions** as well as words: which moment to play (cheer, worry, wave…) and which
  expression, so her body acts out what she says. The app checks them and ignores anything it does not know.
- **Persona per character:** who she is (name, personality, way of speaking, relationship to the user, limits)
  is a file next to the model in the characters folder. Built-in characters ship one. Swapping the model swaps
  the person.
- **Memory stays on the machine:** history, summaries and remembered facts live in the profile folder. Only what a
  request needs is sent, and only to the provider the user chose. The user can see and delete what she
  remembers.
- **Agent abilities are tools** the model may call. Reading the cabinet's state is the first one. Anything that
  acts outside the app (calendar entries, notifications) asks the user first.
- **Soft enforcement still holds:** she can remind, nag or sulk, never close or block a game.
- **Before a Steam release:** companion and role-play use raises questions of age rating, content limits and
  provider terms. To be decided before shipping, not now.

Seams that exist today: `Lines.For` / `Lines.Status` produce what she says, `CompanionView.Say` shows it,
`CompanionView.Play(Moment)` acts it, and `Director` maps cabinet events to moments.

## Open

1. **Long-term memory: an existing framework or our own design?** Candidates to compare: Letta (formerly
   MemGPT), mem0, Zep, LangGraph's memory, the lorebooks of SillyTavern-style chat apps, or our own (rolling
   summary + fact store + search over a local SQLite file). Criteria: runs inside a .NET desktop app with no
   extra server, works with any provider, stores locally, and the user can inspect and delete it. Most of the
   candidates are Python libraries or separate services (to be checked), so "use a framework" may end up meaning
   "borrow its design".
   *Researched:* [../research/2026-10-02-companion-memory.md](../research/2026-10-02-companion-memory.md)
   recommends our own memory in-process (SQLite), built from Letta's, mem0's and Generative Agents' designs, on
   Microsoft.Extensions.AI for providers. Accepted, with memory + auto-compaction (like Claude and ChatGPT)
   as the baseline and Hermes Agent as the reference for the agent phase; the decisions are listed there.
2. What she may remember about the user, and how the user reviews and erases it.
3. Persona format: our own, or the community "character card" format used by SillyTavern-style apps (to be
   checked), so users can bring a card along with a model.
4. Embeddings for memory search: the provider's embeddings endpoint, or a small local model.
5. Speech: which text-to-speech (provider or local), a voice per character, and speech-to-text for
   voice-to-voice.
6. Unprompted remarks: how often, and whether she ever starts a conversation herself.
7. Showing the user what is sent and what it costs.
