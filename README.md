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
(size, companion, speech bubbles, characters, mute, in-game tickets, energy saver, always on top, start with
Windows, change player, quit).

The companion on the stool follows the mouse pointer with her eyes and head, waves when clicked, and reacts to
what the cabinet does: claps for a Hard solve, a small fist pump for the others, glances at the screen, worries
about debt, dozes while the session is paused. In a conversation she looks away to think and back to answer, nods
in your pauses as you talk to her, and her head follows what her voice stresses. She says a line about it in a speech bubble; click her for where things stand. The tray menu switches her
off, silences her, picks another character or adds one; a model file dropped on the cabinet adds it too.

She can also talk. In tray → "Chat settings…" pick a provider (OpenAI, Anthropic, OpenRouter, DeepSeek, Qwen, a
local Ollama or LM Studio, or any OpenAI-compatible server), paste your own API key and name a model. Then clicking
her opens a slip at the foot of the stool: type, press Enter, and her answer streams into the bubble while she acts
it out. She knows the cabinet's numbers and remembers:

- the conversation itself, one continuous thread per character. The newest part (Chat settings → "Word for word":
  32k tokens by default, 8k with a server on this PC) is sent word for word, only ever growing at its end, so
  providers serve it from their cache at a tenth of the price (Claude is asked to cache it); older talk is folded
  into a summary, normally while the conversation is quiet. Each turn tells her the time and how long since you
  last talked;
- facts about you (shared by every character) and about the two of you, plus dated moments. When you just tell her
  something worth keeping she says she'll remember it and jots it in a little notebook; everything else is picked
  up quietly by upkeep, which reads the conversation once it has been quiet for ten minutes (two while a game
  runs), checking it against the facts that relate to it. A moment is one to three sentences (at most 60 words);
  a day gets up to three, one per thing that happened (a later talk on the same thing rewrites its moment), and
  small talk none;
- two short notes she always has in mind, rewritten by upkeep when something important changes;
- your agenda: plans, exams and appointments with their dates, which she writes down when you mention them (or
  upkeep does later), and reminders you ask her for ("remind me at 9 to call Mum", "an hour before the interview",
  "vitamins every day at 8": a reminder can be some time ahead of its item and can repeat daily, on weekdays, weekly
  or monthly). What is coming up in the next week, and what has just passed (so she can ask how it went), is in
  every turn; two days after, a one-off item leaves the agenda and a repeating one moves on to its next time.
  A reminder goes off on a ticket at the screen corner with Snooze and OK, and she tells you in her own words and
  your language once she has finished what she is saying (never cutting herself or you off). Tray → "Reminders…"
  sets how long the ticket stays and a snooze lasts, a reminder before every timed item on the agenda, a chime
  (even when muted), a Windows notification, and your phone or elsewhere through [ntfy](https://ntfy.sh), a Discord
  webhook, a Telegram bot or any webhook (addresses and tokens stored encrypted, "Try it" to check). One that came
  due while the cabinet was closed goes off when it next starts, saying so. Tray → "Calendars…" links your Google
  or Outlook calendar by its private address (read every 15 minutes, read-only, the address stored encrypted): its
  next two weeks join the agenda;
- she asks how things went: after something you told her about is over, once you are back at the PC and free (not
  in a call, which she tells from another app using the microphone or camera, not in a full-screen game or
  presentation, not outside 08:00–22:00), once, and only if you have not talked since. Gentle (at most one every
  three hours), Chatty or Off in tray → "Reminders…". Not for each calendar meeting, nor for plain reminders;
- and before each reply, whatever of all that relates to what you said, including a line or two from any earlier
  conversation, however old (the whole conversation is kept and searched): by meaning, with a small model that runs
  on this PC (so "nervous about tomorrow" finds your interview, in English or Chinese), and by shared words.
  Chat settings → "Memory search" picks the multilingual model (about 100 languages, about 200 MB of memory while
  chatting), the small one (Chinese and English, about 50 MB) or shared words only;
- what has not come up in a long time fades (a forgetting curve: importance and every recall make a fact last
  longer; pinned and the most important ones never fade), and old moments are rolled up: days into weeks after
  two weeks, into months after three months, into years after two years, so they stop piling up. About weekly,
  while quiet, she reviews her facts: what says the same thing is merged, and what is out of date (a plan already
  past, something since changed, trivia that never came up again) is retired. Retired is not deleted: the memory
  book lists it under "Retired by her review", to bring back, and it comes back by itself if it comes up again.

A SillyTavern-style character card (`.png` or `.json`, V1 to V3) dropped on the cabinet or picked with "Add a
character…" gives whoever is on the stool its personality, example dialogue and lorebook (background that comes
up when its keywords are mentioned); she greets you as the card says.

She can speak aloud too, with a voice from GPT-SoVITS running on this PC (its official Windows package; a laptop
graphics card starts each sentence in about a second). Tray → "Voice settings…": point it at the GPT-SoVITS folder,
then give her a voice: drop on the cabinet (or add in the window) a voice's folder or `.zip`, which replaces her
voice, or clips, which join it. A voice is clips of one person, 3 to 10 seconds each, with the words said in them, and maybe a model someone
trained on them (a `.ckpt` and a `.pth`; v2 to v4 all work). The words come from a training list (any `.list`,
one line per clip: `clips/001.wav|hana|JA|今日も頑張ろうね。`), a `.lab` beside a clip, or the clip's file name
("【开心】今天也要加油哦.wav", the mood in brackets picking it for that mood); if none says them the window asks.
"What goes in a voice folder?" in the window says the same. For example:

```
hana/
  hana.list
  clips/hana_001.wav, hana_002.wav, …
```

Without a model she speaks as a clone: GPT-SoVITS's base model copies the tone of her speaking clip (the one nearest
6 seconds, or the one chosen and listened to in the window). The base model is v4 (the clearest; it makes each
sentence whole before she says it, 1.5–3.5 s) or v2ProPlus (streams, so she starts in about half a second; a little
less sure of hard words), which also blends in the tone of her first four clips for about 0.1 s more per sentence.
With a minute or more of clips (five sounds better), "Train a model on its clips" trains her own: v2ProPlus, the way
GPT-SoVITS's web UI does it, about a second per second of speech plus a minute (Hana's five minutes took 5.4 on a
laptop graphics card with 8 GB, which it all but fills; once it starts it tells the time left, and after one run the estimate is
this PC's own); her voice rests meanwhile, and a game starting stops it. The model
(about 0.3 GB) goes into the voice's folder, and the window swaps between it and the clones. A voice speaks one language
(Japanese, English or Chinese): when you write in another, she speaks her own and the bubble shows your language,
like a subtitled anime (the model writes `[say: …]` before each sentence). While a game runs she rests her voice:
the server is stopped so the game has the graphics card, and started again after. Tray → "Voice" switches it off;
"Mute sounds" mutes her too. Clicking her stops her mid-sentence, and so does talking over her with the talk key
or asking something new. She then remembers only what you heard, ending where she stopped, plus what she hadn't said
yet, so she can come back to it. Without her voice, what you heard means the pages of her bubble you had seen.

You can talk to her aloud as well. Hold the mouse on her, or hold the talk key in any app (Right Ctrl unless Chat
settings says otherwise), say it, and let go: she leans in to listen, nodding in your pauses, and answers what she
heard as if you had typed it. The microphone at the end of the slip writes what you say into the slip instead, to change before you send it.
She hears on this PC, on the processor, with SenseVoice-Small (Chinese, English, Japanese, Korean, Cantonese, told
apart by itself): about a tenth of a second for a sentence. The microphone is open only while she listens, and
nothing is recorded or kept. The talk key's state is read each frame (no keyboard hook), so the key still does what
it does in the app in front.

She can also listen without the key, if you choose (Chat settings → Listening). With follow-up listening she listens a few
seconds after she speaks (3 to 20, after her answers, her own questions or anything she says, as ticked), and if you
start talking she hears it as your answer. A conversation (the tray's Conversation switch, or a double tap of the talk
key) has her listen after everything she says, until you've been quiet for 30 seconds. She notices you start and finish
by herself (Silero's voice detector and Pipecat's Smart Turn, on the processor). She never listens on her own in a call,
in full screen or quiet time, or while a game runs, unless you allow it, and never while she is speaking. The CRT shows
MIC whenever the microphone is open.

She can do things too, with tools (Chat settings → Abilities). So far she can open a web page in your browser. She
says what she's about to do in her own words, and anything acting outside the app asks first: a card by her stool
shows what and where (the real address, whatever she said about it), with Allow once, Always (for that site) and No.
Each tool can be Allowed, Ask me first, or Off; the sites you said Always to are listed there to forget. Auto lets her
go ahead without asking at all (what's switched off stays off). A model that
can't take tools (some that run on this PC) answers as before. Design:
[docs/specs/2026-10-05-tools-and-abilities.md](docs/specs/2026-10-05-tools-and-abilities.md).

Anything too long for her bubble (a hint written out, a plan, a list) she hands over as a memo. She types it on her
phone and sends it, and it prints out of the cabinet's slot. Click it to read it whole (code in a fixed-width font),
copy it, pin it on the cabinet's side (five fit), or tear it off. Every memo is kept in the memory book's Memos tab,
where you can write your own too: click the cabinet's screen and open MEMOS (NEW; an app lifts the screen out to face
you, readable at any cabinet size, and a click away settles it back), or tray → "Write a memo…".
They print the same way, and she reads them unless you untick "Let her read it": if one needs something from her (a
reminder, a date), she does it and says so; if not, she says nothing. Point at a memo on the cabinet to read it there.
Click one and it flies out onto your desktop as a sheet beside the cabinet, to read and write on: drag it anywhere,
open several side by side, and its × (or Esc) sends it back.
Pull one off the slot (or off the side) and stick it anywhere on the cabinet's side; pull it off the cabinet to take it
down. Each can have its own paper colour and a sticker. Memos are yours: forgetting leaves them be.

**Focus sessions and music.** The screen's FOCUS app runs a Pomodoro (25 minutes of focus, 5 off, 15 after every
fourth; down and up beside each length change it): the CRT counts down, and she tells you when each part ends, with a nudge to stand
up and drink some water at the breaks. Ask her to start one ("let's focus for 50 minutes") and she does. MUSIC shows
what your music app is playing (Spotify first) and plays, pauses and skips it through Windows' media controls (with
nothing open, play starts Spotify for you); paste your
focus playlist's link (Spotify: Share, Copy link) and, with WITH FOCUS on, it plays while you focus and pauses at breaks.
She can control it too, after asking. And after 50 minutes at the PC without a break, she suggests standing up (tray →
Reminders… to change it).

Chat settings → Usage shows what her requests used, as your provider reports it, this month and day by day:
tokens read, the share served from the provider's cache, cache writes and output, her replies apart from her
upkeep; with the model's prices entered (from your provider's price page), the cost too.

Tray → "Chat history…" shows the conversation itself, every line kept, by day (with the summary she works from
above it). Tray → "Memory book…" shows all of it, a tab each (notes, agenda, facts, moments, forget): edit, pin
(always in mind, upkeep leaves it alone) or delete anything.
"Clear the chat" starts the conversation afresh and keeps what she remembers; "Forget all with …" clears the
conversation and everything about the two of you; "Forget all about me" clears what every character knows about
you. It stays in the profile folder (`companion\memory.db`); the key is encrypted for your Windows
account. Without a provider she keeps to her canned lines.

The window draws at 60 frames a second, and at 15 while a game runs or with "Energy saver" ticked (moments
worth watching still get a few seconds at full pace).

Dev keys (debug builds, click the cabinet first): `F1/F2/F3` solve Easy/Medium/Hard · `F5` fake game on/off ·
`F6` clock ×60 · `F7` AFK on/off · `F9` wipe state · `Esc` quit.
Arguments after `--`: `--data=DIR`, `--no-steam`, `--script=hard@2,game@5,warp@5` (dev actions at given seconds).
Companion dev actions: `moment:cheer` (or `pump`, `greet`, `glance`, `alarm`, `worry`, `doze`, `wake`),
`point:x:y` (a pretend mouse pointer at that canvas point), `character:id` (swap who sits on the stool),
`say:some_text` (a speech bubble), `quiet` (toggle speech bubbles), `click` (as if she were clicked), `saver`
(toggle the energy saver), `legs:left|right|both|ring` (which feet hang off the stool's ring), `add:C:\path\model.glb`
(as if the file were dropped on the window), `pick` (the "Add a character…" dialog), `tray:x:y` (open the tray
menu at that screen point), `chat:some_text` (say something to her), `chatbox` (open the slip), `chatsettings`,
`memorybook` (`memorybook:agenda` at that tab: notes, agenda, facts, moments, forget, or retired), `ticketpress:0`
(the showing ticket's Snooze; 1: OK), `chathistory`, `memoryseed` (a few notes, facts,
agenda items, retired facts and a moment to try it on), `remind:some_text` (a reminder goes off now),
`remindin:seconds:some_text` (sets one that far ahead; negative: one missed while closed), `remindersettings`,
`remindertry` (its "Try it"), `elsewhere:http://127.0.0.1:port` (ntfy and the webhook pointed at a local test
server), `checkin` (she asks about something past now), `presence` (logs do-not-disturb, call, idle),
`pastplan:minutes:some_text` (something on the agenda that ended that long ago), `usage` (the Usage tab),
`calendar:URL` (links a calendar named Test), `calendars`, `forget:chat|all|me` (its three forget
buttons), `upkeep` (digest the conversation now), `moment:note`, `voicesettings`, `voicetest` (her voice's
sample line), `voice:some_text` (a bubble she also says aloud, if it is in her voice's language) and
`voicemodel:v2ProPlus` (speak as a clone with that base model, or `voicemodel:trained` with its own, as saving the
window does), `voicetrain` (train her voice, as the window's button does) and `voicetrainstop`, `hear:C:\path\said.wav`
(a recording she hears as if said holding the talk key), `talk:C:\path\said.wav` (the same, played out in time: she
listens and nods for its length first) and `dictate:C:\path\said.wav` (as if said into the slip's
microphone), and, opening the real microphone, `mic` (click the slip's microphone), `listen` and `listened` (as if
held on her, then let go), and `closeup:hands` (her camera narrowed onto her hands, to judge fingers; `closeup:off`).
Hands-free, without ever opening the real microphone: `fakemic` (from then on she hears only recordings),
`mictalk:C:\path\said.wav` (said into her open microphone), `converse` (`converse:on`, `converse:off`) and
`listeningsettings` (the Chat settings Listening tab). Her tools: `allow`, `always` and `deny` (answer the card she
asks with, as its buttons do) and `abilities` (the Chat settings Abilities tab). With `--offscreen` a page she opens is
only logged, never opened. Memos: `memo:title:text` (she types one and sends it, as the tool has her do),
`memoseed` (memos hanging, pinned and kept), `memoopen` (the one hanging; `memoopen:0` the one pinned first),
`memosheet:pin` (the latest sheet's tools: pin, off, edit, done, print, delete; `close` and `esc` as its × and Esc;
`memosheet:move:-200:100` drags it, in screen pixels),
`memowrite` (one of your own),
`memonote:title:text` (one of your own, printed as if saved: she thinks it over), `memohover` (a pretend pointer on
the hanging memo; `memohover:0` the top one on the side, `memohover:off`), `memocarry:a:b` (the hanging memo carried
by hand to that spot on the side; `memocarry` alone, off the cabinet), `memosheet:colour:mint`, `memosheet:sticker:cat`,
`memosheet:write:title:text` and `memosheet:tell` (a sheet's paper, sticker, fields and tick), `crt` (the screen's
system opens; `crt:memos` an app too), `crtclick:x:y` (a click on the screen, in its own pixels), `crtmove:0:1` (an app
moved, as holding and carrying it would), `crtaway` (a press away from the lifted screen), `crtclose` and
`memorybook:memos`. Focus and music: `focus` (a session started from the screen; `focus:skip`, `focus:pause`,
`focus:resume`, `focus:stop`), `focustool:start:50` (as her tool would), `standup` (she suggests standing up, as after a
long sitting), `musicfake:Title:Artist` (pretends a music app plays; `musicfake:off`), `musiclink:<link>` (as if pasted
into MUSIC), `musictool:pause` (her tool), `musicprobe` (checks Windows' media controls answer: how many sessions,
whose; no titles).
`--offscreen` opens the app's windows off
every monitor; `tools\dev\shoot.ps1 -Hidden` uses it to test without anything appearing on screen.
`tools\dev\clip.ps1` records a clip instead, to judge motion: Godot's movie writer renders every frame off screen
(`-Script "moment:cheer@3" -Seconds 6`), and ffmpeg makes a GIF and an MP4 of her.

## Layout

```
src/InsertCoin.Core       rules as a pure function (Ledger.Step) — no I/O, no clock
src/InsertCoin.Adapters   LeetCode GraphQL source, Steam registry detector, atomic JSON store
src/InsertCoin.Mind       the companion's mind, no Godot: provider connection (Microsoft.Extensions.AI), conversation
                          store (SQLite), prompt, stage directions, auto-compaction, encrypted key; Tools/: her
                          tools and their permissions (Toolbox) and the built-in ones (BuiltIn); memos (Memo);
                          Voice/: what of
                          a reply is spoken, voice profiles and their import, the GPT-SoVITS client, training
                          (VoiceTraining, and train_voice.py, run with GPT-SoVITS's Python), hearing (SenseVoice
                          through sherpa-onnx)
src/InsertCoin.Game       Godot 4.7 (.NET) app: Main (the cabinet's loop: ledger, screen, window, tray), App/Stool (whoever
                          is on the stool: her figure, chat, voice and their windows), cabinet scene, companion,
                          director, desktop window, tray, audio
tests/                    xUnit: every rule with a fake clock; adapters with recorded responses; the mind with a fake model
tools/art                 cozy_cabinet.py — the cabinet is a 3D model rendered to SVG, exported as skin layers;
                          app_icon.py — the app icon (.ico for the .exe, .png for the window)
tools/sfx                 generate.py — chip-style synth that writes the sound effects
tools/models              fetch.py — downloads the local models: her memory search's, her ears' (SenseVoice)
tools/dev                 shoot.ps1 runs the app in a throwaway muted profile and screenshots it (-Window: one of its
                          own windows too; -Desk: all its windows where they sit together); clip.ps1 records a clip of
                          her moving; sheet.ps1 makes contact sheets
art/cozy                  previews and layer SVGs (sources of the PNGs in the game)
```

## Build

```
dotnet test                                   # rules + adapters
dotnet build src/InsertCoin.Game              # game assembly
python tools/art/cozy_cabinet.py --skin       # regenerate art -> src/InsertCoin.Game/skins/cozy (needs Chrome)
python tools/sfx/generate.py                  # regenerate sounds -> src/InsertCoin.Game/sounds
python tools/art/app_icon.py                  # regenerate the app icon -> src/InsertCoin.Game/icon.ico, icon.png (needs Chrome)
python tools/models/fetch.py                  # local models -> src/InsertCoin.Game/models (not in git: 380 MB)
```

The models are optional: without the memory models she recalls by shared words only, and without SenseVoice
(`fetch.py listening`, 240 MB) she can't be talked to aloud. `build.cmd` fetches all three when the folder is
missing; `python tools/models/fetch.py small` beforehand gives a lighter build with only the small memory model.

### Standalone build

```
python tools/export/fetch_template.py --get <folder>/templates/4.7.2-stable-mono   # once: 40 MB instead of the 1.2 GB archive
build.cmd                                                                                  # -> builds\InsertCoin\InsertCoin.exe + data folder
```

The export preset points at that template path. Godot's exporter needs `src/InsertCoin.Game/InsertCoin.Game.sln`
(the repo-level solution is `.slnx`). Only one copy of the app runs at a time: a second launch exits quietly.
Godot's exporter writes the app icon (`icon.ico`) and the product name into the `.exe` itself; no `rcedit` needed.

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
- `SpeechBubble` draws what she says: canned lines from `App/Lines.cs` (built from the ledger's numbers) type
  themselves out; replies from her mind start as thinking dots and grow as they stream in. A long one is shown a
  page at a time, like subtitles: each page turns once her voice has said it (`Voice.Progress`), or, when she is not
  speaking, once there has been time to read it, and the bubble fades only when she has finished. `ChatBox` is the slip
  you type on, `Paper` the shared look (cream paper, the cabinet's ink, Fredoka).
- `App/Chat.cs` connects her to `InsertCoin.Mind`: sends what you type, acts out stage directions (`[cheer]`,
  `[wave]`, `[worry]`, `[note: …]`…), logs cabinet events into the conversation, folds it when it grows, runs
  upkeep every minute, and lets her use her tools (`Toolbox` and `BuiltIn` in `InsertCoin.Mind`; `PermissionCard`
  is where she asks). `App/ChatSettings.cs` is the provider window, `App/MemoryBook.cs` the memory book. In
  `InsertCoin.Mind`: `Recall` picks memories, `Upkeep` holds the digest and note-rewrite requests. Design: [docs/specs/2026-10-02-companion-helper.md](docs/specs/2026-10-02-companion-helper.md),
  memory: [docs/research/2026-10-02-companion-memory.md](docs/research/2026-10-02-companion-memory.md).
- `App/Voice.cs` is her voice: `VoiceServer` runs the player's GPT-SoVITS hidden (tied to the cabinet, so it ends
  with it), `Speech` (in `InsertCoin.Mind`) decides what of a reply is said, `GptSoVits` streams each sentence's
  audio, and her mouth (`Face.Mouth`) follows its loudness. `App/VoiceTrainer.cs` trains a voice's own model (both
  run tied to the cabinet through `CabinetJob`). `App/VoiceSettings.cs` is its window. Measurements:
  [docs/research/2026-10-02-companion-voice.md](docs/research/2026-10-02-companion-voice.md).
- Her ears: `App/Ears.cs` opens the microphone while she listens (Godot records it into a muted bus of its own),
  `TalkKey` reads the talk key, and `Hearing` (in `InsertCoin.Mind`) turns what was said into words with
  SenseVoice. `App/Listening.cs` ties them together: held on her, the talk key, the slip's microphone, and hands-free
  (`HandsFree`, `TurnTaking`, `VoiceDetector`, `SmartTurn` in `InsertCoin.Mind`). Design:
  [docs/specs/2026-10-05-hands-free-listening.md](docs/specs/2026-10-05-hands-free-listening.md).
- `App/Stool.cs` holds whoever is on the stool: her figure, the chat and its slip, her voice and its training, the
  windows for them, and characters, cards and voices coming in. `Main.cs` is the cabinet's own loop.
- `Rig`, `Puppet`, `Face` move her. Three layers each frame: poses (seated, plus reaction poses switched by
  short timelines, hands placed by two-bone IK), springs (every joint follows its pose as a damped spring, looser
  towards the hand, so moves overshoot and settle), and life on top (breathing, sway, head drift, fidgets, eyes
  that jump between fixation points, blinks). Poses use VRM humanoid bone names and are fitted to the character's
  proportions (hip height, leg length), so they are not tied to one model.
  In conversation (`Moment.Listen`, `Think`, `Speak`, `CutOff`) her gaze follows what people do: away, up and to
  one side, to think (her finger goes to her chin when it takes a while); at you while you talk, nodding in your
  pauses (`Backchannel`, in `InsertCoin.Mind`, finds them from the microphone's loudness); her head dipping where
  her voice swells as she talks. Her moves are kept small and asymmetric, the way a grown-up moves. The research
  behind it: [docs/research/2026-10-05-companion-agent.md](docs/research/2026-10-05-companion-agent.md) §7.

The character stays at the 3D origin and the camera is placed around her: the VRM plugin's hair physics goes wrong
when the character itself is moved or turned.

### Adding a character

- **As a user, no tools needed:** tray menu → "Add a character…" and pick a `.vrm`, `.glb`, `.gltf` or a `.zip`
  holding one (Sketchfab's glTF downloads come zipped), or drop the file on the cabinet. It is copied into
  `characters` in the profile folder (`%APPDATA%\InsertCoin\characters`; a `.gltf` gets a folder of its own with
  the `.bin` and textures it refers to) and takes the stool at once. A file of the same name is replaced.
  "Open characters folder" shows that folder; files put there by hand appear after a restart. Files are read at
  run time, in the standalone build too.
- **Built into the app:** put the model at `src/InsertCoin.Game/characters/<id>/<id>.vrm` (lower-case id) with a
  note of its source and licence, then re-import:
  `Godot_..._console.exe --headless --path src/InsertCoin.Game --import`.

Either way `"Companion": "<id>"` in `settings.json` selects it (the id is the file name).

Their voice, if they have one, is in `<id>.voice` in the characters folder: `voice.json` (language, clips and
their words, quality), the model's weights and clips. Dropping a voice on the cabinet puts it there for whoever is
on the stool (or for a model dropped with it).

Who they are when they talk is a character card if they have one (`<id>.card.png` or `.json` in the characters
folder, or `card.png` / `card.json` in their own folder; dropping a card on the cabinet puts it there), else a
plain-text description: `persona.md` in the character's folder (built-in ones ship one), or `<file name>.md` beside
a loose model file. Without either they are a plain friendly companion.

What each kind of file gets:

| | VRM made with VRoid Studio | other VRM | GLB / glTF with a humanoid skeleton |
|---|---|---|---|
| sits, gestures, follows the pointer | yes | yes | yes |
| ink and two-tone shading | yes | yes | yes |
| cabinet palette | yes (by VRoid's material names) | own colours | own colours |
| expressions and blinking | yes | yes | only if its blend shapes have recognisable names |
| hair and cloth physics | yes | yes | bones named hair, skirt, tail… get springs; legs are solid to them, arms to hair; skirt panels follow the thighs |

A plain GLB does not say which bone is which, so `HumanoidMap` works it out from the usual naming habits (Mixamo,
Unreal, Blender, Biped, Daz) and the skeleton's shape; `Rig` then copes with any units, facing and rest pose (T or
A). A model without a skeleton (a sculpt, most OBJ-derived files) cannot be posed: the stool stays empty and the
CRT says why. OBJ and USDZ are not read at all; they carry no skeleton.

The skin's canvas is wider and taller than the cabinet art (see `layout()` in `tools/art/cozy_cabinet.py`) so that
a raised arm stays inside the window.

## Assets

Fonts: Press Start 2P and Fredoka (SIL OFL 1.1, licenses in `src/InsertCoin.Game/fonts`). Art and sounds are
original, generated by the tools above.

Companion: the VRM importer and MToon shader are [godot-vrm](https://github.com/V-Sekai/godot-vrm) (MIT, licences in
`src/InsertCoin.Game/addons`). The characters are VRoid's samples "Sendagaya Shino" and "Sakurada Fumiriya",
released by pixiv under CC0 (see `SOURCE.txt` in each folder under `src/InsertCoin.Game/characters`); their
`persona.md` descriptions are ours. Memory search models (fetched, not in git): multilingual-e5-small (intfloat,
MIT) and bge-small-zh-v1.5 (BAAI, MIT), ONNX exports by Xenova; each folder under `src/InsertCoin.Game/models`
carries its licence note. Run with ONNX Runtime (MIT) and Microsoft.ML.Tokenizers (MIT).
