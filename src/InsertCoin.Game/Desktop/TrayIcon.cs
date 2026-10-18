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
    private PopupMenu _menu = null!;
    private readonly Dictionary<TrayMood, Texture2D> _icons = [];
    private TrayMood? _mood;

    public void Build(bool muted, bool voice)
    {
        _menu = new PopupMenu();
        _menu.IdPressed += OnMenu;
        AddChild(_menu);
        _menu.AddItem("Show cabinet", ShowId);
        _menu.AddItem("Settings…", SettingsId);
        _menu.AddSeparator();
        _menu.AddItem("Memory book…", MemoryId);
        _menu.AddItem("Chat history…", HistoryId);
        _menu.AddItem("Write a memo…", MemoId);
        _menu.AddItem("Check LeetCode now", CheckId);
        _menu.AddSeparator();
        _menu.AddCheckItem("Mute sounds", MuteId);
        _menu.AddCheckItem("Voice", VoiceId);
        _menu.AddCheckItem("Conversation (keeps listening)", ConversationId);
        _menu.AddSeparator();
        _menu.AddItem("Quit", QuitId);
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
    /// Opens this same menu at a screen point: a right click on her or the cabinet, so the two never differ. A test run
    /// logs it instead: shown, a menu takes focus (and a hidden run's stopped answering).
    /// </summary>
    public void ShowMenu(Vector2I at)
    {
        if (Companion.Paper.OffScreen)
            GD.Print($"Menu: at {at.X},{at.Y}: {DevItems()}");
        else
            _menu.Popup(new Rect2I(at, Vector2I.Zero));
    }

    /// <summary>Dev: the menu's entries as they are now, for the log; a switch says whether it is on ([x]) or off ([ ]).</summary>
    public string DevItems() => string.Join(" | ", Enumerable.Range(0, _menu.ItemCount)
        .Where(i => !_menu.IsItemSeparator(i))
        .Select(i => _menu.GetItemText(i) + (_menu.IsItemCheckable(i) ? _menu.IsItemChecked(i) ? " [x]" : " [ ]" : "")));

    /// <summary>Dev: presses the entry whose text starts with this, as a click on it would, and closes the menu; false when there is none.</summary>
    public bool DevPress(string text)
    {
        var at = Enumerable.Range(0, _menu.ItemCount).FirstOrDefault(i => !_menu.IsItemSeparator(i) && _menu.GetItemText(i).StartsWith(text, StringComparison.OrdinalIgnoreCase), -1);
        if (at < 0)
            return false;
        _menu.Hide();
        OnMenu(_menu.GetItemId(at));
        return true;
    }

    /// <summary>Shows the switches as they are now (changed in Settings, say).</summary>
    public void Sync(bool muted, bool voice)
    {
        _menu.SetItemChecked(_menu.GetItemIndex(MuteId), muted);
        _menu.SetItemChecked(_menu.GetItemIndex(VoiceId), voice);
    }

    /// <summary>Shows "Getting started…" under Settings… while anything is left of it, and takes it away once nothing is.</summary>
    public void ShowStart(bool left)
    {
        var at = _menu.GetItemIndex(StartId);
        if (left && at < 0)
        {
            _menu.AddItem("Getting started…", StartId);
            _menu.SetItemIndex(_menu.GetItemIndex(StartId), _menu.GetItemIndex(SettingsId) + 1);
        }
        else if (!left && at >= 0)
            _menu.RemoveItem(at);
    }

    /// <summary>Shows whether a conversation is on (it ends by itself after a quiet while, or by the talk key).</summary>
    public void SetConversation(bool on) => _menu.SetItemChecked(_menu.GetItemIndex(ConversationId), on);

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

    private bool Toggle(int id)
    {
        var index = _menu.GetItemIndex(id);
        var now = !_menu.IsItemChecked(index);
        _menu.SetItemChecked(index, now);
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
