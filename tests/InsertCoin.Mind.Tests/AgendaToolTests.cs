using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>Spike #105: her agenda as a tool, and a reply that ends as she writes it down (flow C).</summary>
public class AgendaToolTests
{
    private static readonly DateTimeOffset Wednesday = new(2026, 10, 21, 19, 30, 0, TimeSpan.FromHours(-4));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static FunctionCallContent Agenda(string id, params (string Key, object? Value)[] arguments) =>
        new($"call-{id}", BuiltIn.AgendaName, arguments.ToDictionary(a => a.Key, a => a.Value));

    private static (Conversation, Toolbox) Set(TempStore temp, AgendaFlow flow) =>
        (new Conversation(temp.Store, "shino", Shino, new FakeClock(Wednesday)) { AgendaFlow = flow }, new Toolbox([]));

    private static IEnumerable<string> Results(List<ChatMessage> request) =>
        request.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString() ?? "");

    [Fact]
    public async Task Said_as_written_a_call_that_goes_through_ends_the_reply_with_no_second_request()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.SaidAsWritten);
        var model = new FakeToolModel(["Gym, Mondays at seven. Got it.",
            Agenda("gym", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym"), ("minutes_before", 60), ("repeat", "weekly"))]);

        var answer = await conversation.Answer(model, "gym every monday at 7, remind me an hour before", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Gym, Mondays at seven. Got it.", answer);
        Assert.Single(model.Requests);
        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal(("Gym", new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.FromHours(-4)), TimeSpan.FromHours(1), Repeat.Weekly),
                     (plan.Text, plan.At, plan.Before, plan.Repeat));
        // what was done is in the conversation, for her next turn
        Assert.Contains(temp.Store.After("shino", 0), l => l.Speaker == Speaker.Cabinet && l.Text.Contains($"Added #{plan.Id}: Gym, Mon 26 Oct 07:00, reminder 1 hour before, every week"));
    }

    [Fact]
    public async Task Said_as_written_a_failed_call_goes_back_to_her_and_she_puts_it_right()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.SaidAsWritten);
        var model = new FakeToolModel(["Dentist next Tuesday, noted.", Agenda("bad", ("action", "add"), ("when", "next Tuesday"), ("what", "Dentist"))],
                                      ["Wait, what time?"]);

        var answer = await conversation.Answer(model, "dentist next tuesday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Dentist next Tuesday, noted. Wait, what time?", answer);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("can't read \"next Tuesday\" as a time"));
        Assert.Empty(temp.Store.Agenda());
    }

    [Fact]
    public async Task Said_as_written_a_call_with_no_words_before_it_goes_back_so_she_speaks_once()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.SaidAsWritten);
        var model = new FakeToolModel([Agenda("quiet", ("action", "add"), ("when", "2026-10-23"), ("what", "Essay due"))], ["Friday, essay. On the list."]);

        var answer = await conversation.Answer(model, "essay due friday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Friday, essay. On the list.", answer);
        Assert.Equal(2, model.Requests.Count);
        Assert.Single(temp.Store.Agenda());
    }

    [Fact]
    public async Task Said_as_written_every_call_of_a_round_is_made_and_one_failing_sends_the_round_back()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.SaidAsWritten);
        var model = new FakeToolModel(["Monday and Wednesday, both down.",
            Agenda("mon", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym")),
            Agenda("wed", ("action", "add"), ("when", "2026-10-28T07:00"), ("what", "Gym"), ("repeat", "every other week"))], ["Every other week I can't do, so just this Wednesday?"]);

        await conversation.Answer(model, "gym monday and every other wednesday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(2, Results(model.Requests[1]).Count());
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("can't repeat"));
        Assert.Single(temp.Store.Agenda());
    }

    [Fact]
    public async Task Said_as_written_two_calls_that_both_go_through_end_the_reply()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.SaidAsWritten);
        var model = new FakeToolModel(["Monday and Wednesday, both down.",
            Agenda("mon", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym")),
            Agenda("wed", ("action", "add"), ("when", "2026-10-28T07:00"), ("what", "Gym"))]);

        await conversation.Answer(model, "gym monday and wednesday", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Single(model.Requests);
        Assert.Equal(2, temp.Store.Agenda().Count);
    }

    [Fact]
    public async Task Tool_first_the_result_always_goes_back_before_she_says_more()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.ToolFirst);
        var model = new FakeToolModel(["Sure, I'll write it down.", Agenda("gym", ("action", "add"), ("when", "2026-10-26T07:00"), ("what", "Gym"))],
                                      ["It's on your agenda."]);

        var answer = await conversation.Answer(model, "gym monday at 7", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Sure, I'll write it down. It's on your agenda.", answer);
        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(Results(model.Requests[1]), r => r.Contains("Added #"));
    }

    [Fact]
    public async Task A_change_and_a_cancel_go_by_the_number_she_was_shown()
    {
        using var temp = new TempStore();
        var gym = temp.Store.AddPlan("Gym", Wednesday.AddDays(5).Date.AddHours(7) is var d ? new DateTimeOffset(d, Wednesday.Offset) : default, false, null, Wednesday, "shino");
        var call = temp.Store.AddPlan("Call mum", new DateTimeOffset(2026, 10, 24, 18, 0, 0, Wednesday.Offset), false, null, Wednesday, "shino");
        var (conversation, tools) = Set(temp, AgendaFlow.SaidAsWritten);
        var model = new FakeToolModel(["Gym at eight then, and no call.",
            Agenda("move", ("action", "change"), ("item", gym), ("when", "2026-10-26T08:00"), ("minutes_before", 0)),
            Agenda("off", ("action", "cancel"), ("item", call))]);

        await conversation.Answer(model, "move the gym to 8 and remind me then, and the call's off", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Single(model.Requests);
        var plan = Assert.Single(temp.Store.Agenda());
        Assert.Equal((gym, 8, TimeSpan.Zero), (plan.Id, plan.At.Hour, plan.Before));
    }

    [Fact]
    public async Task With_tags_the_agenda_tool_is_not_offered()
    {
        using var temp = new TempStore();
        var (conversation, tools) = Set(temp, AgendaFlow.Tags);
        tools.Put(BuiltIn.PrintMemo((_, _) => "Printed."));
        var model = new FakeToolModel(["[plan: 2026-10-26 07:00 | Gym] Gym on Monday, got it."]);

        await conversation.Answer(model, "gym monday at 7", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.DoesNotContain(model.Options[0]!.Tools!, t => t.Name == BuiltIn.AgendaName);
        Assert.Single(temp.Store.Agenda());
    }
}
