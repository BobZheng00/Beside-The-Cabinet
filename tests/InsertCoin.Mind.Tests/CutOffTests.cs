namespace InsertCoin.Mind.Tests;

/// <summary>Cut off mid-reply: her line keeps what the player heard, and she knows what she had not said yet.</summary>
public class CutOffTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    /// <summary>A reply streamed a few characters at a time through the directions, as her conversation does it.</summary>
    private static (Speech Speech, string Shown, List<string> Spoken) Stream(string language, string reply)
    {
        var spoken = new List<string>();
        var shown = new System.Text.StringBuilder();
        var speech = new Speech(language, spoken.Add);
        void Show(string piece)
        {
            shown.Append(piece);
            speech.Text(piece);
        }
        var stage = new StageDirections(speech.Direction, Show);
        for (var i = 0; i < reply.Length; i += 3)
            Show(stage.Feed(reply[i..Math.Min(reply.Length, i + 3)]));
        Show(stage.Finish());
        speech.Finish();
        return (speech, shown.ToString(), spoken);
    }

    [Fact]
    public void Where_her_voice_stopped_is_found_in_the_subtitles()
    {
        var (speech, shown, spoken) = Stream("ja", "[wave] [say: おかえり！] Welcome back! [say: 今日も頑張ったね。] You did well today.");

        Assert.Equal(["おかえり！", "今日も頑張ったね。"], spoken);
        Assert.Equal("Welcome back! You did well today.", shown);
        Assert.Equal("Welcome back!", shown[..speech.CutAt(1, 0)!.Value].Trim());                 // the first line said: its subtitle heard
        var half = speech.CutAt(1, 0.5)!.Value;
        Assert.InRange(half, shown.IndexOf("did", StringComparison.Ordinal), shown.IndexOf("today", StringComparison.Ordinal));
        Assert.Null(speech.CutAt(2, 0));                                                          // all of it said
    }

    [Fact]
    public void Where_her_voice_stopped_is_found_in_the_sentences_she_spoke_as_written()
    {
        var (speech, shown, spoken) = Stream("ja", "おかえり！今日も頑張ったね。ゆっくり休んでね。");

        Assert.Equal(3, spoken.Count);
        Assert.Equal(shown.IndexOf("今日", StringComparison.Ordinal), speech.CutAt(1, 0));
        Assert.Equal(shown.IndexOf("ゆっくり", StringComparison.Ordinal), speech.CutAt(2, 0));
    }

    [Fact]
    public void A_cut_falls_at_the_start_of_a_word_and_leaves_no_comma_behind()
    {
        const string text = "Welcome back! I was thinking, maybe we could try the hard one tonight.";
        Assert.Equal(("Welcome back! I was thinking, maybe we—", "could try the hard one tonight."),
                     Conversation.Cut(text, text.IndexOf("could", StringComparison.Ordinal) + 3));
        Assert.Equal(("Welcome back! I was thinking—", "maybe we could try the hard one tonight."),
                     Conversation.Cut(text, text.IndexOf(", maybe", StringComparison.Ordinal) + 1));
        Assert.Equal(("今日も—", "頑張ったね。"), Conversation.Cut("今日も頑張ったね。", 3));       // between any two characters
        Assert.Equal(("—", text), Conversation.Cut(text, 0));
        Assert.Equal((text, ""), Conversation.Cut(text, text.Length - 1));                     // only the full stop left: nothing was cut
        Assert.Equal(("Welcome back!", "I was thinking, maybe we could try the hard one tonight."), Conversation.Cut(text, 14));   // a finished sentence: no dash
    }

    [Fact]
    public async Task Her_line_keeps_what_was_heard_and_a_note_says_what_she_had_not_said()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        const string reply = "Welcome back! I was thinking we could try the hard one tonight.";
        await conversation.Answer(new FakeModel(reply), "hi", "", _ => { }, (_, _) => { });

        Assert.Equal("could try the hard one tonight.", conversation.CutOff(reply.IndexOf("could", StringComparison.Ordinal)));
        Assert.Null(conversation.CutOff(3));                                                     // the same reply is not cut twice

        var next = new FakeModel("Oh, sorry!");
        await conversation.Answer(next, "wait, what time is it?", "", _ => { }, (_, _) => { });
        var sent = string.Join("\n", next.Requests.Single().Select(m => m.Text));
        Assert.Contains("Welcome back! I was thinking we—", sent);
        Assert.DoesNotContain("we could try", sent.Replace("You hadn't said yet: \"could try", ""));
        Assert.Contains("[cabinet] The player cut you off there. You hadn't said yet: \"could try the hard one tonight.\"", sent);
    }

    [Fact]
    public async Task A_reply_stopped_while_still_being_written_keeps_what_was_written()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        using var stop = new CancellationTokenSource();
        var model = new FakeModel("Welcome back! ", "I was thinking ", "we could try ", "the hard one.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversation.Answer(model, "hi", "", shown =>
        {
            if (shown.StartsWith("I was"))
                stop.Cancel();                              // the player cuts in after the second piece
        }, (_, _) => { }, stop.Token));

        Assert.Equal("Welcome back! I was thinking", temp.Store.After("shino", 0).Last().Text);
        Assert.Equal("I was thinking", conversation.CutOff("Welcome back! ".Length));
        Assert.Equal(["Welcome back!", "The player cut you off there. You hadn't said yet: \"I was thinking\""],
                     temp.Store.After("shino", 0).TakeLast(2).Select(l => l.Text));
    }
}
