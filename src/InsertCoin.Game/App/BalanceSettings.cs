using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// The balance of work, life and games (tray → Balance…): how much play a day can earn, Monday to Thursday, on Friday
/// and at the weekend, and the player's bedtime, after which play costs more. Changes count from the next day, so the
/// cap can't be raised, nor bedtime moved, in the moment (spec 2026-10-07-balance.md).
/// </summary>
public partial class BalanceSettings : Window
{
    private static readonly int[] Caps = [30, 45, 60, 75, 90, 105, 120, 135, 150, 180, 210, 240];
    private static readonly string[] Bedtimes = ["21:00", "21:30", "22:00", "22:30", "23:00", "23:30", "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00"];

    /// <summary>After Save: the caps in minutes (Monday to Thursday, Friday, the weekend) and the bedtime ("23:30", or null for none).</summary>
    public event Action<int, int, int, string?>? Saved;

    private OptionButton _weekday = null!, _friday = null!, _weekend = null!, _bedtime = null!;
    private int[] _caps = Caps;
    private string?[] _bedtimes = [];

    /// <param name="today">What holds today, which a change doesn't touch.</param>
    public void Build(AppSettings settings, string today)
    {
        Title = "Balance";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        page.AddChild(outer);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(column);

        column.AddChild(new Label { Text = "Balance", ThemeTypeVariation = "Heading" });
        column.AddChild(Hint($"Good work earns play: LeetCode problems, and focus sessions (15 minutes of play for every " +
                             $"{settings.FocusPerPlay * 15} minutes focused). A day can only earn so much, and each night half of the play " +
                             "you didn't use fades, so it can't be saved up for the weekend."));

        column.AddChild(new Label { Text = "Play a day can earn", ThemeTypeVariation = "Subheading" });
        _caps = [.. Caps.Union([settings.WeekdayCapMinutes, settings.FridayCapMinutes, settings.WeekendCapMinutes]).Order()];
        var caps = Grid(column);
        _weekday = Choice(caps, "Monday to Thursday", _caps.Select(Hours), Array.IndexOf(_caps, settings.WeekdayCapMinutes));
        _friday = Choice(caps, "Friday", _caps.Select(Hours), Array.IndexOf(_caps, settings.FridayCapMinutes));
        _weekend = Choice(caps, "Saturday and Sunday", _caps.Select(Hours), Array.IndexOf(_caps, settings.WeekendCapMinutes));
        column.AddChild(Hint("A day runs from 04:00 to 04:00. What is left from the days before doesn't count towards it, and paying back time " +
                             "owed doesn't either."));

        column.AddChild(new Label { Text = "Bedtime", ThemeTypeVariation = "Subheading" });
        _bedtimes = [null, .. Bedtimes.Union(settings.Bedtime is { } own ? [own] : []).OrderBy(Late)];
        _bedtime = Choice(Grid(column), "Go to bed at", _bedtimes.Select(b => b ?? "None"), Array.IndexOf(_bedtimes, settings.Bedtime));
        column.AddChild(Hint("After your bedtime, until the day ends at 04:00, play costs one and a half times as much, and time owed grows " +
                             "that much faster."));

        column.AddChild(Hint("Changes count from tomorrow, so none can be made in the moment. " + today));

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        cancel.Pressed += QueueFree;
        save.Pressed += () =>
        {
            Saved?.Invoke(_caps[_weekday.Selected], _caps[_friday.Selected], _caps[_weekend.Selected], _bedtimes[_bedtime.Selected]);
            QueueFree();
        };
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        outer.AddChild(buttons);
    }

    public void Open() => Paper.Open(this, new Vector2(520, 560));

    /// <summary>Bedtimes in the order of the night: 21:00 first, 03:00 last.</summary>
    private static int Late(string? time) => time is null ? -1 : (int.Parse(time[..2]) + 20) % 24 * 60 + int.Parse(time[3..]);

    private static string Hours(int minutes) => $"{minutes / 60}:{minutes % 60:00}";

    private static GridContainer Grid(VBoxContainer column)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        column.AddChild(grid);
        return grid;
    }

    private static OptionButton Choice(GridContainer grid, string label, IEnumerable<string> options, int selected)
    {
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(150, 0) });
        var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        foreach (var option in options)
            choice.AddItem(option);
        choice.Selected = Math.Max(0, selected);
        grid.AddChild(choice);
        return choice;
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
}
