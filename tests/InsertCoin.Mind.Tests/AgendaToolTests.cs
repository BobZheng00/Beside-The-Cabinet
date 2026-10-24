using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>Her agenda as a tool, and a reply that ends as she writes it down (said as written, spike #105's flow C).</summary>
public class AgendaToolTests
{
    private static readonly DateTimeOffset Wednesday = new(2026, 10, 21, 19, 30, 0, TimeSpan.FromHours(-4));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static FunctionCallContent Agenda(string id, params (string Key, object? Value)[] arguments) =>
        new($"call-{id}", BuiltIn.AgendaName, arguments.ToDictionary(a => a.Key, a => a.Value));

    private static (Conversation, Toolbox) Set(TempStore temp, TimeSpan? lead = null) =>
        (new Conversation(temp.Store, "shino", Shino, new FakeClock(Wednesday)) { ReminderLead = () => lead }, new Toolbox([]));

    private static IEnumerable<string> Results(List<ChatMessage> request) =>
        request.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString() ?? "");

    /// <summary>What the cabinet's lines in the conversation say: each call she made, and what came of it.</summary>
    private static List<string> Recorded(TempStore temp) =>
        [.. temp.Store.After("shino", 0).Where(l => l.Speaker == Speaker.Cabinet).Select(l => l.Text)];

    [Fact]
    public async Task A_call_that_goes_through_after_her_words_ends_the_reply_with_no_second_request()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Gym, Mondays at seven. Got it.",
            Agenda("gym", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym"), ("remind", "before"), ("minutes_before", 60), ("repeat", "weekly"))]);

        var answer = await conversation.Answer(model, "gym every monday at 7, remind me an hour before", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Gym, Mondays at seven. Got it.", answer);
        Assert.Single(model.Requests);
        // "every Monday": weekly, from a Monday
        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal(("Gym", new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.FromHours(-4)), TimeSpan.FromHours(1), Repeat.Weekly, DayOfWeek.Monday),
                     (plan.Text, plan.At, plan.Before, plan.Repeat, plan.At.DayOfWeek));
        // what was done is in the conversation, for her next turn
        Assert.Contains(Recorded(temp), l => l.Contains($"Added #{plan.Id}: Gym, Mon 26 Oct 07:00, reminder 1 hour before, every week"));
    }

    [Fact]
    public async Task A_time_the_cabinet_can_t_read_comes_back_to_her_rather_than_being_dropped()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Dentist next Tuesday, noted.", Agenda("bad", ("action", "add"), ("when", "next Tuesday"), ("what", "Dentist"), ("remind", "none"))],
                                      ["Wait, what time?"]);

        var answer = await conversation.Answer(model, "dentist next tuesday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Dentist next Tuesday, noted. Wait, what time?", answer);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("can't read \"next Tuesday\" as a time"));
        Assert.Empty(temp.Store.Agenda());
    }

    [Fact]
    public async Task A_call_with_no_words_before_it_goes_back_so_she_speaks_once()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel([Agenda("quiet", ("action", "add"), ("when", "2026-10-23"), ("what", "Essay due"), ("remind", "none"))], ["Friday, essay. On the list."]);

        var answer = await conversation.Answer(model, "essay due friday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Friday, essay. On the list.", answer);
        Assert.Equal(2, model.Requests.Count);
        Assert.Single(temp.Store.Agenda());
    }

    [Fact]
    public async Task Every_call_of_a_round_is_made_and_one_failing_sends_the_round_back()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Monday and Wednesday, both down.",
            Agenda("mon", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym"), ("remind", "none")),
            Agenda("wed", ("action", "add"), ("when", "2026-10-28T07:00"), ("what", "Gym"), ("remind", "none"), ("repeat", "every other week"))],
            ["Every other week I can't do, so just this Wednesday?"]);

        await conversation.Answer(model, "gym monday and every other wednesday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(2, Results(model.Requests[1]).Count());
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("can't repeat \"every other week\""));
        Assert.Single(temp.Store.Agenda());
    }

    [Fact]
    public async Task Two_calls_in_one_round_that_both_go_through_are_both_made_and_end_the_reply()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Monday and Wednesday, both down.",
            Agenda("mon", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym"), ("remind", "none")),
            Agenda("wed", ("action", "add"), ("when", "2026-10-28T07:00"), ("what", "Gym"), ("remind", "none"))]);

        await conversation.Answer(model, "gym monday and wednesday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Single(model.Requests);
        Assert.Equal(2, temp.Store.Agenda().Count);
    }

    [Fact]
    public async Task A_round_with_another_tool_in_it_goes_back_to_her()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        tools.Put(BuiltIn.PrintMemo((_, _) => "Printed."));
        var model = new FakeToolModel(["Friday's essay, down and on a memo.",
            Agenda("essay", ("action", "add"), ("when", "2026-10-23"), ("what", "Essay due"), ("remind", "none")),
            new FunctionCallContent("call-memo", "print_memo", new Dictionary<string, object?> { ["title"] = "Essay", ["text"] = "Due Friday" })], ["There you go."]);

        await conversation.Answer(model, "essay due friday, and print it for me", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task No_reminder_sets_none_and_the_result_says_so()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Mia's birthday, Saturday. No reminder, then.",
            Agenda("mia", ("action", "add"), ("when", "2026-10-24"), ("what", "Mia's birthday"), ("remind", "none"), ("minutes_before", 0))]);

        await conversation.Answer(model, "mia's birthday saturday, no reminder, i won't forget", "", _ => { }, (_, _) => { }, tools: tools);

        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Null(plan.Before);
        Assert.Contains(Recorded(temp), l => l.Contains($"Added #{plan.Id}: Mia's birthday, Sat 24 Oct (whole day), no reminder"));
    }

    [Fact]
    public async Task Remind_me_ten_minutes_before_sets_a_reminder_ten_minutes_ahead()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Dentist Friday at half three, a nudge ten minutes before.",
            Agenda("dentist", ("action", "add"), ("when", "2026-10-23T15:30"), ("what", "Dentist"), ("remind", "before"), ("minutes_before", 10))]);

        await conversation.Answer(model, "dentist friday at 3:30. remind me, 10 min before", "", _ => { }, (_, _) => { }, tools: tools);

        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal((new DateTimeOffset(2026, 10, 23, 15, 30, 0, TimeSpan.FromHours(-4)), TimeSpan.FromMinutes(10)), (plan.At, plan.Before));
        Assert.Contains(Recorded(temp), l => l.Contains("Dentist, Fri 23 Oct 15:30, reminder 10 min before"));
    }

    [Theory]
    [InlineData(null, null, "It didn't work")]                   // left out: the reminder is never assumed
    [InlineData("before", null, "needs minutes_before")]
    [InlineData("keep", null, "not \"keep\"")]                  // nothing to keep on something new
    [InlineData("sometimes", null, "not \"sometimes\"")]
    public async Task An_add_without_a_reminder_chosen_comes_back_to_her(string? remind, int? minutes, string why)
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        (string, object?)[] arguments = [("action", "add"), ("when", "2026-10-23T15:30"), ("what", "Dentist"), .. remind is null ? [] : new[] { ("remind", (object?)remind) },
                                         .. minutes is null ? [] : new[] { ("minutes_before", (object?)minutes) }];
        var model = new FakeToolModel(["Dentist, Friday.", Agenda("dentist", arguments)], ["Shall I remind you?"]);

        await conversation.Answer(model, "dentist friday at 3:30", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(Results(model.Requests[1]), r => r.Contains(why));
        Assert.Empty(temp.Store.Agenda());
    }

    [Fact]
    public async Task A_timed_item_with_no_reminder_of_its_own_is_said_to_have_the_player_s_usual_one()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, lead: TimeSpan.FromMinutes(15));
        var model = new FakeToolModel(["Interview Thursday at two, written down.",
            Agenda("job", ("action", "add"), ("when", "2026-10-29T14:00"), ("what", "Job interview"), ("remind", "none"))]);

        await conversation.Answer(model, "job interview next thursday at 2", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Contains(Recorded(temp), l => l.Contains("Job interview, Thu 29 Oct 14:00, reminder 15 min before, as the player has for everything on their agenda"));
    }

    [Fact]
    public async Task A_change_and_a_cancel_go_by_the_number_she_was_shown()
    {
        using var temp = new TempStore();
        var gym = temp.Store.AddPlan("Gym", new DateTimeOffset(2026, 10, 26, 7, 0, 0, Wednesday.Offset), false, TimeSpan.FromHours(1), Wednesday, "shino", Repeat.Weekly);
        var call = temp.Store.AddPlan("Call mum", new DateTimeOffset(2026, 10, 24, 18, 0, 0, Wednesday.Offset), false, null, Wednesday, "shino");
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Gym at eight then, and no call.",
            Agenda("move", ("action", "change"), ("item", gym), ("when", "2026-10-26T08:00"), ("remind", "keep")),
            Agenda("off", ("action", "cancel"), ("item", call), ("remind", "keep"))]);

        await conversation.Answer(model, "move the gym to 8, and the call's off", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Single(model.Requests);
        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal((gym, 8, TimeSpan.FromHours(1), Repeat.Weekly), (plan.Id, plan.At.Hour, plan.Before, plan.Repeat));
        Assert.Contains(Recorded(temp), l => l.Contains($"Changed #{gym}: Gym, Mon 26 Oct 08:00, reminder 1 hour before, every week"));
        Assert.Contains(Recorded(temp), l => l.Contains($"Cancelled #{call}: Call mum, off the agenda with any reminder it had"));
    }

    [Fact]
    public async Task A_change_can_take_a_reminder_away_or_stop_a_repeat()
    {
        using var temp = new TempStore();
        var gym = temp.Store.AddPlan("Gym", new DateTimeOffset(2026, 10, 26, 7, 0, 0, Wednesday.Offset), false, TimeSpan.FromHours(1), Wednesday, "shino", Repeat.Weekly);
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Just this Monday, no nudge.", Agenda("once", ("action", "change"), ("item", gym), ("remind", "none"), ("repeat", "once"))]);

        await conversation.Answer(model, "only this monday, and don't remind me", "", _ => { }, (_, _) => { }, tools: tools);

        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal((null, Repeat.Once), (plan.Before, plan.Repeat));
        Assert.Contains(Recorded(temp), l => l.Contains($"Changed #{gym}: Gym, Mon 26 Oct 07:00, no reminder."));
    }

    [Fact]
    public async Task A_calendar_s_item_or_a_number_not_shown_comes_back_to_her()
    {
        using var temp = new TempStore();
        var sync = temp.Store.AddCalendarPlan("Work", "sync@work", "Team sync", new DateTimeOffset(2026, 10, 22, 10, 0, 0, Wednesday.Offset), allDay: false, Wednesday);
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Off with the sync, and #99.",
            Agenda("sync", ("action", "cancel"), ("item", sync), ("remind", "keep")),
            Agenda("none", ("action", "change"), ("item", 99L), ("when", "2026-10-22T11:00"), ("remind", "keep"))], ["Ah, those aren't mine to change."]);

        await conversation.Answer(model, "cancel the sync", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("is from their calendar, which decides it"));
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("there is no #99 on the agenda now"));
        Assert.Single(temp.Store.Agenda());
    }

    [Theory]
    [InlineData("2026-10-23T15:30", "2026-10-23T15:30:00-04:00")]          // no offset: the player's own time
    [InlineData("2026-10-23T15:30-04:00", "2026-10-23T15:30:00-04:00")]    // the player's own offset
    [InlineData("2026-11-02T09:00-05:00", "2026-11-02T09:00:00-05:00")]    // after the clocks go back, the new offset is theirs
    [InlineData("2026-10-23T15:30Z", null)]                                 // turned into UTC: 11:30 there, set wrong
    [InlineData("2026-11-02T09:00-04:00", null)]                            // the old offset after the clocks go back
    public async Task A_time_with_an_offset_not_the_player_s_comes_back_to_her(string when, string? set)
    {
        using var temp = new TempStore();
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var conversation = new Conversation(temp.Store, "shino", Shino, new ZonedClock(Wednesday, eastern));
        var model = new FakeToolModel(["The dentist, noted.", Agenda("dentist", ("action", "add"), ("when", when), ("what", "Dentist"), ("remind", "none"))],
                                      ["Hm, let me put that in your time."]);

        await conversation.Answer(model, "dentist", "", _ => { }, (_, _) => { }, tools: new Toolbox([]));

        if (set is null)
        {
            Assert.Equal(2, model.Requests.Count);
            Assert.Contains(Results(model.Requests[1]), r => r.Contains($"\"{when}\" is in another time zone from the player's"));
            Assert.Empty(temp.Store.Agenda());
        }
        else
        {
            Assert.Single(model.Requests);
            var plan = Assert.Single(temp.Store.Agenda());
            Assert.Equal((DateTimeOffset.Parse(set, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(set, System.Globalization.CultureInfo.InvariantCulture).Offset),
                         (plan.At, plan.At.Offset));
        }
    }

    /// <summary>A clock that stands still, in a real time zone (so a date across a clock change has its own offset).</summary>
    private sealed class ZonedClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    [Fact]
    public async Task Something_already_past_isn_t_added()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["Lunch, noted.", Agenda("lunch", ("action", "add"), ("when", "2026-10-21T12:00"), ("what", "Lunch"), ("remind", "none"))], ["Oh, that's been and gone."]);

        await conversation.Answer(model, "lunch at noon", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Contains(Results(model.Requests[1]), r => r.Contains("Not added: Wed 21 Oct 12:00 is already past"));
        Assert.Empty(temp.Store.Agenda());
    }

    [Fact]
    public async Task A_provider_without_tools_still_has_her_tags()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["[plan: 2026-10-26 07:00 | Gym | 1 hour before, weekly] Gym on Monday, got it."])
        {
            RefusesTools = new InvalidOperationException("registry.ollama.ai/library/gemma:2b does not support tools"),
        };

        var answer = await conversation.Answer(model, "gym monday at 7", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.True(tools.Refused);
        Assert.Equal("Gym on Monday, got it.", answer);
        Assert.Contains("[plan: YYYY-MM-DD HH:MM | what it is]", model.Requests[^1][0].Text);
        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal(("Gym", TimeSpan.FromHours(1), Repeat.Weekly), (plan.Text, plan.Before, plan.Repeat));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ja")]                                      // her voice's paragraph has an example of its own
    [InlineData("en")]
    public async Task With_tools_she_is_told_of_the_agenda_tool_and_not_the_tags(string? voice)
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        conversation.VoiceLanguage = voice;
        var model = new FakeToolModel(["Hi!"]);

        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);

        var told = string.Join("\n", model.Requests[0].Select(m => m.Text));
        Assert.Contains(model.Options[0]!.Tools!, t => t.Name == BuiltIn.AgendaName);
        Assert.Contains("with the agenda tool", told);
        Assert.DoesNotContain("[plan", told);
        Assert.DoesNotContain("[remind", told);
        if (voice is not null)
            Assert.Contains("[look] [say: ...]", told);
    }

    [Fact]
    public void Without_tools_her_voice_s_example_still_shows_a_plan_before_what_she_says()
    {
        var told = Prompt.Build(Shino, "", "", null, [], "", "hi", voiceLanguage: "ja")[0].Text;

        Assert.Contains("[plan: ...] [say: ...]", told);
        Assert.Contains("[plan: YYYY-MM-DD HH:MM | what it is]", told);
    }

    [Fact]
    public async Task A_plan_tag_she_writes_beside_the_agenda_call_is_not_set_twice()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["[plan: 2026-10-23 15:30 | Dentist appointment] Friday, half three, the dentist.",
            Agenda("dentist", ("action", "add"), ("when", "2026-10-23T15:30"), ("what", "Dentist"), ("remind", "before"), ("minutes_before", 10))]);

        var answer = await conversation.Answer(model, "dentist friday at 3:30, remind me 10 min before", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Friday, half three, the dentist.", answer);
        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal(TimeSpan.FromMinutes(10), plan.Before);
    }

    [Fact]
    public async Task A_plan_tag_she_writes_anyway_with_tools_is_set()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp);
        var model = new FakeToolModel(["[plan: 2026-10-23 15:30 | Dentist] Friday, half three, the dentist."]);

        await conversation.Answer(model, "dentist friday at 3:30", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Dentist", Assert.Single(temp.Store.Agenda()).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]                                     // a provider without tools: the tags are all she has
    public async Task A_plan_tag_the_cabinet_can_t_read_is_told_to_her_after_her_reply(bool withTools)
    {
        using var temp = new TempStore();
        var (conversation, _) = Set(temp);
        var model = new FakeToolModel(["[remind: Monday 9am | Call the bank] The bank, Monday morning."]);

        await conversation.Answer(model, "remind me to call the bank monday", "", _ => { }, (_, _) => { }, tools: withTools ? new Toolbox([]) : null);

        Assert.Empty(temp.Store.Agenda());
        var lines = temp.Store.After("shino", 0);
        Assert.Equal(Speaker.Her, lines[^2].Speaker);
        Assert.Equal((Speaker.Cabinet, true), (lines[^1].Speaker, lines[^1].Instruction));
        Assert.StartsWith("Your [remind: Monday 9am | Call the bank] didn't go through: the cabinet can't read it", lines[^1].Text);
    }

    [Fact]
    public void Nothing_she_is_told_says_the_cabinet_reminds_them_by_itself()
    {
        var withTools = Prompt.Build(Shino, "", "", null, [], "", "hi", tools: true);
        var withTags = Prompt.Build(Shino, "", "", null, [], "", "hi");
        var tool = BuiltIn.Agenda((_, _, _, _, _, _, _) => "").Function;

        Assert.DoesNotContain("by itself", withTools[0].Text + withTags[0].Text + tool.Description + tool.JsonSchema.ToString());
        Assert.Contains("remind", ((System.Text.Json.JsonElement)tool.JsonSchema).GetProperty("required").ToString());
    }
}
