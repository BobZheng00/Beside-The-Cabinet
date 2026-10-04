namespace InsertCoin.Mind.Tests;

/// <summary>Memory that stops growing: one moment a day at most, none for small talk, roll-ups, and digests against related facts.</summary>
public class BoundedMemoryTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static string Digested(string? episode) =>
        $$"""{"episode": {{(episode is null ? "null" : $"\"{episode}\"")}}, "facts": []}""";

    [Fact]
    public async Task Small_talk_files_no_moment_and_a_second_talk_the_same_day_rewrites_the_day_s_moment()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        var model = new FakeModel();

        temp.Store.Add("shino", Speaker.User, "hi", Friday);
        temp.Store.Add("shino", Speaker.Her, "Hi!", Friday);
        model.Answers.Enqueue(Digested(null));
        Assert.True(await conversation.Digest(model));
        Assert.Empty(temp.Store.Episodes("shino"));                          // only a greeting: nothing to remember

        temp.Store.Add("shino", Speaker.User, "I have an interview on Friday", Friday.AddHours(1));
        model.Answers.Enqueue(Digested("Bob told her about his interview."));
        Assert.True(await conversation.Digest(model));
        temp.Store.Add("shino", Speaker.User, "it's for a backend role", Friday.AddHours(2));
        model.Answers.Enqueue(Digested("Bob told her about his interview for a backend role."));
        Assert.True(await conversation.Digest(model));

        var day = Assert.Single(temp.Store.Episodes("shino"));
        Assert.Equal("Bob told her about his interview for a backend role.", day.Text);
        Assert.Contains("The day's moment so far:\nBob told her about his interview.", model.Requests[^1][1].Text);

        temp.Store.Add("shino", Speaker.User, "morning", Friday.AddDays(1));
        model.Answers.Enqueue(Digested("A new day: Bob is nervous."));
        Assert.True(await conversation.Digest(model));
        Assert.Equal(2, temp.Store.Episodes("shino").Count);                 // the next day has its own
    }

    [Fact]
    public async Task Old_moments_roll_up_into_months_and_months_into_years()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        temp.Store.AddEpisode("shino", "Week one of June.", 1, 2, new DateTimeOffset(2026, 6, 1, 20, 0, 0, Friday.Offset), Span.Week);
        temp.Store.AddEpisode("shino", "A day in late June.", 3, 4, new DateTimeOffset(2026, 6, 24, 20, 0, 0, Friday.Offset));
        temp.Store.AddEpisode("shino", "Last week.", 5, 6, Friday.AddDays(-7));
        var model = new FakeModel("June: they got to know each other.");

        Assert.True(await conversation.Consolidate(model));

        Assert.Contains("all from June 2026", model.Requests.Single()[0].Text);
        var moments = temp.Store.Episodes("shino");
        Assert.Equal([(Span.Month, "June: they got to know each other."), (Span.Day, "Last week.")], moments.Select(e => (e.Span, e.Text)));
        Assert.Equal("June 2026", moments[0].Label);

        clock.Now = Friday.AddYears(3);
        temp.Store.AddEpisode("shino", "July.", 7, 8, new DateTimeOffset(2026, 7, 1, 20, 0, 0, Friday.Offset), Span.Month);
        model = new FakeModel("2026: the year they met.");
        Assert.True(await conversation.Consolidate(model));
        Assert.Contains(temp.Store.Episodes("shino"), e => e is { Span: Span.Year, Label: "2026" });
    }

    [Fact]
    public async Task Upkeep_reconciles_against_the_facts_that_relate_not_all_of_them()
    {
        using var temp = new TempStore();
        for (var i = 0; i < 100; i++)
            temp.Store.AddFact(MindStore.Shared, $"The player likes snack number {i}.", 3, Friday);
        temp.Store.AddFact(MindStore.Shared, "The player's interview is on Friday 9 October 2026.", 7, Friday);
        temp.Store.AddFact(MindStore.Shared, "The player's name is Bob.", 10, Friday);
        temp.Store.Add("shino", Speaker.User, "the interview got moved to Monday", Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel();
        model.Answers.Enqueue("""{"episode": null, "facts": []}""");

        Assert.True(await conversation.Digest(model));

        var asked = model.Requests.Single()[1].Text;
        Assert.Contains("interview is on Friday 9 October", asked);              // related by its words
        Assert.Contains("name is Bob", asked);                                  // essential, whatever the topic
        Assert.InRange(asked.Split("snack number").Length - 1, 1, Conversation.DigestFacts);
    }
}
