using System.Globalization;

namespace InsertCoin.Mind;

/// <summary>Something going on in the player's life with no date, for her to follow up (see <see cref="Threads"/>).</summary>
public sealed record OpenThread(long Id, string Text, DateTimeOffset Opened, DateTimeOffset Mentioned, DateTimeOffset? Asked);

/// <summary>One change upkeep asks for to the open threads: open one, update one with news, or close one that is settled.</summary>
public sealed record ThreadChange(string Op, long? Id, string? Text);

/// <summary>
/// Open threads (phase 4, docs/specs/2026-10-11-observational-memory.md): what is going on in the player's life with no
/// date, that a friend would ask about later (a worry, a decision, something they are waiting for). Upkeep opens, updates
/// and closes them from her notes, as it does the agenda's plans; a couple of days after one was last mentioned she asks
/// how it is going, once, at the PC or by text while they are out (the check-ins' routing), and whatever comes of it, by
/// text, in person or not at all, her next notes tell upkeep. Unmentioned for three weeks, one lapses.
/// </summary>
public static class Threads
{
    public static readonly TimeSpan AskAfter = TimeSpan.FromDays(2), Lapse = TimeSpan.FromDays(21);

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>The thread to ask about now, the longest waiting first: mentioned at least <see cref="AskAfter"/> ago, not asked about since, not lapsed.</summary>
    public static OpenThread? ToAskAbout(IReadOnlyList<OpenThread> open, DateTimeOffset now) =>
        open.Where(t => now - t.Mentioned >= AskAfter && now - t.Mentioned < Lapse && (t.Asked is not { } asked || asked < t.Mentioned))
            .OrderBy(t => t.Mentioned)
            .FirstOrDefault();

    /// <summary>What the cabinet tells her, for her to ask how it is going (as <see cref="Agenda.CheckIn"/> does for a plan).</summary>
    public static string CheckIn(OpenThread thread, DateTimeOffset now) =>
        $"They told you about this {When.Ago(thread.Mentioned, now)}: \"{thread.Text}\", and you have not heard how it is going. " +
        "Ask about it in one short sentence, without pushing.";

    /// <summary>The open threads as upkeep's request lists them, each with its number.</summary>
    public static string Listed(IReadOnlyList<OpenThread> open) =>
        open.Count == 0 ? "(none)" : string.Join("\n", open.Select(t => $"#{t.Id} (since {t.Opened.ToString("d MMM", English)}) {t.Text}"));
}
