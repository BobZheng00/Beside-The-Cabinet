using System.Globalization;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>One occurrence of an event in the player's calendar (a repeating event gives one per time).</summary>
/// <param name="Uid">Stable across reads: the event's id, and for a repeat, the time this occurrence was planned for.</param>
public sealed record CalendarEvent(string Uid, string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay);

/// <summary>
/// Somewhere the player's calendar is read from. Today a private link (<see cref="IcsCalendar"/>: Google's "secret
/// address in iCal format", Outlook's published ICS link); signing in with Google or Microsoft would be another
/// source, read the same way and put on the agenda by the same <see cref="CalendarSync"/>.
/// </summary>
public interface ICalendarSource
{
    /// <summary>What the player calls it ("Google", "Work"); the agenda's items from it carry this name.</summary>
    string Name { get; }

    /// <summary>The events between <paramref name="from"/> and <paramref name="to"/>, repeats expanded.</summary>
    Task<IReadOnlyList<CalendarEvent>> Read(DateTimeOffset from, DateTimeOffset to, CancellationToken cancel = default);
}

/// <summary>A calendar read from a private iCalendar (.ics) link; read-only, refreshed now and then.</summary>
public sealed class IcsCalendar(HttpClient http, string name, string url) : ICalendarSource
{
    public string Name => name;

    public async Task<IReadOnlyList<CalendarEvent>> Read(DateTimeOffset from, DateTimeOffset to, CancellationToken cancel = default)
    {
        var address = url.Trim();
        if (address.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            address = "https://" + address["webcal://".Length..];
        var text = await http.GetStringAsync(address, cancel);
        if (!text.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal))
            throw new FormatException("that link does not give a calendar (no BEGIN:VCALENDAR)");
        return Ics.Events(text, from, to, TimeZoneInfo.Local);
    }
}

/// <summary>
/// Puts a calendar's events on the agenda and keeps them in step: new ones added, moved or renamed ones changed,
/// ones no longer there (cancelled, moved out) taken off; each read-only, marked with the calendar's name. What the
/// player changed on an item here (its reminder) is kept.
/// </summary>
public static class CalendarSync
{
    /// <summary>How far ahead events are put on the agenda.</summary>
    public static readonly TimeSpan Ahead = TimeSpan.FromDays(14);

    /// <returns>How many items were added, changed and taken off.</returns>
    public static (int Added, int Changed, int Removed) Apply(MindStore store, string source, IReadOnlyList<CalendarEvent> events, DateTimeOffset from,
                                                             DateTimeOffset now)
    {
        var mine = store.Agenda().Where(p => p.Source == source).ToList();
        var byUid = store.CalendarUids(source);
        var seen = new HashSet<long>();
        int added = 0, changed = 0, removed = 0;
        foreach (var e in events)
        {
            if (byUid.TryGetValue(e.Uid, out var id) && mine.FirstOrDefault(p => p.Id == id) is { } plan)
            {
                seen.Add(id);
                if (plan.Text != e.Title || plan.At != e.Start || plan.AllDay != e.AllDay)
                {
                    store.UpdatePlan(id, e.Title, e.Start, e.AllDay, plan.Before, Repeat.Once);
                    changed++;
                }
            }
            else
            {
                seen.Add(store.AddCalendarPlan(source, e.Uid, e.Title, e.Start, e.AllDay, now));
                added++;
            }
        }
        // gone from the calendar, though still within what was read: cancelled or moved away
        foreach (var plan in mine.Where(p => !seen.Contains(p.Id) && p.End > from))
        {
            store.DeletePlan(plan.Id);
            removed++;
        }
        return (added, changed, removed);
    }
}

/// <summary>
/// Reads iCalendar (RFC 5545) as Google and Outlook publish it: events with their time zones (IANA or Windows
/// names), whole days, durations, repeats (daily, weekly on given days, monthly on a day or a "second Tuesday",
/// yearly; with an interval, a count or an end), dates left out of a repeat, single occurrences moved or cancelled.
/// Rarer repeat rules are read as their first occurrence only.
/// </summary>
public static class Ics
{
    private const int MostOccurrences = 1000;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] Days = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    private sealed record Property(string Name, Dictionary<string, string> Parameters, string Value);

    private sealed class Event
    {
        public List<Property> Properties { get; } = [];
        public Property? Get(string name) => Properties.FirstOrDefault(p => p.Name == name);
        public IEnumerable<Property> All(string name) => Properties.Where(p => p.Name == name);
    }

    /// <summary>The events (occurrences) between <paramref name="from"/> and <paramref name="to"/>, by start.</summary>
    public static IReadOnlyList<CalendarEvent> Events(string text, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo local)
    {
        var events = Parse(text);
        // single occurrences moved or cancelled: by the event's id and the time they were planned for
        var overrides = events.Where(e => e.Get("RECURRENCE-ID") is not null && e.Get("UID") is not null)
            .ToDictionary(e => (e.Get("UID")!.Value, When(e.Get("RECURRENCE-ID")!, local).At.ToUniversalTime()), e => e);
        var found = new List<CalendarEvent>();
        foreach (var e in events.Where(e => e.Get("RECURRENCE-ID") is null))
        {
            if (e.Get("UID")?.Value is not { } uid || e.Get("DTSTART") is not { } dtstart || Cancelled(e))
                continue;
            var (start, allDay, zone) = When(dtstart, local);
            var length = Length(e, start, allDay, local);
            var title = Unescape(e.Get("SUMMARY")?.Value ?? "(no title)");
            var excluded = e.All("EXDATE").SelectMany(x => x.Value.Split(',').Select(v => When(x with { Value = v }, local).At.ToUniversalTime())).ToHashSet();
            var occurrences = e.Get("RRULE") is { } rule ? Expand(rule.Value, start, zone, to) : [start];
            var repeats = e.Get("RRULE") is not null;
            foreach (var at in occurrences)
            {
                if (excluded.Contains(at.ToUniversalTime()))
                    continue;
                var occurrenceUid = repeats ? $"{uid}@{at.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}" : uid;
                if (overrides.TryGetValue((uid, at.ToUniversalTime()), out var moved))
                {
                    if (Cancelled(moved) || moved.Get("DTSTART") is not { } movedStart)
                        continue;
                    var (newStart, newAllDay, _) = When(movedStart, local);
                    Add(occurrenceUid, Unescape(moved.Get("SUMMARY")?.Value ?? title), newStart, newStart + Length(moved, newStart, newAllDay, local), newAllDay);
                    continue;
                }
                Add(occurrenceUid, title, at, at + length, allDay);
            }
        }
        return [.. found.OrderBy(e => e.Start)];

        void Add(string uid, string title, DateTimeOffset start, DateTimeOffset end, bool allDay)
        {
            // shown in the player's own time: an occurrence counted in New York's time is still the player's evening
            if (end > from && start < to)
                found.Add(new CalendarEvent(uid, title, start.ToOffset(local.GetUtcOffset(start.UtcDateTime)), end.ToOffset(local.GetUtcOffset(end.UtcDateTime)), allDay));
        }
    }

    private static List<Event> Parse(string text)
    {
        // lines folded at 75 characters continue with a space or a tab
        var unfolded = text.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "").Split('\n');
        var events = new List<Event>();
        Event? current = null;
        var depth = 0;                                      // inside the event, nested parts (alarms) are skipped
        foreach (var line in unfolded)
        {
            if (line.Length == 0)
                continue;
            if (line == "BEGIN:VEVENT")
            {
                current = new Event();
                depth = 0;
                continue;
            }
            if (current is null)
                continue;
            if (line == "END:VEVENT")
            {
                events.Add(current);
                current = null;
                continue;
            }
            if (line.StartsWith("BEGIN:", StringComparison.Ordinal))
                depth++;
            else if (line.StartsWith("END:", StringComparison.Ordinal))
                depth--;
            else if (depth == 0 && Read(line) is { } property)
                current.Properties.Add(property);
        }
        return events;
    }

    private static Property? Read(string line)
    {
        // NAME;PARAM=value;PARAM="quoted:value":VALUE — the value starts at the first colon outside quotes
        var quoted = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }
        if (colon < 0)
            return null;
        var head = line[..colon].Split(';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in head.Skip(1))
            if (part.IndexOf('=') is var eq and > 0)
                parameters[part[..eq]] = part[(eq + 1)..].Trim('"');
        return new Property(head[0].ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    private static bool Cancelled(Event e) => e.Get("STATUS")?.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A DTSTART-like value: its moment, whether it is a whole day, and the zone its wall-clock time is in.</summary>
    private static (DateTimeOffset At, bool AllDay, TimeZoneInfo Zone) When(Property property, TimeZoneInfo local)
    {
        var value = property.Value.Trim();
        if (value.Length == 8 || property.Parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase))
        {
            var day = DateTime.ParseExact(value[..8], "yyyyMMdd", Invariant);
            return (new DateTimeOffset(day, local.GetUtcOffset(day)), true, local);
        }
        if (value.EndsWith('Z'))
        {
            var utc = DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss'Z'", Invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return (new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(local.GetUtcOffset(utc)), false, local);
        }
        var wall = DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss", Invariant);
        var zone = property.Parameters.TryGetValue("TZID", out var id) ? Zone(id) ?? local : local;
        var at = new DateTimeOffset(wall, zone.GetUtcOffset(wall));
        return (at.ToOffset(local.GetUtcOffset(at.UtcDateTime)), false, zone);
    }

    private static TimeZoneInfo? Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);        // IANA ("Asia/Shanghai") or Windows ("China Standard Time")
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static TimeSpan Length(Event e, DateTimeOffset start, bool allDay, TimeZoneInfo local)
    {
        if (e.Get("DTEND") is { } end)
            return When(end, local).At - start;
        if (e.Get("DURATION") is { } duration && Duration(duration.Value) is { } length)
            return length;
        return allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
    }

    /// <summary>"PT1H30M", "P1D", "P1W", "-PT15M".</summary>
    private static TimeSpan? Duration(string value)
    {
        var sign = value.StartsWith('-') ? -1 : 1;
        var text = value.TrimStart('+', '-');
        if (!text.StartsWith('P'))
            return null;
        var total = TimeSpan.Zero;
        var number = new StringBuilder();
        foreach (var c in text[1..])
        {
            if (char.IsDigit(c))
            {
                number.Append(c);
                continue;
            }
            if (c == 'T')
                continue;
            var n = number.Length > 0 ? int.Parse(number.ToString(), Invariant) : 0;
            number.Clear();
            total += c switch
            {
                'W' => TimeSpan.FromDays(7 * n),
                'D' => TimeSpan.FromDays(n),
                'H' => TimeSpan.FromHours(n),
                'M' => TimeSpan.FromMinutes(n),
                'S' => TimeSpan.FromSeconds(n),
                _ => TimeSpan.Zero,
            };
        }
        return sign * total;
    }

    /// <summary>The starts of a repeating event up to <paramref name="to"/>, counted in its own zone's wall-clock time (so summer time is right).</summary>
    private static IEnumerable<DateTimeOffset> Expand(string rule, DateTimeOffset start, TimeZoneInfo zone, DateTimeOffset to)
    {
        var parts = rule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].ToUpperInvariant());
        var frequency = parts.GetValueOrDefault("FREQ");
        var interval = parts.TryGetValue("INTERVAL", out var i) && int.TryParse(i, out var n) && n > 0 ? n : 1;
        int? count = parts.TryGetValue("COUNT", out var c) && int.TryParse(c, out var limit) ? limit : null;
        DateTimeOffset? until = parts.TryGetValue("UNTIL", out var u) ? When(new Property("UNTIL", new(), u), zone).At : null;
        var unsupported = parts.Keys.Any(k => k is not ("FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYDAY" or "BYMONTHDAY" or "WKST" or "BYMONTH"));
        if (frequency is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY") || unsupported)
        {
            yield return start;                             // a rule this reader does not know: its first occurrence only
            yield break;
        }
        var wall = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var byDay = parts.TryGetValue("BYDAY", out var d) ? d.Split(',') : [];
        var byMonthDay = parts.TryGetValue("BYMONTHDAY", out var md) ? md.Split(',').Select(x => int.Parse(x, Invariant)).ToArray() : [];
        var produced = 0;
        for (var step = 0; produced < MostOccurrences; step++)
        {
            var candidates = frequency switch
            {
                "DAILY" => new[] { wall.AddDays(step * interval) }.Where(x => byDay.Length == 0 || byDay.Any(day => day.EndsWith(Days[(int)x.DayOfWeek]))),
                "WEEKLY" => Week(wall, step * interval, byDay),
                "MONTHLY" => Month(wall, step * interval, byDay, byMonthDay),
                _ => [wall.AddYears(step * interval)],
            };
            var any = false;
            foreach (var candidate in candidates.Where(x => x >= wall).OrderBy(x => x))
            {
                var at = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
                if (at > to || until is { } end && at > end || count is { } most && produced >= most)
                    yield break;
                produced++;
                any = true;
                yield return at;
            }
            if (!any && step > 0 && new DateTimeOffset(Shift(wall, frequency, step * interval), zone.GetUtcOffset(wall)) > to)
                yield break;
        }
    }

    private static DateTime Shift(DateTime wall, string frequency, int by) => frequency switch
    {
        "DAILY" => wall.AddDays(by),
        "WEEKLY" => wall.AddDays(7 * by),
        "MONTHLY" => wall.AddMonths(by),
        _ => wall.AddYears(by),
    };

    /// <summary>The week <paramref name="weeks"/> after the first: on its BYDAY days, or the start's own weekday.</summary>
    private static IEnumerable<DateTime> Week(DateTime first, int weeks, string[] byDay)
    {
        var monday = first.Date.AddDays(-(((int)first.DayOfWeek + 6) % 7)).AddDays(7 * weeks);
        if (byDay.Length == 0)
            return [first.AddDays(7 * weeks)];
        return byDay.Select(day => Array.IndexOf(Days, day[^2..]))
                    .Where(index => index >= 0)
                    .Select(index => monday.AddDays((index + 6) % 7) + first.TimeOfDay);
    }

    /// <summary>The month <paramref name="months"/> after the first: on its BYMONTHDAY days, its BYDAY days ("2TU", "-1FR"), or the start's day.</summary>
    private static IEnumerable<DateTime> Month(DateTime first, int months, string[] byDay, int[] byMonthDay)
    {
        var month = new DateTime(first.Year, first.Month, 1).AddMonths(months);
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        if (byMonthDay.Length > 0)
            return byMonthDay.Select(day => day > 0 ? day : length + day + 1).Where(day => day >= 1 && day <= length)
                             .Select(day => month.AddDays(day - 1) + first.TimeOfDay);
        if (byDay.Length > 0)
        {
            var days = new List<DateTime>();
            foreach (var spec in byDay)
            {
                var weekday = Array.IndexOf(Days, spec[^2..]);
                if (weekday < 0)
                    continue;
                var matching = Enumerable.Range(0, length).Select(offset => month.AddDays(offset)).Where(x => (int)x.DayOfWeek == weekday).ToList();
                if (spec.Length > 2 && int.TryParse(spec[..^2], out var ordinal))
                {
                    var index = ordinal > 0 ? ordinal - 1 : matching.Count + ordinal;
                    if (index >= 0 && index < matching.Count)
                        days.Add(matching[index] + first.TimeOfDay);
                }
                else
                    days.AddRange(matching.Select(x => x + first.TimeOfDay));
            }
            return days;
        }
        return first.Day <= length ? [month.AddDays(first.Day - 1) + first.TimeOfDay] : [];
    }

    private static string Unescape(string text) =>
        text.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
}
