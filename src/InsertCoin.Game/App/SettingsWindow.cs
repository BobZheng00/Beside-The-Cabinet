using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// A page of the settings: its name in dev actions and links, the group it is listed in (<see cref="SettingsWindow.First"/>:
/// above the groups), its name in the list, and how it is made: the first time it shows, or each time when it is
/// <paramref name="Fresh"/> (it tells how things stand on the other pages).
/// </summary>
public sealed record SettingsPage(string Id, string Group, string Name, Func<Control> Make, bool Fresh = false);

/// <summary>
/// Every setting in one window (tray → Settings…): the pages listed on the left in three groups (her, the player's day,
/// the cabinet), the one chosen on the right. A page is saved as it is changed, so there is nothing to save or cancel;
/// only a connection to something outside (her provider) waits for its own button (spec 2026-10-10-settings-and-setup.md).
/// </summary>
public partial class SettingsWindow : Window
{
    public const string First = "", Her = "Her", Day = "Your day", Cabinet = "The cabinet";
    private static readonly string[] Groups = [First, Her, Day, Cabinet];

    private readonly Dictionary<string, SettingsPage> _pages = [];
    private readonly Dictionary<string, Button> _entries = [];
    private readonly Dictionary<string, ScrollContainer> _made = [];
    private MarginContainer _area = null!;
    private string? _showing;

    /// <param name="open">The page to show first, or null for the first one.</param>
    public void Build(IEnumerable<SettingsPage> pages, string? open)
    {
        Title = "Settings";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var sheet = new PanelContainer();
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(sheet);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        sheet.AddChild(row);

        var list = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
        list.AddThemeConstantOverride("separation", 2);
        row.AddChild(list);
        var chosen = new ButtonGroup();
        var all = pages.OrderBy(p => Array.IndexOf(Groups, p.Group)).ToList();      // in each group, in the order given
        string? group = null;
        foreach (var page in all)
        {
            _pages[page.Id] = page;
            if (page.Group != group)
            {
                if (group is not null)
                    list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
                group = page.Group;
                if (group != First)
                    list.AddChild(new Label { Text = group.ToUpperInvariant(), ThemeTypeVariation = "Group" });
            }
            var entry = new Button { Text = page.Name, ThemeTypeVariation = "Side", ToggleMode = true, ButtonGroup = chosen, Alignment = HorizontalAlignment.Left };
            entry.Pressed += () => ShowPage(page.Id);
            list.AddChild(entry);
            _entries[page.Id] = entry;
        }
        list.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        list.AddChild(new Label { Text = "Changes are saved as you make them.", ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        _area = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddChild(_area);
        ShowPage(open is not null && _pages.ContainsKey(open) ? open : all[0].Id);
    }

    public void Open() => Paper.Open(this, new Vector2(820, 660));

    /// <summary>The page showing.</summary>
    public string? Showing => _showing;

    /// <summary>Shows a page, made the first time it shows; each keeps where it was scrolled to.</summary>
    public void ShowPage(string id)
    {
        if (!_pages.TryGetValue(id, out var page))
            return;
        if (page.Fresh && _showing != id && _made.Remove(id, out var stale))
            stale.QueueFree();
        if (!_made.TryGetValue(id, out var made))
        {
            made = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            made.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            margin.AddThemeConstantOverride("margin_right", 14);                    // room for the scroll bar
            margin.AddChild(page.Make());
            made.AddChild(margin);
            _area.AddChild(made);
            _made[id] = made;
        }
        foreach (var (other, shown) in _made)
            shown.Visible = other == id;
        _entries[id].ButtonPressed = true;
        _showing = id;
    }

    /// <summary>Makes a page again if it has been made, as what it shows changed elsewhere (a voice added, a character).</summary>
    public void RefreshPage(string id)
    {
        if (!_made.Remove(id, out var made))
            return;
        made.Visible = false;
        made.QueueFree();
        if (_showing == id)
            ShowPage(id);
    }

    /// <summary>The page made for <paramref name="id"/>, if it has been.</summary>
    public Control? PageOf(string id) => _made.TryGetValue(id, out var made) ? made.GetChild(0).GetChild<Control>(0) : null;

    /// <summary>
    /// Dev: on the page showing, presses the button or flips the tick whose text starts with <paramref name="label"/>;
    /// with a value, sets the choice (by number, from 0), the field or the number that follows the label with that text.
    /// </summary>
    public void DevDo(string label, string? value)
    {
        if (_showing is not null && PageOf(_showing) is { } page)
            DevDo(page, label, value, _showing);
    }

    /// <summary>Dev: the same within <paramref name="root"/> (another window of the cabinet's: Welcome, Meet), named <paramref name="where"/> in the log.</summary>
    public static void DevDo(Node root, string label, string? value, string where)
    {
        var all = Within(root).Where(c => c.IsVisibleInTree()).ToList();
        if (value is null)
        {
            switch (all.OfType<Button>().FirstOrDefault(b => b is not OptionButton && b.Text.StartsWith(label, StringComparison.OrdinalIgnoreCase)))
            {
                case CheckBox tick:
                    tick.ButtonPressed = !tick.ButtonPressed;
                    GD.Print($"Settings: \"{tick.Text}\" {(tick.ButtonPressed ? "ticked" : "unticked")}");
                    break;
                case { } button:
                    button.EmitSignal(BaseButton.SignalName.Pressed);
                    GD.Print($"Settings: \"{button.Text}\" pressed");
                    break;
                default:
                    GD.PrintErr($"Settings: nothing called \"{label}\" on {where}");
                    break;
            }
            return;
        }
        var at = all.FindIndex(c => c is Label named && named.Text.Equals(label, StringComparison.OrdinalIgnoreCase));
        switch (at < 0 ? null : all.Skip(at + 1).FirstOrDefault(c => c is OptionButton or LineEdit or Godot.Range))
        {
            case OptionButton choice when int.TryParse(value, out var index) && index < choice.ItemCount:
                choice.Select(index);
                choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                GD.Print($"Settings: {label} is \"{choice.GetItemText(index)}\"");
                break;
            case LineEdit field:
                field.Text = value;
                field.EmitSignal(LineEdit.SignalName.TextChanged, value);
                field.EmitSignal(LineEdit.SignalName.TextSubmitted, value);
                GD.Print($"Settings: {label} typed in");
                break;
            case Godot.Range number:                        // a number to set, or a slider
                number.Value = double.Parse(value, CultureInfo.InvariantCulture);
                GD.Print($"Settings: {label} is {number.Value}");
                break;
            default:
                GD.PrintErr($"Settings: no choice or field after \"{label}\" on {where}");
                break;
        }
    }

    /// <summary>Dev: the pointer resting on the control with this text on the page showing, so its hover tip shows; the real mouse never moves.</summary>
    public void DevHover(string label)
    {
        if (_showing is null || PageOf(_showing) is not { } page)
            return;
        var target = Within(page).FirstOrDefault(c => c.IsVisibleInTree() && c.TooltipText.Length > 0
                                                      && ((c as Label)?.Text ?? (c as Button)?.Text ?? "").StartsWith(label, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            GD.PrintErr($"Settings: nothing with a hover tip called \"{label}\" on {_showing}");
            return;
        }
        Notification((int)NotificationVpMouseEnter);       // tips only show in a window the mouse is in
        PushInput(new InputEventMouseMotion { Position = target.GetGlobalRect().GetCenter() }, true);
    }

    /// <summary>Dev: scrolls the page showing this far down (pixels), to see the end of a long one.</summary>
    public void DevScroll(int down)
    {
        if (_showing is not null && _made.TryGetValue(_showing, out var made))
            made.ScrollVertical = down;
    }

    /// <summary>
    /// Dev: logs any page wider than the room it has (a row of buttons that doesn't fit, say: it would run off the
    /// window's right edge), with the widest parts in it.
    /// </summary>
    public void DevWidths()
    {
        foreach (var (id, made) in _made)
        {
            var page = PageOf(id)!;
            var room = made.Size.X - 14;
            if (page.GetCombinedMinimumSize().X <= room)
                continue;
            GD.PrintErr($"Settings: {id} needs {page.GetCombinedMinimumSize().X:0} px, has {room:0}");
            foreach (var part in Within(page).Where(c => c.IsVisibleInTree() && c.GetCombinedMinimumSize().X > room * 0.8f))
                GD.PrintErr($"  {part.GetType().Name} {part.GetCombinedMinimumSize().X:0} px {(part as Label)?.Text ?? (part as Button)?.Text}");
        }
    }

    private static IEnumerable<Control> Within(Node node) =>
        node.GetChildren().OfType<Control>().SelectMany(c => Within(c).Prepend(c));
}
