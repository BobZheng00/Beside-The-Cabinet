using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

/// <summary>What earns play time, as the player chose it (spec 2026-10-27-what-earns-play-time.md).</summary>
public class SourcesTests
{
    [Fact]
    public void A_profile_from_before_the_choice_keeps_focus_and_activities_and_LeetCode_when_a_name_is_set()
    {
        Assert.Equal(Earning.All, Earning.Of(focus: true, activities: true, leetCode: null, username: "BobSmith"));
        Assert.Equal(new Earning(true, true, false), Earning.Of(focus: true, activities: true, leetCode: null, username: null));
    }

    [Fact]
    public void LeetCode_earns_only_when_ticked_and_with_a_name()
    {
        Assert.False(Earning.Of(true, true, leetCode: true, username: " ").LeetCode);
        Assert.False(Earning.Of(true, true, leetCode: false, username: "BobSmith").LeetCode);
        Assert.True(Earning.Of(true, true, leetCode: true, username: "BobSmith").LeetCode);
    }

    [Fact]
    public void Nothing_ticked_is_no_play_time()
    {
        Assert.False(Earning.Of(false, false, false, "BobSmith").Any);
        Assert.False(Earning.Of(false, false, null, null).Any);
        Assert.True(Earning.Of(false, false, null, "BobSmith").Any);
    }

    [Fact]
    public void Focus_that_doesnt_earn_is_kept_and_pays_nothing_nor_makes_a_good_day()
    {
        var m = new Machine(rules: new Rules { Earns = new Earning(false, true, true) });

        var focused = Assert.Single(m.Focus(TimeSpan.FromHours(1)).OfType<FocusPaid>());

        Assert.Equal(Min(60), focused.FocusedMs);
        Assert.Equal(0, focused.Paid.EarnedMs);
        Assert.Equal(0, m.State.TimeLeftMs(m.Rules));
        Assert.Equal(Min(60), m.State.Today!.FocusedMs);
        Assert.Empty(m.State.GoodDays);
    }

    [Fact]
    public void Focus_that_doesnt_earn_leaves_a_debt_as_it_is()
    {
        var m = new Machine(rules: new Rules { Earns = new Earning(false, true, true) });
        m.Play(TimeSpan.FromMinutes(5));
        m.Quit();
        var owed = m.State.DebtMs;

        m.Focus(TimeSpan.FromMinutes(40));

        Assert.Equal(owed, m.State.DebtMs);
    }

    [Fact]
    public void An_activity_that_doesnt_earn_pays_nothing_nor_makes_a_good_day()
    {
        var m = new Machine(rules: new Rules { Earns = new Earning(true, false, true) });

        Assert.Empty(m.Send(new ActivityDone("the gym", Size.Big, 1)));

        Assert.Equal(0, m.State.TimeLeftMs(m.Rules));
        Assert.Empty(m.State.Today!.Activities);
        Assert.Empty(m.State.GoodDays);
    }
}
