using Godot;
using InsertCoin.Core;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Balance: the balance of work, life and games. What earns play time (the first run's choices, to change
/// them: focus, the player's own activities, LeetCode with their name, or no play time); the programs that count as play
/// besides Steam games, picked from those running or by their file; how much play a day can earn,
/// Monday to Thursday, on Friday and at the weekend; and the player's bedtime, after which play costs more. What earns
/// counts at once; a cap or bedtime is saved at once but counts from the next day, so the cap can't be raised, nor
/// bedtime moved, in the moment (spec 2026-10-07-balance.md, 2026-10-27-what-earns-play-time.md). With no play time
/// only the ticks and the bedtime show: no caps, and no price after bedtime.
/// </summary>
public partial class BalanceSettings : VBoxContainer
{
    private static readonly int[] Caps = [30, 45, 60, 75, 90, 105, 120, 135, 150, 180, 210, 240];
    private static readonly string[] Bedtimes = ["21:00", "21:30", "22:00", "22:30", "23:00", "23:30", "00:00", "00:30", "01:00", "01:30", "02:00", "02:30", "03:00"];

    /// <summary>The caps in minutes (Monday to Thursday, Friday, the weekend) and the bedtime ("23:30", or null for none), as chosen now.</summary>
    public event Action<int, int, int, string?>? Chosen;
    /// <summary>What earns play time, as ticked now: focus, activities, LeetCode.</summary>
    public event Action<bool, bool, bool>? Earning;
    /// <summary>"Change…" by the LeetCode name, or LeetCode ticked without one: the cabinet's screen asks for it.</summary>
    public event Action? ChangePlayer;
    /// <summary>The programs that count as play, as they are after each change.</summary>
    public event Action<PlayProgram[]>? Programs;
    /// <summary>"Add by its file…": the cabinet asks for a program's file, and hands it back through <see cref="Add"/>.</summary>
    public event Action? PickFile;

    private OptionButton _weekday = null!, _friday = null!, _weekend = null!, _bedtime = null!, _running = null!;
    private int[] _caps = Caps;
    private string?[] _bedtimes = [];
    private List<PlayProgram> _programs = [];
    private IReadOnlyList<PlayProgram> _runningNow = [];
    private PlayProgram[] _offered = [];
    private VBoxContainer _picked = null!;

    /// <param name="today">What holds today, which a change doesn't touch.</param>
    /// <param name="running">The programs running now, to pick from as play.</param>
    public void Build(AppSettings settings, string today, IReadOnlyList<PlayProgram> running)
    {
        Form.Page(this, "Balance");
        var about = Form.Hint(this, "Good work earns play time, from what you tick below. A day can only earn so much, and each night half of the play " +
                                    "you didn't use fades, so it can't be saved up for the weekend.");
        var balance = new List<Control> { about };          // what goes with no play time, and comes back with it

        Form.Subheading(this, "What earns your play time");
        var player = new VBoxContainer();
        var name = new HBoxContainer();
        name.AddThemeConstantOverride("separation", 10);
        name.AddChild(new Label { Text = settings.Username ?? "none yet", SizeFlagsVertical = SizeFlags.ShrinkCenter });
        Form.Button(name, "Change…", () => ChangePlayer?.Invoke());
        Form.Row(Form.Grid(player), "LeetCode name", name);
        Form.Hint(player, "Your solved problems are read from your public LeetCode profile. Change… asks for the name on the cabinet's screen.");
        var ticked = settings.Earning.LeetCode;
        Label later = null!;
        void Shown(bool playTime)
        {
            foreach (var part in balance)
                part.Visible = playTime;
            later.Text = playTime ? "The caps and bedtime count from tomorrow, so none can be changed in the moment. " + today
                                  : "Half an hour before your bedtime you get a word to start winding down. It counts from tomorrow, so it " +
                                    "can't be changed in the moment.";
        }
        Sources(this, settings, player, (focus, activities, leetCode) =>
        {
            Earning?.Invoke(focus, activities, leetCode);
            if (leetCode && !ticked && string.IsNullOrWhiteSpace(settings.Username))
                ChangePlayer?.Invoke();                     // ticked with no name: the screen asks for one
            ticked = leetCode;
            Shown(focus || activities || leetCode);
        });

        Form.Subheading(this, "Programs that count as play");
        _programs = [.. settings.PlayPrograms];
        _runningNow = running;
        _picked = new VBoxContainer();
        AddChild(_picked);
        _running = Form.Choice(Form.Grid(this), "Running now", ["Choose one to add…"], 0, index =>
        {
            if (index > 0)
                Add(_offered[index - 1]);
        }, wide: true);
        Form.Button(Form.Buttons(this), "Add by its file…", () => PickFile?.Invoke());
        Form.Hint(this, "Steam games count by themselves. A program added here counts as a game does while its window is in front: a " +
                        "video player, a game launcher, whatever you count. It doesn't pause while you sit still, only when the screen " +
                        "locks or you've left the PC. Only the program is known, by its name: no window title or browser tab is read.");
        ShowPrograms();

        balance.Add(Form.Subheading(this, "Play a day can earn"));
        _caps = [.. Caps.Union([settings.WeekdayCapMinutes, settings.FridayCapMinutes, settings.WeekendCapMinutes]).Order()];
        var caps = Form.Grid(this);
        balance.Add(caps);
        _weekday = Form.Choice(caps, "Monday to Thursday", _caps.Select(Hours), Array.IndexOf(_caps, settings.WeekdayCapMinutes), _ => Changed());
        _friday = Form.Choice(caps, "Friday", _caps.Select(Hours), Array.IndexOf(_caps, settings.FridayCapMinutes), _ => Changed());
        _weekend = Form.Choice(caps, "Saturday and Sunday", _caps.Select(Hours), Array.IndexOf(_caps, settings.WeekendCapMinutes), _ => Changed());
        balance.Add(Form.Hint(this, "A day runs from 04:00 to 04:00. What is left from the days before doesn't count towards it, and paying back time " +
                                    "owed doesn't either."));

        Form.Subheading(this, "Bedtime");
        _bedtimes = [null, .. Bedtimes.Union(settings.Bedtime is { } own ? [own] : []).OrderBy(Late)];
        _bedtime = Form.Choice(Form.Grid(this), "Go to bed at", _bedtimes.Select(b => b ?? "None"), Array.IndexOf(_bedtimes, settings.Bedtime), _ => Changed());
        balance.Add(Form.Hint(this, "After your bedtime, until the day ends at 04:00, play costs one and a half times as much, and time owed grows " +
                                    "that much faster."));

        later = Form.Hint(this, "");
        Shown(settings.Earning.Any);
    }

    /// <summary>
    /// What earns play time, the same on the first run and here: focus sessions and the player's own activities, ticked
    /// to start with, LeetCode problems, and "No play time", which clears the three (unticked again, the first two come
    /// back). Nothing ticked is no play time, however it came about.
    /// </summary>
    /// <param name="leetCode">What goes with LeetCode (its name), shown while it is ticked.</param>
    /// <param name="chosen">Focus, activities and LeetCode, as ticked after each change.</param>
    public static void Sources(Container parent, AppSettings settings, Control leetCode, Action<bool, bool, bool> chosen)
    {
        var now = settings.Earning;
        CheckBox Tick(string text, bool on, string about)
        {
            var tick = Form.Tick(parent, text, on);
            Form.Indented(parent).AddChild(Form.Hint(about));
            return tick;
        }
        var ticks = new[]
        {
            Tick("Focus sessions", now.Focus, $"The screen's FOCUS app: 15 minutes of play for every {settings.FocusPerPlay * 15} minutes focused."),
            Tick("Your own activities", now.Activities, "The gym, chores, whatever you count: tell your companion you did it, or log it on the " +
                                                        "screen's DONE app. Each pays by its size, once a day."),
            Tick("LeetCode problems", now.LeetCode, "Problems you solve on LeetCode: 15 minutes for an Easy one, 30 for a Medium, an hour for a Hard."),
        };
        parent.AddChild(leetCode);
        var none = Form.Tick(parent, "No play time, just keep me company", !now.Any);

        void Changed()
        {
            none.SetPressedNoSignal(!ticks.Any(t => t.ButtonPressed));
            leetCode.Visible = ticks[2].ButtonPressed;
            chosen(ticks[0].ButtonPressed, ticks[1].ButtonPressed, ticks[2].ButtonPressed);
        }
        foreach (var tick in ticks)
            tick.Toggled += _ => Changed();
        none.Toggled += on =>
        {
            ticks[0].SetPressedNoSignal(!on);
            ticks[1].SetPressedNoSignal(!on);
            ticks[2].SetPressedNoSignal(false);
            Changed();
        };
        leetCode.Visible = now.LeetCode;
    }

    /// <summary>Adds a program to those that count as play; one there already stays as it is.</summary>
    public void Add(PlayProgram program)
    {
        if (!_programs.Any(p => p.Path.Equals(program.Path, StringComparison.OrdinalIgnoreCase)))
        {
            _programs.Add(program);
            Programs?.Invoke([.. _programs]);
        }
        Callable.From(ShowPrograms).CallDeferred();         // not while "Running now" is still telling of its choice
    }

    /// <summary>The programs that count as play, each with its × to take it off, and those running that could be added.</summary>
    private void ShowPrograms()
    {
        foreach (var row in _picked.GetChildren())
        {
            _picked.RemoveChild(row);
            row.QueueFree();
        }
        if (_programs.Count == 0)
            _picked.AddChild(Form.Hint("None yet."));
        foreach (var program in _programs)
        {
            var row = Form.Buttons(_picked);
            row.AddChild(new Label { Text = program.Name, TooltipText = program.Path, MouseFilter = MouseFilterEnum.Pass, SizeFlagsVertical = SizeFlags.ShrinkCenter });
            Form.Button(row, "×", () =>
            {
                _programs.Remove(program);
                Programs?.Invoke([.. _programs]);
                ShowPrograms();
            }).TooltipText = "Stop counting it as play";
        }
        _offered = [.. _runningNow.Where(r => !_programs.Any(p => p.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase))).OrderBy(r => r.Name)];
        _running.Clear();
        _running.AddItem(_offered.Length > 0 ? "Choose one to add…" : "None to add");
        foreach (var program in _offered)
            _running.AddItem(program.Name);
        _running.Select(0);
        _running.Disabled = _offered.Length == 0;
    }

    private void Changed() => Chosen?.Invoke(_caps[_weekday.Selected], _caps[_friday.Selected], _caps[_weekend.Selected], _bedtimes[_bedtime.Selected]);

    /// <summary>Bedtimes in the order of the night: 21:00 first, 03:00 last.</summary>
    private static int Late(string? time) => time is null ? -1 : (int.Parse(time[..2]) + 20) % 24 * 60 + int.Parse(time[3..]);

    private static string Hours(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
}
