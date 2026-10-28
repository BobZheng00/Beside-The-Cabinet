# Beside the Cabinet (working name INSERT COIN): working notes

A companion on a stool beside a little desktop arcade cabinet (Godot 4.7.2 .NET): they chat, remember, speak, listen, keep the player's agenda and reach them by text, and the cabinet keeps a balance of work, life and games (good work earns play time: focus, the player's own activities, LeetCode solves). Players see "Beside the Cabinet"; the code, folders and the cabinet's marquee keep INSERT COIN. These notes call the companion "she" for short; the app's own words use the character's name, or "they", and the READMEs "your companion", then "they".

The README is the front page for strangers; the manual is in READMEs beside what they describe: `docs/README.md` every feature and setting, `tools/dev/README.md` every dev key and dev action, `src/README.md` building and running, `src/InsertCoin.Game/README.md` how the app is put together, `src/InsertCoin.Game/characters/README.md` adding a character. Keep them current as things change. `docs/specs` records what was decided and which parts are built. Read those rather than re-deriving them. This file is about how to work here. Where things stand (what's built, what was decided and when, the order of work) is in `docs/roadmap.md`, read when it's needed; what's open is in GitHub issues.

## Where things are

- `src/InsertCoin.Core`: the rules (pure, no I/O).
- `src/InsertCoin.Adapters`: LeetCode, Steam and the JSON store.
- `src/InsertCoin.Mind`: her mind, without Godot. Logic belongs here, with tests (fake clock, fake model). Main parts:
  - conversation, prompt, memory and upkeep in SQLite (`MindStore`);
  - agenda, calendars, delivery of reminders (`Elsewhere`), usage;
  - `Voice/`.
- `src/InsertCoin.Game`: the Godot app, kept thin.
  - `Main.cs`: the cabinet's loop and wiring.
  - `App/Stool.cs`: everything about her (chat, voice, reminders and check-ins she speaks, her windows).
  - `App/Listening.cs`: her microphone: the talk key, holding her, the slip's microphone, hands-free turns and conversations.
  - `Companion/`: her figure and bubble.
  - `Desktop/`: window, tray, tickets, and Windows bits (`Presence`, `WindowsNotice`).
- `docs/specs`:
  - `2026-09-30-insert-coin-design.md`: the cabinet;
  - `2026-10-02-companion-helper.md`: the companion's decisions and open questions;
  - `2026-10-04-companion-memory-v2.md`: memory, agenda, reminders, check-ins, usage and calendars, with sections marked *Built*.
  - `2026-10-05-hands-free-listening.md`: follow-up listening, conversations, the MIC light.
  - `2026-10-05-tools-and-abilities.md` (draft): built-in tools versus connectors, the first abilities, the order.
  - `2026-10-09-a-word-first.md`: what she brings up herself starts with a call and waits for the player's answer; subtitles wait for the reader.
  - `2026-10-07-balance.md` (draft): earning from more than LeetCode (focus, the player's own activities with proof), a daily cap and a nightly fade, pictures and files, rest and sleep, the order from here.
  - `2026-10-10-reaching-the-player.md`: a roommate who calls at the PC and texts when the player is away; texting with her on Telegram, both ways.
  - `2026-10-10-settings-and-setup.md`: one Settings window of short pages, saved as changed; guides for setup outside the app; Getting started; the short tray.
  - `2026-10-11-observational-memory.md`: her memory as a dated log of notes (phases 1 to 4; what is built where).
  - `2026-10-15-skills.md`: skills in the open SKILL.md format, the folder a skill may work in, her suggested
    changes to a skill, and servers the player adds (MCP); the order of work.
  - `2026-10-17-on-the-cabinet.md`: Settings and the switches from the cabinet itself: a right-click menu on her
    and the cabinet, a SETTINGS app and a row of switches on the CRT, a Memory page in Settings.
  - `2026-10-18-chatgpt-plan.md`: her mind on the player's ChatGPT plan (Sign in with ChatGPT), a backup asked for
    when the plan runs out, first in Getting started; OpenAI's waitlist before Steam.
  - `2026-10-24-how-she-talks.md`: a person beside the player, not an assistant reporting work: lean, situational
    base instructions, the agenda result handed back as known, the manner check that measures it.
  - `2026-10-24-default-cards.md`: who Shino is (an adult illustration student, a friend), her new card and her own
    card in Chinese, chosen by the player's language or a setting; Fumiriya's next.
  - `2026-10-27-what-earns-play-time.md`: the first run asks what earns play time (focus, the player's own
    activities, LeetCode) or none; with none, the companion without a balance; programs the player picks count as play.
  - `2026-10-27-any-character.md`: any character fits beside the cabinet as their card comes: her instructions set
    no place, and the cabinet says what its own things are in plain words.
- `docs/publishing.md`: publishing on GitHub, then Steam: the license, the name, the personal-information review and its rewrite of the history, what Steam needs (a checklist the user comes back to).
- `private/` (git-ignored, never published): the user's own notes; `personal-review.md` lists what of theirs is in the repository and its history, and what replaces it.
- `docs/research`: research notes; `2026-10-11-memory-benchmark.md` is how her memory measures up (benchmarks chosen, results, why no vector database); `2026-10-27-natural-reactions.md` is what makes her replies read like a friend's rather than a counsellor's (the research, and the habits the manner check could count).
- `docs/memory-issues.md`: what goes wrong in her memory, with evidence and status (M1 to M14, R1, R2).
- `tools/bench/memory.cs`: the memory benchmark (LongMemEval, LoCoMo, staying in character, the free retrieval score).
- `docs/roadmap.md`: where we are: what's built, what was decided and when, the order of work (this file's last
  section until 2026-10-13). Update it at each milestone.
- `.claude/pm.md`: this project's settings and notes for the pm plugin, which runs the work (`/pm:run`, `/pm:park`,
  `/pm:brainstorm`; its agents and gate come with it). `.claude/skills/smoke`: `/smoke`.
  `.github/workflows/ci.yml`: CI on every pull request.

## Build and test

- `dotnet test InsertCoin.slnx` runs Core, Adapters and Mind.
- `tools/dev/check.ps1` runs everything a commit must pass, and records the result against the commit: the solution
  and the game built with no warnings, the tests, and the smoke suite. It takes minutes and waits its turn for the
  smoke suite: run it in the background. CI (`.github/workflows/ci.yml`) builds and tests every pull request on
  Windows, without the smoke suite.
- **The solution does not include the game.**
  - Build it with `dotnet build src/InsertCoin.Game/InsertCoin.Game.csproj`.
  - Do this after any change under `src/InsertCoin.Game` and before every run. Godot runs `src/InsertCoin.Game/.godot/mono/temp/bin/Debug/InsertCoin.Game.dll`, so a run without that build tests the old code.
- Keep builds at 0 warnings. Windows-only APIs need `[SupportedOSPlatform("windows")]` outside the game; the game itself targets `net10.0-windows10.0.19041.0` (Windows' media controls, a WinRT API, since 2026-10-06).
- Line endings: git has `autocrlf=true` and normalises on commit. Python edits should read and write with `newline=''`.
- **Editing gotchas on Windows** (each cost time on 2026-10-05):
  - Python edit scripts with Windows paths or `\n` in them: write the script to a file with the Write tool and run it. A heredoc garbles backslashes (`\v`, `\a` became control characters), and quotes can break bash. Use `chr(92)` for a lone backslash. Afterwards, check changed files for control characters.
  - Git Bash `sed -i` turns CRLF files into LF. It's harmless (git normalises), but normalise before committing.
  - Arrays for PowerShell scripts (`-Shots 6,9`) don't survive bash: run `shoot.ps1` with the PowerShell tool.
  - Mock servers must be started with `run_in_background`. One started with `&` dies when its shell call ends.

## Running it

- Test runs, clips and benchmarks are scripts with every option in their headers: `tools\dev\shoot.ps1` (a hidden,
  muted, throwaway profile; screenshots taken from the app's own windows), `tools\dev\clip.ps1` (motion is judged on
  clips, not stills), `tools/bench/memory.cs` (her memory). Every dev action is in `tools/dev/README.md`.
- The launch scripts and these read the Godot editor's path from `GODOT`, or from `private\local.cmd`.
- **Never** open the real microphone in a test, launch the real profile unasked, send keystrokes or take focus, flip
  "Start with Windows" (Windows' own setting) or untick "Mute sounds" in a test. One copy of the player's app runs
  at a time; a hidden test copy runs beside it (#31: its own desktop and profile, no tray icon, no talk key or
  microphone).
- How this PC runs them (its paths, the demo profile, the mock servers, the provider's quirks) is in its own notes,
  which git ignores: @private/notes.md

## Rules the user set

- **Soft enforcement only.** She reminds, nags or sulks; nothing ever closes or blocks a game.
- **Keys and secrets.** Never use or ask for the user's real API keys. Keys and secrets (API keys, calendar links, webhook tokens) are encrypted with DPAPI (`KeyStore`). They never go in settings.json or the log.
- **Third-party voices and character models** (the demo profile's) are for the user's personal testing only. Never commit them.
- **Acting outside the app:** whatever she does there asks the player first. Downloads need the user's permission.
- **Git:**
  - Work on a branch, then fast-forward `main`. Once the repository is public, `main` takes changes only through
    pull requests (the user, 2026-10-12; the ruleset in `docs/publishing.md`): push the branch, and the user
    merges its pull request on GitHub (Rebase and merge); then `main` here is brought up to GitHub's.
  - Commit message: a plain sentence saying what changed for the player, then details.
  - End commits with the `Co-Authored-By` line from the system reminder, without a `Claude-Session` link (the user, 2026-10-12: those links would show in the public history).
- **Web research** results end with a "Sources:" list.
- **Her manner is her character's.** The app decides what she does and when (whether she asks, a verdict, an amount, how often); her character card decides how she says it. Prompts say what to get across, never a tone ("tease", "playfully"): a gentle, considerate card must stay so (the user, 2026-10-07).
- **Fix behaviour in code before prompts.** Prompt rules drift and pile up ("I do not want to fully relay on the prompt and instructions as they can drift away and can get massive", 2026-10-11): prefer narrow requests whose shape the code checks, limits and orders kept by code, and measurements. A prompt line that is needed reads naturally ("try to fix it without hard prompting").
- **Her motion is grown-up:** small, few, asymmetric moves, with feeling shown in her face and head more than her limbs. The user found the old cheer (arms up, legs kicking, bouncing on the seat) childish. Ground new motion in how people move (agent note §7).
- **Keep the structure simple.** Add an interface only where a second implementation is planned (`ICalendarSource`, for Google or Microsoft sign-in later). Stool.cs is big but sectioned; its microphone part became `Listening.cs` when turn-taking was reworked.

## How work flows

- **The backlog is GitHub issues**, not this file or the conversation: what's open, its priority (`p1` to `p3`) and
  its state (`ready`, `in-progress`, `decision`, `brainstorm`, `manual-check`, `blocked`). Read it with
  `gh issue list` or `/pm:run status`. A session can be compacted or ended at any point without losing anything.
- **`/pm:run` runs it** (the pm plugin; this project's settings and notes are in `.claude/pm.md`, in the iterate
  phase since 2026-10-13): ready issues go to implementer agents, each in a worktree of its own; each result goes
  to QA and a reviewer in fresh contexts; a pull request is marked ready only when both pass. The user merges
  every pull request (their choice, 2026-10-13).
- **Something new mid-task**, found by you or raised by the user, is parked as an issue (`/pm:park`) and the task goes
  on, unless the user says to switch now.
- **Done** means committed, `tools/dev/check.ps1` passed on that commit in a clean checkout, and QA's verdict PASS.
  The plugin's gate holds `gh pr ready` to that, and refuses pushes to `main`. A merge run from a session
  (`gh pr merge`) isn't checked against it: the gate only asks the user to confirm it (`merge: user` in
  `.claude/pm.md`), so the PM runs one only for a pull request already marked ready, when the user asks. The user
  lifted the outright deny in `.claude/settings.json` on 2026-10-15. The gate needs `gh` on PATH.
- **The user is brought in** for: a choice the player would notice that the specs don't settle, or one that touches
  a rule above; what only they can unblock (their keys, real profile, microphone, money, downloads, GitHub
  settings); something serious (CI red on main, a crash or lost data, three failed rounds); a design to talk
  through (`/pm:brainstorm`); anything irreversible or public beyond a draft pull request. Everything else goes in the
  PM's digest, on the pinned "PM log" issue.
- **What only the user can check** (their microphone and provider, a voice's sound, motion's feel, their phone) is
  an issue labelled `manual-check`, batched for when they next use the app. Never wait on it.
- **Smoke runs:** `tools\dev\smoke.ps1` runs every scenario in `tools/dev/smoke/scenarios.json` hidden (`-List` says
  what each checks), one run at a time on this PC, beside the player's app or the demo if one is open (never stopping
  it).
  `tools/dev/mock_mind.py` stands in for her mind. Never loosen a scenario's checks to make a run pass.

## How the code reads

- Names are plain words. Doc comments are prose about what something means for the player ("she", "the player"). Spelling is British (colour, summarise).
- Test names are sentences (`A_reminder_goes_off_ahead_of_its_item...`). Match the surrounding file.
- **Time:**
  - Pass the `TimeZoneInfo` wherever dates are made: a repeat keeps its wall-clock time across a clock change.
  - Tests use explicit offsets, never the machine's.
- **Godot gotchas:**
  - An `Unfocusable` window still takes mouse clicks.
  - Measure text on a hidden label, never on the one shown (the speech-bubble bug of 2026-10-05).
  - Every script and shader has a `.uid` file beside it (Godot 4.4+: its lasting id); commit it with the script. One
    made outside the editor gets it when Godot next opens the project (the editor, an export): 41 were missing
    until Preview 1's export wrote them (2026-10-13).
