namespace InsertCoin.Mind;

/// <summary>
/// When she nods while the player talks to her: in their pauses, as listeners do. Listeners nod a few times a minute,
/// mostly where the speaker pauses after a stretch of speech, and not at every pause. Nods that follow what the
/// speaker does read as listening; the same nods at random moments do not (Gratch and others, 2007).
/// </summary>
/// <remarks>
/// Speech and pause are told apart by loudness alone, against the microphone's background, which is learnt as it
/// goes. A voice detector (Silero) would do better in a noisy room; this needs nothing more than the level the
/// microphone already reports.
/// </remarks>
public sealed class Backchannel(Random? random = null)
{
    private const double Pause = 0.3;           // seconds of quiet that make a pause rather than a gap between words
    private const double Stretch = 1.2;         // seconds of speech since the last pause before a nod is due
    private const double Apart = 2.5;           // seconds at least from one nod to the next
    private const double Chance = 0.7;          // a nod at most pauses, not all

    private readonly Random _random = random ?? Random.Shared;
    private float _background = -1;
    private double _speech, _quiet, _sinceNod = Apart;

    /// <summary>
    /// The microphone's loudness (0–1, as the cabinet's ears report it) over the last <paramref name="seconds"/>;
    /// true when she should nod now.
    /// </summary>
    public bool Hear(float level, double seconds)
    {
        _sinceNod += seconds;
        var loud = _background >= 0 && level > Math.Max(0.1f, 3 * _background);
        // the background: down at once to anything quieter; up quickly while quiet, slowly while someone talks
        _background = _background < 0 || level < _background ? level
                    : _background + (level - _background) * (float)Math.Min(1, seconds * (loud ? 0.01 : 0.5));
        if (loud)
        {
            _speech += seconds;
            _quiet = 0;
            return false;
        }
        var before = _quiet;
        _quiet += seconds;
        if (before >= Pause || _quiet < Pause)
            return false;
        // a pause has just begun: one chance at a nod
        var nod = _speech >= Stretch && _sinceNod >= Apart && _random.NextDouble() < Chance;
        _speech = 0;
        if (nod)
            _sinceNod = 0;
        return nod;
    }
}
