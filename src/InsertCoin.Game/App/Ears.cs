using System.Runtime.InteropServices;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The microphone, open only while she listens. Godot records it into a muted bus of its own (nothing is played
/// back), and what was said comes back at 16 kHz for <see cref="Hearing"/>. Nothing is kept once it is heard.
/// Listening hands-free, the voice detector reads it as it comes in (<see cref="Fresh"/>), and while she waits for the
/// player to start, all but the last moment is let go (<see cref="Keep"/>).
/// </summary>
public partial class Ears : Node
{
    private const string Bus = "Ears";
    /// <summary>The longest she listens at a time; SenseVoice hears up to about half a minute well.</summary>
    public const double Longest = 30;

    private AudioStreamPlayer _player = null!;
    private AudioEffectCapture _capture = null!;
    private readonly List<float> _said = [];
    private int _rate, _fresh;                  // _fresh: how much of _said the voice detector has read
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
    public double Seconds => _rate > 0 ? _said.Count / (double)_rate : 0;

    public override void _Ready()
    {
        var index = AudioServer.GetBusIndex(Bus);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, Bus);
            AudioServer.SetBusMute(index, true);                  // heard, never played back
            AudioServer.AddBusEffect(index, new AudioEffectCapture { BufferLength = 0.5f });
        }
        _capture = (AudioEffectCapture)AudioServer.GetBusEffect(index, 0);
        _player = new AudioStreamPlayer { Stream = new AudioStreamMicrophone(), Bus = Bus };
        AddChild(_player);
    }

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
        Begin((int)AudioServer.GetMixRate());
        _capture.ClearBuffer();
        _player.Play();
    }

    /// <summary>Dev: listens without opening the microphone; what it hears is only what <see cref="Push"/> gives it (16 kHz).</summary>
    public void Pretend()
    {
        if (Listening)
            return;
        Begin(Hearing.SampleRate);
        _pretend = true;
    }

    /// <summary>Dev: sound heard as if through the microphone, while pretending.</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        if (_pretend)
            Add(samples);
    }

    private void Begin(int rate)
    {
        _said.Clear();
        _fresh = 0;
        Level = Peak = 0;
        _rate = rate;
        Listening = true;
    }

    /// <summary>Closes the microphone: what was said, at 16 kHz (empty if it wasn't listening).</summary>
    public float[] Stop()
    {
        if (!Listening)
            return [];
        Take();
        if (!_pretend)
            _player.Stop();
        _pretend = false;
        Listening = false;
        Level = 0;
        var said = Hearing.To16k(CollectionsMarshal.AsSpan(_said), _rate);
        _said.Clear();
        return said;
    }

    /// <summary>What it has heard since this was last asked, at 16 kHz: for the voice detector.</summary>
    public float[] Fresh()
    {
        Take();
        var fresh = Hearing.To16k(CollectionsMarshal.AsSpan(_said)[_fresh..], _rate);
        _fresh = _said.Count;
        return fresh;
    }

    /// <summary>The last <paramref name="seconds"/> it heard, at 16 kHz: for the end-of-turn model.</summary>
    public float[] Last(double seconds)
    {
        var count = Math.Min(_said.Count, (int)(seconds * _rate));
        return Hearing.To16k(CollectionsMarshal.AsSpan(_said)[^count..], _rate);
    }

    /// <summary>Lets go of all but the last <paramref name="seconds"/>: nothing is kept from before the player starts.</summary>
    public void Keep(double seconds)
    {
        var drop = _said.Count - (int)(seconds * _rate);
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
        if (_pretend)
            return;
        var frames = _capture.GetFramesAvailable();
        if (frames == 0)
            return;
        var buffer = _capture.GetBuffer(frames);
        var samples = new float[buffer.Length];
        for (var i = 0; i < buffer.Length; i++)
            samples[i] = (buffer[i].X + buffer[i].Y) / 2;
        Add(samples);
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

    public override void _ExitTree()
    {
        if (Listening && !_pretend)
            _player.Stop();
    }
}
