namespace InsertCoin.Mind.Tests;

/// <summary>What she had in mind for each reply, kept with it, for the player to see in the chat history and correct.</summary>
public class InMindTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Then = Friday.AddDays(-200);
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private sealed record Memory(TempStore Temp, Conversation Conversation, long Job, long Promise, long Violin, long Fear, long Dentist);

    /// <summary>
    /// A memory with something of every kind: her lists (about the player, the two of them, herself), a smaller fact, one
    /// no longer true, a note out of view and a sitting long ago, all of the last four about the dentist.
    /// </summary>
    private static Memory Seeded()
    {
        var temp = new TempStore();
        var store = temp.Store;
        store.Add("shino", Speaker.User, "my dentist appointment is at 3", Then);
        var last = store.Add("shino", Speaker.Her, "Good luck at the dentist!", Then.AddMinutes(1));
        store.Observe("shino", [new Observation(0, Then, Priority.Medium, "Bob went to the dentist.", 1, last)], last);
        store.Condense("shino", [store.Observations("shino")[0].Id], [new Observation(0, Then, Priority.Medium, "A quiet week.", 1, last, Span.Week)]);
        store.Fold("shino", last, Then.AddHours(1));
        var job = store.AddFact(MindStore.Shared, "The player works at Acme.", 7, Friday.AddDays(-3));
        var promise = store.AddFact("shino", "Bob promised Shino a Hard problem.", 6, Friday.AddDays(-2));
        var violin = store.AddFact(MindStore.Herself("shino"), "Shino used to play the violin.", 7, Friday.AddDays(-1));
        var fear = store.AddFact(MindStore.Shared, "The player is scared of the dentist.", 4, Friday.AddDays(-5));
        var dentist = store.AddFact(MindStore.Shared, "The player's dentist is Dr Ruiz.", 5, Friday.AddDays(-40));
        store.Retire(dentist, $"{Conversation.Changed} The player's dentist is Dr Lee.", Friday.AddDays(-4));
        return new Memory(temp, new Conversation(store, "shino", Shino, new FakeClock(Friday)), job, promise, violin, fear, dentist);
    }

    private static long Latest(MindStore store, Speaker speaker) => store.After("shino", 0).Last(l => l.Speaker == speaker).Id;

    [Fact]
    public async Task Each_reply_keeps_her_lists_then_what_recall_brought_each_with_its_kind_and_date()
    {
        var (temp, conversation, job, promise, violin, fear, dentist) = Seeded();
        using var owned = temp;

        await conversation.Answer(new FakeModel("Again? You'll be fine."), "off to the dentist again", "", _ => { }, (_, _) => { });
        var reply = Latest(temp.Store, Speaker.Her);
        var kept = temp.Store.InMind(reply);

        Assert.Equal([(Minded.You, job, Friday.AddDays(-3)), (Minded.Us, promise, Friday.AddDays(-2)), (Minded.Me, violin, Friday.AddDays(-1))],
                     kept.Where(k => k.Listed).Select(k => (k.Kind, k.Id, k.At)));
        var recalled = kept.Where(k => !k.Listed).ToList();
        Assert.Contains(recalled, k => k.Kind == Minded.You && k.Id == fear && k.At == Friday.AddDays(-5) && k.Text == "The player is scared of the dentist.");
        Assert.Contains(recalled, k => k.Kind == Minded.Was && k.Id == dentist && k.At == Friday.AddDays(-4));    // dated when it stopped being true
        Assert.Contains(recalled, k => k.Kind == Minded.Note && k.Text == "Bob went to the dentist." && k.At == Then);
        Assert.Contains(recalled, k => k.Kind == Minded.Said && k.Text == "Player: my dentist appointment is at 3\nShino: Good luck at the dentist!" && k.At == Then);
        Assert.All(kept, k => Assert.Equal(k.Kind is Minded.You or Minded.Us or Minded.Me, k.Editable));

        Assert.Empty(temp.Store.InMind(Latest(temp.Store, Speaker.User)));        // only her replies keep anything
        Assert.Equal([reply], temp.Store.WithInMind(1, reply + 10));
    }

    [Fact]
    public async Task What_she_is_made_to_forget_is_gone_from_her_next_reply_an_edited_fact_reads_as_edited_and_the_talk_stays_word_for_word()
    {
        var (temp, conversation, job, promise, _, _, dentist) = Seeded();
        using var owned = temp;
        var store = temp.Store;
        await conversation.Answer(new FakeModel("Again? You'll be fine."), "off to the dentist again", "", _ => { }, (_, _) => { });
        var reply = Latest(store, Speaker.Her);
        var kept = store.InMind(reply);

        foreach (var memory in kept.Where(k => k.Id == promise || k.Kind is Minded.Note or Minded.Said or Minded.Was))
            store.ForgetInMind(memory);
        store.UpdateFact(job, "The player works at Initech.");
        var next = new FakeModel("Hm.");
        await conversation.Answer(next, "the dentist again, ugh", "", _ => { }, (_, _) => { });

        var sent = string.Join("\n", next.Requests.Single().Select(m => m.Text));
        Assert.DoesNotContain("Hard problem", sent);
        Assert.DoesNotContain("Bob went to the dentist", sent);
        Assert.DoesNotContain("my dentist appointment is at 3", sent);
        Assert.DoesNotContain("Dr Ruiz", sent);
        Assert.Contains("The player works at Initech.", sent);
        var now = store.InMind(reply);
        Assert.DoesNotContain(now, k => k.Id == promise || k.Kind is Minded.Note or Minded.Said or Minded.Was);
        Assert.Contains(now, k => k.Id == job && k.Text == "The player works at Initech.");
        Assert.Equal(["my dentist appointment is at 3", "Good luck at the dentist!"], store.Between("shino", 1, 2).Select(l => l.Text));

        // a forgotten fact's id, taken again by a new one, is not taken for what that reply had in mind
        Assert.Equal(dentist, store.AddFact(MindStore.Shared, "The player likes tea.", 7, Friday));
        Assert.DoesNotContain(store.InMind(reply), k => k.Text == "The player likes tea.");
    }

    [Fact]
    public async Task Clearing_the_chat_takes_what_its_replies_had_in_mind_with_it()
    {
        var (temp, conversation, _, _, _, _, _) = Seeded();
        using var owned = temp;
        await conversation.Answer(new FakeModel("Again? You'll be fine."), "off to the dentist again", "", _ => { }, (_, _) => { });
        var reply = Latest(temp.Store, Speaker.Her);

        temp.Store.ForgetConversation("shino");

        Assert.Empty(temp.Store.InMind(reply));
        Assert.Empty(temp.Store.WithInMind(1, long.MaxValue));
    }
}
