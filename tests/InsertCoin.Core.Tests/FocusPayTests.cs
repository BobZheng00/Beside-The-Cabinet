using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class FocusPayTests
{
    [Fact]
    public void Half_an_hour_of_focus_adds_seven_and_a_half_minutes_at_once()
    {
        var m = new Machine();

        var paid = Assert.Single(m.Focus(TimeSpan.FromMinutes(30)).OfType<FocusPaid>());

        Assert.Equal(Min(30), paid.FocusedMs);
        Assert.Equal(Min(7.5), paid.Paid.CreditedMs);
        Assert.Equal(0, paid.Paid.Coins);
        Assert.Equal(Min(7.5), m.State.FuelMs);
    }

    [Fact]
    public void A_coin_drops_when_focus_completes_one()
    {
        var m = new Machine();
        m.Focus(TimeSpan.FromMinutes(30));

        var second = m.Focus(TimeSpan.FromMinutes(30)).OfType<FocusPaid>().Single();

        Assert.Equal(1, second.Paid.Coins);
        Assert.Equal(1, m.State.Credits);
        Assert.Equal(0, m.State.FuelMs);
    }

    [Fact]
    public void An_hour_of_focus_is_a_coin_and_is_counted_for_the_day_and_week()
    {
        var m = new Machine();

        Assert.Equal(1, m.Focus(TimeSpan.FromHours(1)).OfType<FocusPaid>().Single().Paid.Coins);

        Assert.Equal(Min(60), m.State.Today!.FocusedMs);
        Assert.Equal(Min(60), m.State.Week.FocusedMs);
        Assert.Equal(Min(15), m.State.Week.EarnedMs);
    }

    [Fact]
    public void Focus_pays_back_debt_first()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(5));                 // 7.5 min of debt
        m.Quit();

        var events = m.Focus(TimeSpan.FromMinutes(40));  // 10 min: 7.5 of it to the debt

        Assert.IsType<FocusPaid>(events[0]);
        Assert.Equal(Min(7.5), Assert.Single(events.OfType<DebtPaid>()).PaidMs);
        Assert.Equal(0, m.State.DebtMs);
        Assert.Equal(Min(2.5), m.State.FuelMs);
    }

    [Fact]
    public void A_full_tank_stays_in_the_slot_while_a_game_runs()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMinutes(5));                 // 10 min left on the token in the slot

        var paid = m.Focus(TimeSpan.FromMinutes(20)).OfType<FocusPaid>().Single();

        Assert.Equal(0, paid.Paid.Coins);
        Assert.Equal(Min(15), m.State.FuelMs);
        m.Play(TimeSpan.FromMinutes(1));
        Assert.Equal(Min(14), m.State.FuelMs);
        Assert.Empty(m.Events.OfType<CreditsOut>());
    }

    [Fact]
    public void No_time_focused_pays_nothing()
    {
        var m = new Machine();

        Assert.Empty(m.Focus(TimeSpan.Zero));
    }
}
