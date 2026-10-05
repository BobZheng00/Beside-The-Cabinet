namespace InsertCoin.Mind;

/// <summary>
/// One hands-free turn: whether the player starts talking while she listens, and when they have finished. It goes by
/// the voice detector's word on each moment of sound (Silero) and, at each pause, the end-of-turn model's (Smart
/// Turn): a quarter second of quiet asks the model; if it says they have finished, the turn ends there, and if not
/// she waits on, up to <see cref="Quiet"/> of quiet (docs/research/2026-10-05-companion-agent.md §2).
/// </summary>
/// <param name="window">How long she waits for them to start, in seconds.</param>
public sealed class TurnTaking(double window)
{
    public const double Starting = 0.25;        // seconds of speech that start a turn: a cough or a click is shorter
    public const double Pause = 0.25;           // seconds of quiet that ask the end-of-turn model
    public const double Quiet = 2.0;            // seconds of quiet that end a turn, whatever the model said
    public const double Longest = 30;           // a turn ends at this length: her ears hear about this much well

    public enum Heard
    {
        Nothing,
        /// <summary>The player has started talking.</summary>
        Started,
        /// <summary>A pause: ask the end-of-turn model about what was said so far, and give its answer to <see cref="Verdict"/>.</summary>
        AskModel,
        /// <summary>The player has finished: hear what they said.</summary>
        Finished,
        /// <summary>The window passed and they said nothing.</summary>
        Expired,
    }

    private double _clock, _speech, _quiet, _turn;
    private bool _talking, _done;
    private int _pause;                         // which pause the model was last asked about (0: none pending)
    private bool _finished;                     // the model said this pause ends the turn

    /// <summary>Whether the player is talking now (the turn has started).</summary>
    public bool Talking => _talking && !_done;

    /// <summary>The pause the last <see cref="Heard.AskModel"/> was about, to hand back with the model's answer.</summary>
    public int Asking { get; private set; }

    /// <summary>The next <paramref name="seconds"/> of sound: whether the voice detector heard speech in it.</summary>
    public Heard Hear(bool speech, double seconds)
    {
        if (_done)
            return Heard.Nothing;
        _clock += seconds;
        if (!_talking)
        {
            _speech = speech ? _speech + seconds : 0;
            if (_speech >= Starting)
            {
                _talking = true;
                _turn = _speech;
                return Heard.Started;
            }
            return _clock >= window ? End(Heard.Expired) : Heard.Nothing;
        }

        _turn += seconds;
        if (speech)
        {
            _quiet = 0;
            _pause = 0;
            _finished = false;
        }
        else
            _quiet += seconds;
        if (_turn >= Longest || _quiet >= Quiet || _finished)
            return End(Heard.Finished);
        if (_quiet >= Pause && _pause == 0)
        {
            _pause = Asking = Asking + 1;
            return Heard.AskModel;
        }
        return Heard.Nothing;
    }

    /// <summary>The end-of-turn model's answer about pause <paramref name="asking"/>: whether the player has finished.</summary>
    public void Verdict(int asking, bool finished)
    {
        if (asking == _pause && asking != 0)
            _finished = finished;       // a pause the player has since talked on from is no longer the question
    }

    private Heard End(Heard how)
    {
        _done = true;
        return how;
    }
}
