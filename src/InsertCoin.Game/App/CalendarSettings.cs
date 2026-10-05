using System.Globalization;
using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// The player's calendars: link one with its private address (Google's "secret address in iCal format", Outlook's
/// published ICS link), see when each was last read and what came of it, read them now, or unlink one. Read-only:
/// she knows what is on them; she does not change them.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class CalendarSettings : Window
{
    private Calendars _calendars = null!;
    private VBoxContainer _list = null!;
    private LineEdit _name = null!, _url = null!;
    private Label _result = null!;

    public void Build(Calendars calendars)
    {
        _calendars = calendars;
        Title = "Calendars";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;
        calendars.Changed += Refresh;
        TreeExiting += () => calendars.Changed -= Refresh;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        page.AddChild(column);

        column.AddChild(new Label { Text = "Calendars", ThemeTypeVariation = "Heading" });
        column.AddChild(Hint("Link your calendar and its next two weeks join your agenda, read again every 15 minutes: she knows what is coming up, " +
                             "and your reminder before things on your agenda (tray → Reminders…) covers them too. She only reads it."));

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 6);
        column.AddChild(_list);

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Link a calendar", ThemeTypeVariation = "Subheading" });
        column.AddChild(Hint("Google Calendar: Settings → your calendar under “Settings for my calendars” → Integrate calendar → “Secret address in iCal " +
                             "format”. (A work or school account may have it switched off.)"));
        column.AddChild(Hint("Outlook: Settings → Calendar → Shared calendars → Publish a calendar → “Can view all details” → Publish → the ICS link."));
        column.AddChild(Hint("Anyone with the address can read the calendar: it is kept on this PC, encrypted for your Windows account."));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        grid.AddChild(new Label { Text = "Name" });
        _name = new LineEdit { PlaceholderText = "Google, Work…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(_name);
        grid.AddChild(new Label { Text = "Address" });
        _url = new LineEdit { PlaceholderText = "https://…/basic.ics", Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(_url);
        column.AddChild(grid);
        var add = new HBoxContainer();
        add.AddThemeConstantOverride("separation", 10);
        var link = new Button { Text = "Link it", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        link.Pressed += Link;
        _result = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        add.AddChild(link);
        add.AddChild(_result);
        column.AddChild(add);
        column.AddChild(Hint("Signing in with Google or Microsoft (read live, and later let her add events, asking you first) can come later; " +
                             "a linked calendar keeps working alongside it."));

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        var sync = new Button { Text = "Read them now", ThemeTypeVariation = "Quiet" };
        sync.Pressed += () => _ = _calendars.Sync();
        buttons.AddChild(sync);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "Close" };
        close.Pressed += QueueFree;
        buttons.AddChild(close);
        column.AddChild(buttons);
        Refresh();
    }

    public void Open() => Paper.Open(this, new Vector2(560, 600));

    private void Link()
    {
        var name = _name.Text.Trim();
        var url = _url.Text.Trim();
        if (name.Length == 0 || !(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                  || url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)))
        {
            Show("Give it a name, and an address starting with https:// (or webcal://).", Paper.Bad);
            return;
        }
        _calendars.Add(new CalendarLink(name, "link", url));
        _name.Text = _url.Text = "";
        Show("Reading it…", Paper.Faint);
    }

    private void Refresh()
    {
        if (!IsInstanceValid(this))
            return;
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        var links = _calendars.Links;
        if (links.Count == 0)
            _list.AddChild(Hint("No calendar linked yet."));
        foreach (var link in links)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddChild(new Label { Text = link.Name });
            var status = _calendars.Status(link.Name);
            var said = status switch
            {
                null => "Not read yet.",
                { Error: { } error } => $"Could not be read at {status.At.ToString("HH:mm", CultureInfo.InvariantCulture)}: {error}",
                _ => $"Read at {status.At.ToString("HH:mm", CultureInfo.InvariantCulture)}: {status.Events} events in the next two weeks.",
            };
            var line = Hint(said);
            if (status?.Error is not null)
                line.AddThemeColorOverride("font_color", Paper.Bad);
            text.AddChild(line);
            row.AddChild(text);
            var remove = new Button { Text = "Unlink", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, TooltipText = "Its events leave your agenda" };
            remove.Pressed += () => _calendars.Remove(link.Name);
            row.AddChild(remove);
            _list.AddChild(row);
        }
        if (_result.Text == "Reading it…")
            _result.Text = "";
    }

    private void Show(string text, Color colour)
    {
        _result.Text = text;
        _result.AddThemeColorOverride("font_color", colour);
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
}
