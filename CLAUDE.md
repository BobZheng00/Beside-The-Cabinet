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
- `docs/research`: research notes.

## Build and test

- `dotnet test InsertCoin.slnx` runs Core, Adapters and Mind.
- **The solution does not include the game.**
  - Build it with `dotnet build src/InsertCoin.Game/InsertCoin.Game.csproj`.
  - Do this after any change under `src/InsertCoin.Game` and before every run. Godot runs `src/InsertCoin.Game/.godot/mono/temp/bin/Debug/InsertCoin.Game.dll`, so a run without that build tests the old code.
- dotnet prints in Chinese on this PC: `已通过` = passed, `失败` = failed, `个警告` / `个错误` = warnings / errors.
- Keep builds at 0 warnings. Windows-only APIs need `[SupportedOSPlatform("windows")]`.
- Line endings: git has `autocrlf=true` and normalises on commit. Python edits should read and write with `newline=''`.
- **Editing gotchas on this PC** (each cost time on 2026-10-05):
  - Python edit scripts with Windows paths or `\n` in them: write the script to a file with the Write tool and run it. A heredoc garbles backslashes (`\v`, `\a` became control characters), and quotes can break bash. Use `chr(92)` for a lone backslash. Afterwards, check changed files for control characters.
  - Git Bash `sed -i` turns CRLF files into LF. It's harmless (git normalises), but normalise before committing.
  - Arrays for PowerShell scripts (`-Shots 6,9`) don't survive bash: run `shoot.ps1` with the PowerShell tool.
  - Mock servers must be started with `run_in_background`. One started with `&` dies when its shell call ends.

## Running it safely (the user often plays a game while we work)

- **Test runs** use `tools\dev\shoot.ps1 -Hidden -Out <tools>\voice-trial\shots -Tag <name> -Script "action@sec,..." -Shots sec,...`.
  - It uses a throwaway muted profile, ignores Steam, shows nothing on screen and captures from the window itself.
  - `-Window "<title>"` also captures one of the app's own windows. `-Settings ', "Key": value'` seeds settings.
  - The log is `<Out>\run-<Tag>.log` (and `.err`).
- **Dev script text:** underscores for spaces, no commas.
- **Never open the real microphone in tests.**
  - Push-to-talk: `talk:<wav>` or `hear:<wav>`.
  - Hands-free: `fakemic` first, then `mictalk:<wav>`.
  - Test speech is `<tools>\agent-trial\body\talk.wav` (three sentences, 0.6 s pauses). `mock_reply.py <port> <think s> "<reply>"` sits next to it.
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
  | `<tools>\agent-trial\tools\mock_tools.py 8771 [--refuse]` | OpenAI- and Anthropic-style; makes tool calls ("open" in a line calls `open_link`, "memo" writes one, its words coming slowly; told of the player's own memo, a reminder if it says "remind", else `[quiet]`); `--refuse` turns tools down |
  | `<tools>\agent-trial\tools\check.cs` | `dotnet run check.cs`: her tools through both real provider clients against the mock |

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
- **Keep the structure simple.** Add an interface only where a second implementation is planned (`ICalendarSource`, for Google or Microsoft sign-in later). Stool.cs is big but sectioned; its microphone part became `Listening.cs` when turn-taking was reworked.

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
- the cabinet and her figure;
- voice: GPT-SoVITS, streamed, with lip sync;
- listening: SenseVoice, push-to-talk;
- chat on the player's own provider, with memory v2;
- agenda, reminders, check-ins, usage, ICS calendars;
- from the agent phase (2026-10-05):
  - being cut off: her line keeps only what was heard (`Chat.Interrupt`, `Conversation.CutOff`, `Speech.CutAt`);
  - her body in conversation: nods in pauses (`Backchannel`), looks away to think, finger to her chin, head follows her voice, grown-up applause (agent note §7);
  - hands-free listening: follow-up listening and conversations, the MIC light on the CRT (spec `2026-10-05-hands-free-listening.md`; `App/Listening.cs`);
  - tools groundwork: `Toolbox` (permissions Allowed / Ask / Off, Always per site, an Auto switch), the tool loop in `Conversation` (up to 10 calls a reply), the permission card by the stool (`PermissionCard`), Chat settings → Abilities, and `open_link` (spec `2026-10-05-tools-and-abilities.md`, "Groundwork");
  - memos: `print_memo`, printed out of the slot, pinned on the cabinet's side, read in `MemoReader`, kept in the memory book's Memos tab, the player's own (the CRT's menu or the tray; she thinks each over unseen and acts or stays quiet, `Chat.Consider`, unless kept from her); she types hers on her phone; carried by hand and stuck anywhere on the side, read by pointing at them, a paper colour and a sticker each (spec, "Memos: built", "Memos, next");
  - the CRT's small system: a click on the screen opens its menu (`CrtMenu`); memos first, focus sessions and more to join it.

**Waiting on the user** (all in `docs/to-try.md`; keep it current):
- ~~the agent spike's recording~~ done 2026-10-06 (agent note "The spike's recording"): on their earphones no echo, interrupting by voice feasible; Smart Turn's threshold raised to 0.95. Open: whether they ever use speakers; Chinese and Japanese unrecorded;
- real-use checks: reminders, check-ins, phones, calls, calendars, cache, her body, follow-up listening, conversations.

**Now: tools**, the last part of the agent phase. Draft spec: `docs/specs/2026-10-05-tools-and-abilities.md` (brainstormed 2026-10-05).
- **Decided:**
  - tools are framed like Claude Code's: built-in tools, connectors the player chooses (keys, sign-ins, local MCP servers; no remote MCP), plugins later;
  - acting outside the app asks first;
  - while a tool works, she says a short line the model writes itself, fitting what she's doing.
- **First abilities, agreed:**
  - LeetCode hints in tiers, finding the problem from the browser's window title;
  - memos: long text printed from the cabinet, pinned on its side, archived, the player's own too, and she types them on her phone;
  - focus sessions with stand-up and water nudges;
  - music by a link plus Windows' media controls (Spotify's and YouTube Music's APIs are closed);
  - web search: the provider's own, or Tavily or SearXNG (DuckDuckGo has no search API);
  - opening links and apps.
- **Order:**
  1. groundwork (*built 2026-10-05*);
  2. memos (*built 2026-10-05*);
  3. focus sessions and music;
  4. LeetCode hints;
  5. web search.
- **Next step:** focus sessions and music (the user's music service is still an open question).

**Open questions for the user:**
- which music they use (Spotify desktop or YouTube Music in a browser);
- whether her idle leg swings should be smaller and rarer;
- whether Chat settings is cut off on the right on their real screen (it is in off-screen captures, old version too).

**To check:** about 2.4 s of near-silence once seen at the start of her GPT-SoVITS audio (agent note §5).

**Parked:** a motion-file layer (VRMA, or Kimodo- or Mixamo-sourced clips) for hand gestures while she speaks. Survey in agent note §7.

**After the agent phase:** character import (community cards, your own 3D model and voice), mini-games on the CRT. Steam last: the app should be mature before it goes public.
