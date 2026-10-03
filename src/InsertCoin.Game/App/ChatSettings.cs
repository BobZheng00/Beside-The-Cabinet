using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

namespace InsertCoin.Game.App;

/// <summary>
/// Where the user connects her to their own AI provider (server, key, model) and edits the two notes she always has
/// in mind: what she knows about them (shared by every character) and how the two of them get along.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class ChatSettings : Window
{
    /// <summary>The provider chosen (null: none) after Save; the key and notes are already stored by then.</summary>
    public event Action<Provider?>? Saved;

    private static readonly Vector2 BaseSize = new(540, 660);

    private Chat _chat = null!;
    private string _character = "", _name = "";
    private OptionButton _preset = null!;
    private LineEdit _url = null!, _key = null!, _model = null!;
    private Label _result = null!, _aboutCount = null!, _usCount = null!;
    private TextEdit _about = null!, _us = null!;
    private Button _forget = null!;
    private Protocol _protocol;
    private bool _confirmForget;

    public void Build(Chat chat, Provider? current, string characterId, string characterName)
    {
        _chat = chat;
        _character = characterId;
        _name = characterName;
        Title = $"Talk with {characterName}";
        Visible = false;
        Unresizable = false;
        Transient = true;
        var scale = DisplayServer.ScreenGetDpi(DisplayServer.WindowGetCurrentScreen()) / 96f;
        ContentScaleFactor = scale;
        Size = (Vector2I)(BaseSize * scale);
        MinSize = Size;
        Theme = PaperTheme();
        CloseRequested += QueueFree;

        var page = new PanelContainer { AnchorRight = 1, AnchorBottom = 1 };
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        page.AddChild(column);

        column.AddChild(new Label { Text = $"Talk with {characterName}", ThemeTypeVariation = "Heading" });
        column.AddChild(Hint($"{characterName} answers through your own AI provider. Your key stays on this PC, encrypted for your Windows account."));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(grid);
        _preset = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var preset in Provider.Presets)
            _preset.AddItem(preset.Name);
        _url = new LineEdit { PlaceholderText = "https://…/v1", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _key = new LineEdit { Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _model = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Row(grid, "Provider", _preset);
        Row(grid, "Server", _url);
        Row(grid, "API key", _key);
        Row(grid, "Model", _model);

        var test = new HBoxContainer();
        test.AddThemeConstantOverride("separation", 10);
        var testButton = new Button { Text = "Test", ThemeTypeVariation = "Quiet" };
        _result = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        test.AddChild(testButton);
        test.AddChild(_result);
        column.AddChild(test);

        column.AddChild(new HSeparator());
        (_about, _aboutCount) = Note(column, "About you (every character knows this)",
                                     "Your name, what you're working towards, your exam dates…", chat.Store.Note(MindStore.Shared, MindStore.AboutUser));
        (_us, _usCount) = Note(column, $"You and {characterName}",
                               "How you two get along, what to call you, running jokes…", chat.Store.Note(characterId, MindStore.Us));

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        _forget = new Button { Text = "Forget our conversation", ThemeTypeVariation = "Quiet" };
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        buttons.AddChild(_forget);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        column.AddChild(buttons);

        _preset.ItemSelected += index => UsePreset(Provider.Presets[index], keepFields: false);
        testButton.Pressed += () => _ = Test();
        cancel.Pressed += QueueFree;
        save.Pressed += Save;
        _forget.Pressed += Forget;

        var match = current is null ? 0 : Array.FindIndex(Provider.Presets, p => p.Protocol == current.Protocol && p.BaseUrl == current.BaseUrl);
        _preset.Select(match >= 0 ? match : Provider.Presets.Length - 1);
        UsePreset(Provider.Presets[_preset.Selected], keepFields: current is not null);
        if (current is not null)
        {
            _protocol = current.Protocol;
            _url.Text = current.BaseUrl;
            _model.Text = current.Model;
        }
    }

    /// <summary>Opens the window in the middle of the screen the cabinet is on.</summary>
    public void Open()
    {
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        Position = screen.Position + (screen.Size - Size) / 2;
        Show();
        GrabFocus();
    }

    private void UsePreset(Preset preset, bool keepFields)
    {
        _protocol = preset.Protocol;
        if (!keepFields)
        {
            _url.Text = preset.BaseUrl;
            _model.Text = preset.ModelHint;
        }
        _model.PlaceholderText = preset.ModelHint.Length > 0 ? preset.ModelHint : "the model's name at this provider";
        _key.PlaceholderText = _chat.Keys.Has ? "saved (leave empty to keep it)" : preset.NeedsKey ? "paste your key" : "not needed";
        _result.Text = "";
    }

    private Provider? Chosen() => Provider.From(_protocol.ToString(), _url.Text, _model.Text);

    private async Task Test()
    {
        if (Chosen() is not { } provider)
        {
            Show(_result, "Fill in the server and the model first.", Paper.Bad);
            return;
        }
        Show(_result, "Asking…", Paper.Faint);
        try
        {
            using var client = provider.Connect(_key.Text.Trim().Length > 0 ? _key.Text.Trim() : _chat.Keys.Load());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var reply = await client.GetResponseAsync("Say hello in at most five words.", new ChatOptions { MaxOutputTokens = 300 }, timeout.Token);
            if (IsInstanceValid(this))
                Show(_result, $"Connected. The model says: {reply.Text.Trim()}", Paper.Good);
        }
        catch (Exception e)
        {
            if (IsInstanceValid(this))
                Show(_result, Trouble.Explain(e), Paper.Bad);
        }
    }

    private void Save()
    {
        if (_key.Text.Trim().Length > 0)
            _chat.Keys.Save(_key.Text);
        _chat.Store.SetNote(MindStore.Shared, MindStore.AboutUser, _about.Text.Trim());
        _chat.Store.SetNote(_character, MindStore.Us, _us.Text.Trim());
        Saved?.Invoke(Chosen());
        QueueFree();
    }

    /// <summary>Wipes the conversation with this character; asks for a second click first.</summary>
    private void Forget()
    {
        if (!_confirmForget)
        {
            _confirmForget = true;
            _forget.Text = "Click again to forget";
            GetTree().CreateTimer(4).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !_confirmForget)
                    return;
                _confirmForget = false;
                _forget.Text = "Forget our conversation";
            };
            return;
        }
        _confirmForget = false;
        _chat.Store.Forget(_character);
        _us.Text = "";
        _forget.Text = "Forgotten";
        _forget.Disabled = true;
    }

    private static void Row(GridContainer grid, string label, Control field)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(field);
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };

    private static (TextEdit Field, Label Count) Note(VBoxContainer column, string title, string placeholder, string text)
    {
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var count = new Label { ThemeTypeVariation = "Hint" };
        head.AddChild(count);
        column.AddChild(head);
        var field = new TextEdit
        {
            Text = text, PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 92),
            WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        void Count()
        {
            if (field.Text.Length > Prompt.NoteLimit)
            {
                field.Text = field.Text[..Prompt.NoteLimit];       // a cap, like Hermes': short notes stay sharp
                field.SetCaretLine(field.GetLineCount() - 1);
                field.SetCaretColumn(field.GetLine(field.GetLineCount() - 1).Length);
            }
            count.Text = $"{field.Text.Length} / {Prompt.NoteLimit}";
        }
        field.TextChanged += Count;
        Count();
        column.AddChild(field);
        return (field, count);
    }

    private static void Show(Label label, string text, Color colour)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
    }

    /// <summary>The cabinet's paper and ink for a plain window: cream page, purple buttons, Fredoka.</summary>
    private static Theme PaperTheme()
    {
        var theme = new Theme { DefaultFont = Paper.Font(450), DefaultFontSize = 15 };
        theme.SetStylebox("panel", "PanelContainer", Paper.Box(Paper.Sheet, Paper.Sheet, 0, 0, 22, 18));
        theme.SetColor("font_color", "Label", Paper.Letters);

        theme.SetTypeVariation("Heading", "Label");
        theme.SetFont("font", "Heading", Paper.Font(620));
        theme.SetFontSize("font_size", "Heading", 22);
        theme.SetColor("font_color", "Heading", Paper.Ink);
        theme.SetTypeVariation("Hint", "Label");
        theme.SetColor("font_color", "Hint", Paper.Faint);
        theme.SetFontSize("font_size", "Hint", 13);

        foreach (var field in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", field, Paper.Box(Colors.White, new Color("C9BFE6"), 1, 8, 10, 6));
            theme.SetStylebox("focus", field, Paper.Box(Colors.Transparent, Paper.Accent, 2, 8, 10, 6));
            theme.SetStylebox("read_only", field, Paper.Box(Paper.Wash, new Color("C9BFE6"), 1, 8, 10, 6));
            theme.SetColor("font_color", field, Paper.Letters);
            theme.SetColor("font_placeholder_color", field, Paper.Faint);
            theme.SetColor("caret_color", field, Paper.Ink);
            theme.SetColor("selection_color", field, Paper.Pink);
        }
        theme.SetColor("background_color", "TextEdit", Colors.White);

        Button("Button", Paper.Accent, Paper.AccentLight, Paper.AccentDark, Colors.White);
        Button("OptionButton", Paper.Wash, new Color("E2DAF7"), new Color("D3C8F2"), Paper.Letters);
        theme.SetTypeVariation("Quiet", "Button");
        Button("Quiet", Paper.Wash, new Color("E2DAF7"), new Color("D3C8F2"), Paper.Letters);
        void Button(string type, Color fill, Color hover, Color pressed, Color text)
        {
            theme.SetStylebox("normal", type, Paper.Box(fill, fill, 0, 10, 14, 6));
            theme.SetStylebox("hover", type, Paper.Box(hover, hover, 0, 10, 14, 6));
            theme.SetStylebox("pressed", type, Paper.Box(pressed, pressed, 0, 10, 14, 6));
            theme.SetStylebox("disabled", type, Paper.Box(Paper.Wash, Paper.Wash, 0, 10, 14, 6));
            theme.SetStylebox("focus", type, new StyleBoxEmpty());
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
                theme.SetColor(state, type, text);
            theme.SetColor("font_disabled_color", type, Paper.Faint);
        }

        theme.SetStylebox("panel", "PopupMenu", Paper.Box(Colors.White, new Color("C9BFE6"), 1, 8, 6, 6));
        theme.SetStylebox("hover", "PopupMenu", Paper.Box(Paper.Wash, Paper.Wash, 0, 6, 8, 4));
        theme.SetColor("font_color", "PopupMenu", Paper.Letters);
        theme.SetColor("font_hover_color", "PopupMenu", Paper.Ink);
        theme.SetStylebox("separator", "HSeparator", Paper.Box(new Color("E2DAF7"), new Color("E2DAF7"), 0, 0, 0, 1));
        return theme;
    }
}
