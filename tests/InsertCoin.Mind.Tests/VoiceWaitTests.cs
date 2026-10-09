namespace InsertCoin.Mind.Tests;

/// <summary>Waiting for her: thinking sounds, answering her card aloud, and no long quiet before she speaks.</summary>
public class VoiceWaitTests
{
    private static readonly TimeSpan S = TimeSpan.FromSeconds(1);

    [Fact]
    public void A_thinking_sound_comes_only_when_her_words_are_slow_and_twice_at_most()
    {
        Assert.False(Thinking.Due(true, false, 0.5 * S, 0.5 * S, 0));          // a quick reply: no sound
        Assert.True(Thinking.Due(true, false, 1.2 * S, 1.2 * S, 0));           // a second with nothing: one
        Assert.False(Thinking.Due(true, false, 0.4 * S, 3 * S, 0));            // words are coming in
        Assert.False(Thinking.Due(true, true, 3 * S, 3 * S, 0));               // she is speaking
        Assert.False(Thinking.Due(true, false, 3 * S, 3 * S, 1));              // a second one waits longer…
        Assert.True(Thinking.Due(true, false, 7 * S, 6 * S, 1));               // …then may come
        Assert.False(Thinking.Due(true, false, 30 * S, 30 * S, 2));            // and that's all
        Assert.False(Thinking.Due(false, false, 30 * S, 30 * S, 0));           // the reply is done
    }

    [Fact]
    public void Thinking_sounds_vary_never_one_of_the_last_two_again()
    {
        var thinking = new Thinking(new Random(7));
        var picks = Enumerable.Range(0, 200).Select(_ => thinking.Pick(8)).ToList();

        for (var i = 2; i < picks.Count; i++)
            Assert.DoesNotContain(picks[i], new[] { picks[i - 1], picks[i - 2] });
        Assert.Equal(8, picks.Distinct().Count());
        Assert.Equal([0, 0, 0], Enumerable.Range(0, 3).Select(_ => new Thinking().Pick(1)));
    }

    [Fact]
    public void Each_voice_language_has_its_own_sounds_and_others_none()
    {
        Assert.Contains("えっと…", Thinking.Sounds("ja"));
        Assert.Contains("嗯……", Thinking.Sounds("zh"));
        Assert.True(Thinking.Sounds("en").Count >= 6);
        Assert.Empty(Thinking.Sounds("fr"));
    }

    [Theory]
    [InlineData("Yes.", Answer.Once)]
    [InlineData("Yeah, go ahead!", Answer.Once)]
    [InlineData("um, okay", Answer.Once)]
    [InlineData("yes please", Answer.Once)]
    [InlineData("好的。", Answer.Once)]
    [InlineData("好的吧", Answer.Once)]
    [InlineData("可以", Answer.Once)]
    [InlineData("うん、いいよね", Answer.Once)]
    [InlineData("Always.", Answer.Always)]
    [InlineData("Yes, always!", Answer.Always)]
    [InlineData("以后都可以", Answer.Always)]
    [InlineData("No.", Answer.No)]
    [InlineData("Don't.", Answer.No)]
    [InlineData("no thanks", Answer.No)]
    [InlineData("不用了。", Answer.No)]
    [InlineData("まだ", Answer.No)]
    public void A_short_answer_said_aloud_answers_her_card(string heard, Answer answer) => Assert.Equal(answer, SpokenAnswer.Of(heard));

    [Theory]
    [InlineData("yes but first tell me what it is")]
    [InlineData("what's on the page?")]
    [InlineData("open YouTube instead")]
    [InlineData("")]
    [InlineData("please")]
    public void Anything_more_is_something_else_to_say_to_her(string heard) => Assert.Null(SpokenAnswer.Of(heard));

    [Fact]
    public void The_quiet_before_her_first_sound_is_dropped_keeping_a_short_lead_in()
    {
        const int rate = 1000;                              // a thousand samples a second, to count easily
        var quiet = new QuietStart(rate);
        var line = new float[300];
        line[250] = 0.5f;

        Assert.Empty(quiet.Feed(new float[2000]));          // two seconds of quiet: nothing to play
        var heard = quiet.Feed(line);
        var rest = quiet.Feed([0.001f, 0.001f]);

        Assert.Equal(110, heard.Length);                    // 60 ms of lead-in before the sound, then the rest
        Assert.Equal(0.5f, heard[60]);
        Assert.Equal(2, rest.Length);                       // once she is sounding, quiet stays: it is speech
        Assert.Equal(TimeSpan.FromSeconds(2.19), quiet.DroppedTime);
    }

    [Fact]
    public void A_long_quiet_is_meant_and_passes_on()
    {
        var quiet = new QuietStart(1000);

        Assert.Empty(quiet.Feed(new float[3900]));
        Assert.Equal(200, quiet.Feed(new float[200]).Length);
    }

    [Fact]
    public void A_thinking_sound_is_kept_without_its_quiet_at_both_ends()
    {
        var clip = new float[1000];
        clip[300] = 0.3f;
        clip[400] = -0.3f;

        var trimmed = QuietStart.Trim(clip, 1000);

        Assert.Equal(60 + 101 + 120, trimmed.Length);
        Assert.Empty(QuietStart.Trim(new float[500], 1000));
    }
}
