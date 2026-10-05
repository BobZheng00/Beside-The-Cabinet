using System.Text;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>
/// One character's side of the never-ending conversation with the player: answers what the player says (streamed,
/// with stage directions split off), logs what the cabinet does and what she says unprompted, and folds the
/// older half of the window into a summary when it fills up: while the conversation is quiet if it can (a cold cache
/// costs nothing extra then), at once if it must (auto-compaction; see docs/specs/2026-10-04-companion-memory-v2.md).
/// </summary>
public sealed class Conversation(MindStore store, string character, Persona persona, TimeProvider? clock = null)
{
    /// <summary>How long a conversation stays quiet before upkeep reads it, and how much it reads at once (tokens).</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);
    /// <summary>
    /// How old moments get before they are rolled up, the oldest first: days into weeks after two weeks, days and weeks
    /// into months after three months, and anything shorter into years after two years. With at most one moment a day,
    /// that keeps her moments to a couple of dozen recent ones plus one per older month and year.
    /// </summary>
    public static readonly TimeSpan WeeksAfter = TimeSpan.FromDays(14), MonthsAfter = TimeSpan.FromDays(90), YearsAfter = TimeSpan.FromDays(730);
    /// <summary>Facts upkeep reconciles a stretch of conversation against at most (the related ones, see <see cref="Recall.Related"/>).</summary>
    public const int DigestFacts = 60;
    /// <summary>Moments a day may have (one per distinct thing that happened), and how long each may be.</summary>
    public const int DayMoments = 3, MomentWords = 60;

    /// <summary>How long a rolled-up moment may be: a week 80 words, a month 100, a year 150.</summary>
    public static int RolledUpWords(Span span) => span switch { Span.Week => 80, Span.Month => 100, _ => 150 };
    public const int DigestAtOnce = 3000;
    private const int KeepLines = 12, SummaryLimit = 3200, NoteImportance = 7, CoreFrom = 6, CoreFacts = 40, GiveUpAfter = 3, LoreBudget = 500;
    private static readonly ChatOptions ReplyOptions = new() { MaxOutputTokens = 600 };     // room for models that think first
    private static readonly ChatOptions FoldOptions = new() { MaxOutputTokens = 1500 };
    private static readonly ChatOptions DigestOptions = new() { MaxOutputTokens = 2000 };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private (long From, int Times) _failed;

    public string Character => character;
    public Persona Persona => persona;

    /// <summary>The local model for search by meaning, when one is loaded; without it she recalls by shared words.</summary>
    public LocalEmbedder? Embedder { get; set; }

    /// <summary>The language her voice speaks while she has one that can talk; null leaves voice out of the prompt.</summary>
    public string? VoiceLanguage { get; set; }

    /// <summary>How much of the conversation is kept word for word, in tokens (see <see cref="Prompt.DefaultWindow"/>).</summary>
    public int Window { get; set; } = Prompt.DefaultWindow;

    private History? _history;

    /// <summary>The player's "remind me before things on my agenda" (null: only the reminders asked for).</summary>
    public TimeSpan? ReminderLead { get; set; }

    /// <summary>The whole conversation, searchable.</summary>
    public History History => _history ??= new History(store, character, persona.Name);

    /// <summary>
    /// Streams her answer to <paramref name="said"/>: shown text through <paramref name="text"/>, stage directions
    /// through <paramref name="direction"/>. Both lines are logged; a failed request leaves only the player's.
    /// </summary>
    /// <returns>Everything she said.</returns>
    public Task<string> Answer(IChatClient client, string said, string situation, Action<string> text, Action<string, string?> direction,
                               CancellationToken cancel = default) =>
        Reply(client, said, fromCabinet: false, situation, text, direction, cancel);

    /// <summary>
    /// Something the cabinet asks her to tell the player in her own words (a reminder going off, see
    /// <see cref="Agenda.Announcement"/>): answered as a message would be, but logged as the cabinet's line.
    /// </summary>
    public Task<string> Announce(IChatClient client, string note, string situation, Action<string> text, Action<string, string?> direction,
                                 CancellationToken cancel = default) =>
        Reply(client, note, fromCabinet: true, situation, text, direction, cancel);

    private async Task<string> Reply(IChatClient client, string said, bool fromCabinet, string situation, Action<string> text,
                                     Action<string, string?> direction, CancellationToken cancel)
    {
        var now = _clock.GetLocalNow();
        var summary = store.SummaryOf(character);
        var folded = summary?.Through ?? 0;
        // what she remembers: facts, episodes, and the conversation before the window, searched as a whole
        var meaning = await MeaningOf(said, cancel);
        History.CatchUp(folded);
        var excerpts = History.Search(said, folded, meaning?.Query, meaning?.Model);
        var memories = Recall.Find(said, store.Facts(character), store.Episodes(character).Where(e => e.Last <= folded).ToList(),
                                   excerpts, persona.Name, now, meaning);
        store.Recalled(memories.Where(m => m.FactId is not null).Select(m => m.FactId!.Value), now);
        var recent = store.After(character, folded);
        // a character card's lorebook: background whose keys came up just now (or in the last couple of lines)
        var lore = new List<Memory>();
        var loreUsed = 0;
        foreach (var entry in persona.LoreFor(string.Join("\n", recent.TakeLast(2).Select(l => l.Text).Append(said))))
            if ((loreUsed += Prompt.Tokens(entry.Content)) <= LoreBudget)
                lore.Add(new Memory(entry.Content));
        var messages = Prompt.Build(persona, store.Note(MindStore.Shared, MindStore.AboutUser), store.Note(character, MindStore.Us),
                                    summary, recent, situation, fromCabinet ? $"[cabinet] {said.Trim()}" : said, [.. lore, .. memories],
                                    VoiceLanguage, Window, now, Agenda.Block(store.Agenda(), now, ReminderLead));
        store.Add(character, fromCabinet ? Speaker.Cabinet : Speaker.User, said.Trim(), now);

        // "[note: ...]": the player told her something to keep; it is kept now, not at the next upkeep.
        // "[plan: ...]" and "[remind: ...]": something ahead with a date, on the agenda at once
        var stage = new StageDirections((name, payload) =>
        {
            if (name == "note" && payload is { Length: > 2 } && !store.Facts(character).Any(f => Recall.Same(f.Text, payload)))
                store.AddFact(MindStore.Shared, payload, NoteImportance, now);
            if (name is "plan" or "remind" && payload is not null && Agenda.ParseDirection(payload, name == "remind", now) is { } plan)
                AddPlan(plan, now);
            direction(name, payload);
        });
        var reply = new StringBuilder();
        void Show(string piece)
        {
            if (piece.Length == 0)
                return;
            reply.Append(piece);
            text(piece);
        }
        await foreach (var update in client.GetStreamingResponseAsync(messages, ReplyOptions, cancel))
            Show(stage.Feed(update.Text));
        Show(stage.Finish());

        var answer = reply.ToString().Trim();
        if (answer.Length > 0)
            store.Add(character, Speaker.Her, answer, _clock.GetLocalNow());
        return answer;
    }

    /// <summary>
    /// Puts something on the agenda. When it is there already (on the same day, saying the same thing, or at the same
    /// time about the same thing) it only adds what is new: a reminder, a repeat. A reminder for something wholly
    /// past is not set. Returns the item's id, or null.
    /// </summary>
    private long? AddPlan(PlanSpec spec, DateTimeOffset now)
    {
        var wanted = new Plan(0, spec.Text, spec.At, spec.AllDay, spec.Before, null, now, character, spec.Repeat);
        if (wanted.RemindAt() is { } at && at <= now && wanted.End <= now && spec.Repeat == Repeat.Once)
            return null;
        var words = Recall.Terms(spec.Text).ToHashSet();
        if (store.Agenda().FirstOrDefault(p => p.At.Date == spec.At.Date
                                               && (Recall.Same(p.Text, spec.Text) || p.At == spec.At && Recall.Terms(p.Text).Any(words.Contains))) is { } same)
        {
            var before = spec.Before ?? same.Before;
            var repeat = spec.Repeat != Repeat.Once ? spec.Repeat : same.Repeat;
            if (before != same.Before || repeat != same.Repeat)
                store.UpdatePlan(same.Id, same.Text, same.At, same.AllDay, before, repeat);
            return same.Id;
        }
        return store.AddPlan(spec.Text, spec.At, spec.AllDay, spec.Before, now, character, spec.Repeat);
    }

    /// <summary>When the player or she last said something (the cabinet's lines aside), or null.</summary>
    public DateTimeOffset? LastTalked =>
        store.Through(character, long.MaxValue, 40).Where(l => l.Speaker != Speaker.Cabinet).Select(l => (DateTimeOffset?)l.At).Max();

    /// <summary>Something on the agenda she may ask about now, over for at least <paramref name="after"/> (see <see cref="Agenda.ToAskAbout"/>), or null.</summary>
    public Plan? ToAskAbout(TimeSpan after) => Agenda.ToAskAbout(store.Agenda(), _clock.GetLocalNow(), LastTalked, after).FirstOrDefault();

    /// <summary>Something the cabinet did, for her to know about later.</summary>
    public void Witness(string what) => store.Add(character, Speaker.Cabinet, what, _clock.GetLocalNow());

    /// <summary>Something she said on her own (a canned line about an event).</summary>
    public void Said(string line) => store.Add(character, Speaker.Her, line, _clock.GetLocalNow());

    /// <summary>The window is full: fold it now, even mid-conversation.</summary>
    public bool NeedsFolding => Prompt.Tokens(Unfolded()) > Window;

    /// <summary>The window is three quarters full: fold it the next time the conversation is quiet.</summary>
    public bool NeedsQuietFold => Prompt.Tokens(Unfolded()) > Window * 3 / 4;

    /// <summary>Nothing has been said or has happened for <paramref name="quiet"/> (or ever).</summary>
    public bool IsQuiet(TimeSpan quiet) => store.Through(character, long.MaxValue, 1) is not [var last] || _clock.GetLocalNow() - last.At >= quiet;

    /// <summary>
    /// Folds the window's older part into the summary, keeping its newest half (and at least a dozen lines) word for
    /// word. Returns false when there was nothing to fold or the model gave nothing back (the lines stay, and the next
    /// attempt includes them).
    /// </summary>
    /// <param name="quiet">Folding while quiet: from three quarters full; otherwise only a full window is folded.</param>
    public async Task<bool> Fold(IChatClient client, bool quiet = false, CancellationToken cancel = default)
    {
        var summary = store.SummaryOf(character);
        var lines = store.After(character, summary?.Through ?? 0);
        if (Prompt.Tokens(lines) <= (quiet ? Window * 3 / 4 : Window))
            return false;

        var keep = 0;
        var kept = 0;
        while (keep < lines.Count && (keep < KeepLines || kept + Prompt.Tokens(lines[^(keep + 1)].Text) + 4 <= Window / 2))
            kept += Prompt.Tokens(lines[^++keep].Text) + 4;
        var fold = lines.Take(lines.Count - keep).ToList();
        if (fold.Count == 0)
            return false;

        var response = await client.GetResponseAsync(Prompt.Fold(persona, summary, fold), FoldOptions, cancel);
        var folded = response.Text.Trim();
        if (folded.Length == 0)
            return false;
        store.Fold(character, folded.Length > SummaryLimit ? folded[..SummaryLimit] : folded, fold[^1].Id, _clock.GetLocalNow());
        return true;
    }

    /// <summary>What the player said and her stored facts and episodes as vectors, or null without a model (or if it fails).</summary>
    private async Task<Meaning?> MeaningOf(string said, CancellationToken cancel)
    {
        if (Embedder is not { } embedder)
            return null;
        try
        {
            var query = await Task.Run(() => embedder.Embed(said, query: true), cancel);
            return new Meaning(query, embedder.Model, store.Vectors(MindStore.FactKind, embedder.Model.Name),
                               store.Vectors(MindStore.EpisodeKind, embedder.Model.Name));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;                    // the model being swapped, say: shared words will do for this once
        }
    }

    /// <summary>
    /// Gives facts and episodes without a vector from the loaded model one, a batch at a time (the model runs off the
    /// calling thread; the store is written on it). Returns how many were done.
    /// </summary>
    public async Task<int> Index(int batch = 64, CancellationToken cancel = default)
    {
        if (Embedder is not { } embedder)
            return 0;
        History.CatchUp(store.SummaryOf(character)?.Through ?? 0);       // passages for what has left the window
        var model = embedder.Model.Name;
        var haveFacts = store.Vectors(MindStore.FactKind, model);
        var haveEpisodes = store.Vectors(MindStore.EpisodeKind, model);
        var todo = store.Facts(character).Where(f => !haveFacts.ContainsKey(f.Id)).Select(f => (Kind: MindStore.FactKind, f.Id, f.Text))
            .Concat(store.Episodes(character).Where(e => !haveEpisodes.ContainsKey(e.Id)).Select(e => (Kind: MindStore.EpisodeKind, e.Id, e.Text)))
            .Concat(History.Unread(model).Select(p => (Kind: MindStore.PassageKind, p.Id, p.Text)))       // the old conversation, a batch at a time
            .Take(batch).ToList();
        if (todo.Count == 0)
            return 0;
        var vectors = await Task.Run(() => todo.Select(t => embedder.Embed(t.Text, query: false)).ToList(), cancel);
        for (var i = 0; i < todo.Count; i++)
        {
            store.SetVector(todo[i].Kind, todo[i].Id, model, vectors[i]);
            if (todo[i].Kind == MindStore.PassageKind)
                History.Learned(model, todo[i].Id, vectors[i]);
        }
        return todo.Count;
    }

    /// <summary>The player has said something upkeep has not read yet, and the conversation has been quiet for <paramref name="quiet"/>.</summary>
    public bool NeedsDigest(TimeSpan quiet)
    {
        var lines = store.After(character, store.Digested(character));
        return lines.Any(l => l.Speaker == Speaker.User) && _clock.GetLocalNow() - lines[^1].At >= quiet;
    }

    /// <summary>
    /// Upkeep: reads the next stretch of conversation not yet digested, files a dated episode, adds, updates and
    /// deletes facts, and rewrites the always-present notes when something important about them changed. Returns
    /// false when there was nothing to read or the answer could not be used (it is tried again next time, and a
    /// stretch that fails three times is skipped so it cannot block everything after it).
    /// </summary>
    public async Task<bool> Digest(IChatClient client, CancellationToken cancel = default)
    {
        var from = store.Digested(character);
        var pending = store.After(character, from);
        if (pending.Count == 0)
            return false;
        var chunk = new List<Line>();
        var used = 0;
        foreach (var line in pending)
        {
            if (chunk.Count > 0 && used + Prompt.Tokens(line.Text) > DigestAtOnce)
                break;
            chunk.Add(line);
            used += Prompt.Tokens(line.Text) + 4;
        }

        // what she knows that relates to this stretch, not everything: the request stays the same size however much she knows
        var all = store.Facts(character);
        var transcript = string.Join('\n', chunk.Select(l => l.Text));
        var known = all.Count <= DigestFacts ? all : Recall.Related(transcript, all, DigestFacts, await MeaningOf(transcript[..Math.Min(transcript.Length, 2000)], cancel));
        // a few moments a day at most: a later stretch rewrites the moment it carries on, or adds one if it is something else
        var today = store.Episodes(character).Where(e => e.Span == Span.Day && e.At.Date == chunk[0].At.Date).ToList();
        var agenda = store.Agenda();
        var response = await client.GetResponseAsync(Upkeep.Request(persona, known, chunk, today, agenda), DigestOptions, cancel);
        if (Upkeep.Parse(response.Text) is not { } digestion)
        {
            _failed = _failed.From == from ? (from, _failed.Times + 1) : (from, 1);
            if (_failed.Times >= GiveUpAfter)
                store.SetDigested(character, chunk[^1].Id);
            return false;
        }

        var now = _clock.GetLocalNow();
        var byId = known.ToDictionary(f => f.Id);
        var touched = new HashSet<string>();                // scopes whose important facts changed
        foreach (var change in digestion.Changes)
        {
            switch (change.Op)
            {
                // something she had retired has come up again: back to the foreground, not a second copy
                case "add" when change.Text is { Length: > 2 } text && store.Retired(character).FirstOrDefault(f => Recall.Same(f.Text, text)) is { } back:
                    store.Restore(back.Id, now);
                    if (back.Importance >= CoreFrom)
                        touched.Add(back.Scope);
                    break;
                case "add" when change.Text is { Length: > 2 } text && !store.Facts(character).Any(f => Recall.Same(f.Text, text)):
                    var scope = change.About == "us" ? character : MindStore.Shared;
                    var importance = change.Importance ?? 5;
                    store.AddFact(scope, text, importance, now);
                    if (importance >= CoreFrom)
                        touched.Add(scope);
                    break;
                case "update" when change.Id is { } id && byId.TryGetValue(id, out var old) && !old.Pinned && change.Text is { Length: > 2 } text:
                    store.UpdateFact(id, text, change.Importance);
                    if (Math.Max(old.Importance, change.Importance ?? 0) >= CoreFrom)
                        touched.Add(old.Scope);
                    break;
                case "delete" when change.Id is { } id && byId.TryGetValue(id, out var gone) && !gone.Pinned:
                    store.DeleteFact(id);
                    if (gone.Importance >= CoreFrom)
                        touched.Add(gone.Scope);
                    break;
            }
        }
        // things ahead with a date: on the agenda (relative dates were made absolute from the lines' timestamps)
        var plans = agenda.ToDictionary(p => p.Id);
        foreach (var change in digestion.Plans)
        {
            var when = change.When is { } w ? Agenda.ParseWhen(w, now) : null;
            switch (change.Op)
            {
                case "add" when when is { } at && change.Text is { Length: > 1 } text:
                    var (before, repeat) = Agenda.ParseOptions(change.Options);
                    AddPlan(new PlanSpec(at.At, at.AllDay, text, before ?? (change.Remind ? TimeSpan.Zero : null), repeat), now);
                    break;
                case "update" when change.Id is { } id && plans.TryGetValue(id, out var plan):
                    var moved = when ?? (plan.At, plan.AllDay);
                    var (lead, again) = Agenda.ParseOptions(change.Options);
                    store.UpdatePlan(id, change.Text is { Length: > 1 } renamed ? renamed : plan.Text, moved.At, moved.AllDay,
                                     lead ?? (change.Remind ? TimeSpan.Zero : plan.Before), again != Repeat.Once ? again : plan.Repeat);
                    break;
                case "cancel" when change.Id is { } id && plans.ContainsKey(id):
                    store.DeletePlan(id);
                    break;
            }
        }
        if (digestion.Episode is { Length: > 0 } episode)
        {
            var continued = digestion.Continues is { } id ? today.FirstOrDefault(e => e.Id == id) : null;
            if (continued is null && today.Count >= DayMoments)
                continued = today[^1];                      // the day is full: the latest takes it in
            if (continued is not null)
                store.UpdateEpisode(continued.Id, episode, chunk[^1].Id);
            else
                store.AddEpisode(character, episode, chunk[0].Id, chunk[^1].Id, chunk[0].At);      // dated when it happened
        }
        store.SetDigested(character, chunk[^1].Id);

        foreach (var scope in touched)
            await RewriteNote(client, scope, cancel);
        return true;
    }

    /// <summary>
    /// Rolls old moments up (see <see cref="WeeksAfter"/>): days into weeks, then days and weeks into months, then
    /// anything shorter into years, one group per call, the oldest first. A group needs at least two moments; a lone
    /// one waits for the next, longer roll-up. Returns false when there was nothing to combine.
    /// </summary>
    public async Task<bool> Consolidate(IChatClient client, CancellationToken cancel = default)
    {
        var now = _clock.GetLocalNow();
        var episodes = store.Episodes(character);
        foreach (var (into, after, key, period) in RollUps)
        {
            var group = episodes.Where(e => e.Span < into && now - e.At > after).GroupBy(e => key(e.At))
                .Where(g => g.Count() >= 2).OrderBy(g => g.Key).FirstOrDefault();
            if (group is null)
                continue;
            var response = await client.GetResponseAsync(Upkeep.Combine(persona, period(group.Key), into, group), FoldOptions, cancel);
            var text = response.Text.Trim();
            if (text.Length == 0)
                return false;
            store.AddEpisode(character, text, group.Min(e => e.First), group.Max(e => e.Last), group.Min(e => e.At), into);
            foreach (var moment in group)
                store.DeleteEpisode(moment.Id);
            return true;
        }
        return false;
    }

    private static readonly (Span Into, TimeSpan After, Func<DateTimeOffset, DateOnly> Key, Func<DateOnly, string> Period)[] RollUps =
    [
        (Span.Week, WeeksAfter, MondayOf, monday => "the week starting " + monday.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)),
        (Span.Month, MonthsAfter, at => new DateOnly(at.Year, at.Month, 1), first => first.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)),
        (Span.Year, YearsAfter, at => new DateOnly(at.Year, 1, 1), first => first.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];

    /// <summary>How often her facts are reviewed (sooner when many new ones have come), and how many are read at once.</summary>
    public static readonly TimeSpan ReviewEvery = TimeSpan.FromDays(7);
    public const int ReviewAfterNew = 30, ReviewAtOnce = 80;

    /// <summary>
    /// Sleep-time review of what she knows (as ChatGPT has managed saved memories since 2025): about weekly, or once
    /// many new facts have come, the model reads her facts a batch at a time, from where the last batch stopped, and
    /// merges what says the same thing and retires what is obsolete (past plans, contradicted, trivia long unmentioned).
    /// Retired facts are kept, to restore; pinned ones are never touched. Returns false when no review was due.
    /// </summary>
    public async Task<bool> Review(IChatClient client, CancellationToken cancel = default)
    {
        var now = _clock.GetLocalNow();
        var key = $"review:{character}";
        // the mark: when the last review finished, how many facts there were then, and where a review under way has got to
        var (last, count, from) = store.Mark(key)?.Split('|') is [var a, var c, var f]
            ? (DateTimeOffset.Parse(a, System.Globalization.CultureInfo.InvariantCulture), int.Parse(c), long.Parse(f))
            : (DateTimeOffset.MinValue, 0, 0L);
        var facts = store.Facts(character);
        if (from == 0 && now - last < ReviewEvery && facts.Count - count < ReviewAfterNew)
            return false;
        var batch = facts.Where(x => x.Id > from).Take(ReviewAtOnce).ToList();
        if (batch.Count == 0)
        {
            store.SetMark(key, $"{now:O}|{facts.Count}|0");
            return false;
        }
        var response = await client.GetResponseAsync(Upkeep.Review(persona, batch, now), DigestOptions, cancel);
        if (Upkeep.ParseReview(response.Text) is not { } verdict)
            return false;                                   // tried again next time, from the same place

        var mine = batch.Where(x => !x.Pinned).ToDictionary(x => x.Id);
        var touched = new HashSet<string>();
        foreach (var merge in verdict.Merges.Where(m => m.Ids.Distinct().Count() == m.Ids.Count && m.Ids.All(mine.ContainsKey) && m.Ids.Select(id => mine[id].Scope).Distinct().Count() == 1))
        {
            var merged = merge.Ids.Select(id => mine[id]).ToList();
            var importance = merge.Importance ?? merged.Max(x => x.Importance);
            if (merged.Count == 1)
                store.UpdateFact(merged[0].Id, merge.Text, importance);
            else
            {
                var into = store.AddFact(merged[0].Scope, merge.Text, importance, merged.Min(x => x.Learned));
                var why = $"merged into \"{(merge.Text.Length > 60 ? merge.Text[..60] + "…" : merge.Text)}\"";
                foreach (var old in merged)
                    store.Retire(old.Id, why, now);
            }
            touched.UnionWith(merged.Where(x => Math.Max(x.Importance, importance) >= CoreFrom).Select(x => x.Scope));
            foreach (var id in merge.Ids)
                mine.Remove(id);
        }
        foreach (var retirement in verdict.Retirements.Where(r => mine.ContainsKey(r.Id)))
        {
            store.Retire(retirement.Id, retirement.Why, now);
            if (mine[retirement.Id].Importance >= CoreFrom)
                touched.Add(mine[retirement.Id].Scope);
        }
        var end = batch[^1].Id == facts[^1].Id;
        store.SetMark(key, end ? $"{now:O}|{store.Facts(character).Count}|0" : $"{(last == DateTimeOffset.MinValue ? now : last):O}|{count}|{batch[^1].Id}");
        foreach (var scope in touched)
            await RewriteNote(client, scope, cancel);
        return true;
    }

    public static DateOnly MondayOf(DateTimeOffset at)
    {
        var day = DateOnly.FromDateTime(at.DateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    /// <summary>Rewrites "about the player" (shared) or "you and me" from the important facts behind it.</summary>
    private async Task RewriteNote(IChatClient client, string scope, CancellationToken cancel)
    {
        var name = scope == MindStore.Shared ? MindStore.AboutUser : MindStore.Us;
        var facts = store.Facts(character).Where(f => f.Scope == scope && (f.Importance >= CoreFrom || f.Pinned))
            .OrderByDescending(f => f.Pinned).ThenByDescending(f => f.Importance).ThenByDescending(f => f.Learned).Take(CoreFacts).ToList();
        var subject = scope == MindStore.Shared ? "the player" : $"the player and {persona.Name}";
        var response = await client.GetResponseAsync(Upkeep.Rewrite(subject, store.Note(scope, name), facts), FoldOptions, cancel);
        var note = response.Text.Trim();
        if (note.Length > 0)
            store.SetNote(scope, name, note.Length > Prompt.NoteLimit ? note[..Prompt.NoteLimit] : note);
    }

    private IReadOnlyList<Line> Unfolded() => store.After(character, store.SummaryOf(character)?.Through ?? 0);
}
