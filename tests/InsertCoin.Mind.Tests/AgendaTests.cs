namespace InsertCoin.Mind.Tests;

/// <summary>The agenda: plans and reminders from her replies and from upkeep, in every turn, going off on time.</summary>
public class AgendaTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");
    private static DateTimeOffset On(int day, int hour) => new(2026, 10, day, hour, 0, 0, Friday.Offset);

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
        Assert.Contains($"<agenda>\nComing up:\n- #{reminder.Id} Fri 2 Oct 21:00 (in an hour): Call Mum (a reminder goes off then)\n</agenda>\n<cabinet>", next.Requests.Single()[^1].Text);
        Assert.Contains("[remind: YYYY-MM-DD HH:MM | what to remind them of]", next.Requests.Single()[0].Text);

        Assert.Empty(Agenda.Due(temp.Store.Agenda(), Friday.AddMinutes(89)));
        Assert.Equal([reminder.Id], Agenda.Due(temp.Store.Agenda(), Friday.AddMinutes(90)).Select(p => p.Id));
        temp.Store.Reminded(reminder.Id, Friday.AddMinutes(90));
        Assert.Empty(Agenda.Due(temp.Store.Agenda(), Friday.AddMinutes(91)));
    }

    [Fact]
    public void Changes_are_read_as_an_item_s_number_and_what_changes()
    {
        Assert.Equal("12 | - | - | - | -", Plain(Agenda.ParseChange("#12", Friday)));
        Assert.Equal("12 | 2026-10-05 10:00 | - | - | -", Plain(Agenda.ParseChange(" 12 | 2026-10-05 10:00", Friday)));
        Assert.Equal("12 | 2026-10-05 10:00 | 1 hour before | - | -", Plain(Agenda.ParseChange("#12 | 1 hour before | 2026-10-05 10:00", Friday)));
        Assert.Equal("12 | - | Monday to Friday, daily | - | -", Plain(Agenda.ParseChange("#12 | Monday to Friday | daily", Friday)));
        Assert.Null(Agenda.ParseChange("the dentist", Friday));
        Assert.Null(Agenda.ParseChange("#-3", Friday));
        Assert.Null(Agenda.ParseChange("", Friday));
        // a time that isn't written as one, even beside an option that is read, is left over, never dropped
        Assert.Equal("12 | - | 1 hour before | tomorrow 3pm | -", Plain(Agenda.ParseChange("#12 | tomorrow 3pm | 1 hour before", Friday)));
        Assert.Equal("12 | 2026-10-05 10:00 | - | 2026-10-06 10:00 | -", Plain(Agenda.ParseChange("#12 | 2026-10-05 10:00 | 2026-10-06 10:00", Friday)));
        Assert.Equal("12 | - | - | daily, at noon | -", Plain(Agenda.ParseChange("#12 | daily, at noon", Friday)));
        Assert.Equal("12 | - | - | remind me tomorrow 3pm | -", Plain(Agenda.ParseChange("#12 | remind me tomorrow 3pm", Friday)));
        Assert.Equal("12 | - | - | 1 hour before tomorrow at 3 | -", Plain(Agenda.ParseChange("#12 | 1 hour before tomorrow at 3", Friday)));
        Assert.Equal("12 | - | - | daily at 9:00 | -", Plain(Agenda.ParseChange("#12 | daily at 9:00", Friday)));
        Assert.Equal("12 | - | - | next Tuesday | -", Plain(Agenda.ParseChange("#12 | next Tuesday", Friday)));
        // and an option the cabinet can't do
        Assert.Equal("12 | - | - | - | no reminder | stop repeating", Plain(Agenda.ParseChange("#12 | no reminder | stop repeating", Friday)));
        Assert.Equal("12 | - | - | - | every Monday", Plain(Agenda.ParseChange("#12 | every Monday", Friday)));

        static string? Plain((long Id, (DateTimeOffset At, bool AllDay)? When, string? Options, string? Unread, string? Unable)? c) =>
            c is { } x ? $"{x.Id} | {x.When?.At.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "-"} | {x.Options ?? "-"} | {x.Unread ?? "-"} | {x.Unable ?? "-"}" : null;
    }

    [Fact]
    public async Task Asked_to_call_something_off_she_takes_it_off_the_agenda_in_that_reply_and_its_reminder_never_goes_off()
    {
        using var temp = new TempStore();
        var dentist = temp.Store.AddPlan("Dentist", On(3, 10), allDay: false, TimeSpan.FromHours(1), Friday, "shino");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel($"[cancel: #{dentist}] Done, it's off.");

        var answer = await conversation.Answer(model, "the dentist time doesn't work, take it off", "", _ => { }, (_, _) => { });

        Assert.Equal("Done, it's off.", answer);
        Assert.Contains($"- #{dentist} Sat 3 Oct 10:00 (tomorrow): Dentist (a reminder 1 hour before)", model.Requests.Single()[^1].Text);
        Assert.Contains("[cancel: #12]", model.Requests.Single()[0].Text);
        Assert.Empty(temp.Store.Agenda());
        Assert.Empty(Agenda.Due(temp.Store.Agenda(), Friday.AddDays(1).AddHours(10)));
    }

    [Fact]
    public async Task Asked_to_move_something_or_change_its_reminder_she_changes_that_same_item()
    {
        using var temp = new TempStore();
        var dentist = temp.Store.AddPlan("Dentist", On(3, 10), allDay: false, TimeSpan.FromHours(1), Friday, "shino");
        temp.Store.Reminded(dentist, Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel($"[change: #{dentist} | 2026-10-06 15:00] Moved to Tuesday at three."), "the dentist moved to Tuesday 3pm", "", _ => { }, (_, _) => { });
        var moved = Assert.Single(temp.Store.Agenda());
        Assert.Equal((dentist, "Dentist", "Tue 6 Oct 15:00", TimeSpan.FromHours(1), (DateTimeOffset?)null), (moved.Id, moved.Text, moved.Label, moved.Before!.Value, moved.Reminded));

        await conversation.Answer(new FakeModel($"[change: #{dentist} | 1 day before] I'll tell you the day before."), "remind me the day before instead", "", _ => { }, (_, _) => { });
        var reminded = Assert.Single(temp.Store.Agenda());
        Assert.Equal(("Tue 6 Oct 15:00", TimeSpan.FromDays(1)), (reminded.Label, reminded.Before!.Value));
    }

    [Fact]
    public async Task She_changes_only_an_item_she_was_shown_and_never_a_calendar_s()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var dentist = store.AddPlan("Dentist", On(3, 10), allDay: false, before: null, Friday, "shino");
        var trip = store.AddPlan("Trip to the coast", On(22, 0), allDay: true, before: null, Friday, null);    // beyond the week: not shown
        var sync = store.AddCalendarPlan("Work", "sync@work", "Team sync", Friday.AddDays(3), allDay: false, Friday);
        var conversation = new Conversation(store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel($"[cancel: #{sync}] [cancel: #{trip}] [change: #999 | 2026-10-08 10:00] [cancel: #{sync + 100}] Hm.");

        await conversation.Answer(model, "cancel the team sync and the trip", "", _ => { }, (_, _) => { });

        Assert.Contains("- Mon 5 Oct 19:30 (in 3 days): Team sync (from their calendar)", model.Requests.Single()[^1].Text);    // no number to change it by
        Assert.Contains("An item without a number is from their calendar", model.Requests.Single()[0].Text);
        Assert.Equal([dentist, sync, trip], store.Agenda().Select(p => p.Id));
        Assert.Equal(Friday.AddDays(3), store.Agenda().Single(p => p.Id == sync).At);
    }

    [Fact]
    public async Task A_cancel_of_a_number_not_on_the_agenda_is_logged_with_why_and_her_next_turn_hears_of_it()
    {
        using var temp = new TempStore();
        var dentist = temp.Store.AddPlan("Dentist", On(3, 10), allDay: false, TimeSpan.FromHours(1), Friday, "shino");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel($"[cancel: #{dentist + 7}] Done, it's off."), "take the dentist off", "", _ => { }, (_, _) => { });
        var next = new FakeModel("Oh, sorry!");
        await conversation.Answer(next, "thanks", "", _ => { }, (_, _) => { });

        Assert.Single(temp.Store.Agenda());
        var why = $"Your [cancel: #{dentist + 7}] didn't go through: there is no #{dentist + 7} on the agenda now. Tell the player briefly, then put it right or ask them.";
        var lines = temp.Store.Through("shino", long.MaxValue, 10);
        Assert.Equal(["take the dentist off", "Done, it's off.", why, "thanks", "Oh, sorry!"], lines.Select(l => l.Text));
        Assert.Equal(Speaker.Cabinet, lines[2].Speaker);
        Assert.Contains($"[cabinet] {why}", string.Join("\n", next.Requests.Single().Select(m => m.Text)));
    }

    [Fact]
    public async Task A_change_to_a_time_the_cabinet_can_t_read_leaves_the_item_as_it_was_and_her_next_turn_hears_why()
    {
        using var temp = new TempStore();
        var dentist = temp.Store.AddPlan("Dentist", On(3, 10), allDay: false, TimeSpan.FromHours(1), Friday, "shino");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel($"[change: #{dentist} | tomorrow 3pm | 30 min before] Moved to three."), "move the dentist to 3pm", "", _ => { }, (_, _) => { });
        var next = new FakeModel("Hm.");
        await conversation.Answer(next, "ok", "", _ => { }, (_, _) => { });

        var item = Assert.Single(temp.Store.Agenda());
        Assert.Equal((On(3, 10), TimeSpan.FromHours(1)), (item.At, item.Before!.Value));       // not half done: the reminder stays as well
        Assert.Contains($"[cabinet] Your [change: #{dentist} | tomorrow 3pm | 30 min before] didn't go through: the cabinet can't read \"tomorrow 3pm\" as a time: " +
                        "write it YYYY-MM-DD HH:MM (YYYY-MM-DD for a whole day).", string.Join("\n", next.Requests.Single().Select(m => m.Text)));
    }

    [Fact]
    public async Task A_reminder_that_goes_off_while_she_replies_goes_off_only_once_when_she_then_changes_it()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var stretch = store.AddPlan("Stretch", Friday.AddMinutes(10), allDay: false, TimeSpan.FromMinutes(10), Friday, "shino", Repeat.Daily);
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(store, "shino", Shino, clock);
        var wentOff = 0;
        void Tick()                                         // what the cabinet's reminder check does when one is due
        {
            foreach (var plan in Agenda.Due(store.Agenda(), clock.Now))
            {
                wentOff++;
                store.Advance(plan.Id, Agenda.NextAfter(plan, clock.Now, zone: clock.LocalTimeZone));
            }
        }

        await conversation.Answer(new FakeModel("Sure, ", $"[change: #{stretch} | 10 min before | daily] ten minutes ahead it is."), "remind me ten minutes before stretching",
                                  "", _ => { if (wentOff == 0) Tick(); }, (_, _) => { });
        Tick();

        Assert.Equal(1, wentOff);
        Assert.Equal(Friday.AddDays(1).AddMinutes(10), Assert.Single(store.Agenda()).At);
    }

    [Fact]
    public async Task A_number_called_off_and_reused_by_a_new_plan_in_the_same_reply_never_changes_the_new_item()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        store.AddPlan("Dentist", On(3, 10), allDay: false, before: null, Friday, "shino");
        var haircut = store.AddPlan("Haircut", On(4, 11), allDay: false, before: null, Friday, "shino");
        var conversation = new Conversation(store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel($"[cancel: #{haircut}] [plan: 2026-10-05 12:00 | Lunch with Sam] [change: #{haircut} | 2026-10-08 10:00] All sorted."),
                                  "no haircut, lunch with Sam on Monday instead", "", _ => { }, (_, _) => { });

        var lunch = store.Agenda().Single(p => p.Text == "Lunch with Sam");
        Assert.Equal((haircut, On(5, 12)), (lunch.Id, lunch.At));                              // its number reused, and left alone
        Assert.Equal(["Dentist", "Lunch with Sam"], store.Agenda().Select(p => p.Text));
        Assert.DoesNotContain(store.Through("shino", long.MaxValue, 10), l => l.Speaker == Speaker.Cabinet);   // the haircut is off, as she said
    }

    [Fact]
    public async Task Calling_the_same_item_off_twice_in_a_reply_is_not_a_change_that_failed()
    {
        using var temp = new TempStore();
        var dentist = temp.Store.AddPlan("Dentist", On(3, 10), allDay: false, before: null, Friday, "shino");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel($"[cancel: #{dentist}] Off it goes. [cancel: #{dentist}] [change: #{dentist} | 1 hour before] Gone."), "cancel the dentist", "", _ => { }, (_, _) => { });

        Assert.Empty(temp.Store.Agenda());
        Assert.DoesNotContain(temp.Store.Through("shino", long.MaxValue, 10), l => l.Speaker == Speaker.Cabinet);
    }

    [Fact]
    public async Task A_change_the_cabinet_can_t_make_says_what_it_can_and_a_half_read_time_or_a_bare_tag_is_refused_too()
    {
        using var temp = new TempStore();
        var dentist = temp.Store.AddPlan("Dentist", On(3, 10), allDay: false, TimeSpan.FromHours(1), Friday, "shino", Repeat.Weekly);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(new FakeModel($"[change: #{dentist} | stop repeating] [change: #{dentist} | 30 min before tomorrow at 3] [cancel] Done."),
                                  "make the dentist a one-off, and move it to tomorrow at 3", "", _ => { }, (_, _) => { });

        var item = Assert.Single(temp.Store.Agenda());
        Assert.Equal((On(3, 10), TimeSpan.FromHours(1), Repeat.Weekly), (item.At, item.Before!.Value, item.Repeat));
        Assert.Equal([
            $"Your [change: #{dentist} | stop repeating] didn't go through: the cabinet can't do \"stop repeating\" yet: a change can only move an item, " +
            "set its reminder (\"1 hour before\") or make it repeat (daily, weekdays, weekly, monthly). Tell the player briefly, then put it right or ask them.",
            $"Your [change: #{dentist} | 30 min before tomorrow at 3] didn't go through: the cabinet can't read \"30 min before tomorrow at 3\" as a time: " +
            "write it YYYY-MM-DD HH:MM (YYYY-MM-DD for a whole day). Tell the player briefly, then put it right or ask them.",
            "Your [cancel] didn't go through: it needs the item's number from the agenda, as in #12. Tell the player briefly, then put it right or ask them.",
        ], temp.Store.Through("shino", long.MaxValue, 10).Where(l => l.Speaker == Speaker.Cabinet).Select(l => l.Text));
    }

    [Fact]
    public void Past_items_are_mentioned_for_two_days_then_leave_the_agenda()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var exam = store.AddPlan("Calculus exam", Friday.Date.AddDays(1), allDay: true, before: null, Friday, null);
        var interview = store.AddPlan("Job interview", Friday.Date.AddDays(10).AddHours(10), allDay: false, before: null, Friday, null);
        var missed = store.AddPlan("Water the plants", Friday.AddHours(1), allDay: false, before: TimeSpan.Zero, Friday, "shino");

        Assert.Equal($"""
            <agenda>
            Coming up:
            - #{missed} Fri 2 Oct 20:30 (in an hour): Water the plants (a reminder goes off then)
            - #{exam} Sat 3 Oct (tomorrow): Calculus exam
            </agenda>

            """.ReplaceLineEndings("\n"), Agenda.Block(store.Agenda(), Friday));
        var sunday = Friday.AddDays(2);
        Assert.Contains($"Just past:\n- #{missed} Fri 2 Oct 20:30 (2 days ago): Water the plants\n- #{exam} Sat 3 Oct (yesterday): Calculus exam\n", Agenda.Block(store.Agenda(), sunday));
        Assert.Contains($"- #{interview} Mon 12 Oct 10:00 (in 5 days): Job interview", Agenda.Block(store.Agenda(), Friday.AddDays(5)));
        Assert.DoesNotContain("Job interview", Agenda.Block(store.Agenda(), Friday.AddDays(2.5)));       // more than a week ahead

        store.ClosePast(Friday.AddDays(4));
        Assert.Equal(["Water the plants", "Job interview"], store.Agenda().Select(p => p.Text));     // a reminder that never went off stays, to be mentioned
        store.Reminded(missed, Friday.AddDays(4));
        store.ClosePast(Friday.AddDays(4));
        Assert.Equal(["Job interview"], store.Agenda().Select(p => p.Text));
    }

    [Fact]
    public void A_reminder_to_do_something_leaves_the_agenda_once_it_has_gone_off_while_a_plan_stays_to_ask_about()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var teeth = store.AddPlan("Brush your teeth", Friday.AddMinutes(5), allDay: false, before: TimeSpan.Zero, Friday, "shino");
        var interview = store.AddPlan("Job interview", Friday.AddMinutes(5), allDay: false, before: TimeSpan.FromMinutes(30), Friday, "shino");
        store.Reminded(teeth, Friday.AddMinutes(5));
        store.Reminded(interview, Friday.AddMinutes(5));

        store.ClosePast(Friday.AddMinutes(6));

        Assert.Equal(["Job interview"], store.Agenda().Select(p => p.Text));     // she asks how that went; brushing needs no asking
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
        model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "The calculus exam moved to Monday 12 October; Mia cancelled her party; the player has a dentist appointment on Tuesday 6 October at 15:00 and asked to be reminded that day."}]}""");
        model.Answers.Enqueue($$"""
            {"facts": [], "plans": [
              {"op": "update", "id": {{exam}}, "when": "2026-10-12"},
              {"op": "cancel", "id": {{party}}},
              {"op": "add", "when": "2026-10-06 15:00", "text": "Dentist appointment"},
              {"op": "add", "when": "2026-10-06", "text": "Dentist at 15:00 today", "remind": true},
              {"op": "add", "when": "someday", "text": "Learn to juggle"},
              {"op": "cancel", "id": 999}]}
            """);

        Assert.True(await conversation.Digest(model));

        Assert.Contains($"The agenda:\n#{party} 2026-10-03 19:00 Mia's party\n#{exam} 2026-10-09 Calculus exam\n", model.Requests[1][1].Text);
        Assert.Contains("They go in \"plans\", not in facts.", model.Requests[1][0].Text);
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
        model.Answers.Enqueue("""{"observations": [{"date": "2026-10-02", "priority": "medium", "text": "The player thinks the team sync is off this week."}]}""");
        model.Answers.Enqueue($$"""{"facts": [], "plans": [{"op": "cancel", "id": {{sync}}}, {"op": "update", "id": {{sync}}, "when": "2026-10-09 10:00"}]}""");

        Assert.True(await conversation.Digest(model));
        Assert.Contains("The agenda:\n(empty)", model.Requests[1][1].Text);          // upkeep never sees them: a busy calendar would fill every request
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
