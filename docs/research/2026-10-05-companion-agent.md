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

**Measured, 2026-10-05.** The test setup:
- the cabinet, hidden and muted;
- the test voice (hana, a trained v2ProPlus voice, which streams) on GPT-SoVITS on the laptop's graphics card;
- a recorded question heard as if said with the talk key, three times for each kind of reply;
- a mock model that answers after 0.6 s, as a fast provider does, at about 60 tokens a second.

| Step (after the question was heard) | Japanese spoken, English subtitle | Japanese only |
|---|---|---|
| Speech to text (4.2 s of speech) | 0.10–0.12 s | 0.10–0.12 s |
| The model's first word (the mock's 0.6 s) | 0.61–0.77 s | 0.61–0.72 s |
| The first sentence to speak, complete | 0.91–1.19 s | 0.75–0.84 s |
| GPT-SoVITS's first audio of it | +0.55–0.85 s | +0.55–0.66 s |
| **Her first sound heard** | **1.49–1.85 s** | **1.34–1.52 s** |

What this means:
- **Faster than estimated.** With push-to-talk and a fast provider, she answers in about 1.5–2 s from the end of the player's speech. Hands-free will add the wait for the end of the turn (0.2–0.5 s).
- **The provider is now the biggest part,** its first word. A real one varies from about 0.3 s to over 2 s with model and load. GPT-SoVITS comes next, at about 0.6 s; the earlier 1.0–1.5 s was a v4 voice, which doesn't stream.
- **A shorter first sentence saves little.** "おかえり！" alone came about 0.15 s sooner than a two-sentence opening, and GPT-SoVITS took about as long for it. So speaking from the first clause would save only that much. Asking her to open with a short word or interjection would gain the same, free.
- **Only something that doesn't wait for the model** can hide the rest: her body reacting, or a short sound in her own voice.

The app now logs each spoken reply's "first sound was heard N ms after asking".

**To check:** in one run, the first 2.4 s of her audio was nearly silent (her mouth never opened before a cut). GPT-SoVITS may sometimes start a line with a long silence. That would add to the delay the player feels, and "first sound" counts it as sound.

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

What people do, from studies of real conversations (*read*):

- **Thinking of an answer, people look away.** Andrist and others recorded people talking and measured these "cognitive" glances. They start about 1.3 s before the answer begins and end about 2.2 s into it, lasting about 3.5 s. They go up more often than any other way (39%, against 29% down and about 31% to the side). A robot that looked away at those moments was rated more thoughtful, and people waited longer before asking again.
- **Speakers look away more than listeners.** Kendon (1967) found that over 70% of turns start with the speaker looking away, and over 70% end with them looking at the listener: the look back hands the turn over. A listener looks at the speaker in long stretches. Glances aside for intimacy last about 1.1 s every 7 s when listening, and 2 s every 5 s when speaking.
- **Listeners nod in the speaker's pauses.** Rates go from about 3.5 to 9 nods a minute. One motion-capture study of Japanese attentive listening found:
  - about half the nods are a single dip (51%), a third double (33%), the rest three or more;
  - each dip is about 4° and under a second;
  - about a quarter start with a small lift.
  Later cycles of a nod get smaller. Timing is what counts. Gratch's rapport agent nodded in time with the speaker and built rapport; the same nods at random moments did not.
- **Gestures land with stress.** A gesture's stroke comes just before or with the stressed syllable, never after (McNeill). The head does the same in speech.
- **Cut off, a speaker who yields stops and lets the gesture go.** Holding it means "I'm not done".
- **Waiting for a slow answer:** an agent that bridged the delay with its body (looking away, a thinking gesture) seemed faster, more present and more natural than a progress badge or thinking bubble. Most people preferred it.
- **Grown-up movement.** The usual dimensions of expressive movement are how big, how fast, how fluid, how strong, how often repeated, and how much in all (Hartmann, Mancini, Pelachaud). Bigger, faster, more frequent gestures read as more extraverted (Neff). Big symmetric moves repeated over and over, like bouncing, kicking or both arms up, read as a child. Animators also avoid "twinning", both sides moving alike: it looks mechanical.

**Built (2026-10-05):**
- **Listening:** she leans in with a slight tilt and keeps her eyes on the player, glancing aside now and then. She nods in their pauses: after at least 1.2 s of speech and 0.3 s of quiet, at most one nod every 2.5 s and not at every pause (`Backchannel`, from the microphone's loudness; Silero would do better in a noisy room). When they finish, a "got it" nod.
- **Thinking:**
  - a beat on the player, then away, up or level, to her own side;
  - she alternates looking away for 3 to 4 s with a brief look back;
  - after about 2.4 s her right index finger goes to her chin, placed from her eye bones so it fits any head;
  - she starts answering still looking away for a moment, then looks at the player.
- **Speaking:** her head dips where her voice swells and tilts to one side, then the other, phrase by phrase. She glances aside for a second or two every few seconds. No fidgets while she talks, listens or thinks.
- **Cut off:** she stops mid-gesture, gives a small start (a blink, her head drawn back) and looks at the player.
- **Celebrating, made grown-up.**
  - *Before:* the cheer had a crouch, both arms up, legs kicking and bouncing on the seat.
  - *Now:* a small "oh", then applause at her chest, a warm smile rather than a laugh, hands held together a moment, a nod.
  - The first version clapped flat palm to palm, and the user found it looked like "two hands hitting each other".
    - Most people clap with the hands at an angle (Repp, 1987): one hand waits, the other comes down across it.
    - Now her left palm waits turned up, and her right does the moving: it opens quickly off the rebound and strikes faster and faster, the hand trailing the forearm.
    - The claps are four, uneven: the first slower and bigger, the rest dying away.
    - Each clap goes through her body: a dip of the shoulders and a bob of chest and head just after the hands meet.
  - The fist pump stops at her shoulder instead of above her head.
- *Measured* only by eye, on clips from `tools\dev\clip.ps1`. They are in `<tools>\voice-trial\clips-body`.

**Not yet:** a gesture per sentence while she speaks (hands); mouth shapes per sound; nods from a real voice detector.

**Motion files instead of hand-made moves** (surveyed 2026-10-05, *read*). Procedural moves fit any character and mix with gaze, breathing and springs, but lack the small irregularities of a real performance. The usual answer is recorded motion underneath, with procedural layers on top. That needs a clip layer: play an upper-body motion file over her seated pose. godot-vrm can't read `.vrma` yet; it is glTF, and its rotations match how `Puppet` already poses bones. Where motions could come from:

| Source | Seated clap? | Licence for a Steam release | Notes |
|---|---|---|---|
| [Kimodo](https://github.com/nv-tlabs/kimodo) (NVIDIA, 2026) | Generated from text | SOMA models: NVIDIA Open Model License, commercial use | Local; under 3 GB of graphics memory with the text encoder on the processor. Up to 10 s. Linux first, Windows via Docker. Its skeleton would need mapping to hers |
| [Text-To-VRMA](https://booth.pm/ja/items/8617663) (BOOTH, free) | Generated from text | The author says commercial use is fine, no credit needed | A Windows app around NVIDIA's motion model; writes `.vrma`. A third party's program |
| [Mixamo](https://www.mixamo.com) | "Sitting Clap", "Sitting Laugh", "Sitting Talking" | Allowed in a game; the raw files can't be redistributed (a problem if the repo goes public) | Needs an Adobe account |
| Webcam motion capture ([Rokoko Vision](https://www.rokoko.com/products/vision), [FreeMoCap](https://freemocap.org)) | Whatever the user performs | The user's own | Rokoko Vision 3.0 has no fingers |
| [VRoid's 7 free VRMA](https://vroid.booth.pm/items/5512385) | No (standing poses) | Commercial, with credit to pixiv | |
| [Quaternius UAL](https://quaternius.com/packs/universalanimationlibrary.html) | None found | CC0 | Game-style moves |
| CMU motion capture | None found | Any use | Older data |
| Bandai Namco Research dataset | — | Non-commercial only: ruled out | |

**Other companions** (*read*):
- [AIRI](https://github.com/moeru-ai/airi): VRM and Live2D, blinking and gaze, voice detection, speech-to-text, a model and a voice; on Steam.
- [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber): the backend maps emotions to expressions; MIT licence.

Sources:
- [Andrist, Tan, Gleicher, Mutlu: Conversational Gaze Aversion for Humanlike Robots (HRI 2014)](https://pages.cs.wisc.edu/~bilge/pubs/2014/HRI14-Andrist.pdf)
- [Rossano: Gaze in Conversation (Kendon 1967, Argyle and Cook 1976 summarised)](https://www.eva.mpg.de/documents/Wiley-Blackwell/Rossano_Gaze_Handbook-of-conversation-analysis_2012_1658279.pdf)
- [The Role of Eye Gaze in Regulating Turn Taking (Frontiers in Psychology, 2021)](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2021.616471/full)
- [Kato and others: Real-time Generation of Listener Nodding via Prediction of Kinematic Parameters (ICMI 2026)](https://arxiv.org/abs/2607.12329)
- [Structure of nods in conversation (PLOS One)](https://journals.plos.org/plosone/article?id=10.1371%2Fjournal.pone.0323448)
- [Head motion synchrony in verbal communication (PLOS One)](https://journals.plos.org/plosone/article?id=10.1371%2Fjournal.pone.0286098)
- [Gratch and others: Creating Rapport with Virtual Agents (IVA 2007)](https://people.ict.usc.edu/~nwang/PDF/GratchIVA07-rapport.pdf)
- [The temporal relation between beat gestures and speech](https://www.researchgate.net/publication/233275273_The_temporal_relation_between_beat_gestures_and_speech)
- [Gestures in overlap: the situated establishment of speakership](https://d-nb.info/1255182490/34)
- [Behavioral and Symbolic Fillers as Delay Mitigation for Embodied Conversational Agents (2025)](https://arxiv.org/abs/2508.11781)
- [Hartmann, Mancini, Pelachaud: Implementing Expressive Gesture Synthesis for Embodied Conversational Agents](https://dl.acm.org/doi/10.1007/11678816_22)
- [Neff and others: Evaluating the Effect of Gesture and Language on Personality Perception in Conversational Agents](https://dl.acm.org/doi/10.5555/1889075.1889103)
- [Animation Mentor: Twinning and why you should (usually) avoid it](https://www.animationmentor.com/blog/twinning-and-why-you-should-usually-avoid-it/)
- [Repp: The sound of two hands clapping (1987)](https://pubmed.ncbi.nlm.nih.gov/3571727/), and [Inferring the hand configuration from hand clapping sounds](https://www.researchgate.net/publication/228531719_Inferring_the_hand_configuration_from_hand_clapping_sounds)
- [Kimodo model card (SOMA, NVIDIA Open Model License)](https://huggingface.co/nvidia/Kimodo-SOMA-RP-v1.1)
- [Mixamo FAQ: licensing](https://community.adobe.com/t5/mixamo-discussions/mixamo-faq-licensing-royalties-ownership-eula-and-tos/td-p/13234775)
- [VRoid: VRM Animation and 7 free motions](https://vroid.com/en/news/6HozzBIV0KkcKf9dc1fZGW)
- [godot-vrm: VRMA support (open issue)](https://github.com/V-Sekai/godot-vrm/issues/77)
- [Free motion capture libraries and their licences (CG Channel on Bandai Namco's)](https://www.cgchannel.com/2022/05/download-3000-free-mocap-moves-from-bandai-namco-research/)

## The spike, before any recording (2026-10-05)

The test program is in `<tools>\agent-trial` (README there). What it found on this PC without recording anything:

- **Echo cancellation is available.** A microphone stream opened for communications gets Windows' acoustic echo cancellation, noise suppression, automatic gain and deep noise suppression. A plain stream, the way the cabinet records now, gets noise suppression only (*measured*: asked of Windows, stream never started).
- **Ducking.** Windows is set to its default, "reduce other sounds by 80%" during communications. The session measures whether opting out of ducking keeps the player's game or music at full volume.
- **Telling headphones from speakers.** The output device here reports itself as "speakers" whatever it is. Detecting headphones from Windows can't be relied on.
- **Smart Turn ported to C#.** The features match transformers' to 0.000025.
- **Use the full-precision build.** The 8 MB build is 8-bit, and its borderline answers shift with the runtime: 0.43 to 0.81 on the same input across ONNX Runtime versions and optimisation levels. The 32 MB full-precision build gave 0.874 in every runtime and at every level.
  - Speed on this CPU, features included: about 54 ms a decision, against 40 ms for the 8-bit build.
  - Pipecat calls this build "gpu", but it runs fine on a CPU.
- **Synthetic speech misleads it.** On Windows' voices, Smart Turn often took a mid-sentence pause for the end, in Chinese and Japanese above all: each part is synthesised on its own and ends in a falling tone. Its own benchmark gives Chinese 86% accuracy, against English 94% and Japanese 96%. The player's recordings decide.

## The spike's recording (2026-10-06)

The user recorded the session (`<tools>\agent-trial\recordings\20261006-2119`, results in its `results.txt`). Their setup: earphones (which Windows calls "speakers"), English only.

**Her voice into the microphone** (*measured*): about −97 dB on both the plain and the communications stream, which is silence. Through earphones she can't hear herself, so she can't interrupt herself. Windows' echo cancellation isn't needed on this setup. (Her voice measured −26 dB at the output, so it was playing.)

**Talking over her** (*measured*, four tries on each stream): the user's voice was heard 0.70–0.86 s after the cue, their reaction time included. No false starts on the communications stream; one on the plain stream. **Interrupting her by voice is feasible on earphones.** On speakers it is still unmeasured.

**Does a communications microphone turn other sounds down?** Not measured. The spike set the ducking preference on the capture session, which Windows refuses (`AUDCLNT_E_WRONG_ENDPOINT_TYPE`): it belongs on a playback session. On earphones the plain stream is enough, so this only matters for speakers.

**Knowing when they've finished** (*measured*, eleven turns, Smart Turn full precision; see the per-pause probabilities in `results.txt`):
- every real ending scored high (0.92–0.99), but one 0.66;
- thinking pauses mid-sentence mostly scored low (0.01–0.28), but three scored high: "…binary trees is …" 0.79, "let me see, um …" 0.90, and one of their own lines 0.89;
- so at the app's threshold of 0.5, three cut-offs in eleven turns. A fixed silence did no better (1.2 s: three cut-offs, and always 1.2 s of waiting).

**Changed** (2026-10-06): the threshold is now **0.95** (`SmartTurn.Sure`). On these turns: no cut-offs; most endings still end about a quarter second after the user stops; two (0.92, 0.66) wait for the 2 s quiet instead. Eleven turns is a small sample: worth looking at again after real use.

## To decide together

1. **How a conversation starts:** *decided 2026-10-05* (`docs/specs/2026-10-05-hands-free-listening.md`).
   - The talk key stays the default.
   - Follow-up listening and conversations are the player's options.
   - No wake word for now.
2. **Interrupting her by voice:** *measured 2026-10-06* on the user's earphones: no echo, and their voice heard promptly (above). On speakers still unmeasured. Building it waits on the user (whether they always use earphones with her).
3. **Tools:** *decided 2026-10-05* (`docs/specs/2026-10-05-tools-and-abilities.md`; groundwork built).
   - Built-in tools first, each with its permission (Allowed, Ask, Off).
   - Connectors the player chooses later, local MCP servers among them; no remote MCP.
4. **Which abilities first:** the brainstorm.
5. **Bridging the wait:** is a short sound in her own voice welcome, or should only her body bridge it?

## A possible order, once decided

1. **Spike:** Windows echo cancellation, Silero and Smart Turn v3 on the player's own voice and speakers (measure missed and false turns).
2. **Conversation mode:** being interrupted, and her line cut to what was heard.
3. **Her body** while listening, thinking and being interrupted (*built 2026-10-05*, §7).
4. **Tools:** function calls with approval through her and the ticket, and the first few abilities.
5. **More abilities, or MCP,** from the brainstorm.
