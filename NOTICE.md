# Notice

Beside the Cabinet

Required Notice: Copyright 2026 Shenyan Zheng (https://github.com/BobZheng00/Beside-The-Cabinet)

## What the licence covers

The source code, documents and tools in this repository are licensed under the PolyForm Noncommercial License 1.0.0
([LICENSE.md](LICENSE.md)): anyone may use, change and share them for free, for any noncommercial purpose. Selling them,
or using them commercially, is not allowed. Only the copyright holder may publish the app commercially.

## What it doesn't cover

These stay the copyright holder's, with all rights reserved, and LICENSE.md grants nothing for them:

- **The name and look of the app**: "Beside the Cabinet" and its icon. A fork must go by another name
  and look different enough not to be taken for this app.
- **The cabinet's artwork and the app's sounds**: the pictures under `art/` and `src/InsertCoin.Game/skins/`, and the
  sounds under `src/InsertCoin.Game/sounds/`, along with what `tools/art` and `tools/sfx` make.
- **The built-in characters' personas and voices**: `src/InsertCoin.Game/characters/*/persona*.md` and
  `src/InsertCoin.Game/characters/*/voice.zip`, and the personas' text in the appendices of
  `docs/specs/2026-10-24-default-cards.md`.

They may be used only as part of this app, unchanged, or in a noncommercial fork of it; never on their own, never in
another project, and never commercially.

## Others' work in it

| What | Whose | Licence |
|---|---|---|
| Godot Engine (the app runs on it) | Juan Linietsky, Ariel Manzur and contributors | MIT |
| Shino and Fumiriya's 3D models (`characters/*/*.vrm`) | pixiv Inc. (VRoid Studio sample models) | CC0 |
| Their English voices, Eve and George (`characters/*/voice.zip`): speakers of the VCTK corpus as Pocket TTS speaks them | CSTR, University of Edinburgh; Kyutai | CC BY 4.0 |
| Fredoka, Press Start 2P (`fonts/`, licences beside them) | The Fredoka Project Authors; The Press Start 2P Project Authors | SIL Open Font License 1.1 |
| VRM add-on, MToon shader (`addons/`, licences inside) | V-Sekai contributors; Masataka SUMI | MIT |
| .NET libraries: Anthropic, Microsoft.Extensions.AI, Microsoft.Data.Sqlite, ONNX Runtime, Microsoft.ML.Tokenizers, SharpCompress | their authors | MIT |
| sherpa-onnx | Xiaomi Corporation and its contributors | Apache-2.0 |
| qwentts.cpp (`tts-server`, in a release's `qwen-tts/`), with ggml, cpp-httplib and yyjson, built by `tools/export/build_qwen_tts.py` | The omnivoice.cpp authors; The ggml authors; yhirose; YaoYuan | MIT |

Models fetched into `src/InsertCoin.Game/models/` (not in this repository; each folder carries its own `LICENSE.txt`):
SenseVoice-Small (Tongyi Lab, Alibaba Group; FunASR Model Open Source License 1.1, which asks for its name and
attribution to be kept), multilingual-e5-small (intfloat) and bge-small-zh-v1.5 (BAAI), both MIT, in Xenova's ONNX exports, Silero VAD (MIT), Smart Turn v3.2 (BSD
2-Clause, Daily), and Pocket TTS's English model (Kyutai, CC BY 4.0; its code MIT), changed: exported to ONNX by
`tools/models/pocket_export.py`, its main model and flow quantised to 8 bits, and hosted on this repository's release
`pocket-tts-en-1`. GPT-SoVITS (MIT) isn't part of the app: it is installed on the player's PC from its makers' own
download, when the player asks. The same goes for Qwen3-TTS's weights (Alibaba Cloud, Apache-2.0), fetched from
`ldov/Qwen3-TTS-GGUF` on Hugging Face, or HF-Mirror, when the player asks on Their voice.
