namespace InsertCoin.Mind.Tests;

public class WordFirstTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 9, 21, 4, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void A_remark_with_a_say_for_each_sentence_is_cut_before_its_second()
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
    public void A_remark_in_her_voices_own_language_is_cut_after_its_first_sentence_even_inside_a_piece()
    {
        var (call, then) = WordFirst.Split([new(null, "wave"), new("Bob? To"), new("morrow at ten, the dentist.")]);

        Assert.Equal([new Piece(null, "wave"), new Piece("Bob?")], call);
        Assert.Equal([new Piece(" To"), new Piece("morrow at ten, the dentist.")], then);
    }

    [Fact]
    public void One_say_over_several_sentences_still_holds_back_all_but_the_first()
    {
        var (call, then) = WordFirst.Split([new(null, "say", "おかえり、ボブさん！"), new("晚上好，回来啦！明早十点要去超市。")]);

        Assert.Equal("晚上好，回来啦！", WordFirst.Shown(call));
        Assert.Equal("明早十点要去超市。", WordFirst.Shown(then));
    }

    [Fact]
    public void A_remark_of_one_sentence_has_nothing_to_hold_back()
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
    public async Task Her_mind_is_asked_for_a_call_with_the_note_but_only_the_note_is_kept()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(Evening));
        var model = new FakeModel("Bob? What time would you like to be asleep by?");

        await conversation.Announce(model, "The player has no bedtime set.", "", _ => { }, (_, _) => { }, aside: WordFirst.Ask);

        Assert.Contains("[cabinet] The player has no bedtime set.\n" + WordFirst.Ask, model.Requests[0][^1].Text);
        Assert.Equal("The player has no bedtime set.", temp.Store.After("shino", 0).First(l => l.Speaker == Speaker.Cabinet).Text);
    }
}
