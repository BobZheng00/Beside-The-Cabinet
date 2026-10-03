using System.Net;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

public class ConversationTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public async Task An_answer_streams_its_text_acts_its_direction_and_is_logged()
    {
        using var temp = new TempStore();
        var model = new FakeModel("[pu", "mp] Two ", "Hards today!");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var shown = new List<string>();
        var acted = new List<string>();

        var answer = await conversation.Answer(model, "I did it", "Now: Friday.", shown.Add, (name, _) => acted.Add(name));

        Assert.Equal("Two Hards today!", answer);
        Assert.Equal("Two Hards today!", string.Concat(shown));
        Assert.Equal(["pump"], acted);
        Assert.Equal([(Speaker.User, "I did it"), (Speaker.Her, "Two Hards today!")],
                     temp.Store.After("shino", 0).Select(l => (l.Speaker, l.Text)));
    }

    [Fact]
    public async Task The_prompt_holds_persona_notes_history_and_the_cabinet_right_now()
    {
        using var temp = new TempStore();
        temp.Store.SetNote(MindStore.Shared, MindStore.AboutUser, "Exam on Friday.");
        temp.Store.SetNote("shino", MindStore.Us, "She calls the player Bob.");
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        conversation.Witness("The player solved \"Two Sum\" (Easy): +1 token.");
        conversation.Said("+1 coin!");
        var model = new FakeModel("Hi!");

        await conversation.Answer(model, "hello", "Play time banked: 15 min.", _ => { }, (_, _) => { });

        var sent = model.Requests.Single();
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Contains("Shino is cheerful.", sent[0].Text);
        Assert.Contains("Exam on Friday.", sent[0].Text);
        Assert.Contains("She calls the player Bob.", sent[0].Text);
        // turns alternate, starting with the player's side
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], sent.Select(m => m.Role));
        Assert.Contains("[cabinet] The player solved \"Two Sum\"", sent[1].Text);
        Assert.Contains("[cabinet] Friday 2 October, 19:30", sent[1].Text);      // when the talk (re)started
        Assert.Equal("+1 coin!", sent[2].Text);
        Assert.StartsWith("<cabinet>\nPlay time banked: 15 min.\n</cabinet>", sent[3].Text);
        Assert.EndsWith("hello", sent[3].Text);
    }

    [Fact]
    public async Task A_failed_request_keeps_what_the_player_said_and_explains_itself()
    {
        using var temp = new TempStore();
        var model = new FakeModel { Fails = new HttpRequestException("no", null, HttpStatusCode.Unauthorized) };
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        var e = await Assert.ThrowsAsync<HttpRequestException>(() => conversation.Answer(model, "hello", "", _ => { }, (_, _) => { }));

        Assert.Equal([Speaker.User], temp.Store.After("shino", 0).Select(l => l.Speaker));
        Assert.Contains("API key", Trouble.Explain(e));
    }

    [Fact]
    public async Task A_long_conversation_is_folded_into_a_summary_keeping_the_newest_lines()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        for (var i = 0; i < 60; i++)
        {
            clock.Now = Friday.AddMinutes(i);
            temp.Store.Add("shino", i % 2 == 0 ? Speaker.User : Speaker.Her, $"line {i} " + new string('x', 400), clock.Now);
        }
        Assert.True(conversation.NeedsFolding);
        var model = new FakeModel("The player and Shino talked about x.");

        Assert.True(await conversation.Fold(model));

        var summary = temp.Store.SummaryOf("shino")!;
        Assert.Equal("The player and Shino talked about x.", summary.Text);
        var left = temp.Store.After("shino", summary.Through);
        Assert.InRange(left.Count, 6, 20);
        Assert.StartsWith("line 59", left[^1].Text);
        Assert.False(conversation.NeedsFolding);
        Assert.Contains("[Fri 2 Oct 19:30] Player: line 0", model.Requests.Single()[1].Text);

        // the next answer gets the summary instead of the folded lines
        var next = new FakeModel("ok");
        await conversation.Answer(next, "hi", "", _ => { }, (_, _) => { });
        Assert.Contains("The player and Shino talked about x.", next.Requests.Single()[0].Text);
        Assert.DoesNotContain("line 0 ", string.Concat(next.Requests.Single().Select(m => m.Text)));
    }

    [Fact]
    public async Task Nothing_is_folded_while_the_conversation_is_short()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        conversation.Said("Hi!");
        var model = new FakeModel("summary");

        Assert.False(await conversation.Fold(model));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void Forgetting_clears_one_character_and_keeps_shared_notes()
    {
        using var temp = new TempStore();
        temp.Store.SetNote(MindStore.Shared, MindStore.AboutUser, "Likes Hades.");
        temp.Store.SetNote("shino", MindStore.Us, "Friends.");
        temp.Store.Add("shino", Speaker.User, "hi", Friday);
        temp.Store.Add("fumiriya", Speaker.User, "yo", Friday);

        temp.Store.Forget("shino");

        Assert.Empty(temp.Store.After("shino", 0));
        Assert.Single(temp.Store.After("fumiriya", 0));
        Assert.Equal("", temp.Store.Note("shino", MindStore.Us));
        Assert.Equal("Likes Hades.", temp.Store.Note(MindStore.Shared, MindStore.AboutUser));
    }
}
