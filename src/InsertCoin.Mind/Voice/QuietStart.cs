namespace InsertCoin.Mind;

/// <summary>
/// Drops the quiet at the start of a line of her voice. GPT-SoVITS often begins a line with about a second of near-silence
/// (0.85 to 1.36 s with Hana's voice, 2026-10-09; 2.4 s once, agent note §5), which the player hears as her being slow
/// to answer. Fed the line's audio piece by piece, it passes
/// nothing until the first sound, then everything from just before it (a little lead-in, so the sound isn't clipped).
/// A line that stays quiet for <see cref="Longest"/> is passed on as it is.
/// </summary>
public sealed class QuietStart(int sampleRate, TimeSpan? leadIn = null)
{
    /// <summary>Louder than this is sound (about -34 dB of full scale); GPT-SoVITS's silence is far below it.</summary>
    public const float Loud = 0.02f;

    /// <summary>The lead-in kept before the first sound.</summary>
    public static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(60);

    /// <summary>The pause kept before a line that follows another she is saying: a breath between sentences.</summary>
    public static readonly TimeSpan Between = TimeSpan.FromMilliseconds(300);

    private readonly TimeSpan _leadIn = leadIn ?? LeadIn;

    /// <summary>Quiet longer than this isn't dropped: something else is going on.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(4);

    private bool _sounding;

    /// <summary>The quiet dropped so far, in samples.</summary>
    public int Dropped { get; private set; }

    public TimeSpan DroppedTime => TimeSpan.FromSeconds((double)Dropped / sampleRate);

    /// <summary>What of <paramref name="samples"/> to play.</summary>
    public float[] Feed(float[] samples)
    {
        if (_sounding)
            return samples;
        var first = Array.FindIndex(samples, s => MathF.Abs(s) > Loud);
        if (first < 0)
        {
            if (Dropped + samples.Length > Longest.TotalSeconds * sampleRate)
            {
                _sounding = true;                           // a long quiet is meant: let it be
                return samples;
            }
            Dropped += samples.Length;
            return [];
        }
        _sounding = true;
        var from = Math.Max(0, first - (int)(_leadIn.TotalSeconds * sampleRate));
        Dropped += from;
        return from == 0 ? samples : samples[from..];
    }

    /// <summary>A whole clip without its quiet at both ends (a thinking sound), keeping a short lead-in and tail.</summary>
    public static float[] Trim(float[] samples, int sampleRate)
    {
        var edge = (int)(LeadIn.TotalSeconds * sampleRate);
        var first = Array.FindIndex(samples, s => MathF.Abs(s) > Loud);
        if (first < 0)
            return [];
        var last = Array.FindLastIndex(samples, s => MathF.Abs(s) > Loud);
        var from = Math.Max(0, first - edge);
        var to = Math.Min(samples.Length, last + 1 + edge * 2);
        return samples[from..to];
    }
}
