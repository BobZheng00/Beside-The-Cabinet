# The voice without an NVIDIA card

*Measured and read 2026-11-08, with the trials of Pocket TTS and Qwen TTS on 2026-11-09 (#278), for Preview 3
(`docs/specs/2026-11-08-a-new-player-gets-it.md`). The user: "will the small
tts model work fine on modern apple sillicon or x86 cpu or iGPU? i guess we want to bring it up to user to let them see if
they like the performance instead of hiding it".*

**In short.** GPT-SoVITS, the engine the app uses, runs without a card only with its lighter model, v2ProPlus, and even that
can't keep up with speech on a processor: a sentence of about 3 seconds takes 6 to 8 seconds to make. Its default for a
voice with no trained model, v4, is far too slow there, at 24 to 31 seconds for even a short line. A small model without voice cloning (Kokoro) keeps up on 4
cores or more. Apple Silicon looks better than an x86 processor for all of them. Nothing usable was found for integrated
graphics. Two smaller engines were then tried on the same processor. Pocket TTS needs about a third of a second of work per
second of speech in the app's own runtime, and copies a voice from about 30 seconds of it. A native build of Qwen TTS about
keeps up on a fast processor, and has the voices the user picked for Chinese and Japanese.

## Measured on an x86 processor

A fast laptop processor, with the graphics card hidden from the engine, on its fast cores, one thread a core, at
below-normal priority. It's a fast laptop chip, so a mainstream one will be slower. English sentences of
1 to 6 seconds, spoken one at a time, as the app speaks them.

Work per second of speech: under 1 keeps up with speech, over 1 falls behind it.

| Engine | Fast cores | Work per second of speech | First sound |
|---|---|---|---|
| GPT-SoVITS v4, 16 steps (the app's default for a voice with no trained model) | 8 | 18.4 s | 24 s for a short line, 51 s for 3 s of speech |
| GPT-SoVITS v4, 16 steps | 4 | 23.3 s | 31 s for a short line, 69 s for 3 s of speech |
| GPT-SoVITS v2ProPlus | 8 | 1.59 s | 2.6 s |
| GPT-SoVITS v2ProPlus | 4 | 1.76 s | 2.9 s |
| GPT-SoVITS v2ProPlus | 2 | 2.28 s | 3.6 s |
| Kokoro v1.0 (ONNX, no voice cloning) | 8 | 0.55 s (0.39 s after the first line) | when the sentence is done: 0.6 to 2 s |
| Kokoro v1.0 | 4 | 0.63 s | 0.5 to 3.3 s |
| Kokoro v1.0 | 2 | 1.07 s | 1.6 to 5.3 s |
| Pocket TTS, the app's runtime, the public January export | 8, with 4 threads | 0.30 to 0.37 s | 0.83 to 1.1 s |
| Pocket TTS, the same | 8, with 8 threads | 1.36 to 1.56 s | 3.5 to 4.8 s |
| Qwen TTS 0.6B, plain PyTorch, full weights | 8 | 2.6 to 2.95 s | when the line is done |
| Qwen TTS 0.6B, native build (qwentts.cpp), Q8 weights | all, with 16 threads | 0.93 s | 0.17 to 0.19 s |
| Qwen TTS 0.6B, the native build | 8 | 1.05 s | 0.20 to 0.24 s |
| Qwen TTS 0.6B, the native build | 4 | 1.40 s | 0.28 to 0.32 s |
| Qwen TTS 0.6B, the native build | 8 slow cores | 1.86 s | 0.32 to 0.34 s |

Qwen TTS's native build is a program of about 2 MB, with 1.26 GB of weights, and used 3.5 GB of memory at its peak. It was
built from source for the processor only, and timed through its own local server after one warm-up request.

With a graphics card, a laptop's starts each sentence in about a second (`docs/README.md`). A sentence of 3.4 seconds took
GPT-SoVITS 5.7 to 8.3 seconds on the processor, so it would need to be made whole before it plays. Loading takes 12 to 35
seconds.

## How long a sample Pocket TTS wants

On the newest models, with five voices and up to three lines a length. A line counts as whole when the app's ears hear it
nearly word for word.

| Language | Sample | Whole lines |
|---|---|---|
| English | 10 to 12 s | 15 of 15 |
| English | 22 to 25 s | 13 of 13 |
| English | 27 to 31 s | 15 of 15 |
| English | 33 to 37 s | 14 of 15 |
| English | 38 to 44 s | 6 of 14 |
| English | 50 s or more | 0 of 6 |
| Japanese | up to 50 s | all |
| Japanese | 66 s or more | 0 of 5 |
| Chinese | 10 to 11 s | 6 of 6 |
| Chinese | 16 to 17 s | 10 of 12 |

By ear the user preferred about 30 seconds (five clips). The model carries on from where a sample stops, so a sample should
end on a short pause.

## How the voices compared

Speaker match here is GPT-SoVITS's ERes2NetV2. A voice's own clips score 0.91 to 0.96 against the average of its clips. It
says whose voice something sounds like, and nothing about how natural it sounds, so the choices were made by ear.

- **Pocket TTS in English,** Eve and George copied from 27 to 30 seconds of themselves: 0.90 and 0.92 on the newest model. In
  the app's runtime with the January export, 0.86 and 0.74, with the first word garbled in 4 or 5 lines of 15.
- **Pocket TTS in Chinese** is a community model, it lost lines from longer samples, and the user found it poor.
- **Qwen TTS 0.6B's built-in voices:** all 132 takes kept their words, by the app's ears. Between a voice's own takes on the
  native build: Serena 0.87 and Aiden 0.84 in Chinese, Ono Anna 0.78 and Aiden 0.81 in Japanese. For scale, Pocket TTS's
  English scores 0.87 and 0.89. Each voice said three lines, so a rarer failure wouldn't show yet.
- **Qwen TTS forces a dialect** on Dylan (Beijing) and Eric (Sichuan) whenever the language is Chinese, and the native build
  copies that.

## Published

- **GPT-SoVITS v2ProPlus** (its README): 0.526 s of work per second of speech on an M4's processor, 0.028 on an RTX 4060 Ti,
  0.014 on an RTX 4090. That's a long text in batches, so it isn't comparable with the sentences above.
- **GPT-SoVITS on Apple Silicon:** a Rust and MLX port reports about 4 times real time on an M3 Max. A preprint ran the
  whole of it on an M4 Pro with 24 GB: fine-tuning 37 minutes of speech took about 70 minutes, and first audio in a full
  voice agent about 1.6 seconds, after fixing seven platform problems.
- **Kokoro on Apple Silicon:** with MLX, about 12 to 15 times real time on an M1 Max and 22 on an M4 Max.
- **Pocket TTS** (Kyutai, about 100M parameters, clones from a sample): its README reports about 6 times real time on a
  MacBook Air M4 using 2 processor cores, about 2.3 to 2.5 times on a cloud x86 machine with 4 virtual cores, and about 200 ms
  to the first audio. A standalone Windows x86 binary exists. Its official models are English, French, German, Portuguese,
  Italian, Spanish and Dutch; Chinese and Japanese are community models. Its terms forbid cloning a voice without consent.
- **Integrated graphics:** no real-time figures were found for any of these on Intel or AMD integrated graphics. ONNX
  exports with DirectML exist for GPT-SoVITS, without timings. Don't count on it.

## What follows

- **Preview 3 speaks with three engines** (the spec's "The voice"): Pocket TTS for English, which comes with the app; Qwen TTS
  for Chinese and Japanese, one download of 1.26 GB; and GPT-SoVITS for the closest copy of a voice, with v2ProPlus by
  default on a PC without a card, where the page says what to expect and still offers it.
- **Pocket TTS's newest models** aren't yet in a form the app's runtime loads (#280).
- **Not measured yet:** Qwen TTS on a graphics card or integrated graphics, on a real low-end PC, or inside the app.

Sources:
- [GPT-SoVITS README](https://raw.githubusercontent.com/RVC-Boss/GPT-SoVITS/main/README.md)
- [OminiX-MLX: GPT-SoVITS on MLX](https://mintlify.com/OminiX-ai/OminiX-MLX/tts/gpt-sovits)
- [Benchmarking Real-Time Voice Cloning on Consumer Apple Silicon (SSRN)](https://papers.ssrn.com/sol3/papers.cfm?abstract_id=6540098)
- [vllm-mlx audio benchmarks](https://vllm-mlx.is-a.dev/benchmarks/audio/)
- [Pocket TTS README](https://raw.githubusercontent.com/kyutai-labs/pocket-tts/main/README.md)
- [Aemeath ONNX GPT-SoVITS voice pack (DirectML provider)](https://huggingface.co/Mark42IRP/Aemeath_onnx_GSV_model)
- [sherpa-onnx's text-to-speech models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models)
- [Qwen3-TTS 0.6B CustomVoice model card](https://huggingface.co/Qwen/Qwen3-TTS-12Hz-0.6B-CustomVoice)
- [qwentts.cpp](https://github.com/ServeurpersoCom/qwentts.cpp)
- [Qwen3-TTS GGUF weights](https://huggingface.co/ldov/Qwen3-TTS-GGUF)
