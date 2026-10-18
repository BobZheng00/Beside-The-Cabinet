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
    public async Task What_the_cabinet_tells_her_to_do_is_kept_as_an_instruction_and_still_reaches_her()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        conversation.Witness("The player solved \"Two Sum\" (Easy).");
        const string ask = "The player has no bedtime set. Ask them when they usually go to sleep.";
        await conversation.Announce(new FakeModel("When do you usually turn in?"), ask, "", _ => { }, (_, _) => { });
        await conversation.Answer(new FakeModel("[cancel: #99] Done, it's off."), "cancel the dentist", "", _ => { }, (_, _) => { });
        await conversation.Answer(new FakeModel("Welcome back! I was thinking we could try the hard one."), "hi", "", _ => { }, (_, _) => { });
        conversation.CutOff("Welcome back! ".Length);
        var next = new FakeModel("Sorry!");
        await conversation.Answer(next, "around midnight", "", _ => { }, (_, _) => { });

        var lines = temp.Store.After("shino", 0);
        Assert.Equal([ask, "Your [cancel: #99] didn't go through: there is no #99 on the agenda now. Tell the player briefly, then put it right or ask them.",
                      "The player cut you off there. You hadn't said yet: \"I was thinking we could try the hard one.\""],
                     lines.Where(l => l.Instruction).Select(l => l.Text));
        // what happened at the cabinet, and what either of them said, are not
        Assert.Contains(lines, l => l is { Speaker: Speaker.Cabinet, Instruction: false, Text: "The player solved \"Two Sum\" (Easy)." });
        Assert.All(lines.Where(l => l.Speaker != Speaker.Cabinet), l => Assert.False(l.Instruction));
        var sent = string.Join("\n", next.Requests.Single().Select(m => m.Text));
        Assert.Contains($"[cabinet] {ask}", sent);
        Assert.Contains("[cabinet] Your [cancel: #99] didn't go through", sent);
    }

    [Fact]
    public void Lines_kept_before_instructions_were_marked_are_read_as_records()
    {
        var folder = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "memory.db");
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE lines (id INTEGER PRIMARY KEY, character TEXT NOT NULL, speaker TEXT NOT NULL, text TEXT NOT NULL, at TEXT NOT NULL);
                INSERT INTO lines (character, speaker, text, at) VALUES ('shino', 'Cabinet', 'Reminders the player set are going off.', '2026-10-02T19:00:00.0000000+08:00');
                PRAGMA user_version = 16;
                """;
            create.ExecuteNonQuery();
        }
        try
        {
            using var store = new MindStore(path);
            Assert.False(Assert.Single(store.After("shino", 0)).Instruction);
            Assert.True(store.Between("shino", store.Add("shino", Speaker.Cabinet, "Tell them.", Friday, instruction: true), long.MaxValue).Single().Instruction);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
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
        Assert.StartsWith("<now>\nFriday 2 October 2026, 19:30 (evening). You were talking a moment ago.\n</now>\n<cabinet>\nPlay time banked: 15 min.\n</cabinet>",
                          sent[3].Text);
        Assert.EndsWith("hello\n\n<character>\nReply as Shino, in character: Shino's own voice, manner and way of speaking, as described at the start.\n</character>",
                        sent[3].Text);
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
    public async Task A_long_conversation_is_folded_into_dated_notes_keeping_the_newest_lines()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock) { Window = 4000 };
        for (var i = 0; i < 60; i++)
        {
            clock.Now = Friday.AddMinutes(i);
            temp.Store.Add("shino", i % 2 == 0 ? Speaker.User : Speaker.Her, $"line {i} " + new string('x', 400), clock.Now);
        }
        Assert.True(conversation.NeedsFolding);
        var model = new FakeModel("""{"observations": [{"date": "2026-10-02", "priority": "high", "text": "The player and Shino talked about x."}]}""");

        Assert.True(await conversation.Fold(model));

        var observed = temp.Store.Observations("shino").Single();
        Assert.Equal(("The player and Shino talked about x.", Priority.High, Friday, 1L), (observed.Text, observed.Priority, observed.At, observed.First));
        Assert.Equal(temp.Store.Folded("shino"), observed.Last);
        var left = temp.Store.After("shino", temp.Store.Folded("shino"));
        Assert.InRange(left.Count, 6, 20);
        Assert.StartsWith("line 59", left[^1].Text);
        Assert.False(conversation.NeedsFolding);
        Assert.Contains("[Fri 2 Oct 2026 19:30] Player: line 0", model.Requests.Single()[1].Text);

        // the next answer gets her notes instead of the folded lines
        var next = new FakeModel("ok");
        await conversation.Answer(next, "hi", "", _ => { }, (_, _) => { });
        Assert.Contains("Fri 2 Oct 2026 (today)\n- ! The player and Shino talked about x.", next.Requests.Single()[0].Text);
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

    [Fact]
    public void Clearing_the_conversation_starts_it_afresh_and_keeps_what_she_remembers()
    {
        using var temp = new TempStore();
        temp.Store.SetNote("shino", MindStore.Us, "Friends.");
        temp.Store.AddFact("shino", "Bob has an interview on Friday.", 4, Friday);
        var first = temp.Store.Add("shino", Speaker.User, "I have an interview on Friday", Friday);
        var last = temp.Store.Add("shino", Speaker.Her, "Good luck!", Friday);
        temp.Store.Observe("shino", [new Observation(0, Friday, Priority.High, "They talked about the interview.", first, last)], last);
        temp.Store.Fold("shino", last, Friday);

        temp.Store.ForgetConversation("shino");

        Assert.Empty(temp.Store.After("shino", 0));
        Assert.Equal(0, temp.Store.Folded("shino"));
        // her notes stay, from before the conversation now: in view even though nothing has been folded yet
        Assert.Equal((0L, 0L), temp.Store.Observations("shino").Select(o => (o.First, o.Last)).Single());
        Assert.Single(Log.Left(temp.Store.Observations("shino"), temp.Store.Folded("shino")));
        Assert.Equal(0, temp.Store.Digested("shino"));
        Assert.Equal("Friends.", temp.Store.Note("shino", MindStore.Us));
        Assert.Single(temp.Store.Facts("shino"));

        var again = temp.Store.Add("shino", Speaker.User, "hello again", Friday);
        Assert.Equal(["hello again"], temp.Store.After("shino", 0).Select(l => l.Text));
        Assert.True(again > 0);
    }

    [Fact]
    public async Task A_reply_that_comes_back_empty_is_asked_for_once_more()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("Welcome back!");
        model.Streams.Enqueue([]);                                         // a busy provider: no words at all
        var shown = "";

        await conversation.Answer(model, "I'm back", "", words => shown += words, (_, _) => { });

        Assert.Equal("Welcome back!", shown);
        Assert.Equal(2, model.Requests.Count);

        var silent = new FakeModel("Hello!");
        silent.Streams.Enqueue([]);
        silent.Streams.Enqueue([]);
        shown = "";
        await conversation.Answer(silent, "hello?", "", words => shown += words, (_, _) => { });
        Assert.Equal("", shown);                                           // once more only
        Assert.Equal(2, silent.Requests.Count);
    }
}
