using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class WeeklyResetTests
{
    // Monday 2026-10-05 04:00 local is the next week boundary after T0.
    private static readonly DateTimeOffset Boundary = new(2026, 10, 5, 4, 0, 0, T0.Offset);

    [Fact]
    public void Monday_4am_expires_credits_and_fuel_but_keeps_debt()
    {
        var m = new Machine();
        m.GiveTokens(3);
        m.Now = Boundary.AddMinutes(-1);
        m.Tick([], TimeSpan.Zero, TimeSpan.Zero);
        Assert.Empty(m.Events.OfType<WeeklyReset>());

        m.Now = Boundary;
        var reset = Assert.Single(m.Tick([], TimeSpan.Zero, TimeSpan.Zero).OfType<WeeklyReset>());

        Assert.Equal(Min(45), reset.ExpiredMs);
        Assert.Equal(0, m.State.Credits);
        Assert.Equal(0, m.State.Week.Solves);
    }

    [Fact]
    public void Debt_survives_the_reset()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(10));
        m.Quit();
        m.Now = Boundary;

        var reset = m.Tick([], TimeSpan.Zero, TimeSpan.Zero).OfType<WeeklyReset>().Single();

        Assert.Equal(Min(15), reset.DebtCarriedMs);
        Assert.Equal(Min(15), m.State.DebtMs);
    }

    [Fact]
    public void Reset_waits_for_the_running_session_to_end()
    {
        var m = new Machine();
        m.GiveTokens(4);
        m.Now = Boundary.AddMinutes(-5);
        m.Play(TimeSpan.FromMinutes(10));                // crosses the boundary mid-game

        Assert.Empty(m.Events.OfType<WeeklyReset>());
        Assert.Equal(Min(50), m.State.TimeLeftMs(m.Rules));

        m.Quit();
        Assert.Equal(Min(50), Assert.Single(m.Events.OfType<WeeklyReset>()).ExpiredMs);
        Assert.Equal(0, m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_pending_reset_applies_before_new_earnings()
    {
        var m = new Machine();
        m.GiveTokens(2);
        m.Now = Boundary.AddMinutes(-1);
        m.Play(TimeSpan.FromMinutes(2));

        var events = m.Solve("monday", Difficulty.Medium);

        Assert.IsType<WeeklyReset>(events[0]);
        Assert.Equal(2, m.State.Credits);
    }

    [Fact]
    public void Missed_weeks_reset_once()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Now = Boundary.AddDays(21);

        m.Tick([], TimeSpan.Zero, TimeSpan.Zero);
        m.Tick([], TimeSpan.Zero, TimeSpan.Zero);

        Assert.Single(m.Events.OfType<WeeklyReset>());
        Assert.Equal(GameCalendar.WeekStartOf(GameCalendar.DayOf(m.Now, m.Rules)), m.State.WeekStart);
    }
}
