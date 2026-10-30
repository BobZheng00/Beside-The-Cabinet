using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

/// <summary>No play time: the cabinet keeps the player company without a balance (spec 2026-10-27-what-earns-play-time.md).</summary>
public class NoPlayTimeTests
{
    private static readonly Earning Nothing = new(false, false, false);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, T0.Offset);

    [Fact]
    public void A_game_is_still_noticed_with_its_time_kept_and_nothing_spent()
    {
        var m = new Machine(rules: new Rules { Earns = Nothing });

        m.Play(TimeSpan.FromMinutes(30));

        Assert.NotNull(m.State.Session);
        Assert.Equal(Min(30), m.State.Session!.PlayedMs);
        Assert.Equal(Min(30), m.State.Today!.PlayedMs);
        Assert.Contains("Graveyard Keeper 2", m.State.Today.Games);
        Assert.Equal(0, m.State.DebtMs);
        Assert.Equal(0, m.State.Session.DrawnMs);
        Assert.Single(m.Events.OfType<SessionStarted>());
        Assert.DoesNotContain(m.Events, e => e is TokenInserted or LowTime or CreditsOut or IouPrinted or LateNight);
    }

    [Fact]
    public void A_game_ends_as_a_session_does_so_the_break_and_her_quiet_still_have_it()
    {
        var m = new Machine(rules: new Rules { Earns = Nothing });
        m.Play(TimeSpan.FromMinutes(10));

        var ended = Assert.Single(m.Quit().OfType<SessionEnded>());

        Assert.Equal(Min(10), ended.Receipt.PlayedMs);
        Assert.Null(m.State.Session);
    }

    [Fact]
    public void Past_bedtime_a_game_costs_nothing_and_no_price_is_named()
    {
        var m = new Machine(At(30, 23, 30), new Rules { Earns = Nothing, Bedtime = TimeSpan.FromHours(23) });

        m.Play(TimeSpan.FromMinutes(10));

        Assert.Empty(m.Events.OfType<LateNight>());
        Assert.Equal(0, m.State.Today!.LateExtraMs);
        Assert.Equal(TimeSpan.FromHours(23), m.State.Today.Bedtime);     // the bedtime words still have it
    }

    [Fact]
    public void Banked_play_time_doesnt_fade_and_is_there_as_it_was_when_play_time_is_back()
    {
        var m = new Machine();
        m.GiveTokens(4);
        var banked = m.State.TimeLeftMs(m.Rules);
        var on = m.Rules;

        m.Change(on with { Earns = Nothing });
        m.CheckAt(At(30, 10).AddDays(3));

        Assert.Empty(m.Events.OfType<Faded>());
        m.Change(on);
        Assert.Equal(banked, m.State.TimeLeftMs(m.Rules));
        Assert.Equal(4, m.State.Credits);
    }

    [Fact]
    public void A_debt_is_frozen_while_a_game_goes_on_and_is_there_as_it_was_when_play_time_is_back()
    {
        var m = new Machine();
        m.Play(TimeSpan.FromMinutes(20));
        var owed = m.State.DebtMs;
        var tickets = m.Events.OfType<IouPrinted>().Count();
        var on = m.Rules;

        m.Change(on with { Earns = Nothing });
        m.Play(TimeSpan.FromMinutes(40));
        m.Quit();

        Assert.Equal(owed, m.State.DebtMs);
        Assert.Equal(tickets, m.Events.OfType<IouPrinted>().Count());
        m.Change(on);
        Assert.Equal(owed, m.State.DebtMs);
    }

    [Fact]
    public void Focus_pays_nothing_and_the_streak_doesnt_grow()
    {
        var m = new Machine(rules: new Rules { Earns = Nothing });

        m.Focus(TimeSpan.FromMinutes(50));

        Assert.Equal(0, m.State.TimeLeftMs(m.Rules));
        Assert.Equal(Min(50), m.State.Today!.FocusedMs);
        Assert.Empty(m.State.GoodDays);
        Assert.Empty(m.Events.OfType<StreakStar>());
    }
}
