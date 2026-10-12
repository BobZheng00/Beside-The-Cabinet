using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>How much an observation matters: what matters for weeks, useful detail, or something in passing.</summary>
public enum Priority { High, Medium, Low }

/// <summary>
/// Something she observed in the conversation, from lines <see cref="First"/> to <see cref="Last"/>: on the day it was
/// said, or, condensed (see <see cref="Log.ToCondense"/>), for the week, month or year starting at <see cref="At"/>.
/// <see cref="Condensed"/>: it went into a longer one's notes, so it is out of view and only recalled.
/// </summary>
/// <param name="Source">Null for her own note; <see cref="Diary.Cabinet"/> for the cabinet's record of a day.</param>
public sealed record Observation(long Id, DateTimeOffset At, Priority Priority, string Text, long First, long Last, Span Span = Span.Day, bool Condensed = false,
                                 string? Source = null);

/// <summary>A stretch of her log to condense: the notes of a finished week, month or year, into notes for <see cref="Span"/> starting <see cref="At"/>.</summary>
public sealed record Stretch(Span Span, DateTimeOffset At, string Period, IReadOnlyList<Observation> Notes);

/// <summary>
/// Her memory of what has left the window, as a dated log of observations (docs/specs/2026-10-11-observational-memory.md):
/// the request that observes the lines leaving the window, the one that condenses a finished week, month or year, what
/// of the log she sees, and how it is laid out. The model only writes notes and says how much each matters; the
/// dates, the order, the layout, what to condense and the size are kept here, so they never drift.
/// </summary>
public static class Log
{
    /// <summary>Tokens of lines observed in one request, and of her latest observations sent with them, for continuity.</summary>
    public const int ObserveAtOnce = 6000, Continuity = 600;

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>What of the log she sees: three eighths of her window (12k tokens at 32k), her newest observations.</summary>
    public static int Budget(int window) => window * 3 / 8;

    /// <summary>
    /// Her notes on what has left the window (line <paramref name="folded"/> and before): those she sees in place of the
    /// lines. Upkeep notes lines while they are still in the window, word for word, so those notes wait; a note that
    /// straddles the edge shows as soon as its first line leaves, so nothing falls between the two.
    /// </summary>
    public static IReadOnlyList<Observation> Left(IReadOnlyList<Observation> log, long folded) => [.. log.Where(o => o.First <= folded)];

    /// <summary>The newest notes in view that fit in <paramref name="tokens"/>, oldest first (condensed ones are out of view).</summary>
    public static IReadOnlyList<Observation> Newest(IReadOnlyList<Observation> log, int tokens)
    {
        var current = log.Where(o => !o.Condensed).ToList();
        var used = 0;
        var from = current.Count;
        while (from > 0)
        {
            var o = current[from - 1];
            var cost = Prompt.Tokens(o.Text) + 3 + (from == current.Count || Group(current[from]) != Group(o) ? 10 : 0);     // a group starts a heading
            if (used + cost > tokens)
                break;
            used += cost;
            from--;
        }
        return current.Skip(from).ToList();
    }

    /// <summary>
    /// The notes as she reads them: a heading for each day, week, month or year, with how long ago when
    /// <paramref name="now"/> is known (whole days only, so it changes at midnight and the cached prompt holds), and "!"
    /// before what matters most.
    /// </summary>
    public static string Laid(IReadOnlyList<Observation> observations, DateTimeOffset? now)
    {
        var text = new StringBuilder();
        (Span, DateTime)? group = null;
        foreach (var o in observations)
        {
            if (Group(o) != group)
            {
                group = Group(o);
                text.Append(text.Length == 0 ? "" : "\n").Append(Heading(o.Span, o.At));
                if (now is { } today)
                    text.Append(" (").Append(DaysAgo(o.At, today)).Append(')');
            }
            text.Append("\n- ").Append(o.Priority == Priority.High ? "! " : "").Append(o.Text);
        }
        return text.ToString();
    }

    private static (Span, DateTime) Group(Observation o) => (o.Span, o.At.Date);

    /// <summary>When a note is from, as its heading says: "Fri 2 Oct 2026", "Week of 21 Sep 2026", "September 2026", "2026".</summary>
    public static string Heading(Span span, DateTimeOffset at) => span switch
    {
        Span.Week => "Week of " + at.ToString("d MMM yyyy", English),
        Span.Month => at.ToString("MMMM yyyy", English),
        Span.Year => at.ToString("yyyy", English),
        _ => at.ToString("ddd d MMM yyyy", English),
    };

    private static string DaysAgo(DateTimeOffset then, DateTimeOffset now)
    {
        var days = DateOnly.FromDateTime(now.DateTime).DayNumber - DateOnly.FromDateTime(then.DateTime).DayNumber;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            _ => When.Ago(new DateTimeOffset(then.Date, then.Offset), new DateTimeOffset(now.Date, now.Offset)),
        };
    }

    /// <summary>
    /// The request that observes <paramref name="lines"/> as they leave the window: one observation per thing worth
    /// keeping, with the day it was said and how much it matters. It sees this batch and her latest observations, never
    /// the whole history, so it is the same small task however long the conversation has been. Its example shows the form
    /// only: one with a story in it came back as the player's past ("his previously noted job interview at Acme", the
    /// smoke test, 2026-10-11).
    /// </summary>
    public static List<ChatMessage> Observe(Persona persona, IReadOnlyList<Observation> latest, IReadOnlyList<Line> lines) =>
    [
        new(ChatRole.System, $$"""
            You keep {{persona.Name}}'s notes on her conversation with the player; she sits beside the player's arcade cabinet and chats with them. The lines below are leaving the part of the conversation she reads word for word, and your notes are what she will have of them. Answer with JSON only:

            {"observations": [{"date": "YYYY-MM-DD", "priority": "high", "text": "The player …"}]}

            One observation per thing worth keeping: what the player did, said, decided or plans; what they like, dislike or worry about; people, places, names, numbers and dates exactly as said; what {{persona.Name}} said (about herself too), suggested or promised; how the two of them got on. "date" is the day it was said, from the timestamps. In "text", write dates out in full ("Friday 9 October", "in March 2026"), never "tomorrow" or "last week": work them out from the timestamps. "priority": "high" for what will matter for weeks (plans, goals, people, strong likes and dislikes, promises, a change to something known before), "medium" for useful detail, "low" for something in passing. Leave out greetings, small talk and the cabinet's numbers; when nothing is worth keeping, answer {"observations": []}. Write in the third person ("The player…", "they", "{{persona.Name}}…"), in the language the conversation is in.
            """),
        new(ChatRole.User, (latest.Count > 0 ? $"Her latest notes, for what came just before (don't repeat them):\n{Laid(latest, null)}\n\n" : "") +
                           $"The lines:\n{Upkeep.Transcript(persona, lines)}"),
    ];

    /// <summary>
    /// Reads the observer's answer into observations of <paramref name="lines"/>: each dated by the first line of the day
    /// it names (or of the batch's last day, when it names none of them). Null when it is not the JSON asked for; an
    /// empty list when nothing was worth keeping.
    /// </summary>
    public static List<Observation>? Parse(string answer, IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0 || Items(answer) is not { } items)
            return null;
        var made = new List<Observation>();
        foreach (var (named, priority, text) in items)
        {
            var day = named is { } d && DateOnly.TryParseExact(d.Trim(), "yyyy-MM-dd", English, DateTimeStyles.None, out var parsed) ? parsed : (DateOnly?)null;
            var said = lines.Where(l => DateOnly.FromDateTime(l.At.DateTime) == day).ToList();
            if (said.Count == 0)
                said = [.. lines.Where(l => l.At.Date == lines[^1].At.Date)];
            made.Add(new Observation(0, said[0].At, priority, text, said[0].Id, said[^1].Id));
        }
        return made;
    }

    /// <summary>
    /// What to condense next, when the notes in view no longer fit what she sees of them: the oldest finished week of
    /// day notes, else the oldest finished month of week notes, else the oldest finished year of month notes. A period
    /// still going on is left as it is. Null when everything fits or nothing is finished yet.
    /// </summary>
    public static Stretch? ToCondense(IReadOnlyList<Observation> log, int window, DateTimeOffset now)
    {
        var current = log.Where(o => !o.Condensed).ToList();
        if (Newest(current, Budget(window)).Count == current.Count)
            return null;
        foreach (var into in new[] { Span.Week, Span.Month, Span.Year })
        {
            var group = current.Where(o => o.Span == into - 1).GroupBy(o => Start(o.At, into))
                .Where(g => g.Key < Start(now, into)).OrderBy(g => g.Key).FirstOrDefault();
            if (group is not null)
                return new Stretch(into, group.Key, Period(into, group.Key), [.. group]);
        }
        return null;
    }

    private static DateTimeOffset Start(DateTimeOffset at, Span span) => span switch
    {
        Span.Week => new DateTimeOffset(Conversation.MondayOf(at).ToDateTime(TimeOnly.MinValue), at.Offset),
        Span.Month => new DateTimeOffset(at.Year, at.Month, 1, 0, 0, 0, at.Offset),
        _ => new DateTimeOffset(at.Year, 1, 1, 0, 0, 0, at.Offset),
    };

    private static string Period(Span span, DateTimeOffset at) => span switch
    {
        Span.Week => "the week starting " + at.ToString("dddd d MMMM yyyy", English),
        Span.Month => at.ToString("MMMM yyyy", English),
        _ => at.ToString("yyyy", English),
    };

    /// <summary>
    /// The request that condenses a finished stretch of her notes into notes for the whole of it, about a third as long:
    /// a narrow task that sees only that stretch. What matters most is kept nearly word for word, and where a later note
    /// changes an earlier one only what is true now; the originals stay in her log either way.
    /// </summary>
    public static List<ChatMessage> Condense(Persona persona, Stretch stretch)
    {
        var words = Math.Max(40, stretch.Notes.Sum(n => n.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length) / 3);
        return
        [
            new(ChatRole.System, $$"""
                You keep {{persona.Name}}'s notes on her conversation with the player. Below are her notes from {{stretch.Period}}. Condense them into fewer notes for {{stretch.Period}} as a whole, at most {{words}} words in all. Keep every note marked "!" nearly word for word, with its names, dates and numbers; where a later note changes an earlier one, keep only what is true by the end; merge what repeats; drop what was only in passing. Answer with JSON only:

                {"observations": [{"priority": "high", "text": "The player …"}]}

                "priority": "high" for what will matter for weeks, "medium" for useful detail, "low" for the rest. Third person, in the language of the notes.
                """),
            new(ChatRole.User, Laid(stretch.Notes, null)),
        ];
    }

    /// <summary>Reads a condensing answer into notes for the whole <paramref name="stretch"/>; null when it is not the JSON asked for, or has nothing.</summary>
    public static List<Observation>? Parse(string answer, Stretch stretch) =>
        Items(answer) is { Count: > 0 } items
            ? [.. items.Select(i => new Observation(0, stretch.At, i.Priority, i.Text, stretch.Notes.Min(n => n.First), stretch.Notes.Max(n => n.Last), stretch.Span))]
            : null;

    /// <summary>The notes in an answer, as written: the day named (if any), how much each matters, and its words. Null when it is not the JSON asked for.</summary>
    private static List<(string? Day, Priority Priority, string Text)>? Items(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            if (!json.RootElement.TryGetProperty("observations", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;
            return [.. list.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object && Upkeep.Text(o, "text")?.Trim() is { Length: > 2 })
                .Select(o => (Upkeep.Text(o, "date"), Upkeep.Text(o, "priority")?.Trim().ToLowerInvariant() switch
                {
                    "high" => Priority.High,
                    "low" => Priority.Low,
                    _ => Priority.Medium,
                }, Upkeep.Text(o, "text")!.Trim()))];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
