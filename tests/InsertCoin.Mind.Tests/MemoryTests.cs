namespace InsertCoin.Mind.Tests;

public class MemoryTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static Fact F(long id, string text, int importance = 5, bool pinned = false, string scope = MindStore.Shared) =>
        new(id, scope, text, importance, Friday.AddDays(-3), null, pinned);

    // ---- Recall ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Recall_finds_facts_sharing_words_even_in_other_forms()
    {
        var facts = new[] { F(1, "The player has a calculus exam on Friday 9 October."), F(2, "The player likes Hades."), F(3, "The player's cat is called Mochi.") };

        var found = Recall.Find("I'm so nervous about my exams", facts, [], [], "Shino", Friday);

        Assert.Equal([1L], found.Select(m => m.FactId!.Value));
    }

    [Fact]
    public void Recall_matches_chinese_by_pairs_of_characters()
    {
        var facts = new[] { F(1, "玩家下周五有数学考试。"), F(2, "玩家喜欢玩《哈迪斯》。") };

        var found = Recall.Find("考试好难啊", facts, [], [], "Shino", Friday);

        Assert.Equal([1L], found.Select(m => m.FactId!.Value));
    }

    [Fact]
    public void Recall_keeps_pinned_facts_and_drops_unrelated_ones()
    {
        var facts = new[] { F(1, "The player's name is Bob.", 10, pinned: true), F(2, "The player likes Hades.") };

        var found = Recall.Find("what should I eat tonight", facts, [], [], "Shino", Friday);

        Assert.Equal([1L], found.Select(m => m.FactId!.Value));
    }

    [Fact]
    public void Recall_quotes_old_talk_and_episodes_with_their_dates()
    {
        var episodes = new[] { new Episode(1, "Bob planned a graph problem marathon.", 1, 5, Friday.AddDays(-7)) };
        var excerpts = new[] { new Excerpt("Player: graph problems scare me\nShino: We'll start small.", Friday.AddDays(-7), null) };

        var found = Recall.Find("more graph problems today?", [], episodes, excerpts, "Shino", Friday);

        Assert.Contains(found, m => m.Text == "Fri 25 Sep (7 days ago): Bob planned a graph problem marathon.");
        Assert.Contains(found, m => m.Text == "Said on Fri 25 Sep (7 days ago): Player: graph problems scare me Shino: We'll start small.");
    }

    [Fact]
    public async Task The_whole_conversation_before_the_window_can_be_found_again()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday.AddDays(-200));
        // two sittings long ago, then a long gap: the first mentions the dentist, the second does not
        temp.Store.Add("shino", Speaker.User, "my dentist appointment is at 3", clock.Now);
        temp.Store.Add("shino", Speaker.Her, "Good luck at the dentist!", clock.Now.AddMinutes(1));
        temp.Store.Add("shino", Speaker.Cabinet, "The player solved \"Two Sum\" (Easy): +1 token.", clock.Now.AddMinutes(2));
        temp.Store.Add("shino", Speaker.User, "what a day", clock.Now.AddHours(5));
        var last = temp.Store.Add("shino", Speaker.Her, "Rest well.", clock.Now.AddHours(5).AddMinutes(1));
        temp.Store.Observe("shino", [new Observation(0, clock.Now, Priority.Low, "Long ago they talked about everyday things.", 1, last)], last, clock.Now.AddHours(6));
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("Oh, the dentist!");

        await conversation.Answer(model, "do you remember my dentist?", "", _ => { }, (_, _) => { });

        Assert.Equal(2, temp.Store.Passages("shino").Count);                   // one per sitting, the cabinet's line left out
        Assert.DoesNotContain("Two Sum", temp.Store.Passages("shino")[0].Text);
        var turn = model.Requests.Single()[^1].Text;
        Assert.Contains("Said on Mon 16 Mar (6 months ago): Player: my dentist appointment is at 3 Shino: Good luck at the dentist!", turn);

        temp.Store.ForgetConversation("shino");
        Assert.Empty(conversation.History.Search("dentist", long.MaxValue));   // cleared with the conversation
    }

    // ---- Notes taken during a reply --------------------------------------------------------------------------------

    [Fact]
    public async Task A_note_in_a_reply_is_kept_at_once_and_comes_back_when_relevant()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var acted = new List<string>();

        var answer = await conversation.Answer(new FakeModel("[note: The player has a job interview on Tuesday 6 October.] Got it, I'll remember!"),
                                               "I have a job interview next Tuesday", "", _ => { }, (name, _) => acted.Add(name));

        Assert.Equal("Got it, I'll remember!", answer);
        Assert.Equal(["note"], acted);
        var fact = Assert.Single(temp.Store.Facts("shino"));
        Assert.Equal((MindStore.Shared, "The player has a job interview on Tuesday 6 October.", 7), (fact.Scope, fact.Text, fact.Importance));

        var next = new FakeModel("Good luck!");
        await conversation.Answer(next, "the interview is tomorrow, wish me luck", "", _ => { }, (_, _) => { });
        Assert.Contains("<memories>\n- The player has a job interview on Tuesday 6 October.\n</memories>", next.Requests.Single()[^1].Text);
        Assert.NotNull(temp.Store.Facts("shino").Single().Recalled);
    }

    [Fact]
    public async Task Something_told_twice_is_kept_once()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        await conversation.Answer(new FakeModel("[note: The player has an interview on Tuesday 6 October.] Noted!"), "interview tuesday", "", _ => { }, (_, _) => { });
        await conversation.Answer(new FakeModel("[note: The player has an interview on Tuesday, 6 October!] Noted!"), "interview tuesday!", "", _ => { }, (_, _) => { });

        Assert.Single(temp.Store.Facts("shino"));
        Assert.True(Recall.Same("玩家下周二有面试。", "玩家下周二有面试！"));
        Assert.False(Recall.Same("The player likes Hades.", "The player likes Celeste."));
    }

    // ---- Upkeep ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Upkeep_reads_json_even_wrapped_in_a_code_fence()
    {
        var digestion = Upkeep.Parse("""
            ```json
            {"episode": "They talked about exams.", "facts": [{"op": "add", "about": "player", "text": "Bob has exams.", "importance": 8}, {"op": "delete", "id": 3}, 7]}
            ```
            """);

        Assert.NotNull(digestion);
        Assert.Equal("They talked about exams.", digestion.Episode);
        Assert.Equal([new FactChange("add", null, "player", "Bob has exams.", 8), new FactChange("delete", 3, null, null, null)], digestion.Changes);
        Assert.Null(Upkeep.Parse("Sorry, I can't do that."));
    }

    [Fact]
    public async Task Upkeep_files_an_episode_reconciles_facts_and_rewrites_the_notes_they_touch()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var store = temp.Store;
        var exam = store.AddFact(MindStore.Shared, "The player has an exam next week.", 6, Friday.AddDays(-5));
        var stale = store.AddFact(MindStore.Shared, "The player is learning Rust.", 5, Friday.AddDays(-5));
        var pinned = store.AddFact(MindStore.Shared, "The player's name is Bob.", 10, Friday.AddDays(-5));
        store.PinFact(pinned, true);
        store.SetNote(MindStore.Shared, MindStore.AboutUser, "Bob, studying for exams.");
        store.Add("shino", Speaker.User, "My exam got moved to Monday 12 October. I dropped Rust btw.", Friday);
        store.Add("shino", Speaker.Her, "Monday then! Call me Coach from now on?", Friday);
        var conversation = new Conversation(store, "shino", Shino, clock);
        Assert.False(conversation.NeedsDigest(Conversation.Quiet));         // still going on
        clock.Now = Friday.AddMinutes(11);
        Assert.True(conversation.NeedsDigest(Conversation.Quiet));

        var model = new FakeModel();
        model.Answers.Enqueue($$"""
            {"episode": "Fri 2 Oct: the exam moved to Monday 12 October; Bob dropped Rust.", "facts": [
              {"op": "update", "id": {{exam}}, "text": "The player's exam is on Monday 12 October 2026.", "importance": 8},
              {"op": "delete", "id": {{stale}}},
              {"op": "delete", "id": {{pinned}}},
              {"op": "add", "about": "us", "text": "The player calls Shino 'Coach'.", "importance": 6},
              {"op": "update", "id": 999, "text": "nonsense"}]}
            """);
        model.Answers.Enqueue("Bob. Exam on Monday 12 October 2026.");          // about the player
        model.Answers.Enqueue("Bob calls Shino 'Coach'.");                       // the two of them

        Assert.True(await conversation.Digest(model));

        var facts = store.Facts("shino");
        Assert.Equal(["The player's exam is on Monday 12 October 2026.", "The player's name is Bob.", "The player calls Shino 'Coach'."],
                     facts.Select(f => f.Text));
        Assert.Equal("shino", facts.Single(f => f.Text.Contains("Coach")).Scope);
        Assert.Equal("Fri 2 Oct: the exam moved to Monday 12 October; Bob dropped Rust.", Assert.Single(store.Episodes("shino")).Text);
        Assert.Equal("Bob. Exam on Monday 12 October 2026.", store.Note(MindStore.Shared, MindStore.AboutUser));
        Assert.Equal("Bob calls Shino 'Coach'.", store.Note("shino", MindStore.Us));
        Assert.Contains("Bob, studying for exams.", model.Requests[1][1].Text);    // the rewrite starts from the note as it was
        Assert.False(conversation.NeedsDigest(TimeSpan.Zero));
        Assert.Contains($"#{exam} [player, 6] The player has an exam next week.", model.Requests[0][1].Text);
        Assert.Contains("[Fri 2 Oct 2026 19:30] Player: My exam got moved", model.Requests[0][1].Text);
    }

    [Fact]
    public async Task Upkeep_that_cannot_be_read_is_retried_then_skipped()
    {
        using var temp = new TempStore();
        temp.Store.Add("shino", Speaker.User, "hi", Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday.AddHours(1)));
        var model = new FakeModel("I'm not JSON");

        Assert.False(await conversation.Digest(model));
        Assert.False(await conversation.Digest(model));
        Assert.True(conversation.NeedsDigest(Conversation.Quiet));
        Assert.False(await conversation.Digest(model));

        Assert.False(conversation.NeedsDigest(Conversation.Quiet));         // given up on that stretch
        Assert.Empty(temp.Store.Facts("shino"));
    }

    [Fact]
    public void Cabinet_events_alone_do_not_call_for_upkeep()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday.AddHours(1)));
        conversation.Witness("The player solved \"Two Sum\" (Easy): +1 token.");
        conversation.Said("+1 coin!");

        Assert.False(conversation.NeedsDigest(Conversation.Quiet));
    }
}
