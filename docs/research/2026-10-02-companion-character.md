# Companion character on the stool — research note (2026-10-02)

Idea: an anime character sits on the stool beside the cabinet, reacts to what happens (coins, warnings, debt,
streaks) and, with an LLM behind it, acts as a small time-management helper. Not in v1. This note records what
is available and what it would cost. Decisions for the LLM side:
[../specs/2026-10-02-companion-helper.md](../specs/2026-10-02-companion-helper.md).

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

## Motion: what makes a seated character look alive (researched 2026-10-02)

Asked after the first spike, where the wave and cheer looked stiff and the idle too still.

- **Live2D's idle recipe.** Its SDK "Breath" effect is nothing but sine waves on a handful of parameters with
  periods that never line up: head angle X 6.5 s, Y 3.5 s, Z 5.5 s, body angle 15.5 s, breath 3.2 s, each at a
  fair amplitude ([Cubism SDK manual: Breath](https://docs.live2d.com/en/cubism-sdk-manual/breath/)). Hair and
  cloth physics are then driven by that motion. The lesson: the head is never still, and physics needs something
  to react to.
- **Second-order dynamics.** Instead of easing a value, let it follow its target like a damped spring: it
  accelerates, overshoots and settles, which reads as weight
  ([Godot C# version of t3ssel8r's formulation](https://gist.github.com/Andicraft/ab605dda924a0321e755abf8f573ebd7)).
- **Overlapping action and drag.** In a wave the upper arm leads, the forearm follows later and the hand later
  still ("successive breaking of joints"); parts do not start or stop together
  ([Brian Lemay](http://www.brianlemay.com/animation/successivebreaking.html),
  [AnimSchool](https://blog.animschool.edu/2024/04/19/follow-through-and-overlap-animation-tips/)).
  Anticipation: a small move the other way before a big one.
- **Eyes and blinks.** Eyes do not glide or stare: they jump to a fixation point in 40-80 ms and hold, the head
  follows 80-150 ms later and only part of the way, blinks are irregular (about 3.5 s mean, skewed), come in
  doubles now and then, and often accompany a large gaze shift
  ([one open-source avatar's numbers](https://github.com/Yuuzulight/Mana/pull/753)).
- **Postural sway.** Research on idle motion finds that small continuous posture shifts and breathing are what
  make a virtual character read as present
  ([Sci Rep 2021](https://www.nature.com/articles/s41598-021-91710-5)).

Applied in `Puppet.cs` / `Face.cs`: springs on every joint (looser towards the hand), reaction timelines with
anticipation (crouch before the cheer), a wave whose wrist trails the forearm, hands that travel through space
with the elbow bent, breathing that lifts chest and shoulders with a faster inhale, constant torso sway and head
drift on incommensurate periods, a faint draught on hair and skirt, eye saccades with the head lagging, skewed
blink timing, and a set of fidgets (leg swings, head tilts, re-settling, humming sway, a stretch).

Measured after the change: between two idle frames 1.8 s apart at size M, 7-9 % of the pixels in her part of the
window differ, spread over head, hair, shoulders, arms and feet.

A second CC0 VRoid sample (Sakurada Fumiriya, 16 MB zip, taller, longer legs) was dropped in to test swapping:
it worked without changes; the poses were then made to scale with hip height and leg length so that his feet
reach the ring and his arms straighten in the cheer.

## User-supplied models (tested 2026-10-02)

Question: if users are later allowed to bring their own model, is a swap quick?

- Characters from popular games and anime are not published under open licences. What exists is typically fan-use terms
  (non-commercial, often in MMD format or behind a login). The closest thing found that is well known,
  freely downloadable and permissively licensed is Alicia Solid, Niconico's 3D mascot: no credit needed,
  modification allowed, commercial use by individuals but not by companies
  ([terms](https://3d.nicovideo.jp/alicia/rule.html); the file is also in
  [UniVRM's test models](https://github.com/vrm-c/UniVRM/tree/master/Tests/Models/Alicia_vrm-0.51)).
  She is a useful test because she was not made with VRoid Studio: different material names, proportions and rig.
- Test: `alicia.vrm` (7.9 MB) placed in the profile's `characters` folder, not in the repo. Result: loaded at run
  time through godot-vrm's glTF extensions (`Companion/vrm_loader.gd`), no editor import, in the editor run and in
  the exported standalone build. She sits, waves, cheers and smiles, and her long hair hangs from the physics; her colours
  stay her own because her materials do not carry VRoid's names.
- So a user upload needs: a folder, a file picker or drag-and-drop on top of it, and a licence reminder. Bundling
  is a different matter: only CC0 or similarly open models should ship inside the app.

Also fixed here: the canvas was exactly as wide as the cabinet art, so a waving hand was cut at the window edge.
The skin generator now pads the canvas up and to the left (647x820 instead of 600x753) for the reach of a tall
character. The "stretch" fidget was removed (it looked stiff) and replaced by a deep breath.

## Energy and speech (2026-10-02)

CPU time of the app over 20 s on the dev machine, as a share of one core (run from the editor binary, size M):

| | companion on | companion off |
|---|---|---|
| before: 60 fps cap with her, no cap without | 10.8 % | 8.2 % |
| now, full pace (60 fps cap either way) | 10.8 % | 4.1 % |
| now, slow pace (15 fps: a game is running, or Energy saver) | 3.5-4.5 % | 1.7 % |

The surprise was the right-hand column: the marquee lights and glows animate all the time, so without a cap the
cabinet alone was being redrawn at the monitor's refresh rate. The cap now applies with or without her.

Speech: a paper bubble over her head in the art's ink, text typed out. Lines are canned and built from the
ledger's numbers (`App/Lines.cs`): one per event, the most pressing wins when several happen at once, and a
status line when she is clicked. An LLM would replace that one function.

## GLB / glTF models (2026-10-02)

Sketchfab offers OBJ, USDZ, glTF and GLB, not VRM. VRM is glTF plus labels (which bone is which, expressions,
spring bones, toon materials), so a rigged GLB can be used if the app supplies the labels itself. OBJ and USDZ
carry no skeleton and are out.

Added: `HumanoidMap` guesses the humanoid bones from names and structure; `Rig` takes the skeleton's transform
into account (units, facing) and straightens A-pose limbs so one set of poses fits; plain PBR materials are
converted to the toon shader; blend shapes named blink/smile/etc. become expressions.

Tested with three Mixamo-rigged samples from the three.js repository (not bundled): Michelle, Soldier (stored
facing the other way) and Xbot, all in centimetres under a rotated armature. All three sit, wave and cheer, in the
editor run and in the exported build. An unrigged model (Khronos' Duck) is refused with a message on the CRT.

Then tested with a real Sketchfab download (22 MB, a personal test only, not in the repo): 3ds Max Biped bone names with Sketchfab's numeric suffixes,
A-pose, centimetres under two rotated parent nodes, 376 bones of which 283 are hair, skirt and coat chains, no
blend shapes. She sat, waved and cheered on the first run. What that model changed:

- Characters are now seated by their hip joints, not the hips bone: Biped puts the pelvis bone level with the
  joints, VRoid puts it 4 cm above, and she floated by that much.
- Legs too short to reach the foot ring hang and swing instead of being forced towards it.
- Loose chains named hair, skirt, piao, tail, ribbon and the like get Godot's `SpringBoneSimulator3D` (no
  gravity), so they trail and settle instead of being rigid. Thighs and shins are capsule colliders for them.
- A skirt rigged for standing hangs straight through the thighs of a seated figure. Collisions alone do not fix
  that, so each skirt panel is swung forward with the thigh under it: most at the front, not at the back, and
  only far enough to lay the hem on the thigh (`Puppet.Followers`). A sphere standing in for the seat was tried
  and dropped: it threw skirt and coat tails upwards.

- Her fingers (found by the player, 2026-10-03): with a fist or a pencil to hold her hands stayed open and flat,
  palms turned the wrong ways, and the notebook never appeared. Biped numbers fingers instead of naming them
  (`Finger0` the thumb, `Finger1`–`Finger4` index to little, `Finger11`, `Finger12` the next joints), so none
  were mapped: no curl, no hand straightened along its fingers, and the pad and pencil (placed from finger
  joints) were left out. `HumanoidMap` now reads the numbers. `Rig` also rolls each hand flat at rest, palm down
  with the index side to the front (VRMs are already; a game rig's hand bone can be rolled any way), and the
  thumb now folds in as the fingers close (it stuck straight out of every character's fist). Checked on her, a
  VRM and a Mixamo model: fist, notebook and pencil, and resting hands unchanged on the other two.
- Her note-taking still looked strange to the player, rightly: that pose had been made up by eye, with no
  reference. Every finger of both hands bent alike (a claw under the pad, no grip on the pencil), the pad floated
  between the hands, and the pencil ran from the index knuckle. Rebuilt from how a pocket notebook is held and
  written in: the pad lies on the left palm, fingers straight beneath it and the thumb over its edge, nearly flat
  (tipped 25° to the viewer; tipped further, the writing hand has to come at the page from the viewer's side); the
  right hand writes with the dynamic tripod grip handwriting is taught with
  ([NHS](https://www.uhd.nhs.uk/uploads/about/docs/our_publications/patient_information_leaflets/Childrens_therapy/Development_of_Pencil_Skills.pdf),
  [kidtherapy.org](https://kidtherapy.org/pencil-grip-development/)): the pencil between the pads of thumb and
  index finger, resting on the middle finger, ring and little fingers tucked, palm down to the page. Fingers now
  bend one by one (`Puppet.Grip`). The pad is placed from her posed left hand and the pencil between her right thumb
  and index fingertips, its point on the page, so both move with her hands. Judged in close-up (`closeup:hands`)
  on her, the VRM and the Mixamo model.

Still missing for such a model: blinking and expressions (no blend shapes, the eyes are painted), eye movement
(no eye bones).

Colliders, after trying each on this model: thighs and shins are solid to all loose chains; arms are solid to hair
only. Each capsule is shrunk until no resting joint is inside it. Tried and dropped: a torso capsule (it swept
the hair she wears over her shoulders round to her back), a head sphere (it parted her fringe), arms and torso
against cloth (her shirt rode up) and a sphere for the seat (skirt and coat tails thrown upwards). So hair can
still pass through her torso on a strong head turn, and cloth ignores the seat.

Not tested: rigs with non-English bone names.

## Suggested spike (as planned before it was run)

1. Add godot-vrm (about 11 MB repo) to a scratch copy of the game, import one CC0 VRoid sample (11–17 MB).
2. Render it in a transparent SubViewport under the Compatibility renderer with the cabinet camera; check toon
   shading, outlines and hair physics.
3. Pose it sitting on the stool, add blink, breathing, mouse look-at and one reaction to `CoinsEarned`.

Pass: it looks right next to the cabinet at size M on a light background and the window stays transparent.
