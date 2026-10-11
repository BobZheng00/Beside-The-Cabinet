namespace InsertCoin.Mind.Tests;

/// <summary>Open threads: what is going on with no date, kept from her notes and asked about once, a couple of days on.</summary>
public class ThreadsTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public async Task Upkeep_opens_a_thread_from_her_notes_updates_it_with_news_and_closes_it_when_settled()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        var model = new FakeModel();
        async Task Talk(string said, string change)
        {
            temp.Store.Add("shino", Speaker.User, said, clock.Now);
            clock.Now = clock.Now.AddMinutes(11);
            model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "NOTE"}]}""".Replace("NOTE", said));
            model.Answers.Enqueue("""{"facts": [], "plans": [], "threads": [CHANGE]}""".Replace("CHANGE", change));
            Assert.True(await conversation.Digest(model));
        }

        await Talk("my cat is sick, she's at the vet", """{"op": "open", "text": "The player's cat is sick and at the vet."}""");
        await Talk("the cat again", """{"op": "open", "text": "The player's cat is sick and at the vet."}""");       // already open: not twice
        var cat = Assert.Single(temp.Store.Threads());
        Assert.Contains($"Open threads:\n#{cat.Id} (since 2 Oct) The player's cat is sick and at the vet.", model.Requests[^1][1].Text);

        await Talk("the vet says she'll be fine", $$"""{"op": "update", "id": {{cat.Id}}, "text": "The player's cat is recovering."}""");
        Assert.Equal("The player's cat is recovering.", Assert.Single(temp.Store.Threads()).Text);
        await Talk("she's all better!", $$"""{"op": "close", "id": {{cat.Id}}}""");
        Assert.Empty(temp.Store.Threads());
    }

    [Fact]
    public void A_thread_is_asked_about_once_a_couple_of_days_after_it_came_up_again_after_news_and_lapses_after_three_weeks()
    {
        var cat = new OpenThread(1, "The player's cat is sick.", Friday, Friday, null);

        Assert.Null(Threads.ToAskAbout([cat], Friday.AddDays(1)));                     // too soon
        Assert.Equal(cat, Threads.ToAskAbout([cat], Friday.AddDays(2)));
        var asked = cat with { Asked = Friday.AddDays(2) };
        Assert.Null(Threads.ToAskAbout([asked], Friday.AddDays(5)));                   // once
        var news = asked with { Mentioned = Friday.AddDays(4) };
        Assert.Equal(news, Threads.ToAskAbout([news], Friday.AddDays(6)));             // news since: asked about again, later
        Assert.Null(Threads.ToAskAbout([cat], Friday.AddDays(22)));                    // lapsed

        var note = Threads.CheckIn(cat, Friday.AddDays(2));
        Assert.StartsWith("They told you about this ", note);
        Assert.Contains("\"The player's cat is sick.\", and you have not heard how it is going.", note);
    }
}
