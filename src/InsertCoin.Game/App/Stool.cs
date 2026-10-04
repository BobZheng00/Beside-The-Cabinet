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
    /// <summary>A press on her (or of the talk key) held this long is talking to her, not a click.</summary>
    private const double HoldSeconds = 0.35;
    private const string EarsModel = "res://models/sense-voice/";

    /// <summary>How she is being talked to: held on her or by the talk key (she answers what she heard at once), or by the slip's microphone (written into it).</summary>
    private enum Talk { None, Hold, Key, Mic }

    private SettingsFile _settings = null!;
    private CabinetView _cabinet = null!;
    private DesktopWindow _window = null!;
    private Func<string> _situation = null!, _status = null!;
    private Action<double> _lively = null!;
    private string _characters = "";
    private Chat _chat = null!;
    private ChatBox _chatBox = null!;
    private Voice _voice = null!;
    private VoiceTrainer _trainer = null!;
    private ChatSettings? _chatSettings;
    private MemoryBook? _memoryBook;
    private ChatHistory? _history;
    private VoiceSettings? _voiceSettings;
    private bool _gameRunning;
    private Ears _ears = null!;
    private Task<Hearing?>? _hearing;
    private Talk _talking;
    private bool _pressedOnHer, _heldToTalk, _keyUsed;
    private double _pressedFor, _keyFor;

    /// <summary>Characters were added: all there are now (for the tray's list), and the one seated.</summary>
    public event Action<(string Id, string Name)[], string>? CharactersAdded;

    /// <summary>Her figure, while someone is on the stool.</summary>
    public CompanionView? Her { get; private set; }

    /// <summary>She is answering the user (a remark about the cabinet would cut in).</summary>
    public bool Answering => _chat.Answering;

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

        _chat = new Chat(dataDir);
        _chat.UseSearch(settings.Now.MemorySearch);        // remembered first, so connecting loads the right model
        _chat.Connect(ChatProvider());
        _chatBox = new ChatBox();
        AddChild(_chatBox);
        _chatBox.Submitted += said => _ = Ask(said);

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

        _ears = new Ears();
        AddChild(_ears);
        _ears.Full += () => _ = Heard();
        _window.Pressed += at =>
        {
            _pressedOnHer = Her?.Hits(at) == true;
            _pressedFor = 0;
        };
        _window.DragStarted += () =>
        {
            _pressedOnHer = false;
            if (_talking == Talk.Hold)
                StopListening();                            // she was being dragged, not talked to
        };
        _window.Released += () =>
        {
            _pressedOnHer = false;
            if (_talking == Talk.Hold)
                _ = Heard();
            _heldToTalk = false;
        };
        _chatBox.MicPressed += () =>
        {
            if (_talking == Talk.Mic)
                _ = Heard();
            else if (_talking == Talk.None)
                Listen(Talk.Mic);
        };

        GetWindow().FilesDropped += Add;                     // a model, card or voice dropped on the cabinet or on her
        var upkeep = new Godot.Timer { WaitTime = 60, Autostart = true };
        upkeep.Timeout += () => _ = _chat.Upkeep(_gameRunning);
        AddChild(upkeep);
        ShowHer(settings.Now.ShowCompanion);
    }

    // ---- What the cabinet tells her ------------------------------------------------------------------------------------

    /// <summary>A game is running: her voice rests and training stops, to leave the game the graphics card.</summary>
    public bool GameRunning
    {
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

    /// <summary>A line in her speech bubble, said aloud too when her voice can, unless the user has asked for quiet.</summary>
    public void Say(string line)
    {
        if (!_settings.Now.SpeechBubbles || Her is null)
            return;
        Her.Say(line);
        _voice.Line(line);
        _chat.Said(line);
    }

    /// <summary>She waves and says where things stand; when she can talk, the slip to type to her opens too.</summary>
    public void Clicked()
    {
        if (_heldToTalk)
            return;                                         // that was a hold: she was being talked to
        _voice.Stop();
        _lively(6);
        Her?.Play(Moment.Greet);
        if (!_chat.Answering)
            Say(_status());
        OpenChat();
    }

    /// <summary>The window is solid here: her figure, or the slip.</summary>
    public bool Hits(Vector2 canvasPoint) => Her?.Hits(canvasPoint) == true || _chatBox.Hits(canvasPoint);

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
        if (_hearing is { IsCompletedSuccessfully: true, Result: { } hearing })
            hearing.Dispose();
    }

    // ---- Talking to her aloud -----------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_pressedOnHer && !_heldToTalk && _talking == Talk.None)
        {
            _pressedFor += delta;
            if (_pressedFor >= HoldSeconds)
            {
                _heldToTalk = true;                         // the release is the end of talking, not a click
                Listen(Talk.Hold);
            }
        }
        if (_settings.Now.TalkKey != TalkKey.Off)
        {
            if (TalkKey.IsDown(_settings.Now.TalkKey))
            {
                _keyFor += delta;
                if (_keyFor >= HoldSeconds && !_keyUsed && _talking == Talk.None)
                {
                    _keyUsed = true;                        // once per hold, whether she could listen or not
                    Listen(Talk.Key);
                }
            }
            else
            {
                _keyFor = 0;
                _keyUsed = false;
                if (_talking == Talk.Key)
                    _ = Heard();
            }
        }
        if (_talking != Talk.None)
            _chatBox.SetLevel(_ears.Level);
    }

    /// <summary>Whether she has ears in this build (SenseVoice, fetched by tools/models/fetch.py).</summary>
    private static bool HasEars => ModelFiles.Present(EarsModel + Hearing.ModelFile);

    /// <summary>She stops talking, leans in and listens: the microphone opens until the player lets go (or clicks the microphone again).</summary>
    private void Listen(Talk how)
    {
        if (Her is null || _chat.Answering)
            return;
        if (!_chat.Ready || !HasEars)
        {
            if (how != Talk.Key)                            // a key held for something else in another app says nothing
                Say(!_chat.Ready ? "I can only answer once I'm connected: tray → Chat settings." : "I can't hear in this build (no speech model).");
            return;
        }
        _hearing ??= Task.Run(LoadHearing);                 // loads while the player talks
        _voice.Stop();
        _talking = how;
        _ears.Start();
        _lively(Ears.Longest + 10);
        Her.Play(Moment.Listen);
        OpenChat(focus: how != Talk.Key);                   // the talk key is held in another app: leave its focus alone
        _chatBox.SetListening(true);
    }

    /// <summary>The microphone closes without anything being heard (she was dragged instead).</summary>
    private void StopListening()
    {
        _talking = Talk.None;
        _ears.Stop();
        _chatBox.SetListening(false);
        Her?.Play(Moment.Heard);
    }

    /// <summary>The player has finished: what they said is heard, then answered, or written into the slip.</summary>
    private async Task Heard()
    {
        if (_talking == Talk.None)
            return;
        var how = _talking;
        _talking = Talk.None;
        var seconds = _ears.Seconds;
        var said = _ears.Stop();
        var silent = _ears.Peak == 0;
        _chatBox.SetListening(false);
        Her?.Play(Moment.Heard);
        if (silent)
        {
            Say("I can't hear you. Is the microphone allowed? Windows Settings → Privacy & security → Microphone.");
            return;
        }
        if (seconds >= 0.3)                                 // less is a slip of the finger
            await Understand(said, how);
    }

    /// <summary>Hears <paramref name="said"/> (16 kHz) and answers it, or writes it into the slip.</summary>
    private async Task Understand(float[] said, Talk how)
    {
        if (await (_hearing ??= Task.Run(LoadHearing)) is not { } hearing)
        {
            Say("I can't hear right now: my ears didn't load (see the log).");
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var words = await Task.Run(() => hearing.Hear(said));
        GD.Print($"Ears: {said.Length / (double)Hearing.SampleRate:0.0} s heard in {clock.ElapsedMilliseconds} ms");   // never what was said
        if (!IsInsideTree() || words.Length == 0)
            return;
        if (how == Talk.Mic)
            _chatBox.Insert(words);
        else if (!_chat.Answering)
        {
            _chatBox.ShowHeard(words);
            await Ask(words);
        }
    }

    private static Hearing? LoadHearing()
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var model = ModelFiles.OnDisk(EarsModel + Hearing.ModelFile, "sense-voice");
            ModelFiles.OnDisk(EarsModel + Hearing.TokensFile, "sense-voice");
            var hearing = new Hearing(Path.GetDirectoryName(model)!);
            GD.Print($"Ears: SenseVoice loaded in {clock.ElapsedMilliseconds} ms");
            return hearing;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Ears: {e.Message}");
            return null;
        }
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
    }

    /// <summary>Dev: a recording heard as if said holding the talk key, or into the slip's microphone (no microphone opened).</summary>
    private async Task HearFile(string path, Talk how)
    {
        if (Her is null || !_chat.Ready || !HasEars)
        {
            GD.PrintErr("Ears: hear: needs her on the stool, a chat provider and the speech model");
            return;
        }
        OpenChat(focus: false);                             // as listening does
        var wav = new Wav();
        var samples = wav.Feed(File.ReadAllBytes(path));
        await Understand(Hearing.To16k(samples, wav.SampleRate), how);
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
    private void OpenChat(bool focus = true)
    {
        if (!_chat.Ready || Her is null)
            return;
        if (focus)
            DisplayServer.WindowMoveToForeground();
        _chatBox.Open(new Vector2(14, _cabinet.Skin.Canvas.Y - ChatBox.Height - 14), $"Say something to {_chat.Conversation!.Persona.Name}…");
    }

    private async Task Ask(string said)
    {
        if (Her is null)
            return;
        _chatBox.SetWaiting(true);
        _lively(120);                                       // keep her lively while she thinks and talks
        try
        {
            await _chat.Ask(said, _situation(), Her, _voice.Begin());
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
        _chatSettings.Build(_chat, ChatProvider(), character.Name, _settings.Now.MemorySearch, _settings.Now.TalkKey, HasEars);
        _chatSettings.Saved += (provider, search, talkKey) =>
        {
            _settings.Save(_settings.Now with
            {
                ChatProtocol = provider?.Protocol.ToString(), ChatBaseUrl = provider?.BaseUrl, ChatModel = provider?.Model, MemorySearch = search,
                TalkKey = talkKey,
            });
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
        _memoryBook.Build(_chat.Store, character.Id, character.Name);
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
            case "hear": _ = HearFile(rest, Talk.Key); break;            // hear:path.wav as if said holding the talk key (no microphone)
            case "dictate": _ = HearFile(rest, Talk.Mic); break;         // dictate:path.wav as if said into the slip's microphone
            case "mic": _chatBox.DevMic(); break;           // clicks the slip's microphone (opens the real microphone)
            case "listen": Listen(Talk.Hold); break;        // listens as if held on her (opens the real microphone)…
            case "listened": _ = Heard(); break;            // …until this
            case "chatsettings": OpenChatSettings(); break;
            case "memorybook": OpenMemoryBook(); break;
            case "chathistory": OpenChatHistory(); break;
            case "forget": if (IsInstanceValid(_memoryBook)) _memoryBook!.DevForget(rest); break;   // forget:chat|all|me, as the memory book's buttons
            case "memoryseed": SeedMemory(); break;          // a few notes, facts, a moment and a line, to try the memory book on
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
            default: return false;
        }
        return true;
    }
}
