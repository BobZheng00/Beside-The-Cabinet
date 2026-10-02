# Companion long-term memory — research note and proposal (2026-10-02)

Question from [../specs/2026-10-02-companion-helper.md](../specs/2026-10-02-companion-helper.md): should her long-term
memory use an existing framework or our own design? Status of claims: **read** = taken from the linked page;
**untested** = not tried in this project yet.

## Short answer

**Our own memory, built from proven designs, on top of off-the-shelf .NET plumbing.** The memory frameworks are
Python or TypeScript servers with their own databases. Shipping one inside a desktop app means bundling and
running a second program next to the cabinet. What they *do* is well documented and small enough to rebuild
in-process: a few tables in a local SQLite file and some background model calls. The parts worth taking
off the shelf are the model-provider layer and the storage engine, not the memory logic.

## The frameworks (read)

| | What it is | Why not as-is |
|---|---|---|
| [Letta](https://docs.letta.com/guides/agents/architectures/sleeptime) (formerly MemGPT) | Apache-2.0. Memory *blocks* pinned in the prompt that the agent edits itself, plus searchable archival memory; "sleep-time" agents consolidate memory in the background between conversations | Python server (FastAPI + Postgres). The chat model must call tools to manage memory. Open-LLM-VTuber, a Live2D companion app, added Letta memory and its users report extra latency, frequent errors, and that "the model must support tool/function calling" ([discussion](https://github.com/orgs/Open-LLM-VTuber/discussions/260)) |
| [mem0](https://docs.mem0.ai/migration/oss-v2-to-v3) | Extracts facts from each exchange, then for each candidate looks up similar memories and lets a model decide ADD / UPDATE / DELETE / NOOP ([breakdown](https://memo.d.foundation/breakdown/mem0)) | Python/TS library over an external vector database (Qdrant, Chroma, pgvector…). From .NET only as a service ([example](https://deployedinazure.com/getting-started-mem0-dot-net-agent-framework/)) |
| Zep / Graphiti | Temporal knowledge graph | Needs a graph database server |
| [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/context-providers) (.NET) | Agent runtime; memory plugs in as an `AIContextProvider` (add context before a call, store after it) | A shape, not a memory: its long-term providers call Foundry, mem0 or Neo4j services. Worth adopting later for the agent tools (calendar, reminders); our memory can have the same before/after shape so it fits |

Benchmarks do not settle it. On LoCoMo, putting the whole history in the prompt is more accurate than any memory
framework, but costs ~26,000 tokens and ~17 s per reply against ~1,800 tokens and ~1.4 s. Vendors' own numbers
contradict each other (Zep's 84 % was revised to 75 %; mem0 reproduced it at 58 %)
([mem0's summary, partisan](https://mem0.ai/blog/ai-memory-benchmarks-in-2026)). Lesson for us: keep recent
conversation verbatim (best quality, and a companion's daily volume is small) and use memory for what no longer fits.

## Designs worth borrowing (read)

- **Letta: core blocks + sleep time.** A few short, always-present blocks ("persona", "about the user") that are
  rewritten as things change, and memory upkeep done while idle instead of in the reply path.
- **mem0: extract, then reconcile.** New facts are compared with similar old ones, so "exam on Friday" updates the
  old "exam next week" instead of piling up beside it.
- **Generative Agents** ([Park et al. 2023](https://agentpatterns.ai/agent-design/generative-agents-memory-stream/)):
  rank memories by recency (exponential decay) + importance (model-rated 1–10) + relevance (similarity to the
  current message); write higher-level "reflections" when enough important things have happened.
- **MemoryBank / SiliconFriend** ([paper](https://arxiv.org/pdf/2305.10250)), built for long-term AI companions:
  a forgetting curve: memories that are rarely recalled and unimportant fade, recalled ones are reinforced.
- **Companion apps.** Kindroid ([docs](https://kindroid.ai/docs/article/memory/)) splits memory into persistent
  (backstory, key memories), cascaded (summaries of older chat) and retrievable (searched long-term memory, plus
  journal entries recalled by keyword). SillyTavern's character cards ([V2/V3](https://tavernsprite.com/blog/sillytavern-character-card-format/))
  carry a persona and a keyword-triggered lorebook; people already share them, so importing them gives the
  role-play use case content on day one.

## Off-the-shelf plumbing (read, untested here)

- **Microsoft.Extensions.AI** `IChatClient` / `IEmbeddingGenerator`: one interface for every provider, with
  streaming and function calling. The OpenAI client takes any base URL, so OpenAI-compatible providers and local
  servers (Ollama, LM Studio) work ([example](https://codetraveler.io/2026/08/17/connecting-to-any-openai-compatible-endpoint-with-microsoft-extensions-ai-openai/)).
  Anthropic's official C# SDK (`Anthropic` on NuGet, beta) implements `IChatClient` and takes a base URL
  ([docs](https://platform.claude.com/docs/en/api/sdks/csharp)). This is exactly decision 5 (bring your own
  key, provider and model).
- **SQLite** (`Microsoft.Data.Sqlite`) with FTS5 full-text search: one local file, inspectable, easy to back up
  or wipe. Vector similarity over a companion's few thousand memories is a brute-force loop, no vector
  database needed.
- **Embeddings:** Anthropic has no embeddings endpoint and many third-party providers differ, so a small local
  model keeps memory search provider-independent and offline. `SmartComponents.LocalEmbeddings` runs bge-micro
  (16 MB, ONNX) in-process ([docs](https://github.com/dotnet-smartcomponents/smartcomponents/blob/main/docs/local-embeddings.md));
  it is a prerelease package, so it needs a spike inside the Godot export first.

## Proposal

### What she keeps

| Layer | What | In every prompt? | Written by |
|---|---|---|---|
| Persona | Who she is: built-in, or the character card next to the model | yes | the character's author |
| Core | "About you" (name, goals, schedule, likes) and "Us" (how she calls you, running jokes, tone), a few hundred words each, rewritten when facts change | yes | background upkeep; editable by you |
| Recent | The last conversation turns, verbatim, up to a token budget | yes | the chat itself |
| Episodes | One dated summary per conversation, plus cabinet milestones (first Hard, a week over budget, a 7-day combo) written straight from ledger events with no model call; weekly digests roll them up | when relevant | background upkeep / the ledger |
| Facts | Short statements with importance, scope, when learned and last recalled | when relevant | background upkeep (extract, then reconcile) |
| Lore | Keyword-triggered entries from a character card's lorebook, and notes you pin | when a keyword appears | card author / you |

Scope: facts about *you* could be shared by all characters (any of them knows your exam is Friday), while
"Us", episodes and lore belong to one character (each relationship is its own). Open question below.

### One turn

1. You type (click her; later, speak). The cabinet state is read live (time left, debt, streak, session).
2. Memory search: the message is matched against facts, episodes and lore by keywords (FTS5) and, once
   embeddings land, by meaning; candidates are ranked by relevance + recency + importance and the best fit a
   budget.
3. The prompt is assembled stable-first, so providers' prompt caching makes repeat turns cheap:
   rules + persona → core → remembered things + cabinet state → recent turns → your message.
4. The reply streams into the bubble. A tag at the start (`[cheer]`, `[worry]`…) picks her moment and
   expression; it is stripped from the text, and unknown tags are ignored.
5. Nothing else happens in the reply path. Memory upkeep never makes you wait.

### Upkeep (her "sleep time")

After a conversation has been quiet for a while, or while a game runs and the cabinet is at slow pace anyway:
summarise the conversation into an episode, extract facts and reconcile them with similar old ones (ADD / UPDATE
/ DELETE / NOOP), rewrite the core blocks if facts changed, and fade the importance of facts that are never
recalled. Each step is a separate, small model call that returns plain JSON. **It does not depend on the chat
model calling tools**, so small and local models work, which is where Letta-based companions struggled. A step
whose output does not parse is skipped and retried later; nothing is lost because the raw conversation log is kept.

### You stay in charge

- Everything lives in the profile folder (`companion/memory.db`); only what one request needs is sent, and only
  to the provider you chose. The key is encrypted with Windows DPAPI.
- A "memory book" window lists what she knows (core, facts, episodes): edit, pin, delete, forget everything.
- Optionally she mentions when she notes something important ("I'll remember your exam is Friday").

### Order of work

1. **Talk:** provider settings (base URL, key, model), chat box on click, streamed replies with stage tags,
   canned lines as fallback, conversation log, persona and core blocks (edited by hand at first).
2. **Remember:** upkeep (episodes, facts, reconcile, core rewrite), keyword search, memory book.
3. **Remember better:** local embeddings (after a spike in the export), forgetting curve, weekly digests,
   character card + lorebook import.
4. **Later:** agent tools (calendar, reminders) through function calling, possibly on Agent Framework;
   text-to-speech, then voice-to-voice.

## Open questions for the user

1. Facts about you: shared by every character, or does each character get to know you separately?
2. Should she say out loud when she notes something, or remember silently (the memory book always shows it)?
3. Ship the small local embedding model (about 16 MB plus the ONNX runtime) in step 3, or rely on keyword
   search only?
