using System.Globalization;

namespace InsertCoin.Mind;

/// <summary>
/// Her sense of time, in words: what part of the day it is, how long ago something was, and how long since the
/// player and she last talked. Plain English, like the rest of what she is told; she answers in the player's language.
/// </summary>
public static class When
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    public static string PartOfDay(DateTimeOffset at) => at.Hour switch
    {
        < 5 => "the middle of the night",
        < 12 => "morning",
        < 17 => "afternoon",
        < 21 => "evening",
        _ => "late evening",
    };

    /// <summary>"a moment ago", "40 minutes ago", "5 hours ago", "yesterday", "3 days ago", "2 weeks ago", "4 months ago", "a year ago"…</summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var gap = now - then;
        var days = (DateOnly.FromDateTime(now.DateTime).DayNumber - DateOnly.FromDateTime(then.DateTime).DayNumber);
        if (gap < TimeSpan.FromMinutes(2))
            return "a moment ago";
        if (gap < TimeSpan.FromMinutes(60))
            return $"{(int)gap.TotalMinutes} minutes ago";
        if (days == 0)
            return gap < TimeSpan.FromHours(2) ? "an hour ago" : $"{(int)gap.TotalHours} hours ago";
        if (days == 1)
            return gap < TimeSpan.FromHours(12) ? $"{(int)gap.TotalHours} hours ago" : "yesterday";
        if (days < 14)
            return $"{days} days ago";
        if (days < 60)
            return $"{days / 7} weeks ago";
        if (days < 365)
            return $"{days / 30} months ago";
        return days < 730 ? "a year ago" : $"{days / 365} years ago";
    }

    /// <summary>The time now, for this turn: "Sunday 4 October 2026, 14:05 (afternoon)."</summary>
    public static string Now(DateTimeOffset now) => $"{now.ToString("dddd d MMMM yyyy, HH:mm", English)} ({PartOfDay(now)}).";

    /// <summary>
    /// How long since they last talked, from the last line either of them said before this one: "You were talking a
    /// moment ago." or "You last talked yesterday, Saturday 3 October at 21:07." Null if they never have.
    /// </summary>
    public static string? SinceTalked(IReadOnlyList<Line> lines, DateTimeOffset now)
    {
        var last = lines.LastOrDefault(l => l.Speaker != Speaker.Cabinet);
        if (last is null)
            return null;
        var ago = Ago(last.At, now);
        return now - last.At < TimeSpan.FromMinutes(30)
            ? $"You were talking {ago}."
            : $"You last talked {ago}, {last.At.ToString("dddd d MMMM 'at' HH:mm", English)}.";
    }
}
