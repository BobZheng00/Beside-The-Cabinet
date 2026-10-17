# Dev keys, dev actions and test runs

What debug builds and test runs have for working on the app, and the scripts that run it in a throwaway profile.
Building and running it: [src/README.md](../../src/README.md); what each feature does:
[docs/README.md](../../docs/README.md).

## Dev keys

Dev keys (debug builds, click the cabinet first): `F1/F2/F3` solve Easy/Medium/Hard · `F4` the companion asks for your bedtime
now (a word first; unanswered, its ticket) · `F5` fake game on/off · `F6` clock ×60 · `F7` AFK on/off · `F8` they
suggest standing up now (a word first, no ticket) · `F9` wipe state · `F10` their upkeep now (notes, facts, plans and
threads from the talk so far, without waiting for ten quiet minutes) · `Esc` quit.

## Arguments

Arguments after `--`: `--data=DIR`, `--no-steam`, `--script=hard@2,game@5,warp@5` (dev actions at given seconds).

## Dev actions

Each runs at its time in `--script=action@seconds,...`; text in one has underscores for spaces
and no commas.

Companion dev actions: `moment:cheer` (or `pump`, `greet`, `glance`, `alarm`, `worry`, `doze`, `wake`),
`point:x:y` (a pretend mouse pointer at that canvas point), `character:id` (swap who sits on the stool),
`manner:relaxed` or `manner:neat` (how whoever is on the stool sits), `showmanner` ("Show me" on it),
`say:some_text` (a speech bubble), `quiet` (toggle speech bubbles), `click` (as if the companion were clicked), `bubbleclick`
(a click on the bubble's words while it waits on their call), `bubblechip:yes` or `bubblechip:notnow` (that answer on the
bubble clicked), `nocall` (another app's microphone doesn't count as a call), `saver`
(toggle the energy saver), `legs:left|right|both|ring` (which feet hang off the stool's ring), `hum` (a tune in their
head now: a sway, or a nod when relaxed), `add:C:\path\model.glb`
(as if the file were dropped on the window), `pick` (the "Add a character…" dialog), `tray:x:y` (open the tray
menu at that screen point), `chat:some_text` (say something to them), `chatbox` (open the slip), `settings`
(Settings; `settings:voice` at that page: `start`, `mind`, `voice`, `listening`, `characters`, `abilities`, `privacy`, `skills`, `addons`,
`reminders`, `checkins`, `phone`, `calendars`, `balance`, `cabinet`, `usage`), `settingsdo:Label` (presses the button or
flips the tick with that text on the page showing; `settingsdo:Label:value` sets the choice after that label, by number
from 0, or types in its field), `settingsscroll:600` (scrolls the page showing), `settingsup` (on a page below a page, a skill's,
presses its way back up, top left), `settingshover:Label` (rests a pretend pointer on that control, so its
hover tip shows; capture it with `-Desk`), `settingswidths` (logs any page wider
than its room, which would run off the window's right edge, then how many of the pages it measured), `trayitems` (logs the tray menu's entries),
`welcome` (the first run's Welcome again), `welcomedo:Label[:value]` and `meetdo:Label[:value]` (as `settingsdo`, on
the Welcome and on "Meet …"),
`memorybook` (`memorybook:agenda` at that tab: notes, agenda, facts, diary, memos, forget, or retired), `memorybookdo:Close`
(presses that button in it, `+_Add`, `Keep_yours` and `×` too: a × then says `Forget?`, or `Take_off?` on the agenda, the
second click to press), `memorybooktype:words:new_words` (types the new words into the field showing
with those words in it, a fact, an agenda item or a note, without leaving it), `memorybookleave` (leaves that field, as a click
elsewhere: it is saved, or a question shows), `memorychange:words:new_words` (their fact with those words, or else their agenda item, is reworded
under the open book, as upkeep might: a clash with a field typed in but not left), `memorycancel:words` (their agenda
item with those words is cancelled under the open book, as upkeep might), `ticketpress:0`
(the showing ticket's Snooze; 1: OK), `chathistory`, `chathistorydo:Reply` (clicks the newest reply shown that kept
something, opening or closing what they had in mind for it; `chathistorydo:Reply:words`, the newest with those words; `chathistorydo:Reply:2`, the second newest; `chathistorydo:Forget:words` and `chathistorydo:Edit:words:new_words` press Forget, or Edit, type and Save,
on the memory showing with those words in it; `chathistorydo:Top` scrolls it to the top, as the player might;
`chathistorydo:Below` presses "New lines below"; `chathistorydo:Close` closes it), `memoryseed` (a few notes, facts,
agenda items, retired facts and a note to try it on), `skillseed` (skills to try Settings → Skills and their skill
tool on: tea-timer, with a file and a script, one with no description and a folder with no `SKILL.md`, in the profile's
own skills folder, and journal and Daily Notes, a name the format doesn't allow, shown with a hint, in a vault's
skills folder in the profile; the mock mind reads a skill the player's words name, then answers), `skilllink` (links
that vault's skills folder on the open Skills page, as picking it in "Link a skills folder…" does),
`skillpage:tea-timer` (opens that skill's own page on the open Skills page, as its Settings › button does; `settingsup` goes back up), `notesseed` (a
stand-in notes folder, `notes` with a `today.md`, beside the test profile in `<profile>-files`, never the player's),
`skillgrant` (grants that folder to the skill whose page is open, as picking it in "Grant a folder…" does; the mock
mind, asked to "add a line to my note notes/today.md", reads it with `read_note` and appends "- A line from the mock
mind." with `append_to_note`, then answers), `notetail` (logs the last line of that `today.md`; `notetail:other.md`,
another note there), `noteplayer:a_line` (adds that line to that `today.md` as the player or another app would, so an
Undo after it is refused),
`remind:some_text` (a reminder goes off now),
`remindin:seconds:some_text` (sets one that far ahead; negative: one missed while closed),
`remindertry` ("Try it" on Settings → Reminders, once open there), `elsewhere:http://127.0.0.1:port` (ntfy and the webhook pointed at a local test
server), `checkin` (they ask about something past now), `presence` (logs do-not-disturb, call, idle),
`later:now` (their plan to reach the player comes due now), `whereabouts:away` (the player is away whatever the cabinet watches; `whereabouts:away:90`, away for 90 minutes already; `here`, `unsure`, `asleep`; `whereabouts:off` watches
again; a test run takes the player as here), `telegramat:http://127.0.0.1:port` (a stand-in for Telegram's
Bot API), `paircode:CODE` (the next pairing code, for it to send back),
`pastplan:minutes:some_text` (something on the agenda that ended that long ago),
`calendar:URL` (links a calendar named Test), `forget:chat|all|me` (its three forget
buttons), `upkeep` (note the conversation and keep its facts now), `moment:note`, `voicetest` (the voice's
sample line), `voice:some_text` (a bubble they also say aloud, if it is in their voice's language), `thinkingsounds:C:\path\folder` (their
thinking sounds saved as WAV files) and
`voicemodel:v2ProPlus` (speak as a clone with that base model, or `voicemodel:trained` with its own, as choosing it
in Settings does), `voicetrain` (train their voice, as the page's button does) and `voicetrainstop`, `hear:C:\path\said.wav`
(a recording they hear as if said holding the talk key), `talk:C:\path\said.wav` (the same, played out in time: they
listen and nod for its length first) and `dictate:C:\path\said.wav` (as if said into the slip's
microphone), and, opening the real microphone (except in a test copy, where they hear silence), `mic` (click the
slip's microphone), `listen` and `listened` (as if the mouse were held on them, then let go), and `closeup:hands` (the camera narrowed onto their hands, to judge fingers; `closeup:off`).

Hands-free, without ever opening the real microphone: `fakemic` (from then on they hear only recordings),
`mictalk:C:\path\said.wav` (said into their open microphone), `converse` (`converse:on`, `converse:off`). Their tools:
`allow`, `always` and `deny` (answer the card they ask with, as its buttons do; on a suggested change to a skill,
`allow` is Remember it and `deny` Not now), `seechange` (presses See the change on that card; `seechange:end` opens it and scrolls to the end of the lines; the mock mind suggests a
change to a skill when told "tea-timer from now on new_words instead of old_words", as with
`chat:tea-timer_from_now_on_rinse_the_pot_first_instead_of_warm_the_pot_first` after `skillseed`), and
`fakewindows:1._Two_Sum_-_LeetCode|notes.md` (the window titles they read instead; a test run reads stand-ins anyway). With `--offscreen` a page they open is
only logged, never opened. Memos: `memo:title:text` (they type one and send it, as the tool has them do),
`memoseed` (memos hanging, pinned and kept), `memoopen` (the one hanging; `memoopen:0` the one pinned first),
`memosheet:pin` (the latest sheet's tools: pin, off, edit, done, print, delete; `close` and `esc` as its × and Esc;
`memosheet:move:-200:100` drags it, in screen pixels),
`memowrite` (one of your own),
`memonote:title:text` (one of your own, printed as if saved: they think it over), `memohover` (a pretend pointer on
the hanging memo; `memohover:0` the top one on the side, `memohover:off`), `memocarry:a:b` (the hanging memo carried
by hand to that spot on the side; `memocarry` alone, off the cabinet), `memosheet:colour:mint`, `memosheet:sticker:cat`,
`memosheet:write:title:text` and `memosheet:tell` (a sheet's paper, sticker, fields and tick), `crt` (the screen's
system opens; `crt:memos` an app too), `crtclick:x:y` (a click on the screen, in its own pixels), `crtmove:0:1` (an app
moved, as holding and carrying it would), `crtaway` (a press away from the lifted screen), `crtclose` and
`memorybook:memos`. Focus and music: `focus` (a session started from the screen; `focus:skip`, `focus:pause`,
`focus:resume`, `focus:stop`), `focustool:start:50` (as their tool would), `standup` (they suggest standing up, as after a
long sitting), `musicfake:Title:Artist` (pretends a music app plays; `musicfake:off`), `musiclink:<link>` (as if pasted
into MUSIC, named by Spotify), `musictool:pause` and `musictool:start_playlist:Deep_Focus` (their tool), `musicprobe` (checks
Windows' media controls answer: how many sessions, whose; no titles), `spotifyprobe:Deep_Focus` (what Spotify's window
shows to press, and whether that playlist's play button is there; presses nothing).

Handing them things: `attach:C:\path\photo.jpg` (as if dropped on them), `pastepicture:C:\path\pic.png` (as if pasted; the
clipboard is left alone), `clip` (the slip's paper clip, its ways showing), `screenshot` (its "Your screen"),
`fakescreen:C:\path\pic.png` (every screenshot takes this picture instead; a test run never takes the real screen),
`phone` (its "From your phone…" card; in a test run the page listens on this PC alone) and `phonesend:C:\path\photo.jpg`
(a pretend phone opens the page, sends that photo and tries a wrong key).

Rest: `rest:winddown`, `rest:bedtime`, `rest:stillup`, `rest:break` (that word now), `rest:notice` (what the record of days
shows) and `rest:ask` (asking for a bedtime).

Activities: `done:the_gym` (as if DID IT were tapped on the DONE app), `proofroll:0` (proof always wanted; `proofroll:1`
never) and `crt:done`; `allow` and `deny` answer their card as Yes, count it and Not yet.

Balance: `focuspaid:30` (pays as a 30-minute focus part would), `bedtime:2330` (sets your bedtime, today's too),
`nextday` (as if the night had passed: the next check turns the day and half the unplayed time fades).

## Test runs

`--offscreen` makes a copy a test copy: its windows open off every monitor; with it,
`--shots=DIR` has the app picture its own windows when a file `<n>.want` appears there. A test copy runs beside the
player's own copies (their app, the demo): it never takes the app's one-copy lock, needs a profile of its own (`--data`,
or it quits), shows no tray icon or Windows notice (it logs what the notice would have said), never reads the talk key or
the mouse's buttons (the companion's eyes don't follow the player's pointer either), and never opens the microphone (what would open it hears silence, or a
recording: `talk:`, `hear:`, `fakemic` and `mictalk:`). Each says so in its log (`Main: a test run…`, `Tray: none in a
test run`, `Listening: a test run…`, `Ears: a test run never opens the microphone…`). A copy without `--offscreen` keeps
to one at a time: a second one quits at once (`Main: another copy is running; this one quits`). The test scripts write
Godot's own log into the run's profile (`--log-file`), so the player's log (in Godot's user folder) is never written
over. Godot's shader cache in that folder is still shared: it only ever holds the same compiled shaders.
`tools\dev\clip.ps1` records a clip instead, to judge motion: Godot's movie writer renders every frame off screen
(`-Script "moment:cheer@3" -Seconds 6`), and ffmpeg makes a GIF and an MP4 of the companion.

What a hidden run guarantees (`shoot.ps1 -Hidden`, every smoke scenario, `clip.ps1`): nothing of it reaches the
player's screen, taskbar or focus, from start to finish. No console, no window flashing up, no taskbar button, no
tray icon or Windows notice, and the window in front stays theirs. The app runs on a Windows desktop of its own
(`tools/dev/hidden.cs`), the only way to keep Godot from showing its window and asking for the focus before any of the
app's code runs. Windows draws nothing on a desktop no one looks at, so the app pictures its own windows there (`--shots`):
the screenshots are the same as before, except that a window's title bar isn't in its picture. Meanwhile the run watches
the player's desktop: a window of the run's on any monitor or on the taskbar, or in front, is said in a
`seen by the player:` line, which fails a smoke scenario. The app and everything it starts live in a job that ends
with the script: a run killed halfway leaves no unseen copy behind. A run without `-Hidden` shows the app as before,
and is no test copy: quit the player's copies first.

Every option of `shoot.ps1`, `clip.ps1` and `sheet.ps1` is in its header.

## Checks and the smoke suite

What a commit must pass before its pull request is marked ready, run the same way by the agents and by hand:

- `tools\dev\check.ps1`: the solution and the game built with no warnings, the unit tests and the smoke scenarios the
  branch's changes need, recorded against the commit in `<main checkout>\.qa\results\<sha>.checks.json` with which
  scenarios ran and why each other one didn't. `-All` runs every scenario, `-NoSmoke` none for a quick pass,
  `-Status` says what is recorded for HEAD, `-Verdict PASS|FAIL -Because "..."` records QA's verdict on it. The pm
  plugin's gate wants both before a pull request is marked ready.
- `tools\dev\smoke.ps1`: every scenario in `tools/dev/smoke/scenarios.json`, each a hidden test run through
  `shoot.ps1`, judged PASS, FAIL or BLOCKED from its screenshots and log. The report and shots go to
  `<main checkout>\.qa\smoke\<branch>`. One run at a time on this PC; the player's own copies of the app (their app,
  the demo) are never stopped and never block it: its test copies run beside them. The `beside` scenario shows it
  with stand-ins (hidden copies that aren't test runs, on throwaway profiles, started `--lock=beside`): one holds
  their one-copy lock, a second must quit at once, and the test copy runs and passes beside them. `--lock=NAME` gives
  a copy on a profile of its own the lock `InsertCoin.Cabinet.NAME` instead of the player's (`InsertCoin.Cabinet`,
  ignored without `--data` or on the real profile), so the player's own app can always start meanwhile; the
  scenario fails if a stand-in took the player's lock, and notes in the report whether it stayed free (or that the
  player's own app holds it, which is no failure). `smoke.ps1 -SelfCheck` (run by `pick-check.ps1`) checks that
  judgement on made-up copies. `-List` says what each scenario checks, `-Only boot,chat` runs some. In a worktree
  it borrows the main checkout's `GODOT` and models.
- Which scenarios a check runs (`smoke.ps1 -Changed`, from `git diff origin/main...HEAD`): every one when shared code
  or the harness changed (the file's `all`: `Main.cs`, `Stool.cs`, `project.godot`, Core, `smoke.ps1`, `shoot.ps1`,
  `mock_mind.py`, the scenarios…), each one whose `covers` match a changed file, and `boot` whenever anything the app
  runs changed (a character's `persona.md` too; READMEs never count); none when only tests or docs did; every one
  when git can't say what changed, or nothing did. The others are reported as SKIPPED, with the reason, and
  `check.ps1` prints each scenario's result and reason.
  `-Changed -Explain` says what a change would run without running it (`-Files a,b` for a list of your own), and
  `tools\dev\smoke\pick-check.ps1`, which `check.ps1` runs first, checks the rule on a few lists. Main's baseline
  (`smoke.ps1` with no options) runs everything.
- `tools\dev\mock_mind.py <port>`: an OpenAI-compatible stand-in for the companion's mind with a fixed reply, logging
  each request. A profile points at it with `"ChatProtocol": "OpenAi", "ChatBaseUrl": "http://127.0.0.1:<port>/v1",
  "ChatModel": "mock"`, and needs no key.
- A scenario has a name, what it checks, a script of dev actions and the shots to take, and optionally settings, a
  window to capture, the desk, the mock mind, and log patterns it expects, forbids or ignores (the file's own
  `about` says how each works). Calibrate `expect` and `ignore` from real runs; never loosen `forbid` to make a run
  pass.
