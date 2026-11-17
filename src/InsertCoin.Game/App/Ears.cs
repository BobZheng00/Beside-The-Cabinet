using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The microphone, open only while she listens: opened as a call opens it (see <see cref="Microphone"/>), so Windows
/// can take her own voice and the room's noise out, and what was said comes back at 16 kHz for <see cref="Hearing"/>.
/// Nothing is kept once it is heard. Listening hands-free, the voice detector reads it as it comes in
/// (<see cref="Fresh"/>), and while she waits for the player to start, all but the last moment is let go
/// (<see cref="Keep"/>).
/// </summary>
public partial class Ears : Node
{
    /// <summary>The longest she listens at a time; SenseVoice hears up to about half a minute well.</summary>
    public const double Longest = 30;

    private Microphone? _microphone;
    private readonly List<float> _said = [];
    private int _fresh;                         // how much of _said the voice detector has read
    private bool _pretend;

    /// <summary>She has listened as long as she will: stop and hear it.</summary>
    public event Action? Full;

    public bool Listening { get; private set; }
    /// <summary>Dev: listening without the microphone, to what <see cref="Push"/> gives it.</summary>
    public bool Pretending => Listening && _pretend;
    /// <summary>How loud it is now, 0 to 1, for the slip's meter.</summary>
    public float Level { get; private set; }
    /// <summary>The loudest moment since she began listening: 0 means the microphone gave pure silence (blocked, or none).</summary>
    public float Peak { get; private set; }
    public double Seconds => _said.Count / (double)Hearing.SampleRate;

    /// <summary>Opens the microphone and starts keeping what it hears.</summary>
    public void Start()
    {
        if (Listening)
            return;
        if (Companion.Paper.OffScreen)
        {
            GD.Print("Ears: a test run never opens the microphone; she hears silence, or a recording");
            Pretend();
            return;
        }
        Begin();
        try
        {
            _microphone = new Microphone();
        }
        catch (Exception e)
        {
            // she hears nothing, and says so as for a microphone that isn't allowed
            GD.PrintErr($"Ears: the microphone didn't open ({e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")})");
        }
    }

    /// <summary>Dev: listens without opening the microphone; what it hears is only what <see cref="Push"/> gives it (16 kHz).</summary>
    public void Pretend()
    {
        if (Listening)
            return;
        Begin();
        _pretend = true;
    }

    /// <summary>Dev: sound heard as if through the microphone, while pretending.</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        if (_pretend)
            Add(samples);
    }

    private void Begin()
    {
        _said.Clear();
        _fresh = 0;
        Level = Peak = 0;
        Listening = true;
    }

    /// <summary>Closes the microphone: what was said, at 16 kHz (empty if it wasn't listening).</summary>
    public float[] Stop()
    {
        if (!Listening)
            return [];
        Take();
        Close();
        _pretend = false;
        Listening = false;
        Level = 0;
        var said = _said.ToArray();
        _said.Clear();
        return said;
    }

    /// <summary>What it has heard since this was last asked, at 16 kHz: for the voice detector.</summary>
    public float[] Fresh()
    {
        Take();
        var fresh = _said[_fresh..].ToArray();
        _fresh = _said.Count;
        return fresh;
    }

    /// <summary>The last <paramref name="seconds"/> it heard, at 16 kHz: for the end-of-turn model.</summary>
    public float[] Last(double seconds) => _said[^Math.Min(_said.Count, (int)(seconds * Hearing.SampleRate))..].ToArray();

    /// <summary>Lets go of all but the last <paramref name="seconds"/>: nothing is kept from before the player starts.</summary>
    public void Keep(double seconds)
    {
        var drop = _said.Count - (int)(seconds * Hearing.SampleRate);
        if (drop <= 0)
            return;
        _said.RemoveRange(0, drop);
        _fresh = Math.Max(0, _fresh - drop);
    }

    public override void _Process(double delta)
    {
        if (Listening)
            Take();
    }

    private void Take()
    {
        if (_microphone is null)
            return;
        try
        {
            Add(_microphone.Read());
        }
        catch (Exception e)
        {
            // unplugged while open: she hears nothing more, as for a microphone that gives no sound
            GD.PrintErr($"Ears: the microphone stopped ({e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")})");
            Close();
        }
    }

    private void Close()
    {
        _microphone?.Dispose();
        _microphone = null;
    }

    private void Add(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return;
        var sum = 0f;
        foreach (var s in samples)
        {
            _said.Add(s);
            sum += s * s;
            Peak = Math.Max(Peak, Math.Abs(s));
        }
        Level = Math.Clamp(MathF.Sqrt(sum / samples.Length) * 10, 0, 1);    // speech at a desk microphone: about 0.02–0.1
        if (Seconds >= Longest)
            Full?.Invoke();
    }

    public override void _ExitTree() => Close();

    /// <summary>Dev: <paramref name="words"/> in Windows' own speech synthesiser's voice, as a WAV file: speech for a test run with no recording at hand.</summary>
    public static byte[] Synthesised(string words)
    {
        using var synthesiser = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        using var speech = synthesiser.SynthesizeTextToStreamAsync(words).AsTask().GetAwaiter().GetResult();
        using var wav = new MemoryStream();
        speech.AsStreamForRead().CopyTo(wav);
        return wav.ToArray();
    }
}
