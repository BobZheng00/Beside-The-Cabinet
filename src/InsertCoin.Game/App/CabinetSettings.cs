using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → General: the name the player is called by and the language their companion replies in; how the cabinet
/// sits on the desktop (its size, above other windows or not, starting with Windows), its sounds, tickets while a game
/// runs, and saving energy; Help and About at the end. Each change is used at once.
/// </summary>
public partial class CabinetSettings : VBoxContainer
{
    private static readonly (string Size, string Label)[] Sizes = [("S", "Small"), ("M", "Medium"), ("L", "Large")];
    /// <summary>The languages to choose from: Auto (the player's own), and those a built-in character has a card in.</summary>
    private static readonly (string? Code, string Label)[] Languages = [(null, "Auto"), ("en", "English"), ("zh", "中文")];

    /// <summary>The name they call the player by, as it is typed.</summary>
    public event Action<string>? Named;
    /// <summary>The language they reply in, with their card in it if they have one ("en", "zh"), or null for the player's own (Auto).</summary>
    public event Action<string?>? LanguageChosen;
    /// <summary>The size chosen: S, M or L.</summary>
    public event Action<string>? Sized;
    public event Action<bool>? OnTop, WithWindows, Muted, Tickets, SaveEnergy;

    /// <param name="withWindows">Whether it starts with Windows now (kept by Windows, not in the settings).</param>
    public void Build(SettingsFile settings, bool withWindows)
    {
        var now = settings.Now;
        Form.Page(this);
        var grid = Form.Grid(this);
        Form.Field(grid, "Your name", now.CallMe, "what the characters call you", typed: text => Named?.Invoke(text));
        Form.Choice(grid, "Language", Languages.Select(l => l.Label), Math.Max(0, Array.FindIndex(Languages, l => l.Code == now.Language)),
                    index => LanguageChosen?.Invoke(Languages[index].Code));
        Form.Hint(this, "Auto follows the language you write in. Chosen, they reply in it whatever you write, and use their own card in it if they have one.");

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
        Form.Hint(this, "The cabinet's sounds and your companion's voice. A reminder's chime can still ring.");
        Form.Link(this, "Reminders", "reminders");
        Form.Tick(this, "Tickets while you play", now.InGameTickets, on => Tickets?.Invoke(on));
        var tickets = Form.Hint(this, "");
        void Said() => tickets.Text = settings.Now.Earning.Any         // as what earns play time changes on Play time
            ? "A ticket at the corner of the screen during a game: five minutes left, one minute left, out of time, time owed, past your bedtime."
            : "With no play time there are none: nothing counts down or is owed.";
        Said();
        settings.Changed += Said;
        TreeExiting += () => settings.Changed -= Said;

        Form.Subheading(this, "Energy");
        Form.Tick(this, "Energy saver", now.EnergySaver, on => SaveEnergy?.Invoke(on));
        Form.Hint(this, "The cabinet draws at 60 frames a second, and at 15 while a game runs. With this, at 15 all the time; moments worth " +
                        "watching still get a few seconds at full pace.");

        HelpAndAbout();
    }

    private const string Project = "https://github.com/BobZheng00/Beside-The-Cabinet";

    /// <summary>Help opens the manual in the browser; About says what the app is, which version, and under what licence.</summary>
    private void HelpAndAbout()
    {
        Form.Subheading(this, "Help and about");
        var row = Form.Buttons(this);
        Form.Button(row, "Help", () => Form.Browse(Project + "/blob/main/docs/README.md"));
        var about = new VBoxContainer { Visible = false };
        about.AddThemeConstantOverride("separation", 6);
        Form.Button(row, "About", () =>
        {
            about.Visible = !about.Visible;
            if (about.Visible)
                Form.Bring(about);
        });
        AddChild(about);
        // an export's own version (export_presets.cfg); run from source, there is none
        var version = OS.HasFeature("template") ? System.Diagnostics.FileVersionInfo.GetVersionInfo(OS.GetExecutablePath()).ProductVersion : null;
        Form.Hint(about, $"Beside the Cabinet{(version is null ? ", run from source" : $" {version}")}: a companion beside a little arcade cabinet, " +
                         "and a balance of work, life and games.");
        Form.Hint(about, "Free for non-commercial use (PolyForm Noncommercial 1.0.0). The work of others it includes is listed in " +
                         "THIRD-PARTY-NOTICES.txt beside the app.");
        Form.Button(about, "Project page", () => Form.Browse(Project)).SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
    }
}
