using Godot;

namespace InsertCoin.Game.Desktop;

public enum TrayMood { Fine, Low, Debt }

/// <summary>System tray icon: a coin tinted by state, a tooltip with the readout, and the app menu.</summary>
public partial class TrayIcon : Node
{
    public event Action? ShowCabinet, CheckNow, ChangePlayer, Quit;
    public event Action<bool>? AlwaysOnTopChanged, StartWithWindowsChanged;
    public event Action<string>? SizeChanged;

    private const int ShowId = 0, CheckId = 1, PlayerId = 2, TopId = 3, StartupId = 4, QuitId = 5, SizeId = 10;
    private static readonly string[] Sizes = ["S", "M", "L"];
    private static readonly Dictionary<TrayMood, Color> Tint = new()
    {
        [TrayMood.Fine] = new Color("4dd9e8"), [TrayMood.Low] = new Color("ffb347"), [TrayMood.Debt] = new Color("ff4f9a"),
    };

    private StatusIndicator _indicator = null!;
    private PopupMenu _menu = null!;
    private readonly Dictionary<TrayMood, Texture2D> _icons = [];
    private TrayMood? _mood;

    public void Build(bool alwaysOnTop, bool startWithWindows, string size)
    {
        _menu = new PopupMenu();
        _menu.AddItem("Show cabinet", ShowId);
        _menu.AddItem("Check LeetCode now", CheckId);
        _menu.AddItem("Change player…", PlayerId);
        _menu.AddSeparator();
        foreach (var (name, i) in new[] { "Small", "Medium", "Large" }.Select((n, i) => (n, i)))
        {
            _menu.AddRadioCheckItem($"Size: {name}", SizeId + i);
            _menu.SetItemChecked(_menu.GetItemIndex(SizeId + i), Sizes[i] == size);
        }
        _menu.AddSeparator();
        _menu.AddCheckItem("Always on top", TopId);
        _menu.AddCheckItem("Start with Windows", StartupId);
        _menu.AddSeparator();
        _menu.AddItem("Quit", QuitId);
        _menu.SetItemChecked(_menu.GetItemIndex(TopId), alwaysOnTop);
        _menu.SetItemChecked(_menu.GetItemIndex(StartupId), startWithWindows);
        _menu.IdPressed += OnMenu;
        AddChild(_menu);

        foreach (var (mood, tint) in Tint)
            _icons[mood] = CoinIcon(tint);
        _indicator = new StatusIndicator { Tooltip = "INSERT COIN" };
        AddChild(_indicator);
        _indicator.Menu = _indicator.GetPathTo(_menu);
        Set(TrayMood.Fine, "INSERT COIN");
    }

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
            case CheckId: CheckNow?.Invoke(); break;
            case PlayerId: ChangePlayer?.Invoke(); break;
            case TopId: AlwaysOnTopChanged?.Invoke(Toggle(TopId)); break;
            case StartupId: StartWithWindowsChanged?.Invoke(Toggle(StartupId)); break;
            case QuitId: Quit?.Invoke(); break;
            case >= SizeId and < SizeId + 3:
                for (var i = 0; i < 3; i++)
                    _menu.SetItemChecked(_menu.GetItemIndex(SizeId + i), SizeId + i == id);
                SizeChanged?.Invoke(Sizes[id - SizeId]);
                break;
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
