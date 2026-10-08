namespace InsertCoin.Core;

/// <summary>Days start at <see cref="Rules.DayStartsAt"/> local time; weeks start on the Monday game day.</summary>
public static class GameCalendar
{
    public static DateOnly DayOf(DateTimeOffset moment, Rules rules) =>
        DateOnly.FromDateTime(moment.DateTime - rules.DayStartsAt);

    public static DateOnly WeekStartOf(DateOnly day) =>
        day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Consecutive solve days ending on <paramref name="day"/> (0 if that day has no solve).</summary>
    public static int StreakEndingOn(IReadOnlySet<DateOnly> solveDays, DateOnly day)
    {
        var streak = 0;
        while (solveDays.Contains(day.AddDays(-streak)))
            streak++;
        return streak;
    }

    /// <summary>The live streak: ends today, or yesterday if today has no solve yet.</summary>
    public static int CurrentStreak(LedgerState state, DateTimeOffset now, Rules rules)
    {
        var today = DayOf(now, rules);
        var streak = StreakEndingOn(state.SolveDays, today);
        return streak > 0 ? streak : StreakEndingOn(state.SolveDays, today.AddDays(-1));
    }

    /// <summary>
    /// Whether it is past the bedtime that holds today: from bedtime until the game day ends (later still while a
    /// session keeps the day from turning). Never without a bedtime the player confirmed.
    /// </summary>
    public static bool IsLateNight(LedgerState state, DateTimeOffset now, Rules rules)
    {
        if (state.Today is not { Bedtime: { } bedtime } today)
            return false;
        var into = now.DateTime - rules.DayStartsAt - today.Day.ToDateTime(TimeOnly.MinValue);
        var bedtimeInto = ((bedtime - rules.DayStartsAt).Ticks % TimeSpan.TicksPerDay + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
        return into.Ticks >= bedtimeInto;
    }
}
