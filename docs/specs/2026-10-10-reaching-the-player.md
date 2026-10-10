# Reaching the player

*Decided with the user, 2026-10-10. Not built yet. Numbers marked (to tune) are first guesses, to be set on real use.*

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
| **Here** | input in the last 2 minutes (to tune), talking with her, a game running |
| **Unsure** | no input for 2 to 10 minutes (to tune), or her call went unanswered |
| **Away** | the screen locked, no input for 10 minutes or more, the player told her they're heading out, or their last message came by text |
| **Asleep** | the player said goodnight, or no input in the quiet hours |

Her mind's part: she hears "I'm off to the gym" or "going to bed" and marks it (a tag in her reply, as her plans are).
It lasts until the player is back at the PC: any input there makes them **here** again, and what was waiting for them
there is told, if still fresh.

### 3. How she reaches them

The roommate, as one table:

| | Here | Unsure | Away | Asleep |
|---|---|---|---|---|
| **Must** | says it at once, with its ticket (as now) | the same; a text if no input within 2 minutes | a text at once | a text (they set it for then) |
| **Should** | a word first, a ticket if unanswered (as now) | a word first; a text if still no input 2 minutes after the ticket and it makes sense anywhere | a text if it makes sense anywhere; otherwise it waits for them, while fresh | it waits for the morning, while fresh |
| **Could** | a word first, no ticket (as now) | a word first; otherwise it lapses | it lapses (a text when she keeps in touch) | it lapses |

At the PC nothing changes. A reminder no longer goes to the phone while the player is there, unless they tick "also
when I'm at the PC" (the old behaviour).

What goes out is in her words: a remark already written for her bubble is sent as it is; when the player is away from
the start, her turn is told it goes to their phone as a text. A must-reach one never waits on her mind: if her words
haven't come in 30 seconds (to tune), the plain line goes, and hers don't follow.

**No double texting:** while a text of hers is unanswered, she sends no other of her own; only must-reach ones still go.
The quiet hours hold too, except for what the player set for then.

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
there in her words, with nothing to reply to. They move with Telegram into one window (tray → "Her phone…", name to
settle), with how much she reaches out and "also when I'm at the PC". The Windows notification stays with the
reminders.

## Order

1. This spec.
2. Where the player is and the routing, with today's features moved onto it. At the PC nothing changes; away, must
   and should reach the one-way places in her words; reminders stop going out while the player is at the PC.
3. Telegram, both ways: pairing, texting, photos and files, buttons for her cards, messages that waited.
4. How much she reaches out, with "keeps in touch".
5. Later: voice notes; Discord; the private route; a channel for players in mainland China, where Telegram and
   Discord are blocked.

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

- The numbers marked to tune: 2 and 10 minutes for unsure and away, 2 minutes before a text, 30 seconds for her words,
  the limits on texts of her own.
- Whether both the ticket and a text, when she's unsure, is one too many.
- Whether the cabinet can set her bot's picture itself, sparing a step.

## References

- [Telegram Bot API](https://core.telegram.org/bots/api) (getUpdates keeps updates up to 24 hours; inline keyboards)
- [Telegram: deep linking](https://core.telegram.org/bots/features#deep-linking) (`?start=` codes)
- [Discord: every voice and video call is now end-to-end encrypted](https://discord.com/blog/every-voice-and-video-call-on-discord-is-now-end-to-end-encrypted)
- [TechRepublic: Discord DAVE encrypts calls, but not text chats](https://www.techrepublic.com/article/news-discord-dave-end-to-end-encryption/)
