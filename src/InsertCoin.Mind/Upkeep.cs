using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>One change upkeep asks for: add a fact (about the player or about "us"), update one, or delete one.</summary>
public sealed record FactChange(string Op, long? Id, string? About, string? Text, int? Importance);

/// <summary>One change to the agenda upkeep asks for: add an item, update one, or cancel one.</summary>
/// <param name="Options">How long before to remind and how it repeats, as words (see <see cref="Agenda.ParseOptions"/>).</param>
public sealed record PlanChange(string Op, long? Id, string? When, string? Text, bool Remind, string? Options = null);

/// <summary>Facts a review folds into one (one id: a rewrite).</summary>
public sealed record Merge(IReadOnlyList<long> Ids, string Text, int? Importance);

/// <summary>A fact a review moves to the background, and why.</summary>
public sealed record Retirement(long Id, string Why);

/// <summary>What a review of her facts asks for.</summary>
public sealed record Verdict(IReadOnlyList<Merge> Merges, IReadOnlyList<Retirement> Retirements);

/// <summary>What upkeep drew from a stretch of conversation: a dated episode and changes to what she knows.</summary>
public sealed record Digestion(string? Episode, IReadOnlyList<FactChange> Changes)
{
    /// <summary>The moment of the day the episode rewrites (it continues it), or null for a new one.</summary>
    public long? Continues { get; init; }

    /// <summary>Changes to the agenda: things ahead with a date.</summary>
    public IReadOnlyList<PlanChange> Plans { get; init; } = [];
}

/// <summary>
/// Her "sleep time" (Letta's term): after a conversation has gone quiet, a model reads the part she has not
/// digested yet and returns plain JSON: an episode summary and fact changes, reconciled against what she already
/// knows (mem0's add / update / delete). No tool calling, so any model that can write JSON will do; output that
/// does not parse is simply tried again later.
/// </summary>
public static class Upkeep
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <param name="known">The facts to reconcile against: those related to these lines (see <see cref="Recall.Related"/>).</param>
    /// <param name="today">The moments already filed for the day these lines are from: the new part may continue one of them.</param>
    /// <param name="agenda">What is on the agenda now, so the new part's plans are added once and changes reach the right item.</param>
    public static List<ChatMessage> Request(Persona persona, IReadOnlyList<Fact> known, IReadOnlyList<Line> lines, IReadOnlyList<Episode>? today = null,
                                            IReadOnlyList<Plan>? agenda = null)
    {
        var facts = known.Count == 0
            ? "(none yet)"
            : string.Join("\n", known.Select(f => $"#{f.Id} [{(f.Scope == MindStore.Shared ? "player" : "us")}, {f.Importance}] {f.Text}"));
        var length = $"one to three sentences, at most {Conversation.MomentWords} words";
        var episode = today is not { Count: > 0 }
            ? $"\"episode\": a moment on what happened ({length}; it is filed under its date already), or null when it was only greetings or small talk with nothing worth remembering. \"continues\": null."
            : $"\"episode\" and \"continues\": the day's moments so far are below. If the new part carries on one of them, give that moment rewritten to take it in " +
              $"(the whole of it, still {length}) and its number in \"continues\". " +
              (today.Count < Conversation.DayMoments
                  ? $"If it is about something else, give a new moment ({length}) and \"continues\": null. "
                  : "The day already has as many moments as it may: rewrite the one the new part is closest to. ") +
              "If it adds nothing worth remembering, \"episode\": null.";
        var day = today is not { Count: > 0 } ? "" : $"The day's moments so far:\n{string.Join("\n", today.Select(e => $"#{e.Id} {e.Text}"))}\n\n";
        var plans = agenda is not { Count: > 0 }
            ? "(empty)"
            : string.Join("\n", agenda.Select(p => $"#{p.Id} {p.At.ToString(p.AllDay ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", English)}{(p.Remind ? " [reminder]" : "")} {p.Text}"));
        return
        [
            new(ChatRole.System, $$"""
                You maintain the long-term memory of {{persona.Name}}, a companion who sits beside the player's arcade cabinet and chats with them. Read the new part of their conversation and answer with JSON only, in this form:

                {"episode": "...", "continues": null, "facts": [
                  {"op": "add", "about": "player", "text": "The player is looking for a backend developer job.", "importance": 8},
                  {"op": "update", "id": 12, "text": "...", "importance": 6},
                  {"op": "delete", "id": 5}],
                 "plans": [
                  {"op": "add", "when": "2026-10-06 15:00", "text": "Job interview at Acme", "remind": true, "before": "1 hour"},
                  {"op": "add", "when": "2026-10-05 08:00", "text": "Take vitamins", "remind": true, "repeat": "daily"},
                  {"op": "update", "id": 3, "when": "2026-10-12", "text": "Calculus exam"},
                  {"op": "cancel", "id": 4}]}

                {{episode}}

                Facts are short, standalone statements in the third person, in the language the conversation is in. "about": "player" for the player's life, goals, likes, habits and people; "us" for what is between the player and {{persona.Name}}: nicknames, promises, jokes, how they like to talk. Importance: 1-3 trivia, 4-6 useful, 7-9 important (goals, strong likes and dislikes), 10 essential (their name). Update a known fact when the conversation changes it, delete one that is no longer true, and never add one that is already known. Leave out the cabinet's numbers (time banked, streaks, tokens): they are always on screen. If nothing is worth keeping, give an empty list.

                Plans are things ahead with a date: appointments, exams, deadlines, trips. They go in "plans", not in facts. "when" is "YYYY-MM-DD HH:MM", or "YYYY-MM-DD" for a whole day; turn relative dates ("tomorrow", "next Friday") into dates using the timestamps. Add what the agenda below does not have yet, update an item the conversation moves or changes, and cancel one that is called off. "remind": true only when the player asked to be reminded: with "before" ("10 min", "1 hour", "1 day") when they asked to be reminded that long ahead, else "when" is when the reminder goes off. "repeat" ("daily", "weekdays", "weekly", "monthly") only for something that recurs. If there are none, give an empty list.
                """),
            new(ChatRole.User, $"{day}The agenda:\n{plans}\n\nKnown facts that may relate:\n{facts}\n\nNew part of the conversation:\n{Transcript(persona, lines)}"),
        ];
    }

    /// <summary>Reads the model's answer; null when it is not the JSON asked for.</summary>
    public static Digestion? Parse(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = json.RootElement;
            var episode = root.TryGetProperty("episode", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var changes = new List<FactChange>();
            if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Array)
                foreach (var f in facts.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
                    changes.Add(new FactChange(
                        Text(f, "op")?.ToLowerInvariant() ?? "",
                        f.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var n) ? n : null,
                        Text(f, "about")?.ToLowerInvariant(),
                        Text(f, "text"),
                        f.TryGetProperty("importance", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var level) ? level : null));
            var continues = root.TryGetProperty("continues", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var moment) ? moment : (long?)null;
            var plans = new List<PlanChange>();
            if (root.TryGetProperty("plans", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var p in list.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
                    plans.Add(new PlanChange(
                        Text(p, "op")?.ToLowerInvariant() ?? "",
                        p.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var n) ? n : null,
                        Text(p, "when"),
                        Text(p, "text")?.Trim(),
                        p.TryGetProperty("remind", out var r) && r.ValueKind == JsonValueKind.True,
                        string.Join(", ", new[] { Text(p, "before") is { Length: > 0 } b ? (b.Contains("before") ? b : b + " before") : null, Text(p, "repeat") }
                                              .Where(o => o is not null))));
            return new Digestion(string.IsNullOrWhiteSpace(episode) ? null : episode.Trim(), changes) { Continues = continues, Plans = plans };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The request that rewrites one of the notes she always has in mind from the facts behind it, keeping what the
    /// note says unless a fact contradicts it (the player may have written parts of it).
    /// </summary>
    public static List<ChatMessage> Rewrite(string subject, string note, IEnumerable<Fact> facts) =>
    [
        new(ChatRole.System, $"""
            You keep a short note that a companion always has in mind about {subject}. Rewrite the note using the facts below. Keep what the note says unless a fact contradicts it: the player may have written parts of it. Most important first, plain sentences, no lists, in the language most of the facts are in, at most {Prompt.NoteLimit - 100} characters. Output only the note.
            """),
        new(ChatRole.User, $"The note now:\n{(note.Trim().Length > 0 ? note.Trim() : "(empty)")}\n\nFacts:\n{string.Join("\n", facts.Select(f => $"- {f.Text}"))}"),
    ];

    /// <summary>
    /// The request that combines moments into one for a longer stretch (<paramref name="period"/>: "the week starting
    /// Monday 7 September 2026", "September 2026", "2026"): days into a week, weeks into a month, months into a year.
    /// </summary>
    public static List<ChatMessage> Combine(Persona persona, string period, Span into, IEnumerable<Episode> moments) =>
    [
        new(ChatRole.System, $"""
            You keep the memory of a player and {persona.Name}, a companion who sits beside the player's arcade cabinet. Combine the moments below, all from {period}, into one summary of {(into == Span.Year ? "four to six" : "two to four")} sentences, at most {Conversation.RolledUpWords(into)} words. Keep what will matter later (events, plans, feelings, how the two got along, what changed), in the language the moments are in. Output only the summary.
            """),
        new(ChatRole.User, string.Join("\n", moments.Select(e => $"- {e.Label}: {e.Text}"))),
    ];

    /// <summary>
    /// The request that reviews a batch of her facts (sleep time, about weekly): merge what says the same thing, and
    /// retire what she no longer needs in mind. Retired facts are kept, to restore.
    /// </summary>
    public static List<ChatMessage> Review(Persona persona, IReadOnlyList<Fact> facts, DateTimeOffset now) =>
    [
        new(ChatRole.System, $$"""
            You tend the long-term memory of {{persona.Name}}, a companion who sits beside the player's arcade cabinet. Today is {{now.ToString("dddd d MMMM yyyy", English)}}. Below are facts she keeps, each with its number, who it is about, its importance (1-10), when she learned it and when it last came up. Answer with JSON only:

            {"merge": [{"ids": [3, 7], "text": "one fact saying what they both say", "importance": 6}], "retire": [{"id": 5, "why": "a few words"}]}

            Merge facts that say the same thing or read better as one; a merge of a single fact rewrites it (a plan whose date has passed but which still matters, say, put in the past tense). Retire what she no longer needs in mind: plans and dates that have passed and no longer matter, facts another fact contradicts or replaces, and trivia that has not come up for months. Keep anything still true that could matter. Retiring is not deleting: the player can bring a fact back. Facts marked pinned stay as they are. When nothing needs doing, answer {"merge": [], "retire": []}.
            """),
        new(ChatRole.User, string.Join("\n", facts.Select(f =>
            $"#{f.Id} [{(f.Scope == MindStore.Shared ? "player" : "us")}, {f.Importance}{(f.Pinned ? ", pinned" : "")}] {f.Text} " +
            $"(learned {f.Learned.ToString("d MMM yyyy", English)}; last came up {(f.Recalled is { } at ? When.Ago(at, now) : "never")})"))),
    ];

    /// <summary>Reads a review's answer; null when it is not the JSON asked for.</summary>
    public static Verdict? ParseReview(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = json.RootElement;
            var merges = new List<Merge>();
            if (root.TryGetProperty("merge", out var merge) && merge.ValueKind == JsonValueKind.Array)
                foreach (var m in merge.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object))
                {
                    var ids = m.TryGetProperty("ids", out var list) && list.ValueKind == JsonValueKind.Array
                        ? list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out _)).Select(x => x.GetInt64()).ToList()
                        : [];
                    if (ids.Count > 0 && Text(m, "text") is { Length: > 2 } text)
                        merges.Add(new Merge(ids, text.Trim(),
                            m.TryGetProperty("importance", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var level) ? level : null));
                }
            var retirements = new List<Retirement>();
            if (root.TryGetProperty("retire", out var retire) && retire.ValueKind == JsonValueKind.Array)
                foreach (var r in retire.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
                    if (r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var n))
                        retirements.Add(new Retirement(n, Text(r, "why")?.Trim() is { Length: > 0 } why ? why : "no longer needed"));
            return new Verdict(merges, retirements);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Transcript(Persona persona, IEnumerable<Line> lines) => string.Join("\n", lines.Select(line =>
        $"[{line.At.ToString("ddd d MMM yyyy HH:mm", English)}] " + line.Speaker switch
        {
            Speaker.User => "Player: ",
            Speaker.Her => $"{persona.Name}: ",
            _ => "Cabinet: ",
        } + line.Text));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
