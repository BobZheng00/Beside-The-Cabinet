namespace InsertCoin.Mind.Tests;

/// <summary>The agenda: plans and reminders from her replies and from upkeep, in every turn, going off on time.</summary>
public class AgendaTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public void Directions_are_read_as_a_time_and_what_it_is()
    {
        Assert.Equal((Friday.Date.AddDays(7).AddHours(15), false, "Dentist"), Plain(Agenda.ParseDirection("2026-10-09 15:00 | Dentist", false, Friday)));
        Assert.Equal((Friday.Date.AddDays(7), true, "Calculus exam"), Plain(Agenda.ParseDirection("2026-10-09 | Calculus exam", false, Friday)));
        Assert.Equal((Friday.Date.AddDays(7), true, "Exam today"), Plain(Agenda.ParseDirection("2026-10-09 | Exam today", true, Friday)));   // goes off that morning
        Assert.Null(Agenda.ParseDirection("next Friday | Dentist", false, Friday));
        Assert.Null(Agenda.ParseDirection("2026-10-09 15:00", false, Friday));
        Assert.Equal(Friday.Offset, Agenda.ParseDirection("2026-10-09 15:00 | Dentist", false, Friday)!.At.Offset);

        static (DateTime, bool, string)? Plain(PlanSpec? p) => p is { } x ? (x.At.DateTime, x.AllDay, x.Text) : null;
    }

    [Fact]
    public async Task A_reminder_asked_for_in_conversation_is_set_shown_in_every_turn_and_goes_off_on_time()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        var acted = new List<string>();

        var answer = await conversation.Answer(new FakeModel("[remind: 2026-10-02 21:00 | Call Mum] Sure, I'll remind you at nine!"),
                                               "remind me to call mum at 9", "", _ => { }, (name, _) => acted.Add(name));

        Assert.Equal("Sure, I'll remind you at nine!", answer);
        Assert.Equal(["remind"], acted);
        var reminder = Assert.Single(temp.Store.Agenda());
        Assert.Equal(("Call Mum", true, "shino"), (reminder.Text, reminder.Remind, reminder.By));

        // the same again (she says it twice, or upkeep finds it later) is not a second reminder; one already past is not set
        await conversation.Answer(new FakeModel("[remind: 2026-10-02 21:00 | Call mum] Got it."), "don't forget", "", _ => { }, (_, _) => { });
        await conversation.Answer(new FakeModel("[remind: 2026-10-02 08:00 | Breakfast] Oops."), "remind me at 8am today", "", _ => { }, (_, _) => { });
        Assert.Single(temp.Store.Agenda());

        var next = new FakeModel("Not yet!");
        await conversation.Answer(next, "is it time?", "", _ => { }, (_, _) => { });
        Assert.Contains("<agenda>\nComing up:\n- Fri 2 Oct 21:00 (in an hour): Call Mum (a reminder goes off then)\n</agenda>\n<cabinet>", next.Requests.Single()[^1].Text);
        Assert.Contains("[remind: YYYY-MM-DD HH:MM | what to remind them of]", next.Requests.Single()[0].Text);

        Assert.Empty(Agenda.Due(temp.Store.Agenda(), Friday.AddMinutes(89)));
        Assert.Equal([reminder.Id], Agenda.Due(temp.Store.Agenda(), Friday.AddMinutes(90)).Select(p => p.Id));
        temp.Store.Reminded(reminder.Id, Friday.AddMinutes(90));
        Assert.Empty(Agenda.Due(temp.Store.Agenda(), Friday.AddMinutes(91)));
    }

    [Fact]
    public void Past_items_are_mentioned_for_two_days_then_leave_the_agenda()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        store.AddPlan("Calculus exam", Friday.Date.AddDays(1), allDay: true, before: null, Friday, null);
        store.AddPlan("Job interview", Friday.Date.AddDays(10).AddHours(10), allDay: false, before: null, Friday, null);
        var missed = store.AddPlan("Water the plants", Friday.AddHours(1), allDay: false, before: TimeSpan.Zero, Friday, "shino");

        Assert.Equal("""
            <agenda>
            Coming up:
            - Fri 2 Oct 20:30 (in an hour): Water the plants (a reminder goes off then)
            - Sat 3 Oct (tomorrow): Calculus exam
            </agenda>

            """.ReplaceLineEndings("\n"), Agenda.Block(store.Agenda(), Friday));
        var sunday = Friday.AddDays(2);
        Assert.Contains("Just past:\n- Fri 2 Oct 20:30 (2 days ago): Water the plants\n- Sat 3 Oct (yesterday): Calculus exam\n", Agenda.Block(store.Agenda(), sunday));
        Assert.Contains("- Mon 12 Oct 10:00 (in 5 days): Job interview", Agenda.Block(store.Agenda(), Friday.AddDays(5)));
        Assert.DoesNotContain("Job interview", Agenda.Block(store.Agenda(), Friday.AddDays(2.5)));       // more than a week ahead

        store.ClosePast(Friday.AddDays(4));
        Assert.Equal(["Water the plants", "Job interview"], store.Agenda().Select(p => p.Text));     // a reminder that never went off stays, to be mentioned
        store.Reminded(missed, Friday.AddDays(4));
        store.ClosePast(Friday.AddDays(4));
        Assert.Equal(["Job interview"], store.Agenda().Select(p => p.Text));
    }

    [Fact]
    public void A_reminder_moved_to_a_new_time_goes_off_again()
    {
        using var temp = new TempStore();
        var id = temp.Store.AddPlan("Stretch", Friday.AddHours(1), allDay: false, before: TimeSpan.Zero, Friday, null);
        temp.Store.Reminded(id, Friday.AddHours(1));

        temp.Store.UpdatePlan(id, "Stretch", Friday.AddHours(1), allDay: false, TimeSpan.Zero, Repeat.Once);
        Assert.NotNull(temp.Store.Agenda().Single().Reminded);                     // only renamed: stays done
        temp.Store.UpdatePlan(id, "Stretch again", Friday.AddHours(3), allDay: false, TimeSpan.Zero, Repeat.Once);
        Assert.Null(temp.Store.Agenda().Single().Reminded);
    }

    [Fact]
    public async Task Upkeep_files_dated_things_on_the_agenda_and_keeps_it_up_to_date()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var exam = store.AddPlan("Calculus exam", Friday.Date.AddDays(7), allDay: true, before: null, Friday.AddDays(-3), null);
        var party = store.AddPlan("Mia's party", Friday.Date.AddDays(1).AddHours(19), allDay: false, before: null, Friday.AddDays(-3), null);
        store.Add("shino", Speaker.User, "the exam moved to the 12th, Mia cancelled her party, and I have a dentist appointment Tuesday at 3. remind me Tuesday", Friday);
        var conversation = new Conversation(store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel();
        model.Answers.Enqueue($$"""
            {"episode": null, "facts": [], "plans": [
              {"op": "update", "id": {{exam}}, "when": "2026-10-12"},
              {"op": "cancel", "id": {{party}}},
              {"op": "add", "when": "2026-10-06 15:00", "text": "Dentist appointment"},
              {"op": "add", "when": "2026-10-06", "text": "Dentist at 15:00 today", "remind": true},
              {"op": "add", "when": "someday", "text": "Learn to juggle"},
              {"op": "cancel", "id": 999}]}
            """);

        Assert.True(await conversation.Digest(model));

        Assert.Contains($"The agenda:\n#{party} 2026-10-03 19:00 Mia's party\n#{exam} 2026-10-09 Calculus exam\n", model.Requests.Single()[1].Text);
        Assert.Contains("They go in \"plans\", not in facts.", model.Requests.Single()[0].Text);
        Assert.Equal([("Dentist at 15:00 today", "Tue 6 Oct", true), ("Dentist appointment", "Tue 6 Oct 15:00", false), ("Calculus exam", "Mon 12 Oct", false)],
                     store.Agenda().Select(p => (p.Text, p.Label, p.Remind)));
    }

    [Fact]
    public async Task A_calendar_s_items_are_the_calendar_s_to_change()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var sync = store.AddCalendarPlan("Work", "sync@work", "Team sync", Friday.AddDays(3), allDay: false, Friday);
        store.Add("shino", Speaker.User, "the team sync is off this week, I think", Friday);
        var conversation = new Conversation(store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel();
        model.Answers.Enqueue($$"""{"episode": null, "facts": [], "plans": [{"op": "cancel", "id": {{sync}}}, {"op": "update", "id": {{sync}}, "when": "2026-10-09 10:00"}]}""");

        Assert.True(await conversation.Digest(model));
        Assert.Contains("The agenda:\n(empty)", model.Requests.Single()[1].Text);          // upkeep never sees them: a busy calendar would fill every request
        Assert.Equal(Friday.AddDays(3), store.Agenda().Single().At);

        // asked for a reminder and a repeat on it in conversation: the reminder is added, whether it repeats stays the calendar's
        await conversation.Answer(new FakeModel("[plan: 2026-10-05 19:30 | Team sync | 10 min before, weekly] Got it."), "remind me before the team sync", "", _ => { }, (_, _) => { });
        Assert.Equal((TimeSpan.FromMinutes(10), Repeat.Once), store.Agenda().Single() is var p ? (p.Before!.Value, p.Repeat) : default);
    }

    [Fact]
    public void Forgetting_all_about_the_player_clears_the_agenda()
    {
        using var temp = new TempStore();
        temp.Store.AddPlan("Calculus exam", Friday.Date.AddDays(7), allDay: true, before: null, Friday, null);
        temp.Store.ForgetPlayer();
        Assert.Empty(temp.Store.Agenda());
    }
}
