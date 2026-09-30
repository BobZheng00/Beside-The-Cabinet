using System.Diagnostics;
using Godot;
using InsertCoin.Adapters;
using InsertCoin.Core;
using InsertCoin.Game.App;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Desktop;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game;

/// <summary>
/// Composition root and main loop: Steam check every 5 s, LeetCode every 60 s (or on CHECK), everything fed through
/// the ledger, events handed to the director, state saved on every change.
/// Debug builds add a dev console: F1/F2/F3 solve Easy/Medium/Hard, F5 fake game on/off, F6 time ×60,
/// F7 AFK on/off, F9 wipe state.
/// User args (after "--"): --data=DIR uses another profile folder, --no-steam ignores Steam,
/// --script=easy@2,hard@4,game@6,warp@6,afk@30,check@8,wipe@0 plays dev actions at the given seconds.
/// </summary>
public partial class Main : Node2D
{
    private static readonly TimeSpan CheckCooldown = TimeSpan.FromSeconds(10);
    private static readonly string DefaultDataDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "InsertCoin");

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Rules _rules = new();
    private Ledger _ledger = null!;
    private LedgerState _state = null!;
    private AppSettings _settings = null!;
    private JsonFileStore<LedgerState> _stateStore = null!;
    private JsonFileStore<AppSettings> _settingsStore = null!;
    private ITaskSource _tasks = null!;
    private IGameDetector _games = null!;
    private CabinetView _cabinet = null!;
    private Director _director = null!;
    private DesktopWindow _window = null!;
    private System.Threading.Mutex? _singleInstance;

    private TimeSpan _lastTick;
    private DateTimeOffset _lastSave, _lastCheck = DateTimeOffset.MinValue, _nextPoll = DateTimeOffset.MinValue;
    private DateTimeOffset _lastJoypad = DateTimeOffset.MinValue;
    private int _backoffSeconds = 60;
    private bool _polling, _noSteam, _linkDown;
    private bool _devGame, _devWarp, _devAfk;

    public override void _Ready()
    {
        _singleInstance = new System.Threading.Mutex(true, "InsertCoin.Cabinet", out var first);
        if (!first)
        {
            GetTree().Quit();
            return;
        }

        var args = OS.GetCmdlineUserArgs().Select(a => a.TrimStart('-').Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        var dataDir = args.TryGetValue("data", out var dir) && dir.Length > 0 ? dir : DefaultDataDir;
        _settingsStore = new JsonFileStore<AppSettings>(Path.Combine(dataDir, "settings.json"));
        _stateStore = new JsonFileStore<LedgerState>(Path.Combine(dataDir, "state.json"));
        _settings = _settingsStore.Load() ?? new AppSettings();
        _state = _stateStore.Load() ?? LedgerState.New(DateTimeOffset.Now, _rules);
        _ledger = new Ledger(_rules);
        _tasks = new LeetCodeSource(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _games = args.ContainsKey("no-steam") ? new NoGames() : new SteamDetector(new WindowsSteamRegistry(), _settings.IgnoredSteamAppIds);

        var skin = CabinetSkin.Load(_settings.Skin);
        _cabinet = new CabinetView();
        AddChild(_cabinet);
        _cabinet.Build(skin);
        _director = new Director(_cabinet);

        _window = new DesktopWindow();
        AddChild(_window);
        _window.Build(skin.Layers["base"], skin.Scale, skin.Canvas);
        _window.Hover += p => _cabinet.HoverCheck(p is { } q && _cabinet.HitsCheck(q));
        _window.Clicked += p => { if (_cabinet.HitsCheck(p)) _cabinet.PressCheck(); };
        _cabinet.CheckPressed += OnCheck;
        PlaceWindow();

        _cabinet.Tray.Set(_state.Credits);
        AddTimer(5.0, Tick);
        AddTimer(1.0, RefreshScreen);
        AddTimer(5.0, () => { if (DateTimeOffset.Now >= _nextPoll) Poll(); });
        Tick();
        Poll();
        if (args.TryGetValue("script", out var script))
            foreach (var step in script.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('@')))
                GetTree().CreateTimer(double.Parse(step[1], System.Globalization.CultureInfo.InvariantCulture)).Timeout += () => Dev(step[0]);
    }

    // ---- Loop ----------------------------------------------------------------------------------------------------

    private void Tick()
    {
        var now = _clock.Elapsed;
        var elapsed = (now - _lastTick) * (_devWarp ? 60 : 1);
        _lastTick = now;

        var detected = _games.RunningGames();
        _noSteam = detected is null;
        var games = (detected ?? []).ToList();
        if (_devGame)
            games.Add(new RunningGame("dev", "Dev Game"));

        var sinceJoypad = DateTimeOffset.Now - _lastJoypad;
        var idle = _devAfk ? TimeSpan.FromHours(1) : UserActivity.IdleFor() < sinceJoypad ? UserActivity.IdleFor() : sinceJoypad;

        Apply(new Tick(games, elapsed, idle));
        if (_state.Session is not null && DateTimeOffset.Now - _lastSave > TimeSpan.FromMinutes(1))
            Save();
        SaveWindowPosition();
    }

    private async void Poll()
    {
        if (_polling)
            return;
        if (string.IsNullOrWhiteSpace(_settings.Username))
        {
            _nextPoll = DateTimeOffset.Now.AddSeconds(60);
            return;
        }
        _polling = true;
        var user = _settings.Username!;
        try
        {
            var recent = await _tasks.FetchRecentAsync(user, CancellationToken.None);
            Apply(_state.Username == user ? new TasksObserved(recent) : new PlayerSet(user, recent));
            _linkDown = false;
            _backoffSeconds = 60;
        }
        catch (TaskSourceUnavailableException e)
        {
            GD.PrintErr($"LeetCode: {e.Message}");
            _linkDown = true;
            _backoffSeconds = Math.Min(_backoffSeconds * 2, 900);
        }
        finally
        {
            _polling = false;
            _nextPoll = DateTimeOffset.Now.AddSeconds(_backoffSeconds);
            RefreshScreen();
        }
    }

    private void OnCheck()
    {
        if (DateTimeOffset.Now - _lastCheck < CheckCooldown)
            return;
        _lastCheck = DateTimeOffset.Now;
        _cabinet.Screen.Flash("CHECK", string.IsNullOrWhiteSpace(_settings.Username) ? "NO PLAYER SET" : "ASKING LEETCODE…", 1.5);
        _backoffSeconds = 60;
        Poll();
    }

    private void Apply(LedgerInput input)
    {
        var result = _ledger.Step(_state, input, DateTimeOffset.Now);
        _state = result.State;
        foreach (var e in result.Events)
            _director.Play(e);
        if (result.Events.Count > 0 || input is PlayerSet)
            Save();
        RefreshScreen();
    }

    // ---- Presentation ----------------------------------------------------------------------------------------------

    private void RefreshScreen()
    {
        var s = _state;
        var playing = s.Session is not null && !s.Paused;
        // Between 5 s ticks, run the clock locally so the countdown moves every second.
        var since = playing ? (long)((_clock.Elapsed - _lastTick).TotalMilliseconds * (_devWarp ? 60 : 1)) : 0;
        var left = s.TimeLeftMs(_rules);
        var shownLeft = Math.Max(0, left - since);
        var shownDebt = s.DebtMs + (left == 0 ? (long)(since * _rules.DebtMultiplier) : 0);

        _cabinet.Screen.Show(new ScreenState
        {
            TimeLeftMs = shownLeft,
            DebtMs = shownDebt,
            Credits = s.Credits,
            Fuel = Math.Max(0, s.FuelMs - since) / (float)_rules.TokenMs,
            Streak = GameCalendar.CurrentStreak(s, DateTimeOffset.Now, _rules),
            BestStreak = s.BestStreak,
            Playing = s.Session is not null,
            Status = StatusLine(),
            WeekSolves = s.Week.Solves,
            WeekTokens = s.Week.TokensEarned,
            WeekPlayedMs = s.Week.PlayedMs,
        });
        _cabinet.SetScreenPower(s.Paused ? 0.3f : s.Session is not null ? 1f : 0.7f);
        _cabinet.Printer.SetPartial(shownLeft == 0 && shownDebt > 0 ? shownDebt % _rules.TokenMs / (float)_rules.TokenMs : 0, animate: true);
        if (s.Session is not null && shownLeft == 0)
            _cabinet.Lights.SetModeIfNot(LightMode.Alarm);
        else
            _cabinet.Lights.SetModeIfNot(LightMode.Chase, onlyFrom: LightMode.Alarm);
    }

    private string StatusLine()
    {
        if (_state.Paused) return "PAUSED · AFK";
        if (_state.Session is { } session) return "GAME: " + string.Join(" + ", session.GameNames).ToUpperInvariant();
        if (_noSteam) return "NO STEAM";
        if (string.IsNullOrWhiteSpace(_settings.Username)) return "NO PLAYER · SEE SETTINGS";
        if (_linkDown) return "LINK DOWN · RETRYING";
        return (_settings.Username ?? "").ToUpperInvariant();
    }

    // ---- Persistence & window ----------------------------------------------------------------------------------------

    private void Save()
    {
        _stateStore.Save(_state);
        _lastSave = DateTimeOffset.Now;
    }

    private void PlaceWindow()
    {
        var size = DisplayServer.WindowGetSize();
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
        var fallback = usable.End - size - new Vector2I(40, 80);
        DisplayServer.WindowSetPosition(_settings.WindowX is int x && _settings.WindowY is int y ? new Vector2I(x, y) : fallback);
    }

    private void SaveWindowPosition()
    {
        var pos = DisplayServer.WindowGetPosition();
        if (pos.X == _settings.WindowX && pos.Y == _settings.WindowY)
            return;
        _settings = _settings with { WindowX = pos.X, WindowY = pos.Y };
        _settingsStore.Save(_settings);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            if (_stateStore is not null)
                Save();
            _singleInstance?.Dispose();
        }
    }

    // ---- Input & dev console -----------------------------------------------------------------------------------------

    public override void _Input(InputEvent e)
    {
        if (e is InputEventJoypadButton || e is InputEventJoypadMotion { AxisValue: > 0.3f or < -0.3f })
            _lastJoypad = DateTimeOffset.Now;
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.Escape)
        {
            Save();
            GetTree().Quit();
            return;
        }
        if (!OS.IsDebugBuild())
            return;
        var action = key.Keycode switch
        {
            Key.F1 => "easy", Key.F2 => "medium", Key.F3 => "hard", Key.F5 => "game", Key.F6 => "warp", Key.F7 => "afk", Key.F9 => "wipe",
            _ => null,
        };
        if (action is not null)
            Dev(action);
    }

    private void Dev(string action)
    {
        switch (action)
        {
            case "easy": DevSolve(Difficulty.Easy); break;
            case "medium": DevSolve(Difficulty.Medium); break;
            case "hard": DevSolve(Difficulty.Hard); break;
            case "game": _devGame = !_devGame; Tick(); break;
            case "warp":
                _devWarp = !_devWarp;
                _ledger = new Ledger(_devWarp ? _rules with { MaxTick = TimeSpan.FromMinutes(10) } : _rules);
                _cabinet.Screen.Flash(_devWarp ? "x60" : "x1", "DEV CLOCK", 1.5);
                break;
            case "afk": _devAfk = !_devAfk; Tick(); break;
            case "check": _cabinet.PressCheck(); break;
            case "wipe":
                _state = LedgerState.New(DateTimeOffset.Now, _rules) with { Username = _settings.Username };
                _cabinet.Tray.Set(0);
                Save();
                RefreshScreen();
                break;
        }
    }

    private void DevSolve(Difficulty difficulty) =>
        Apply(new TasksObserved([new SolvedTask($"dev-{Guid.NewGuid():N}", $"Dev {difficulty} Problem", difficulty, DateTimeOffset.Now)]));

    private sealed class NoGames : IGameDetector
    {
        public IReadOnlyList<RunningGame> RunningGames() => [];
    }

    private void AddTimer(double seconds, Action action)
    {
        var timer = new Godot.Timer { WaitTime = seconds, Autostart = true };
        timer.Timeout += action;
        AddChild(timer);
    }
}
