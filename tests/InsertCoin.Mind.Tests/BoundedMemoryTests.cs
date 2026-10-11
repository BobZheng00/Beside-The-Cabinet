namespace InsertCoin.Mind.Tests;

/// <summary>Memory that stops growing: small talk leaves no notes, and facts are kept from her notes against the related ones only.</summary>
public class BoundedMemoryTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public async Task Small_talk_leaves_no_notes_and_nothing_to_keep_facts_from()
    {
        using var temp = new TempStore();
        temp.Store.Add("shino", Speaker.User, "hi", Friday);
        temp.Store.Add("shino", Speaker.Her, "Hi!", Friday.AddMinutes(1));
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday.AddHours(1)));
        var model = new FakeModel();
        model.Answers.Enqueue("""{"observations": []}""");

        Assert.True(await conversation.Digest(model));

        Assert.Single(model.Requests);                                      // read, and nothing to keep from it
        Assert.Empty(temp.Store.Observations("shino"));
        Assert.Equal(2, temp.Store.Digested("shino"));
        Assert.False(conversation.NeedsDigest(TimeSpan.Zero));
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
        model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "The player's interview moved to Monday 5 October."}]}""");
        model.Answers.Enqueue("""{"facts": []}""");

        Assert.True(await conversation.Digest(model));

        var asked = model.Requests[1][1].Text;                              // facts are kept from her notes
        Assert.Contains("Her new notes:\nFri 2 Oct 2026\n- ! The player's interview moved to Monday 5 October.", asked);
        Assert.Contains("interview is on Friday 9 October", asked);         // related by its words
        Assert.Contains("name is Bob", asked);                              // essential, whatever the topic
        Assert.InRange(asked.Split("snack number").Length - 1, 1, Conversation.DigestFacts);
    }
}
