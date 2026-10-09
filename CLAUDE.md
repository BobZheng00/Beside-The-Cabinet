# INSERT COIN: working notes

A desktop arcade cabinet (Godot 4.7.2 .NET) for a balance of work, life and games: good work earns gaming time (so far LeetCode solves, spent on Steam games; focus and the player's own activities next). A 3D companion sits on a stool beside it. She chats, remembers, speaks, listens, and keeps the player's agenda.

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
  - `2026-10-07-balance.md` (draft): earning from more than LeetCode (focus, the player's own activities with proof), a daily cap and a nightly fade, pictures and files, rest and sleep, the order from here.
- `docs/research`: research notes.

## Build and test

- `dotnet test InsertCoin.slnx` runs Core, Adapters and Mind.
- **The solution does not include the game.**
  - Build it with `dotnet build src/InsertCoin.Game/InsertCoin.Game.csproj`.
  - Do this after any change under `src/InsertCoin.Game` and before every run. Godot runs `src/InsertCoin.Game/.godot/mono/temp/bin/Debug/InsertCoin.Game.dll`, so a run without that build tests the old code.
- dotnet prints in Chinese on this PC: `已通过` = passed, `失败` = failed, `个警告` / `个错误` = warnings / errors.
- Keep builds at 0 warnings. Windows-only APIs need `[SupportedOSPlatform("windows")]` outside the game; the game itself targets `net10.0-windows10.0.19041.0` (Windows' media controls, a WinRT API, since 2026-10-06).
- Line endings: git has `autocrlf=true` and normalises on commit. Python edits should read and write with `newline=''`.
- **Editing gotchas on this PC** (each cost time on 2026-10-05):
  - Python edit scripts with Windows paths or `\n` in them: write the script to a file with the Write tool and run it. A heredoc garbles backslashes (`\v`, `\a` became control characters), and quotes can break bash. Use `chr(92)` for a lone backslash. Afterwards, check changed files for control characters.
  - Git Bash `sed -i` turns CRLF files into LF. It's harmless (git normalises), but normalise before committing.
  - Arrays for PowerShell scripts (`-Shots 6,9`) don't survive bash: run `shoot.ps1` with the PowerShell tool.
  - Mock servers must be started with `run_in_background`. One started with `&` dies when its shell call ends.

## Running it safely (the user often plays a game while we work)

- **Test runs** use `tools\dev\shoot.ps1 -Hidden -Out <tools>\voice-trial\shots -Tag <name> -Script "action@sec,..." -Shots sec,...`.
  - It uses a throwaway muted profile, ignores Steam, shows nothing on screen and captures from the window itself.
  - `-Window "<title>"` also captures one of the app's own windows. `-Desk` puts all its windows together where they sit (memo sheets beside the cabinet), as `<Tag>-<n>-desk.png`. `-Settings ', "Key": value'` seeds settings.
  - Script times count from about 2 s after launch, shot times from launch: shoot a beat after the last action.
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
- **Test assets** go in `<tools>\...`, not %TEMP%: a cleanup tool on this PC empties %TEMP%. Pictures and files to hand her: `<tools>\agent-trial\files` (`rotated.jpg`: sideways, with an EXIF time; `screen.png`: a stand-in screenshot; `notes.txt`, `hello.pdf`, `dot.png`; `attach_check.cs` sends a picture and a PDF through both real provider clients to the mock).
- **Screenshots and the phone page in tests:** a test run never takes the real screen (`fakescreen:` gives a stand-in) and its phone page listens on 127.0.0.1 only, so Windows' firewall never asks.
- **Mock servers** (never the user's real keys):

  | Command | What it is |
  |---|---|
  | `<tools>\voice-trial\scripts\mock_llm.py 8765 [think]` | OpenAI-compatible; logs `requests.jsonl` |
  | `<tools>\listen-trial\mock_long.py 8766` | long replies |
  | `<tools>\listen-trial\fake_anthropic.py 8770` | Anthropic-style, with cache counts |
  | `<tools>\listen-trial\hook_sink.py <port>` | ntfy/webhook receiver; writes `hooks.jsonl` |
  | `<tools>\listen-trial\cal\test.ics` | a test calendar; serve it with `python -m http.server 8768` |
  | `<tools>\agent-trial\tools\mock_tools.py 8771 [--refuse]` | OpenAI- and Anthropic-style; makes tool calls ("open" in a line calls `open_link`, "memo" writes one, its words coming slowly; told of the player's own memo, a reminder if it says "remind", else `[quiet]`; "focus" starts a 50-minute session, "stop focusing" stops it; "pause the music", "my playlist"; told of a focus turn or a long sitting, a short line; "screen" calls `look_at_screen`; a message with a picture gets `[saw: …]`; "went to the gym" logs it, and a picture while it waits for proof gets `judge_proof`, unsure if the line says "blurry"; "my bedtime is" sets 23:30; "help me with this" reads window titles then looks the problem up; "search for …", "read https://…", "open the app …"; Anthropic-style, "search the web" searches on its side then prints a memo); `--refuse` turns tools down |
  | `<tools>\agent-trial\tools\fake_searxng.py 8889` | a stand-in SearXNG (JSON results; 403 without `format=json`); logs `searxng.jsonl`. Not 8888: that's where the player's own would be |
  | `<tools>\agent-trial\tools\search_loop_check.cs` | `dotnet run search_loop_check.cs`: Anthropic's own search then a memo through the real client, against `mock_tools.py` |
  | `<tools>\agent-trial\probe\hear.cs` | `dotnet run hear.cs <folder>`: her ears (SenseVoice) on WAV files, e.g. her thinking sounds |
  | `<tools>\agent-trial\files\say-*.wav` | short spoken answers made with Windows' speech synthesiser ("Yes.", "Yes, always.", a longer sentence) for `talk:` while her card asks |
  | `<tools>\agent-trial\probe\page.cs` | `dotnet run page.cs <address>`: a real page as `read_page` gives it to her (this PC's DNS can't resolve example.com: use docs.python.org) |
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
- **Her manner is her character's.** The app decides what she does and when (whether she asks, a verdict, an amount, how often); her character card decides how she says it. Prompts say what to get across, never a tone ("tease", "playfully"): a gentle, considerate card must stay so (the user, 2026-10-07).
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
  - memos: `print_memo`, printed out of the slot, pinned on the cabinet's side, read and written on sheets out on the desktop, several at once, each its own small window (`MemoSheet`), kept in the memory book's Memos tab, the player's own (the CRT's menu or the tray; she thinks each over unseen and acts or stays quiet, `Chat.Consider`, unless kept from her); she types hers on her phone; carried by hand and stuck anywhere on the side, read by pointing at them, a paper colour and a sticker each (spec, "Memos: built", "Memos, next");
  - the CRT's small system (`App/Crt/CrtOs`): a click folds the readout away and app icons pop in on the cabinet, four to a page, swiped between, held to rearrange; an app opens out of its icon as the screen lifts out to face the player (readable at any cabinet size, a CRT look up close), and a press away settles it back. Apps: FOCUS, MUSIC, MEMOS, AGENDA (spec, "The CRT's small system", "Two views");
  - balance, step 2 (2026-10-07, spec `2026-10-07-balance.md`, "The ledger: built"): focus pays by the minute (a coin per focused hour), a daily cap on earned play (Monday to Thursday 1:30, Friday 2:00, weekends 2:30; tray → Balance…), half of unplayed time fading each night instead of the weekly expiry, play after a set bedtime costing ×1.5 until 04:00, a record of days in the ledger (`DayRecord`), the LeetCode name optional at setup;
  - the streak counts good days (2026-10-09, spec `2026-10-07-balance.md`, "The streak"): a solve, an activity or 25 minutes of focus; from the third in a row the day's first earning brings a star (`GoodDays`, `StreakStar`);
  - balance, step 6 (2026-10-09, spec `2026-10-07-balance.md`, "Help with learning: built"): hints in tiers for anything (her prompt), `window_titles` (Privacy tab, allowed), `leetcode_problem` (leetcode.com's GraphQL; leetcode.cn by number), `read_page` (public web only, `Pages`, `HtmlText`), `web_search` (Anthropic's own, hosted; or the SearXNG add-on), `look_at_screen` taking the window the player was working in (the card names it), `open_app` (Start menu), Auto pausing for the rest of a reply once she has read the web, Chat settings → Privacy (Auto never skips it; music and record-of-days ticks) and → Add-ons;
  - balance, step 5 (2026-10-08, spec `2026-10-07-balance.md`, "Rest and sleep: built"): words around bedtime (half an hour before, at it, once after), a break after two hours of play, what the record of days shows once a day, asking for a bedtime (`Rest`, `Stool.MaybeRest`, `set_bedtime` confirmed on her card);
  - balance, step 4 (2026-10-08, spec `2026-10-07-balance.md`, "Activities: built"): activities she sizes and the player confirms on her card (`log_activity`; `Activities`), paid like problems once a day (`ActivityDone` in the ledger), proof when the cabinet's rule wants it (`judge_proof`: sure, unsure half, doubtful nothing yet), DONE on the CRT (`DoneApp`);
  - balance, step 3 (2026-10-08, spec `2026-10-07-balance.md`, "Handing her things: built"): pictures, PDFs and text files handed to her with a message (`Attachment`; dropped on her, pasted, the slip's paper clip; chips above the slip), her `[saw]` line kept in their place, models that can't see remembered (`UnseeingModels`), EXIF orientation and time (`Exif`), `look_at_screen` (asks first; `Screenshot`), photos from a phone through a QR code and a page on the home network (`PhoneCard`, `PhonePage`, our own `QrCode`);
  - focus sessions, music and standing up: a Pomodoro counting down on the screen (`FocusSession`, `App/Focus`), she says each turn and has a `focus_session` tool; the player's music app through Windows' media controls (`Desktop/Music`, Spotify first, started when needed), saved playlists started from their beginning by pressing Spotify's own play button through UI Automation (`Desktop/SpotifyButtons`), the starred one starting focus sessions; a `music` tool that asks; after 50 minutes at the PC she suggests standing up and water (spec, "Focus, music and standing up: built").

**Waiting on the user** (all in `docs/to-try.md`; keep it current):
- ~~the agent spike's recording~~ done 2026-10-06 (agent note "The spike's recording"): on their earphones no echo, interrupting by voice feasible; Smart Turn's threshold raised to 0.95. Open: whether they ever use speakers; Chinese and Japanese unrecorded;
- real-use checks: reminders, check-ins, phones, calls, calendars, cache, her body, follow-up listening, conversations.

**Tools**, the last part of the agent phase. Draft spec: `docs/specs/2026-10-05-tools-and-abilities.md` (brainstormed 2026-10-05).
- **Decided:**
  - tools are framed like Claude Code's: built-in tools, connectors the player chooses (keys, sign-ins, local MCP servers; no remote MCP), plugins later;
  - optional extras are **add-ons** (the user, 2026-10-09): off until turned on, each saying what it needs and where data goes; connectors become add-ons. No service of ours and no third party's free tier as a default: "no resources or holdbacks on my side";
  - acting outside the app asks first;
  - while a tool works, she says a short line the model writes itself, fitting what she's doing.
- **First abilities, agreed:**
  - LeetCode hints in tiers, finding the problem from the browser's window title;
  - memos: long text printed from the cabinet, pinned on its side, archived, the player's own too, and she types them on her phone;
  - focus sessions with stand-up and water nudges;
  - music by a link plus Windows' media controls (Spotify's and YouTube Music's APIs are closed);
  - web search: the provider's own (only Anthropic's works through our clients), else SearXNG as an add-on the player runs (DuckDuckGo has no search API);
  - opening links and apps.
- **Order:**
  1. groundwork (*built 2026-10-05*);
  2. memos (*built 2026-10-05*);
  3. focus sessions and music (*built 2026-10-06*);
  4. LeetCode hints and web search: moved into the balance order (below), widened to help with any learning.

**Now: balance** (the user's turn, 2026-10-07: LeetCode and Steam are a small part; any learning or good habit can earn play, for a balance of work, life and games). Draft spec: `docs/specs/2026-10-07-balance.md`.
- **Decided:**
  - three ways to earn: checked (LeetCode), timed (focus sessions, paid by the minute), told (the player's own list of activities);
  - a daily cap on the play that can be earned, and unplayed time fading over days, so it can't be saved up for the weekend;
  - proof for activities when the player wants it: a photo or a screenshot she judges by how sure she is, fun and not annoying;
  - pictures and files handed to her (drop, paste, the phone, a screenshot when asked);
  - rest and sleep: bedtime and long-play nudges, and patterns from a record of days.
- **Numbers, decided 2026-10-07:** a coin per focused hour, paid by the minute at once; caps Monday to Thursday 1:30, Friday 2:00, weekends 2:30 (adjustable); half of unplayed time fading each night; play after a confirmed bedtime costs ×1.5 until 04:00.
- **Also decided:** the player sets neither what an activity is worth nor how much proof it needs; her manner always comes from her character card (the app decides what and when, the card how).
- **Order (proposed):** 1. the spec; 2. the ledger (focus pays, the cap, the fade, the late-night price, the record of days); 3. pictures and files; 4. activities with proof; 5. rest and sleep; 6. help with learning, with web search, and opening apps. *All built by 2026-10-09.*
- **Next step:** the user tries steps 2 to 6 for real (`docs/to-try.md`; the phone's QR code is untested with a real phone; their provider is an OpenAI-compatible server, so no web search without the SearXNG add-on). Then: what the balance order leaves (the streak counting more than LeetCode, clipboard reading, local MCP servers as add-ons), or character import.

**Open questions for the user:**
- ~~whether her idle leg swings should be smaller and rarer~~ keep them as they are (2026-10-09);
- whether Chat settings is cut off on the right on their real screen (it is in off-screen captures, old version too): they are checking it on the demo (2026-10-09).

**Answered 2026-10-09:** earphones almost always (a roommate), so no echo test on speakers; mainly English, sometimes Chinese (a Chinese recording for Smart Turn whenever they like); **thinking sounds: yes** (below).

**On the plate** (explained to the user 2026-10-09; the order is theirs to choose):
1. ~~the streak counting any good day~~ *built 2026-10-09*;
2. ~~thinking sounds~~ *built 2026-10-09* and made in Hana's voice in a test run; how they sound is for the user's ears (`<tools>\voice-trial\thinking`) (tools spec, "Bridging a tool's wait"; `Thinking`, `Voice.MakeSounds`);
3. ~~answering her card by voice~~ *built 2026-10-09*, tried with recordings through her real ears, not yet with the real microphone (`SpokenAnswer`; tools spec, "Not yet");
4. ~~an agenda item that is an activity~~ *built 2026-10-09* (`Activities.Within`, `Agenda.CheckIn`);
5. ~~bedtime drift~~ *built 2026-10-09*: a step towards the bedtime in her notice (`Rest.Drift`, `Rest.Step`); the bedtime itself stays the player's;
6. memos: dragged onto the cabinet's side by hand; her phone and paper on other skins;
7. ~~the 2.4 s of near-silence once seen at the start of her audio~~ cut since 2026-10-09 (`QuietStart`): measured, almost every line began with 0.4 to 1.4 s of it, so she now answers that much sooner;
8. later abilities, most as add-ons: the clipboard (asking first), weather, writing to the calendar (Google or Microsoft sign-in), local MCP servers, a gesture per sentence (parked: motion files);
9. the next phase: character import, mini-games on the CRT, Steam last.

**Release, talked over 2026-10-09** (the user's idea: open source on GitHub, and on Steam at a low price for those who want it easy and updated; the community contributes): an established model (Krita, Mindustry, Shattered Pixel Dungeon). To settle before then:
- the licence, before the first outside contribution: Valve says copyleft (GPL) is problematic with the Steamworks SDK, permissive (MIT, Apache 2.0) is fine; we use no Steamworks SDK today;
- the name, the cabinet's art and the default character kept outside the code's licence, so a resold copy isn't ours;
- every bundled model and library allows commercial redistribution (SenseVoice, Smart Turn, the e5 model…; GPT-SoVITS is the player's own install; test characters never ship);
- the repository's history checked for anything personal before it goes public;
- Steam Direct is $100 per app; Valve takes 30%.

**To check:** about 2.4 s of near-silence once seen at the start of her GPT-SoVITS audio (agent note §5).

**Parked:** a motion-file layer (VRMA, or Kimodo- or Mixamo-sourced clips) for hand gestures while she speaks. Survey in agent note §7.

**After the agent phase:** character import (community cards, your own 3D model and voice), mini-games on the CRT. Steam last: the app should be mature before it goes public.
