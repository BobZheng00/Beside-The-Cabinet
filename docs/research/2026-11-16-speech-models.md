# Better ears: four speech models against SenseVoice-Small, and what Voice Clarity gives

*Measured 2026-11-16 on the owner's PC for #346, step 1 of `docs/specs/2026-11-16-talking-in-the-room.md`. The owner,
2026-11-15: "a stronger local model that can run fast on my GPU can be good first step. we want to measure latency
first tho". The scripts are in `tools/bench/speech/` (each has its use in its header); the recordings, models and runs
stay on the PC and are not committed.*

**In short.**
- **Not done yet: the graphics card.** The sherpa-onnx the app ships runs only on the processor, and timing the
  candidates on the card needs k2-fsa's CUDA build of it, a download that needs the owner's yes first. Everything
  below is on the processor, with the owner's voice (GPT-SoVITS) loaded on the card beside it.
- **On the processor, every candidate is too slow for every turn.** SenseVoice hears a 2, 6 or 15 second turn in
  0.13, 0.25 or 0.6 s. Qwen3-ASR 0.6B takes 0.9, 1.7 or 4.3 s, Fun-ASR-Nano about the same, Qwen3-ASR 1.7B 1.6, 3.0
  or 7.4 s. So SenseVoice stays the ears for when the card is busy (a game running, or too little memory beside the
  voice).
- **Where they're better, it's accents and distance.** On English said by speakers of other languages, SenseVoice
  gets 23 to 29% of words wrong; Qwen3-ASR 0.6B 12 to 25%, 1.7B 8 to 23%. Across the room in English, 11% against
  7.6% and 6.9%. In clean Chinese SenseVoice, Qwen3-ASR 0.6B and Fun-ASR-Nano are about equal (3%). In Japanese,
  SenseVoice is as good as either Qwen (7% against 9 to 10%), and Fun-ASR-Nano is the best (5%) but worse than SenseVoice on accented English. Nemotron is
  the weakest nearly everywhere, and in Mandarin far behind.
- **Names are the hardest part, for every model.** Without hints, at best about half the names were heard in any fair
  spelling ("Shino" comes out as "She know", 师奶, 死の). Given the names as hints, Qwen3-ASR 0.6B hears more of them
  in English (30 to 55%) and Japanese (29 to 42%), not in Chinese.
- **The ears are a small part of the wait.** With SenseVoice, the player waits 0.6 to 2.2 s for the turn to be judged
  over, about 0.1 to 0.6 s for the words, and then 1.3 s (median) for the voice's first sound once the first sentence
  is there. A short question Smart Turn isn't sure of waits the whole 2 s of quiet; across the room, noise and echo
  keep the turn open longer still.
- **Voice Clarity is there** on this PC for both USB microphones, once the app opens them for communications: echo
  cancelling, noise suppression, gain control and deep noise suppression. Today the app gets only plain noise
  suppression. The laptop's built-in microphone has its maker's processing instead.
- **Catching the name from the ears' text doesn't work well enough**: 11% of names said first were caught from
  SenseVoice's words, 40% from Qwen3-ASR's with hints, which then also "heard" a name 5 times an hour in other speech.
  The streaming model keeps 4 threads busy a third of the time, all the time. A keyword spotter is the thing to try next; it is
  a download too.

**Recommendation.** For #348: Qwen3-ASR 0.6B on the graphics card (Apache-2.0, k2-fsa's own export, hints that help),
with SenseVoice-Small kept on the processor for when the card is busy, if its time on the card turns out well under
half a second (to measure, once the CUDA build is allowed). Not the 1.7B until there is an export that holds up (the
one tried here broke with hints), and not Nemotron. For #349: try sherpa-onnx's keyword spotter first; reading the
name from the ears' words is a fallback that needs matching by sound, not spelling.

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
  Hiragana and katakana count the same (ばたばた, バタバタ); kana where the line has kanji (わからない, 分からない)
  counts as an error, for every model alike.
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
- **Today the app gets only plain noise suppression** on the USB microphones: Godot doesn't open it for communications. That and
  no echo cancelling is consistent with the owner's "i have to get very close to the microphone".
- Gain control comes with it: a far voice is brought up, which should help the ears at a distance as much as the
  noise suppression does.

**For the owner, once, with their own microphone:** `dotnet run tools/bench/speech/hear_voice_clarity.cs` records the
same 10 seconds twice at once from the default microphone, as the app hears today and as a call does, while a voice
talks from the speakers (to hear the echo cancelling, use speakers, not headphones), then plays both back. The
recordings stay in `voice-clarity-check` in the current folder. To try another microphone, make it the default in
Windows' sound settings first. No test runs it: it opens the microphone.

## Accuracy

Error rate, %: words wrong for English and accented English, characters wrong for Chinese and Japanese. 181 of the 187
recordings (6 left out as the synthesiser's slips: four of Windows' Japanese voice reading English, one of Qwen TTS's
Korean speaker, one Japanese line Qwen TTS said as something else), each in three rooms. "Ran on": answers where the
model repeated itself until it was stopped.

| Model | en clean | en near | en far | accented clean | accented near | accented far | zh clean | zh near | zh far | ja clean | ja near | ja far | ran on |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| SenseVoice-Small (today) | 6.2 | 6.9 | 11.1 | 23.2 | 24.0 | 28.6 | 3.2 | 3.0 | 4.3 | 6.7 | 7.2 | 17.3 | 0 |
| Qwen3-ASR 0.6B | 5.3 | 5.9 | 7.6 | 12.0 | 14.7 | 24.6 | 2.7 | 2.9 | 3.8 | 9.5 | 10.0 | 25.0 | 0 |
| Qwen3-ASR 1.7B (third party's export) | 6.6 | 5.9 | 6.9 | 8.4 | 8.8 | 22.5 | 14.0 | 2.4 | 4.2 | 8.8 | 9.7 | 18.3 | 0 |
| Fun-ASR-Nano | 5.9 | 11.8 | 9.9 | 18.3 | 22.0 | 25.9 | 2.7 | 2.8 | 3.8 | 4.9 | 6.0 | 13.2 | 2 |
| Nemotron 3.5 Streaming, 560 ms | 5.8 | 5.8 | 15.4 | 23.2 | 24.1 | 38.3 | 8.2 | 8.6 | 17.4 | 9.3 | 11.8 | 41.2 | 0 |

- **Accented English is where today's ears fall down**, and where Qwen3-ASR does best. Most of those errors come
  from the hardest stand-in (Windows' Japanese voice reading English), which SenseVoice often turns into Japanese.
- **Across the room in English**, both Qwen3-ASR models lose less than SenseVoice (11% to 7%).
- **Chinese** is close for all but Nemotron (Mandarin is outside its 19 ready locales). The 1.7B's 14% in the clean
  room is its export: it answered one line in Persian, cut two short, and left one empty.
- **Japanese** favours Fun-ASR-Nano, then SenseVoice. Part of Qwen's extra 3% is writing in kanji what the line has
  in kana and back (伸びて for 延びて), which counts as wrong.
- **Fun-ASR-Nano repeated itself twice** ("FUMI REHEHEHE…", stopped at 200 tokens after 15 s). The Qwen3-ASR models
  can too: sherpa-onnx stops their repetition by itself ("decode collapsed into a repetition loop… truncating"), as
  it did here on other speech with hints given.
- **The project's own test speech** (`talk.wav`, three sentences, and the four short answers): every model heard
  `talk.wav` right. Of the short answers, Nemotron heard "Not now" as "Knock out" and "No, open YouTube" as "Note open
  YouTube", and the 1.7B export said "るもの。" for "Yes, always."; the others had them.

**Names** (% of names said that were heard, in any fair spelling):

| Model | en clean | en near | en far | accented clean | accented near | accented far | zh clean | zh near | zh far | ja clean | ja near | ja far |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| SenseVoice-Small | 25 | 38 | 28 | 25 | 28 | 22 | 29 | 42 | 29 | 32 | 25 | 7 |
| Qwen3-ASR 0.6B | 34 | 28 | 28 | 42 | 42 | 22 | 38 | 33 | 21 | 46 | 32 | 7 |
| Qwen3-ASR 1.7B | 38 | 38 | 25 | 42 | 42 | 22 | 38 | 50 | 42 | 64 | 61 | 32 |
| Fun-ASR-Nano | 28 | 25 | 22 | 36 | 36 | 25 | 42 | 38 | 29 | 64 | 46 | 32 |
| Nemotron, 560 ms | 34 | 41 | 22 | 31 | 25 | 11 | 8 | 8 | 12 | 46 | 39 | 4 |

With the names given as hints, on the clips that hold a name, all rooms together (names heard % / error rate %):

| Model | en without | en with | accented without | accented with | zh without | zh with | ja without | ja with |
|---|---|---|---|---|---|---|---|---|
| Qwen3-ASR 0.6B | 30 / 8.9 | 55 / 5.9 | 35 / 20.4 | 51 / 20.6 | 31 / 7.6 | 28 / 8.1 | 29 / 22.9 | 42 / 20.0 |
| Qwen3-ASR 1.7B | 33 / 9.1 | 44 / 50.2 | 35 / 15.4 | 45 / 39.2 | 43 / 16.8 | 29 / 43.7 | 52 / 18.6 | 54 / 19.9 |
| Fun-ASR-Nano | 25 / 12.7 | 32 / 9.4 | 32 / 25.0 | 36 / 23.6 | 36 / 7.1 | 38 / 7.1 | 48 / 13.4 | 40 / 18.5 |

- Hints help Qwen3-ASR 0.6B in English and Japanese and leave the rest of the line as good or better. They don't help
  in Chinese: 诗乃 is still heard as 师奶 or 吃奶, the same sounds.
- With hints, both Qwen3-ASR models sometimes answer with the hint list itself in broken characters ("Shino ʫ nota͘
  Hana ʫ…"): the 1.7B export on a third of the English lines, k2-fsa's 0.6B export on 5 or 6 stretches of an hour of
  other speech. The app would have to throw such answers away.
- How the names went wrong, first words of the lines that start with Shino (clean and near): SenseVoice "She" 19
  times, 师奶 or 师乃, 死の, "Sheino", "Sino"; Qwen3-ASR 0.6B "She", "Chino", 市の; with hints "Shino" 28 times out of
  about 40, しの.

## Speed, on the processor, with the voice loaded on the card

The PC quiet (no game, no test run, the card idle), GPT-SoVITS loaded on the card with the owner's voice as the
player has it, 4 threads as the app uses. Each model in a process of its own.

| Model | Load | First turn after loading | Memory (private) |
|---|---|---|---|
| SenseVoice-Small | 2.1 s | 0.15 s | 317 MB |
| Qwen3-ASR 0.6B | 5.9 s | 0.97 s | 1.2 GB |
| Qwen3-ASR 1.7B | 10.3 s | 1.7 s | 2.6 GB |
| Fun-ASR-Nano | 9.0 s | 0.67 s | 1.1 GB |
| Nemotron, 560 ms | 3.0 s | 0.54 s | 0.8 GB |
| Nemotron, 160 ms | 3.0 s | 0.98 s | 0.8 GB |

From the end of the turn to the words, s: the median and [lowest, highest] of 5 tries on each of 9 clips of that
length (3 a language):

| Model | ~2 s turn | ~6 s turn | ~15 s turn |
|---|---|---|---|
| SenseVoice-Small | 0.13 [0.09, 0.20] | 0.25 [0.21, 0.35] | 0.60 [0.46, 0.67] |
| Qwen3-ASR 0.6B | 0.86 [0.71, 1.15] | 1.68 [1.47, 2.02] | 4.33 [3.33, 6.21] |
| Qwen3-ASR 1.7B | 1.64 [1.20, 2.50] | 3.02 [2.50, 4.26] | 7.44 [3.85, 8.94] |
| Fun-ASR-Nano | 0.62 [0.48, 1.10] | 1.37 [1.21, 1.99] | 4.12 [3.02, 6.33] |

**The streaming model** hears while you talk, so its words are nearly ready when you stop. Measured as if live (the
clip arriving at the pace it was said):

| Model | Last word lands, after the end of the words | First words show, after speech starts | Processor time per second of speech |
|---|---|---|---|
| Nemotron, 560 ms | 0.7 to 0.8 s (median of the three lengths; 0.6 to 0.9) | 1.2 s (0.6 to 2.9) | 0.38 s |
| Nemotron, 160 ms | 1.3 to 1.4 s: smaller chunks, more work in all, and 4 threads lag behind | | |

On the processor the streaming model's last word lands later than SenseVoice hears a whole 15 s turn (0.6 s), and it
keeps a third of 4 threads busy the whole time someone speaks. The "words while you talk" (decided against for the
first version, the spec's "Turn-based, fast and interruptible") wouldn't be worth it here.

## The whole loop

From the end of the player's words: when the turn was judged over (Silero and Smart Turn, as the app does it), when
the words were heard, when the mind's first words came (the mock mind, which answers at once: a real provider adds its
own wait here), when the first sentence was complete, and when the voice's first sound came (GPT-SoVITS with the
owner's voice, v2ProPlus, a Japanese line). 18 clips (each length, clean and far, each language), 3 times each.

With SenseVoice-Small:

| Room, length | Turn judged over | Words heard | Mind's first words | First sentence | Voice's first sound |
|---|---|---|---|---|---|
| clean, ~2 s | 2.18 [0.51, 2.22] | 2.37 [0.70, 2.46] | 2.37 [0.71, 2.46] | 2.37 [0.71, 2.46] | 3.60 [1.70, 4.47] |
| clean, ~6 s | 0.58 [0.56, 2.06] | 0.84 [0.80, 2.50] | 0.84 [0.80, 2.50] | 0.84 [0.80, 2.50] | 2.66 [1.54, 4.05] |
| clean, ~15 s | 0.68 | 1.29 | 1.30 | 1.30 | 2.54 [2.27, 3.05] |
| far, ~2 s | 1.28 [1.04, 2.56] | 1.46 [1.22, 2.84] | 1.47 [1.22, 2.85] | 1.47 [1.22, 2.85] | 3.34 [2.16, 4.63] |
| far, ~6 s | 1.25 [0.95, 1.55] | 1.54 [1.20, 1.88] | 1.54 [1.20, 1.88] | 1.54 [1.20, 1.88] | 2.89 [1.96, 3.85] |
| far, ~15 s | 2.69 [2.46, 2.92] | 3.42 [3.17, 3.70] | 3.42 [3.18, 3.70] | 3.42 [3.18, 3.70] | 4.59 [3.96, 5.12] |

With Qwen3-ASR 0.6B on the processor, the words come 0.9 to 4.4 s later than that (a 15 s turn across the room:
7.8 s to the words, 9.7 s to the voice).

- **The turn's end is the biggest wait we control.** In the clean room Smart Turn was sure at the first pause (0.5 to
  0.7 s after the words) on 4 of the 9 recordings; the rest waited for the full 2 s of quiet, among them two of the
  three short questions ("Shino, what's on today?" and its Chinese twin), the turns that most want a quick answer.
  Across the room, noise and echo keep Silero hearing "speech" after the words have stopped, so even a turn Smart Turn
  is sure of ends later (about 1.25 s), and a long one 2.5 to 2.9 s after. Voice Clarity's noise suppression should
  shorten that.
- **Smart Turn ended 4 of the 18 recordings (12 of 54 turns) at a pause in the middle**: after "So today was kind
  of a mess." and its Chinese twin, and after the name in "フミリヤ、今夜八時ごろに…". A name, then a pause, is exactly how a call to them
  starts, which matters for #349: the turn shouldn't end at the name.
- **The voice's first sound came 1.3 s after the first sentence** (median; 0.7 to 2.4), GPT-SoVITS's own first-package
  delay 1.3 s (0.6 to 4.5) in its log. That is more than the 0.37 to 0.50 s measured on 2026-10-03 with a cloned
  English voice; this voice is a trained v2ProPlus one speaking Japanese, with four clips blended.
- **The mind's first words** were 5 to 10 ms after the words were heard: the mock is instant. The owner's provider
  adds its own time to first token, not measured here (the owner's key isn't used for tests).

## Catching their name

Three ways, on the processor, with the names Shino, Hana and Fumiriya (in their spellings in the three languages)
listened for at once:
- **The ears on each stretch of speech**: Silero finds where speech starts (a stretch ends at 0.6 s of quiet, so the
  comma after a name doesn't), the first 2 s of it are heard whole, and the name counts when the words start with it
  (with a stretch ending at 0.25 s of quiet, as for a turn's pauses, the catches were about the same);
- **The streaming model on the stream**: Nemotron runs all the time, and the name counts when an utterance's words
  start with it;
- **A keyword spotter** (round 2, the owner allowed the download): sherpa-onnx's own, already in the library the app
  ships, with k2-fsa's 3M-parameter Chinese and English model (int8, 31 MB download, Apache-2.0), on one thread, all
  the time. The names are written in its units by hand (`keywords.txt`: English phones, which also stand for the
  Japanese シノ, ハナ and フミリヤ, and pinyin for 诗乃 and 花); `keywords-more.txt` adds a few more ways of saying
  them. Silero runs beside it, and a name counts only when it comes within 1.5 s of speech starting after a quiet.

On the clips whose line starts with a name (caught / said), on the clips with a name later or none (should not be
caught), and on other speech, an hour and 13 minutes of sentences from this repository's docs said by Windows' English
voices in the near room (a TV or another person; 20 minutes of it for the streaming model):

| Way | Said first: caught | Not first: caught | Other speech: false starts | Per hour | Processor per second of sound |
|---|---|---|---|---|---|
| SenseVoice on each stretch | 31 / 270 (11%) | 0 / 51 | 0 in 73 min | 0 | 0.02 s |
| SenseVoice, across the room (clean clips; other speech far) | 14 / 90 | 0 / 17 | 0 in 73 min | 0 | 0.02 s |
| Qwen3-ASR 0.6B on each stretch | 34 / 270 (13%) | 0 / 51 | 0 in 73 min | 0 | 0.06 s |
| Qwen3-ASR 0.6B, names as hints | 107 / 270 (40%) | 0 / 51 | 6 in 73 min | 4.9 | 0.07 s |
| Nemotron 560 ms, streaming | 31 / 270 (11%) | 0 / 51 | 0 in 22 min | 0 | 0.31 s |
| Keyword spotter, 1 thread | 83 / 270 (31%) | 0 / 51 | 3 in 73 min | 2.5 | 0.027 s |
| Keyword spotter, more ways of saying them | 90 / 270 (33%) | 0 / 51 | 4 in 73 min | 3.3 | 0.027 s |
| Keyword spotter, a lower threshold (0.15) | 96 / 270 (36%) | 0 / 51 | 3 in 73 min | 2.5 | 0.034 s |
| Keyword spotter, other speech across the room (far clips) | 7 / 90 | 0 / 17 | 4 in 73 min | 3.3 | 0.032 s |

The two best, by room and by language (names said first, caught):

| Way | clean | near | far | English | accented | Chinese | Japanese |
|---|---|---|---|---|---|---|---|
| Qwen3-ASR 0.6B with hints | 41 / 90 | 38 / 90 | 28 / 90 | 34 / 72 | 43 / 90 | 8 / 48 | 22 / 60 |
| Keyword spotter, more ways | 49 / 90 | 34 / 90 | 7 / 90 | 24 / 72 | 27 / 90 | 34 / 48 | 5 / 60 |
| SenseVoice on each stretch | 14 / 90 | 13 / 90 | 4 / 90 | 6 / 72 | 9 / 90 | 7 / 48 | 9 / 60 |

- **None is good enough yet.** The ears hear the rest of the line and lose the name, which is the one word that
  matters here. With hints, Qwen3-ASR catches English names about half the time (34 of 72) and Chinese ones hardly at
  all (8 of 48), and its false starts were all the garbled hint list described above.
- **The keyword spotter is the mirror image**: the best in Chinese (34 of 48: 诗乃 is in its own units) and in the
  clean room, but it misses nearly every name across the room (7 of 90) and nearly every Japanese one (5 of 60: there
  is no Japanese model, and English phones fit Japanese speech poorly). Its false starts were all "Shino" or 花 heard
  in English sentences, 2.5 to 3.3 an hour; 花 (huā), one syllable, is too short a keyword. No name that came later
  in a sentence was taken for a call (the 1.5 s rule).
- **What listening all day costs**: the keyword spotter and Silero together take 0.027 s of one core per second of
  sound, about 3% of one core, around the clock, and 47 MB of memory. Spread over this laptop processor's 32 threads, about
  0.1% of it. SenseVoice on each stretch of speech is about the same while people talk and nothing in quiet, but
  needs Silero all the time (well under 1%). Battery use wasn't measured: a core never quite idle keeps a laptop from
  its deepest sleep states, which matters more on battery than the 3% does.
- **From the ears' words, false starts are rare** when nothing is hinted: no sentence of other speech came out starting with a name, even
  though some had a name later in them.
- **Nemotron on the stream** takes 31% of 4 threads all the time, for 11%: not a way to listen all day.
- **What would do better**: the spotter as the cheap gate in the clean and near room, and the ears to confirm (only
  the first 2 s of a stretch it flags, so the cost stays small); matching the ears' first word by sound rather than
  spelling (师奶, 吃奶 and 诗乃 are all "shī nǎi"; "She know" and "Shino" are close); a keyword model with Japanese, or
  one trained on the character's name. Distance is the open problem for every way here, which is where Voice
  Clarity's noise suppression and gain control come in (#347) before anything else is tried.
- These are synthesised voices and other speech from one kind of source: the real false start rate needs real rooms,
  the owner's TV or their friends.

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

## What this means for the next steps

**#348, which ears.**
- On the processor, none of the candidates can hear every turn: a 6 s turn would wait 1.4 to 3 s more than today.
  SenseVoice-Small stays for the processor.
- Qwen3-ASR 0.6B is the one to try on the graphics card: the best on accents and distance here, hints that help in
  English and Japanese, Apache-2.0, k2-fsa's own export, 838 MB to download. It needs:
  - its time on the card, beside the voice, under about half a second for a 6 s turn to be worth it (to measure);
  - the garbled hint answers thrown away (a check of the shape of the answer, not a prompt);
  - kanji and kana in Japanese are no worse than today's, and Chinese the same.
- Not Qwen3-ASR 1.7B for now: the only export there is broke with hints and slipped without them. With an export of
  our own (k2-fsa's script) it may be the most accurate; it is also 2.6 GB of memory.
- Not Fun-ASR-Nano: best in Japanese, but not on accented English, and it repeated itself.
- Not Nemotron: weakest in Chinese and at a distance, a third of the processor while it listens, and the speed it
  promises doesn't beat SenseVoice on whole turns here.
- Tidying what was heard before their mind sees it: not needed for SenseVoice's or Qwen's clean-room output (3 to 10%
  wrong, mostly small); at a distance and with accents, names are what goes wrong, which the hints address.

**#349, how the name is caught.** A keyword spotter first (a download to ask for). Reading it from the ears' words
catches 11 to 40% here. The turn must also not end at the pause after the name.

**#347, the microphone in communications mode.** Voice Clarity is there on the owner's USB microphones; the owner's
recording with `hear_voice_clarity.cs` will say how much it does in their room.

**Still to do.**
- On the graphics card: each model's time and memory beside the voice. Needs k2-fsa's CUDA build of sherpa-onnx
  1.13.8 (567 MB) in the trial folder, with the CUDA 12.5 and cuDNN 9.5 already on this PC.
- The keyword spotter: its catches, false starts and cost on the same recordings.
- Real voices: the owner's own, from across the room, once, with their microphone (the script above records it; the
  same recordings could go through `ears.cs`).

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
