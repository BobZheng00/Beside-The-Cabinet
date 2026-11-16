# Better ears: four speech models against SenseVoice-Small, and what Voice Clarity gives

*Measured 2026-11-16 on the owner's PC for #346, step 1 of `docs/specs/2026-11-16-talking-in-the-room.md`. The owner,
2026-11-15: "a stronger local model that can run fast on my GPU can be good first step. we want to measure latency
first tho". The scripts are in `tools/bench/speech/` (each has its use in its header); the recordings, models and runs
stay on the PC and are not committed.*

PLACEHOLDER-SHORT

## What was measured, and how

- **The ears.** Today's SenseVoice-Small and the four candidates, all through sherpa-onnx 1.13.8, the version and
  runtime the app ships, with its own settings (`Hearing.cs`: 4 threads, language found by itself):
  - Qwen3-ASR 0.6B, int8, k2-fsa's own export (`sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25`);
  - Qwen3-ASR 1.7B, int8: k2-fsa publishes no export of it, so a third party's in k2-fsa's layout
    (`thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8` on Hugging Face). It leaks Qwen's language tag into the text
    ("language English<asr_text>Yes."), which the harness strips, and once said "るもの。" for "Yes, always.": an export
    of our own would be needed before shipping it;
  - Fun-ASR-Nano (the 2512 release), int8, k2-fsa's export;
  - Nemotron 3.5 ASR Streaming 0.6B, int8, k2-fsa's exports at 560 ms and 160 ms chunks, streamed as if live: the
    clip comes in 20 ms pieces at the pace it was said, each heard as soon as the model is free.
- **The recordings** (`make_audio.py`, `corpus.json`). 33 lines a player might say to them, 11 in each of English,
  Chinese and Japanese, about 2, 6 and 15 seconds long, 18 of them with a character's or the player's name in
  them. Each said by four voices of its language (Qwen TTS's preset speakers on the processor, and Windows' own),
  and the English ones also by five speakers of other languages (Chinese, Japanese and Korean) for accented English:
  187 recordings. Then each in three rooms: as made (clean); near (a room about 1.5 m away: echo of 0.35 s, the voice
  a little louder than its echo, quiet behind at 25 dB under the voice); far (across the room, about 4 m: echo of
  0.6 s, louder than the voice itself, a fan and pink noise 10 dB under the voice). 561 clips.
  - Synthesised voices are cleaner and more regular than people, and a made-up room is not a real one, so read the
    rooms as a ranking of the models, not as what the owner will get. Accents from speakers of other languages
    reading English are a weak stand-in for a person's accent.
  - A recording no model hears within 35% (names aside) is left out as the synthesiser's slip, not the ears'.
    Windows' Japanese voice reading English (it spells it out in katakana) and a few Qwen TTS lines that came out as
    something else are among them; the tables say how many.
- **The project's own test speech** (`talk.wav` and the four `say-*.wav`): heard by every model, below.
- **Names**: "Shino", "Hana", "Fumiriya" and the player's ("Alex", 林晓, 健太). A name counts as heard when any fair
  spelling of it is in the text (Shino, 诗乃, シノ, しの, 詩乃; Hana, 花, ハナ, はな; Fumiriya, フミリヤ). With hints: the
  names given to the model as hotwords, where sherpa-onnx takes them (the Qwen3-ASR models and Fun-ASR-Nano;
  SenseVoice and k2-fsa's Nemotron export take none).
- **Error rates**: word error rate for English, character error rate for Chinese and Japanese, after the same
  tidying for every model (numbers written as digits or words count the same, punctuation and case don't count).
  Japanese spelled in kana where the line has kanji (わからない, 分からない) counts as an error for every model alike.
- **Timings**: only when the PC was otherwise quiet (no game, no other test run, the graphics card idle), 9 clean
  clips of each length, 5 times each: the median and the lowest and highest.

## Voice Clarity on this PC

Read from Windows' own list of each microphone's effects for a kind of stream (`voice_clarity.cs`: Windows'
`AudioEffectsManager`, which opens no microphone and records nothing), Windows 11 build 26200:

| Microphone | Communications (a call) | Speech | Game chat | An ordinary stream (what the app gets today) |
|---|---|---|---|---|
| USB webcam (the default, and the default for calls) | echo cancelling, noise suppression, gain control, **deep noise suppression** | the same | the same | noise suppression |
| USB headset | echo cancelling, noise suppression, gain control, **deep noise suppression** | the same | the same | noise suppression |
| The laptop's built-in microphone | none listed | echo cancelling, noise suppression, gain control (its maker's own) | none listed | echo cancelling, noise suppression |

With raw processing every microphone lists nothing.

- **Voice Clarity applies here**, to both USB microphones, once the app opens them for communications (or speech, or
  game chat). Deep noise suppression is Voice Clarity's own effect; Microsoft says it works on its own for apps that
  use communications mode "when the OEM device does not offer Communications Mode processing", which is what this
  shows.
- **The built-in microphone has its maker's processing instead.** Windows lists none for a communications stream on
  it. Whether its maker's echo cancelling then applies can't be read from the list: the owner's script below records
  it to find out.
- **Today the app gets only plain noise suppression** on the USB microphones: Godot opens an ordinary stream. That and
  no echo cancelling is consistent with the owner's "i have to get very close to the microphone".
- Gain control comes with it: a far voice is brought up, which should help the ears at a distance as much as the
  noise suppression does.

**For the owner, once, with their own microphone:** `dotnet run tools/bench/speech/hear_voice_clarity.cs` records the
same 10 seconds twice at once from the default microphone, as the app hears today and as a call does, while a voice
talks from the speakers (to hear the echo cancelling, use speakers, not headphones), then plays both back. The
recordings stay in `voice-clarity-check` in the current folder. To try another microphone, make it the default in
Windows' sound settings first. No test runs it: it opens the microphone.

PLACEHOLDER-RESULTS

## Licences, sizes, and what shipping each would take

| Model | Licence | Download (sherpa-onnx, compressed) | On disk | Languages |
|---|---|---|---|---|
| SenseVoice-Small (today) | FunASR Model Licence 1.1 (use and share, keep the name) | 155 MB | 230 MB | zh, en, ja, ko, yue |
| Qwen3-ASR 0.6B | Apache-2.0 | 838 MB | 954 MB | 30 languages, 22 Chinese dialects |
| Qwen3-ASR 1.7B | Apache-2.0 | about 2.4 GB (no k2-fsa export: a third party's, or our own) | 2.3 GB | the same |
| Fun-ASR-Nano | Apache-2.0 (its model card) | 803 MB | 972 MB | zh (and dialects), en, ja |
| Nemotron 3.5 ASR Streaming 0.6B | OpenMDW-1.1, "ready for commercial use" (model card, checked 2026-11-16) | 453 MB a chunk size | 653 MB | 19 locales ready, among them en and ja; Mandarin only "broad coverage" (20% character errors on FLEURS by NVIDIA's own table) |

- **All four run in the sherpa-onnx the app already ships**, on the processor: no new runtime, only the model,
  downloaded on first use like Qwen TTS's.
- **On the graphics card they don't, as shipped.** The app's sherpa-onnx (NuGet 1.13.8) is built for the processor
  only: asked for CUDA it says "Please compile with -DSHERPA_ONNX_ENABLE_GPU=ON … Fallback to cpu!", and its
  DirectML provider is missing too. k2-fsa's CUDA build for Windows (567 MB, CUDA 12 and cuDNN 9) would need those
  NVIDIA libraries beside it (about 1 GB more, and not the CUDA 11.8 that the GPT-SoVITS package carries), and there
  is no DirectML build to download.

PLACEHOLDER-RECOMMEND

## Sources

- k2-fsa's model releases: https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models, and its sherpa-onnx 1.13.8
  release (the CUDA builds): https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8
- Qwen3-ASR: https://huggingface.co/Qwen/Qwen3-ASR-0.6B, https://huggingface.co/Qwen/Qwen3-ASR-1.7B; the 1.7B export
  used: https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8
- Fun-ASR-Nano: https://huggingface.co/FunAudioLLM/Fun-ASR-Nano-2512
- Nemotron 3.5 ASR Streaming 0.6B, its card (licence, languages, FLEURS table):
  https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b; OpenMDW-1.1: https://openmdw.ai/license/1-1/
- Voice Clarity for apps in communications mode, as reported from Microsoft's Windows Insider notes:
  https://www.windowscentral.com/software-apps/windows-11/all-windows-11-pcs-will-soon-support-this-ai-feature-once-exclusive-to-surface-no-npu-required,
  https://www.neowin.net/news/windows-11-insider-canary-chanel-build-26040-adds-voice-clarity-feature-and-more/
- Windows' list of a microphone's effects: `Windows.Media.Effects.AudioEffectsManager.CreateAudioCaptureEffectsManager`
  (Windows SDK).
