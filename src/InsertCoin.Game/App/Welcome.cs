using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// The first run: three short pages before the cabinet is the player's: the name she calls them (their initials on the
/// cabinet come from it), who keeps them company (seated on the stool as they choose), and her mind, which she needs to
/// talk. What can wait (her voice, talking aloud, the phone, a calendar, LeetCode) is left to Getting started. Closing
/// it early keeps what was chosen so far (spec 2026-10-10-settings-and-setup.md, "The first run").
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

    /// <param name="characters">Who can sit on the stool: their id, name and who they are in a sentence.</param>
    /// <param name="mind">Makes the page that connects her mind (Settings → Her mind, without what can wait).</param>
    /// <param name="connected">Whether her mind is connected now.</param>
    public void Build(SettingsFile settings, (string Id, string Name, string About)[] characters, string seated, Action<string> seat,
                      Action<string> named, Func<Control> mind, Func<bool> connected)
    {
        Title = "Welcome";
        Visible = false;
        CloseRequested += Finish;
        _connected = connected;
        _mind = mind;

        var sheet = new PanelContainer();
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(sheet);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        sheet.AddChild(column);
        var area = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(area);

        // the name she calls them
        var first = Form.Page(new VBoxContainer(), "Welcome to INSERT COIN");
        Form.Hint(first, "A desktop arcade cabinet for a balance of work, life and games, with a companion on the stool beside it, who " +
                         "chats, remembers and keeps your day. Three things and it's yours: about five minutes.");
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
        foreach (var (id, who, about) in characters)
        {
            var pick = new CheckBox { Text = who, ButtonGroup = group, ButtonPressed = id == seated };
            pick.Toggled += on =>
            {
                if (!on)
                    return;
                seat(id);
                _chosen = true;
            };
            second.AddChild(pick);
            if (about.Length > 0)
                Form.Indented(second).AddChild(Form.Hint(about));
        }
        Form.Hint(second, "They sit on the stool by the cabinet. You can change who it is, or add a character of your own, at any time: " +
                          "Settings → Characters, or drop a 3D model on the cabinet.");
        _pages.Add(second);

        // their mind (made as the page comes up, for whoever was chosen), and what waits for later
        var third = new VBoxContainer();
        third.AddThemeConstantOverride("separation", 10);
        _mindPage = new VBoxContainer();
        third.AddChild(_mindPage);
        third.AddChild(new HSeparator());
        Form.Hint(third, "The rest can wait for whenever you like: a voice, talking aloud, your phone, a calendar and LeetCode are in " +
                         "Getting started, in the tray and at the top of Settings.");
        _pages.Add(third);

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
            if (_at < _pages.Count - 1)
                Go(1);
            else
                Finish();
        }, quiet: false);
        settings.Changed += Refresh;                        // her mind saved: Finish, not Skip
        TreeExiting += () => settings.Changed -= Refresh;
        Refresh();
    }

    public void Open() => Paper.Open(this, new Vector2(640, 600));

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
