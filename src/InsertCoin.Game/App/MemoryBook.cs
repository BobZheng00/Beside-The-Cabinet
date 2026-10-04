using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Everything she remembers, in the open: the two notes she always has in mind, the facts upkeep and her own notes
/// have collected (edit, pin to keep it always in mind, or delete), and the dated moments of past conversations.
/// Nothing changes until Save; the forget buttons ask twice and act at once, and the page then shows what is left,
/// keeping unsaved changes to the rest.
/// </summary>
public partial class MemoryBook : Window
{
    private MindStore _store = null!;
    private string _character = "", _name = "";
    private TextEdit _about = null!, _us = null!;
    private readonly List<(Fact Fact, LineEdit Text, CheckBox Pin, Control Row)> _facts = [];
    private readonly HashSet<long> _deletedFacts = [], _deletedEpisodes = [];
    private VBoxContainer _factList = null!, _episodeList = null!;
    private Label? _noFacts;
    private readonly Dictionary<string, Action> _forget = [];

    /// <summary>The user wants to read the conversation itself.</summary>
    public event Action? History;

    public void Build(MindStore store, string characterId, string characterName)
    {
        _store = store;
        _character = characterId;
        _name = characterName;
        Title = $"What {characterName} remembers";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        page.AddChild(column);

        var heading = new HBoxContainer();
        heading.AddChild(new Label { Text = $"What {characterName} remembers", ThemeTypeVariation = "Heading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var history = new Button { Text = "Chat history…", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        history.Pressed += () => History?.Invoke();
        heading.AddChild(history);
        column.AddChild(heading);
        column.AddChild(Hint("All of it stays on this PC. Change or delete anything here and it is gone from every conversation after."));

        _about = Note(column, "About you (every character knows this)", "Your name, what you're working towards, your exam dates…",
                      store.Note(MindStore.Shared, MindStore.AboutUser));
        _us = Note(column, $"You and {characterName}", "How you two get along, what to call you, running jokes…", store.Note(characterId, MindStore.Us));

        column.AddChild(new Label { Text = "Things learned", ThemeTypeVariation = "Subheading" });
        _factList = List(column, 150);
        var facts = store.Facts(characterId).OrderByDescending(f => f.Pinned).ThenByDescending(f => f.Importance).ThenByDescending(f => f.Learned).ToList();
        foreach (var fact in facts)
            AddFactRow(fact);
        ShowNoFacts();

        column.AddChild(new Label { Text = "Moments", ThemeTypeVariation = "Subheading" });
        _episodeList = List(column, 100);
        ShowEpisodes();

        var forget = new HBoxContainer();
        forget.AddThemeConstantOverride("separation", 8);
        var chat = Twice("Clear the chat", _forget["chat"] = () => _store.ForgetConversation(_character));
        chat.TooltipText = $"Starts the conversation with {characterName} afresh. What she remembers (notes, facts, moments) stays.";
        forget.AddChild(chat);
        var all = Twice($"Forget all with {characterName}", _forget["all"] = () =>
        {
            _store.Forget(_character);
            _us.Text = "";
            ShowWhatIsLeft();
        });
        all.TooltipText = "The conversation and everything about the two of you.";
        forget.AddChild(all);
        var me = Twice("Forget all about me", _forget["me"] = () =>
        {
            _store.ForgetPlayer();
            _about.Text = "";
            ShowWhatIsLeft();
        });
        me.TooltipText = "What every character knows about you. Conversations stay.";
        forget.AddChild(me);
        column.AddChild(forget);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        cancel.Pressed += QueueFree;
        save.Pressed += Save;
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        column.AddChild(buttons);
    }

    public void Open() => Paper.Open(this, new Vector2(600, 760));

    /// <summary>Dev: as if a forget button ("chat", "all" or "me") were clicked twice.</summary>
    public void DevForget(string which)
    {
        if (_forget.TryGetValue(which, out var forget))
            forget();
    }

    private void AddFactRow(Fact fact)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var whose = new Label { Text = fact.Scope == MindStore.Shared ? "you" : "us", ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(26, 0) };
        var text = new LineEdit { Text = fact.Text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"Learned {Day(fact.Learned)}, importance {fact.Importance}/10" };
        var pin = new CheckBox { Text = "pin", ButtonPressed = fact.Pinned, TooltipText = "Always in mind, and upkeep leaves it alone" };
        if (fact.Faded(DateTimeOffset.Now))
        {
            // forgotten for now: it has not come up in a long time; pinning brings it back
            text.Modulate = new Color(1, 1, 1, 0.5f);
            text.TooltipText += ". Faded: it has not come up in a long time, so she no longer recalls it. Pin it to keep it.";
        }
        var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this" };
        delete.Pressed += () => { _deletedFacts.Add(fact.Id); row.QueueFree(); };
        row.AddChild(whose);
        row.AddChild(text);
        row.AddChild(pin);
        row.AddChild(delete);
        _factList.AddChild(row);
        _facts.Add((fact, text, pin, row));
    }

    private void AddEpisodeRow(Episode episode)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var when = episode.Week ? "Week of " + episode.At.ToString("d MMM", CultureInfo.InvariantCulture) : Day(episode.At);
        row.AddChild(new Label { Text = when, ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(78, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(new Label { Text = episode.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        delete.Pressed += () => { _deletedEpisodes.Add(episode.Id); row.QueueFree(); };
        row.AddChild(delete);
        _episodeList.AddChild(row);
    }

    /// <summary>After a forget: what is gone leaves the page; unsaved changes to the rest stay.</summary>
    private void ShowWhatIsLeft()
    {
        var left = _store.Facts(_character).Select(f => f.Id).ToHashSet();
        foreach (var gone in _facts.Where(f => !left.Contains(f.Fact.Id)).ToList())
        {
            if (IsInstanceValid(gone.Row))
                gone.Row.QueueFree();
            _facts.Remove(gone);
            _deletedFacts.Remove(gone.Fact.Id);
        }
        ShowNoFacts();
        ShowEpisodes();
    }

    private void ShowNoFacts()
    {
        if (_facts.Any(f => !_deletedFacts.Contains(f.Fact.Id)) || IsInstanceValid(_noFacts))
            return;
        _noFacts = Hint("Nothing yet. Facts appear here after you have talked a while.");
        _factList.AddChild(_noFacts);
    }

    private void ShowEpisodes()
    {
        foreach (var child in _episodeList.GetChildren())
            child.QueueFree();
        var episodes = _store.Episodes(_character).Where(e => !_deletedEpisodes.Contains(e.Id)).Reverse().ToList();
        if (episodes.Count == 0)
            _episodeList.AddChild(Hint("Nothing yet."));
        foreach (var episode in episodes)
            AddEpisodeRow(episode);
    }

    private void Save()
    {
        _store.SetNote(MindStore.Shared, MindStore.AboutUser, _about.Text.Trim());
        _store.SetNote(_character, MindStore.Us, _us.Text.Trim());
        foreach (var (fact, text, pin, _) in _facts)
        {
            if (_deletedFacts.Contains(fact.Id))
                continue;
            if (text.Text.Trim().Length == 0)
                _store.DeleteFact(fact.Id);
            else if (text.Text.Trim() != fact.Text)
                _store.UpdateFact(fact.Id, text.Text);
            if (pin.ButtonPressed != fact.Pinned)
                _store.PinFact(fact.Id, pin.ButtonPressed);
        }
        foreach (var id in _deletedFacts)
            _store.DeleteFact(id);
        foreach (var id in _deletedEpisodes)
            _store.DeleteEpisode(id);
        QueueFree();
    }

    /// <summary>A button that only acts on a second click within a few seconds, then says it is done.</summary>
    private Button Twice(string label, Action act)
    {
        var button = new Button { Text = label, ThemeTypeVariation = "Quiet" };
        var armed = false;
        var turn = 0;                                       // only the latest timer puts the button back
        void Back(double after)
        {
            var mine = ++turn;
            GetTree().CreateTimer(after).Timeout += () =>
            {
                if (IsInstanceValid(button) && mine == turn)
                {
                    armed = false;
                    button.Text = label;
                    button.Disabled = false;
                }
            };
        }
        button.Pressed += () =>
        {
            if (armed)
            {
                act();
                armed = false;
                button.Text = "Done";
                button.Disabled = true;
                Back(2);
                return;
            }
            armed = true;
            button.Text = "Click again to forget";
            Back(4);
        };
        return button;
    }

    private static VBoxContainer List(VBoxContainer column, float height)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, height), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        column.AddChild(scroll);
        return list;
    }

    private static TextEdit Note(VBoxContainer column, string title, string placeholder, string text)
    {
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = title, ThemeTypeVariation = "Subheading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var count = new Label { ThemeTypeVariation = "Hint" };
        head.AddChild(count);
        column.AddChild(head);
        var field = new TextEdit { Text = text, PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 76), WrapMode = TextEdit.LineWrappingMode.Boundary };
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
        field.TextSet += Count;
        Count();
        column.AddChild(field);
        return field;
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };

    private static string Day(DateTimeOffset at) => at.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
