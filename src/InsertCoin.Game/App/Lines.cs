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

    /// <summary>
    /// Reminders going off: "Reminder: call Mum." One that came due while the cabinet was closed says when:
    /// "While you were away (Fri 2 Oct 21:00): call Mum."
    /// </summary>
    public static string Reminder(IReadOnlyList<InsertCoin.Mind.Plan> due, DateTimeOffset now)
    {
        string When(InsertCoin.Mind.Plan p) => p.At.Date == now.Date && !p.AllDay ? p.At.ToString("HH:mm", CultureInfo.InvariantCulture) : p.Label;
        var late = due.Where(p => now - (p.RemindAt() ?? p.At) > InsertCoin.Mind.Agenda.Missed).ToList();
        var onTime = due.Except(late).ToList();
        var said = new List<string>();
        // one that goes off ahead of its item says when the item is
        string Said(InsertCoin.Mind.Plan p) => p.Text.TrimEnd('.') + (p.At - now > TimeSpan.FromMinutes(1) ? $" ({When(p)})" : "");
        if (onTime.Count > 0)
            said.Add($"{(onTime.Count == 1 ? "Reminder" : "Reminders")}: {string.Join("; ", onTime.Select(Said))}.");
        if (late.Count > 0)
            said.Add($"While you were away: {string.Join("; ", late.Select(p => $"{p.Text.TrimEnd('.')} ({When(p)})"))}.");
        return string.Join(" ", said);
    }

    /// <summary>A focus session started on the cabinet.</summary>
    public static string FocusStarted(InsertCoin.Mind.FocusPlan plan) => $"{plan.Focus} minutes of focus. You've got this!";

    /// <summary>A part of a focus session ended and the next began.</summary>
    public static string Focus(InsertCoin.Mind.FocusTurn turn, InsertCoin.Mind.FocusPlan plan) => turn.Began switch
    {
        InsertCoin.Mind.FocusPart.Break => $"Break: {plan.Break} minutes. Stand up, stretch, have some water.",
        InsertCoin.Mind.FocusPart.LongBreak => $"Long break: {plan.LongBreak} minutes. Well done! Step away for a bit.",
        _ => $"Back to it: {plan.Focus} minutes.",
    };

    /// <summary>The player ended a focus session.</summary>
    public static string FocusStopped(int done) => done == 0 ? "Session over." : $"Session over: {done} focus {(done == 1 ? "part" : "parts")}. Nice work.";

    /// <summary>Her plain words about rest, without her mind (see <see cref="InsertCoin.Mind.Rest"/>).</summary>
    public static string Rest(InsertCoin.Mind.Nudge nudge) => nudge switch
    {
        InsertCoin.Mind.Nudge.WindDown => "Half an hour to bedtime. Time to start winding down?",
        InsertCoin.Mind.Nudge.Bedtime => "It's bedtime. Sleep well!",
        InsertCoin.Mind.Nudge.StillUp => "Still up? It's past your bedtime.",
        _ => "Two hours in. Rest your eyes, stretch, have some water?",
    };

    /// <summary>A long stretch at the PC with no break.</summary>
    public const string StandUp = "You've been at it a while. Stand up, stretch, have some water?";

    private static string? For(LedgerEvent e, LedgerState state, Rules rules, Random random) => e switch
    {
        CoinsEarned { Task.Difficulty: Difficulty.Hard } c => Pick(random, $"JACKPOT! {c.Tokens} coins for a Hard one!", $"A Hard one! That's {Span(c.Tokens * rules.TokenMs)} of play.") + Bonus(c),
        CoinsEarned { Tokens: 1 } c => Pick(random, "+1 coin. Every one counts!", $"Solved! {Span(rules.TokenMs)} more to play.") + Bonus(c),
        CoinsEarned c => Pick(random, $"Nice! +{c.Tokens} coins.", $"Solved! {Span(c.Tokens * rules.TokenMs)} more to play.") + Bonus(c),
        StreakStar s => Pick(random, $"Day {s.Streak} of your streak! A bonus coin.", $"That's {s.Streak} good days in a row. Bonus coin!"),
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
        CapReached r => $"That's all the play today can earn ({Span(r.CapMs)}). Anything more is just for you.",
        LateNight l => $"It's past your bedtime. Play costs x{l.Multiplier.ToString("0.#", CultureInfo.InvariantCulture)} now.",
        Faded f => $"New day! {Span(f.FadedMs)} faded overnight; {Span(f.LeftMs)} left.",
        _ => null,
    };

    private static string Bonus(CoinsEarned c) => c.StarToken ? $" Day {c.Streak} streak bonus!" : "";

    private static int Urgency(LedgerEvent e) => e switch
    {
        LowTime or CreditsOut or IouPrinted => 5,
        DebtPaid or CapReached or LateNight => 4,
        CoinsEarned or StreakStar => 3,
        SessionStarted or SessionEnded or Faded => 2,
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
