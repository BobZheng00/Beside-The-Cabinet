using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Her mind: the player's own AI provider, which she answers through (server, key and model, with a test;
/// used once saved, so picking a provider to see its server never switches her over), or their ChatGPT plan (signed in
/// to in their browser; used once signed in), how she finds what to remember, and how much of the conversation she
/// keeps word for word. What she remembers is in the memory book (see <see cref="MemoryBook"/>).
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
    private Button _site = null!;
    private Label _siteRow = null!;                         // hidden with the button: a grid closes up over what is hidden
    private string _name = "";
    private Label _hint = null!, _now = null!, _result = null!;
    private HBoxContainer _buttons = null!;
    private VBoxContainer _plan = null!;                    // the ChatGPT plan's part, shown in place of server, key and model
    private Label? _planSaid;
    private OptionButton? _planModel;
    private IReadOnlyList<(string Slug, string Name)> _models = [];
    private CancellationTokenSource? _signingIn;
    private Protocol _protocol;
    private Provider? _saved;
    private bool _unsaved;                                  // the fields changed since she last took them up

    /// <param name="brief">Only the provider (the first run): how she remembers can wait.</param>
    public void Build(Chat chat, Provider? current, string name, string? search, int? window, bool brief = false)
    {
        _chat = chat;
        _saved = current;
        _name = name;
        Form.Page(this, $"{name}'s mind");
        _hint = Form.Hint(this, $"{name} answers through your own AI provider: pick it, get a key on its site and paste it, name a model, then Test and Save. " +
                                "Your key stays on this PC, encrypted for your Windows account.");
        _now = Form.Result(this);
        ShowNow();
        chat.Plan.Changed += PlanChanged;

        var grid = Form.Grid(this);
        _preset = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var preset in Provider.Presets)
            _preset.AddItem(preset.Name);
        Form.Row(grid, "Provider", _preset);
        _url = Form.Field(grid, "Server", null, "https://…/v1", typed: _ => Unsaved());
        _key = Form.Field(grid, "API key", null, "", secret: true, typed: _ => Unsaved());
        _site = new Button { ThemeTypeVariation = "Quiet", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _site.Pressed += () => OS.ShellOpen(Provider.Presets[_preset.Selected].Site);
        _siteRow = Form.Row(grid, "", _site);
        _model = Form.Field(grid, "Model", null, "", typed: _ => Unsaved());

        _plan = new VBoxContainer();
        _plan.AddThemeConstantOverride("separation", 10);
        AddChild(_plan);

        _buttons = Form.Buttons(this);
        Form.Button(_buttons, "Test", () => _ = Test());
        Form.Button(_buttons, "Save", Save, quiet: false);
        _result = Form.Result(_buttons);

        // how she finds what to remember: a local model (by meaning, any language it knows) or shared words only
        if (!brief)
            Remembering(chat, search, window);

        _preset.ItemSelected += index =>
        {
            UsePreset(Provider.Presets[index], keepFields: false);
            Unsaved();
        };
        var match = current is null ? 0
            : Array.FindIndex(Provider.Presets, p => p.Protocol == current.Protocol && (p.BaseUrl == current.BaseUrl || p.Protocol == Protocol.ChatGpt));
        _preset.Select(match >= 0 ? match : Provider.Presets.Length - 1);
        UsePreset(Provider.Presets[_preset.Selected], keepFields: current is not null);
        if (current is not null && current.Protocol != Protocol.ChatGpt)
        {
            _protocol = current.Protocol;
            _url.Text = current.BaseUrl;
            _model.Text = current.Model;
        }
    }

    /// <summary>How she finds what to remember, and how much of the conversation she keeps word for word.</summary>
    private void Remembering(Chat chat, string? search, int? window)
    {
        AddChild(new HSeparator());
        var memory = Form.Grid(this);
        var searches = Chat.Searchable().Select(m => (m.Name, m.Label)).Append((Name: Chat.WordsOnly, Label: "Shared words only (no model, least memory)")).ToList();
        Form.Choice(memory, "Memory search", searches.Select(s => s.Label), Math.Max(0, searches.FindIndex(s => s.Name == search)),
                    index => SearchChosen?.Invoke(searches[index].Name), wide: true);
        Form.Choice(memory, "Word for word", Windows.Select(w => w.Label), Math.Max(0, Array.FindIndex(Windows, w => w.Tokens == window)),
                    index => WindowChosen?.Invoke(Windows[index].Tokens), wide: true);
        var cached = chat.Cached is { } c ? $" The last {c.Replies} {(c.Replies == 1 ? "reply" : "replies")}: {c.Share:P0} of what was read came from the provider's cache." : "";
        Form.Hint(this, $"How {_name} finds what to remember: by meaning with a model that runs on this PC, or by shared words. " +
                        "The multilingual model understands the most languages; the small one uses less memory. " +
                        $"Word for word: the newest part of your conversation, which {_name} reads in full every time; older talk is summarised " +
                        "while you are away. Providers cache it, so within a conversation reading it again costs about a tenth." + cached);
    }

    /// <summary>The line at the top: which provider she answers through now, or that she can't talk yet.</summary>
    private void ShowNow()
    {
        if (OnPlan && _chat.Plan.Stopped is { } why)
            Form.Say(_now, $"{_name} waits: " + why switch
            {
                PlanStop.Limit => "your ChatGPT plan's usage limit is reached.",
                PlanStop.NotEligible => "this ChatGPT account can't share its plan with apps.",
                _ => "signed out of ChatGPT.",
            }, Paper.Bad);
        else if (_saved is { } provider)
            Form.Say(_now, $"{_name} answers through {provider.Through}, with {provider.Model}.", Paper.Good);
        else if (_protocol == Protocol.ChatGpt)
            Form.Say(_now, $"Not connected: {_name} can't talk with you until you continue with ChatGPT.", Paper.Bad);
        else
            Form.Say(_now, $"Not connected: {_name} can't talk with you until this is filled in and saved.", Paper.Bad);
    }

    private bool _acting;                                   // signing in or disconnecting here: this page says how it went

    /// <summary>The plan stopped, or works again, while the page shows: shown as it now stands (not what this page itself just did).</summary>
    private void PlanChanged()
    {
        if (_acting)
            return;
        Callable.From(() =>
        {
            if (!IsInstanceValid(this))
            {
                _chat.Plan.Changed -= PlanChanged;
                return;
            }
            ShowNow();
            if (_plan.Visible)
                ShowPlan();
        }).CallDeferred();
    }

    private void Unsaved()
    {
        _unsaved = true;
        Form.Say(_result, "Not saved yet.", Paper.Faint);
    }

    private void UsePreset(Preset preset, bool keepFields)
    {
        _protocol = preset.Protocol;
        var plan = preset.Protocol == Protocol.ChatGpt;
        if (!keepFields && !plan)
        {
            _url.Text = preset.BaseUrl;
            _model.Text = preset.ModelHint;
        }
        _model.PlaceholderText = preset.ModelHint.Length > 0 ? preset.ModelHint : "the model's name at this provider";
        _key.PlaceholderText = _chat.Keys.Has ? "saved (leave empty to keep it)" : preset.NeedsKey ? "paste your key" : "not needed";
        var named = preset.Name.Split(" (")[0];
        _site.Text = preset.NeedsKey ? $"Get a key from {named}…" : $"Get {named}…";
        _site.Visible = _siteRow.Visible = preset.Site.Length > 0;
        _result.Text = "";
        // the plan has no server, key or model to type, and nothing to test or save: it is signed in to
        foreach (var field in new Control[] { _url, _key, _model })
            field.Visible = ((Control)field.GetParent().GetChild(field.GetIndex() - 1)).Visible = !plan;
        _hint.Visible = _buttons.Visible = !plan;
        _plan.Visible = plan;
        ShowNow();
        if (plan)
            ShowPlan();
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
            var connected = $"Connected. The model says: {reply.Text.Trim()}" + (_unsaved ? " Save to use it." : "");
            if (!IsInstanceValid(this))
                return;
            Form.Say(_result, connected + " Now trying its web search…", Paper.Good);
            // whether she can search the web without the add-on, so the player knows before she has to say she can't
            using var searching = new CancellationTokenSource(TimeSpan.FromSeconds(90));       // the player's provider took 47 s to search
            var searches = await provider.SearchesItself(_key.Text.Trim().Length > 0 ? _key.Text.Trim() : _chat.Keys.Load(), searching.Token);
            if (IsInstanceValid(this))
                Form.Say(_result, connected + " " + (searches, provider.Protocol) switch
                {
                    (true, Protocol.Anthropic) => $"Web search: {_name} can use your provider's own.",
                    (true, _) => "Web search: your provider has one, but only through OpenAI's newer Responses API, not used here yet. " +
                                 "Until it is, the SearXNG add-on (Add-ons) gives web search.",
                    _ => "Web search: your provider has none for this model. The SearXNG add-on (Add-ons) gives web search.",
                }, Paper.Good);
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

    // ---- The ChatGPT plan ---------------------------------------------------------------------------------------------------

    private bool OnPlan => _saved?.Protocol == Protocol.ChatGpt;

    /// <summary>
    /// The plan's part, as things stand: Continue with ChatGPT until signed in; then which account, the model from its
    /// list, "Using ChatGPT plan" with Manage usage, and Disconnect (OpenAI's guidelines for apps on a player's plan).
    /// </summary>
    private void ShowPlan()
    {
        foreach (var child in _plan.GetChildren())
        {
            _plan.RemoveChild(child);
            child.QueueFree();
        }
        var plan = _chat.Plan;
        if (!plan.Connected)
        {
            _planModel = null;
            Form.Hint(_plan, $"Use your ChatGPT plan: {_name}'s replies, check-ins, reminders and memory run on the usage included in your ChatGPT plan " +
                             "(Plus or Pro), within the limit you set for this app in ChatGPT's settings. Nothing is charged here. " +
                             "Continue with ChatGPT opens your browser to sign in.");
            var signIn = Form.Buttons(_plan);
            Form.Button(signIn, "Continue with ChatGPT", () => _ = ContinueWithChatGpt(), quiet: false);
            _planSaid = Form.Result(signIn);
            if (OnPlan)
                Form.Say(_planSaid, $"Signed out of ChatGPT: sign in again, and {_name} uses your plan again.", Paper.Bad);
            return;
        }
        Form.Say(Form.Result(_plan), $"Connected as {plan.Email ?? "your ChatGPT account"}", Paper.Good);
        var grid = Form.Grid(_plan);
        _planModel = Form.Choice(grid, "Model", [_models.FirstOrDefault(m => m.Slug == _saved?.Model).Name ?? _saved?.Model ?? "…"], 0,
                                 index =>
                                 {
                                     if (OnPlan)                    // else kept for "Use your ChatGPT plan"
                                         Use(_models.ElementAtOrDefault(index).Slug);
                                 }, wide: true);
        var row = Form.Buttons(_plan);
        row.AddChild(new Label { Text = OnPlan ? "Using ChatGPT plan" : "Not in use yet", SizeFlagsVertical = SizeFlags.ShrinkCenter });
        Form.Button(row, "Manage usage", () => Chat.OpenPage(new Uri(ChatGptPlan.ManageUsage)));
        if (!OnPlan)
            Form.Button(row, "Use your ChatGPT plan", () => Use(_models.ElementAtOrDefault(_planModel?.Selected ?? 0).Slug), quiet: false);
        Form.Button(row, "Disconnect", () => _ = Disconnect());
        _planSaid = Form.Result(_plan);
        if (plan.Stopped is { } why)
            Say(ChatGptPlan.Plainly(why), Paper.Bad);
        if (_models.Count > 0)
            ShowModels();
        else
            _ = ListModels();
    }

    /// <summary>Says how the plan's part went, on its line, if the page is still there.</summary>
    private void Say(string text, Color colour)
    {
        if (IsInstanceValid(_planSaid))
            Form.Say(_planSaid!, text, colour);
    }

    /// <summary>Asks for the account's models, then shows them.</summary>
    private async Task ListModels()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _models = await _chat.Plan.Models(timeout.Token);
            if (IsInstanceValid(this))
                ShowModels();
        }
        catch (Exception e)
        {
            Say(e is PlanStopped ? e.Message : "ChatGPT's list of models didn't come. Try again later.", Paper.Bad);
        }
    }

    /// <summary>The account's models, in the choice: the one she uses chosen, or, when it is no longer offered, the first.</summary>
    private void ShowModels()
    {
        if (!IsInstanceValid(_planModel))
            return;
        _planModel!.Clear();
        foreach (var (_, name) in _models)
            _planModel.AddItem(name);
        if (_models.Count == 0)
        {
            Say("Your ChatGPT account offers no models to apps yet.", Paper.Bad);
            return;
        }
        var at = Math.Max(0, _models.ToList().FindIndex(m => m.Slug == _saved?.Model));
        _planModel.Select(at);
        if (OnPlan && _models[at].Slug != _saved?.Model)
            Use(_models[at].Slug);
    }

    /// <summary>
    /// Continue with ChatGPT: the player signs in in their browser and allows plan usage; then she uses the plan, with
    /// the first model their account offers. A test run opens no browser: the stand-in's sign-in is visited directly.
    /// </summary>
    public async Task ContinueWithChatGpt()
    {
        _signingIn?.Cancel();
        using var signing = _signingIn = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        Say("Sign in in your browser and allow plan usage, then come back here.", Paper.Faint);
        try
        {
            _acting = true;
            try
            {
                await _chat.Plan.SignIn(Browse, signing.Token);
            }
            finally
            {
                _acting = false;
            }
            var models = await _chat.Plan.Models(signing.Token);
            GD.Print($"ChatGPT: signed in, plan usage allowed; {models.Count} models offered");     // never the email or a token
            _models = models;
            if (models.Count > 0)
                Use(OnPlan && models.Any(m => m.Slug == _saved!.Model) ? _saved!.Model : models[0].Slug);
            if (!IsInstanceValid(this))
                return;
            ShowPlan();
            Say(models.Count == 0 ? "Signed in, but your ChatGPT account offers no models to apps yet." : $"Signed in: {_name} uses your ChatGPT plan now.",
                models.Count == 0 ? Paper.Bad : Paper.Good);
        }
        catch (OperationCanceledException) when (signing != _signingIn)
        {
            // Continue with ChatGPT pressed again: that sign-in goes on instead
        }
        catch (Exception e)
        {
            GD.Print($"ChatGPT: sign-in didn't finish ({e.GetType().Name})");
            Say(e switch
                {
                    PlanSignInFailed or PlanStopped => e.Message,
                    OperationCanceledException => "Sign-in took too long. Continue with ChatGPT to try again.",
                    _ => "ChatGPT's sign-in couldn't be reached. Check your connection and try again.",
                }, Paper.Bad);
        }
        finally
        {
            if (signing == _signingIn)
                _signingIn = null;
        }
    }

    /// <summary>Opens the sign-in in the player's browser (they asked, by pressing Continue with ChatGPT). A test run never does.</summary>
    private static Task Browse(Uri page)
    {
        if (!Paper.OffScreen)
        {
            OS.ShellOpen(page.AbsoluteUri);
            return Task.CompletedTask;
        }
        GD.Print(page.IsLoopback ? "ChatGPT: a test run opens no browser; the stand-in's sign-in is visited directly" : "ChatGPT: a test run opens no browser");
        if (page.IsLoopback)
            _ = Visit(page);
        return Task.CompletedTask;

        // the stand-in answers with a redirect to the app's listener, followed as a browser would
        static async Task Visit(Uri page)
        {
            using var browser = new System.Net.Http.HttpClient();
            try
            {
                using var _ = await browser.GetAsync(page);
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                GD.PrintErr($"ChatGPT: the stand-in's sign-in couldn't be visited: {e.Message}");
            }
        }
    }

    /// <summary>She uses the plan from now on, with this model.</summary>
    private void Use(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || (OnPlan && slug == _saved!.Model))
            return;
        var first = !OnPlan;
        _saved = new Provider(Protocol.ChatGpt, ChatGptPlan.Api, slug);
        _unsaved = false;
        Saved?.Invoke(_saved);
        if (!IsInstanceValid(this))
            return;
        ShowNow();
        if (first)
            ShowPlan();                                     // "Using ChatGPT plan" now
    }

    /// <summary>Disconnect: the sign-in forgotten here and ended at ChatGPT; she has no mind until another is chosen.</summary>
    private async Task Disconnect()
    {
        _acting = true;
        bool told;
        try
        {
            told = await _chat.Plan.Disconnect();
        }
        finally
        {
            _acting = false;
        }
        GD.Print($"ChatGPT: disconnected{(told ? "" : "; ChatGPT couldn't be told")}");
        if (OnPlan)
        {
            _saved = null;
            Saved?.Invoke(null);
        }
        if (!IsInstanceValid(this))
            return;
        ShowNow();
        ShowPlan();
        Say(told ?"Disconnected: signed out of ChatGPT here and there."
                                  : "Signed out here, but ChatGPT couldn't be told: disconnect the app in ChatGPT's settings too.", told ? Paper.Good : Paper.Bad);
    }
}
