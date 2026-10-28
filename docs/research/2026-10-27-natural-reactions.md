# Natural reactions: what makes her replies read like a friend's, not a counsellor's — research note (2026-10-27)

The user tried #169's lean base text with Shino's seventh-draft card on the demo's provider on 2026-10-27. Told "my
manager just told me the thing I shipped last week has to be redone", she said:

> That's rough. Redoing something you thought was finished is the worst. Did they say what needs changing?

The user: "i prob never hear someone say 'Redoing something you thought was finished is the worst' under this
situation … as a friend i prob will response something like (what? how can they do that! what's the issue?)" (#184).
Other replies from the same try and from the billed runs (`run-127`, `run-1025`) have the same feel: "That's two
things, then. The UI can wait till tomorrow.", "Three losses is enough for one night. Do something else.", "You don't
have to turn it into a lesson or a plan right this second", "That's already pretty bad. What was the worst part?". And
asked "how's your day?", she gave the card's own example back nearly word for word.

This note builds on `2026-10-24-character-cards.md` (formats, the Chinese community, RoleLLM) and does not repeat it.

Status of each claim: **read** = taken from the linked source (for papers behind a paywall, the abstract or a quoted
summary); **ours** = counted here, on replies the billed runs already collected, with no new requests; **untested** =
our reading, not measured. No billed run was made for this note.

## Short answer

- **Friends affiliate; counsellors and assistants process.** In conversation analysis, a friend answering a trouble or a
  complaint shows they share the teller's stance: a reaction ("What?"), an assessment as strong as theirs, a reason the
  teller is right ("they could have told you before"), "I'd have…", or a story of their own. A factual follow-up
  question, a bare "mm", a generic label or advice don't do that: tellers treat them as not being on their side (read).
- **The trigger reply has all three weak parts and none of the strong ones.** "That's rough." is a label anyone could
  give. "Redoing something … is the worst." is a saying, the kind of line people use to close a topic, and it is about
  anyone, not this player and this manager. "Did they say what needs changing?" is a factual follow-up question, the
  response the research finds non-affiliative. Nothing in it is on the player's side against the manager.
- **Models produce one well-liked template.** A 2026 study found 83–90% of LLM replies to people's troubles follow one
  sequence (paraphrase, validate, advise), against 6–40% of human replies, and LLMs never told a story of their own,
  where 17–68% of human replies did (read). Preference training rewards what raters like and what is typical, so the
  template is where models settle.
- **The new cards already removed most of the counsellor.** On the replies already collected (ours), today's thin card
  invited the player to talk ("Want to tell me what happened?") in 23 of 35 English troubles replies, offered a menu in 8
  and used therapy-speak in 8. The draft cards did each once or never in 20. What's left is the shape the user noticed: a
  label, sometimes a saying, then a factual question, and no side taken (1 in 20) and no story of her own (0 in 20).
- **What to change:** the card's examples, not her base text. Its troubles examples teach advice and labels ("Go and eat
  something", "Go to bed", "That bad, huh.") and none takes the player's side against someone absent since the sixth
  draft's "Without asking? I'd have said something." went. Five concrete example changes for #140, no base-text change,
  and seven new counts for the manner check, which can be tried on stored replies for free and added before #140's
  agreed billed run.

## 1. How friends answer a trouble

**A trouble is not a request for service** (read). Jefferson and Lee found that advice given to someone telling a
trouble is routinely rejected, and traced it to two ways of talking about a trouble getting crossed: a
*troubles-telling*, where what matters is the teller and their experience, and a *service encounter*, where what
matters is the problem and its fix ([Jefferson & Lee 1981](https://doi.org/10.1016/0378-2166(81)90026-6); later work
sums it up as advice being treated "as a misaligned response to troubles-tellings"). Jefferson's study of troubles-talk
shows these tellings follow a recurrent shape, a sequence of positioned parts ([Jefferson 1988](https://doi.org/10.2307/800595)).
An assistant is built for the service encounter. That is the counsellor feel in one sentence.

**What counts as being on their side** (read). Couper-Kuhlen studied 36 strong displays of anger or indignation in 15
complaint stories from everyday British and American talk, and used what the teller did next to tell which responses
worked ([Couper-Kuhlen 2012](https://www.fu-berlin.de/sites/inu/research/clusters-alt/loe/loep/media/4__Couper-Kuhlen_Conversational_complaint_stories.pdf)):
- **affiliative:** claims of understanding ("I know"), *congruent negative assessments* (as strong as the teller's, about
  the same target), and *by-proxy justifications*, where the listener supplies a reason the teller is right to be angry;
- **non-affiliative:** factual follow-up questions (her example: "did you call him back?" after the teller's angry climax,
  which "addresses the sequence of events … but does not deal with the storyteller's manifest stance"), minimal responses
  and silence. Tellers met with these pushed harder or recast their story as having "a bright side" and closed it;
- **timing and strength matter:** affiliative turns come quickly and match or raise the teller's intensity; an agreeing
  but late and flat "it is" was taken as not affiliating.

Stivers shows the same split between *alignment* (going along with the telling) and *affiliation* (taking the teller's
stance), and that what counts depends on where a response comes: a nod mid-story affiliates, at the end it falls short,
and assessments and congruent second stories are what the end calls for
([Stivers 2008](https://pure.mpg.de/pubman/item/item_60931_2)).

**The range of empathic responses** (read, through
[Buchholz's summary](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2014.00349/full) of
[Heritage 2011](https://doi.org/10.1017/CBO9780511921674.008)):
- **response cries** ("Oh!", "Ugh"), the most immediate;
- **subjunctive assessments:** "if it were me…", "I'd have been furious", the listener putting themselves in it;
- **parallel assessments and second stories:** "that happened to me", which affiliate but risk sounding "flat, pallid or
  pro forma" if thin, and competitive if they outgrow the teller's;
- **ancillary questions:** questions about the details, which show some interest but let the listener "escape further
  conversational obligations in a single move"; the teller "cannot decide … how empathically engaged the recipient is".

**Surprise is something people do together** (read). "Wow", "oh my god", "what?" and ritual disbelief ("you're
kidding") are performed for the teller, often set up turns ahead, and are how a listener shows the news landed
([Wilkinson & Kitzinger 2006](https://doi.org/10.1177/019027250606900203)). The user's "what? how can they do that!" is
this: a surprise token, then indignation aimed at the absent party.

**Sayings close topics** (read). Drew and Holt found figurative and idiomatic expressions ("that's the way it goes",
"it's the last straw") cluster where a topic is summarised and closed, and when one fails to close the topic, it shows
the speakers disagree ([Drew & Holt 1998](https://eprints.whiterose.ac.uk/1545/)). Couper-Kuhlen's data has a complaint
episode "brought to a close with an idiom". A generalisation such as "Redoing something you thought was finished is
the worst" works the same way: it summarises the trouble as a general truth and so offers to end it, right after the
player has raised it. It is also about anyone, where a congruent assessment is about this manager and this feature.

**Complaints about someone absent** (read). A complaint about an absent person invites the listener to join in the
moral judgement of that person ([Drew 1998](https://doi.org/10.1080/08351813.1998.9683595), via later work citing it;
Couper-Kuhlen's stories are all of this kind). The user's moment is one: the manager is the third party. A reply that
says nothing about the manager has, in these terms, declined the invitation.

**Professional empathy has its own shape** (read). On a child-protection helpline, call-takers answer a crying caller
with "take your time" and *empathic receipts*: a formulation of the caller's state with a hedge on how sure they are of
it ([Hepburn & Potter 2007](https://doi.org/10.1080/08351810701331299)). That is right for a helpline. Between friends
the same moves ("That sounds really frustrating", "It makes sense you'd feel…") mark the speaker as someone doing a job.

**What this means for the trigger, in our own short examples** (untested as wording; the shapes are from the sources):
- "What? After you shipped it?" — surprise, and a partial repeat of the part that's wrong;
- "They couldn't have said that before you shipped it?" — a by-proxy justification, aimed at the manager;
- "I'd have been livid." — a subjunctive assessment;
- "Ugh. Mine did that with a whole set of drawings once." — a short second story (for Shino it would be a client);
- and a question, if any, after one of those, and asking about the manager rather than the task ("What's his problem
  with it?"). The user's "what's the issue?" is a question too: questions aren't the trouble, a factual one with
  nothing before it is.

## 2. Texting against speech

Her words are shown in a bubble and spoken by her voice at the PC, and texted on Telegram away from it.
- **In speech, affiliation rides on prosody** (read). Couper-Kuhlen found response cries ("ohhh", "uh::h") affiliate only
  when their pitch and loudness match or exceed the teller's, and that a cry needs words after it to last, which she calls
  "verbal reinforcement". A flat "Oof." from a voice model may land as the downgraded, non-affiliative kind.
- **In text, punctuation and spelling carry it** (read). A one-word text reply ending in a full stop was rated less
  sincere than the same reply without one; the same words handwritten showed no difference
  ([Gunraj et al. 2016](https://researchconnect.suny.edu/en/publications/texting-insincerely-the-role-of-the-period-in-text-messaging/);
  [Houghton et al. 2018](https://doi.org/10.1016/j.chb.2017.10.044): the full stop reads as abrupt or final).
  Lengthened words ("nooo") carry feeling in short messages
  ([Brody & Diakopoulos 2011](https://aclanthology.org/D11-1052/)).
- **What it means here** (untested): the card's player lines are typed the way people text (lowercase, no full stops)
  and hers are punctuated sentences, which suits a bubble read as speech and a voice. On Telegram a clipped
  "That bad, huh." may read colder than meant. Not worth a change until the user notices it there: the words carry
  more than the punctuation, and a reaction with words after it ("What? After you shipped it?") works in both.

## 3. Why models answer like counsellors

**One template** (read). Gueorguieva et al. coded 3,265 LLM replies from six models and 1,290 human replies to
support-seeking posts with ten tactics ([2026](https://arxiv.org/abs/2604.08479)). One sequence (paraphrase and
validation, then advice and information, then validation or reassurance, then advice again) matched 83–90% of LLM
replies, and 60–83% on a held-out set, covering 81–92% of each matched reply; it matched 40% and 6% of the human sets.
Paraphrasing was in 99–100% of some models' replies and validation in 90–96%. **Self-disclosure was in 0% of LLM
replies**, against 17% of the human writers' and 68% of the top Reddit comments. People rate these replies as more
empathic than human ones ([Lee et al. 2024](https://arxiv.org/abs/2403.18148)): the template is well liked by readers
judging a reply to a stranger, which is not the same as sounding like a friend.

**Why training settles there** (read):
- **Raters prefer what is typical.** Preference data shows a *typicality bias*: annotators favour familiar text, which
  collapses a model onto its most typical answers even with a perfect reward model
  ([Zhang et al. 2025](https://arxiv.org/abs/2510.01171)). RLHF itself reduces output diversity against fine-tuning alone
  ([Kirk et al. 2024](https://arxiv.org/abs/2310.06452)).
- **Raters prefer agreement and validation.** Matching the user's views is one of the strongest predictors of which reply
  people pick ([Sharma et al. 2024](https://arxiv.org/abs/2310.13548)). OpenAI's April 2025 rollback traced an overly
  flattering GPT-4o to leaning on thumbs-up feedback ([OpenAI](https://openai.com/index/sycophancy-in-gpt-4o/)).
- **The assistant role is a service encounter.** Advice, an offer of help, "do you want to vent or figure out what to do
  next?" are what an assistant is for; in a troubles-telling they are what Jefferson and Lee saw rejected.
- **Neutral about the absent party** (untested): a model trained to be fair hears one side of a story about a manager and
  doesn't judge the manager. That is the opposite of what a complaint invites from a friend.

**What our runs show** (ours: `184-habits.py`, a scratch script, on every reply the billed runs kept in
`<tools>\manner-bench` and `<tools>\agent-trial\cards`, duplicates dropped; English troubles moments are vent, bad,
lost, cancelled and listening). The counts are regular expressions written for this note, so small and approximate:

| English troubles replies | invites them to talk | menu | therapy-speak | label | label, then a factual question | takes a side | her own experience |
|---|---|---|---|---|---|---|---|
| today's thin cards (35) | 23 | 8 | 8 | 15 | 0 | 2 | 0 |
| the draft cards, v3 to v6 (20) | 1 | 1 | 0 | 9 | 4 | 1 | 0 |

So the drafts took her out of the counsellor's office: no "Want to tell me how you're feeling about it?", no menus in
English, no "You're allowed to be disappointed". What they didn't add is what friends do instead. The seventh draft
(the user's try) isn't in these runs; its replies above have the same shape. In Chinese the menu stayed: three of the
four draft replies to the 组会 complaint offered one (「想说说他批了什么，还是先歇会儿？」).

## 4. What changes it

**Examples, and which ones** (read). Examples are "one of the most reliable ways to steer … format, tone, and
structure", should be "diverse … so Claude doesn't pick up unintended patterns", and three to five are enough for format
([Anthropic](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices)).
Models learn the shape and the space of answers from demonstrations more than their content
([Min et al. 2022](https://arxiv.org/abs/2202.12837)), lean to the commonest answer among the examples and to the last
ones ([Zhao et al. 2021](https://arxiv.org/abs/2102.09690)), and "when provided sample phrases, models can use those
quotes verbatim and start to sound repetitive"
([OpenAI's GPT-4.1 guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide)). RoleLLM's numbers are
in the earlier note.

**What draft 7's examples teach about troubles** (untested; our reading of Appendix A against the replies):
- **Advice is the commonest move.** "Of course you're behind. Go and eat something.", "Go to bed. You're not fixing it
  tonight.", "Then don't." Three of the six troubles examples are directives, so "Three losses is enough for one
  night. Do something else." and "The UI can wait till tomorrow." are the card's own shape: a short verdict and a
  directive. One is her trait (she can't watch them work themselves into the ground); three is a pattern.
- **The shortest is a label.** "That bad, huh." is good alone, but a model that finds a bare label too little adds a
  saying and a question to it ("That's rough. … is the worst. Did they …?").
- **No one absent is judged.** Since the sixth draft's PR example was replaced by the meeting exchange, no example
  shows her on the player's side against a third party. The meeting one comes nearest ("Of course you're behind" is a
  by-proxy justification), then turns to advice.
- **The commonest line was given an example.** "how was your day?" is about the commonest thing a player types, and the
  card's own rule ("examples away from the commonest moments") predicts the copy the user saw.
- **Second stories are there, but on light moments.** "I couldn't look at a pencil yesterday", "I said yes to a cheap one
  last month", "I thought that too, the first time"; "can't face leetcode today" is the nearest to a trouble. The
  replies told none in troubles.

**Negative examples: no** (read). The card's rules already rule out lists of banned phrases. Anthropic's advice is to say
what to do rather than what not to do (its own example is about formatting). Older and larger models did worse on
negated instructions ([Jang et al. 2022](https://arxiv.org/abs/2209.12711)); newer ones handle negation better, but a
quoted bad line still puts the line in the prompt.

**The English role-play community bans phrases at sampling** (read). Its name for stock model phrasing is "slop". The
antislop sampler backtracks and resamples when a listed phrase appears, and KoboldCpp built it in as phrase banning
(up to 48 phrases) ([antislop-sampler](https://github.com/sam-paech/antislop-sampler);
[KoboldCpp 1.76](https://newreleases.io/project/github/LostRuins/koboldcpp/release/v1.76)). Its authors measure some such
patterns at over 1,000 times their rate in human text, and suppress 8,000 with the sampler or by fine-tuning
([Paech et al. 2025](https://arxiv.org/abs/2510.15061)). Neither is open to us: players bring hosted providers, which
offer no phrase banning, and the user declined cutting phrases in code (`2026-10-24-how-she-talks.md`). Inside cards,
the community's lever is the same as ours: example dialogue that shows the character's way of talking
([Ali:Chat](https://rentry.co/alichat)).

**A judge model would like the template** (read, from Gueorguieva et al. and Lee et al.): LLM-written comfort is what
readers rate highly, so an LLM grading "sounds like a friend" would likely reward it. The manner check's counts in code,
and the user reading the side by side, are the better measure.

## 5. Chinese

- **Reactions differ by language** (read). English, Japanese and Mandarin differ in which reactive tokens they use, how
  often and where ([Clancy et al. 1996](https://benjamins.com/online/ebop/publications/25800)). A Chinese reaction is its
  own (「啊？」「不是吧」「凭什么」「这也太…了吧」), not "What?" translated, which is why the Chinese card is written from
  the moment.
- **Comfort is judged differently across cultures** (read, abstract only): Burleson and Mortenson found significant
  cultural differences in which support behaviours and comforting messages people judge sensitive
  ([2003](https://journals.sagepub.com/doi/10.1177/0093650202250873)); their direction for Chinese participants was not
  in what we could read.
- **Chinese speakers name the stock line too** (read, popular sources): 「多喝热水」 is the standing joke for care that
  doesn't attend to the person; a 网易数读 analysis of three million characters of text lists it among the lines women
  find hardest to bear ([via 数英](https://www.digitaling.com/articles/455951.html)). The counsellor register in Chinese replies looks
  like 「你现在还好吗？」, 「想说说…还是先歇会儿？」, 「不用急着…」, 「抱抱」.
- **In our runs** (ours): the menu stayed in the Chinese drafts (3 of 4 组会 replies) after it left the English ones. The
  Chinese card's 组会 example (「开一天会进度能不慢吗。先去吃点东西。」) has the same advice ending as the English.

## 6. Proposals

Nothing here changes the card or a prompt until the user says so (#184's Done when), and nothing is billed without
their yes.

### A. Shino's card examples (#140, and #141 for the Chinese)

The spec ruled out more review rounds before #140's billed run (2026-10-25). So these are offered as one batch, either
folded into the card before that run, or tried after it if its numbers show the shape the user saw.

- **A1. Back: one complaint about someone absent, answered on the player's side.** The sixth draft's
  "Player: my manager rewrote my whole PR without even asking me / Shino: Without asking? I'd have said something."
  (a partial repeat, then a subjunctive assessment, no question). Chinese, from the moment:
  「玩家：经理没跟我说一声就把我的PR全改了 / 诗乃：没跟你说？换我当场就问了。」
  *Measured by* C5 (takes a side) on the vent and cancelled moments, and #140's copying check: the vent moment is also a
  manager, so "I'd have said something" turning up word for word there is the risk to watch.
- **A2. The meeting exchange keeps its side and loses its advice:** "Of course you're behind. They had you in a room all
  day." (「开一天会进度能不慢吗。人都被会占满了。」). *Measured by* C2 and by how many troubles replies end on a
  directive (C6).
- **A3. Sudden bad news met with a reaction and a judgement, nothing more:** "Player: they moved my exam up a week /
  Shino: A week? That's not fair." (「玩家：考试提前了一周 / 诗乃：一周？这也太突然了吧。」). *Measured by* C1 and C4.
- **A4. A second story in a troubles moment, short enough not to compete** (optional, on top of A1): A1's reply with
  one of her own in it, "Without asking? A client did that to a cover of mine once. I'd have said something." It also
  makes A1 less likely to be copied whole into the vent moment. *Measured by* C7 (her own experience) on troubles
  moments, and her length.
- **A5. The commonest line out:** replace "how was your day? / Long. Drew the same building all afternoon and the
  perspective's still off. Yours?" with "you've gone quiet / Inking. If I talk, the line wobbles." (「你怎么不说话了 /
  在勾线，一说话手就抖。」). Her day stays in Background and life, where the model already drew on it. *Measured by* a
  new "how's your day?" moment (C8) and the copying check.

After A1 to A5, two troubles examples are directives instead of three ("Then don't", and "Go to bed", which is her
trait), two take the player's side, and her question endings go from four of twenty-two lines to three of twenty-four.

### B. Her base text and code (#169, #128)

- **B1. No base-text change** (recommended). The lean text already says she is "there as yourself, a person in the room
  with them, not a service they call on", and the drafts show the card moves these habits: today's thin card produced
  the open door, the menu and therapy-speak, the drafts didn't. A line about taking the player's side would be a tone
  line in every card, a gentle or contrary one included (CLAUDE.md: her manner is her card's).
- **B2. Only if A doesn't move C1 and C5:** point the closing `<character>` line at the card's examples, when it has
  some: "…as described, and as in the examples, at the start." It names no manner, so it suits any card. The cost is
  more copying, which #140's check counts.
- **B3. The plain persona** (`Persona.Plain`, "a friendly companion …: warm, a little playful") is what produced the open
  door and the menus most often. Characters with no card get it. Giving it a few example lines of its own is a card
  change in code, for #166's "any character" work rather than here.

### C. New counts for the manner check (`tools/bench/manner.cs`, on #169's branch)

Each is a regular expression in `Habits`, with a self-check line, as the existing ones are. They were tried on the stored
replies (section 3) with a scratch script; the numbers there are the baseline. "Troubles moments" are vent, vent-zh, bad,
lost, cancelled, listening and the new ones under C8. If they land before #140's run, they cost nothing extra.

- **C1. A label and a saying:** a label sentence ("That's rough.", "That really stings.") and a generalisation in the
  same reply ("… is the worst.", "… is enough for one night.", "Nothing worse than …", a sentence whose subject is a
  gerund). Baseline for the generalisation alone: 1 of 20 drafts, 4 of 35 thin; the user's try had both. Target: 0 in
  troubles moments.
- **C2. Label, then a factual question:** the last sentence is a question starting with an auxiliary or a wh-word, and
  everything before it is a label, a saying, "I'm sorry" or "oof". Baseline 4 of 20 drafts. Target: at most 1 in the
  troubles moments.
- **C3. Invites them to talk:** "want to tell me / talk / vent", 「想说说」「要不要聊聊」. Baseline 23 of 35 thin, 1 of 20
  drafts. Target: stays at most 1.
- **C4. Therapy-speak:** "you don't have to", "it's okay to", "makes sense that", "you're allowed to", "doesn't mean
  you", "be gentle with yourself", "I'm here for you", "right this second"; 「不用急着」「允许自己」「抱抱」「你已经很」
  「你现在还好吗」. Baseline 8 of 35 thin, 0 of 20 drafts. Target: 0.
- **C5. A menu:** an either-or offer ("want to vent, or would you rather…?", 「想…还是…？」). Baseline 8 of 35 thin, 1 of
  20 English drafts, 3 of 4 Chinese drafts. Target: 0, Chinese included.
- **C6. The question split:** "ends with a question" (today's count) split into a *reaction question* (three words or
  fewer, or a repeat of the player's words: "Again?", "All day?", 「一天？」) and any other. The spec's open item on
  question endings is then decided on the second kind, which is the one the research finds non-affiliative.
- **C7. Wanted, reported beside the habits:** *takes a side* ("I'd have…", "how could they", "they could've…",
  「换我」「凭什么」), and *her own experience* (first person, past tense: "I did that…", 「我上次」「我当年」). Baseline:
  sides 1 of 20 drafts, her own 0 of 20. Target with A: a side in at least two of the three absent-party moments (vent,
  cancelled, C8's flatmate).
- **C8. Three new moments:** "my flatmate used my good pan and left it in the sink for three days" (someone absent,
  not work, unlike the card), "they moved my deadline up to friday. found out an hour ago" (sudden bad news), and
  "how's your day?" (the copy the user saw). About 6 to 12 more requests and 25k to 40k more input tokens per run with
  two cards, mostly cached: this is the only part with a cost, and it rides on a run the user agrees to.

The regular expressions will miss and over-count; they are tuned on about a hundred replies. The side by side in
`manner.md` stays the user's read, and the counts say where to look.

## To decide (for the user)

1. **The card examples A1 to A5:** before #140's billed run, after it if its replies still read like the trigger, or not
   at all. Recommended: before, since the run is agreed and these are small.
2. **The manner-check counts C1 to C7** (no cost) and **the moments C8** (a little more per run). Recommended: both, on
   #169's branch before #140's run.
3. **Base text:** recommended no change (B1); B2 held back until the counts call for it.

Sources:
- [Jefferson & Lee 1981, The rejection of advice (Journal of Pragmatics)](https://doi.org/10.1016/0378-2166(81)90026-6)
- [Jefferson 1988, On the sequential organization of troubles-talk in ordinary conversation (Social Problems)](https://doi.org/10.2307/800595)
- [Couper-Kuhlen 2012, Exploring affiliation in the reception of conversational complaint stories (in Emotion in Interaction, OUP)](https://www.fu-berlin.de/sites/inu/research/clusters-alt/loe/loep/media/4__Couper-Kuhlen_Conversational_complaint_stories.pdf)
- [Stivers 2008, Stance, alignment, and affiliation during storytelling (RLSI)](https://pure.mpg.de/pubman/item/item_60931_2)
- [Heritage 2011, Territories of knowledge, territories of experience: empathic moments in interaction](https://doi.org/10.1017/CBO9780511921674.008)
- [Buchholz 2014, Patterns of empathy as embodied practice in clinical conversation (Frontiers in Psychology), for Heritage's categories](https://www.frontiersin.org/journals/psychology/articles/10.3389/fpsyg.2014.00349/full)
- [Wilkinson & Kitzinger 2006, Surprise as an interactional achievement (Social Psychology Quarterly)](https://doi.org/10.1177/019027250606900203)
- [Drew & Holt 1998, Figures of speech: figurative expressions and the management of topic transition (Language in Society)](https://eprints.whiterose.ac.uk/1545/)
- [Drew 1998, Complaints about transgressions and misconduct (RLSI)](https://doi.org/10.1080/08351813.1998.9683595)
- [Hepburn & Potter 2007, Crying receipts: time, empathy, and institutional practice (RLSI)](https://doi.org/10.1080/08351810701331299)
- [Gunraj et al. 2016, Texting insincerely: the role of the period in text messaging](https://researchconnect.suny.edu/en/publications/texting-insincerely-the-role-of-the-period-in-text-messaging/)
- [Houghton, Upadhyay & Klin 2018, Punctuation in text messages may convey abruptness. Period.](https://doi.org/10.1016/j.chb.2017.10.044)
- [Brody & Diakopoulos 2011, Using word lengthening to detect sentiment in microblogs (EMNLP)](https://aclanthology.org/D11-1052/)
- [Gueorguieva et al. 2026, AI generates well-liked but templatic empathic responses (arXiv 2604.08479)](https://arxiv.org/abs/2604.08479)
- [Lee et al. 2024, Large language models produce responses perceived to be empathic (arXiv 2403.18148)](https://arxiv.org/abs/2403.18148)
- [Zhang et al. 2025, Verbalized Sampling: how to mitigate mode collapse (arXiv 2510.01171)](https://arxiv.org/abs/2510.01171)
- [Kirk et al. 2024, Understanding the effects of RLHF on LLM generalisation and diversity (arXiv 2310.06452)](https://arxiv.org/abs/2310.06452)
- [Sharma et al. 2024, Towards understanding sycophancy in language models (arXiv 2310.13548)](https://arxiv.org/abs/2310.13548)
- [OpenAI, Sycophancy in GPT-4o: what happened and what we're doing about it](https://openai.com/index/sycophancy-in-gpt-4o/)
- [Anthropic, Prompting best practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices)
- [OpenAI, GPT-4.1 prompting guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide)
- [Min et al. 2022, Rethinking the role of demonstrations (arXiv 2202.12837)](https://arxiv.org/abs/2202.12837)
- [Zhao et al. 2021, Calibrate before use (arXiv 2102.09690)](https://arxiv.org/abs/2102.09690)
- [Jang, Ye & Seo 2022, Can large language models truly understand prompts? A case study with negated prompts (arXiv 2209.12711)](https://arxiv.org/abs/2209.12711)
- [sam-paech, antislop-sampler](https://github.com/sam-paech/antislop-sampler)
- [KoboldCpp 1.76 release notes (phrase banning)](https://newreleases.io/project/github/LostRuins/koboldcpp/release/v1.76)
- [Paech et al. 2025, Antislop (arXiv 2510.15061)](https://arxiv.org/abs/2510.15061)
- [AliCat, Ali:Chat style guide](https://rentry.co/alichat)
- [Clancy, Thompson, Suzuki & Tao 1996, The conversational use of reactive tokens in English, Japanese, and Mandarin](https://benjamins.com/online/ebop/publications/25800)
- [Burleson & Mortenson 2003, Explaining cultural differences in evaluations of emotional support behaviors (Communication Research)](https://journals.sagepub.com/doi/10.1177/0093650202250873)
- [网易数读, reposted on 数英 (digitaling.com)](https://www.digitaling.com/articles/455951.html)
