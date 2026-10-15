namespace InsertCoin.Mind.Tests;

/// <summary>The memory book saves each edit as the player leaves the field, writing only that, and never over a change of hers it hadn't shown.</summary>
public class MemoryEditsTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    /// <summary>Her upkeep after a talk: the exam fact changes (the old one retired, a new one learned) and Rust is no longer true.</summary>
    private static async Task Upkeep(MindStore store, FakeClock clock, long exam, long rust)
    {
        store.Add("shino", Speaker.User, "My exam got moved to Monday. I dropped Rust btw.", clock.Now);
        clock.Now = clock.Now.AddMinutes(11);
        var model = new FakeModel();
        model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "The player's exam moved to Monday; they dropped Rust."}]}""");
        model.Answers.Enqueue($$"""
            {"facts": [
              {"op": "update", "id": {{exam}}, "text": "The player's exam is on Monday 5 October.", "importance": 8},
              {"op": "delete", "id": {{rust}}}]}
            """);
        Assert.True(await new Conversation(store, "shino", Shino, clock).Digest(model));
    }

    [Fact]
    public async Task An_edit_saved_after_upkeep_changed_other_facts_writes_only_itself_and_brings_nothing_back()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var clock = new FakeClock(Friday);
        var hades = store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday.AddDays(-5));
        var exam = store.AddFact(MindStore.Shared, "The player has an exam next week.", 6, Friday.AddDays(-5));
        var rust = store.AddFact(MindStore.Shared, "The player is learning Rust.", 5, Friday.AddDays(-5));
        store.SetNote(MindStore.Shared, MindStore.AboutUser, "Bob, studying for exams.");
        var shown = store.Facts("shino");                                   // the book opens

        await Upkeep(store, clock, exam, rust);                             // while the player is typing in the Hades fact
        store.SetNote(MindStore.Shared, MindStore.AboutUser, "Bob, studying for exams in October.");

        Assert.Equal(Kept.Saved, MemoryEdits.Fact(store, "shino", shown.Single(f => f.Id == hades), "The player loves Hades and Celeste."));

        Assert.Equal(["The player loves Hades and Celeste.", "The player's exam is on Monday 5 October."], store.Facts("shino").Select(f => f.Text));
        Assert.Equal([exam, rust], store.Retired("shino").Select(f => f.Id));
        Assert.Equal("Bob, studying for exams in October.", store.Note(MindStore.Shared, MindStore.AboutUser));
    }

    [Fact]
    public async Task An_edit_to_a_fact_upkeep_changed_meanwhile_is_not_saved_until_the_player_keeps_theirs_and_hers_stays_too()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var clock = new FakeClock(Friday);
        var exam = store.AddFact(MindStore.Shared, "The player has an exam next week.", 6, Friday.AddDays(-5));
        var rust = store.AddFact(MindStore.Shared, "The player is learning Rust.", 5, Friday.AddDays(-5));
        var shown = store.Facts("shino").Single(f => f.Id == exam);

        await Upkeep(store, clock, exam, rust);                             // while the player is typing in that very fact

        Assert.Equal(Kept.Gone, MemoryEdits.Fact(store, "shino", shown, "The player's exam is on Tuesday."));
        Assert.Equal(["The player's exam is on Monday 5 October."], store.Facts("shino").Select(f => f.Text));       // nothing written

        Assert.Equal(exam, MemoryEdits.KeepFact(store, "shino", shown, "The player's exam is on Tuesday.", clock.Now).Id);
        Assert.Equal(["The player's exam is on Tuesday.", "The player's exam is on Monday 5 October."], store.Facts("shino").Select(f => f.Text));
    }

    [Fact]
    public void A_fact_she_reworded_meanwhile_clashes_with_the_players_words_and_one_she_forgot_comes_back_written_anew()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var hades = store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday);
        var shown = store.Facts("shino").Single();
        store.UpdateFact(hades, "The player enjoys Hades.");                // her review, in other words

        Assert.Equal(Kept.Clash, MemoryEdits.Fact(store, "shino", shown, "The player loves Hades."));
        Assert.Equal(Kept.Clash, MemoryEdits.Fact(store, "shino", shown, ""));          // not forgotten over her change either
        Assert.Equal(Kept.Same, MemoryEdits.Fact(store, "shino", shown, "The player enjoys Hades. "));
        Assert.Equal("The player enjoys Hades.", store.Facts("shino").Single().Text);
        Assert.Equal(Kept.Saved, MemoryEdits.Fact(store, "shino", shown with { Text = "The player enjoys Hades." }, ""));                  // emptied: forgotten
        Assert.Empty(store.Facts("shino"));

        Assert.Equal(Kept.Gone, MemoryEdits.Fact(store, "shino", shown, "The player loves Hades."));
        var back = MemoryEdits.KeepFact(store, "shino", shown, "The player loves Hades.", Friday);
        Assert.Equal(back, store.Facts("shino").Single());
        Assert.Equal("The player loves Hades.", back.Text);
    }

    [Fact]
    public void A_note_is_saved_only_over_what_the_book_showed()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        store.SetNote(MindStore.Shared, MindStore.AboutUser, "Bob.");

        Assert.Equal(Kept.Saved, MemoryEdits.Note(store, MindStore.Shared, MindStore.AboutUser, "Bob.", " Bob, studying. "));
        Assert.Equal("Bob, studying.", store.Note(MindStore.Shared, MindStore.AboutUser));
        Assert.Equal(Kept.Same, MemoryEdits.Note(store, MindStore.Shared, MindStore.AboutUser, "Bob.", "Bob, studying."));
        Assert.Equal(Kept.Clash, MemoryEdits.Note(store, MindStore.Shared, MindStore.AboutUser, "Bob.", "Bob, working."));
        Assert.Equal("Bob, studying.", store.Note(MindStore.Shared, MindStore.AboutUser));
    }

    [Fact]
    public void An_agenda_edit_writes_only_its_own_part_onto_the_item_as_she_left_it()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var id = store.AddPlan("Take vitamins", Friday.AddHours(13), allDay: false, TimeSpan.Zero, Friday, "shino", Repeat.Daily);
        var shown = store.Agenda().Single();
        var next = shown.At.AddDays(1);
        store.UpdatePlan(id, shown.Text, next, false, shown.Before, shown.Repeat);            // its reminder went off: on to tomorrow

        Assert.Equal(Kept.Saved, MemoryEdits.PlanText(store, shown, "Take vitamins with water"));
        Assert.Equal(("Take vitamins with water", next), (store.Agenda().Single().Text, store.Agenda().Single().At));

        Assert.Equal(Kept.Clash, MemoryEdits.PlanWhen(store, shown, Friday.AddHours(14), false));   // she moved it, and so did the player
        Assert.Equal(next, store.Agenda().Single().At);

        Assert.Equal(Kept.Saved, MemoryEdits.PlanChoice(store, shown, p => p with { Before = TimeSpan.FromMinutes(10) }));
        Assert.Equal(("Take vitamins with water", next, TimeSpan.FromMinutes(10)),
                     (store.Agenda().Single().Text, store.Agenda().Single().At, store.Agenda().Single().Before));
        Assert.Equal(Kept.Same, MemoryEdits.PlanChoice(store, shown, p => p with { Repeat = Repeat.Daily }));

        store.DeletePlan(id);
        Assert.Equal(Kept.Gone, MemoryEdits.PlanText(store, shown, "Vitamins"));
    }

    [Fact]
    public void An_item_forgotten_meanwhile_is_gone_even_when_a_new_one_took_its_number()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var hades = store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday.AddDays(-2));
        var fact = store.Facts("shino").Single();
        store.AddPlan("Gym", Friday.AddHours(20), false, null, Friday.AddHours(-1), null);
        var plan = store.Agenda().Single();
        store.DeleteFact(hades);                                           // forgotten (Chat history's Forget), then something new learned
        store.DeletePlan(plan.Id);                                         // cancelled, then something new put on the agenda
        Assert.Equal(hades, store.AddFact(MindStore.Shared, "The player has a cat.", 5, Friday));
        Assert.Equal(plan.Id, store.AddPlan("Dentist", Friday.AddHours(22), false, null, Friday, "shino"));

        Assert.Equal(Kept.Gone, MemoryEdits.Fact(store, "shino", fact, "The player loves Hades."));
        Assert.Equal(Kept.Gone, MemoryEdits.PlanText(store, plan, "Gym with Sam"));
        Assert.Equal(Kept.Gone, MemoryEdits.PlanChoice(store, plan, p => p with { Before = TimeSpan.Zero }));
        Assert.Equal("The player has a cat.", store.Facts("shino").Single().Text);
        Assert.Equal(("Dentist", (TimeSpan?)null), (store.Agenda().Single().Text, store.Agenda().Single().Before));
    }

    [Fact]
    public void An_agenda_item_kept_twice_after_it_left_the_agenda_is_put_back_once()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var id = store.AddPlan("Gym", Friday.AddHours(20), false, TimeSpan.Zero, Friday, null);
        var shown = store.Agenda().Single();
        store.DeletePlan(id);

        var back = MemoryEdits.KeepPlan(store, shown, "Gym with Sam", Friday.AddHours(21), false, TimeSpan.Zero, Repeat.Once, Friday);
        var again = MemoryEdits.KeepPlan(store, shown, "Gym with Sam", Friday.AddHours(21), false, TimeSpan.Zero, Repeat.Once, Friday);

        Assert.Equal(back, again);
        Assert.Equal(("Gym with Sam", Friday.AddHours(21)), (store.Agenda().Single().Text, store.Agenda().Single().At));
    }

    [Fact]
    public void Her_change_the_player_never_answered_is_kept_as_what_the_fact_used_to_say_when_theirs_is_kept_on_closing()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var hades = store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday);
        var shown = store.Facts("shino").Single();
        store.UpdateFact(hades, "The player enjoys Hades a lot.");         // hers, while the player typed

        Assert.Equal(Kept.Clash, MemoryEdits.Fact(store, "shino", shown, "The player loves Hades."));
        var kept = MemoryEdits.KeepFact(store, "shino", shown, "The player loves Hades.", Friday.AddHours(1), keepHers: true);

        Assert.Equal((hades, "The player loves Hades."), (kept.Id, kept.Text));
        var hers = Assert.Single(store.Retired("shino"));
        Assert.Equal(("The player enjoys Hades a lot.", "changed to: The player loves Hades."), (hers.Text, hers.Why));
    }
}
