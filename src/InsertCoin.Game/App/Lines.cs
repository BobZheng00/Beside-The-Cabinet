using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Game.App;

/// <summary>
/// What the companion says about what just happened. Plain canned lines built from the ledger's own numbers: instant,
/// free and offline. Her mind (App/Chat.cs) answers when spoken to; these remain her remarks on events.
/// </summary>
public static class Lines
{
    /// <summary>One line for a step's events (the most pressing one wins), or null when there is nothing to say.</summary>
    public static string? For(IReadOnlyList<LedgerEvent> events, LedgerState state, Rules rules, Random random)
    {
        var said = events.Select(e => (Weight: Urgency(e), Line: For(e, state, rules, random))).Where(x => x.Line is not null).ToList();
        return said.Count == 0 ? null : said.MaxBy(x => x.Weight).Line;
    }

    /// <summary>Where things stand, for when she is asked (clicked).</summary>
    public static string Status(LedgerState state, Rules rules, int streak)
    {
        var left = state.TimeLeftMs(rules);
        if (state.DebtMs > 0 && left == 0)
        {
            var solves = (int)Math.Ceiling(state.DebtMs / (double)rules.TokenMs);
            return $"You owe {Span(state.DebtMs)}. {solves} Easy {(solves == 1 ? "solve clears" : "solves clear")} it.";
        }
        if (state.Session is not null)
            return $"{Span(left)} left in this session.";
        if (left > 0)
            return $"{Span(left)} banked." + (streak > 1 ? $" {streak}-day streak!" : " Play or save it?");
        return $"No coins yet. One Easy problem is {Span(rules.TokenMs)} of play.";
    }

    private static string? For(LedgerEvent e, LedgerState state, Rules rules, Random random) => e switch
    {
        CoinsEarned { Task.Difficulty: Difficulty.Hard } c => Pick(random, $"JACKPOT! {c.Tokens} coins for a Hard one!", $"A Hard one! That's {Span(c.Tokens * rules.TokenMs)} of play.") + Bonus(c),
        CoinsEarned { Tokens: 1 } c => Pick(random, "+1 coin. Every one counts!", $"Solved! {Span(rules.TokenMs)} more to play.") + Bonus(c),
        CoinsEarned c => Pick(random, $"Nice! +{c.Tokens} coins.", $"Solved! {Span(c.Tokens * rules.TokenMs)} more to play.") + Bonus(c),
        DebtPaid { DebtLeftMs: 0 } => "Debt cleared. Clean slate!",
        DebtPaid p => $"That paid off {Span(p.PaidMs)}. {Span(p.DebtLeftMs)} still owed.",
        TokenInserted t => t.CreditsLeft > 0 ? $"Token in. {t.CreditsLeft} left after this one." : "Token in. That was your last one.",
        LowTime { Level: Warning.FiveMinutes } => "Five minutes left. Look for a save point?",
        LowTime { Level: Warning.OneMinute } => "One minute! Save your game.",
        CreditsOut => $"Out of time. You're playing on credit now, at x{rules.DebtMultiplier.ToString("0.#", CultureInfo.InvariantCulture)}.",
        IouPrinted i => $"IOU #{i.TicketNumber}. That's {Span(i.DebtMs)} to pay back.",
        SessionStarted => state.TimeLeftMs(rules) > 0 ? $"Have fun! You've got {Span(state.TimeLeftMs(rules))}." : "No coins... this one goes on your tab.",
        SessionEnded end => $"Good game! {Span(end.Receipt.PlayedMs)} played." + (state.DebtMs > 0 ? $" You owe {Span(state.DebtMs)}." : ""),
        Paused => "Stepped away? I'll stop the clock.",
        Resumed => "Welcome back! The clock is running.",
        WeeklyReset { ExpiredMs: > 0 } w => $"New week! {Span(w.ExpiredMs)} expired, sorry.",
        WeeklyReset => "New week, fresh start!",
        _ => null,
    };

    private static string Bonus(CoinsEarned c) => c.StarToken ? $" Day {c.Streak} streak bonus!" : "";

    private static int Urgency(LedgerEvent e) => e switch
    {
        LowTime or CreditsOut or IouPrinted => 5,
        DebtPaid => 4,
        CoinsEarned => 3,
        SessionStarted or SessionEnded or WeeklyReset => 2,
        _ => 1,
    };

    private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];

    /// <summary>"45 min", "1 h 20 min", "under a minute".</summary>
    private static string Span(long ms)
    {
        var minutes = ms / 60_000;
        return minutes < 1 ? "under a minute" : minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }
}
