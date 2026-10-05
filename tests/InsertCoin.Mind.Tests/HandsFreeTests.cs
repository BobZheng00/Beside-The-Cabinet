namespace InsertCoin.Mind.Tests;

/// <summary>When she listens without the talk key: only as the player has chosen, and never where they haven't allowed it.</summary>
public class HandsFreeTests
{
    [Fact]
    public void Out_of_the_box_she_listens_only_in_a_conversation()
    {
        var rules = new HandsFree();

        Assert.Null(rules.After(HandsFree.Said.Answer, conversing: false));
        Assert.Equal(HandsFree.ConversationSeconds, rules.After(HandsFree.Said.Remark, conversing: true));
        Assert.False(rules.Allows(inCall: true, fullScreen: false, playing: false));
        Assert.False(rules.Allows(inCall: false, fullScreen: true, playing: false));
        Assert.False(rules.Allows(inCall: false, fullScreen: false, playing: true));
        Assert.True(rules.Allows(inCall: false, fullScreen: false, playing: false));
    }

    [Fact]
    public void Follow_up_listening_comes_after_the_kinds_of_line_chosen_for_as_long_as_chosen_but_never_under_three_seconds()
    {
        var rules = new HandsFree { FollowUp = true, FollowUpSeconds = 1 };

        Assert.Equal(HandsFree.Shortest, rules.After(HandsFree.Said.Answer, conversing: false));
        Assert.Equal(HandsFree.Shortest, rules.After(HandsFree.Said.Question, conversing: false));
        Assert.Null(rules.After(HandsFree.Said.Remark, conversing: false));
        Assert.Equal(8, (rules with { FollowUpSeconds = 8, AfterQuestions = false }).After(HandsFree.Said.Answer, conversing: false));
        Assert.Null((rules with { AfterQuestions = false }).After(HandsFree.Said.Question, conversing: false));
        Assert.Equal(HandsFree.Longest, (rules with { FollowUpSeconds = 90 }).After(HandsFree.Said.Answer, conversing: false));
    }

    [Fact]
    public void What_she_says_on_her_own_is_a_question_when_it_ends_with_one()
    {
        Assert.Equal(HandsFree.Said.Answer, HandsFree.Kind(answer: true, "Here you go."));
        Assert.Equal(HandsFree.Said.Question, HandsFree.Kind(answer: false, "How did the interview go?"));
        Assert.Equal(HandsFree.Said.Question, HandsFree.Kind(answer: false, "面接はどうだった？」 "));
        Assert.Equal(HandsFree.Said.Remark, HandsFree.Kind(answer: false, "Time for your vitamins."));
    }
}
