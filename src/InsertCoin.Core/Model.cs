using System.Collections.Immutable;

namespace InsertCoin.Core;

public enum Difficulty { Easy, Medium, Hard }

/// <summary>How big an activity the player does is (sized by her the first time): it pays like a problem, small as Easy, medium as Medium, big as Hard.</summary>
public enum Size { Small, Medium, Big }

/// <summary>An accepted problem reported by a task source. Difficulty is null until it has been looked up.</summary>
public sealed record SolvedTask(string Id, string Title, Difficulty? Difficulty, DateTimeOffset AcceptedAt);

/// <summary>
/// A game running now. <paramref name="Picked"/>: a program the player picked to count as play (see <see cref="PlayProgram"/>),
/// counted while it is in front however still they sit: it doesn't pause when they're idle, as a game does.
/// </summary>
public sealed record RunningGame(string Id, string Name, bool Picked = false);

/// <summary>A program the player picked to count as play (Settings → Balance): its file, and its name from the file's description.</summary>
public sealed record PlayProgram(string Path, string Name)
{
    /// <summary>
    /// The picked program that counts as a game now: the one whose window is in front (<paramref name="front"/>, its
    /// file), unless the player has left the PC. Null when none does.
    /// </summary>
    public static RunningGame? InFront(IEnumerable<PlayProgram> picked, string? front, bool away) =>
        away || front is null ? null
        : picked.FirstOrDefault(p => string.Equals(p.Path, front, StringComparison.OrdinalIgnoreCase)) is { } program ? new RunningGame(program.Path, program.Name, Picked: true)
        : null;
}

/// <summary>
/// What earns play time, as the player chose it on the first run or on Balance (spec 2026-10-27-what-earns-play-time.md):
/// a source that's off pays nothing and isn't brought up, and with none on there is no play time.
/// </summary>
public sealed record Earning(bool Focus, bool Activities, bool LeetCode)
{
    public static readonly Earning All = new(true, true, true);

    /// <summary>Anything earns play time: with nothing, there is no play time.</summary>
    public bool Any => Focus || Activities || LeetCode;

    /// <summary>
    /// What earns for a profile: as chosen, and LeetCode only with a name to read. A profile from before the choice
    /// (<paramref name="leetCode"/> unset) earns as it did: by focus and activities, and by LeetCode once a name is set.
    /// </summary>
    public static Earning Of(bool focus, bool activities, bool? leetCode, string? username) =>
        new(focus, activities, (leetCode ?? true) && !string.IsNullOrWhiteSpace(username));
}

/// <summary>Tunable numbers. Defaults are the approved design (spec §3, and the balance spec of 2026-10-07).</summary>
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

    /// <summary>Minutes of focus for each minute of play: 4 is a coin (15 minutes) for every hour of focus.</summary>
    public int FocusPerPlay { get; init; } = 4;

    /// <summary>Focus in a day that makes it a good day for the streak, like a solve or an activity: one focus part's worth.</summary>
    public long GoodDayFocusMs { get; init; } = 25 * 60_000;

    /// <summary>The most play that can be earned in a game day from Monday to Thursday.</summary>
    public long WeekdayCapMs { get; init; } = 90 * 60_000;

    /// <summary>The most play that can be earned on a Friday.</summary>
    public long FridayCapMs { get; init; } = 120 * 60_000;

    /// <summary>The most play that can be earned on a Saturday or a Sunday.</summary>
    public long WeekendCapMs { get; init; } = 150 * 60_000;

    /// <summary>The share of unplayed time that fades each night, as the game day ends.</summary>
    public double NightlyFade { get; init; } = 0.5;

    /// <summary>
    /// The player's bedtime, once they have confirmed one (null until then). Play after it, until the game day ends,
    /// costs <see cref="LateNightMultiplier"/> times as much.
    /// </summary>
    public TimeSpan? Bedtime { get; init; }

    public double LateNightMultiplier { get; init; } = 1.5;

    /// <summary>What earns play time: a source that's off pays nothing, and a good day counts only what's on.</summary>
    public Earning Earns { get; init; } = Earning.All;

    /// <summary>How many past days the record keeps.</summary>
    public int DaysKept { get; init; } = 120;

    public int TokensFor(Difficulty d) => d switch
    {
        Difficulty.Easy => EasyTokens,
        Difficulty.Medium => MediumTokens,
        _ => HardTokens,
    };

    public int TokensFor(Size s) => TokensFor(s switch { Size.Small => Difficulty.Easy, Size.Medium => Difficulty.Medium, _ => Difficulty.Hard });

    /// <summary>The cap for a game day, by its day of the week.</summary>
    public long CapOn(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday or DayOfWeek.Sunday => WeekendCapMs,
        DayOfWeek.Friday => FridayCapMs,
        _ => WeekdayCapMs,
    };
}

public sealed record Session
{
    public ImmutableList<string> GameNames { get; init; } = ImmutableList<string>.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public long PlayedMs { get; init; }
    public long DrawnMs { get; init; }
    public long DebtAddedMs { get; init; }

    /// <summary>What playing after bedtime cost on top of the time played.</summary>
    public long LateExtraMs { get; init; }

    public int IousPrinted { get; init; }
    public int Misses { get; init; }
}

public sealed record WeekStats
{
    public int Solves { get; init; }

    /// <summary>Activities that counted (each once a day).</summary>
    public int Activities { get; init; }

    /// <summary>New play earned (towards the days' caps), from every source.</summary>
    public long EarnedMs { get; init; }

    public long FocusedMs { get; init; }
    public long PlayedMs { get; init; }
    public int IousPrinted { get; init; }
}

/// <summary>
/// One game day (04:00 to 04:00): what it allowed, what was earned and played, and when the player was about. Today's
/// is kept as it goes; the past ones are the record of days that her sense of the player's patterns comes from.
/// </summary>
public sealed record DayRecord
{
    public DateOnly Day { get; init; }

    /// <summary>The most play that could be earned this day, fixed as it began: a change to the setting counts from the next day.</summary>
    public long CapMs { get; init; }

    /// <summary>The bedtime that held this day, fixed as it began; null when none was confirmed.</summary>
    public TimeSpan? Bedtime { get; init; }

    /// <summary>What faded overnight as this day began.</summary>
    public long FadedMs { get; init; }

    /// <summary>New play earned, towards the cap. What paid back debt doesn't count.</summary>
    public long EarnedMs { get; init; }

    /// <summary>Earned past the cap: done, but not paid.</summary>
    public long OverCapMs { get; init; }

    public long DebtPaidMs { get; init; }
    public int Solves { get; init; }

    /// <summary>The problems solved, as "Two Sum (Easy)", in the order they were (her diary's record of the day).</summary>
    public ImmutableList<string> Solved { get; init; } = ImmutableList<string>.Empty;

    /// <summary>The activities that counted this day, each with the share of its worth paid so far (1 in full, 0.5 half).</summary>
    public ImmutableDictionary<string, double> Activities { get; init; } = ImmutableDictionary<string, double>.Empty;

    public long FocusedMs { get; init; }
    public long PlayedMs { get; init; }

    /// <summary>The games played, by name.</summary>
    public ImmutableSortedSet<string> Games { get; init; } = ImmutableSortedSet<string>.Empty;

    /// <summary>What play after bedtime cost on top of the time played.</summary>
    public long LateExtraMs { get; init; }

    /// <summary>When the player was first and last seen using the PC this day.</summary>
    public DateTimeOffset? FirstActive { get; init; }

    public DateTimeOffset? LastActive { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }

    /// <summary>What can still be earned this day.</summary>
    public long RoomMs() => Math.Max(0, CapMs - EarnedMs);

    /// <summary>A day beginning, with the cap and bedtime the rules hold now.</summary>
    public static DayRecord Begin(DateOnly day, Rules rules, long fadedMs = 0) => new()
    {
        Day = day,
        CapMs = rules.CapOn(day),
        Bedtime = rules.Bedtime,
        FadedMs = fadedMs,
    };
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

    /// <summary>
    /// Days that count towards the streak: a solve, an activity, or <see cref="Rules.GoodDayFocusMs"/> of focus. Days
    /// with a solve from before good days were counted (2026-10-09) are only in <see cref="SolveDays"/>: see <see cref="StreakDays"/>.
    /// </summary>
    public ImmutableSortedSet<DateOnly> GoodDays { get; init; } = ImmutableSortedSet<DateOnly>.Empty;
    public int BestStreak { get; init; }

    /// <summary>Every day that counts towards the streak.</summary>
    public ImmutableSortedSet<DateOnly> StreakDays() => GoodDays.Union(SolveDays);

    /// <summary>Game day of the Monday that starts the current week, for the week's numbers.</summary>
    public DateOnly WeekStart { get; init; }
    public WeekStats Week { get; init; } = new();

    /// <summary>Today so far; null only in a save from before days were kept, until its first step.</summary>
    public DayRecord? Today { get; init; }

    /// <summary>The days before today, oldest first, up to <see cref="Rules.DaysKept"/> of them.</summary>
    public ImmutableList<DayRecord> Days { get; init; } = ImmutableList<DayRecord>.Empty;

    public Session? Session { get; init; }
    public bool Paused { get; init; }
    public bool FirstWarningGiven { get; init; }
    public bool FinalWarningGiven { get; init; }
    public bool CreditsOutGiven { get; init; }

    /// <summary>The late-night price was announced this game day.</summary>
    public bool LateNightGiven { get; init; }

    public long TimeLeftMs(Rules rules) => Credits * rules.TokenMs + FuelMs;
    public int IouTickets(Rules rules) => (int)(DebtMs / rules.TokenMs);

    public static LedgerState New(DateTimeOffset installedAt, Rules rules)
    {
        var day = GameCalendar.DayOf(installedAt, rules);
        return new()
        {
            InstalledAt = installedAt,
            WeekStart = GameCalendar.WeekStartOf(day),
            Today = DayRecord.Begin(day, rules),
        };
    }
}
