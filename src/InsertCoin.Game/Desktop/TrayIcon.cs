using Godot;

namespace InsertCoin.Game.Desktop;

public enum TrayMood { Fine, Low, Debt }

/// <summary>System tray icon: a coin tinted by state, a tooltip with the readout, and the app menu.</summary>
public partial class TrayIcon : Node
{
    public event Action? ShowCabinet, CheckNow, ChangePlayer, ChatSettings, AddCharacter, OpenCharacters, Quit;
    public event Action<bool>? AlwaysOnTopChanged, StartWithWindowsChanged, MutedChanged, InGameTicketsChanged, CompanionChanged, SpeechChanged, EnergySaverChanged;
    public event Action<string>? SizeChanged, CharacterChanged;

    private const int ShowId = 0, CheckId = 1, PlayerId = 2, TopId = 3, StartupId = 4, QuitId = 5, MuteId = 6, TicketsId = 7, CompanionId = 8,
        AddId = 9, SizeId = 10, EnergyId = 13, SpeechId = 14, FolderId = 15, ChatId = 16, CharacterId = 20;
    private static readonly int[] Checks = [TopId, StartupId, MuteId, TicketsId, CompanionId, SpeechId, EnergyId];
    private static readonly string[] Sizes = ["S", "M", "L"];
    private static readonly Dictionary<TrayMood, Color> Tint = new()
    {
        [TrayMood.Fine] = new Color("4dd9e8"), [TrayMood.Low] = new Color("ffb347"), [TrayMood.Debt] = new Color("ff4f9a"),
    };

    private StatusIndicator _indicator = null!;
    private PopupMenu _menu = null!;
    private readonly Dictionary<TrayMood, Texture2D> _icons = [];
    private TrayMood? _mood;
    private string[] _characters = [];

    public void Build(bool alwaysOnTop, bool startWithWindows, string size, bool muted, bool inGameTickets, bool companion, bool speech,
                      (string Id, string Name)[] characters, string character, bool energySaver)
    {
        _menu = new PopupMenu();
        _menu.IdPressed += OnMenu;
        AddChild(_menu);
        var checks = new Dictionary<int, bool>
        {
            [TopId] = alwaysOnTop, [StartupId] = startWithWindows, [MuteId] = muted, [TicketsId] = inGameTickets,
            [CompanionId] = companion, [SpeechId] = speech, [EnergyId] = energySaver,
        };
        Fill(checks, size, characters, character);

        foreach (var (mood, tint) in Tint)
            _icons[mood] = CoinIcon(tint);
        _indicator = new StatusIndicator { Tooltip = "INSERT COIN" };
        AddChild(_indicator);
        _indicator.Menu = _indicator.GetPathTo(_menu);
        Set(TrayMood.Fine, "INSERT COIN");
    }

    /// <summary>Lists these characters (one just added, say) with <paramref name="character"/> picked, and the companion shown.</summary>
    public void SetCharacters((string Id, string Name)[] characters, string character)
    {
        var checks = Checks.ToDictionary(id => id, id => _menu.IsItemChecked(_menu.GetItemIndex(id)));
        checks[CompanionId] = true;
        var size = Sizes.Where((_, i) => _menu.IsItemChecked(_menu.GetItemIndex(SizeId + i))).FirstOrDefault() ?? "M";
        Fill(checks, size, characters, character);
    }

    /// <summary>Dev: opens the menu at this screen point, as a right click on the icon would.</summary>
    public void DevShowMenu(Vector2I at) => _menu.Popup(new Rect2I(at, Vector2I.Zero));

    private void Fill(IReadOnlyDictionary<int, bool> checks, string size, (string Id, string Name)[] characters, string character)
    {
        _menu.Clear();
        void Check(string label, int id)
        {
            _menu.AddCheckItem(label, id);
            _menu.SetItemChecked(_menu.GetItemIndex(id), checks[id]);
        }
        void Radio(string label, int id, bool on)
        {
            _menu.AddRadioCheckItem(label, id);
            _menu.SetItemChecked(_menu.GetItemIndex(id), on);
        }

        _menu.AddItem("Show cabinet", ShowId);
        _menu.AddItem("Check LeetCode now", CheckId);
        _menu.AddItem("Change player…", PlayerId);
        _menu.AddSeparator();
        foreach (var (name, i) in new[] { "Small", "Medium", "Large" }.Select((n, i) => (n, i)))
            Radio($"Size: {name}", SizeId + i, Sizes[i] == size);
        _menu.AddSeparator();
        Check("Companion", CompanionId);
        Check("Speech bubbles", SpeechId);
        _menu.AddItem("Chat settings…", ChatId);
        _characters = [.. characters.Select(c => c.Id)];
        foreach (var ((id, name), i) in characters.Select((c, i) => (c, i)))
            Radio($"Companion: {name}", CharacterId + i, id == character);
        _menu.AddItem("Add a character…", AddId);
        _menu.AddItem("Open characters folder", FolderId);
        _menu.AddSeparator();
        Check("Mute sounds", MuteId);
        Check("In-game tickets", TicketsId);
        Check("Energy saver", EnergyId);
        Check("Always on top", TopId);
        Check("Start with Windows", StartupId);
        _menu.AddSeparator();
        _menu.AddItem("Quit", QuitId);
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
            case MuteId: MutedChanged?.Invoke(Toggle(MuteId)); break;
            case TicketsId: InGameTicketsChanged?.Invoke(Toggle(TicketsId)); break;
            case CompanionId: CompanionChanged?.Invoke(Toggle(CompanionId)); break;
            case SpeechId: SpeechChanged?.Invoke(Toggle(SpeechId)); break;
            case ChatId: ChatSettings?.Invoke(); break;
            case AddId: AddCharacter?.Invoke(); break;
            case FolderId: OpenCharacters?.Invoke(); break;
            case EnergyId: EnergySaverChanged?.Invoke(Toggle(EnergyId)); break;
            case QuitId: Quit?.Invoke(); break;
            case >= CharacterId when id < CharacterId + _characters.Length:
                for (var i = 0; i < _characters.Length; i++)
                    _menu.SetItemChecked(_menu.GetItemIndex(CharacterId + i), CharacterId + i == id);
                CharacterChanged?.Invoke(_characters[id - CharacterId]);
                break;
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
