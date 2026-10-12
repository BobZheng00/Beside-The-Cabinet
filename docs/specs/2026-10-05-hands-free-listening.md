# Hands-free listening (2026-10-05)

How the player talks to her without holding the talk key. Decided with the user on 2026-10-05; the research behind it is in `docs/research/2026-10-05-companion-agent.md` (§2–4).

## What was decided

**The talk key stays the default.** Out of the box she listens only while the talk key or the mouse is held on her, or after the slip's microphone is clicked. Everything below is the player's choice, in Chat settings → Listening.

**Follow-up listening** (off until ticked):
- After she has finished saying something, she listens for a few seconds. If the player starts talking in that time, it is their next turn; if not, the microphone closes.
- Voice assistants do the same, also as an option: Alexa's Follow-Up Mode listens 5 s after a reply, Google's Continued Conversation about 8 s.
- **How long:** chosen by the player, 3 to 20 seconds (6 by default). Never under 3: the user's minimum.
- **After what**, each ticked separately:
  - after she answers the player (on);
  - after a question she asks on her own, such as a check-in (on);
  - after anything else she says on her own, such as an event remark or a reminder (off).

**Conversations** (C): a switch, not a default.
- **Started and ended** from the tray ("Conversation (she keeps listening)") or by double-tapping the talk key. The double tap can be turned off in Chat settings.
- **In a conversation** she listens after everything she says.
- **It ends** after 30 s without the player saying anything, or by the switch or the key again.

**A wake word** (D): not now. It would need the microphone open all the time, with a keyword model listening; that is a privacy and battery cost on every PC, false triggers happen, and the small model that runs on a CPU covers Chinese and English only. Revisit if players ask.

**Where she never listens on her own**, unless the player allows each:
- in a call (another app has the microphone or camera);
- in full screen, while presenting, or in Windows' quiet time (Windows' own "hold notifications" signal);
- while a game runs.

**Shown on the cabinet:** while the microphone is open, for any reason, the CRT shows "MIC" in its top right corner, with a dot that blinks like a recording light.

**Never while she speaks.** Her own voice would come back through the microphone. So she listens only once she has finished, and talking over her still needs the key or a click. Talking over her by voice waits for the echo-cancellation spike.

## How a turn is heard

`TurnTaking` (Mind, tested) runs on two models, both on the processor:
- **Silero's voice detector** says, 32 ms at a time, whether someone is speaking. A quarter second of speech starts a turn; coughs and clicks are shorter. While she waits, only the last 0.6 s is kept, so the start of the first word is not lost and nothing older is held.
- **Smart Turn v3.2**, full precision, is asked at each pause of a quarter second whether the player has finished. It judges from the voice (wording and intonation), not only the silence.
  - If it is sure (0.95 or more; at first 0.5, raised after the player's recording, below), the turn ends there.
  - If not, she waits up to 2 s of quiet.
  - A turn ends at 30 s whatever happens.
- **Then as with the talk key:** she nods "got it", hears the turn with SenseVoice, and answers.

**Measured** (*measured*, 2026-10-05, test recordings through a pretend microphone, never the real one):
- the models load in about 0.1 s;
- Smart Turn takes about 0.1 s a pause;
- on the first test sentence it said 0.99 at a 0.6 s pause.

**Measured on the player's own voice** (2026-10-06, the agent spike's recording, eleven English turns; agent note "The spike's recording"): at 0.5 Smart Turn cut them off at three thinking pauses; at 0.95 at none, and two of their endings waited for the 2 s quiet instead. Hence 0.95 (`SmartTurn.Sure`). Through their earphones her voice doesn't reach the microphone at all.

## Built (2026-10-05)

- **Mind:** `HandsFree` (the choices and when they apply), `TurnTaking`, `SmartTurn` (the spike's C# port), `VoiceDetector`, each with tests where they make sense.
- **Game:**
  - `App/Listening.cs` takes over everything about her microphone from `Stool.cs`: the talk key, holding her, the slip's microphone, nods, hands-free turns and conversations.
  - `Ears` also feeds the detector as sound comes in.
  - The MIC light is on `ScreenView`, the Listening tab in `ChatSettings`, and the Conversation switch in the tray.
- **Models:** `src/InsertCoin.Game/models/turn-taking/`, fetched by `python tools/models/fetch.py turns`.
- **Dev actions** (they never open the real microphone):
  - `fakemic`: hands-free listening hears only recordings;
  - `mictalk:<wav>`: a recording said into the open microphone;
  - `converse` (or `converse:on`, `converse:off`);
  - `listeningsettings`: the Chat settings Listening tab.
- **Not tried yet:** the real microphone hands-free, and the double tap (a dev run never presses keys). Both are on the user's own list of things to try.
