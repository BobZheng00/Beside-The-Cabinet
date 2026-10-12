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
- [x] **The name** (the user, 2026-10-12): **Beside the Cabinet**.
  A title in another language waits for the Steam release. On Steam with a plain subtitle (Beside the Cabinet: A
  Desktop Companion). The cabinet's marquee keeps saying INSERT COIN in the game world. Still to check: the trademark
  registers (USPTO, CIPO, CNIPA) and a domain.
- [x] **Which notes stay private** (2026-10-12): the user's list of things to try and their smoke test, and CLAUDE.md's
  notes about this PC; the progress stays public (below).
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

**Decided 2026-10-12: Beside the Cabinet.** The rounds below are how it was reached.

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

**Second round** (the user, 2026-10-12: not a big fan of Coin & Company, though it beats the rest of the first round).
Checked the same day on Steam's store and the web. The everyday phrases that would say it best are taken; two words
that don't usually meet are free, and only those can be protected as a name. Coin & Company reads like a shop's name,
and coin beside company can sound like paid company or a crypto firm, so this round leaves money out.

| Name | Why | Notes |
|---|---|---|
| Neon Hearth | the cabinet's glow, and a hearth: the warm place a home gathers round; nothing about coins, so it outlasts the coin mechanic | nothing by that name on Steam or the web; set aside in the third round, as skins may leave the cabinet anything but neon |
| **Kept Company** | what she does, said plainly; "kept" for what she remembers | no app or game by that name |
| Two's Company | the saying, and two players | taken: Two's Company, Inc., a giftware wholesaler, holds trademarks by that name, and a retro two-player game on itch.io has it |
| Save Point | where a game lets you rest and keeps your progress | taken: two game-tracker apps (savepoint.gg, SavePoint: Game Tracker) and trademark filings, one of them for game apps (2026) |
| Pull Up a Stool | her stool, and an invitation | free, but a search for it brings up toilet games |
| Small Hours | company late at night | a horror game on itch.io |

**Third round** (the user, 2026-10-12: the direction of Neon Hearth, but the cabinet's look may change with skins, so
not neon; and the name has to work in Chinese and Japanese, perhaps thought out in Chinese first). Steam gives each
language its own store name (It Takes Two is 双人成行 in Chinese), so the three names share an idea, each in that
language's own words, rather than being translations: puns don't survive one. Checked the same day on Steam's store
in each language and the web; still to check for the one chosen: trademarks in each market (USPTO, CIPO, China's
CNIPA, Japan's JPO) and Chinese app stores such as TapTap, which these searches barely reach.

| Idea | 中文 | 日本語 | English | Notes |
|---|---|---|---|---|
| Sitting by the hearth | 围炉 (gathering round the stove: 围炉夜话, 围炉煮茶) | いろり (囲炉裏, the hearth a household sits round) | Inglenook (the seat built in beside a hearth: where she sits) | none of the three on Steam; Inglenook is also a California winery and a railway shunting puzzle; set aside in the fourth round |
| A light left on for you | 留灯 | あかり (ともしび is taken: a Steam title, and a mobile game of 2026) | Lamplit | nothing called 留灯 or Lamplit on Steam or the web |
| The deskmate | 同桌 | となりの席 | (Deskmate) | the English is too close to Desktop Mate, a desktop companion on Steam |

**Fourth round** (the user, 2026-10-12: the feeling Lamplit gives, but alone it can't tell what the app is; the idea
should be clear in the name without being as plain as Mate Engine). Lamplit's feeling with the one word that says what
she is, as a phrase rather than a label. All three share one idea, so one Chinese and one Japanese name serve them:
**灯下有伴** (under the lamp, there's company) and **灯りのとなり** (beside the lamplight; となり as in となりの席, someone
right next to you). Nothing by these names on Steam in any of the three languages or on the web; "Lamplight" alone
names two games on Steam, and Lamplight City and The Lamplighters League are there too.

| Name | Why | Notes |
|---|---|---|
| **Company by Lamplight** (recommended) | a scene rather than a label: "company" can only mean someone with you, "by lamplight" the evenings at the desk | said short, "Lamplight" is those games' name |
| Lamplit Friend | keeps the word itself; "friend" says what she is | short form "Lamplit", free |
| Lamplit Companion | the clearest, with the word people search for; nearest to a plain label | short form "Lamplit", free |

**What similar apps on Steam are called** (the user asked, 2026-10-12, still unsatisfied; Steam searched for "desktop
companion", "AI companion", "desktop pet", "AI chat" and 桌宠):
- **Plain labels**, the most common: Desktop Companion (listed twice), Desktop Pet, AI Desktop Pet, Desktop
  Mate, Mate Engine, Companion OS, AI Pals; in Chinese 桌宠屋, 3D桌宠, 桌宠引擎, 虚拟桌宠模拟器 (VPet). Clear, but
  dozens look alike and none could be protected.
- **A short name with a plain subtitle**: Luvu: Your Desktop Pet; Molili AI Friends: Your AI Desk Pal; CMChar: Your
  Virtual Companion; FuBuBu: AI Companion; Your Desktop Companion - Lumi; in Chinese 知秋桌宠, Meowker 喵客 - 工作时的
  猫咪桌宠伴侣, 角色屋 桌面伙伴. The name says who, the subtitle what (and holds the words people search for).
- **A small story or a promise**, the few that stand out: Whispers from the Star; AI2U: With You 'Til The End;
  AIly's Daydream: AI Roomie; Across the Desk: Study Mate.
- The best known keep one English name in every store: Desktop Mate, MateEngine and Whispers from the Star show in
  English in the Chinese and Japanese stores too.
- **霓虹之心** (the user's earlier idea): free in the Chinese store; in English, Neon Hearts City is a game, and "neon"
  on Steam mostly means cyberpunk and action (Neon White, Neon Rage, Cyberpunk Neon Survivor).

**Beside the Cabinet** (the user's idea, 2026-10-12, in the pattern of Across the Desk). The app's own scene: she sits
on her stool beside the cabinet, and so beside the player; a reskinned cabinet is still a cabinet. Nothing by that name
on Steam or the web, nor anything like it in the Chinese and Japanese stores. "Beside", not "besides", which means
"apart from". The risk: "cabinet" alone reads as furniture or a government (the web's other cabinets are political
games: The Cabinet, Cabinet Meeting, Cabinet Bingo). The store page's art makes it an arcade cabinet; elsewhere a plain
subtitle does, and holds the words people search for: **Beside the Cabinet: A Desktop Companion**.

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

- [x] **Personal information out of today's files, progress kept** (2026-10-12). One list of replacements, kept in
  `private/rewrite/rules.py` (it names what it replaces), cleaned every tracked file: the test character is Hana, the
  LeetCode name in tests BobSmith, the town Springfield, the provider "the provider", local folders `<tools>`; two
  research lines about game assets went. CLAUDE.md keeps the rules, the map and the progress, and imports this PC's own
  notes from `private/notes.md`; the list of things to try and the smoke test moved to `private/`. A search of every
  tracked file finds nothing left; all tests pass.
- [x] **The history**, rewritten 2026-10-12 in a separate clone beside the working folder, never in it (the user's
  rule), with git-filter-repo and the same list (`private/rewrite/rewrite.py`): the two private files out of every
  commit, the replacements made in every version of every file and in every commit message, the `Claude-Session:`
  links stripped (the user's choice), all commits and dates kept. Checked: `private/rewrite/check.py --history` finds
  nothing left; its newest files are the same as the working folder's (one tree); the author line is the user's own.
- [ ] **The push**, by the user, from the clean clone: create the GitHub repository, then `git remote add origin
  <its address>` and `git push -u origin main` there. GitHub shows the commits as the user's once their email is on
  their account.
- [ ] **Then work from the clean history.** Its commits have new hashes, so the working folder takes them over:
  first a backup of the old history (`git bundle create <somewhere>/insert-coin-before-publishing.bundle --all`),
  then in the working folder `git fetch <the clean clone> main` and `git reset --hard FETCH_HEAD` (the same files, so
  nothing on disk changes; `private/`, the models and `.godot` are git-ignored and stay). Anything committed in the
  working folder meanwhile is cherry-picked into the clean clone first, as this checklist's own update was.
- [ ] **What players see takes the new name**: the README, the window and tray titles, the first run's welcome, the
  name a webhook gives the app, and Godot's application name (`config/name`, which is also the window title; Godot's
  own folder for its logs moves with it, the player's profile doesn't). The code, the profile folder
  (`%APPDATA%\InsertCoin`) and the one-copy lock keep InsertCoin, so nothing of a player's moves; the cabinet keeps
  INSERT COIN on its marquee and in her instructions. The exe's name is the user's call (a new one means ticking
  "Start with Windows" again).
- [ ] **Words that fit either character**: the pages "Her mind" and "Her voice" and the hints that say "she" read wrong
  with Fumiriya, who is a he; at least what the first run and Getting started show.
- [ ] **LICENSE**, a NOTICE for the name and art, and CONTRIBUTING with the CLA.
- [ ] **A README front page for strangers:** what it is, a screenshot or GIF, what it needs (Windows 10 or 11, x64; a
  graphics card only for her voice), downloading a release, building from source, bringing your own AI key and what
  is sent where. The developer manual moves below it or into `docs/`.
- [x] **The launch scripts** read the Godot editor's path from `GODOT`, or from `private\local.cmd` (2026-10-12; the
  test scripts use the `_console.exe` beside it, and their throwaway profiles no longer carry a LeetCode name). Left as
  it was: the export preset's template path (`custom_template/release` in `export_presets.cfg`), which a builder sets
  to their own.
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
- **Per file:** the list of things to try and the smoke test went private; CLAUDE.md split (rules, map and progress
  public, the developer's own setup and habits in `private/notes.md`, which it imports); everything else stays public,
  fixed.
- **Decided by the user (2026-10-12):** "Bob" stays the tests' player name; the invented test life stays; the
  `Claude-Session:` links are stripped from the commit messages (and left off new commits); the bot name in a test was
  invented anyway; git-filter-repo may be installed.

## Steam, later (nothing here blocks GitHub)

- [ ] **The speech model's licence.** SenseVoice's [FunASR model licence](https://github.com/modelscope/FunASR/blob/main/MODEL_LICENSE)
  asks for attribution but says nothing about paid products, and Alibaba may revise it. Ask them, swap the model, or
  have the Steam build fetch it on first run.
- [ ] **Steam's AI disclosure.** Her chat is live-generated AI: the store page says so and the content survey
  describes the guardrails against illegal content; Valve won't ship adult sexual content generated by live AI, an AI
  "virtual interactive partner" being its own example ([Steamworks: content survey](https://partner.steamgames.com/doc/gettingstarted/contentsurvey),
  [GeekWire](https://www.geekwire.com/2024/valve-software-reveals-new-rules-for-ai-powered-game-development-on-steam/),
  [Gigazine](https://gigazine.net/gsc_news/en/20240111-steam-game-ai)). Players can report what live AI made through
  Steam's overlay.
- [ ] **Adult content, the question the user asked (2026-10-12).** Mate Engine [sidesteps it](https://store.steampowered.com/app/3625270/):
  its chat is "a small, local AI with simple messages", and its store page shows no AI disclosure or mature-content
  notes. Ours is more exposed: the player brings any provider, a local model without filters included, and any
  character card, and NSFW cards are common. Hosted providers' own rules block most of it; a local model blocks none.
  For the Steam build, guardrails the code keeps rather than a prompt alone: a content rule in her instructions (the
  app decides what she does), her replies checked before they show (the provider's own moderation where it has one,
  else a small local classifier, as her ears and memory models run), cards marked NSFW refused on import, and the
  disclosure saying all of it. The GitHub build can leave it to the player, as SillyTavern does.
- [ ] **One codebase for both builds, no Steam branch** (the user asked, 2026-10-12, whether the Steam version should
  be a branch that every change also goes to). Recommended: the Steam build is a second export preset in
  `src/InsertCoin.Game/export_presets.cfg` with `steam` in its custom features, and the code asks
  `OS.HasFeature("steam")` in one place to switch the guardrails on (Workshop and the rest later). Every fix then
  reaches both builds without a merge, and the same tests cover both. A branch that takes every change has to be
  merged into after every commit, and the guardrails sit in the busiest code (her instructions, her replies, card
  import), so those merges would conflict, and sooner or later a fix would reach one build only. Each Steam release
  gets a tag (`steam-1.0`); only if a shipped build needs a fix while `main` is mid-change does a short branch start
  from that tag, merged back and deleted after. The Steamworks SDK, like the models, is fetched for the Steam build,
  not committed. Nothing to do before GitHub.
- [ ] **The Steamworks SDK** (achievements, Workshop) stays out of the public repository: Valve's SDK terms limit
  redistributing it (to check when the time comes); a public build simply goes without it.
- [ ] **Steam Direct:** $100 per app; Valve takes 30%.
- [ ] **What Steam adds** over the free GitHub build, as Mate Engine and Mindustry do: automatic updates, Workshop
  characters and voices, cloud saves, perhaps cosmetic extras. Mate Engine's set-up, the closest to ours: free on
  GitHub, $3.99 on Steam with accessories, event messages, Workshop and automatic updates; its code under AGPL-3.0 with
  terms of its own, its default avatar all rights reserved and not to be put in others' builds. The same split suits
  us: the code under the chosen license, the name, the cabinet's art and the default characters' personas reserved.
