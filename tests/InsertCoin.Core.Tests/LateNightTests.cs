using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class LateNightTests
{
    private static readonly Rules ElevenPm = new() { Bedtime = TimeSpan.FromHours(23) };

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, T0.Offset);

    [Fact]
    public void Without_a_bedtime_play_costs_the_same_all_night()
    {
        var m = new Machine(At(9, 30, 23, 30));
        m.GiveTokens(2);

        m.Play(TimeSpan.FromMinutes(10));

        Assert.Equal(Min(20), m.State.TimeLeftMs(m.Rules));
        Assert.Empty(m.Events.OfType<LateNight>());
    }

    [Fact]
    public void Before_bedtime_play_costs_the_usual()
    {
        var m = new Machine(At(9, 30, 22), ElevenPm);
        m.GiveTokens(2);

        m.Play(TimeSpan.FromMinutes(10));

        Assert.Equal(Min(20), m.State.TimeLeftMs(m.Rules));
        Assert.Empty(m.Events.OfType<LateNight>());
    }

    [Fact]
    public void After_bedtime_play_costs_one_and_a_half()
    {
        var m = new Machine(At(9, 30, 23, 30), ElevenPm);
        m.GiveTokens(2);

        m.Play(TimeSpan.FromMinutes(10));

        var late = Assert.Single(m.Events.OfType<LateNight>());
        Assert.Equal(TimeSpan.FromHours(23), late.Bedtime);
        Assert.Equal(1.5, late.Multiplier);
        Assert.Equal(Min(15), m.State.TimeLeftMs(m.Rules));
        Assert.Equal(Min(10), m.State.Session!.PlayedMs);
        Assert.Equal(Min(5), m.State.Session.LateExtraMs);
        Assert.Equal(Min(5), m.State.Today!.LateExtraMs);
    }

    [Fact]
    public void Overtime_after_bedtime_owes_the_late_price_on_top_of_debts()
    {
        var m = new Machine(At(9, 30, 23, 30), ElevenPm);

        m.Play(TimeSpan.FromMinutes(10));

        Assert.Equal(Min(22.5), m.State.DebtMs);
    }

    [Fact]
    public void A_bedtime_after_midnight_belongs_to_the_same_night()
    {
        var m = new Machine(At(9, 30, 23, 50), new Rules { Bedtime = new TimeSpan(0, 30, 0) });
        m.GiveTokens(4);

        m.Play(TimeSpan.FromMinutes(10));                // 23:50 to midnight: the usual price
        Assert.Empty(m.Events.OfType<LateNight>());

        m.Now = At(10, 1, 0, 40);
        m.Play(TimeSpan.FromMinutes(10));                // 00:40: past it

        Assert.Single(m.Events.OfType<LateNight>());
        Assert.Equal(Min(60 - 10 - 15), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void The_late_price_ends_with_the_game_day()
    {
        var m = new Machine(At(9, 30, 23, 30), ElevenPm);
        m.GiveTokens(4);
        m.Play(TimeSpan.FromMinutes(5));                 // 7.5 min at the late price: 52.5 left
        m.Quit();
        m.Events.Clear();

        m.Now = At(10, 1, 9);                            // Thursday morning: the fade leaves 26.25
        m.Play(TimeSpan.FromMinutes(10));

        Assert.Empty(m.Events.OfType<LateNight>());
        Assert.Equal(Min(26.25 - 10), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_game_that_runs_past_4am_keeps_the_late_price_until_it_ends()
    {
        var m = new Machine(At(10, 1, 3, 55), ElevenPm);
        m.GiveTokens(4);

        m.Play(TimeSpan.FromMinutes(10));                // 03:55 to 04:05

        Assert.Equal(Min(60 - 15), m.State.TimeLeftMs(m.Rules));
    }

    [Fact]
    public void A_new_bedtime_counts_from_the_next_day()
    {
        var m = new Machine(At(9, 30, 22));
        m.Change(m.Rules with { Bedtime = TimeSpan.FromHours(21) });
        m.GiveTokens(2);

        m.Play(TimeSpan.FromMinutes(10));
        Assert.Empty(m.Events.OfType<LateNight>());      // tonight began without one
        m.Quit();

        m.Now = At(10, 1, 21, 30);
        m.Play(TimeSpan.FromMinutes(1));
        Assert.Single(m.Events.OfType<LateNight>());
    }

    [Fact]
    public void The_late_price_is_announced_once_a_night()
    {
        var m = new Machine(At(9, 30, 23, 30), ElevenPm);
        m.GiveTokens(4);
        m.Play(TimeSpan.FromMinutes(2));
        m.Quit();

        m.Play(TimeSpan.FromMinutes(2));

        Assert.Single(m.Events.OfType<LateNight>());
    }
}
