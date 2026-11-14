# A new player gets it: the first run, missions, Settings, the voice and web search

*Decided with the user in one design session, 2026-11-04 to 2026-11-08 (#255), after the audit on the same issue, with the
voices chosen by ear on 2026-11-09 (#278). This is Preview 3's milestone, "Preview 3: a new player gets it". Nothing is built yet, beyond a spike of the first run's look and
motion in the real app ("What was tried"). The order is at the end.*

## Why

The user, 2026-11-04: "for me the onboarding cannot really let me understand the functionality of this app and the UI is
too plain (basically just enter everything while i don't know what's going on). similar things happen with the setting:
it's getting crowded and hard to navigate. even i'm familiar with it, i may have a hard time to locate where can i switch
character, where can i edit memory (mem book is not with in the setting! as a new user they may never know what is a
memory book). other setup steps like tts can be discouraging".

The audit (#255) looked at every first-run step, Getting started page, Settings page and companion window as someone
new. It found 42 things, five of which stop a new player: no default model for OpenAI (#256), an account with no credit
told to slow down (#257), nothing leading on after the Welcome, nothing showing what the app can do, and no way to what
they remember from Settings.

**The finish line.** Every issue in the milestone is merged, and someone who has never seen the app, on a fresh profile,
says what it does, finds the character switch, memory editing and voice setup within a minute each, and gets a voice
working from the app's own guide.

## What it does

### The first run

The cabinet runs the setup arcade-style, the companion is there for all of it, and missions come after. It replaces
`App/Welcome.cs` and the Getting started page. The AI is the last step and can be skipped: the app stays usable without
one, and it's asked for again when the player first talks to them.

**The intro** plays once, across the whole screen, with Skip always there. There is no room art: one beam of light with
dust in it, dust on the cabinet that the first light washes off, a flickering marquee.

1. A dark, neglected cabinet, seen from a way off. The view walks up to it in a few steps.
2. The slots pulse. The player puts a coin in (a click on them).
3. The screen strikes on from a bright line, the dust lifts, the marquee sputters into life.
4. The view leans in until the screen faces the player, and the setup runs on it, close up.
5. The view steps back, the companion arrives (below), and at the landing every light comes on. The dark gives way to
   the desktop as the cabinet settles at its normal size and place, and the companion says hello: in their own words
   with an AI connected, else with a line on how to connect one.

Skip keeps the answers given so far and takes the defaults for the rest: Shino, focus and activities, the AI later.
General in Settings can replay the intro. A profile set up before Preview 3 skips it and the missions, with its play time
unchanged.

**The setup on the screen**, in the words of an arcade:

| Screen | Asks |
|---|---|
| PLAYER 1 / ENTER YOUR NAME | Their name, typed in full (what the companion calls them; the first letters are the cabinet's initials) |
| SELECT PLAYER 2 | Who keeps them company, with ◄ ► |
| SELECT MODE | CREDITS or FREE PLAY |
| ONE LAST THING: CONNECT AN AI | The AI, on a card in front of the screen, or LATER |
| PLAYER 2 JOINED | Nothing: the step back |

- **The companion is Player 2.** "I like the player 2 join since it fits the topic" (2026-11-07).
- **SELECT PLAYER 2 is browsed by peeking**, made safe for fast switching ("make sure it does not look wierd when user
  switch the choice fast"). The name changes at once, and a peek waits for about 0.3 s of rest. One character is out at a
  time, the last one ducking back first. Models are loaded ahead, with a silhouette until then. It's judged on clips by
  mashing ◄ ►. Each character's card comes from a portrait rendered from their own model, so an added character has one too.
  Under the name is the first sentence of their card, as the Welcome showed it, in at most four of the screen's lines, cut
  at a word if it's longer.
- **SELECT MODE asks one thing and names no way of earning**, so a new source of credits doesn't change the first run. The
  ways to earn are a list in Settings → Play time. The words stay brief and natural:
  - CREDITS: "GET THINGS DONE, EARN CREDITS." and "SPEND THEM ON FUN.";
  - FREE PLAY: "NO CREDITS. PLAY AS YOU LIKE."

  That nothing ever closes is said only in Settings → Play time.
- **Connect an AI** is a card in front of the screen: "Sign in with ChatGPT" first ("For a Plus or Pro plan: no key, nothing extra to pay.", #92), then an API key or a model on
  this PC with #256's default models and #257's message and "Save and finish" once the fields are filled, and LATER
  always there.
- **Text on the screen:** at most 6 lines of about 30 characters, shown large, with the smallest letters at least 18 pixels
  high at 1366×768, measured on a recorded frame. 4K with 150% scaling is a manual check on a real screen. Anything
  longer goes on a card or in Settings.
- **Sounds** are made by the app's own sound tools, and silent with Mute.

### Their arrival

In the user's words: "the character are first behind the cabinet, having their head out looking for the user. And wave
their hand and jump out and take the stool" (2026-11-07). They peek out from the cabinet's left edge, look for the player,
find them, wave, crouch, leap, and land seated with both feet on the ring. It plays after the setup, and when the player
switches character. The feet stay behind the cabinet until the jump, so no walk has to be faked. A character the arrival
can't pose (an added model with bones missing or very odd proportions) appears on the stool under the light-up's flash.

*Built on character switch (#268):* `Puppet.Arrival.cs`, the mask in `companion_layer.gdshader`, `ArrivalFit` for what it can pose. Anything that needs them before they land seats them at once under the same flash. The first run's arrival comes with the rest of the first run (#269, #270).

### Missions

Goals ticked by what the player does, which teach the app after the first run. They're kept as data rather than tied to
the screen's apps, so a new app doesn't change the first run ("If we add new apps to the CRT will the on boarding process
change which is not very sustainable?").

| Mission (on the screen) | Ticked when |
|---|---|
| CONNECT AN AI | An AI answers |
| SAY HELLO | A first message, typed or spoken |
| EARN A CREDIT | The first earning from whichever ways to earn are on: five minutes of focus, an activity logged, a solve (only with CREDITS) |
| ASK FOR A REMINDER | A reminder is set, by them or on the agenda |
| OPEN MEMORY | The Memory category is opened |
| HEAR THEIR VOICE | They answer the player aloud (in Chinese and Japanese, where it takes a download, it's an extra: it pays if done, and the list is done without it) |

- **The reward** is 15 minutes of play time for each, so 90 minutes for all six ("we can give them 1-2 hr of play time as
  reward", 2026-11-07), or 75 if the voice one is left undone in Chinese or Japanese, paid as each one clears, once,
  outside the daily cap.
  There's none with FREE PLAY. It's paid as any earning is, so a debt comes off first.
- **Switching the mode** (the user, 2026-11-08: "if user choose to switch to the other mode, we want to bring the mission
  backup. and be careful about user switch the credit mode off"). FREE PLAY has no EARN A CREDIT, and switching to CREDITS
  brings it back if it isn't done. Missions cleared under FREE PLAY are paid then, once. Switching to FREE PLAY takes nothing
  away: what was paid stays in the balance, out of sight as the rest of it is, and it's there again with CREDITS. However
  often the mode is switched, a mission pays once. Unticking every way to earn counts as FREE PLAY, as it does today.
- **They can be hidden** from their list, and General brings them back. Hidden or done, they leave the screen's pages, and
  the companion stops mentioning them.
- **Where they show:** as one of the screen's rotating pages, starting with it, like the time left ("It would be just
  something like the remaining play time that rotate regularly on the CRT screen"); as the full list at the top of Settings
  until they're done, where it takes Getting started's place; and from the companion, who mentions the next one once,
  when it fits, and points them out once after the hello.
- **Three suggestions** sit over the empty slip until the first message: "What can you do?", "Remind me to stretch at 4",
  "Let's focus for 25 minutes".

### Settings

One window still, saved as changed. The list shows only nine plain categories, named the way ChatGPT and Claude name
theirs, and a category's parts are tabs inside it ("I would suggest not, at least not directly show all the sub items in
the setting page"; "consider naming the top level item more general"; "we can keep our style to fit into our app but should
be easy to understand", 2026-11-07). The app's own terms go: Mind, Balance, Add-ons, "memory book", and the heading named
after the character. The memory book moves in as one page ("Moves into setting would be ok").

| Category | What's in it | Today's page |
|---|---|---|
| (Missions) | At the top until they're done | Getting started |
| General | Your name, language, the cabinet's size and sounds, start with Windows, replay the intro, Help and About | Characters, Cabinet |
| Companion | Who keeps you company, as cards, how they sit, adding one | Characters |
| Memory | Tabs: Notes, Agenda, Facts, Diary, Memos, Forget; a way to Chat history, which stays a window of its own | the memory book |
| Voice | Tabs: Their voice, Talking to them | Voice, Talking aloud |
| AI model | Tabs: Connection, What it has cost | Mind, Usage |
| Play time | Mode, ways to earn, the daily limit, what counts as play | Balance |
| Notifications | Tabs: Reminders, Check-ins, Bedtime and quiet hours | Reminders, Check-ins, Balance's bedtime |
| Connections | Tabs: Your phone, Calendars, Web search | Your phone, Calendars, Add-ons |
| Abilities and privacy | Tabs: What they can do, Skills, What they may see | Abilities, Skills, Privacy |

The menu and the screen's AGENDA open Memory there. SETTINGS is a cog on the screen's home bar.

*Built (#260):* the list of categories, each part as a tab, bedtime and the quiet hours together under Notifications. *Built (#70):* Memory's six tabs inside Settings (its page's own tab row, Chat history a button above them), reached from the right-click menu, the tray, the screen's AGENDA at Agenda and Reminders' Open the agenda; a filter on Facts and Diary; red Forget buttons, still asking twice. Not yet: replay the intro, Help and About, under General.

**The look** ("We will do A", 2026-11-07):
- unticked boxes and unchosen radios drawn empty, on every window (built, #259);
- hints darkened from 2.77:1 to about 5:1 (built, #259);
- a pixel icon for each category, in the style of the screen's apps (built, #260);
- character cards with a portrait rendered from their own model;
- one short line per control, the longer explanation behind a small "?";
- connections as cards with their state and one button;
- headings in the screen's pixel font, checked at 1366×768, and everything else in the windows' rounded font (built, #260);
- every page fits Settings at its narrowest: it opens at 850 by 704 times the display scale, cut to the screen the
  cabinet is on, so on a portrait screen beside the main one (1080 px wide at 150%) its pages get about 708 px; smoke's `memorynarrow` and `settingsnarrow` measure them at 692 (built,
  #304 and #312; Companion, Their voice and Your phone with their reworks, #261, #264, #263).

**Smaller fixes from the audit:** a pointer in a page ("see Reminders") is a button that opens it; one name for each thing;
Forget looks destructive; a filter over Facts and Diary; Reminders offers the agenda; plainer words where the pages speak
of servers, tokens and file formats.

### The voice

Every PC can speak now: in English with nothing to set up, and in Chinese or Japanese after one download. The user chose
the voices by ear (2026-11-09, #278: "the default 6 listen good"). The numbers are in
`docs/research/2026-11-08-voice-without-nvidia.md`.

| Language | Engine | Shino | Fumiriya | How it gets there |
|---|---|---|---|---|
| English | Pocket TTS | Eve | George | It comes with the app (about 200 MB) |
| Chinese | Qwen TTS | Serena | Aiden | A download of 1.26 GB, asked for on Their voice |
| Japanese | Qwen TTS | Ono Anna | Aiden | The same download |

- **One engine for each language,** so a player only fetches what they'll hear. Eve and George are real speakers (VCTK, CC
  BY 4.0), and the app credits them.
- **A voice speaks one language,** as now, and that language picks the engine and the voice. It's set once, from the Language
  setting, or on Auto from the PC's display language (Chinese, Japanese, otherwise English), and stays until the player
  changes it on Their voice. A character who already has a voice starts on that voice's language, so an old English voice
  gives way to Eve or George wherever the PC is. Writing in another language doesn't change it: they speak their voice's
  language over the subtitle, as they do now.
- **The download is asked for on Their voice,** never in the middle of a conversation, and the mission points there. Until it's
  done, a player in Chinese or Japanese has them in text.
- **While a game runs the voice rests,** as it does now, and Qwen TTS's server stops, so the game gets its memory and
  processor back. *Built (#281): Qwen TTS for Chinese and Japanese, its download on Their voice, and which language a
  voice starts in (`SpokenLanguage.ForVoice`). The server starts at the first thing there is to say. A Qwen voice's page
  offers Chinese and Japanese until English has its engine (#282) and the list of voices arrives (#283).*
- **Profiles from before Preview 3.** Shino's and Fumiriya's old voices, made with Kokoro-82M, stop shipping, and one that hasn't
  been trained gives way to the suggested voice. A voice the player made or added stays chosen: with no engine for it on
  this PC the page says so and offers the suggested one. A character whose given voice the player removed stays without
  one until they pick a voice.
- **What ships is a suggestion** ("we can open up the choice for user? and what we provide will just be a suggested default
  option?", 2026-11-09). Each character has a suggested voice for each language, and Their voice lists the others that
  language's engine has, each with a "Try it". All nine of Qwen TTS's come with the one download, with Dylan's and Eric's
  accents named (Beijing, Sichuan). Eve and George are the English list for now, and each further one is a short recording
  that comes with the app. A voice the player made is listed under the language it was recorded in.
- **In every case:** each download's size is one number everywhere; the menu's "Voice" switch becomes "Speaks aloud"; the
  status says ready only when it is; Talking to them has a "Try it" that listens for five seconds and shows the words heard.
- **What to expect.** On a fast laptop's processor Pocket TTS needs about a third of a second of work per second of speech.
  Qwen TTS needs 0.93 s across the whole processor, 1.05 s on 8 fast cores, 1.4 s on 4 and 1.86 s on 8 slow ones, with its
  first sound after 0.2 to 0.3 s, and 3.5 GB of memory. Where it falls behind, speech is held back just long enough to play
  without a gap, so a 5-second line waits about 2 seconds on 4 fast cores. The page shows this PC's own speed once they've
  spoken.
- **Their voice is three steps:** hear them, pick another voice, make a new one.
- **A different voice, quickly:** Pocket TTS copies one from about 30 seconds of recording with no training, in English and
  Japanese (its Japanese files are fetched the first time). In Chinese a recording goes to GPT-SoVITS, as Pocket TTS's Chinese
  isn't good enough ("pocket performs quite poor on chinese", 2026-11-09), so that step says first that it needs that
  engine, with its size, and asks before any sentence is read; saying no changes nothing. Copying a voice first asks that it's the
  player's own or that they have permission, as Pocket TTS's terms require.
- **Making one is guided.** The app shows about ten sentences to read and records each one (3 to 10 seconds). Its ears
  check the words, length and loudness as it goes. The player fixes a word, hears the clone, and trains later if they like.
  Adding files still works. Pocket TTS takes about 30 seconds of the takes, ending on a short pause: in English 27 to 31
  seconds kept every line whole, and 38 to 44 seconds lost more than half of them.
- **The closest copy** is GPT-SoVITS, trained on the voice's own clips, and the NVIDIA card check comes first there. Without a
  card the page says so, says what to expect and still offers the install ("i guess we want to bring it up to user to let
  them see if they like the performance instead of hiding it", 2026-11-08). It speaks with v2ProPlus by default there, as v4
  takes 24 to 31 seconds for even a short line on a fast laptop processor. Even v2ProPlus falls behind speech, so sentences
  are made whole before they play: what to expect is 6 to 8 seconds for a sentence of about 3 seconds. Training needs a
  card, and says so.
- Tests drive the recorder with recorded clips, never the real microphone. Recording with a real microphone is a manual
  check for the user.

### Web search

Every provider the app lists searches with its own search ("We should use their native web search functionality for the
stand out model providers like open ai DeepSeek Claude or any other we choose to list", 2026-11-07).

| Provider | Search |
|---|---|
| Anthropic | As now |
| OpenAI with a key | The web_search tool of its Responses API (the app talks to OpenAI through that API) |
| The ChatGPT plan | The same tool |
| DeepSeek | Its Search API |
| OpenRouter | Its web search tool |
| Qwen | Its enable_search switch |
| Ollama | Ollama's web search, with a free key from an ollama.com account |
| LM Studio and other servers | A SearXNG server, as now |

Each search is billed to the player's own account, and AI model → What it has cost counts them.

## What was tried in the real app

A spike on #255 built the arrival and the walk-up and light-up in the real app, with the real art and the two shipped
characters, and the user judged them on clips: "better then expected!", then both feet on the ring, then "good as the first
version. looks good to me." The code is on the local branch `spike-255-arrival` (tags `spike-255-take3` and
`spike-255-intro2`). What it found, for whoever builds it:

- **The arrival** (`Companion/Puppet.Arrival.cs`) is a keyed timeline in the character's space: the hips on a monotone
  curve, then a parabola with real gravity; the legs by two-bone reach; the upper body on its keys through the jump, as
  springs left the head behind; the last 0.7 s blending to the seated pose so nothing jumps when it ends. Both legs go to
  the ring first (a Relaxed character often has one foot down). The cabinet hides them by a mask in the companion's layer.
  The picture stays hidden for the first 0.25 s, or it shows them for one frame where they were last drawn.
- **The camera** is the viewport's canvas transform, so the art, the live screen and the companion move as one. A camera
  going in a straight line (even in distance, not in zoom) reads as walking. A small affine shear turns the cabinet's front
  to face the viewer, undoing the art's 30° view, so the close-up needs no second drawing of the screen.
- **The dark** is shaders on the real art: the room's light through the sprites' modulate, in linear light; the tube found
  by the art's own bright pixels within the screen's quad; the room dithered, as near-black gradients band otherwise. A
  shader that sets `COLOR` loses the node's modulate unless it reads it in `vertex()`.
- **The close-up** is soft at larger sizes: the screen's picture is 400×356 and the art is twice the canvas. The real build
  should draw the screen's picture at the size it's shown, and the additive glow over the screen comes down up close so the
  words keep their ink.
- **The handover to the desktop.** The full-screen view should be a window of its own that shares the cabinet's picture
  (the same 2D world, its own camera), so the cabinet's window never resizes. A test on a hidden desktop showed the two line
  up. The cabinet's window draws nothing meanwhile (`CanvasCullMask` 0); at the end it draws again and the other closes a
  moment later, so one of them always shows the cabinet.

## Decided

- **The first run is arcade-style on the cabinet, with missions after it** (the user, 2026-11-07, "A", over a form with
  pictures). "this should not be what user directly see after open the app for the first time. We still want a more
  interactive UI and flow for user to get into the app provide basic info and then use the mission to help me get familiar
  with functionality".
- **The intro** (the user, 2026-11-07): "the cabinet is originally powered off, in a dark environment that seems to be
  neglected, user come close to it (first person pov)and light it up", then "the pov will step back and having a cool
  animation for the character to sit beside the cabinet and for the cabinet to fully light up". No room art: "the actual UX
  does not have room background anyway".
- **The app stays usable without an AI** (the user, 2026-11-07): "we want to make sure our app is still minimum usable with
  out an LLM".
- **SELECT MODE names no way of earning** (the user, 2026-11-08): "it is a bit wierd to see leetcode beside focus and done
  since it is too specific. and may be in the futrue we need to add more credit source?"
- **The words on the screen are brief and natural** (the user, 2026-11-08), turning down a longer set as "too long, feel very
  AI generated".
- **Motion is judged in the real app first** (the user, 2026-11-08): "if the animation is not properly built, it will give a
  very strange feeling of Uncanny Valley Effect. i will avoid this in any sense".
- **Web search is in Preview 3** (the user, 2026-11-07): "I will say in preview 3, as part of UX improvement".
- **The ChatGPT plan searches with OpenAI's own web search** (the user, 2026-11-08): "yes ChatGPT plan should allow OpenAI's
  web search".
- **The finish line** (the user, 2026-11-07, "A, agent dry run then a friend"): an agent with no knowledge of the app walks
  the tasks on hidden runs and says where it got lost; then a friend who has never seen the app, on a call, thinking aloud,
  while the user watches without helping (about 30 minutes). A second friend stays ready for a retest, as someone can only
  be new once.
- **The voices** (the user, by ear on #278, 2026-11-09): "for english i like Pocket TTS: Eve and George. for chinese Serena is
  good", "for japanese Qwen TTS: Ono Anna sounds great", "i think Aiden is fine for both japanese and chinese", and on the
  compressed weights the app would use, "the default 6 listen good".
- **The defaults** the user took without changing them (2026-11-08): six missions at 15 minutes each; the name typed in full;
  sounds made by the app; Skip keeping the answers; the rotation starting on MISSIONS; Help and About in General; a cog for
  SETTINGS; the audit's fixes; #70, #92, #208 and #243 in the milestone.

## Not built, and why

- **Kokoro** (the user, 2026-11-08): "we prob want to get rid of kokoro since it is larger and preform worse compared to
  Pocket".
- **Qwen TTS on a graphics card, and a Mac build:** Preview 3 ships the processor build. The same source builds for CUDA,
  Vulkan and Metal, and Pocket TTS reports good speeds on Apple Silicon, so neither engine closes that door.
- **GPT-SoVITS v5:** it changes how they sound, not how the voice is set up, so it stays out of this milestone.
- **Room art, walking on the floor, a motion-capture clip:** the arrival passes without them, and a walk would need foot
  contact faked.
- **Showing what the first run skipped again later:** the missions and the companion's one mention do that job.

## Open

- **If Pocket TTS's newest models won't run in the app's runtime, or their terms bar shipping them** (#280), English falls back
  to the public January files, which sound less like the voice, and copying a voice in Japanese goes to GPT-SoVITS as it
  does in Chinese. That's a choice for the user, with those numbers, when the spike reports.
- **Which other Pocket TTS voices to list for English:** by ear, with the build in hand.
- **To settle with the builds in hand** (the user, 2026-11-08: "for the last two points i would need to see the final
  builds. so note them down so we remember to resolve later"): whether missions can be hidden, and what SELECT PLAYER 2 says
  about each character (the first sentence of their card, or short lines as in the mockup). Both are built as this spec
  says until then (#277).
- Sign in with ChatGPT needs OpenAI's approval before a Steam build (`2026-10-18-chatgpt-plan.md`): a Steam build hides it,
  and the card leads with an API key or a model on this PC.

## Order

1. **The two bugs** (#256, #257).
2. **Ticks and contrast**, on every window (#259).
3. **Settings in nine categories** (#260), then **Memory inside it** (#70), **cards and portraits** (#261), **one line per
   control** (#262), and **connections as cards with the audit's smaller fixes** (#263).
4. **The voice:** whether Pocket TTS's newest models run in the app (#280) and Qwen TTS for Chinese and Japanese (#281), then
   English with nothing to set up (#282); then the page's steps and checks (#264), the choice of voices (#283) and the guided
   recorder (#265).
5. **Web search:** OpenAI's Responses API and the ChatGPT plan (#266), then the other providers (#267).
6. **The arrival** (#268), then **the intro** (#269), then **the setup on the screen** (#270) with **Connect an AI** (#92),
   then **SELECT PLAYER 2's peeks** (#271).
7. **Missions** (#272), then **showing them** (#273).
8. **The finish line:** the agent dry run (#274), then a friend (#275).

Also in the milestone: the LeetCode tool's words (#208), the screen's flash read from a seat (#243), and what to settle with
the builds in hand (#277).
