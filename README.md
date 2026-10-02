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
(size, companion, mute, in-game tickets, always on top, start with Windows, change player, quit).

The companion on the stool (branch `companion-spike`) follows the mouse pointer with her eyes and head, waves when
clicked, and reacts to what the cabinet does: cheers for coins, glances at the screen, worries about debt, dozes
while the session is paused.

Dev keys (debug builds, click the cabinet first): `F1/F2/F3` solve Easy/Medium/Hard · `F5` fake game on/off ·
`F6` clock ×60 · `F7` AFK on/off · `F9` wipe state · `Esc` quit.
Arguments after `--`: `--data=DIR`, `--no-steam`, `--script=hard@2,game@5,warp@5` (dev actions at given seconds).
Companion dev actions: `moment:cheer` (or `greet`, `glance`, `alarm`, `worry`, `doze`, `wake`) and `point:x:y`
(a pretend mouse pointer at that canvas point).

## Layout

```
src/InsertCoin.Core       rules as a pure function (Ledger.Step) — no I/O, no clock
src/InsertCoin.Adapters   LeetCode GraphQL source, Steam registry detector, atomic JSON store
src/InsertCoin.Game       Godot 4.7 (.NET) app: cabinet scene, companion, director, desktop window, tray, audio
tests/                    xUnit: every rule with a fake clock; adapters with recorded responses
tools/art                 cozy_cabinet.py — the cabinet is a 3D model rendered to SVG, exported as skin layers
tools/sfx                 generate.py — chip-style synth that writes the sound effects
art/cozy                  previews and layer SVGs (sources of the PNGs in the game)
```

## Build

```
dotnet test                                   # rules + adapters
dotnet build src/InsertCoin.Game              # game assembly
python tools/art/cozy_cabinet.py --skin       # regenerate art -> src/InsertCoin.Game/skins/cozy (needs Chrome)
python tools/sfx/generate.py                  # regenerate sounds -> src/InsertCoin.Game/sounds
```

### Standalone build

```
python tools/export/fetch_template.py --get <folder>/templates/4.7.2-stable-mono   # once: 40 MB instead of the 1.2 GB archive
build.cmd                                                                                  # -> builds\InsertCoin\InsertCoin.exe + data folder
```

The export preset points at that template path. Godot's exporter needs `src/InsertCoin.Game/InsertCoin.Game.sln`
(the repo-level solution is `.slnx`). Only one copy of the app runs at a time: a second launch exits quietly.
The `.exe` still carries Godot's icon (setting a custom one needs `rcedit`).

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
- `Rig`, `Puppet`, `Face` pose her: seated base pose, hands placed by two-bone IK, reaction poses, breathing,
  blinking, gaze, expressions. Poses are written against VRM humanoid bone names, not this model's bones.

The character stays at the 3D origin and the camera is placed around her: the VRM plugin's hair physics goes wrong
when the character itself is moved or turned. Adding or replacing a `.vrm` needs a re-import (see Build).

## Assets

Fonts: Press Start 2P and Fredoka (SIL OFL 1.1, licenses in `src/InsertCoin.Game/fonts`). Art and sounds are
original, generated by the tools above.

Companion: the VRM importer and MToon shader are [godot-vrm](https://github.com/V-Sekai/godot-vrm) (MIT, licences in
`src/InsertCoin.Game/addons`). The character is VRoid's sample "Sendagaya Shino", released by pixiv under CC0
(see `src/InsertCoin.Game/characters/shino/SOURCE.txt`).
