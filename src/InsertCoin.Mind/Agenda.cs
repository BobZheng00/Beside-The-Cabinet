using System.Globalization;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Something on the player's agenda: a plan, deadline or appointment at <see cref="At"/> (a whole day when
/// <see cref="AllDay"/>), or, when <see cref="Remind"/>, a reminder that goes off at that time.
/// </summary>
/// <param name="Reminded">When the reminder went off (or was missed and mentioned later); null until then.</param>
/// <param name="By">The character who wrote it down, or null when the player did.</param>
public sealed record Plan(long Id, string Text, DateTimeOffset At, bool AllDay, bool Remind, DateTimeOffset? Reminded, DateTimeOffset Added, string? By)
{
    /// <summary>When it is over: the end of its day for a whole-day plan, else its time.</summary>
    public DateTimeOffset End => AllDay ? At.AddDays(1) : At;

    /// <summary>"Fri 9 Oct" or "Fri 9 Oct 15:00".</summary>
    public string Label => At.ToString(AllDay ? "ddd d MMM" : "ddd d MMM HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// The player's agenda as she sees it: what is coming up in the next week and what has just passed (so she can ask
/// how it went) go into every turn; reminders go off at their time; two days after, an item leaves the agenda (the
/// conversation about it is what her moments keep). Items come from her replies (<c>[plan: …]</c>, <c>[remind: …]</c>),
/// from upkeep, and from the memory book.
/// </summary>
public static class Agenda
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    public static readonly TimeSpan Ahead = TimeSpan.FromDays(7), Behind = TimeSpan.FromDays(2);

    /// <summary>A whole-day reminder goes off at this time of its day.</summary>
    public static readonly TimeSpan MorningOf = TimeSpan.FromHours(9);

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
    /// A direction's payload, "2026-10-09 15:00 | the dentist": its time and what it is. A reminder for a whole day
    /// goes off that morning. Null when it cannot be read.
    /// </summary>
    public static (DateTimeOffset At, bool AllDay, string Text)? ParseDirection(string payload, bool remind, DateTimeOffset now)
    {
        var bar = payload.IndexOf('|');
        if (bar < 0 || ParseWhen(payload[..bar], now) is not { } when)
            return null;
        var text = payload[(bar + 1)..].Trim();
        if (text.Length < 2)
            return null;
        return remind && when.AllDay ? (when.At + MorningOf, false, text) : (when.At, when.AllDay, text);
    }

    /// <summary>The agenda for this turn: what is coming up in the next week and what has just passed; empty when there is none.</summary>
    public static string Block(IReadOnlyList<Plan> plans, DateTimeOffset now)
    {
        var ahead = plans.Where(p => p.End > now && p.At <= now + Ahead).OrderBy(p => p.At).ToList();
        var past = plans.Where(p => p.End <= now && p.End > now - Behind).OrderBy(p => p.At).ToList();
        if (ahead.Count == 0 && past.Count == 0)
            return "";
        var block = new StringBuilder("<agenda>\n");
        if (ahead.Count > 0)
        {
            block.Append("Coming up:\n");
            foreach (var plan in ahead)
                block.Append($"- {plan.Label} ({Until(plan, now)}): {plan.Text}{(plan.Remind ? " (a reminder goes off then)" : "")}\n");
        }
        if (past.Count > 0)
        {
            block.Append("Just past:\n");
            foreach (var plan in past)
                block.Append($"- {plan.Label} ({(plan.AllDay ? When.Ago(plan.At.AddHours(12), now) : When.Ago(plan.At, now))}): {plan.Text}\n");
        }
        return block.Append("</agenda>\n").ToString();
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

    /// <summary>Reminders whose time has come and that have not gone off yet, oldest first.</summary>
    public static IReadOnlyList<Plan> Due(IReadOnlyList<Plan> plans, DateTimeOffset now) =>
        plans.Where(p => p.Remind && p.Reminded is null && p.At <= now).OrderBy(p => p.At).ToList();

    /// <summary>A reminder that came due more than this long ago was missed (the cabinet was closed): it is mentioned as such.</summary>
    public static readonly TimeSpan Missed = TimeSpan.FromMinutes(5);
}
