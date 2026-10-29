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
    private EngineInstaller _installer = null!;
    private string? _hearingVoice;                          // the voice whose clips her ears are writing down
    private MemoryBook? _memoryBook;
    private ChatHistory? _history;
    private bool _gameRunning;
    private Listening _listening = null!;
    private bool _announcing;                               // telling the player something
    private readonly Queue<(string Title, string Text)> _written = new();     // the player's own memos, for her to think over
    private readonly List<Telling> _toTell = [];            // what she has to tell the player: reminders, focus turns, check-ins, rest… (see Reaching)
    private readonly List<(Notice Text, DateTimeOffset Since)> _inCase = [];   // texts that go if the player turns out to have gone
    private readonly Queue<(string Said, IReadOnlyList<Attachment> Handed)> _texts = new();   // what the player texted her, for her to answer
    private bool _texting;                                  // she is writing to the player by text: her cards come as buttons
    private bool _texted;                                   // the player's last words came by text
    private DateTimeOffset _quietFrom;                      // when she last stopped talking or listening

    /// <summary>Settings should open at this page (a voice just added that still needs something).</summary>
    public event Action<string>? OpenSettings;

    /// <summary>What a settings page shows changed (a voice added or trained, a character): it is made again if open.</summary>
    public event Action<string>? SettingsChanged;

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

    /// <summary>Something drawn over everything on the cabinet (the lifted screen, a memo's card) is over this canvas rectangle: her bubble gives way to it.</summary>
    public Func<Rect2, bool> OverBubble { get; set; } = _ => false;

    /// <summary>Something she would say, while the player can't be spoken to (a full-screen game, presenting): a ticket instead (title, line).</summary>
    public event Action<string, string>? Notice;

    /// <summary>The ticket at the screen corner, for a call of hers left unanswered (see <see cref="WaitForAnswer"/>).</summary>
    public AlertTicket? Ticket { get; set; }

    /// <summary>Sends a text to the player's phone, through the places on Settings → Your phone; set by the cabinet's loop.</summary>
    public Func<Notice, Task>? TextOut { get; set; }

    /// <summary>Whether any place to text the player through is on; set by the cabinet's loop. With none, what would be texted waits for them.</summary>
    public Func<bool> CanText { get; set; } = () => false;

    /// <summary>How long since the player's last input (keys, the mouse, a game pad); set by the cabinet's loop.</summary>
    public Func<TimeSpan> Idle { get; set; } = UserActivity.IdleFor;

    /// <summary>Texting with the player on Telegram, both ways (see <see cref="Texting"/>); set by the cabinet's loop.</summary>
    public Texting? Phone { get; set; }

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

        _chat = new Chat(dataDir)
        {
            ReminderLead = () => settings.Now.ReminderLead, KeepsInTouch = () => settings.Now.Reach == Reach.KeepsInTouch, Earns = () => settings.Now.Earning,
        };
        _chat.UseSearch(settings.Now.MemorySearch);        // remembered first, so connecting loads the right model
        _chat.UseWindow(settings.Now.ChatWindow);
        _chat.SearchThrough(settings.Now.AddOns.Search);
        _chat.Skills.Linked = settings.Now.SkillFolders;
        _chat.Skills.Off = settings.Now.SkillsOff.ToHashSet();
        _chat.Notes.Grants = settings.Now.SkillGrants;
        _chat.Connect(ChatProvider(), BackupProvider());
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
        _chat.AskSkillChange = (change, cancel) => ConfirmOnCard($"Update how {change.Skill} works:",
            change.Plainly + (change.Shortened ? "\nAll of it is under See the change." : ""), cancel, change.Lines);
        _chat.CantSee = model => _settings.Now.UnseeingModels.Contains(model);
        _chat.FoundCantSee += model => _settings.Save(_settings.Now with { UnseeingModels = [.. _settings.Now.UnseeingModels, model] });
        _permission = new PermissionCard();
        AddChild(_permission);
        _chat.Plan.Stopping += why => Callable.From(() => { _ = TellPlan(why); }).CallDeferred();
        _chat.Plan.Changed += () => Callable.From(() =>
        {
            if (_planCard is { } shown && _chat.Plan.Stopped != shown)
                _permission.Withdraw();                     // no longer so (signed in, disconnected, working again): the card goes
        }).CallDeferred();
        _chat.Backup.Back += () =>
        {
            if (_chat.BackupProvider is not null)           // still set: it was what she carried on with
                Callable.From(() => { _ = TellBack(); }).CallDeferred();
        };
        _chat.Backup.Forgot += () => Callable.From(() =>
        {
            GD.Print("ChatGPT: the backup changed after the yes; the yes is taken back");
            if (_chat.OnPlan && _chat.Plan.Stopped is { } why)
                _ = TellPlan(why);                          // still waiting on the plan: asked again, for the backup now set (or told plainly, with none)
        }).CallDeferred();
        _chat.AskPlayer = AskPlayer;
        _chat.Tools.Choose(settings.Now.ToolPermissions);
        _chat.Tools.Auto = settings.Now.ToolsAuto;
        _chat.Tools.Changed += () => _settings.Save(_settings.Now with { ToolPermissions = new(_chat.Tools.Chosen) });
        _chat.ActivitiesEarn(settings.Now.Earning.Activities);
        settings.Changed += () => _chat.ActivitiesEarn(settings.Now.Earning.Activities);
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
        _focus.Told += (note, line, answer) =>
        {
            if (!answer)                                    // a turn shows at the corner as it comes, as a reminder does (the user, 2026-10-10)
                Ticket?.Show("FOCUS", line, _settings.Now.ReminderTicketSeconds, [("OK", () => { })]);
            // a turn reaches them anywhere (focus can be a book away from the PC); her answer to their own press, only here
            Hand(new Telling("FOCUS", note, line, Matters.Must, Anywhere: !answer, DateTimeOffset.Now, TimeSpan.FromMinutes(3), Ticketed: !answer));
        };
        _chat.Focus = FocusAct;
        _chat.Music = MusicAct;
        _chat.Ledger = day => Ledger?.Invoke() is { } state && Rules?.Invoke() is { } rules
            ? InsertCoin.Mind.Situation.Ledger(state, rules, DateTimeOffset.Now, _settings.Now.Username, day)
            : "The cabinet has no numbers yet.";
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
        _installer = new EngineInstaller();
        AddChild(_installer);
        _installer.Installed += engine =>                   // GPT-SoVITS installed for them: her voice runs on it
        {
            _settings.Save(_settings.Now with { VoiceEngine = engine });
            _voice.UseEngine(engine);
            SettingsChanged?.Invoke("voice");
        };

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
        var track = new Godot.Timer { WaitTime = 1, Autostart = true };
        track.Timeout += Track;
        AddChild(track);
        // a test run never goes by whoever is at this PC (the user, busy elsewhere): the player is here, unless a dev action says otherwise
        if (Paper.OffScreen)
            _pretend = Whereabouts.Here;
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

    /// <summary>The cabinet's record of finished days, for her diary.</summary>
    public void Record(LedgerState state) => _chat.Record(state);

    /// <summary>Her memory, the agenda among it (shared by every character).</summary>
    public MindStore Store => _chat.Store;

    /// <summary>
    /// A reminder going off: something she must tell the player, wherever they are (see <see cref="Hand"/>); at the PC,
    /// once she has finished what she is doing, in her own words when her mind is connected. Its ticket has shown it
    /// already, so after ten minutes of waiting to say it she lets it go.
    /// </summary>
    public void Remind(IReadOnlyList<Plan> due, DateTimeOffset now) =>
        Hand(new Telling("REMINDER", Agenda.Announcement(due, now, _settings.Now.ReminderLead), Lines.Reminder(due, now), Matters.Must, Anywhere: true, now,
                         TimeSpan.FromMinutes(10), Ticketed: true, Plain: !_settings.Now.ReminderSpoken, Greet: true));

    // ---- Check-ins: how did it go? ---------------------------------------------------------------------------------

    private static readonly TimeSpan CheckInAfter = TimeSpan.FromMinutes(15), BackFor = TimeSpan.FromMinutes(2), Away = TimeSpan.FromMinutes(5),
        GentleGap = TimeSpan.FromHours(3);
    private DateTimeOffset? _activeSince;                   // the player has been at the PC since then, with no break of five minutes
    private DateTimeOffset? _settledSince;                  // and using it since then, with no pause of a minute: they have sat down to something
    private DateTimeOffset _lastCheckIn = DateTimeOffset.MinValue;

    /// <summary>
    /// Every fifteen seconds: when something the player told her about has been over for a while (see
    /// <see cref="Agenda.ToAskAbout"/>), or something going on they told her about a couple of days ago (an open thread, see
    /// <see cref="Threads"/>), she has something to ask: how it went, how it is going. Not in the quiet hours; Gentle, one every
    /// three hours at most. How it reaches them is the routing's (see <see cref="Hand"/>): at the PC once they are
    /// settled and free (a meeting that runs over keeps its call going, so she waits), by text while they are out.
    /// </summary>
    private void MaybeCheckIn(bool force = false)
    {
        var now = DateTimeOffset.Now;
        var mode = _settings.Now.CheckIns;
        if (!force && (mode == "Off" || Quiet(now.Hour) || mode == "Gentle" && now - _lastCheckIn < GentleGap))
            return;
        if (!_chat.Ready || Her is null || !_settings.Now.SpeechBubbles || _chat.Conversation is not { } conversation || _toTell.Any(t => t.Title == "CHECK-IN"))
            return;
        string note;
        if (conversation.ToAskAbout(CheckInAfter) is { } plan)
        {
            _chat.Store.Asked(plan.Id, now);                // once, whatever comes of it
            note = Agenda.CheckIn(plan, now, _settings.Now.Earning.Activities ? _activities.Within(plan.Text) : null);
        }
        else if (conversation.ThreadToAskAbout() is { } thread)
        {
            _chat.Store.AskedThread(thread.Id, now);        // once, until there is news of it
            note = Threads.CheckIn(thread, now);
        }
        else
            return;
        _lastCheckIn = now;
        GD.Print("Check-in: she has something to ask about");
        // only her mind asks it; it can wait for them, through the night if need be
        Hand(new Telling("CHECK-IN", note, "", Matters.Should, Anywhere: true, now, TimeSpan.FromHours(12), Greet: true));
    }

    // ---- Stand up, have some water --------------------------------------------------------------------------------------

    private DateTimeOffset _lastStandUp = DateTimeOffset.MinValue;

    /// <summary>
    /// Every fifteen seconds: after a long stretch at the PC with no real break (five minutes away resets it), she
    /// suggests standing up and some water, and the count starts again. Never in a call or the quiet hours, nor during a
    /// focus session (its breaks do it); in a full-screen game or presenting, only a ticket (see <see cref="Hand"/>).
    /// </summary>
    private void MaybeStandUp(bool force = false)
    {
        var now = DateTimeOffset.Now;
        var since = _activeSince ?? now.AddMinutes(-50);
        var counted = now - (since > _lastStandUp ? since : _lastStandUp);
        var minutes = _settings.Now.StandUpMinutes;
        if (!force && (minutes <= 0 || _activeSince is null || counted < TimeSpan.FromMinutes(minutes) || Quiet(now.Hour) || _focus.Session is not null
                       || Idle() > Reaching.Pause || Presence.InCall))
            return;
        if (Her is null || _toTell.Any(t => t.Title == "STAND UP"))
            return;
        _lastStandUp = now;
        GD.Print($"Stand up: {(int)(now - since).TotalMinutes} min at the PC");
        Hand(new Telling("STAND UP", FocusSession.NoteOnSitting(now - since), Lines.StandUp, Matters.Could, Anywhere: false, now, TimeSpan.FromMinutes(3)));
    }

    // ---- Rest and sleep ---------------------------------------------------------------------------------------------

    private readonly HashSet<Nudge> _givenTonight = [];
    private DateOnly _night;
    private DateTimeOffset? _sessionStarted;
    private long? _breakAtMs;

    /// <summary>
    /// Every fifteen seconds (see <see cref="Rest"/>): her words around bedtime (half an hour before, at it, once more
    /// after), whatever the quiet hours, while the player is at the PC; a break after two hours in a game (in a game,
    /// tickets); and, at most once a day, with the player at the PC, what the record of days shows, or, with no bedtime
    /// set, asking for one in the evening, which wait until they are settled and free (see <see cref="Hand"/>).
    /// </summary>
    private void MaybeRest()
    {
        if (Her is null || Ledger?.Invoke() is not { } state || Rules?.Invoke() is not { } rules)
            return;
        var now = DateTimeOffset.Now;
        var here = Idle() < Reaching.Pause;
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
            Hand(new Telling("BEDTIME", Rest.Note(nudge, bedtime, rules.LateNightMultiplier), Lines.Rest(nudge), Matters.Should, Anywhere: false, now,
                             TimeSpan.FromMinutes(20)));
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
                Hand(new Telling("BREAK", Rest.Note(Nudge.LongPlay, TimeSpan.Zero, 1), Lines.Rest(Nudge.LongPlay), Matters.Should, Anywhere: false, now,
                                 TimeSpan.FromMinutes(20)));
            }
        }
        if (!_chat.Ready || !here || Quiet(now.Hour))
            return;
        var today = night.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        if (_chat.Store.Mark("rest.said") == today)
            return;
        var note = string.IsNullOrEmpty(_settings.Now.Bedtime) && now.Hour is >= 19 and < 23 ? AskBedtime(state, rules, now) : null;
        var asking = note is not null;
        if (_settings.Now.Privacy.Days)                    // what the record of days shows, unless the player keeps it from her
            note ??= Rest.Notice(state.Days, state.Today?.Bedtime, state.TimeLeftMs(rules), rules.DayStartsAt);
        if (note is null)
            return;
        _chat.Store.SetMark("rest.said", today);
        GD.Print("Rest: she has something to say about rest");
        // only her mind says these; asking for a bedtime matters more than what the record shows
        Hand(new Telling("REST", note, "", asking ? Matters.Should : Matters.Could, Anywhere: false, now, TimeSpan.FromHours(2)));
    }

    /// <summary>
    /// Whether the player has settled at the PC: two minutes of use since they last left it for a minute or more. What
    /// can wait waits for this, so it doesn't meet them as they sit down, while they aren't listening yet. Where a dev
    /// action (or a test run) says the player is, they are settled there.
    /// </summary>
    private bool Settled(DateTimeOffset now) => _pretend is not null || _settledSince is { } since && now - since >= BackFor;

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
        var answer = await _chat.Unhurried(() => ConfirmOnCard($"Your bedtime: {Rest.Clock(bedtime)}?",
                                                               "From tomorrow: a word half an hour before it, and play after it costs more until 04:00", cancel));
        if (answer != true)
            return answer is false ? "The player said not that one. Take that lightly." : "The player didn't answer, so it isn't set.";
        _settings.Save(_settings.Now with { Bedtime = Rest.Clock(bedtime) });
        RulesChanged?.Invoke();
        GD.Print($"Rest: bedtime set to {Rest.Clock(bedtime)}, from tomorrow");
        return $"Set: {Rest.Clock(bedtime)}, from tomorrow.";
    }

    // ---- Reaching the player (docs/specs/2026-10-10-reaching-the-player.md) ------------------------------------------

    private Whereabouts? _where;                            // where the player was last made out to be, for the log
    private Whereabouts? _pretend;                          // dev: where the player is said to be, whatever the cabinet watches
    private DateTimeOffset _nextPass;                       // when what waits is looked at again: once a second, at once when something new comes
    private DateTimeOffset? _awaySince;                     // the player has been away since then
    private DateTimeOffset _chanceAt;                       // her last chance to reach them of her own accord (Keeps in touch)
    private string? _planSeen;                              // her plan to reach them as last logged

    /// <summary>
    /// Something to tell the player, from any feature: told at the PC, texted, kept for when they are back, or let go,
    /// as <see cref="Reaching"/> says for where they are; the most important first, once she is free (see <see cref="PassOn"/>).
    /// </summary>
    internal void Hand(Telling telling)
    {
        _toTell.Add(telling);
        _nextPass = DateTimeOffset.MinValue;
        GD.Print($"Reaching: {telling.Title} to tell ({telling.Matters.ToString().ToLowerInvariant()}{(telling.Anywhere ? "" : ", only at the PC")})");
    }

    /// <summary>Where the player is now (see <see cref="Reaching.Where"/>).</summary>
    private Whereabouts Where(DateTimeOffset now) => _pretend ?? Reaching.Where(Watching(now), AwayAfter, now);

    /// <summary>
    /// What the cabinet watches: their last input, a locked screen, being with her (in a conversation, being listened
    /// to, writing to her) or playing, the quiet hours, and what they last told her about leaving.
    /// </summary>
    private Watched Watching(DateTimeOffset now) =>
        new(Idle(), Presence.Locked, _listening.Conversing || _listening.Busy || _chatBox.IsOpen && !_chatBox.IsEmpty || Playing, Quiet(now.Hour),
            Reaching.Said(_chat.Store.Mark(Reaching.Mark)));

    /// <summary>A game runs and isn't paused for being away from the keyboard.</summary>
    private bool Playing => Ledger?.Invoke() is { Session: not null, Paused: false };

    /// <summary>The away time the player chose (Settings → Your phone).</summary>
    private TimeSpan AwayAfter => TimeSpan.FromMinutes(Math.Clamp(_settings.Now.AwayMinutes, Reaching.FewestAwayMinutes, Reaching.MostAwayMinutes));

    /// <summary>
    /// Every second: how long the player has been at the PC and settled there; where they are (logged when it changes);
    /// what they said about leaving, let go once they are back; and each text kept in case they had gone (see
    /// <see cref="InCase"/>), sent once they turn out to be away, or dropped once they use the PC again.
    /// </summary>
    private void Track()
    {
        var now = DateTimeOffset.Now;
        var idle = Idle();
        _activeSince = idle >= Away ? null : _activeSince ?? now - idle;
        _settledSince = idle >= Reaching.Pause ? null : _settledSince ?? now - idle;
        var watched = Watching(now);
        if (watched.Said is not null && Reaching.Back(watched, now))
        {
            _chat.Store.SetMark(Reaching.Mark, "");
            watched = watched with { Said = null };
            GD.Print("Reaching: they are back at the PC after saying they were off");
        }
        var where = Where(now);
        var back = where == Whereabouts.Here && _where is Whereabouts.Away or Whereabouts.Asleep;
        _awaySince = where == Whereabouts.Away ? _awaySince ?? now : null;
        if (where != _where)
        {
            _where = where;
            GD.Print($"Reaching: the player is {where.ToString().ToLowerInvariant()} (no input for {idle.TotalMinutes:0.0} min" +
                     (watched.Locked ? ", the screen locked" : "") + (watched.Said is { } said ? $", they said {(said.Asleep ? "goodnight" : "they were off")}" : "") +
                     (_pretend is not null ? ", as a dev action has it" : "") + ")");
        }
        if (_settings.Now.Reach == Reach.KeepsInTouch && _chat.Ready && Her is not null)
            KeepInTouch(now, where, back);
        foreach (var kept in _inCase.ToList())
        {
            if (_pretend is null && idle < now - kept.Since)       // used the PC since: they were there after all
            {
                _inCase.Remove(kept);
                GD.Print("Reaching: they were at the PC after all; no text");
            }
            else if (where is Whereabouts.Away or Whereabouts.Asleep)
            {
                _inCase.Remove(kept);
                GD.Print("Reaching: they had gone; the text kept for it goes");
                _ = Send(kept.Text);
            }
        }
    }

    /// <summary>
    /// Keeping in touch: her plan to reach the player comes due at its time, or at once when they are back at the PC;
    /// without one, her first chance comes half an hour into an absence. Whether she says anything, and whether she plans
    /// a next, is hers (see <see cref="Later"/>).
    /// </summary>
    private void KeepInTouch(DateTimeOffset now, Whereabouts where, bool back)
    {
        var mark = _chat.Store.Mark(Reaching.LaterMark);
        var later = Later.FromMark(mark);
        if (mark != _planSeen)
        {
            _planSeen = mark;
            if (later is not null)
                GD.Print($"Reaching: she plans to reach them again at {later.At:HH:mm}: {later.About}");
        }
        if (later is not null && (now >= later.At || back))
        {
            _chat.Store.SetMark(Reaching.LaterMark, "");
            _chanceAt = now;
            if (Quiet(now.Hour) && where == Whereabouts.Away)
                GD.Print("Reaching: her plan came due in the quiet hours with the player out; let go");
            else
                Hand(new Telling("LATER", Reaching.LaterNote(later, back), "", Matters.Could, Anywhere: true, now, Reaching.CanWait));
        }
        else if (later is null && _awaySince is { } since && _chanceAt < since && CanText()
                 && Reaching.MayTextOwn(_settings.Now.Reach, where, now - since, Quiet(now.Hour)))
        {
            _chanceAt = now;
            Hand(new Telling("OWN TEXT", Reaching.OwnText(now - since), "", Matters.Could, Anywhere: true, now, TimeSpan.FromMinutes(1)));
        }
    }

    /// <summary>
    /// What she has to tell the player, once she is free, the most important first, by the routing (see
    /// <see cref="Reaching.Route"/>): at the PC as <see cref="Reaching.AtThePc"/> says (never cutting herself or them
    /// off), a text, or kept for when they are back. What is no longer worth saying is let go. What waits is looked at
    /// once a second (asking Windows about calls takes a moment), anything new at once. While she only waits on the
    /// player's answer to a question by text, what goes to their phone with a plain line (a reminder) still goes, as that
    /// line; the rest waits for her.
    /// </summary>
    private void PassOn()
    {
        var now = DateTimeOffset.Now;
        var asking = Phone?.Asking == true;
        var held = Busy && !asking;
        if (held)
            _quietFrom = now.AddSeconds(1.5);               // a breath after she stops before she starts again
        if (_toTell.Count == 0 || held || now < _quietFrom || now < _nextPass)
            return;
        _nextPass = now.AddSeconds(1);
        foreach (var stale in _toTell.Where(t => !t.FreshAt(now)).ToList())
        {
            _toTell.Remove(stale);
            GD.Print($"Reaching: {stale.Title} let go, no longer worth saying");
        }
        var where = Where(now);
        foreach (var telling in _toTell.OrderBy(t => t.Matters).ThenBy(t => t.At).ToList())
        {
            var route = Reaching.Route(telling.Matters, telling.Anywhere, where, CanText(), _settings.Now.Reach);
            if (asking && !Reaching.GoesWhileAsking(telling, route))
                continue;                                   // it waits for her, as her mind waits on the question
            switch (route.Way)
            {
                case Way.Wait:
                    continue;                               // kept for when they are back at the PC
                case Way.Lapse:
                    _toTell.Remove(telling);
                    GD.Print($"Reaching: {telling.Title} let go: the player is {where.ToString().ToLowerInvariant()}");
                    continue;
                case Way.Text:
                    _toTell.Remove(telling);
                    _ = TextIt(telling, where, herMind: !asking);
                    return;
            }
            if (Her is null || !_settings.Now.SpeechBubbles)
            {
                _toTell.Remove(telling);                    // nobody on the stool, or quiet asked for: a ticket showed what must be seen
                continue;
            }
            var saying = Reaching.AtThePc(telling, where, Settled(now), Presence.DoNotDisturb || _gameRunning, Presence.InCall);
            if (saying == Saying.Later)
                continue;
            _toTell.Remove(telling);
            if (saying == Saying.Ticket)
            {
                if (!telling.Ticketed && telling.Line.Length > 0)
                    Notice?.Invoke(telling.Title, telling.Line);    // they can't be spoken to now: only the ticket
                GD.Print($"Reaching: {telling.Title} only on a ticket: they can't be spoken to now");
                continue;
            }
            GD.Print($"Reaching: {telling.Title} told at the PC ({(route.Way == Way.Call ? "a word first" : "straight on")}" +
                     $"{(saying == Saying.Bubble ? ", her bubble only" : "")}), {(now - telling.At).TotalSeconds:0.0} s after it came");
            _ = Tell(telling, route, bubbleOnly: saying == Saying.Bubble);
            return;
        }
    }

    /// <summary>
    /// Tells the player at the PC: in her own words when her mind is connected, after a word first when the route has
    /// one, else the plain line. With "Also while I'm at the PC", what must reach them goes to their phone too, as a
    /// plain line, as reminders used to. When the route says so, a text is kept in case they had gone after all.
    /// </summary>
    /// <param name="bubbleOnly">They are in a call: her voice would be heard by everyone in it.</param>
    private async Task Tell(Telling telling, Route route, bool bubbleOnly)
    {
        _announcing = true;
        try
        {
            _lively(telling.Greet ? 60 : 30);
            if (telling.Greet)
                Her!.Play(Moment.Greet);
            if (_settings.Now.AlsoAtThePc && telling.Matters == Matters.Must && telling.Anywhere && telling.Line.Length > 0 && CanText())
                _ = Send(new Notice(telling.Heading, telling.Line));
            string? said = null;
            if (_chat.Ready && !telling.Plain && telling.Note.Length > 0)
            {
                var speech = bubbleOnly ? null : _voice.Begin();
                var call = route.Way == Way.Call ? WordFirstFor(speech, withTicket: route.Ticket, unanswered: route.TextIfGone ? InCase : null) : null;
                await _chat.Announce(telling.Note, telling.Line, Situation(), Her!, speech, _voice.Language, call: call);
                said = _chat.Replied;
                if (said is not null)
                    _listening.Said(answer: false, said);
            }
            else if (telling.Line.Length > 0)
            {
                Say(telling.Line);
                said = telling.Line;
            }
            if (route.Way == Way.Say && route.TextIfGone && (said ?? telling.Line) is { Length: > 0 } words)
                InCase(words);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Reaching: {telling.Title}: {e}");      // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _announcing = false;
        }
    }

    /// <summary>
    /// A text to the player's phone while they are away (see <see cref="Way.Text"/>): in her words when her mind can
    /// write them, told it goes as a text, else the plain line. What must reach them never waits long on her mind: after
    /// <see cref="Reaching.HerWordsWithin"/> the plain line goes, and her words don't follow.
    /// </summary>
    /// <param name="herMind">False while her mind waits on a question by text: the plain line goes at once.</param>
    private async Task TextIt(Telling telling, Whereabouts where, bool herMind = true)
    {
        string? words = null;
        if (!herMind && telling.Line.Length > 0)
        {
            GD.Print("Reaching: she waits on a question by text; the plain line goes meanwhile");
            _chat.Said(telling.Line);                       // so she knows what reached them
        }
        else if (_chat.Ready && Her is not null && !telling.Plain && telling.Note.Length > 0)
        {
            _announcing = _texting = _chat.Tools.Away = true;
            try
            {
                words = await _chat.Text($"{telling.Note}\n{Reaching.AsText}", Situation(), Her, _voice.Language,
                                         telling.Matters == Matters.Must ? Reaching.HerWordsWithin : null);
            }
            catch (Exception e)
            {
                GD.PrintErr($"Reaching: {telling.Title}: {e}");
            }
            finally
            {
                _announcing = _texting = _chat.Tools.Away = false;
            }
            if (words is null && telling.Line.Length > 0)
            {
                GD.Print("Reaching: her words didn't come in time; the plain line goes");
                _chat.Said(telling.Line);                   // so she knows what reached them
            }
        }
        var notice = words is { Length: > 0 } ? new Notice(HerName(), words, Hers: true)
                     : telling.Line.Length > 0 ? new Notice(telling.Heading, telling.Line)
                     : null;
        if (notice is null)
        {
            GD.Print($"Reaching: {telling.Title} not texted: only her mind says it, and it {(telling.Matters == Matters.Could ? "let it be" : "couldn't")}");
            return;
        }
        GD.Print($"Reaching: {telling.Title} texted, {(notice.Hers ? "in her words" : "as a plain line")} (the player is {where.ToString().ToLowerInvariant()})");
        await Send(notice);
    }

    /// <summary>
    /// Keeps a text of hers in case the player had gone after all: what she just said with nobody answering, or her call
    /// left unanswered. It goes once they turn out to be away (see <see cref="Track"/>), and is dropped if they use the PC first.
    /// </summary>
    private void InCase(string words)
    {
        if (!CanText())
            return;
        _inCase.Add((new Notice(HerName(), words, Hers: true), DateTimeOffset.Now));
        GD.Print($"Reaching: a text kept in case they've gone; it goes once {AwayAfter.TotalMinutes:0} min pass with no input");
    }

    private async Task Send(Notice notice)
    {
        if (TextOut is { } send)
            await send(notice);
    }

    // ---- Texting with her (Telegram) ---------------------------------------------------------------------------------------

    /// <summary>
    /// The player texted her (see <see cref="Texting"/>): their last words came by text, so they are away until they use
    /// the PC again, and she answers by text once she is free.
    /// </summary>
    public void Texted(string said, IReadOnlyList<Attachment> handed)
    {
        _chat.Store.SetMark(Reaching.Mark, Reaching.Marked(asleep: false, DateTimeOffset.Now));
        _texts.Enqueue((said, handed));
        GD.Print($"Texting: the player texted her{(handed.Count > 0 ? $", with {handed.Count} {(handed.Count == 1 ? "thing" : "things")}" : "")}");
    }

    private void AnswerTexts()
    {
        if (_texts.Count == 0 || Busy || Her is null)
            return;
        var (said, handed) = _texts.Dequeue();
        _ = TextBack(said, handed);
    }

    /// <summary>Her answer to what they texted, by text; what needs them at the PC is out of her reach meanwhile, and her cards come as buttons.</summary>
    private async Task TextBack(string said, IReadOnlyList<Attachment> handed)
    {
        if (Phone is not { } phone)
            return;
        if (!_chat.Ready)
        {
            await phone.Send($"({HerName()} can't answer yet: connect a mind on the PC, in Settings → Mind.)");
            return;
        }
        _announcing = _texting = _chat.Tools.Away = true;
        try
        {
            if (!_texted)
                _chat.Conversation?.Witness(Reaching.TextingBegins);
            _texted = true;
            phone.Typing();
            if (await _chat.TextBack(said, handed, Situation(), Her!, _voice.Language) is { } words)
                await phone.Send(words);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Texting: {e}");                   // nobody awaits this task: say what went wrong here
        }
        finally
        {
            _announcing = _texting = _chat.Tools.Away = false;
        }
    }

    /// <summary>Just paired: she says hello in the new chat, in her own words when her mind is connected.</summary>
    public async Task SayHello()
    {
        if (Phone is not { } phone)
            return;
        string? words = null;
        if (_chat.Ready && Her is not null && !Busy)
        {
            _announcing = _texting = true;
            try
            {
                words = await _chat.Text("The player has just paired their phone with you on Telegram, so that you can text each other while they're " +
                                         "away from the PC. Say hello there, in a short text.", Situation(), Her, _voice.Language, Reaching.HerWordsWithin);
            }
            finally
            {
                _announcing = _texting = false;
            }
        }
        await phone.Send(words ?? $"Hi! It's {HerName()}. Text me here when you're away from the PC.");
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

    /// <summary>The player cut her off: see <see cref="Chat.Interrupt"/>.</summary>
    internal Task Interrupt() => _chat.Interrupt(_voice, Her);

    // ---- A word first (docs/specs/2026-10-09-a-word-first.md) -------------------------------------------------------

    private static readonly TimeSpan Unanswered = TimeSpan.FromSeconds(20), GiveUp = TimeSpan.FromMinutes(5);
    private TaskCompletionSource<bool>? _calling;
    private DateTimeOffset _lastTalked = DateTimeOffset.MinValue;     // the player last said something to her, or answered her call

    /// <summary>She has called the player and waits for their answer before going on (see <see cref="WordFirst"/>).</summary>
    internal bool Calling => _calling is not null;

    /// <summary>Her card or her call waits for the player's answer: her reply goes on once they give it.</summary>
    internal bool Waits => CardOpen || Calling;

    /// <summary>
    /// How what she brings up herself waits for the player's answer to her call, or null when it goes straight on: when
    /// her voice isn't speaking it, or when they are already talking with her (a conversation, something written on her
    /// slip, a word with her in the last two minutes).
    /// </summary>
    /// <param name="withTicket">An unanswered call brings a ticket above every window (not for what may pass unnoticed, such as standing up).</param>
    /// <param name="unanswered">Given the remark when her call goes unanswered: they may have gone (see <see cref="InCase"/>).</param>
    private Func<string, CancellationToken, Task<bool>>? WordFirstFor(Speech? speech, bool withTicket = true, Action<string>? unanswered = null)
    {
        var straight = speech is null ? "her voice isn't speaking it"
            : _listening.Conversing ? "a conversation is on"
            : _chatBox.IsOpen && !_chatBox.IsEmpty ? "they are writing to her"
            : DateTimeOffset.Now - _lastTalked < TimeSpan.FromMinutes(2) ? "they talked with her just now"
            : null;
        if (straight is null)
            return (whole, cancel) => WaitForAnswer(whole, withTicket, unanswered, cancel);
        GD.Print($"Word first: none, straight on ({straight})");
        return null;
    }

    /// <summary>
    /// She has said her call: her bubble keeps it, with "Yes?" and "Not now" to click, and she looks at the player and
    /// waits for their answer (aloud, or a click on those, on her or on her bubble). Unanswered for 20 seconds, she can't
    /// count it as heard (their headset on the desk, say): a ticket above every window shows the whole remark, to hear
    /// (Listen) or take as read (OK), unless it may pass unnoticed; and they may have gone, which
    /// <paramref name="unanswered"/> hears of. After five minutes the rest is only shown. True: she goes on aloud.
    /// </summary>
    private async Task<bool> WaitForAnswer(string whole, bool withTicket, Action<string>? unanswered, CancellationToken cancel)
    {
        var answer = _calling = new TaskCompletionSource<bool>();
        int? ticket = null;
        if (Her is { } her)
        {
            her.Waiting = true;
            her.Play(Moment.Listen);                        // her eyes on them, waiting, as while her card asks
        }
        _listening.Called();
        GD.Print("Word first: she has called the player and waits for an answer");
        try
        {
            if (await Task.WhenAny(answer.Task, Task.Delay(Unanswered, cancel)) != answer.Task)
            {
                cancel.ThrowIfCancellationRequested();
                unanswered?.Invoke(whole);
                if (!withTicket)
                    GD.Print("Word first: no answer; it may pass unnoticed, so no ticket");
                else if (Ticket is { } corner)
                {
                    GD.Print("Word first: no answer; a ticket shows it");
                    ticket = corner.Show((_chat.Conversation?.Persona.Name ?? "Your companion").ToUpperInvariant(), whole, seconds: 0,
                                         buttons: [("Listen", () => answer.TrySetResult(true)), ("OK", () => answer.TrySetResult(false))]);
                }
                if (await Task.WhenAny(answer.Task, Task.Delay(GiveUp - Unanswered, cancel)) != answer.Task)
                {
                    cancel.ThrowIfCancellationRequested();
                    GD.Print("Word first: still no answer; the rest is only shown");
                    return false;
                }
            }
            var aloud = await answer.Task;
            GD.Print(aloud ? "Word first: answered; she goes on" : "Word first: read on the ticket; the rest is only shown");
            return aloud;
        }
        finally
        {
            _calling = null;
            if (Her is { } view)
            {
                view.Waiting = false;
                view.Play(Moment.Heard);
            }
            if (ticket is { } shown)
                Ticket?.Take(shown);
        }
    }

    /// <summary>The player answered her call ("yes?", said or clicked on her bubble, or a click on her): she goes on.</summary>
    internal void GoOn()
    {
        _lastTalked = DateTimeOffset.Now;
        _listening.Drop();
        _calling?.TrySetResult(true);
    }

    /// <summary>"Not now": she stops after her call, keeping the rest to come back to (see <see cref="Chat.Interrupt"/>).</summary>
    internal Task NotNow()
    {
        GD.Print("Word first: not now; she stops after her call");
        return Interrupt();
    }

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
        if (Calling)
        {
            GoOn();                                         // she waits on her call: a click on her (or her bubble) is the player's "yes?"
            return;
        }
        var replying = Replying;
        _listening.Drop();
        // still thinking, none of her reply out yet: the click only opens the slip, so a slip of the mouse doesn't throw
        // the answer away; sending something new still cuts it off (see Ask) (the user, 2026-10-12)
        if (!_chat.StillThinking)
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

    /// <summary>The cabinet is closing: the memory book saves what the player was in the middle of, her mind lets go of its files and her voice's server ends.</summary>
    public void Shutdown()
    {
        if (IsInstanceValid(_memoryBook))
            _memoryBook!.SaveAll();
        _chat?.Dispose();
        _voice?.Shutdown();
    }

    /// <summary>Starts or ends a conversation (the tray's switch): she listens after everything she says.</summary>
    public void Converse(bool on) => _listening.Converse(on);

    /// <summary>A conversation is on.</summary>
    public bool Conversing => _listening.Conversing;

    public override void _Process(double delta)
    {
        PassOn();
        AnswerTexts();
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
                her.Manner = MannerOf(character.Id);
                her.Spoken = () => _voice.Progress;
                her.Subtitled = () => _chat.Speech is { Subtitled: true };
                her.Answered = yes =>                       // "Yes?" or "Not now" clicked on her bubble while she waits on her call
                {
                    if (!Calling)
                        return;
                    if (yes)
                        GoOn();
                    else
                        _ = NotNow();
                };
                her.BubbleUnder = rect => OverBubble(rect);
                her.BubbleClicked = () =>                   // her bubble's words clicked while she waits on her call: "yes?"
                {
                    if (Calling)
                        Clicked();
                };
                _chat.Seat(character.Id, character.Persona(_characters, _settings.Now.CallMe));
                GiveOwnVoice(character.Id, character.Name);
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

    /// <summary>
    /// A built-in character comes with a voice of their own (<c>res://characters/&lt;id&gt;/voice.zip</c>, made with
    /// Kokoro-82M): given to them the first time they sit down without one, and only once, so one the player removed
    /// stays removed.
    /// </summary>
    private void GiveOwnVoice(string id, string name)
    {
        var resource = $"res://characters/{id}/voice.zip";
        if (_settings.Now.VoicesGiven.Contains(id) || !ModelFiles.Present(resource))
            return;
        _settings.Save(_settings.Now with { VoicesGiven = [.. _settings.Now.VoicesGiven, id] });
        if (VoiceProfile.Load(VoiceFolder(id)) is not null)
            return;                                         // they have one already
        try
        {
            (VoiceProfile.Import(ModelFiles.OnDisk(resource, "voice-" + id), VoiceFolder(id)) with { Name = name }).Save(VoiceFolder(id));
            GD.Print($"Voice: {name} given their own voice");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Voice: giving {name} their own voice: {e.Message}");
        }
    }

    /// <summary>Someone else takes the stool.</summary>
    public void Seat(string id)
    {
        _settings.Save(_settings.Now with { Companion = id });
        Her?.QueueFree();
        Her = null;
        ShowHer(_settings.Now.ShowCompanion);
        SettingsChanged?.Invoke("voice");                   // a voice of their own
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
        ["*.zip, *.wav, *.mp3, *.ogg, *.flac ; Clips of the voice (3-10 seconds each), or a voice's .zip"], Add);

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
    private void PickFiles() => Pick($"Hand {HerName()} a picture or a file", FileDialog.FileModeEnum.OpenFiles,
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
        _phone.Build(_chat.Conversation?.Persona.Name ?? "your companion", thisPcOnly: Paper.OffScreen);
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
        _cabinet.Screen.Flash("MIND", "CONNECT IT IN SETTINGS", 3.0);
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
                        voice = VoiceProfile.Import(path, VoiceFolder(who), hearLater: Listening.HasEars);
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
            Meet(open: false);
            return;
        }
        if (added.Count > 0)
            SettingsChanged?.Invoke("characters");          // one more to choose from
        _settings.Save(_settings.Now with { ShowCompanion = true });
        Seat(added.FirstOrDefault() ?? OnStool()!.Value.Id);        // seated again, so a new card takes effect
        _lively(6);
        Her?.Play(Moment.Greet);
        if (cards.Count > 0 && _chat.Conversation?.Persona.Greeting is { } greeting && FirstLine(greeting) is { } line)
            GetTree().CreateTimer(0.8).Timeout += () => Say(line);      // once she has sat down: the bubble goes over her seated head
        if (voice is not null)
            AddedVoice(voice);
        Meet(open: added.Count > 0);                        // what the new one has of their own, and lacks
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
    private void AddedVoice(VoiceProfile voice, bool listened = false)
    {
        _voice.Seat(_voice.Folder);
        SettingsChanged?.Invoke("voice");
        if (!listened && Listening.HasEars && voice.Clips.Any(c => c.Text.Trim().Length == 0))
        {
            _cabinet.Screen.Flash("VOICE", "LISTENING TO ITS CLIPS", 4.0);
            _ = HearVoice(_voice.Folder);
            return;
        }
        if (voice.Missing is not null || VoiceServer.Check(_settings.Now.VoiceEngine) is not null)
        {
            _cabinet.Screen.Flash("VOICE", "ALMOST THERE: SEE SETTINGS, VOICE", 4.0);
            OpenSettings?.Invoke("voice");
            return;
        }
        _cabinet.Screen.Flash("VOICE", $"{voice.Name} - WARMING UP".ToUpperInvariant(), 4.0);
        _lively(20);
        _ = _voice.Test();
    }

    /// <summary>
    /// Her ears write down the words of the voice's clips that came without any (see <see cref="VoiceProfile.Hear"/>),
    /// off the cabinet's thread; then the voice is taken up as just added. The voice page says so meanwhile.
    /// </summary>
    private async Task HearVoice(string folder)
    {
        _hearingVoice = folder;
        SettingsChanged?.Invoke("voice");
        var written = 0;
        try
        {
            if (await _listening.SharedEars() is { } ears)
                written = await Task.Run(() => VoiceProfile.Hear(folder, clip => HeardIn(ears, clip)));
        }
        catch (Exception e)
        {
            GD.PrintErr($"Voice: writing down its clips' words: {e.Message}");
        }
        _hearingVoice = null;
        GD.Print($"Voice: her ears wrote down the words of {written} clips");
        if (folder == _voice.Folder && VoiceProfile.Load(folder) is { } voice)
            AddedVoice(voice, listened: true);
        else
            SettingsChanged?.Invoke("voice");
    }

    /// <summary>The words her ears make out in a clip, or null when it isn't a WAV they can read.</summary>
    private static string? HeardIn(Hearing ears, string clip)
    {
        if (!Path.GetExtension(clip).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var wav = new Wav();
            var samples = wav.Feed(File.ReadAllBytes(clip));
            return wav.SampleRate > 0 && samples.Length > 0 ? ears.Hear(Hearing.To16k(samples, wav.SampleRate)) : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return null;
        }
    }

    // ---- Talking with her ------------------------------------------------------------------------------------------------

    private Provider? ChatProvider() => Provider.From(_settings.Now.ChatProtocol, _settings.Now.ChatBaseUrl, _settings.Now.ChatModel);

    /// <summary>The backup behind the ChatGPT plan (Settings → Mind), if one is set.</summary>
    private Provider? BackupProvider() => Provider.From(_settings.Now.BackupProtocol, _settings.Now.BackupBaseUrl, _settings.Now.BackupModel);

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

    /// <param name="again">What the plan stopped on, asked again after the player's yes (see <see cref="Chat.TakeUnanswered"/>), with what they handed her then.</param>
    internal async Task Ask(string said, Chat.Unanswered? again = null)
    {
        if (Her is null)
            return;
        if (_texted)
        {
            _chat.Conversation?.Witness(Reaching.TextingEnds);     // so she knows which lines were texted
            _texted = false;
        }
        _lastTalked = DateTimeOffset.Now;
        var handed = again?.Handed ?? _chatBox.TakeHanded();     // what waits on the slip goes with it, typed or said
        _listening.Drop();
        await Interrupt();                                  // asking something new cuts off what she was still saying
        _chatBox.SetWaiting(true);
        _lively(120);                                       // keep her lively while she thinks and talks
        try
        {
            await _chat.Ask(said, Situation(), Her, _voice.Begin(thinkAloud: true), _voice.Language, handed, again);
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
        if (_texting)
            _ = Phone?.Send($"{title}\n\n{text}");          // while texting, it comes to their phone too
        return "Printed: it's hanging from the cabinet's slot, for the player to read, pin on the cabinet's side or keep.";
    }

    /// <summary>
    /// She asks whether she may use a tool: the card by her stool, above the slip they type on, and her eyes on the
    /// player while they decide; while texting, the same buttons under a message.
    /// </summary>
    private async Task<Answer> AskPlayer(Asking asking, CancellationToken cancel)
    {
        if (Her is null)
            return Answer.None;
        if (_texting && Phone is { Linked: true } phone)
        {
            var pressed = await phone.Ask($"{asking.What}\n{asking.Detail}",
                                          [("Allow once", "once"), (asking.Scope is { } site ? $"Always for {site}" : "Always", "always"), ("No", "no")], cancel);
            GD.Print($"Tools: she asked by text to use {asking.Tool.Name} on {asking.Detail}: {pressed ?? "no answer"}");
            return pressed switch { "once" => Answer.Once, "always" => Answer.Always, "no" => Answer.No, _ => Answer.None };
        }
        Her.Play(Moment.Listen);
        var asked = _permission.Ask(asking, new Vector2(14, CardBottom), cancel);
        _listening.CardShown();
        var answer = await asked;
        _listening.CardGone();
        Her?.Play(Moment.Heard);
        GD.Print($"Tools: she asked to use {asking.Tool.Name} on {asking.Detail}: {answer}");
        return answer;
    }

    /// <summary>
    /// The ChatGPT plan stopped answering her (its limit reached, signed out, an account that can't share it): said once,
    /// plainly, on her card, with what to do about it first; with a backup set, the card asks whether she carries on with
    /// it, and nothing goes to it without a yes. Until then (or after Not now, or with no backup) she waits: no replies,
    /// check-ins or remarks, while reminders still come in the app's words (each request fails at once, see
    /// <see cref="ChatGptPlan.Client"/>). After a yes she carries on with the backup until the plan works again.
    /// </summary>
    private PlanStop? _planCard;                            // what her card is telling of the plan now, if anything
    private int _planCards;                                 // the plan's cards shown so far: which one _planCard belongs to

    private async Task TellPlan(PlanStop why)
    {
        var backup = _chat.BackupProvider?.Name;
        GD.Print($"ChatGPT: {why switch { PlanStop.Limit => "the plan's usage limit is reached", PlanStop.NotEligible => "the account can't share its plan", _ => "signed out" }}; " +
                 (_chat.OnBackup ? "she carries on with the backup" : backup is null ? "she waits, and reminders come in plain words"
                                                                      : "she asks before carrying on with the backup, and waits meanwhile; reminders come in plain words"));
        if (Her is null || _chat.OnBackup)
            return;
        var name = HerName();
        Her.Play(Moment.Worry);
        _planCard = why;
        var card = ++_planCards;
        _chat.PlanTold = true;
        var where = new Vector2(14, CardBottom);
        bool pressed;
        try
        {
            if (backup is not null)
            {
                var (question, aside) = why switch
                {
                    PlanStop.Limit => ($"The ChatGPT plan's limit is reached. Carry on with {backup} until it resets?", "Manage usage"),
                    PlanStop.NotEligible => ($"This ChatGPT account can't share its plan. Carry on with {backup} meanwhile?", "Continue with ChatGPT"),
                    _ => ($"Signed out of ChatGPT. Carry on with {backup} until you sign in again?", "Continue with ChatGPT"),
                };
                var asked = _chat.BackupProvider;
                var yes = await _permission.AskOver(question, $"{name} waits until you answer; reminders still come.", aside, PutRight, where);
                if (yes && _chat.BackupProvider != asked)
                {
                    // a yes is for the backup it named: asked again, while she still waits on the plan
                    var again = _chat.OnPlan && _chat.Plan.Stopped is not null;
                    GD.Print("ChatGPT: the backup changed while the card asked; " +
                             (again ? "asked again" : _chat.OnPlan ? "the plan works again meanwhile" : "she no longer answers through the plan"));
                    if (again)
                        Callable.From(() => { if (_chat.OnPlan && _chat.Plan.Stopped is { } still) _ = TellPlan(still); }).CallDeferred();
                    return;
                }
                GD.Print(yes ? $"ChatGPT: the player said yes; she carries on with the backup ({backup}) until the plan works again"
                         : !_chat.OnPlan ? "ChatGPT: the card is taken back: she no longer answers through the plan"
                         : "ChatGPT: not now; she waits on the plan");
                if (!yes)
                    return;
                _chat.Backup.Allow();
                if (_chat.OnBackup && _chat.TakeUnanswered() is { } waiting)
                    _ = Ask(waiting.Said, waiting);         // what they wrote when the plan stopped: answered now, on the backup
                return;
            }
            var (what, detail, action) = why switch
            {
                PlanStop.Limit => ("Usage limit reached", $"Review your plan or this app's limit in ChatGPT settings. {name} waits until it works again; reminders still come.",
                                   "Manage usage"),
                PlanStop.NotEligible => ("ChatGPT plan not available", "This ChatGPT account can't share its plan with apps. Sign in with another, or use an API key in Settings → Mind.",
                                         "Continue with ChatGPT"),
                _ => ("Signed out of ChatGPT", $"{name} can't use your ChatGPT plan until you sign in again. Reminders still come.", "Continue with ChatGPT"),
            };
            pressed = await _permission.Tell(what, detail, action, where, CancellationToken.None);
        }
        finally
        {
            if (_planCards == card)                         // not a newer card of the plan's, shown meanwhile
            {
                _planCard = null;
                _chat.PlanTold = false;
            }
        }
        if (pressed)
            PutRight();

        void PutRight()
        {
            if (why == PlanStop.Limit)
                Chat.OpenPage(new Uri(ChatGptPlan.ManageUsage));
            else
            {
                OpenSettings?.Invoke("mind");               // signing in shows how it goes on her mind's page
                if (IsInstanceValid(_mindPage))
                    _ = _mindPage!.ContinueWithChatGpt();
            }
        }
    }

    /// <summary>The plan answered again after she carried on with the backup: she is back on it, said once on her card.</summary>
    private async Task TellBack()
    {
        GD.Print("ChatGPT: the plan answers again; she is back on it, and the backup goes unused");
        if (Her is null)
            return;
        await _permission.Tell("Back on your ChatGPT plan", $"{HerName()} answers through your ChatGPT plan again, not {_chat.BackupProvider?.Name ?? "the backup"}.",
                               null, new Vector2(14, CardBottom), CancellationToken.None);
    }

    /// <summary>Where her card stands: at the foot of the stool, above the slip and whatever waits on it.</summary>
    private float CardBottom => _cabinet.Skin.Canvas.Y - 14 - (_chatBox.IsOpen ? _chatBox.Tall + 8 : 0);

    /// <summary>She counts an activity: the player confirms it on her card ("I'm going to the gym" isn't "I went").</summary>
    private Task<bool?> ConfirmActivity(string what, string detail, CancellationToken cancel) => _chat.Unhurried(() => ConfirmOnCard(what, detail, cancel));

    /// <summary>
    /// The player confirms something she does for them on her card (by text, buttons): yes, not yet, or null when they
    /// let it be. With <paramref name="change"/>, her suggested change to a skill: Remember it or Not now, and the exact
    /// lines behind See the change (by text, in the message).
    /// </summary>
    private async Task<bool?> ConfirmOnCard(string what, string detail, CancellationToken cancel, string? change = null)
    {
        if (Her is null)
            return null;
        if (_texting && Phone is { Linked: true } phone)
        {
            var pressed = change is null
                ? await phone.Ask($"{what}\n{detail}", [("Yes", "yes"), ("Not yet", "no")], cancel)
                : await phone.Ask($"{what}\n{detail}\n\n{change}", [("Remember it", "yes"), ("Not now", "no")], cancel);
            GD.Print($"Card: \"{what}\" by text: {pressed ?? "no answer"}");
            return pressed switch { "yes" => true, "no" => false, _ => null };
        }
        Her.Play(Moment.Listen);
        var asked = change is null ? _permission.Confirm(what, detail, new Vector2(14, CardBottom), cancel)
            : _permission.Suggest(what, detail, change, new Vector2(14, CardBottom), cancel);
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
        // her answer to what they just did: at once, here
        Hand(new Telling("DONE", $"The player logged \"{name}\" as done on the cabinet's screen. What came of it: {said}", line, Matters.Must,
                         Anywhere: false, DateTimeOffset.Now, TimeSpan.FromMinutes(3)));
        return said;
    }

    // ---- Her pages of the settings ---------------------------------------------------------------------------------------

    /// <summary>
    /// Her pages of the settings (see <see cref="SettingsWindow"/>): her mind, her voice, talking aloud, characters, her
    /// abilities, privacy and add-ons, and what her requests used. Each change is saved and used at once.
    /// </summary>
    public IEnumerable<SettingsPage> SettingsPages() =>
    [
        new("mind", SettingsWindow.Companion, "Mind", () => MindPage()),
        new("voice", SettingsWindow.Companion, "Voice", VoicePage),
        new("listening", SettingsWindow.Companion, "Talking aloud", ListeningPage),
        new("characters", SettingsWindow.Companion, "Characters", CharactersPage),
        new("abilities", SettingsWindow.Companion, "Abilities", AbilitiesPage),
        new("privacy", SettingsWindow.Companion, "Privacy", PrivacyPage),
        new("skills", SettingsWindow.Companion, "Skills", SkillsPage, Fresh: true),     // made again each time: skills change on disk
        new("addons", SettingsWindow.Companion, "Add-ons", AddOnsPage),
        new("usage", SettingsWindow.Cabinet, "Usage", UsedPage),
    ];

    /// <summary>The name of whoever is on the stool, for her pages to say.</summary>
    internal string HerName() => OnStool()?.Name ?? "Your companion";

    private int _naming;                                    // the name typed last, so she takes it up once typing stops
    private MeetSheet? _meet;

    /// <summary>Who sits on the stool now, by id.</summary>
    internal string? Seated => OnStool()?.Id;

    /// <summary>How a character sits (see <see cref="Manner"/>): as the player chose, else neat.</summary>
    internal Manner MannerOf(string id) => _settings.Now.Manners.GetValueOrDefault(id);

    /// <summary>The player chose how a character sits: saved, and taken up at once if they are on the stool, so it is seen.</summary>
    internal void SetManner(string id, Manner manner)
    {
        _settings.Save(_settings.Now with { Manners = new(_settings.Now.Manners) { [id] = manner } });
        if (Her is not null && Seated == id)
            Her.Manner = manner;
    }

    /// <summary>"Show me": a few seconds of how whoever is on the stool carries themselves.</summary>
    internal void ShowManner() => Her?.ShowManner();

    /// <summary>Who can sit on the stool, with who they are in their persona's first sentence (the first run's choice).</summary>
    internal (string Id, string Name, string About)[] Meetable() =>
        [.. CompanionView.Available(_characters).Select(c => (c.Id, c.Name, FirstSentence(c.Persona(_characters, null).Description)))];

    private static string FirstSentence(string text) => text.IndexOf(". ", StringComparison.Ordinal) is var end and > 0 and < 240 ? text[..(end + 1)] : "";

    /// <summary>The first run is over: she says hello, in her own words once her mind is connected, else that it isn't yet.</summary>
    internal void Greet()
    {
        var now = DateTimeOffset.Now;
        Hand(_chat.Ready
            ? new Telling("HELLO", "The player has just set up the cabinet and chosen you to keep them company: you are meeting for the first " +
                                   "time. Say hello in a short sentence.", _settings.Now.CallMe is { } name ? $"Hi, {name}!" : "Hi!",
                          Matters.Must, Anywhere: false, now, TimeSpan.FromMinutes(3), Greet: true)
            : new Telling("HELLO", "", "Hi! Connect my mind in Getting started, and we can talk.", Matters.Must, Anywhere: false, now,
                          TimeSpan.FromMinutes(3), Greet: true));
    }

    /// <summary>
    /// "Meet …" for whoever is on the stool (see <see cref="MeetSheet"/>): opened when a character is added
    /// (<paramref name="open"/>), else filled again with what they have now, if it shows.
    /// </summary>
    private void Meet(bool open)
    {
        if (!open && !IsInstanceValid(_meet) || OnStool() is not { } seated
            || CompanionView.Available(_characters).FirstOrDefault(c => c.Id == seated.Id) is not { } character)
            return;
        var opening = !IsInstanceValid(_meet);
        if (opening)
        {
            _meet = new MeetSheet();
            AddChild(_meet);
            _meet.Build();
            _meet.AddCard += PickCharacter;
            _meet.AddVoice += PickVoice;
            _meet.SetUpVoices += () => OpenSettings?.Invoke("voice");
            _meet.Named += CallMe;
            _meet.MannerChosen += manner => SetManner(Seated ?? "", manner);
            _meet.ShowManner += ShowManner;
        }
        var persona = character.CardIn(_characters) is not null ? "their character card"
            : character.Persona(_characters, null).Description == InsertCoin.Mind.Persona.Plain(character.Name).Description ? null
            : "their persona file";
        _meet!.Fill(seated.Name, persona, _voice.Profile is not null, VoiceServer.Check(_settings.Now.VoiceEngine) is null, _settings.Now.CallMe,
                    MannerOf(seated.Id));
        if (opening)
            _meet.Open();
    }

    /// <summary>Dev: presses or types on "Meet …", as <see cref="SettingsWindow.DevDo(string, string?)"/> does.</summary>
    internal void DevMeet(string label, string? value)
    {
        if (IsInstanceValid(_meet))
            _meet!.DevDo(label, value);
    }

    /// <summary>
    /// The name the player gave to be called by (Getting started, Characters): saved at once, and taken up by whoever is
    /// on the stool once the typing stops (taking it up cancels what she is saying).
    /// </summary>
    internal void CallMe(string name)
    {
        _settings.Save(_settings.Now with { CallMe = name.Trim().Length > 0 ? name.Trim() : null });
        var typed = ++_naming;
        GetTree().CreateTimer(1.5).Timeout += () =>
        {
            if (typed == _naming && _chat.Conversation is { } now && CompanionView.Available(_characters).FirstOrDefault(c => c.Id == now.Character) is { } character)
                _chat.Seat(character.Id, character.Persona(_characters, _settings.Now.CallMe));
        };
    }

    /// <summary>Her steps of Getting started: her mind, which she needs to talk; her voice; talking aloud, when this build has ears.</summary>
    internal IEnumerable<StartStep> StartSteps()
    {
        var her = HerName();
        var provider = ChatProvider();
        yield return new("mind", $"{her}'s mind",
            $"{her} talks through your own AI provider (OpenAI, Anthropic, OpenRouter, DeepSeek, Qwen, or a server on this PC): pick one, get a key " +
            "on its site, name a model, Test and Save. Providers charge a little for each reply; one on this PC is free. About 5 minutes.",
            Needed: true, Done: provider is not null,
            provider is null ? $"Not connected: {her} can't talk with you yet."
                : $"{her} answers through {provider.Through}, with {provider.Model}.");
        yield return new("voice", $"{her}'s voice",
            $"{her} speaks aloud with GPT-SoVITS, run on this PC's graphics card: installed for you there (an 8 GB download, 13 GB on disk)" +
            (_voice.Profile is null ? $", then give {her} a voice from clips with their words" : $"; {her} has a voice already, which you can change") +
            ". About 20 minutes; training a model of the voice takes longer.",
            Needed: false, Done: _voice.Now is not (Voice.Phase.NotSetUp or Voice.Phase.NoVoice or Voice.Phase.Failed), _voice.Status);
        if (Listening.HasEars)
            yield return new("listening", "Talking aloud",
                $"Talk to {her} through your microphone: what you say is made into words on this PC, and only the words go to your provider. " +
                "Nothing to install.",
                Needed: false, Done: true,
                _settings.Now.TalkKey == TalkKey.Off ? $"Ready: hold the mouse on {her} and speak."
                    : $"Ready: hold {TalkKey.Keys.FirstOrDefault(k => k.Name == _settings.Now.TalkKey).Label ?? "the talk key"} in any app and speak.");
    }

    private MindSettings? _mindPage;                        // the latest made, for her card's Continue with ChatGPT

    /// <summary>Her provider (used once saved), how she finds what to remember, and how much she keeps word for word; <paramref name="brief"/>, the provider alone.</summary>
    internal Control MindPage(bool brief = false)
    {
        var page = _mindPage = new MindSettings();
        page.Build(_chat, ChatProvider(), BackupProvider(), HerName(), _settings.Now.MemorySearch, _settings.Now.ChatWindow, brief);
        page.Saved += provider =>
        {
            _settings.Save(_settings.Now with { ChatProtocol = provider?.Protocol.ToString(), ChatBaseUrl = provider?.BaseUrl, ChatModel = provider?.Model });
            _chat.Connect(provider, BackupProvider());
            if (!_chat.OnPlan && _planCard is not null)
                _permission.Withdraw();                     // she no longer answers through the plan: its card (telling or asking) goes
        };
        page.BackupSaved += backup =>
        {
            _settings.Save(_settings.Now with { BackupProtocol = backup?.Protocol.ToString(), BackupBaseUrl = backup?.BaseUrl, BackupModel = backup?.Model });
            _chat.Connect(ChatProvider(), backup);
        };
        page.SearchChosen += search =>
        {
            _settings.Save(_settings.Now with { MemorySearch = search });
            _chat.UseSearch(search);
        };
        page.WindowChosen += window =>
        {
            _settings.Save(_settings.Now with { ChatWindow = window });
            _chat.UseWindow(window);
        };
        return page;
    }

    /// <summary>Whether she speaks aloud, the GPT-SoVITS that runs her voice, and the voice of whoever is on the stool.</summary>
    private Control VoicePage()
    {
        var folder = OnStool() is { } character ? VoiceFolder(character.Id) : _voice.Folder;
        var page = new VoiceSettings();
        page.Build(_voice, _trainer, _installer, _settings.Now.VoiceEngine, _settings.Now.EngineInstallTo, HerName(), _settings,
                   hearing: _hearingVoice == folder);
        page.Switched += SetVoice;
        page.InstallEngine += (into, source) =>
        {
            _settings.Save(_settings.Now with { EngineInstallTo = into });
            _installer.Start(into, source);
        };
        page.StopInstall += _installer.Stop;
        page.EngineChosen += engine =>
        {
            if (engine == _settings.Now.VoiceEngine)
                return;
            _settings.Save(_settings.Now with { VoiceEngine = engine });
            _voice.UseEngine(engine);
        };
        page.VoiceChanged += () => _voice.Seat(folder);     // read again: its language, words, model or quality changed
        page.AddVoice += PickVoice;
        page.AddFolder += PickVoiceFolder;
        page.Train += Train;
        page.StopTraining += () => _trainer.Stop("You stopped the training.");
        page.RemoveVoice += () =>
        {
            if (_trainer.Running && _trainer.Folder == folder)
                _trainer.Stop("The voice was removed.");
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            _voice.Seat(folder);
            SettingsChanged?.Invoke("voice");
        };
        return page;
    }

    /// <summary>The talk key, and when she listens without it: both read as they are needed, so saving is enough.</summary>
    private Control ListeningPage()
    {
        var page = new ListeningSettings();
        page.Build(HerName(), _settings.Now.TalkKey, _settings.Now.HandsFree, Listening.HasEars, Listening.HasTurns);
        page.KeyChosen += key => _settings.Save(_settings.Now with { TalkKey = key });
        page.HandsFreeChosen += handsFree => _settings.Save(_settings.Now with { HandsFree = handsFree });
        return page;
    }

    /// <summary>Who is on the stool, or nobody; her speech bubbles; adding a character.</summary>
    private Control CharactersPage()
    {
        var page = new CharacterSettings();
        page.Build(Characters(), OnStool()?.Id ?? "", _settings.Now.ShowCompanion, _settings.Now.SpeechBubbles, _settings.Now.CallMe,
                   MannerOf, SetManner, ShowManner);
        page.Named += CallMe;
        page.ShownChosen += SetCompanion;
        page.BubblesChosen += SetBubbles;
        page.Seated += Seat;
        page.Add += PickCharacter;
        page.OpenFolder += OpenCharactersFolder;
        return page;
    }

    /// <summary>What she can do beyond talking, and how freely.</summary>
    private Control AbilitiesPage()
    {
        var page = new AbilitySettings();
        page.BuildAbilities(_chat, HerName());
        page.PermissionsChosen += ChoosePermissions;
        page.AutoChosen += auto =>
        {
            _settings.Save(_settings.Now with { ToolsAuto = auto });
            _chat.Tools.Auto = auto;
        };
        return page;
    }

    /// <summary>What she may look at of the player's, and what she is told in the background.</summary>
    private Control PrivacyPage()
    {
        var page = new AbilitySettings();
        page.BuildPrivacy(_chat, HerName(), ChatProvider(), _settings.Now.Privacy);
        page.PermissionsChosen += ChoosePermissions;
        page.PrivacyChosen += privacy => _settings.Save(_settings.Now with { Privacy = privacy });
        return page;
    }

    private void ChoosePermissions(IReadOnlyDictionary<string, Permission> permissions)
    {
        _settings.Save(_settings.Now with { ToolPermissions = new(permissions) });
        _chat.Tools.Choose(permissions);
    }

    private SkillSettings? _skillsPage;                    // the Skills page made last, for dev actions on it

    /// <summary>Her skills: the app's own, the folders the player linked, and which are on.</summary>
    private Control SkillsPage()
    {
        var page = _skillsPage = new SkillSettings();
        page.Build(_chat.Skills, _chat.Notes, HerName(), Pick, () =>
                   {
                       var permissions = new AbilitySettings();
                       permissions.BuildNoteTools(_chat);
                       permissions.PermissionsChosen += ChoosePermissions;
                       return permissions;
                   },
                   () => _settings.Save(_settings.Now with
                   {
                       SkillFolders = [.. _chat.Skills.Linked], SkillsOff = [.. _chat.Skills.Off.Order()], SkillGrants = new(_chat.Notes.Grants),
                   }));
        return page;
    }

    /// <summary>Extras the player turns on: web search through their own SearXNG.</summary>
    private Control AddOnsPage()
    {
        var page = new AddOnSettings();
        page.Build(HerName(), ChatProvider(), _settings.Now.AddOns);
        page.Chosen += addOns =>
        {
            _settings.Save(_settings.Now with { AddOns = addOns });
            _chat.SearchThrough(addOns.Search);
        };
        return page;
    }

    /// <summary>What her requests used, and the prices the player entered for the model she runs on.</summary>
    private Control UsedPage()
    {
        var page = Form.Page(new VBoxContainer(), "Usage");
        var model = _settings.Now.ChatModel;
        _ = new UsagePage(page, _chat.Store, model, _settings.Now.ChatPrices, _settings.Now.Currency, (prices, currency) =>
        {
            if (model is null)
                return;                                     // prices are kept per model
            var all = new Dictionary<string, Prices>(_settings.Now.ChatPrices);
            if (prices is null)
                all.Remove(model);
            else
                all[model] = prices;
            _settings.Save(_settings.Now with { ChatPrices = all, Currency = currency });
        });
        return page;
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
        _history.Build(_chat.Store, character.Id, character.Name, _chat.Window);
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
            _cabinet.Screen.Flash("VOICE", "TRAINING - THE VOICE RESTS", 4.0);
    }

    /// <summary>Training ended: her voice comes back, with its new model when it worked.</summary>
    private void TrainingEnded(string folder, VoiceProfile? trained)
    {
        _voice.Training = false;
        if (trained is null)
        {
            _cabinet.Screen.Flash("VOICE", "TRAINING STOPPED: SEE SETTINGS, VOICE", 4.0);
            return;
        }
        _settings.Save(_settings.Now with { TrainingPace = _trainer.Pace });     // the next estimate is this PC's own
        if (Path.GetFullPath(folder) != Path.GetFullPath(_voice.Folder))
            return;                                         // someone else is on the stool now: their voice is untouched
        _voice.Seat(folder);
        _cabinet.Screen.Flash("VOICE", $"{trained.Name} TRAINED - HAVE A LISTEN".ToUpperInvariant(), 4.0);
        _lively(20);
        SettingsChanged?.Invoke("voice");                   // its trained model to choose
        _ = _voice.Test();
    }

    // ---- Dev actions -----------------------------------------------------------------------------------------------------

    /// <summary>Her dev actions (see tools/dev/README.md); false when <paramref name="action"/> isn't one of hers.</summary>
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
            case "manner": SetManner(Seated ?? "", Enum.Parse<Manner>(rest, ignoreCase: true)); break;     // manner:relaxed (or neat) for whoever is on the stool
            case "showmanner": ShowManner(); break;          // "Show me" on how they sit
            case "add": Add([rest]); break;                 // add:path as if it had been dropped on the window
            case "meetdo":                                  // meetdo:label[:value] presses or types on "Meet …"
                var meet = text.Split(':', 2);
                DevMeet(meet[0], meet.Length > 1 ? meet[1] : null);
                break;
            case "pick": PickCharacter(); break;            // the "Add a character…" file dialog
            case "chat":                                    // chat:text sends a line to her as if typed
                OpenChat(focus: false);
                _chatBox.DevSend(text);
                break;
            case "chatbox": OpenChat(focus: false); break;
            case "chatgptat":                               // chatgptat:http://127.0.0.1:port signs in and asks at a stand-in ChatGPT (tools/dev/mock_chatgpt.py)
                ChatGptPlan.Issuer = rest;
                ChatGptPlan.Api = rest + "/v1";
                _chat.Connect(ChatProvider(), BackupProvider());
                break;
            case "plantry": _chat.Plan.TryAgain(); break;  // the ChatGPT plan's next try comes now, not a quarter of an hour on
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
                    Hand(new Telling(rest == "break" ? "BREAK" : "BEDTIME", Rest.Note(word, TimeSpan.FromHours(23), 1.5), Lines.Rest(word), Matters.Should,
                                     Anywhere: false, DateTimeOffset.Now, TimeSpan.FromMinutes(20)));
                }
                else if (Ledger?.Invoke() is { } ledger && Rules?.Invoke() is { } rules)
                    Hand(new Telling("REST", rest == "ask" ? Rest.AskBedtime(Rest.Usual(ledger.Days, rules.DayStartsAt))
                                         : Rest.Notice(ledger.Days, ledger.Today?.Bedtime, ledger.TimeLeftMs(rules), rules.DayStartsAt) ?? "Nothing in the record of days to mention.",
                                     "", rest == "ask" ? Matters.Should : Matters.Could, Anywhere: false, DateTimeOffset.Now, TimeSpan.FromHours(2)));
                break;
            case "later":                                   // later:now has her plan to reach them come due now
                if (Later.FromMark(_chat.Store.Mark(Reaching.LaterMark)) is { } planned)
                    _chat.Store.SetMark(Reaching.LaterMark, (planned with { At = DateTimeOffset.Now }).Mark);
                else
                    GD.Print("Reaching: no plan of hers to bring forward");
                break;
            case "whereabouts":                             // whereabouts:away (here, unsure, asleep) has the player there whatever the cabinet watches; whereabouts:off watches again
                var place = rest.Split(':');                // whereabouts:away:90: away for 90 minutes already
                _pretend = Enum.TryParse<Whereabouts>(place[0], ignoreCase: true, out var where) ? where : null;
                if (place.Length > 1 && _pretend == Whereabouts.Away)
                    _awaySince = DateTimeOffset.Now.AddMinutes(-double.Parse(place[1], System.Globalization.CultureInfo.InvariantCulture));
                GD.Print(_pretend is { } pretend ? $"Reaching: the player is said to be {pretend.ToString().ToLowerInvariant()}" : "Reaching: watching where the player is again");
                break;
            case "proofroll":                               // proofroll:0 always wants proof, proofroll:1 never (instead of chance)
                _activities.Roll = double.Parse(rest, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "phonesend": _ = DevPhone(rest); break;     // phonesend:path as if a phone sent that photo through the card's page
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
            case "nocall": Presence.IgnoreCalls = true; break;     // another app's microphone doesn't count (a test run while the PC is in a call)
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
            case "allow": _permission.Press(Answer.Once); break;        // presses the card's buttons, as a click would: its first,
            case "always": _permission.Press(Answer.Always); break;     // its second (Always, or the plan card's Manage usage),
            case "deny": _permission.Press(Answer.No); break;           // its last
            case "seechange" when rest == "end": _permission.SeeChangeEnd(); break;     // seechange:end opens it and scrolls to the end of the lines
            case "seechange": _permission.SeeChange(); break;  // presses See the change on her suggested change to a skill
            case "memorybook":                              // memorybook:agenda opens it at that tab (notes, agenda, facts, diary, memos, forget; retired)
                OpenMemoryBook();
                if (rest.Length > 0)
                    _memoryBook?.ShowTab(rest);
                break;
            case "chathistory": OpenChatHistory(); break;
            case "chathistorydo": if (IsInstanceValid(_history)) _history!.DevPress(text); break;     // chathistorydo:Reply[:words], Forget:words, Edit:words:new_words, Top, Below, Instructions, Lines, Close
            case "lineswap":                                // lineswap:words: the newest line taken back and the player's words kept under its number, at once
            case "lineclear":                               // lineclear:words: the chat cleared and the player's words kept, at once
                if (OnStool() is not { } seated)
                    break;
                if (name == "lineclear")
                    _chat.Store.ForgetConversation(seated.Id);
                else if (_chat.Store.Through(seated.Id, long.MaxValue, 1) is [var newest])
                    _chat.Store.RemoveLine(newest.Id);
                GD.Print($"Chat: line {_chat.Store.Add(seated.Id, Speaker.User, text, DateTimeOffset.Now)} kept after a {name}");
                break;
            case "forget": if (IsInstanceValid(_memoryBook)) _memoryBook!.DevForget(rest); break;   // forget:chat|all|me, as the memory book's buttons
            case "memorybookdo": if (IsInstanceValid(_memoryBook)) _memoryBook!.DevPress(text); break;     // memorybookdo:Close presses that button in the memory book
            case "memorybooktype" when IsInstanceValid(_memoryBook) && text.Split(':', 2) is [var words, var typed]:   // memorybooktype:words:new_words types in that field, not leaving it
                _memoryBook!.DevType(words, typed);
                break;
            case "memorybookleave": if (IsInstanceValid(_memoryBook)) _memoryBook!.DevLeave(); break;    // leaves the field typed in last, as a click elsewhere
            case "memorychange" when IsInstanceValid(_memoryBook) && text.Split(':', 2) is [var words, var reworded]:   // memorychange:words:new_words: her fact (or agenda item) reworded under the book
                _memoryBook!.DevChange(words, reworded);
                break;
            case "memorycancel": if (IsInstanceValid(_memoryBook)) _memoryBook!.DevCancel(text); break;     // memorycancel:words: her agenda item cancelled under the book
            case "memoryseed": SeedMemory(); break;         // a few notes, facts, a moment and a line, to try the memory book on
            case "skillseed": SeedSkills(); break;          // skills in the app's folder and a vault's, to try the Skills page and her skill tool on
            case "skillmake":                               // skillmake:a-name makes a skill of that name in the app's own skills folder (a long one, to try the pages' widths)
                Directory.CreateDirectory(Path.Combine(_chat.Skills.Own, rest));
                File.WriteAllText(Path.Combine(_chat.Skills.Own, rest, Skills.FileName), $"---\nname: {rest}\ndescription: A stand-in skill, made to try the Skills pages on.\n---\n\nNothing to do.\n");
                GD.Print($"Skills: made {rest}");
                break;
            case "skilllink":                               // links the seeded vault's skills folder on the open Skills page, as picking it there does
                if (IsInstanceValid(_skillsPage) && _skillsPage!.IsVisibleInTree())
                    _skillsPage.DevLink(SeededVault());
                else
                    GD.PrintErr("Skills: the Skills page isn't open");
                break;
            case "skillpage" or "skillgrant" when !IsInstanceValid(_skillsPage) || !_skillsPage!.IsVisibleInTree():
                GD.PrintErr("Skills: the Skills page isn't open");
                break;
            case "skillpage": _skillsPage!.DevOpen(rest); break;     // skillpage:tea-timer opens that skill's own page, as its Settings button does
            case "skillgrant": _skillsPage!.DevGrant(SeededNotes()); break;     // grants the seeded notes folder to the skill whose page is open, as picking it does
            case "notesseed":                               // a stand-in notes folder beside the profile (never the player's), for skillgrant
                Directory.CreateDirectory(SeededNotes());
                File.WriteAllText(Path.Combine(SeededNotes(), "today.md"), "# Today\n- Woke up early.\n");
                GD.Print($"Notes: seeded {SeededNotes()}");
                break;
            case "noteplayer":                              // noteplayer:a_line: the player adds that line to the seeded today.md, as another app would
                File.AppendAllText(Path.Combine(SeededNotes(), "today.md"), text + "\n");
                GD.Print("Notes: the player wrote in today.md");
                break;
            case "notetail":                                // notetail:today.md logs the last line of that note in the seeded folder
                var tail = File.ReadAllLines(Path.Combine(SeededNotes(), rest.Length > 0 ? rest : "today.md")).LastOrDefault(l => l.Trim().Length > 0);
                GD.Print($"Notes: {(rest.Length > 0 ? rest : "today.md")} ends: {tail}");
                break;
            case "checkin": MaybeCheckIn(force: true); break;   // she asks about something past now, whatever the player is doing
            case "presence":                                // logs what she would make of this moment
                var clock = System.Diagnostics.Stopwatch.StartNew();
                GD.Print($"Presence: do not disturb {Presence.DoNotDisturb}, in a call {Presence.InCall}, locked {Presence.Locked}, idle {Idle().TotalSeconds:0} s, " +
                         $"{Where(DateTimeOffset.Now).ToString().ToLowerInvariant()} ({clock.ElapsedMilliseconds} ms)");
                break;
            case "upkeep": _ = _chat.Upkeep(gameRunning: false, now: true); break;     // her upkeep now, without waiting for quiet
            case "say": Say(text); break;                   // say:text puts a line in her bubble (and says it, if her voice can)
            case "voice": Say(text); break;                 // voice:text: the same, named for testing her voice
            case "thinkingsounds": GD.Print($"Voice: {_voice.DevSaveSounds(rest)} thinking sounds saved in {rest}"); break;    // thinkingsounds:folder: her thinking sounds as WAV files
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
            case "bubblechip": Her?.DevAnswer(rest != "notnow"); break;     // bubblechip:yes or bubblechip:notnow: as if that answer on her bubble were clicked
            case "bubblewait": if (Her is not null) Her.Waiting = true; break;     // the answers to a call on the bubble showing, with no call: to see them
            case "legs": Her?.DevLegs(rest); break;         // legs:left|right|both|ring sets which feet hang off the stool's ring
            case "hum": Her?.DevHum(); break;               // hum: a tune in their head now (a sway, or a nod when relaxed)
            case "closeup": Her?.DevCloseUp(rest != "off"); break;     // closeup:hands narrows her camera onto her hands, closeup:off puts it back
            case "moment": Her?.Play(Enum.Parse<Moment>(rest, ignoreCase: true)); break;
            default: return _listening.Dev(name, rest);
        }
        return true;
    }

    /// <summary>
    /// Dev: skills to try the Skills page and her skill tool on. In the app's folder: tea-timer (with a file its
    /// instructions mention and a script, never run), one without a description (broken) and a folder without a SKILL.md
    /// (not a skill); and a notes vault's .claude\skills, for skilllink to link, with journal in it and one named as the
    /// format doesn't allow (shown with a hint, and used).
    /// </summary>
    private void SeedSkills()
    {
        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        var own = _chat.Skills.Own;
        Write(Path.Combine(own, "tea-timer", Skills.FileName),
              "---\nname: tea-timer\ndescription: Brewing a pot of tea the player's way. Use when they want tea or a break with tea.\n---\n\n" +
              "Remind them to warm the pot first. The steeping times are in references/times.md.\nscripts/brew.cmd starts a timer.\n");
        Write(Path.Combine(own, "tea-timer", "references", "times.md"), "Green tea: 2 minutes. Black tea: 4 minutes.\n");
        Write(Path.Combine(own, "tea-timer", "scripts", "brew.cmd"), "@echo off\necho brewed > \"%~dp0brewed.txt\"\n");
        Write(Path.Combine(own, "half-written", Skills.FileName), "---\nname: half-written\ndescription:\n---\n\nNot finished yet.\n");
        Write(Path.Combine(own, "just-notes", "notes.txt"), "Not a skill: there is no SKILL.md here.\n");
        var linked = SeededVault();
        Write(Path.Combine(linked, "journal", Skills.FileName),
              "---\nname: journal\ndescription: >\n  Adding a line to the player's journal\n  at the end of the day.\nmetadata:\n  author: test\n---\n\nAsk how the day went first.\n");
        Write(Path.Combine(linked, "daily-notes", Skills.FileName),    // made for another agent: a name the format doesn't allow, used anyway
              "---\nname: Daily Notes\ndescription: Keeping the player's daily notes tidy.\n---\n\nOne heading a day.\n");
        GD.Print($"Skills: seeded; on: {string.Join(", ", _chat.Skills.On().Select(s => s.Name))}");
    }

    /// <summary>Dev: the seeded vault's skills folder (see <see cref="SeedSkills"/>), in the profile.</summary>
    private string SeededVault() => Path.Combine(Path.GetDirectoryName(_chat.Skills.Own)!, "vault", ".claude", "skills");

    /// <summary>Dev: a stand-in notes folder beside the profile (a folder inside it can't be granted), named "notes" as her paths start.</summary>
    private string SeededNotes() => Path.Combine(Path.GetDirectoryName(_chat.Skills.Own)! + "-files", "notes");

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
        _chat.Store.Observe(character.Id, [new Observation(0, now, Priority.High, "Bob told her about his interview on Friday.", line, line)], line);
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
