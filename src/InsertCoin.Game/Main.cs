using System.Diagnostics;
using Godot;
using InsertCoin.Adapters;
using InsertCoin.Core;
using InsertCoin.Game.App;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;
using InsertCoin.Game.Skins;
using InsertCoin.Mind;

namespace InsertCoin.Game;

/// <summary>
/// Composition root and main loop: Steam check every 5 s, LeetCode every 60 s (or on CHECK), everything fed through
/// the ledger, events handed to the director, state saved on every change.
/// Debug builds add a dev console: F1/F2/F3 solve Easy/Medium/Hard, F5 fake game on/off, F6 time ×60,
/// F7 AFK on/off, F9 wipe state.
/// User args (after "--"): --data=DIR uses another profile folder, --no-steam ignores Steam,
/// --script=easy@2,hard@4,game@6,warp@6,afk@30,check@8,wipe@0,moment:cheer@3 plays dev actions at the given seconds.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class Main : Node2D
{
    private static readonly TimeSpan CheckCooldown = TimeSpan.FromSeconds(10);
    // Frames per second. Something on the cabinet always moves (lights, glows, the companion), so the window is
    // redrawn all the time: full pace when someone may be watching, a slow one while a game runs or to save energy.
    private const int FullPace = 60, SlowPace = 15;
    private static readonly string DefaultDataDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "InsertCoin");

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Rules _rules = new();
    private Ledger _ledger = null!;
    private LedgerState _state = null!;
    private JsonFileStore<LedgerState> _stateStore = null!;
    private SettingsFile _settingsFile = null!;
    private ITaskSource _tasks = null!;
    private IGameDetector _games = null!;
    private CabinetView _cabinet = null!;
    private Stool _stool = null!;
    private readonly Random _random = new();
    private TimeSpan _livelyUntil;
    private Director _director = null!;
    private DesktopWindow _window = null!;
    private TrayIcon _tray = null!;
    private Onboarding _onboarding = null!;
    private AudioBank _audio = null!;
    private AlertTicket _ticket = null!;
    private MusicApp _musicApp = null!;
    private CrtOs _crt = null!;
    private object? _carrier;                               // what a pull on the cabinet is carrying: a memo, or the screen's system
    private Reminders _reminders = null!;
    private KeyStore _elsewhereKeys = null!;                // where reminders go besides the cabinet: addresses and tokens, encrypted
    private ReminderSettings? _reminderSettings;
    private BalanceSettings? _balanceSettings;
    private Calendars _calendars = null!;
    private CalendarSettings? _calendarSettings;
    private static readonly System.Net.Http.HttpClient Couriered = new() { Timeout = TimeSpan.FromSeconds(15) };
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
        Paper.OffScreen = args.ContainsKey("offscreen");
        _settingsFile = new SettingsFile(new JsonFileStore<AppSettings>(Path.Combine(dataDir, "settings.json")));
        _stateStore = new JsonFileStore<LedgerState>(Path.Combine(dataDir, "state.json"));
        _rules = RulesOf(_settings);
        _state = _stateStore.Load() ?? LedgerState.New(DateTimeOffset.Now, _rules);
        _ledger = new Ledger(_rules);
        _tasks = new LeetCodeSource(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _games = args.ContainsKey("no-steam") ? new NoGames() : new SteamDetector(new WindowsSteamRegistry(), _settings.IgnoredSteamAppIds);

        var skin = CabinetSkin.Load(_settings.Skin);
        GetWindow().ContentScaleSize = (Vector2I)skin.Canvas;      // one canvas unit = one viewport unit, whatever the skin's size
        _cabinet = new CabinetView();
        AddChild(_cabinet);
        _cabinet.Build(skin);
        _audio = new AudioBank();
        AddChild(_audio);
        _audio.Build(_settings.Muted);
        _ticket = new AlertTicket();
        AddChild(_ticket);
        _window = new DesktopWindow();
        AddChild(_window);
        _window.Build(skin.Layers["base"], skin.Scale, skin.Canvas);
        _cabinet.Memos.WindowScale = () => _window.WindowScale;

        _stool = new Stool();
        AddChild(_stool);
        MoveChild(_stool, _cabinet.GetIndex() + 1);           // drawn over the cabinet art, under everything else
        _stool.Build(dataDir, _settingsFile, _cabinet, _window,
                     situation: () => Situation.Describe(_state, _rules, DateTimeOffset.Now, _settings.Username),
                     lively: Lively);
        _director = new Director(_cabinet, () => _stool.Her, _audio, _ticket, _rules, () => _settings.InGameTickets);
        _stool.Focus.TimeFocused += time => Apply(new Focused(time));      // focus pays by the minute, at once
        _stool.PayActivity = (name, size, share) => Apply(new ActivityDone(name, size, share)).OfType<ActivityPaid>().FirstOrDefault()?.Paid;
        _stool.PaidToday = name => _state.Today?.Activities.GetValueOrDefault(name) ?? 0;
        _stool.DayOf = moment => GameCalendar.DayOf(moment, _rules);
        _reminders = new Reminders(_stool.Store, _ticket, _audio, () => _settings, _stool.Remind);
        _elsewhereKeys = new KeyStore(Path.Combine(dataDir, "companion", "reminders.bin"));
        _calendars = new Calendars(_stool.Store, new KeyStore(Path.Combine(dataDir, "companion", "calendars.bin")));
        _reminders.Sent += notice => _ = SendOut(notice);
        _crt = new CrtOs();
        AddChild(_crt);
        _musicApp = new MusicApp(_stool.Music, _stool.Focus, _settingsFile);
        _crt.Build(_cabinet.Screen, [new FocusApp(_stool.Focus), new DoneApp(_stool.Activities, name => _state.Today?.Activities.GetValueOrDefault(name) ?? 0, _stool.LogFromScreen),
                                     _musicApp, new MemosApp(_stool.Memos), new AgendaApp(_stool.Store, _stool.OpenAgenda)],
                   _settings.CrtApps, order => SaveSettings(_settings with { CrtApps = order }), () => _window.WindowScale, _cabinet.Skin.Canvas);
        // a focus session counts down on the readout; what she would say in a full-screen game comes as a ticket
        _stool.Focus.Changed += () => _cabinet.Screen.Focus = _stool.Focus.Readout();
        _stool.Notice += (title, line) => _ticket.Show(title, line, 10);
        _window.Hover += p =>
        {
            var under = p is { } c && _crt.Covers(c) ? null : p;      // the lifted screen is over what is below it
            _cabinet.HoverCheck(under is { } q && _cabinet.HitsCheck(q));
            _stool.Memos.Hover(under);
            _crt.Hover(p);
        };
        // carried by hand: memos (off the slot, onto the side, off the cabinet), and on the screen, a swipe or an app being moved
        _window.Grabs = p => !_crt.Covers(p) && _stool.Memos.Grabs(p) || _crt.Grabs(p);
        _window.Pressed += _crt.Pressed;
        _window.Released += _crt.Released;
        _window.CarryStarted += p =>
        {
            if (!_crt.Covers(p) && _stool.Memos.Grabs(p))
            {
                _carrier = _stool.Memos;
                _stool.Memos.Take(p, p);
            }
            else
            {
                _carrier = _crt;
                _crt.CarryStarted(p);
            }
        };
        _window.Carried += p =>
        {
            if (_carrier == _stool.Memos)
                _stool.Memos.Carry(p);
            else if (_carrier == _crt)
                _crt.Carried(p);
        };
        _window.Dropped += p =>
        {
            if (_carrier == _stool.Memos)
                _stool.Memos.LetGo(p);
            else if (_carrier == _crt)
                _crt.Dropped(p);
            _carrier = null;
        };
        _window.AlsoSolid = p => _stool.Hits(p) || _crt.Covers(p);       // the screen, lifted out over her
        _window.PressedElsewhere += _crt.Away;
        _window.Covered = _stool.Memos.Covers;              // a memo's sheet over the cabinet
        _window.KeepsMouse = _stool.Typing;
        _window.Clicked += p =>
        {
            if (_crt.Lifted)
            {
                // on the lifted screen it is its; a memo on the cabinet opens and the screen stays up; anywhere else it settles back
                if (_crt.Covers(p) || !_stool.Memos.Clicked(p))
                    _crt.Clicked(p);
                return;
            }
            if (_cabinet.HitsCheck(p))
                _cabinet.PressCheck();
            else if (_stool.Memos.Clicked(p))
                return;                                     // a memo on the cabinet opens to read
            else if (_crt.Clicked(p))
                return;                                     // the screen's small system
            else if (_stool.Her?.Hits(p) == true)
                _stool.Clicked();
        };
        _cabinet.CheckPressed += OnCheck;
        ApplySize();
        PlaceWindow();
        _window.SetTopmost(_settings.AlwaysOnTop);

        _tray = new TrayIcon();
        AddChild(_tray);
        _tray.Build(_settings.AlwaysOnTop, Startup.IsEnabled(), _settings.Size, _settings.Muted, _settings.InGameTickets, _settings.ShowCompanion,
                    _settings.SpeechBubbles, _settings.Voice, _stool.Characters(), _settings.Companion, _settings.EnergySaver);
        _tray.EnergySaverChanged += on => { SaveSettings(_settings with { EnergySaver = on }); SetPace(); };
        _tray.MutedChanged += muted => { SaveSettings(_settings with { Muted = muted }); _audio.Muted = muted; _stool.Muted = muted; };
        _tray.InGameTicketsChanged += on => SaveSettings(_settings with { InGameTickets = on });
        _tray.CompanionChanged += _stool.SetCompanion;
        _tray.SpeechChanged += _stool.SetBubbles;
        _tray.VoiceChanged += _stool.SetVoice;
        _tray.ConversationChanged += _stool.Converse;
        _stool.ConversingChanged += _tray.SetConversation;
        _stool.MicChanged += on => _cabinet.Screen.Mic = on;
        _tray.ChatSettings += _stool.OpenChatSettings;
        _tray.VoiceSettings += _stool.OpenVoiceSettings;
        _tray.MemoryBook += _stool.OpenMemoryBook;
        _tray.WriteMemo += _stool.WriteMemo;
        _tray.ChatHistory += _stool.OpenChatHistory;
        _tray.Reminders += OpenReminderSettings;
        _tray.Balance += OpenBalanceSettings;
        _tray.Calendars += OpenCalendarSettings;
        _tray.AddCharacter += _stool.PickCharacter;
        _tray.OpenCharacters += _stool.OpenCharactersFolder;
        _tray.CharacterChanged += _stool.Seat;
        _stool.CharactersAdded += _tray.SetCharacters;
        _tray.SizeChanged += size => { SaveSettings(_settings with { Size = size }); ApplySize(); };
        _tray.ShowCabinet += () => _window.Rise(4, _settings.AlwaysOnTop);
        _tray.CheckNow += () => _cabinet.PressCheck();
        _tray.ChangePlayer += () => { _window.Rise(4, _settings.AlwaysOnTop); DisplayServer.WindowMoveToForeground(); _onboarding.Start(keepInitials: _settings.Initials); };
        _tray.AlwaysOnTopChanged += on => { SaveSettings(_settings with { AlwaysOnTop = on }); _window.SetTopmost(on); };
        _tray.StartWithWindowsChanged += Startup.Set;
        _tray.Quit += () => { Save(); GetTree().Quit(); };

        _onboarding = new Onboarding(_cabinet.Screen, _tasks, _audio);
        _onboarding.Completed += (initials, username) =>
        {
            SaveSettings(_settings with { Initials = initials, Username = username, SetUp = true });
            _cabinet.Screen.Flash($"HI {initials}", username is null ? "NO LEETCODE NAME" : $"PLAYER {username}".ToUpperInvariant(), 3.0);
            _cabinet.Lights.Burst(LightMode.Celebrate, 2.0);
            _audio.Play("hello");
            _backoffSeconds = 60;
            Poll();
        };
        AddTimer(0.5, _onboarding.Blink);
        if (!_settings.SetUp && string.IsNullOrWhiteSpace(_settings.Username))
            _onboarding.Start();

        _cabinet.Tray.Set(_state.Credits);
        AddTimer(5.0, Tick);
        AddTimer(5.0, () => _reminders.Tick(DateTimeOffset.Now));
        AddTimer(Calendars.Every.TotalSeconds, () => _ = _calendars.Sync());
        GetTree().CreateTimer(3).Timeout += () => _ = _calendars.Sync();     // soon after starting: the agenda in step
        AddTimer(1.0, RefreshScreen);
        AddTimer(5.0, () => { if (DateTimeOffset.Now >= _nextPoll) Poll(); });
        Tick();
        Poll();
        if (args.ContainsKey("diag"))
            AddTimer(2.0, () =>
            {
                var screen = DisplayServer.WindowGetCurrentScreen();
                GD.Print($"diag screen={screen} scale={DisplayServer.ScreenGetScale(screen)} dpi={DisplayServer.ScreenGetDpi(screen)} " +
                         $"screenSize={DisplayServer.ScreenGetSize(screen)} screenPos={DisplayServer.ScreenGetPosition(screen)} " +
                         $"winPos={DisplayServer.WindowGetPosition()} winSize={DisplayServer.WindowGetSize()} " +
                         $"viewport={GetViewport().GetVisibleRect().Size} contentScale={GetWindow().ContentScaleFactor} " +
                         $"stretch={GetViewport().GetStretchTransform().Scale}");
            });
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
        // while playing every minute; otherwise every ten, for the day's record (when the player was about)
        if (DateTimeOffset.Now - _lastSave > TimeSpan.FromMinutes(_state.Session is not null ? 1 : 10))
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
        _audio.Play("check");
        _cabinet.Screen.Flash("CHECK", string.IsNullOrWhiteSpace(_settings.Username) ? "NO LEETCODE NAME" : "ASKING LEETCODE", 1.5);
        _backoffSeconds = 60;
        Poll();
    }

    private IReadOnlyList<LedgerEvent> Apply(LedgerInput input)
    {
        var result = _ledger.Step(_state, input, DateTimeOffset.Now);
        _state = result.State;
        _stool.GameRunning = _state.Session is not null;      // her voice and its training give the game the graphics card
        if (result.Events.Count > 0)
            Lively(6);
        foreach (var e in result.Events)
            _director.Play(e);
        _stool.Witness(result.Events, _rules);
        if (Lines.For(result.Events, _state, _rules, _random) is { } line && !_stool.Answering)
            _stool.Say(line);
        // Moments worth seeing rise above other windows for a few seconds — never over a running game.
        if (_state.Session is null && result.Events.Any(e => e is CoinsEarned or FocusPaid or ActivityPaid or SessionEnded or Faded))
            _window.Rise(6, _settings.AlwaysOnTop);
        if (result.Events.Count > 0 || input is PlayerSet)
            Save();
        RefreshScreen();
        return result.Events;
    }

    // ---- Presentation ----------------------------------------------------------------------------------------------

    private void RefreshScreen()
    {
        var s = _state;
        var playing = s.Session is not null && !s.Paused;
        // Between 5 s ticks, run the clock locally so the countdown moves every second.
        var late = GameCalendar.IsLateNight(s, DateTimeOffset.Now, _rules) ? _rules.LateNightMultiplier : 1;
        var since = playing ? (long)((_clock.Elapsed - _lastTick).TotalMilliseconds * (_devWarp ? 60 : 1) * late) : 0;
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
            TodayEarnedMs = s.Today?.EarnedMs ?? 0,
            TodayCapMs = s.Today?.CapMs ?? 0,
            TodayFocusedMs = s.Today?.FocusedMs ?? 0,
            TodayPlayedMs = s.Today?.PlayedMs ?? 0,
            Initials = _settings.Initials,
        });
        var mood = shownDebt > 0 && shownLeft == 0 ? TrayMood.Debt : shownLeft < 15 * 60_000 ? TrayMood.Low : TrayMood.Fine;
        var streak = GameCalendar.CurrentStreak(s, DateTimeOffset.Now, _rules);
        _tray.Set(mood, mood == TrayMood.Debt
            ? $"INSERT COIN — {shownDebt / 60_000} min owed ({s.IouTickets(_rules)} IOU)"
            : $"INSERT COIN — {shownLeft / 60_000} min left · {s.Credits} credits" + (streak > 0 ? $" · combo x{streak}" : ""));
        var power = s.Paused ? 0.3f : s.Session is not null ? 1f : 0.7f;
        _cabinet.SetScreenPower(power);
        _stool.Her?.SetScreenLight(power);
        SetPace();
        _cabinet.Printer.SetPartial(shownLeft == 0 && shownDebt > 0 ? shownDebt % _rules.TokenMs / (float)_rules.TokenMs : 0, animate: true);
        if (s.Session is not null && shownLeft == 0)
            _cabinet.Lights.SetModeIfNot(LightMode.Alarm);
        else
            _cabinet.Lights.SetModeIfNot(LightMode.Chase, onlyFrom: LightMode.Alarm);
    }

    private string StatusLine()
    {
        var late = GameCalendar.IsLateNight(_state, DateTimeOffset.Now, _rules);
        if (_state.Paused) return "PAUSED - AFK";
        if (_state.Session is not null && late) return "LATE NIGHT - PLAY x1.5";
        if (_state.Session is { } session) return "GAME: " + string.Join(" + ", session.GameNames).ToUpperInvariant();
        if (late) return "PAST BEDTIME";
        if (_noSteam) return "NO STEAM";
        if (string.IsNullOrWhiteSpace(_settings.Username)) return $"PLAYER {_settings.Initials}";
        if (_linkDown) return "LINK DOWN - RETRYING";
        return (_settings.Username ?? "").ToUpperInvariant();
    }

    // ---- Persistence & window ----------------------------------------------------------------------------------------

    private void Save()
    {
        _stateStore.Save(_state);
        _lastSave = DateTimeOffset.Now;
    }

    /// <summary>Small / Medium / Large = 60 / 80 / 100 % of the skin's canvas.</summary>
    private void ApplySize()
    {
        var scale = _settings.Size switch { "S" => 0.6f, "L" => 1.0f, _ => 0.8f };
        var size = (Vector2I)(_cabinet.Skin.Canvas * scale).Round();
        var bottomRight = DisplayServer.WindowGetPosition() + DisplayServer.WindowGetSize();
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(bottomRight - size);      // grow/shrink from the feet, not the top-left
    }

    /// <summary>Picks the frame rate: slow while a game runs or when saving energy, full otherwise.</summary>
    private void SetPace()
    {
        // Lights and glows animate all the time, so without a cap the window redraws at the monitor's rate.
        var slow = _state.Session is not null                                       // a game is running: stay out of its way
                   || (_settings.EnergySaver && _clock.Elapsed > _livelyUntil);
        Engine.MaxFps = slow ? SlowPace : FullPace;
    }

    /// <summary>Something worth watching is about to happen: full pace for a few seconds even when saving energy.</summary>
    private void Lively(double seconds)
    {
        _livelyUntil = _clock.Elapsed + TimeSpan.FromSeconds(seconds);
        SetPace();
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
        if (pos.X != _settings.WindowX || pos.Y != _settings.WindowY)
            SaveSettings(_settings with { WindowX = pos.X, WindowY = pos.Y });
    }

    private AppSettings _settings => _settingsFile.Now;

    private void SaveSettings(AppSettings settings) => _settingsFile.Save(settings);

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            if (_stateStore is not null)
                Save();
            _stool?.Shutdown();
            _singleInstance?.Dispose();
        }
    }

    // ---- Input & dev console -----------------------------------------------------------------------------------------

    public override void _Input(InputEvent e)
    {
        if (e is InputEventJoypadButton || e is InputEventJoypadMotion { AxisValue: > 0.3f or < -0.3f })
            _lastJoypad = DateTimeOffset.Now;
        if (e is InputEventKey { Pressed: true } typed && _onboarding.Active)
        {
            _onboarding.Key(typed);
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.Escape && _stool.CloseSlip())
        {
            GetViewport().SetInputAsHandled();
            return;
        }
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
        if (_stool.Dev(action))
            return;
        if (action.StartsWith("musiclink:"))
        {
            _ = _musicApp.Save(action["musiclink:".Length..]);     // musiclink:<link> as if added in the MUSIC app
            return;
        }
        if (action.StartsWith("crt"))
        {
            var parts = action.Split(':');
            switch (parts[0])
            {
                case "crt": _crt.DevOpen(parts.Length > 1 ? parts[1] : null); break;                                // crt opens the system; crt:memos an app
                case "crtclick": _crt.DevClickScreen(new Vector2(float.Parse(parts[1]), float.Parse(parts[2]))); break;   // crtclick:x:y, in the screen's pixels
                case "crtmove": _crt.DevMove(int.Parse(parts[1]), int.Parse(parts[2])); break;                        // crtmove:0:1 moves an app, as carrying it would
                case "crtclose": _crt.Close(); break;
                case "crtaway": _crt.Away(); break;                                                                   // a press away from the lifted screen
            }
            return;
        }
        if (action.StartsWith("type:"))
        {
            foreach (var c in action["type:".Length..])
                _onboarding.Key(new InputEventKey { Pressed = true, Unicode = c });
            return;
        }
        if (action.StartsWith("remind:"))                   // remind:text goes off now, as the reminder settings' "Try it"
        {
            _reminders.Try(action["remind:".Length..].Replace('_', ' '), DateTimeOffset.Now);              // underscores for spaces
            return;
        }
        if (action.StartsWith("pastplan:") && action.Split(':', 3) is [_, var ago, var thing])
        {
            // pastplan:minutes:text puts something on the agenda that ended that many minutes ago (told about yesterday)
            var ended = DateTimeOffset.Now.AddMinutes(-double.Parse(ago, System.Globalization.CultureInfo.InvariantCulture));
            _stool.Store.AddPlan(thing.Replace('_', ' '), ended, allDay: false, before: null, ended.AddDays(-1), null);
            return;
        }
        if (action.StartsWith("calendar:"))                 // calendar:URL links a calendar named Test (a local test server's .ics)
        {
            _calendars.Add(new CalendarLink("Test", "link", action["calendar:".Length..]));
            return;
        }
        if (action.StartsWith("ticketpress:"))              // ticketpress:0 presses the showing ticket's first button (Snooze), 1 the next (OK)
        {
            _ticket.DevPress(int.Parse(action["ticketpress:".Length..]));
            return;
        }
        if (action.StartsWith("elsewhere:"))
        {
            // elsewhere:http://127.0.0.1:port points ntfy (topic "test") and the webhook (/hook) at a local test server
            var server = action["elsewhere:".Length..];
            SaveSettings(_settings with { ReminderElsewhere = new Elsewhere { Ntfy = true, NtfyServer = server, Webhook = true } });
            _elsewhereKeys.Save(new ElsewhereSecrets { NtfyTopic = "test", WebhookUrl = server + "/hook" }.ToJson());
            return;
        }
        if (action.StartsWith("focuspaid:"))
        {
            // focuspaid:minutes pays as a focus part of that length would
            Apply(new Focused(TimeSpan.FromMinutes(double.Parse(action["focuspaid:".Length..], System.Globalization.CultureInfo.InvariantCulture))));
            return;
        }
        if (action.StartsWith("bedtime:"))
        {
            // bedtime:HHMM sets the player's bedtime, for today too (as if it had been set yesterday)
            var bedtime = action["bedtime:".Length..].Insert(2, ":");
            SaveSettings(_settings with { Bedtime = bedtime });
            UseRules();
            _state = _state with { Today = _state.Today! with { Bedtime = _rules.Bedtime } };
            RefreshScreen();
            return;
        }
        if (action.StartsWith("remindin:") && action.Split(':', 3) is [_, var after, var what])
        {
            // remindin:seconds:text sets a reminder that far ahead (negative: one missed while the cabinet was closed)
            var at = DateTimeOffset.Now.AddSeconds(double.Parse(after, System.Globalization.CultureInfo.InvariantCulture));
            _stool.Store.AddPlan(what.Replace('_', ' '), at, allDay: false, TimeSpan.Zero, at < DateTimeOffset.Now ? at.AddMinutes(-1) : DateTimeOffset.Now, null);   // set before it came due
            return;
        }
        switch (action)
        {
            case "enter": _onboarding.Key(new InputEventKey { Pressed = true, Keycode = Key.Enter }); break;
            case "player": _onboarding.Start(keepInitials: _settings.Initials); break;
            case "ticket": _ticket.Show("5:00 LEFT", "Your last token is burning"); break;
            case "remindersettings": OpenReminderSettings(); break;
            case "calendars": OpenCalendarSettings(); break;
            case "remindertry": if (IsInstanceValid(_reminderSettings)) _reminderSettings!.DevTry(); break;
            case "easy": DevSolve(Difficulty.Easy); break;
            case "medium": DevSolve(Difficulty.Medium); break;
            case "hard": DevSolve(Difficulty.Hard); break;
            case "game": _devGame = !_devGame; Tick(); break;
            case "balance": OpenBalanceSettings(); break;
            case "nextday":
                // as if the night had passed: the next check turns the day (half of the unplayed time fades)
                _state = _state with { Today = _state.Today! with { Day = _state.Today.Day.AddDays(-1) } };
                Tick();
                break;
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

    /// <summary>A reminder going off, sent beyond the cabinet as the player chose: a Windows notification, their phone…</summary>
    private async Task SendOut(Notice notice)
    {
        if (_settings.ReminderWindows)
            WindowsNotice.Show(notice.Title, notice.Text);
        if (!_settings.ReminderElsewhere.Any)
            return;
        foreach (var (name, error) in await Couriers.Send(Couriered, _settings.ReminderElsewhere, ElsewhereSecrets.FromJson(_elsewhereKeys.Load()), notice))
            if (error is not null)
                GD.PrintErr($"Reminders: could not reach {name}: {error}");
    }

    private void OpenCalendarSettings()
    {
        if (IsInstanceValid(_calendarSettings))
        {
            _calendarSettings!.GrabFocus();
            return;
        }
        _calendarSettings = new CalendarSettings();
        AddChild(_calendarSettings);
        _calendarSettings.Build(_calendars);
        _calendarSettings.Open();
    }

    /// <summary>The rules the player set (caps, bedtime, focus's rate); a new day takes them up, today keeps the ones it began with.</summary>
    private static Rules RulesOf(AppSettings s) => new()
    {
        WeekdayCapMs = s.WeekdayCapMinutes * 60_000L,
        FridayCapMs = s.FridayCapMinutes * 60_000L,
        WeekendCapMs = s.WeekendCapMinutes * 60_000L,
        Bedtime = TimeOnly.TryParseExact(s.Bedtime, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var bedtime)
            ? bedtime.ToTimeSpan() : null,
        FocusPerPlay = Math.Max(1, s.FocusPerPlay),
    };

    private void UseRules()
    {
        _rules = RulesOf(_settings);
        _ledger = new Ledger(_devWarp ? _rules with { MaxTick = TimeSpan.FromMinutes(10) } : _rules);
    }

    private void OpenBalanceSettings()
    {
        if (IsInstanceValid(_balanceSettings))
        {
            _balanceSettings!.GrabFocus();
            return;
        }
        _balanceSettings = new BalanceSettings();
        AddChild(_balanceSettings);
        var today = _state.Today is { } t
            ? $"Today: {t.EarnedMs / 60_000} of {t.CapMs / 60_000} minutes earned, " + (t.Bedtime is { } b ? $"bedtime {b.Hours:00}:{b.Minutes:00}." : "no bedtime.")
            : "";
        _balanceSettings.Build(_settings, today);
        _balanceSettings.Saved += (weekday, friday, weekend, bedtime) =>
        {
            SaveSettings(_settings with { WeekdayCapMinutes = weekday, FridayCapMinutes = friday, WeekendCapMinutes = weekend, Bedtime = bedtime });
            UseRules();
        };
        _balanceSettings.Open();
    }

    private void OpenReminderSettings()
    {
        if (IsInstanceValid(_reminderSettings))
        {
            _reminderSettings!.GrabFocus();
            return;
        }
        _reminderSettings = new ReminderSettings();
        AddChild(_reminderSettings);
        _reminderSettings.Build(ReminderChoices.Of(_settings), ElsewhereSecrets.FromJson(_elsewhereKeys.Load()));
        _reminderSettings.Tried += chime => _reminders.Try("This is how a reminder reaches you.", DateTimeOffset.Now, chime);
        _reminderSettings.Saved += (choices, secrets) =>
        {
            SaveSettings(choices.Into(_settings));
            _elsewhereKeys.Save(secrets == new ElsewhereSecrets() ? null : secrets.ToJson());
        };
        _reminderSettings.Open();
    }

    private void AddTimer(double seconds, Action action)
    {
        var timer = new Godot.Timer { WaitTime = seconds, Autostart = true };
        timer.Timeout += action;
        AddChild(timer);
    }
}
