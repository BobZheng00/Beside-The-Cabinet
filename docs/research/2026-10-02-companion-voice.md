# Companion voice — research note and proposal (2026-10-02)

Question: how should she speak aloud, and later listen? Status of claims: **read** = taken from the linked page;
**untested** = not tried in this project yet.

## Decided (from the player, 2026-10-02)

- **Players recreate characters.** As with 3D models, players recreate anime and game characters, so the voice
  must be that character's. They either **download a voice model someone else made** or, if they can, **clone
  one themselves**. This is the main path.
- **No lower-quality voices.** Windows voices and small fixed-voice models don't suit the program. Without a
  proper voice she stays **text-only**.
- **Cloud is secondary.** A player may bring a key for ElevenLabs or another platform we choose to support.
- **Order.** Speaking comes first; listening (voice-to-voice) follows.
- **Languages and speed.** English, Chinese and Japanese speech, low latency, streamed.

Constraints carried over from [../specs/2026-10-02-companion-helper.md](../specs/2026-10-02-companion-helper.md):
- She stays quiet (events, clicks, an occasional remark, a tray mute).
- Replies stream.
- Stage tags like `[cheer]` are not spoken.
- The development laptop has an 8 GB laptop graphics card, and **the same GPU
  runs the player's games**.

## Short answer

**GPT-SoVITS is the voice engine.** Nothing else comes close for recreating characters:
- **Community.** Nearly every downloadable anime and game character *speech* model is a GPT-SoVITS model. It's the
  open format for character voices, as VRM is for character bodies.
- **Clone it yourself.** The same engine clones from a 5-second clip with no training, or from about a minute of
  audio with training. That covers "if they can, clone one themselves".
- **Languages.** It speaks English, Chinese, Japanese, Korean and Cantonese, and it streams. It's MIT-licensed.

**The app runs it as a separate local program, the way Ollama serves chat models.**
- The player either points the app at an existing GPT-SoVITS install (people who make voices already have one)
  or lets the app download the official Windows package.
- The app starts it hidden when she needs a voice, loads the character's weights through its API, and stops it
  when it isn't needed.
- Running it inside our process isn't realistic yet (see "Running GPT-SoVITS").

**A voice belongs to a character.** It lives in the character's folder and is added by dropping it on the cabinet,
like a model or a card.

**Cloud keys are the second path.** Fish Audio's library is full of character voices and it works from mainland
China. ElevenLabs has the best quality and latency.

## How a voice is made

| Kind | What the player has | Engines |
|---|---|---|
| Downloaded, trained | GPT weights (`.ckpt`) + SoVITS weights (`.pth`), plus one or more reference clips with their transcripts | GPT-SoVITS |
| Cloned from a clip | A 3–10 s clip of the character and its transcript, no training | GPT-SoVITS; also Qwen3-TTS, CosyVoice 3, Fish, ElevenLabs |
| Trained yourself | A minute or more of the character's lines → trained in the GPT-SoVITS WebUI → the same two files | GPT-SoVITS |
| Cloud voice | A voice id from a library, or a clone made on the platform | Fish Audio, ElevenLabs |

Trained models still need a **reference clip** at synthesis time. The clip sets the tone, so community packs often
ship several clips labelled by emotion. AI Hobbyist's inference package lets you
["select different emotional reference audio"](https://github.com/AI-Hobbyist/GPT-SoVITS-Inference/blob/main/docs/cn/README.md)
to steer emotion. That fits our stage tags: `[worry]` can pick the sad clip and `[cheer]` the happy one.

## Where voices come from (read)

| Source | What's there | Format | Terms |
|---|---|---|---|
| [AI Hobbyist](https://www.ai-hobbyist.com/) | Full casts of several popular games | GPT-SoVITS v2 / v3 / v4; reference clips come separately | "No redistribution, no commercial use"; training audio belongs to the game makers |
| Hugging Face, [voice-models.com](https://voice-models.com/), the AI Hub Discord, Bilibili, ModelScope | VTubers, anime, whole franchises | GPT-SoVITS, and many more RVC | mostly trained on game or stream audio without consent |
| [AivisHub](https://note.com/aivis_project/n/nd689f3f45dae?hl=en) | Japanese voices with a licence in each file (CC0, ACML, ACML-NC, custom) | Style-Bert-VITS2 `.aivmx` | licensed |
| [Fish Audio](https://fish.audio/developers/) library | 2M+ uploaded voices, many characters | cloud voice id | the platform's terms |
| ElevenLabs Voice Library | 11k+ voices shared by their owners | cloud voice id | consented |

**RVC** is the biggest pool by count (voice-models.com lists 30,000+; [weights.gg closed on 1 April
2026](https://nicevois.com/weights-gg-shutdown)). But RVC changes one voice *into* another: it needs a separate
voice to read the text first, which adds a stage and latency. It suits singing better than conversation. We leave
it out for now; a GPT-SoVITS clone from a few seconds of the same character gets most of the way there.

**Law is moving here.** In a ruling reported on 1 October 2026, the Tokyo District Court [recognised a right to
one's own voice](https://www.technology.org/2026/10/02/tokyo-court-voice-publicity-rights-ai-clone-kenjiro-tsuda/)
in an AI-clone case. China's Beijing Internet Court reached a similar conclusion in 2024. Our policy follows the
one for 3D models:
- **Bundled:** none (or only voices we have the rights to).
- **Imported:** the player may import anything for personal use.
- **Shown:** a model's licence or source note, when it has one.
- **Linked from the app:** only sources that carry licences or handle consent.
- **Not done:** we don't host or redistribute anyone's voice.

## Running GPT-SoVITS from the app (read)

| Option | Versions | Size | Speed (as published) | Verdict |
|---|---|---|---|---|
| **Official package, run by the app** (`api_v2.py`) | all: v1–v4, v2Pro, v2ProPlus | [8.2 GB](https://huggingface.co/lj1995/GPT-SoVITS-windows-package) Windows package (includes training tools) | v2ProPlus RTF 0.028 on RTX 4060 Ti ([issue](https://github.com/RVC-Boss/GPT-SoVITS/issues/2821)); streams (`streaming_mode`); `/set_gpt_weights` and `/set_sovits_weights` switch voices | **Start here.** Every community model works, and training comes in the same package |
| [GSV-TTS-Lite](https://github.com/chinokikiss/GSV-TTS-Lite) | v2, v2Pro, v2ProPlus | Python + PyTorch | 133 ms first audio, 0.8 GB VRAM (RTX 3050 Laptop) | Faster and lighter, but no v3/v4, and some game packs are v4 |
| [Genie-TTS](https://github.com/High-Logic/Genie-TTS) | v2, v2ProPlus (converted to ONNX, conversion needs PyTorch) | ~200 MB runtime | ~1.1 s first audio on CPU | A CPU route; no GPU, no v3/v4 |
| Native ports: [GPT-SoVITS-cpp](https://github.com/GPT-SoVITS-Devel/GPT-SoVITS-cpp) (C++, ONNX/TensorRT), [gpt-sovits-rust-cpu](https://github.com/ldqm1/gpt-sovits-rust-cpu), [DragonianVoice](https://github.com/PriesiaMioShirakana/DragonianVoice) (has C# bindings) | v2 / v2ProPlus | small | cpp: 0.46 s first audio on RTX 2080 Ti with TensorRT | Early; partial language support (rust: Chinese only); every model must be converted with Python first |
| Our own port to ONNX Runtime in C# | what we port | small | — | Later, if size or startup becomes the problem. The hard parts are converting weights without PyTorch and the text front end (Chinese polyphones, Japanese accent, English) |

So **the official runtime runs beside the app** to begin with.

## Measured on the development laptop (2026-10-02)

**Set-up.**
- **Hardware:** an 8 GB laptop graphics card, on mains power. About 2.4 GB of graphics memory is
  already used by the desktop, a wallpaper engine and browsers.
- **Runtime:** the official package `GPT-SoVITS-v2pro-20250604` (7.6 GB download, 14 GB unpacked) running
  `api_v2.py`.
- **Voices:** base weights only, cloned zero-shot from 5 s public-domain LibriVox clips (English, Japanese,
  Chinese).
- **Lines:** short companion lines of about 3–4 s, one request each.
- **Speed:** "Speed" is generation time divided by audio length; below 1 means faster than real time.
- **Scripts:** in `<tools>\voice-trial\scripts`.

**Quiet GPU:**

| | v2ProPlus | v4 | GSV-TTS-Lite (v2ProPlus) |
|---|---|---|---|
| Start (server up / first line ready) | 14–30 s / +6 s | +13 s first line | 56 s (in-process) / +6 s |
| Graphics memory, loaded / while speaking | +2.0 GB / +3.3 GB | +3.8 GB / +3.9 GB | +2.4 GB |
| First audio, streaming (`streaming_mode` 3 / 2 / 1) | 0.27 / 0.41 / 0.43 s median, ~0.55 s worst | 1.4–1.5 s | 0.18 s median (one 3.9 s outlier) |
| Speed (generation ÷ audio length) | 0.29–0.31 | ~1.0 (too slow to stream safely) | 0.18 |
| Swap a voice's weights | 1.7 s | 2.9 s | — |

**Beside a game:**
- **The player's real game** (Graveyard Keeper 2 in the foreground):
  - v2ProPlus: first audio about 0.9 s, speed 0.8–1.0. Borderline: gaps between chunks are likely.
  - v4: one line didn't finish in 2 minutes.
- **A load scene standing in for a game** (Godot, uncapped, GPU-bound at about 200 fps, little memory):
  - The game alone: 198 fps whether the voice server was loaded or not.
  - While she spoke: 145 fps (5.0 → 7.1 ms median frame, no hitches).
  - The voice under that load: first audio 1.1 s, speed 0.8.
- **The same scene with 2 GB more textures** (total graphics memory over 8 GB):
  - The game fell to **17 fps with the voice model merely loaded**, and 8 fps while she spoke.
  - The voice took about 13 s per line.
  - Running out of graphics memory is the real danger, not compute.
- **Windows background throttling:** exempting the server made no difference under load. On a quiet GPU, runs
  varied between 0.41 and 0.95 s to first audio either way. Exempting it is cheap, so the app does it anyway.

**Intelligibility.** Each line was synthesised once, transcribed back with SenseVoice, and scored as the share of
characters the transcript got wrong. Single samples are noisy: the same cell moved by up to 0.25 between runs.

| Clip → speaking | v2ProPlus run 1 (game running) | v2ProPlus run 2 (quiet) | v4 | Lite |
|---|---|---|---|---|
| en → en / ja / zh | 0.00 / 0.06 / 0.08 | 0.00 / 0.09 / 0.12 | 0.05 / 0.06 / 0.12 | 0.02 / 0.15 / 0.12 |
| ja → en / ja / zh | 0.19 / 0.09 / 0.12 | **0.44** / 0.06 / 0.16 | 0.12 / 0.06 / 0.04 | 0.05 / 0.18 / 0.28 |
| zh → en / ja / zh | **0.31** / 0.06 / 0.04 | 0.12 / 0.09 / 0.12 | 0.00 / 0.06 / 0.04 | 0.27 / **0.32** / **0.44** |

**What the numbers say:**
- **Language.** A voice is clearest in its own language. English from a Japanese or Chinese clip is the most
  error-prone pairing, and models trained on one language will drift further than these base weights. That
  supports fixing each voice to its model's language.
- **Lite dropped words.** It left out the first sentence in three of the Chinese-clip lines.
- **What she uses.** v2ProPlus is the voice for conversation on this class of laptop. v3/v4 community voices
  load and sound good, but at about real time they can only speak whole sentences, with about 1.5 s or more
  before each.

**Rules that follow:**
- **While a game runs, stop the voice server.** That gives back 2–3 GB of graphics memory. She is text-only then,
  or uses a cloud voice if the player set one up.
- **After the game ends, restart the server in the background.** It takes about 20 s, and she is text-only until
  it's ready.
- **One sentence per request,** with `streaming_mode` 2.
- **v3/v4 quality: 16 sampling steps by default** (decided 2026-10-03 after listening). Measured with the
  community v4 voice and with a zero-shot clone:

  | Steps | First audio | Speed | How it sounded |
  |---|---|---|---|
  | 32 | 1.4–2.3 s | about 1.0 | best |
  | 16 | about 0.9 s | 0.64 | **default** |
  | 8 | 0.6–0.8 s | 0.43 | metallic, robotic |
  | 4 | 0.4 s | 0.34 | |

  Speech recognition misheard about the same share of characters at every step setting: the steps change how
  natural she sounds, not whether she's understood. The voice settings offer 16 (balanced), 32 (best) and 8
  (faster, can sound metallic).
- **Launch the server hidden** (`CREATE_NO_WINDOW`) with Windows background throttling switched off for it.

**v4 doesn't stream (found 2026-10-03).** GPT-SoVITS streams only models without a separate vocoder (v1, v2, v2Pro,
v2ProPlus). For v3/v4 it prints "SoVits V3/4模型不支持流式推理模式" and falls back to returning each piece between
punctuation once it is whole (`TTS.py`). In the demo, a reply's first sentence "今は10月3日土曜日の午後3時5分だよ。"
took 2.8 s to first sound: 1.1 s predicting the speech, 1.6 s synthesising. Hana cloned from the same 6 s clip
with both base models (`bench_hana_models.py`, a laptop graphics card, desktop apps open):

| Line | v4, 16 steps: first audio / misheard | v2ProPlus: first audio / misheard |
|---|---|---|
| おかえりなさい！今日はよく頑張りましたね。… (9–11 s) | 1.54 s / 5% | 1.14 s (first after loading) / 7% |
| ハードを二問も解いたんですか？… | 2.76 s / 7% | 0.41 s / 29% ("二問も解いた" mangled, トークン → 当館) |
| もう遅いですよ。続きは明日にして、… | 2.07 s / 7% | 0.43 s / 0% |
| 今は10月3日土曜日の午後3時5分だよ。 | 3.52 s / 0% | 0.44 s / 26% (numbers written as kanji: spoken right) |

Inside the cabinet, v2ProPlus started each line 0.25–0.67 s after it was handed over. v2 models also come out
about half as loud as v4, so her mouth now follows loudness relative to the voice's own recent peak. A clone's
voice settings offer both base models; v4 stays the default until the player has compared them.

## Other engines, for reference (read; not planned)

Some newer zero-shot models clone from a clip as well as GPT-SoVITS does:
- **[Qwen3-TTS](https://github.com/QwenLM/Qwen3-TTS)** (Apache, Jan 2026, 10 languages, 3 s clone, fast streaming).
- **[Fun-CosyVoice 3](https://tongyilab.substack.com/p/fun-cosyvoice-3-major-upgrade-and)** (Apache, ~150 ms first
  audio).
- **[Chatterbox Multilingual](https://www.resemble.ai/learn/models/chatterbox-multilingual)** (MIT).
- **[IndexTTS 2.5](https://huggingface.co/IndexTeam/IndexTTS-2.5)**.
- **[Fish Audio S2](https://fish.audio/s2/)** (4.4B; too big next to a game).

Checked again 2026-10-03, for what would run on the development laptop:
- **Qwen3-TTS through [faster-qwen3-tts](https://github.com/andimarafioti/faster-qwen3-tts)** (MIT; CUDA graphs, no
  FlashAttention, native Windows setup, streams): 0.41 s first audio and 2.26× real time for 0.6B on an RTX 4060.
  The official package is slow without FlashAttention 2, and vLLM-Omni has no online serving yet. Its Base models
  can be fine-tuned, and its cross-lingual cloning
  [beats CosyVoice 3](https://arxiv.org/html/2601.15621v1). **The one to try first.**
- **[dots.tts](https://github.com/rednote-hilab/dots.tts)** (rednote, June 2026, Apache, 2B, 24 languages, 48 kHz):
  85 ms first audio streaming on the authors' hardware; heavier, and no Windows notes.
- **IndexTTS 2.5** (Japanese added) needs [about 6 GB](https://huggingface.co/IndexTeam/IndexTTS-2.5), doesn't
  stream, and has bilibili's own licence.
- **Qwen-Audio-3.0-TTS** (July 2026) is [hosted only](https://openrouter.ai/qwen/qwen-audio-3.0-tts-plus).

None of them has a community of trained character models, so each would only be another way to "clone from a
clip". Worth adding later if one clearly beats GPT-SoVITS's zero-shot quality. Most expose an OpenAI-style
`/v1/audio/speech` server (via [vLLM-Omni](https://github.com/QwenLM/Qwen3-TTS), Speaches and others), so one
generic engine kind would cover them.

Not planned, per the decision: Windows voices, Kokoro, Supertonic, Piper, Pocket TTS (fixed or weak voices).

## Cloud (read; secondary)

| Service | EN/ZH/JA | Character voices | First audio | Price | From mainland China |
|---|---|---|---|---|---|
| [Fish Audio](https://fish.audio/developers/) | ✓✓✓ (80+) | 2M+ community voices; clone from 15 s | <150 ms, WebSocket | [$15 per million UTF-8 bytes](https://texttolab.com/blog/fish-audio-pricing) (a Chinese character is 3 bytes) | works |
| [ElevenLabs](https://elevenlabs.io/docs/overview/models) Flash v2.5 | ✓✓✓ (32) | Voice Library; instant and professional clones | ~75 ms model | subscription, highest | [proxy needed](https://yangmao.ai/en/providers/elevenlabs/china-access/) |
| Others, reference: [MiniMax](https://www.together.ai/blog/minimax-speech-2-6), [Volcengine](https://developer.volcengine.com/articles/7631415579070136370), Alibaba (Qwen3-TTS / CosyVoice) | ✓✓✓ | 5–10 s clones, catalogue voices | 0.1–0.3 s | per character | native |

A quiet companion says about 36,000 characters a month. On Fish that's about $0.50 a month in English and about
$1.60 in Chinese.

## Proposal

**1. A voice file in the character folder.** `voice.json` holds:
- the engine (GPT-SoVITS or a cloud service);
- the weights;
- the reference clips, each with its transcript, language and an optional mood (`calm`, `happy`, `sad`,
  `surprised`) that the stage tags pick from;
- the language it speaks best;
- licence and source notes.

**2. Import, both ways:**
- **Downloaded:** drop a zip or folder with `.ckpt` + `.pth` (+ clips) on the cabinet, and it goes to whoever is on
  the stool. If it has no reference clip, a small form asks for one clip and its transcript.
- **Clone yourself:**
  - Drop a 3–10 s clip, type or confirm its transcript, and she speaks with it at once (zero-shot).
  - "Train a better voice…" opens the GPT-SoVITS WebUI. Its training output is picked up afterwards.
  - Later, the app could run the whole training (slice, transcribe, train) itself.

**3. Engine settings:** the GPT-SoVITS folder, plus Fish and ElevenLabs keys later (DPAPI, like chat keys).
Built: the folder, with a "Get GPT-SoVITS…" link to its releases. Not built yet: a download from inside the app.
The client is our own small one (`GptSoVits`), not Microsoft.Extensions.AI's experimental `ITextToSpeechClient`:
GPT-SoVITS needs reference clips and weights that interface has no place for.

**4. Streaming:**
- As her reply streams in, it's split into sentences (`。！？!?…` and line breaks). Stage tags are dropped and
  each sentence goes to the engine.
- While one sentence plays, the next is already being made.
- The bubble types the reply out as it arrives. Showing each sentence only as she says it is not built.
- Audio plays through Godot's `AudioStreamGenerator`. Her mouth follows the loudness through the VRM's `aa`
  expression, which `Face` already finds, plus `ih/ou/ee/oh` when present.
- Clicking her or muting stops her mid-sentence.
- If the engine isn't available she falls back to text, never to a lesser voice.

**5. Language (decided 2026-10-02).** Each character's voice has a spoken language the player sets (Japanese,
English or Chinese). The bubble is always in the language the player wrote in.
- **Example:** the player asks in English and the character is set to Japanese. She speaks Japanese and the
  bubble shows English, like a subtitled anime.
- **Languages match:** she speaks what she writes.
- **Languages differ:** the model writes each sentence twice, the spoken line first, e.g.
  `[say:お帰りなさい！]Welcome back!`. Our stage-tag parser already reads `[tag:payload]`: the voice speaks the
  payload and the bubble shows the rest.
- **The model forgets the tag:** she says nothing for that sentence (better than speaking the subtitle in the
  wrong language).
- **Cost:** some extra tokens and a little delay.

**6. Listening, later:**
- **In-app recognition:** sherpa-onnx has C# bindings. It provides voice detection plus
  [SenseVoice-Small](https://github.com/QwenAudio/SenseVoice) (Chinese, English, Japanese, Korean, Cantonese; fast
  on CPU). Untested risk: it ships its own `onnxruntime.dll` next to ours.
- **Steps:** push-to-talk first, then hands-free with interruption.
- **Cascade, not realtime services:** speech-to-text, then her normal conversation, then her voice. Realtime
  speech-to-speech services skip her memory and can't use her voice.

## Order of work

1. **Measure on this laptop.** *Done 2026-10-02/03*; see "Measured on the development laptop". The community
   model used was a community v4 voice (LoRA weights, MIT per its card), kept outside the repo for personal
   testing only, as with third-party 3D models. Its weights load through `/set_*_weights` in about 2.6 s.
2. **Speaking.** *Built 2026-10-03:* running the GPT-SoVITS server (stopped while a game runs), `voice.json` and
   import (downloaded or cloned from a clip), sentence streaming, subtitles, lip sync, mood clips, mute and stop,
   text-only fallback, and the Voice settings window.
   - **Unit tests:** against a fake GPT-SoVITS API.
   - **App test** (off-screen, muted, a fake chat model, the real server, a community v4 voice):
     - **Import:** six clips with their words, taken from the training list.
     - **Start:** server up, voice loaded and warmed up in 7 s.
     - **Speaking:** the first audio of each sentence arrived after 1.0–1.5 s.
     - **Subtitles:** asked in English, she spoke Chinese and the bubble showed English.
     - **Lip sync:** her mouth was open for about two thirds of the speech.
     - **Stopping:** a click cut her off cleanly.
     - **Games:** a game start stopped the server and its end brought it back.
     - **Crash safety:** killing the app ended the server too.
3. **Training:** "Train a better voice…" and picking up the result; licence notes in the voice picker.
4. **Cloud:** Fish Audio and ElevenLabs by voice id.
5. **Listening:** push-to-talk, then hands-free.
