using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>
/// A day's requests to one model for one purpose ("replies", "upkeep"), as the provider counted them: input read
/// (all of it, <see cref="Cached"/> and <see cref="Written"/> included), the part served from its cache, the part
/// written to its cache, and output (thinking included, <see cref="Reasoning"/> of it); and the web searches the provider
/// ran itself, which it charges for apart from tokens.
/// </summary>
public sealed record UsageRow(DateOnly Day, string Model, string Purpose, long Requests, long Input, long Cached, long Written, long Output, long Reasoning,
                              long Searches = 0)
{
    /// <summary>Input read at the full price: neither from the cache nor written to it.</summary>
    public long Uncached => Math.Max(0, Input - Cached - Written);
}

/// <summary>A model's prices per million tokens, as the player's provider charges them (entered by the player).</summary>
public sealed record Prices(decimal Input, decimal Cached, decimal Written, decimal Output)
{
    /// <summary>What <paramref name="row"/> cost at these prices.</summary>
    public decimal Cost(UsageRow row) =>
        (row.Uncached * Input + row.Cached * Cached + row.Written * Written + row.Output * Output) / 1_000_000m;
}

/// <summary>
/// Counts what each request used, from the provider's own report (MEAI's <see cref="UsageDetails"/>: input in all,
/// cache reads as <see cref="UsageDetails.CachedInputTokenCount"/>, cache writes, where the provider reports them,
/// among the additional counts), into the store, a row a day per model and purpose, with each web search the provider
/// ran in it (a search's results, Anthropic's or OpenAI's).
/// </summary>
public sealed class Metered(IChatClient inner, MindStore store, string model, string purpose, TimeProvider? clock = null) : DelegatingChatClient(inner)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                              CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Count(response.Usage, response.Messages.SelectMany(m => m.Contents).Count(c => c is WebSearchToolResultContent));
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                     [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        var searches = 0;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
                searches += update.Contents.Count(c => c is WebSearchToolResultContent);
                yield return update;
            }
        }
        finally
        {
            Count(usage, searches);
        }
    }

    private void Count(UsageDetails? usage, int searches)
    {
        if (usage is null && searches == 0)
            return;
        usage ??= new UsageDetails();
        var written = usage.AdditionalCounts?.Where(kv => kv.Key.Contains("CacheCreation", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value) ?? 0;
        store.AddUsage(DateOnly.FromDateTime(_clock.GetLocalNow().DateTime), model, purpose, usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0,
                       written, usage.OutputTokenCount ?? 0, usage.ReasoningTokenCount ?? 0, searches);
    }
}
