using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Godot;
using InsertCoin.Core;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

namespace InsertCoin.Game.App;

/// <summary>
/// Talking with her. What the user types goes to their own provider; her answer streams into her bubble and its
/// stage directions are acted out. Cabinet events and her canned lines go into the same conversation, so she
/// knows what happened. After an answer the older conversation is folded into a summary when it has grown long.
/// Everything runs on the main thread: the mind's awaits come back through Godot's synchronization context.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Chat : IDisposable
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(90);

    /// <summary>The <see cref="UseSearch"/> choice for no local model: recall by shared words only.</summary>
    public const string WordsOnly = "words";

    private IChatClient? _client, _talk;                // _talk: her replies, cached where the provider needs asking (see Provider.ForConversation)
    private Provider? _provider;
    private int? _window;
    private readonly Queue<(long Input, long Cached)> _usage = new();
    private Conversation? _conversation;
    private LocalEmbedder? _embedder;
    private string? _search;
    private int _searchLoads;
    private CancellationTokenSource? _answering;
    private TaskCompletionSource? _written;             // done once the reply under way is written, or stopped
    private Speech? _speech;                            // what her voice makes of the latest reply
    private bool _folding, _upkeeping, _cutting;
    private DateTimeOffset _upkeepAfter;

    public Chat(string dataDir)
    {
        var folder = Path.Combine(dataDir, "companion");
        Store = new MindStore(Path.Combine(folder, "memory.db"));
        Keys = new KeyStore(Path.Combine(folder, "key.bin"));
    }

    public MindStore Store { get; }
    public KeyStore Keys { get; }

    /// <summary>A provider is set up and someone is on the stool.</summary>
    public bool Ready => _client is not null && _conversation is not null;
    public bool Answering => _answering is not null;
    public Conversation? Conversation => _conversation;

    /// <summary>Uses this provider (with the stored key) from now on, or none.</summary>
    public void Connect(Provider? provider)
    {
        _client?.Dispose();
        _provider = provider;
        // every request counted (see Metered, and the chat settings' Usage tab): her replies apart from her upkeep
        var timed = provider?.Connect(Keys.Load()) is { } client ? new Timed(client, Used) : null;
        _client = timed is null ? null : new Metered(timed, Store, provider!.Model, "upkeep");
        _talk = timed is null ? null : new Metered(provider!.ForConversation(timed), Store, provider.Model, "replies");
        _usage.Clear();
        UseWindow(_window);
        UseSearch(_search);
    }

    /// <summary>How much of the conversation she keeps word for word (tokens); null: what suits the provider.</summary>
    public void UseWindow(int? tokens)
    {
        _window = tokens;
        if (_conversation is not null)
            _conversation.Window = Window;
    }

    public int Window => _window ?? _provider?.DefaultWindow ?? Prompt.DefaultWindow;

    /// <summary>Of the input her last replies read, the share the provider served from its cache (by its own count), and over how many replies; null before any.</summary>
    public (double Share, int Replies)? Cached =>
        _usage.Count == 0 || _usage.Sum(u => u.Input) == 0 ? null : ((double)_usage.Sum(u => u.Cached) / _usage.Sum(u => u.Input), _usage.Count);

    private void Used(UsageDetails usage)
    {
        _usage.Enqueue((usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0));
        while (_usage.Count > 20)
            _usage.Dequeue();
    }

    /// <summary>Someone else is on the stool: talk continues in their own conversation.</summary>
    public void Seat(string characterId, Persona persona)
    {
        _answering?.Cancel();
        _conversation = new Conversation(Store, characterId, persona) { Embedder = _embedder, Window = Window, ReminderLead = ReminderLead };
    }

    /// <summary>The player's "remind me before things on my agenda", as set now (see <see cref="Conversation.ReminderLead"/>).</summary>
    public Func<TimeSpan?> ReminderLead { get; init; } = () => null;

    /// <summary>The local models for search by meaning that this build carries (a light build may leave the big one out).</summary>
    public static EmbeddingModel[] Searchable() => [.. EmbeddingModel.All.Where(m => Godot.FileAccess.FileExists($"res://models/{m.Name}/model.onnx"))];

    /// <summary>The model searching now, if any (it may still be loading).</summary>
    public EmbeddingModel? Searching => _embedder?.Model;

    /// <summary>
    /// Search by meaning with this model (a name from <see cref="Searchable"/>, <see cref="WordsOnly"/>, or null for
    /// the best one present). It loads in the background, a second or two, and only while a provider is set up:
    /// someone who never chats does not pay for it in memory.
    /// </summary>
    public async void UseSearch(string? choice)
    {
        _search = choice;
        var model = _client is null || choice == WordsOnly ? null : Searchable().FirstOrDefault(m => m.Name == choice) ?? Searchable().FirstOrDefault();
        if (model?.Name == _embedder?.Model.Name)
            return;
        var load = ++_searchLoads;
        LocalEmbedder? loaded = null;
        if (model is not null)
            try
            {
                loaded = await Task.Run(() =>
                {
                    var folder = $"res://models/{model.Name}/";
                    using var tokenizer = new MemoryStream(Godot.FileAccess.GetFileAsBytes(folder + model.TokenizerFile));
                    return new LocalEmbedder(model, ModelFiles.OnDisk(folder + "model.onnx", model.Name), tokenizer);
                });
            }
            catch (Exception e)
            {
                GD.PrintErr($"Chat: could not load {model.Name}, recalling by shared words: {e.Message}");
            }
        if (load != _searchLoads)
        {
            loaded?.Dispose();                  // a newer choice came in while this one loaded
            return;
        }
        var old = _embedder;
        _embedder = loaded;
        if (_conversation is not null)
            _conversation.Embedder = loaded;
        old?.Dispose();
        GD.Print(loaded is null ? "Chat: recalling by shared words" : $"Chat: recalling by meaning with {loaded.Model.Name}");
        await Index();
    }

    /// <summary>Gives what she knows vectors from the loaded model, a batch at a time.</summary>
    private async Task Index()
    {
        if (_conversation is not { Embedder: not null } conversation)
            return;
        try
        {
            await conversation.Index();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: indexing memories failed, will try again later: {e.Message}");
        }
    }

    /// <summary>
    /// Sends what the user said and plays her answer on <paramref name="view"/>; with <paramref name="speech"/>, her
    /// voice says it too (and she is told how her voice works).
    /// </summary>
    /// <param name="voiceLanguage">
    /// The language of her voice whenever she has one switched on, ready this moment or not: her instructions stay the
    /// same while her voice rests (during a game), so the start of the prompt stays cached.
    /// </param>
    public Task Ask(string said, string situation, CompanionView view, Speech? speech = null, string? voiceLanguage = null) =>
        Respond(said, fromCabinet: false, fallback: null, situation, view, speech, voiceLanguage);

    /// <summary>
    /// Has her pass on what the cabinet asks (reminders going off, see <see cref="Agenda.Announcement"/>) in her own
    /// words, shown and said as a reply is; when that fails, she says <paramref name="fallback"/> instead.
    /// </summary>
    public Task Announce(string note, string fallback, string situation, CompanionView view, Speech? speech = null, string? voiceLanguage = null) =>
        Respond(note, fromCabinet: true, fallback, situation, view, speech, voiceLanguage);

    private async Task Respond(string said, bool fromCabinet, string? fallback, string situation, CompanionView view, Speech? speech, string? voiceLanguage)
    {
        if (_talk is null || _conversation is null || _answering is not null)
            return;
        var client = _talk;
        var conversation = _conversation;
        conversation.VoiceLanguage = voiceLanguage ?? speech?.Language;
        using var answering = _answering = new CancellationTokenSource(AnswerTimeout);
        _written = new TaskCompletionSource();
        _speech = speech;
        bool Here() => GodotObject.IsInstanceValid(view) && conversation == _conversation;     // not swapped for someone else meanwhile
        var clock = Stopwatch.StartNew();
        long? shown = null;
        try
        {
            view.BeginReply();
            Func<IChatClient, string, string, Action<string>, Action<string, string?>, CancellationToken, Task<string>> reply =
                fromCabinet ? conversation.Announce : conversation.Answer;
            await reply(client, said, situation,
                                      words =>
                                      {
                                          if (!Here())
                                              return;
                                          shown ??= clock.ElapsedMilliseconds;
                                          view.Continue(words);
                                          speech?.Text(words);
                                      },
                                      (name, payload) =>
                                      {
                                          if (!Here())
                                              return;
                                          Act(view, name);
                                          speech?.Direction(name, payload);
                                      },
                                      answering.Token);
            GD.Print($"Chat: her bubble showed its first words {shown} ms after asking, the whole reply after {clock.ElapsedMilliseconds} ms");
            if (Here())
                speech?.Finish();
        }
        catch (OperationCanceledException) when (_cutting)
        {
            // the player cut her off: Interrupt takes it from here
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: {e.GetType().Name}: {e.Message}");
            if (Here() && fallback is not null)
            {
                if (fallback.Length > 0)
                    view.Continue(fallback);                // a reminder still gets said, in the plain words; a check-in just lapses
            }
            else if (Here())
            {
                view.Play(Moment.Worry);
                view.Continue(Trouble.Explain(e));
            }
        }
        finally
        {
            _answering = null;
            _cutting = false;
            _written.TrySetResult();
            if (GodotObject.IsInstanceValid(view))
                view.EndReply();
        }

        await Index();                          // a note she just took becomes searchable by meaning
        if (_folding || !conversation.NeedsFolding || _client is not { } plain)
            return;
        _folding = true;
        try
        {
            await conversation.Fold(plain);         // the window is full: fold now (normally upkeep folds it while quiet)
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: folding the conversation failed, will try again later: {e.Message}");
        }
        finally
        {
            _folding = false;
        }
    }

    /// <summary>
    /// The player cut her off (clicked her, talked over her, asked something new): her voice stops, a reply still
    /// being written is stopped, and her line keeps only what they heard of it, or, without her voice, saw (see
    /// <see cref="Conversation.CutOff"/>). Nothing is cut when she had finished.
    /// </summary>
    public async Task Interrupt(Voice voice, CompanionView? view)
    {
        var conversation = _conversation;
        var here = view is not null && GodotObject.IsInstanceValid(view);
        int? at = _speech is { } speech && voice.Saying(speech) ? speech.CutAt(voice.Heard.Lines, voice.Heard.Into)
                : here ? view!.Seen : null;
        if (here && (_answering is not null || voice.Speaking || at is not null))
            view!.Play(Moment.CutOff);
        voice.Stop();
        if (_answering is { } answering)
        {
            _cutting = true;
            answering.Cancel();
            if (_written is { } written)
                await written.Task;
        }
        _speech = null;
        if (at is not { } cut || conversation is null || conversation != _conversation || conversation.CutOff(cut) is not { } unsaid)
            return;
        GD.Print($"Chat: cut off {cut} characters into her reply, {unsaid.Length} unsaid");
        if (view is not null && GodotObject.IsInstanceValid(view))
            view.Cut(cut);
    }

    /// <summary>
    /// Her sleep time, called every minute: once the conversation has been quiet for a while (sooner while a game
    /// runs, since she has nothing else to do) the part she has not digested yet becomes an episode and facts. A
    /// failure (offline, say) waits ten minutes before the next try.
    /// </summary>
    public async Task Upkeep(bool gameRunning, bool now = false)
    {
        if (_client is null || _conversation is not { } conversation || _answering is not null || _upkeeping || _folding
            || (!now && DateTimeOffset.Now < _upkeepAfter))
            return;
        var digest = now || conversation.NeedsDigest(gameRunning ? TimeSpan.FromMinutes(2) : Conversation.Quiet);
        _upkeeping = true;
        try
        {
            // something new to read; else old moments to combine; else, when quiet and due, a review of her facts;
            // and vectors for whatever has none yet
            if (digest)
                await conversation.Digest(_client);
            else if (!await conversation.Consolidate(_client) && conversation.IsQuiet(Conversation.Quiet))
                await conversation.Review(_client);
            await conversation.Index();
            // the window three quarters full and the talk quiet: fold it now, when a 5-minute cache has gone cold anyway
            if (conversation.IsQuiet(Conversation.Quiet) && conversation.NeedsQuietFold)
            {
                _folding = true;
                try
                {
                    await conversation.Fold(_client, quiet: true);
                }
                finally
                {
                    _folding = false;
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: upkeep failed, will try again later: {e.Message}");
            _upkeepAfter = DateTimeOffset.Now.AddMinutes(10);
        }
        finally
        {
            _upkeeping = false;
        }
    }

    /// <summary>Logs what happened at the cabinet, for her to know about later.</summary>
    public void Witness(IEnumerable<LedgerEvent> events, Rules rules)
    {
        if (!Ready)
            return;
        foreach (var e in events)
            if (Situation.Witness(e, rules) is { } what)
                _conversation!.Witness(what);
    }

    /// <summary>Logs a canned line she said on her own.</summary>
    public void Said(string line)
    {
        if (Ready)
            _conversation!.Said(line);
    }

    public void Dispose()
    {
        _answering?.Cancel();
        _client?.Dispose();
        _embedder?.Dispose();
        Store.Dispose();
    }

    /// <summary>
    /// Logs how a reply streamed in: when the first piece came, when the first words did (a model that thinks first
    /// sends nothing to show until it has thought), when it ended, and the tokens it took when the provider says.
    /// </summary>
    private sealed class Timed(IChatClient inner, Action<UsageDetails> used) : DelegatingChatClient(inner)
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var clock = Stopwatch.StartNew();
            long? firstPiece = null, firstWords = null;
            var pieces = 0;
            UsageDetails? usage = null;
            try
            {
                await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    firstPiece ??= clock.ElapsedMilliseconds;
                    if (update.Text.Length > 0)
                    {
                        firstWords ??= clock.ElapsedMilliseconds;
                        pieces++;
                    }
                    usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
                    yield return update;
                }
            }
            finally
            {
                GD.Print($"Chat: reply streamed: first piece after {firstPiece} ms, first words after {firstWords} ms, done after {clock.ElapsedMilliseconds} ms, "
                         + $"{pieces} pieces of words" + (usage is null ? "" : $"; {usage.OutputTokenCount} tokens out ({usage.ReasoningTokenCount ?? 0} of them thinking), "
                         + $"{usage.InputTokenCount} in ({usage.CachedInputTokenCount ?? 0} of them from the provider's cache)"));
                if (usage is not null)
                    used(usage);
            }
        }
    }

    /// <summary>Plays a stage direction from her reply; ones her body has no move for are ignored.</summary>
    private static void Act(CompanionView view, string direction)
    {
        Moment? moment = direction switch
        {
            "cheer" => Moment.Cheer,
            "pump" => Moment.Pump,
            "wave" => Moment.Greet,
            "look" => Moment.Glance,
            "gasp" => Moment.Alarm,
            "worry" => Moment.Worry,
            "note" => Moment.Note,
            _ => null,
        };
        if (moment is { } m)
            view.Play(m);
    }
}
