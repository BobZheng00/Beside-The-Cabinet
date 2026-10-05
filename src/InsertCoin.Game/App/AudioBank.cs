using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Plays the generated effects (tools/sfx). Buses so cute sounds and must-hear alerts have separate volumes: SFX
/// (coins, buttons), Alerts (low time, credits out, IOUs), and Chimes (a reminder the player asked to hear even with
/// the cabinet's sounds muted). Muting mutes SFX and Alerts; her voice mutes itself.
/// </summary>
public partial class AudioBank : Node
{
    private const string Sfx = "SFX", Alerts = "Alerts", Chimes = "Chimes";
    private static readonly string[] Names =
    [
        "coin", "jackpot", "token", "paid", "cashbox", "warn5", "warn1", "continue_tick", "credits_out", "printer",
        "game_on", "game_over", "check", "key", "error", "hello", "remind",
    ];

    private readonly Dictionary<string, AudioStream> _streams = [];

    public void Build(bool muted)
    {
        foreach (var bus in new[] { Sfx, Alerts, Chimes })
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
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(Chimes), -4);
        foreach (var name in Names)
            _streams[name] = GD.Load<AudioStream>($"res://sounds/{name}.wav");
        Muted = muted;
    }

    public bool Muted
    {
        get => AudioServer.IsBusMute(AudioServer.GetBusIndex(Sfx));
        set
        {
            AudioServer.SetBusMute(AudioServer.GetBusIndex(Sfx), value);
            AudioServer.SetBusMute(AudioServer.GetBusIndex(Alerts), value);
        }
    }

    /// <summary>The reminder chime; heard even with the cabinet's sounds muted when <paramref name="evenMuted"/>.</summary>
    public void Chime(bool evenMuted) => Start("remind", evenMuted ? Chimes : Alerts, 1f, 0);

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
