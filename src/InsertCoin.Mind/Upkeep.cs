using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>One change upkeep asks for: add a fact (about the player or about "us"), update one, or delete one.</summary>
public sealed record FactChange(string Op, long? Id, string? About, string? Text, int? Importance);

/// <summary>What upkeep drew from a stretch of conversation: a dated episode and changes to what she knows.</summary>
public sealed record Digestion(string? Episode, IReadOnlyList<FactChange> Changes);

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
    /// <param name="today">The moment already filed for the day these lines are from, to be rewritten rather than joined by another.</param>
    public static List<ChatMessage> Request(Persona persona, IReadOnlyList<Fact> known, IReadOnlyList<Line> lines, Episode? today = null)
    {
        var facts = known.Count == 0
            ? "(none yet)"
            : string.Join("\n", known.Select(f => $"#{f.Id} [{(f.Scope == MindStore.Shared ? "player" : "us")}, {f.Importance}] {f.Text}"));
        var episode = today is null
            ? "\"episode\": one or two sentences on what happened (it is filed under its date already), or null when it was only greetings or small talk with nothing worth remembering."
            : $"\"episode\": the day's moment so far is below; give it rewritten to take in the new part too (one to three sentences, the whole day), or null when the new part adds nothing worth remembering.";
        var day = today is null ? "" : $"The day's moment so far:\n{today.Text}\n\n";
        return
        [
            new(ChatRole.System, $$"""
                You maintain the long-term memory of {{persona.Name}}, a companion who sits beside the player's arcade cabinet and chats with them. Read the new part of their conversation and answer with JSON only, in this form:

                {"episode": "...", "facts": [
                  {"op": "add", "about": "player", "text": "The player has a job interview on Tuesday 6 October 2026.", "importance": 8},
                  {"op": "update", "id": 12, "text": "...", "importance": 6},
                  {"op": "delete", "id": 5}]}

                {{episode}}

                Facts are short, standalone statements in the third person, in the language the conversation is in. "about": "player" for the player's life, plans, dates, goals, likes, habits and people; "us" for what is between the player and {{persona.Name}}: nicknames, promises, jokes, how they like to talk. Importance: 1-3 trivia, 4-6 useful, 7-9 important (dates, goals, strong likes and dislikes), 10 essential (their name). Update a known fact when the conversation changes it, delete one that is no longer true, and never add one that is already known. Turn relative dates ("tomorrow", "next week") into dates, using the timestamps. Leave out the cabinet's numbers (time banked, streaks, tokens): they are always on screen. If nothing is worth keeping, give an empty list.
                """),
            new(ChatRole.User, $"{day}Known facts that may relate:\n{facts}\n\nNew part of the conversation:\n{Transcript(persona, lines)}"),
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
                        f.TryGetProperty("id", out var id) && id.TryGetInt64(out var n) ? n : null,
                        Text(f, "about")?.ToLowerInvariant(),
                        Text(f, "text"),
                        f.TryGetProperty("importance", out var i) && i.TryGetInt32(out var level) ? level : null));
            return new Digestion(string.IsNullOrWhiteSpace(episode) ? null : episode.Trim(), changes);
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
            You keep the memory of a player and {persona.Name}, a companion who sits beside the player's arcade cabinet. Combine the moments below, all from {period}, into one summary of {(into == Span.Year ? "four to six" : "two to four")} sentences. Keep what will matter later (events, plans, feelings, how the two got along, what changed), in the language the moments are in. Output only the summary.
            """),
        new(ChatRole.User, string.Join("\n", moments.Select(e => $"- {e.Label}: {e.Text}"))),
    ];

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
