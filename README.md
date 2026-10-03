# INSERT COIN

A desktop arcade cabinet that pays out gaming time for solved LeetCode problems and charges it back while Steam
games run. Soft enforcement only: warnings, sounds and IOU tickets — it never closes a game.

Design: [docs/specs/2026-09-30-insert-coin-design.md](docs/specs/2026-09-30-insert-coin-design.md)

## Run

| | |
|---|---|
| `builds\InsertCoin\InsertCoin.exe` | The standalone app (after `build.cmd`). Real profile in `%APPDATA%\InsertCoin`, real Steam detection. No dev keys. |
| `run.cmd` | Same as the app, but run from source through the Godot editor binary (debug build, dev keys on). |
| `demo.cmd` | Demo profile (`%APPDATA%\InsertCoin-demo`), Steam ignored. Safe to play with. |

Drag the cabinet by its body. The big gold button asks LeetCode right away. The tray icon has the menu
(size, companion, speech bubbles, characters, mute, in-game tickets, energy saver, always on top, start with
Windows, change player, quit).

The companion on the stool follows the mouse pointer with her eyes and head, waves when clicked, and reacts to
what the cabinet does: cheers for coins, glances at the screen, worries about debt, dozes while the session is
paused. She says a line about it in a speech bubble; click her for where things stand. The tray menu switches her
off, silences her, picks another character or adds one; a model file dropped on the cabinet adds it too.

She can also talk. In tray → "Chat settings…" pick a provider (OpenAI, Anthropic, OpenRouter, DeepSeek, Qwen, a
local Ollama or LM Studio, or any OpenAI-compatible server), paste your own API key and name a model. Then clicking
her opens a slip at the foot of the stool: type, press Enter, and her answer streams into the bubble while she acts
it out. She knows the cabinet's numbers and remembers:

- the conversation itself, one continuous thread per character; older parts are folded into a summary as it grows;
- facts about you (shared by every character) and about the two of you, plus dated moments. When you just tell her
  something worth keeping she says she'll remember it and jots it in a little notebook; everything else is picked
  up quietly by upkeep, which reads the conversation once it has been quiet for ten minutes (two while a game
  runs);
- two short notes she always has in mind, rewritten by upkeep when something important changes;
- and before each reply, whatever of all that relates to what you said: by meaning, with a small model that runs
  on this PC (so "nervous about tomorrow" finds your interview, in English or Chinese), and by shared words.
  Chat settings → "Memory search" picks the multilingual model (about 100 languages, about 200 MB of memory while
  chatting), the small one (Chinese and English, about 50 MB) or shared words only;
- what has not come up in a long time fades (a forgetting curve: importance and every recall make a fact last
  longer; pinned and the most important ones never fade), and moments older than two weeks are combined a week
  at a time.

A SillyTavern-style character card (`.png` or `.json`, V1 to V3) dropped on the cabinet or picked with "Add a
character…" gives whoever is on the stool its personality, example dialogue and lorebook (background that comes
up when its keywords are mentioned); she greets you as the card says.

Tray → "Memory book…" shows all of it: edit, pin (always in mind, upkeep leaves it alone) or delete anything, or
forget everything. It stays in the profile folder (`companion\memory.db`); the key is encrypted for your Windows
account. Without a provider she keeps to her canned lines.

The window draws at 60 frames a second, and at 15 while a game runs or with "Energy saver" ticked (moments
worth watching still get a few seconds at full pace).

Dev keys (debug builds, click the cabinet first): `F1/F2/F3` solve Easy/Medium/Hard · `F5` fake game on/off ·
`F6` clock ×60 · `F7` AFK on/off · `F9` wipe state · `Esc` quit.
Arguments after `--`: `--data=DIR`, `--no-steam`, `--script=hard@2,game@5,warp@5` (dev actions at given seconds).
Companion dev actions: `moment:cheer` (or `pump`, `greet`, `glance`, `alarm`, `worry`, `doze`, `wake`),
`point:x:y` (a pretend mouse pointer at that canvas point), `character:id` (swap who sits on the stool),
`say:some_text` (a speech bubble), `quiet` (toggle speech bubbles), `click` (as if she were clicked), `saver`
(toggle the energy saver), `legs:left|right|both|ring` (which feet hang off the stool's ring), `add:C:\path\model.glb`
(as if the file were dropped on the window), `pick` (the "Add a character…" dialog), `tray:x:y` (open the tray
menu at that screen point), `chat:some_text` (say something to her), `chatbox` (open the slip), `chatsettings`,
`memorybook`, `upkeep` (digest the conversation now) and `moment:note`. `--offscreen` opens the app's windows off
every monitor; `tools\dev\shoot.ps1 -Hidden` uses it to test without anything appearing on screen.

## Layout

```
src/InsertCoin.Core       rules as a pure function (Ledger.Step) — no I/O, no clock
src/InsertCoin.Adapters   LeetCode GraphQL source, Steam registry detector, atomic JSON store
src/InsertCoin.Mind       the companion's mind, no Godot: provider connection (Microsoft.Extensions.AI), conversation
                          store (SQLite), prompt, stage directions, auto-compaction, encrypted key
src/InsertCoin.Game       Godot 4.7 (.NET) app: cabinet scene, companion, director, desktop window, tray, audio
tests/                    xUnit: every rule with a fake clock; adapters with recorded responses; the mind with a fake model
tools/art                 cozy_cabinet.py — the cabinet is a 3D model rendered to SVG, exported as skin layers;
                          app_icon.py — the app icon (.ico for the .exe, .png for the window)
tools/sfx                 generate.py — chip-style synth that writes the sound effects
tools/models              fetch.py — downloads the local embedding models for her memory search
tools/dev                 shoot.ps1 runs the app in a throwaway muted profile and screenshots it (-Window: one of its
                          own windows too); sheet.ps1 makes contact sheets
art/cozy                  previews and layer SVGs (sources of the PNGs in the game)
```

## Build

```
dotnet test                                   # rules + adapters
dotnet build src/InsertCoin.Game              # game assembly
python tools/art/cozy_cabinet.py --skin       # regenerate art -> src/InsertCoin.Game/skins/cozy (needs Chrome)
python tools/sfx/generate.py                  # regenerate sounds -> src/InsertCoin.Game/sounds
python tools/art/app_icon.py                  # regenerate the app icon -> src/InsertCoin.Game/icon.ico, icon.png (needs Chrome)
python tools/models/fetch.py                  # her memory search models -> src/InsertCoin.Game/models (not in git: 140 MB)
```

The models are optional: without them she recalls by shared words only. `build.cmd` fetches both when the folder
is missing; `python tools/models/fetch.py small` beforehand gives a lighter build with only the small one.

### Standalone build

```
python tools/export/fetch_template.py --get <folder>/templates/4.7.2-stable-mono   # once: 40 MB instead of the 1.2 GB archive
build.cmd                                                                                  # -> builds\InsertCoin\InsertCoin.exe + data folder
```

The export preset points at that template path. Godot's exporter needs `src/InsertCoin.Game/InsertCoin.Game.sln`
(the repo-level solution is `.slnx`). Only one copy of the app runs at a time: a second launch exits quietly.
Godot's exporter writes the app icon (`icon.ico`) and the product name into the `.exe` itself; no `rcedit` needed.

Godot 4.7.2 .NET is expected at `<tools>\Godot\Godot_v4.7.2-stable_mono_win64\`. After changing art or sounds,
re-import: `Godot_..._console.exe --headless --path src/InsertCoin.Game --import`.
The project uses the Compatibility renderer: Forward+ cannot do transparent windows here.

## Skins

A skin is a folder in `src/InsertCoin.Game/skins/<id>` with `skin.json`: full-canvas layers (`base` plus the parts
that animate), sprites (coins, ticket) and anchor points (screen quad, bulbs, tray, slots, CHECK button). Code binds
to anchors, never to art, so a new skin needs no code changes.

`skin.json` also carries `world`: the 3D camera and stool position the art was drawn with, so live 3D can be
rendered into the same picture.

Art revert points: git tags `art-v3-boxy`, `art-v4-traditional`, `art-v5-perspective`.

## Companion

A VRM character (`src/InsertCoin.Game/characters/<id>/<id>.vrm`) rendered in an off-screen 3D viewport through the
skin's own camera and shown as a layer over the cabinet art. Code in `src/InsertCoin.Game/Companion`:

- `CompanionView` sets up the viewport and camera, feeds the mouse pointer in, answers hit tests.
- `ToonMaterials` + `toon.gdshaderinc` replace the model's materials: two flat tones from one fixed light, texture
  values remapped onto the skin's palette, a pink wash from the CRT, thin ink lines inside the figure.
- `companion_layer.gdshader` inks the silhouette with the art's outline colour and width.
- `SpeechBubble` draws what she says: canned lines from `App/Lines.cs` (built from the ledger's numbers) type
  themselves out; replies from her mind start as thinking dots and grow as they stream in. `ChatBox` is the slip
  you type on, `Paper` the shared look (cream paper, the cabinet's ink, Fredoka).
- `App/Chat.cs` connects her to `InsertCoin.Mind`: sends what you type, acts out stage directions (`[cheer]`,
  `[wave]`, `[worry]`, `[note: …]`…), logs cabinet events into the conversation, folds it when it grows, runs
  upkeep every minute. `App/ChatSettings.cs` is the provider window, `App/MemoryBook.cs` the memory book. In
  `InsertCoin.Mind`: `Recall` picks memories, `Upkeep` holds the digest and note-rewrite requests. Design: [docs/specs/2026-10-02-companion-helper.md](docs/specs/2026-10-02-companion-helper.md),
  memory: [docs/research/2026-10-02-companion-memory.md](docs/research/2026-10-02-companion-memory.md).
- `Rig`, `Puppet`, `Face` move her. Three layers each frame: poses (seated, plus reaction poses switched by
  short timelines, hands placed by two-bone IK), springs (every joint follows its pose as a damped spring, looser
  towards the hand, so moves overshoot and settle), and life on top (breathing, sway, head drift, fidgets, eyes
  that jump between fixation points, blinks). Poses use VRM humanoid bone names and are fitted to the character's
  proportions (hip height, leg length), so they are not tied to one model.

The character stays at the 3D origin and the camera is placed around her: the VRM plugin's hair physics goes wrong
when the character itself is moved or turned.

### Adding a character

- **As a user, no tools needed:** tray menu → "Add a character…" and pick a `.vrm`, `.glb`, `.gltf` or a `.zip`
  holding one (Sketchfab's glTF downloads come zipped), or drop the file on the cabinet. It is copied into
  `characters` in the profile folder (`%APPDATA%\InsertCoin\characters`; a `.gltf` gets a folder of its own with
  the `.bin` and textures it refers to) and takes the stool at once. A file of the same name is replaced.
  "Open characters folder" shows that folder; files put there by hand appear after a restart. Files are read at
  run time, in the standalone build too.
- **Built into the app:** put the model at `src/InsertCoin.Game/characters/<id>/<id>.vrm` (lower-case id) with a
  note of its source and licence, then re-import:
  `Godot_..._console.exe --headless --path src/InsertCoin.Game --import`.

Either way `"Companion": "<id>"` in `settings.json` selects it (the id is the file name).

Who they are when they talk is a character card if they have one (`<id>.card.png` or `.json` in the characters
folder, or `card.png` / `card.json` in their own folder; dropping a card on the cabinet puts it there), else a
plain-text description: `persona.md` in the character's folder (built-in ones ship one), or `<file name>.md` beside
a loose model file. Without either they are a plain friendly companion.

What each kind of file gets:

| | VRM made with VRoid Studio | other VRM | GLB / glTF with a humanoid skeleton |
|---|---|---|---|
| sits, gestures, follows the pointer | yes | yes | yes |
| ink and two-tone shading | yes | yes | yes |
| cabinet palette | yes (by VRoid's material names) | own colours | own colours |
| expressions and blinking | yes | yes | only if its blend shapes have recognisable names |
| hair and cloth physics | yes | yes | bones named hair, skirt, tail… get springs; legs are solid to them, arms to hair; skirt panels follow the thighs |

A plain GLB does not say which bone is which, so `HumanoidMap` works it out from the usual naming habits (Mixamo,
Unreal, Blender, Biped, Daz) and the skeleton's shape; `Rig` then copes with any units, facing and rest pose (T or
A). A model without a skeleton (a sculpt, most OBJ-derived files) cannot be posed: the stool stays empty and the
CRT says why. OBJ and USDZ are not read at all; they carry no skeleton.

The skin's canvas is wider and taller than the cabinet art (see `layout()` in `tools/art/cozy_cabinet.py`) so that
a raised arm stays inside the window.

## Assets

Fonts: Press Start 2P and Fredoka (SIL OFL 1.1, licenses in `src/InsertCoin.Game/fonts`). Art and sounds are
original, generated by the tools above.

Companion: the VRM importer and MToon shader are [godot-vrm](https://github.com/V-Sekai/godot-vrm) (MIT, licences in
`src/InsertCoin.Game/addons`). The characters are VRoid's samples "Sendagaya Shino" and "Sakurada Fumiriya",
released by pixiv under CC0 (see `SOURCE.txt` in each folder under `src/InsertCoin.Game/characters`); their
`persona.md` descriptions are ours. Memory search models (fetched, not in git): multilingual-e5-small (intfloat,
MIT) and bge-small-zh-v1.5 (BAAI, MIT), ONNX exports by Xenova; each folder under `src/InsertCoin.Game/models`
carries its licence note. Run with ONNX Runtime (MIT) and Microsoft.ML.Tokenizers (MIT).
