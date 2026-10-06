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
            cancellationToken.ThrowIfCancellationRequested();      // as a real client stops when the reply is called off
            yield return new ChatResponseUpdate(ChatRole.Assistant, piece);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// A model that uses tools: each streamed request gets the next prepared round, made of words (strings) and the
/// calls it makes (<see cref="FunctionCallContent"/>); after the last round it just says "Done.".
/// </summary>
internal sealed class FakeToolModel(params object[][] rounds) : IChatClient
{
    private int _round;
    public List<List<ChatMessage>> Requests { get; } = [];
    public List<ChatOptions?> Options { get; } = [];
    /// <summary>Thrown for any request that offers tools, as a model without them does.</summary>
    public Exception? RefusesTools { get; init; }

    public static FunctionCallContent Call(string name, string argument, string value) => new($"call-{name}-{value.GetHashCode():x}", name, new Dictionary<string, object?> { [argument] = value });

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("her replies are streamed");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add([.. messages]);
        Options.Add(options);
        if (RefusesTools is not null && options?.Tools is { Count: > 0 })
            throw RefusesTools;
        var round = _round < rounds.Length ? rounds[_round++] : ["Done."];
        foreach (var piece in round)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return piece is FunctionCallContent call ? new ChatResponseUpdate(ChatRole.Assistant, [call]) : new ChatResponseUpdate(ChatRole.Assistant, (string)piece);
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
