using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class SpendingTests
{
    [Fact]
    public void Launching_a_game_inserts_a_token_without_charging_the_start_tick()
    {
        var m = new Machine();
        m.GiveTokens(2);

        var events = m.Play(TimeSpan.Zero);

        Assert.IsType<SessionStarted>(events[0]);
        Assert.Equal(1, Assert.Single(events.OfType<TokenInserted>()).CreditsLeft);
        Assert.Equal(1, m.State.Credits);
        Assert.Equal(Min(15), m.State.FuelMs);
    }

    [Fact]
    public void Fuel_burns_and_the_next_token_goes_in_by_itself()
    {
        var m = new Machine();
        m.GiveTokens(2);

        m.Play(TimeSpan.FromMinutes(20));

        Assert.Equal(2, m.Events.OfType<TokenInserted>().Count());
        Assert.Equal(0, m.State.Credits);
        Assert.Equal(Min(10), m.State.FuelMs);
        Assert.Equal(Min(20), m.State.Session!.DrawnMs);
    }

    [Fact]
    public void Two_games_at_once_are_charged_once()
    {
        var m = new Machine();
        m.GiveTokens(1);
        RunningGame[] both = [new("1", "A"), new("2", "B")];

        m.Tick(both, Check, TimeSpan.Zero);
        for (var i = 0; i < 12; i++)
            m.Tick(both, Check, TimeSpan.Zero);

        Assert.Equal(Min(14), m.State.FuelMs);
        Assert.Equal(new[] { "A", "B" }, m.State.Session!.GameNames);
    }

    [Fact]
    public void Warnings_fire_when_total_time_crosses_five_and_one_minute()
    {
        var m = new Machine();
        m.GiveTokens(1);

        m.Play(TimeSpan.FromMinutes(9.5));
        Assert.Empty(m.Events.OfType<LowTime>());

        m.Play(TimeSpan.FromMinutes(1));
        Assert.Equal(Warning.FiveMinutes, Assert.Single(m.Events.OfType<LowTime>()).Level);

        m.Play(TimeSpan.FromMinutes(4));
        Assert.Equal(new[] { Warning.FiveMinutes, Warning.OneMinute }, m.Events.OfType<LowTime>().Select(w => w.Level));
    }

    [Fact]
    public void Starting_a_game_with_little_time_warns_immediately()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMinutes(12));
        m.Quit();
        m.Events.Clear();

        m.Play(TimeSpan.Zero);

        Assert.Equal(Warning.FiveMinutes, Assert.Single(m.Events.OfType<LowTime>()).Level);
    }

    [Fact]
    public void Warnings_rearm_after_earning_time_back()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMinutes(11));
        m.Solve("mid-game");
        m.Play(TimeSpan.FromMinutes(15));

        Assert.Equal(2, m.Events.OfType<LowTime>().Count(w => w.Level == Warning.FiveMinutes));
    }

    [Fact]
    public void Running_out_gives_credits_out_then_debt_at_one_and_a_half_times()
    {
        var m = new Machine();
        m.GiveTokens(1);

        m.Play(TimeSpan.FromMinutes(25));

        Assert.Single(m.Events.OfType<CreditsOut>());
        Assert.Equal(Min(15), m.State.DebtMs);
        Assert.Equal(0, m.State.FuelMs);
        Assert.Equal(Min(15), m.State.Session!.DebtAddedMs);
    }

    [Fact]
    public void An_iou_prints_every_ten_minutes_of_overtime()
    {
        var m = new Machine();

        m.Play(TimeSpan.FromMinutes(35));

        var ious = m.Events.OfType<IouPrinted>().ToList();
        Assert.Equal(new[] { 1, 2, 3 }, ious.Select(i => i.TicketNumber));
        Assert.Equal(3, m.State.IouTickets(m.Rules));
        Assert.Equal(3, m.State.Session!.IousPrinted);
        Assert.Equal(3, m.State.Week.IousPrinted);
    }

    [Fact]
    public void Afk_pauses_the_meter_and_input_resumes_it()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMinutes(1));

        m.Play(TimeSpan.FromMinutes(30), idle: TimeSpan.FromMinutes(10));
        Assert.Single(m.Events.OfType<Paused>());
        Assert.True(m.State.Paused);
        Assert.Equal(Min(14), m.State.FuelMs);

        m.Play(TimeSpan.FromMinutes(1));
        Assert.Single(m.Events.OfType<Resumed>());
        Assert.Equal(Min(13), m.State.FuelMs);
    }

    [Fact]
    public void A_long_gap_between_checks_is_capped()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.Zero);

        m.Tick([new RunningGame("1", "G")], TimeSpan.FromHours(8), TimeSpan.Zero);

        Assert.Equal(Min(15) - 30_000, m.State.FuelMs);
    }

    [Fact]
    public void Session_ends_after_two_missed_checks_with_a_receipt()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMinutes(20));

        Assert.Empty(m.Tick([], Check, TimeSpan.Zero));
        var ended = Assert.Single(m.Tick([], Check, TimeSpan.Zero).OfType<SessionEnded>());

        Assert.Null(m.State.Session);
        Assert.Equal(Min(20), ended.Receipt.PlayedMs);
        Assert.Equal(Min(15), ended.Receipt.DrawnMs);
        Assert.Equal(Min(7.5), ended.Receipt.DebtAddedMs);
        Assert.Equal(new[] { "Graveyard Keeper 2" }, ended.Receipt.GameNames);
    }

    [Fact]
    public void A_game_back_after_one_miss_continues_the_same_session()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMinutes(1));
        m.Tick([], Check, TimeSpan.Zero);
        m.Play(TimeSpan.FromMinutes(1));

        Assert.Single(m.Events.OfType<SessionStarted>());
        Assert.Empty(m.Events.OfType<SessionEnded>());
    }
}
