# Notice

Beside the Cabinet

Required Notice: Copyright 2026 Shenyan Zheng (https://github.com/BobZheng00/beside-the-cabinet)

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
- **The built-in characters' personas and voices**: `src/InsertCoin.Game/characters/*/persona.md` and
  `src/InsertCoin.Game/characters/*/voice.zip`.

They may be used only as part of this app, unchanged, or in a noncommercial fork of it; never on their own, never in
another project, and never commercially.

## Others' work in it

| What | Whose | Licence |
|---|---|---|
| Godot Engine (the app runs on it) | Juan Linietsky, Ariel Manzur and contributors | MIT |
| Shino and Fumiriya's 3D models (`characters/*/*.vrm`) | pixiv Inc. (VRoid Studio sample models) | CC0 |
| Their voices were made with Kokoro-82M | hexgrad | Apache-2.0 |
| Fredoka, Press Start 2P (`fonts/`, licences beside them) | The Fredoka Project Authors; The Press Start 2P Project Authors | SIL Open Font License 1.1 |
| VRM add-on, MToon shader (`addons/`, licences inside) | V-Sekai contributors; Masataka SUMI | MIT |
| .NET libraries: Anthropic, Microsoft.Extensions.AI, Microsoft.Data.Sqlite, ONNX Runtime, Microsoft.ML.Tokenizers, SharpCompress | their authors | MIT |
| sherpa-onnx | Xiaomi Corporation and its contributors | Apache-2.0 |

Models fetched into `src/InsertCoin.Game/models/` (not in this repository; each folder carries its own `LICENSE.txt`):
SenseVoice-Small (Tongyi Lab, Alibaba Group; FunASR Model Open Source License 1.1, which asks for its name and
attribution to be kept), multilingual-e5-small and bge-small-zh-v1.5 (MIT), Silero VAD (MIT), Smart Turn v3.2 (BSD
2-Clause, Daily). GPT-SoVITS (MIT) isn't part of the app: it is installed on the player's PC from its makers' own
download, when the player asks.
