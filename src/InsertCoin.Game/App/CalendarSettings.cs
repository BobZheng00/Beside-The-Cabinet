using System.Globalization;
using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Calendars: link one with its private address (Google's "secret address in iCal format", Outlook's
/// published ICS link), see when each was last read and what came of it, read them now, or unlink one. Read-only:
/// she knows what is on them; she does not change them.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class CalendarSettings : VBoxContainer
{
    private Calendars _calendars = null!;
    private VBoxContainer _list = null!;
    private LineEdit _name = null!, _url = null!;
    private Label _result = null!;

    public void Build(Calendars calendars)
    {
        _calendars = calendars;
        calendars.Changed += Refresh;
        TreeExiting += () => calendars.Changed -= Refresh;

        Form.Page(this, "Calendars");
        Form.Hint(this, "Link your calendar and its next two weeks join your agenda, read again every 15 minutes: she knows what is coming up, " +
                        "and your reminder before things on your agenda (see Reminders) covers them too. She only reads it.");

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 6);
        AddChild(_list);
        Form.Button(this, "Read them now", () => _ = _calendars.Sync()).SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

        AddChild(new HSeparator());
        Form.Subheading(this, "Link a calendar");
        Form.Hint(this, "Google Calendar: Settings → your calendar under “Settings for my calendars” → Integrate calendar → “Secret address in iCal " +
                        "format”. (A work or school account may have it switched off.)");
        Form.Hint(this, "Outlook: Settings → Calendar → Shared calendars → Publish a calendar → “Can view all details” → Publish → the ICS link.");
        Form.Hint(this, "Anyone with the address can read the calendar: it is kept on this PC, encrypted for your Windows account.");
        var grid = Form.Grid(this);
        _name = Form.Field(grid, "Name", null, "Google, Work…");
        _url = Form.Field(grid, "Address", null, "https://…/basic.ics", secret: true);
        var add = Form.Buttons(this);
        Form.Button(add, "Link it", Link);
        _result = Form.Result(add);
        Form.Hint(this, "Signing in with Google or Microsoft (read live, and later let her add events, asking you first) can come later; " +
                        "a linked calendar keeps working alongside it.");
        Refresh();
    }

    private void Link()
    {
        var name = _name.Text.Trim();
        var url = _url.Text.Trim();
        if (name.Length == 0 || !(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                  || url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)))
        {
            Form.Say(_result, "Give it a name, and an address starting with https:// (or webcal://).", Paper.Bad);
            return;
        }
        _calendars.Add(new CalendarLink(name, "link", url));
        _name.Text = _url.Text = "";
        Form.Say(_result, "Reading it…", Paper.Faint);
    }

    private void Refresh()
    {
        if (!IsInstanceValid(this))
            return;
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        var links = _calendars.Links;
        if (links.Count == 0)
            _list.AddChild(Form.Hint("No calendar linked yet."));
        foreach (var link in links)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddChild(new Label { Text = link.Name });
            var status = _calendars.Status(link.Name);
            var said = status switch
            {
                null => "Not read yet.",
                { Error: { } error } => $"Could not be read at {status.At.ToString("HH:mm", CultureInfo.InvariantCulture)}: {error}",
                _ => $"Read at {status.At.ToString("HH:mm", CultureInfo.InvariantCulture)}: {status.Events} events in the next two weeks.",
            };
            var line = Form.Hint(said);
            if (status?.Error is not null)
                line.AddThemeColorOverride("font_color", Paper.Bad);
            text.AddChild(line);
            row.AddChild(text);
            var remove = new Button { Text = "Unlink", ThemeTypeVariation = "Quiet", SizeFlagsVertical = SizeFlags.ShrinkBegin, TooltipText = "Its events leave your agenda" };
            remove.Pressed += () => _calendars.Remove(link.Name);
            row.AddChild(remove);
            _list.AddChild(row);
        }
        if (_result.Text == "Reading it…")
            _result.Text = "";
    }
}
