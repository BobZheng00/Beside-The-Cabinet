using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class FadeTests
{
    // Thursday 2026-10-01 04:00 local: the end of the first night after T0.
    private static readonly DateTimeOffset Morning = new(2026, 10, 1, 4, 0, 0, T0.Offset);

    [Fact]
    public void Each_night_half_of_the_unplayed_time_fades()
    {
        var m = new Machine();
        m.GiveTokens(3);
        Assert.Empty(m.CheckAt(Morning.AddMinutes(-1)).OfType<Faded>());

        var faded = Assert.Single(m.CheckAt(Morning).OfType<Faded>());

        Assert.Equal(Min(22.5), faded.FadedMs);
        Assert.Equal(Min(22.5), faded.LeftMs);
        Assert.Equal(1, m.State.Credits);
        Assert.Equal(Min(7.5), m.State.FuelMs);
        Assert.Equal(Min(22.5), m.State.Today!.FadedMs);
    }

    [Fact]
    public void Debt_never_fades()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(10));
        m.Quit();

        Assert.Empty(m.CheckAt(Morning).OfType<Faded>());
        Assert.Equal(Min(15), m.State.DebtMs);
    }

    [Fact]
    public void The_fade_waits_for_a_running_session_to_end()
    {
        var m = new Machine();
        m.GiveTokens(4);
        m.Now = Morning.AddMinutes(-5);
        m.Play(TimeSpan.FromMinutes(10));                // crosses 04:00 mid-game

        Assert.Empty(m.Events.OfType<Faded>());
        Assert.Equal(Min(50), m.State.TimeLeftMs(m.Rules));

        m.Quit();
        Assert.Equal(Min(25), Assert.Single(m.Events.OfType<Faded>()).FadedMs);
        Assert.Equal(Min(25), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_pending_fade_applies_before_new_earnings()
    {
        var m = new Machine();
        m.GiveTokens(2);
        m.Now = Morning.AddMinutes(-1);
        m.Play(TimeSpan.FromMinutes(2));                 // the game holds the night open: 28 min left

        var events = m.Solve("thursday", Difficulty.Medium);

        Assert.IsType<Faded>(events[0]);
        Assert.IsType<CoinsEarned>(events[1]);
        Assert.Equal(Min(14 + 30), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void Each_missed_night_takes_its_share()
    {
        var m = new Machine();
        m.GiveTokens(4);

        var faded = Assert.Single(m.CheckAt(Morning.AddDays(2)).OfType<Faded>());   // three nights: Wednesday to Saturday

        Assert.Equal(Min(52.5), faded.FadedMs);
        Assert.Equal(Min(7.5), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void Saved_time_never_passes_a_days_cap()
    {
        var m = new Machine();
        for (var day = 0; day < 14; day++)
        {
            m.CheckAt(T0.AddDays(day));
            Assert.True(m.State.TimeLeftMs(m.Rules) <= m.Rules.WeekendCapMs);
            m.Focus(TimeSpan.FromHours(12));             // more than any cap: each day earns all it can
            Assert.Equal(m.State.Today!.CapMs, m.State.Today.EarnedMs);
        }
    }

    [Fact]
    public void The_weeks_numbers_start_again_on_Monday_and_nothing_else_expires()
    {
        var m = new Machine();
        m.Solve("wednesday");

        m.CheckAt(new DateTimeOffset(2026, 10, 5, 4, 0, 0, T0.Offset));   // Monday 04:00, five nights on

        Assert.Equal(0, m.State.Week.Solves);
        Assert.Equal(new DateOnly(2026, 10, 5), m.State.WeekStart);
        Assert.Equal((long)Math.Round(Min(15) / 32.0), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_save_from_before_days_were_kept_starts_its_first_day_without_a_fade()
    {
        var m = new Machine();
        m.GiveTokens(2);
        m.State = m.State with { Today = null };

        Assert.Empty(m.CheckAt(Morning.AddDays(3)).OfType<Faded>());

        Assert.Equal(Min(30), m.State.TimeLeftMs(m.Rules));
        Assert.Equal(new DateOnly(2026, 10, 4), m.State.Today!.Day);
    }
}
