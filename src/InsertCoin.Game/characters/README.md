# Characters

Shino and Fumiriya come with the app: VRoid's sample models "Sendagaya Shino" and "Sakurada Fumiriya", released
by pixiv under CC0 (`SOURCE.txt` in each folder), with personas (`persona.md`) and voices (`voice.zip`) of our
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
