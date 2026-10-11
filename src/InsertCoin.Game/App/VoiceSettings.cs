using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Her voice: whether she speaks aloud, the GPT-SoVITS that runs her voice, and the voice of whoever is on
/// the stool (the language it speaks, its trained model or the base model it is cloned with, the clip it speaks with and
/// the words in it, how carefully it is made), and training it a model of its own. A change is written to the voice's
/// folder at once; the words in a clip once the field is left.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class VoiceSettings : VBoxContainer
{
    private const string GetEngine = "https://github.com/RVC-Boss/GPT-SoVITS/releases";
    private static readonly (int Steps, string Label)[] Qualities =
    [
        (VoiceProfile.DefaultSteps, "Balanced (16 steps)"), (32, "Best (32 steps, slower to start)"), (8, "Faster (8 steps, can sound metallic)"),
    ];
    /// <summary>The base models a clip can be cloned with (<see cref="VoiceProfile.CloneVersions"/>).</summary>
    private static readonly Dictionary<string, string> Clones = new()
    {
        ["v4"] = "Clone with v4: clear, starts once each sentence is made",
        ["v2ProPlus"] = "Clone with v2ProPlus: quickest, blends in four of its clips",
    };

    /// <summary>What a voice folder holds, for the "What goes in a voice folder?" button.</summary>
    public const string FolderGuide =
        "A voice is clips of one person speaking, each with the words said in it.\n\n" +
        "Clips: .wav, .mp3, .ogg or .flac files, one speaker, no music or noise. 3 to 10 seconds each is best; " +
        "the clip she speaks with must be 3 to 10 seconds.\n\n" +
        "Their words, in any of three ways:\n" +
        "  • a training list (any .list file), one line per clip:\n" +
        "        clips/001.wav|hana|JA|今日も頑張ろうね。\n" +
        "    (the language: JA, ZH, EN, KO or YUE)\n" +
        "  • a text file beside each clip with the same name: 001.wav and 001.lab\n" +
        "  • the words as the clip's file name: 今日も頑張ろうね.wav, with a mood in front for a clip\n" +
        "    to use for that mood: [happy] やった！.wav\n\n" +
        "Maybe a model someone trained on the voice: a .ckpt and a .pth file (GPT-SoVITS v2 to v4).\n\n" +
        "For example:\n" +
        "    hana/\n" +
        "        hana.list\n" +
        "        clips/hana_001.wav, hana_002.wav, …\n\n" +
        "To clone her voice, one clip with its words is enough; she speaks with the clip nearest 6 seconds, or the one " +
        "you choose. More clips help a v2ProPlus clone, which blends in the tone of four of them, and training needs " +
        "them: at least a minute in all, five or more sounds better.\n\n" +
        "A folder (or a .zip of it) is a whole voice: dropped on the cabinet or added with Add a folder…, it replaces " +
        "her voice. Clips dropped or added with Add clips… join it.";

    /// <summary>She speaks aloud, or not (the tray's Voice).</summary>
    public event Action<bool>? Switched;
    /// <summary>A GPT-SoVITS folder that works was chosen, or the field was emptied (null).</summary>
    public event Action<string?>? EngineChosen;
    /// <summary>The voice's choices were written to its folder: it is read again.</summary>
    public event Action? VoiceChanged;
    /// <summary>The user wants to add a voice (a file, or a folder), or take this one away; the page is made again after.</summary>
    public event Action? AddVoice, AddFolder, RemoveVoice;
    /// <summary>The user wants this voice trained (its changes written first), or the training stopped.</summary>
    public event Action? Train, StopTraining;

    private Voice _voice = null!;
    private VoiceTrainer _trainer = null!;
    private VoiceProfile? _profile;
    private LineEdit _engine = null!, _words = null!;
    private Label _status = null!, _result = null!;
    private OptionButton? _language, _model, _quality, _clip;
    private readonly List<string> _languages = [];
    private readonly List<string?> _models = [];
    private readonly List<string> _clipFiles = [];
    private readonly Dictionary<string, string> _edited = [];          // words changed on the page, by clip file
    private string? _shown;
    private Label? _training;
    private Button? _train, _stop;
    private ProgressBar? _progress;
    private AudioStreamPlayer _preview = null!;
    private bool _removing;

    public void Build(Voice voice, VoiceTrainer trainer, string? engineFolder, string name, SettingsFile settings)
    {
        _voice = voice;
        _trainer = trainer;
        _profile = voice.Profile;
        _preview = new AudioStreamPlayer();
        AddChild(_preview);

        Form.Page(this, "Her voice");
        var on = Form.Tick(this, "She speaks aloud", settings.Now.Voice, ticked => Switched?.Invoke(ticked));
        void Sync() => on.SetPressedNoSignal(settings.Now.Voice);          // the tray's Voice flipped meanwhile
        settings.Changed += Sync;
        TreeExiting += () => settings.Changed -= Sync;
        Form.Hint(this, $"{name} speaks with GPT-SoVITS, running on this PC: with a model trained on her voice, or cloning it from clips " +
                        "with their words. Drop a voice's folder (or .zip) on the cabinet, or add one below. While a game runs she rests her " +
                        "voice, to leave the game the graphics card.");
        Form.Button(this, "What goes in a voice folder?", ShowGuide).SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

        var engine = Form.Grid(this);
        var folderRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        folderRow.AddThemeConstantOverride("separation", 8);
        _engine = new LineEdit { Text = engineFolder ?? "", PlaceholderText = @"C:\GPT-SoVITS-v2pro-20250604", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        folderRow.AddChild(_engine);
        Form.Button(folderRow, "Browse…", Browse);
        Form.Row(engine, "GPT-SoVITS", folderRow);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Form.Row(engine, "", _status);
        var get = new Button { Text = "Get GPT-SoVITS…", ThemeTypeVariation = "Quiet", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        get.Pressed += () => OS.ShellOpen(GetEngine);
        Form.Row(engine, "", get);

        AddChild(new HSeparator());
        if (_profile is null)
            Form.Subheading(this, "No voice yet.");
        else
            BuildVoice(_profile);

        var actions = Form.Buttons(this);
        var addFile = Form.Button(actions, "Add clips…", () => AddVoice?.Invoke());
        addFile.TooltipText = "Clips join this voice; a voice's .zip replaces it";
        var addFolder = Form.Button(actions, "Add a folder…", () => AddFolder?.Invoke());
        addFolder.TooltipText = Words.Wrap("A voice's folder (clips with their words, maybe a model): it replaces this voice");
        if (_profile is not null)
        {
            Form.Button(actions, "Test", () => _ = Test());
            Button remove = null!;
            remove = Form.Button(actions, "Remove voice", () =>
            {
                if (!_removing)
                {
                    _removing = true;
                    remove.Text = "Sure? Click again";
                    return;
                }
                RemoveVoice?.Invoke();
            });
        }
        _result = Form.Result(this);                        // under the buttons: beside them, the row is wider than the page

        _engine.TextChanged += text =>
        {
            ShowStatus();
            var folder = text.Trim();
            if (folder.Length == 0 || VoiceServer.Check(folder) is null)
                EngineChosen?.Invoke(folder.Length > 0 ? folder : null);
        };
        voice.Changed += ShowStatus;
        trainer.Changed += ShowTraining;
        TreeExiting += () =>
        {
            voice.Changed -= ShowStatus;
            trainer.Changed -= ShowTraining;
            KeepWords();                                    // words still being typed when the window closed
        };
        ShowStatus();
        ShowTraining();
    }

    private void BuildVoice(VoiceProfile profile)
    {
        var grid = Form.Grid(this);
        var seconds = profile.Clips.Sum(c => c.Seconds ?? 0);
        Form.Row(grid, "Voice", new Label
        {
            Text = $"{profile.Name} · {profile.Clips.Count} {(profile.Clips.Count == 1 ? "clip" : "clips")}" +
                   (seconds >= 60 ? $" ({seconds / 60:0.0} min)" : $" ({seconds:0} s)") + (profile.Trained ? " · trained model" : ""),
        });

        _language = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var code in SpokenLanguage.Offered.Append(profile.Language).Where(c => c.Length > 0).Distinct())
        {
            _language.AddItem(SpokenLanguage.Name(code));
            _languages.Add(code);
        }
        if (profile.Language.Length == 0)
        {
            _language.AddItem("Choose…");
            _languages.Add("");
        }
        _language.Select(_languages.IndexOf(profile.Language));
        _language.ItemSelected += _ => Apply();
        Form.Row(grid, "Speaks", _language);

        // its trained model, or a clone: the player can swap between them
        var model = _model = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, FitToLongestItem = false };   // a long label never widens the page
        if (profile.Trained)
        {
            model.AddItem($"Its trained model ({profile.Version ?? "GPT-SoVITS"}): sounds most like her");
            _models.Add(null);
        }
        foreach (var version in VoiceProfile.CloneVersions)
        {
            model.AddItem(Clones[version]);
            _models.Add(version);
        }
        model.Select(Math.Max(0, _models.IndexOf(profile.SpeaksTrained ? null : profile.Runs)));
        Form.Row(grid, "Model", model);

        var quality = _quality = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (_, label) in Qualities)
            quality.AddItem(label);
        var chosen = Array.FindIndex(Qualities, q => q.Steps == profile.Steps);
        quality.Select(chosen >= 0 ? chosen : 0);
        quality.ItemSelected += _ => Apply();
        var qualityLabel = Form.Row(grid, "Quality", quality);
        void ShowQuality() => qualityLabel.Visible = quality.Visible = Runs() is "v3" or "v4";     // steps only matter to v3 and v4
        model.ItemSelected += _ =>
        {
            ShowQuality();
            Apply();
        };
        ShowQuality();

        // the clip she speaks with: the first everyday one, which the player can change and listen to; or one just
        // added, still without its words
        var speaking = profile.Clips.FirstOrDefault() is { Text.Length: 0 } added ? added : profile.ClipFor(null) ?? profile.Clips.FirstOrDefault();
        var clipRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        clipRow.AddThemeConstantOverride("separation", 8);
        var clip = _clip = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, FitToLongestItem = false, Disabled = profile.Clips.Count == 0 };
        foreach (var c in profile.Clips)
        {
            _clipFiles.Add(c.File);
            clip.AddItem(Path.GetFileName(c.File) + (c.Seconds is { } s ? $" · {s:0.0} s" : "") + (c.Mood is { } mood ? $" · {mood}" : "") +
                         (c.Text.Trim().Length == 0 ? " · no words yet" : ""));
        }
        if (speaking is not null)
            clip.Select(profile.Clips.ToList().IndexOf(speaking));
        var play = new Button { Text = "▶", ThemeTypeVariation = "Quiet", TooltipText = "Listen to it", Disabled = speaking is null };
        clipRow.AddChild(clip);
        clipRow.AddChild(play);
        Form.Row(grid, "Clip", clipRow);
        _shown = speaking?.File;
        _words = new LineEdit
        {
            Text = speaking?.Text ?? "", Editable = speaking is not null, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = "exactly what is said in the clip",
        };
        _words.TextChanged += words =>
        {
            if (_shown is not null)
                _edited[_shown] = words;
        };
        _words.TextSubmitted += _ => KeepWords();
        _words.FocusExited += KeepWords;
        Form.Row(grid, "Words in it", _words);
        clip.ItemSelected += index =>
        {
            _shown = _clipFiles[(int)index];
            _words.Text = _edited.TryGetValue(_shown, out var words) ? words : _profile!.Clips.FirstOrDefault(c => c.File == _shown)?.Text ?? "";
            _preview.Stop();
            Apply();                                        // she speaks with this one from now on
        };
        play.Pressed += () => Preview(_shown);

        // training a model of its own
        var trainBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        trainBox.AddThemeConstantOverride("separation", 6);
        var trainRow = new HBoxContainer();
        trainRow.AddThemeConstantOverride("separation", 8);
        _train = new Button { Text = profile.Trained ? "Train it again" : "Train a model on its clips", ThemeTypeVariation = "Quiet" };
        _stop = new Button { Text = "Stop training", ThemeTypeVariation = "Quiet" };
        trainRow.AddChild(_train);
        trainRow.AddChild(_stop);
        _progress = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _training = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, ThemeTypeVariation = "Hint" };
        trainBox.AddChild(trainRow);
        trainBox.AddChild(_progress);
        trainBox.AddChild(_training);
        Form.Row(grid, "Train", trainBox);
        _train.Pressed += () =>
        {
            KeepWords();                                    // train on what is on the page now
            Train?.Invoke();
        };
        _stop.Pressed += () => StopTraining?.Invoke();

        if (profile.Missing is { } missing)
            AddChild(Note($"It still needs {missing}.", Paper.Bad));
        if (speaking?.Seconds is < 3 or > 10)
            AddChild(Note("The clip she speaks with should be 3 to 10 seconds; this one may not work.", Paper.Bad));
        if (profile.About is { Length: > 0 } about)
        {
            var line = System.Text.RegularExpressions.Regex.Replace(about, @"\s+", " ");
            Form.Hint(this, "From its author: " + (line.Length > 160 ? line[..160] + "…" : line));
        }
        Form.Hint(this, "Voices shared online are often made from a game's or a voice actor's lines: fine for yourself, not for sharing on.");
    }

    /// <summary>The version the page's choice of model runs on.</summary>
    private string? Runs() => _profile is null ? null
        : _model is null || _models[_model.Selected] is not { } clone ? (_profile with { CloneWith = null }).Runs
        : clone;

    /// <summary>Words typed for a clip are written once the field is left (not at every letter: the voice is read again each time).</summary>
    private void KeepWords()
    {
        if (_profile is not null && _edited.Count > 0 && _edited.Any(e => _profile.Clips.FirstOrDefault(c => c.File == e.Key)?.Text != e.Value.Trim()))
            Apply();
    }

    /// <summary>Writes the voice's choices to its folder, and has it read again.</summary>
    private void Apply()
    {
        if (_profile is not { } profile)
            return;
        var language = _language is null ? profile.Language : _languages[_language.Selected];
        var clips = profile.Clips.Select(c => _edited.TryGetValue(c.File, out var words)
            ? c with { Text = words.Trim(), Language = c.Language.Length > 0 ? c.Language : language }
            : c).ToList();
        if (clips.FindIndex(c => c.File == _shown) is > 0 and var chosen)
        {
            var speaking = clips[chosen];                  // the chosen one first: she speaks with it, and v2 models blend it in
            clips.RemoveAt(chosen);
            clips.Insert(0, speaking);
        }
        var steps = _quality is null ? profile.Steps : Qualities[_quality.Selected].Steps;
        var cloneWith = _model is null ? profile.CloneWith : _models[_model.Selected];
        (profile with { Language = language, Clips = clips, Steps = steps, CloneWith = cloneWith }).Save(_voice.Folder);
        _profile = VoiceProfile.Load(_voice.Folder) ?? profile;
        _edited.Clear();
        VoiceChanged?.Invoke();
    }

    private async Task Test()
    {
        KeepWords();                                        // test what is on the page now
        Form.Say(_result, "Getting her voice ready…", Paper.Faint);
        var problem = await _voice.Test();
        if (IsInstanceValid(this))
            Form.Say(_result, problem ?? "Listen…", problem is null ? Paper.Good : Paper.Bad);
    }

    /// <summary>Plays a clip as it is, to choose the one she speaks with.</summary>
    private void Preview(string? file)
    {
        if (file is null)
            return;
        var path = Path.GetFullPath(Path.Combine(_voice.Folder, file));
        AudioStream? stream = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => AudioStreamWav.LoadFromFile(path),
            ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
            ".mp3" => AudioStreamMP3.LoadFromFile(path),
            _ => null,
        };
        if (stream is null)
        {
            Form.Say(_result, "This kind of clip can't be played here.", Paper.Bad);
            return;
        }
        _preview.Stream = stream;
        _preview.Play();
    }

    private void ShowGuide()
    {
        var dialog = new AcceptDialog { Title = "What goes in a voice folder?", DialogText = FolderGuide, DialogAutowrap = true, Transient = true };
        AddChild(dialog);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new Vector2I(560, 560));
    }

    private void Browse()
    {
        var dialog = new FileDialog
        {
            Title = "Where is GPT-SoVITS?",
            ModeOverridesTitle = false,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentDir = Directory.Exists(_engine.Text) ? _engine.Text : "C:\\",
        };
        dialog.DirSelected += folder =>
        {
            _engine.Text = folder;
            _engine.EmitSignal(LineEdit.SignalName.TextChanged, folder);        // as if typed: used if it works
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void ShowStatus()
    {
        if (!IsInstanceValid(this))
            return;
        if (VoiceServer.Check(_engine.Text.Trim()) is { } problem)
            Form.Say(_status, problem, Paper.Bad);
        else
            Form.Say(_status, _voice.Status, _voice.Now switch
            {
                Voice.Phase.Ready => Paper.Good,
                Voice.Phase.Failed => Paper.Bad,
                _ => Paper.Faint,
            });
    }

    /// <summary>The Train row: what training would take, or how far it has got, or why it can't.</summary>
    private void ShowTraining()
    {
        if (!IsInstanceValid(this) || _training is null || _profile is null)
            return;
        var mine = _trainer.Folder is { } folder && Path.GetFullPath(folder) == Path.GetFullPath(_voice.Folder);
        _progress!.Visible = _stop!.Visible = _trainer.Running && mine;
        _train!.Visible = !_trainer.Running;
        if (_trainer.Running)
        {
            _progress.Value = _trainer.Done;
            var left = _trainer.Left is { } time ? $", about {Minutes(time)} left" : "";
            Form.Say(_training, mine ? $"Now: {_trainer.Stage}{left}. Her voice rests until it is done; you can close this window." +
                                       (_trainer.Crowded ? " Other apps are using much of the graphics card, so it will be slow: closing them helps." : "")
                                     : "Training another voice: one at a time.", Paper.Faint);
            return;
        }
        if (mine && _trainer.Problem is { } stopped)
        {
            Form.Say(_training, stopped, Paper.Bad);
            return;
        }
        var clips = VoiceTraining.Clips(_profile).Count;
        var seconds = VoiceTraining.Seconds(_profile);
        if (VoiceTraining.Needs(_profile) is { } needs)
        {
            _train.Disabled = true;
            Form.Say(_training, $"Training needs {needs}. Add a folder of her clips with their words (What goes in a voice folder?).", Paper.Faint);
            return;
        }
        _train.Disabled = false;
        var (estimate, measured) = _trainer.Estimate(_profile);
        Form.Say(_training, $"On {clips} clips, {seconds / 60:0.0} minutes of speech: about {Minutes(estimate)} " +
                            (measured ? "on this PC. " : "with an 8 GB laptop graphics card (once it starts, it tells the time left on yours). ") +
                            "It takes the whole graphics card, so her voice rests meanwhile, and a game starting stops it." +
                            (_profile.Trained ? "" : " Then you can swap between the trained model and a clone under Model.") +
                            (seconds < VoiceTraining.GoodSeconds ? " Five minutes of speech or more sounds better." : ""), Paper.Faint);
    }

    private static string Minutes(TimeSpan time) => Math.Max(1, Math.Round(time.TotalMinutes)) is var m && m == 1 ? "a minute" : $"{m} minutes";

    private static Label Note(string text, Color colour)
    {
        var label = Form.Hint(text);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
}
