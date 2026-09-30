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
}
