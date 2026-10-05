# INSERT COIN: working notes

A desktop arcade cabinet (Godot 4.7.2 .NET): LeetCode solves pay for Steam gaming time. A 3D companion sits on a stool beside it. She chats, remembers, speaks, listens, and keeps the player's agenda.

The README is the manual: features, every dev action, build commands. `docs/specs` records what was decided and which parts are built. Read those rather than re-deriving them. This file is about how to work here, and where we are.

## Where things are

- `src/InsertCoin.Core`: the rules (pure, no I/O).
- `src/InsertCoin.Adapters`: LeetCode, Steam and the JSON store.
- `src/InsertCoin.Mind`: her mind, without Godot. Logic belongs here, with tests (fake clock, fake model). Main parts:
  - conversation, prompt, memory and upkeep in SQLite (`MindStore`);
  - agenda, calendars, delivery of reminders (`Elsewhere`), usage;
  - `Voice/`.
- `src/InsertCoin.Game`: the Godot app, kept thin.
  - `Main.cs`: the cabinet's loop and wiring.
  - `App/Stool.cs`: everything about her (chat, voice, listening, reminders and check-ins she speaks, her windows).
  - `Companion/`: her figure and bubble.
  - `Desktop/`: window, tray, tickets, and Windows bits (`Presence`, `WindowsNotice`).
- `docs/specs`:
  - `2026-09-30-insert-coin-design.md`: the cabinet;
  - `2026-10-02-companion-helper.md`: the companion's decisions and open questions;
  - `2026-10-04-companion-memory-v2.md`: memory, agenda, reminders, check-ins, usage and calendars, with sections marked *Built*.
- `docs/research`: research notes.

## Build and test

- `dotnet test InsertCoin.slnx` runs Core, Adapters and Mind.
- **The solution does not include the game.**
  - Build it with `dotnet build src/InsertCoin.Game/InsertCoin.Game.csproj`.
  - Do this after any change under `src/InsertCoin.Game` and before every run. Godot runs `src/InsertCoin.Game/.godot/mono/temp/bin/Debug/InsertCoin.Game.dll`, so a run without that build tests the old code.
- dotnet prints in Chinese on this PC: `已通过` = passed, `失败` = failed, `个警告` / `个错误` = warnings / errors.
- Keep builds at 0 warnings. Windows-only APIs need `[SupportedOSPlatform("windows")]`.
- Line endings: git has `autocrlf=true` and normalises on commit. Python edits should read and write with `newline=''`.

## Running it safely (the user often plays a game while we work)

- **Test runs** use `tools\dev\shoot.ps1 -Hidden -Out <tools>\voice-trial\shots -Tag <name> -Script "action@sec,..." -Shots sec,...`.
  - It uses a throwaway muted profile, ignores Steam, shows nothing on screen and captures from the window itself.
  - `-Window "<title>"` also captures one of the app's own windows. `-Settings ', "Key": value'` seeds settings.
  - The log is `<Out>\run-<Tag>.log` (and `.err`).
- **Dev script text:** underscores for spaces, no commas.
- **Motion** is judged on clips, not stills: `tools\dev\clip.ps1 -Script "moment:cheer@3" -Seconds 6 -Tag cheer`.
  - Godot's movie writer renders off screen, in game time; frames are 518×656 whatever the window size.
  - Look at them as contact sheets (ffmpeg `fps=10,tile=8x3`). `talk:<wav>` plays a recording to her as if said live.
- **Never launch the real profile** (`run.cmd`, `builds\InsertCoin\InsertCoin.exe`) unless asked. Never send keystrokes or take focus.
- **One copy at a time.** A second copy exits quietly, so the shoot produces nothing. If the user's demo is running:
  1. check its command line contains `InsertCoin-demo`;
  2. stop it by PID and say so;
  3. relaunch `demo.cmd` afterwards.
- **Before GPU-heavy work** (her voice, local models), check that no game is running.
- **Screenshots:** delete any that caught something of the user's.
- **Test assets** go in `<tools>\...`, not %TEMP%: a cleanup tool on this PC empties %TEMP%.
- **Mock servers** (never the user's real keys):

  | Command | What it is |
  |---|---|
  | `<tools>\voice-trial\scripts\mock_llm.py 8765 [think]` | OpenAI-compatible; logs `requests.jsonl` |
  | `<tools>\listen-trial\mock_long.py 8766` | long replies |
  | `<tools>\listen-trial\fake_anthropic.py 8770` | Anthropic-style, with cache counts |
  | `<tools>\listen-trial\hook_sink.py <port>` | ntfy/webhook receiver; writes `hooks.jsonl` |
  | `<tools>\listen-trial\cal\test.ics` | a test calendar; serve it with `python -m http.server 8768` |

## Rules the user set

- **Soft enforcement only.** She reminds, nags or sulks; nothing ever closes or blocks a game.
- **Keys and secrets.** Never use or ask for the user's real API keys. Keys and secrets (API keys, calendar links, webhook tokens) are encrypted with DPAPI (`KeyStore`). They never go in settings.json or the log.
- **Third-party voices and character models** (Hana and others) are for the user's personal testing only. Never commit them.
- **Acting outside the app:** whatever she does there asks the player first. Downloads need the user's permission.
- **Git:**
  - Work on a branch, then fast-forward `main`.
  - Commit message: a plain sentence saying what changed for the player, then details.
  - End commits with the attribution lines from the system reminder.
- **Web research** results end with a "Sources:" list.
- **Her motion is grown-up:** small, few, asymmetric moves, with feeling shown in her face and head more than her limbs. The user found the old cheer (arms up, legs kicking, bouncing on the seat) childish. Ground new motion in how people move (agent note §7).
- **Keep the structure simple.** Add an interface only where a second implementation is planned (`ICalendarSource`, for Google or Microsoft sign-in later). Stool.cs is big but sectioned. Split it when the agent phase reworks turn-taking, along the lines that design needs.

## How the code reads

- Names are plain words. Doc comments are prose about what something means for the player ("she", "the player"). Spelling is British (colour, summarise).
- Test names are sentences (`A_reminder_goes_off_ahead_of_its_item...`). Match the surrounding file.
- **Time:** this PC is on US Eastern time.
  - Pass the `TimeZoneInfo` wherever dates are made: a repeat keeps its wall-clock time across a clock change.
  - Tests use explicit offsets, never the machine's.
- **Godot gotchas:**
  - An `Unfocusable` window still takes mouse clicks.
  - Measure text on a hidden label, never on the one shown (the speech-bubble bug of 2026-10-05).

## Where we are (update this at each milestone)

**Built and on `main`:**
- the cabinet;
- her figure;
- voice: GPT-SoVITS voices, streamed, with lip sync;
- listening: SenseVoice, push-to-talk;
- chat on the player's own provider, with memory v2;
- the agenda and reminders (ticket, chime, Windows, ntfy, Discord, Telegram, webhook);
- check-ins, gated on presence and calls;
- the Usage tab, with prices the player enters;
- calendars by private ICS link.

**Now (from 2026-10-05):** the user is trying it in real use. Not yet confirmed for real:
- check-in quality with a real model;
- the provider's cache hit rate (Usage tab);
- ntfy, Discord and Telegram on a phone;
- call detection (Teams, Zoom);
- do-not-disturb;
- a real Google or Outlook ICS link.

The Windows notification was confirmed working on 2026-10-05. Everything waiting on the user (these checks, the spike session, open decisions) is listed in `docs/to-try.md`: keep it current, crossing items off as results come in.

**Next: the agent phase, design first.** Three parts:
- hands-free voice conversation: she notices when the player has finished, and stops when talked over;
- real tools, asking before acting outside the app;
- her body during conversation.

The research note is written: `docs/research/2026-10-05-companion-agent.md`. The user chose to start with:
1. **A spike.** Windows' echo cancellation, plus Silero and Smart Turn v3, on their own voice and speakers.
   - The test program is built in `<tools>\agent-trial` (README there): `check`, `selftest`, `session`, `analyze <dir>`.
   - It waits for the user's 15-minute recording session.
   - Then run `analyze` on their `recordings\<date-time>` folder and put the results in the agent note.
2. **Conversation mode.**
   - *Built 2026-10-05:* being cut off. Her line keeps what was heard, and a cabinet line says what she hadn't said (`Chat.Interrupt`, `Conversation.CutOff`, `Speech.CutAt`).
   - *Measured 2026-10-05:* her delay. About 1.5–2 s to her first sound, with a fast provider (agent note §5).
   - *Built 2026-10-05:* her body in conversation (agent note §7).
     - She nods in the player's pauses (`Backchannel`), looks away to think (finger to her chin on a long wait), looks back to answer, and her head follows her voice's stress. She reacts when cut off.
     - The cheer is now soft applause (see the rule on her motion).
     - Motion is judged on clips from `tools\dev\clip.ps1`.
   - Next: discuss with the user how hands-free follow-up listening should behave. They weren't sure the proposed behaviour is what players expect.

Tools, abilities and fillers are decided later. A brainstorm on which abilities she should have comes before calling the phase good.

**After that:** character import (community cards, plus your own 3D model and voice), and mini-games on the CRT. Steam comes last: the app should be mature before it goes public.
