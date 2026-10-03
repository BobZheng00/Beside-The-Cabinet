using System.Runtime.InteropServices;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The microphone, open only while she listens. Godot records it into a muted bus of its own (nothing is played
/// back), and what was said comes back at 16 kHz for <see cref="Hearing"/>. Nothing is kept once it is heard.
/// </summary>
public partial class Ears : Node
{
    private const string Bus = "Ears";
    /// <summary>The longest she listens at a time; SenseVoice hears up to about half a minute well.</summary>
    public const double Longest = 30;

    private AudioStreamPlayer _player = null!;
    private AudioEffectCapture _capture = null!;
    private readonly List<float> _said = [];
    private int _rate;

    /// <summary>She has listened as long as she will: stop and hear it.</summary>
    public event Action? Full;

    public bool Listening { get; private set; }
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
        _said.Clear();
        Level = Peak = 0;
        _rate = (int)AudioServer.GetMixRate();
        _capture.ClearBuffer();
        _player.Play();
        Listening = true;
    }

    /// <summary>Closes the microphone: what was said, at 16 kHz (empty if it wasn't listening).</summary>
    public float[] Stop()
    {
        if (!Listening)
            return [];
        Take();
        _player.Stop();
        Listening = false;
        Level = 0;
        var said = Hearing.To16k(CollectionsMarshal.AsSpan(_said), _rate);
        _said.Clear();
        return said;
    }

    public override void _Process(double delta)
    {
        if (Listening)
            Take();
    }

    private void Take()
    {
        var frames = _capture.GetFramesAvailable();
        if (frames == 0)
            return;
        var sum = 0f;
        foreach (var frame in _capture.GetBuffer(frames))
        {
            var s = (frame.X + frame.Y) / 2;
            _said.Add(s);
            sum += s * s;
            Peak = Math.Max(Peak, Math.Abs(s));
        }
        Level = Math.Clamp(MathF.Sqrt(sum / frames) * 10, 0, 1);    // speech at a desk microphone: about 0.02–0.1
        if (Seconds >= Longest)
            Full?.Invoke();
    }

    public override void _ExitTree()
    {
        if (Listening)
            _player.Stop();
    }
}
