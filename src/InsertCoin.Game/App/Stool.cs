using System.Runtime.Versioning;
using Godot;
using InsertCoin.Core;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The stool and whoever sits on it: her figure, her mind (the chat, and the slip to type to her on), her voice and
/// its training, and their windows. Characters, character cards and voices come in through it. The cabinet tells it
/// what happens and whether a game runs; it flashes the cabinet's screen and livens the window up.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class Stool : Node2D
{
    private SettingsFile _settings = null!;
    private CabinetView _cabinet = null!;
    private DesktopWindow _window = null!;
    private Func<string> _situation = null!;
    private Action<double> _lively = null!;
    private string _characters = "";
    private Chat _chat = null!;
    private ChatBox _chatBox = null!;
    private PermissionCard _permission = null!;
    private Memos _memos = null!;
    private Focus _focus = null!;
    private Music _music = null!;
    private Voice _voice = null!;
    private VoiceTrainer _trainer = null!;
    private ChatSettings? _chatSettings;
    private MemoryBook? _memoryBook;
    private ChatHistory? _history;
    private VoiceSettings? _voiceSettings;
    private bool _gameRunning;
    private Listening _listening = null!;
    private bool _announcing;                               // passing on a reminder
    private readonly Queue<(IReadOnlyList<Plan> Due, DateTimeOffset At)> _reminding = new();
    private readonly Queue<(string Title, string Text)> _written = new();     // the player's own memos, for her to think over
    private readonly Queue<(string Ticket, string Note, string Line, DateTimeOffset At)> _told = new();     // focus sessions' turns, a long sitting
    private DateTimeOffset _quietFrom;                      // when she last stopped talking or listening

    /// <summary>Characters were added: all there are now (for the tray's list), and the one seated.</summary>
    public event Action<(string Id, string Name)[], string>? CharactersAdded;

    /// <summary>The player's memos: on the cabinet, in the archive, and in their own hand.</summary>
    public Memos Memos => _memos;

    /// <summary>Focus sessions (the screen's FOCUS app, and her tool).</summary>
    public Focus Focus => _focus;

    /// <summary>The activities that count (see <see cref="Mind.Activities"/>).</summary>
    public Activities Activities => _activities;

    private Activities _activities = null!;

    /// <summary>The cabinet pays an activity (its name, size and the share of its worth) and says what it paid; set by the cabinet's loop.</summary>
    public Func<string, Size, double, Payout?>? PayActivity { get; set; }

    /// <summary>How much of an activity's worth the cabinet has paid today; set by the cabinet's loop.</summary>
    public Func<string, double>? PaidToday { get; set; }

    /// <summary>The game day of a moment; set by the cabinet's loop.</summary>
    public Func<DateTimeOffset, DateOnly>? DayOf { get; set; }

    /// <summary>The cabinet's state and rules as they are now, for rest and sleep; set by the cabinet's loop.</summary>
    public Func<LedgerState>? Ledger { get; set; }
    public Func<Rules>? Rules { get; set; }

    /// <summary>The player's settings changed what the cabinet's rules are (a bedtime set through her); set by the cabinet's loop.</summary>
    public Action? RulesChanged { get; set; }

    /// <summary>The player's music app (the screen's MUSIC app, and her tool).</summary>
    public Music Music => _music;

    /// <summary>Something she would say, while the player can't be spoken to (a full-screen game, presenting): a ticket instead (title, line).</summary>
    public event Action<string, string>? Notice;

    /// <summary>Her figure, while someone is on the stool.</summary>
    public CompanionView? Her { get; private set; }

    /// <summary>She is answering, still saying something, or listening: the cabinet's own remarks wait (they would cut in).</summary>
    public bool Answering => _chat.Answering || _voice.Speaking || _listening.Busy;

    /// <summary>A reply is being written.</summary>
    internal bool Thinking => _chat.Answering;

    /// <summary>She is replying: a reply being written, said, or still paging through her bubble.</summary>
    internal bool Replying => _chat.Answering || _voice.Speaking || Her?.Seen is not null;

    /// <summary>A provider is set up and someone is on the stool: she can answer.</summary>
    internal bool CanAnswer => _chat.Ready;

    /// <summary>The microphone opened or closed (the cabinet's MIC light); a conversation started or ended (the tray's switch).</summary>
    public event Action<bool>? MicChanged, ConversingChanged;

    /// <param name="situation">Where things stand on the cabinet, told to her mind with what the user says.</param>
    /// <param name="lively">Full pace for this many seconds: something worth watching is about to happen.</param>
    public void Build(string dataDir, SettingsFile settings, CabinetView cabinet, DesktopWindow window,
                      Func<string> situation, Action<double> lively)
    {
        _settings = settings;
        _cabinet = cabinet;
        _window = window;
        _situation = situation;
        _lively = lively;
        _characters = Path.Combine(dataDir, "characters");

        _chat = new Chat(dataDir) { ReminderLead = () => settings.Now.ReminderLead };
        _chat.UseSearch(settings.Now.MemorySearch);        // remembered first, so connecting loads the right model
        _chat.UseWindow(settings.Now.ChatWindow);
        _chat.SearchThrough(settings.Now.AddOns.Search);
        _chat.Connect(ChatProvider());
        _chatBox = new ChatBox();
        AddChild(_chatBox);
        _chatBox.Submitted += said => _ = Ask(said);
        _chatBox.Attaching += how =>
        {
            switch (how)
            {
                case "file": PickFiles(); break;
                case "screen": HandScreen(); break;
                case "phone": OpenPhone(); break;
            }
        };
        _chatBox.PastedPicture += picture => HandPicture(picture, "pasted", photo: false);
        _chatBox.PastedFiles += Hand;
        // her screenshots: the window the player was working in (or its whole monitor), else the screen the pointer is on
        _chat.Screen = whole => OpenWindows.Working() is { } working && Screenshot.Of(working.Window, whole) is { } picture
            ? (Handing.Picture(picture, whole ? "screen" : "window", photo: false).Thing,
               whole ? $"the player's screen ({working.Title} in front)" : $"the window the player was working in ({working.Title})")
            : Screenshot.Take() is { } screen ? (Handing.Picture(screen, "screen", photo: false).Thing, "the player's screen") : null;
        _activities = new Activities(_chat.Store)
        {
            Confirm = ConfirmActivity,
            Pay = (name, size, share) => PayActivity?.Invoke(name, size, share),
            PaidToday = name => PaidToday?.Invoke(name) ?? 0,
            DayOf = moment => DayOf?.Invoke(moment) ?? DateOnly.FromDateTime(moment.DateTime.AddHours(-4)),
            AtThePc = () => _activeSince is { } since ? DateTimeOffset.Now - since : TimeSpan.Zero,
            Blind = () => _chat.CantSee(_settings.Now.ChatModel ?? ""),
            InFront = () => _chat.InFront,
        };
        _chat.Activities = _activities;
        _chat.Bedtime = SetBedtime;
        _chat.CantSee = model => _settings.Now.UnseeingModels.Contains(model);
        _chat.FoundCantSee += model => _settings.Save(_settings.Now with { UnseeingModels = [.. _settings.Now.UnseeingModels, model] });
        _permission = new PermissionCard();
        AddChild(_permission);
        _chat.AskPlayer = AskPlayer;
        _chat.Tools.Choose(settings.Now.ToolPermissions);
        _chat.Tools.Auto = settings.Now.ToolsAuto;
        _chat.Tools.Changed += () => _settings.Save(_settings.Now with { ToolPermissions = new(_chat.Tools.Chosen) });
        _memos = new Memos();
        AddChild(_memos);
        _memos.Build(_chat.Store, cabinet.Memos, id => Characters().FirstOrDefault(c => c.Id == id).Name ?? id ?? "",
                     herName: () => _chat.Ready && Her is not null ? OnStool()?.Name : null, onCabinet: window.Solid);
        _chat.PrintMemo = PrintMemo;
        if (settings.Now.Playlist is { } kept)               // one link, saved before playlists had names: now the first of them
            settings.Save(settings.Now with
            {
                Playlist = null, Playlists = [.. settings.Now.Playlists, new SavedPlaylist(kept, "My playlist")], FocusPlaylist = settings.Now.FocusPlaylist ?? kept,
            });
        _music = new Music { Links = () => settings.Now.Playlists.Select(p => p.Link) };
        AddChild(_music);
        _music.PlaylistRenamed += Rename;
        foreach (var unnamed in settings.Now.Playlists.Where(p => p.Name == "My playlist"))     // the one carried over: its name as Spotify has it
            _ = Named(unnamed.Link);

        async Task Named(string link)
        {
            if (await Desktop.Music.NameOf(link) is { } name)
                Rename(link, name);
        }

        void Rename(string link, string name) =>
            settings.Save(settings.Now with { Playlists = [.. settings.Now.Playlists.Select(p => p.Link == link ? p with { Name = name } : p)] });
        _focus = new Focus();
        AddChild(_focus);
        _focus.Build(settings, _music);
        _focus.Told += (note, line) => _told.Enqueue(("FOCUS", note, line, DateTimeOffset.Now));
        _chat.Focus = FocusAct;
        _chat.Music = MusicAct;
        _chat.Playlist = name => SavedPlaylist(name) is { } playlist ? $"{playlist.Name} ({Desktop.Music.Shown(playlist.Link)})" : $"No saved playlist called \"{name}\"";
        _memos.Written += (title, text) =>
        {
            Her?.Play(Moment.Paper);                        // she sees it come out of the slot
            if (_chat.Ready)
                _written.Enqueue((title, text));
        };

        _voice = new Voice();
        AddChild(_voice);
        _voice.Build(Path.Combine(dataDir, "companion", "voice-server.log"), () => Her);
        _voice.Muted = settings.Now.Muted;
        _voice.Answering = () => _chat.Answering && !_permission.IsOpen;     // waiting on her card, she isn't thinking: no thinking sounds
        _voice.Testing += line =>                           // only the bubble: she is already saying it in her own language
        {
            if (_settings.Now.SpeechBubbles)
                Her?.Say(line);
        };
        _voice.UseEngine(settings.Now.VoiceEngine);
        _trainer = new VoiceTrainer();
        AddChild(_trainer);
        _trainer.Build(Path.Combine(dataDir, "companion"), settings.Now.TrainingPace);
        _trainer.Finished += TrainingEnded;

        _listening = new Listening();
        AddChild(_listening);
        _listening.Build(this, settings, window, _chatBox);
        _listening.MicChanged += on => MicChanged?.Invoke(on);
        _listening.ConversingChanged += on => ConversingChanged?.Invoke(on);

        GetWindow().FilesDropped += Add;                     // a model, card or voice dropped on the cabinet or on her
        var upkeep = new Godot.Timer { WaitTime = 60, Autostart = true };
        upkeep.Timeout += () => _ = _chat.Upkeep(_gameRunning);
        AddChild(upkeep);
        var checkIns = new Godot.Timer { WaitTime = 15, Autostart = true };
        checkIns.Timeout += () =>
        {
            MaybeCheckIn();
            MaybeStandUp();
            MaybeRest();
        };
        AddChild(checkIns);
        ShowHer(settings.Now.ShowCompanion);
    }

    // ---- What the cabinet tells her ------------------------------------------------------------------------------------

    /// <summary>A game is running: her voice rests and training stops, to leave the game the graphics card.</summary>
    public bool GameRunning
    {
        get => _gameRunning;
        set
        {
            _gameRunning = value;
            _voice.GameRunning = value;
            _focus.GameRunning = value;
            if (value && _trainer.Running)
                _trainer.Stop("Training stopped: a game started (training takes the whole graphics card). Train again when you're done.");
        }
    }

    /// <summary>Mute sounds mutes her voice too.</summary>
    public bool Muted
    {
        set => _voice.Muted = value;
    }

    /// <summary>What just happened on the cabinet, for her mind to take note of.</summary>
    public void Witness(IEnumerable<LedgerEvent> events, Rules rules) => _chat.Witness(events, rules);

    /// <summary>Her memory, the agenda among it (shared by every character).</summary>
    public MindStore Store => _chat.Store;

    /// <summary>
    /// Reminders going off: she tells the player once she has finished what she is doing (answering, speaking,
    /// listening), never cutting herself or them off; in her own words when her mind is connected. The ticket has
    /// shown them already, so after ten minutes of waiting she lets them go.
    /// </summary>
    public void Remind(IReadOnlyList<Plan> due, DateTimeOffset now) => _reminding.Enqueue((due, now));

    // ---- Check-ins: how did it go? ---------------------------------------------------------------------------------

    private static readonly TimeSpan CheckInAfter = TimeSpan.FromMinutes(15), BackFor = TimeSpan.FromMinutes(2), Away = TimeSpan.FromMinutes(5),
        Pause = TimeSpan.FromMinutes(1), GentleGap = TimeSpan.FromHours(3);
    private DateTimeOffset? _activeSince;                   // the player has been at the PC since then, with no break of five minutes
    private DateTimeOffset? _settledSince;                  // and using it since then, with no pause of a minute: they have sat down to something
    private DateTimeOffset _lastCheckIn = DateTimeOffset.MinValue;

    /// <summary>
    /// Every fifteen seconds: when something the player told her about has been over for a while (see
    /// <see cref="Agenda.ToAskAbout"/>), she asks how it went, but only once they are back and free: settled at the PC
    /// (see <see cref="Settled"/>, so not as they sit down) and using it now, not in a call (another app has the microphone
    /// or camera), not in anything Windows holds notifications back for (full screen, presenting, quiet time, locked), not
    /// in the quiet hours, and not while she is busy. Gentle: one every three hours at most. A meeting that runs over keeps its call going, so she waits.
    /// </summary>
    private void MaybeCheckIn(bool force = false)
    {
        var now = DateTimeOffset.Now;
        var idle = UserActivity.IdleFor();
        _activeSince = idle >= Away ? null : _activeSince ?? now - idle;
        _settledSince = idle >= Pause ? null : _settledSince ?? now - idle;
        var mode = _settings.Now.CheckIns;
        if (!force && (mode == "Off" || Quiet(now.Hour) || idle > Pause || !Settled(now)
                       || mode == "Gentle" && now - _lastCheckIn < GentleGap))
            return;
        if (!_chat.Ready || Her is null || !_settings.Now.SpeechBubbles || Busy || _reminding.Count > 0 || _chat.Conversation is not { } conversation)
            return;
        if (!force && (Presence.DoNotDisturb || Presence.InCall))
            return;
        if (conversation.ToAskAbout(CheckInAfter) is not { } plan)
            return;
        _chat.Store.Asked(plan.Id, now);                    // once, whatever comes of it
        _lastCheckIn = now;
        GD.Print("Check-in: she asks how something went");
        _ = AskHowItWent(plan);
    }

    // ---- Stand up, have some water --------------------------------------------------------------------------------------

    private DateTimeOffset _lastStandUp = DateTimeOffset.MinValue;

    /// <summary>
    /// Every fifteen seconds: after a long stretch at the PC with no real break (five minutes away resets it, as for
    /// check-ins), she suggests standing up and some water, and the count starts again. Never in a call or the quiet
    /// hours, nor during a focus session (its breaks do it); in a full-screen game or presenting, only a ticket.
    /// </summary>
    private void MaybeStandUp(bool force = false)
    {
        var now = DateTimeOffset.Now;
        var since = _activeSince ?? now.AddMinutes(-50);
        var counted = now - (since > _lastStandUp ? since : _lastStandUp);
        var minutes = _settings.Now.StandUpMinutes;
        if (!force && (minutes <= 0 || _activeSince is null || counted < TimeSpan.FromMinutes(minutes) || Quiet(now.Hour) || _focus.Session is not null
                       || UserActivity.IdleFor() > TimeSpan.FromMinutes(1) || Presence.InCall))
            return;
        if (Her is null || _told.Any(t => t.Ticket == "STAND UP"))
            return;
        _lastStandUp = now;
        GD.Print($"Stand up: {(int)(now - since).TotalMinutes} min at the PC");
        _told.Enqueue(("STAND UP", FocusSession.NoteOnSitting(now - since), Lines.StandUp, now));
    }

    // ---- Rest and sleep ---------------------------------------------------------------------------------------------

    private readonly HashSet<Nudge> _givenTonight = [];
    private DateOnly _night;
    private DateTimeOffset? _sessionStarted;
    private long? _breakAtMs;

    /// <summary>
    /// Every fifteen seconds (see <see cref="Rest"/>): her words around bedtime (half an hour before, at it, once more
    /// after), whatever the quiet hours, while the player is at the PC; a break after two hours in a game (in a game,
    /// tickets); and, at most once a day, when they are back and free, what the record of days shows, or, with no bedtime
    /// set, asking for one in the evening.
    /// </summary>
    private void MaybeRest()
    {
        if (Her is null || Ledger?.Invoke() is not { } state || Rules?.Invoke() is not { } rules)
            return;
        var now = DateTimeOffset.Now;
        var here = UserActivity.IdleFor() < TimeSpan.FromMinutes(1);
        var night = GameCalendar.DayOf(now, rules);
        if (night != _night)
        {
            _night = night;
            _givenTonight.Clear();
        }
        // today's bedtime: one set today counts from tomorrow
        if (state.Today?.Bedtime is { } bedtime && here && Rest.AtBedtime(bedtime, now, _givenTonight, rules.DayStartsAt) is { } nudge)
        {
            _givenTonight.Add(nudge);
            GD.Print($"Rest: {nudge} ({Rest.Clock(bedtime)})");
            _told.Enqueue(("BEDTIME", Rest.Note(nudge, bedtime, rules.LateNightMultiplier), Lines.Rest(nudge), now));
        }
        if (state.Session is { } session)
        {
            if (session.StartedAt != _sessionStarted)
            {
                _sessionStarted = session.StartedAt;
                _breakAtMs = null;
            }
            if (!state.Paused && Rest.BreakDue(session.PlayedMs, _breakAtMs))
            {
                _breakAtMs = session.PlayedMs;
                GD.Print("Rest: a break after two hours of play");
                _told.Enqueue(("BREAK", Rest.Note(Nudge.LongPlay, TimeSpan.Zero, 1), Lines.Rest(Nudge.LongPlay), now));
            }
        }
        if (!_chat.Ready || _gameRunning || Busy || !here || Presence.DoNotDisturb || Presence.InCall || Quiet(now.Hour) || !Settled(now))
            return;
        var today = night.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        if (_chat.Store.Mark("rest.said") == today)
            return;
        var note = string.IsNullOrEmpty(_settings.Now.Bedtime) && now.Hour is >= 19 and < 23 ? AskBedtime(state, rules, now) : null;
        if (_settings.Now.Privacy.Days)                    // what the record of days shows, unless the player keeps it from her
            note ??= Rest.Notice(state.Days, state.Today?.Bedtime, state.TimeLeftMs(rules), rules.DayStartsAt);
        if (note is null)
            return;
        _chat.Store.SetMark("rest.said", today);
        GD.Print("Rest: she has something to say about rest");
        _told.Enqueue(("REST", note, "", now));             // only her mind says these
    }

    /// <summary>
    /// Whether the player has settled at the PC: two minutes of use since they last left it for a minute or more. What
    /// can wait waits for this, so it doesn't meet them as they sit down, while they aren't listening yet.
    /// </summary>
    private bool Settled(DateTimeOffset now) => _settledSince is { } since && now - since >= BackFor;

    /// <summary>With no bedtime set: asking for one, once; a second time a week on, if the record of days shows their usual night by then.</summary>
    private string? AskBedtime(LedgerState state, Rules rules, DateTimeOffset now)
    {
        var asked = _chat.Store.Mark("bedtime.asked")?.Split('|');
        var usual = _settings.Now.Privacy.Days ? Rest.Usual(state.Days, rules.DayStartsAt) : null;
        var times = asked is [var count, _] && int.TryParse(count, out var n) ? n : 0;
        if (times >= 2 || times == 1 && (usual is null || !DateOnly.TryParse(asked![1], out var first) || first.AddDays(7) > DateOnly.FromDateTime(now.DateTime)))
            return null;
        _chat.Store.SetMark("bedtime.asked", $"{times + 1}|{DateOnly.FromDateTime(now.DateTime):O}");
        return Rest.AskBedtime(usual);
    }

    /// <summary>Her tool sets the player's bedtime once they confirm it on her card; it counts from tomorrow.</summary>
    private async Task<string> SetBedtime(string time, CancellationToken cancel)
    {
        if (Rest.ParseBedtime(time) is not { } bedtime)
            return "That isn't a bedtime the cabinet takes: a time from 20:00 to 03:30, like 23:30.";
        var answer = await ConfirmOnCard($"Your bedtime: {Rest.Clock(bedtime)}?", "From tomorrow: a word half an hour before it, and play after it costs more until 04:00", cancel);
        if (answer != true)
            return answer is false ? "The player said not that one. Take that lightly." : "The player didn't answer, so it isn't set.";
        _settings.Save(_settings.Now with { Bedtime = Rest.Clock(bedtime) });
        RulesChanged?.Invoke();
        GD.Print($"Rest: bedtime set to {Rest.Clock(bedtime)}, from tomorrow");
        return $"Set: {Rest.Clock(bedtime)}, from tomorrow.";
    }

    /// <summary>What a focus session or a long sitting has for her to say, once she is free (as reminders are); stale after three minutes.</summary>
    private void PassOnTold()
    {
        var now = DateTimeOffset.Now;
        if (_told.Count == 0 || Busy || _reminding.Count > 0 || now < _quietFrom || Her is null)
            return;
        var (ticket, note, line, at) = _told.Dequeue();
        if (now - at > TimeSpan.FromMinutes(3))
            return;
        _ = Tell(ticket, note, line);
    }

    private async Task Tell(string ticket, string note, string line)
    {
        if (Presence.DoNotDisturb || _gameRunning)
        {
            Notice?.Invoke(ticket, line);                   // they can't be spoken to now: only the ticket
            return;
        }
        if (!_settings.Now.SpeechBubbles || Her is null)
            return;
        _announcing = true;
        try
        {
            _lively(30);
            if (_chat.Ready)
            {
                // in a call, only her bubble: her voice would be heard by everyone in it
                await _chat.Announce(note, line, Situation(), Her, Presence.InCall ? null : _voice.Begin(), _voice.Language);
                if (_chat.Replied is { } said)
                    _listening.Said(answer: false, said);
            }
            else
                Say(line);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Focus: {e}");                     // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _announcing = false;
        }
    }

    // ---- Her focus and music tools ---------------------------------------------------------------------------------------

    /// <summary>Her focus_session tool: what she asked for, done, and how things stand now (what she says, she says herself).</summary>
    private string FocusAct(string action, int? focus, int? rest)
    {
        string Stands() => _focus.Session!.Describe(DateTimeOffset.Now);    // read after what was done: a session just started has all its time
        switch (action)
        {
            case "start" when _focus.Session is not null:
                return $"A session is already running. {Stands()}";
            case "start":
                _focus.Start(focus, rest, byPlayer: false);
                return $"Started. {Stands()} The screen counts down; you'll be told when the part ends." +
                       (_focus.WithMusic ? " Their music plays while they focus." : "");
            case "stop" or "pause" or "resume" or "skip" when _focus.Session is null:
                return "No focus session is running.";
            case "stop":
                var done = _focus.Session!.Done;
                _focus.Stop(byPlayer: false);
                return $"Stopped, after {Words.Count(done, "focus part")}.";
            case "pause":
                _focus.Pause();
                return $"Paused. {Stands()}";
            case "resume":
                _focus.Resume();
                return $"Going again. {Stands()}";
            case "skip":
                _focus.Skip(byPlayer: false);
                return $"Skipped to the next part. {Stands()}";
            default:
                return "Unknown action: use start, pause, resume, skip or stop.";
        }
    }

    /// <summary>Her music tool (the player has allowed it by now).</summary>
    private async Task<string> MusicAct(string action, string? name)
    {
        var app = _music.Now?.App ?? "the music app";
        var saved = string.Join(", ", _settings.Now.Playlists.Select(p => $"\"{p.Name}\""));
        return action switch
        {
            "start_playlist" or "play_playlist" when SavedPlaylist(name) is not { } playlist =>
                _settings.Now.Playlists.Length == 0 ? "No playlists are saved: they can add one in the screen's MUSIC app (a link copied in Spotify)."
                    : $"No saved playlist matches \"{name}\". Saved: {saved}.",
            "start_playlist" or "play_playlist" when SavedPlaylist(name) is { } playlist =>
                await _music.StartPlaylist(playlist.Link, playlist.Name) ? $"\"{playlist.Name}\" is playing from its beginning."
                    : $"\"{playlist.Name}\" is open in Spotify, but its play button never showed, so what played last carries on.",
            "play" => await _music.Play() ? $"{app} is playing." : "No music app is open and Spotify isn't on this PC.",
            "pause" => await _music.Pause() ? $"{app} is paused." : "No music app is open.",
            "next" => await _music.Next() ? "Skipped to the next track." : "No music app is open.",
            "previous" => await _music.Previous() ? "Back a track." : "No music app is open.",
            _ => "Unknown action: use play_playlist, play, pause, next or previous.",
        };
    }

    /// <summary>The saved playlist a name means (the same name, else one it is part of); no name: the one focus starts with, else the first.</summary>
    private SavedPlaylist? SavedPlaylist(string? name)
    {
        var saved = _settings.Now.Playlists;
        if (name is null)
            return saved.FirstOrDefault(p => p.Link == _settings.Now.FocusPlaylist) ?? saved.FirstOrDefault();
        return saved.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? saved.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
    }

    private bool Quiet(int hour) =>
        _settings.Now.QuietFrom > _settings.Now.QuietUntil
            ? hour >= _settings.Now.QuietFrom || hour < _settings.Now.QuietUntil
            : hour >= _settings.Now.QuietFrom && hour < _settings.Now.QuietUntil;

    private async Task AskHowItWent(Plan plan)
    {
        _announcing = true;
        try
        {
            _lively(60);
            Her!.Play(Moment.Greet);
            await _chat.Announce(Agenda.CheckIn(plan, DateTimeOffset.Now, _activities.Within(plan.Text)), "", Situation(), Her, _voice.Begin(), _voice.Language);
            if (_chat.Replied is { } asked)
                _listening.Said(answer: false, asked);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Check-in: {e}");
        }
        finally
        {
            _announcing = false;
        }
    }

    /// <summary>The player cut her off: see <see cref="Chat.Interrupt"/>.</summary>
    internal Task Interrupt() => _chat.Interrupt(_voice, Her);

    /// <summary>Her card is asking: what the player says next may answer it (see <see cref="SpokenAnswer"/>).</summary>
    internal bool CardOpen => _permission.IsOpen;

    /// <summary>The player answered her card aloud.</summary>
    internal void AnswerCard(Answer answer) => _permission.Choose(answer);

    /// <summary>She stops talking, but the reply goes on (her card still waits for its answer).</summary>
    internal void Hush() => _voice.Stop();

    /// <summary>Her voice is saying something, or about to.</summary>
    internal bool Speaking => _voice.Speaking;

    /// <summary>Full pace for this many seconds: something worth watching is under way.</summary>
    internal void Lively(double seconds) => _lively(seconds);

    /// <summary>She is answering, speaking, listening, or hearing what was just said.</summary>
    private bool Busy => _chat.Answering || _voice.Speaking || _listening.Busy || _announcing;

    private void PassOnReminders()
    {
        var now = DateTimeOffset.Now;
        if (Busy)
            _quietFrom = now.AddSeconds(1.5);               // a breath after she stops before she starts again
        if (Busy || _reminding.Count == 0 || now < _quietFrom)
            return;
        var (due, at) = _reminding.Dequeue();
        if (now - at > TimeSpan.FromMinutes(10))
        {
            GD.Print("Reminders: she was busy too long to say it; the ticket did");
            return;
        }
        GD.Print($"Reminders: she passes {due.Count} on, {(now - at).TotalSeconds:0.0} s after it went off");
        _ = PassOn(due);
    }

    private async Task PassOn(IReadOnlyList<Plan> due)
    {
        if (Her is null || !_settings.Now.SpeechBubbles)
            return;
        _announcing = true;
        try
        {
            var now = DateTimeOffset.Now;
            var lead = _settings.Now.ReminderLead;
            _lively(60);
            Her.Play(Moment.Greet);
            if (_chat.Ready && _settings.Now.ReminderSpoken)
                // in a call, only her bubble: her voice would be heard by everyone in it
            {
                await _chat.Announce(Agenda.Announcement(due, now, lead), Lines.Reminder(due, now), Situation(), Her,
                                     Presence.InCall ? null : _voice.Begin(), _voice.Language);
                if (_chat.Replied is { } said)
                    _listening.Said(answer: false, said);
            }
            else
                Say(Lines.Reminder(due, now));
        }
        catch (Exception e)
        {
            GD.PrintErr($"Reminders: {e}");                 // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _announcing = false;
        }
    }

    /// <summary>A line in her speech bubble, said aloud too when her voice can, unless the user has asked for quiet.</summary>
    public void Say(string line)
    {
        if (!_settings.Now.SpeechBubbles || Her is null)
            return;
        Her.Say(line);
        _voice.Line(line);
        _chat.Said(line);
        _listening.Said(answer: false, line);
    }

    /// <summary>She waves (no line about play time: the user found it repetitive, 2026-10-07); when she can talk, the slip to type to her opens too.</summary>
    public void Clicked()
    {
        if (_listening.HeldToTalk)
            return;                                         // that was a hold: she was being talked to
        var replying = Replying;
        _listening.Drop();
        _ = Interrupt();
        _lively(6);
        if (!replying)
            Her?.Play(Moment.Greet);                        // cut off, she stops and looks at the player instead
        OpenChat();
    }

    /// <summary>The window is solid here: her figure, or the slip.</summary>
    public bool Hits(Vector2 canvasPoint) =>
        Her?.Hits(canvasPoint) == true || _chatBox.Hits(canvasPoint) || _permission.Hits(canvasPoint);

    /// <summary>The slip keeps the mouse here, for typing.</summary>
    public bool Typing(Vector2 canvasPoint) => _chatBox.Hits(canvasPoint);

    /// <summary>Closes the slip (Escape); false when it wasn't open.</summary>
    public bool CloseSlip()
    {
        if (!_chatBox.IsOpen)
            return false;
        _chatBox.Close();
        return true;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && _chatBox?.IsOpen == true && _chatBox.IsEmpty)
            _chatBox.Close();                               // clicked away without typing
    }

    /// <summary>The cabinet is closing: her mind lets go of its files and her voice's server ends.</summary>
    public void Shutdown()
    {
        _chat?.Dispose();
        _voice?.Shutdown();
    }

    /// <summary>Starts or ends a conversation (the tray's switch): she listens after everything she says.</summary>
    public void Converse(bool on) => _listening.Converse(on);

    public override void _Process(double delta)
    {
        PassOnReminders();
        PassOnTold();
        ThinkOverMemos();
    }

    /// <summary>A memo of the player's own to write (the screen's menu, or tray → Write a memo…).</summary>
    public void WriteMemo() => _memos.Write();

    /// <summary>All the memos: the memory book, at its Memos tab.</summary>
    public void OpenMemos()
    {
        OpenMemoryBook();
        _memoryBook?.ShowTab("memos");
    }

    /// <summary>The agenda: the memory book, at its Agenda tab.</summary>
    public void OpenAgenda()
    {
        OpenMemoryBook();
        _memoryBook?.ShowTab("agenda");
    }

    /// <summary>
    /// A memo the player wrote, once she is free: she thinks it over unseen, and acts on it (a reminder, a date on
    /// the agenda, a word of help) or lets it be, saying nothing. Either way it is in the conversation, so she knows it's there.
    /// </summary>
    private void ThinkOverMemos()
    {
        if (_written.Count == 0 || Busy || DateTimeOffset.Now < _quietFrom || Her is null)
            return;
        var (title, text) = _written.Dequeue();
        _ = ThinkOver(title, text);
    }

    private async Task ThinkOver(string title, string text)
    {
        _announcing = true;
        try
        {
            await _chat.Consider(Memo.Written(title, text), Situation(), Her!, () => Presence.InCall ? null : _voice.Begin(), _voice.Language);
            if (_chat.Replied is { } said)
            {
                _lively(30);
                _listening.Said(answer: false, said);
            }
            else
                GD.Print($"Memos: she read \"{title}\" and let it be");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Memos: {e}");                     // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _announcing = false;
        }
    }

    // ---- The tray's switches ---------------------------------------------------------------------------------------------

    /// <summary>Someone on the stool, or nobody.</summary>
    public void SetCompanion(bool on)
    {
        _settings.Save(_settings.Now with { ShowCompanion = on });
        ShowHer(on);
    }

    public void SetBubbles(bool on)
    {
        _settings.Save(_settings.Now with { SpeechBubbles = on });
        if (!on)
            Her?.Hush();
    }

    public void SetVoice(bool on)
    {
        _settings.Save(_settings.Now with { Voice = on });
        WakeVoice();
    }

    public void OpenCharactersFolder()
    {
        Directory.CreateDirectory(_characters);             // files put here by hand show up in the menu after a restart
        OS.ShellOpen(_characters);
    }

    // ---- Who is on the stool ---------------------------------------------------------------------------------------------

    /// <summary>Seats the companion on the stool (built the first time she is wanted) or leaves it empty.</summary>
    private void ShowHer(bool show)
    {
        if (show && Her is null && CompanionView.Available(_characters) is { Length: > 0 } characters)
        {
            var character = characters.FirstOrDefault(c => c.Id == _settings.Now.Companion) ?? characters[0];
            var her = Her = new CompanionView();
            AddChild(her);
            MoveChild(her, 0);                              // under the slip
            try
            {
                her.Build(_cabinet.Skin, character);
                her.Spoken = () => _voice.Progress;
                _chat.Seat(character.Id, character.Persona(_characters, _settings.Now.Username));
                _voice.Seat(VoiceFolder(character.Id));
            }
            catch (Exception e)
            {
                // a model file of the user's that will not load must not take the cabinet down with it
                GD.PrintErr($"Companion '{character.Id}': {e.Message}");
                _cabinet.Screen.Flash("NO GO", $"{character.Name}: {e.Message}".ToUpperInvariant(), 6.0);
                her.QueueFree();
                Her = null;
            }
        }
        Her?.SetActive(show);
        WakeVoice();
    }

    /// <summary>Her voice runs while she is shown and it is switched on (it still rests while a game runs).</summary>
    private void WakeVoice() => _voice.Enabled = _settings.Now.Voice && _settings.Now.ShowCompanion && Her is not null;

    /// <summary>Where a character's voice is kept: a folder beside them in the user's characters folder.</summary>
    private string VoiceFolder(string characterId) => Path.Combine(_characters, characterId + ".voice");

    /// <summary>Someone else takes the stool.</summary>
    public void Seat(string id)
    {
        _settings.Save(_settings.Now with { Companion = id });
        Her?.QueueFree();
        Her = null;
        ShowHer(_settings.Now.ShowCompanion);
    }

    public (string Id, string Name)[] Characters() => [.. CompanionView.Available(_characters).Select(c => (c.Id, c.Name))];

    /// <summary>The character chosen for the stool (whether shown or not), with the name their persona goes by.</summary>
    private (string Id, string Name)? OnStool()
    {
        if (_chat.Conversation is { } conversation)
            return (conversation.Character, conversation.Persona.Name);
        var characters = CompanionView.Available(_characters);
        return (characters.FirstOrDefault(c => c.Id == _settings.Now.Companion) ?? characters.FirstOrDefault()) is { } c ? (c.Id, c.Name) : null;
    }

    // ---- Bringing characters, cards and voices in -------------------------------------------------------------------------

    /// <summary>Asks for a model, card or voice with the system's own file dialog, then adds it.</summary>
    public void PickCharacter() => Pick("Add a character", FileDialog.FileModeEnum.OpenFile,
        ["*.vrm, *.glb, *.gltf, *.zip, *.png, *.json, *.wav, *.mp3, *.ogg, *.flac ; Characters, character cards and voices",
         "*.vrm, *.glb, *.gltf, *.zip ; 3D characters (VRM, glTF, or a zip holding one)",
         "*.png, *.json ; Character cards (SillyTavern style) for whoever is on the stool",
         "*.zip, *.wav, *.mp3, *.ogg, *.flac ; Voices (a voice's .zip, or clips of it)"], Add);

    /// <summary>Asks for clips to add to her voice, or a voice's .zip to replace it.</summary>
    private void PickVoice() => Pick("Add clips or a voice", FileDialog.FileModeEnum.OpenFiles,
        ["*.zip, *.wav, *.mp3, *.ogg, *.flac ; Clips of her voice (3-10 seconds each), or a voice's .zip"], Add);

    /// <summary>Asks for a voice's folder (clips with their words, maybe a model; see <see cref="VoiceSettings.FolderGuide"/>), then adds it.</summary>
    private void PickVoiceFolder() => Pick("Add a voice's folder", FileDialog.FileModeEnum.OpenDir, [], paths =>
    {
        if (VoiceProfile.LooksLikeVoice(paths[0]))
            Add(paths);
        else
            _cabinet.Screen.Flash("VOICE", "NO CLIPS OR MODEL IN THAT FOLDER", 4.0);
    });

    private void Pick(string title, FileDialog.FileModeEnum mode, string[] filters, Action<string[]> picked)
    {
        var dialog = new FileDialog
        {
            Title = title,
            ModeOverridesTitle = false,
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentDir = OS.GetSystemDir(OS.SystemDir.Downloads),
            Filters = filters,
        };
        void Done(string[] paths)
        {
            dialog.QueueFree();
            picked(paths);
        }
        dialog.FileSelected += path => Done([path]);
        dialog.FilesSelected += paths => Done(paths);
        dialog.DirSelected += path => Done([path]);
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        _window.Rise(4, _settings.Now.AlwaysOnTop);
        DisplayServer.WindowMoveToForeground();             // the dialog belongs to the cabinet's window: bring both forward
        dialog.PopupCentered();
    }

    // ---- Handing her things ------------------------------------------------------------------------------------------

    /// <summary>Asks for pictures or files to hand her with the next message.</summary>
    private void PickFiles() => Pick("Hand her a picture or a file", FileDialog.FileModeEnum.OpenFiles,
        ["*.png, *.jpg, *.jpeg, *.webp, *.gif, *.pdf, *.txt, *.md, *.csv, *.json, *.log, *.py, *.cs, *.js, *.ts, *.java, *.cpp, *.c, *.h, *.go, *.rs, *.sql, *.html, *.css ; Pictures, PDFs and text",
         "* ; Any file"], Hand);

    /// <summary>Files dropped on her, pasted or picked: each waits on the slip, as a chip, to go with the next message.</summary>
    private void Hand(string[] paths)
    {
        if (!CanBeHanded())
            return;
        foreach (var path in paths)
            if (!Hand(Handing.From(path)))
                break;
        if (_chatBox.Handed.Count > 0)
            OpenChat();
    }

    /// <summary>One thing ready to hand over, onto the slip; false once the slip is full.</summary>
    private bool Hand((Attachment? Thing, Texture2D? Thumb, string? Why) handing)
    {
        if (handing.Thing is not { } thing)
            _cabinet.Screen.Flash("CAN'T", handing.Why!, 3.0);
        else if (!_chatBox.Hand(thing, handing.Thumb))
        {
            _cabinet.Screen.Flash("SLIP FULL", $"{ChatBox.MostHanded} THINGS AT MOST", 3.0);
            return false;
        }
        return true;
    }

    /// <summary>A picture in hand (pasted, a screenshot), to go with the next message.</summary>
    private void HandPicture(Image picture, string name, bool photo)
    {
        if (!CanBeHanded())
            return;
        var (thing, thumb) = Handing.Picture(picture, name, photo);
        if (!_chatBox.Hand(thing, thumb))
            _cabinet.Screen.Flash("SLIP FULL", $"{ChatBox.MostHanded} THINGS AT MOST", 3.0);
        else
            OpenChat();
    }

    private PhoneCard? _phone;

    /// <summary>
    /// The slip's "From your phone…": a card with a code to scan; a photo sent from the phone through it lands on the
    /// slip, as one dropped on her would.
    /// </summary>
    private void OpenPhone()
    {
        if (!CanBeHanded())
            return;
        if (IsInstanceValid(_phone))
        {
            _phone!.GrabFocus();
            return;
        }
        _phone = new PhoneCard();
        AddChild(_phone);
        _phone.Build(_chat.Conversation?.Persona.Name ?? "her", thisPcOnly: Paper.OffScreen);
        _phone.Received += (name, data) =>
        {
            Hand(Handing.From(name, data));
            if (_chatBox.Handed.Count > 0)
                OpenChat(focus: false);
        };
        _phone.Open();
    }

    /// <summary>Dev: a pretend phone opens the card's page, sends a photo through it, and tries a wrong key.</summary>
    private async Task DevPhone(string path)
    {
        if (_phone?.DevAddress is not { } address)
            return;
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var page = await http.GetAsync(address);
        GD.Print($"Phone (dev): the page answers {(int)page.StatusCode} with {(await page.Content.ReadAsStringAsync()).Length} characters");
        var photo = new System.Net.Http.ByteArrayContent(await File.ReadAllBytesAsync(path));
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, address + "/photo") { Content = photo };
        request.Headers.Add("X-Name", Uri.EscapeDataString(Path.GetFileName(path)));
        GD.Print($"Phone (dev): sending {Path.GetFileName(path)} gets {(int)(await http.SendAsync(request)).StatusCode}");
        GD.Print($"Phone (dev): a wrong key gets {(int)(await http.GetAsync(address + "x")).StatusCode}");
    }

    /// <summary>Things can be handed to her only while her mind is connected (it is what looks at them); the screen says so otherwise.</summary>
    private bool CanBeHanded()
    {
        if (_chat.Ready && Her is not null)
            return true;
        _cabinet.Screen.Flash("HER MIND", "CONNECT IT IN CHAT SETTINGS", 3.0);
        return false;
    }

    /// <summary>The slip's "Your screen": the screen the pointer is on, as it is now, to go with the next message.</summary>
    private void HandScreen()
    {
        if (Screenshot.Take() is { } screen)
            HandPicture(screen, "screen", photo: false);
        else
            _cabinet.Screen.Flash("SCREEN", "COULDN'T TAKE IT", 3.0);
    }

    /// <summary>
    /// Copies models (picked or dropped) into the user's characters folder and seats the first of them. A character
    /// card goes to whoever is on the stool instead (a model dropped with it first), who then greets as the card says;
    /// so does a voice. Anything else she can take (a picture, a PDF, a text file) is handed to her: it waits on the
    /// slip to go with the next message.
    /// </summary>
    private void Add(string[] paths)
    {
        var handed = paths.Where(Handable).ToArray();
        if (handed.Length > 0)
        {
            Hand(handed);
            paths = [.. paths.Except(handed)];
            if (paths.Length == 0)
                return;
        }
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
                    if ((added.FirstOrDefault() ?? OnStool()?.Id) is { } who && CharacterFiles.ImportCard(path, _characters, who) is { } name)
                        cards.Add(name);
                }
                else if (!CharacterFiles.HoldsModel(path) && VoiceProfile.LooksLikeVoice(path))
                {
                    if ((added.FirstOrDefault() ?? OnStool()?.Id) is { } who)
                        voice = VoiceProfile.Import(path, VoiceFolder(who));
                }
                else if (CharacterFiles.Import(path, _characters) is { } id)
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
            CharactersAdded?.Invoke(Characters(), added[0]);
        _settings.Save(_settings.Now with { ShowCompanion = true });
        Seat(added.FirstOrDefault() ?? OnStool()!.Value.Id);        // seated again, so a new card takes effect
        _lively(6);
        Her?.Play(Moment.Greet);
        if (cards.Count > 0 && _chat.Conversation?.Persona.Greeting is { } greeting && FirstLine(greeting) is { } line)
            GetTree().CreateTimer(0.8).Timeout += () => Say(line);      // once she has sat down: the bubble goes over her seated head
        if (voice is not null)
            AddedVoice(voice);
    }

    /// <summary>A dropped file that is for her to look at, not a character, a character card or a voice.</summary>
    private static bool Handable(string path)
    {
        if (Directory.Exists(path) || !File.Exists(path) || CharacterFiles.HoldsModel(path) || VoiceProfile.LooksLikeVoice(path))
            return false;
        try
        {
            var start = new byte[8192];
            int read;
            using (var file = File.OpenRead(path))
                read = file.Read(start, 0, start.Length);
            if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".json" && InsertCoin.Mind.CharacterCard.Read(File.ReadAllBytes(path)) is not null)
                return false;                               // a character card
            return Attachment.TypeOf(Path.GetFileName(path), start.AsSpan(0, read)) is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The first sentence of a card's greeting, without its *actions*, if it is short enough for the bubble.</summary>
    private static string? FirstLine(string greeting)
    {
        var spoken = System.Text.RegularExpressions.Regex.Replace(greeting, @"\*[^*]*\*", " ").Trim();
        var end = spoken.IndexOfAny(['.', '!', '?', '。', '！', '？', '\n']);
        var line = (end >= 0 ? spoken[..(end + 1)] : spoken).Trim();
        return line.Length is > 1 and <= 140 ? line : null;
    }

    /// <summary>A voice was just given to whoever is on the stool: she tries it out, or says what it still needs.</summary>
    private void AddedVoice(VoiceProfile voice)
    {
        _voice.Seat(_voice.Folder);
        if (voice.Missing is not null || VoiceServer.Check(_settings.Now.VoiceEngine) is not null)
        {
            _cabinet.Screen.Flash("VOICE", "ALMOST THERE: SEE VOICE SETTINGS", 4.0);
            OpenVoiceSettings();
            return;
        }
        _cabinet.Screen.Flash("VOICE", $"{voice.Name} - WARMING UP".ToUpperInvariant(), 4.0);
        _lively(20);
        _ = _voice.Test();
    }

    // ---- Talking with her ------------------------------------------------------------------------------------------------

    private Provider? ChatProvider() => Provider.From(_settings.Now.ChatProtocol, _settings.Now.ChatBaseUrl, _settings.Now.ChatModel);

    /// <summary>Opens the slip at the foot of the stool for the user to type to her, once she can answer.</summary>
    /// <param name="focus">Bring the window forward for typing (the user clicked her); dev actions leave focus alone.</param>
    internal void OpenChat(bool focus = true)
    {
        if (!_chat.Ready || Her is null)
            return;
        if (focus)
            DisplayServer.WindowMoveToForeground();
        _chatBox.Open(new Vector2(14, _cabinet.Skin.Canvas.Y - ChatBox.Height - 14), $"Say something to {_chat.Conversation!.Persona.Name}…");
    }

    internal async Task Ask(string said)
    {
        if (Her is null)
            return;
        var handed = _chatBox.TakeHanded();                 // what waits on the slip goes with it, typed or said
        _listening.Drop();
        await Interrupt();                                  // asking something new cuts off what she was still saying
        _chatBox.SetWaiting(true);
        _lively(120);                                       // keep her lively while she thinks and talks
        try
        {
            await _chat.Ask(said, Situation(), Her, _voice.Begin(thinkAloud: true), _voice.Language, handed);
            if (_chat.Replied is { } reply)
                _listening.Said(answer: true, reply);
            if (_chat.TakeShot() is { } shot && Her is { } her)
            {
                // she took a look at their screen: once she has said she will, she sees it and answers
                for (var waited = 0; _voice.Speaking && waited < 80; waited++)
                    await ToSignal(GetTree().CreateTimer(0.1), Godot.Timer.SignalName.Timeout);
                await _chat.Announce($"This is the screenshot you took of {shot.Of}.", "", Situation(), her, _voice.Begin(thinkAloud: true), _voice.Language, [shot.Shot]);
                if (_chat.Replied is { } seen)
                    _listening.Said(answer: true, seen);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"Chat: {e}");                      // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _chatBox.SetWaiting(false);
            _lively(10);
        }
    }

    /// <summary>Where things stand on the cabinet, for her: the cabinet's own account, a focus session, the music, and the memos on it.</summary>
    private string Situation()
    {
        var lines = new List<string> { _situation() };
        if (_focus.Session is { } session)
            lines.Add(session.Describe(DateTimeOffset.Now));
        if (_music.Now is not null && _settings.Now.Privacy.Music)
            lines.Add(_music.Describe());
        if (_settings.Now.Playlists.Length > 0)
            lines.Add("Their saved playlists: " + string.Join(", ", _settings.Now.Playlists.Select(p =>
                $"\"{p.Name}\"{(p.Link == _settings.Now.FocusPlaylist ? " (starts their focus sessions)" : "")}")) + ".");
        lines.Add(_activities.Describe());
        var memos = _memos.OnTheCabinet.Select(t => $"\"{t}\"").ToList();
        if (memos.Count > 0)
            lines.Add($"Memos on the cabinet (hanging from the slot or pinned on its side): {string.Join(", ", memos)}.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// She prints a memo: she sends it from her phone, and a moment later it comes out of the cabinet's slot.
    /// </summary>
    private string PrintMemo(string title, string text, string by)
    {
        _memos.Print(title, text, by, delay: Her?.SendMemo() ?? 0);
        return "Printed: it's hanging from the cabinet's slot, for the player to read, pin on the cabinet's side or keep.";
    }

    /// <summary>
    /// She asks whether she may use a tool: the card by her stool, above the slip they type on, and her eyes on the
    /// player while they decide.
    /// </summary>
    private async Task<Answer> AskPlayer(Asking asking, CancellationToken cancel)
    {
        if (Her is null)
            return Answer.None;
        Her.Play(Moment.Listen);
        var asked = _permission.Ask(asking, new Vector2(14, CardBottom), cancel);
        _listening.CardShown();
        var answer = await asked;
        _listening.CardGone();
        Her?.Play(Moment.Heard);
        GD.Print($"Tools: she asked to use {asking.Tool.Name} on {asking.Detail}: {answer}");
        return answer;
    }

    /// <summary>Where her card stands: at the foot of the stool, above the slip and whatever waits on it.</summary>
    private float CardBottom => _cabinet.Skin.Canvas.Y - 14 - (_chatBox.IsOpen ? _chatBox.Tall + 8 : 0);

    /// <summary>She counts an activity: the player confirms it on her card ("I'm going to the gym" isn't "I went").</summary>
    private Task<bool?> ConfirmActivity(string what, string detail, CancellationToken cancel) => ConfirmOnCard(what, detail, cancel);

    /// <summary>The player confirms something she does for them on her card: yes, not yet, or null when they let it be.</summary>
    private async Task<bool?> ConfirmOnCard(string what, string detail, CancellationToken cancel)
    {
        if (Her is null)
            return null;
        Her.Play(Moment.Listen);
        var asked = _permission.Confirm(what, detail, new Vector2(14, CardBottom), cancel);
        _listening.CardShown();
        var answer = await asked;
        _listening.CardGone();
        Her?.Play(Moment.Heard);
        GD.Print($"Card: \"{what}\" {answer switch { true => "confirmed", false => "not yet", _ => "not answered" }}");
        return answer;
    }

    /// <summary>The player logged an activity on the screen (DONE): it counts, or waits for proof; she says so in her own words.</summary>
    internal async Task<string> LogFromScreen(string name)
    {
        var said = await _activities.Log(name, null, null, null, confirmed: true);
        GD.Print($"Activities: {name} logged on the screen: {said}");
        var line = said.Contains("judge_proof", StringComparison.Ordinal) ? $"{name}? Show me a photo of it!"
            : said.StartsWith("Counted", StringComparison.Ordinal) ? $"{name}: that counts. Nice!" : said;
        _told.Enqueue(("DONE", $"The player logged \"{name}\" as done on the cabinet's screen. What came of it: {said}", line, DateTimeOffset.Now));
        return said;
    }

    /// <summary>Provider, server, key and model.</summary>
    public void OpenChatSettings()
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
        _chatSettings.Build(_chat, ChatProvider(), character.Name, _settings.Now.MemorySearch, _settings.Now.TalkKey, _settings.Now.HandsFree, Listening.HasEars, Listening.HasTurns, _settings.Now.ChatWindow,
                            _settings.Now.ChatPrices, _settings.Now.Currency, _settings.Now.Privacy, _settings.Now.AddOns);
        _chatSettings.PricesSaved += (model, prices, currency) =>
        {
            var all = new Dictionary<string, Prices>(_settings.Now.ChatPrices);
            if (prices is null)
                all.Remove(model);
            else
                all[model] = prices;
            _settings.Save(_settings.Now with { ChatPrices = all, Currency = currency });
        };
        _chatSettings.Saved += (provider, search, talkKey, handsFree, window, permissions, auto, privacy, addOns) =>
        {
            _settings.Save(_settings.Now with
            {
                ChatProtocol = provider?.Protocol.ToString(), ChatBaseUrl = provider?.BaseUrl, ChatModel = provider?.Model, MemorySearch = search,
                TalkKey = talkKey, HandsFree = handsFree, ChatWindow = window, ToolPermissions = new(permissions), ToolsAuto = auto,
                Privacy = privacy, AddOns = addOns,
            });
            _chat.SearchThrough(addOns.Search);
            _chat.Tools.Choose(permissions);
            _chat.Tools.Auto = auto;
            _chat.UseWindow(window);
            _chat.UseSearch(search);
            _chat.Connect(provider);
        };
        _chatSettings.Open();
    }

    /// <summary>The conversation with whoever is on the stool, as stored.</summary>
    public void OpenChatHistory()
    {
        if (IsInstanceValid(_history))
        {
            _history!.GrabFocus();
            return;
        }
        if (OnStool() is not { } character)
            return;
        _history = new ChatHistory();
        AddChild(_history);
        _history.Build(_chat.Store, character.Id, character.Name);
        _history.Open();
    }

    /// <summary>What whoever is on the stool remembers: notes, facts, moments.</summary>
    public void OpenMemoryBook()
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
        _memoryBook.Build(_chat.Store, character.Id, character.Name, _memos);
        _memoryBook.History += OpenChatHistory;
        _memoryBook.Open();
    }

    // ---- Her voice ------------------------------------------------------------------------------------------------------

    /// <summary>The GPT-SoVITS that runs her voice, and the voice of whoever is on the stool.</summary>
    public void OpenVoiceSettings()
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
        _voiceSettings.Build(_voice, _trainer, _settings.Now.VoiceEngine, character.Name);
        _voiceSettings.Saved += engine =>
        {
            var changed = engine != _settings.Now.VoiceEngine;
            _settings.Save(_settings.Now with { VoiceEngine = engine });
            if (changed)
                _voice.UseEngine(engine);
            _voice.Seat(VoiceFolder(character.Id));          // read again: its language, words, model or quality may have changed
        };
        _voiceSettings.AddVoice += PickVoice;
        _voiceSettings.AddFolder += PickVoiceFolder;
        _voiceSettings.Train += Train;
        _voiceSettings.StopTraining += () => _trainer.Stop("You stopped the training.");
        _voiceSettings.RemoveVoice += () =>
        {
            var folder = VoiceFolder(character.Id);
            if (_trainer.Running && _trainer.Folder == folder)
                _trainer.Stop("The voice was removed.");
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            _voice.Seat(folder);
            OpenVoiceSettings();
        };
        _voiceSettings.Open();
    }

    /// <summary>Trains the voice of whoever is on the stool a model of its own (her voice rests meanwhile), or says why not.</summary>
    private void Train()
    {
        if (_trainer.Check(_settings.Now.VoiceEngine, _voice.Profile, _gameRunning) is { } problem)
        {
            _trainer.Refuse(_voice.Folder, problem);
            return;
        }
        _voice.Training = true;
        _trainer.Start(_settings.Now.VoiceEngine!, _voice.Folder, _voice.Profile!);
        if (_trainer.Running)
            _cabinet.Screen.Flash("VOICE", "TRAINING - SHE RESTS HER VOICE", 4.0);
    }

    /// <summary>Training ended: her voice comes back, with its new model when it worked.</summary>
    private void TrainingEnded(string folder, VoiceProfile? trained)
    {
        _voice.Training = false;
        if (trained is null)
        {
            _cabinet.Screen.Flash("VOICE", "TRAINING STOPPED: SEE VOICE SETTINGS", 4.0);
            return;
        }
        _settings.Save(_settings.Now with { TrainingPace = _trainer.Pace });     // the next estimate is this PC's own
        if (Path.GetFullPath(folder) != Path.GetFullPath(_voice.Folder))
            return;                                         // someone else is on the stool now: their voice is untouched
        _voice.Seat(folder);
        _cabinet.Screen.Flash("VOICE", $"{trained.Name} TRAINED - HAVE A LISTEN".ToUpperInvariant(), 4.0);
        _lively(20);
        if (IsInstanceValid(_voiceSettings))
        {
            _voiceSettings!.QueueFree();                    // open again, to show its trained model
            _voiceSettings = null;
            Callable.From(OpenVoiceSettings).CallDeferred();
        }
        _ = _voice.Test();
    }

    // ---- Dev actions -----------------------------------------------------------------------------------------------------

    /// <summary>Her dev actions (see the README); false when <paramref name="action"/> isn't one of hers.</summary>
    public bool Dev(string action)
    {
        var (name, rest) = action.IndexOf(':') is var colon and >= 0 ? (action[..colon], action[(colon + 1)..]) : (action, "");
        var text = rest.Replace('_', ' ');                  // underscores for spaces
        switch (name)
        {
            case "point":                                   // point:x:y pretends the mouse pointer is at this canvas point
                var at = rest.Split(':');
                Her?.DevPointer(new Vector2(float.Parse(at[0]), float.Parse(at[1])));
                break;
            case "character": Seat(rest); break;            // character:id swaps who sits on the stool
            case "add": Add([rest]); break;                 // add:path as if it had been dropped on the window
            case "pick": PickCharacter(); break;            // the "Add a character…" file dialog
            case "chat":                                    // chat:text sends a line to her as if typed
                OpenChat(focus: false);
                _chatBox.DevSend(text);
                break;
            case "chatbox": OpenChat(focus: false); break;
            case "attach": Hand([rest]); break;             // attach:path as if dropped on her (a picture, a PDF, a text file)
            case "pastepicture":                            // pastepicture:path as if that picture had been pasted (the clipboard is left alone)
                HandPicture(Image.LoadFromFile(rest), "pasted", photo: false);
                break;
            case "clip": OpenChat(focus: false); _chatBox.DevClip(); break;     // clicks the slip's paper clip: its ways show
            case "fakescreen": Screenshot.StandIn = rest; break;                // fakescreen:path: screenshots take this picture instead
            case "screenshot": HandScreen(); break;         // the slip's "Your screen"
            case "phone": OpenPhone(); break;               // the slip's "From your phone…": the card with its code
            case "done": _ = LogFromScreen(rest.Replace('_', ' ')); break;     // done:name as if DID IT were tapped on the screen's DONE app
            case "rest":                                    // rest:winddown, bedtime, stillup or break: that word now; rest:notice or rest:ask: what the record shows, or asking for a bedtime
                if (Enum.TryParse<Nudge>(rest, ignoreCase: true, out var word) || rest == "break")
                {
                    word = rest == "break" ? Nudge.LongPlay : word;
                    _told.Enqueue(("BEDTIME", Rest.Note(word, TimeSpan.FromHours(23), 1.5), Lines.Rest(word), DateTimeOffset.Now));
                }
                else if (Ledger?.Invoke() is { } ledger && Rules?.Invoke() is { } rules)
                    _told.Enqueue(("REST", rest == "ask" ? Rest.AskBedtime(Rest.Usual(ledger.Days, rules.DayStartsAt))
                                       : Rest.Notice(ledger.Days, ledger.Today?.Bedtime, ledger.TimeLeftMs(rules), rules.DayStartsAt) ?? "Nothing in the record of days to mention.",
                                   "", DateTimeOffset.Now));
                break;
            case "proofroll":                               // proofroll:0 always wants proof, proofroll:1 never (instead of chance)
                _activities.Roll = double.Parse(rest, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "phonesend": _ = DevPhone(rest); break;     // phonesend:path as if a phone sent that photo through the card's page
            case "chatsettings": OpenChatSettings(); break;
            case "usage": OpenChatSettings(); _chatSettings?.DevUsage(); break;     // the chat settings' Usage tab
            case "listeningsettings": OpenChatSettings(); _chatSettings?.DevListening(); break;     // its Listening tab
            case "abilities": OpenChatSettings(); _chatSettings?.DevAbilities(); break;     // its Abilities tab
            case "privacy": OpenChatSettings(); _chatSettings?.DevPrivacy(); break;     // its Privacy tab
            case "addons": OpenChatSettings(); _chatSettings?.DevAddOns(); break;     // its Add-ons tab
            case "fakewindows": OpenWindows.StandIn = text.Split('|'); break;    // fakewindows:title|title: the window titles she reads instead
            case "memo":                                    // memo:title:text: she prints a memo, as the tool has her do
                var parts = text.Split(':', 2);
                Her?.Typing();
                GetTree().CreateTimer(4).Timeout += () => PrintMemo(parts[0], parts.Length > 1 ? parts[1] : parts[0], OnStool()?.Id ?? "");
                break;
            case "memoseed": _memos.DevSeed(OnStool()?.Id ?? ""); break;    // memos hanging, pinned and archived, to try them on
            case "focus":                                   // focus starts a session from the screen; focus:stop, :skip, :pause, :resume
                switch (rest)
                {
                    case "": _focus.Start(); break;
                    case "stop": _focus.Stop(); break;
                    case "skip": _focus.Skip(); break;
                    case "pause": _focus.Pause(); break;
                    case "resume": _focus.Resume(); break;
                }
                break;
            case "focustool":                               // focustool:start:50 as her tool would (action, minutes)
                var asked = rest.Split(':');
                GD.Print($"Focus: her tool says \"{FocusAct(asked[0], asked.Length > 1 ? int.Parse(asked[1]) : null, null)}\"");
                break;
            case "musicprobe": _music.DevProbe(); break;    // checks Windows' media controls answer (how many sessions, whose; no titles)
            case "standup": MaybeStandUp(force: true); break;   // she suggests standing up, as after a long sitting
            case "musicfake": _music.DevPlaying(rest is "" or "off" ? null : text.Split(':')[0], text.Split(':').ElementAtOrDefault(1) ?? ""); break;   // musicfake:Title:Artist pretends it plays; musicfake:off none
            case "musictool":                               // musictool:pause, musictool:start_playlist:Deep_Focus as her tool would
                var said = text.Split(':', 2);
                _ = MusicAct(said[0], said.Length > 1 ? said[1] : null).ContinueWith(t => GD.Print($"Music: her tool says \"{t.Result}\""));
                break;
            case "spotifyprobe": _music.DevSpotify(rest.Length > 0 ? text : null); break;     // what Spotify's window shows to press (spotifyprobe:Deep_Focus); presses nothing
            case "memoopen": _memos.DevOpen(rest.Length > 0 ? int.Parse(rest) : null); break;     // memoopen: the one hanging; memoopen:0 pinned at place 0
            case "memosheet": _memos.DevSheet(text); break;        // the latest sheet: pin, off, edit, done, print, delete, close, esc (not copy: it would take the clipboard); colour:pink, sticker:star, write:title:text, tell, move:dx:dy
            case "memohover": _memos.DevHover(rest is "" or "off" ? null : int.Parse(rest), off: rest == "off"); break;   // memohover: the hanging one; memohover:0 the top pinned; memohover:off
            case "memocarry":                               // memocarry:a:b carries the hanging memo to that spot on the side; memocarry alone, off the cabinet
                var to = rest.Split(':');
                _memos.DevCarry(to.Length == 2 ? new Vector2(float.Parse(to[0]), float.Parse(to[1])) : null);
                break;
            case "memowrite": _memos.Write(); break;        // a memo of the player's own
            case "memonote":                                // memonote:title:text: one of the player's own, printed as if written and saved
                var note = text.Split(':', 2);
                _memos.Print(note[0], note.Length > 1 ? note[1] : note[0], by: null);
                break;
            case "allow": _permission.Choose(Answer.Once); break;       // answers the card she asks with, as its buttons would
            case "always": _permission.Choose(Answer.Always); break;
            case "deny": _permission.Choose(Answer.No); break;
            case "memorybook":                              // memorybook:agenda opens it at that tab (notes, agenda, facts, moments, forget; retired)
                OpenMemoryBook();
                if (rest.Length > 0)
                    _memoryBook?.ShowTab(rest);
                break;
            case "chathistory": OpenChatHistory(); break;
            case "forget": if (IsInstanceValid(_memoryBook)) _memoryBook!.DevForget(rest); break;   // forget:chat|all|me, as the memory book's buttons
            case "memoryseed": SeedMemory(); break;         // a few notes, facts, a moment and a line, to try the memory book on
            case "checkin": MaybeCheckIn(force: true); break;   // she asks about something past now, whatever the player is doing
            case "presence":                                // logs what she would make of this moment
                var clock = System.Diagnostics.Stopwatch.StartNew();
                GD.Print($"Presence: do not disturb {Presence.DoNotDisturb}, in a call {Presence.InCall}, idle {UserActivity.IdleFor().TotalSeconds:0} s ({clock.ElapsedMilliseconds} ms)");
                break;
            case "upkeep": _ = _chat.Upkeep(gameRunning: false, now: true); break;     // her upkeep now, without waiting for quiet
            case "say": Say(text); break;                   // say:text puts a line in her bubble (and says it, if her voice can)
            case "voice": Say(text); break;                 // voice:text: the same, named for testing her voice
            case "thinkingsounds": GD.Print($"Voice: {_voice.DevSaveSounds(rest)} thinking sounds saved in {rest}"); break;    // thinkingsounds:folder: her thinking sounds as WAV files
            case "voicesettings": OpenVoiceSettings(); break;
            case "voicetest": _ = _voice.Test(); break;      // her voice says its sample line (waiting for it to be ready)
            case "voicemodel":                              // voicemodel:v2ProPlus clones her voice with that base model, voicemodel:trained uses its own
                if (_voice.Profile is { } voice && (VoiceProfile.CloneVersions.Contains(rest) || rest == "trained" && voice.Trained))
                {
                    (voice with { CloneWith = rest == "trained" ? null : rest }).Save(_voice.Folder);
                    _voice.Seat(_voice.Folder);
                }
                break;
            case "voicetrain": Train(); break;              // trains her voice a model of its own, as the window's Train does
            case "voicetrainstop": _trainer.Stop("Stopped by a dev action."); break;
            case "quiet": SetBubbles(!_settings.Now.SpeechBubbles); break;
            case "click": Clicked(); break;                 // as if she had been clicked
            case "legs": Her?.DevLegs(rest); break;         // legs:left|right|both|ring sets which feet hang off the stool's ring
            case "closeup": Her?.DevCloseUp(rest != "off"); break;     // closeup:hands narrows her camera onto her hands, closeup:off puts it back
            case "moment": Her?.Play(Enum.Parse<Moment>(rest, ignoreCase: true)); break;
            default: return _listening.Dev(name, rest);
        }
        return true;
    }

    /// <summary>Dev: something for the memory book to show, about whoever is on the stool.</summary>
    private void SeedMemory()
    {
        if (OnStool() is not { } character)
            return;
        var now = DateTimeOffset.Now;
        _chat.Store.SetNote(MindStore.Shared, MindStore.AboutUser, "Bob is getting ready for interviews.");
        _chat.Store.SetNote(character.Id, MindStore.Us, "Friends since the cabinet arrived.");
        _chat.Store.AddFact(MindStore.Shared, "Bob likes Hades.", 5, now);
        _chat.Store.AddFact(character.Id, "Bob promised her a Hard problem this week.", 6, now);
        var line = _chat.Store.Add(character.Id, Speaker.User, "I have an interview on Friday.", now);
        _chat.Store.AddEpisode(character.Id, "Bob told her about his interview on Friday.", line, line, now);
        // the agenda: a plan, a reminder, and something just past
        _chat.Store.AddPlan("Job interview at Acme", now.Date.AddDays(3).AddHours(10), allDay: false, TimeSpan.FromHours(1), now, character.Id);
        _chat.Store.AddPlan("Take vitamins", now.Date.AddDays(1).AddHours(8), allDay: false, TimeSpan.Zero, now, character.Id, Repeat.Daily);
        _chat.Store.AddPlan("Calculus exam", now.Date.AddDays(-1), allDay: true, before: null, now, null);
        // and what a review would have retired
        _chat.Store.Retire(_chat.Store.AddFact(MindStore.Shared, "Bob wants to see a film on Saturday 5 September.", 4, now.AddDays(-40)), "the film date has passed", now);
        _chat.Store.AddFact(MindStore.Shared, "Bob's interview is on Friday.", 7, now);
        _chat.Store.Retire(_chat.Store.AddFact(MindStore.Shared, "Bob has an interview at the end of the week.", 7, now), "merged into \"Bob's interview is on Friday.\"", now);
    }
}
