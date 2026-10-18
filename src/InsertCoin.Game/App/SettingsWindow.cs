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
    public const string First = "", Companion = "Companion", Day = "Your day", Cabinet = "The cabinet";
    private static readonly string[] Groups = [First, Companion, Day, Cabinet];

    private readonly Dictionary<string, SettingsPage> _pages = [];
    private readonly Dictionary<string, Button> _entries = [];
    private readonly Dictionary<string, ScrollContainer> _made = [];
    private MarginContainer _area = null!;
    private PanelContainer _sheet = null!;
    private string? _showing;

    /// <param name="open">The page to show first, or null for the first one.</param>
    /// <param name="companion">Who is on the stool: their name heads the companion's pages.</param>
    public void Build(IEnumerable<SettingsPage> pages, string? open, string companion = "Your companion")
    {
        Title = "Settings";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var sheet = _sheet = new PanelContainer();
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(sheet);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        sheet.AddChild(row);

        // the list scrolls only on a screen too short for it (the window is cut to the screen), so its last page and the
        // note below it are never out of reach
        var side = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(200, 0) };
        side.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        row.AddChild(side);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 2);
        side.AddChild(list);
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
                    list.AddChild(new Label { Text = (group == Companion ? companion : group).ToUpperInvariant(), ThemeTypeVariation = "Group" });
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

    public void Open() => Paper.Open(this, new Vector2(820, 704));      // tall enough for every page in the list, and its note

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
                    LogFocus(root);
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

    /// <summary>Dev: on a page below a page, presses its way back up (see <see cref="Form.Page(VBoxContainer, string, string, Action)"/>).</summary>
    public void DevUp()
    {
        if (_showing is not null && PageOf(_showing) is { } page
            && Within(page).OfType<Button>().FirstOrDefault(b => b.Name == "Up" && b.IsVisibleInTree()) is { } up)
        {
            GD.Print($"Settings: \"{up.Text}\" pressed");
            up.EmitSignal(BaseButton.SignalName.Pressed);
            LogFocus(page);
        }
        else
            GD.PrintErr($"Settings: no way up on {_showing}");
    }

    /// <summary>Dev: where keyboard focus is after a press, if on a button (the theme draws no focus ring to see it by).</summary>
    private static void LogFocus(Node within)
    {
        if (within.GetViewport()?.GuiGetFocusOwner() is Button focused)
            GD.Print($"Settings: keyboard focus on \"{focused.Text}\"");
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
    /// window's right edge), with the widest parts in it; then how many of the pages it measured (those made so far),
    /// naming each page below a page that showed in one of them (a skill's).
    /// </summary>
    public void DevWidths()
    {
        // a page too wide widens everything around it past the window's edge, so the room is the page showing's less that excess
        if (_showing is null)
            return;
        var room = _made[_showing].Size.X - (_sheet.Size.X - GetVisibleRect().Size.X) - 14;
        var below = new List<string>();
        foreach (var (id, made) in _made)
        {
            var page = PageOf(id)!;
            if (Within(page).OfType<Button>().FirstOrDefault(b => b.Name == "Up") is { } up)
                below.Add($"{id} {up.GetParent().GetChild<Label>(1).Text}");
            if (page.GetCombinedMinimumSize().X <= room)
                continue;
            GD.PrintErr($"Settings: {id} needs {page.GetCombinedMinimumSize().X:0} px, has {room:0}");
            foreach (var part in Within(page).Where(c => c.IsVisibleInTree() && c.GetCombinedMinimumSize().X > room * 0.8f))
                GD.PrintErr($"  {part.GetType().Name} {part.GetCombinedMinimumSize().X:0} px {(part as Label)?.Text ?? (part as Button)?.Text}");
        }
        GD.Print($"Settings: {_made.Count} of {_pages.Count} pages measured{(below.Count > 0 ? $", below them {string.Join(", ", below)}" : "")}");
    }

    private static IEnumerable<Control> Within(Node node) =>
        node.GetChildren().OfType<Control>().SelectMany(c => Within(c).Prepend(c));
}
