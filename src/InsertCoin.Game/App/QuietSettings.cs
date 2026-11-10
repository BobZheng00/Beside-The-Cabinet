using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Notifications → Bedtime and quiet hours: the player's bedtime, after which play costs more (with no play
/// time, only a word to wind down), and the hours nothing of the companion's own comes up. Each change is saved at once;
/// a bedtime counts from the next day, so it can't be moved in the moment (spec 2026-10-07-balance.md).
/// </summary>
public partial class QuietSettings : VBoxContainer
{
    private static readonly string[] Bedtimes = ["21:00", "21:30", "22:00", "22:30", "23:00", "23:30", "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00"];

    /// <summary>The bedtime chosen ("23:30"), or null for none.</summary>
    public event Action<string?>? BedtimeChosen;
    /// <summary>The quiet hours chosen: from this hour until that one.</summary>
    public event Action<int, int>? QuietChosen;

    /// <param name="today">What holds today, which a change doesn't touch.</param>
    public void Build(SettingsFile settings, string today)
    {
        var now = settings.Now;
        Form.Page(this);
        Form.Subheading(this, "Bedtime");
        string?[] bedtimes = [null, .. Bedtimes.Union(now.Bedtime is { } own ? [own] : []).OrderBy(Late)];
        Form.Choice(Form.Grid(this), "Go to bed at", bedtimes.Select(b => b ?? "None"), Array.IndexOf(bedtimes, now.Bedtime), index => BedtimeChosen?.Invoke(bedtimes[index]));
        var about = Form.Hint(this, "");
        void Said() => about.Text = settings.Now.Earning.Any         // as what earns play time changes on Play time
            ? "After your bedtime, until the day ends at 04:00, play costs one and a half times as much, and time owed grows that much faster. " +
              "A new bedtime counts from tomorrow, so it can't be moved in the moment. " + today
            : "Half an hour before your bedtime you get a word to start winding down. A new bedtime counts from tomorrow, so it can't be moved in the moment.";
        Said();
        settings.Changed += Said;
        TreeExiting += () => settings.Changed -= Said;

        Form.Subheading(this, "Quiet hours");
        var quiet = new HBoxContainer();
        quiet.AddThemeConstantOverride("separation", 8);
        var from = new OptionButton();
        var until = new OptionButton();
        foreach (var hour in Enumerable.Range(0, 24).Select(h => $"{h:00}:00"))
        {
            from.AddItem(hour);
            until.AddItem(hour);
        }
        from.Selected = now.QuietFrom;
        until.Selected = now.QuietUntil;
        from.ItemSelected += _ => QuietChosen?.Invoke(from.Selected, until.Selected);
        until.ItemSelected += _ => QuietChosen?.Invoke(from.Selected, until.Selected);
        quiet.AddChild(from);
        quiet.AddChild(new Label { Text = "until" });
        quiet.AddChild(until);
        Form.Row(Form.Grid(this), "Not from", quiet);
        Form.Hint(this, "No check-ins, no word to stand up and no texts of their own in these hours. Reminders still come.");
    }

    /// <summary>Bedtimes in the order of the night: 21:00 first, 03:00 last.</summary>
    private static int Late(string? time) => time is null ? -1 : (int.Parse(time[..2]) + 20) % 24 * 60 + int.Parse(time[3..]);
}
