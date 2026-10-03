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

    private IChatClient? _client;
    private Conversation? _conversation;
    private LocalEmbedder? _embedder;
    private string? _search;
    private int _searchLoads;
    private CancellationTokenSource? _answering;
    private bool _folding, _upkeeping;
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
        _client = provider?.Connect(Keys.Load()) is { } client ? new Timed(client) : null;
        UseSearch(_search);
    }

    /// <summary>Someone else is on the stool: talk continues in their own conversation.</summary>
    public void Seat(string characterId, Persona persona)
    {
        _answering?.Cancel();
        _conversation = new Conversation(Store, characterId, persona) { Embedder = _embedder };
    }

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
    public async Task Ask(string said, string situation, CompanionView view, Speech? speech = null)
    {
        if (_client is null || _conversation is null || _answering is not null)
            return;
        var client = _client;
        var conversation = _conversation;
        conversation.VoiceLanguage = speech?.Language;
        using var answering = _answering = new CancellationTokenSource(AnswerTimeout);
        bool Here() => GodotObject.IsInstanceValid(view) && conversation == _conversation;     // not swapped for someone else meanwhile
        var clock = Stopwatch.StartNew();
        long? shown = null;
        try
        {
            view.BeginReply();
            await conversation.Answer(client, said, situation,
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
        catch (Exception e)
        {
            GD.PrintErr($"Chat: {e.GetType().Name}: {e.Message}");
            if (Here())
            {
                view.Play(Moment.Worry);
                view.Continue(Trouble.Explain(e));
            }
        }
        finally
        {
            _answering = null;
            if (GodotObject.IsInstanceValid(view))
                view.EndReply();
        }

        await Index();                          // a note she just took becomes searchable by meaning
        if (_folding || !conversation.NeedsFolding)
            return;
        _folding = true;
        try
        {
            await conversation.Fold(client);
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
            // something new to read; else old moments to combine into a week; and vectors for whatever has none yet
            if (digest)
                await conversation.Digest(_client);
            else
                await conversation.Consolidate(_client);
            await conversation.Index();
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
    private sealed class Timed(IChatClient inner) : DelegatingChatClient(inner)
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
                         + $"{pieces} pieces of words" + (usage is null ? "" : $"; {usage.OutputTokenCount} tokens out ({usage.ReasoningTokenCount ?? 0} of them thinking), {usage.InputTokenCount} in"));
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
