using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

namespace InsertCoin.Game.App;

/// <summary>Where the user connects her to their own AI provider: server, key and model, with a test. What she
/// remembers is in the memory book (see <see cref="MemoryBook"/>).</summary>
[SupportedOSPlatform("windows")]
public partial class ChatSettings : Window
{
    /// <summary>The provider chosen (null: none) after Save; the key is already stored by then.</summary>
    public event Action<Provider?>? Saved;

    private Chat _chat = null!;
    private OptionButton _preset = null!;
    private LineEdit _url = null!, _key = null!, _model = null!;
    private Label _result = null!;
    private Protocol _protocol;

    public void Build(Chat chat, Provider? current, string characterName)
    {
        _chat = chat;
        Title = $"Talk with {characterName}";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        page.AddChild(column);

        column.AddChild(new Label { Text = $"Talk with {characterName}", ThemeTypeVariation = "Heading" });
        column.AddChild(Hint($"{characterName} answers through your own AI provider: pick it, paste your key, name a model. " +
                             "Your key stays on this PC, encrypted for your Windows account."));

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

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        column.AddChild(buttons);

        _preset.ItemSelected += index => UsePreset(Provider.Presets[index], keepFields: false);
        testButton.Pressed += () => _ = Test();
        cancel.Pressed += QueueFree;
        save.Pressed += Save;

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

    public void Open() => Paper.Open(this, new Vector2(540, 400));

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
        Saved?.Invoke(Chosen());
        QueueFree();
    }

    private static void Row(GridContainer grid, string label, Control field)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(field);
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };

    private static void Show(Label label, string text, Color colour)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
    }
}
