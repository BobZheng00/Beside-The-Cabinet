using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Mind;

/// <summary>The cabinet in plain words, for her context: where things stand now, and what just happened.</summary>
public static class Situation
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>
    /// What of the cabinet matters right now, for every turn: play time, and only what bears on this moment (a game on,
    /// time owed, the day's cap reached, past bedtime). The rest of the numbers, what was done today, the streak and how
    /// play is earned are her cabinet tool's (see <see cref="Ledger"/>), and each solve, focus part and streak day is in
    /// the talk as it happens (see <see cref="Witness"/>): the whole ledger in every turn made her answer a "hey!" with
    /// tokens and streaks, and today's solves kept her opening with them (5 of 8 openers, then 3 of 8 with them still
    /// here; the user, 2026-10-12: the cabinet weighed too much in her mind). The time itself is in the turn's own
    /// <c>now</c> (see <see cref="When"/>). With no play time, only the game being played: there is no balance to tell.
    /// </summary>
    public static string Describe(LedgerState s, Rules rules, DateTimeOffset now)
    {
        var lines = new List<string>();
        if (Playing(s, rules) is { } playing)
            lines.Add(playing);
        if (!rules.Earns.Any)
            return lines.Count > 0 ? lines[0] : "Right now: not playing.";
        var left = s.TimeLeftMs(rules);
        lines.Add(left > 0 ? $"Play time banked: {Words.Span(left)}." : "Play time banked: none.");
        if (s.DebtMs > 0)
            lines.Add($"Owed: {Words.Span(s.DebtMs)} ({Words.Count(s.IouTickets(rules), "IOU ticket")}).");
        if (s.Today is { } today)
        {
            if (today.RoomMs() <= 0)
                lines.Add($"Today's cap of {Words.Span(today.CapMs)} is reached: nothing more is paid until {Clock(rules.DayStartsAt)}.");
            if (today.Bedtime is { } bedtime && GameCalendar.IsLateNight(s, now, rules))
                lines.Add($"It is past their bedtime ({Clock(bedtime)}): play costs x{Times(rules.LateNightMultiplier)} until {Clock(rules.DayStartsAt)}.");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Right now: the game being played, or null when none is.</summary>
    private static string? Playing(LedgerState s, Rules rules) => s.Session switch
    {
        null => null,
        _ when s.Held => $"Right now: {string.Join(" + ", s.Session.GameNames)} is open behind another window{(rules.Earns.Any ? " (clock paused)" : "")}, " +
                         $"{Words.Span(s.Session.PlayedMs)} into the session.",
        _ when s.Paused => $"Right now: in a game of {string.Join(" + ", s.Session.GameNames)}, but the player stepped away{(rules.Earns.Any ? " (clock paused)" : "")}.",
        var session => $"Right now: playing {string.Join(" + ", session.GameNames)}, {Words.Span(session.PlayedMs)} into the session.",
    };

    /// <summary>What a day has seen done ("1 problem solved, the gym, 1 h 10 min focused"), or null when nothing yet.</summary>
    private static string? Done(DayRecord day)
    {
        var done = new List<string>();
        if (day.Solves > 0)
            done.Add($"{Words.Count(day.Solves, "problem")} solved");
        done.AddRange(day.Activities.OrderBy(a => a.Key).Select(a => a.Value >= 1 ? a.Key : $"{a.Key} (half)"));
        if (day.FocusedMs >= 60_000)
            done.Add($"{Words.Span(day.FocusedMs)} focused");
        if (day.PlayedMs >= 60_000)
            done.Add($"{Words.Span(day.PlayedMs)} played");
        return done.Count > 0 ? string.Join(", ", done) : null;
    }

    /// <summary>
    /// The cabinet's numbers in full, for her cabinet tool: where things stand, how play is earned, and, when
    /// <paramref name="day"/> names one ("today", "yesterday", a date), that day's record with what it earned. With no
    /// play time, what was done and played, the bedtime and the day's record, and nothing of a balance.
    /// </summary>
    public static string Ledger(LedgerState s, Rules rules, DateTimeOffset now, string? player, string? day)
    {
        var lines = new List<string>();
        var balance = rules.Earns.Any;
        if (rules.Earns.LeetCode && !string.IsNullOrWhiteSpace(player))
            lines.Add($"The player's LeetCode account: {player}.");

        var left = s.TimeLeftMs(rules);
        if (balance)
        {
            lines.Add(left > 0
                ? $"Play time banked: {Words.Span(left)}. Each night at {Clock(rules.DayStartsAt)}, {Share(rules.NightlyFade)} of what isn't played fades."
                : "Play time banked: none.");
            lines.Add(s.DebtMs > 0 ? $"Owed: {Words.Span(s.DebtMs)} ({Words.Count(s.IouTickets(rules), "IOU ticket")})." : "Owed: nothing.");
        }
        if (s.Today is { } today)
        {
            if (balance)
                lines.Add(today.RoomMs() > 0
                    ? $"Today ({today.Day.DayOfWeek}): {Earned(today.EarnedMs)} of the day's cap of {Words.Span(today.CapMs)}; {Words.Span(today.RoomMs())} more can be earned."
                    : $"Today ({today.Day.DayOfWeek}): the day's cap of {Words.Span(today.CapMs)} is reached; nothing more is paid until {Clock(rules.DayStartsAt)}.");
            if (Done(today) is { } done)
                lines.Add($"So far today: {done}.");
            if (today.Bedtime is { } bedtime)
                lines.Add(!balance ? $"Bedtime: {Clock(bedtime)}."
                    : GameCalendar.IsLateNight(s, now, rules)
                    ? $"Bedtime: {Clock(bedtime)}, and it is past it: play costs x{Times(rules.LateNightMultiplier)} until {Clock(rules.DayStartsAt)}."
                    : $"Bedtime: {Clock(bedtime)}; play after it costs x{Times(rules.LateNightMultiplier)} until {Clock(rules.DayStartsAt)}.");
        }

        var streak = GameCalendar.CurrentStreak(s, now, rules);
        if (balance)
            lines.Add($"Streak of good days ({GoodDay(rules)}): {Words.Count(streak, "day")} (best {s.BestStreak}).");
        lines.Add("This week: " + (rules.Earns.LeetCode ? $"{Words.Count(s.Week.Solves, "problem")} solved, " : "") +
                  (s.Week.FocusedMs >= 60_000 ? $"{Words.Span(s.Week.FocusedMs)} focused, " : "no focus sessions, ") +
                  (balance ? $"{Earned(s.Week.EarnedMs)}, " : "") +
                  (s.Week.PlayedMs >= 60_000 ? $"{Words.Span(s.Week.PlayedMs)} played." : "no games played yet."));

        lines.Add(Playing(s, rules) ?? "Right now: not playing.");
        if (!balance)
        {
            lines.Add("The player chose no play time: the cabinet keeps no balance, and their games cost nothing. It never closes a game.");
            if (day is { Length: > 0 })
                lines.Add(Day(s, rules, now, day, balance));
            return string.Join("\n", lines);
        }
        lines.Add($"How play is earned: a token is {Words.Span(rules.TokenMs)} of play. {Pays(rules)} " +
                  $"A good day has {GoodDay(rules)}; from the {Ordinal(rules.StreakBonusFromDay)} good day in a row, the day's first earning brings a bonus token. " +
                  "A day can earn at most its cap " +
                  $"(Monday to Thursday {Words.Span(rules.WeekdayCapMs)}, Friday {Words.Span(rules.FridayCapMs)}, weekends {Words.Span(rules.WeekendCapMs)}). " +
                  $"Each night at {Clock(rules.DayStartsAt)}, {Share(rules.NightlyFade)} of what isn't played fades. Play after the player's bedtime costs " +
                  $"x{Times(rules.LateNightMultiplier)} until {Clock(rules.DayStartsAt)}; when time runs out the game goes on, on credit at " +
                  $"x{Times(rules.DebtMultiplier)}, and the cabinet prints IOU tickets. The cabinet never closes a game.");
        if (day is { Length: > 0 })
            lines.Add(Day(s, rules, now, day, balance));
        return string.Join("\n", lines);
    }

    /// <summary>A day's record as the cabinet keeps it (see <see cref="Diary"/>): what was done, then what it earned (with a balance).</summary>
    private static string Day(LedgerState s, Rules rules, DateTimeOffset now, string asked, bool balance)
    {
        var today = GameCalendar.DayOf(now, rules);
        DateOnly? wanted = asked.Trim().ToLowerInvariant() switch
        {
            "today" => today,
            "yesterday" => today.AddDays(-1),
            var d when DateOnly.TryParseExact(d, "yyyy-MM-dd", English, DateTimeStyles.None, out var date) => date,
            _ => null,
        };
        if (wanted is not { } named)
            return $"No day called \"{asked}\": name it as today, yesterday or YYYY-MM-DD.";
        var record = s.Today?.Day == named ? s.Today : s.Days.FirstOrDefault(d => d.Day == named);
        if (record is null)
            return $"{named.ToString("dddd d MMMM yyyy", English)}: no record (the cabinet keeps the last {rules.DaysKept} days).";
        var entry = Diary.Entry(record, s.StreakDays().Contains(named));
        return $"{named.ToString("dddd d MMMM yyyy", English)}{(named == today ? " (today, so far)" : "")}: {entry}" +
               (balance && Diary.Accounts(record) is { Length: > 0 } accounts ? $" {accounts}" : "");
    }

    /// <summary>What pays, of what earns for this player only ("LeetCode problems pay …; focus pays ….").</summary>
    private static string Pays(Rules rules)
    {
        var pays = new List<string>();
        if (rules.Earns.LeetCode)
            pays.Add($"LeetCode problems pay Easy {Words.Count(rules.EasyTokens, "token")}, Medium {rules.MediumTokens}, Hard {rules.HardTokens}");
        if (rules.Earns.Activities)
            pays.Add($"the player's own activities pay by their size (small {rules.TokensFor(Size.Small)}, medium {rules.TokensFor(Size.Medium)}, big {rules.TokensFor(Size.Big)})");
        if (rules.Earns.Focus)
            pays.Add($"focus pays {Words.Span(rules.TokenMs)} for every {Words.Span(rules.TokenMs * rules.FocusPerPlay)} focused");
        var said = string.Join("; ", pays);
        return $"{char.ToUpperInvariant(said[0])}{said[1..]}.";        // something earns: with nothing, Ledger says there's no play time instead
    }

    /// <summary>What makes a good day, of what earns ("a solve, an activity or 25 min of focus").</summary>
    private static string GoodDay(Rules rules) => Words.Either([
        .. rules.Earns.LeetCode ? ["a solve"] : Array.Empty<string>(),
        .. rules.Earns.Activities ? ["an activity"] : Array.Empty<string>(),
        .. rules.Earns.Focus ? [$"{Words.Span(rules.GoodDayFocusMs)} of focus"] : Array.Empty<string>()]);

    private static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", _ => $"{n}th" };

    /// <summary>
    /// What an event looks like in her conversation log: what happened, as a friend would notice it, not what it paid.
    /// Null for the cabinet's bookkeeping (a token going in, the clock pausing, the overnight fade, the cap, a part of a
    /// debt paid, an IOU printed): it shows on the cabinet's screen, and her cabinet tool has the numbers.
    /// </summary>
    public static string? Witness(LedgerEvent e, Rules rules) => e switch
    {
        DebtPaid or StreakStar when !rules.Earns.Any => null,     // paid as play time went off (LeetCode's last answer): no balance to tell of
        CoinsEarned c => $"The player solved \"{c.Task.Title}\" ({c.Task.Difficulty?.ToString() ?? "unknown difficulty"})" +
                         (c.StarToken && rules.Earns.Any ? $": day {c.Streak} of a streak of good days." : "."),
        FocusPaid f => $"A focus part ended: {Words.Span(f.FocusedMs)} focused.",
        StreakStar star => $"Today became a good day, day {star.Streak} of a streak.",
        ActivityPaid a => $"\"{a.Name}\" counted{(a.Total < 1 ? " by half for now" : a.Share < 1 ? " in full now" : "")}.",
        DebtPaid { DebtLeftMs: 0 } => "That paid off all the debt.",
        LowTime l => $"{Words.Span(l.TimeLeftMs)} of play time left.",
        CreditsOut => $"Play time ran out; the game goes on, on credit at x{Times(rules.DebtMultiplier)}.",
        SessionStarted started => $"The player started playing {string.Join(" + ", started.GameNames)}.",
        SessionEnded ended => $"The player stopped playing after {Words.Span(ended.Receipt.PlayedMs)}.",
        LateNight l => $"Play went past the player's bedtime ({Clock(l.Bedtime)}).",
        _ => null,
    };

    private static string Earned(long ms) => ms >= 60_000 ? $"{Words.Span(ms)} earned" : "nothing earned yet";

    private static string Clock(TimeSpan time) => $"{time.Hours:00}:{time.Minutes:00}";

    private static string Times(double multiplier) => multiplier.ToString("0.##", English);

    private static string Share(double share) => share == 0.5 ? "half" : $"{(share * 100).ToString("0", English)}%";
}

public static class Words
{
    /// <summary>"45 min", "1 h 20 min", "under a minute".</summary>
    public static string Span(long ms)
    {
        var minutes = ms / 60_000;
        return minutes < 1 ? "under a minute" : minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }

    /// <summary>"a, b or c", "a or b", "a"; empty for none.</summary>
    public static string Either(IReadOnlyList<string> things) =>
        things.Count < 2 ? string.Concat(things) : $"{string.Join(", ", things.Take(things.Count - 1))} or {things[^1]}";

    /// <summary>"1 day", "3 days".</summary>
    public static string Count(int n, string thing) => n == 1 ? $"1 {thing}" : $"{n} {thing}s";

    /// <summary>Text in lines of at most <paramref name="width"/> characters, broken between words: for a hover tip, which doesn't wrap by itself.</summary>
    public static string Wrap(string text, int width = 60)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                    line = line.Length == 0 ? word : $"{line} {word}";
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }
}
