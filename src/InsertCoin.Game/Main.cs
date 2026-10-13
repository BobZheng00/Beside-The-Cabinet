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
    private Calendars _calendars = null!;
    private SettingsWindow? _settingsWindow;
    private Welcome? _welcome;
    private Texting _texting = null!;
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
                     situation: () => Situation.Describe(_state, _rules, DateTimeOffset.Now),
                     lively: Lively);
        _director = new Director(_cabinet, () => _stool.Her, _audio, _ticket, _rules, () => _settings.InGameTickets);
        _stool.Focus.TimeFocused += time => Apply(new Focused(time));      // focus pays by the minute, at once
        _stool.PayActivity = (name, size, share) => Apply(new ActivityDone(name, size, share)).OfType<ActivityPaid>().FirstOrDefault()?.Paid;
        _stool.PaidToday = name => _state.Today?.Activities.GetValueOrDefault(name) ?? 0;
        _stool.DayOf = moment => GameCalendar.DayOf(moment, _rules);
        _stool.Ledger = () => _state;
        _stool.Rules = () => _rules;
        _stool.RulesChanged = UseRules;
        _reminders = new Reminders(_stool.Store, _ticket, _audio, () => _settings, _stool.Remind);
        _elsewhereKeys = new KeyStore(Path.Combine(dataDir, "companion", "reminders.bin"));
        _calendars = new Calendars(_stool.Store, new KeyStore(Path.Combine(dataDir, "companion", "calendars.bin")));
        _reminders.Sent += SendOut;
        // where the player is goes by their input, a game pad's too; what reaches them away goes to their phone
        _stool.Idle = Idle;
        _stool.TextOut = Text;
        _stool.CanText = () => _settings.ReminderElsewhere.Any;
        // texting with her on Telegram: a bot already paired (or set up before pairing existed, with its chat) is listened to
        _texting = new Texting();
        AddChild(_texting);
        _stool.Phone = _texting;
        _texting.Texted += _stool.Texted;
        _texting.Paired += (token, chat) =>
        {
            _elsewhereKeys.Save((ElsewhereSecrets.FromJson(_elsewhereKeys.Load()) with { TelegramToken = token, TelegramChat = chat }).ToJson());
            SaveSettings(_settings with { ReminderElsewhere = _settings.ReminderElsewhere with { Telegram = true } });
            _ = _stool.SayHello();
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.RefreshPage("phone");
        };
        if (_settings.ReminderElsewhere.Telegram && ElsewhereSecrets.FromJson(_elsewhereKeys.Load()) is { TelegramToken: { } bot, TelegramChat: { } chat })
            _texting.Use(bot, chat);
        _crt = new CrtOs();
        AddChild(_crt);
        _musicApp = new MusicApp(_stool.Music, _stool.Focus, _settingsFile);
        _crt.Build(_cabinet.Screen, [new FocusApp(_stool.Focus), new DoneApp(_stool.Activities, name => _state.Today?.Activities.GetValueOrDefault(name) ?? 0, _stool.LogFromScreen),
                                     _musicApp, new MemosApp(_stool.Memos), new AgendaApp(_stool.Store, _stool.OpenAgenda)],
                   _settings.CrtApps, order => SaveSettings(_settings with { CrtApps = order }), () => _window.WindowScale, _cabinet.Skin.Canvas);
        // a focus session counts down on the readout; what she would say in a full-screen game comes as a ticket
        _stool.Focus.Changed += () => _cabinet.Screen.Focus = _stool.Focus.Readout();
        _stool.Notice += (title, line) => _ticket.Show(title, line, 10);
        _stool.Ticket = _ticket;
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
            else if (_stool.Her?.Hits(p) == true || _stool.OnBubble(p))
                _stool.Clicked(p);
        };
        _cabinet.CheckPressed += OnCheck;
        ApplySize();
        PlaceWindow();
        _window.SetTopmost(_settings.AlwaysOnTop);

        _tray = new TrayIcon();
        AddChild(_tray);
        _tray.Build(_settings.Muted, _settings.Voice);
        _settingsFile.Changed += () =>
        {
            _tray.Sync(_settings.Muted, _settings.Voice);   // flipped in Settings too
            _tray.ShowStart(StartLeft());
        };
        _tray.MutedChanged += SetMuted;
        _tray.VoiceChanged += _stool.SetVoice;
        _tray.ConversationChanged += _stool.Converse;
        _stool.ConversingChanged += _tray.SetConversation;
        _stool.MicChanged += on => _cabinet.Screen.Mic = on;
        _tray.Settings += () => OpenSettings(null);
        _tray.GettingStarted += () => OpenSettings("start");
        _stool.OpenSettings += OpenSettings;
        _stool.SettingsChanged += page =>
        {
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.RefreshPage(page);
        };
        _tray.MemoryBook += _stool.OpenMemoryBook;
        _tray.WriteMemo += _stool.WriteMemo;
        _tray.ChatHistory += _stool.OpenChatHistory;
        _tray.ShowCabinet += () => _window.Rise(4, _settings.AlwaysOnTop);
        _tray.CheckNow += () => _cabinet.PressCheck();
        _tray.Quit += () => { Save(); GetTree().Quit(); };

        _onboarding = new Onboarding(_cabinet.Screen, _tasks, _audio);
        _onboarding.Completed += username =>
        {
            SaveSettings(_settings with { Username = username });
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.RefreshPage("balance");     // the LeetCode name shown there
            _cabinet.Screen.Flash($"HI {_settings.Initials}", username is null ? "NO LEETCODE NAME" : $"PLAYER {username}".ToUpperInvariant(), 3.0);
            _cabinet.Lights.Burst(LightMode.Celebrate, 2.0);
            _audio.Play("hello");
            _backoffSeconds = 60;
            Poll();
        };
        AddTimer(0.5, _onboarding.Blink);
        if (!_settings.SetUp && string.IsNullOrWhiteSpace(_settings.Username))
            GetTree().CreateTimer(1.0).Timeout += OpenWelcome;          // the cabinet first, then the welcome beside it

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

        Apply(new Tick(games, elapsed, Idle()));
        _tray.ShowStart(StartLeft());                       // her voice coming up, a calendar linked: not settings
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
        _stool.Record(_state);
        if (Lines.Remark(result.Events, _state, _rules, _random, DateTimeOffset.Now) is { } remark)
            _stool.Hand(remark);                            // in her own words, once she is free
        // Moments worth seeing rise above other windows for a few seconds — never over a running game.
        if (_state.Session is null && result.Events.Any(e => e is CoinsEarned or FocusPaid or ActivityPaid or StreakStar or SessionEnded or Faded))
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
            ? $"Beside the Cabinet — {shownDebt / 60_000} min owed ({s.IouTickets(_rules)} IOU)"
            : $"Beside the Cabinet — {shownLeft / 60_000} min left · {s.Credits} credits" + (streak > 0 ? $" · combo x{streak}" : ""));
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
            Key.F1 => "easy", Key.F2 => "medium", Key.F3 => "hard", Key.F4 => "rest:ask", Key.F5 => "game", Key.F6 => "warp", Key.F7 => "afk", Key.F8 => "standup", Key.F9 => "wipe", Key.F10 => "upkeep",
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
        if (action == "settings" || action.StartsWith("settings:"))
        {
            OpenSettings(action.Length > "settings:".Length ? action["settings:".Length..] : null);     // settings:page opens it at that page
            return;
        }
        if (action.StartsWith("welcomedo:"))                // welcomedo:label[:value]: as settingsdo, on the welcome
        {
            var parts = action["welcomedo:".Length..].Replace('_', ' ').Split(':', 2);
            if (IsInstanceValid(_welcome))
                _welcome!.DevDo(parts[0], parts.Length > 1 ? parts[1] : null);
            return;
        }
        if (action.StartsWith("settingsdo:"))
        {
            // settingsdo:label presses the button or flips the tick with that text on the page showing; settingsdo:label:value
            // sets the choice (by number) or the field after the label
            var parts = action["settingsdo:".Length..].Replace('_', ' ').Split(':', 2);
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.DevDo(parts[0], parts.Length > 1 ? parts[1] : null);
            return;
        }
        if (action.StartsWith("settingsscroll:"))         // settingsscroll:600 scrolls the page showing that far down
        {
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.DevScroll(int.Parse(action["settingsscroll:".Length..]));
            return;
        }
        if (action.StartsWith("settingshover:"))          // settingshover:label rests the pointer on that control, so its hover tip shows
        {
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.DevHover(action["settingshover:".Length..].Replace('_', ' '));
            return;
        }
        if (action == "settingswidths")                     // logs any page made so far that is wider than its room
        {
            if (IsInstanceValid(_settingsWindow))
                _settingsWindow!.DevWidths();
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
        if (action.StartsWith("telegramat:"))
        {
            // telegramat:http://127.0.0.1:port talks to a stand-in Telegram, so that no real bot is needed; listening starts again there
            Telegram.Server = action["telegramat:".Length..];
            if (ElsewhereSecrets.FromJson(_elsewhereKeys.Load()) is { TelegramToken: { } bot, TelegramChat: var chat } && _settings.ReminderElsewhere.Telegram)
                _texting.Use(bot, chat);
            return;
        }
        if (action.StartsWith("paircode:"))                 // paircode:CODE: the next pairing code, for a stand-in Telegram to send back
        {
            Texting.DevCode = action["paircode:".Length..];
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
            case "player": _onboarding.Start(); break;
            case "welcome": OpenWelcome(); break;                // the first run's welcome, again
            case "trayitems": GD.Print($"Tray: {_tray.DevItems()}"); break;      // the tray menu's entries, as they are now
            case "ticket": _ticket.Show("5:00 LEFT", "Your last token is burning"); break;
            case "bubbleclick": if (_stool.OnBubble(_stool.Her?.BubbleCentre ?? default)) _stool.Clicked(); break;     // a click on her bubble while it waits on her call
            case "remindertry":                             // the "Try it" on Settings → Reminders
                if (IsInstanceValid(_settingsWindow) && _settingsWindow!.PageOf("reminders") is ReminderSettings reminders)
                    reminders.DevTry();
                break;
            case "easy": DevSolve(Difficulty.Easy); break;
            case "medium": DevSolve(Difficulty.Medium); break;
            case "hard": DevSolve(Difficulty.Hard); break;
            case "game": _devGame = !_devGame; Tick(); break;
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

    /// <summary>
    /// A reminder going off, also kept in Windows' notification centre if the player chose that. It reaches their phone
    /// through her (see <see cref="Stool.TextOut"/>): while they are away, or also at the PC if they chose that.
    /// </summary>
    private void SendOut(Notice notice)
    {
        if (_settings.ReminderWindows)
            WindowsNotice.Show(notice.Title, notice.Text);
    }

    /// <summary>A text to the player's phone, through each place on Settings → Your phone; a place that fails is logged.</summary>
    private async Task Text(Notice notice)
    {
        if (!_settings.ReminderElsewhere.Any)
            return;
        foreach (var (name, error) in await Couriers.Send(Couriered, _settings.ReminderElsewhere, ElsewhereSecrets.FromJson(_elsewhereKeys.Load()), notice))
            if (error is not null)
                GD.PrintErr($"Reaching: could not reach {name}: {error}");
            else
                GD.Print($"Reaching: sent to {name}");
    }

    /// <summary>How long since the player's last input: keys and the mouse, as Windows counts it, or a game pad (the dev key F7 pretends an hour).</summary>
    private TimeSpan Idle()
    {
        if (_devAfk)
            return TimeSpan.FromHours(1);
        var sinceJoypad = DateTimeOffset.Now - _lastJoypad;
        var idle = UserActivity.IdleFor();
        return idle < sinceJoypad ? idle : sinceJoypad;
    }

    // ---- Settings ------------------------------------------------------------------------------------------------------

    /// <summary>Opens Settings (tray → Settings…), at <paramref name="page"/> if one is given; open already, it comes forward, at that page.</summary>
    private void OpenSettings(string? page)
    {
        if (IsInstanceValid(_settingsWindow))
        {
            if (page is not null)
                _settingsWindow!.ShowPage(page);
            _settingsWindow!.GrabFocus();
            return;
        }
        _settingsWindow = new SettingsWindow();
        AddChild(_settingsWindow);
        _settingsWindow.Build([.. SettingsPages(page), .. _stool.SettingsPages()], page, _stool.HerName());
        _settingsWindow.Open();
    }

    /// <summary>
    /// The cabinet's pages of the settings: Getting started while anything is left of it (or when asked for), the
    /// player's day (reminders, check-ins, their phone, calendars, balance) and the cabinet itself.
    /// </summary>
    private IEnumerable<SettingsPage> SettingsPages(string? open) =>
    [
        .. StartLeft() || open == "start" ? [new SettingsPage("start", SettingsWindow.First, "Getting started", StartPage, Fresh: true)] : Array.Empty<SettingsPage>(),
        new("reminders", SettingsWindow.Day, "Reminders", RemindersPage),
        new("checkins", SettingsWindow.Day, "Check-ins", CheckInsPage),
        new("phone", SettingsWindow.Day, "Your phone", YourPhonePage),
        new("calendars", SettingsWindow.Day, "Calendars", CalendarsPage),
        new("balance", SettingsWindow.Day, "Balance", BalancePage),
        new("cabinet", SettingsWindow.Cabinet, "Cabinet", CabinetPage),
    ];

    /// <summary>
    /// The first run (see <see cref="Welcome"/>): the player's name, who keeps them company and her mind; then the
    /// cabinet is theirs, their initials taken from the name, and she says hello. The rest waits in Getting started.
    /// </summary>
    private void OpenWelcome()
    {
        if (IsInstanceValid(_welcome))
            return;
        _welcome = new Welcome();
        AddChild(_welcome);
        _welcome.Build(_settingsFile, _stool, () => Provider.From(_settings.ChatProtocol, _settings.ChatBaseUrl, _settings.ChatModel) is not null);
        _welcome.Done += name =>
        {
            var initials = new string([.. (name ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(3)]);
            SaveSettings(_settings with { SetUp = true, Initials = initials.Length > 0 ? initials : _settings.Initials });
            _cabinet.Screen.Flash($"HI {_settings.Initials}", "LET'S PLAY", 3.0);
            _cabinet.Lights.Burst(LightMode.Celebrate, 2.0);
            _audio.Play("hello");
            _stool.Greet();
        };
        _welcome.Open();
    }

    /// <summary>Getting started's steps: hers (her mind, voice, talking aloud), then the player's phone and calendar, and LeetCode.</summary>
    private List<StartStep> StartSteps()
    {
        var where = _settings.ReminderElsewhere;
        var places = new[] { (where.Telegram, "Telegram"), (where.Ntfy, "ntfy"), (where.Discord, "Discord"), (where.Webhook, "a webhook") }
            .Where(p => p.Item1).Select(p => p.Item2).ToList();
        var linked = _calendars.Links.Count;
        return
        [
            .. _stool.StartSteps(),
            new("phone", "Your phone",
                $"Reminders, and {_stool.HerName()}'s texts while you are away from the PC; with Telegram you can text back. Needs Telegram on your phone " +
                "(or ntfy, Discord, any webhook). About 5 minutes.",
                Needed: false, Done: places.Count > 0, places.Count > 0 ? $"They reach you by {string.Join(", ", places)}." : "Not set up."),
            new("calendars", "Calendars",
                $"Your calendar's next two weeks join the agenda, so {_stool.HerName()} knows what is coming up. Needs its secret iCal address (Google or " +
                "Outlook). About 2 minutes.",
                Needed: false, Done: linked > 0, linked > 0 ? $"{linked} linked." : "Not set up."),
            new("balance", "LeetCode",
                "Problems you solve on LeetCode earn play: 15 minutes for an Easy one, 30 for a Medium, an hour for a Hard. Needs your " +
                "LeetCode name (Change… beside it on Balance). A minute.",
                Needed: false, Done: _settings.Username is { Length: > 0 }, _settings.Username is { Length: > 0 } user ? $"Playing as {user}." : "Not set up."),
        ];
    }

    /// <summary>Whether Getting started has anything left and isn't hidden: it is listed first in Settings and in the tray till then.</summary>
    private bool StartLeft() => !_settings.StartHidden && GettingStarted.Left(_settings.CallMe, StartSteps(), _settings.StartLater);

    private Control StartPage()
    {
        static string Time(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
        var page = new GettingStarted();
        page.Build(_stool.HerName(), _settings.CallMe, StartSteps(), _settings.StartLater,
                   $"A day can earn up to {Time(_settings.WeekdayCapMinutes)} of play Monday to Thursday, {Time(_settings.FridayCapMinutes)} on Friday " +
                   $"and {Time(_settings.WeekendCapMinutes)} at weekends; " +
                   (_settings.Bedtime is { } bedtime ? $"play after your bedtime ({bedtime}) costs more." : "no bedtime is set."));
        page.Named += name =>
        {
            _stool.CallMe(name);
            _settingsWindow?.RefreshPage("characters");    // the name shown there too
        };
        page.Open += id => _settingsWindow?.ShowPage(id);
        page.Later += id =>
        {
            SaveSettings(_settings with { StartLater = [.. _settings.StartLater, id] });
            _settingsWindow?.RefreshPage("start");
        };
        page.HideAsked += () => SaveSettings(_settings with { StartHidden = true });
        return page;
    }

    private Control RemindersPage()
    {
        var page = new ReminderSettings();
        page.Build(ReminderChoices.Of(_settings));
        page.Chosen += choices => SaveSettings(choices.Into(_settings));
        page.TryIt = TryReminder;
        return page;
    }

    private Control CheckInsPage()
    {
        var page = new CheckInSettings();
        page.Build(CheckInChoices.Of(_settings));
        page.Chosen += choices => SaveSettings(choices.Into(_settings));
        return page;
    }

    /// <summary>"Try it": a reminder goes off now as one would, in the cabinet and wherever else it is set to go; what came of it.</summary>
    private async Task<(string Said, bool Fine)> TryReminder()
    {
        const string text = "This is how a reminder reaches you.";
        _reminders.Try(text, DateTimeOffset.Now, chime: _settings.ReminderSound);      // in the cabinet; sent on from here, to say how it went
        var now = DateTimeOffset.Now;
        var notice = Reminders.NoticeFor([new Plan(0, text, now, false, TimeSpan.Zero, null, now, null)], now);
        var said = new List<string> { "On the ticket and in the speech bubble" };
        var fine = true;
        if (_settings.ReminderWindows)
        {
            var shown = WindowsNotice.Show(notice.Title, notice.Text);
            said.Add(shown ? "a Windows notification" : "not in Windows (it refused)");
            fine &= shown;
        }
        var where = _settings.ReminderElsewhere;
        var secrets = ElsewhereSecrets.FromJson(_elsewhereKeys.Load());
        foreach (var (name, error) in where.Any ? await Couriers.Send(Couriered, where, secrets, notice) : [])
        {
            said.Add(error is null ? name : $"not {name} ({error})");
            fine &= error is null;
        }
        if (new[] { where.Ntfy, where.Discord, where.Telegram, where.Webhook }.Count(on => on) > Couriers.Parcels(where, secrets, notice).Count())
        {
            said.Add("not everywhere ticked on Your phone (fill in what each asks for)");
            fine = false;
        }
        return (string.Join(", ", said) + ".", fine);
    }

    private Control YourPhonePage()
    {
        var page = new PhoneSettings();
        page.Build(_settings.ReminderElsewhere, ElsewhereSecrets.FromJson(_elsewhereKeys.Load()), _settings.AwayMinutes, _settings.AlsoAtThePc, _settings.Reach,
                   _texting, _stool.HerName());
        page.Chosen += (where, secrets) =>
        {
            // the Telegram bot is the pairing's: kept as it is now, whatever the page held when it was made
            var paired = ElsewhereSecrets.FromJson(_elsewhereKeys.Load());
            SaveSettings(_settings with { ReminderElsewhere = where with { Telegram = _settings.ReminderElsewhere.Telegram } });
            secrets = secrets with { TelegramToken = paired.TelegramToken, TelegramChat = paired.TelegramChat };
            _elsewhereKeys.Save(secrets == new ElsewhereSecrets() ? null : secrets.ToJson());
        };
        page.TelegramForgotten += () =>
        {
            var kept = ElsewhereSecrets.FromJson(_elsewhereKeys.Load()) with { TelegramToken = null, TelegramChat = null };
            _elsewhereKeys.Save(kept == new ElsewhereSecrets() ? null : kept.ToJson());
            SaveSettings(_settings with { ReminderElsewhere = _settings.ReminderElsewhere with { Telegram = false } });
            _texting.Use(null, null);
            _settingsWindow?.RefreshPage("phone");
        };
        page.AwayChosen += minutes => SaveSettings(_settings with { AwayMinutes = minutes });
        page.AtThePcChosen += on => SaveSettings(_settings with { AlsoAtThePc = on });
        page.ReachChosen += reach => SaveSettings(_settings with { Reach = reach });
        return page;
    }

    private Control CalendarsPage()
    {
        var page = new CalendarSettings();
        page.Build(_calendars);
        return page;
    }

    private Control BalancePage()
    {
        var page = new BalanceSettings();
        var today = _state.Today is { } t
            ? $"Today: {t.EarnedMs / 60_000} of {t.CapMs / 60_000} minutes earned, " + (t.Bedtime is { } b ? $"bedtime {b.Hours:00}:{b.Minutes:00}." : "no bedtime.")
            : "";
        page.Build(_settings, today);
        page.Chosen += (weekday, friday, weekend, bedtime) =>
        {
            SaveSettings(_settings with { WeekdayCapMinutes = weekday, FridayCapMinutes = friday, WeekendCapMinutes = weekend, Bedtime = bedtime });
            UseRules();
        };
        page.ChangePlayer += () =>
        {
            // the cabinet's screen asks for the LeetCode name
            _window.Rise(4, _settings.AlwaysOnTop);
            DisplayServer.WindowMoveToForeground();
            _onboarding.Start();
        };
        return page;
    }

    private Control CabinetPage()
    {
        var page = new CabinetSettings();
        page.Build(_settingsFile, Startup.IsEnabled());
        page.Sized += size => { SaveSettings(_settings with { Size = size }); ApplySize(); };
        page.OnTop += on => { SaveSettings(_settings with { AlwaysOnTop = on }); _window.SetTopmost(on); };
        page.WithWindows += Startup.Set;
        page.Muted += SetMuted;
        page.Tickets += on => SaveSettings(_settings with { InGameTickets = on });
        page.SaveEnergy += on => { SaveSettings(_settings with { EnergySaver = on }); SetPace(); };
        return page;
    }

    /// <summary>Mute sounds (the tray's switch, or Settings → Cabinet): the cabinet's sounds and her voice.</summary>
    private void SetMuted(bool muted)
    {
        SaveSettings(_settings with { Muted = muted });
        _audio.Muted = muted;
        _stool.Muted = muted;
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

    private void AddTimer(double seconds, Action action)
    {
        var timer = new Godot.Timer { WaitTime = seconds, Autostart = true };
        timer.Timeout += action;
        AddChild(timer);
    }
}
