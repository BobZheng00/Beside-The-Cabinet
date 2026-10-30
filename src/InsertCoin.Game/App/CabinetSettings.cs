using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Cabinet: how the cabinet sits on the desktop (its size, above other windows or not, starting with
/// Windows), its sounds, tickets while a game runs, and saving energy. Each change is used at once.
/// </summary>
public partial class CabinetSettings : VBoxContainer
{
    private static readonly (string Size, string Label)[] Sizes = [("S", "Small"), ("M", "Medium"), ("L", "Large")];

    /// <summary>The size chosen: S, M or L.</summary>
    public event Action<string>? Sized;
    public event Action<bool>? OnTop, WithWindows, Muted, Tickets, SaveEnergy;

    /// <param name="withWindows">Whether it starts with Windows now (kept by Windows, not in the settings).</param>
    public void Build(SettingsFile settings, bool withWindows)
    {
        var now = settings.Now;
        Form.Page(this, "Cabinet");
        Form.Choice(Form.Grid(this), "Size", Sizes.Select(s => s.Label), Array.FindIndex(Sizes, s => s.Size == now.Size), index => Sized?.Invoke(Sizes[index].Size));
        Form.Hint(this, "Drag the cabinet by its body to move it anywhere on your desktop.");
        Form.Tick(this, "Always on top", now.AlwaysOnTop, on => OnTop?.Invoke(on));
        Form.Hint(this, "Above other windows, so it is always in sight.");
        Form.Tick(this, "Start with Windows", withWindows, on => WithWindows?.Invoke(on));
        Form.Hint(this, "Opens by itself when you sign in to Windows.");

        Form.Subheading(this, "Sounds and tickets");
        var muted = Form.Tick(this, "Mute sounds", now.Muted, on => Muted?.Invoke(on));
        void Sync() => muted.SetPressedNoSignal(settings.Now.Muted);        // the tray's Mute flipped meanwhile
        settings.Changed += Sync;
        TreeExiting += () => settings.Changed -= Sync;
        Form.Hint(this, "The cabinet's sounds and your companion's voice. A reminder's chime can still ring (see Reminders).");
        Form.Tick(this, "Tickets while you play", now.InGameTickets, on => Tickets?.Invoke(on));
        var tickets = Form.Hint(this, "");
        void Said() => tickets.Text = settings.Now.Earning.Any         // as what earns play time changes on Balance
            ? "A ticket at the corner of the screen during a game: five minutes left, one minute left, out of time, time owed, past your bedtime."
            : "With no play time there are none: the cabinet keeps no time for your games.";
        Said();
        settings.Changed += Said;
        TreeExiting += () => settings.Changed -= Said;

        Form.Subheading(this, "Energy");
        Form.Tick(this, "Energy saver", now.EnergySaver, on => SaveEnergy?.Invoke(on));
        Form.Hint(this, "The cabinet draws at 60 frames a second, and at 15 while a game runs. With this, at 15 all the time; moments worth " +
                        "watching still get a few seconds at full pace.");
    }
}
