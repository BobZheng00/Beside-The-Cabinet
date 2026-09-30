using System.Collections.Immutable;

namespace InsertCoin.Core;

public enum Difficulty { Easy, Medium, Hard }

/// <summary>An accepted problem reported by a task source. Difficulty is null until it has been looked up.</summary>
public sealed record SolvedTask(string Id, string Title, Difficulty? Difficulty, DateTimeOffset AcceptedAt);

public sealed record RunningGame(string Id, string Name);

/// <summary>Tunable numbers. Defaults are the approved design (spec §3).</summary>
public sealed record Rules
{
    public long TokenMs { get; init; } = 15 * 60_000;
    public int EasyTokens { get; init; } = 1;
    public int MediumTokens { get; init; } = 2;
    public int HardTokens { get; init; } = 4;
    public int StreakBonusFromDay { get; init; } = 3;
    public double DebtMultiplier { get; init; } = 1.5;
    public long FirstWarningMs { get; init; } = 5 * 60_000;
    public long FinalWarningMs { get; init; } = 60_000;
    public TimeSpan DayStartsAt { get; init; } = TimeSpan.FromHours(4);
    public TimeSpan AfkAfter { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan MaxTick { get; init; } = TimeSpan.FromSeconds(30);
    public int SessionEndMisses { get; init; } = 2;

    public int TokensFor(Difficulty d) => d switch
    {
        Difficulty.Easy => EasyTokens,
        Difficulty.Medium => MediumTokens,
        _ => HardTokens,
    };
}

public sealed record Session
{
    public ImmutableList<string> GameNames { get; init; } = ImmutableList<string>.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public long PlayedMs { get; init; }
    public long DrawnMs { get; init; }
    public long DebtAddedMs { get; init; }
    public int IousPrinted { get; init; }
    public int Misses { get; init; }
}

public sealed record WeekStats
{
    public int Solves { get; init; }
    public int TokensEarned { get; init; }
    public long PlayedMs { get; init; }
    public int IousPrinted { get; init; }
}

/// <summary>Everything the machine remembers. All durations are whole milliseconds.</summary>
public sealed record LedgerState
{
    public int Credits { get; init; }
    public long FuelMs { get; init; }
    public long DebtMs { get; init; }

    public DateTimeOffset InstalledAt { get; init; }
    public string? Username { get; init; }
    public ImmutableHashSet<string> SeenTaskIds { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableSortedSet<DateOnly> SolveDays { get; init; } = ImmutableSortedSet<DateOnly>.Empty;
    public int BestStreak { get; init; }

    /// <summary>Game day of the Monday that starts the current week. Behind the calendar while a reset is pending.</summary>
    public DateOnly WeekStart { get; init; }
    public WeekStats Week { get; init; } = new();

    public Session? Session { get; init; }
    public bool Paused { get; init; }
    public bool FirstWarningGiven { get; init; }
    public bool FinalWarningGiven { get; init; }
    public bool CreditsOutGiven { get; init; }

    public long TimeLeftMs(Rules rules) => Credits * rules.TokenMs + FuelMs;
    public int IouTickets(Rules rules) => (int)(DebtMs / rules.TokenMs);

    public static LedgerState New(DateTimeOffset installedAt, Rules rules) => new()
    {
        InstalledAt = installedAt,
        WeekStart = GameCalendar.WeekStartOf(GameCalendar.DayOf(installedAt, rules)),
    };
}
