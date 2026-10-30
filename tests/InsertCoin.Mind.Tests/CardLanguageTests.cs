namespace InsertCoin.Mind.Tests;

/// <summary>Her card in another language (persona.zh.md), chosen by the player's own lines or by the Language setting (#141).</summary>
public class CardLanguageTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private const string Auto = "Reply in the language the player last wrote in.";

    private static readonly Persona Shino = new("Shino", "Shino draws every day.")
    {
        Languages = new Dictionary<string, Persona> { ["zh"] = new("Shino", "诗乃每天画画。") },
    };

    private static Conversation Seated(TempStore temp, string? language = null, Persona? persona = null) =>
        new(temp.Store, "shino", persona ?? Shino, new FakeClock(Friday)) { Language = () => language };

    /// <summary>What she was told in her latest request: her base text, and the whole of it.</summary>
    private static (string System, string All) Told(FakeModel model) =>
        (model.Requests[^1][0].Text, string.Join("\n", model.Requests[^1].Select(m => m.Text)));

    private static async Task<FakeModel> Say(Conversation conversation, string said, string? aside = null)
    {
        var model = new FakeModel("嗯。");
        await conversation.Answer(model, said, "", _ => { }, (_, _) => { }, aside: aside);
        return model;
    }

    [Fact]
    public void A_card_in_Chinese_calls_the_player_by_their_name_or_玩家_when_they_gave_none()
    {
        const string text = "她和{{user}}是朋友。";

        Assert.Equal("她和玩家是朋友。", Persona.Written("Shino", text, null, "zh").Description);
        Assert.Equal("她和Bob是朋友。", Persona.Written("Shino", text, "Bob", "zh").Description);
        Assert.Equal("她和the player是朋友。", Persona.Written("Shino", text, null).Description);
    }

    [Fact]
    public async Task Two_of_the_players_lines_in_a_row_in_Chinese_take_her_to_her_Chinese_card_and_one_does_not()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);

        var first = await Say(conversation, "今天好累");
        Assert.Contains("Shino draws every day.", Told(first).System);
        Assert.Null(conversation.CardLanguage);

        var second = await Say(conversation, "组会又开了一天");
        Assert.Contains("诗乃每天画画。", Told(second).System);
        Assert.DoesNotContain("Shino draws every day.", Told(second).System);
        Assert.Contains(Auto, Told(second).System);
        Assert.Equal("zh", conversation.CardLanguage);
    }

    [Fact]
    public async Task One_line_in_English_keeps_her_Chinese_card_and_two_take_her_back()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);
        await Say(conversation, "今天好累");
        await Say(conversation, "组会又开了一天");

        Assert.Contains("诗乃每天画画。", Told(await Say(conversation, "anyway")).System);
        Assert.Contains("Shino draws every day.", Told(await Say(conversation, "what are you drawing")).System);
        Assert.Null(conversation.CardLanguage);
    }

    [Fact]
    public async Task From_her_Chinese_card_two_lines_in_Japanese_take_her_to_her_main_card_and_she_replies_in_theirs()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);
        await Say(conversation, "今天好累");
        await Say(conversation, "组会又开了一天");
        await Say(conversation, "ただいま");

        var told = Told(await Say(conversation, "今日は疲れた"));

        Assert.Contains("Shino draws every day.", told.System);
        Assert.Contains(Auto, told.System);
        Assert.Null(conversation.CardLanguage);
    }

    [Fact]
    public async Task The_cabinets_lines_and_lines_with_no_letters_never_count()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);
        await Say(conversation, "今天好累");
        conversation.Witness("The player solved \"Two Sum\" (Easy).");
        await conversation.Announce(new FakeModel("Time to stretch."), "Remind the player to stretch their legs.", "", _ => { }, (_, _) => { });
        await Say(conversation, "???");
        await Say(conversation, "42");

        var told = Told(await Say(conversation, "组会又开了一天"));

        Assert.Contains("诗乃每天画画。", told.System);       // the two Chinese lines are the latest two that count, so in a row
    }

    [Fact]
    public async Task A_line_by_text_counts_like_one_at_the_PC()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);
        await Say(conversation, "今天好累", aside: "[cabinet] The player is texting you from their phone.");

        Assert.Contains("诗乃每天画画。", Told(await Say(conversation, "在地铁上", aside: "[cabinet] The player is texting you from their phone.")).System);
    }

    [Fact]
    public async Task Her_own_remarks_go_by_the_same_lines()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);
        await Say(conversation, "今天好累");
        await Say(conversation, "组会又开了一天");

        var reminder = new FakeModel("该起来走走了。");
        await conversation.Announce(reminder, "Remind the player to stretch their legs.", "", _ => { }, (_, _) => { });
        var call = new FakeModel("喂。");
        await conversation.Call(call, null);

        Assert.Contains("诗乃每天画画。", Told(reminder).System);
        Assert.Contains("诗乃每天画画。", Told(call).System);
    }

    [Fact]
    public async Task After_a_restart_she_is_on_the_card_the_stored_lines_give()
    {
        using var temp = new TempStore();
        await Say(Seated(temp), "今天好累");
        await Say(Seated(temp), "组会又开了一天");

        var again = Seated(temp);

        Assert.Equal("zh", again.CardLanguage);
        Assert.Contains("诗乃每天画画。", Told(await Say(again, "嗯")).System);
    }

    [Fact]
    public async Task A_language_chosen_takes_its_card_whatever_the_player_writes_or_her_main_card_without_one()
    {
        using var temp = new TempStore();

        var chinese = Told(await Say(Seated(temp, "zh"), "hey, I'm back"));
        var english = Told(await Say(Seated(temp, "en"), "今天好累"));
        var noCard = Told(await Say(Seated(temp, "zh", new Persona("Robin", "Robin is kind.")), "hey"));

        Assert.Contains("诗乃每天画画。", chinese.System);
        Assert.Contains("Shino draws every day.", english.System);
        Assert.Contains("Robin is kind.", noCard.System);
    }

    [Fact]
    public async Task A_card_the_player_gave_her_stands_for_her_in_every_language_with_its_lorebook_and_notes()
    {
        using var temp = new TempStore();
        var card = CharacterCard.Read(System.Text.Encoding.UTF8.GetBytes("""
            {"spec": "chara_card_v2", "data": {"name": "Shino", "description": "Shino from the card.", "first_mes": "Hi there.",
             "post_history_instructions": "Stay as Shino from the card.",
             "character_book": {"entries": [{"keys": ["harbour"], "content": "The harbour at home smells of salt.", "enabled": true}]}}}
            """))!.ToPersona(null);

        var told = Told(await Say(Seated(temp, "zh", card), "说说harbour吧"));

        Assert.Empty(card.Languages);
        Assert.Contains("Shino from the card.", told.System);
        Assert.Contains("The harbour at home smells of salt.", told.All);
        Assert.Contains("Stay as Shino from the card.", told.All);
    }

    [Fact]
    public void A_language_chosen_is_the_one_her_instructions_name_wherever_they_said_the_players()
    {
        var persona = new Persona("Shino", "x");
        var chosen = Prompt.Build(persona, "", "", null, [], "", "hi", voiceLanguage: "ja", language: "zh");
        var auto = Prompt.Build(persona, "", "", null, [], "", "hi", voiceLanguage: "ja");
        var same = Prompt.Build(persona, "", "", null, [], "", "hi", voiceLanguage: "zh", language: "zh")[^1].Text;

        // the base text
        Assert.Contains("Reply in Chinese, whatever language the player writes in.", chosen[0].Text);
        Assert.DoesNotContain(Auto, chosen[0].Text);
        Assert.Contains(Auto, auto[0].Text);
        // her voice and its subtitles
        Assert.Contains("a faithful translation in Chinese: nothing added or left out", chosen[0].Text);
        Assert.Contains("a faithful translation in the player's language: nothing added or left out", auto[0].Text);
        Assert.EndsWith("<voice>\nYour voice speaks Japanese, and you write in Chinese. When that isn't Japanese, put a [say: ...] in front of every " +
                        "sentence, the last ones too, with the whole sentence in Japanese: only what is in it is heard.\n</voice>", chosen[^1].Text);
        Assert.EndsWith("<voice>\nYour voice speaks Chinese, and you write in Chinese.\n</voice>", same);
        Assert.DoesNotContain("the player last wrote in", string.Join("\n", chosen.Select(m => m.Text)));
        // memo and agenda titles are in the language she replies in, which the base text names
        var tools = string.Join("\n", BuiltIn.PrintMemo((_, _) => "").Function.JsonSchema.ToString(),
                                BuiltIn.Agenda((_, _, _, _, _, _, _) => "").Function.JsonSchema.ToString());
        Assert.Contains("in the language you reply in", tools);
        Assert.DoesNotContain("player's language", tools);
    }

    [Fact]
    public async Task Her_cached_base_text_changes_only_when_her_card_changes()
    {
        using var temp = new TempStore();
        var conversation = Seated(temp);
        string[] said = ["hi", "how's the drawing", "今天好累", "组会又开了一天", "嗯", "anyway"];
        var told = new List<string>();
        foreach (var line in said)
            told.Add(Told(await Say(conversation, line)).System);

        // the conversation goes after it, so only a change of card changes it: once, at the second line in Chinese
        Assert.Equal([false, false, false, true, false, false], told.Select((t, i) => i > 0 && t != told[i - 1]));
    }
}
