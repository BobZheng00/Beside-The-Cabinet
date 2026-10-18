using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>Texting with her through the player's own Telegram bot: what comes in, what goes out, and pairing with one chat.</summary>
public class TelegramTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-4));

    /// <summary>Telegram as the bot sees it: each request logged, each answered with the JSON given for its method.</summary>
    private sealed class FakeTelegram(Dictionary<string, (HttpStatusCode Status, string Body)> answers) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Asked.Add((path, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            var (status, body) = answers.FirstOrDefault(a => path.EndsWith(a.Key)).Value;
            return new HttpResponseMessage(status == 0 ? HttpStatusCode.OK : status) { Content = new StringContent(body ?? """{"ok":true,"result":true}""", Encoding.UTF8) };
        }
    }

    private static (Telegram Bot, FakeTelegram Server) Bot(params (string Method, HttpStatusCode Status, string Body)[] answers)
    {
        var server = new FakeTelegram(answers.ToDictionary(a => a.Method, a => (a.Status, a.Body)));
        return (new Telegram(new HttpClient(server), "123:ABC"), server);
    }

    [Fact]
    public async Task Its_name_is_found_from_the_token_and_a_token_telegram_doesnt_know_finds_none()
    {
        var (bot, server) = Bot(("getMe", HttpStatusCode.OK, """{"ok":true,"result":{"id":1,"is_bot":true,"first_name":"Hana","username":"hana_cabinet_bot"}}"""));
        Assert.Equal("hana_cabinet_bot", await bot.Me());
        Assert.Equal("/bot123:ABC/getMe", server.Asked.Single().Path);

        var (unknown, _) = Bot(("getMe", HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}"""));
        Assert.Null(await unknown.Me());
    }

    [Fact]
    public async Task What_comes_in_is_read_text_a_photo_at_its_largest_a_file_and_a_press()
    {
        var (bot, server) = Bot(("getUpdates", HttpStatusCode.OK, """
            {"ok":true,"result":[
              {"update_id":10,"message":{"message_id":1,"date":1791648000,"chat":{"id":42,"type":"private"},"text":"At the gym!"}},
              {"update_id":11,"message":{"message_id":2,"date":1791648060,"chat":{"id":42,"type":"private"},"caption":"proof",
                "photo":[{"file_id":"small","width":90},{"file_id":"big","width":1280}]}},
              {"update_id":12,"message":{"message_id":3,"date":1791648120,"chat":{"id":-7,"type":"group"},"document":{"file_id":"doc","file_name":"notes.pdf"}}},
              {"update_id":13,"callback_query":{"id":"cb1","data":"ab12:yes","message":{"message_id":9,"date":1791648000,"chat":{"id":42,"type":"private"}}}},
              {"update_id":14,"edited_message":{"message_id":1}}
            ]}
            """));

        var updates = await bot.Updates(10, TimeSpan.FromSeconds(50));

        Assert.Contains("\"offset\":10", server.Asked.Single().Body);
        Assert.Contains("\"timeout\":50", server.Asked.Single().Body);
        Assert.Equal(new Update(10, "42", true, DateTimeOffset.FromUnixTimeSeconds(1791648000), Text: "At the gym!"), updates[0]);
        Assert.Equal(("proof", "big", "photo.jpg"), (updates[1].Text, updates[1].File, updates[1].FileName));
        Assert.Equal(("-7", false, "doc", "notes.pdf"), (updates[2].Chat, updates[2].Private, updates[2].File, updates[2].FileName));
        Assert.Equal(("42", "ab12:yes", "cb1", 9L), (updates[3].Chat, updates[3].Pressed, updates[3].Press, updates[3].Message));
        Assert.Equal((14L, ""), (updates[4].Id, updates[4].Chat));      // something else: only to confirm
    }

    [Fact]
    public async Task What_goes_out_carries_its_buttons_under_the_last_part_of_a_long_text()
    {
        var (bot, server) = Bot();

        await bot.Send("42", "Allow once?", [("Allow once", "x:once"), ("No", "x:no")]);
        await bot.Send("42", new string('a', 4500), [("OK", "x:ok")]);

        Assert.Contains("\"inline_keyboard\":[[{\"text\":\"Allow once\",\"callback_data\":\"x:once\"},{\"text\":\"No\",\"callback_data\":\"x:no\"}]]", server.Asked[0].Body);
        Assert.Contains("\"chat_id\":\"42\"", server.Asked[0].Body);
        Assert.DoesNotContain("inline_keyboard", server.Asked[1].Body);
        Assert.Contains("inline_keyboard", server.Asked[2].Body);
        Assert.Equal(3, server.Asked.Count);
    }

    [Fact]
    public async Task A_press_is_answered_and_its_buttons_go()
    {
        var (bot, server) = Bot();

        await bot.Answered(new Update(13, "42", true, Noon, Pressed: "ab12:yes", Press: "cb1", Message: 9));

        Assert.Equal(["/bot123:ABC/answerCallbackQuery", "/bot123:ABC/editMessageReplyMarkup"], server.Asked.Select(a => a.Path));
        Assert.Contains("\"callback_query_id\":\"cb1\"", server.Asked[0].Body);
        Assert.Contains("\"message_id\":9", server.Asked[1].Body);
    }

    [Fact]
    public async Task A_photo_is_fetched_by_its_path()
    {
        var (bot, server) = Bot(("getFile", HttpStatusCode.OK, """{"ok":true,"result":{"file_id":"big","file_path":"photos/file_1.jpg"}}"""),
                                ("photos/file_1.jpg", HttpStatusCode.OK, "JPEG"));

        Assert.Equal("JPEG"u8.ToArray(), await bot.File("big"));
        Assert.Equal("/file/bot123:ABC/photos/file_1.jpg", server.Asked[1].Path);
    }

    [Fact]
    public async Task Telegram_saying_no_is_an_error_with_its_code_even_without_its_json()
    {
        var (busy, _) = Bot(("getUpdates", HttpStatusCode.Conflict, """{"ok":false,"error_code":409,"description":"Conflict: terminated by other getUpdates request"}"""));
        var (down, _) = Bot(("getUpdates", HttpStatusCode.BadGateway, "<html>bad gateway</html>"));

        Assert.Equal(409, (await Assert.ThrowsAsync<TelegramException>(() => busy.Updates(0, TimeSpan.Zero))).Code);
        Assert.Equal(502, (await Assert.ThrowsAsync<TelegramException>(() => down.Updates(0, TimeSpan.Zero))).Code);
    }

    [Fact]
    public void Pairing_takes_its_own_code_once_in_a_chat_of_ones_own_within_ten_minutes()
    {
        var code = Pairing.NewCode();
        var start = new Update(1, "42", true, Noon, Text: $"/start {code}");

        Assert.Matches("^[a-f0-9]{24}$", code);
        Assert.NotEqual(code, Pairing.NewCode());
        Assert.Equal($"https://t.me/hana_cabinet_bot?start={code}", Pairing.Link("hana_cabinet_bot", code));
        Assert.Equal($"https://web.telegram.org/k/#?tgaddr=tg%3A%2F%2Fresolve%3Fdomain%3Dhana_cabinet_bot%26start%3D{code}",
                     Pairing.WebLink("hana_cabinet_bot", code));
        Assert.True(Pairing.Pairs(start, code, Noon.AddMinutes(-3), Noon));
        Assert.False(Pairing.Pairs(start, code, Noon.AddMinutes(-11), Noon));                    // too late
        Assert.False(Pairing.Pairs(start with { Private = false }, code, Noon, Noon));            // a group
        Assert.False(Pairing.Pairs(start with { Text = "/start 123" }, code, Noon, Noon));        // someone guessing
        Assert.False(Pairing.Pairs(start with { Text = "/start" }, code, Noon, Noon));
    }

    [Fact]
    public void What_was_texted_at_once_comes_together_and_what_waited_says_how_long()
    {
        var together = Telegram.Together([("Still asleep?", Noon.AddMinutes(-1)), ("Morning!", Noon.AddHours(-3))], Noon);     // in the order sent

        Assert.Equal("Morning! (sent 3 hours ago)\nStill asleep?", together);
    }

    [Fact]
    public async Task A_text_of_hers_keeps_what_she_wrote_after_her_tools_not_the_lines_that_filled_their_wait()
    {
        using var temp = new TempStore();
        Toolbox Tools() => new([BuiltIn.ReadPage((_, _) => Task.FromResult("Light rain, ending around noon."))]);
        FakeToolModel Model() => new(["I'll check the forecast.", FakeToolModel.Call("read_page", "url", "https://wttr.in/Springfield")],
                                     ["That page was sparse, so one more look.", FakeToolModel.Call("read_page", "url", "https://wttr.in/Springfield?2")],
                                     ["It should stop around noon."]);
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "x"), new FakeClock(Noon));

        var texted = await conversation.Answer(Model(), "when will the rain stop?", "", _ => { }, (_, _) => { }, tools: Tools(), texting: true);
        var said = await conversation.Answer(Model(), "and at the PC?", "", _ => { }, (_, _) => { }, tools: Tools());

        Assert.Equal("It should stop around noon.", texted);
        Assert.Contains("It should stop around noon.", temp.Store.After("shino", 0).Where(l => l.Speaker == Speaker.Her).Select(l => l.Text));
        Assert.StartsWith("I'll check the forecast.", said);       // at the PC those lines filled the wait, and were heard
    }

    [Fact]
    public async Task While_she_texts_what_needs_the_player_at_the_pc_is_turned_down()
    {
        var opened = new List<Uri>();
        var asked = 0;
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)]) { Away = true, AskPlayer = (_, _) => { asked++; return Task.FromResult(Answer.Once); } };

        var (result, record) = await tools.Use(BuiltIn.OpenLink(opened.Add).Function, new AIFunctionArguments { ["url"] = "https://leetcode.com/" });

        Assert.Empty(opened);
        Assert.Equal(0, asked);
        Assert.Contains("away from the PC", result!.ToString());
        Assert.Contains("needs them at the PC", record);
        Assert.True(BuiltIn.OpenLink(_ => { }).AtThePc);
        Assert.False(BuiltIn.PrintMemo((_, _) => "").AtThePc);
    }

    private static readonly (string Text, string Data)[] MayI = [("Allow once", "once"), ("Always for leetcode.com", "always"), ("No", "no")],
                                                         Counts = [("Yes", "yes"), ("Not yet", "no")];

    /// <summary>When Telegram dated her question, by its own clock (a second off the PC's), and a while after.</summary>
    private static readonly DateTimeOffset Delivered = Noon.AddSeconds(1), Later = Noon.AddSeconds(8);

    /// <summary>Her question by text, sent at noon through a bot to the stand-in Telegram, which dates it <see cref="Delivered"/>.</summary>
    private static (Questions Questions, Task<string?> Asked, FakeTelegram Server) Asked((string Text, string Data)[] buttons)
    {
        var (bot, server) = Bot(("sendMessage", HttpStatusCode.OK, $$$"""{"ok":true,"result":{"message_id":7,"date":{{{Delivered.ToUnixTimeSeconds()}}}}}"""));
        var questions = new Questions();
        // sent before Ask returns, so that the test goes on with the question already out
        return (questions, questions.Ask("May I open leetcode.com?", buttons, (text, b) => Task.FromResult(bot.Send("42", text, b).GetAwaiter().GetResult()),
                                         Noon, CancellationToken.None), server);
    }

    /// <summary>What its button sends back when pressed, as the stand-in Telegram was given it.</summary>
    private static string Button(FakeTelegram server, string data) =>
        Regex.Match(server.Asked[0].Body, $"\"callback_data\":\"(\\w+:{data})\"").Groups[1].Value;

    [Fact]
    public async Task A_press_answers_her_question_by_text_with_its_button()
    {
        var (questions, asked, server) = Asked(MayI);

        questions.Pressed(Button(server, "always"));

        Assert.Equal("always", await asked);
        Assert.False(questions.Asking);
        Assert.Single(server.Asked);
    }

    [Fact]
    public async Task A_question_by_text_ends_unanswered_after_its_time_and_the_chat_says_so()
    {
        var (questions, asked, server) = Asked(MayI);

        questions.Lapse(Noon + Questions.Waits - TimeSpan.FromSeconds(1));
        Assert.True(questions.Asking);
        questions.Lapse(Noon + Questions.Waits);

        Assert.Null(await asked);
        Assert.False(questions.Asking);
        Assert.Equal(TimeSpan.FromSeconds(120), Questions.Waits);         // as long as her card by the stool waits
        Assert.Contains("inline_keyboard", server.Asked[0].Body);
        Assert.Contains(Questions.Unanswered, server.Asked[1].Body);
        Assert.DoesNotContain("inline_keyboard", server.Asked[1].Body);
        questions.Pressed(Button(server, "once"));                      // too late: nothing more
        Assert.Equal(2, server.Asked.Count);
    }

    [Theory]
    [InlineData("Yes!", "once")]
    [InlineData("ok, go ahead", "once")]
    [InlineData("always", "always")]
    [InlineData("No thanks.", "no")]
    [InlineData("好的", "once")]
    [InlineData("不要", "no")]
    [InlineData("はい", "once")]
    [InlineData("いいえ", "no")]
    public async Task A_short_yes_always_or_no_typed_answers_her_question_as_its_button_would(string typed, string data)
    {
        var (questions, asked, server) = Asked(MayI);

        Assert.True(questions.Typed(typed, Later));

        Assert.Equal(data, await asked);
        Assert.Single(server.Asked);                                    // answered: the chat says nothing more
    }

    [Theory]
    [InlineData("yes", "yes")]
    [InlineData("yes, always", "yes")]
    [InlineData("nope", "no")]
    public async Task A_question_with_only_yes_and_no_takes_always_as_yes(string typed, string data)
    {
        var (questions, asked, _) = Asked(Counts);

        Assert.True(questions.Typed(typed, Later));
        Assert.Equal(data, await asked);
    }

    [Fact]
    public async Task Anything_else_sent_meanwhile_leaves_her_question_unanswered_and_is_a_new_message()
    {
        var (questions, asked, server) = Asked(MayI);
        var (photo, sent, _) = Asked(Counts);

        Assert.False(questions.Typed("what's that site for?", Later));
        Assert.False(photo.Typed(null, Later));                         // a photo or a file, typed nothing

        Assert.Null(await asked);
        Assert.Null(await sent);
        Assert.Contains(Questions.Unanswered, server.Asked[1].Body);
        Assert.False(new Questions().Typed("yes", Later));              // with no question waiting, a yes is only a message
    }

    [Fact]
    public async Task A_question_that_never_got_through_is_answered_by_nothing_typed_and_lapses()
    {
        var (bot, server) = Bot(("sendMessage", HttpStatusCode.BadGateway, "<html>bad gateway</html>"));
        var questions = new Questions();
        DateTimeOffset? Send(string text, IReadOnlyList<(string Text, string Data)>? buttons)
        {
            try
            {
                return bot.Send("42", text, buttons).GetAwaiter().GetResult();
            }
            catch (TelegramException)
            {
                return null;                                            // as the cabinet's texting has it: not sent
            }
        }
        var asked = questions.Ask("May I open leetcode.com?", MayI, (text, b) => Task.FromResult(Send(text, b)), Noon, CancellationToken.None);

        Assert.False(questions.Typed("ok", Later));                      // meant for something else: they never got the question
        Assert.True(questions.Asking);
        questions.Lapse(Noon + Questions.Waits);

        Assert.Null(await asked);
        Assert.Contains("inline_keyboard", server.Asked[0].Body);
    }

    [Fact]
    public async Task What_the_player_sent_before_her_question_reached_them_never_answers_it()
    {
        var (questions, asked, server) = Asked(MayI);

        Assert.False(questions.Typed("ok", Noon.AddSeconds(-3)));        // "ok" to her last line, sent before the question
        Assert.False(questions.Typed("yes", Delivered));                 // the same second: can't be told apart, so not an answer
        Assert.False(questions.Typed("what's that site for?", Delivered));
        Assert.True(questions.Asking);                                   // still open: those are ordinary messages
        Assert.True(questions.Typed("no", Later));

        Assert.Equal("no", await asked);
        Assert.Single(server.Asked);
    }

    [Fact]
    public void A_reminder_due_while_her_question_waits_still_goes_by_text_as_its_plain_line()
    {
        var reminder = new Telling("REMINDER", "note", "Reminder: call Mum.", Matters.Must, true, Noon, TimeSpan.FromMinutes(10));
        var howDidItGo = new Telling("HOW DID IT GO", "Ask how the dentist went.", "", Matters.Should, true, Noon, TimeSpan.FromHours(3));

        Assert.True(Reaching.GoesWhileAsking(reminder, Reaching.Route(Matters.Must, true, Whereabouts.Away)));
        Assert.True(Reaching.GoesWhileAsking(reminder, Reaching.Route(Matters.Must, true, Whereabouts.Asleep)));
        Assert.False(Reaching.GoesWhileAsking(howDidItGo, Reaching.Route(Matters.Should, true, Whereabouts.Away)));   // only her mind says it: it waits for her
        Assert.False(Reaching.GoesWhileAsking(reminder, Reaching.Route(Matters.Must, true, Whereabouts.Here)));       // at the PC: it waits for her
    }
}
