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
voice         their voice in English: Kyutai's Pocket TTS (CC BY 4.0), exported by pocket_export.py (#280), 185 MB
The app works without any of them (memory search by shared words only; no talking to her aloud; without the turn
models, she listens only while the talk key is held or she is held; without the voice, English stays in text).

Each file is pinned: fetched from a fixed revision (a Hugging Face commit; Silero's from sherpa-onnx's release, which
has none; Pocket TTS's export from a release of this repository) and kept only if its SHA-256 is the one below, so a
release ships exactly the files checked here. A file
already there that isn't the pinned one is fetched again; `check` fails on one.
"""
import hashlib
import sys
import urllib.request
from pathlib import Path

# folder, where its files come from, {source: (name in the folder, SHA-256)}, the licence written beside them
MODELS = {
    "multilingual": ("multilingual-e5-small",
                     "https://huggingface.co/Xenova/multilingual-e5-small/resolve/761b726dd34fb83930e26aab4e9ac3899aa1fa78",
                     {"onnx/model_quantized.onnx": ("model.onnx", "f80102d3f2a1229f387d3c81909990d8945513e347b0eab049f7de3c6f98c193"),
                      "sentencepiece.bpe.model": ("sentencepiece.bpe.model", "cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865")},
                     "multilingual-e5-small (https://huggingface.co/intfloat/multilingual-e5-small), MIT License.\n"
                     "Liang Wang, Nan Yang, Xiaolong Huang, Linjun Yang, Rangan Majumder, Furu Wei: Multilingual E5 Text Embeddings (2024).\n"
                     "ONNX export and 8-bit quantisation: https://huggingface.co/Xenova/multilingual-e5-small.\n"
                     "The SentencePiece model is XLM-RoBERTa's (MIT, Facebook AI).\n"),
    "small": ("bge-small-zh-v1.5", "https://huggingface.co/Xenova/bge-small-zh-v1.5/resolve/75c43b069aac4d136ba6bc1122f995fedcfd2781",
              {"onnx/model_quantized.onnx": ("model.onnx", "15b717c382bcb518ba457b93ea6850ede7f4f1cd8937454aa06972366cd19bcc"),
               "vocab.txt": ("vocab.txt", "45bbac6b341c319adc98a532532882e91a9cefc0329aa57bac9ae761c27b291c")},
              "bge-small-zh-v1.5 (https://huggingface.co/BAAI/bge-small-zh-v1.5), MIT License, Beijing Academy of Artificial Intelligence.\n"
              "ONNX export and 8-bit quantisation: https://huggingface.co/Xenova/bge-small-zh-v1.5.\n"),
    "listening": ("sense-voice",
                  "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/2365baeacb507f821a0c8120fcee3d484dba7a07",
                  {"model.int8.onnx": ("model.int8.onnx", "c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51"),
                   "tokens.txt": ("tokens.txt", "f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc")},
                  "SenseVoice-Small (https://github.com/FunAudioLLM/SenseVoice), Tongyi Lab, Alibaba Group.\n"
                  "FunASR Model Open Source License Agreement 1.1 (https://github.com/modelscope/FunASR/blob/main/MODEL_LICENSE):\n"
                  "free to use, copy, modify and share, keeping its name and this attribution.\n"
                  "ONNX export and 8-bit quantisation for sherpa-onnx: "
                  "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.\n"),
    "turns": ("turn-taking", "",
              {"https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx":
                   ("silero_vad.onnx", "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6"),
               "https://huggingface.co/pipecat-ai/smart-turn-v3/resolve/f766f81d3cfdf7737ac64aad813d91bbfd56bf93/smart-turn-v3.2-gpu.onnx":
                   ("smart-turn-v3.2.onnx", "ab8dc64b88713f90b571c15b714bd1330e6c883cad8763dacf65c9376dc539be")},
              "Silero VAD (https://github.com/snakers4/silero-vad), MIT License, Silero Team: the copy sherpa-onnx ships.\n"
              "Smart Turn v3.2 (https://huggingface.co/pipecat-ai/smart-turn-v3), BSD 2-Clause License, Copyright (c) 2024-2025, Daily.\n"
              "Its full-precision build (smart-turn-v3.2-gpu.onnx), which runs on the processor too.\n"),
    "voice": ("pocket-tts-en", "https://github.com/BobZheng00/Beside-The-Cabinet/releases/download/pocket-tts-en-1",
              {"text_conditioner.onnx": ("text_conditioner.onnx", "94721d7d170d7810009899dad4f795cfbfb66c491e83212463c38ad3466ca6eb"),
               "encoder.onnx": ("encoder.onnx", "a4db1b4eb1046a6549ad9e5e21946d84b860fd611e2d4f58c6d2454399441d71"),
               "lm_main.int8.onnx": ("lm_main.int8.onnx", "31b7c05f52ae5ef6908e5ad2d27ecde38e571a8c9af60923030ec12e72729b84"),
               "lm_flow.int8.onnx": ("lm_flow.int8.onnx", "7ae46f00a4e7de1a69a8165f618a5a7fe835896666f674d9e64a2c03f7a2bd7b"),
               "decoder.onnx": ("decoder.onnx", "feb0e7df0c922a03172098766f993f1d7ff16216635ed264c6ff36474be9a746"),
               "vocab.json": ("vocab.json", "2db0001bb15a3dbb42f5c50f9973d6a610e69d8276d1a732bcad0e523c99186a"),
               "token_scores.json": ("token_scores.json", "0ca95ed85ec94ee887b333c80da05f6119264eebad05ed5058857e55084b5b53")},
              "Pocket TTS (https://github.com/kyutai-labs/pocket-tts), Kyutai: code MIT, weights CC BY 4.0\n"
              "(https://creativecommons.org/licenses/by/4.0/). Its English model, changed: exported to ONNX by\n"
              "tools/models/pocket_export.py (#280), its main model and flow quantised to 8 bits.\n"
              "Eve's and George's voices are speakers of the VCTK corpus (CSTR, University of Edinburgh), CC BY 4.0.\n"),
}


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def fetch(key):
    folder, base, files, licence = MODELS[key]
    target = Path(__file__).resolve().parents[2] / "src" / "InsertCoin.Game" / "models" / folder
    target.mkdir(parents=True, exist_ok=True)
    for source, (name, pinned) in files.items():
        path = target / name
        if path.is_file() and sha256(path) == pinned:
            print(f"have {folder}/{name}")
            continue
        print(f"fetching {folder}/{source} ...", flush=True)
        url = source if source.startswith("https://") else f"{base}/{source}"
        partial = path.with_suffix(path.suffix + ".part")
        urllib.request.urlretrieve(url, partial)
        got = sha256(partial)
        if got != pinned:
            partial.unlink()
            sys.exit(f"{url} is not the file pinned here (its SHA-256 is {got}, not {pinned}): it was not kept.")
        partial.replace(path)
    (target / "LICENSE.txt").write_text(licence, encoding="utf-8")


def check():
    """Says which models an export would carry, and fails (exit 1) when none is there, one is there only in part (an
    emptied or half-fetched folder, which would otherwise ship quietly without memory search or her ears), or one holds
    a file that isn't the pinned one. A model whose folder is absent is left out on purpose (a lighter build), and
    only said."""
    root = Path(__file__).resolve().parents[2] / "src" / "InsertCoin.Game" / "models"
    have, problems = [], []
    for key, (folder, _, files, _) in MODELS.items():
        # its LICENSE.txt too: release.py takes the notices from it
        missing = [name for name in [*(name for name, _ in files.values()), "LICENSE.txt"]
                   if not (root / folder / name).is_file() or (root / folder / name).stat().st_size == 0]
        unpinned = [name for name, pinned in files.values()
                    if name not in missing and sha256(root / folder / name) != pinned]
        if not missing and not unpinned:
            have.append(folder)
        elif (root / folder).exists():
            if missing:
                problems.append(f"models/{folder} lacks {', '.join(missing)} (python tools/models/fetch.py {key})")
            problems += [f"models/{folder}/{name} isn't the pinned file (python tools/models/fetch.py {key} fetches it again)"
                         for name in unpinned]
        else:
            print(f"leaves out {folder} (python tools/models/fetch.py {key} adds it)")
    if not have and not problems:
        problems.append("models/ holds none of them (python tools/models/fetch.py fetches them all)")
    if have:
        print(f"carries {', '.join(have)}")
    for problem in problems:
        print(problem)
    if problems:
        sys.exit("The models folder lacks files fetch.py puts there, or holds others: fetch them (a download), then build again.")


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
