namespace InsertCoin.Mind.Tests;

/// <summary>Memory that tends itself: a quiet review merges what says the same thing and retires what is obsolete, kept to restore.</summary>
public class ReviewTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");
    private const string Nothing = """{"merge": [], "retire": []}""";

    [Fact]
    public async Task A_review_merges_and_retires_but_leaves_what_is_pinned_and_retired_facts_can_come_back()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var exam = store.AddFact(MindStore.Shared, "The player has an exam on Monday 12 October 2026.", 8, Friday.AddDays(-10));
        var again = store.AddFact(MindStore.Shared, "The player's exam is on 12 October.", 7, Friday.AddDays(-4));
        var film = store.AddFact(MindStore.Shared, "The player wants to see a film on Saturday 5 September 2026.", 4, Friday.AddDays(-40));
        var name = store.AddFact(MindStore.Shared, "The player's name is Bob.", 10, Friday.AddDays(-40));
        store.PinFact(name, true);
        store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday.AddDays(-20));
        var conversation = new Conversation(store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel();
        model.Answers.Enqueue($$"""
            {"merge": [{"ids": [{{exam}}, {{again}}], "text": "The player's exam is on Monday 12 October 2026.", "importance": 8}],
             "retire": [{"id": {{film}}, "why": "the film date has passed"}, {"id": {{name}}, "why": "?"}, {"id": 999}]}
            """);
        model.Answers.Enqueue("Bob. Exam on Monday 12 October 2026.");     // the note about the player, rewritten

        Assert.True(await conversation.Review(model));
        Assert.True(await conversation.RefreshNotes(model));

        var asked = model.Requests[0];
        Assert.Contains("Today is Friday 2 October 2026", asked[0].Text);
        Assert.Contains($"#{film} [player, 4] The player wants to see a film on Saturday 5 September 2026. (learned 23 Aug 2026; last came up never)", asked[1].Text);
        Assert.Contains($"#{name} [player, 10, pinned]", asked[1].Text);
        Assert.Equal(["The player's name is Bob.", "The player likes Hades.", "The player's exam is on Monday 12 October 2026."],
                     store.Facts("shino").Select(f => f.Text));
        var merged = store.Facts("shino")[^1];
        Assert.Equal((8, Friday.AddDays(-10)), (merged.Importance, merged.Learned));          // as old as the oldest it came from
        const string into = "merged into \"The player's exam is on Monday 12 October 2026.\"";
        Assert.Equal([(exam, into), (again, into), (film, "the film date has passed")],
                     store.Retired("shino").Select(f => (f.Id, f.Why)));
        Assert.Equal("Bob. Exam on Monday 12 October 2026.", store.Note(MindStore.Shared, MindStore.AboutUser));
        Assert.False(await conversation.Review(model));                                       // not due again for a week

        // it comes up again: back to the foreground, not a second copy
        store.Add("shino", Speaker.User, "remember that film I never saw?", Friday.AddHours(1));
        model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "The player asked about the film they never saw."}]}""");
        model.Answers.Enqueue("""{"facts": [{"op": "add", "about": "player", "text": "The player wants to see a film on Saturday 5 September 2026.", "importance": 4}]}""");
        Assert.True(await conversation.Digest(model));
        Assert.Contains(store.Facts("shino"), f => f.Id == film);
        Assert.DoesNotContain(store.Retired("shino"), f => f.Id == film);
        Assert.Equal(4, store.Facts("shino").Count);
    }

    [Fact]
    public async Task Reviews_come_weekly_or_after_many_new_facts_a_batch_at_a_time()
    {
        using var temp = new TempStore();
        for (var i = 0; i < 100; i++)
            temp.Store.AddFact(MindStore.Shared, $"The player likes snack number {i}.", 3, Friday);
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        var model = new FakeModel(Nothing);
        int Read() => model.Requests[^1][1].Text.Split('\n').Length;

        Assert.True(await conversation.Review(model));
        Assert.Equal(Conversation.ReviewAtOnce, Read());
        Assert.True(await conversation.Review(model));                    // the rest, from where it stopped
        Assert.Equal(20, Read());
        Assert.False(await conversation.Review(model));

        clock.Now = Friday.AddDays(3);
        for (var i = 0; i < Conversation.ReviewAfterNew; i++)
            temp.Store.AddFact(MindStore.Shared, $"The player has hobby number {i}.", 3, clock.Now);
        Assert.True(await conversation.Review(model));                    // many new facts: due early

        while (await conversation.Review(model)) { }
        clock.Now = Friday.AddDays(11);
        Assert.True(await conversation.Review(model));                    // a week on
        Assert.Equal(130, temp.Store.Facts("shino").Count);               // a review that changes nothing changes nothing
    }

    [Fact]
    public async Task A_review_that_cannot_be_read_tries_again_from_the_same_place()
    {
        using var temp = new TempStore();
        temp.Store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        Assert.False(await conversation.Review(new FakeModel("I'd rather not.")));
        Assert.True(await conversation.Review(new FakeModel(Nothing)));
    }
}
