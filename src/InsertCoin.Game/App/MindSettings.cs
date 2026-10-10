using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Her mind: the player's own AI provider, which she answers through (server, key and model, with a test;
/// used once saved, so picking a provider to see its server never switches her over), how she finds what to remember,
/// and how much of the conversation she keeps word for word. What she remembers is in the memory book (see
/// <see cref="MemoryBook"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public partial class MindSettings : VBoxContainer
{
    /// <summary>Save: the provider as filled in (null: none). A key typed in is already stored by then.</summary>
    public event Action<Provider?>? Saved;
    /// <summary>The memory search chosen (see <see cref="Chat.UseSearch"/>).</summary>
    public event Action<string>? SearchChosen;
    /// <summary>How much of the conversation she keeps word for word, in tokens (null: what suits the provider).</summary>
    public event Action<int?>? WindowChosen;

    /// <summary>The window sizes offered: tokens (null: what suits the provider) and how each is described.</summary>
    private static readonly (int? Tokens, string Label)[] Windows =
    [
        (null, "Automatic: 32k tokens, or 8k with a server on this PC"),
        (8_000, "8k tokens: least to send, more is summarised"),
        (32_000, "32k tokens: hours of talk, word for word"),
        (100_000, "100k tokens: for long-context models; costs more after a break"),
    ];

    private Chat _chat = null!;
    private OptionButton _preset = null!;
    private LineEdit _url = null!, _key = null!, _model = null!;
    private Label _now = null!, _result = null!;
    private Protocol _protocol;
    private Provider? _saved;
    private bool _unsaved;                                  // the fields changed since she last took them up

    public void Build(Chat chat, Provider? current, string name, string? search, int? window)
    {
        _chat = chat;
        _saved = current;
        Form.Page(this, "Her mind");
        Form.Hint(this, $"{name} answers through your own AI provider: pick it, paste your key, name a model, then Test and Save. " +
                        "Your key stays on this PC, encrypted for your Windows account.");
        _now = Form.Result(this);
        ShowNow();

        var grid = Form.Grid(this);
        _preset = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var preset in Provider.Presets)
            _preset.AddItem(preset.Name);
        Form.Row(grid, "Provider", _preset);
        _url = Form.Field(grid, "Server", null, "https://…/v1", typed: _ => Unsaved());
        _key = Form.Field(grid, "API key", null, "", secret: true, typed: _ => Unsaved());
        _model = Form.Field(grid, "Model", null, "", typed: _ => Unsaved());

        var buttons = Form.Buttons(this);
        Form.Button(buttons, "Test", () => _ = Test());
        Form.Button(buttons, "Save", Save, quiet: false);
        _result = Form.Result(buttons);

        // how she finds what to remember: a local model (by meaning, any language it knows) or shared words only
        AddChild(new HSeparator());
        var memory = Form.Grid(this);
        var searches = Chat.Searchable().Select(m => (m.Name, m.Label)).Append((Name: Chat.WordsOnly, Label: "Shared words only (no model, least memory)")).ToList();
        Form.Choice(memory, "Memory search", searches.Select(s => s.Label), Math.Max(0, searches.FindIndex(s => s.Name == search)),
                    index => SearchChosen?.Invoke(searches[index].Name), wide: true);
        Form.Choice(memory, "Word for word", Windows.Select(w => w.Label), Math.Max(0, Array.FindIndex(Windows, w => w.Tokens == window)),
                    index => WindowChosen?.Invoke(Windows[index].Tokens), wide: true);
        var cached = chat.Cached is { } c ? $" Her last {c.Replies} {(c.Replies == 1 ? "reply" : "replies")}: {c.Share:P0} of what she read came from the provider's cache." : "";
        Form.Hint(this, "How she finds what to remember: by meaning with a model that runs on this PC, or by shared words. " +
                        "The multilingual model understands the most languages; the small one uses less memory. " +
                        "Word for word: the newest part of your conversation, which she reads in full every time; older talk is summarised " +
                        "while you are away. Providers cache it, so within a conversation reading it again costs about a tenth." + cached);

        _preset.ItemSelected += index =>
        {
            UsePreset(Provider.Presets[index], keepFields: false);
            Unsaved();
        };
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

    /// <summary>The line at the top: which provider she answers through now, or that she can't talk yet.</summary>
    private void ShowNow()
    {
        if (_saved is { } provider)
            Form.Say(_now, $"She answers through {(Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var server) ? server.Host : provider.BaseUrl)}, with {provider.Model}.", Paper.Good);
        else
            Form.Say(_now, "Not connected: she can't talk with you until this is filled in and saved.", Paper.Bad);
    }

    private void Unsaved()
    {
        _unsaved = true;
        Form.Say(_result, "Not saved yet.", Paper.Faint);
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
            Form.Say(_result, "Fill in the server and the model first.", Paper.Bad);
            return;
        }
        Form.Say(_result, "Asking…", Paper.Faint);
        try
        {
            using var client = provider.Connect(_key.Text.Trim().Length > 0 ? _key.Text.Trim() : _chat.Keys.Load());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var reply = await client.GetResponseAsync("Say hello in at most five words.", new ChatOptions { MaxOutputTokens = 300 }, timeout.Token);
            if (IsInstanceValid(this))
                Form.Say(_result, $"Connected. The model says: {reply.Text.Trim()}" + (_unsaved ? " Save to use it." : ""), Paper.Good);
        }
        catch (Exception e)
        {
            if (IsInstanceValid(this))
                Form.Say(_result, Trouble.Explain(e), Paper.Bad);
        }
    }

    private void Save()
    {
        if (_key.Text.Trim().Length > 0)
        {
            _chat.Keys.Save(_key.Text);
            _key.Text = "";
            _key.PlaceholderText = "saved (leave empty to keep it)";
        }
        _saved = Chosen();
        _unsaved = false;
        Saved?.Invoke(_saved);
        ShowNow();
        Form.Say(_result, _saved is null ? "Saved, without a provider." : "Saved.", _saved is null ? Paper.Bad : Paper.Good);
    }
}
