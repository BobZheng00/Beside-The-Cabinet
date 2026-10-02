# Companion character on the stool — research note (2026-10-02)

Idea: an anime character sits on the stool beside the cabinet, reacts to what happens (coins, warnings, debt,
streaks) and, with an LLM behind it, acts as a small time-management helper. Not in v1. This note records what
is available and what it would cost.

Status of each claim: **read** = taken from the linked page; **untested** = not tried in this project when the
note was written. The spike further down has since tested the VRM route.

## Short answer

Feasible. A 3D anime model in the open **VRM** format is the better fit than Live2D for this project:
open format, MIT-licensed Godot importer maintained for Godot 4.7, free character creator with commercial
rights, and a 3D model can be posed sitting and rendered with the same camera as the cabinet.

## Options

### A. 3D anime model (VRM) — recommended

- **Format:** VRM is an open glTF-based standard for humanoid avatars: standard skeleton, facial expressions
  (happy, angry, sad, relaxed, surprised, blink, mouth shapes), look-at, hair/cloth spring bones, toon material
  (MToon), licence metadata inside the file.
- **Godot plugin:** [V-Sekai/godot-vrm](https://github.com/V-Sekai/godot-vrm), MIT. Imports VRM 0.x and 1.0,
  MToon shader, spring bones, expressions as animation tracks, retargets to Godot's humanoid skeleton profile.
  Last commits June–July 2026 are fixes for Godot 4.7 (read). A fork,
  [AzPepoze/godot-vrm](https://github.com/AzPepoze/godot-vrm), rewrites spring bones in C++ for speed.
  `.vrma` animation files are not imported yet (read).
- **Engine pieces already in Godot 4.7:** `LookAtModifier3D` (4.4), `SpringBoneSimulator3D` (4.5), IK modifiers
  such as `TwoBoneIK3D` (4.6). Head following the mouse and a procedural sitting pose need no plugin.
- **Models:**
  - Make our own in **VRoid Studio** (free). Models made with it may be used and sold commercially; the one
    restriction is on apps that themselves generate avatars from VRoid parts, which this is not.
  - VRoid's older sample characters (AvatarSample D–G and others) were released as **CC0**, 11–17 MB each:
    [OpenGameArt mirror](https://opengameart.org/content/vroid-studio-cc0-models). AvatarSample A–C allow
    commercial use without credit but are not CC0.
  - [Open Source Avatars](https://github.com/ToxSam/open-source-avatars): 300+ VRM avatars, mostly CC0, mixed styles.
- **Animations:**
  - [Quaternius Universal Animation Library](https://quaternius.itch.io/universal-animation-library): CC0,
    120+ humanoid clips including sitting, free tier 15 MB with a Godot export. Which sitting clips are in the
    free tier is not confirmed.
  - [VRoid's 7 free VRMA clips](https://vroid.booth.pm/items/5512385): greeting, peace sign, etc. Commercial use
    with credit; no sitting clip; the format is not imported by godot-vrm yet.
  - Mixamo: free in a finished game, raw files may not be redistributed, needs an Adobe account.
- **Untested here:** MToon outlines under the Compatibility (OpenGL) renderer, which this app needs for its
  transparent window. One artifact report in that renderer was fixed in 2023
  ([issue #63](https://github.com/V-Sekai/godot-vrm/issues/63)); nothing newer found either way.

### B. Live2D

- **Godot plugin:** [gd_cubism](https://github.com/MizunagiKB/gd_cubism), unofficial GDExtension, Godot 4.3+,
  usable from C#, v0.9.1 (March 2025). It links against Live2D's proprietary Cubism SDK for Native, which has to
  be downloaded under Live2D's licence and built in. Whether prebuilt binaries exist and whether it works in the
  Compatibility renderer: not found.
- **Licence:** [SDK Release License](https://www.live2d.com/en/sdk/license/). Individuals and businesses under
  10 million yen yearly sales are exempt from the contract and fee. Apps where users load their own models
  ("expandable applications", e.g. avatar systems) need a separate contract regardless of size — that collides
  with the skins idea.
- **Models:** Live2D's own sample characters (Hiyori, Haru, Mao…) may be used commercially only under the same
  10 million yen limit. They are front-facing, standing figures.
- **Fit:** a Live2D model is drawn for one view and one pose. A character sitting on our stool at the cabinet's
  three-quarter angle would have to be drawn and rigged for exactly that, which means commissioned work.

### C. Open-source 2D puppets (Inochi2D)

BSD-licensed Live2D alternative. No Godot runtime yet; the project reported a hiatus in 2024. Not usable now.

### D. Our own 2D cut-out character

Drawn by our art generator and animated with Godot's built-in 2D skeleton. Fully original and matches the
cabinet exactly, no licences. The risk is quality: the generator is good at hard-surface shapes, an appealing
anime face and hair is a different problem.

## How it would fit the app

- The cabinet art comes from a 3D model with a known camera (`tools/art/cozy_cabinet.py`). A 3D character
  rendered in a transparent SubViewport with the same camera sits on the stool with correct perspective, layered
  over the base art. The stool becomes an anchor in `skin.json`; a character becomes part of a skin or its own
  swappable folder.
- Liveliness without any LLM: breathing and idle sway, blinking, head and eyes following the mouse, hair
  physics, and reactions to the events the Director already emits (cheer on coins, glance at the screen on a
  warning, slump while in debt, wave on start, doze when AFK).
- LLM layer: on an event or on a click, send a compact snapshot (credits, time left, debt, streak, week stats,
  running game, time of day) and get back `{ line, emotion, gesture }`. The line goes in a speech bubble, the
  emotion drives the VRM expression, the gesture picks a clip. It stays read-only towards games: advice and
  nudges only, in line with soft enforcement.
- For a published build: no bundled API key. Bring-your-own key, a local model, or canned lines as fallback.
- Cost: the window must redraw continuously while the character moves, so this is where the energy-saver
  switch from the spec becomes real. Canvas has to grow sideways so she does not cover the cabinet.

## Existing projects (prior art)

- [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber): MIT, Live2D avatar + LLM + voice, desktop-pet mode.
- [Project AIRI](https://www.ailover-atlas.com/en/projects/airi/): MIT, Live2D and VRM, many LLM providers.
- [Mate Engine](https://github.com/shinyflvre/Mate-Engine): free Unity desktop companion on Steam, custom VRM, small local LLM.
- Desktop Mate (Steam): commercial 3D desktop character, paid characters.

None of them ties the character to a rule system like the token ledger.

## Spike result (2026-10-02, branch `companion-spike`)

Done with godot-vrm (addon files only, about 100 KB zipped) and the CC0 VRoid sample "Sendagaya Shino" (14.9 MB).
No animation pack was needed: the poses are written in code.

What was verified on the dev machine, in the real app, under the Compatibility renderer:

- **It renders and the window stays transparent.** The plugin's MToon shader works as imported, outlines included.
- **Same picture as the cabinet.** `skin.json` now exports the art's camera and the stool's position; a Godot
  frustum camera with the same lens shift puts her on the stool with matching perspective (first test, standing:
  her feet landed on the centre of the seat).
- **Same drawing style.** MToon is replaced at load by our own shader: two flat tones from the art's light
  direction, texture values remapped onto the skin's palette (plum vest, lavender skirt, pink bow, purple hair),
  a faint pink wash from the CRT, and the silhouette inked with the art's outline colour (`#1E1630`, measured
  identical in screenshots) at the art's line width.
- **Alive.** Breathing, blinking, hair and skirt physics, head and eyes following the mouse pointer, an
  occasional leg swing, glances at the screen. Reactions wired to ledger events: cheer on coins and paid debt,
  glance on token/session, alarm on low time, worry on debt, doze while paused. A click on her gets a wave.
- **Standalone export works** with the existing template (build grows from 188 to 208 MB).
- **Cost:** 300-350 MB memory (about 250 without her) and about 9 % of one core, capped at 60 fps; the tray menu's
  "Companion" switch stops the 3D rendering entirely.

Found along the way:

- The plugin's spring-bone physics breaks when the character node is moved or rotated in the world (hair blows
  sideways). Workaround in use: she stays at the origin and the camera is placed around her.
- VRoid exports hair and skirt with zero gravity; some is added at load so the skirt drapes when seated.
- This sample wears a short skirt. Seated, that needed a hands-in-lap pose and a shader rule that paints the
  white cloth on the skin texture as dark shorts. A character made for the app should simply wear shorts.
- A face shaded by its real normals looks wrong in two tones; the face is drawn flat.

Not done: LLM, speech bubble, energy saver (e.g. fewer frames while a game runs), user-supplied models,
a character of our own.

## Suggested spike (as planned before it was run)

1. Add godot-vrm (about 11 MB repo) to a scratch copy of the game, import one CC0 VRoid sample (11–17 MB).
2. Render it in a transparent SubViewport under the Compatibility renderer with the cabinet camera; check toon
   shading, outlines and hair physics.
3. Pose it sitting on the stool, add blink, breathing, mouse look-at and one reaction to `CoinsEarned`.

Pass: it looks right next to the cabinet at size M on a light background and the window stays transparent.
