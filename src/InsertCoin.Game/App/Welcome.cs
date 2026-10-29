using Godot;
using InsertCoin.Adapters;
using InsertCoin.Core;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// The first run: four short pages before the cabinet is the player's: the name she calls them (their initials on the
/// cabinet come from it), who keeps them company (seated on the stool as they choose), what earns their play time
/// (focus, their own activities, LeetCode with their name, or no play time), and her mind, which she needs to talk.
/// What can wait (her voice, talking aloud, the phone, a calendar) is left to Getting started. Closing it early keeps
/// what was chosen so far (spec 2026-10-10-settings-and-setup.md, "The first run"; 2026-10-27-what-earns-play-time.md).
/// </summary>
public partial class Welcome : Window
{
    /// <summary>The first run is over (finished, skipped or closed), with the name given, if any.</summary>
    public event Action<string?>? Done;

    private readonly List<Control> _pages = [];
    private int _at;
    private Button _back = null!, _next = null!;
    private string _name = "";
    private Func<bool> _connected = null!;
    private Func<Control> _mind = null!;
    private VBoxContainer _mindPage = null!;
    private bool _done, _chosen = true;                     // _chosen: another character since her mind's page was made, which names them
    private SettingsFile _settings = null!;
    private ITaskSource _tasks = null!;
    private Control _earns = null!;
    private VBoxContainer _leetCode = null!;
    private LineEdit _leetCodeName = null!;
    private Label _found = null!;
    private string? _nobody;                                // the name LeetCode said there's no player called

    /// <param name="stool">Who can sit on it and how, who sits there, the name they call the player, and her mind's page.</param>
    /// <param name="connected">Whether her mind is connected now.</param>
    /// <param name="tasks">LeetCode, to check the name given there.</param>
    public void Build(SettingsFile settings, Stool stool, Func<bool> connected, ITaskSource tasks)
    {
        Title = "Welcome";
        Visible = false;
        CloseRequested += Finish;
        _connected = connected;
        _settings = settings;
        _tasks = tasks;
        _mind = () => stool.MindPage(brief: true);
        Action<string> named = stool.CallMe;

        var sheet = new PanelContainer();
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(sheet);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        sheet.AddChild(column);
        var area = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(area);

        // the name she calls them
        var first = Form.Page(new VBoxContainer(), "Welcome to Beside the Cabinet");
        Form.Hint(first, "A desktop arcade cabinet for a balance of work, life and games, with a companion on the stool beside it, who " +
                         "chats, remembers and keeps your day. Four things and it's yours: about five minutes.");
        var name = Form.Field(Form.Grid(first), "Your name", settings.Now.CallMe, "your name, or what you like to be called", typed: text =>
        {
            _name = text.Trim();
            named(text);
            Refresh();
        });
        _name = name.Text.Trim();
        Form.Hint(first, "What your companion calls you; your initials on the cabinet come from it too.");
        _pages.Add(first);

        // who keeps them company: seated as chosen, so they see who it is
        var second = Form.Page(new VBoxContainer(), "Who keeps you company?");
        var group = new ButtonGroup();
        var manner = new VBoxContainer();
        void HowTheySit(string id)
        {
            foreach (var old in manner.GetChildren())
            {
                manner.RemoveChild(old);
                old.QueueFree();
            }
            Form.Subheading(manner, "How they sit");
            CharacterSettings.Manners(manner, stool.MannerOf(id), chosen => stool.SetManner(id, chosen), stool.ShowManner);
        }
        foreach (var (id, who, about) in stool.Meetable())
        {
            var pick = new CheckBox { Text = who, ButtonGroup = group, ButtonPressed = id == stool.Seated };
            pick.Toggled += on =>
            {
                if (!on)
                    return;
                stool.Seat(id);
                HowTheySit(id);
                _chosen = true;
            };
            second.AddChild(pick);
            if (about.Length > 0)
                Form.Indented(second).AddChild(Form.Hint(about));
        }
        second.AddChild(manner);
        HowTheySit(stool.Seated ?? "");
        Form.Hint(second, "They sit on the stool by the cabinet, and move into a manner as you choose it. You can change who it is, how " +
                          "they sit, or add a character of your own at any time: Settings → Characters, or drop a 3D model on the cabinet.");
        _pages.Add(second);

        // what earns their play time: LeetCode with a name, checked as Balance's Change… checks it
        var third = Form.Page(new VBoxContainer(), "What earns your play time?");
        Form.Hint(third, "Good work earns time to play. Tick what counts for you; you can change it at any time in Settings → Balance.");
        _leetCode = new VBoxContainer();
        _leetCodeName = Form.Field(Form.Grid(_leetCode), "LeetCode name", settings.Now.Username, "your name or profile link on LeetCode", typed: _ =>
        {
            _nobody = null;
            _found.Visible = false;
        });
        var asking = Form.Buttons(_leetCode);
        Form.Button(asking, "Check", () => _ = Ask());
        _found = Form.Result(asking);
        _found.Visible = false;
        BalanceSettings.Sources(third, settings.Now, _leetCode, (focus, activities, leetCode) =>
            settings.Save(settings.Now with { FocusEarns = focus, ActivitiesEarn = activities, LeetCodeEarns = leetCode }));
        _earns = third;
        _pages.Add(third);

        // their mind (made as the page comes up, for whoever was chosen), and what waits for later
        var fourth = new VBoxContainer();
        fourth.AddThemeConstantOverride("separation", 10);
        _mindPage = new VBoxContainer();
        fourth.AddChild(_mindPage);
        fourth.AddChild(new HSeparator());
        Form.Hint(fourth, "The rest can wait for whenever you like: a voice, talking aloud, your phone and a calendar are in Getting " +
                          "started, in the tray and at the top of Settings.");
        _pages.Add(fourth);

        foreach (var page in _pages)
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            margin.AddThemeConstantOverride("margin_right", 14);         // room for the scroll bar
            margin.AddChild(page);
            scroll.AddChild(margin);
            area.AddChild(scroll);
        }
        var buttons = Form.Buttons(column);
        _back = Form.Button(buttons, "Back", () => Go(-1));
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _next = Form.Button(buttons, "Next", () =>
        {
            if (_pages[_at] == _earns && !LeetCodeKept())
                return;
            if (_at < _pages.Count - 1)
                Go(1);
            else
                Finish();
        }, quiet: false);
        settings.Changed += Refresh;                        // her mind saved: Finish, not Skip
        TreeExiting += () => settings.Changed -= Refresh;
        Refresh();
    }

    public void Open() => Paper.Open(this, new Vector2(640, 640), besideCabinet: true);     // who is chosen is seen on the stool

    private void Go(int by)
    {
        _at = Math.Clamp(_at + by, 0, _pages.Count - 1);
        if (_at == _pages.Count - 1 && _chosen)
        {
            foreach (var old in _mindPage.GetChildren())
            {
                _mindPage.RemoveChild(old);
                old.QueueFree();
            }
            _mindPage.AddChild(_mind());
            _chosen = false;
        }
        Refresh();
    }

    /// <summary>The page they are on, and the buttons as they stand: Next only once there is a name; Finish once her mind is connected.</summary>
    private void Refresh()
    {
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].GetParent().GetParent<Control>().Visible = i == _at;
        _back.Visible = _at > 0;
        var last = _at == _pages.Count - 1;
        _next.Text = !last ? "Next" : _connected() ? "Finish" : "Skip for now";
        _next.Disabled = _at == 0 && _name.Length == 0;
    }

    /// <summary>
    /// Next from what earns: with LeetCode ticked, the name typed is kept and the first run goes on, checked or not (one
    /// not checked yet is checked when the cabinet first reads it). It stays only for a name that isn't one, or one
    /// LeetCode said no player has.
    /// </summary>
    private bool LeetCodeKept()
    {
        if (!_leetCode.Visible)
            return true;
        if (!LeetCodeProfile.TryParse(_leetCodeName.Text, out var username, out var error))
        {
            Form.Say(_found, Sentence(error!), Paper.Bad);
            return false;
        }
        if (username == _nobody)
        {
            Form.Say(_found, $"LeetCode has no player called {username}.", Paper.Bad);
            return false;
        }
        Keep(username!);
        return true;
    }

    /// <summary>Check: LeetCode is asked whether the player is there. If it doesn't answer, the name is kept and checked later.</summary>
    private async Task Ask()
    {
        if (!LeetCodeProfile.TryParse(_leetCodeName.Text, out var username, out var error))
        {
            Form.Say(_found, Sentence(error!), Paper.Bad);
            return;
        }
        Form.Say(_found, "Asking LeetCode…", Paper.Faint);
        string said;
        Color colour;
        try
        {
            var there = await _tasks.PlayerExistsAsync(username!, CancellationToken.None);
            if (!IsInstanceValid(_leetCode) || !_leetCode.Visible)
                return;                                     // closed, or LeetCode unticked, while LeetCode was asked
            if (there)
                Keep(username!);
            else
                _nobody = username;
            (said, colour) = there ? ($"Found: {username}.", Paper.Good) : ($"LeetCode has no player called {username}.", Paper.Bad);
        }
        catch (TaskSourceUnavailableException)
        {
            if (!IsInstanceValid(_leetCode) || !_leetCode.Visible)
                return;
            Keep(username!);
            (said, colour) = ("LeetCode didn't answer, so the name is kept and checked later.", Paper.Faint);
        }
        Form.Say(_found, said, colour);
    }

    private void Keep(string username) => _settings.Save(_settings.Now with { Username = username, LeetCodeEarns = true });

    /// <summary>What the cabinet's screen says of a name ("NOT A LEETCODE LINK"), as a sentence for the page.</summary>
    private static string Sentence(string screen)
    {
        var said = screen.ToLowerInvariant().Replace("leetcode", "LeetCode");
        return $"{char.ToUpperInvariant(said[0])}{said[1..]}.";
    }

    private void Finish()
    {
        if (_done)
            return;
        _done = true;
        Done?.Invoke(_name.Length > 0 ? _name : null);
        QueueFree();
    }

    /// <summary>Dev: presses the button, ticks the choice or types in the field with this label, on the page showing or below it.</summary>
    public void DevDo(string label, string? value) => SettingsWindow.DevDo(this, label, value, "Welcome");
}
