using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// A page of the settings: its name in dev actions and links, the category it is in, its tab there (empty: the category
/// is this page alone), and how it is made: the first time it shows, or each time when it is <paramref name="Fresh"/>
/// (it tells how things stand on the other pages).
/// </summary>
public sealed record SettingsPage(string Id, string Category, string Tab, Func<Control> Make, bool Fresh = false);

/// <summary>
/// Every setting in one window (tray → Settings…): plain categories listed on the left, the one chosen on the right
/// under its name, its parts as tabs when it has more than one. A page is saved as it is changed, so there is nothing to
/// save or cancel; only a connection to something outside (their AI provider) waits for its own button (specs
/// 2026-10-10-settings-and-setup.md, 2026-11-08-a-new-player-gets-it.md).
/// </summary>
public partial class SettingsWindow : Window
{
    public const string Start = "Getting started", General = "General", Companion = "Companion", Memory = "Memory", Voice = "Voice", AiModel = "AI model",
        PlayTime = "Play time", Notifications = "Notifications", Connections = "Connections", Abilities = "Abilities and privacy";
    private static readonly string[] Categories = [Start, General, Companion, Memory, Voice, AiModel, PlayTime, Notifications, Connections, Abilities];

    private readonly Dictionary<string, SettingsPage> _pages = [];
    private readonly Dictionary<string, List<SettingsPage>> _parts = [];    // each category's pages, in the order given
    private readonly Dictionary<string, Button> _entries = [];              // the list, by category
    private readonly Dictionary<string, string> _lastPart = [];             // the page each category showed last
    private readonly Dictionary<string, ScrollContainer> _made = [];
    private MarginContainer _area = null!;
    private PanelContainer _sheet = null!;
    private Label _heading = null!;
    private Control _strip = null!;                         // the tabs and the line under them
    private TabBar _tabs = null!;
    private string? _tabsOf;                                // the category the tabs are for
    private string? _showing;
    private string _companion = "";                         // who the pages made so far were made for (see Rename)
    private readonly HashSet<string> _stale = [];           // pages to make again when next shown (see Rename)

    /// <param name="open">The page to show first, or null for the first one.</param>
    /// <param name="companion">Who is on the stool: the pages name them.</param>
    public void Build(IEnumerable<SettingsPage> pages, string? open, string companion = "Your companion")
    {
        Title = "Settings";
        Visible = false;
        Transient = true;
        // what Memory can't yet save stops the first close (see MemorySettings.Leave)
        CloseRequested += () =>
        {
            if (PageOf("memory") is MemorySettings memory && !memory.Leave())
                ShowPage("memory");
            else
                QueueFree();
        };
        _companion = companion;

        var sheet = _sheet = new PanelContainer();
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(sheet);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        sheet.AddChild(row);

        // the list scrolls only on a screen too short for it (the window is cut to the screen), so its last entry and the
        // note below it are never out of reach
        var side = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(232, 0) };
        side.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        row.AddChild(side);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 2);
        side.AddChild(list);
        foreach (var page in pages)
        {
            _pages[page.Id] = page;
            if (!_parts.TryGetValue(page.Category, out var parts))
                _parts[page.Category] = parts = [];
            parts.Add(page);
        }
        var chosen = new ButtonGroup();
        foreach (var category in Categories.Where(_parts.ContainsKey))
        {
            var entry = new Button
            {
                Text = category, Icon = SettingsIcons.Of(category), TextureFilter = Control.TextureFilterEnum.Nearest,
                ThemeTypeVariation = "Side", ToggleMode = true, ButtonGroup = chosen, Alignment = HorizontalAlignment.Left,
            };
            entry.Pressed += () => ShowPage(_lastPart.GetValueOrDefault(category, _parts[category][0].Id));
            list.AddChild(entry);
            _entries[category] = entry;
        }
        list.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        list.AddChild(new Label { Text = "Changes are saved as you make them.", ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        row.AddChild(column);
        _heading = new Label { ThemeTypeVariation = "Heading" };
        column.AddChild(_heading);
        var strip = new VBoxContainer();
        strip.AddThemeConstantOverride("separation", 0);
        _strip = strip;
        column.AddChild(strip);
        _tabs = new TabBar();
        _tabs.TabChanged += tab => ShowPage(_parts[_tabsOf!][(int)tab].Id);
        strip.AddChild(_tabs);
        var line = new HSeparator();
        line.AddThemeConstantOverride("separation", 1);
        strip.AddChild(line);
        _area = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(_area);
        ShowPage(open is not null && _pages.ContainsKey(open) ? open : _parts[_entries.Keys.First()][0].Id);
        if (Paper.OffScreen)                                // a test run logs the list, and the page it opened at
            GD.Print($"Settings: the list: {string.Join(" | ", _entries.Keys)}; opened at {_showing}");
    }

    private static readonly Vector2I Wanted = new(850, 704);    // tall enough for the whole list, and its note

    public void Open()
    {
        Paper.Open(this, Wanted);
        Theme.SetFont("font", "Heading", GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf"));     // the screen's pixel font: at 16 px, its grid
        Theme.SetFontSize("font_size", "Heading", 16);
        // the log says the size it opened at, a moment on once Windows has placed it, and the screens it was sized for:
        // on a narrow one (a portrait screen beside the main one) its pages get less room than asked for (#312)
        var screen = DisplayServer.WindowGetCurrentScreen();
        var room = DisplayServer.ScreenGetUsableRect(screen).Size;
        GetTree().CreateTimer(1).Timeout += () =>
        {
            if (IsInstanceValid(this))
                GD.Print($"Settings: opened {Size.X}x{Size.Y}, its pages {GetVisibleRect().Size.X:0}x{GetVisibleRect().Size.Y:0}, sized for the cabinet's screen {screen} ({room.X}x{room.Y} free at {DisplayServer.ScreenGetDpi(screen)} dpi); screens: "
                         + string.Join(", ", Enumerable.Range(0, DisplayServer.GetScreenCount()).Select(s => $"{DisplayServer.ScreenGetSize(s).X}x{DisplayServer.ScreenGetSize(s).Y} at {DisplayServer.ScreenGetDpi(s)} dpi")));
        };
    }

    /// <summary>The page showing.</summary>
    public string? Showing => _showing;

    /// <summary>Shows a page, made the first time it shows; each keeps where it was scrolled to.</summary>
    public void ShowPage(string id)
    {
        if (!_pages.TryGetValue(id, out var page))
            return;
        if ((page.Fresh || _stale.Contains(id)) && _showing != id && _made.Remove(id, out var stale))
        {
            stale.QueueFree();
            _stale.Remove(id);
        }
        if (!_made.TryGetValue(id, out var made))
        {
            made = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            made.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            var content = page.Make();
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = content.SizeFlagsVertical };   // a page may fill the height (Memory, for its lists to scroll inside)
            margin.AddThemeConstantOverride("margin_right", 14);                    // room for the scroll bar
            margin.AddChild(content);
            made.AddChild(margin);
            _area.AddChild(made);
            _made[id] = made;
        }
        foreach (var (other, shown) in _made)
            shown.Visible = other == id;
        _showing = id;
        _lastPart[page.Category] = id;
        _entries[page.Category].ButtonPressed = true;
        _heading.Text = page.Category.ToUpperInvariant();
        ShowTabs(page);
    }

    /// <summary>The tabs of the page's category, with the page's own chosen; none when the category is a page alone.</summary>
    private void ShowTabs(SettingsPage page)
    {
        var parts = _parts[page.Category];
        _strip.Visible = parts.Count > 1;
        _tabs.SetBlockSignals(true);                        // only the player's own change of tab shows another page
        if (_tabsOf != page.Category)
        {
            _tabs.ClearTabs();
            foreach (var part in parts)
                _tabs.AddTab(part.Tab);
            _tabsOf = page.Category;
        }
        _tabs.CurrentTab = parts.IndexOf(page);
        _tabs.SetBlockSignals(false);
    }

    /// <summary>Makes a page again if it has been made, as what it shows changed elsewhere (a voice added, a character).</summary>
    public void RefreshPage(string id)
    {
        _stale.Remove(id);
        if (!_made.Remove(id, out var made))
            return;
        made.Visible = false;
        made.QueueFree();
        if (_showing == id)
            ShowPage(id);
    }

    /// <summary>
    /// Someone else took the stool: every page made before, which may say the old name, is made again: the one showing
    /// at once, others when next shown. The one showing stays as it is when it is <paramref name="choosingOn"/> (the
    /// player may be choosing on it), and is made again when next shown.
    /// </summary>
    public void Rename(string companion, string choosingOn)
    {
        if (_companion == companion)
            return;
        _companion = companion;
        foreach (var id in _made.Keys.Where(id => id != _showing).ToList())
            RefreshPage(id);
        if (_showing == choosingOn)
            _stale.Add(_showing);
        else if (_showing is not null)
            RefreshPage(_showing);
        GD.Print($"Settings: the pages now name {companion}; {_showing} made again {(_showing == choosingOn ? "when next shown" : "now")}");
    }

    /// <summary>The page made for <paramref name="id"/>, if it has been.</summary>
    public Control? PageOf(string id) => _made.TryGetValue(id, out var made) ? made.GetChild(0).GetChild<Control>(0) : null;

    /// <summary>
    /// Dev: chooses the category or the tab called <paramref name="label"/>, as a click on it would; else, on the page
    /// showing, presses the button or flips the tick whose text starts with it; with a value, sets the choice (by
    /// number, from 0), the field or the number that follows the label with that text.
    /// </summary>
    public void DevDo(string label, string? value)
    {
        if (value is null && _entries.TryGetValue(label, out var entry))
        {
            entry.EmitSignal(BaseButton.SignalName.Pressed);
            GD.Print($"Settings: category \"{label}\" chosen; {_showing} showing");
            return;
        }
        if (value is null && _tabsOf is not null && _parts[_tabsOf].FindIndex(p => p.Tab.Equals(label, StringComparison.OrdinalIgnoreCase)) is var tab and >= 0)
        {
            _tabs.CurrentTab = tab;
            GD.Print($"Settings: tab \"{label}\" chosen; {_showing} showing");
            return;
        }
        if (_showing is not null && PageOf(_showing) is { } page)
        {
            var was = _showing;
            DevDo(page, label, value, was);
            if (_showing != was)
                GD.Print($"Settings: {was} led to {_showing}");
        }
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
            // a choice by its place in the list (0 first) or by what it says ("中文")
            case OptionButton choice when (int.TryParse(value, out var number) ? number : Enumerable.Range(0, choice.ItemCount).FirstOrDefault(i => choice.GetItemText(i) == value, -1))
                                          is var index and >= 0 && index < choice.ItemCount:
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

    /// <summary>Dev: the window as it opens on a screen of this size at 100%, to read its pixel headings there (this PC has its own size and scale).</summary>
    public void DevScreen(Vector2I screen)
    {
        ContentScaleFactor = 1;
        MinSize = Vector2I.Zero;
        MinSize = Size = new Vector2I(Math.Min(Wanted.X, screen.X - 16), Math.Min(Wanted.Y, screen.Y - 48));
        GD.Print($"Settings: laid out as on a {screen.X}x{screen.Y} screen: {Size.X}x{Size.Y}");
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
