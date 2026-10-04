using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Mind;

/// <summary>The cabinet in plain words, for her context: where things stand now, and what just happened.</summary>
public static class Situation
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>A few short lines on where things stand at the cabinet right now (the time itself is in the turn's own <c>now</c>, see <see cref="When"/>).</summary>
    public static string Describe(LedgerState s, Rules rules, DateTimeOffset now, string? player)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(player))
            lines.Add($"Player: {player} on LeetCode.");

        var left = s.TimeLeftMs(rules);
        var daysLeft = s.WeekStart.AddDays(7).DayNumber - GameCalendar.DayOf(now, rules).DayNumber;
        lines.Add(left > 0
            ? $"Play time banked: {Words.Span(left)}. Unused time expires when the week ends, in {Words.Count(Math.Max(daysLeft, 0), "day")}."
            : "Play time banked: none.");
        lines.Add(s.DebtMs > 0 ? $"Owed: {Words.Span(s.DebtMs)} ({Words.Count(s.IouTickets(rules), "IOU ticket")})." : "Owed: nothing.");

        var streak = GameCalendar.CurrentStreak(s, now, rules);
        lines.Add($"Daily solve streak: {Words.Count(streak, "day")} (best {s.BestStreak}).");
        lines.Add($"This week: {Words.Count(s.Week.Solves, "problem")} solved, {Words.Count(s.Week.TokensEarned, "token")} earned, " +
                  (s.Week.PlayedMs >= 60_000 ? $"{Words.Span(s.Week.PlayedMs)} played." : "no games played yet."));

        lines.Add(s.Session switch
        {
            null => "Right now: not playing.",
            _ when s.Paused => $"Right now: in a game of {string.Join(" + ", s.Session.GameNames)}, but the player stepped away (clock paused).",
            var session => $"Right now: playing {string.Join(" + ", session.GameNames)}, {Words.Span(session.PlayedMs)} into the session.",
        });
        return string.Join("\n", lines);
    }

    /// <summary>
    /// What an event looks like in her conversation log, or null for routine ones (a token going in every quarter
    /// hour, the clock pausing) that would only bury what matters.
    /// </summary>
    public static string? Witness(LedgerEvent e, Rules rules) => e switch
    {
        CoinsEarned c => $"The player solved \"{c.Task.Title}\" ({c.Task.Difficulty?.ToString() ?? "unknown difficulty"}): " +
                         $"+{Words.Count(c.Tokens, "token")}" + (c.StarToken ? $", with a day-{c.Streak} streak bonus." : "."),
        DebtPaid { DebtLeftMs: 0 } => "That paid off all the debt.",
        DebtPaid p => $"That paid off {Words.Span(p.PaidMs)} of debt; {Words.Span(p.DebtLeftMs)} still owed.",
        LowTime l => $"{Words.Span(l.TimeLeftMs)} of play time left.",
        CreditsOut => $"Play time ran out; the game goes on, on credit at x{rules.DebtMultiplier.ToString("0.#", English)}.",
        IouPrinted i => $"IOU ticket #{i.TicketNumber} printed.",
        SessionStarted started => $"The player started playing {string.Join(" + ", started.GameNames)}.",
        SessionEnded ended => $"The player stopped playing after {Words.Span(ended.Receipt.PlayedMs)}" +
                              (ended.Receipt.IousPrinted > 0 ? $"; {Words.Count(ended.Receipt.IousPrinted, "IOU ticket")} printed." : "."),
        WeeklyReset w => "A new week started" + (w.ExpiredMs > 0 ? $"; {Words.Span(w.ExpiredMs)} of unused time expired." : ".") +
                         (w.DebtCarriedMs > 0 ? $" {Words.Span(w.DebtCarriedMs)} of debt carried over." : ""),
        _ => null,
    };
}

public static class Words
{
    /// <summary>"45 min", "1 h 20 min", "under a minute".</summary>
    public static string Span(long ms)
    {
        var minutes = ms / 60_000;
        return minutes < 1 ? "under a minute" : minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }

    /// <summary>"1 day", "3 days".</summary>
    public static string Count(int n, string thing) => n == 1 ? $"1 {thing}" : $"{n} {thing}s";
}
