# Talking in the room: their name, better ears and talking over them

*Decided with the user in one design session, 2026-11-15 to 2026-11-16 (#343), after they tried the Preview 3 builds
and found the companion hard to talk to. Nothing is built yet: the first step measures before anything is chosen. The
order is at the end.*

## Why

The user, 2026-11-15, after talking to the companion in the real app: "the STT is not very good even if i switched to
the correct input device. i have to get very close to the microphone and speak carefully without accent." And what
they want instead: "a siri like experience (but please try to be better than siri in terms of we want to act like we
are speaking to a person in the same room/house)".

What exists (`2026-10-05-hands-free-listening.md`):
- the talk key is the default; follow-up listening and conversations are the player's choice;
- Silero's voice detector and Smart Turn v3.2 decide when a turn has ended;
- SenseVoice-Small hears it, on the processor, so it never takes the graphics card from their voice or a game. It is
  quick (3 to 4 s of speech in about 0.1 s) but small, and it shows: it wants a close microphone and careful speech;
- they never listen while they speak, because their own voice would come back through the microphone;
- a wake word was put off on 2026-10-05, for privacy, battery and false starts.

Settings doesn't say which microphone they hear you on (#342).

## What it does

### Their name

- Opt-in, in Settings → Listening: "Answer to <their name>". Off until ticked.
- While it's on, the microphone listens for their name whenever the PC is awake and unlocked, across the room too. It
  never listens in a call, while a game runs, in full screen, while presenting or in quiet hours: the rules hands-free
  listening keeps.
- Only speech that starts with their name reaches them. The rest is heard on this PC and let go at once, never kept
  or sent.
- The name is the character's own, from their card, in English, Chinese or Japanese, with no "Hey".
- On hearing their name they react as a person would: they turn and look up, in their own manner, then listen. Their
  name and a request in one breath ("Hana, what's on today?") is answered straight away. A false start is a glance and
  nothing more.
- The CRT's MIC light has two states: listening only for their name, and listening to you.
- How the name is caught (a small keyword model on the processor, or the new ears on a stream) comes from the
  measurement.

Every assistant that answers to a word keeps a microphone open; what differs is what gates it and where the sound is
heard. Siri, Alexa and Google listen with a small model on the device and keep nothing until the word. Google's Look
and Talk needs no word: a camera sees its owner look at the screen from within five feet. Ours is gated by the rules
above, heard only on this PC, and a false start is in character.

### Talking over them

- The app opens the microphone in Windows' communications mode, so Windows 11's Voice Clarity takes out their voice,
  background noise and the room's echo before the app hears anything. This needs the app's own microphone capture in
  place of Godot's.
- The stream opts out of Windows' ducking, so music, videos and calls in other apps keep their volume while the
  microphone is open. Left to its default, Windows turns other sounds down by 80% while a communications stream is
  open.
- With that, they hear you while they speak. If you talk over them, they stop where they are and listen, as a person
  would. What they didn't get to say isn't kept as said.
- Where Voice Clarity isn't there, they keep today's rule: no listening while they speak.
- The microphone choice and its test (#342) run on the new capture.

### Better ears

- A stronger local model replaces SenseVoice-Small where it's measurably better: on the graphics card when it's free,
  and on the processor while a game runs or when memory beside their voice is short.
- Names are given to it as hints: theirs, the player's, and what's on the agenda today.
- Their mind is told the words were heard aloud, so it allows for a slip.
- The candidates, all run on this PC, are measured first: Qwen3-ASR 0.6B and 1.7B (Apache-2.0; 30 languages and 22
  Chinese dialects), Fun-ASR-Nano (Chinese, English and Japanese), and NVIDIA's Nemotron 3.5 ASR Streaming 0.6B (19
  locales ready, English and Japanese among them; words appear while you talk; OpenMDW-1.1). sherpa-onnx, which
  runs SenseVoice today, runs all four.
- Speech services on the player's own key (OpenAI's transcription and others) come later, as a choice.

### The whole loop, measured

- Turn by turn, the app logs the time from the end of the player's words to: the turn judged over, the words heard,
  their mind's first words, their first sentence, and their voice's first sound.
- The bar is set from what's measured, not before.

## Decided

- **Their name, opt-in, listened for while you're home** (the user, 2026-11-16). Asked how to start talking without the
  key, they picked it with "this is what i'm thinking about but is there solution other than always open mic? how does
  similar product resolve this issue? what can we innovate here?"; then, of when the microphone listens, "While you're
  home", over "only while you're at the PC" and over a glance at the webcam.
- **Voice Clarity, for talking over them and being heard from further away** (the user, 2026-11-15), over an echo
  canceller of our own.
- **Turn-based, fast and interruptible** (the user, 2026-11-16), over overlapping like a person (their mind starting on
  the words so far, small "mm"s while you talk, live captions). Asked first: "i guess streaming of transcript will not
  help much? the LLM will only start after we decide a sentence is complete. how complicated should we design the voice
  to voice experience?" Mostly so: whole turns are heard in about 0.1 s, and the waits are the end of the turn, their
  mind and their voice. The streaming model is measured to check.
- **Measure first** (the user, 2026-11-15: "a stronger local model that can run fast on my GPU can be good first step.
  we want to measure latency first tho"; 2026-11-16, of the speed bar: "Measure first, then set it"). All four
  candidates' downloads are allowed, each about 1 to 4 GB.
- **Tidying what was heard is judged on the ears** (the user, 2026-11-16: "we will judge based on the stt model
  quality").
- **Speech services later** (the user, 2026-11-15: "we may should offer different choice of STT, maybe including third
  party API, but a stronger local model that can run fast on my GPU can be good first step").

## Not built, and why

- **A glance instead of their name** (the webcam, as in Google's Look and Talk): a camera on all the time costs more
  privacy than a microphone.
- **Overlap**: their mind starting early, "mm"s while you talk and live captions are not in the first version.
- **Sound straight into their mind** (speech to speech, as in ChatGPT's voice mode): it would replace their own voice and
  the player's provider.
- **Only your voice wakes them** (a voiceprint): anyone saying their name gets a look, a visitor or the TV too.
- **A phone or another device as a microphone around the house.**

## Open

- Which ears: the user judges from the measurement, on accuracy at a distance and with an accent, and on speed.
- Whether to tidy what was heard before their mind sees it, from those ears' quality.
- The speed bar, from the measurement.
- How their name is caught: a keyword model, or the ears on a stream.
- Which speech services, and when.
- What a PC without Voice Clarity gets beyond today's rule.
- Whether the player's exceptions to hands-free's rules (listening while a game runs, say) reach their name too.
- What listening for their name all day costs the processor, and so a laptop's battery: measured with the name
  catcher (#346).

## Order

1. **Measure** (#346, a spike): the four candidates against SenseVoice-Small on the user's laptop graphics card (8 GB)
   beside their voice, and on the processor; accuracy in English, Chinese and Japanese, clean, at a distance and
   accented; the whole loop's timings; whether Voice Clarity applies on this PC; how a name would be caught.
2. **The microphone in communications mode, and talking over them** (#347), with the microphone choice (#342) on it.
3. **The chosen ears** (#348), with names as hints and each turn's timings logged.
4. **Their name** (#349).

## Sources

- [Look and Talk to Google on Nest Hub Max](https://support.google.com/googlenest/answer/11410414): within 5 feet,
  looking at the camera.
- [Qwen3-ASR-0.6B](https://huggingface.co/Qwen/Qwen3-ASR-0.6B) and
  [Qwen3-ASR-1.7B](https://huggingface.co/Qwen/Qwen3-ASR-1.7B):
  Apache-2.0, 30 languages and 22 Chinese dialects.
- [Fun-ASR-Nano](https://huggingface.co/FunAudioLLM/Fun-ASR-Nano-2512).
- [Nemotron 3.5 ASR Streaming 0.6B](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b): its locales and
  licence.
- [sherpa-onnx v1.13.8](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8).
- [Windows' default ducking](https://learn.microsoft.com/en-us/windows/win32/coreaudio/stream-attenuation) and
  [opting out of it](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-setduckingpreference).
- The measurements: `docs/research/2026-11-16-speech-models.md` (#358).
