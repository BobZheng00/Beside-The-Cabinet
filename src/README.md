# Building and running from source

How to build, run and test the app. Dev keys and test runs: [tools/dev/README.md](../tools/dev/README.md); how
the app is put together: [InsertCoin.Game/README.md](InsertCoin.Game/README.md).

## What you need

- Windows 10 or 11, 64-bit.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Godot 4.7.2 with .NET, not the standard build ([every version](https://godotengine.org/download/archive/)).
- Python 3, to fetch the local models; remaking the art and the icon also needs Chrome.

## Build

```
dotnet test InsertCoin.slnx                              # Core, Adapters and Mind
dotnet build src/InsertCoin.Game/InsertCoin.Game.csproj  # the game
python tools/art/cozy_cabinet.py --skin                  # regenerate art -> src/InsertCoin.Game/skins/cozy (needs Chrome)
python tools/sfx/generate.py                             # regenerate sounds -> src/InsertCoin.Game/sounds
python tools/art/app_icon.py                             # regenerate the app icon -> src/InsertCoin.Game/icon.ico, icon.png (needs Chrome)
python tools/models/fetch.py                             # local models -> src/InsertCoin.Game/models (not in git: 380 MB)
```

The models are optional: without the memory models the companion recalls by shared words only, and without
SenseVoice (`fetch.py listening`, 240 MB) they can't be talked to aloud. `build.cmd` fetches all three when the folder is
missing; `python tools/models/fetch.py small` beforehand gives a lighter build with only the small memory model.
`build.cmd` and `release.py` stop, saying what's missing, when the folder holds none of them or one only in part (an
emptied or half-fetched folder); `python tools/models/fetch.py check` says the same without building. A worktree
needs none of its own: run from source there, the app reads the main checkout's.

## Running it

Set `GODOT` to your Godot 4.7.2 .NET editor's exe, or put `set "GODOT=…"` in `private\local.cmd` (git ignores
`private/`): `run.cmd`, `demo.cmd`, `firstrun.cmd`, `build.cmd` and the test scripts read it. After changing art or sounds,
re-import: `Godot_..._console.exe --headless --path src/InsertCoin.Game --import`.
The project uses the Compatibility renderer: Forward+ cannot do transparent windows here.

| | |
|---|---|
| `builds\BesideTheCabinet\BesideTheCabinet.exe` | The standalone app (after `build.cmd`). Real profile in `%APPDATA%\InsertCoin`, real Steam detection. No dev keys. |
| `run.cmd` | Same as the app, but run from source through the Godot editor binary (debug build, dev keys on). |
| `demo.cmd` | Demo profile (`%APPDATA%\InsertCoin-demo`), Steam ignored. Safe to play with. |
| `firstrun.cmd` | A first run, as a new player sees it: a fresh profile every time (`%APPDATA%\InsertCoin-firstrun`, emptied at each start), Steam ignored. Quit the demo first. |
| `update.cmd` | After pull requests are merged: brings this folder up to GitHub's `main`, builds the code `run.cmd` and `demo.cmd` start, and rebuilds the standalone app (`build.cmd`). Quit the app first. It changes nothing if the folder isn't on `main` or has changes of its own. |

### Standalone build

```
python tools/export/fetch_template.py --get <folder>/templates/4.7.2-stable-mono          # once: 40 MB instead of the 1.2 GB archive
build.cmd                                                                                  # -> builds\BesideTheCabinet\BesideTheCabinet.exe + data folder
```

The export preset (`custom_template/release` in `src/InsertCoin.Game/export_presets.cfg`) names that template: set it to
yours. Godot's exporter needs `src/InsertCoin.Game/InsertCoin.Game.sln`
(the repo-level solution is `.slnx`). Only one copy of the app runs at a time: a second launch exits quietly (test copies, started
`--offscreen` by the test scripts, run beside it: `tools/dev/README.md`).
Godot's exporter writes the app icon (`icon.ico`) and the product name into the `.exe` itself; no `rcedit` needed.

To share a build, make a release instead: `python tools/export/release.py 0.1.0-preview.1` exports into
`builds/release/<version>/`, puts in sherpa-onnx's own build without text-to-speech (the NuGet one carries espeak-ng,
GPL-3.0, for speech the app never uses), writes `THIRD-PARTY-NOTICES.txt` from what shipped, adds the licence, the
notice and a README, and zips it. It stops on any library its notices can't account for. Set the version in
`export_presets.cfg` first (`application/file_version` and `product_version`: 0.1.0-preview.1 is 0.1.0.1).

A version tag does the same on GitHub (`.github/workflows/release.yml`): pushing `v0.1.0-preview.2` sets up Godot and
its template (pinned by SHA-256) and the models on a Windows runner, runs `release.py 0.1.0-preview.2`, and leaves a
draft release (a pre-release for a version with a hyphen) with the zip and its SHA-256, for you to write the notes and
publish. A pull request that changes the workflow, `tools/export`, `tools/models` or the export preset gets a dry run:
the same build, versioned `0.1.0-dryrun.1` from the preset, kept only as the run's artifact for 7 days.

## Layout

```
src/InsertCoin.Core       rules as a pure function (Ledger.Step) — no I/O, no clock
src/InsertCoin.Adapters   LeetCode GraphQL source, Steam registry detector, atomic JSON store
src/InsertCoin.Mind       the companion's mind, no Godot: provider connection (Microsoft.Extensions.AI), conversation
                          store (SQLite), prompt, stage directions, notes and upkeep, encrypted key; Tools/: the
                          companion's tools and their permissions (Toolbox) and the built-in ones (BuiltIn); skills,
                          the SKILL.md folders read where they are (Skills); memos (Memo);
                          Voice/: what of
                          a reply is spoken, voice profiles and their import, the GPT-SoVITS client, training
                          (VoiceTraining, and train_voice.py, run with GPT-SoVITS's Python), hearing (SenseVoice
                          through sherpa-onnx)
src/InsertCoin.Game       Godot 4.7 (.NET) app: Main (the cabinet's loop: ledger, screen, window, tray), App/Stool (whoever
                          is on the stool: their figure, chat, voice and windows), cabinet scene, companion,
                          director, desktop window, tray, audio
tests/                    xUnit: every rule with a fake clock; adapters with recorded responses; the mind with a fake model
tools/art                 cozy_cabinet.py — the cabinet is a 3D model rendered to SVG, exported as skin layers;
                          app_icon.py — the app icon (.ico for the .exe, .png for the window)
tools/sfx                 generate.py — chip-style synth that writes the sound effects
tools/models              fetch.py — downloads the local models: memory search's, and the ears' (SenseVoice)
tools/dev                 shoot.ps1 runs the app in a throwaway muted profile and screenshots it (-Window: one of its
                          own windows too; -Desk: all its windows where they sit together); clip.ps1 records a clip of
                          the companion moving; sheet.ps1 makes contact sheets
tools/bench               memory.cs — the companion's memory on LongMemEval and LoCoMo, and staying in character (docs/research/2026-10-11-memory-benchmark.md):
                          `dotnet run tools/bench/memory.cs -- --data <json> --out <folder> [--set locomo|persona]`;
                          it runs on a profile's own provider and key (the demo's), billed to its owner
art/cozy                  previews and layer SVGs (sources of the PNGs in the game)
```
