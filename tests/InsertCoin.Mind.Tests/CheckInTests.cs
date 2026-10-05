using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>She asks how things went, only when she has not had the chance; and usage counted from what providers report.</summary>
public class CheckInTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 14, 0, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");
    private static readonly TimeSpan After = TimeSpan.FromMinutes(15);

    [Fact]
    public void She_asks_about_what_the_player_told_her_once_it_is_over_and_not_about_reminders_repeats_or_calendars()
    {
        Plan P(string text, DateTimeOffset at, TimeSpan? before = null, Repeat repeat = Repeat.Once, string? source = null, DateTimeOffset? asked = null) =>
            new(text.Length, text, at, false, before, null, Monday.AddDays(-2), null, repeat, source, asked);
        var interview = P("Job interview", Monday.AddHours(-3), TimeSpan.FromHours(1));
        var plans = new[]
        {
            interview,
            P("Call Mum", Monday.AddHours(-2), TimeSpan.Zero),                         // a reminder to do something
            P("Stand-up", Monday.AddHours(-4), repeat: Repeat.Weekdays),               // comes round every day
            P("Team sync", Monday.AddHours(-1), source: "Work"),                       // from a calendar
            P("Dentist", Monday.AddHours(-5), asked: Monday.AddHours(-4)),             // asked already
            P("Lunch with Mia", Monday.AddMinutes(-10)),                               // only just over
            P("Exam", Monday.AddDays(-3)),                                             // long past
        };

        Assert.Equal(["Job interview"], Agenda.ToAskAbout(plans, Monday, lastTalked: Monday.AddHours(-6), After).Select(p => p.Text));
        Assert.Empty(Agenda.ToAskAbout(plans, Monday, lastTalked: Monday.AddHours(-1), After));        // they have talked since: she had her chance
        Assert.Contains("They had \"Job interview\" (Mon 5 Oct 11:00, 3 hours ago) and you have not heard how it went.", Agenda.CheckIn(interview, Monday));
    }

    [Fact]
    public async Task Asked_once_she_does_not_ask_again()
    {
        using var temp = new TempStore();
        var id = temp.Store.AddPlan("Job interview", Monday.AddHours(-3), allDay: false, before: null, Monday.AddDays(-1), null);
        temp.Store.Add("shino", Speaker.User, "wish me luck", Monday.AddHours(-4));
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Monday));
        Assert.Equal(id, conversation.ToAskAbout(After)?.Id);

        var model = new FakeModel("So, how did the interview go?");
        await conversation.Announce(model, Agenda.CheckIn(conversation.ToAskAbout(After)!, Monday), "", _ => { }, (_, _) => { });
        temp.Store.Asked(id, Monday);

        Assert.Null(conversation.ToAskAbout(After));
        Assert.NotNull(temp.Store.Agenda().Single().Asked);
    }

    private sealed class Reporting(params UsageDetails[] usages) : IChatClient
    {
        private int _next;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usages[_next++] });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            yield return new ChatResponseUpdate { Contents = [new UsageContent(usages[_next++])] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Usage_is_counted_by_day_model_and_purpose_and_priced_with_the_cache_in_mind()
    {
        using var temp = new TempStore();
        var anthropicLike = new UsageDetails
        {
            InputTokenCount = 1050, CachedInputTokenCount = 900, OutputTokenCount = 20,
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["CacheCreationInputTokens"] = 50 },
        };
        var openAiLike = new UsageDetails { InputTokenCount = 2000, CachedInputTokenCount = 0, OutputTokenCount = 300, ReasoningTokenCount = 200 };
        using var replies = new Metered(new Reporting(anthropicLike, anthropicLike), temp.Store, "claude-x", "replies", new FakeClock(Monday));
        using var upkeep = new Metered(new Reporting(openAiLike), temp.Store, "claude-x", "upkeep", new FakeClock(Monday));

        await foreach (var _ in replies.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])) { }
        await replies.GetResponseAsync("hi again");
        await upkeep.GetResponseAsync("digest");

        var rows = temp.Store.Usage(DateOnly.FromDateTime(Monday.DateTime));
        var talk = rows.Single(r => r.Purpose == "replies");
        Assert.Equal((2L, 2100L, 1800L, 100L, 40L, 200L), (talk.Requests, talk.Input, talk.Cached, talk.Written, talk.Output, talk.Uncached));
        var prices = new Prices(Input: 3m, Cached: 0.3m, Written: 3.75m, Output: 15m);
        Assert.Equal((200 * 3m + 1800 * 0.3m + 100 * 3.75m + 40 * 15m) / 1_000_000m, prices.Cost(talk));
        Assert.Equal((2000L, 0L, 300L, 200L), rows.Single(r => r.Purpose == "upkeep") is var u ? (u.Uncached, u.Cached, u.Output, u.Reasoning) : default);
    }
}
