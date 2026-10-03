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
        _client = provider?.Connect(Keys.Load());
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
                    return new LocalEmbedder(model, OnDisk(folder + "model.onnx", model.Name), tokenizer);
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

    /// <summary>
    /// A real file for a model packed into the app: run from source it is the project's own file; in the built app,
    /// which keeps it inside its package, a copy in the local cache (%LOCALAPPDATA%\InsertCoin\models), made once.
    /// </summary>
    private static string OnDisk(string resource, string name)
    {
        var project = ProjectSettings.GlobalizePath(resource);
        if (File.Exists(project))
            return project;
        var cached = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "InsertCoin", "models", name,
                                  Path.GetFileName(resource));
        using var source = Godot.FileAccess.Open(resource, Godot.FileAccess.ModeFlags.Read)
                           ?? throw new FileNotFoundException($"{resource} is not in this build");
        if (File.Exists(cached) && new FileInfo(cached).Length == (long)source.GetLength())
            return cached;
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        var partial = cached + ".part";
        using (var copy = File.Create(partial))
            while (source.GetPosition() < source.GetLength())
                copy.Write(source.GetBuffer(1 << 20));                  // a megabyte at a time, never the whole model in memory
        File.Move(partial, cached, overwrite: true);
        return cached;
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
        try
        {
            view.BeginReply();
            await conversation.Answer(client, said, situation,
                                      words =>
                                      {
                                          if (!Here())
                                              return;
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
