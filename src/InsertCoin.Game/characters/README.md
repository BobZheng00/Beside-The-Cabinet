# Characters

Shino and Fumiriya come with the app: VRoid's sample models "Sendagaya Shino" and "Sakurada Fumiriya", released
by pixiv under CC0 (`SOURCE.txt` in each folder), with personas (`persona.md`, and Shino's `persona.zh.md` in Chinese) and voices (`voice.zip`) of our
own, which stay ours ([NOTICE.md](../../../NOTICE.md)).

## Adding a character

- **As a user, no tools needed:** Settings → Characters → "Add a character…" and pick a `.vrm`, `.glb`, `.gltf` or a `.zip`
  holding one (Sketchfab's glTF downloads come zipped), or drop the file on the cabinet. It is copied into
  `characters` in the profile folder (`%APPDATA%\InsertCoin\characters`; a `.gltf` gets a folder of its own with
  the `.bin` and textures it refers to) and takes the stool at once. A file of the same name is replaced.
  "Open characters folder" shows that folder; files put there by hand appear after a restart. Files are read at
  run time, in the standalone build too.
- **Built into the app:** put the model at `src/InsertCoin.Game/characters/<id>/<id>.vrm` (lower-case id) with a
  note of its source and licence, then re-import:
  `Godot_..._console.exe --headless --path src/InsertCoin.Game --import`.

Either way `"Companion": "<id>"` in `settings.json` selects it (the id is the file name).

Their voice, if they have one, is in `<id>.voice` in the characters folder: `voice.json` (language, clips and
their words, quality), the model's weights and clips. Dropping a voice on the cabinet puts it there for whoever is
on the stool (or for a model dropped with it).

Who they are when they talk is a character card if they have one (`<id>.card.png` or `.json` in the characters
folder, or `card.png` / `card.json` in their own folder; dropping a card on the cabinet puts it there), else a
plain-text description: `persona.md` in the character's folder (built-in ones ship one), or `<file name>.md` beside
a loose model file. Without either they are a plain friendly companion.

### Writing a persona

Any plain description works. Shino's own (`shino/persona.md`) suggests a layout, from the cards people share, if you
want one: a first line saying who they are (the first run's "Who keeps you company?" shows that first sentence), then
five sections and some examples:

- **Looks:** what they look like and wear, matching their model, since the model they talk with never sees it;
- **Personality:** what they're like day to day and underneath, what drives them, what they're afraid of;
- **Background and life:** where they're from, what they're working on, what they like and don't;
- **`<name>` and {{user}}:** what the two of them are to each other;
- **How they talk:** their way of talking, and how it changes with their mood;
- **Examples:** short exchanges, each a `Player:` line and their answer, in their own words.

What helps most is what they do, not words for their manner ("she mentions it and goes back to her drawing" rather
than "cheeky"), a life of their own, and examples of different shapes: some a word or two, a few questions, some about
their own day. Keep the examples away from what players say most ("how was your day?"), as those come back nearly word
for word.

`{{user}}` in a persona is filled in with the name the player asked to be called by, or "the player" when they gave
none, as in a character card (`{{char}}` becomes the character's name). Keep `Player:` in the examples, so they are never
taken for things the player really said.

### A persona in another language

`persona.<language>.md` beside `persona.md` (or `<file name>.<language>.md` beside a loose model) is their own card in
that language, with the codes `en`, `ja`, `ko` and `zh`. Write it in that language from the start rather than
translating: Shino's `persona.zh.md` has the same person and facts as `persona.md`, in Chinese wording of its own,
with `玩家：` in its examples. Settings → Characters → Language decides when it's used: on Auto, once two of the
player's messages in a row are in that language, until two in a row are in another; chosen, whatever the player
writes. In a card in Chinese, `{{user}}` with no name given becomes "玩家". A character card stands for them in every
language, so with one these files aren't read.

What each kind of file gets:

| | VRM made with VRoid Studio | other VRM | GLB / glTF with a humanoid skeleton |
|---|---|---|---|
| sits, gestures, follows the pointer | yes | yes | yes |
| ink and two-tone shading | yes | yes | yes |
| cabinet palette | yes (by VRoid's material names) | own colours | own colours |
| expressions and blinking | yes | yes | only if its blend shapes have recognisable names |
| hair and cloth physics | yes | yes | bones named hair, skirt, tail… get springs; legs are solid to them, arms to hair; skirt panels follow the thighs |

A plain GLB does not say which bone is which, so `HumanoidMap` works it out from the usual naming habits (Mixamo,
Unreal, Blender, Biped, Daz) and the skeleton's shape; `Rig` then copes with any units, facing and rest pose (T or
A). A model without a skeleton (a sculpt, most OBJ-derived files) cannot be posed: the stool stays empty and the
CRT says why. OBJ and USDZ are not read at all; they carry no skeleton.
