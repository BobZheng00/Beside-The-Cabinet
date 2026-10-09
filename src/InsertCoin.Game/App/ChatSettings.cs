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
    /// After Save: the provider chosen (null: none), the memory search (see <see cref="Chat.UseSearch"/>), the talk key
    /// (see <see cref="TalkKey"/>), when she listens without it (see <see cref="HandsFree"/>), the window, the
    /// permission for each of her tools (see <see cref="Toolbox.Chosen"/>), whether she asks at all (see
    /// <see cref="Toolbox.Auto"/>), what she is told in the background (<see cref="Privacy"/>) and the add-ons; the API
    /// key is already stored by then.
    /// </summary>
    public event Action<Provider?, string, string, HandsFree, int?, IReadOnlyDictionary<string, Permission>, bool, Privacy, AddOns>? Saved;

    /// <summary>The permissions offered for a tool, as the Abilities tab names them.</summary>
    private static readonly (Permission Permission, string Label)[] Permissions =
        [(Permission.Allowed, "Allowed"), (Permission.Ask, "Ask me first"), (Permission.Off, "Off")];

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
    private CheckBox _auto = null!;
    private CheckBox _conversationKey = null!, _followUp = null!, _afterAnswers = null!, _afterQuestions = null!, _afterRemarks = null!,
        _inCalls = null!, _inFullScreen = null!, _whilePlaying = null!;
    private SpinBox _followUpSeconds = null!;
    private HandsFree _handsFree = new();
    private readonly List<(Tool Tool, OptionButton Choice)> _abilities = [];         // on the Abilities tab and the Privacy tab alike
    private CheckBox _music = null!, _days = null!, _searxng = null!;
    private LineEdit _searxngAddress = null!;
    private Label _searxngResult = null!;
    private Privacy _privacy = new();
    private AddOns _addOns = new();
    private readonly Dictionary<string, Permission> _always = [];      // "tool:site" the player answered Always for
    private Label _result = null!;
    private Protocol _protocol;

    /// <param name="hears">This build has her ears (the speech model), so she can be talked to aloud.</param>
    /// <param name="turns">…and the turn-taking models, so she can listen hands-free.</param>
    /// <param name="window">Tokens of the conversation she keeps word for word, or null for what suits the provider.</param>
    /// <param name="prices">What the player's provider charges, per model, as they entered it (see the Usage tab).</param>
    public void Build(Chat chat, Provider? current, string characterName, string? search, string talkKey, HandsFree handsFree, bool hears, bool turns,
                      int? window, IReadOnlyDictionary<string, Prices> prices, string currency, Privacy privacy, AddOns addOns)
    {
        _chat = chat;
        _handsFree = handsFree;
        _privacy = privacy;
        _addOns = addOns;
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
        var listeningTab = new ScrollContainer { Name = "Listening", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(listeningTab);
        var listening = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        listening.AddThemeConstantOverride("separation", 10);
        listeningTab.AddChild(listening);
        var abilitiesTab = new ScrollContainer { Name = "Abilities", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(abilitiesTab);
        var abilities = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        abilities.AddThemeConstantOverride("separation", 10);
        abilitiesTab.AddChild(abilities);
        var privacyTab = new ScrollContainer { Name = "Privacy", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(privacyTab);
        var privacyPage = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        privacyPage.AddThemeConstantOverride("separation", 10);
        privacyTab.AddChild(privacyPage);
        var addOnsTab = new ScrollContainer { Name = "Add-ons", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(addOnsTab);
        var addOnsPage = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        addOnsPage.AddThemeConstantOverride("separation", 10);
        addOnsTab.AddChild(addOnsPage);
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

        BuildListening(listening, characterName, talkKey, hears, turns);
        BuildAbilities(abilities, characterName);
        BuildPrivacy(privacyPage, characterName, current);
        BuildAddOns(addOnsPage, characterName, current);

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

    /// <summary>The Listening tab: the talk key, and when she listens without it.</summary>
    private void BuildListening(VBoxContainer page, string name, string talkKey, bool hears, bool turns)
    {
        page.AddChild(new Label { Text = "Talking aloud", ThemeTypeVariation = "Heading" });
        var talk = new GridContainer { Columns = 2 };
        talk.AddThemeConstantOverride("h_separation", 12);
        _talkKey = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = !hears, ClipText = true };
        foreach (var (_, label, _) in TalkKey.Keys)
            _talkKey.AddItem(label);
        _talkKey.Select(Math.Max(0, Array.FindIndex(TalkKey.Keys, k => k.Name == talkKey)));
        Row(talk, "Talk key", _talkKey);
        page.AddChild(talk);
        page.AddChild(Hint(hears
            ? $"Talk to {name} aloud: hold the mouse on her, or hold this key in any app, and let go when you're done; she " +
              "answers what she heard. The microphone on the slip writes what you say into it instead, to change before sending. " +
              "Heard on this PC; nothing is recorded. The cabinet's screen shows MIC while the microphone is open."
            : "Talking aloud needs the speech model, which this build doesn't have (python tools/models/fetch.py listening)."));

        page.AddChild(new HSeparator());
        page.AddChild(new Label { Text = "Without the key", ThemeTypeVariation = "Heading" });
        var h = _handsFree;
        _conversationKey = Tick(page, "Double-tap the talk key to start or end a conversation", h.ConversationKey);
        page.AddChild(Hint($"In a conversation {name} listens after everything she says, so you can answer without the key, until you've " +
                           $"been quiet for {HandsFree.ConversationSeconds:0} seconds. The tray's Conversation switch does the same."));
        _followUp = Tick(page, "Listen for a moment after she speaks", h.FollowUp);
        var seconds = Indented(page);
        seconds.AddChild(new Label { Text = "for" });
        _followUpSeconds = new SpinBox
        {
            MinValue = HandsFree.Shortest, MaxValue = HandsFree.Longest, Step = 1, Value = h.FollowUpSeconds, Suffix = "seconds",
            CustomMinimumSize = new Vector2(140, 0),
        };
        seconds.AddChild(_followUpSeconds);
        _afterAnswers = Tick(Indented(page), "after she answers you", h.AfterAnswers);
        _afterQuestions = Tick(Indented(page), "after she asks you something on her own", h.AfterQuestions);
        _afterRemarks = Tick(Indented(page), "after anything else she says on her own", h.AfterRemarks);
        page.AddChild(Hint("If you start talking within those seconds, she hears it as your answer; if not, the microphone closes. " +
                           "She never listens while she is speaking: talking over her still needs the key or a click."));

        page.AddChild(new Label { Text = "She may listen on her own…" });
        _inCalls = Tick(Indented(page), "during a call", h.InCalls);
        _inFullScreen = Tick(Indented(page), "in full screen or Windows' quiet time", h.InFullScreen);
        _whilePlaying = Tick(Indented(page), "while a game runs", h.WhilePlaying);

        void Enable()
        {
            foreach (var box in new[] { _conversationKey, _followUp, _inCalls, _inFullScreen, _whilePlaying })
                box.Disabled = !turns || !hears;
            foreach (var box in new[] { _afterAnswers, _afterQuestions, _afterRemarks })
                box.Disabled = !turns || !hears || !_followUp.ButtonPressed;
            _followUpSeconds.Editable = turns && hears && _followUp.ButtonPressed;
        }
        _followUp.Toggled += _ => Enable();
        Enable();
        if (!turns)
            page.AddChild(Hint("Listening without the key needs the turn-taking models, which this build doesn't have " +
                               "(python tools/models/fetch.py turns)."));
    }

    /// <summary>The Abilities tab: what she can do beyond talking, and how freely, tool by tool.</summary>
    private void BuildAbilities(VBoxContainer page, string name)
    {
        var tools = _chat.Tools;
        page.AddChild(new Label { Text = "Abilities", ThemeTypeVariation = "Heading" });
        page.AddChild(Hint($"What {name} can do beyond talking. Allowed: she does it when it helps. Ask me first: she asks, and a card by " +
                           "her stool lets you say yes or no. Off: she doesn't have it. Whatever acts outside this app asks first unless you allow it."));
        _auto = Tick(page, "Auto: she goes ahead without asking", tools.Auto);
        page.AddChild(Hint($"Like an auto mode: where she would ask first, she just does it. She still says what she's doing, and what's " +
                           "switched off stays off. Every use is in the chat history. Two exceptions: what's on the Privacy tab, and the rest " +
                           "of a reply once she has read a page or search results, since a page could try to talk her into things."));
        page.AddChild(new Label { Text = "Built in", ThemeTypeVariation = "Subheading" });
        page.AddChild(Choices(tools.All.Where(t => t.Source == Tool.BuiltIn && !t.Private)));
        page.AddChild(Hint(_chat.WebSearch is { } searching
            ? $"She searches the web {searching}."
            : "She can't search the web: your provider has no search of its own that this app can use. The SearXNG add-on gives her one."));

        foreach (var (key, permission) in tools.Chosen.Where(c => c.Key.Contains(':')))
            _always[key] = permission;
        if (_always.Count > 0)
        {
            page.AddChild(new Label { Text = "Always allowed", ThemeTypeVariation = "Subheading" });
            foreach (var key in _always.Keys.Order().ToList())
            {
                var colon = key.IndexOf(':');
                var title = tools.All.FirstOrDefault(t => t.Name == key[..colon])?.Title ?? key[..colon];
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(new Label { Text = $"{title}: {key[(colon + 1)..]}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true });
                var forget = new Button { Text = "Forget", ThemeTypeVariation = "Quiet" };
                forget.Pressed += () =>
                {
                    _always.Remove(key);
                    row.QueueFree();
                };
                row.AddChild(forget);
                page.AddChild(row);
            }
            page.AddChild(Hint("Where you answered Always on her card. Forget one, and she asks again there."));
        }
        if (tools.Refused)
            page.AddChild(Hint("Your model turned tools down, so for now she answers without them. Choosing a model that takes tools brings them back."));
    }

    /// <summary>A permission to choose for each tool (the provider's own search only runs or doesn't: it can't ask).</summary>
    private GridContainer Choices(IEnumerable<Tool> listed)
    {
        var tools = _chat.Tools;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var tool in listed)
        {
            var offered = Permissions.Where(p => tool.Hosted is null || p.Permission != Permission.Ask).ToList();
            var choice = new OptionButton { CustomMinimumSize = new Vector2(160, 0) };
            foreach (var (_, label) in offered)
                choice.AddItem(label);
            choice.Select(Math.Max(0, offered.FindIndex(p => p.Permission == tools.PermissionOf(tool))));
            grid.AddChild(new Label { Text = tool.Title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tool.Function.Description,
                                      MouseFilter = Control.MouseFilterEnum.Pass });
            grid.AddChild(choice);
            _abilities.Add((tool, choice));
        }
        return grid;
    }

    /// <summary>The permission chosen for a tool on its row.</summary>
    private static Permission Chosen(Tool tool, OptionButton choice) =>
        Permissions.Where(p => tool.Hosted is null || p.Permission != Permission.Ask).ElementAt(choice.Selected).Permission;

    /// <summary>
    /// The Privacy tab: what she can look at that is the player's own (asked for, like her other abilities, but never
    /// skipped by Auto), and what she is told in the background. All of it goes to their provider with the conversation.
    /// </summary>
    private void BuildPrivacy(VBoxContainer page, string name, Provider? current)
    {
        var provider = current is not null && Uri.TryCreate(current.BaseUrl, UriKind.Absolute, out var server) ? server.Host : "your AI provider";
        page.AddChild(new Label { Text = "Privacy", ThemeTypeVariation = "Heading" });
        page.AddChild(Hint($"What {name} can see of what's yours. Whatever she looks at or is told goes to {provider} with your conversation, " +
                           "as everything you say to her does; nothing else leaves this PC."));
        page.AddChild(new Label { Text = "When you ask for help", ThemeTypeVariation = "Subheading" });
        page.AddChild(Choices(_chat.Tools.All.Where(t => t.Private)));
        page.AddChild(Hint("Auto never skips these: whatever is set to ask first here always asks."));
        page.AddChild(new Label { Text = "In the background", ThemeTypeVariation = "Subheading" });
        _music = Tick(page, "What's playing in your music app (track and artist)", _privacy.Music);
        _days = Tick(page, "What your record of days shows: nights running late, days without play", _privacy.Days);
        page.AddChild(Hint("Your play time, focus and what you've done today she always knows: the cabinet runs on them. Web searches go to your " +
                           "provider, or to your own SearXNG (Add-ons); pages she reads are fetched by this PC from their site."));
    }

    /// <summary>
    /// The Add-ons tab: optional extras, off until the player turns them on, each saying what it needs and where their
    /// data goes. The first: web search through their own SearXNG.
    /// </summary>
    private void BuildAddOns(VBoxContainer page, string name, Provider? current)
    {
        page.AddChild(new Label { Text = "Add-ons", ThemeTypeVariation = "Heading" });
        page.AddChild(Hint("Extras that are off until you turn them on. Each says what it needs and where what you give it goes."));
        page.AddChild(new Label { Text = "Web search with SearXNG", ThemeTypeVariation = "Subheading" });
        _searxng = Tick(page, "Search the web through my SearXNG", _addOns.SearXng);
        var row = Indented(page);
        row.AddChild(new Label { Text = "Address" });
        _searxngAddress = new LineEdit { Text = _addOns.SearXngAddress, PlaceholderText = "http://localhost:8888", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(_searxngAddress);
        var test = new Button { Text = "Test", ThemeTypeVariation = "Quiet" };
        row.AddChild(test);
        _searxngResult = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0), Visible = false };
        page.AddChild(_searxngResult);
        var own = current?.Protocol == Protocol.Anthropic ? "your provider's own search" : "no search at all (your provider has none this app can use)";
        page.AddChild(Hint($"SearXNG is a free, open-source search engine you run yourself, on this PC or one you trust: it asks the big search " +
                           $"engines for you, and nothing goes through anyone else. With it on, {name} searches through it; without it, {own}. " +
                           "To run it: Docker Desktop, then docker run -d -p 8888:8080 searxng/searxng, and in its settings.yml add json under " +
                           "search: formats: (and restart it)."));
        test.Pressed += async () =>
        {
            if (SearXng.Parse(_searxngAddress.Text) is not { } address)
            {
                Show(_searxngResult, "That isn't an address (like http://localhost:8888).", Paper.Bad);
                return;
            }
            Show(_searxngResult, "Searching…", Paper.Faint);
            try
            {
                var said = await Chat.TrySearXng(address);
                if (IsInstanceValid(this))
                    Show(_searxngResult, said, said.StartsWith("Works") ? Paper.Good : Paper.Bad);
            }
            catch (Exception e)
            {
                if (IsInstanceValid(this))
                    Show(_searxngResult, e.Message, Paper.Bad);
            }
        };
    }

    /// <summary>The permissions as chosen: only what differs from a tool's standard, the sites still allowed always, and choices for tools not listed here kept.</summary>
    private Dictionary<string, Permission> PermissionsChosen()
    {
        var chosen = new Dictionary<string, Permission>(_always);
        foreach (var (key, permission) in _chat.Tools.Chosen.Where(c => !c.Key.Contains(':') && _abilities.All(a => a.Tool.Name != c.Key)))
            chosen[key] = permission;
        foreach (var (tool, choice) in _abilities)
            if (Chosen(tool, choice) is var permission && permission != tool.Standard)
                chosen[tool.Name] = permission;
        return chosen;
    }

    private static CheckBox Tick(Container parent, string text, bool on)
    {
        var box = new CheckBox
        {
            Text = text, ButtonPressed = on, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0),
        };
        parent.AddChild(box);
        return box;
    }

    /// <summary>A row set in under the line above it.</summary>
    private static HBoxContainer Indented(VBoxContainer page)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(22, 0) });
        page.AddChild(row);
        return row;
    }

    /// <summary>When she listens without the key, as ticked.</summary>
    private HandsFree HandsFreeChosen() => _handsFree with
    {
        ConversationKey = _conversationKey.ButtonPressed, FollowUp = _followUp.ButtonPressed, FollowUpSeconds = _followUpSeconds.Value,
        AfterAnswers = _afterAnswers.ButtonPressed, AfterQuestions = _afterQuestions.ButtonPressed, AfterRemarks = _afterRemarks.ButtonPressed,
        InCalls = _inCalls.ButtonPressed, InFullScreen = _inFullScreen.ButtonPressed, WhilePlaying = _whilePlaying.ButtonPressed,
    };

    public void Open() => Paper.Open(this, new Vector2(560, 720));

    /// <summary>Dev: shows the Usage tab.</summary>
    public void DevUsage() => _tabs.CurrentTab = 5;

    /// <summary>Dev: shows the Privacy tab.</summary>
    public void DevPrivacy() => _tabs.CurrentTab = 3;

    /// <summary>Dev: shows the Add-ons tab.</summary>
    public void DevAddOns() => _tabs.CurrentTab = 4;

    /// <summary>Dev: shows the Abilities tab.</summary>
    public void DevAbilities() => _tabs.CurrentTab = 2;

    /// <summary>Dev: shows the Listening tab.</summary>
    public void DevListening() => _tabs.CurrentTab = 1;

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
        Saved?.Invoke(Chosen(), _searches[_search.Selected], TalkKey.Keys[_talkKey.Selected].Name, HandsFreeChosen(), Windows[_window.Selected].Tokens,
                      PermissionsChosen(), _auto.ButtonPressed, _privacy with { Music = _music.ButtonPressed, Days = _days.ButtonPressed },
                      _addOns with { SearXng = _searxng.ButtonPressed, SearXngAddress = _searxngAddress.Text.Trim() });
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
        label.Visible = true;
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
    }
}
