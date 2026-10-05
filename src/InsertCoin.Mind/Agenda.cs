using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>How an agenda item comes round again.</summary>
public enum Repeat { Once, Daily, Weekdays, Weekly, Monthly }

/// <summary>
/// Something on the player's agenda: a plan, deadline, appointment or a plain reminder at <see cref="At"/> (a whole
/// day when <see cref="AllDay"/>). With <see cref="Before"/> it has a reminder that long ahead of its time (zero: at
/// the time; for a whole day, ahead of that morning); with <see cref="Repeat"/> it comes round again.
/// </summary>
/// <param name="Reminded">When the reminder went off (or was missed and mentioned later); null until then.</param>
/// <param name="By">The character who wrote it down, or null when the player did.</param>
public sealed record Plan(long Id, string Text, DateTimeOffset At, bool AllDay, TimeSpan? Before, DateTimeOffset? Reminded, DateTimeOffset Added,
                          string? By, Repeat Repeat = Repeat.Once)
{
    /// <summary>It has a reminder of its own.</summary>
    public bool Remind => Before is not null;

    /// <summary>When it is over: the end of its day for a whole-day plan, else its time.</summary>
    public DateTimeOffset End => AllDay ? At.AddDays(1) : At;

    /// <summary>"Fri 9 Oct" or "Fri 9 Oct 15:00".</summary>
    public string Label => At.ToString(AllDay ? "ddd d MMM" : "ddd d MMM HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// When its reminder goes off: <see cref="Before"/> ahead of its time (a whole day's, ahead of that morning), or,
    /// for a timed plan with none of its own, <paramref name="lead"/> ahead (the player's "remind me before things on
    /// my agenda"). Null when it has no reminder.
    /// </summary>
    public DateTimeOffset? RemindAt(TimeSpan? lead = null) =>
        (Before ?? (AllDay ? null : lead)) is { } before ? (AllDay ? At + Agenda.MorningOf : At) - before : null;
}

/// <summary>What a direction or upkeep asks to put on the agenda.</summary>
public sealed record PlanSpec(DateTimeOffset At, bool AllDay, string Text, TimeSpan? Before, Repeat Repeat);

/// <summary>
/// The player's agenda as she sees it: what is coming up in the next week and what has just passed (so she can ask
/// how it went) go into every turn; reminders go off at their time; a repeating item moves on to its next time;
/// two days after, a one-off item leaves the agenda (the conversation about it is what her moments keep). Items
/// come from her replies (<c>[plan: …]</c>, <c>[remind: …]</c>), from upkeep, and from the memory book.
/// </summary>
public static partial class Agenda
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    public static readonly TimeSpan Ahead = TimeSpan.FromDays(7), Behind = TimeSpan.FromDays(2);

    /// <summary>A whole-day item's reminder is counted from this time of its day.</summary>
    public static readonly TimeSpan MorningOf = TimeSpan.FromHours(9);

    /// <summary>A reminder that came due more than this long ago was missed (the cabinet was closed): it is mentioned as such.</summary>
    public static readonly TimeSpan Missed = TimeSpan.FromMinutes(5);

    private static readonly string[] Formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"];

    /// <summary>
    /// Reads "2026-10-09 15:00" or "2026-10-09" (a whole day) as a local time, in the offset of <paramref name="now"/>.
    /// Null when it is neither.
    /// </summary>
    public static (DateTimeOffset At, bool AllDay)? ParseWhen(string when, DateTimeOffset now)
    {
        when = when.Trim();
        if (DateTime.TryParseExact(when, Formats, English, DateTimeStyles.None, out var timed))
            return (new DateTimeOffset(timed, now.Offset), false);
        if (DateTime.TryParseExact(when, "yyyy-MM-dd", English, DateTimeStyles.None, out var day))
            return (new DateTimeOffset(day, now.Offset), true);
        return null;
    }

    /// <summary>
    /// Reads options as written after a direction's second bar or in upkeep's JSON: how long before to remind ("30
    /// min before", "an hour before", "1 day before") and whether it repeats ("daily", "weekdays", "weekly",
    /// "monthly", "every day"…). Anything else is ignored.
    /// </summary>
    public static (TimeSpan? Before, Repeat Repeat) ParseOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return (null, Repeat.Once);
        var text = options.ToLowerInvariant().Replace("the day before", "1 day before").Replace("the night before", "1 day before");
        var repeat = text switch
        {
            _ when WeekdaysWord().IsMatch(text) => Repeat.Weekdays,
            _ when DailyWord().IsMatch(text) => Repeat.Daily,
            _ when WeeklyWord().IsMatch(text) => Repeat.Weekly,
            _ when MonthlyWord().IsMatch(text) => Repeat.Monthly,
            _ => Repeat.Once,
        };
        TimeSpan? before = null;
        if (BeforeWords().Match(text) is { Success: true } m)
        {
            var count = m.Groups["n"].Value is "a" or "an" ? 1 : int.Parse(m.Groups["n"].Value, English);
            before = m.Groups["unit"].Value[0] switch
            {
                'm' => TimeSpan.FromMinutes(count),
                'h' => TimeSpan.FromHours(count),
                _ => TimeSpan.FromDays(count),
            };
        }
        else if (AtTheTime().IsMatch(text))
            before = TimeSpan.Zero;
        return (before, repeat);
    }

    /// <summary>
    /// A direction's payload, "2026-10-09 15:00 | the dentist" with optional "| 30 min before, weekly": what to put on
    /// the agenda. A [remind: …] goes off at its time (a whole day's, that morning). Null when it cannot be read.
    /// </summary>
    public static PlanSpec? ParseDirection(string payload, bool remind, DateTimeOffset now)
    {
        var parts = payload.Split('|', 3);
        if (parts.Length < 2 || ParseWhen(parts[0], now) is not { } when)
            return null;
        var text = parts[1].Trim();
        if (text.Length < 2)
            return null;
        var (before, repeat) = ParseOptions(parts.Length > 2 ? parts[2] : null);
        return new PlanSpec(when.At, when.AllDay, text, remind ? TimeSpan.Zero : before, repeat);
    }

    /// <summary>"at the time", "30 min before", "1 hour before", "1 day before"…</summary>
    public static string Describe(TimeSpan before) => before switch
    {
        { TotalMinutes: 0 } => "at the time",
        { TotalDays: >= 1 } when before.TotalDays % 1 == 0 => $"{before.TotalDays:0} day{(before.TotalDays == 1 ? "" : "s")} before",
        { TotalHours: >= 1 } when before.TotalHours % 1 == 0 => $"{before.TotalHours:0} hour{(before.TotalHours == 1 ? "" : "s")} before",
        _ => $"{before.TotalMinutes:0} min before",
    };

    /// <summary>"every day", "on weekdays", "every week", "every month", or empty.</summary>
    public static string Describe(Repeat repeat) => repeat switch
    {
        Repeat.Daily => "every day",
        Repeat.Weekdays => "on weekdays",
        Repeat.Weekly => "every week",
        Repeat.Monthly => "every month",
        _ => "",
    };

    /// <summary>The next time a repeating item comes round after <paramref name="at"/>.</summary>
    public static DateTimeOffset Next(DateTimeOffset at, Repeat repeat) => repeat switch
    {
        Repeat.Daily => at.AddDays(1),
        Repeat.Weekdays => at.AddDays(at.DayOfWeek switch { DayOfWeek.Friday => 3, DayOfWeek.Saturday => 2, _ => 1 }),
        Repeat.Weekly => at.AddDays(7),
        Repeat.Monthly => at.AddMonths(1),
        _ => at,
    };

    /// <summary>A repeating item's first time after <paramref name="now"/> whose reminder (or, without one, whose end) is still ahead.</summary>
    public static DateTimeOffset NextAfter(Plan plan, DateTimeOffset now, TimeSpan? lead = null)
    {
        if (plan.Repeat == Repeat.Once)
            return plan.At;
        var next = plan;
        do
            next = next with { At = Next(next.At, plan.Repeat) };
        while ((next.RemindAt(lead) ?? next.End) <= now);
        return next.At;
    }

    /// <summary>The agenda for this turn: what is coming up in the next week and what has just passed; empty when there is none.</summary>
    public static string Block(IReadOnlyList<Plan> plans, DateTimeOffset now, TimeSpan? lead = null)
    {
        var ahead = plans.Where(p => p.End > now && p.At <= now + Ahead).OrderBy(p => p.At).ToList();
        var past = plans.Where(p => p.Repeat == Repeat.Once && p.End <= now && p.End > now - Behind).OrderBy(p => p.At).ToList();
        if (ahead.Count == 0 && past.Count == 0)
            return "";
        var block = new StringBuilder("<agenda>\n");
        if (ahead.Count > 0)
        {
            block.Append("Coming up:\n");
            foreach (var plan in ahead)
            {
                var notes = new List<string>();
                if (plan.Repeat != Repeat.Once)
                    notes.Add(Describe(plan.Repeat));
                if (plan.AllDay && plan.RemindAt() is { } morning)
                    notes.Add($"a reminder {(morning.Date == plan.At.Date ? "at" : morning.ToString("'on' ddd d MMM 'at'", English))} {morning:HH:mm}");
                else if (plan.Before is { } before)
                    notes.Add(before == TimeSpan.Zero ? "a reminder goes off then" : $"a reminder {Describe(before)}");
                else if (plan.RemindAt(lead) is not null)
                    notes.Add($"a reminder {Describe(lead!.Value)}");
                block.Append($"- {plan.Label} ({Until(plan, now)}): {plan.Text}{(notes.Count > 0 ? $" ({string.Join("; ", notes)})" : "")}\n");
            }
        }
        if (past.Count > 0)
        {
            block.Append("Just past:\n");
            foreach (var plan in past)
                block.Append($"- {plan.Label} ({(plan.AllDay ? When.Ago(plan.At.AddHours(12), now) : When.Ago(plan.At, now))}): {plan.Text}\n");
        }
        return block.Append("</agenda>\n").ToString();
    }

    /// <summary>
    /// What the cabinet tells her when reminders go off, for her to pass on in her own words (see
    /// <see cref="Conversation.Announce"/>): each one, with how soon its item is or that it was missed.
    /// </summary>
    public static string Announcement(IReadOnlyList<Plan> due, DateTimeOffset now, TimeSpan? lead = null)
    {
        string One(Plan plan)
        {
            var at = plan.RemindAt(lead) ?? plan.At;
            var when = now - at > Missed ? $"came due {When.Ago(at, now)}, while the cabinet was closed"
                : plan.End <= now || plan.At - now < TimeSpan.FromMinutes(1) ? "now"
                : $"{Until(plan, now)}, {plan.Label}";
            return $"\"{plan.Text}\" ({when})";
        }
        return due.Count == 1
            ? $"A reminder the player set is going off: {One(due[0])}. Tell them in one or two short sentences, in your own words."
            : $"Reminders the player set are going off: {string.Join("; ", due.Select(One))}. Tell them in one to three short sentences, in your own words.";
    }

    /// <summary>"today", "tomorrow", "in 3 hours", "in 5 days"…</summary>
    public static string Until(Plan plan, DateTimeOffset now)
    {
        var days = DateOnly.FromDateTime(plan.At.DateTime).DayNumber - DateOnly.FromDateTime(now.DateTime).DayNumber;
        var gap = plan.At - now;
        if (plan.AllDay)
            return days <= 0 ? "today" : days == 1 ? "tomorrow" : $"in {days} days";
        if (gap < TimeSpan.FromMinutes(1))
            return "now";
        if (gap < TimeSpan.FromHours(1))
            return $"in {(int)Math.Ceiling(gap.TotalMinutes)} minutes";
        if (days == 0)
            return gap < TimeSpan.FromHours(2) ? "in an hour" : $"today, in {(int)gap.TotalHours} hours";
        return days == 1 ? "tomorrow" : $"in {days} days";
    }

    /// <summary>
    /// Reminders whose time has come and that have not gone off yet, oldest first. One whose time had already passed
    /// when it was added goes off only while its plan is still ahead; so does one from <paramref name="lead"/> (the
    /// player did not ask for it): a missed reminder the player asked for is still worth a word, a missed default
    /// one for something already over is not.
    /// </summary>
    public static IReadOnlyList<Plan> Due(IReadOnlyList<Plan> plans, DateTimeOffset now, TimeSpan? lead = null) =>
        plans.Where(p => p.Reminded is null && p.RemindAt(lead) is { } at && at <= now
                         && (p.End > now || p.Before is not null && p.Added <= at))
             .OrderBy(p => p.RemindAt(lead))
             .ToList();

    [GeneratedRegex(@"\bweekdays?\b|\bevery\s+weekday\b|\bmonday\s+to\s+friday\b")]
    private static partial Regex WeekdaysWord();
    [GeneratedRegex(@"\bdaily\b|\bevery\s*day\b|\beach\s+day\b")]
    private static partial Regex DailyWord();
    [GeneratedRegex(@"\bweekly\b|\bevery\s+week\b|\beach\s+week\b")]
    private static partial Regex WeeklyWord();
    [GeneratedRegex(@"\bmonthly\b|\bevery\s+month\b|\beach\s+month\b")]
    private static partial Regex MonthlyWord();
    [GeneratedRegex(@"(?<n>\d+|an?)\s*(?<unit>m|mins?|minutes?|h|hrs?|hours?|d|days?)\s*(before|early|ahead|earlier)")]
    private static partial Regex BeforeWords();
    [GeneratedRegex(@"\bat\s+the\s+time\b|\bon\s+time\b|\bremind\b")]
    private static partial Regex AtTheTime();
}
