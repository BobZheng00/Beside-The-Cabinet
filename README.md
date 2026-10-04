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

She can speak aloud too, with a voice from GPT-SoVITS running on this PC (its official Windows package; a laptop
graphics card starts each sentence in about a second). Tray → "Voice settings…": point it at the GPT-SoVITS folder,
then give her a voice: drop on the cabinet (or add in the window) a voice's folder or `.zip`, which replaces her
voice, or clips, which join it. A voice is clips of one person, 3 to 10 seconds each, with the words said in them, and maybe a model someone
trained on them (a `.ckpt` and a `.pth`; v2 to v4 all work). The words come from a training list (any `.list`,
one line per clip: `clips/001.wav|hana|JA|今日も頑張ろうね。`), a `.lab` beside a clip, or the clip's file name
("【开心】今天也要加油哦.wav", the mood in brackets picking it for that mood); if none says them the window asks.
"What goes in a voice folder?" in the window says the same. For example:

```
hana/
  hana.list
  clips/hana_001.wav, hana_002.wav, …
```

Without a model she speaks as a clone: GPT-SoVITS's base model copies the tone of her speaking clip (the one nearest
6 seconds, or the one chosen and listened to in the window). The base model is v4 (the clearest; it makes each
sentence whole before she says it, 1.5–3.5 s) or v2ProPlus (streams, so she starts in about half a second; a little
less sure of hard words), which also blends in the tone of her first four clips for about 0.1 s more per sentence.
With a minute or more of clips (five sounds better), "Train a model on its clips" trains her own: v2ProPlus, the way
GPT-SoVITS's web UI does it, about a second per second of speech plus a minute (Hana's five minutes took 5.4 on a
laptop graphics card with 8 GB, which it all but fills; once it starts it tells the time left, and after one run the estimate is
this PC's own); her voice rests meanwhile, and a game starting stops it. The model
(about 0.3 GB) goes into the voice's folder, and the window swaps between it and the clones. A voice speaks one language
(Japanese, English or Chinese): when you write in another, she speaks her own and the bubble shows your language,
like a subtitled anime (the model writes `[say: …]` before each sentence). While a game runs she rests her voice:
the server is stopped so the game has the graphics card, and started again after. Tray → "Voice" switches it off;
"Mute sounds" mutes her too. Clicking her stops her mid-sentence.

You can talk to her aloud as well. Hold the mouse on her, or hold the talk key in any app (Right Ctrl unless Chat
settings says otherwise), say it, and let go: she leans in to listen, and answers what she heard as if you had typed
it. The microphone at the end of the slip writes what you say into the slip instead, to change before you send it.
She hears on this PC, on the processor, with SenseVoice-Small (Chinese, English, Japanese, Korean, Cantonese, told
apart by itself): about a tenth of a second for a sentence. The microphone is open only while she listens, and
nothing is recorded or kept. The talk key's state is read each frame (no keyboard hook), so the key still does what
it does in the app in front.

Tray → "Chat history…" shows the conversation itself, every line kept, by day (with the summary she works from
above it). Tray → "Memory book…" shows all of it: edit, pin (always in mind, upkeep leaves it alone) or delete anything.
"Clear the chat" starts the conversation afresh and keeps what she remembers; "Forget all with …" clears the
conversation and everything about the two of you; "Forget all about me" clears what every character knows about
you. It stays in the profile folder (`companion\memory.db`); the key is encrypted for your Windows
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
`memorybook`, `chathistory`, `memoryseed` (a few notes, facts and a moment to try it on), `forget:chat|all|me` (its three forget
buttons), `upkeep` (digest the conversation now), `moment:note`, `voicesettings`, `voicetest` (her voice's
sample line), `voice:some_text` (a bubble she also says aloud, if it is in her voice's language) and
`voicemodel:v2ProPlus` (speak as a clone with that base model, or `voicemodel:trained` with its own, as saving the
window does), `voicetrain` (train her voice, as the window's button does) and `voicetrainstop`, `hear:C:\path\said.wav`
(a recording she hears as if said holding the talk key) and `dictate:C:\path\said.wav` (as if said into the slip's
microphone), and, opening the real microphone, `mic` (click the slip's microphone), `listen` and `listened` (as if
held on her, then let go), and `closeup:hands` (her camera narrowed onto her hands, to judge fingers; `closeup:off`).
`--offscreen` opens the app's windows off
every monitor; `tools\dev\shoot.ps1 -Hidden` uses it to test without anything appearing on screen.

## Layout

```
src/InsertCoin.Core       rules as a pure function (Ledger.Step) — no I/O, no clock
src/InsertCoin.Adapters   LeetCode GraphQL source, Steam registry detector, atomic JSON store
src/InsertCoin.Mind       the companion's mind, no Godot: provider connection (Microsoft.Extensions.AI), conversation
                          store (SQLite), prompt, stage directions, auto-compaction, encrypted key; Voice/: what of
                          a reply is spoken, voice profiles and their import, the GPT-SoVITS client, training
                          (VoiceTraining, and train_voice.py, run with GPT-SoVITS's Python), hearing (SenseVoice
                          through sherpa-onnx)
src/InsertCoin.Game       Godot 4.7 (.NET) app: Main (the cabinet's loop: ledger, screen, window, tray), App/Stool (whoever
                          is on the stool: her figure, chat, voice and their windows), cabinet scene, companion,
                          director, desktop window, tray, audio
tests/                    xUnit: every rule with a fake clock; adapters with recorded responses; the mind with a fake model
tools/art                 cozy_cabinet.py — the cabinet is a 3D model rendered to SVG, exported as skin layers;
                          app_icon.py — the app icon (.ico for the .exe, .png for the window)
tools/sfx                 generate.py — chip-style synth that writes the sound effects
tools/models              fetch.py — downloads the local models: her memory search's, her ears' (SenseVoice)
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
python tools/models/fetch.py                  # local models -> src/InsertCoin.Game/models (not in git: 380 MB)
```

The models are optional: without the memory models she recalls by shared words only, and without SenseVoice
(`fetch.py listening`, 240 MB) she can't be talked to aloud. `build.cmd` fetches all three when the folder is
missing; `python tools/models/fetch.py small` beforehand gives a lighter build with only the small memory model.

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
  themselves out; replies from her mind start as thinking dots and grow as they stream in. A long one is shown a
  page at a time, like subtitles: each page turns once her voice has said it (`Voice.Progress`), or, when she is not
  speaking, once there has been time to read it, and the bubble fades only when she has finished. `ChatBox` is the slip
  you type on, `Paper` the shared look (cream paper, the cabinet's ink, Fredoka).
- `App/Chat.cs` connects her to `InsertCoin.Mind`: sends what you type, acts out stage directions (`[cheer]`,
  `[wave]`, `[worry]`, `[note: …]`…), logs cabinet events into the conversation, folds it when it grows, runs
  upkeep every minute. `App/ChatSettings.cs` is the provider window, `App/MemoryBook.cs` the memory book. In
  `InsertCoin.Mind`: `Recall` picks memories, `Upkeep` holds the digest and note-rewrite requests. Design: [docs/specs/2026-10-02-companion-helper.md](docs/specs/2026-10-02-companion-helper.md),
  memory: [docs/research/2026-10-02-companion-memory.md](docs/research/2026-10-02-companion-memory.md).
- `App/Voice.cs` is her voice: `VoiceServer` runs the player's GPT-SoVITS hidden (tied to the cabinet, so it ends
  with it), `Speech` (in `InsertCoin.Mind`) decides what of a reply is said, `GptSoVits` streams each sentence's
  audio, and her mouth (`Face.Mouth`) follows its loudness. `App/VoiceTrainer.cs` trains a voice's own model (both
  run tied to the cabinet through `CabinetJob`). `App/VoiceSettings.cs` is its window. Measurements:
  [docs/research/2026-10-02-companion-voice.md](docs/research/2026-10-02-companion-voice.md).
- Her ears: `App/Ears.cs` opens the microphone while she listens (Godot records it into a muted bus of its own),
  `TalkKey` reads the talk key, and `Hearing` (in `InsertCoin.Mind`) turns what was said into words with
  SenseVoice. `App/Stool.cs` ties them together: held on her, the talk key, or the slip's microphone.
- `App/Stool.cs` holds whoever is on the stool: her figure, the chat and its slip, her voice and its training, the
  windows for them, and characters, cards and voices coming in. `Main.cs` is the cabinet's own loop.
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

Their voice, if they have one, is in `<id>.voice` in the characters folder: `voice.json` (language, clips and
their words, quality), the model's weights and clips. Dropping a voice on the cabinet puts it there for whoever is
on the stool (or for a model dropped with it).

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
