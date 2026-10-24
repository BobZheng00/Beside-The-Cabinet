"""Downloads the local models she runs on this PC into src/InsertCoin.Game/models/, which git ignores (they are
large); the build ships whichever are present.

    python tools/models/fetch.py                  # all of them
    python tools/models/fetch.py small            # only the small memory model (a lighter build)
    python tools/models/fetch.py check            # which an export would carry (build.cmd runs it; nothing is fetched)

multilingual  memory search: multilingual-e5-small (intfloat, MIT), about 100 languages, 113 MB
small         memory search: bge-small-zh-v1.5 (BAAI, MIT), Chinese first and fair English, 23 MB
listening     her ears: SenseVoice-Small (FunAudioLLM; FunASR model licence, which asks for attribution), Chinese,
              English, Japanese, Korean and Cantonese, 240 MB
turns         hands-free turns: Silero's voice detector (MIT, 0.6 MB) and Pipecat's Smart Turn v3.2 at full
              precision (BSD-2, 32 MB), which tell when the player starts talking and when they have finished
The app works without any of them (memory search by shared words only; no talking to her aloud; without the turn
models, she listens only while the talk key is held or she is held).
"""
import sys
import urllib.request
from pathlib import Path

MODELS = {
    "multilingual": ("multilingual-e5-small", "https://huggingface.co/Xenova/multilingual-e5-small/resolve/main",
                     {"onnx/model_quantized.onnx": "model.onnx", "sentencepiece.bpe.model": "sentencepiece.bpe.model"},
                     "multilingual-e5-small (https://huggingface.co/intfloat/multilingual-e5-small), MIT License.\n"
                     "Liang Wang, Nan Yang, Xiaolong Huang, Linjun Yang, Rangan Majumder, Furu Wei: Multilingual E5 Text Embeddings (2024).\n"
                     "ONNX export and 8-bit quantisation: https://huggingface.co/Xenova/multilingual-e5-small.\n"
                     "The SentencePiece model is XLM-RoBERTa's (MIT, Facebook AI).\n"),
    "small": ("bge-small-zh-v1.5", "https://huggingface.co/Xenova/bge-small-zh-v1.5/resolve/main",
              {"onnx/model_quantized.onnx": "model.onnx", "vocab.txt": "vocab.txt"},
              "bge-small-zh-v1.5 (https://huggingface.co/BAAI/bge-small-zh-v1.5), MIT License, Beijing Academy of Artificial Intelligence.\n"
              "ONNX export and 8-bit quantisation: https://huggingface.co/Xenova/bge-small-zh-v1.5.\n"),
    "listening": ("sense-voice", "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main",
                  {"model.int8.onnx": "model.int8.onnx", "tokens.txt": "tokens.txt"},
                  "SenseVoice-Small (https://github.com/FunAudioLLM/SenseVoice), Tongyi Lab, Alibaba Group.\n"
                  "FunASR Model Open Source License Agreement 1.1 (https://github.com/modelscope/FunASR/blob/main/MODEL_LICENSE):\n"
                  "free to use, copy, modify and share, keeping its name and this attribution.\n"
                  "ONNX export and 8-bit quantisation for sherpa-onnx: "
                  "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.\n"),
    "turns": ("turn-taking", "",
              {"https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx": "silero_vad.onnx",
               "https://huggingface.co/pipecat-ai/smart-turn-v3/resolve/main/smart-turn-v3.2-gpu.onnx": "smart-turn-v3.2.onnx"},
              "Silero VAD (https://github.com/snakers4/silero-vad), MIT License, Silero Team: the copy sherpa-onnx ships.\n"
              "Smart Turn v3.2 (https://huggingface.co/pipecat-ai/smart-turn-v3), BSD 2-Clause License, Copyright (c) 2024-2025, Daily.\n"
              "Its full-precision build (smart-turn-v3.2-gpu.onnx), which runs on the processor too.\n"),
}


def fetch(key):
    folder, base, files, licence = MODELS[key]
    target = Path(__file__).resolve().parents[2] / "src" / "InsertCoin.Game" / "models" / folder
    target.mkdir(parents=True, exist_ok=True)
    for source, name in files.items():
        path = target / name
        if path.exists() and path.stat().st_size > 0:
            print(f"have {folder}/{name}")
            continue
        print(f"fetching {folder}/{source} ...", flush=True)
        partial = path.with_suffix(path.suffix + ".part")
        urllib.request.urlretrieve(source if source.startswith("https://") else f"{base}/{source}", partial)
        partial.replace(path)
    (target / "LICENSE.txt").write_text(licence, encoding="utf-8")


def check():
    """Says which models an export would carry, and fails (exit 1) when none is there or one is there only in part:
    an emptied or half-fetched folder, which would otherwise ship quietly without memory search or her ears. A model
    whose folder is absent is left out on purpose (a lighter build), and only said."""
    root = Path(__file__).resolve().parents[2] / "src" / "InsertCoin.Game" / "models"
    have, problems = [], []
    for key, (folder, _, files, _) in MODELS.items():
        # its LICENSE.txt too: release.py takes the notices from it
        missing = [name for name in [*files.values(), "LICENSE.txt"]
                   if not (root / folder / name).is_file() or (root / folder / name).stat().st_size == 0]
        if not missing:
            have.append(folder)
        elif (root / folder).exists():
            problems.append(f"models/{folder} lacks {', '.join(missing)} (python tools/models/fetch.py {key})")
        else:
            print(f"leaves out {folder} (python tools/models/fetch.py {key} adds it)")
    if not have and not problems:
        problems.append("models/ holds none of them (python tools/models/fetch.py fetches them all)")
    if have:
        print(f"carries {', '.join(have)}")
    for problem in problems:
        print(problem)
    if problems:
        sys.exit("The models folder lacks files fetch.py puts there: fetch them (a download), then build again.")


def main():
    if sys.argv[1:] == ["check"]:
        return check()
    wanted = sys.argv[1:] or list(MODELS)
    for key in wanted:
        if key not in MODELS:
            sys.exit(f"unknown model '{key}': choose from {', '.join(MODELS)}")
        fetch(key)
    print("done")


if __name__ == "__main__":
    main()
