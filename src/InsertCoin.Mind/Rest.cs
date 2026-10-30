using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Mind;

/// <summary>A word about rest she gives at its moment.</summary>
public enum Nudge { WindDown, Bedtime, StillUp, LongPlay }

/// <summary>
/// Rest and sleep (the balance spec, "Rest and sleep"). Around the player's bedtime: a word to wind down half an hour
/// before it, time to sleep at it, once more half an hour after it, then quiet. After two hours in one game session, a
/// break. And, at most once a day, what the record of days shows: nights going on late, or days without play while
/// time is banked. The bedtime is the player's own; the record only shows what is, and she never takes a late pattern
/// as the target. What she is told says what to get across; how she says it is her character's.
/// </summary>
public static class Rest
{
    public static readonly TimeSpan Before = TimeSpan.FromMinutes(30), After = TimeSpan.FromMinutes(30), LongPlay = TimeSpan.FromHours(2);

    /// <summary>
    /// When the player's usual night ends this far past their bedtime, she suggests a step towards it rather than the
    /// whole way at once: in bed <see cref="Step"/> earlier than usual for a few nights, then earlier again. Sleep
    /// times move best in small steps; the bedtime they set stays theirs.
    /// </summary>
    public static readonly TimeSpan Drift = TimeSpan.FromMinutes(45), Step = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1), LastWord = TimeSpan.FromHours(1);
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>
    /// The word about bedtime due now: to wind down from half an hour before it, time to sleep from it, once more from
    /// half an hour after it (for an hour), each once a night; null when none is.
    /// </summary>
    public static Nudge? AtBedtime(TimeSpan bedtime, DateTimeOffset now, IReadOnlySet<Nudge> given, TimeSpan dayStartsAt)
    {
        var into = Into(now.TimeOfDay, dayStartsAt);
        var bed = Into(bedtime, dayStartsAt);
        Nudge? due = into >= bed + After && into < bed + After + LastWord ? Nudge.StillUp
            : into >= bed && into < bed + After ? Nudge.Bedtime
            : into >= bed - Before && into < bed ? Nudge.WindDown
            : null;
        return due is { } nudge && !given.Contains(nudge) ? nudge : null;
    }

    /// <summary>A break is due after two hours of play in one session, and two hours after the last one.</summary>
    /// <param name="lastBreakAtMs">The session's play time when she last suggested one, or null.</param>
    public static bool BreakDue(long playedMs, long? lastBreakAtMs) =>
        playedMs >= LongPlay.TotalMilliseconds && (lastBreakAtMs is not { } last || playedMs - last >= LongPlay.TotalMilliseconds);

    /// <summary>What she is told for a nudge, to say in her own words.</summary>
    /// <param name="latePrice">What play costs after bedtime, or null with no play time (then no price is named).</param>
    public static string Note(Nudge nudge, TimeSpan bedtime, double? latePrice) => nudge switch
    {
        Nudge.WindDown => $"It's half an hour to the player's bedtime ({Clock(bedtime)}). Give them a word to start winding down. Briefly, and no lecture.",
        Nudge.Bedtime => $"It's the player's bedtime ({Clock(bedtime)}): time to sleep. Say so briefly" +
                         (latePrice is { } price ? $"; from now until 04:00 play costs x{price.ToString("0.#", English)}" : "") + ". No lecture.",
        Nudge.StillUp => $"It's half an hour past the player's bedtime ({Clock(bedtime)}) and they're still up. Say it once more, briefly and without lecturing: you won't again tonight.",
        _ => "The player has been in a game for two hours. Suggest a break: rest their eyes, stretch, some water. Briefly, and no lecture.",
    };

    /// <summary>
    /// What she might notice in the record of days, to mention once: nights going on half an hour past bedtime (past
    /// one in the morning without one) on three of the last four; or three days without play while at least half an
    /// hour is banked (rest counts too; never with no play time, which passes nothing banked). Null when there is nothing.
    /// </summary>
    public static string? Notice(IReadOnlyList<DayRecord> days, TimeSpan? bedtime, long bankedMs, TimeSpan dayStartsAt)
    {
        var line = bedtime ?? TimeSpan.FromHours(1);
        var nights = days.TakeLast(4).ToList();
        var late = nights.Where(d => d.LastActive is { } last && Into(last.TimeOfDay, dayStartsAt) > Into(line, dayStartsAt) + After).ToList();
        if (nights.Count >= 3 && late.Count >= 3)
        {
            var latest = late.Max(d => Into(d.LastActive!.Value.TimeOfDay, dayStartsAt));
            return $"From the record of days: the player was still at the PC past {(bedtime is null ? "one in the morning" : $"their bedtime ({Clock(line)})")} " +
                   $"on {late.Count} of the last {nights.Count} nights, once until about {Clock(latest + dayStartsAt)}. If it fits, mention it once, briefly " +
                   "and without lecturing; the bedtime is theirs to keep." + StepTowards(days, bedtime, dayStartsAt);
        }
        var quiet = days.TakeLast(3).ToList();
        if (quiet.Count == 3 && quiet.All(d => d.PlayedMs < 60_000) && bankedMs >= 30 * 60_000)
            return $"From the record of days: the player hasn't played for three days, with {Words.Span(bankedMs)} banked (half of it fades each night). " +
                   "Rest counts too: you might suggest they enjoy some of it.";
        return null;
    }

    /// <summary>
    /// A step towards the player's bedtime, when their nights usually end <see cref="Drift"/> or more past it: in bed
    /// <see cref="Step"/> earlier than usual (never earlier than the bedtime itself), as a sentence for the notice; empty
    /// otherwise.
    /// </summary>
    private static string StepTowards(IReadOnlyList<DayRecord> days, TimeSpan? bedtime, TimeSpan dayStartsAt)
    {
        if (bedtime is not { } goal || Usual(days, dayStartsAt) is not { } usual || Into(usual, dayStartsAt) - Into(goal, dayStartsAt) < Drift)
            return "";
        var step = Into(usual, dayStartsAt) - Step;
        var by = (step < Into(goal, dayStartsAt) ? goal : step + dayStartsAt);
        return $" Their nights usually end around {Clock(usual)}: rather than the whole way at once, you might suggest a step, in bed by about " +
               $"{Clock(TimeSpan.FromTicks(by.Ticks % Day.Ticks))} for the next few nights, then earlier again once that holds.";
    }

    /// <summary>
    /// When the player's nights usually end, from the record of days: the middle of their last times at the PC on the
    /// evenings of the latest two weeks, to the quarter hour; null with fewer than five such evenings.
    /// </summary>
    public static TimeSpan? Usual(IReadOnlyList<DayRecord> days, TimeSpan dayStartsAt)
    {
        var ends = days.TakeLast(14)
                       .Select(d => d.LastActive is { } last ? Into(last.TimeOfDay, dayStartsAt) : TimeSpan.Zero)
                       .Where(into => into >= TimeSpan.FromHours(14))       // from 18:00 on
                       .Order()
                       .ToList();
        if (ends.Count < 5)
            return null;
        var middle = TimeSpan.FromMinutes(Math.Round(ends[ends.Count / 2].TotalMinutes / 15) * 15);
        return TimeSpan.FromTicks((middle + dayStartsAt).Ticks % Day.Ticks);
    }

    /// <summary>
    /// What she is told when the player has no bedtime: to ask for one, once, with their usual time if the record shows
    /// one; with <paramref name="price"/> (play time on), that play after it costs more.
    /// </summary>
    public static string AskBedtime(TimeSpan? usual, bool price = true) => usual is { } time
        ? $"The player has no bedtime set. From the record of days, their nights usually end around {Clock(time)}. Ask them once, briefly, what " +
          "time they'd like to be asleep by (around then or a little earlier, if they'd like a suggestion); if they say, use set_bedtime."
        : "The player has no bedtime set. Ask them once, briefly, what time they'd like to be asleep by; if they say, use set_bedtime. Half an " +
          "hour before it you give a word to wind down" + (price ? ", and play after it costs more." : ".");

    /// <summary>A bedtime as the player gave it, "23:30" or "0:30": from 20:00 to 03:30, or null.</summary>
    public static TimeSpan? ParseBedtime(string? text) =>
        TimeOnly.TryParse(text?.Trim(), English, DateTimeStyles.None, out var time) && (time.Hour >= 20 || time.Hour < 3 || time is { Hour: 3, Minute: <= 30 })
            ? time.ToTimeSpan() : null;

    public static string Clock(TimeSpan time) => $"{time.Hours:00}:{time.Minutes:00}";

    /// <summary>How far into the game day a time of day is.</summary>
    private static TimeSpan Into(TimeSpan time, TimeSpan dayStartsAt) => TimeSpan.FromTicks(((time - dayStartsAt).Ticks % Day.Ticks + Day.Ticks) % Day.Ticks);
}
