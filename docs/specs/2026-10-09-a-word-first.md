# A word first

*Decided with the user, 2026-10-09. Built the same day.*

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

What she brings up herself (a reminder going off, a check-in, the day's word on rest or bedtime, standing up, a focus
turn) comes after a word first:

1. Her whole remark is written before any of it is shown or said. Her mind is asked to open with a short sentence that
   only gets the player's attention (their name, say). Nobody waits for it, so the time it takes doesn't matter.
2. She says that sentence, her call, and her bubble shows it with a small blinking triangle at its foot: more to come,
   as a visual novel's text box waits for a click.
3. She waits for an answer:
   - **aloud:** if the player listens hands-free after her questions, her microphone opens after the call as it does
     after a question. With the talk key, any time. A short answer ("yes?", "what is it?", 嗯？, なに？) and she goes
     on. "Not now" ("later", "wait", 等一下, あとで) and she stops after her call. Anything longer is something else to
     say to her: she stops after her call and answers it;
   - **a click** on her or on her bubble: she goes on;
   - **a message typed** to her: she stops after her call and answers it.
4. **No answer in 20 seconds** (the headset on the desk, the player deep in something): she can't count it as heard.
   A ticket slides in at the screen corner, above every window, showing her whole remark with **Listen** (she goes on,
   aloud) and **OK** (it was read there: the rest is only shown in her bubble). A reminder already has its own ticket,
   so it gets no second one.
5. **No answer in 5 minutes:** the rest is only shown, so what else she has to say isn't held up for long.

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

## Not built, and why

- **Replaying her last line by a click on her bubble:** the user's call, 2026-10-09: "we can ask her to reexplain, but
  just replay the audio seems break down the interaction". Asking her again works already.
- **Detecting whether headphones are worn:** Windows can tell headphones from speakers, but not whether they are on
  someone's head. An unanswered call covers it without guessing.

## Where it lives

- `WordFirst` (Mind): what her mind is asked, how her remark is cut after its call (`Split`), what a spoken answer
  means (`Of`).
- `Chat.Announce(…, call)`: writes it whole, plays the call, waits, plays the rest.
- `Stool.WaitForAnswer`, `Stool.Calling`, `GoOn`, `NotNow`; `Listening.Called`; the bubble's `Waiting` and `Subtitled`;
  `AlertTicket.Take`.
