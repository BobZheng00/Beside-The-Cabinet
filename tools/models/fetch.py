"""Downloads the local models she runs on this PC into src/InsertCoin.Game/models/, which git ignores (they are
large); the build ships whichever are present.

    python tools/models/fetch.py                  # all three
    python tools/models/fetch.py small            # only the small memory model (a lighter build)

multilingual  memory search: multilingual-e5-small (intfloat, MIT), about 100 languages, 113 MB
small         memory search: bge-small-zh-v1.5 (BAAI, MIT), Chinese first and fair English, 23 MB
listening     her ears: SenseVoice-Small (FunAudioLLM; FunASR model licence, which asks for attribution), Chinese,
              English, Japanese, Korean and Cantonese, 240 MB
The app works without any of them (memory search by shared words only; no talking to her aloud).
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
        urllib.request.urlretrieve(f"{base}/{source}", partial)
        partial.replace(path)
    (target / "LICENSE.txt").write_text(licence, encoding="utf-8")


def main():
    wanted = sys.argv[1:] or list(MODELS)
    for key in wanted:
        if key not in MODELS:
            sys.exit(f"unknown model '{key}': choose from {', '.join(MODELS)}")
        fetch(key)
    print("done")


if __name__ == "__main__":
    main()
