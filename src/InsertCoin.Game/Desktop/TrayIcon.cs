using Godot;

namespace InsertCoin.Game.Desktop;

public enum TrayMood { Fine, Low, Debt }

/// <summary>
/// System tray icon: a coin tinted by state, a tooltip with the readout, and the app menu: the cabinet, Settings…,
/// Getting started… while anything is left of it, her books, a memo, a check of LeetCode, the switches flipped often
/// (sounds, her voice, a conversation), and Quit.
/// Everything else is in Settings (see <see cref="App.SettingsWindow"/>).
/// </summary>
public partial class TrayIcon : Node
{
    public event Action? ShowCabinet, Settings, GettingStarted, MemoryBook, ChatHistory, WriteMemo, CheckNow, Quit;
    public event Action<bool>? MutedChanged, VoiceChanged, ConversationChanged;

    private const int ShowId = 0, SettingsId = 1, MemoryId = 2, HistoryId = 3, MemoId = 4, CheckId = 5, MuteId = 6, VoiceId = 7, ConversationId = 8, QuitId = 9, StartId = 10;
    private static readonly Dictionary<TrayMood, Color> Tint = new()
    {
        [TrayMood.Fine] = new Color("4dd9e8"), [TrayMood.Low] = new Color("ffb347"), [TrayMood.Debt] = new Color("ff4f9a"),
    };

    private StatusIndicator _indicator = null!;
    private PopupMenu _menu = null!, _onCabinet = null!;
    // the right-click menu's parent: a popup menu takes its scale from the cabinet's window times its parent's, so this
    // one's scale undoes the cabinet's size (#163)
    private readonly Node2D _sizer = new();
    private readonly Dictionary<TrayMood, Texture2D> _icons = [];
    private TrayMood? _mood;

    /// <summary>The tray's menu (Windows' own, shown by the icon) and the one a right click on her or the cabinet opens.</summary>
    private PopupMenu[] Menus => [_menu, _onCabinet];

    public void Build(bool muted, bool voice)
    {
        _menu = Menu(this);
        // drawn by the app, not Windows' own menu: that one holds the app still while open, her voice and motion too
        AddChild(_sizer);
        _onCabinet = Menu(_sizer);
        Sync(muted, voice);

        foreach (var (mood, tint) in Tint)
            _icons[mood] = CoinIcon(tint);
        // a test run has its menu (the dev actions open it) but never an icon: the tray is the player's own copy's
        _indicator = new StatusIndicator { Tooltip = "Beside the Cabinet", Visible = !Companion.Paper.OffScreen };
        if (Companion.Paper.OffScreen)
            GD.Print("Tray: none in a test run");
        AddChild(_indicator);
        _indicator.Menu = _indicator.GetPathTo(_menu);
        Set(TrayMood.Fine, "Beside the Cabinet");
    }

    /// <summary>
    /// Opens the right-click menu (the tray's entries, kept the same way) at a screen point: a right click on her or the
    /// cabinet. A test run logs it, and shows it only when <paramref name="evenInTest"/>: while it shows, a hidden run
    /// takes no screenshots (#88).
    /// </summary>
    public void ShowMenu(Vector2I at, bool evenInTest = false)
    {
        if (Companion.Paper.OffScreen)
        {
            GD.Print($"Menu: at {at.X},{at.Y}: {Items(_onCabinet)}");
            if (!evenInTest)
                return;
        }
        // the same size whatever the cabinet's, and as large in inches on any screen
        var dpi = DisplayServer.ScreenGetDpi(DisplayServer.GetScreenFromRect(new Rect2(at, Vector2.One)));
        var cabinet = _sizer.GetViewport().GetFinalTransform().Scale.X;
        _sizer.Scale = Vector2.One * (MenuScale * dpi / 96f / cabinet);
        _onCabinet.Popup(new Rect2I(at, Vector2I.Zero));
        if (Companion.Paper.OffScreen)
            GD.Print($"Menu: shown {_onCabinet.Size.X}x{_onCabinet.Size.Y} at {_onCabinet.Position.X},{_onCabinet.Position.Y} by a cabinet at {cabinet:0.0}, " +
                     $"on a {dpi} dpi screen: at scale {_onCabinet.ContentScaleFactor:0.00} ({_onCabinet.ContentScaleFactor * 96f / dpi:0.00} per 96 dpi)");
    }

    /// <summary>
    /// The right-click menu's scale on a 96 dpi screen: the size the player found best, the one it had by a large
    /// cabinet (1.0) with Windows at 125% (#163), kept as large in inches on other screens.
    /// </summary>
    private const float MenuScale = 1f / 1.25f;

    /// <summary>Dev: the tray menu's entries as they are now, for the log; a switch says whether it is on ([x]) or off ([ ]).</summary>
    public string DevItems() => Items(_menu);

    private static string Items(PopupMenu menu) => string.Join(" | ", Enumerable.Range(0, menu.ItemCount)
        .Where(i => !menu.IsItemSeparator(i))
        .Select(i => menu.GetItemText(i) + (menu.IsItemCheckable(i) ? menu.IsItemChecked(i) ? " [x]" : " [ ]" : "")));

    /// <summary>Dev: presses the entry of the right-click menu whose text starts with this, as a click on it would, and closes it; false when there is none.</summary>
    public bool DevPress(string text)
    {
        var at = Enumerable.Range(0, _onCabinet.ItemCount).FirstOrDefault(i => !_onCabinet.IsItemSeparator(i) && _onCabinet.GetItemText(i).StartsWith(text, StringComparison.OrdinalIgnoreCase), -1);
        if (at < 0)
            return false;
        _onCabinet.Hide();
        OnMenu(_onCabinet.GetItemId(at));
        return true;
    }

    /// <summary>Shows the switches as they are now (changed in Settings, say).</summary>
    public void Sync(bool muted, bool voice)
    {
        Check(MuteId, muted);
        Check(VoiceId, voice);
    }

    /// <summary>Shows "Getting started…" under Settings… while anything is left of it, and takes it away once nothing is.</summary>
    public void ShowStart(bool left) => Show(StartId, "Getting started…", SettingsId, left);

    /// <summary>Shows "Check LeetCode now" under "Write a memo…" while LeetCode earns play time, and takes it away while it doesn't.</summary>
    public void ShowCheck(bool on) => Show(CheckId, "Check LeetCode now", MemoId, on);

    /// <summary>An entry in both menus, just below another, or taken away.</summary>
    private void Show(int id, string text, int below, bool shown)
    {
        foreach (var menu in Menus)
        {
            var at = menu.GetItemIndex(id);
            if (shown && at < 0)
            {
                menu.AddItem(text, id);
                menu.SetItemIndex(menu.GetItemIndex(id), menu.GetItemIndex(below) + 1);
            }
            else if (!shown && at >= 0)
                menu.RemoveItem(at);
        }
    }

    /// <summary>Shows whether a conversation is on (it ends by itself after a quiet while, or by the talk key).</summary>
    public void SetConversation(bool on) => Check(ConversationId, on);

    public void Set(TrayMood mood, string tooltip)
    {
        if (mood != _mood)
        {
            _mood = mood;
            _indicator.Icon = _icons[mood];
        }
        if (_indicator.Tooltip != tooltip)
            _indicator.Tooltip = tooltip;
    }

    private void OnMenu(long id)
    {
        switch (id)
        {
            case ShowId: ShowCabinet?.Invoke(); break;
            case SettingsId: Settings?.Invoke(); break;
            case StartId: GettingStarted?.Invoke(); break;
            case MemoryId: MemoryBook?.Invoke(); break;
            case HistoryId: ChatHistory?.Invoke(); break;
            case MemoId: WriteMemo?.Invoke(); break;
            case CheckId: CheckNow?.Invoke(); break;
            case MuteId: MutedChanged?.Invoke(Toggle(MuteId)); break;
            case VoiceId: VoiceChanged?.Invoke(Toggle(VoiceId)); break;
            case ConversationId: ConversationChanged?.Invoke(Toggle(ConversationId)); break;
            case QuitId: Quit?.Invoke(); break;
        }
    }

    /// <summary>The menu's entries, the one place both menus are made from.</summary>
    private PopupMenu Menu(Node into)
    {
        var menu = new PopupMenu();
        menu.IdPressed += OnMenu;
        into.AddChild(menu);
        menu.AddItem("Show cabinet", ShowId);
        menu.AddItem("Settings…", SettingsId);
        menu.AddSeparator();
        menu.AddItem("Memory book…", MemoryId);
        menu.AddItem("Chat history…", HistoryId);
        menu.AddItem("Write a memo…", MemoId);         // "Check LeetCode now" goes under it while LeetCode earns (see ShowCheck)
        menu.AddSeparator();
        menu.AddCheckItem("Mute sounds", MuteId);
        menu.AddCheckItem("Voice", VoiceId);
        menu.AddCheckItem("Conversation (keeps listening)", ConversationId);
        menu.AddSeparator();
        menu.AddItem("Quit", QuitId);
        return menu;
    }

    private void Check(int id, bool on)
    {
        foreach (var menu in Menus)
            menu.SetItemChecked(menu.GetItemIndex(id), on);
    }

    private bool Toggle(int id)
    {
        var now = !_menu.IsItemChecked(_menu.GetItemIndex(id));
        Check(id, now);
        return now;
    }

    /// <summary>A small coin drawn in code: rim, face and a slot mark.</summary>
    private static Texture2D CoinIcon(Color tint)
    {
        const int size = 32;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var centre = new Vector2(15.5f, 15.5f);
        var rim = new Color("1e1630");
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = new Vector2(x, y).DistanceTo(centre);
            if (d > 15f)
                continue;
            var slot = Math.Abs(x - 15.5f) < 2f && Math.Abs(y - 15.5f) < 7f;
            var colour = d > 12.5f || slot ? rim : tint.Lightened(Mathf.Clamp((12f - new Vector2(x, y).DistanceTo(new Vector2(11, 10))) / 20f, 0, 0.45f));
            image.SetPixel(x, y, colour);
        }
        return ImageTexture.CreateFromImage(image);
    }
}
