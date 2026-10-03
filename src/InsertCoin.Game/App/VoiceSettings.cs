using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Where the user gives her a voice: the GPT-SoVITS that runs it, and the voice of whoever is on the stool (the
/// language it speaks, the clip it takes its tone from and the words in that clip, how carefully it is made).
/// </summary>
[SupportedOSPlatform("windows")]
public partial class VoiceSettings : Window
{
    private const string GetEngine = "https://github.com/RVC-Boss/GPT-SoVITS/releases";
    private static readonly (int Steps, string Label)[] Qualities =
    [
        (VoiceProfile.DefaultSteps, "Balanced (16 steps)"), (32, "Best (32 steps, slower to start)"), (8, "Faster (8 steps, can sound metallic)"),
    ];

    /// <summary>Saved: the GPT-SoVITS folder chosen (the voice's own changes are already written to its folder).</summary>
    public event Action<string?>? Saved;
    /// <summary>The user wants to add a voice, or take this one away; the window closes and is opened again after.</summary>
    public event Action? AddVoice, RemoveVoice;

    private Voice _voice = null!;
    private VoiceProfile? _profile;
    private LineEdit _engine = null!, _words = null!;
    private Label _status = null!, _result = null!;
    private OptionButton? _language, _quality;
    private readonly List<string> _languages = [];
    private bool _removing;

    public void Build(Voice voice, string? engineFolder, string characterName)
    {
        _voice = voice;
        _profile = voice.Profile;
        Title = $"{characterName}'s voice";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        page.AddChild(column);

        column.AddChild(new Label { Text = $"{characterName}'s voice", ThemeTypeVariation = "Heading" });
        column.AddChild(Hint($"{characterName} speaks with GPT-SoVITS, running on this PC. Voices shared online are GPT-SoVITS models " +
                             "(a .ckpt and a .pth, often with clips of the voice); or clone one from a 3 to 10 second clip. " +
                             "Drop either on the cabinet, or add one here. While a game runs she rests her voice, to leave the game the graphics card."));

        var engine = new GridContainer { Columns = 2 };
        engine.AddThemeConstantOverride("h_separation", 12);
        engine.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(engine);
        var folderRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        folderRow.AddThemeConstantOverride("separation", 8);
        _engine = new LineEdit { Text = engineFolder ?? "", PlaceholderText = @"C:\GPT-SoVITS-v2pro-20250604", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var browse = new Button { Text = "Browse…", ThemeTypeVariation = "Quiet" };
        folderRow.AddChild(_engine);
        folderRow.AddChild(browse);
        Row(engine, "GPT-SoVITS", folderRow);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Row(engine, "", _status);
        var get = new Button { Text = "Get GPT-SoVITS…", ThemeTypeVariation = "Quiet", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        Row(engine, "", get);

        column.AddChild(new HSeparator());
        if (_profile is null)
            column.AddChild(new Label { Text = "No voice yet.", ThemeTypeVariation = "Subheading" });
        else
            BuildVoice(column, _profile);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        var add = new Button { Text = _profile is null ? "Add a voice…" : "Another voice…", ThemeTypeVariation = "Quiet" };
        actions.AddChild(add);
        if (_profile is not null)
        {
            var test = new Button { Text = "Test", ThemeTypeVariation = "Quiet" };
            var remove = new Button { Text = "Remove voice", ThemeTypeVariation = "Quiet" };
            actions.AddChild(test);
            actions.AddChild(remove);
            test.Pressed += () => _ = Test();
            remove.Pressed += () =>
            {
                if (!_removing)
                {
                    _removing = true;
                    remove.Text = "Sure? Click again";
                    return;
                }
                RemoveVoice?.Invoke();
                QueueFree();
            };
        }
        _result = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        actions.AddChild(_result);
        column.AddChild(actions);

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        column.AddChild(buttons);

        browse.Pressed += Browse;
        get.Pressed += () => OS.ShellOpen(GetEngine);
        add.Pressed += () =>
        {
            AddVoice?.Invoke();
            QueueFree();
        };
        cancel.Pressed += QueueFree;
        save.Pressed += () =>
        {
            Apply();
            QueueFree();
        };
        _engine.TextChanged += _ => ShowStatus();
        voice.Changed += ShowStatus;
        TreeExiting += () => voice.Changed -= ShowStatus;
        ShowStatus();
    }

    public void Open() => Paper.Open(this, new Vector2(580, _profile is null ? 440 : _profile.About is { Length: > 0 } ? 680 : 620));

    private void BuildVoice(VBoxContainer column, VoiceProfile profile)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(grid);
        Row(grid, "Voice", new Label { Text = profile.Name + (profile.Gpt is null ? " (cloned from a clip)" : profile.Version is { } v ? $" · GPT-SoVITS {v}" : "") });

        _language = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
        Row(grid, "Speaks", _language);

        var clip = profile.ClipFor(null) ?? profile.Clips.FirstOrDefault();
        Row(grid, "Clip", new Label { Text = clip is null ? "none" : Path.GetFileName(clip.File) + (clip.Seconds is { } s ? $" · {s:0.0} s" : "") });
        _words = new LineEdit
        {
            Text = clip?.Text ?? "", Editable = clip is not null, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "exactly what is said in the clip",
        };
        Row(grid, "Words in it", _words);

        if (profile.Slow)
        {
            _quality = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var (_, label) in Qualities)
                _quality.AddItem(label);
            var chosen = Array.FindIndex(Qualities, q => q.Steps == profile.Steps);
            _quality.Select(chosen >= 0 ? chosen : 0);
            Row(grid, "Quality", _quality);
        }

        if (profile.Missing is { } missing)
            column.AddChild(Note($"It still needs {missing}.", Paper.Bad));
        if (clip?.Seconds is < 3 or > 10)
            column.AddChild(Note("GPT-SoVITS wants a clip of 3 to 10 seconds; this one may not work.", Paper.Bad));
        if (profile.About is { Length: > 0 } about)
        {
            var line = System.Text.RegularExpressions.Regex.Replace(about, @"\s+", " ");
            column.AddChild(Hint("From its author: " + (line.Length > 160 ? line[..160] + "…" : line)));
        }
        column.AddChild(Hint("Voices shared online are often made from a game's or a voice actor's lines: fine for yourself, not for sharing on."));
    }

    /// <summary>Writes the voice's changes to its folder and hands back the GPT-SoVITS folder chosen.</summary>
    private void Apply()
    {
        if (_profile is { } profile)
        {
            var main = profile.ClipFor(null) ?? profile.Clips.FirstOrDefault();
            var language = _language is null ? profile.Language : _languages[_language.Selected];
            var clips = profile.Clips.Select(c => c == main
                ? c with { Text = _words.Text.Trim(), Language = c.Language.Length > 0 ? c.Language : language }
                : c).ToList();
            var steps = _quality is null ? profile.Steps : Qualities[_quality.Selected].Steps;
            (profile with { Language = language, Clips = clips, Steps = steps }).Save(_voice.Folder);
        }
        Saved?.Invoke(_engine.Text.Trim().Length > 0 ? _engine.Text.Trim() : null);
    }

    private async Task Test()
    {
        Apply();                                    // test what is on the page now
        _profile = _voice.Profile;
        Show(_result, "Getting her voice ready…", Paper.Faint);
        var problem = await _voice.Test();
        if (IsInstanceValid(this))
            Show(_result, problem ?? "Listen…", problem is null ? Paper.Good : Paper.Bad);
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
            ShowStatus();
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
            Show(_status, problem, Paper.Bad);
        else
            Show(_status, _voice.Status, _voice.Now switch
            {
                Voice.Phase.Ready => Paper.Good,
                Voice.Phase.Failed => Paper.Bad,
                _ => Paper.Faint,
            });
    }

    private static void Row(GridContainer grid, string label, Control field)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(field);
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };

    private static Label Note(string text, Color colour)
    {
        var label = Hint(text);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    private static void Show(Label label, string text, Color colour)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
    }
}
