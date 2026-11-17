namespace InsertCoin.Mind;

/// <summary>
/// Whether she can hear the player over her own voice. Windows must cancel echo on the microphone for calls (Voice
/// Clarity), and to take her voice out it must hear it: it listens to the output for calls, so her voice has to play
/// there. It plays on Windows' default output; where that isn't the output for calls, her own words would come back
/// as the player's.
/// </summary>
public static class TalkingOver
{
    /// <param name="cancelsEcho">Windows lists echo cancelling on the microphone for calls.</param>
    /// <param name="callsOutput">Windows' default output for calls (its id), if any.</param>
    /// <param name="voiceOutput">Where her voice plays: Windows' default output (its id), if any.</param>
    /// <returns>Null when she can; otherwise why not, for the log.</returns>
    public static string? WhyNot(bool cancelsEcho, string? callsOutput, string? voiceOutput) =>
        !cancelsEcho ? "the microphone for calls doesn't cancel echo"
        : string.IsNullOrEmpty(voiceOutput) || callsOutput != voiceOutput
            ? "her voice plays on another output than the one for calls, so Windows can't take it out"
        : null;
}
