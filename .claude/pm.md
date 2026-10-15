---
# iterate: it works end to end, CI is green and Preview 1 is out; small issues, fixes, refactors and debt (the user, 2026-10-13)
phase: iterate
# builds the solution and the game with no warnings, runs the unit tests and the smoke scenarios the branch's changes need: 0 passed, 1 failed, 3 blocked
check: powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev/check.ps1
# test runs take turns on this PC (one lock), so more implementers mostly wait
parallel: 2
# the user merges every pull request (their choice, 2026-10-13)
merge: user
# Windows-only APIs, a GPU for her voice, local models, Godot on this PC
runs-on: local
specs: docs/specs
roadmap: docs/roadmap.md
---

# Beside the Cabinet: notes for the PM and its agents

A Godot 4.7 .NET desktop app for Windows: a companion on a stool beside a little arcade cabinet. CLAUDE.md is how to
work here (its rules, how the code reads, where things are); `private/notes.md`, which it imports, is this PC's own.

## How QA sees the product

- `check` (tools/dev/check.ps1) builds the solution and the game with no warnings, runs the unit tests and the smoke
  scenarios the branch's changes need (tools/dev/smoke.ps1 -Changed, hidden: every one when shared code or the harness
  changed, each whose paths changed, `boot` for any app change, none for tests or docs alone). It records its own
  result in the same place and format as the plugin's checks, with which scenarios ran and why each other one was
  skipped (also in the smoke report). `-All` runs every scenario. It takes minutes and waits its turn: run it in the
  background.
- QA: read the screenshots of the scenarios that ran (the smoke report lists them) and compare them with main's
  baseline (below); make aimed runs only for what the change touches, never a tour of the UI it doesn't.
- Test runs take turns on this PC: smoke.ps1, shoot.ps1 and clip.ps1 share one lock, and only one copy of the app
  runs at a time. A smoke run blocked by a running copy of the app reports BLOCKED (exit 3): retry later, never stop
  the copy. If it's the user's demo or their own app, leave it.
- In a worktree, before test runs of your own: `powershell -NoProfile -ExecutionPolicy Bypass -File
  tools/dev/smoke.ps1 -Prepare` (where Godot is, the main checkout's models, the import, the build).
- Aimed runs: `tools\dev\shoot.ps1 -Hidden -Out <worktree>\.qa\shots -Tag <name> -Script "action@s,..." -Shots s,...`
  with the dev actions in tools/dev/README.md. Run it with the PowerShell tool, so arrays survive. Read the
  screenshots. `-Window "<title>"` captures one of the app's own windows too (without its title bar).
- Hidden runs (`shoot.ps1 -Hidden`, the smoke suite, `clip.ps1`) never show on the user's screen: the app runs on a
  Windows desktop of its own, so no console, window, taskbar button or tray icon reaches theirs, and the window in
  front stays theirs, from start to finish. A run that shows them anything says so in a `seen by the player:` line,
  and the smoke scenario fails. Never run the app any other way while the user is at the PC (a run without `-Hidden`
  shows it on their screen).
- Motion is judged on clips: `clip.ps1 -Out <worktree>\.qa\clips`, then a contact sheet of the frames.
- Her mind in tests is `python tools/dev/mock_mind.py <port> --log <folder>/mind.jsonl --quit-after <s>`, with the
  profile settings in tools/dev/README.md. Never a real provider.
- A release build is tested the same way: `shoot.ps1 -Hidden -Exe <the exe>`, or the exe with Godot's `--quit-after <frames>`
  so its log gets written (a killed release build leaves it empty). `--data`, `--offscreen` and `--script` work in
  release builds. Releases to share are made only with `tools/export/release.py` (it keeps GPL code out).
- Compare with main's latest smoke run in `<main checkout>\.qa\smoke\main\`. After visible changes merge, refresh it:
  run smoke.ps1 in the main checkout on main (with no options it runs every scenario).
- A worktree's `src\InsertCoin.Game\models` is a link to the main checkout's models. Before removing a worktree,
  remove that link first (`cmd /c rmdir "<worktree>\src\InsertCoin.Game\models"`), then `git worktree remove`.
  Never delete a worktree with `Remove-Item -Recurse`, which can follow the link.
- Look for what a player would notice: text cut off or running off a window, overlapping windows, blank or black
  areas, the wrong language or American spelling, a control that does nothing, the companion called "she" or "her"
  in the app's own words or in the READMEs (their name, or "they").
- A new script or shader brings its `.uid` file into the same commit (Godot writes it when it next opens the
  project); a pull request with a script and no `.uid` is unfinished.

## Never in tests

The real microphone, the user's real keys or provider, the real profile (run.cmd, the built exe without `--data`),
sending keystrokes or taking focus, flipping "Start with Windows", unticking "Mute sounds", anything billed to the
user (their key runs only with their leave, as for a benchmark). The user often plays a game while agents work: check
none is running before GPU-heavy work. The machine's own notes (private/notes.md, imported by CLAUDE.md) have the
details. Nothing personal in public text: the public names are in private/notes.md, "What the public files call
things", and `python private/rewrite/check.py . --history` finds what slipped.

## Only the user can check

Their microphone and provider, how a voice sounds, how motion feels, their phone (the QR code, Telegram), and how a
build behaves on other people's PCs (their friends' reports on the previews).

## For review

CLAUDE.md's "Rules the user set" and "How the code reads": soft enforcement only; keys only through KeyStore; acting
outside the app asks first; prompts say what to get across, never a tone; behaviour fixed in code before prompts; her
motion grown-up; structure simple; British spelling; test names as sentences; the companion's name or "they" in what
players read.
