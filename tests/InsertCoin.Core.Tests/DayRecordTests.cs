using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

public class DayRecordTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 1, 4, 0, 0, T0.Offset);

    [Fact]
    public void Today_joins_the_record_of_days_when_the_day_turns()
    {
        var m = new Machine();
        m.Solve("wednesday", Difficulty.Medium);
        m.Focus(TimeSpan.FromMinutes(50));
        m.Play(TimeSpan.FromMinutes(20));
        m.Quit();

        m.CheckAt(Morning);

        var wednesday = Assert.Single(m.State.Days);
        Assert.Equal(new DateOnly(2026, 9, 30), wednesday.Day);
        Assert.Equal(Min(90), wednesday.CapMs);
        Assert.Equal(1, wednesday.Solves);
        Assert.Equal(Min(50), wednesday.FocusedMs);
        Assert.Equal(Min(30 + 12.5), wednesday.EarnedMs);
        Assert.Equal(Min(20), wednesday.PlayedMs);
        Assert.NotNull(wednesday.LastPlayed);
        Assert.Equal(new DateOnly(2026, 10, 1), m.State.Today!.Day);
    }

    [Fact]
    public void The_record_keeps_only_so_many_days()
    {
        var m = new Machine(rules: new Rules { DaysKept = 3 });

        for (var day = 1; day <= 5; day++)
            m.CheckAt(T0.AddDays(day));

        Assert.Equal(
            [new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4)],
            m.State.Days.Select(d => d.Day));
    }

    [Fact]
    public void The_day_records_when_the_player_was_first_and_last_at_the_PC()
    {
        var m = new Machine();
        m.CheckAt(T0.AddMinutes(1), idle: TimeSpan.FromMinutes(30));     // away: not seen
        Assert.Null(m.State.Today!.FirstActive);

        m.CheckAt(T0.AddMinutes(2), idle: TimeSpan.FromSeconds(10));
        m.CheckAt(T0.AddHours(15), idle: TimeSpan.FromSeconds(2));        // Thursday 01:00, still Wednesday's game day

        Assert.Equal(T0.AddMinutes(2) - TimeSpan.FromSeconds(10), m.State.Today!.FirstActive);
        Assert.Equal(T0.AddHours(15) - TimeSpan.FromSeconds(2), m.State.Today.LastActive);
    }

    [Fact]
    public void A_new_day_takes_its_cap_and_bedtime_from_the_rules_of_the_time()
    {
        var m = new Machine();
        m.Change(m.Rules with { FridayCapMs = Min(60), Bedtime = TimeSpan.FromHours(23) });

        m.CheckAt(new DateTimeOffset(2026, 10, 2, 9, 0, 0, T0.Offset));   // Friday

        Assert.Equal(Min(60), m.State.Today!.CapMs);
        Assert.Equal(TimeSpan.FromHours(23), m.State.Today.Bedtime);
        Assert.Null(m.State.Days[0].Bedtime);
    }
}
