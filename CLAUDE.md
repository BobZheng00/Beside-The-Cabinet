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
  - `2026-10-09-a-word-first.md`: what she brings up herself starts with a call and waits for the player's answer; subtitles wait for the reader.
  - `2026-10-07-balance.md` (draft): earning from more than LeetCode (focus, the player's own activities with proof), a daily cap and a nightly fade, pictures and files, rest and sleep, the order from here.
  - `2026-10-10-reaching-the-player.md`: a roommate who calls at the PC and texts when the player is away; texting with her on Telegram, both ways.
  - `2026-10-10-settings-and-setup.md`: one Settings window of short pages, saved as changed; guides for setup outside the app; Getting started; the short tray.
  - `2026-10-11-observational-memory.md`: her memory as a dated log of notes (phases 1 to 4; what is built where).
- `docs/research`: research notes; `2026-10-11-memory-benchmark.md` is how her memory measures up (benchmarks chosen, results, why no vector database).
- `docs/memory-issues.md`: what goes wrong in her memory, with evidence and status (M1 to M14, R1, R2).
- `tools/bench/memory.cs`: the memory benchmark (LongMemEval, LoCoMo, staying in character, the free retrieval score).

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
  - `-KeyFrom "$env:APPDATA\InsertCoin-demo"` runs on the demo's own provider: its AI key copied still encrypted, removed after, and nothing else of theirs. Only when the user offers it for a test (as on 2026-10-11), since it is billed to them.
  - The log is `<Out>\run-<Tag>.log` (and `.err`).
- **Benchmarks of her memory** (`tools/bench/memory.cs`, header has every option; findings in `docs/research/2026-10-11-memory-benchmark.md`, issues in `docs/memory-issues.md`):
  - They run on the demo's own provider and key, read encrypted in place: billed to the user. On 2026-10-11 they allowed any size (their model is a GPT model); still say what runs and roughly how many tokens.
  - Data in `<tools>\memory-bench\data` (LongMemEval S cleaned, MIT; LoCoMo `locomo10.json`, CC BY-NC: never commit). Runs in `<tools>\memory-bench\runs\<name>` (`results.jsonl`, `locomo.jsonl`, `persona.jsonl`; each memory kept in `stores`, and a later run at the same window reuses a LoCoMo memory already taken in). Helpers there: `ref\paired.py <run> <conv> <window>` (question by question against the old memory's runs), `ref\locomo_peek.py`, `reach.sh <label>` (build and print the free retrieval score), `probe\*.cs` (one-off probes against the provider).
  - `--set locomo --mode reach` replays her real search and recall on memories already taken in, no model: seconds, the same every time; try retrieval changes there first.
  - Build the harness to a folder of its own and run the dll (`dotnet build tools/bench/memory.cs -o <tools>\memory-bench\bin-<name>`, then `dotnet <that>\memory.dll …`): a running copy locks `dotnet run`'s output.
  - Git Bash turns `/d/…` into `D:/…` only for the first path of a comma-separated list: write such lists as `D:/…`. A memory another run is still writing keeps its newest pages in `-wal`: copy that too.
  - Run enough for a conclusion, not the full set (the user, 2026-10-11, stopping a LongMemEval run that went on for hours): say how long a run will take, and stop runs once the answer is clear.
  - The user's provider: ignores the output ceiling (asked for 120 tokens it sent 1,916), gives HTTP 524 after 100 s and empty replies when busy. The harness retries 524s and re-asks empty replies; about five runs at once is its limit.
- **Dev script text:** underscores for spaces, no commas.
- **Never open the real microphone in tests.**
- **Her remarks in tests:** when another app holds the microphone (Discord, 2026-10-09), Windows reports a call and she keeps to her bubble, so nothing waits on a call: start the script with `nocall`. A voice linked into a test profile is linked per profile (`profile-<Tag>`).
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
- **Settings in tests:** `settings:<page>` opens it at a page, `settingsdo:<label>[:<value>]` presses, ticks, chooses or types there, `settingswidths` logs a page too wide for the window (capture with `-Window "Settings"`). Never flip "Start with Windows" in a test: it is Windows' real setting, not the test profile's. Don't untick "Mute sounds" either, nor tick a chime "even when muted" before a "Try it".
- **Where the player is, in tests:** a test run takes the player as here (it never goes by the user's own input on this PC); `whereabouts:away` (here, unsure, asleep) says otherwise. Texts go to `hook_sink.py` through `elsewhere:http://127.0.0.1:<port>`.
- **Mock servers** (never the user's real keys):

  | Command | What it is |
  |---|---|
  | `<tools>\voice-trial\scripts\mock_llm.py 8765 [think]` | OpenAI-compatible; logs `requests.jsonl` |
  | `<tools>\agent-trial\body\mock_call.py 8791 [think] "call" "remark" [--fail-calls N]` | OpenAI-compatible; answers her word-first call request (it asks her to "only get their attention") and her remark apart; `--fail-calls N` fails every call request after the first N, to try her last call as the fallback; logs `call-requests.jsonl` |
  | `<tools>\listen-trial\mock_long.py 8766` | long replies |
  | `<tools>\listen-trial\fake_anthropic.py 8770` | Anthropic-style, with cache counts |
  | `<tools>\listen-trial\hook_sink.py <port>` | ntfy/webhook receiver; writes `hooks.jsonl` |
  | `<tools>\listen-trial\cal\test.ics` | a test calendar; serve it with `python -m http.server 8768` |
  | `<tools>\agent-trial\tools\mock_tools.py 8771 [--refuse]` | OpenAI- and Anthropic-style; makes tool calls ("open" in a line calls `open_link`, "memo" writes one, its words coming slowly; told of the player's own memo, a reminder if it says "remind", else `[quiet]`; "focus" starts a 50-minute session, "stop focusing" stops it; "pause the music", "my playlist"; told of a focus turn or a long sitting, a short line; "screen" calls `look_at_screen`; a message with a picture gets `[saw: …]`; "went to the gym" logs it, and a picture while it waits for proof gets `judge_proof`, unsure if the line says "blurry"; "my bedtime is" sets 23:30; "help me with this" reads window titles then looks the problem up; "search for …", "read https://…", "open the app …"; Anthropic-style, "search the web" searches on its side then prints a memo); `--refuse` turns tools down |
  | `<tools>\agent-trial\tools\fake_telegram.py 8893 "3:/start TESTCODE" "8:text:Hi" …` | a stand-in Telegram Bot API (token `123:TEST`): plays the player's side from a script timed from the app's first call (`/start CODE`, `text:`, `stranger:`, `photo:`, `press:0`, `old:minutes:text`); logs `telegram.jsonl`. Pair in a test with `telegramat:http://127.0.0.1:8893`, `paircode:TESTCODE`, `settings:phone`, `settingsdo:Bot_token:123:TEST`, `settingsdo:Check` |
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
- **Fix behaviour in code before prompts.** Prompt rules drift and pile up ("I do not want to fully relay on the prompt and instructions as they can drift away and can get massive", 2026-10-11): prefer narrow requests whose shape the code checks, limits and orders kept by code, and measurements. A prompt line that is needed reads naturally ("try to fix it without hard prompting").
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
  - a word first (2026-10-09, spec `2026-10-09-a-word-first.md`): what she brings up herself with nothing else showing it starts with a call, asked for on its own alongside the remark (`Conversation.Call`, her last one as the fallback), and waits for an answer (`WordFirst`, `Stool.WaitForAnswer`), Yes? and Not now on her bubble, a ticket above every window when unanswered (not for standing up); a reminder and a focus turn go straight on, each with its ticket, and so does her answer to the player's own press on the cabinet (2026-10-10, `Stool.Telling`); subtitles wait for the reader; hands-free answers to her card or call no longer close at once;
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

**Now: her memory** (the user, 2026-10-11: worried that long-term memory gets facts wrong as the talk grows; they care most about the quality of summaries and of fact updates, and want structural fixes rather than ever more prompt rules, which drift and grow). Measured on LongMemEval and LoCoMo with `tools/bench/memory.cs`; spec `docs/specs/2026-10-11-observational-memory.md`; issues `docs/memory-issues.md`.
- **Decided:** observational memory (after Mastra's): a dated log of notes she keeps adding to instead of one summary rewritten each time; narrow requests whose layout, dates and sizes the code keeps; the originals kept and recallable. Moments retire into her notes and the memory book's Moments page lists them; a fact that changes keeps its history (both the user's choice, 2026-10-11). What she always has in mind is listed from her facts by code, not rewritten by the model, and the Notes are the player's own (the user's choice, 2026-10-11, after asking whether any cap on rewrites could get both accuracy and cost right). No vector database (ChromaDB needs a server from .NET; our exact scan is milliseconds); sqlite-vec only past hundreds of thousands of passages.
- **On `main` since 2026-10-11** (06ce77f, merged once LoCoMo showed 84%): phases 1 to 3 (e4ffbeb the log, b63583a nothing kept cut off, 5ca0f8e condensing, 782c0f8 one digestion), retrieval (a9a6dc5) and the cost fix (3afb21d). The demo runs it; its memory from before is in `companion\backup-before-new-memory-2026-10-11`. 2cfd3b0 fixed a version step that ran again at every start (M10). Then the profile change (a39c3e9): the profile listed from her facts (M9), one thing per fact (M8), the Notes the player's, the log's tab the Diary; the demo's memory before it is in `companion\backup-before-profile-2026-10-11`. The game's build output is kept at `main` (rebuild from `main` after building a branch) so the demo never runs unmeasured memory.
- **Measured so far** (details in the research note and the tracker): LongMemEval 10/10 with only the evidence, 10/10 with the whole history in the window, 9/9 with her memory (one question of each kind; the 44-question sample on updates, time and multi-session runs on the old memory). LoCoMo at the 8k window, old memory: 70% strict, 77% Mem0's grade over 6 conversations. Phase 1 on conversation 26 against the old memory: a tie with its first run, 77% against 69% with its second (runs swing about 8 points: compare question by question). Retrieval, free score over 9 conversations: the evidence reached her for 31%, 56% with whole passages. Phase 1's LongMemEval pilot 10/10. Phase 3 on conversation 26: 78% (time questions 31 of 37), but upkeep up to 179k tokens from about 115k (facts inflated, the profile rewritten after every batch). **Phase 3 with 3afb21d and retrieval: 84%** against 75% and 69% for the old memory's runs and 78% for phase 3 on the same questions (multi-hop 15 to 21 of 32; evidence that had left the window reached her 82% of the time, from 43%); upkeep 134k. **The profile change: 84% again** (7 flipped each way), upkeep 116k (the old memory 112k), facts a median of 16 words. With everything in view (the old memory at 32k) 81% of the same questions: the memory is at what the model can do with the whole conversation. Old memory over all of LoCoMo: 69% at 8k (ten conversations), 82% with everything in view at 32k (nine).
- **Measured on the final build (2026-10-11 night):** LoCoMo conversation 42, 80% against 67% for the old memory at 8k and 81% with the whole conversation in view; conversation 44, 84% against 75% and 84%; LongMemEval, 9 of 9 against the old memory's 7 on the same questions, upkeep about 30% less. Never-said questions: not a memory gap (research note). Prompt caching reviewed: the order is right, the lever is the cache's life (research note); no probe for now, the user's call.
- **In flight (2026-10-11 night), and what to do when each finishes:**
  1. ~~`runs\final-locomo-42`~~ done and recorded: 80% (old memory 67% at 8k, 81% with everything in view).
  2. ~~Phase 4 parts 2 and 3~~ measured and merged: from scratch on conversation 26 they first lost 6 questions about her own life, her facts listed in full though some were kept wrong (M11); with her list taking the player's rule (2035efc), answered again on that memory: 128 of 152 (84%) against 127 on `main`, never-said 7 of 47 as before; upkeep 133k against 116k (+15%, threads and her facts).
  3. Then phase 4 part 4, as the user approved it: a "From her memory" button under each of her replies in Chat history, opening what she had in mind (her lists) and what recall brought, each with its kind and date; Forget on every one, Edit on facts; the talk word for word can't be edited.
  The old memory's LongMemEval sample stays paused at 22 of 44 (`memory-32k`); the LoCoMo baselines are done (`locomo`, `locomo-b`, `locomo-c`).
- **Facts checked (2026-10-11 night):** every fact of three LoCoMo memories against its conversation: 87% right, none about the wrong person (research note). Two errors in code, fixed on `main` (da4f101): a changed name written over as a rewording (M11), merges dropping names and numbers (M12). Open: things said in passing with a date reach no note with it (M13).
- **Smoke test, first round (2026-10-11 night, the user):** fixed in fec656e: she kept a `[note: …]` as she replied and said she'd remember after everything told (the note sometimes became her reply, in the third person; the notes went in as important facts outside upkeep's rules), so the rule and the note are gone and upkeep alone keeps facts; music, `open_link`, `open_app` and `look_at_screen` said in their own descriptions that the player is asked first, so she asked "Shall I start some music?" with music set to Always; a reminder's words that failed (the provider's 503) left the cabinet's request in the talk and her next replies said it late, so what the cabinet asks her to say is taken back when nothing comes of it. Not fixed: her Japanese `[say]` sometimes says other words than the bubble or leaves a sentence out (`Speech.Unheard` counts them).
- **Smoke test, step 2 (2026-10-12, the user):** facts, agenda and threads as the checklist expects; one note told of "his previously noted job interview at Acme", never said: the note-taker's own example (M14), fixed in f245bd4, with "they" for the player. Not fixed: she read wttr.in's `?format=3` (the weather now) for tomorrow's, and the note keeps her wrong forecast.
- **Smoke test, steps 3 and 4 (2026-10-12, the user):** every answer right; the move retired as history and its thread settled; Bailey a fact of its own beside Mochi. Fixed the same day: the memory book follows changes while open (unless the player has changes not applied), has Apply beside Save and close, and is 700 wide (a memo's line asked for 400 beside its buttons); a click on her while she is still thinking, nothing of her reply out, no longer stops the reply (a misclick lost one). Not fixed: "How was my day yesterday?" is answered from the cabinet's record alone ("Nothing else notable is recorded") though the day's talk is in view word for word; moving the record into the talk after its day, and rewording it, changed nothing in three tries each, so neither was kept.
- **Less of the cabinet in her mind (2026-10-12, the user: "reduce the weight of the cabinet in the character's cognition as we step away from just a leetcode/game time balancer"):** her turn's `<cabinet>` block has only what bears on the moment (play time, a game on, time owed, the cap reached, past bedtime), her opening says what the cabinet is for in two sentences, a `cabinet_numbers` tool gives the rest (today, the week, the streak, how play is earned, a day's record with what it earned: `Situation.Ledger`), what she witnesses says what happened, not what it paid, the bookkeeping (token in, pause, fade, cap, part of a debt, IOU) is off her bubble and the fade counts down on the CRT (`ScreenView.FlashCount`), and the day's entry leaves out what it earned (`Diary.Accounts`, the tool's; the user's call, reversing "everything the record holds"). Openers on a copy of the demo's memory (`probe\openers_before.cs`, `openers_after.cs`): the cabinet brought up unasked in 5 of 8 before, 2 of 8 after (both a game suggested for tonight); "How was my day yesterday?" now looks the day up and names what was done. A name to be called by waits for Getting started (the user).
- **Left on the memory topic, not urgent:** search warmed up off the main thread (M4); keys for facts that hold one value (home, job), the next step on fact updates: the model's choice of update or add is what remains (a new cat written as a change of the old one); her texts in the day's entry; upkeep cost on long conversations (+20% on conversation 44, M3).
- **Order agreed (2026-10-11):** 1. measure and merge; 2. retrieval (done on its branch, to confirm with answers); 3. phase 4, what makes her a companion: a day's entry from the cabinet's own records (*built 2026-10-11*; everything the record holds, the user's call), her own story (*built 2026-10-11*), open threads to follow up (*built 2026-10-11*; she may text about one; the answer may come by text, in person or not at all), which memories a reply used (its marker and how to fix a memory from it are the user's to choose); 4. small robustness: ask again when the provider sends an empty reply (R1, *done*), character cards cut mid-word at 8,000 characters (*done*), search warmed up off the main thread (M4, not yet).
- **Waiting on the user's call:** her provider's own web search through OpenAI's Responses API (their provider searches that way with a GPT model; it would change her main reply path); "Sign in with ChatGPT" as a way to use a ChatGPT plan instead of an API key (open-source apps may; Anthropic allows no such thing for Claude plans, Agent SDK included).

**Now: settings and setup** (the user, 2026-10-10: "our config is too long and hard to navigate and there is no clear on boarding guide"; spec `docs/specs/2026-10-10-settings-and-setup.md`).
- **Decided:** one Settings window, pages listed by what the player wants to do, saved as changed (a connection waits for its own button or guide); guides a step at a time for setup outside the app (Telegram's first, paired by a code, no chat id); Getting started after the initials; a tray of about ten entries.
- **Order:** 1. the window and the short tray (*built 2026-10-10*: `SettingsWindow`, a page class each, `Form`; the old windows are pages now, Chat settings split into Her mind, Talking aloud, Abilities, Privacy, Add-ons and Usage); 2. reaching the player's step 2 (below), its settings on Your phone (*built 2026-10-10*); 3. Telegram both ways with the first guide (*built 2026-10-10*); 4. Getting started, with guides for her mind and the rest.

**Next: reaching the player** (the user's turn, 2026-10-10: her as a roommate in real life; spec `docs/specs/2026-10-10-reaching-the-player.md`; its settings are on Settings → Your phone).
- **Decided:**
  - features only hand over something to tell, with how much it matters (must, should, could) and where it makes sense (anywhere, only at the PC); where the player is (here, unsure, away, asleep) is worked out in one place, and one table decides voice, a word first, or a text;
  - one setting, how much she reaches out: only what you set, like a roommate (default), keeps in touch (texts of her own: some players want a companion app that reaches out);
  - one conversation and memory, at the PC and by text; she answers where the player last spoke to her;
  - Telegram first (paired by a `?start=` code, locked to that chat; not end-to-end encrypted, and the window says so). Discord's text isn't end-to-end encrypted either; Discord and a private route (Tailscale and the phone page) come later.
- **Order:** 1. the spec (*2026-10-10*); 2. where the player is and the routing, today's features moved onto it, the one-way places in her words (*built 2026-10-10*: `Reaching` and `Telling` in the Mind, `Stool.Hand`/`PassOn`/`Track`, `Chat.Text`; the spec's "Built" section has what differs from the plan); 3. Telegram both ways (*built 2026-10-10*: `Telegram`/`Pairing` in the Mind, `Texting` in the game, `Stool.Texted`, the steps on Your phone); 4. how much she reaches out (*built 2026-10-10*; her own texts planned by her since 2026-10-11: `Reach`, `Later`, `Stool.KeepInTouch`); 5. later: voice notes, Discord, the private route, a channel for mainland China.

**Open questions for the user:**
- ~~whether her idle leg swings should be smaller and rarer~~ keep them as they are (2026-10-09);
- ~~whether Chat settings is cut off on the right on their real screen~~ Chat settings is part of Settings now (2026-10-10); a too-wide row of buttons (the voice page's) was found by `settingswidths` and fixed. Whether anything is still cut off on their screen is in `docs/to-try.md`;
- a remark she starts herself is easy to miss when the player isn't expecting it (2026-10-09, the bedtime question as they sat down). Fixed: she waits until they've settled (`Stool.Settled`). **Built 2026-10-09: a word first** (spec `2026-10-09-a-word-first.md`): her call first, going on once they answer (aloud, or a click on her or her bubble), a ticket above every window when unanswered for 20 seconds (their headset on the desk), subtitles that wait for the reader. Not built, the user's call: replaying her last line ("we can ask her to reexplain"). Changed 2026-10-10 after their first try (their model wrote only the call, once in Japanese, and the blinking triangle said nothing to them): her call is asked for on its own alongside the remark (the user's design: no model can lose the remark), and her turn says she writes in the player's language; Yes? and Not now on her bubble; reminders straight on. Tried by them the same night (F8, Yes?): works on their model; the remark had opened with their name again after the call, so it is now told not to;
- what she calls the player (2026-10-10): their LeetCode name fills a card's {{user}} and the cabinet block, so Hana says "BobSmith" (ボブスミスさん), not "Bob". A name of their own to be called by (at setup, or a setting), or with character import;
- **decided 2026-10-09:** a Japanese voice calls the player by name with さん by default ("most English-speaking users expect it"), unless the card or the player says otherwise. Her lines in the history show only what was shown, never a [say], so the model copied that and left [say] out after the first sentence (heard: "おかえり、ボブ！", the rest silent); her turn now ends with a `<voice>` reminder, and the log counts sentences that went unheard (`Speech.Unheard`). 2026-10-10: "BobSmithさん？" showed in her bubble, with no [say], so the call went unheard: the さん line was an order about calling the player (and her call request asks just that), it clashed with "nothing added or left out" in the subtitle, and a line with さん looked Japanese, so [say] was left out. Now さん is how a Japanese speaker says the name, the subtitle a translation that has just the name, and [say] is left out only when all of it is in her voice's language (`Prompt.Voice`). The same day a remark of hers came in Chinese though the player last wrote in English: she now writes "in the language the player last wrote in". 2026-10-11, on their own model: as the player came back she copied the voice's example ("おかえり！ Welcome back!") whole, all in Japanese; the example is now a line no moment of the cabinet's calls for, and the next try greeted them in English.

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
