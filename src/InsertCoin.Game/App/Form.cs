using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// What the settings pages are made of, in the cabinet's paper style: headings, hints, ticks, choices and fields. Each
/// takes what to do when the player changes it, since a page is saved as it is changed (see <see cref="SettingsWindow"/>).
/// </summary>
public static class Form
{
    /// <summary>A page to fill: a column (a page of the settings, which the window heads).</summary>
    public static VBoxContainer Page(VBoxContainer page)
    {
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        page.AddThemeConstantOverride("separation", 10);
        return page;
    }

    /// <summary>A page to fill: a column, with its heading.</summary>
    public static VBoxContainer Page(VBoxContainer page, string heading)
    {
        Page(page).AddChild(new Label { Text = heading, ThemeTypeVariation = "Heading" });
        return page;
    }

    /// <summary>
    /// A page below a page: first, top left, where it is ("‹ Skills › journal"), the page above it a button back up to
    /// it; then its heading, wrapped when long. Every page below a page starts this way.
    /// </summary>
    public static VBoxContainer Page(VBoxContainer page, string above, string heading, Action up)
    {
        var path = new HBoxContainer();
        path.AddThemeConstantOverride("separation", 6);
        page.AddChild(path);
        Button(path, $"‹ {above}", up).Name = "Up";             // the name finds it for dev actions (settingsup)
        path.AddChild(new Label { Text = $"› {heading}", ThemeTypeVariation = "Hint", ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        Page(page, heading);
        var named = page.GetChild<Label>(-1);                  // a name the player gave can be long: it wraps, never widening the page
        named.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        named.CustomMinimumSize = new Vector2(200, 0);
        return page;
    }

    public static Label Subheading(Container parent, string text)
    {
        var label = new Label { Text = text, ThemeTypeVariation = "Subheading" };
        parent.AddChild(label);
        return label;
    }

    public static Label Hint(string text) => new()
    {
        Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0),
    };

    public static Label Hint(Container parent, string text)
    {
        var hint = Hint(text);
        parent.AddChild(hint);
        return hint;
    }

    /// <summary>A tick, and what to do when it is flipped.</summary>
    public static CheckBox Tick(Container parent, string text, bool on, Action<bool>? flipped = null)
    {
        var box = new CheckBox
        {
            Text = text, ButtonPressed = on, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0),
        };
        if (flipped is not null)
            box.Toggled += on => flipped(on);
        parent.AddChild(box);
        return box;
    }

    /// <summary>Two columns: a label, and what it names.</summary>
    public static GridContainer Grid(Container parent)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        parent.AddChild(grid);
        return grid;
    }

    public static Label Row(GridContainer grid, string label, Control field)
    {
        var name = new Label { Text = label };
        grid.AddChild(name);
        grid.AddChild(field);
        return name;
    }

    /// <summary>A choice in a list, and what to do when another is chosen. A long one is cut short rather than widening the page.</summary>
    public static OptionButton Choice(GridContainer grid, string label, IEnumerable<string> options, int selected, Action<int>? chosen = null, bool wide = false)
    {
        var choice = new OptionButton
        {
            SizeFlagsHorizontal = wide ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkBegin, ClipText = wide, FitToLongestItem = !wide,
        };
        foreach (var option in options)
            choice.AddItem(option);
        choice.Selected = Math.Max(0, Math.Min(selected, choice.ItemCount - 1));
        if (chosen is not null)
            choice.ItemSelected += index => chosen((int)index);
        Row(grid, label, choice);
        return choice;
    }

    /// <summary>A slider for a whole number, what it is set to written beside it, and what to do when it moves.</summary>
    public static HSlider Slider(GridContainer grid, string label, int min, int max, int value, Func<int, string> says, Action<int> moved)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = 1, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(160, 0),
        };
        var shown = new Label { Text = says(value), CustomMinimumSize = new Vector2(100, 0) };
        slider.ValueChanged += now =>
        {
            shown.Text = says((int)now);
            moved((int)now);
        };
        row.AddChild(slider);
        row.AddChild(shown);
        Row(grid, label, row);
        return slider;
    }

    /// <summary>A field to type in, and what to do with each change.</summary>
    public static LineEdit Field(GridContainer grid, string label, string? value, string placeholder, bool secret = false, Action<string>? typed = null)
    {
        var field = new LineEdit { Text = value ?? "", PlaceholderText = placeholder, Secret = secret, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (typed is not null)
            field.TextChanged += text => typed(text);
        Row(grid, label, field);
        return field;
    }

    /// <summary>A row set in under the line above it.</summary>
    public static HBoxContainer Indented(Container parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(22, 0) });
        parent.AddChild(row);
        return row;
    }

    /// <summary>A row of buttons and what came of them.</summary>
    public static HBoxContainer Buttons(Container parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);
        return row;
    }

    public static Button Button(Container parent, string text, Action pressed, bool quiet = true)
    {
        var button = new Button { Text = text, ThemeTypeVariation = quiet ? "Quiet" : "", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    /// <summary>A line saying what came of something (a test, a link), wrapped, in a colour that says how it went.</summary>
    public static Label Result(Container parent)
    {
        var label = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(160, 0) };
        parent.AddChild(label);
        return label;
    }

    public static void Say(Label label, string text, Color colour)
    {
        label.Visible = true;
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
    }

    /// <summary>
    /// A small button to another page of the settings, named as that page is: a pointer in a page ("see Reminders") is
    /// one of these. It opens the page where it is pressed.
    /// </summary>
    public static Button Link(Container parent, string page, string id)
    {
        var button = Button(parent, $"{page} ›", () => (parent.GetWindow() as SettingsWindow)?.ShowPage(id));
        button.ThemeTypeVariation = "QuietSmall";
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        return button;
    }

    /// <summary>
    /// A connection's own panel, first on its page: what it is, how it stands in a word, a line on it and its one button
    /// (its rules and options come after it). <see cref="Standing.Show"/> fills it in, and again as things change.
    /// </summary>
    public static Standing Connection(Container parent, string what)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Paper.Box(Paper.Wash, Paper.Wash, 2, 10, 14, 12));
        parent.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        column.AddChild(top);
        top.AddChild(new Label { Text = what, ThemeTypeVariation = "Subheading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var state = new Label { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        state.AddThemeFontSizeOverride("font_size", 13);
        top.AddChild(state);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);
        var line = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(130, 0) };     // beside Continue with ChatGPT, at 125%
        row.AddChild(line);
        var button = new Button { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(button);
        var result = Result(column);
        result.Visible = false;
        return new Standing(what, state, line, button, result);
    }

    /// <summary>Scrolls the page to <paramref name="control"/> once it is laid out, and gives it the keys if it takes them (a field).</summary>
    public static async void Bring(Control control)
    {
        await control.ToSignal(control.GetTree(), SceneTree.SignalName.ProcessFrame);     // what was just shown is laid out by then
        if (!GodotObject.IsInstanceValid(control))
            return;
        for (var up = control.GetParent(); up is not null; up = up.GetParent())
            if (up is ScrollContainer scroll)
            {
                scroll.EnsureControlVisible(control);
                break;
            }
        if (control.FocusMode != Control.FocusModeEnum.None)
            control.GrabFocus();
    }

    /// <summary>The tick and the fields that belong to it: the fields only show while it is ticked.</summary>
    public static void ShowWhile(CheckBox tick, params Control[] fields)
    {
        void Show()
        {
            foreach (var field in fields)
                field.Visible = tick.ButtonPressed;
        }
        tick.Toggled += _ => Show();
        Show();
    }
}

/// <summary>
/// A connection's panel (see <see cref="Form.Connection"/>): its state in a word, on green when it works, red when it
/// doesn't and plain when it isn't set up; its line; its one button; and what came of pressing it.
/// </summary>
public sealed class Standing
{
    private readonly string _what;
    private readonly Label _state, _line;
    private readonly Button _button;
    private Action _pressed = () => { };

    public Standing(string what, Label state, Label line, Button button, Label result)
    {
        (_what, _state, _line, _button, Result) = (what, state, line, button, result);
        button.Pressed += () => _pressed();
    }

    /// <summary>Where what came of the button is said (<see cref="Form.Say"/>).</summary>
    public Label Result { get; }

    /// <param name="works">True: it works; false: it should and doesn't; null: not set up.</param>
    public void Show(string state, bool? works, string line, string button, Action pressed)
    {
        var (fill, ink) = works switch { true => (Paper.Good, Colors.White), false => (Paper.Bad, Colors.White), null => (new Color("E2DAF7"), Paper.Letters) };
        _state.Text = state;
        _state.AddThemeStyleboxOverride("normal", Paper.Box(fill, fill, 0, 8, 8, 2));
        _state.AddThemeColorOverride("font_color", ink);
        _line.Text = line;
        _button.Text = button;
        _pressed = pressed;
        if (Paper.OffScreen)                                // a test run logs how each connection stands
            GD.Print($"Settings: {_what}: {state}; {button}");
    }
}
