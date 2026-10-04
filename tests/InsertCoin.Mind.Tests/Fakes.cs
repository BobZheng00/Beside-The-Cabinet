using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>A model that answers with prepared text (streamed in the given pieces) and remembers what it was sent.</summary>
internal sealed class FakeModel(params string[] pieces) : IChatClient
{
    public List<List<ChatMessage>> Requests { get; } = [];
    public List<ChatOptions?> Options { get; } = [];
    public Exception? Fails { get; init; }
    /// <summary>Answers for non-streamed requests, one per request, before falling back to the pieces.</summary>
    public Queue<string> Answers { get; } = new();

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add([.. messages]);
        Options.Add(options);
        if (Fails is not null)
            throw Fails;
        var text = Answers.Count > 0 ? Answers.Dequeue() : string.Concat(pieces);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add([.. messages]);
        Options.Add(options);
        if (Fails is not null)
            throw Fails;
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, piece);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>A clock that stands still unless moved.</summary>
internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("test", Now.Offset, "test", "test");
}

/// <summary>A store in a temporary file, deleted afterwards.</summary>
internal sealed class TempStore : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));
    public MindStore Store { get; }

    public TempStore() => Store = new MindStore(Path.Combine(_folder, "memory.db"));

    public void Dispose()
    {
        Store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
