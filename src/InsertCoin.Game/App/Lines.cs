using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Game.App;

/// <summary>
/// What the companion says about what just happened, and her plain lines: instant, free and offline, for when her mind
/// can't be reached. Only what a friend would react to: the cabinet's bookkeeping (a token going in, the clock pausing,
/// the overnight fade, the day's cap, part of a debt paid, an IOU printed) shows on its own screen (see
/// <see cref="Director"/>), not in her bubble (the user, 2026-10-12: her first words each morning were the fade).
/// </summary>
public static class Lines
{
    /// <summary>
    /// What she has to say about a step's events (the most pressing one), handed over as anything she tells is (see
    /// <see cref="InsertCoin.Mind.Reaching"/>): in her own words when her mind can, told what happened, else the plain
    /// line (these were the plain lines only, in no character's voice: "JACKPOT! 4 coins for a Hard one!", 2026-10-12).
    /// Each makes sense only at the PC, for a few minutes; while a game runs she isn't spoken through, and a warning
    /// the cabinet put on a ticket (time running low or out, past bedtime) needs nothing more. Null when there is
    /// nothing to say.
    /// </summary>
    public static InsertCoin.Mind.Telling? Remark(IReadOnlyList<LedgerEvent> events, LedgerState state, Rules rules, Random random, DateTimeOffset now)
    {
        var said = events.Select(e => (Event: e, Weight: Urgency(e), Line: For(e, state, rules, random))).Where(x => x.Line is not null).ToList();
        if (said.Count == 0)
            return null;
        var (e, _, line) = said.MaxBy(x => x.Weight);
        var what = InsertCoin.Mind.Situation.Witness(e, rules) ?? line!;
        return new InsertCoin.Mind.Telling(Title(e), $"{what} Say something about it in a short sentence, in your own words.", line!,
                                           InsertCoin.Mind.Matters.Must, Anywhere: false, now, TimeSpan.FromMinutes(3),
                                           // no play time: the cabinet shows nothing of a game, so nothing here goes on a ticket either
                                           Ticketed: !rules.Earns.Any || e is LowTime or CreditsOut or LateNight or SessionStarted);
    }

    /// <summary>What a ticket or the log calls it.</summary>
    private static string Title(LedgerEvent e) => e switch
    {
        CoinsEarned => "SOLVED",
        StreakStar => "STREAK",
        DebtPaid => "DEBT PAID",
        LowTime => "TIME LEFT",
        CreditsOut => "OUT OF TIME",
        SessionStarted => "GAME ON",
        SessionEnded => "GAME OVER",
        LateNight => "LATE NIGHT",
        _ => "CABINET",
    };

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
    public static string FocusStarted(InsertCoin.Mind.FocusPlan plan) => $"{InsertCoin.Mind.Words.Count(plan.Focus, "minute")} of focus. You've got this!";

    /// <summary>A part of a focus session ended and the next began.</summary>
    public static string Focus(InsertCoin.Mind.FocusTurn turn, InsertCoin.Mind.FocusPlan plan) => turn.Began switch
    {
        InsertCoin.Mind.FocusPart.Break => $"Break: {InsertCoin.Mind.Words.Count(plan.Break, "minute")}. Stand up, stretch, have some water.",
        InsertCoin.Mind.FocusPart.LongBreak => $"Long break: {InsertCoin.Mind.Words.Count(plan.LongBreak, "minute")}. Well done! Step away for a bit.",
        _ => $"Back to it: {InsertCoin.Mind.Words.Count(plan.Focus, "minute")}.",
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
        DebtPaid or StreakStar when !rules.Earns.Any => null,     // a last LeetCode answer paying with no play time: no balance to talk of
        CoinsEarned { Task.Difficulty: Difficulty.Hard } c => Pick(random, "A Hard one! Well done.", "You cracked a Hard one!") + Bonus(c, rules),
        CoinsEarned c => Pick(random, "Solved! Nice work.", "Another one done!") + Bonus(c, rules),
        StreakStar s => Pick(random, $"Day {s.Streak} of your streak!", $"That's {s.Streak} good days in a row!"),
        DebtPaid { DebtLeftMs: 0 } => "Debt cleared. Clean slate!",
        LowTime { Level: Warning.FiveMinutes } => "Five minutes left. Look for a save point?",
        LowTime { Level: Warning.OneMinute } => "One minute! Save your game.",
        CreditsOut => $"Out of time. You're playing on credit now, at x{rules.DebtMultiplier.ToString("0.#", CultureInfo.InvariantCulture)}.",
        SessionStarted when !rules.Earns.Any => "Have fun!",
        SessionStarted => state.TimeLeftMs(rules) > 0 ? $"Have fun! You've got {Span(state.TimeLeftMs(rules))}." : "No coins... this one goes on your tab.",
        SessionEnded end => $"Good game! {Span(end.Receipt.PlayedMs)} played.",
        LateNight l => $"It's past your bedtime. Play costs x{l.Multiplier.ToString("0.#", CultureInfo.InvariantCulture)} now.",
        _ => null,
    };

    private static string Bonus(CoinsEarned c, Rules rules) => c.StarToken && rules.Earns.Any ? $" Day {c.Streak} of your streak!" : "";

    private static int Urgency(LedgerEvent e) => e switch
    {
        LowTime or CreditsOut => 5,
        DebtPaid or LateNight => 4,
        CoinsEarned or StreakStar => 3,
        SessionStarted or SessionEnded => 2,
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
