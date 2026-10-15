# How the app is put together

The Godot 4.7 (.NET) app, kept thin: the companion's mind is in `src/InsertCoin.Mind`, without Godot, and the
cabinet's rules in `src/InsertCoin.Core` ([the layout](../README.md#layout)). Characters:
[characters/README.md](characters/README.md).

## The companion

A VRM character (`src/InsertCoin.Game/characters/<id>/<id>.vrm`) rendered in an off-screen 3D viewport through the
skin's own camera and shown as a layer over the cabinet art. Code in `src/InsertCoin.Game/Companion`:

- `CompanionView` sets up the viewport and camera, feeds the mouse pointer in, answers hit tests.
- `ToonMaterials` + `toon.gdshaderinc` replace the model's materials: two flat tones from one fixed light, texture
  values remapped onto the skin's palette, a pink wash from the CRT, thin ink lines inside the figure.
- `companion_layer.gdshader` inks the silhouette with the art's outline colour and width.
- `SpeechBubble` draws what the companion says: the plain lines from `App/Lines.cs` (on events a friend would react
  to, when their mind can't say them in their own words) type themselves out; replies from the mind start as
  thinking dots and grow as they stream in. A long one is shown a page at a time, like subtitles: each page turns
  once the voice has said it (`Voice.Progress`), or, when they are not speaking, once there has been time to read it,
  and the bubble fades only when they have finished. `ChatBox` is the slip you type on, `Paper` the shared look
  (cream paper, the cabinet's ink, Fredoka).
- `App/Chat.cs` connects the companion to `InsertCoin.Mind`: sends what you type, acts out stage directions
  (`[cheer]`, `[wave]`, `[worry]`, `[plan: …]`…), logs cabinet events into the conversation, folds it when it grows,
  runs upkeep every minute, and lets them use their tools (`Toolbox` and `BuiltIn` in `InsertCoin.Mind`;
  `PermissionCard` is where they ask). `App/MindSettings.cs` is the Mind page in Settings (the provider),
  `App/MemoryBook.cs` the memory book. In `InsertCoin.Mind`: `Recall` picks memories, `Log` keeps their notes on your
  talks, a log by day, `Upkeep` turns new notes into changes to their facts and your agenda while the talk is
  quiet, and `MemoryEdits` saves an edit made in the memory book without writing over a change of theirs. Design: [docs/specs/2026-10-02-companion-helper.md](../../docs/specs/2026-10-02-companion-helper.md),
  memory: [docs/specs/2026-10-11-observational-memory.md](../../docs/specs/2026-10-11-observational-memory.md)
  (the research before it: [docs/research/2026-10-02-companion-memory.md](../../docs/research/2026-10-02-companion-memory.md)).
- `App/Voice.cs` is the voice: `VoiceServer` runs the player's GPT-SoVITS hidden (tied to the cabinet, so it ends
  with it), `Speech` (in `InsertCoin.Mind`) decides what of a reply is said, `GptSoVits` streams each sentence's
  audio, and the mouth (`Face.Mouth`) follows its loudness. `App/VoiceTrainer.cs` trains a voice's own model (both
  run tied to the cabinet through `CabinetJob`). `App/VoiceSettings.cs` is its page in Settings. Measurements:
  [docs/research/2026-10-02-companion-voice.md](../../docs/research/2026-10-02-companion-voice.md).
- The ears: `App/Ears.cs` opens the microphone while the companion listens (Godot records it into a muted bus of its
  own), `TalkKey` reads the talk key, and `Hearing` (in `InsertCoin.Mind`) turns what was said into words with
  SenseVoice. `App/Listening.cs` ties them together: the mouse held on the companion, the talk key, the slip's
  microphone, and hands-free (`HandsFree`, `TurnTaking`, `VoiceDetector`, `SmartTurn` in `InsertCoin.Mind`). Design:
  [docs/specs/2026-10-05-hands-free-listening.md](../../docs/specs/2026-10-05-hands-free-listening.md).
- `App/Stool.cs` holds whoever is on the stool: their figure, the chat and its slip, their voice and its training,
  the windows for them, and characters, cards and voices coming in. `Main.cs` is the cabinet's own loop.
- `Rig`, `Puppet`, `Face` move the companion. Three layers each frame: poses (seated, plus reaction poses switched by
  short timelines, hands placed by two-bone IK), springs (every joint follows its pose as a damped spring, looser
  towards the hand, so moves overshoot and settle), and life on top (breathing, sway, head drift, fidgets, eyes
  that jump between fixation points, blinks). Poses use VRM humanoid bone names and are fitted to the character's
  proportions (hip height, leg length), so they are not tied to one model.
  In conversation (`Moment.Listen`, `Think`, `Speak`, `CutOff`) their gaze follows what people do: away, up and to
  one side, to think (a finger goes to the chin when it takes a while); at you while you talk, nodding in your
  pauses (`Backchannel`, in `InsertCoin.Mind`, finds them from the microphone's loudness); the head dipping where
  the voice swells as they talk. Their moves are kept small and asymmetric, the way a grown-up moves. The research
  behind it: [docs/research/2026-10-05-companion-agent.md](../../docs/research/2026-10-05-companion-agent.md) §7.

The character stays at the 3D origin and the camera is placed around them: the VRM plugin's hair physics goes wrong
when the character itself is moved or turned.

## Skins

A skin is a folder in `src/InsertCoin.Game/skins/<id>` with `skin.json`: full-canvas layers (`base` plus the parts
that animate), sprites (coins, ticket) and anchor points (screen quad, bulbs, tray, slots, CHECK button). Code binds
to anchors, never to art, so a new skin needs no code changes.

`skin.json` also carries `world`: the 3D camera and stool position the art was drawn with, so live 3D can be
rendered into the same picture.

Art revert points: git tags `art-v3-boxy`, `art-v4-traditional`, `art-v5-perspective`.

The skin's canvas is wider and taller than the cabinet art (see `layout()` in `tools/art/cozy_cabinet.py`) so that
a raised arm stays inside the window.
