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
public partial class Main : Node2D
{
    private static readonly TimeSpan CheckCooldown = TimeSpan.FromSeconds(10);
    // Frames per second. Something on the cabinet always moves (lights, glows, the companion), so the window is
    // redrawn all the time: full pace when someone may be watching, a slow one while a game runs or to save energy.
    private const int FullPace = 60, SlowPace = 15;
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
    private CompanionView? _companion;
    private Chat _chat = null!;
    private ChatBox _chatBox = null!;
    private ChatSettings? _chatSettings;
    private MemoryBook? _memoryBook;
    private Voice _voice = null!;
    private VoiceSettings? _voiceSettings;
    private string _charactersDir = "";
    private readonly Random _random = new();
    private TimeSpan _livelyUntil;
    private Director _director = null!;
    private DesktopWindow _window = null!;
    private TrayIcon _tray = null!;
    private Onboarding _onboarding = null!;
    private AudioBank _audio = null!;
    private AlertTicket _ticket = null!;
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
        _charactersDir = Path.Combine(dataDir, "characters");
        _settingsStore = new JsonFileStore<AppSettings>(Path.Combine(dataDir, "settings.json"));
        _stateStore = new JsonFileStore<LedgerState>(Path.Combine(dataDir, "state.json"));
        _settings = _settingsStore.Load() ?? new AppSettings();
        _state = _stateStore.Load() ?? LedgerState.New(DateTimeOffset.Now, _rules);
        _ledger = new Ledger(_rules);
        _tasks = new LeetCodeSource(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _games = args.ContainsKey("no-steam") ? new NoGames() : new SteamDetector(new WindowsSteamRegistry(), _settings.IgnoredSteamAppIds);

        var skin = CabinetSkin.Load(_settings.Skin);
        GetWindow().ContentScaleSize = (Vector2I)skin.Canvas;      // one canvas unit = one viewport unit, whatever the skin's size
        _cabinet = new CabinetView();
        AddChild(_cabinet);
        _cabinet.Build(skin);
        _chat = new Chat(dataDir);
        _chat.UseSearch(_settings.MemorySearch);           // remembered first, so connecting loads the right model
        _chat.Connect(ChatProvider());
        _voice = new Voice();
        AddChild(_voice);
        _voice.Build(Path.Combine(dataDir, "companion", "voice-server.log"), () => _companion);
        _voice.Muted = _settings.Muted;
        _voice.UseEngine(_settings.VoiceEngine);
        ShowCompanion(_settings.ShowCompanion);
        _audio = new AudioBank();
        AddChild(_audio);
        _audio.Build(_settings.Muted);
        _ticket = new AlertTicket();
        AddChild(_ticket);
        _director = new Director(_cabinet, () => _companion, _audio, _ticket, _rules, () => _settings.InGameTickets);

        _window = new DesktopWindow();
        AddChild(_window);
        _window.Build(skin.Layers["base"], skin.Scale, skin.Canvas);
        _window.Hover += p => _cabinet.HoverCheck(p is { } q && _cabinet.HitsCheck(q));
        _chatBox = new ChatBox();
        AddChild(_chatBox);
        _chatBox.Submitted += said => _ = Ask(said);
        _window.AlsoSolid = p => _companion?.Hits(p) == true || _chatBox.Hits(p);
        _window.KeepsMouse = _chatBox.Hits;
        _window.Clicked += p =>
        {
            if (_cabinet.HitsCheck(p))
                _cabinet.PressCheck();
            else if (_companion?.Hits(p) == true)
                ClickedHer();
        };
        GetWindow().FilesDropped += AddCharacters;             // a model dropped on the cabinet or on her
        _cabinet.CheckPressed += OnCheck;
        ApplySize();
        PlaceWindow();
        _window.SetTopmost(_settings.AlwaysOnTop);

        _tray = new TrayIcon();
        AddChild(_tray);
        _tray.Build(_settings.AlwaysOnTop, Startup.IsEnabled(), _settings.Size, _settings.Muted, _settings.InGameTickets, _settings.ShowCompanion,
                    _settings.SpeechBubbles, _settings.Voice, Characters(), _settings.Companion, _settings.EnergySaver);
        _tray.EnergySaverChanged += on => { SaveSettings(_settings with { EnergySaver = on }); SetPace(); };
        _tray.MutedChanged += muted => { SaveSettings(_settings with { Muted = muted }); _audio.Muted = muted; _voice.Muted = muted; };
        _tray.InGameTicketsChanged += on => SaveSettings(_settings with { InGameTickets = on });
        _tray.CompanionChanged += on => { SaveSettings(_settings with { ShowCompanion = on }); ShowCompanion(on); };
        _tray.SpeechChanged += on =>
        {
            SaveSettings(_settings with { SpeechBubbles = on });
            if (!on)
                _companion?.Hush();
        };
        _tray.ChatSettings += OpenChatSettings;
        _tray.VoiceChanged += on => { SaveSettings(_settings with { Voice = on }); WakeVoice(); };
        _tray.VoiceSettings += OpenVoiceSettings;
        _tray.MemoryBook += OpenMemoryBook;
        _tray.AddCharacter += PickCharacter;
        _tray.OpenCharacters += () =>
        {
            Directory.CreateDirectory(_charactersDir);          // files put here by hand show up in the menu after a restart
            OS.ShellOpen(_charactersDir);
        };
        _tray.CharacterChanged += Seat;
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
            SaveSettings(_settings with { Initials = initials, Username = username });
            _cabinet.Screen.Flash($"HI {initials}", $"PLAYER {username}".ToUpperInvariant(), 3.0);
            _cabinet.Lights.Burst(LightMode.Celebrate, 2.0);
            _audio.Play("hello");
            _backoffSeconds = 60;
            Poll();
        };
        AddTimer(0.5, _onboarding.Blink);
        if (string.IsNullOrWhiteSpace(_settings.Username))
            _onboarding.Start();

        _cabinet.Tray.Set(_state.Credits);
        AddTimer(5.0, Tick);
        AddTimer(1.0, RefreshScreen);
        AddTimer(5.0, () => { if (DateTimeOffset.Now >= _nextPoll) Poll(); });
        AddTimer(60.0, () => _ = _chat.Upkeep(gameRunning: _state.Session is not null));
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
        _audio.Play("check");
        _cabinet.Screen.Flash("CHECK", string.IsNullOrWhiteSpace(_settings.Username) ? "NO PLAYER SET" : "ASKING LEETCODE", 1.5);
        _backoffSeconds = 60;
        Poll();
    }

    private void Apply(LedgerInput input)
    {
        var result = _ledger.Step(_state, input, DateTimeOffset.Now);
        _state = result.State;
        _voice.GameRunning = _state.Session is not null;      // her voice gives the game the graphics card
        if (result.Events.Count > 0)
            Lively(6);
        foreach (var e in result.Events)
            _director.Play(e);
        _chat.Witness(result.Events, _rules);
        if (Lines.For(result.Events, _state, _rules, _random) is { } line && !_chat.Answering)
            Say(line);
        // Moments worth seeing rise above other windows for a few seconds — never over a running game.
        if (_state.Session is null && result.Events.Any(e => e is CoinsEarned or SessionEnded or WeeklyReset))
            _window.Rise(6, _settings.AlwaysOnTop);
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
            Initials = _settings.Initials,
        });
        var mood = shownDebt > 0 && shownLeft == 0 ? TrayMood.Debt : shownLeft < 15 * 60_000 ? TrayMood.Low : TrayMood.Fine;
        var streak = GameCalendar.CurrentStreak(s, DateTimeOffset.Now, _rules);
        _tray.Set(mood, mood == TrayMood.Debt
            ? $"INSERT COIN — {shownDebt / 60_000} min owed ({s.IouTickets(_rules)} IOU)"
            : $"INSERT COIN — {shownLeft / 60_000} min left · {s.Credits} credits" + (streak > 0 ? $" · combo x{streak}" : ""));
        var power = s.Paused ? 0.3f : s.Session is not null ? 1f : 0.7f;
        _cabinet.SetScreenPower(power);
        _companion?.SetScreenLight(power);
        SetPace();
        _cabinet.Printer.SetPartial(shownLeft == 0 && shownDebt > 0 ? shownDebt % _rules.TokenMs / (float)_rules.TokenMs : 0, animate: true);
        if (s.Session is not null && shownLeft == 0)
            _cabinet.Lights.SetModeIfNot(LightMode.Alarm);
        else
            _cabinet.Lights.SetModeIfNot(LightMode.Chase, onlyFrom: LightMode.Alarm);
    }

    private string StatusLine()
    {
        if (_state.Paused) return "PAUSED - AFK";
        if (_state.Session is { } session) return "GAME: " + string.Join(" + ", session.GameNames).ToUpperInvariant();
        if (_noSteam) return "NO STEAM";
        if (string.IsNullOrWhiteSpace(_settings.Username)) return "NO PLAYER";
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

    /// <summary>Seats the companion on the stool (built the first time she is wanted) or leaves it empty.</summary>
    private void ShowCompanion(bool show)
    {
        if (show && _companion is null && CompanionView.Available(_charactersDir) is { Length: > 0 } characters)
        {
            var character = characters.FirstOrDefault(c => c.Id == _settings.Companion) ?? characters[0];
            _companion = new CompanionView();
            AddChild(_companion);
            MoveChild(_companion, _cabinet.GetIndex() + 1);       // drawn over the cabinet art, under everything else
            try
            {
                _companion.Build(_cabinet.Skin, character);
                _chat.Seat(character.Id, character.Persona(_charactersDir, _settings.Username));
                _voice.Seat(VoiceFolder(character.Id));
            }
            catch (Exception e)
            {
                // a model file of the user's that will not load must not take the cabinet down with it
                GD.PrintErr($"Companion '{character.Id}': {e.Message}");
                _cabinet.Screen.Flash("NO GO", $"{character.Name}: {e.Message}".ToUpperInvariant(), 6.0);
                _companion.QueueFree();
                _companion = null;
            }
        }
        _companion?.SetActive(show);
        WakeVoice();
        SetPace();
    }

    /// <summary>Her voice runs while she is shown and it is switched on (it still rests while a game runs).</summary>
    private void WakeVoice() => _voice.Enabled = _settings.Voice && _settings.ShowCompanion && _companion is not null;

    /// <summary>Where a character's voice is kept: a folder beside them in the user's characters folder.</summary>
    private string VoiceFolder(string characterId) => Path.Combine(_charactersDir, characterId + ".voice");

    /// <summary>Someone else takes the stool.</summary>
    private void Seat(string id)
    {
        SaveSettings(_settings with { Companion = id });
        _companion?.QueueFree();
        _companion = null;
        ShowCompanion(_settings.ShowCompanion);
    }

    private (string Id, string Name)[] Characters() => [.. CompanionView.Available(_charactersDir).Select(c => (c.Id, c.Name))];

    /// <summary>Asks for a model with the system's own file dialog, then adds it.</summary>
    private void PickCharacter()
    {
        var dialog = new FileDialog
        {
            Title = "Add a character",
            ModeOverridesTitle = false,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads),
            Filters = ["*.vrm, *.glb, *.gltf, *.zip, *.png, *.json, *.wav, *.mp3, *.ogg, *.flac ; Characters, character cards and voices",
                       "*.vrm, *.glb, *.gltf, *.zip ; 3D characters (VRM, glTF, or a zip holding one)",
                       "*.png, *.json ; Character cards (SillyTavern style) for whoever is on the stool",
                       "*.zip, *.wav, *.mp3, *.ogg, *.flac ; Voices (a GPT-SoVITS model's .zip, or a 3-10 second clip)"],
        };
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            AddCharacters([path]);
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        _window.Rise(4, _settings.AlwaysOnTop);
        DisplayServer.WindowMoveToForeground();             // the dialog belongs to the cabinet's window: bring both forward
        dialog.PopupCentered();
    }

    /// <summary>
    /// Copies models (picked or dropped) into the user's characters folder and seats the first of them. A character
    /// card goes to whoever is on the stool instead (a model dropped with it first), who then greets as the card says.
    /// </summary>
    private void AddCharacters(string[] paths)
    {
        var added = new List<string>();
        var cards = new List<string>();
        VoiceProfile? voice = null;
        string? refused = null;
        // models first, then cards and voices, which go to the model added with them (or whoever is on the stool)
        foreach (var path in paths.OrderBy(p => CharacterFiles.HoldsModel(p) ? 0 : 1))
            try
            {
                if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".json")
                {
                    if ((added.FirstOrDefault() ?? OnStool()?.Id) is { } who && CharacterFiles.ImportCard(path, _charactersDir, who) is { } name)
                        cards.Add(name);
                }
                else if (!CharacterFiles.HoldsModel(path) && VoiceProfile.LooksLikeVoice(path))
                {
                    if ((added.FirstOrDefault() ?? OnStool()?.Id) is { } who)
                        voice = VoiceProfile.Import(path, VoiceFolder(who));
                }
                else if (CharacterFiles.Import(path, _charactersDir) is { } id)
                    added.Add(id);
            }
            catch (Exception e)
            {
                // an unreadable or half-downloaded file must not take the cabinet down with it
                GD.PrintErr($"Adding '{path}': {e.Message}");
                if (e is InvalidDataException)
                    refused = e.Message;
            }
        if (added.Count == 0 && cards.Count == 0 && voice is null)
        {
            _cabinet.Screen.Flash("NO GO", (refused ?? "ONLY .VRM, .GLB, .GLTF, A CHARACTER CARD OR A VOICE").ToUpperInvariant(), 5.0);
            return;
        }
        if (added.Count == 0 && cards.Count == 0)
        {
            AddedVoice(voice!);
            return;
        }
        if (added.Count > 0)
            _tray.SetCharacters(Characters(), added[0]);
        SaveSettings(_settings with { ShowCompanion = true });
        Seat(added.FirstOrDefault() ?? OnStool()!.Value.Id);        // seated again, so a new card takes effect
        Lively(6);
        _companion?.Play(Moment.Greet);
        if (cards.Count > 0 && _chat.Conversation?.Persona.Greeting is { } greeting && FirstLine(greeting) is { } line)
            GetTree().CreateTimer(0.8).Timeout += () => Say(line);      // once she has sat down: the bubble goes over her seated head
        if (voice is not null)
            AddedVoice(voice);
    }

    /// <summary>A voice was just given to whoever is on the stool: she tries it out, or says what it still needs.</summary>
    private void AddedVoice(VoiceProfile voice)
    {
        _voice.Seat(_voice.Folder);
        if (voice.Missing is not null || VoiceServer.Check(_settings.VoiceEngine) is not null)
        {
            _cabinet.Screen.Flash("VOICE", "ALMOST THERE: SEE VOICE SETTINGS", 4.0);
            OpenVoiceSettings();
            return;
        }
        _cabinet.Screen.Flash("VOICE", $"{voice.Name} - WARMING UP".ToUpperInvariant(), 4.0);
        Lively(20);
        _ = _voice.Test();
    }

    /// <summary>Asks for a voice with the system's own file dialog (a downloaded model's .zip, or a clip), then adds it.</summary>
    private void PickVoice()
    {
        var dialog = new FileDialog
        {
            Title = "Add a voice",
            ModeOverridesTitle = false,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads),
            Filters = ["*.zip, *.wav, *.mp3, *.ogg, *.flac ; Voices (a GPT-SoVITS model's .zip, or a 3-10 second clip)"],
        };
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            AddCharacters([path]);
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        _window.Rise(4, _settings.AlwaysOnTop);
        DisplayServer.WindowMoveToForeground();
        dialog.PopupCentered();
    }

    /// <summary>The GPT-SoVITS that runs her voice, and the voice of whoever is on the stool.</summary>
    private void OpenVoiceSettings()
    {
        if (IsInstanceValid(_voiceSettings))
        {
            _voiceSettings!.GrabFocus();
            return;
        }
        if (OnStool() is not { } character)
            return;
        _voiceSettings = new VoiceSettings();
        AddChild(_voiceSettings);
        _voiceSettings.Build(_voice, _settings.VoiceEngine, character.Name);
        _voiceSettings.Saved += engine =>
        {
            var changed = engine != _settings.VoiceEngine;
            SaveSettings(_settings with { VoiceEngine = engine });
            if (changed)
                _voice.UseEngine(engine);
            _voice.Seat(VoiceFolder(character.Id));          // read again: its language, words or quality may have changed
        };
        _voiceSettings.AddVoice += PickVoice;
        _voiceSettings.RemoveVoice += () =>
        {
            var folder = VoiceFolder(character.Id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            _voice.Seat(folder);
            OpenVoiceSettings();
        };
        _voiceSettings.Open();
    }

    /// <summary>The first sentence of a card's greeting, without its *actions*, if it is short enough for the bubble.</summary>
    private static string? FirstLine(string greeting)
    {
        var spoken = System.Text.RegularExpressions.Regex.Replace(greeting, @"\*[^*]*\*", " ").Trim();
        var end = spoken.IndexOfAny(['.', '!', '?', '。', '！', '？', '\n']);
        var line = (end >= 0 ? spoken[..(end + 1)] : spoken).Trim();
        return line.Length is > 1 and <= 140 ? line : null;
    }

    /// <summary>A line in her speech bubble, unless the user has asked for quiet.</summary>
    private void Say(string line)
    {
        if (!_settings.SpeechBubbles || _companion is null)
            return;
        _companion.Say(line);
        _voice.Line(line);
        _chat.Said(line);
    }

    /// <summary>She waves and says where things stand; when she can talk, the slip to type to her opens too.</summary>
    private void ClickedHer()
    {
        _voice.Stop();
        Lively(6);
        _companion?.Play(Moment.Greet);
        if (!_chat.Answering)
            Say(Lines.Status(_state, _rules, GameCalendar.CurrentStreak(_state, DateTimeOffset.Now, _rules)));
        OpenChat();
    }

    private Provider? ChatProvider() => Provider.From(_settings.ChatProtocol, _settings.ChatBaseUrl, _settings.ChatModel);

    /// <summary>Opens the slip at the foot of the stool for the user to type to her, once she can answer.</summary>
    /// <param name="focus">Bring the window forward for typing (the user clicked her); dev actions leave focus alone.</param>
    private void OpenChat(bool focus = true)
    {
        if (!_chat.Ready || _companion is null)
            return;
        if (focus)
            DisplayServer.WindowMoveToForeground();
        _chatBox.Open(new Vector2(14, _cabinet.Skin.Canvas.Y - ChatBox.Height - 14), $"Say something to {_chat.Conversation!.Persona.Name}…");
    }

    private async Task Ask(string said)
    {
        if (_companion is null)
            return;
        _chatBox.SetWaiting(true);
        Lively(120);                                     // keep her lively while she thinks and talks
        try
        {
            await _chat.Ask(said, Situation.Describe(_state, _rules, DateTimeOffset.Now, _settings.Username), _companion, _voice.Begin());
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: {e}");                  // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _chatBox.SetWaiting(false);
            Lively(10);
        }
    }

    /// <summary>Provider, server, key and model.</summary>
    private void OpenChatSettings()
    {
        if (IsInstanceValid(_chatSettings))
        {
            _chatSettings!.GrabFocus();
            return;
        }
        if (OnStool() is not { } character)
            return;
        _chatSettings = new ChatSettings();
        AddChild(_chatSettings);
        _chatSettings.Build(_chat, ChatProvider(), character.Name, _settings.MemorySearch);
        _chatSettings.Saved += (provider, search) =>
        {
            SaveSettings(_settings with
            {
                ChatProtocol = provider?.Protocol.ToString(), ChatBaseUrl = provider?.BaseUrl, ChatModel = provider?.Model, MemorySearch = search,
            });
            _chat.UseSearch(search);
            _chat.Connect(provider);
        };
        _chatSettings.Open();
    }

    /// <summary>What whoever is on the stool remembers: notes, facts, moments.</summary>
    private void OpenMemoryBook()
    {
        if (IsInstanceValid(_memoryBook))
        {
            _memoryBook!.GrabFocus();
            return;
        }
        if (OnStool() is not { } character)
            return;
        _memoryBook = new MemoryBook();
        AddChild(_memoryBook);
        _memoryBook.Build(_chat.Store, character.Id, character.Name);
        _memoryBook.Open();
    }

    /// <summary>The character chosen for the stool (whether shown or not), with the name their persona goes by.</summary>
    private (string Id, string Name)? OnStool()
    {
        if (_chat.Conversation is { } conversation)
            return (conversation.Character, conversation.Persona.Name);
        var characters = CompanionView.Available(_charactersDir);
        return (characters.FirstOrDefault(c => c.Id == _settings.Companion) ?? characters.FirstOrDefault()) is { } c ? (c.Id, c.Name) : null;
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

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(settings);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && _chatBox?.IsOpen == true && _chatBox.IsEmpty)
            _chatBox.Close();                           // clicked away without typing
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            if (_stateStore is not null)
                Save();
            _chat?.Dispose();
            _voice?.Shutdown();
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
        if (key.Keycode == Key.Escape && _chatBox.IsOpen)
        {
            _chatBox.Close();
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
        if (action.StartsWith("point:"))             // point:x:y pretends the mouse pointer is at this canvas point
        {
            var at = action.Split(':');
            _companion?.DevPointer(new Vector2(float.Parse(at[1]), float.Parse(at[2])));
            return;
        }
        if (action.StartsWith("character:"))         // character:id swaps who sits on the stool
        {
            Seat(action["character:".Length..]);
            return;
        }
        if (action.StartsWith("add:"))               // add:path adds a model as if it had been dropped on the window
        {
            AddCharacters([action["add:".Length..]]);
            return;
        }
        if (action.StartsWith("chat:"))              // chat:text sends a line to her as if typed (underscores for spaces)
        {
            OpenChat(focus: false);
            _chatBox.DevSend(action["chat:".Length..].Replace('_', ' '));
            return;
        }
        if (action is "chatbox")                      // opens the slip to type to her
        {
            OpenChat(focus: false);
            return;
        }
        if (action is "chatsettings")                 // the "Chat settings…" window
        {
            OpenChatSettings();
            return;
        }
        if (action is "memorybook")                   // the "Memory book…" window
        {
            OpenMemoryBook();
            return;
        }
        if (action is "voicesettings")                // the "Voice settings…" window
        {
            OpenVoiceSettings();
            return;
        }
        if (action is "voicetest")                    // her voice says its sample line (waiting for it to be ready)
        {
            _ = _voice.Test();
            return;
        }
        if (action.StartsWith("voice:"))             // voice:text puts a line in her bubble and says it aloud (underscores for spaces)
        {
            Say(action["voice:".Length..].Replace('_', ' '));
            return;
        }
        if (action is "upkeep")                       // her upkeep now, without waiting for the conversation to go quiet
        {
            _ = _chat.Upkeep(gameRunning: false, now: true);
            return;
        }
        if (action is "pick")                         // the "Add a character…" file dialog
        {
            PickCharacter();
            return;
        }
        if (action.StartsWith("tray:"))              // tray:x:y opens the tray menu at this screen point
        {
            var at = action.Split(':');
            _tray.DevShowMenu(new Vector2I(int.Parse(at[1]), int.Parse(at[2])));
            return;
        }
        if (action.StartsWith("say:"))               // say:text puts a line in her speech bubble (underscores for spaces)
        {
            Say(action["say:".Length..].Replace('_', ' '));
            return;
        }
        if (action is "quiet")                        // toggles her speech bubbles
        {
            SaveSettings(_settings with { SpeechBubbles = !_settings.SpeechBubbles });
            if (!_settings.SpeechBubbles)
                _companion?.Hush();
            return;
        }
        if (action is "click")                        // as if she had been clicked
        {
            ClickedHer();
            return;
        }
        if (action is "saver")                        // toggles the energy saver
        {
            SaveSettings(_settings with { EnergySaver = !_settings.EnergySaver });
            SetPace();
            return;
        }
        if (action.StartsWith("legs:"))              // legs:left|right|both|ring sets which feet hang off the stool's ring
        {
            _companion?.DevLegs(action["legs:".Length..]);
            return;
        }
        if (action.StartsWith("moment:"))
        {
            _companion?.Play(Enum.Parse<Moment>(action["moment:".Length..], ignoreCase: true));
            return;
        }
        if (action.StartsWith("type:"))
        {
            foreach (var c in action["type:".Length..])
                _onboarding.Key(new InputEventKey { Pressed = true, Unicode = c });
            return;
        }
        switch (action)
        {
            case "enter": _onboarding.Key(new InputEventKey { Pressed = true, Keycode = Key.Enter }); break;
            case "player": _onboarding.Start(keepInitials: _settings.Initials); break;
            case "ticket": _ticket.Show("5:00 LEFT", "Your last token is burning"); break;
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
