using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Mind;

/// <summary>
/// The cabinet's record of a day, in her diary (phase 4, docs/specs/2026-10-11-observational-memory.md): written by code
/// from the ledger's record of the day, never by the model, so what was solved, focused, done, played and earned, and
/// when the player was at the PC, is always right. One note a day, dated at its end, in her log with her own notes.
/// </summary>
public static class Diary
{
    /// <summary>What marks a note in her log as the cabinet's record rather than hers (see <see cref="Observation.Source"/>).</summary>
    public const string Cabinet = "cabinet";

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>The day in a sentence, everything its record holds; <paramref name="good"/> when it counted towards the streak.</summary>
    public static string Entry(DayRecord day, bool good)
    {
        var parts = new List<string>();
        if (day.Solved.Count > 0)
            parts.Add($"solved {Words.Count(day.Solved.Count, "problem")}: {string.Join(", ", day.Solved)}");
        else if (day.Solves > 0)
            parts.Add($"solved {Words.Count(day.Solves, "problem")}");     // a day from before titles were kept
        if (day.Activities.Count > 0)
            parts.Add("did " + string.Join(", ", day.Activities.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Value >= 1 ? a.Key : $"{a.Key} (counted half)")));
        if (day.FocusedMs > 0)
            parts.Add($"focused {Words.Span(day.FocusedMs)}");
        if (day.PlayedMs > 0)
            parts.Add($"played {Words.Span(day.PlayedMs)}" + (day.Games.Count > 0 ? $" ({string.Join(", ", day.Games)})" : "")
                      + (day.LastPlayed is { } last ? $", the last game ending at {last.ToString("HH:mm", English)}" : ""));
        if (day.LateExtraMs > 0)
            parts.Add($"playing past bedtime cost {Words.Span(day.LateExtraMs)} extra");
        if (day.Bedtime is { } bedtime)
            parts.Add($"bedtime {bedtime.ToString(@"hh\:mm", English)}");
        if (day.EarnedMs > 0)
            parts.Add($"earned {Words.Span(day.EarnedMs)} of play" +
                      (day.OverCapMs > 0 ? $", and {Words.Span(day.OverCapMs)} more past the day's cap of {Words.Span(day.CapMs)}" : ""));
        if (day.DebtPaidMs > 0)
            parts.Add($"paid back {Words.Span(day.DebtPaidMs)} of debt");
        if (day.FadedMs > 0)
            parts.Add($"{Words.Span(day.FadedMs)} of unplayed time faded overnight as it began");
        if (day.FirstActive is { } first && day.LastActive is { } seen)
            parts.Add($"at the PC from {first.ToString("HH:mm", English)} to {seen.ToString("HH:mm", English)}");
        if (good)
            parts.Add("a good day, towards the streak");
        return parts.Count == 0 ? "The cabinet's record: not at the PC all day." : $"The cabinet's record: {string.Join("; ", parts)}.";
    }
}
