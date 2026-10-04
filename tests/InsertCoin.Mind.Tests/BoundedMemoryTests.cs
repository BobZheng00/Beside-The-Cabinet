namespace InsertCoin.Mind.Tests;

/// <summary>Memory that stops growing: a few moments a day at most, none for small talk, roll-ups, and digests against related facts.</summary>
public class BoundedMemoryTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static string Digested(string? episode, long? continues = null) =>
        $$"""{"episode": {{(episode is null ? "null" : $"\"{episode}\"")}}, "continues": {{(continues is { } id ? id.ToString() : "null")}}, "facts": []}""";

    [Fact]
    public async Task Small_talk_files_no_moment_and_a_busy_day_gets_up_to_three()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        var model = new FakeModel();
        async Task Talk(string said, int halfHoursLater, string answer)
        {
            temp.Store.Add("shino", Speaker.User, said, Friday.AddMinutes(30 * halfHoursLater));
            model.Answers.Enqueue(answer);
            Assert.True(await conversation.Digest(model));
        }

        await Talk("hi", 0, Digested(null));
        Assert.Empty(temp.Store.Episodes("shino"));                          // only a greeting: nothing to remember

        await Talk("I have an interview on Friday", 1, Digested("Bob told her about his interview."));
        var interview = temp.Store.Episodes("shino").Single().Id;
        await Talk("it's for a backend role", 2, Digested("Bob told her about his interview for a backend role.", continues: interview));
        Assert.Contains($"The day's moments so far:\n#{interview} Bob told her about his interview.", model.Requests[^1][1].Text);
        await Talk("I finally beat Hades", 3, Digested("Bob beat Hades and they celebrated."));                     // something else
        await Talk("going to the gym", 4, Digested("Bob went to the gym."));
        Assert.Equal(["Bob told her about his interview for a backend role.", "Bob beat Hades and they celebrated.", "Bob went to the gym."],
                     temp.Store.Episodes("shino").Select(e => e.Text));

        await Talk("cooked dinner", 5, Digested("Bob went to the gym, then cooked dinner."));                   // the day is full
        Assert.Equal(3, temp.Store.Episodes("shino").Count);
        Assert.Equal("Bob went to the gym, then cooked dinner.", temp.Store.Episodes("shino")[^1].Text);
        Assert.Contains("rewrite the one the new part is closest to", model.Requests[^1][0].Text);
        Assert.Contains($"at most {Conversation.MomentWords} words", model.Requests[^1][0].Text);

        await Talk("morning", 48, Digested("A new day: Bob is nervous."));
        Assert.Equal(4, temp.Store.Episodes("shino").Count);                 // the next day has its own
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
        Assert.Contains("at most 100 words", model.Requests.Single()[0].Text);
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
