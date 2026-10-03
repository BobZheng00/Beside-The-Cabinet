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

    private IChatClient? _client;
    private Conversation? _conversation;
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
    }

    /// <summary>Someone else is on the stool: talk continues in their own conversation.</summary>
    public void Seat(string characterId, Persona persona)
    {
        _answering?.Cancel();
        _conversation = new Conversation(Store, characterId, persona);
    }

    /// <summary>Sends what the user said and plays her answer on <paramref name="view"/>.</summary>
    public async Task Ask(string said, string situation, CompanionView view)
    {
        if (_client is null || _conversation is null || _answering is not null)
            return;
        var client = _client;
        var conversation = _conversation;
        using var answering = _answering = new CancellationTokenSource(AnswerTimeout);
        bool Here() => GodotObject.IsInstanceValid(view) && conversation == _conversation;     // not swapped for someone else meanwhile
        try
        {
            view.BeginReply();
            await conversation.Answer(client, said, situation,
                                      words => { if (Here()) view.Continue(words); },
                                      (name, _) => { if (Here()) Act(view, name); },
                                      answering.Token);
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
        if (!now && !conversation.NeedsDigest(gameRunning ? TimeSpan.FromMinutes(2) : Conversation.Quiet))
            return;
        _upkeeping = true;
        try
        {
            await conversation.Digest(_client);
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
