# Companion agent: talking hands-free, tools and her body (research note, 2026-10-05)

**Question:** how do today's voice agents handle turn-taking and tools, and what does that mean for her? This note prepares a design discussion; nothing is decided yet.

How sure each claim is: **read** means taken from the linked page; **measured** means tried in this project; **untested** means not tried here yet.

## Where we start

- **Listening is push-to-talk.** The player holds the talk key, holds the mouse on her, or uses the slip's microphone.
  - SenseVoice hears a sentence in about 0.1 s (*measured*).
  - The Silero voice detector is in the same package and was measured, but is not used yet.
- **She speaks with GPT-SoVITS,** one sentence at a time. The first audio of a sentence arrives 1.0–1.5 s after it is sent (*measured*). A click cuts her off.
- **She acts through stage directions in her reply:** `[note: …]`, `[plan: …]`, `[remind: …]`, `[cheer]`. These are not tool calls: nothing comes back to her.
- **Already decided** ([voice note](2026-10-02-companion-voice.md)): a **cascade**. Speech-to-text, then her own conversation, then her own voice. No realtime speech-to-speech service, because those skip her memory and cannot use her voice.

## 1. Is the cascade still right?

A [2026 review](https://ai.ksopyla.com/posts/voice-to-voice-models-2026-review/) (*read*) describes three designs:

| Design | Strength | Weakness |
|---|---|---|
| **Full duplex** (one model hears and speaks at once: Moshi, NVIDIA PersonaPlex) | about 200 ms responses; natural overlap and "mm-hm"s | weaker at following instructions; little context |
| **Thinker-talker** (Qwen-Omni) | near duplex, with tools | its own voice |
| **Cascade** | unlimited context, any model, tools | turn by turn, and slower |

[PersonaPlex](https://arxiv.org/html/2602.06053) is a 7B model that can clone a voice. It would also need the graphics card that the player's games and GPT-SoVITS use.

**Conclusion: the cascade stays.** The work is making it *feel* natural.

## 2. Knowing when the player has finished

- **The old way** is a voice detector plus a fixed silence. It either cuts in on a thinking pause or waits too long.
- **Current agents** add an **end-of-turn model** at each pause. The model judges from the voice itself (wording and intonation) whether the speaker is done. If not, the agent waits longer, up to a limit.
  - Examples: [LiveKit's turn detector](https://docs.livekit.io/agents/logic/turns/) and [Pipecat Smart Turn](https://huggingface.co/pipecat-ai/smart-turn-v3).
  - LiveKit can also adapt how long it waits to each speaker's own pauses (*read*).
- **For us: Smart Turn v3** ([announcement](https://www.daily.co/blog/announcing-smart-turn-v3-with-cpu-inference-in-just-12ms/), *read*):
  - an 8 MB ONNX model, BSD-2 licence;
  - 23 languages, including Chinese and Japanese;
  - about 12 ms on a CPU;
  - it runs on the ONNX Runtime we already ship.

  It would sit behind the Silero detector. *Untested:* how well it does on the player's own voice, and on Japanese.

## 3. Being interrupted

- **What agents do** ([LiveKit](https://docs.livekit.io/agents/logic/turns/), *read*):
  - stop speaking when the user starts;
  - keep in the conversation only the part of the reply the user actually heard, so the agent does not think it said the rest;
  - treat a short "mm-hm" as no interruption (a minimum length or word count), and carry on after a false start.
- **For us:**
  - her voice knows how far she got;
  - her line is logged up to there, marked as cut off.
- **The hard part is echo.** On speakers, her voice reaches the microphone, and she would interrupt herself. Options:
  1. **Headphones only** for interrupting by voice. On speakers, the key or a click still cuts her off.
  2. **Windows 11's own echo cancellation** ([sample](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/AcousticEchoCancellation/README.md), *read*). A microphone opened for communications gets "Voice Clarity": echo cancellation against what the speakers play, plus noise suppression.
     - We would capture the microphone ourselves instead of through Godot.
     - *To check:* Windows may turn other sounds down (the player's game or music) while a communications stream is open.
  3. **WebRTC's echo canceller (AEC3)**, fed with her own audio as the reference. We have those exact samples. It needs a native library, and her playback must be matched to the microphone in time.

  [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber) uses the browser's version of option 3 to allow interruption without headphones (*read*).

  **I'd try option 2 first.** It needs nothing extra.

## 4. Starting a conversation without the key

- **Conversation mode.** The key or a click opens a conversation, which stays open while the back-and-forth goes on.
  - It closes after some seconds of silence.
  - The microphone is open only during it, with a visible meter.
  - The same applies when she speaks first (a reminder, a check-in): she then listens for the answer.
- **A wake word** ("Shino"). sherpa-onnx's [keyword spotter](https://k2-fsa.github.io/sherpa/onnx/kws/index.html) has a 3M-parameter Chinese–English model that takes custom keywords without training and runs on the CPU (*read*).
  - The microphone would then always be open, so it must be the player's choice.
  - False triggers are possible, and Japanese is not covered.

## 5. How fast she answers

| Step | Today (estimate) |
|---|---|
| Noticing the player has finished | 0.2–0.5 s |
| Speech to text | 0.1 s (*measured*) |
| Model, to the end of the first sentence | 0.6–1.5 s (depends on the provider) |
| GPT-SoVITS, to the first audio | 1.0–1.5 s (*measured*) |
| **Total** | **about 2–3.5 s** |

Voice agents aim for 0.3–0.8 s ([LiveKit](https://livekit.com/blog/async-tools-voice-agents), [SignalWire](https://signalwire.com/blog/truth-about-voice-ai-latency), *read*). Ways to bridge the gap:
- **Her body reacts at once:** a nod, a "thinking" look.
- **A short sound in her own voice** ("mm?", "えっと", "嗯"), made once when her voice loads and played straight away.
- **Speaking from the first clause,** not the first full sentence.
- **Starting the model before the turn surely ended.** This costs extra requests.

## 6. Tools

- **Today:** stage directions.
  - Good: they stream, work with any model, and cost no extra round trip.
  - Limit: they only go one way.
- **Real tool calls:**
  - Microsoft.Extensions.AI (already used for the chat) runs the loop: the model asks, we run the tool, the model continues.
  - Marking a tool "approval required" makes it stop and ask before running ([docs](https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/function-tools-approvals), *read*; open bugs exist around reordered messages, [#7156](https://github.com/dotnet/extensions/issues/7156)).
  - The official C# MCP SDK turns any MCP server's tools into the same kind of function ([docs](https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Client.McpClientTool.html), *read*). That would let a player plug in the same tools as in Claude Desktop. Hermes Agent and Open-LLM-VTuber both do this (*read*).
- **What voice changes** ([LiveKit](https://livekit.com/blog/async-tools-voice-agents), [Stream](https://getstream.io/blog/speculative-tool-calling-voice/), *read*):
  - A tool call costs one more trip to the model. She should say something first ("let me look"), or her body should show it.
  - Slow work runs in the background: she says she's on it, and tells the result once it's ready and the player is free.
  - A tool that changes something must never run twice when she is interrupted.
- **Caching:** the tool list sits at the start of every request, the part providers cache. It must stay the same from turn to turn.
- **Permission.** Hermes asks before commands that match a list of dangerous patterns, with modes from manual to off ([security](https://hermes-agent.nousresearch.com/docs/user-guide/security), *read*). The spec already says:
  - reading needs no permission;
  - acting inside the app needs none;
  - acting outside the app asks first: in her words, with a ticket offering Allow / Deny.
- **Models:** hosted models handle tools well; small local ones less reliably. Directions stay as the fallback.

**Abilities to brainstorm** (a starting list, not a plan):
- **Reading:** web search (a provider's built-in search, or a search API), the weather, what is on screen (only on request), files the player gives her.
- **Inside the app:** timers and focus sessions on the cabinet, her settings, the memory book.
- **Outside the app, after asking:** writing to the calendar (needs Google or Microsoft sign-in), opening a link or an app, media keys, sending a message to the player's phone, MCP servers.

## 7. Her body during a conversation

- **Listening:** she turns to the player and nods softly at their pauses (the voice detector says when).
- **Waiting for the model:** a thinking look, or her pencil and pad.
- **Speaking:** a gesture per sentence, not one per reply. Lip sync from loudness is fine for now; mouth shapes per sound could come later.
- **Interrupted:** she stops mid-gesture and looks at the player.
- **References** (*read*):
  - [AIRI](https://github.com/moeru-ai/airi): VRM and Live2D, blinking and gaze, voice detection, speech-to-text, a model and a voice; on Steam.
  - [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber): the backend maps emotions to expressions; MIT licence.

## To decide together

1. **How a conversation starts:**
   - conversation mode from the key or a click (my suggestion to start with);
   - a wake word as an option later.
2. **Interrupting her on speakers:** spike Windows' echo cancellation first. If it falls short, interrupting by voice works only with headphones.
3. **Tools:**
   - a set of our own first, each with its permission rule;
   - MCP later, as an advanced setting.
4. **Which abilities first:** the brainstorm.
5. **Bridging the wait:** is a short sound in her own voice welcome, or should only her body bridge it?

## A possible order, once decided

1. **Spike:** Windows echo cancellation, Silero and Smart Turn v3 on the player's own voice and speakers (measure missed and false turns).
2. **Conversation mode:** being interrupted, and her line cut to what was heard.
3. **Her body** while listening, thinking and being interrupted.
4. **Tools:** function calls with approval through her and the ticket, and the first few abilities.
5. **More abilities, or MCP,** from the brainstorm.
