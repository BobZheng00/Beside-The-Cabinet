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
    /// <summary>How long a conversation stays quiet before upkeep reads it, and how much of her notes it keeps facts from at once (tokens).</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);
    /// <summary>Facts upkeep reconciles her new notes against at most (the related ones, see <see cref="Recall.Related"/>).</summary>
    public const int DigestFacts = 60;
    public const int DigestAtOnce = 3000;

    /// <summary>Why a fact was retired when it stopped being true: it changed (followed by what it is now), or it ended. Recalled as what used to be.</summary>
    public const string Changed = "changed to:", Ended = "no longer true";
    private const int KeepLines = 12, NoteImportance = 7, GiveUpAfter = 3, LoreBudget = 500;

    /// <summary>A merged fact longer than this (about 30 words) joined different things into a paragraph, and isn't taken (M8).</summary>
    private const int MergedTokens = 45;
    // a ceiling, not a target: the prompt keeps her short, and a model that thinks first spends this on its thinking too
    // (a GPT model thought 1,522 tokens for a short story, 2026-10-11), so 600 could leave nothing for her words
    private static readonly ChatOptions ReplyOptions = new() { MaxOutputTokens = 2000 };
    private static readonly ChatOptions FoldOptions = new() { MaxOutputTokens = 1500 };
    private static readonly ChatOptions ObserveOptions = new() { MaxOutputTokens = 4000 }, CondenseOptions = new() { MaxOutputTokens = 4000 };      // room to think first
    private static readonly ChatOptions DigestOptions = new() { MaxOutputTokens = 2000 };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private (long From, int Times) _failed, _failedKeep;            // a stretch of lines, or of notes, upkeep couldn't read, and how often
    private int _digestAtOnce = DigestAtOnce, _reviewAtOnce = ReviewAtOnce;     // smaller after an answer too long to come back whole

    public string Character => character;
    public Persona Persona => persona;

    /// <summary>The local model for search by meaning, when one is loaded; without it she recalls by shared words.</summary>
    public LocalEmbedder? Embedder { get; set; }

    /// <summary>The language her voice speaks while she has one that can talk; null leaves voice out of the prompt.</summary>
    public string? VoiceLanguage { get; set; }

    /// <summary>How much of the conversation is kept word for word, in tokens (see <see cref="Prompt.DefaultWindow"/>).</summary>
    public int Window { get; set; } = Prompt.DefaultWindow;

    /// <summary>
    /// The model she runs on can't take pictures or PDFs: it turned one down. None is sent to it, and she is told she
    /// can't see what the player shows her. Set from what was learnt about the model, and by a reply that learns it.
    /// </summary>
    public bool Unseeing { get; set; }

    /// <summary>Her latest reply reached the provider's length limit and was cut short (see <see cref="ReplyOptions"/>).</summary>
    public bool CutShort { get; private set; }

    private History? _history;

    /// <summary>The player's "remind me before things on my agenda", as set now (null: only the reminders asked for).</summary>
    public Func<TimeSpan?> ReminderLead { get; init; } = () => null;

    /// <summary>The player chose "Keeps in touch", as set now: she may plan when to reach them while they are out (see <see cref="Later"/>).</summary>
    public Func<bool> KeepsInTouch { get; init; } = () => false;

    /// <summary>The whole conversation, searchable.</summary>
    public History History => _history ??= new History(store, character, persona.Name);

    /// <summary>
    /// Streams her answer to <paramref name="said"/>: shown text through <paramref name="text"/>, stage directions
    /// through <paramref name="direction"/>. Both lines are logged; a failed request leaves only the player's.
    /// </summary>
    /// <param name="tools">
    /// What she can do while she answers (see <see cref="Toolbox"/>). Each tool she uses is logged as a cabinet line
    /// between the player's line and hers.
    /// </param>
    /// <param name="handed">
    /// What the player hands her with it (see <see cref="Attachment"/>): she starts her reply with a [saw] line for each,
    /// kept in the player's line in its place.
    /// </param>
    /// <param name="aside">How they are talking this time (texting, see <see cref="Reaching.Texted"/>): sent with what they said, not logged with it.</param>
    /// <param name="texting">Her reply goes as a text, whole: what she said while her tools ran is left out (see <see cref="Reply"/>).</param>
    /// <returns>Everything she said.</returns>
    public Task<string> Answer(IChatClient client, string said, string situation, Action<string> text, Action<string, string?> direction,
                               CancellationToken cancel = default, Toolbox? tools = null, IReadOnlyList<Attachment>? handed = null, string? aside = null,
                               bool texting = false) =>
        Reply(client, said, fromCabinet: false, situation, text, direction, tools, cancel, handed ?? [], aside, texting);

    /// <summary>
    /// Something the cabinet asks her to tell the player in her own words (a reminder going off, see
    /// <see cref="Agenda.Announcement"/>): answered as a message would be, but logged as the cabinet's line.
    /// </summary>
    /// <param name="aside">How to put it this time (see <see cref="WordFirst.Called"/>): sent with the note, not logged with it.</param>
    public Task<string> Announce(IChatClient client, string note, string situation, Action<string> text, Action<string, string?> direction,
                                 CancellationToken cancel = default, Toolbox? tools = null, IReadOnlyList<Attachment>? handed = null, string? aside = null,
                                 bool texting = false) =>
        Reply(client, note, fromCabinet: true, situation, text, direction, tools, cancel, handed ?? [], aside, texting);

    /// <summary>
    /// Her call before something she brings up herself (see <see cref="WordFirst"/>): a few words of her own that only
    /// get the player's attention, asked for on their own, alongside the remark. Her mind sees the conversation, so the
    /// call is in their language, but nothing of this turn, so it can't give the remark away. Nothing of it is logged
    /// (see <see cref="CalledFirst"/>); it is kept as her last call (see <see cref="LastCall"/>).
    /// </summary>
    /// <returns>Her words, her voice's [say] and what her body does, cut to the first sentence; null when nothing came.</returns>
    public async Task<List<Piece>?> Call(IChatClient client, Toolbox? tools, CancellationToken cancel = default)
    {
        var now = _clock.GetLocalNow();
        var folded = store.Folded(character);
        var sent = Prompt.Build(persona, Known(MindStore.Shared), Known(character), Log.Left(store.Observations(character), folded),
                                store.After(character, folded), "", $"[cabinet] {WordFirst.Ask}", memories: null,
                                voiceLanguage: VoiceLanguage, window: Window, now: now, tools: tools?.Offered is not null, keepsInTouch: KeepsInTouch(), asking: tools?.AsksFirst, herself: Known(MindStore.Herself(character)));
        var written = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync(sent, ReplyOptions, cancel))
            written.Append(update.Text);
        var (call, _) = WordFirst.Split([.. WordFirst.Pieces(written.ToString())
            .Where(p => p.Words is not null || p.Direction == "say" || p.Direction is { } moment && Prompt.Moments.Contains(moment))]);
        // no blanks at either end, so that what it shows is what is kept
        var first = call.FindIndex(p => p.Words is not null);
        if (first >= 0)
            call[first] = new Piece(call[first].Words!.TrimStart());
        var last = call.FindLastIndex(p => p.Words is not null);
        if (last >= 0)
            call[last] = new Piece(call[last].Words!.TrimEnd());
        call.RemoveAll(p => p.Words is { Length: 0 });
        if (!call.Any(p => p.Words is not null || p.Direction == "say"))
            return null;
        store.SetMark(CallMark, WordFirst.Written(call));
        return call;
    }

    /// <summary>Her last call that came (see <see cref="Call"/>), for when asking for one fails: her own words, for her voice's language. Null before her first.</summary>
    public List<Piece>? LastCall => store.Mark(CallMark) is { Length: > 0 } written ? WordFirst.Pieces(written) : null;

    private string CallMark => $"call.{character}.{VoiceLanguage}";

    /// <summary>
    /// She called the player before her latest reply (see <see cref="Call"/>): her line starts with <paramref name="call"/>,
    /// as it was shown, and a cut through the two counts from its start (see <see cref="CutOff"/>).
    /// </summary>
    public void CalledFirst(string call)
    {
        if (_lastReply is not { } reply || call.Length == 0)
            return;
        _lastReply = (reply.Line, call + reply.Streamed);
        store.Rewrite(reply.Line, (call + reply.Streamed).Trim());
    }

    /// <remarks>
    /// The short lines she says before a tool fill its wait at the PC. A text goes only once it is whole, so there they
    /// read as a log of her tries ("That forecast page didn't load, so I'm trying…", 2026-10-11): a text keeps what she
    /// wrote after her last tool, and so does her line, as that is what the player read.
    /// </remarks>
    private async Task<string> Reply(IChatClient client, string said, bool fromCabinet, string situation, Action<string> text,
                                     Action<string, string?> direction, Toolbox? tools, CancellationToken cancel, IReadOnlyList<Attachment> handed,
                                     string? aside, bool texting = false)
    {
        var now = _clock.GetLocalNow();
        var folded = store.Folded(character);
        var log = Log.Left(store.Observations(character), folded);
        // what she remembers: facts and what used to be true, her notes out of view, and the conversation before the
        // window, searched as a whole
        var meaning = await MeaningOf(said, cancel);
        History.CatchUp(folded);
        var excerpts = History.Search(said, folded, meaning?.Query, meaning?.Model);
        var memories = Recall.Find(said, store.Facts(character), excerpts, persona.Name, now, meaning, [.. log.Except(Log.Newest(log, Log.Budget(Window)))],
                                   [.. store.Retired(character).Where(f => f.Why is { } why && (why.StartsWith(Changed) || why == Ended))]);
        store.Recalled(memories.Where(m => m.FactId is not null).Select(m => m.FactId!.Value), now);
        var recent = store.After(character, folded);
        // a character card's lorebook: background whose keys came up just now (or in the last couple of lines)
        var lore = new List<Memory>();
        var loreUsed = 0;
        foreach (var entry in persona.LoreFor(string.Join("\n", recent.TakeLast(2).Select(l => l.Text).Append(said))))
            if ((loreUsed += Prompt.Tokens(entry.Content)) <= LoreBudget)
                lore.Add(new Memory(entry.Content));
        var offered = tools?.Offered;
        // what is handed over goes with this turn only: pictures and PDFs as they are (when the model takes them), a text file's words
        List<ChatMessage> Messages(bool withTools, bool seeing)
        {
            var turn = (fromCabinet ? $"[cabinet] {said.Trim()}" : said.Trim()) + (aside is null ? "" : $"\n{aside}") + Handing(handed, seeing);
            var built = Prompt.Build(persona, Known(MindStore.Shared), Known(character),
                                     log, recent, situation, turn, [.. lore, .. memories],
                                     VoiceLanguage, Window, now, Agenda.Block(store.Agenda(), now, ReminderLead()), withTools, KeepsInTouch(), asking: tools?.AsksFirst, herself: Known(MindStore.Herself(character)));
            foreach (var thing in handed)
                if (thing.Text)
                    built[^1].Contents.Add(new TextContent($"\n--- {thing.Name} ---\n{thing.Words()}\n--- end of {thing.Name} ---"));
                else if (seeing)
                    built[^1].Contents.Add(new DataContent(thing.Data, thing.MediaType) { Name = thing.Name });
            return built;
        }
        var seeing = handed.Any(a => !a.Text) && !Unseeing;
        var line = store.Add(character, fromCabinet ? Speaker.Cabinet : Speaker.User, Kept(said, handed, [], seeing), now);

        var reply = new StringBuilder();
        void Show(string piece)
        {
            if (piece.Length == 0)
                return;
            reply.Append(piece);
            text(piece);
        }
        // "[note: ...]": the player told her something to keep; it is kept now, not at the next upkeep.
        // "[plan: ...]" and "[remind: ...]": something ahead with a date, on the agenda at once
        // "[saw: ...]": what a picture or file handed to her shows, kept in its place
        var seen = new List<string>();
        var stage = new StageDirections((name, payload) =>
        {
            if (name == "saw" && payload?.Trim() is { Length: > 0 } saw)
                seen.Add(saw);
            if (name == "note" && payload is { Length: > 2 } && !store.Facts(character).Any(f => Recall.Same(f.Text, payload)))
                store.AddFact(MindStore.Shared, payload, NoteImportance, now);
            if (name is "plan" or "remind" && payload is not null && Agenda.ParseDirection(payload, name == "remind", now, _clock.LocalTimeZone) is { } plan)
                AddPlan(plan, now);
            // "[out]" and "[sleep]": the player is leaving the PC now, or going to sleep (see Reaching.Where)
            if (name is "out" or "sleep")
                store.SetMark(Reaching.Mark, Reaching.Marked(name == "sleep", now));
            // "[later: HH:MM | what about]": her plan to reach the player again while they are out (see Later)
            if (name == "later" && payload is not null && Later.Read(payload, now, _clock.LocalTimeZone) is { } later)
                store.SetMark(Reaching.LaterMark, later.Mark);
            direction(name, payload);
        }, Show);
        var heard = false;                                  // anything at all came back
        var acted = false;                                  // a tool was used in this reply
        var toolsDone = 0;                                  // where in her reply what she wrote after her last tool begins
        var searches = new Dictionary<string, string>();    // the provider's own web searches in this reply: what for, by call
        async Task Stream(IChatClient talking, List<ChatMessage> sent, ChatOptions options)
        {
            var afterTool = false;
            await foreach (var update in talking.GetStreamingResponseAsync(sent, options, cancel))
            {
                heard = true;
                if (tools is not null && Provider.ToolStarting(update) is { } starting)
                    tools.Begun(starting);
                foreach (var content in update.Contents)
                    if (content is WebSearchToolCallContent search)
                    {
                        searches[search.CallId] = string.Join("; ", search.Queries ?? []);
                        tools?.Hosted(BuiltIn.SearchName);
                    }
                    else if (content is WebSearchToolResultContent found)
                    {
                        store.Add(character, Speaker.Cabinet, Searched(searches.GetValueOrDefault(found.CallId), found), _clock.GetLocalNow());
                        toolsDone = reply.Length;
                        acted = true;
                    }
                if (update.FinishReason == ChatFinishReason.Length)
                    CutShort = true;
                if (update.Contents.Any(c => c is FunctionResultContent))
                {
                    afterTool = true;                       // what she says next carries on after a pause
                    toolsDone = reply.Length;
                    acted = true;
                }
                else if (update.Text is { Length: > 0 } piece)
                {
                    Show(stage.Feed(afterTool ? Spaced(reply, piece) : piece));
                    afterTool = false;
                }
            }
        }
        _lastReply = null;
        tools?.Fresh();
        CutShort = false;
        try
        {
            for (bool withTools = offered is not null, again = true; ;)
            {
                try
                {
                    var sent = Messages(withTools, seeing);
                    if (withTools)
                        await Stream(Calling(client, tools!), sent, new ChatOptions { MaxOutputTokens = ReplyOptions.MaxOutputTokens, Tools = offered });
                    else
                        await Stream(client, sent, ReplyOptions);
                    // nothing at all came back, as a busy provider sometimes answers (R1): asked once more, unless a tool was
                    // used, which asking again would repeat
                    if (reply.Length == 0 && !acted && again)
                    {
                        again = false;
                        continue;
                    }
                    break;
                }
                catch (Exception e) when (withTools && !heard && !cancel.IsCancellationRequested && tools!.Hosting && (Toolbox.RefusesHosted(e) || Toolbox.Refuses(e)))
                {
                    // its own web search turned down (switched off for the account, or a server that doesn't run it): the other
                    // tools stay, and only if those are turned down too does she answer without any
                    tools.HostedRefused = true;
                    offered = tools.Offered;
                    withTools = offered is not null;
                }
                catch (Exception e) when (withTools && !heard && !cancel.IsCancellationRequested && Toolbox.Refuses(e))
                {
                    tools!.Refused = true;                  // a model without tools: she answers as before, with stage directions only
                    withTools = false;
                }
                catch (Exception e) when (seeing && !heard && !cancel.IsCancellationRequested && Trouble.Refused(e))
                {
                    Unseeing = true;                        // a model that can't take pictures: she says she can't see it, and none is sent again
                    seeing = false;
                }
            }
            Show(stage.Finish());
        }
        catch (OperationCanceledException) when (reply.Length > 0)
        {
            Keep(reply.ToString());                         // stopped part-way (cut off, or out of time): what she had written is hers
            throw;
        }
        finally
        {
            if (handed.Count > 0)
                store.Rewrite(line, Kept(said, handed, seen, seeing));
        }
        var whole = reply.ToString();
        return Keep(texting && toolsDone > 0 && !string.IsNullOrWhiteSpace(whole[toolsDone..]) ? whole[toolsDone..] : whole);
    }

    /// <summary>
    /// What the turn tells her about what was handed to her: what it is, and to start her reply with what she saw of
    /// each (kept in its place, as she won't see it again); or that the model she runs on can't take pictures.
    /// </summary>
    private static string Handing(IReadOnlyList<Attachment> handed, bool seeing)
    {
        if (handed.Count == 0)
            return "";
        static string Things(IEnumerable<Attachment> things) =>
            string.Join(" and ", things.Select(a => $"the {a.Kind} \"{a.Name}\"" + (a.Taken is { } t ? $" (taken {t.ToString("dddd d MMMM yyyy, HH:mm", Prompt.English)})" : "")));
        var unseen = seeing ? new List<Attachment>() : handed.Where(a => !a.Text).ToList();
        var taken = handed.Except(unseen).ToList();
        var note = new StringBuilder();
        if (unseen.Count > 0)
            note.Append($"\n[cabinet] The player tried to show you {Things(unseen)}, but the model you run on can't take pictures or PDFs: say so briefly.");
        if (taken.Count > 0)
            note.Append($"\n[cabinet] Handed to you with this: {Things(taken)}{(taken.Any(a => a.Text) ? " (the text is below)" : "")}. Begin your reply with " +
                        "[saw: what it is and shows, in one or two short sentences, with whatever you would want to remember, such as numbers, names and dates], " +
                        $"one for each, in this order: that line is kept in its place, and you won't see {(taken.Count == 1 ? "it" : "them")} again.");
        return note.ToString();
    }

    /// <summary>
    /// The line kept for what was said and handed over: each thing in brackets, a picture or PDF with what she saw of
    /// it, a short text file word for word.
    /// </summary>
    private static string Kept(string said, IReadOnlyList<Attachment> handed, IReadOnlyList<string> seen, bool seeing)
    {
        var line = new StringBuilder(said.Trim());
        for (var i = 0; i < handed.Count; i++)
        {
            var thing = handed[i];
            var saw = i < seen.Count ? seen[i] : null;
            if (line.Length > 0)
                line.Append(thing.Text ? '\n' : ' ');
            if (thing.Text && thing.Words() is { Length: <= Attachment.KeptWordForWord } words)
                line.Append($"[file: {thing.Name}]\n{words}\n[end of {thing.Name}]");
            else if (!thing.Text && !seeing)
                line.Append($"[{thing.Kind}: {thing.Name}, not seen: the model can't take them]");
            else
            {
                var name = thing.Name + (thing.Taken is { } t ? $", taken {t.ToString("yyyy-MM-dd HH:mm", Prompt.English)}" : "");
                line.Append(saw is null ? $"[{thing.Kind}: {name}]" : $"[{thing.Kind}: {name}: {saw}]");
            }
        }
        return line.ToString();
    }

    /// <summary>
    /// The client that lets her use tools: the model asks, the toolbox runs the call (asking the player first where
    /// they want that), the model carries on with the result, up to <see cref="Toolbox.Rounds"/> times. Calls run
    /// where this reply runs (the app's main thread, where the slip and the store live), not where the response came in.
    /// </summary>
    private FunctionInvokingChatClient Calling(IChatClient client, Toolbox tools)
    {
        var here = SynchronizationContext.Current is null ? TaskScheduler.Current : TaskScheduler.FromCurrentSynchronizationContext();
        var done = new HashSet<string>();                   // the calls made in this reply
        return new FunctionInvokingChatClient(client)
        {
            MaximumIterationsPerRequest = Toolbox.Rounds,
            FunctionInvoker = (call, cancel) =>
                new ValueTask<object?>(Task.Factory.StartNew(() => Use(tools, call, done, cancel), cancel, TaskCreationOptions.DenyChildAttach, here).Unwrap()),
        };
    }

    private async Task<object?> Use(Toolbox tools, FunctionInvocationContext call, HashSet<string> done, CancellationToken cancel)
    {
        // a model going round in circles doesn't open the same page ten times: the same call twice in a reply isn't made again
        if (!done.Add(call.Function.Name + System.Text.Json.JsonSerializer.Serialize(call.Arguments.ToDictionary(a => a.Key, a => a.Value), AIJsonUtilities.DefaultOptions)))
            return "You've already done exactly this in this reply. Don't repeat it; carry on.";
        object? result;
        string record;
        try
        {
            (result, record) = await tools.Use(call.Function, call.Arguments, cancel);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // something of ours went wrong around the tool: she and the record say so, rather than a bare "failed"
            (result, record) = ($"It didn't work: {e.Message}", $"You tried to use {call.Function.Name}; it didn't work: {e.Message}");
        }
        store.Add(character, Speaker.Cabinet, record, _clock.GetLocalNow());
        return result;
    }

    /// <summary>
    /// The line kept for a web search the provider ran: what for, and the pages it found (the sources, for the chat
    /// history), like the line for one of her own tools.
    /// </summary>
    private static string Searched(string? query, WebSearchToolResultContent found)
    {
        var pages = (found.Outputs ?? []).OfType<UriContent>().Select(u => BuiltIn.Shown(u.Uri)).Distinct().Take(5).ToList();
        var line = $"You searched the web for \"{query ?? "?"}\": " + (pages.Count == 0 ? "nothing came back." : $"found {string.Join(", ", pages)}.");
        return line.Length <= 300 ? line : line[..299] + "…";
    }

    /// <summary>Her first words after a tool ran, spaced from what she said before it (not in Chinese or Japanese, which use no spaces).</summary>
    private static string Spaced(StringBuilder before, string piece) =>
        before.Length == 0 || char.IsWhiteSpace(before[^1]) || char.IsWhiteSpace(piece[0]) || before[^1] >= '\u2E80' || piece[0] >= '\u2E80'
            ? piece : " " + piece;

    private (long Line, string Streamed)? _lastReply;      // her latest reply as it streamed in (untrimmed), and its line

    private string Keep(string streamed)
    {
        var answer = streamed.Trim();
        if (answer.Length > 0)
            _lastReply = (store.Add(character, Speaker.Her, answer, _clock.GetLocalNow()), streamed);
        return answer;
    }

    /// <summary>
    /// The player cut her latest reply off <paramref name="at"/> characters into it, as it streamed in (see
    /// <see cref="Speech.CutAt"/>): her line keeps only what they heard, ending in a dash, and a cabinet line after it
    /// tells her so, with what she had not said yet, so she can come back to it if it still matters.
    /// </summary>
    /// <returns>What she had not said, or null when nothing of it was left (or there is no reply to cut).</returns>
    public string? CutOff(int at)
    {
        if (_lastReply is not { } last)
            return null;
        var (heard, unsaid) = Cut(last.Streamed, at);
        if (unsaid.Length == 0)
            return null;
        store.Rewrite(last.Line, heard);
        store.Add(character, Speaker.Cabinet, $"The player cut you off there. You hadn't said yet: \"{unsaid}\"", _clock.GetLocalNow());
        _lastReply = null;
        return unsaid;
    }

    /// <summary>
    /// "…and I was thinking we—" and the rest, cut <paramref name="at"/> characters in: at the start of a word cut in
    /// two (Chinese and Japanese can be cut between any characters), with no comma left dangling, and no dash after a
    /// sentence that was finished.
    /// </summary>
    public static (string Heard, string Unsaid) Cut(string text, int at)
    {
        static bool Latin(char c) => char.IsLetterOrDigit(c) && c < '\u2E80';
        at = Math.Clamp(at, 0, text.Length);
        while (at > 0 && at < text.Length && Latin(text[at]) && Latin(text[at - 1]))
            at--;
        var unsaid = text[at..].Trim();
        if (!unsaid.Any(char.IsLetterOrDigit))
            return (text.Trim(), "");
        var heard = text[..at].Trim().TrimEnd(',', '，', '、', ';', '；', ':', '：', '-', '—');
        return (heard.Length > 0 && ".!?。！？…\"”」』)）".Contains(heard[^1]) ? heard : heard + "—", unsaid);
    }

    /// <summary>
    /// Puts something on the agenda. When it is there already (on the same day, saying the same thing, or at the same
    /// time about the same thing) it only adds what is new: a reminder, a repeat (not to a calendar's item: whether
    /// that repeats is the calendar's to say). A reminder for something wholly past is not set. Returns the item's id, or null.
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
            var repeat = spec.Repeat != Repeat.Once && same.Source is null ? spec.Repeat : same.Repeat;
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

    /// <summary>Something going on that the player told her about a couple of days ago, to ask how it is going (see <see cref="Threads"/>).</summary>
    public OpenThread? ThreadToAskAbout() => Threads.ToAskAbout(store.Threads(), _clock.GetLocalNow());

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
    /// Folds the window's older part out of it, keeping its newest half (and at least a dozen lines) word for word: her
    /// notes on the lines leaving are what she sees of them from now on (see <see cref="Log"/>). Upkeep has usually
    /// noted them while the talk was quiet; lines it hasn't (the window filled in the middle of a conversation) are
    /// observed first. Returns false when there was nothing to fold or those lines couldn't be noted yet (they stay, and
    /// the next attempt includes them).
    /// </summary>
    /// <param name="quiet">Folding while quiet: from three quarters full; otherwise only a full window is folded.</param>
    public async Task<bool> Fold(IChatClient client, bool quiet = false, CancellationToken cancel = default)
    {
        var lines = Unfolded();
        if (Prompt.Tokens(lines) <= (quiet ? Window * 3 / 4 : Window))
            return false;

        var keep = 0;
        var kept = 0;
        while (keep < lines.Count && (keep < KeepLines || kept + Prompt.Tokens(lines[^(keep + 1)].Text) + 4 <= Window / 2))
            kept += Prompt.Tokens(lines[^++keep].Text) + 4;
        if (keep == lines.Count)
            return false;
        var through = lines[lines.Count - keep - 1].Id;
        while (store.Digested(character) < through)
            if (await Observe(client, through, cancel) is null)
                return false;
        store.Fold(character, through, _clock.GetLocalNow());
        return true;
    }

    /// <summary>
    /// Notes the next batch of lines not yet observed, up to <paramref name="through"/>, into her log: a few thousand tokens
    /// at a time, with her latest notes for continuity. Returns the new notes; null when nothing usable came back (the
    /// lines wait for the next try; a batch that fails three times is passed over without notes, still searchable word
    /// for word, so it can't hold up the rest).
    /// </summary>
    private async Task<IReadOnlyList<Observation>?> Observe(IChatClient client, long through, CancellationToken cancel)
    {
        var pending = store.After(character, store.Digested(character)).Where(l => l.Id <= through).ToList();
        if (pending.Count == 0)
            return [];
        for (var atOnce = Log.ObserveAtOnce; ;)
        {
            var batch = new List<Line>();
            var used = 0;
            foreach (var line in pending)
            {
                if (batch.Count > 0 && used + Prompt.Tokens(line.Text) > atOnce)
                    break;
                batch.Add(line);
                used += Prompt.Tokens(line.Text) + 4;
            }
            var latest = Log.Newest(store.Observations(character), Log.Continuity);
            var response = await Whole(client, Log.Observe(persona, latest, batch), ObserveOptions, cancel);
            if (response is null && batch.Count > 1)
            {
                atOnce = Math.Max(1, used / 2);     // more to note than an answer holds: the same lines in smaller batches
                continue;
            }
            if (response is null || Log.Parse(response.Text, batch) is not { } observed)
            {
                _failed = _failed.From == batch[0].Id ? (batch[0].Id, _failed.Times + 1) : (batch[0].Id, 1);
                if (_failed.Times < GiveUpAfter)
                    return null;
                observed = [];
            }
            store.Observe(character, observed, batch[^1].Id);
            return observed;
        }
    }

    /// <summary>
    /// When her notes no longer fit what she sees of them (see <see cref="Log.Budget"/>), condenses the oldest finished
    /// stretch one step (see <see cref="Log.ToCondense"/>): a week of days into notes for the week, a month of weeks into
    /// the month, a year of months into the year. The originals stay in her log, out of view, recalled when they come
    /// up. Only while the talk is quiet, so her cached prompt doesn't change in the middle of a conversation; one step a
    /// call, as upkeep runs. False when nothing needed condensing or the answer couldn't be used (tried again later).
    /// </summary>
    public async Task<bool> Condense(IChatClient client, CancellationToken cancel = default)
    {
        if (!IsQuiet(Quiet) || Log.ToCondense(Log.Left(store.Observations(character), store.Folded(character)), Window, _clock.GetLocalNow()) is not { } stretch)
            return false;
        List<Observation>? condensed = stretch.Notes.Count == 1
            ? [stretch.Notes[0] with { Id = 0, At = stretch.At, Span = stretch.Span }]           // a lone note just moves up a level
            : await Whole(client, Log.Condense(persona, stretch), CondenseOptions, cancel) is { } response ? Log.Parse(response.Text, stretch) : null;
        if (condensed is null)
            return false;
        store.Condense(character, [.. stretch.Notes.Select(n => n.Id)], condensed);
        return true;
    }

    /// <summary>What the player said and her stored facts and notes as vectors, or null without a model (or if it fails).</summary>
    private async Task<Meaning?> MeaningOf(string said, CancellationToken cancel)
    {
        if (Embedder is not { } embedder)
            return null;
        try
        {
            var query = await Task.Run(() => embedder.Embed(said, query: true), cancel);
            return new Meaning(query, embedder.Model, store.Vectors(MindStore.FactKind, embedder.Model.Name), store.Vectors(MindStore.ObservationKind, embedder.Model.Name));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;                    // the model being swapped, say: shared words will do for this once
        }
    }

    /// <summary>
    /// Gives facts, notes and old passages without a vector from the loaded model one, a batch at a time (the model runs
    /// off the calling thread; the store is written on it). Returns how many were done.
    /// </summary>
    public async Task<int> Index(int batch = 64, CancellationToken cancel = default)
    {
        if (Embedder is not { } embedder)
            return 0;
        History.CatchUp(store.Folded(character));       // passages for what has left the window
        var model = embedder.Model.Name;
        var haveFacts = store.Vectors(MindStore.FactKind, model);
        var haveObservations = store.Vectors(MindStore.ObservationKind, model);
        var todo = store.Facts(character).Where(f => !haveFacts.ContainsKey(f.Id)).Select(f => (Kind: MindStore.FactKind, f.Id, f.Text))
            .Concat(store.Observations(character).Where(o => !haveObservations.ContainsKey(o.Id)).Select(o => (Kind: MindStore.ObservationKind, o.Id, o.Text)))
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

    /// <summary>
    /// The player has said something upkeep has not noted yet and the conversation has been quiet for
    /// <paramref name="quiet"/>, or she has notes whose facts aren't kept yet.
    /// </summary>
    public bool NeedsDigest(TimeSpan quiet)
    {
        var lines = store.After(character, store.Digested(character));
        return lines.Any(l => l.Speaker == Speaker.User) && _clock.GetLocalNow() - lines[^1].At >= quiet || Unkept().Count > 0;
    }

    /// <summary>
    /// Upkeep, a few minutes into the quiet, in two narrow steps: notes the next stretch of the conversation not yet
    /// observed into her log (the notes come into view once their lines leave the window), then keeps what those notes
    /// tell of: facts, reconciled against the ones that relate (mem0's add, update, delete; a fact that changes keeps its
    /// history), and the agenda's plans, which reach her next reply as they are (see <see cref="Known"/>). Notes whose facts couldn't be kept are taken up again first. Returns false when there was nothing to read or the
    /// answer couldn't be used (tried again next time; a stretch that fails three times is passed over).
    /// </summary>
    public async Task<bool> Digest(IChatClient client, CancellationToken cancel = default)
    {
        if (Unkept().Count == 0)
        {
            if (store.After(character, store.Digested(character)).Count == 0 || await Observe(client, long.MaxValue, cancel) is not { } observed)
                return false;
            if (observed.Count == 0)
                return true;                                // read, and nothing worth noting: nothing to keep
        }
        return await Keep(client, cancel);
    }

    private string KeptMark => $"kept:{character}";

    /// <summary>Her day notes whose facts and plans upkeep hasn't kept yet, oldest first.</summary>
    private IReadOnlyList<Observation> Unkept()
    {
        var kept = long.TryParse(store.Mark(KeptMark), out var id) ? id : 0;
        return [.. store.Observations(character).Where(o => o.Id > kept && o.Span == Span.Day && !o.Condensed && o.Source is null).OrderBy(o => o.Id)];
    }

    /// <summary>Keeps the facts and plans her unkept notes tell of (see <see cref="Digest"/>), a few thousand tokens of notes at a time.</summary>
    private async Task<bool> Keep(IChatClient client, CancellationToken cancel)
    {
        var notes = new List<Observation>();
        var used = 0;
        foreach (var note in Unkept())
        {
            if (notes.Count > 0 && used + Prompt.Tokens(note.Text) > _digestAtOnce)
                break;
            notes.Add(note);
            used += Prompt.Tokens(note.Text) + 3;
        }
        // what she knows that relates to these notes, not everything: the request stays the same size however much she knows
        var all = store.Facts(character);
        var text = string.Join('\n', notes.Select(n => n.Text));
        var known = all.Count <= DigestFacts ? all : Recall.Related(text, all, DigestFacts, await MeaningOf(text[..Math.Min(text.Length, 2000)], cancel));
        // the player's own items: a calendar's are its to change (and a busy one would fill every request)
        var agenda = store.Agenda().Where(p => p.Source is null).ToList();
        var threads = store.Threads();
        if (await Whole(client, Upkeep.Request(persona, known, notes, agenda, threads), DigestOptions, cancel) is not { } response)
        {
            _digestAtOnce = Math.Max(300, used / 2);        // too much for one answer: fewer notes next time
            return false;
        }
        _digestAtOnce = DigestAtOnce;
        if (Upkeep.Parse(response.Text) is not { } digestion)
        {
            _failedKeep = _failedKeep.From == notes[0].Id ? (notes[0].Id, _failedKeep.Times + 1) : (notes[0].Id, 1);
            if (_failedKeep.Times >= GiveUpAfter)
                store.SetMark(KeptMark, notes[^1].Id.ToString(System.Globalization.CultureInfo.InvariantCulture));     // passed over: the notes stay in her log
            return false;
        }

        var now = _clock.GetLocalNow();
        var byId = known.ToDictionary(f => f.Id);
        foreach (var change in digestion.Changes)
        {
            switch (change.Op)
            {
                // something she had retired has come up again: back to the foreground, not a second copy
                case "add" when change.Text is { Length: > 2 } text2 && store.Retired(character).FirstOrDefault(f => Recall.Same(f.Text, text2)) is { } back:
                    store.Restore(back.Id, now);
                    break;
                case "add" when change.Text is { Length: > 2 } text2 && !store.Facts(character).Any(f => Recall.Same(f.Text, text2)):
                    var scope = change.About switch { "us" => character, "her" => MindStore.Herself(character), _ => MindStore.Shared };
                    store.AddFact(scope, text2, change.Importance ?? 5, now);
                    break;
                // a fact that changed keeps its history: the old one retired as changed, recalled as what used to be, and the
                // new one learned now; the same thing in other words is just rewritten
                case "update" when change.Id is { } id && byId.TryGetValue(id, out var old) && !old.Pinned && change.Text is { Length: > 2 } text2:
                    if (Recall.Same(old.Text, text2))
                        store.UpdateFact(id, text2, change.Importance);
                    else
                    {
                        store.Retire(id, $"{Changed} {text2}", now);
                        store.AddFact(old.Scope, text2, change.Importance ?? old.Importance, now);
                    }
                    break;
                // no longer true: retired, not deleted, so what used to be can still be recalled
                case "delete" when change.Id is { } id && byId.TryGetValue(id, out var gone) && !gone.Pinned:
                    store.Retire(id, Ended, now);
                    break;
            }
        }
        // things ahead with a date: on the agenda (her notes write dates out in full)
        var plans = agenda.ToDictionary(p => p.Id);
        foreach (var change in digestion.Plans)
        {
            var when = change.When is { } w ? Agenda.ParseWhen(w, now, _clock.LocalTimeZone) : null;
            switch (change.Op)
            {
                case "add" when when is { } at && change.Text is { Length: > 1 } text2:
                    var (before, repeat) = Agenda.ParseOptions(change.Options);
                    AddPlan(new PlanSpec(at.At, at.AllDay, text2, before ?? (change.Remind ? TimeSpan.Zero : null), repeat), now);
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
        // what is going on with no date, to follow up (see Threads)
        var open = threads.ToDictionary(t => t.Id);
        foreach (var change in digestion.Threads)
        {
            switch (change.Op)
            {
                case "open" when change.Text is { Length: > 2 } text2 && !open.Values.Any(t => Recall.Same(t.Text, text2)):
                    store.AddThread(text2, now);
                    break;
                case "update" when change.Id is { } id && open.ContainsKey(id):
                    store.MentionThread(id, change.Text is { Length: > 2 } news ? news : null, now);
                    break;
                case "close" when change.Id is { } id && open.ContainsKey(id):
                    store.CloseThread(id, "settled", now);
                    break;
            }
        }
        store.LapseThreads(now - Threads.Lapse, now);
        store.SetMark(KeptMark, notes[^1].Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

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
        var batch = facts.Where(x => x.Id > from).Take(_reviewAtOnce).ToList();
        if (batch.Count == 0)
        {
            store.SetMark(key, $"{now:O}|{facts.Count}|0");
            return false;
        }
        if (await Whole(client, Upkeep.Review(persona, batch, now), DigestOptions, cancel) is not { } response)
        {
            _reviewAtOnce = Math.Max(10, batch.Count / 2);  // too much for one answer: a smaller batch next time
            return false;
        }
        _reviewAtOnce = ReviewAtOnce;
        if (Upkeep.ParseReview(response.Text) is not { } verdict)
            return false;                                   // tried again next time, from the same place

        var mine = batch.Where(x => !x.Pinned).ToDictionary(x => x.Id);
        // a merge is of what says the same thing: one that comes out long joined different things into a paragraph (M8), and
        // one that drops a name or number lost a detail, which merged-away facts no longer bring back (M12)
        foreach (var merge in verdict.Merges.Where(m => m.Ids.Distinct().Count() == m.Ids.Count && m.Ids.All(mine.ContainsKey) && m.Ids.Select(id => mine[id].Scope).Distinct().Count() == 1
                                                        && Prompt.Tokens(m.Text) <= MergedTokens && (m.Ids.Count == 1 || m.Ids.All(id => Recall.Within(mine[id].Text, m.Text)))))
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
            foreach (var id in merge.Ids)
                mine.Remove(id);
        }
        foreach (var retirement in verdict.Retirements.Where(r => mine.ContainsKey(r.Id)))
        {
            store.Retire(retirement.Id, retirement.Why, now);
        }
        var end = batch[^1].Id == facts[^1].Id;
        store.SetMark(key, end ? $"{now:O}|{store.Facts(character).Count}|0" : $"{(last == DateTimeOffset.MinValue ? now : last):O}|{count}|{batch[^1].Id}");
        return true;
    }

    public static DateOnly MondayOf(DateTimeOffset at)
    {
        var day = DateOnly.FromDateTime(at.DateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    /// <summary>
    /// Asks, and asks once more with twice the room when the provider cut the answer off at its length limit (a model
    /// that thinks first spends the room on its thinking too); null when even that came back cut, so a cut answer is
    /// never kept. Some providers say so and some don't hold to the limit at all (the player's provider, 2026-10-11).
    /// </summary>
    private static async Task<ChatResponse?> Whole(IChatClient client, List<ChatMessage> request, ChatOptions options, CancellationToken cancel)
    {
        var response = await client.GetResponseAsync(request, options, cancel);
        if (response.FinishReason != ChatFinishReason.Length)
            return response;
        var roomier = options.Clone();
        roomier.MaxOutputTokens = (options.MaxOutputTokens ?? 2000) * 2;
        response = await client.GetResponseAsync(request, roomier, cancel);
        return response.FinishReason == ChatFinishReason.Length ? null : response;
    }

    /// <summary>
    /// Puts the cabinet's record of each finished day not yet in her diary into it (see <see cref="Diary"/>), a note dated at
    /// the end of that day: written by code, so always right, and nothing upkeep keeps facts from. The first time, every
    /// day the ledger keeps (up to 120), so nothing is missed.
    /// </summary>
    public void Record(InsertCoin.Core.LedgerState state)
    {
        var through = DateOnly.TryParseExact(store.Mark($"diary:{character}"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                             System.Globalization.DateTimeStyles.None, out var day) ? day : DateOnly.MinValue;
        var good = state.StreakDays();
        var zone = _clock.LocalTimeZone;
        var notes = state.Days.Where(d => d.Day > through).Select(d =>
        {
            var end = d.Day.ToDateTime(new TimeOnly(23, 59));
            return new Observation(0, new DateTimeOffset(end, zone.GetUtcOffset(end)), Priority.Medium, Diary.Entry(d, good.Contains(d.Day)), 0, 0,
                                   Source: Diary.Cabinet);
        }).ToList();
        if (notes.Count > 0)
            store.Record(character, notes, state.Days[^1].Day);
    }

    /// <summary>
    /// What she always has in mind about the player (<see cref="MindStore.Shared"/>), the two of them, or herself
    /// (<see cref="MindStore.Herself"/>, which has no note of the player's), for this reply (see <see cref="Prompt.Known"/>):
    /// the important facts, the same for each. Listing all of hers, small ones too, put trivia kept wrong ahead of the
    /// conversation itself ("a dog named Oliver", a cat in fact): answers fell from 84% to 80% on LoCoMo (2026-10-11).
    /// </summary>
    private string Known(string scope) =>
        Prompt.Known(MindStore.IsHerself(scope) ? "" : store.Note(scope, scope == MindStore.Shared ? MindStore.AboutUser : MindStore.Us),
                     [.. store.Facts(character).Where(f => f.Scope == scope)], [.. store.Retired(character).Where(f => f.Scope == scope)], _clock.GetLocalNow());

    private IReadOnlyList<Line> Unfolded() => store.After(character, store.Folded(character));
}
