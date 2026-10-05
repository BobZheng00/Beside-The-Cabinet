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
    /// <summary>
    /// The provider chosen (null: none), the memory search (see <see cref="Chat.UseSearch"/>) and the talk key (see
    /// <see cref="TalkKey"/>) after Save; the API key is already stored by then.
    /// </summary>
    public event Action<Provider?, string, string, int?>? Saved;

    /// <summary>The window sizes offered: tokens (null: what suits the provider) and how each is described.</summary>
    private static readonly (int? Tokens, string Label)[] Windows =
    [
        (null, "Automatic: 32k tokens, or 8k with a server on this PC"),
        (8_000, "8k tokens: least to send, more is summarised"),
        (32_000, "32k tokens: hours of talk, word for word"),
        (100_000, "100k tokens: for long-context models; costs more after a break"),
    ];

    private Chat _chat = null!;
    private UsagePage _usage = null!;
    private TabContainer _tabs = null!;
    private OptionButton _preset = null!, _search = null!, _talkKey = null!, _window = null!;
    private readonly List<string> _searches = [];
    private LineEdit _url = null!, _key = null!, _model = null!;
    private Label _result = null!;
    private Protocol _protocol;

    /// <param name="hears">This build has her ears (the speech model), so she can be talked to aloud.</param>
    /// <param name="window">Tokens of the conversation she keeps word for word, or null for what suits the provider.</param>
    /// <param name="prices">What the player's provider charges, per model, as they entered it (see the Usage tab).</param>
    public void Build(Chat chat, Provider? current, string characterName, string? search, string talkKey, bool hears, int? window,
                      IReadOnlyDictionary<string, Prices> prices, string currency)
    {
        _chat = chat;
        Title = $"Talk with {characterName}";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        page.AddChild(outer);
        var tabs = _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(tabs);
        var column = new VBoxContainer { Name = "Connection" };
        column.AddThemeConstantOverride("separation", 10);
        tabs.AddChild(column);
        var usage = new VBoxContainer { Name = "Usage" };
        usage.AddThemeConstantOverride("separation", 8);
        tabs.AddChild(usage);
        _usage = new UsagePage(usage, chat.Store, current?.Model, prices, currency);

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

        // how she finds what to remember: a local model (by meaning, any language it knows) or shared words only
        column.AddChild(new HSeparator());
        var memory = new GridContainer { Columns = 2 };
        memory.AddThemeConstantOverride("h_separation", 12);
        _search = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var model in Chat.Searchable())
        {
            _search.AddItem(model.Label);
            _searches.Add(model.Name);
        }
        _search.AddItem("Shared words only (no model, least memory)");
        _searches.Add(Chat.WordsOnly);
        var chosen = _searches.IndexOf(search ?? "");
        _search.Select(chosen >= 0 ? chosen : 0);
        Row(memory, "Memory search", _search);
        _window = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
        foreach (var (_, label) in Windows)
            _window.AddItem(label);
        _window.Select(Math.Max(0, Array.FindIndex(Windows, w => w.Tokens == window)));
        Row(memory, "Word for word", _window);
        column.AddChild(memory);
        var cached = chat.Cached is { } c ? $" Her last {c.Replies} {(c.Replies == 1 ? "reply" : "replies")}: {c.Share:P0} of what she read came from the provider's cache." : "";
        column.AddChild(Hint("How she finds what to remember: by meaning with a model that runs on this PC, or by shared words. " +
                             "The multilingual model understands the most languages; the small one uses less memory. " +
                             "Word for word: the newest part of your conversation, which she reads in full every time; older talk is summarised " +
                             "while you are away. Providers cache it, so within a conversation reading it again costs about a tenth." + cached));

        // talking to her aloud
        column.AddChild(new HSeparator());
        var talk = new GridContainer { Columns = 2 };
        talk.AddThemeConstantOverride("h_separation", 12);
        _talkKey = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = !hears };
        foreach (var (_, label, _) in TalkKey.Keys)
            _talkKey.AddItem(label);
        _talkKey.Select(Math.Max(0, Array.FindIndex(TalkKey.Keys, k => k.Name == talkKey)));
        Row(talk, "Talk key", _talkKey);
        column.AddChild(talk);
        column.AddChild(Hint(hears
            ? $"Talk to {characterName} aloud: hold the mouse on her, or hold this key in any app, and let go when you're done; she " +
              "answers what she heard. The microphone on the slip writes what you say into it instead, to change before sending. " +
              "Heard on this PC; nothing is recorded."
            : "Talking aloud needs the speech model, which this build doesn't have (python tools/models/fetch.py listening)."));

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        outer.AddChild(buttons);

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

    public void Open() => Paper.Open(this, new Vector2(560, 720));

    /// <summary>Dev: shows the Usage tab.</summary>
    public void DevUsage() => _tabs.CurrentTab = 1;

    /// <summary>The prices entered on the Usage tab, after Save: for this model (null: none), and the currency they are in.</summary>
    public event Action<string, Prices?, string>? PricesSaved;

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
        Saved?.Invoke(Chosen(), _searches[_search.Selected], TalkKey.Keys[_talkKey.Selected].Name, Windows[_window.Selected].Tokens);
        if (_model.Text.Trim() is { Length: > 0 } model)
            PricesSaved?.Invoke(model, _usage.Prices, _usage.Currency);
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
