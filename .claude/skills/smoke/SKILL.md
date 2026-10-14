---
name: smoke
description: Run the smoke suite (or part of it) on this checkout without anything appearing on screen, and show what passed, what failed and the screenshots that matter. Use for /smoke, or when Bob asks whether the app still works.
argument-hint: "[scenario names, comma-separated]"
---

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/dev/smoke.ps1 -Only "$ARGUMENTS"` (all scenarios when
empty) in the background, and wait for it. It builds the game first, waits its turn if another smoke run is going,
and stops as BLOCKED if a copy of the app is running. Never stop a copy of the app to make room. If it's the demo,
say so and leave it to Bob.

Then:

- Read `report.md` from the folder it names.
- For each FAIL, Read its screenshots and log lines, and say in a sentence what went wrong for the player. Where it
  looks like a real bug and not the suite, offer to park it (`/park`).
- Read one screenshot of each passing scenario too: a run can pass its checks and still look wrong (clipped text, a
  window in the wrong place). Mention only what a player would notice.
- `tools/dev/smoke.ps1 -List` says what each scenario checks.

Answer in a few lines: how many passed, what failed and why, the report's path. Send Bob the screenshots only if they
ask.
