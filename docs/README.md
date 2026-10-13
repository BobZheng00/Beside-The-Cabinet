# Everything it does

A guide to every feature and setting, for players and the curious. The code still goes by its working name,
INSERT COIN (the cabinet's marquee keeps it too), and the guide calls the companion "she" for short: any
character fits. Building, running and testing it: [src/README.md](../src/README.md) and
[tools/dev/README.md](../tools/dev/README.md).

## The cabinet, Settings and the first run

Drag the cabinet by its body. The big gold button asks LeetCode right away. The tray icon has the menu: the
cabinet, Settings…, Getting started… (until it is done), the memory book and chat history, a memo of your own, a
LeetCode check, and the switches flipped often (Mute sounds, Voice, Conversation). Settings is one window with a page
for each thing, listed down its left side: Getting started; under her name, Mind, Voice, Talking aloud, Characters,
Abilities, Privacy and Add-ons; under Your day, Reminders, Check-ins, Your phone, Calendars and Balance; under The
cabinet, Cabinet and Usage. Each is saved as you change it; only her provider waits for its Save. At first run a Welcome asks three things: your name (what she
calls you; your initials on the cabinet come from it), who keeps you company (seated on the stool as you pick), and
her mind (provider, key, model, Test and Save; Skip for now leaves her unable to talk). Then she says hello. The rest
is in Getting started, first in Settings and in the tray: her voice, talking aloud, your phone, a calendar and
LeetCode (Settings → Balance → Change… asks for the name on the cabinet's screen), each saying what it gives and
takes, with Set up… (its page) and Later, and what balance starts with. It stays until everything is done or put
off, or you hide it. A character added later gets a "Meet …" sheet: who they are (a card, or none yet), their voice,
and the name every character calls you.

The window draws at 60 frames a second, and at 15 while a game runs or with "Energy saver" ticked (moments
worth watching still get a few seconds at full pace).

## Balance

What earns play: LeetCode problems (Easy 1 coin, Medium 2, Hard 4; a coin is 15 minutes), and focus sessions, which pay by the minute as each part ends, however short: 15
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

## The companion on the stool

The companion on the stool follows the mouse pointer with her eyes and head, waves when clicked, and reacts to
what the cabinet does: claps for a Hard solve, a small fist pump for the others, glances at the screen, worries
about debt, dozes while the session is paused. In a conversation she looks away to think and back to answer, nods
in your pauses as you talk to her, and her head follows what her voice stresses. She says a line about it in a speech bubble; click her and she waves, and the slip to type to her opens. Settings → Characters leaves
the stool empty, silences her bubbles, picks another character or adds one; a model file dropped on the cabinet adds it too.
It also sets how each character sits, whoever they are: neat (knees together, hands in the lap, head tilts) or relaxed
(knees a little apart, often a foot down off the ring, a hand on one thigh and the other by the hip, fewer tilts, the
legs still, a nod to a tune instead of a sway); they move into it as it is chosen, and "Show me"
plays a few seconds of it. Fumiriya starts relaxed, the others neat; the first run and "Meet …" ask it too.

## Talking with her, and what she remembers

She can also talk. In Settings → Mind pick a provider (OpenAI, Anthropic, OpenRouter, DeepSeek, Qwen, a local
Ollama or LM Studio, or any OpenAI-compatible server), paste your own API key ("Get a key from …" opens the
provider's page for one), name a model, Test and Save. Then clicking
her opens a slip at the foot of the stool: type, press Enter, and her answer streams into the bubble while she acts
it out. She knows the cabinet's numbers and remembers:

- the conversation itself, one continuous thread per character. The newest part (Settings → Mind, "Word for word":
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
  against the facts that relate to them. She answers as anyone would and keeps them quietly, at her next upkeep. A
  fact that changes keeps its history: the old one is retired as changed, and "where did I live before?" can still
  find it, marked until when;
- the cabinet's record of each day in her diary, written by code from the cabinet's own numbers so it is always right:
  what you solved and played by name, your focus, activities, bedtime and when you were at the PC. What a day earned
  is not in it;
- of the cabinet, only what matters at the moment: your play time, a game on, time owed, the day's cap reached, past
  your bedtime. The rest (today's and the week's numbers, the streak, how play is earned, a day's record with what it
  earned) she looks up with her cabinet tool when you ask. The cabinet's bookkeeping (a token going in, the
  overnight fade, the cap, an IOU) shows on its own screen, not in her bubble: a new day's fade counts the time down
  on the CRT;
- what she has told you about herself (her past, what she likes), kept as hers and always in mind, so her stories stay
  the same; the memory book marks those facts "me";
- what is going on in your life with no date (a worry, a decision, something you're waiting for): a couple of days
  after it comes up she asks how it's going, once, at the PC or by text while you're out; the memory book's Agenda
  lists them, and Settled closes one;
- what she always has in mind about you and about the two of you: what you write on the memory book's Notes, word for
  word, then her most important facts (pinned first, then by importance), listed afresh for every reply, so a change
  is right in her very next one; for two weeks a fact that changed says what it was, and one that ended says so;
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
  Settings → Mind, "Memory search", picks the multilingual model (about 100 languages, about 200 MB of memory while
  chatting), the small one (Chinese and English, about 50 MB) or shared words only;
- what has not come up in a long time fades (a forgetting curve: importance and every recall make a fact last
  longer; pinned and the most important ones never fade). About weekly,
  while quiet, she reviews her facts: what says the same thing is merged, and what is out of date (a plan already
  past, something since changed, trivia that never came up again) is retired. Retired is not deleted: the memory
  book lists it under "Retired by the review", to bring back, and it comes back by itself if it comes up again.

## Away from the PC

She reaches you as a roommate would ([docs/specs/2026-10-10-reaching-the-player.md](specs/2026-10-10-reaching-the-player.md)).
At the PC she tells you herself. You're away after five minutes without the mouse or keys (Settings → Your phone,
"Away after", 2 to 30), at once with the screen locked, or when you tell her you're heading out or going to bed;
asleep if that's in the quiet hours. Away, what matters reaches your phone as a text in her words, through
[ntfy](https://ntfy.sh), a Discord webhook, a Telegram bot or any webhook (Settings → Your phone; addresses and tokens
stored encrypted, "Send a test" to check): reminders, a focus session's turns, and asking how something went (a
reminder never waits more than 30 seconds for her words: then the plain line goes). Asleep, only reminders. What only
makes sense at the PC (bedtime words, a break from a long game) waits for you while it still matters; a suggestion
to stand up is dropped. In between, when she isn't sure, she calls you first, and texts if you turn out to have gone. "Also while
I'm at the PC" sends reminders to your phone at the PC too, as a plain line, as they used to. "… reaches out" sets how
much reaches your phone: only what you set (reminders, focus turns), like a roommate (that, and asking how things
went: the default), or keeps in touch, when she also texts you things of her own while you're out, when she plans to:
she can plan one in her goodbye when you tell her you're heading out, or gets a first chance half an hour after you
leave without a word, and each time she may plan the next. Never at night, and only if she has something to say;
texts of hers you haven't answered don't stop her. If you're back before one she planned, she brings it up in person.

## Texting with her

On Telegram you can text her back, from anywhere (Settings → Your phone, "Text with … on
Telegram": make a bot with @BotFather, paste its token, scan the code and press Start; about two minutes). She
answers by text, knowing you're out; her cards come as buttons, a memo as a message (it prints too), and photos and
files you send reach her. What needs you at the PC (your screen, opening things) waits until you're back. What you
texted while the cabinet was closed (Telegram keeps it a day) she reads when it next runs, and answers once. Only
your chat is listened to. Her chat goes through Telegram's servers and isn't end-to-end encrypted. Each profile
needs a bot of its own, the demo too.

## Character cards

A SillyTavern-style character card (`.png` or `.json`, V1 to V3) dropped on the cabinet or picked with "Add a
character…" gives whoever is on the stool its personality, example dialogue and lorebook (background that comes
up when its keywords are mentioned); she greets you as the card says.

## Her voice

She can speak aloud too, with a voice from GPT-SoVITS running on this PC (its official Windows package; a laptop
graphics card starts each sentence in about a second). Settings → Voice: point it at the GPT-SoVITS folder, or
use the one it finds on the PC ("Found on this PC"), or press "Install it for me": it downloads the package her voice
is tested with (v2pro of June 2025, 8.2 GB; the build for RTX 50 cards on those) from Hugging Face, or HF-Mirror for
mainland China, checks it against its published SHA-256, and unpacks it (about 13 GB) in a folder of its own inside
the one chosen, then uses it. It carries on with Settings closed, and a download stopped, or cut short by closing the
cabinet, carries on from where it got to. Then give her a voice: drop on the cabinet (or add on that page) a voice's folder or `.zip`, which replaces her
voice, or clips, which join it. A voice is clips of one person, 3 to 10 seconds each, with the words said in them, and maybe a model someone
trained on them (a `.ckpt` and a `.pth`; v2 to v4 all work). The words come from a training list (any `.list`,
one line per clip: `clips/001.wav|hana|JA|今日も頑張ろうね。`), a `.lab` beside a clip, or the clip's file name
("【开心】今天也要加油哦.wav", the mood in brackets picking it for that mood), and are used as they are, so a whole
dataset with its list goes in unchanged. When none says them, her ears (SenseVoice) listen to the clip and write them
down, in the background, for `.wav` clips of 16, 24 or 32 bits: every clip of a folder is kept for it. Ears can
mishear, so the page marks those clips "words to check", says so, and shows how to listen and correct them; changing
a clip's words, or "They're right", clears the mark. Without her ears, a folder keeps only its clips with words (six clips when none has any),
and the page asks for the rest ("no words yet"). "What goes in a voice folder?" on the page says the same, and where a voice can come from (your own recordings, a friend who agrees, a voice actor, an
open licence; shared game or anime voices are for yourself only) and how to record one. Shino and Fumiriya come with
voices of their own (`characters/<id>/voice.zip`: twelve English clips each, everyday, happy, sad and surprised, a
minute in all, so they can be trained too), made with Kokoro-82M (Apache-2.0; its voices af_bella and am_fenrir,
synthetic, not a real person's): given to them the first time they sit down without one, and only once, so a voice
removed stays removed (`VoicesGiven`). For example:

```
hana/
  hana.list
  clips/hana_001.wav, hana_002.wav, …
```

Without a model she speaks as a clone: GPT-SoVITS's base model copies the tone of her speaking clip (the one nearest
6 seconds, or the one chosen and listened to on the Voice page). The base model is v4 (the clearest; it makes each
sentence whole before she says it, 1.5–3.5 s) or v2ProPlus (streams, so she starts in about half a second; a little
less sure of hard words), which also blends in the tone of her first four clips for about 0.1 s more per sentence.
With a minute or more of clips (five sounds better), "Train a model on its clips" trains her own: v2ProPlus, the way
GPT-SoVITS's web UI does it, about a second per second of speech plus a minute (Hana's five minutes took 5.4 on a
laptop graphics card with 8 GB, which it all but fills; once it starts it tells the time left, and after one run the estimate is
this PC's own); her voice rests meanwhile, and a game starting stops it. The model
(about 0.3 GB) goes into the voice's folder, and the Voice page chooses between it and the clones. A voice speaks one language
(Japanese, English or Chinese): when you write in another, she speaks her own and the bubble shows your language,
like a subtitled anime (the model writes `[say: …]` before each sentence, told the two must say the same and reminded
at the end of each turn; a Japanese voice gets names in katakana, since it reads Latin letters as English, and calls
you by name with さん unless her character or you say otherwise). A sentence left without a `[say]` is silent, and
the log counts them ("went unheard"). While a game runs she rests her voice:
the server is stopped so the game has the graphics card, and started again after. The tray's Voice (or "… speaks aloud" in Settings) switches it off;
"Mute sounds" mutes her too. Clicking her stops her mid-sentence, and so does talking over her with the talk key
or asking something new; while she is still thinking, before any of her reply is out, a click only opens the slip,
and only something new sent stops her. She then remembers only what you heard, ending where she stopped, plus what she hadn't said
yet, so she can come back to it. Without her voice, what you heard means the pages of her bubble you had seen.

## A word first

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

## Talking to her aloud

You can talk to her aloud as well. Hold the mouse on her, or hold the talk key in any app (Right Ctrl unless Settings →
Talking aloud says otherwise), say it, and let go: she leans in to listen, nodding in your pauses, and answers what she
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

## Tools

She can do things too, with tools (Settings → Abilities): open a web page in your browser or an app from your
Start menu, read a page, search the web, look up a LeetCode problem, check the cabinet's numbers, and the rest below. She
says what she's about to do in her own words, and anything acting outside the app asks first: a card by her stool
shows what and where (the real address, whatever she said about it), with Allow once, Always (for that site or app) and No.
Each tool can be Allowed, Ask me first, or Off; the sites you said Always to are listed there to forget. Auto lets her
go ahead without asking (what's switched off stays off), except for what's on the Privacy page, and for the rest of a
reply once she has read a page or search results: a page could try to talk her into things. A model that
can't take tools (some that run on this PC) answers as before. Design:
[docs/specs/2026-10-05-tools-and-abilities.md](specs/2026-10-05-tools-and-abilities.md).

## Help with learning

Ask her for help with what you're on ("help me with this") and she reads your window titles to
see what it is (a browser tab's title, an editor's file), and when she needs to see it (your code, an error, an
exercise) she asks to take a screenshot of the window you were working in (the card names it; "your whole screen" if you
say so). Then she helps a step at a time, each only when you ask for more: a
nudge, the approach, an outline, and the whole answer only if you ask again after that; long steps come as a memo. For a
LeetCode problem she looks up its statement, topics and LeetCode's own hints (from leetcode.com; a leetcode.cn tab is
found by its number). She can read a web page you name, and search the web: with your provider's own search where it
has one this app can use (Anthropic's), or through your own SearXNG (Add-ons). She opens apps from your Start menu after
asking (games stay the cabinet's).

## Privacy and add-ons

Settings → Privacy gathers what she can see of yours: your window titles (allowed unless
you change it) and your screen (she asks first), which Auto never skips; and what she is told in the background, each
with a tick: what's playing in your music app, and what the record of days shows. What she sees goes to your AI
provider with the conversation; pages she reads are fetched by this PC. Settings → Add-ons holds extras that stay
off until you turn them on. The first is web search through SearXNG, a free, open-source search engine you run
yourself (with Docker: `docker run -d -p 8888:8080 searxng/searxng`, then add `json` under `search: formats:` in its
settings.yml); its Test button says whether it answers, and what to change if not.

## Memos

Anything too long for her bubble (a hint written out, a plan, a list) she hands over as a memo. She types it on her
phone and sends it, and it prints out of the cabinet's slot. Click it to read it whole (code in a fixed-width font),
copy it, pin it on the cabinet's side (five fit), or tear it off. Every memo is kept in the memory book's Memos tab,
where you can write your own too: click the cabinet's screen and open MEMOS (NEW; an app lifts the screen out to face
you, readable at any cabinet size, and a click away settles it back), or tray → "Write a memo…".
They print the same way, and she reads them unless you untick "Let … read it": if one needs something from her (a
reminder, a date), she does it and says so; if not, she says nothing. Point at a memo on the cabinet to read it there.
Click one and it flies out onto your desktop as a sheet beside the cabinet, to read and write on: drag it anywhere,
open several side by side, and its × (or Esc) sends it back.
Pull one off the slot (or off the side) and stick it anywhere on the cabinet's side; pull it off the cabinet to take it
down. Each can have its own paper colour and a sticker. Memos are yours: forgetting leaves them be.

## Focus sessions and music

The screen's FOCUS app runs a Pomodoro (25 minutes of focus, 5 off, 15 after every
fourth; down and up beside each length change it): the CRT counts down, and she tells you when each part ends, with a nudge to stand
up and drink some water at the breaks. Ask her to start one ("let's focus for 50 minutes") and she does. MUSIC shows
what your music app is playing (Spotify first) and plays, pauses and skips it through Windows' media controls (with
nothing open, play starts Spotify for you). Each focus part pays into the cabinet as it ends (see Balance). Save playlists under MY PLAYLISTS (Spotify: Share, Copy link, then ADD):
START plays one from its beginning (the app presses Spotify's own play button for it), and the starred one starts your
focus sessions; it pauses at breaks and carries on after. She can control it too, after asking ("put on Deep Focus"). And after 50 minutes at the PC without a break, she suggests standing up (Settings →
Check-ins to change it).

## Rest and sleep

With a bedtime (Settings → Balance, or tell her one: she sets it after you confirm on her card, from the
next day), she gives you a word to wind down half an hour before it, says it's time to sleep at it, and once more half
an hour after, then leaves it; in a game, tickets. After two hours in one game she suggests a break. Without a bedtime
she asks for one, once, some evening (and once more a week on, with your usual time from the record of days). At most
once a day she mentions what the record shows: nights going on late, or days without play while time is banked. When your
nights usually end well past your bedtime, she suggests a step towards it (in bed half an hour earlier than usual for a
few nights) rather than the whole way at once; your bedtime stays as you set it. And when she asks how something on your
agenda went that is one of your activities ("gym at 6"), saying you went counts it.

## Activities

Tell her about something good you did ("I went to the gym", "I cooked dinner") and she counts it towards
your play time: it pays like a problem of its size (small 1 coin, medium 2, big 4), once a day. The first time, she sizes
it; you confirm on a card by her stool ("Yes, count it" or "Not yet": "I'm going" isn't "I went"), and from then on it
pays the same. Neither its worth nor how much proof it needs is yours to set. Now and then the cabinet wants proof
(rarely for small things, usually for big ones, always when it doesn't add up, never for what can't be photographed;
less often once your proofs have convinced her): show her a photo or a screenshot and she judges it (sure pays in
full, unsure half, doubtful nothing yet; a better picture that day still pays; a photo taken on another day counts half
at most). The screen's DONE app lists what counts, with DID IT to log one yourself and × to take one off.

## Handing her things

Drop a picture, a PDF or a text file (notes, code, a log) on her, paste one into the slip
(a screenshot from Win+Shift+S, a picture copied in the browser, files copied in Explorer), or use the slip's paper clip:
"Picture or file…", "Your screen" (the screen the pointer is on, as it is now) or "From your phone…" (a card with a code:
scan it on the same Wi-Fi and choose photos; they land on the slip; the page closes with the card or after ten
minutes, and the first time Windows may ask to let the cabinet take connections: allow it on private networks). Each
waits as a chip above the slip and goes with what you say next, typed or said. Ask her to look at your screen and she
does, after asking (a card by her stool). Pictures are made no larger than models read in full (phone photos turned
upright, with the time they were taken); she keeps what she saw in words, not the picture. With a model that can't
take pictures or PDFs she says so, and none is sent to it again.

## Usage, the chat history and the memory book

Settings → Usage shows what her requests used, as your provider reports it, this month and day by day:
tokens read, the share served from the provider's cache, cache writes and output, her replies apart from her
upkeep; with the model's prices entered (from your provider's price page), the cost too.

Tray → "Chat history…" shows the conversation itself, every line kept, by day (with the notes she works from above
it). Tray → "Memory book…" shows all of it, a tab each (notes: yours, agenda, facts, diary: her log, memos, forget): edit, pin
(always in mind, upkeep leaves it alone) or delete anything. Apply saves and keeps it open, Save and close saves and
closes; while it is open it follows what upkeep and her replies change, unless you have changes not yet applied (a
line says so then).
"Clear the chat" starts the conversation afresh and keeps what she remembers; "Forget all with …" clears the
conversation and everything about the two of you; "Forget all about me" clears what every character knows about
you. It stays in the profile folder (`companion\memory.db`); the key is encrypted for your Windows
account. Without a provider she keeps to her canned lines.

## More in this folder

- [specs](specs): what was decided for each part, and which parts are built, starting with the cabinet
  ([2026-09-30-insert-coin-design.md](specs/2026-09-30-insert-coin-design.md)) and the balance
  ([2026-10-07-balance.md](specs/2026-10-07-balance.md)).
- [research](research): the research behind her character, memory, voice and body, and how her memory measures up
  ([2026-10-11-memory-benchmark.md](research/2026-10-11-memory-benchmark.md)).
- [memory-issues.md](memory-issues.md): what goes wrong in her memory, with evidence and status.
- [publishing.md](publishing.md): publishing on GitHub, then Steam.
