namespace InsertCoin.Mind.Tests;

public class WordFirstTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 9, 21, 4, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void A_call_with_more_after_it_is_cut_before_its_second_say()
    {
        List<Piece> remark =
        [
            new(null, "wave"), new(null, "say", "ボブさん？"), new("Bob? "),
            new(null, "say", "明日は十時にスーパーだね。"), new("Tomorrow at ten, the supermarket. "),
            new(null, "say", "毎晩何時までに寝たい？"), new("What time would you like to be asleep by?"),
        ];

        var (call, then) = WordFirst.Split(remark);

        Assert.Equal(remark[..3], call);
        Assert.Equal(remark[3..], then);
        Assert.Equal("Bob?", WordFirst.Shown(call));
    }

    [Fact]
    public void A_call_in_her_voices_own_language_is_cut_after_its_first_sentence_even_inside_a_piece()
    {
        var (call, then) = WordFirst.Split([new(null, "wave"), new("Bob? To"), new("morrow at ten, the dentist.")]);

        Assert.Equal([new Piece(null, "wave"), new Piece("Bob?")], call);
        Assert.Equal([new Piece(" To"), new Piece("morrow at ten, the dentist.")], then);
    }

    [Fact]
    public void One_say_over_several_sentences_still_keeps_only_the_first_shown()
    {
        var (call, then) = WordFirst.Split([new(null, "say", "おかえり、ボブさん！"), new("晚上好，回来啦！明早十点要去超市。")]);

        Assert.Equal("晚上好，回来啦！", WordFirst.Shown(call));
        Assert.Equal("明早十点要去超市。", WordFirst.Shown(then));
    }

    [Fact]
    public void A_call_of_one_sentence_is_kept_whole()
    {
        var (call, then) = WordFirst.Split([new(null, "say", "そろそろ寝る時間だよ。"), new("Time for bed soon.")]);

        Assert.Equal(2, call.Count);
        Assert.Empty(then);
    }

    [Theory]
    [InlineData("Yes?", WordFirst.Reply.GoOn)]
    [InlineData("Yeah, what's up?", WordFirst.Reply.GoOn)]
    [InlineData("Hm?", WordFirst.Reply.GoOn)]
    [InlineData("嗯？怎么了", WordFirst.Reply.GoOn)]
    [InlineData("なに？", WordFirst.Reply.GoOn)]
    [InlineData("Not now.", WordFirst.Reply.NotNow)]
    [InlineData("Later", WordFirst.Reply.NotNow)]
    [InlineData("hold on", WordFirst.Reply.NotNow)]
    [InlineData("等一下", WordFirst.Reply.NotNow)]
    [InlineData("ちょっと待って", WordFirst.Reply.NotNow)]
    [InlineData("No.", WordFirst.Reply.NotNow)]
    [InlineData("Can you remind me to call my mum tomorrow at nine?", null)]
    [InlineData("明天早上九点提醒我给妈妈打电话", null)]
    [InlineData("…", null)]
    public void What_the_player_says_to_her_call_goes_on_waits_or_is_a_message(string heard, WordFirst.Reply? reply) =>
        Assert.Equal(reply, WordFirst.Of(heard));

    [Fact]
    public async Task The_remark_is_told_she_has_called_them_but_only_the_note_is_kept()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(Evening));
        var model = new FakeModel("What time would you like to be asleep by?");

        await conversation.Announce(model, "The player has no bedtime set.", "", _ => { }, (_, _) => { }, aside: WordFirst.Called);

        Assert.Contains("[cabinet] The player has no bedtime set.\n" + WordFirst.Called, model.Requests[0][^1].Text);
        Assert.Equal("The player has no bedtime set.", temp.Store.After("shino", 0).First(l => l.Speaker == Speaker.Cabinet).Text);
    }

    [Fact]
    public async Task Her_call_is_asked_for_on_its_own_with_the_conversation_and_nothing_of_it_is_kept()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "hana", new Persona("Hana", "Hana is calm."), new FakeClock(Evening)) { VoiceLanguage = "ja" };
        await conversation.Answer(new FakeModel("Hi."), "Hello!", "", _ => { }, (_, _) => { });
        var model = new FakeModel("[wave] [say: ボブさん？] Bob? [say: 明日は十時にスーパーだね。] Tomorrow at ten, the supermarket. [note: x]");

        var call = await conversation.Call(model, null);

        Assert.Equal([new Piece(null, "wave"), new Piece(null, "say", "ボブさん？"), new Piece("Bob?")], call);
        Assert.Contains(WordFirst.Ask, model.Requests[0][^1].Text);
        Assert.Contains(model.Requests[0], m => m.Text.Contains("Hello!"));
        Assert.Equal(2, temp.Store.After("hana", 0).Count);
        Assert.Empty(temp.Store.Facts("hana"));
    }

    [Fact]
    public async Task Her_last_call_is_kept_for_her_voices_language_for_when_asking_fails()
    {
        using var temp = new TempStore();
        var persona = new Persona("Hana", "Hana is calm.");
        Conversation Seated(string language) => new(temp.Store, "hana", persona, new FakeClock(Evening)) { VoiceLanguage = language };
        Assert.Null(Seated("ja").LastCall);

        await Seated("ja").Call(new FakeModel("[say: ボブさん、ちょっといい？] Bob, got a sec?"), null);
        Assert.Null(await Seated("ja").Call(new FakeModel("[wave]"), null));

        Assert.Equal([new Piece(null, "say", "ボブさん、ちょっといい？"), new Piece("Bob, got a sec?")], Seated("ja").LastCall);
        Assert.Null(Seated("zh").LastCall);
    }

    [Fact]
    public async Task Her_line_starts_with_her_call_and_not_now_after_it_keeps_the_rest_unsaid()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "hana", new Persona("Hana", "Hana is calm."), new FakeClock(Evening));
        await conversation.Announce(new FakeModel("You've been sitting for fifty minutes."), "The player has been at the PC for 50 min.", "",
                                    _ => { }, (_, _) => { }, aside: WordFirst.Called);

        conversation.CalledFirst("Bob? ");
        Assert.Equal("Bob? You've been sitting for fifty minutes.", temp.Store.After("hana", 0).Last(l => l.Speaker == Speaker.Her).Text);

        Assert.Equal("You've been sitting for fifty minutes.", conversation.CutOff("Bob?".Length));
        Assert.Equal("Bob?", temp.Store.After("hana", 0).Last(l => l.Speaker == Speaker.Her).Text);
    }
}
