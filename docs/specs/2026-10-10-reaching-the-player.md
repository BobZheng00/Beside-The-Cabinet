# Reaching the player

*Decided with the user, 2026-10-10. Steps 2 and 3 built the same day (see "Built" below); how much she reaches out is
not built yet. Numbers marked (to tune) are first guesses, to be set on real use.*

## Why

Until now, only reminders left the PC: a plain line ("Reminder: 14:30 Check the door") sent to ntfy, a Discord webhook,
a Telegram bot or any webhook, every time, whether the player was at the PC or not. The user wanted to frame her as a
companion in real life instead, and talked it through:

- **A roommate:** "when the user is in front of the pc, the character does not need to send msg but can just call the
  suer in voice. but when she is not sure if user is active, she can try to reach out, and if she is sure user is not
  here (maybe go out, maybe go sleep, maybe just not available) she can send a msg just like what ppl usually do and
  the conversation will happen there before user comes back."
- **Both ways:** "i want to enable the two side communication so the converastion can actually happen on mobile or
  other devices not just on PC."
- **It has to scale:** deciding for each new feature whether it texts, each with a setting of its own, "making the
  whole flow not modulize and not easy to scale up".
- **Some players want her to reach out:** "some user may use the app as a 'Companion apps' which may seek the
  functionality to actually pull them back." So that is the player's setting, not ruled out.

## Three parts

A feature never decides anything about phones. It hands over something to tell; one place knows where the player is;
one table decides how she reaches them.

### 1. Something to tell

What every feature hands over: the note for her mind, the plain line (said when her mind can't be reached), how long it
stays fresh, and two facts about it:

- **How much it matters:**
  - **must:** something the player set going with a time (a reminder, the agenda's lead, a focus session's turn);
  - **should:** something of hers that matters (how did it go, a word on bedtime, a break after long play);
  - **could:** a suggestion that may pass unnoticed (standing up, what the record of days shows, a text of her own).
- **Where it makes sense:** **anywhere**, or **only at the PC** (standing up, a break from a long game, a bedtime word
  about playing late: away from the PC, they have already happened or don't apply).

A new feature makes those two choices once and knows nothing more. Today's features:

| What | Matters | Makes sense |
|---|---|---|
| A reminder, the agenda's lead before an item | must | anywhere |
| A focus session's turn (a part or a break ending) | must | anywhere (focus can be a book away from the PC) |
| How did it go | should | anywhere |
| Words around bedtime, a break after two hours of play | should | only at the PC |
| Standing up, what the record of days shows | could | only at the PC |
| A text of her own (when she keeps in touch, below) | could | anywhere |

Her answer to what the player has just said or done is not routed: she answers where they spoke to her (below).

Today's `Stool.Telling` (Answer, Turn, Own, Suggestion) becomes this, and what each feature checks now before speaking
(in a call, a full-screen game, Windows holding notifications back, settled at the PC) moves into the routing, so each
check is made in one place.

### 2. Where the player is

Worked out by the app, from what it already watches:

| Where | When |
|---|---|
| **Here** | input in the last minute (the pause the app already counts), talking with her, a game running |
| **Unsure** | no input for a minute or more, but not yet the away time, or her call went unanswered |
| **Away** | no input for the away time (5 minutes by default, on a slider from 2 to 30: the user, 2026-10-10), the screen locked, the player told her they're heading out, or their last message came by text |
| **Asleep** | the player said goodnight, or no input in the quiet hours |

Her mind's part: she hears "I'm off to the gym" or "going to bed" and marks it (a tag in her reply, as her plans are).
It lasts until the player is back at the PC: any input there makes them **here** again, and what was waiting for them
there is told, if still fresh.

### 3. How she reaches them

The roommate, as one table:

| | Here | Unsure | Away | Asleep |
|---|---|---|---|---|
| **Must** | says it at once, with its ticket (as now) | the same; a text if the away time passes with no input | a text at once | a text (they set it for then) |
| **Should** | a word first, a ticket if unanswered (as now) | a word first, a ticket if unanswered; a text if the away time passes with no input and it makes sense anywhere | a text if it makes sense anywhere; otherwise it waits for them, while fresh | it waits for the morning, while fresh |
| **Could** | a word first, no ticket (as now) | a word first; otherwise it lapses | it lapses (a text when she keeps in touch) | it lapses |

At the PC nothing changes. A reminder no longer goes to the phone while the player is there, unless they tick "also
when I'm at the PC" (the old behaviour).

A ticket on the PC and a text can both go (the user, 2026-10-10: "good enough"): the ticket waits on the desk, the text
reaches the player wherever they are. Answering either settles the other: a click on the ticket, or any input before
the text is due, and no text goes; a reply to the text takes the ticket back.

What goes out is in her words: a remark already written for her bubble is sent as it is; when the player is away from
the start, her turn is told it goes to their phone as a text. A must-reach one never waits on her mind: if her words
haven't come in 30 seconds (to tune), the plain line goes, and hers don't follow.

**Messages left for someone who is out** (the user, 2026-10-10: "if there are relative important things (must and
should by default i guess), i will just keep leaving msg to the person and the person will check and answer them all
when they eventually see it. if she know's i'm here then i guess the old rule applies"): while the player is away,
whatever the table sends goes as it comes, however many, and they answer it all when they look. Only her own (could)
wait while a text of hers is unanswered. At the PC, one thing at a time, as now: her remarks wait behind an unanswered
call, and a newer ticket replaces the one showing. The quiet hours hold too, except for what the player set for then.

## How much she reaches out

One setting for all of it, changing only what reaches the phone (at the PC she is the same):

- **Only what you set:** must-reach only.
- **Like a roommate** (the default, the user's choice, matching today's defaults): must and should.
- **Keeps in touch:** could too, and texts of her own when the player has been away a while ("how's the day going?").
  Here her mind does decide: the app opens a moment (at most two a day, three hours apart, after an hour away, never in
  the quiet hours, never while a text of hers is unanswered; all to tune), and she writes something or stays quiet
  (`[quiet]`, as when she thinks over the player's memo). How clingy she sounds is her card's; how often is the
  player's setting.

## Texting with her

- **One conversation, one memory.** What the player texts and what they say at the PC are one history, each line
  knowing where it was said. Her turn says how they are talking ("you're texting them; they're out"), so she writes as
  a text: short, and with no voice tags (`[say: …]`), the text being in the player's language.
- **She answers where they last spoke to her.** A text from the sofa while the PC is on gets a text back; back at the
  PC, she speaks again, knowing what was texted.
- **Cards become buttons** in the chat (Yes, No, Always): a permission, an activity to confirm, a bedtime.
- **What needs the player at the PC is left out while texting:** looking at the screen, reading window titles,
  opening links and apps. The music asks with buttons (pausing what was left playing); a focus session can start (for
  a book away from the PC), its turns texted.
- **Photos and files from the phone** are handed to her as any picture or file is: proof from the gym, something to
  look at.
- **A memo** she writes while texting comes as its own message, and prints on the cabinet too, for when they're back.
- **Later:** voice notes in her own voice.

## Telegram first

The user, 2026-10-10: "i'm not a frequent user of telegram, and i'm concerned with the msg not encrypted. maybe try
discord first?" Discord's text isn't end-to-end encrypted either (its DAVE protocol covers calls only, and Discord
keeps text unencrypted for moderation), and its setup is longer. So: "we will go with telegram first and we will
consider the discord and private setup later".

Telegram is two-way with no server of ours: the cabinet holds one request open to Telegram for new messages (long
polling), so it only ever calls out, opens no port, and works wherever the phone is.

**Setting it up** (about two minutes, all but one step in Telegram itself):
1. In Telegram, message @BotFather: `/newbot`, a name (hers) and a username; copy the token it gives.
2. Paste it into the cabinet; it is kept encrypted (`KeyStore`). The cabinet finds the bot's username and gives it her
   name and a description.
3. The cabinet shows a QR code and a link, `t.me/<her bot>?start=<code>`. The phone opens her chat; pressing Start
   sends the code, and that chat is hers. No chat id to look up.

Her picture is set through BotFather (`/setuserpic`); the guide in the window shows how.

**Locked to the player.** Anyone who finds a bot's name can message it. The code is single-use and lasts 10 minutes;
after it, only that private chat is listened to. Anyone else is ignored and never answered, nothing of theirs reaches
her mind, and the log says only that a message from someone else was ignored. "Pair again" starts over.

**Where data goes,** said in the window, as an add-on says: her chat goes through Telegram's servers and is not
end-to-end encrypted (bots can't use Telegram's secret chats). What she texts can include what she knows of the
player: their agenda, how they've slept, what they told her.

**When the PC sleeps or the cabinet is closed,** she can't text or answer. Telegram keeps the player's messages for up
to 24 hours; when the cabinet next runs, she reads what came and answers once, knowing how long it waited, like a
roommate who was asleep. Older ones are gone. How reachable she is depends on Windows' sleep settings.

**A bot for her alone, and one per profile:** Telegram lets one copy read a bot's messages, so the demo and the real
profile each pair with their own.

The reminders' one-way Telegram option becomes this one: a token and chat id already there count as paired.

## The one-way places

ntfy, the Discord webhook and any webhook stay, for players who want notifications only: what she would text goes
there in her words, with nothing to reply to. They are on Settings → Your phone (spec `2026-10-10-settings-and-setup.md`),
with the away time and "Also while I'm at the PC", and Telegram and how much she reaches out join them there. The
Windows notification stays with the reminders.

## Order

1. This spec.
2. Where the player is and the routing, with today's features moved onto it. At the PC nothing changes; away, must
   and should reach the one-way places in her words; reminders stop going out while the player is at the PC.
   *Built 2026-10-10* (below).
3. Telegram, both ways: pairing, texting, photos and files, buttons for her cards, messages that waited.
   *Built 2026-10-10* (below).
4. How much she reaches out, with "keeps in touch".
5. Later: voice notes; Discord; the private route; a channel for players in mainland China, where Telegram and
   Discord are blocked.

## Built (step 2, 2026-10-10)

- **One place.** `Reaching` (in the Mind library, with tests) works out where the player is (`Where`), holds the table
  (`Route`) and says how something comes at the PC (`AtThePc`); `Telling` is what every feature hands over. In the
  game, `Stool.Hand` takes it, `PassOn` routes it once she is free (the most important first), `Track` watches every
  second, and `TextOut` (set by `Main`) sends a text through the places on Your phone.
- **Today's features on it:**

  | What | Matters | Makes sense | Fresh for |
  |---|---|---|---|
  | A reminder | must | anywhere | 10 minutes (its ticket has shown it) |
  | A focus session's turn | must | anywhere | 3 minutes |
  | Her answer to the player's own press (DONE, START) | must | only at the PC | 3 minutes |
  | How did it go | should | anywhere | 12 hours (through the night) |
  | Bedtime words, a break after long play | should | only at the PC | 20 minutes |
  | Asking for a bedtime | should | only at the PC | 2 hours |
  | What the record of days shows | could | only at the PC | 2 hours |
  | Standing up | could | only at the PC | 3 minutes |

- **At the PC, one rule for all** (it was a check in each feature): what stays fresh an hour or more can wait, so
  it waits until the player has settled in (two minutes of use since sitting down), and is out of a call and a
  full-screen app or game; the rest comes now: in a call in her bubble only, in full screen or a game only as a
  ticket. Unsure, she doesn't wait for settled: she reaches out. A must in a game shows only its own ticket now
  (before, a reminder also went into her bubble, unseen behind a full-screen game).
- **Texts.** Away, her words are written told it is a text (`Reaching.AsText`, kept with the note), and nothing
  is shown or said at the PC (`Chat.Text`). A must waits at most 30 seconds for them; then the plain line goes. Her
  texts come as messages from her (her name, a speech balloon on ntfy), the plain lines as notices (an alarm clock).
  With no place to text through, what would be texted waits for the player instead.
- **Kept in case they had gone.** A must said while unsure, or a should whose call goes unanswered, keeps her
  words as a text; it goes once the player is away (the away time with no input, a locked screen), and is dropped at
  their next input.
- **Heading out.** Her mind marks `[out]` or `[sleep]` when the player says they are leaving now or going to
  sleep; it holds until they use the PC again more than a minute after (`Reaching.Leaving`).
- **Asleep** is away in the quiet hours, or after `[sleep]`.
- **The check-in's note** no longer says the player is back at the cabinet, as it may go by text.
- **Settings → Your phone:** "Away after" (a slider, 2 to 30 minutes, 5 by default) and "Also while I'm at the PC"
  (reminders to the phone as a plain line at the PC too, as before).
- **Tests and dev.** A test run takes the player as here (it never goes by whoever sits at this PC);
  `whereabouts:away` (here, unsure, asleep) says where they are, `whereabouts:off` watches again.

## Built (step 3, 2026-10-10)

- **The client** (`Telegram`, in the Mind library, with tests): getMe, getUpdates with a long wait, sendMessage with
  buttons (a long text in parts, the buttons under the last), typing, answering a press (its buttons go), fetching a
  photo or a file, naming the bot. `Pairing`: a 24-character code, used once, good for ten minutes, only from a private
  chat.
- **Listening** (`Texting`, in the game): one long wait at a time, while a bot is set up. Only the paired chat is
  listened to; anyone else is ignored and logged as such. What came at once, or while the cabinet was closed, is one
  message to her (`Telegram.Together`), in the order sent, each that waited with how long ago it was sent. A 409
  (another copy reading the same bot) is logged as "each profile needs a bot of its own"; a token Telegram doesn't
  know stops it.
- **Her side** (`Stool.Texted`): their last words came by text, so they are away until they use the PC again
  (the `[out]` mark); she answers by text once free (`Chat.TextBack`, told `Reaching.Texted`), with "typing…" while
  she writes. The conversation keeps where texting began and ended. Her cards (a permission, an activity to
  confirm, a bedtime) come as buttons; a memo comes as a message and prints too; what needs the PC (`Tool.AtThePc`:
  the screen, window titles, opening a link or an app) is turned down when called (`Toolbox.Away`), the tool list
  staying the same so the provider's cache holds.
- **Her texts while away** (step 2) go to the paired chat as messages from her, without her name in front.
- **Settings → Your phone:** "Text with her on Telegram": paired, with Pair again and Forget it; or the steps (Open
  BotFather, paste the token and Check it: the bot is found and given her name and a description; then a QR code and
  its link, live until Start is pressed), and where her chat goes. The one-way Telegram fields are gone: a token and
  chat id already kept count as paired.
- **Tests and dev:** `telegramat:http://127.0.0.1:port` (a stand-in Telegram), `paircode:CODE` (the next code), and
  `<tools>\agent-trial\tools\fake_telegram.py`, which plays the player's side from a script.
- **Not yet:** voice notes; "Pair again" lets the old chat go until the cabinet restarts if pairing isn't finished.

## Not chosen, and why

- **Each feature deciding whether it texts, with a setting each:** the user's point, it doesn't scale. Features now
  say only how much something matters and where it makes sense.
- **Her mind deciding when to text:** a model only thinks when asked, so something has to wake it; asking every few
  minutes costs money and drifts; the app knows presence better (input, a locked screen, calls); and the player should
  be able to tell when their phone will buzz. It also keeps the rule that the app decides what and when, her card how.
  Her mind still decides what she says, whether a suggestion is worth a text, where the player has gone from what they
  say, and, when she keeps in touch, whether to write at all.
- **No texts that pull the player back** (Claude's first proposal, for the app's balance): some players want exactly
  that, so it is a setting, and not the default.
- **Discord first:** no more private than Telegram for text; a developer portal, a server shared with the bot, and a
  live connection to keep. Later.
- **The private route first:** the phone page grown into a chat, reached from anywhere through Tailscale (an app on
  the PC and the phone linking the player's own devices, encrypted end to end), with a ping carrying no words through
  ntfy or Discord. The most private, but more to install, and a page instead of a chat app. Later, as an add-on.

## Open

- The numbers marked to tune: 30 seconds for her words, the limits on texts of her own. Settled 2026-10-10: one away
  time, 5 minutes by default, on a slider; it also decides when what she said at an unsure moment goes by text.
- Whether the cabinet can set her bot's picture itself, sparing a step.

## References

- [Telegram Bot API](https://core.telegram.org/bots/api) (getUpdates keeps updates up to 24 hours; inline keyboards)
- [Telegram: deep linking](https://core.telegram.org/bots/features#deep-linking) (`?start=` codes)
- [Discord: every voice and video call is now end-to-end encrypted](https://discord.com/blog/every-voice-and-video-call-on-discord-is-now-end-to-end-encrypted)
- [TechRepublic: Discord DAVE encrypts calls, but not text chats](https://www.techrepublic.com/article/news-discord-dave-end-to-end-encryption/)
