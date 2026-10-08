using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class CapTests
{
    private static readonly DateTimeOffset ThursdayMorning = new(2026, 10, 1, 9, 0, 0, T0.Offset);

    [Fact]
    public void Earnings_stop_at_the_days_cap_and_the_rest_counts_as_over_it()
    {
        var m = new Machine();                           // a Wednesday: 1:30
        m.Solve("hard-1", Difficulty.Hard);

        var events = m.Solve("hard-2", Difficulty.Hard);

        var earned = events.OfType<CoinsEarned>().Single();
        Assert.Equal(Min(60), earned.Paid.EarnedMs);
        Assert.Equal(Min(30), earned.Paid.CreditedMs);
        Assert.Equal(Min(30), earned.Paid.OverCapMs);
        Assert.Equal(2, earned.Paid.Coins);
        Assert.Equal(Min(90), Assert.Single(events.OfType<CapReached>()).CapMs);
        Assert.Equal(6, m.State.Credits);
        Assert.Equal(Min(30), m.State.Today!.OverCapMs);
    }

    [Fact]
    public void Past_the_cap_a_solve_still_counts_but_pays_nothing()
    {
        var m = new Machine();
        m.Focus(TimeSpan.FromHours(6));                  // the whole 1:30

        var events = m.Solve("easy");

        Assert.Equal(0, events.OfType<CoinsEarned>().Single().Paid.CreditedMs);
        Assert.Empty(events.OfType<CapReached>());
        Assert.Equal(6, m.State.Credits);
        Assert.Contains("easy", m.State.SeenTaskIds);    // done, so it never pays later either
        Assert.Equal(1, m.State.Today!.Solves);
    }

    [Theory]
    [InlineData(2026, 9, 28, 90)]                        // Monday
    [InlineData(2026, 10, 1, 90)]                        // Thursday
    [InlineData(2026, 10, 2, 120)]                       // Friday
    [InlineData(2026, 10, 3, 150)]                       // Saturday
    [InlineData(2026, 10, 4, 150)]                       // Sunday
    public void The_cap_depends_on_the_day_of_the_week(int year, int month, int day, int minutes)
    {
        var m = new Machine(new DateTimeOffset(year, month, day, 12, 0, 0, T0.Offset));

        m.Focus(TimeSpan.FromHours(20));

        Assert.Equal(Min(minutes), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_friday_night_past_midnight_keeps_fridays_cap()
    {
        var m = new Machine(new DateTimeOffset(2026, 10, 3, 1, 0, 0, T0.Offset));   // Saturday 01:00, still Friday's game day

        m.Focus(TimeSpan.FromHours(20));

        Assert.Equal(Min(120), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void Paying_back_debt_does_not_count_towards_the_cap()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(20));                // 30 min of debt
        m.Quit();

        m.Focus(TimeSpan.FromHours(10));                 // 150 min earned: 30 to the debt, 90 new, 30 over

        Assert.Equal(0, m.State.DebtMs);
        Assert.Equal(Min(90), m.State.TimeLeftMs(m.Rules));
        Assert.Equal(Min(30), m.State.Today!.DebtPaidMs);
        Assert.Equal(Min(30), m.State.Today.OverCapMs);
    }

    [Fact]
    public void What_is_left_from_yesterday_is_not_part_of_todays_cap()
    {
        var m = new Machine();
        m.Focus(TimeSpan.FromHours(6));                  // Wednesday's 1:30

        m.Now = ThursdayMorning;                         // 45 min left after the fade
        m.Focus(TimeSpan.FromHours(6));                  // and Thursday's own 1:30

        Assert.Equal(Min(45 + 90), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_change_to_the_cap_counts_from_the_next_day()
    {
        var m = new Machine();
        m.Change(m.Rules with { WeekdayCapMs = Min(30) });

        m.Focus(TimeSpan.FromHours(10));
        Assert.Equal(Min(90), m.State.Today!.EarnedMs);  // today keeps the cap it began with

        m.Now = ThursdayMorning;
        m.Focus(TimeSpan.FromHours(10));
        Assert.Equal(Min(30), m.State.Today!.EarnedMs);
    }
}
