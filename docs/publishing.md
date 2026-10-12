# Publishing: GitHub first, Steam later

*Started 2026-10-12 with the user. A checklist to come back to: tick what is done, note what was decided.*

The plan (the user, 2026-10-09 and 2026-10-12): the source on GitHub, kept up to date with the work as it goes (the
progress public, not a single squashed commit); later a paid or free release on Steam for those who want it easy and
updated. Nobody else should be able to sell it.

## Decisions waiting

- [ ] **The license.** Recommended: **PolyForm Noncommercial 1.0.0** (see below). Alternative: the Functional Source
  License (FSL-1.1-MIT).
- [ ] **A contributor agreement (CLA)** so outside contributions can go into the Steam build too (CLA Assistant, a free
  GitHub app). Needed with any non-commercial license, before the first outside pull request.
- [ ] **The name** (shortlist below). The cabinet's marquee can keep saying INSERT COIN in the game world either way.
- [ ] **Which notes stay private** after the personal-information review (below): the user's call where it is unclear.
- [x] **The email in commits:** fine (the user), as long as GitHub shows the commits as theirs: add the address
  under GitHub → Settings → Emails and verify it; leave "Block command line pushes that expose my email" off, or the
  push is refused.

## The license

The copyright holder is not bound by their own license: whatever is chosen, the user can still sell the app on Steam.
The license only says what everyone else may do.

**What apps like this one do** (checked 2026-10-12):

| App | What it is | License | On Steam |
|---|---|---|---|
| [Aseprite](https://aseprite.org/faq/) | pixel art editor | its own EULA: the source is public, you may compile it for yourself, never redistribute builds | paid |
| [Barotrauma](https://barotraumagame.com/ufaqs/public-source-code/) | game | its own EULA: the source is public, no standalone copies; art and sounds not in the repository; contributions may be used commercially | paid |
| [Mate Engine](https://github.com/shinyflvre/Mate-Engine) | desktop VRM companion (the closest to ours) | AGPL-3.0 with its own "MateProv2" terms; the default avatar all rights reserved | $3.99, free on GitHub; Steam adds accessories, Workshop, automatic updates |
| [VPet Simulator](https://github.com/LorisYounger/VPet) | desktop pet | Apache-2.0 code; its animations and images under separate terms (commercial use needs the authors' permission) | free |
| [Krita](https://krita.org/en/about/license/) | painting app | GPL-3.0, the Steam build too | paid, free elsewhere |
| [Mindustry](https://gamingonlinux.com/2019/09/mindustry-the-factorio-like-tower-defense-game-is-now-on-steam/page=1) | game | GPL-3.0 | paid, free on itch.io; Steam adds achievements, cloud saves, Workshop |

Three patterns: **source-available with a no-redistribution EULA** (Aseprite, Barotrauma), **copyleft code with
protected art or avatar** (Mate Engine, Krita, Mindustry: anyone may legally sell copies of the code, yet people buy
on Steam for convenience, updates and extras), and **permissive code with restricted assets** (VPet).

**Why PolyForm Noncommercial:** the first pattern is what the user asked for (nobody else sells it), and PolyForm
Noncommercial gives it with a standard license instead of a custom EULA to draft: anyone may use, change and share it
for noncommercial purposes (personal use, hobby projects, schools, charities); nobody may sell it or use it
commercially. Unlike Aseprite's EULA, people may still share builds and forks for free, which a community needs.
- It is **source-available, not open source** (the Open Source Definition rules out banning commercial use): say so
  in the README. Some contributors stay away from such projects.
- **Contributions need a CLA**, or each contributor's code arrives under the noncommercial terms and couldn't go into
  the Steam build.
- **The name and the art are the stronger protection** against a reseller than any license: keep them reserved
  ("the name and the cabinet's art are not covered by the code's license"), and pick a name that can be protected.
- **Copyright needs human authorship** ([Thaler v. Perlmutter, 2025](https://www.skadden.com/-/media/files/publications/2025/03/appellate_court_affirms_human_authorship_requirement_for_copyrighting_aigenerated_works.pdf);
  the Copyright Office's [January 2025 report](https://www.mayerbrown.com/en/insights/publications/2025/02/human-input-necessary-for-copyrightability-of-works-created-with-artificial-intelligence)):
  for code written largely with an AI, a license may count for less in court than the name. Worth a short talk with
  a lawyer before Steam.
- **The alternative, FSL-1.1-MIT** ([Sentry's announcement](https://blog.sentry.io/introducing-the-functional-source-license-freedom-without-free-riding/)):
  any use except a competing product, and each version becomes MIT two years after its release. Friendlier (people may
  use it at work, and it ends up open source), but a two-year-old version could then be sold under another name.
- **Not plain GPL:** it lets anyone sell copies. Valve also warns that copyleft is a problem combined with the
  Steamworks SDK ([Steamworks: distributing open source](https://partner.steamgames.com/doc/sdk/uploading/distributing_opensource)).

## The name

"Insert Coin" is a common phrase and several small games already use it ([one on itch.io](https://starkium.itch.io/insert-coin),
[another](https://maxyjof.itch.io/insert-coin/donate)), so it would be hard to protect. A quick search (2026-10-12)
found no game or app with these names; a trademark search (USPTO, CIPO), Steam's store, a domain and a GitHub
organisation are still to check for whichever is chosen.

| Name | Why | Notes |
|---|---|---|
| **Coin & Company** | the coin-op cabinet, and the company she keeps; reads warm, shortens to "Coin & Co." | no software found by that name |
| **Change Machine** | an arcade's change machine hands out tokens; this one helps change habits | habit apps abound, none by that name found |
| **Two Credits** | an arcade's "2 credits": the player and her | no game or app found |
| **Cozy Cabinet** | the art direction in two words | cozy organising games exist, none by that name found; says less about her |
| **Quarter Life** | an arcade's quarters, a life in balance, the quarter-life years of its players | a Hasbro parody board game is called "Quarter Life Crisis", and it sounds like Valve's Half-Life: riskier on Steam |
| Player Two | the companion as player two | taken in this very space: Player2 is an AI gaming-companion platform; avoid |

## Ready already (checked 2026-10-12)

- No keys or secrets anywhere in the history (193 commits searched for API keys, bot tokens, private calendar links
  and the user's provider address).
- No third-party characters, voices or benchmark data ever committed; the local models are git-ignored and fetched by
  `tools/models/fetch.py`, which writes each one's licence beside it.
- Everything bundled is permissive: the fonts under OFL (their licence files ship with them), the VRM add-ons under
  MIT, Shino and Fumiriya under CC0, the NuGet packages under MIT or Apache-2.0; the cabinet's art and sounds are made
  by our own scripts (`tools/art`, `tools/sfx`).
- Sizes are fine: the largest file is 18.7 MB (GitHub refuses files over 100 MB).

## Before going public

- [ ] **Personal information out, progress kept.** The review (below) lists what to change. The user's own notes go
  to a git-ignored `private/` folder that CLAUDE.md imports, so the working notes stay whole on this PC; the
  progress (specs, research, CLAUDE.md's "Where we are", the commits) stays public.
- [ ] **The history**, rewritten in a separate clone, never in the working folder (the user's rule): the private files
  taken out of every commit, the personal terms replaced in every version of every file and in the commit messages,
  every commit and date kept. Then a scan of the whole rewritten history for every term, the build and the tests, and
  only then a push. Afterwards the clean clone is the one to work in (its commits have new hashes): copy `private/`,
  the models and the demo profile's characters across.
- [ ] **LICENSE**, a NOTICE for the name and art, and CONTRIBUTING with the CLA.
- [ ] **A README front page for strangers:** what it is, a screenshot or GIF, what it needs (Windows 10 or 11, x64; a
  graphics card only for her voice), downloading a release, building from source, bringing your own AI key and what
  is sent where. The developer manual moves below it or into `docs/`.
- [ ] **The launch scripts** (`run.cmd`, `demo.cmd`, `firstrun.cmd`, `tools/dev/*.ps1`) point at `<tools>\Godot`:
  read the Godot path from `GODOT` as `build.cmd` does.
- [ ] **The release zip:** a THIRD-PARTY-NOTICES file for the libraries and models in it (MIT, Apache and BSD ask for
  their notices in binary copies), and SenseVoice's attribution. The build is about 700 MB (GitHub allows 2 GB a file).
- [ ] **The GitHub page:** description, topics, a social preview image, the first release.

## The personal-information review

*Done 2026-10-12*: 14 readers went through every line of the docs and notes, every comment and sentence-like string in
the code and tests, every line only older versions held, and all 193 commit messages. The detailed list, with where each
item is and what replaces it, stays private (`private/personal-review.md`, git-ignored).

- **No secrets** anywhere, in any version.
- **Found, in today's files:** the developer's handle and its Japanese forms (about 45 lines), their provider and model
  (about 40), local paths (43), their PC's hardware (about 10), the third-party characters and game voice packs used for
  testing (about 120, mostly in voice tests and the voice research note, two lines of which go entirely), a few errands and
  times that look real, and their own Telegram bot's name in a test.
- **Found only in the history:** their city and school in three commit messages, and older versions of the same kinds of
  items.
- **Mostly invented:** the life in the tests (exams, interviews, appointments, pets, moves); kept unless the user wants
  it neutral.
- **Per file:** `docs/to-try.md` and `docs/smoke-test.md` go private; CLAUDE.md splits (rules, map and progress public, the
  developer's own setup and habits in `private/notes.md`, which it imports); everything else stays public after the fixes.
- **Waiting on the user:** "Bob" as the tests' player name (keep or rename), the invented test life (keep or neutral),
  the `Claude-Session:` links in commit messages (keep or strip; they open only for the user's account), whether the bot
  name in a test is their real bot, and the rewrite tool (git-filter-repo, a download, or a small filter of our own).

## Steam, later (nothing here blocks GitHub)

- [ ] **The speech model's licence.** SenseVoice's [FunASR model licence](https://github.com/modelscope/FunASR/blob/main/MODEL_LICENSE)
  asks for attribution but says nothing about paid products, and Alibaba may revise it. Ask them, swap the model, or
  have the Steam build fetch it on first run.
- [ ] **Steam's AI disclosure.** Her chat is live-generated AI: the store page says so and the survey describes the
  guardrails; Valve won't approve adult sexual content generated by live AI ([GeekWire](https://www.geekwire.com/2024/valve-software-reveals-new-rules-for-ai-powered-game-development-on-steam/),
  [Gigazine](https://gigazine.net/gsc_news/en/20240111-steam-game-ai)). The app imports any character card: expect a
  question about it.
- [ ] **The Steamworks SDK** (achievements, Workshop) stays out of the public repository: Valve's SDK terms limit
  redistributing it (to check when the time comes); a public build simply goes without it.
- [ ] **Steam Direct:** $100 per app; Valve takes 30%.
- [ ] **What Steam adds** over the free GitHub build, as Mate Engine and Mindustry do: automatic updates, Workshop
  characters and voices, cloud saves, perhaps cosmetic extras.
