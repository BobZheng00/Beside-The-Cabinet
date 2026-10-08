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
        lines.Add(left > 0
            ? $"Play time banked: {Words.Span(left)}. Each night at {Clock(rules.DayStartsAt)}, {Share(rules.NightlyFade)} of what isn't played fades."
            : "Play time banked: none.");
        lines.Add(s.DebtMs > 0 ? $"Owed: {Words.Span(s.DebtMs)} ({Words.Count(s.IouTickets(rules), "IOU ticket")})." : "Owed: nothing.");

        lines.Add($"Focus pays {Words.Span(rules.TokenMs)} of play for every {Words.Span(rules.TokenMs * rules.FocusPerPlay)} focused.");
        if (s.Today is { } today)
        {
            lines.Add(today.RoomMs() > 0
                ? $"Today ({today.Day.DayOfWeek}): {Earned(today.EarnedMs)} of the day's cap of {Words.Span(today.CapMs)}; {Words.Span(today.RoomMs())} more can be earned."
                : $"Today ({today.Day.DayOfWeek}): the day's cap of {Words.Span(today.CapMs)} is reached; nothing more is paid until {Clock(rules.DayStartsAt)}.");
            var done = new List<string>();
            if (today.Solves > 0)
                done.Add($"{Words.Count(today.Solves, "problem")} solved");
            if (today.FocusedMs >= 60_000)
                done.Add($"{Words.Span(today.FocusedMs)} focused");
            if (today.PlayedMs >= 60_000)
                done.Add($"{Words.Span(today.PlayedMs)} played");
            if (done.Count > 0)
                lines.Add($"So far today: {string.Join(", ", done)}.");
            if (today.Bedtime is { } bedtime)
                lines.Add(GameCalendar.IsLateNight(s, now, rules)
                    ? $"Bedtime: {Clock(bedtime)}, and it is past it: play costs x{Times(rules.LateNightMultiplier)} until {Clock(rules.DayStartsAt)}."
                    : $"Bedtime: {Clock(bedtime)}; play after it costs x{Times(rules.LateNightMultiplier)} until {Clock(rules.DayStartsAt)}.");
        }

        var streak = GameCalendar.CurrentStreak(s, now, rules);
        lines.Add($"Daily solve streak: {Words.Count(streak, "day")} (best {s.BestStreak}).");
        lines.Add($"This week: {Words.Count(s.Week.Solves, "problem")} solved, " +
                  (s.Week.FocusedMs >= 60_000 ? $"{Words.Span(s.Week.FocusedMs)} focused, " : "no focus sessions, ") +
                  $"{Earned(s.Week.EarnedMs)}, " +
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
                         $"+{Words.Count(c.Tokens, "token")}" + (c.StarToken ? $", with a day-{c.Streak} streak bonus" : "") + Capped(c.Paid) + ".",
        FocusPaid f => $"A focus part ended: {Words.Span(f.FocusedMs)} focused, worth {Words.Span(f.Paid.EarnedMs)} of play" + Capped(f.Paid) + ".",
        DebtPaid { DebtLeftMs: 0 } => "That paid off all the debt.",
        DebtPaid p => $"That paid off {Words.Span(p.PaidMs)} of debt; {Words.Span(p.DebtLeftMs)} still owed.",
        CapReached r => $"That reached today's cap of {Words.Span(r.CapMs)}: nothing more is paid until {Clock(rules.DayStartsAt)}.",
        LowTime l => $"{Words.Span(l.TimeLeftMs)} of play time left.",
        CreditsOut => $"Play time ran out; the game goes on, on credit at x{Times(rules.DebtMultiplier)}.",
        IouPrinted i => $"IOU ticket #{i.TicketNumber} printed.",
        SessionStarted started => $"The player started playing {string.Join(" + ", started.GameNames)}.",
        SessionEnded ended => $"The player stopped playing after {Words.Span(ended.Receipt.PlayedMs)}" +
                              (ended.Receipt.IousPrinted > 0 ? $"; {Words.Count(ended.Receipt.IousPrinted, "IOU ticket")} printed." : "."),
        LateNight l => $"Play went past the player's bedtime ({Clock(l.Bedtime)}): it costs x{Times(l.Multiplier)} until {Clock(rules.DayStartsAt)}.",
        Faded f => $"A new day: overnight {Words.Span(f.FadedMs)} of unplayed time faded, {Words.Span(f.LeftMs)} left.",
        _ => null,
    };

    /// <summary>What today's cap did to an earning, if anything.</summary>
    private static string Capped(Payout paid) => paid.OverCapMs <= 0 ? ""
        : paid.CreditedMs > 0 ? $", but today's cap let only {Words.Span(paid.CreditedMs)} of it count"
        : ", but today's cap was already reached, so it paid nothing";

    private static string Earned(long ms) => ms >= 60_000 ? $"{Words.Span(ms)} earned" : "nothing earned yet";

    private static string Clock(TimeSpan time) => $"{time.Hours:00}:{time.Minutes:00}";

    private static string Times(double multiplier) => multiplier.ToString("0.##", English);

    private static string Share(double share) => share == 0.5 ? "half" : $"{(share * 100).ToString("0", English)}%";
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
