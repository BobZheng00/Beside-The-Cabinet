using System.Collections.Immutable;
using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

/// <summary>The streak counts good days: a solve, an activity, or a focus part's worth of focus.</summary>
public class StreakTests
{
    private static readonly TimeSpan Part = TimeSpan.FromMinutes(25);

    [Fact]
    public void Focus_or_an_activity_makes_a_good_day_and_the_third_pays_a_star()
    {
        var m = new Machine();
        m.Focus(Part);                                      // Wednesday: focus
        m.Now += TimeSpan.FromDays(1);
        m.Send(new ActivityDone("a walk", Size.Small, 1));  // Thursday: a walk
        m.Now += TimeSpan.FromDays(1);
        var before = m.State.Credits;

        var star = Assert.Single(m.Focus(Part).OfType<StreakStar>());    // Friday: focus again

        Assert.Equal(3, star.Streak);
        Assert.Equal(m.Rules.TokenMs, star.Paid.CreditedMs);
        Assert.Equal(3, GameCalendar.CurrentStreak(m.State, m.Now, m.Rules));
        Assert.Equal(3, m.State.BestStreak);
        Assert.True(m.State.Credits > before);
    }

    [Fact]
    public void Less_than_a_focus_parts_worth_doesnt_count_until_the_days_focus_adds_up()
    {
        var m = new Machine();

        m.Focus(TimeSpan.FromMinutes(15));
        Assert.Empty(m.State.StreakDays());

        m.Focus(TimeSpan.FromMinutes(10));
        Assert.Equal([new DateOnly(2026, 9, 30)], m.State.StreakDays());
    }

    [Fact]
    public void A_day_pays_one_star_whatever_makes_it_good_first()
    {
        var m = new Machine();
        m.Solve("mon");
        m.Now += TimeSpan.FromDays(1);
        m.Solve("tue");
        m.Now += TimeSpan.FromDays(1);

        Assert.Single(m.Focus(Part).OfType<StreakStar>());
        var solved = m.Solve("wed").OfType<CoinsEarned>().Single();
        Assert.Empty(m.Send(new ActivityDone("the gym", Size.Medium, 1)).OfType<StreakStar>());

        Assert.False(solved.StarToken);                     // the day was good already: its star was paid
        Assert.Equal(1, solved.Tokens);
    }

    [Fact]
    public void A_solve_still_brings_its_star_with_its_coins()
    {
        var m = new Machine();
        m.Focus(Part);
        m.Now += TimeSpan.FromDays(1);
        m.Send(new ActivityDone("cooking dinner", Size.Small, 0.5));       // half counts as a good day too
        m.Now += TimeSpan.FromDays(1);

        var events = m.Solve("third");

        Assert.True(events.OfType<CoinsEarned>().Single().StarToken);
        Assert.Empty(events.OfType<StreakStar>());
    }

    [Fact]
    public void Past_the_cap_the_day_still_counts_but_the_star_isnt_paid()
    {
        var m = new Machine();
        m.Focus(Part);
        m.Now += TimeSpan.FromDays(1);
        m.Focus(Part);
        m.Now += TimeSpan.FromDays(1);
        m.Focus(TimeSpan.FromHours(8));                     // 2:00 of play: a Friday's cap, and a good day

        var star = Assert.Single(m.Events.OfType<StreakStar>());

        Assert.Equal(0, star.Paid.CreditedMs);
        Assert.Equal(m.Rules.TokenMs, star.Paid.OverCapMs);
        Assert.Equal(3, m.State.BestStreak);
    }

    [Fact]
    public void Solve_days_kept_before_good_days_were_counted_still_make_the_streak()
    {
        var m = new Machine();
        m.State = m.State with { SolveDays = ImmutableSortedSet.Create(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 29)) };

        var star = Assert.Single(m.Focus(Part).OfType<StreakStar>());

        Assert.Equal(3, star.Streak);
    }
}
