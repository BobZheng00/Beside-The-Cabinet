# INSERT COIN

A desktop arcade cabinet for a balance of work, life and games: solved LeetCode problems and focus sessions pay out
gaming time, and Steam games use it up. A day can only earn so much, unplayed time fades overnight, and play after
your bedtime costs more. Soft enforcement only: warnings, sounds and IOU tickets — it never closes a game.

Design: [docs/specs/2026-09-30-insert-coin-design.md](docs/specs/2026-09-30-insert-coin-design.md), and for the
balance: [docs/specs/2026-10-07-balance.md](docs/specs/2026-10-07-balance.md)

## Run

| | |
|---|---|
| `builds\InsertCoin\InsertCoin.exe` | The standalone app (after `build.cmd`). Real profile in `%APPDATA%\InsertCoin`, real Steam detection. No dev keys. |
| `run.cmd` | Same as the app, but run from source through the Godot editor binary (debug build, dev keys on). |
| `demo.cmd` | Demo profile (`%APPDATA%\InsertCoin-demo`), Steam ignored. Safe to play with. |

Drag the cabinet by its body. The big gold button asks LeetCode right away. The tray icon has the menu: the
cabinet, Settings…, the memory book and chat history, a memo of your own, a LeetCode check, and the switches flipped
often (Mute sounds, Voice, Conversation). Settings is one window with a page for each thing, listed down its left
side (her mind, voice, talking aloud, characters, abilities, privacy, add-ons; reminders, check-ins, your phone,
calendars, balance; the cabinet, usage), each saved as you change it; only her provider waits for its Save. At
first run the cabinet asks for your initials, then your LeetCode name, which is optional: Enter with none goes on
without it (Settings → Balance → Change… adds one later).

**Balance.** What earns play: LeetCode problems (Easy 1 coin, Medium 2, Hard 4; a coin is 15 minutes), and focus sessions, which pay by the minute as each part ends, however short: 15
minutes of play for every hour focused (half an hour adds 7:30 at once; the fuel bar fills, and a coin drops when it
completes one). A day (04:00 to 04:00) can earn at most 1:30 from Monday to Thursday, 2:00 on Friday and 2:30 at the
weekend; past that, work still counts in the day's record but pays nothing (paying back time owed doesn't count
towards it). Each night half of the unplayed time fades, so it can't be saved up for the weekend (time owed never
fades). After the bedtime you set, until 04:00, play costs ×1.5 (on credit, ×2.25): the screen says LATE NIGHT and a
ticket tells you when it starts. Settings → Balance sets the caps and your bedtime; changes count from the next day.
The screen's attract pages show today's earnings against the cap. A day with a solve, an activity or 25 minutes of
focus is a good day; from the third good day in a row, the first thing each day earns brings a bonus star coin
(STREAK xN BONUS). Every day is kept in a record (what was earned and
from what, focused, played, when you were first and last at the PC), the start of her sense of your patterns.

The companion on the stool follows the mouse pointer with her eyes and head, waves when clicked, and reacts to
what the cabinet does: claps for a Hard solve, a small fist pump for the others, glances at the screen, worries
about debt, dozes while the session is paused. In a conversation she looks away to think and back to answer, nods
in your pauses as you talk to her, and her head follows what her voice stresses. She says a line about it in a speech bubble; click her and she waves, and the slip to type to her opens. Settings → Characters leaves
the stool empty, silences her bubbles, picks another character or adds one; a model file dropped on the cabinet adds it too.

She can also talk. In Settings → Her mind pick a provider (OpenAI, Anthropic, OpenRouter, DeepSeek, Qwen, a local
Ollama or LM Studio, or any OpenAI-compatible server), paste your own API key, name a model, Test and Save. Then clicking
her opens a slip at the foot of the stool: type, press Enter, and her answer streams into the bubble while she acts
it out. She knows the cabinet's numbers and remembers:

- the conversation itself, one continuous thread per character. The newest part (Settings → Her mind, "Word for word":
  32k tokens by default, 8k with a server on this PC) is sent word for word, only ever growing at its end, so
  providers serve it from their cache at a tenth of the price (Claude is asked to cache it). Each turn tells her
  the time and how long since you last talked;
- her notes on your talks, a log by day (docs/specs/2026-10-11-observational-memory.md): once the conversation has
  been quiet for ten minutes (two while a game runs), upkeep notes what was said, one short note per thing worth
  keeping (what you did, plan, like or worry about, names, numbers and dates written out, what she promised), and
  small talk none. When older talk leaves the word-for-word part, her notes on it take its place at the start of her
  prompt: the newest that fit three eighths of the window (12k tokens at 32k), "!" before what matters most. As they
  age, a finished week of notes is condensed into notes for the week, weeks into months and months into years, so
  they never outgrow their room; the originals stay, recalled when they come up;
- facts about you (shared by every character) and about the two of you, kept from her notes by upkeep, checked
  against the facts that relate to them. When you just tell her something worth keeping she says she'll remember it
  and jots it in a little notebook at once. A fact that changes keeps its history: the old one is retired as
  changed, and "where did I live before?" can still find it, marked until when;
- two short notes she always has in mind, rewritten by upkeep when something important changes;
- your agenda: plans, exams and appointments with their dates, which she writes down when you mention them (or
  upkeep does later), and reminders you ask her for ("remind me at 9 to call Mum", "an hour before the interview",
  "vitamins every day at 8": a reminder can be some time ahead of its item and can repeat daily, on weekdays, weekly
  or monthly). What is coming up in the next week, and what has just passed (so she can ask how it went), is in
  every turn; two days after, a one-off item leaves the agenda and a repeating one moves on to its next time.
  A reminder goes off on a ticket at the screen corner with Snooze and OK, and she tells you in her own words and
  your language once she has finished what she is saying (never cutting herself or you off). Settings →
  Reminders sets how long the ticket stays and a snooze lasts, a reminder before every timed item on the agenda, a
  chime (even when muted) and a Windows notification ("Try it" sends one everywhere a reminder goes). While you're
  away from the PC, it reaches your phone instead, as a text in her words (below). One that came
  due while the cabinet was closed goes off when it next starts, saying so. Settings → Calendars links your Google
  or Outlook calendar by its private address (read every 15 minutes, read-only, the address stored encrypted): its
  next two weeks join the agenda;
- she asks how things went: after something you told her about is over, once you are back at the PC and free (not
  in a call, which she tells from another app using the microphone or camera, not in a full-screen game or
  presentation, not outside 08:00–22:00), or by text while you're still out; once, and only if you have not talked
  since. Gentle (at most one every three hours), Chatty or Off in Settings → Check-ins. Not for each calendar
  meeting, nor for plain reminders;
- and before each reply, whatever of all that relates to what you said, including a line or two from any earlier
  conversation, however old (the whole conversation is kept and searched): by meaning, with a small model that runs
  on this PC (so "nervous about tomorrow" finds your interview, in English or Chinese), and by shared words.
  Settings → Her mind, "Memory search", picks the multilingual model (about 100 languages, about 200 MB of memory while
  chatting), the small one (Chinese and English, about 50 MB) or shared words only;
- what has not come up in a long time fades (a forgetting curve: importance and every recall make a fact last
  longer; pinned and the most important ones never fade). About weekly,
  while quiet, she reviews her facts: what says the same thing is merged, and what is out of date (a plan already
  past, something since changed, trivia that never came up again) is retired. Retired is not deleted: the memory
  book lists it under "Retired by her review", to bring back, and it comes back by itself if it comes up again.

**Away from the PC.** She reaches you as a roommate would ([docs/specs/2026-10-10-reaching-the-player.md](docs/specs/2026-10-10-reaching-the-player.md)).
At the PC she tells you herself. You're away after five minutes without the mouse or keys (Settings → Your phone,
"Away after", 2 to 30), at once with the screen locked, or when you tell her you're heading out or going to bed;
asleep if that's in the quiet hours. Away, what matters reaches your phone as a text in her words, through
[ntfy](https://ntfy.sh), a Discord webhook, a Telegram bot or any webhook (Settings → Your phone; addresses and tokens
stored encrypted, "Send a test" to check): reminders, a focus session's turns, and asking how something went (a
reminder never waits more than 30 seconds for her words: then the plain line goes). Asleep, only reminders. What only
makes sense at the PC (bedtime words, a break from a long game) waits for you while it still matters; a suggestion
to stand up is dropped. In between, when she isn't sure, she calls you first, and texts if you turn out to have gone. "Also while
I'm at the PC" sends reminders to your phone at the PC too, as a plain line, as they used to. "She reaches out" sets how
much reaches your phone: only what you set (reminders, focus turns), like a roommate (that, and asking how things
went: the default), or keeps in touch, when she also texts you things of her own while you're out, when she plans to:
she can plan one in her goodbye when you tell her you're heading out, or gets a first chance half an hour after you
leave without a word, and each time she may plan the next. Never at night, and only if she has something to say;
texts of hers you haven't answered don't stop her. If you're back before one she planned, she brings it up in person.

**Texting with her.** On Telegram you can text her back, from anywhere (Settings → Your phone, "Text with her on
Telegram": make a bot with @BotFather, paste its token, scan the code and press Start; about two minutes). She
answers by text, knowing you're out; her cards come as buttons, a memo as a message (it prints too), and photos and
files you send reach her. What needs you at the PC (your screen, opening things) waits until you're back. What you
texted while the cabinet was closed (Telegram keeps it a day) she reads when it next runs, and answers once. Only
your chat is listened to. Her chat goes through Telegram's servers and isn't end-to-end encrypted. Each profile
needs a bot of its own, the demo too.

A SillyTavern-style character card (`.png` or `.json`, V1 to V3) dropped on the cabinet or picked with "Add a
character…" gives whoever is on the stool its personality, example dialogue and lorebook (background that comes
up when its keywords are mentioned); she greets you as the card says.

She can speak aloud too, with a voice from GPT-SoVITS running on this PC (its official Windows package; a laptop
graphics card starts each sentence in about a second). Settings → Her voice: point it at the GPT-SoVITS folder,
then give her a voice: drop on the cabinet (or add on that page) a voice's folder or `.zip`, which replaces her
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
like a subtitled anime (the model writes `[say: …]` before each sentence, told the two must say the same and reminded
at the end of each turn; a Japanese voice gets names in katakana, since it reads Latin letters as English, and calls
you by name with さん unless her character or you say otherwise). A sentence left without a `[say]` is silent, and
the log counts them ("went unheard"). While a game runs she rests her voice:
the server is stopped so the game has the graphics card, and started again after. The tray's Voice (or "She speaks aloud" in Settings) switches it off;
"Mute sounds" mutes her too. Clicking her stops her mid-sentence, and so does talking over her with the talk key
or asking something new. She then remembers only what you heard, ending where she stopped, plus what she hadn't said
yet, so she can come back to it. Without her voice, what you heard means the pages of her bubble you had seen.

What she brings up herself with nothing else to show it (a check-in, a word on rest or bedtime, standing up) comes
after a word first. Her mind writes the whole of it while her call is asked for on its own: a few words of
hers that only get your attention ("ボブさん？"; her last one if that fails). She says the call, and her bubble keeps it
with two answers to click, Yes? and Not now, while she looks at you and waits.
Yes? (or something short, "yes?", 嗯？, said with the talk key or hands-free if you listen after her questions, or a
click on her or her bubble): she goes on. Not now: she stops there and keeps the rest for later. Anything longer, or
a message typed: she stops there and answers it. Unanswered for 20 seconds (your headset on the desk, say), a ticket
above every window shows the whole of it, with Listen and OK, unless it can pass unnoticed (standing up); after five
minutes the rest is only shown. She goes straight on without her voice, in a conversation, while you are writing to
her, or when you talked with her in the last two minutes. A reminder and a focus session's turn always go straight on,
each with a ticket at the corner as it comes, and so does her answer when you've just pressed something on the cabinet
(START, STOP, SKIP, DID IT). And when her voice speaks another language
than her bubble, each page waits for you to read it (a Chinese or Japanese character counted as three letters), the
last one after her voice has finished (`docs/specs/2026-10-09-a-word-first.md`).

You can talk to her aloud as well. Hold the mouse on her, or hold the talk key in any app (Right Ctrl unless Chat
settings says otherwise), say it, and let go: she leans in to listen, nodding in your pauses, and answers what she
heard as if you had typed it. The microphone at the end of the slip writes what you say into the slip instead, to change before you send it.
She hears on this PC, on the processor, with SenseVoice-Small (Chinese, English, Japanese, Korean, Cantonese, told
apart by itself): about a tenth of a second for a sentence. The microphone is open only while she listens, and
nothing is recorded or kept. The talk key's state is read each frame (no keyboard hook), so the key still does what
it does in the app in front.

When she's slow to find her words (a model that thinks first, a tool running) and you're waiting, she thinks aloud in
her own voice now and then ("um…", "let's see…", "えっと…": made in her voice when it loads, varied, at most twice a
reply, never on a quick one). When her card asks you something, you can answer it aloud: a short "yes", "always" or
"no" (in Chinese, Japanese or Korean too) does what its buttons do; anything else is a new message. GPT-SoVITS's
habit of starting a line with up to a second of silence is cut, so her first word comes sooner.

She can also listen without the key, if you choose (Settings → Talking aloud). With follow-up listening she listens a few
seconds after she speaks (3 to 20, after her answers, her own questions or anything she says, as ticked), and if you
start talking she hears it as your answer. A conversation (the tray's Conversation switch, or a double tap of the talk
key) has her listen after everything she says, until you've been quiet for 30 seconds. She notices you start and finish
by herself (Silero's voice detector and Pipecat's Smart Turn, on the processor). She never listens on her own in a call,
in full screen or quiet time, or while a game runs, unless you allow it, and never while she is speaking. The CRT shows
MIC whenever the microphone is open.

She can do things too, with tools (Settings → Abilities): open a web page in your browser or an app from your
Start menu, read a page, search the web, look up a LeetCode problem, and the rest below. She
says what she's about to do in her own words, and anything acting outside the app asks first: a card by her stool
shows what and where (the real address, whatever she said about it), with Allow once, Always (for that site or app) and No.
Each tool can be Allowed, Ask me first, or Off; the sites you said Always to are listed there to forget. Auto lets her
go ahead without asking (what's switched off stays off), except for what's on the Privacy tab, and for the rest of a
reply once she has read a page or search results: a page could try to talk her into things. A model that
can't take tools (some that run on this PC) answers as before. Design:
[docs/specs/2026-10-05-tools-and-abilities.md](docs/specs/2026-10-05-tools-and-abilities.md).

**Help with learning.** Ask her for help with what you're on ("help me with this") and she reads your window titles to
see what it is (a browser tab's title, an editor's file), and when she needs to see it (your code, an error, an
exercise) she asks to take a screenshot of the window you were working in (the card names it; "your whole screen" if you
say so). Then she helps a step at a time, each only when you ask for more: a
nudge, the approach, an outline, and the whole answer only if you ask again after that; long steps come as a memo. For a
LeetCode problem she looks up its statement, topics and LeetCode's own hints (from leetcode.com; a leetcode.cn tab is
found by its number). She can read a web page you name, and search the web: with your provider's own search where it
has one this app can use (Anthropic's), or through your own SearXNG (Add-ons). She opens apps from your Start menu after
asking (games stay the cabinet's).

**Privacy and add-ons.** Settings → Privacy gathers what she can see of yours: your window titles (allowed unless
you change it) and your screen (she asks first), which Auto never skips; and what she is told in the background, each
with a tick: what's playing in your music app, and what the record of days shows. What she sees goes to your AI
provider with the conversation; pages she reads are fetched by this PC. Settings → Add-ons holds extras that stay
off until you turn them on. The first is web search through SearXNG, a free, open-source search engine you run
yourself (with Docker: `docker run -d -p 8888:8080 searxng/searxng`, then add `json` under `search: formats:` in its
settings.yml); its Test button says whether it answers, and what to change if not.

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
nothing open, play starts Spotify for you). Each focus part pays into the cabinet as it ends (see Balance). Save playlists under MY PLAYLISTS (Spotify: Share, Copy link, then ADD):
START plays one from its beginning (the app presses Spotify's own play button for it), and the starred one starts your
focus sessions; it pauses at breaks and carries on after. She can control it too, after asking ("put on Deep Focus"). And after 50 minutes at the PC without a break, she suggests standing up (Settings →
Check-ins to change it).

**Rest and sleep.** With a bedtime (Settings → Balance, or tell her one: she sets it after you confirm on her card, from the
next day), she gives you a word to wind down half an hour before it, says it's time to sleep at it, and once more half
an hour after, then leaves it; in a game, tickets. After two hours in one game she suggests a break. Without a bedtime
she asks for one, once, some evening (and once more a week on, with your usual time from the record of days). At most
once a day she mentions what the record shows: nights going on late, or days without play while time is banked. When your
nights usually end well past your bedtime, she suggests a step towards it (in bed half an hour earlier than usual for a
few nights) rather than the whole way at once; your bedtime stays as you set it. And when she asks how something on your
agenda went that is one of your activities ("gym at 6"), saying you went counts it.

**Activities.** Tell her about something good you did ("I went to the gym", "I cooked dinner") and she counts it towards
your play time: it pays like a problem of its size (small 1 coin, medium 2, big 4), once a day. The first time, she sizes
it; you confirm on a card by her stool ("Yes, count it" or "Not yet": "I'm going" isn't "I went"), and from then on it
pays the same. Neither its worth nor how much proof it needs is yours to set. Now and then the cabinet wants proof
(rarely for small things, usually for big ones, always when it doesn't add up, never for what can't be photographed;
less often once your proofs have convinced her): show her a photo or a screenshot and she judges it (sure pays in
full, unsure half, doubtful nothing yet; a better picture that day still pays; a photo taken on another day counts half
at most). The screen's DONE app lists what counts, with DID IT to log one yourself and × to take one off.

**Handing her things.** Drop a picture, a PDF or a text file (notes, code, a log) on her, paste one into the slip
(a screenshot from Win+Shift+S, a picture copied in the browser, files copied in Explorer), or use the slip's paper clip:
"Picture or file…", "Your screen" (the screen the pointer is on, as it is now) or "From your phone…" (a card with a code:
scan it on the same Wi-Fi and choose photos; they land on the slip; the page closes with the card or after ten
minutes, and the first time Windows may ask to let the cabinet take connections: allow it on private networks). Each
waits as a chip above the slip and goes with what you say next, typed or said. Ask her to look at your screen and she
does, after asking (a card by her stool). Pictures are made no larger than models read in full (phone photos turned
upright, with the time they were taken); she keeps what she saw in words, not the picture. With a model that can't
take pictures or PDFs she says so, and none is sent to it again.

Settings → Usage shows what her requests used, as your provider reports it, this month and day by day:
tokens read, the share served from the provider's cache, cache writes and output, her replies apart from her
upkeep; with the model's prices entered (from your provider's price page), the cost too.

Tray → "Chat history…" shows the conversation itself, every line kept, by day (with the notes she works from above
it). Tray → "Memory book…" shows all of it, a tab each (notes, agenda, facts, moments: her log, forget): edit, pin
(always in mind, upkeep leaves it alone) or delete anything.
"Clear the chat" starts the conversation afresh and keeps what she remembers; "Forget all with …" clears the
conversation and everything about the two of you; "Forget all about me" clears what every character knows about
you. It stays in the profile folder (`companion\memory.db`); the key is encrypted for your Windows
account. Without a provider she keeps to her canned lines.

The window draws at 60 frames a second, and at 15 while a game runs or with "Energy saver" ticked (moments
worth watching still get a few seconds at full pace).

Dev keys (debug builds, click the cabinet first): `F1/F2/F3` solve Easy/Medium/Hard · `F4` she asks for your bedtime
now (a word first; unanswered, its ticket) · `F5` fake game on/off · `F6` clock ×60 · `F7` AFK on/off · `F8` she
suggests standing up now (a word first, no ticket) · `F9` wipe state · `Esc` quit.
Arguments after `--`: `--data=DIR`, `--no-steam`, `--script=hard@2,game@5,warp@5` (dev actions at given seconds).
Companion dev actions: `moment:cheer` (or `pump`, `greet`, `glance`, `alarm`, `worry`, `doze`, `wake`),
`point:x:y` (a pretend mouse pointer at that canvas point), `character:id` (swap who sits on the stool),
`say:some_text` (a speech bubble), `quiet` (toggle speech bubbles), `click` (as if she were clicked), `bubbleclick`
(a click on her bubble's words while it waits on her call), `bubblechip:yes` or `bubblechip:notnow` (that answer on her
bubble clicked), `nocall` (another app's microphone doesn't count as a call), `saver`
(toggle the energy saver), `legs:left|right|both|ring` (which feet hang off the stool's ring), `add:C:\path\model.glb`
(as if the file were dropped on the window), `pick` (the "Add a character…" dialog), `tray:x:y` (open the tray
menu at that screen point), `chat:some_text` (say something to her), `chatbox` (open the slip), `settings`
(Settings; `settings:voice` at that page: `mind`, `voice`, `listening`, `characters`, `abilities`, `privacy`, `addons`,
`reminders`, `checkins`, `phone`, `calendars`, `balance`, `cabinet`, `usage`), `settingsdo:Label` (presses the button or
flips the tick with that text on the page showing; `settingsdo:Label:value` sets the choice after that label, by number
from 0, or types in its field), `settingsscroll:600` (scrolls the page showing), `settingshover:Label` (rests a pretend pointer on that control, so its
hover tip shows; capture it with `-Desk`), `settingswidths` (logs any page wider
than its room, which would run off the window's right edge),
`memorybook` (`memorybook:agenda` at that tab: notes, agenda, facts, moments, forget, or retired), `ticketpress:0`
(the showing ticket's Snooze; 1: OK), `chathistory`, `memoryseed` (a few notes, facts,
agenda items, retired facts and a note to try it on), `remind:some_text` (a reminder goes off now),
`remindin:seconds:some_text` (sets one that far ahead; negative: one missed while closed),
`remindertry` ("Try it" on Settings → Reminders, once open there), `elsewhere:http://127.0.0.1:port` (ntfy and the webhook pointed at a local test
server), `checkin` (she asks about something past now), `presence` (logs do-not-disturb, call, idle),
`later:now` (her plan to reach the player comes due now), `whereabouts:away` (the player is away whatever the cabinet watches; `whereabouts:away:90`, away for 90 minutes already; `here`, `unsure`, `asleep`; `whereabouts:off` watches
again; a test run takes the player as here), `telegramat:http://127.0.0.1:port` (a stand-in Telegram, such as
`fake_telegram.py`), `paircode:CODE` (the next pairing code, for it to send back),
`pastplan:minutes:some_text` (something on the agenda that ended that long ago),
`calendar:URL` (links a calendar named Test), `forget:chat|all|me` (its three forget
buttons), `upkeep` (note the conversation and keep its facts now), `moment:note`, `voicetest` (her voice's
sample line), `voice:some_text` (a bubble she also says aloud, if it is in her voice's language), `thinkingsounds:C:\path\folder` (her
thinking sounds saved as WAV files) and
`voicemodel:v2ProPlus` (speak as a clone with that base model, or `voicemodel:trained` with its own, as choosing it
in Settings does), `voicetrain` (train her voice, as the page's button does) and `voicetrainstop`, `hear:C:\path\said.wav`
(a recording she hears as if said holding the talk key), `talk:C:\path\said.wav` (the same, played out in time: she
listens and nods for its length first) and `dictate:C:\path\said.wav` (as if said into the slip's
microphone), and, opening the real microphone, `mic` (click the slip's microphone), `listen` and `listened` (as if
held on her, then let go), and `closeup:hands` (her camera narrowed onto her hands, to judge fingers; `closeup:off`).
Hands-free, without ever opening the real microphone: `fakemic` (from then on she hears only recordings),
`mictalk:C:\path\said.wav` (said into her open microphone), `converse` (`converse:on`, `converse:off`). Her tools:
`allow`, `always` and `deny` (answer the card she asks with, as its buttons do), and
`fakewindows:1._Two_Sum_-_LeetCode|notes.md` (the window titles she reads instead; a test run reads stand-ins anyway). With `--offscreen` a page she opens is
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
into MUSIC, named by Spotify), `musictool:pause` and `musictool:start_playlist:Deep_Focus` (her tool), `musicprobe` (checks
Windows' media controls answer: how many sessions, whose; no titles), `spotifyprobe:Deep_Focus` (what Spotify's window
shows to press, and whether that playlist's play button is there; presses nothing).
Handing her things: `attach:C:\path\photo.jpg` (as if dropped on her), `pastepicture:C:\path\pic.png` (as if pasted; the
clipboard is left alone), `clip` (the slip's paper clip, its ways showing), `screenshot` (its "Your screen"),
`fakescreen:C:\path\pic.png` (every screenshot takes this picture instead; a test run never takes the real screen),
`phone` (its "From your phone…" card; in a test run the page listens on this PC alone) and `phonesend:C:\path\photo.jpg`
(a pretend phone opens the page, sends that photo and tries a wrong key).
Rest: `rest:winddown`, `rest:bedtime`, `rest:stillup`, `rest:break` (that word now), `rest:notice` (what the record of days
shows) and `rest:ask` (asking for a bedtime).
Activities: `done:the_gym` (as if DID IT were tapped on the DONE app), `proofroll:0` (proof always wanted; `proofroll:1`
never) and `crt:done`; `allow` and `deny` answer her card as Yes, count it and Not yet.
Balance: `focuspaid:30` (pays as a 30-minute focus part would), `bedtime:2330` (sets your bedtime, today's too),
`nextday` (as if the night had passed: the next check turns the day and half the unplayed time fades).
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
tools/bench               memory.cs — her memory on LongMemEval and LoCoMo, and staying in character (docs/research/2026-10-11-memory-benchmark.md):
                          `dotnet run tools/bench/memory.cs -- --data <json> --out <folder> [--set locomo|persona]`;
                          it runs on a profile's own provider and key (the demo's), billed to its owner
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

- **As a user, no tools needed:** Settings → Characters → "Add a character…" and pick a `.vrm`, `.glb`, `.gltf` or a `.zip`
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
