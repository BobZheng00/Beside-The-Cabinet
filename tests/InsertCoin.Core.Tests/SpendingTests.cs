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
        Assert.Equal(Min(14) - 5_000, m.State.FuelMs);   // the check it paused in is spent, as the screen counted it down

        m.Play(TimeSpan.FromMinutes(1));
        Assert.Single(m.Events.OfType<Resumed>());
        Assert.Equal(Min(13), m.State.FuelMs);           // and the one it resumed in isn't, as the screen stood still
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
    public void The_countdown_between_checks_stops_where_a_check_is_capped_so_the_time_left_shown_never_jumps_up_after_a_stall()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.Zero);
        var shown = m.State.TimeLeftMs(m.Rules) - m.Ledger.SpentSince(m.State, TimeSpan.FromHours(8), m.Now);

        m.Tick([new RunningGame("1", "G")], TimeSpan.FromHours(8), TimeSpan.Zero);   // the PC asleep

        Assert.Equal(Min(15) - 30_000, shown);
        Assert.Equal(shown, m.State.TimeLeftMs(m.Rules));
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
        Assert.Equal(Min(20) + 5_000, ended.Receipt.PlayedMs);       // and the first missed check: the screen counted on
        Assert.Equal(Min(15), ended.Receipt.DrawnMs);
        Assert.Equal(Min(7.5) + 7_500, ended.Receipt.DebtAddedMs);
        Assert.Equal(new[] { "Graveyard Keeper 2" }, ended.Receipt.GameNames);
    }

    [Theory]
    [InlineData(4_000)]                                 // out of time in the check it closes in
    [InlineData(64_000)]                                // the one-minute warning's
    [InlineData(304_000)]                               // the five-minute warning's
    public void The_check_a_game_closes_in_is_spent_quietly_so_nothing_warns_or_prints_once_it_is_gone(long leftAtClose)
    {
        var m = new Machine();
        m.GiveTokens(1);
        var game = new[] { new RunningGame("1", "G") };
        m.Play(TimeSpan.Zero, "G");
        while (m.State.TimeLeftMs(m.Rules) > leftAtClose + 5_000)
            m.Tick(game, Check, TimeSpan.Zero);
        m.Tick(game, TimeSpan.FromMilliseconds(m.State.TimeLeftMs(m.Rules) - leftAtClose), TimeSpan.Zero);
        m.Events.Clear();

        m.Quit();

        Assert.DoesNotContain(m.Events, e => e is LowTime or CreditsOut or IouPrinted);
        Assert.Single(m.Events.OfType<SessionEnded>());
        Assert.Equal(Math.Max(0, leftAtClose - 5_000), m.State.TimeLeftMs(m.Rules));
        Assert.Equal((long)(Math.Max(0, 5_000 - leftAtClose) * m.Rules.DebtMultiplier), m.State.DebtMs);
    }

    [Fact]
    public void Out_of_time_in_the_check_a_game_pauses_in_is_said_when_it_goes_on()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMilliseconds(Min(15)) - Check);     // 5 s left
        m.Events.Clear();

        m.Tick([new RunningGame("1", "Graveyard Keeper 2")], Check, TimeSpan.FromMinutes(10));
        Assert.True(m.State.Paused);
        Assert.Equal(0, m.State.TimeLeftMs(m.Rules));
        Assert.Empty(m.Events.OfType<CreditsOut>());

        m.Tick([new RunningGame("1", "Graveyard Keeper 2")], Check, TimeSpan.Zero);
        Assert.Single(m.Events.OfType<CreditsOut>());
    }

    [Theory]
    [InlineData(false)]                                 // the game closes
    [InlineData(true)]                                  // the game pauses, then goes on
    public void No_token_goes_in_in_the_check_a_game_closes_or_pauses_in_and_one_it_needed_goes_in_as_it_goes_on(bool paused)
    {
        var m = new Machine();
        m.GiveTokens(2);
        var game = new[] { new RunningGame("1", "G") };
        m.Play(TimeSpan.Zero, "G");
        while (m.State.FuelMs > 7_000)
            m.Tick(game, Check, TimeSpan.Zero);
        m.Tick(game, TimeSpan.FromMilliseconds(m.State.FuelMs - 2_000), TimeSpan.Zero);
        Assert.Equal((1, 2_000L), (m.State.Credits, m.State.FuelMs));    // 2 s on the token in the slot, one to come
        m.Events.Clear();

        if (paused)
            m.Tick(game, Check, TimeSpan.FromMinutes(10));
        else
            m.Quit();

        Assert.Empty(m.Events.OfType<TokenInserted>());
        Assert.Equal(Min(15) - 3_000, m.State.TimeLeftMs(m.Rules));    // the 3 s the tank lacked, taken ahead

        m.Tick(game, Check, TimeSpan.Zero);                 // it goes on, or the next session starts
        Assert.Single(m.Events.OfType<TokenInserted>());
        Assert.Equal((0, Min(15) - 3_000), (m.State.Credits, m.State.FuelMs));
    }

    [Fact]
    public void A_session_saved_open_spends_nothing_at_the_first_check_after_the_app_starts_which_sends_no_time()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Play(TimeSpan.FromMilliseconds(Min(15)) - Check);     // 5 s left as the PC shut down
        var left = m.State.TimeLeftMs(m.Rules);
        m.Events.Clear();
        m.Now = m.Now.AddHours(10);

        m.Tick([], TimeSpan.Zero, TimeSpan.Zero);       // the app's first check this run, no game running
        m.Tick([], Check, TimeSpan.Zero);

        Assert.Single(m.Events.OfType<SessionEnded>());
        Assert.DoesNotContain(m.Events, e => e is LowTime or CreditsOut or IouPrinted);
        Assert.Equal(left, m.State.TimeLeftMs(m.Rules));
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
