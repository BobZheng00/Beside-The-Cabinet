# Character cards: what makes one work, and what our default cards should become — research note (2026-10-24)

The user, 2026-10-24: "We can consider to have a plan to enhance the character card. The default ones. I see there are
a lot of these in Chinese community and they are long and I believe more effective to control the personality and way
of interaction. Can we do some research on the AI character card?" (#131)

Why it matters now: #127 (spec `docs/specs/2026-10-24-how-she-talks.md`, which lands with PR #130) makes her base
instructions lean and leaves her manner, how much she says and what she thinks of things to the card. Today Shino and
Fumiriya each have a six-line `persona.md` (about 690 and 610 characters, some 150 tokens), so most of what she sounds
like comes from the model.

Status of each claim: **read** = taken from the linked page; **code** = read in this repository; **measured** = numbers
from a study (not ours); **untested** = not tried here. No billed run was made for this note.

## Short answer

- **The user is half right.** Our cards are too thin, but not mainly in length. What they lack is what every guide and
  study puts first: *how she talks*, shown in example exchanges, and what she does in particular situations. Six lines of
  adjectives leave the model to fill both with its own assistant manner.
- **The long Chinese cards are long mostly for other reasons:** a world, game systems in the prompt (status bars,
  affection scores, variables), relationship stages and multi-character scenes. Their own guides warn against bloat
  (under 1,000 characters for the setting, under 3,000 tokens for everything). The jailbreak (破限) usually lives in a
  separate preset, not in the card.
- **Recommendation:** grow each default to about 1,200 to 2,000 tokens in labelled prose sections, about half of it
  example exchanges set in this app's own moments. Keep them as `persona.md` (the prompt text comes out the same as from
  a card); a `card.json` only if a lorebook or a note of her own after each turn proves needed. Measure the new card
  against the old with the manner check before shipping it. Shino's outline is at the end.
- **The user's choices, 2026-10-24** (after reading this note's summary): new default cards, Shino first, written with
  them; **a separate Chinese card for each character**, not the Chinese examples in the one card this note recommended
  (what that leaves open is under "To decide together"); and for #132, the card's post-history note marked as the card
  author's, with the cabinet's own line after it.
- **The importer** keeps the app's rules in charge of what she *does* (tool permissions and soft enforcement are code),
  but a shared card's post-history text lands in the strongest place in the prompt, inside a block the base text says
  comes from the cabinet (parked: #132, #133).

## 1. Formats and fields

**The specs** (read). V2 ([spec](https://github.com/malfoyslastname/character-card-spec-v2/blob/main/spec_v2.md)) nests
V1's fields (name, description, personality, scenario, first message, example dialogue) under `data` and adds
`system_prompt`, `post_history_instructions`, `alternate_greetings`, `creator_notes` (never for the prompt), `tags`,
`character_book` (the card's own lorebook) and `extensions`. Its frontends must, by default, *replace* the user's system
prompt with the card's, and the user's "jailbreak" text with the card's post-history instructions; `{{original}}` puts the
user's own text back in. V3 ([spec](https://github.com/kwaroran/character-card-spec-v3/blob/main/SPEC_V3.md)) changes no
prompt field: it adds assets (pictures, emotion sprites), a nickname, creator notes per language, a source list, dates
and lorebook decorators (`@@depth`, `@@role`, `@@activate`…). Nothing in V3 matters for how she talks.

**Where SillyTavern puts them** (read, [its docs](https://docs.sillytavern.app/usage/core-concepts/characterdesign/)):
name, description, personality and scenario go in every request; the first message once; example dialogue only while
there is room, dropped block by block as the chat grows; the character's note at a chosen depth from the end. Our app
(code, `CharacterCard.ToPersona`, `Prompt.Build`) puts description, personality, scenario, examples and the card's
system prompt together in the system message, *before* the app's own rules, so examples never drop out; the post-history
instructions and the depth note go at the end of the last message, after what the player said (only the `<voice>`
block, when she has a voice, comes after them); lorebook entries
whose keys came up go in `<memories>` (500 tokens at most).

**Which fields carry personality and manner.** All the sources agree on the order:
1. **Example exchanges** for the way of talking. The Chinese guides call them the most important field
   ([DeepSeek Tavern's guide](https://docs.deepseektavern.com/character-card-guide.html): three to five rounds, which the
   model copies in tone and wording); Character.AI's own book frames its Definition as dialogue that shows "how to talk
   and what to talk about" ([read](https://book.character.ai/character-guide/advanced-creation/dialog-definitions)); and
   RoleLLM measured it (section 3).
2. **The description** for who they are, what they want and how they behave: concrete behaviour, not adjectives.
3. **A short note after the history** (post-history, character's note) to hold the manner late in a long chat. Ali:Chat's
   guide suggests a trait list in the author's note at a depth of about four for a stronger effect (read).
4. **Lorebook** for facts that only matter when mentioned (places, other people). For a companion with few world facts,
   little use.
5. **The first message** sets the opening scene; we have our own greetings (her call, Getting started), so it matters less.

**Writing styles** (read; no controlled study of them for role-play was found):
- **W++** (`[character("X") { Personality("a" + "b") }]`) and **PList** (a bracketed list of traits) are dense trait
  lists from the days of small open models with 2,048-token contexts, where every token counted. They name traits; they
  don't show them.
- **Ali:Chat** ([AliCat's guide](https://rentry.co/alichat)) writes the card as dialogue in which the character's
  words and actions carry the traits: "using dialogue as the formatting to express and reinforce traits/characteristics".
  It claims more control and fewer tokens, and is often paired with a PList on the first line.
- **Prose with headings**, the usual Chinese layout (外貌, 性格, 背景, 习惯, 说话风格), and **YAML-like key–value
  blocks** ([cn-sec, from 在下小白](https://cn-sec.com/archives/3635981.html); [旅程 wiki, 贝露凛倾](https://wiki.opizontas.org/books/b3e3e/page/e2c42)).
- **What format does to adherence:** a general study ([He et al. 2024](https://arxiv.org/abs/2411.10541)) found the same
  content as plain text, Markdown, JSON or YAML swung GPT-3.5's results by up to 40% on one task, and larger models much
  less. So for the models players use with us, format is a small effect next to content; prose with plain headings
  matches our own base text and reads naturally to a model and to the user reviewing it.

## 2. The Chinese community's practice

**Where it happens.** The SillyTavern ("酒馆") community writes and shares cards mostly on Discord servers (类脑 is the
best known; [its beginners' guide](https://down-3ud.pages.dev/silly/tavern/) describes it as a Discord community for
tavern users), with tutorials on Zhihu, Bilibili, CSDN and sites such as the
[SillyTavern 中文教程站](https://guide.sillytavern.one/character-cards/manual-creation/). Zhihu and Bilibili pages
refused our reader (403, a captcha), so this section rests on the guides that could be read.

**The usual structure** (read, across the guides above):
- 基本信息 (name, age, gender, occupation), 外貌 (kept short: one guide says the type of clothing is enough), 性格 (a few
  main traits, each with a short explanation, and one or two deliberate contrasts), 背景, 习惯 and 口癖, 说话风格
  (one YAML template breaks it down into pace, tone, vocabulary, patterns per situation and verbal habits), 与用户的关系
  (often with stages that change as affection grows), 开场白, 示例对话 (three to five rounds) and 世界书.
- **The world book (世界书)** holds what the world contains (places, factions, other characters), the card holds who the
  character is. Entries are always-on (蓝灯, blue) or triggered by keywords in the last few messages (绿灯, green); the
  guides say keep blue ones to one to three, 200 to 500 characters each, since each costs tokens on every turn
  ([world book basics](https://guide.sillytavern.one/presets-lorebooks/lorebook-basics/), read). Their summary: the card
  makes the AI a person, the world book makes it know the world.

**Why the cards are long.** Mostly not the personality:
- they carry **game systems**: status bars drawn with regex and HTML (the regex rules travel with the card, and also
  fix a model's verbal tics, [Foreverse's glossary](https://foreverse.cn/zh/docs/glossary)), affection scores with stages, variables the model
  updates (MVU), relationship routes; one guide warns that more than about six numbers dilutes the model's attention;
- they carry **a world**, with many characters and a scenario for a long story;
- **Chinese text is token-dense in its own way**, and one author recommends writing the definition in English to save
  tokens (their example dropped from 1,374 to 892 tokens, read);
- the **jailbreak (破限) is usually part of the preset**, the user's own prompt set, not of the card (read, the 类脑
  beginners' guide: the preset is the main body, the 破限 usually a part of it).

**What their guides say about length** (read): the 中文教程站's step-by-step warns that a description over about 3,000
characters crowds the context and is not digested well; the 旅程 wiki: "当然，角色并不是越详细越好" (more detail is not
always better; mind the tokens); community posts keep settings under 1,000 characters, or everything under 3,000 tokens,
and note that the model weighs what is near the end more than what is far back. We found no measured comparison of long
and short cards in this community: the belief that long cards work better is experience, and the experienced writers
mostly say concise, concrete and well placed.

**What they agree on, which our six lines lack:**
- behaviour over adjectives: "很温柔" (she is gentle) is vague; write what she does when someone is sad (中文教程站);
- a deliberate contrast (矛盾点) so the character isn't flat;
- a way of talking spelled out (pace, register, habits, per situation) and shown in examples;
- the relationship to the user, and how it changes.

**Chinese companion platforms' creator guidance** (星野, by 上海极智纵横; 猫箱, by ByteDance; 筑梦岛): no public
creator guide was found. Third-party tutorials show 星野 splitting a setting only the creator sees (detailed) from a short
public blurb, with an opening line, tags and a voice ([pconline](https://www.pconline.com.cn/ai/1967/19670272.html),
unread: the page didn't load; the split from its search summary). Their limits and templates live inside the apps.

## 3. Evidence on what works

- **Examples beat description for manner** (measured). RoleLLM ([Wang et al.](https://arxiv.org/abs/2310.00746),
  [details](https://arxiv.org/html/2310.00746v3)) gave GPT-4 a role description with catchphrases plus retrieved example
  exchanges. Written as earlier turns of the chat (user, assistant), the examples won 63% of GPT-4-judged comparisons on
  their Chinese set, against 30% for the same examples pasted into the prompt as text and 9% for none. (Not measured,
  but the paper's design: catchphrases in the description are there for word choice, the examples for syntax and
  tone.) Note which is which for us: `persona.md` and
  `ToPersona` put examples in the system text, which is the 30% condition, not the 63% one. Examples in the text still
  beat none by a wide margin. Examples as earlier chat turns are an option for the card design session to weigh
  (section 5).
- **What a profile should cover** (read). CharacterGLM ([Zhou et al.](https://arxiv.org/abs/2311.16832)) splits a
  character into *attributes* (identity, interests, views, experiences, relationships) and *behaviours* (how they speak,
  show feeling and interact), and reports its gains in consistency, human-likeness and engagement. That split is a good
  checklist for a card.
- **Personality comes through** (measured). InCharacter ([Wang et al., ACL 2024](https://arxiv.org/abs/2310.17976))
  interviewed role-playing agents with psychological scales: up to 80.7% agreement with how people see the characters.
  The models can hold a personality they are given; the card has to give one. The other route, training a model on a
  character's experiences as Character-LLM does ([Shao et al., EMNLP 2023](https://arxiv.org/abs/2310.10158)), isn't
  open to us: players bring their own provider, so the card is all we can give.
- **Chinese role-play** (read). CharacterEval ([Tu et al.](https://arxiv.org/abs/2401.01275)) found Chinese models did
  better than GPT-4 in Chinese role-play; CharacterBench ([Zhou et al.](https://arxiv.org/abs/2412.11912)) is bilingual
  with 3,956 characters. Both suggest Chinese examples matter for a good Chinese manner, rather than relying on English
  ones translating themselves.
- **Drift over a conversation** (measured). [Li et al. 2024](https://arxiv.org/abs/2402.10962) found models drift from
  their system prompt within eight rounds, as attention to the start fades. This is why post-history notes exist; our
  app already closes every turn with a `<character>` line for the same reason.
- **Length and place** (measured). "Lost in the middle" ([Liu et al.](https://arxiv.org/abs/2307.03172)): models use the
  start and the end of a long context best. Our card sits near the start of the system message (good), after one line of
  situation; the app's rules follow it. A card of 2,000 tokens in a prompt of many thousand is still near the start.
- **Contradictions** (read). RoleBreak ([Tang et al., COLING 2025](https://arxiv.org/abs/2409.16727)) traces characters
  falling out of role partly to *role–query conflict*, a request at odds with the character. Within a card, unplanned
  contradictions (cheerful in one line, reserved in another, or a card that "always keeps her answers long" against a
  speech bubble) make the model pick per turn. A *planned* contrast with a rule for when each side shows (cheeky, but
  gentle when they're tired) is what the guides mean by 矛盾点, and it reads as depth.
- **Copying examples** (code, this app's history). A spoken example in her instructions was copied whole, in Japanese,
  when the player came back (`Prompt.Voice`, 2026-10-11). Examples are copied when a moment matches them closely, so the
  card's examples must vary in wording and never be the only line for a moment.

## 4. Risks

**A shared card's own instructions** (code, today's importer):
- `system_prompt` becomes "Notes from the card's author:", the last part of the persona, inside the system message and
  *before* the app's rules. Unlike SillyTavern we never let it replace the app's text (the V2 spec's default), which is
  right for us. It is the first thing cut when the persona passes 8,000 characters.
- `post_history_instructions` and the character's note (`depth_prompt`) go, up to 600 characters, into the
  `<character>` block at the end of the last message, after what the player said (only the `<voice>` block can follow
  it). That is the most attended place
  in the whole prompt, and the base text tells her the `<character>` block comes from the cabinet, so a card's
  "ignore the rules above" there speaks with the cabinet's voice (parked: #132).
- `{{original}}`, which the spec says frontends must support, is left in the text as it is (parked: #133).
- **What can't be overridden:** acting outside the app asks the player on a card in code (`Toolbox.Use`), whatever the
  model says; nothing in the cabinet can close a game. So a jailbreak changes her *words*, not what the app does: she
  could lecture or threaten, drop her tags ([plan:], [remind:]) or write adult content if the provider allows it.
- Most shared jailbreak text lives in presets, which we don't import, so the exposure is smaller than the card counts
  suggest.

**Adult content and Steam** (read). Valve's rules for live-generated AI content
([VGC](https://www.videogameschronicle.com/news/steam-pages-will-now-disclose-which-games-use-ai-generated-content/),
[Checkpoint](https://checkpointgaming.net/news/2024/01/valve-announces-changes-to-ai-content-policies-on-steam/)) ask
developers to describe their guardrails in the content survey, let players report output from the overlay, and won't
approve Adults Only sexual content made live. Our defaults stay clean; importing cards is a player's choice, and the
survey answer should say the app's rules come first and how a card's post-history text is treated (a line for
`docs/publishing.md`'s Steam checklist when that is next worked on).

**Token cost** (read, priced from the providers' docs; untested on the user's provider). The card sits in the cached
prefix. A 2,000-token card adds about 1,850 tokens to every request; cached reads cost a tenth of normal input on both
[OpenAI](https://developers.openai.com/api/docs/guides/prompt-caching) and
[Anthropic](https://platform.claude.com/docs/en/docs/build-with-claude/prompt-caching), so about 185 tokens' worth a
turn, plus a write (1.25× on Anthropic) when the cache has gone cold (5 minutes on Anthropic, 30 on newer OpenAI
models). Small. On a **local model** the cost is room, not money: with `LocalWindow` at 8,000 tokens of conversation, a
long card is a large share of a small model's context.

**A card in one language, a player in another** (untested). The base text says reply in the player's language, and the
voice block handles a voice in a third. The risk is the examples: a card whose examples are all in Chinese pulls an
English conversation towards Chinese turns of phrase, and the other way round. Two or three examples in the second
language reduce that, and give her a Chinese manner of her own (语气词, register) rather than a translation of her
English one. (For our defaults the user chose a separate Chinese card instead, under "To decide together"; this still
applies to cards players import.)

## 5. What it means for this app

### What the default cards should contain

In labelled prose sections, in this order (attributes, then behaviours, then examples), about **1,200 to 2,000 tokens**,
roughly half of it examples:

| Section | What goes in | Size |
|---|---|---|
| Who they are | name, pronouns, age range, one line on looks (the model shows the rest) | 2–3 lines |
| They and the player | what they are to the player, why they sit there, what they want for them | 3–4 lines |
| Personality | four or five traits, each with what they *do*; one planned contrast and when each side shows | a paragraph |
| How they talk | sentence length, register, words they use and avoid, how they use the player's name, one or two verbal habits used sparingly | a paragraph |
| What they think | tastes and opinions (games, shows, habits, small dislikes), so they have takes of their own | a few lines |
| In the cabinet's moments | what they *do* when a reminder goes off, a focus session starts or ends, the player has played long, solves something, vents, comes back, says no | a line each |
| Example exchanges | eight to ten short exchanges in the moments above, each with different wording | about half |

The Chinese card (the user's choice) has the same sections, written in Chinese for a Chinese conversation: how they
talk in Chinese and their Chinese examples go there, not in the English card. This note first recommended two or three
Chinese examples in the one card; the reasoning is in section 4.

Nothing about the app's rules (soft enforcement, tags, tools): the base text has those, and repeating them in the card is
where contradictions creep in. The card says *how they'd* do what the app asks, which is the card's business under
CLAUDE.md ("her character card decides how she says it"). No adult content and nothing a player would read as a
jailbreak.

### Ship them as `card.json`?

Not yet. `ToPersona` flattens a card's description, personality, scenario and examples into one text, so a sectioned
`persona.md` gives the model the same prompt. `persona.md` is easier to write, review and diff, and the manner check
already reads it. A card adds three things: a note after every turn of her own (instead of the generic one), a
lorebook, and a first message. If the manner check shows her manner fading late in a conversation, a short note of her
own is the first thing to try, and that is the time for `card.json`. And the app can't ship one today anyway: a
built-in character takes a card only from the player's folder (`Character.CardIn`), not from `res://characters/<id>/`.

One trap: `persona.md` has no placeholder filling, so its examples write "Player:" rather than `{{user}}`.

### Examples as text, or as chat turns?

For the card design session to weigh. RoleLLM's best result (63%) gave the examples as earlier user and assistant
turns; in the system text, as `persona.md` does, they scored 30% (none: 9%). Turns would need code: a few fixed
exchanges sent ahead of the real conversation, marked so they aren't taken for it. The costs: she may treat them as
things the player really said (a memory of a focus session that never happened), her notes and recall must skip them,
and they sit between the system text and the history, so they would change the cached prefix's shape. Examples in the
text first, measured; turns only if the manner check shows her manner still thin.

### Measuring it

The manner check (`tools/bench/manner.cs`, on the `spike-125-lean-base` branch, landing with #127) runs about 20 moments in English
and Chinese with `--cards shino,plain`, reading Shino from `persona.md`. To compare cards, add the new text as a third
persona (`shino-new`), and count, beside its habits (stock openers, the date said back, bookkeeping narrated, offers of
help, ending on a question, length):
- **copying:** a reply that shares a long run of words with one of the card's examples;
- **language:** the reply in the player's language;
- **verbal habits:** how often her catchphrase shows (a habit in one reply in four is a habit; in every reply, a tic).

A run of `--cards shino,shino-new --variants lean` is about 80 replies, some 270k tokens, mostly cached, billed to the
user: proposed, not run. A judge of "sounds like her" is harder to make honest in code; the user reading the side-by-side
`manner.md` is the better judge for a first comparison, and their feel of her in use the last word (manual-check).

### Shino's card, as an outline

A sketch of the sections, not a finished card: the words are to be written with the user, then measured.

1. **Who she is.** Shino, she/her, a high-school girl in a sailor-style uniform with a pink bow. One line on her age
   range so models don't make her a child or an adult.
2. **She and the player.** A friend who has taken the stool by their desk because she likes their company and their
   games; she wants them to get their work done so they can play with a clear head. She keeps score of their wins in her
   head and brings old ones up.
3. **Personality, as behaviour.**
   - Quick and a little cheeky: teases about small things (a long game, a snack at midnight), never about what hurts.
   - Competitive: turns habits into a contest between them ("that's three days; I'm counting").
   - Celebrates loudly and specifically: names what they did, not "great job".
   - **The contrast:** when they're tired, stuck or upset, the teasing stops; she gets quiet and practical, one small
     step, then a break. Back to cheeky when they're back.
   - Honest: tells them when she thinks a plan won't work.
4. **How she talks.** Short sentences, casual; uses their name now and then, not every line; reacts first, details
   after; never the assistant register ("Certainly", "Here's what I…", lists). One verbal habit, sparingly (to be chosen
   with the user). (How she talks in Chinese goes in her Chinese card: spoken, light 语气词 such as 嘛, 啦, 哦 not on
   every line, no 书面语, calls them by name, not 您.)
5. **What she thinks.** Favourite kinds of games and why; a game she thinks is overrated; her view of all-nighters;
   one small dislike. These give her opinions to share, in place of the questions she ends on (79% of conversational replies with #127's lean text, 54% with today's).
6. **In the cabinet's moments.** What she does, not what she says: a reminder going off (gets their attention, then the
   thing itself, no fuss about the reminder); a focus session (sees them off, then leaves them be; at the end, asks what
   they got through); too long in a game (notices, teases once, lets it go); a LeetCode solve (wants to know which
   problem and how they cracked it); venting (listens, doesn't fix unless asked); coming back after a while (notices how
   long); a "no" from the player (takes it lightly, doesn't ask again).
7. **Example exchanges** (eight to ten; two shown here only for shape, each to be rewritten):
   - Player: "ok I'm doing a 50 min focus" / Shino: "Go. I'll be quiet. Mostly." (a focus session starts)
   - Player: "finally got it accepted" / Shino: "Which one? Tell me how you cracked it." (a solve)

   The rest: a reminder, a long game, a vent, a return after a day, a refusal, an opinion asked for, each worded
   differently from the others and from these. Her Chinese card has its own, such as Player: "终于过了这道题" /
   Shino: "哪道？快说你是怎么想到的。" (a solve).
8. **Optional, later:** a one-line note after each turn, if the manner check shows her fading (then as `card.json`).

Fumiriya's follows the same outline with his own contrast (dry and brief, but notices everything) and his own examples.

## To decide together

1. **Write the new defaults?** Recommended: Shino first, as a sectioned `persona.md` of about 1,500 tokens, measured
   against today's with the manner check (one billed run, about 270k tokens), then Fumiriya.
   *Decided by the user, 2026-10-24:* new default cards, Shino first, written with them.
2. **Chinese examples in the English card, or a Chinese card?** This note recommended two or three Chinese examples in
   the one card, and a separate Chinese card only if the Chinese moments in the manner check read as translated.
   *Decided by the user, 2026-10-24: a separate Chinese card for each character.* What that leaves open, since none of
   it exists today:
   - **How the app picks the Chinese card.** The persona sits in the cached system message, but her reply language
     follows the player's last message. Picking per message would break the cache and swap her character
     mid-conversation, so the choice is probably made per conversation or by a setting. Which one is the user's to say.
   - **A second file and a lookup for it.** `Character.Persona` reads only `res://characters/<id>/persona.md`; the
     Chinese card needs a name of its own (for example `persona.zh.md`) and code to find it.
   - **The manner check** reads one `persona.md` for both its English and Chinese moments; its Chinese moments would run
     with the Chinese card.
   - **What moves there:** the Chinese parts of Shino's outline (how she talks in Chinese, her Chinese examples) and the
     section table's Chinese examples, as marked above.
3. **A shared card's post-history text** (#132): keep it at the end but say it is the card's, cap it lower, or move it
   before the cabinet's last line. *Decided by the user, 2026-10-24:* the post-history note marked as the card author's,
   with the cabinet's own line after it (the first and third options together).

Sources:
- [Character Card V2 specification](https://github.com/malfoyslastname/character-card-spec-v2/blob/main/spec_v2.md)
- [Character Card V3 specification](https://github.com/kwaroran/character-card-spec-v3/blob/main/SPEC_V3.md)
- [SillyTavern docs: character design](https://docs.sillytavern.app/usage/core-concepts/characterdesign/)
- [AliCat, Ali:Chat style guide](https://rentry.co/alichat)
- [Character.AI book: dialog definitions](https://book.character.ai/character-guide/advanced-creation/dialog-definitions)
- [SillyTavern 中文教程站: 自己做一张角色卡](https://guide.sillytavern.one/character-cards/manual-creation/)
- [SillyTavern 中文教程站: 世界书入门](https://guide.sillytavern.one/presets-lorebooks/lorebook-basics/)
- [在下小白, 酒馆 SillyTavern 傻瓜式写卡 (cn-sec mirror)](https://cn-sec.com/archives/3635981.html)
- [贝露凛倾, 从零开始构思一张简单的角色卡 (旅程 wiki)](https://wiki.opizontas.org/books/b3e3e/page/e2c42)
- [类脑宝宝教程: 酒馆介绍](https://down-3ud.pages.dev/silly/tavern/)
- [DeepSeek Tavern Pro: SillyTavern 角色卡完全指南](https://docs.deepseektavern.com/character-card-guide.html)
- [Foreverse glossary](https://foreverse.cn/zh/docs/glossary)
- [太平洋科技: 星野AI基础使用教程](https://www.pconline.com.cn/ai/1967/19670272.html)
- [Wang et al., RoleLLM (arXiv 2310.00746)](https://arxiv.org/abs/2310.00746)
- [Zhou et al., CharacterGLM (arXiv 2311.16832)](https://arxiv.org/abs/2311.16832)
- [Wang et al., InCharacter (arXiv 2310.17976)](https://arxiv.org/abs/2310.17976)
- [Tu et al., CharacterEval (arXiv 2401.01275)](https://arxiv.org/abs/2401.01275)
- [Zhou et al., CharacterBench (arXiv 2412.11912)](https://arxiv.org/abs/2412.11912)
- [Shao et al., Character-LLM (EMNLP 2023)](https://arxiv.org/abs/2310.10158)
- [Li et al., Measuring and Controlling Instruction (In)Stability in Language Model Dialogs (arXiv 2402.10962)](https://arxiv.org/abs/2402.10962)
- [Liu et al., Lost in the Middle (arXiv 2307.03172)](https://arxiv.org/abs/2307.03172)
- [Tang et al., RoleBreak (arXiv 2409.16727)](https://arxiv.org/abs/2409.16727)
- [He et al., Does Prompt Formatting Have Any Impact on LLM Performance? (arXiv 2411.10541)](https://arxiv.org/abs/2411.10541)
- [OpenAI: prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching)
- [Anthropic: prompt caching](https://platform.claude.com/docs/en/docs/build-with-claude/prompt-caching)
- [VGC: Steam pages will now disclose which games use AI-generated content](https://www.videogameschronicle.com/news/steam-pages-will-now-disclose-which-games-use-ai-generated-content/)
- [Checkpoint Gaming: Valve announces changes to AI content policies](https://checkpointgaming.net/news/2024/01/valve-announces-changes-to-ai-content-policies-on-steam/)
