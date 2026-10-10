# A word first

*Decided with the user, 2026-10-09. Built the same day. Changed 2026-10-10 after their first try: reminders go straight
on, and her bubble has Yes? and Not now to click.*

## Why

The user, testing the demo: they sat down, and she asked about their bedtime at once, in Japanese, with the meaning
in a Chinese bubble. They heard her voice but had to realise they should read the bubble, and by then they had lost
what she said. Two more cases came up:

- **Their own words:** "in my study and work, when someone wants to talk with me they will first raise my attention
  and only continue after I reply 'yes?'".
- **A headset plugged in but not worn:** her voice goes nowhere, and her bubble, not on top of anything, is easily
  missed altogether.

Her voice is what catches the player's attention; when it speaks another language, the bubble carries the meaning.
The flow broke because the meaning came before the player had looked, and the bubble didn't wait for them.

## What she does

What she brings up herself with nothing else to show it (a check-in, the day's word on rest or bedtime, standing up, a
focus turn) comes after a word first:

1. Her whole remark is written before any of it is shown or said. Her mind is asked to open with a short sentence that
   only gets the player's attention (their name, say), and to write all the rest with it: the cabinet holds the rest
   back and won't ask again. Nobody waits for it, so the time it takes doesn't matter.
2. She says that sentence, her call, and her bubble shows it with two answers at its foot to click, **Yes?** and
   **Not now**, while she looks at the player and waits, as while her card asks.
3. She waits for an answer:
   - **aloud:** if the player listens hands-free after her questions, her microphone opens after the call as it does
     after a question. With the talk key, any time. A short answer ("yes?", "what is it?", 嗯？, なに？) and she goes
     on. "Not now" ("later", "wait", 等一下, あとで) and she stops after her call. Anything longer is something else to
     say to her: she stops after her call and answers it;
   - **a click** on Yes?, on her or on her bubble's words: she goes on; on Not now: she stops after her call;
   - **a message typed** to her: she stops after her call and answers it.
4. **No answer in 20 seconds** (the headset on the desk, the player deep in something): she can't count it as heard.
   A ticket slides in at the screen corner, above every window, showing her whole remark with **Listen** (she goes on,
   aloud) and **OK** (it was read there: the rest is only shown in her bubble).
5. **No answer in 5 minutes:** the rest is only shown, so what else she has to say isn't held up for long.

A reminder going off goes straight on, without a call: its ticket already shows it, with Snooze and OK, so there is
nothing to wait for (the user, 2026-10-10: "i do not really need to answer yes / not now (i can just click snooze or
ok…)").

When she stops after her call, her line keeps only what was heard, and the rest is remembered as unsaid, as when she is
cut off: she can come back to it.

She goes straight on, as before, when:
- she has no voice now (switched off, in a call, a game running): the bubble alone;
- the player is already talking with her: a conversation is on, they are writing to her (something on her slip), or
  they talked with her in the last two minutes (a message, or an answer to her call). Her slip being open doesn't count
  by itself: it opens when they speak to her hands-free and stays open until the app loses focus.

## Subtitles wait for the reader

When her voice speaks another language than her bubble, the bubble is a subtitle, read to understand:
- each page stays at least as long as it takes to read;
- the last stays that long after her voice has finished;
- Chinese, Japanese and Korean read slower than English: a character counts as about three letters. A 45-character
  Chinese bubble had about 5 seconds; it now has about 10.

## Found on the way

Hands-free listening closed at once whenever a reply of hers was under way, to keep from hearing her. Her card waiting
for an answer is inside a reply (her tool waits on it), so answering her card hands-free never worked; her call would
have had the same fate. A card or a call waiting quietly for the player now doesn't count as her speaking
(`Stool.Waits`).

## Tried (2026-10-09)

Hidden runs with Hana's voice and a mock model, the bedtime question as the remark:
- a click on her bubble, the ticket's Listen, and "Yes." heard hands-free: she went on with the rest;
- "Not now." heard: she stopped after "Bob?", 63 characters kept as unsaid;
- a longer sentence heard, or a message typed: cut after "Bob?", and the message answered;
- no answer: the ticket after 20 seconds; its OK showed the rest without her voice;
- a reminder with the provider unreachable: her bubble showed it in plain words, as before;
- a Chinese subtitle was still up 5 seconds after her voice finished and gone 4 seconds later (about 7 by the reckoning).

Not yet tried: the real microphone, and a real model writing its own call.

## Tried with a real model (2026-10-10)

The user, on their own model (a GPT model through their provider), with two reminders (still called first then):
- each came back as only the call, "BobSmith！" and "ボブスミスさん！", with nothing after it, so nothing waited: her
  bubble showed only their name, the second in Japanese. Told the rest was said "once they answer", her mind took it
  as being asked again later. It is now told to write all of it in this reply, the rest held back and never asked for
  again, and the log says when a remark had nothing to hold back;
- the Japanese bubble: a remark she starts herself has no message of the player's to follow for its language, and a
  call that is only a name followed the さん guidance into Japanese. Her turn now ends with "you write in the language
  the player writes in", and the さん example shows the subtitle keeping the player's language (`[say: サムさん？] Sam?`);
- the blinking triangle said nothing: "the bubble does not look different then the others and there is no indication
  i can click on it or this is some pending msg i should continue". Hence Yes? and Not now on it, and her eyes on them;
- a reminder needs no call, its ticket having Snooze and OK; what has no ticket does (a check-in, a word on rest that
  brings up tomorrow's plans).

Then hidden runs with Hana's voice and a mock model, standing up as the remark: Not now stopped her after "Bob, got a
sec?" (68 characters kept unsaid), Yes? brought the rest, a reminder went straight on, and the requests carried the
new wording. Not yet tried: the real mouse on Yes? and Not now, and the new wording on the user's model.

## Not built, and why

- **Replaying her last line by a click on her bubble:** the user's call, 2026-10-09: "we can ask her to reexplain, but
  just replay the audio seems break down the interaction". Asking her again works already.
- **Detecting whether headphones are worn:** Windows can tell headphones from speakers, but not whether they are on
  someone's head. An unanswered call covers it without guessing.

## Where it lives

- `WordFirst` (Mind): what her mind is asked, how her remark is cut after its call (`Split`), what a spoken answer
  means (`Of`).
- `Chat.Announce(…, call)`: writes it whole, plays the call, waits, plays the rest.
- `Stool.WaitForAnswer`, `Stool.Calling`, `GoOn`, `NotNow`; `Listening.Called`; the bubble's `Waiting` (with its answers,
  `Answered`, `OnAnswer`) and `Subtitled`; `AlertTicket.Take`.
