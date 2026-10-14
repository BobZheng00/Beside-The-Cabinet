# `/pm setup`: the first run

Once, after the pull request that brings in this setup has been merged. Show Bob step 4's list in one message and
wait for their yes before creating issues: they're public. Everything else here needs no question.

## 1. Check

- `gh auth status` works, with the `repo` scope.
- This session is in the main checkout, on `main`, clean, and up to date (`git pull --ff-only`).
- Auto mode is on for this session (Shift+Tab, or `"permissions": {"defaultMode": "auto"}` in
  `~/.claude/settings.json`). If not, tell Bob in one line: background agents can't answer permission prompts.

## 2. Labels

```
gh label create ready        --force --color 0E8A16 --description "Clear enough to build without asking"
gh label create in-progress  --force --color FBCA04 --description "An implementer has it"
gh label create decision     --force --color D93F0B --description "Waiting on Bob's choice: the question is the last PM comment"
gh label create brainstorm   --force --color 5319E7 --description "Needs a design session (/brainstorm)"
gh label create manual-check --force --color 1D76DB --description "Only Bob can check this: their mic, provider, a voice, motion, their phone"
gh label create blocked      --force --color B60205 --description "Waiting on something outside the repository"
gh label create found-during --force --color C2E0C6 --description "Parked while working on something else"
gh label create chore        --force --color EDEDED --description "Tooling, CI, upkeep"
gh label create p1           --force --color B60205 --description "Now: broken for players, or blocks other work"
gh label create p2           --force --color FBCA04 --description "Next"
gh label create p3           --force --color C5DEF5 --description "Later"
gh label create pm-log       --force --color BFDADC --description "The PM's run digests"
```

`bug`, `enhancement` and `documentation` are GitHub's own.

## 3. The PM log

`gh issue create --title "PM log" --label pm-log --body "Each /pm run posts its digest here. Bob: a comment here is read at the start of every run (for example: run three implementers at a time)."`,
then `gh issue pin <n>`. Read its latest comments from Bob at the start of each run.

## 4. Seed the backlog

The open items of `docs/roadmap.md` (drafted from it on 2026-10-13), plus the unchecked boxes in
`docs/publishing.md` and the open issues in `docs/memory-issues.md`. Check the roadmap once more for anything left
out, then show Bob the list (titles and labels) and create them after their yes. Each body should say where it comes
from (the roadmap section, the spec, M13...), and have a Done when where one can be written. What's private
(`private/to-try.md`) stays private: an issue may point to it, never quote it.

| Title | Labels | From |
|---|---|---|
| The smoke suite passes on main, calibrated against real runs | chore p1 ready | this setup. Done when: `tools/dev/smoke.ps1` on main passes every scenario, or each failure is filed as its own issue; known harmless log lines are in `ignore`, each with the reason in this issue; scenarios that can prove they did something have an `expect`; `.qa\smoke\main` holds the baseline |
| CI is green on main | chore p1 ready | this setup: the first runs of `.github/workflows/ci.yml` |
| Each of her replies in Chat history can show what she had in mind | enhancement p2 ready | roadmap, memory phase 4 part 4, approved: "From her memory" under each reply, her lists and what recall brought, each with its kind and date; Forget on every one, Edit on facts; the talk word for word not editable (spec `2026-10-11-observational-memory.md`) |
| Recall's search warms up off the main thread (M4) | enhancement p3 ready | memory-issues M4 |
| Things said in passing with a date reach a note with it (M13) | bug p3 | memory-issues M13 (triage: what a fix can be shown with, without billed runs) |
| Upkeep costs less on long conversations (M3) | chore p3 | roadmap, "Left on the memory topic" |
| A fact that holds one value (home, job) is updated, not added again | enhancement p3 brainstorm | roadmap, "Left on the memory topic" |
| Her texts are in the day's entry | enhancement p3 | roadmap, "Left on the memory topic" |
| Finish or drop the old memory's LongMemEval sample (paused at 22 of 44) | chore p3 decision | roadmap, "In flight" (billed to Bob) |
| Her own web search through the provider (OpenAI's Responses API) | enhancement decision | roadmap, "Waiting on the user's call" |
| "Sign in with ChatGPT" instead of an API key | enhancement decision | roadmap, "Waiting on the user's call" |
| The player can ask her to say her last line again | enhancement decision | roadmap, open questions ("we can ask her to reexplain") |
| She mentions something not set up yet, once | enhancement p3 | settings and setup spec, "Not built" |
| Try GPT-SoVITS v5 for her voice | chore p3 decision | roadmap, her voice (an 8 GB download) |
| Real-use checks from Bob's list (reminders, check-ins, phones, calls, calendars, her body, listening, conversations, balance) | manual-check | roadmap, "Waiting on the user"; details in `private/to-try.md` |
| The phone's QR code works with a real phone | manual-check | roadmap, balance "Next step" |
| Answering her card by voice works with the real microphone | manual-check | roadmap, "On the plate" 3 |
| Her thinking sounds sound right | manual-check | roadmap, "On the plate" 2 |
| A Chinese recording for Smart Turn | manual-check p3 | roadmap, "Answered 2026-10-09" |
| Memos: her phone and paper on other skins | enhancement p3 brainstorm | roadmap, "On the plate" 6 |
| Later abilities as add-ons: the clipboard (asking first), weather, writing to the calendar, local MCP servers | enhancement p3 brainstorm | roadmap, "On the plate" 8 and balance's leftovers |
| Reaching the player, later: voice notes, Discord, a private route, a channel for mainland China | enhancement p3 brainstorm | reaching-the-player spec, order step 5 |
| Character import: community cards, your own 3D model and voice | enhancement brainstorm | roadmap, the next phase |
| Mini-games on the CRT | enhancement brainstorm | roadmap, the next phase |
| A gesture per sentence (motion files) | enhancement p3 brainstorm | roadmap, "Parked" |
| Releases are built by CI from a version tag | chore p3 ready | new: a workflow on a `v*` tag, on a Windows runner, running `tools/export/release.py` (setup-godot with export templates) and attaching the zip to a draft release |
| The GitHub page: description, topics, social preview, first release | chore decision | `docs/publishing.md` (GitHub settings are Bob's) |
| Right after going public: the settings in docs/publishing.md | chore decision | `docs/publishing.md` |
| Steam: the checklist in docs/publishing.md | chore p3 brainstorm | `docs/publishing.md`, "Steam, later" |

## 5. Optional: Claude on GitHub

Bob may want `@claude` on issues and pull requests, and a review on every pull request, run on their plan: they run
`/install-github-app` themselves (it needs their GitHub admin and a `claude setup-token` token). Mention it once in the
first digest; never install it for them.

## 6. Then

Run `/pm` as usual. The first ready issues are the two `p1` chores: the smoke suite and CI.
