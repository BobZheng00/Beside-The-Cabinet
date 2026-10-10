using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Balance: the balance of work, life and games. How much play a day can earn, Monday to Thursday, on Friday
/// and at the weekend; the player's bedtime, after which play costs more; and their LeetCode name, whose solves earn.
/// A change is saved at once but counts from the next day, so the cap can't be raised, nor bedtime moved, in the
/// moment (spec 2026-10-07-balance.md).
/// </summary>
public partial class BalanceSettings : VBoxContainer
{
    private static readonly int[] Caps = [30, 45, 60, 75, 90, 105, 120, 135, 150, 180, 210, 240];
    private static readonly string[] Bedtimes = ["21:00", "21:30", "22:00", "22:30", "23:00", "23:30", "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00"];

    /// <summary>The caps in minutes (Monday to Thursday, Friday, the weekend) and the bedtime ("23:30", or null for none), as chosen now.</summary>
    public event Action<int, int, int, string?>? Chosen;
    /// <summary>"Change…" by the LeetCode name: the cabinet's screen asks for it.</summary>
    public event Action? ChangePlayer;

    private OptionButton _weekday = null!, _friday = null!, _weekend = null!, _bedtime = null!;
    private int[] _caps = Caps;
    private string?[] _bedtimes = [];

    /// <param name="today">What holds today, which a change doesn't touch.</param>
    public void Build(AppSettings settings, string today)
    {
        Form.Page(this, "Balance");
        Form.Hint(this, $"Good work earns play: LeetCode problems, and focus sessions (15 minutes of play for every " +
                        $"{settings.FocusPerPlay * 15} minutes focused). A day can only earn so much, and each night half of the play " +
                        "you didn't use fades, so it can't be saved up for the weekend.");

        var player = Form.Grid(this);
        var name = new HBoxContainer();
        name.AddThemeConstantOverride("separation", 10);
        name.AddChild(new Label { Text = settings.Username ?? "none yet", SizeFlagsVertical = SizeFlags.ShrinkCenter });
        Form.Button(name, "Change…", () => ChangePlayer?.Invoke());
        Form.Row(player, "LeetCode", name);
        Form.Hint(this, "Your solved problems are read from your public LeetCode profile. Change… asks for the name on the cabinet's screen.");

        Form.Subheading(this, "Play a day can earn");
        _caps = [.. Caps.Union([settings.WeekdayCapMinutes, settings.FridayCapMinutes, settings.WeekendCapMinutes]).Order()];
        var caps = Form.Grid(this);
        _weekday = Form.Choice(caps, "Monday to Thursday", _caps.Select(Hours), Array.IndexOf(_caps, settings.WeekdayCapMinutes), _ => Changed());
        _friday = Form.Choice(caps, "Friday", _caps.Select(Hours), Array.IndexOf(_caps, settings.FridayCapMinutes), _ => Changed());
        _weekend = Form.Choice(caps, "Saturday and Sunday", _caps.Select(Hours), Array.IndexOf(_caps, settings.WeekendCapMinutes), _ => Changed());
        Form.Hint(this, "A day runs from 04:00 to 04:00. What is left from the days before doesn't count towards it, and paying back time " +
                        "owed doesn't either.");

        Form.Subheading(this, "Bedtime");
        _bedtimes = [null, .. Bedtimes.Union(settings.Bedtime is { } own ? [own] : []).OrderBy(Late)];
        _bedtime = Form.Choice(Form.Grid(this), "Go to bed at", _bedtimes.Select(b => b ?? "None"), Array.IndexOf(_bedtimes, settings.Bedtime), _ => Changed());
        Form.Hint(this, "After your bedtime, until the day ends at 04:00, play costs one and a half times as much, and time owed grows " +
                        "that much faster.");

        Form.Hint(this, "Changes count from tomorrow, so none can be made in the moment. " + today);
    }

    private void Changed() => Chosen?.Invoke(_caps[_weekday.Selected], _caps[_friday.Selected], _caps[_weekend.Selected], _bedtimes[_bedtime.Selected]);

    /// <summary>Bedtimes in the order of the night: 21:00 first, 03:00 last.</summary>
    private static int Late(string? time) => time is null ? -1 : (int.Parse(time[..2]) + 20) % 24 * 60 + int.Parse(time[3..]);

    private static string Hours(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
}
