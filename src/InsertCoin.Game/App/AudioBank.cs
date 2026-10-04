using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Plays the generated effects (tools/sfx). Two buses so cute sounds and must-hear alerts have separate volumes:
/// SFX (coins, buttons) and Alerts (low time, credits out, IOUs).
/// </summary>
public partial class AudioBank : Node
{
    private const string Sfx = "SFX", Alerts = "Alerts";
    private static readonly string[] Names =
    [
        "coin", "jackpot", "token", "paid", "cashbox", "warn5", "warn1", "continue_tick", "credits_out", "printer",
        "game_on", "game_over", "check", "key", "error", "hello", "remind",
    ];

    private readonly Dictionary<string, AudioStream> _streams = [];

    public void Build(bool muted)
    {
        foreach (var bus in new[] { Sfx, Alerts })
        {
            if (AudioServer.GetBusIndex(bus) >= 0)
                continue;
            AudioServer.AddBus();
            var index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, bus);
            AudioServer.SetBusSend(index, "Master");
        }
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(Sfx), -9);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(Alerts), -4);
        foreach (var name in Names)
            _streams[name] = GD.Load<AudioStream>($"res://sounds/{name}.wav");
        Muted = muted;
    }

    public bool Muted
    {
        get => AudioServer.IsBusMute(0);
        set => AudioServer.SetBusMute(0, value);
    }

    public void Play(string name, float pitch = 1f, double delay = 0) => Start(name, Sfx, pitch, delay);

    public void Alert(string name, double delay = 0) => Start(name, Alerts, 1f, delay);

    private void Start(string name, string bus, float pitch, double delay)
    {
        if (delay > 0)
        {
            GetTree().CreateTimer(delay).Timeout += () => Start(name, bus, pitch, 0);
            return;
        }
        var player = new AudioStreamPlayer { Stream = _streams[name], Bus = bus, PitchScale = pitch };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }
}
