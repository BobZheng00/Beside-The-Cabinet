# The voice without an NVIDIA card

*Measured and read 2026-11-08, for Preview 3 (`docs/specs/2026-11-08-a-new-player-gets-it.md`). The user: "will the small
tts model work fine on modern apple sillicon or x86 cpu or iGPU? i guess we want to bring it up to user to let them see if
they like the performance instead of hiding it".*

**In short.** GPT-SoVITS, the engine the app uses, runs without a card only with its lighter model, v2ProPlus, and even that
can't keep up with speech on a processor: a sentence of about 3 seconds takes 6 to 8 seconds to make. Its default for a
voice with no trained model, v4, is far too slow there, at 24 to 31 seconds for even a short line. A small model without voice cloning (Kokoro) keeps up on 4
cores or more. Apple Silicon looks better than an x86 processor for all of them. Nothing usable was found for integrated
graphics.

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

With a graphics card, a laptop's starts each sentence in about a second (`docs/README.md`). A sentence of 3.4 seconds took
GPT-SoVITS 5.7 to 8.3 seconds on the processor, so it would need to be made whole before it plays. Loading takes 12 to 35
seconds.

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

- **Preview 3 keeps the one engine, with v2ProPlus by default on a PC without a card.** The page says what to expect and
  still offers it, and the player sees this PC's own speed once it's installed.
- **A smaller engine is worth a trial later**, for PCs without a card and for a Mac build: Pocket TTS first, as it clones
  from a sample. It would need a download, and voices in Chinese and Japanese checked.

Sources:
- [GPT-SoVITS README](https://raw.githubusercontent.com/RVC-Boss/GPT-SoVITS/main/README.md)
- [OminiX-MLX: GPT-SoVITS on MLX](https://mintlify.com/OminiX-ai/OminiX-MLX/tts/gpt-sovits)
- [Benchmarking Real-Time Voice Cloning on Consumer Apple Silicon (SSRN)](https://papers.ssrn.com/sol3/papers.cfm?abstract_id=6540098)
- [vllm-mlx audio benchmarks](https://vllm-mlx.is-a.dev/benchmarks/audio/)
- [Pocket TTS README](https://raw.githubusercontent.com/kyutai-labs/pocket-tts/main/README.md)
- [Aemeath ONNX GPT-SoVITS voice pack (DirectML provider)](https://huggingface.co/Mark42IRP/Aemeath_onnx_GSV_model)
