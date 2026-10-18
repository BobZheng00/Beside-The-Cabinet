using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// What the settings pages are made of, in the cabinet's paper style: headings, hints, ticks, choices and fields. Each
/// takes what to do when the player changes it, since a page is saved as it is changed (see <see cref="SettingsWindow"/>).
/// </summary>
public static class Form
{
    /// <summary>A page to fill: a column, with its heading.</summary>
    public static VBoxContainer Page(VBoxContainer page, string heading)
    {
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        page.AddThemeConstantOverride("separation", 10);
        page.AddChild(new Label { Text = heading, ThemeTypeVariation = "Heading" });
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
