using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>
/// What is sent to the model. Ordered from what never changes to what changes every turn (rules and persona, notes,
/// summary, recent conversation, the cabinet right now), so providers that cache a prompt's start can reuse it.
/// </summary>
public static class Prompt
{
    /// <summary>Characters allowed in each always-present note (Hermes keeps its user profile to 1,375).</summary>
    public const int NoteLimit = 1400;
    /// <summary>Tokens of verbatim conversation sent at most; older lines wait to be folded into the summary.</summary>
    public const int HistoryBudget = 6000;
    /// <summary>Stage directions she may start a reply with; each is acted out by her body.</summary>
    public static readonly string[] Moments = ["cheer", "pump", "wave", "look", "gasp", "worry"];

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly TimeSpan Gap = TimeSpan.FromHours(2);       // a pause long enough to say when talk resumed

    public static List<ChatMessage> Build(Persona persona, string aboutUser, string us, Summary? summary, IReadOnlyList<Line> lines,
                                          string situation, string said)
    {
        var system = new StringBuilder($"""
            You are {persona.Name}. You sit on a stool next to the player's desktop arcade cabinet, INSERT COIN, and keep them company.

            {persona.Description.Trim()}

            How INSERT COIN works: the player earns gaming time by solving LeetCode problems: Easy +1 token, Medium +2, Hard +4, and one token is 15 minutes of play. Solving on 3 or more days in a row adds a bonus token. Steam games use the time up; when it runs out the game goes on, on credit at 1.5x, and the cabinet prints IOU tickets. Unused time expires at the end of each week. The cabinet never closes a game: you can encourage, remind and tease, never threaten or lecture.

            How you talk: your words appear in a small speech bubble, so answer in one to three short sentences (under 40 words). Plain text: no lists, no markdown, no actions in asterisks. Reply in the language the player writes in. Stay in character as {persona.Name}.

            Your body: you may start a reply with one stage direction in square brackets. It is acted out, not shown: [cheer] celebrate, [pump] fist pump, [wave] wave, [look] glance at the cabinet's screen, [gasp] alarmed, [worry] worried. Leave it out when none fits.

            Lines starting with [cabinet], and the <cabinet> block, come from the cabinet itself, not from the player.

            What you know about the player:
            {(aboutUser.Trim().Length > 0 ? aboutUser.Trim() : "Nothing yet.")}

            You and the player:
            {(us.Trim().Length > 0 ? us.Trim() : "You have only just met.")}
            """);
        if (summary is not null)
            system.Append("\n\nThe conversation so far, summarised:\n").Append(summary.Text.Trim());

        var turns = new List<(ChatRole Role, StringBuilder Text)>();
        void Put(ChatRole role, string text)
        {
            if (turns.Count > 0 && turns[^1].Role == role)
                turns[^1].Text.Append('\n').Append(text);
            else
                turns.Add((role, new StringBuilder(text)));
        }

        DateTimeOffset? previous = null;
        foreach (var line in Recent(lines))
        {
            if (previous is null || line.At - previous > Gap)
                Put(ChatRole.User, $"[cabinet] {line.At.ToString("dddd d MMMM, HH:mm", English)}");
            previous = line.At;
            switch (line.Speaker)
            {
                case Speaker.User: Put(ChatRole.User, line.Text); break;
                case Speaker.Her: Put(ChatRole.Assistant, line.Text); break;
                default: Put(ChatRole.User, $"[cabinet] {line.Text}"); break;
            }
        }
        Put(ChatRole.User, $"<cabinet>\n{situation.Trim()}\n</cabinet>\n\n{said.Trim()}");

        var messages = new List<ChatMessage> { new(ChatRole.System, system.ToString()) };
        messages.AddRange(turns.Select(t => new ChatMessage(t.Role, t.Text.ToString())));
        return messages;
    }

    /// <summary>The request that folds older lines into the summary (auto-compaction).</summary>
    public static List<ChatMessage> Fold(Persona persona, Summary? summary, IReadOnlyList<Line> lines)
    {
        var transcript = string.Join("\n", lines.Select(line =>
            $"[{line.At.ToString("ddd d MMM HH:mm", English)}] " + line.Speaker switch
            {
                Speaker.User => "Player: ",
                Speaker.Her => $"{persona.Name}: ",
                _ => "Cabinet: ",
            } + line.Text));
        return
        [
            new(ChatRole.System, $"""
                You keep the memory of an ongoing conversation between a player and {persona.Name}, a companion who sits beside the player's arcade cabinet. Merge the summary so far with the new part of the conversation into one updated summary.

                Keep what will matter later: facts about the player, plans and dates, promises, feelings, running jokes, how the two get along, and game sessions or solves worth remembering. Drop small talk and repetition. Write in the third person ("The player...", "{persona.Name}..."), in the language the conversation is mostly in, in at most 250 words. Output only the summary.
                """),
            new(ChatRole.User, $"Summary so far:\n{(summary?.Text.Trim() is { Length: > 0 } text ? text : "(none)")}\n\nNew part of the conversation:\n{transcript}"),
        ];
    }

    /// <summary>A rough token count: about four letters of English per token, one per character of Chinese or Japanese.</summary>
    public static int Tokens(string text)
    {
        var tokens = 0.0;
        foreach (var c in text)
            tokens += c < 128 ? 0.25 : 1;
        return (int)Math.Ceiling(tokens);
    }

    public static int Tokens(IEnumerable<Line> lines) => lines.Sum(line => Tokens(line.Text) + 4);

    /// <summary>The newest lines that fit the history budget.</summary>
    private static IEnumerable<Line> Recent(IReadOnlyList<Line> lines)
    {
        var used = 0;
        var from = lines.Count;
        while (from > 0 && used + Tokens(lines[from - 1].Text) + 4 <= HistoryBudget)
            used += Tokens(lines[--from].Text) + 4;
        return lines.Skip(from);
    }
}
