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
    private Func<string> _situation = null!, _status = null!;
    private Action<double> _lively = null!;
    private string _characters = "";
    private Chat _chat = null!;
    private ChatBox _chatBox = null!;
    private PermissionCard _permission = null!;
    private Memos _memos = null!;
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
    private DateTimeOffset _quietFrom;                      // when she last stopped talking or listening

    /// <summary>Characters were added: all there are now (for the tray's list), and the one seated.</summary>
    public event Action<(string Id, string Name)[], string>? CharactersAdded;

    /// <summary>The player's memos: on the cabinet, in the archive, and in their own hand.</summary>
    public Memos Memos => _memos;

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
    /// <param name="status">Her line about where things stand, said when she is clicked.</param>
    /// <param name="lively">Full pace for this many seconds: something worth watching is about to happen.</param>
    public void Build(string dataDir, SettingsFile settings, CabinetView cabinet, DesktopWindow window,
                      Func<string> situation, Func<string> status, Action<double> lively)
    {
        _settings = settings;
        _cabinet = cabinet;
        _window = window;
        _situation = situation;
        _status = status;
        _lively = lively;
        _characters = Path.Combine(dataDir, "characters");

        _chat = new Chat(dataDir) { ReminderLead = () => settings.Now.ReminderLead };
        _chat.UseSearch(settings.Now.MemorySearch);        // remembered first, so connecting loads the right model
        _chat.UseWindow(settings.Now.ChatWindow);
        _chat.Connect(ChatProvider());
        _chatBox = new ChatBox();
        AddChild(_chatBox);
        _chatBox.Submitted += said => _ = Ask(said);
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
        checkIns.Timeout += () => MaybeCheckIn();
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
        GentleGap = TimeSpan.FromHours(3);
    private DateTimeOffset? _activeSince;                   // the player has been at the PC since then, with no break of five minutes
    private DateTimeOffset _lastCheckIn = DateTimeOffset.MinValue;

    /// <summary>
    /// Every fifteen seconds: when something the player told her about has been over for a while (see
    /// <see cref="Agenda.ToAskAbout"/>), she asks how it went, but only once they are back and free: at the PC for two
    /// minutes and using it now, not in a call (another app has the microphone or camera), not in anything Windows
    /// holds notifications back for (full screen, presenting, quiet time, locked), not in the quiet hours, and not while
    /// she is busy. Gentle: one every three hours at most. A meeting that runs over keeps its call going, so she waits.
    /// </summary>
    private void MaybeCheckIn(bool force = false)
    {
        var now = DateTimeOffset.Now;
        var idle = UserActivity.IdleFor();
        if (idle >= Away)
            _activeSince = null;
        else
            _activeSince ??= now - idle;
        var mode = _settings.Now.CheckIns;
        if (!force && (mode == "Off" || Quiet(now.Hour) || idle > TimeSpan.FromMinutes(1) || _activeSince is not { } since || now - since < BackFor
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
            await _chat.Announce(Agenda.CheckIn(plan, DateTimeOffset.Now), "", Situation(), Her, _voice.Begin(), _voice.Language);
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

    /// <summary>She waves and says where things stand; when she can talk, the slip to type to her opens too.</summary>
    public void Clicked()
    {
        if (_listening.HeldToTalk)
            return;                                         // that was a hold: she was being talked to
        var replying = Replying;
        _listening.Drop();
        _ = Interrupt();
        _lively(6);
        if (!replying)
        {
            Her?.Play(Moment.Greet);
            Say(_status());                                 // cut off, she stops and looks at the player instead
        }
        OpenChat();
    }

    /// <summary>The window is solid here: her figure, or the slip.</summary>
    public bool Hits(Vector2 canvasPoint) =>
        Her?.Hits(canvasPoint) == true || _chatBox.Hits(canvasPoint) || _permission.Hits(canvasPoint) || _memos.SheetHits(canvasPoint);

    /// <summary>The slip keeps the mouse here, for typing.</summary>
    public bool Typing(Vector2 canvasPoint) => _chatBox.Hits(canvasPoint) || _memos.SheetHits(canvasPoint);

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

    /// <summary>
    /// Copies models (picked or dropped) into the user's characters folder and seats the first of them. A character
    /// card goes to whoever is on the stool instead (a model dropped with it first), who then greets as the card says;
    /// so does a voice.
    /// </summary>
    private void Add(string[] paths)
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
        _listening.Drop();
        await Interrupt();                                  // asking something new cuts off what she was still saying
        _chatBox.SetWaiting(true);
        _lively(120);                                       // keep her lively while she thinks and talks
        try
        {
            await _chat.Ask(said, Situation(), Her, _voice.Begin(), _voice.Language);
            if (_chat.Replied is { } reply)
                _listening.Said(answer: true, reply);
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

    /// <summary>Where things stand on the cabinet, for her: the cabinet's own account, and the memos on it.</summary>
    private string Situation()
    {
        var memos = _memos.OnTheCabinet.Select(t => $"\"{t}\"").ToList();
        return memos.Count == 0 ? _situation() : $"{_situation()}\nMemos on the cabinet (hanging from the slot or pinned on its side): {string.Join(", ", memos)}.";
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
        var bottom = _cabinet.Skin.Canvas.Y - 14 - (_chatBox.IsOpen ? ChatBox.Height + 8 : 0);
        var answer = await _permission.Ask(asking, new Vector2(14, bottom), cancel);
        Her?.Play(Moment.Heard);
        GD.Print($"Tools: she asked to use {asking.Tool.Name} on {asking.Detail}: {answer}");
        return answer;
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
                            _settings.Now.ChatPrices, _settings.Now.Currency);
        _chatSettings.PricesSaved += (model, prices, currency) =>
        {
            var all = new Dictionary<string, Prices>(_settings.Now.ChatPrices);
            if (prices is null)
                all.Remove(model);
            else
                all[model] = prices;
            _settings.Save(_settings.Now with { ChatPrices = all, Currency = currency });
        };
        _chatSettings.Saved += (provider, search, talkKey, handsFree, window, permissions, auto) =>
        {
            _settings.Save(_settings.Now with
            {
                ChatProtocol = provider?.Protocol.ToString(), ChatBaseUrl = provider?.BaseUrl, ChatModel = provider?.Model, MemorySearch = search,
                TalkKey = talkKey, HandsFree = handsFree, ChatWindow = window, ToolPermissions = new(permissions), ToolsAuto = auto,
            });
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
            case "chatsettings": OpenChatSettings(); break;
            case "usage": OpenChatSettings(); _chatSettings?.DevUsage(); break;     // the chat settings' Usage tab
            case "listeningsettings": OpenChatSettings(); _chatSettings?.DevListening(); break;     // its Listening tab
            case "abilities": OpenChatSettings(); _chatSettings?.DevAbilities(); break;     // its Abilities tab
            case "memo":                                    // memo:title:text: she prints a memo, as the tool has her do
                var parts = text.Split(':', 2);
                Her?.Typing();
                GetTree().CreateTimer(4).Timeout += () => PrintMemo(parts[0], parts.Length > 1 ? parts[1] : parts[0], OnStool()?.Id ?? "");
                break;
            case "memoseed": _memos.DevSeed(OnStool()?.Id ?? ""); break;    // memos hanging, pinned and archived, to try them on
            case "memoopen": _memos.DevOpen(rest.Length > 0 ? int.Parse(rest) : null); break;     // memoopen: the one hanging; memoopen:0 pinned at place 0
            case "memosheet": _memos.DevSheet(text); break;        // the sheet: pin, off, edit, done, print, delete, away, esc (not copy: it would take the clipboard); colour:pink, sticker:star, write:title:text, tell
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
            case "allow": _permission.DevAnswer(Answer.Once); break;       // answers the card she asks with, as its buttons would
            case "always": _permission.DevAnswer(Answer.Always); break;
            case "deny": _permission.DevAnswer(Answer.No); break;
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
