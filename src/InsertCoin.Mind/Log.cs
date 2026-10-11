using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>How much an observation matters: what matters for weeks, useful detail, or something in passing.</summary>
public enum Priority { High, Medium, Low }

/// <summary>Something she observed in the conversation, on the day it was said, from lines <see cref="First"/> to <see cref="Last"/>.</summary>
public sealed record Observation(long Id, DateTimeOffset At, Priority Priority, string Text, long First, long Last);

/// <summary>
/// Her memory of what has left the window, as a dated log of observations (docs/specs/2026-10-11-observational-memory.md):
/// the request that observes the lines leaving the window, what of the log she sees, and how it is laid out. The model
/// only writes each observation and says how much it matters; the days, the order, the layout and the size are kept
/// here, so they never drift.
/// </summary>
public static class Log
{
    /// <summary>Tokens of lines observed in one request, and of her latest observations sent with them, for continuity.</summary>
    public const int ObserveAtOnce = 6000, Continuity = 600;

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>What of the log she sees: three eighths of her window (12k tokens at 32k), her newest observations.</summary>
    public static int Budget(int window) => window * 3 / 8;

    /// <summary>The newest observations that fit in <paramref name="tokens"/>, oldest first.</summary>
    public static IReadOnlyList<Observation> Newest(IReadOnlyList<Observation> log, int tokens)
    {
        var used = 0;
        var from = log.Count;
        while (from > 0)
        {
            var o = log[from - 1];
            var cost = Prompt.Tokens(o.Text) + 3 + (from == log.Count || log[from].At.Date != o.At.Date ? 10 : 0);   // a day starts a heading
            if (used + cost > tokens)
                break;
            used += cost;
            from--;
        }
        return log.Skip(from).ToList();
    }

    /// <summary>
    /// The observations as she reads them: a heading for each day, with how long ago it was when <paramref name="now"/>
    /// is known (whole days only, so it changes at midnight and the cached prompt holds), and "!" before what matters most.
    /// </summary>
    public static string Laid(IReadOnlyList<Observation> observations, DateTimeOffset? now)
    {
        var text = new StringBuilder();
        DateTime? day = null;
        foreach (var o in observations)
        {
            if (o.At.Date != day)
            {
                day = o.At.Date;
                text.Append(text.Length == 0 ? "" : "\n").Append(o.At.ToString("ddd d MMM yyyy", English));
                if (now is { } today)
                    text.Append(" (").Append(DaysAgo(o.At, today)).Append(')');
            }
            text.Append("\n- ").Append(o.Priority == Priority.High ? "! " : "").Append(o.Text);
        }
        return text.ToString();
    }

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
    /// the whole history, so it is the same small task however long the conversation has been.
    /// </summary>
    public static List<ChatMessage> Observe(Persona persona, IReadOnlyList<Observation> latest, IReadOnlyList<Line> lines) =>
    [
        new(ChatRole.System, $$"""
            You keep {{persona.Name}}'s notes on her conversation with the player; she sits beside the player's arcade cabinet and chats with them. The lines below are leaving the part of the conversation she reads word for word, and your notes are what she will have of them. Answer with JSON only:

            {"observations": [{"date": "2026-10-05", "priority": "high", "text": "The player has a job interview at Acme on Friday 9 October at 15:00 and is nervous about the system-design round."}]}

            One observation per thing worth keeping: what the player did, said, decided or plans; what they like, dislike or worry about; people, places, names, numbers and dates exactly as said; what {{persona.Name}} said, suggested or promised; how the two of them got on. "date" is the day it was said, from the timestamps. In "text", write dates out in full ("Friday 9 October", "in March 2026"), never "tomorrow" or "last week": work them out from the timestamps. "priority": "high" for what will matter for weeks (plans, goals, people, strong likes and dislikes, promises, a change to something known before), "medium" for useful detail, "low" for something in passing. Leave out greetings, small talk and the cabinet's numbers; when nothing is worth keeping, answer {"observations": []}. Write in the third person ("The player…", "{{persona.Name}}…"), in the language the conversation is in.
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
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start || lines.Count == 0)
            return null;
        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            if (!json.RootElement.TryGetProperty("observations", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;
            var made = new List<Observation>();
            foreach (var item in list.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object))
            {
                if (Upkeep.Text(item, "text")?.Trim() is not { Length: > 2 } text)
                    continue;
                var day = Upkeep.Text(item, "date") is { } named && DateOnly.TryParseExact(named.Trim(), "yyyy-MM-dd", English, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
                var said = lines.Where(l => DateOnly.FromDateTime(l.At.DateTime) == day).ToList();
                if (said.Count == 0)
                    said = [.. lines.Where(l => l.At.Date == lines[^1].At.Date)];
                var priority = Upkeep.Text(item, "priority")?.Trim().ToLowerInvariant() switch
                {
                    "high" => Priority.High,
                    "low" => Priority.Low,
                    _ => Priority.Medium,
                };
                made.Add(new Observation(0, said[0].At, priority, text, said[0].Id, said[^1].Id));
            }
            return made;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
