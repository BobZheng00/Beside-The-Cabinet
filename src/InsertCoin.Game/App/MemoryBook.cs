using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Everything she remembers, in the open, a tab each: the two notes she always has in mind; the agenda (plans and
/// reminders, each with how long before to remind and whether it repeats); the facts upkeep and her own notes have
/// collected (edit, pin to keep it always in mind, delete, and bring back what her review retired); the dated
/// moments of past conversations; the player's memos; and the forget buttons. Each change is saved as the player makes
/// it, as in Settings: a field when they leave it (Enter, or a click elsewhere), a tick, a choice or a delete at once;
/// the forget buttons ask twice. Only what the player changed is written, onto the item as it is now. While it is open,
/// every list follows what upkeep, her replies, reminders and Chat history change, in place, except a field the player
/// has changed and not yet left; if she changed that same thing meanwhile, the row shows hers and asks which to keep
/// (<see cref="MemoryEdits"/>). Closing the book, or the app, keeps the player's.
/// </summary>
public partial class MemoryBook : Window
{
    /// <summary>The reminders offered for an agenda item: how long before (null: none; zero: at the time).</summary>
    private static readonly (string Label, TimeSpan? Before)[] Leads =
    [
        ("No reminder", null), ("At the time", TimeSpan.Zero), ("5 min before", TimeSpan.FromMinutes(5)), ("10 min before", TimeSpan.FromMinutes(10)),
        ("15 min before", TimeSpan.FromMinutes(15)), ("30 min before", TimeSpan.FromMinutes(30)), ("1 hour before", TimeSpan.FromHours(1)),
        ("2 hours before", TimeSpan.FromHours(2)), ("1 day before", TimeSpan.FromDays(1)), ("2 days before", TimeSpan.FromDays(2)),
    ];
    private static readonly (string Label, Repeat Repeat)[] Repeats =
        [("Once", Repeat.Once), ("Every day", Repeat.Daily), ("Weekdays", Repeat.Weekdays), ("Every week", Repeat.Weekly), ("Every month", Repeat.Monthly)];

    /// <summary>A question under a field: what the player had typed when it was asked, and what keeping theirs does.</summary>
    private sealed record Question(Control Panel, string Edited, Action KeepYours);

    /// <summary>Something the player can edit, with any questions under it, one per field.</summary>
    private abstract class Editable
    {
        public VBoxContainer Box = null!;
        public readonly Dictionary<Control, Question> Questions = [];
    }

    /// <summary>A fact; <see cref="Shown"/> is it as the book last showed it, the player's edit compared against that.</summary>
    private sealed class FactRow : Editable
    {
        public Fact Shown = null!;
        public LineEdit Text = null!;
        public CheckBox Pin = null!;
    }

    /// <summary>An agenda item; <see cref="Shown"/> is null for a new one until it is first saved.</summary>
    private sealed class PlanRow : Editable
    {
        public Plan? Shown;
        public LineEdit When = null!, Text = null!;
        public OptionButton Lead = null!, Repeat = null!;
        public readonly List<TimeSpan?> Leads = [];
    }

    /// <summary>One of the two notes.</summary>
    private sealed class NoteField : Editable
    {
        public string Scope = "", Name = "", Title = "", Shown = "";
        public TextEdit Field = null!;
    }

    private MindStore _store = null!;
    private string _character = "", _name = "";
    private NoteField _about = null!, _us = null!;
    private TabContainer _tabs = null!;
    private readonly List<FactRow> _facts = [];
    private readonly List<PlanRow> _plans = [];
    private VBoxContainer _planList = null!, _threadList = null!, _factList = null!, _noteList = null!, _retiredList = null!, _memoList = null!;
    private Memos _memos = null!;
    private Button _retiredToggle = null!;
    private Label? _noFacts, _noPlans;
    private readonly Dictionary<string, Action> _forget = [];
    private string?[] _shown = new string?[6];              // what each part was last shown from (see Read)
    private Label _saved = null!;
    private bool _closing;                                  // closing: what is left is saved without asking
    private Control? _devTyped;

    /// <summary>The user wants to read the conversation itself.</summary>
    public event Action? History;

    public void Build(MindStore store, string characterId, string characterName, Memos memos)
    {
        _store = store;
        _memos = memos;
        _character = characterId;
        _name = characterName;
        Title = $"What {characterName} remembers";
        Visible = false;
        Transient = true;
        CloseRequested += Close;
        FocusExited += SaveFields;                          // clicked away from the book: that counts as leaving the field
        TreeExiting += SaveAll;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        page.AddChild(outer);

        var heading = new HBoxContainer();
        heading.AddChild(new Label { Text = $"What {characterName} remembers", ThemeTypeVariation = "Heading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var history = new Button { Text = "Chat history…", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        history.Pressed += () => History?.Invoke();
        heading.AddChild(history);
        outer.AddChild(heading);
        outer.AddChild(Hint("All of it stays on this PC. Change or delete anything here and it is gone from every conversation after."));

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(_tabs);

        var notes = Tab("Notes");
        notes.AddChild(Hint($"Yours to write: {characterName} keeps these in mind as you wrote them and never rewrites them. What {characterName} learns " +
                            "is on Facts, and the most important of it is always in mind too."));
        _about = Note(notes, MindStore.Shared, MindStore.AboutUser, "About you (every character knows this)", "About you", "What to call you, what you're working towards…");
        _us = Note(notes, characterId, MindStore.Us, $"You and {characterName}", $"You and {characterName}", "How you two get along, running jokes…");

        var agenda = Tab("Agenda");
        var top = new HBoxContainer();
        top.AddChild(new Label
        {
            Text = "Plans, dates and reminders. Every reminder shows on a ticket and in the speech bubble (Settings → Reminders for more).",
            ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        var add = new Button { Text = "+ Add", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Something on your agenda, or a reminder" };
        add.Pressed += () => AddPlanRow(null);
        top.AddChild(add);
        agenda.AddChild(top);
        _planList = List(agenda);
        _threadList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        agenda.AddChild(_threadList);

        var facts = Tab("Facts");
        _factList = List(facts);
        // what her review moved to the background: kept, out of mind, and the player can bring it back
        _retiredToggle = new Button { ThemeTypeVariation = "Quiet", ToggleMode = true, Alignment = HorizontalAlignment.Left,
                                      TooltipText = Words.Wrap("Facts the weekly review retired: past plans, things since replaced, trivia long unmentioned. No longer recalled.") };
        facts.AddChild(_retiredToggle);
        var retiredScroll = new ScrollContainer { Visible = false, CustomMinimumSize = new Vector2(0, 180), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _retiredList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _retiredList.AddThemeConstantOverride("separation", 4);
        retiredScroll.AddChild(_retiredList);
        facts.AddChild(retiredScroll);
        _retiredToggle.Toggled += on => { retiredScroll.Visible = on; ShowRetired(); };

        var moments = Tab("Diary");
        moments.AddChild(Hint($"What {characterName} noted from your talks, day by day (\"!\" for what matters most). As they get older, a week of days is " +
                              "condensed into notes for the week, and weeks into months and years; the originals are kept, out of sight."));
        _noteList = List(moments);

        // the player's memos: theirs, not her memory, so they change at once and forgetting leaves them be
        var memoTab = Tab("Memos");
        var memoTop = new HBoxContainer();
        memoTop.AddChild(new Label
        {
            Text = $"What {_name} printed for you, and your own notes, newest first. They're yours: forgetting leaves them be.",
            ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        var write = new Button { Text = "+ Write a memo", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        write.Pressed += memos.Write;
        memoTop.AddChild(write);
        memoTab.AddChild(memoTop);
        _memoList = List(memoTab);
        ShowMemos();
        memos.Changed += ShowMemos;
        TreeExiting += () => memos.Changed -= ShowMemos;

        var forget = Tab("Forget");
        forget.AddThemeConstantOverride("separation", 6);
        void Forget(string key, string label, string explained, Action act)
        {
            var button = Twice(label, _forget[key] = act);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            forget.AddChild(button);
            forget.AddChild(Hint(explained));
            forget.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        }
        Forget("chat", "Clear the chat", $"Starts the conversation with {characterName} afresh. What's remembered (notes, agenda, facts, moments) stays.",
               () => _store.ForgetConversation(_character));
        Forget("all", $"Forget all with {characterName}", "The conversation and everything about the two of you.", () =>
        {
            _store.Forget(_character);
            Refresh();
        });
        Forget("me", "Forget all about me", "What every character knows about you, and your agenda. Conversations stay.", () =>
        {
            _store.ForgetPlayer();
            Refresh();
        });

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        // what was saved last, and when: the room left of Close, all of it
        _saved = new Label { Text = "Changes are saved as you make them.", ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart,
                             SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttons.AddChild(_saved);
        var close = new Button { Text = "Close", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        close.Pressed += Close;
        buttons.AddChild(close);
        outer.AddChild(buttons);

        Refresh();
        var check = new Godot.Timer { WaitTime = 2, Autostart = true };
        check.Timeout += () =>
        {
            if (Visible)
                Refresh();
        };
        AddChild(check);
    }

    public void Open() => Paper.Open(this, new Vector2(700, 680));

    private void Close()
    {
        SaveAll();
        QueueFree();
    }

    /// <summary>
    /// The book is closing, or the app: every field the player changed is saved, and where she changed the same thing
    /// meanwhile, the player's is kept (they changed it, and the question was showing).
    /// </summary>
    public void SaveAll()
    {
        if (_closing)
            return;
        _closing = true;                                    // from here on, nothing on the page changes and nothing is asked: it is going
        SaveEach();
        foreach (var row in Rows())
            foreach (var question in row.Questions.Values.ToList())
                question.KeepYours();
    }

    /// <summary>The book lost focus (a click elsewhere): every field the player changed is saved, as leaving it would.</summary>
    private void SaveFields()
    {
        if (!_closing)
            SaveEach();
    }

    private void SaveEach()
    {
        SaveNote(_about);
        SaveNote(_us);
        foreach (var row in _facts.ToList())
            SaveFact(row);
        foreach (var row in _plans.ToList())
            SavePlan(row);
    }

    private List<Editable> Rows() => [_about, _us, .. _facts, .. _plans];

    /// <summary>Dev: presses the button showing with this label, as a click would.</summary>
    public void DevPress(string label)
    {
        if (FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().FirstOrDefault(b => b.Text == label && b.IsVisibleInTree()) is { } button)
        {
            button.EmitSignal(BaseButton.SignalName.Pressed);
            GD.Print($"Memory book: \"{label}\" pressed");
        }
        else
            GD.PrintErr($"Memory book: no button \"{label}\" showing");
    }

    /// <summary>Dev: types <paramref name="typed"/> into the field showing with <paramref name="words"/> in it (a fact, an agenda item, a note), as the player would, without leaving it.</summary>
    public void DevType(string words, string typed)
    {
        var field = FindChildren("*", "", recursive: true, owned: false).OfType<Control>()
            .FirstOrDefault(c => c.IsVisibleInTree() && ((c as LineEdit)?.Text ?? (c as TextEdit)?.Text)?.Contains(words) == true);
        switch (field)
        {
            case LineEdit line:
                line.Text = typed;
                line.EmitSignal(LineEdit.SignalName.TextChanged, typed);
                break;
            case TextEdit text:
                text.Text = typed;
                break;
            default:
                GD.PrintErr($"Memory book: no field showing with \"{words}\"");
                return;
        }
        _devTyped = field;
        GD.Print($"Memory book: typed in the field with \"{words}\"");
    }

    /// <summary>Dev: leaves the field typed in last, as a click elsewhere would.</summary>
    public void DevLeave()
    {
        if (IsInstanceValid(_devTyped))
            _devTyped!.EmitSignal(Control.SignalName.FocusExited);
    }

    /// <summary>Dev: her memory changes under the book: the fact with these words in it is reworded, as her upkeep might.</summary>
    public void DevChange(string words, string text)
    {
        if (_store.Facts(_character).FirstOrDefault(f => f.Text.Contains(words)) is { } fact)
        {
            _store.UpdateFact(fact.Id, text);
            GD.Print($"Memory book: her fact with \"{words}\" reworded");
        }
        else
            GD.PrintErr($"Memory book: no fact with \"{words}\"");
    }

    /// <summary>What each part of the book shows, read afresh (not when a fact was last recalled, which every reply changes).</summary>
    private string[] Read() =>
    [
        string.Join('\n', _store.Agenda().Select(p => p.ToString())),
        string.Join('\n', _store.Threads().Select(t => $"{t.Id} {t.Text}")),
        string.Join('\n', _store.Facts(_character).Select(f => $"{f.Id} {f.Pinned} {f.Importance} {f.Text}")),
        string.Join('\n', _store.Retired(_character).Select(f => $"{f.Id}")),
        string.Join('\n', _store.Observations(_character).Select(o => $"{o.Id} {o.Condensed} {o.Text}")),
        _store.Note(MindStore.Shared, MindStore.AboutUser) + '\n' + _store.Note(_character, MindStore.Us),
    ];

    /// <summary>
    /// Every two seconds, and after each change here: what changed in her memory (upkeep, a reply, a reminder, Chat
    /// history) shows in place, each part only when it changed.
    /// </summary>
    private void Refresh()
    {
        if (_closing)
            return;
        var now = Read();
        if (now[0] != _shown[0])
            ShowPlans();
        if (now[1] != _shown[1])
            ShowThreads();
        if (now[2] != _shown[2])
            ShowFacts();
        if (now[3] != _shown[3])
            ShowRetired();
        if (now[4] != _shown[4])
            ShowDiary();
        if (now[5] != _shown[5])
            ShowNotes();
        _shown = now;
    }

    /// <summary>What was saved, and when, in the line beside Close.</summary>
    private void Saved(string what)
    {
        _saved.Text = $"{what} at {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}.";
        GD.Print($"Memory book: {what.ToLowerInvariant()}");
    }

    /// <summary>
    /// Under a field: what she did meanwhile, and the player's choice between theirs and the other. Closing the book with
    /// it unanswered keeps theirs; while closing, theirs is kept at once.
    /// </summary>
    private void Ask(Editable row, Control field, string edited, string said, (string Label, Action Act) yours, (string Label, Action Act) other)
    {
        if (_closing)
        {
            yours.Act();
            return;
        }
        Settle(row, field);
        var panel = new HBoxContainer();
        panel.AddThemeConstantOverride("separation", 6);
        panel.AddChild(new Label { Text = said, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                                   Modulate = new Color(1, 0.85f, 0.6f) });
        foreach (var (label, act) in new[] { yours, other })
        {
            var button = new Button { Text = label, ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            button.Pressed += () =>
            {
                Settle(row, field);
                act();
                Refresh();
            };
            panel.AddChild(button);
        }
        row.Box.AddChild(panel);
        if (row.Box.GetChild(row.Box.GetChildCount() - 2) is HSeparator)
            row.Box.MoveChild(panel, row.Box.GetChildCount() - 2);      // above an agenda item's line
        row.Questions[field] = new Question(panel, edited, () =>
        {
            Settle(row, field);
            yours.Act();
        });
        _saved.Text = "Not saved yet: choose which to keep.";
        GD.Print($"Memory book: asked which to keep: {said}");
    }

    /// <summary>The question under a field, if any, is answered.</summary>
    private static void Settle(Editable row, Control field)
    {
        if (row.Questions.Remove(field, out var question))
            question.Panel.QueueFree();
    }

    /// <summary>A question is already showing for exactly this edit: asking again would only flicker.</summary>
    private static bool Asked(Editable row, Control field, string edited) => row.Questions.TryGetValue(field, out var question) && question.Edited == edited;

    /// <summary>Shows a tab ("notes", "agenda", "facts", "diary", "memos", "forget"); "retired" shows the facts with the retired ones open.</summary>
    public void ShowTab(string which)
    {
        if (which == "retired")
        {
            _tabs.CurrentTab = 2;
            _retiredToggle.ButtonPressed = true;
            return;
        }
        var index = Array.IndexOf(["notes", "agenda", "facts", "diary", "memos", "forget"], which);
        if (index >= 0)
            _tabs.CurrentTab = index;
    }

    /// <summary>Dev: as if a forget button ("chat", "all" or "me") were clicked twice.</summary>
    public void DevForget(string which)
    {
        if (_forget.TryGetValue(which, out var forget))
            forget();
    }

    private VBoxContainer Tab(string name)
    {
        var tab = new VBoxContainer { Name = name };
        tab.AddThemeConstantOverride("separation", 8);
        _tabs.AddChild(tab);
        return tab;
    }

    // ---- The notes ---------------------------------------------------------------------------------------------------

    private NoteField Note(VBoxContainer column, string scope, string name, string title, string called, string placeholder)
    {
        var note = new NoteField { Scope = scope, Name = name, Title = called, Box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill } };
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = title, ThemeTypeVariation = "Subheading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var count = new Label { ThemeTypeVariation = "Hint" };
        head.AddChild(count);
        note.Box.AddChild(head);
        var field = note.Field = new TextEdit
        {
            PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
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
        field.TextSet += Count;
        field.FocusExited += () => SaveNote(note);         // Enter is a new line here: leaving it saves
        Count();
        note.Box.AddChild(field);
        column.AddChild(note.Box);
        return note;
    }

    private static bool Changed(NoteField note) => note.Field.Text.Trim() != note.Shown.Trim();

    /// <summary>The two notes as stored, unless the player has changed one and not left it.</summary>
    private void ShowNotes()
    {
        foreach (var note in (NoteField[])[_about, _us])
        {
            if (Changed(note))
                continue;
            var stored = _store.Note(note.Scope, note.Name);
            if (stored != note.Shown)
                note.Field.Text = stored;
            note.Shown = stored;
        }
    }

    private void SaveNote(NoteField note)
    {
        var edited = note.Field.Text.Trim();
        if (!Changed(note) || Asked(note, note.Field, edited))
            return;
        if (MemoryEdits.Note(_store, note.Scope, note.Name, note.Shown, edited) == Kept.Clash)
        {
            var stored = _store.Note(note.Scope, note.Name);
            Ask(note, note.Field, edited, stored.Length == 0 ? "This was cleared meanwhile." : $"This changed meanwhile, to: “{stored}”",
                ("Keep yours", () =>
                {
                    _store.SetNote(note.Scope, note.Name, edited);
                    note.Shown = edited;
                    Saved($"{note.Title} saved");
                }),
                ("Keep the other", () =>
                {
                    note.Shown = _store.Note(note.Scope, note.Name);
                    note.Field.Text = note.Shown;
                }));
            return;
        }
        note.Shown = edited;
        Settle(note, note.Field);
        Saved($"{note.Title} saved");
        Refresh();
    }

    // ---- Facts -------------------------------------------------------------------------------------------------------

    private FactRow AddFactRow(Fact fact)
    {
        var row = new FactRow { Shown = fact, Box = new VBoxContainer() };
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        var whose = new Label { Text = fact.Scope == MindStore.Shared ? "you" : MindStore.IsHerself(fact.Scope) ? "me" : "us", ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(26, 0) };
        var text = row.Text = new LineEdit { Text = fact.Text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"Learned {Day(fact.Learned)}, importance {fact.Importance}/10" };
        var pin = row.Pin = new CheckBox { Text = "pin", ButtonPressed = fact.Pinned, TooltipText = "Always in mind, and upkeep leaves it alone" };
        if (fact.Faded(DateTimeOffset.Now))
        {
            // forgotten for now: it has not come up in a long time; pinning brings it back
            text.Modulate = new Color(1, 1, 1, 0.5f);
            text.TooltipText = Words.Wrap(text.TooltipText + ". Faded: it has not come up in a long time, so it is no longer recalled. Pin it to keep it.");
        }
        var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this" };
        delete.Pressed += () =>
        {
            _store.DeleteFact(row.Shown.Id);
            DropFact(row);
            Saved("Fact forgotten");
            Refresh();
        };
        text.TextSubmitted += _ => SaveFact(row);
        text.FocusExited += () => SaveFact(row);
        pin.Toggled += on =>
        {
            _store.PinFact(row.Shown.Id, on);
            row.Shown = row.Shown with { Pinned = on };
            Saved(on ? "Pinned" : "Unpinned");
            Refresh();
        };
        line.AddChild(whose);
        line.AddChild(text);
        line.AddChild(pin);
        line.AddChild(delete);
        row.Box.AddChild(line);
        _factList.AddChild(row.Box);
        _facts.Add(row);
        return row;
    }

    private static bool Changed(FactRow row) => row.Text.Text.Trim() != row.Shown.Text;

    private void DropFact(FactRow row)
    {
        _facts.Remove(row);
        if (!_closing)
            _factList.RemoveChild(row.Box);             // at once, so the places of the rest count right
        row.Box.QueueFree();
        ShowNoFacts();
    }

    /// <summary>
    /// The facts as stored: each row follows its fact, unless the player has changed it and not left it; what has gone
    /// leaves; what is new comes in on top, where it is seen (the rest keep their places while the book is open).
    /// </summary>
    private void ShowFacts()
    {
        var first = _facts.Count == 0;
        var stored = _store.Facts(_character).OrderByDescending(f => f.Pinned).ThenByDescending(f => f.Importance).ThenByDescending(f => f.Learned).ToList();
        var fresh = stored.ToDictionary(f => f.Id);
        foreach (var row in _facts.ToList())
        {
            if (fresh.Remove(row.Shown.Id, out var fact))
            {
                if (Changed(row))
                    row.Shown = fact with { Text = row.Shown.Text };
                else
                {
                    if (row.Text.Text != fact.Text)
                        row.Text.Text = fact.Text;
                    row.Shown = fact;
                }
                row.Pin.SetPressedNoSignal(fact.Pinned);
            }
            else if (!Changed(row))
                DropFact(row);
        }
        var added = stored.Where(f => fresh.ContainsKey(f.Id)).ToList();
        if (!first)
            added.Reverse();                                // each goes on top in turn, so they end up in order
        foreach (var fact in added)
        {
            var row = AddFactRow(fact);
            if (!first)
                _factList.MoveChild(row.Box, 0);
        }
        ShowNoFacts();
    }

    private void SaveFact(FactRow row)
    {
        var edited = row.Text.Text.Trim();
        if (!_facts.Contains(row) || !Changed(row) || Asked(row, row.Text, edited))
            return;
        switch (MemoryEdits.Fact(_store, _character, row.Shown.Id, row.Shown.Text, edited))
        {
            case Kept.Clash:
                Ask(row, row.Text, edited, $"{_name} changed this meanwhile, to: “{Stored(row)?.Text}”",
                    ("Keep yours", () =>
                    {
                        row.Shown = FactNow(MemoryEdits.KeepFact(_store, _character, row.Shown, edited, DateTimeOffset.Now)) ?? row.Shown;
                        Saved("Fact saved");
                    }),
                    ($"Keep {_name}'s", () =>
                    {
                        if (Stored(row) is { } hers)
                        {
                            row.Shown = hers;
                            row.Text.Text = hers.Text;
                        }
                    }));
                return;
            case Kept.Gone:
                var why = _store.Retired(_character).FirstOrDefault(f => f.Id == row.Shown.Id)?.Why;
                Ask(row, row.Text, edited, why is null ? $"{_name} let this go meanwhile." : $"{_name} retired this meanwhile ({why}).",
                    ("Keep yours", () =>
                    {
                        row.Shown = FactNow(MemoryEdits.KeepFact(_store, _character, row.Shown, edited, DateTimeOffset.Now)) ?? row.Shown;
                        Saved("Fact saved");
                    }),
                    ("Let it go", () => DropFact(row)));
                return;
            case Kept.Saved when edited.Length == 0:
                DropFact(row);
                Saved("Fact forgotten");
                break;
            default:
                row.Shown = row.Shown with { Text = edited };
                Settle(row, row.Text);
                Saved("Fact saved");
                break;
        }
        Refresh();
    }

    private Fact? Stored(FactRow row) => FactNow(row.Shown.Id);

    private Fact? FactNow(long id) => _store.Facts(_character).FirstOrDefault(f => f.Id == id);

    private void ShowRetired()
    {
        var retired = _store.Retired(_character).OrderByDescending(f => f.Retired).ToList();
        _retiredToggle.Visible = retired.Count > 0;
        _retiredToggle.Text = $"{(_retiredToggle.ButtonPressed ? "▾" : "▸")} Retired by the review ({retired.Count})";
        foreach (var child in _retiredList.GetChildren())
            child.QueueFree();
        if (!_retiredToggle.ButtonPressed)
            return;
        foreach (var fact in retired)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = fact.Scope == MindStore.Shared ? "you" : MindStore.IsHerself(fact.Scope) ? "me" : "us", ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(26, 0) });
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = new Color(1, 1, 1, 0.6f) };
            text.AddChild(new Label { Text = fact.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            text.AddChild(new Label { Text = $"{Day(fact.Retired!.Value)}: {fact.Why}", ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            row.AddChild(text);
            var back = new Button { Text = "Bring back", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, TooltipText = "Kept in mind again" };
            back.Pressed += () =>
            {
                _store.Restore(fact.Id, DateTimeOffset.Now);
                Saved("Brought back");
                Refresh();
            };
            var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this for good", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            delete.Pressed += () =>
            {
                _store.DeleteFact(fact.Id);
                Saved("Fact forgotten");
                Refresh();
            };
            row.AddChild(back);
            row.AddChild(delete);
            _retiredList.AddChild(row);
        }
    }

    private void ShowNoFacts()
    {
        var none = _facts.Count == 0;
        if (none && !IsInstanceValid(_noFacts))
        {
            _noFacts = Hint("Nothing yet. Facts appear here after you have talked a while.");
            _factList.AddChild(_noFacts);
        }
        else if (!none && IsInstanceValid(_noFacts))
        {
            if (!_closing)
                _factList.RemoveChild(_noFacts);
            _noFacts!.QueueFree();
            _noFacts = null;
        }
    }

    // ---- The agenda --------------------------------------------------------------------------------------------------

    /// <summary>What is going on with no date, for her to ask about a couple of days after it came up (see <see cref="Threads"/>); Settled closes one.</summary>
    private void ShowThreads()
    {
        foreach (var child in _threadList.GetChildren())
            child.QueueFree();
        var threads = _store.Threads();
        if (threads.Count > 0)
            _threadList.AddChild(Hint($"Going on: {_name} asks how it is going a couple of days after it comes up, once."));
        foreach (var thread in threads)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = $"{thread.Text} (since {Day(thread.Opened)})", AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                     SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var settled = new Button { Text = "Settled", ThemeTypeVariation = "Quiet", TooltipText = "It's over: no more asking about it" };
            settled.Pressed += () =>
            {
                _store.CloseThread(thread.Id, "settled in the memory book", DateTimeOffset.Now);
                Saved("Settled");
                Refresh();
            };
            row.AddChild(settled);
            _threadList.AddChild(row);
        }
    }

    /// <summary>
    /// The agenda as stored, by time: each row follows its item, except a field the player has changed and not left;
    /// what has gone leaves; a new item not yet saved stays at the end.
    /// </summary>
    private void ShowPlans()
    {
        var stored = _store.Agenda();
        var order = stored.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        foreach (var row in _plans.ToList())
        {
            if (row.Shown is null)
                continue;
            if (stored.FirstOrDefault(p => p.Id == row.Shown.Id) is { } plan)
                Follow(row, plan);
            else if (!TextChanged(row) && !WhenChanged(row))
                DropPlan(row);
        }
        foreach (var plan in stored.Where(p => _plans.All(r => r.Shown?.Id != p.Id)))
            AddPlanRow(plan);
        var sorted = _plans.OrderBy(r => r.Shown is { } p && order.TryGetValue(p.Id, out var i) ? i : int.MaxValue).ToList();
        _plans.Clear();
        _plans.AddRange(sorted);
        for (var i = 0; i < sorted.Count; i++)
            _planList.MoveChild(sorted[i].Box, i);
        ShowNoPlans();
    }

    private void ShowNoPlans()
    {
        var none = _plans.Count == 0;
        if (none && !IsInstanceValid(_noPlans))
        {
            _noPlans = Hint($"Nothing yet. Tell {_name} about plans and dates, or ask for a reminder.");
            _planList.AddChild(_noPlans);
        }
        else if (!none && IsInstanceValid(_noPlans))
        {
            if (!_closing)
                _planList.RemoveChild(_noPlans);
            _noPlans!.QueueFree();
            _noPlans = null;
        }
    }

    private static string When(Plan plan) => plan.At.ToString(plan.AllDay ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static bool TextChanged(PlanRow row) => row.Text.Text.Trim() != (row.Shown?.Text ?? "");

    private static bool WhenChanged(PlanRow row) => row.Shown is { } plan && row.When.Text.Trim() != When(plan);

    /// <summary>A row takes on its item as stored, except a field the player has changed and not left.</summary>
    private static void Follow(PlanRow row, Plan plan)
    {
        var shown = plan;
        if (TextChanged(row))
            shown = shown with { Text = row.Shown!.Text };
        else if (row.Text.Text != plan.Text)
            row.Text.Text = plan.Text;
        if (WhenChanged(row))
            shown = shown with { At = row.Shown!.At, AllDay = row.Shown.AllDay };
        else if (row.When.Text != When(plan))
        {
            row.When.Text = When(plan);
            row.When.Modulate = Colors.White;
        }
        SelectLead(row, plan.Before);
        row.Repeat.Selected = Array.FindIndex(Repeats, r => r.Repeat == plan.Repeat);
        row.Shown = shown;
    }

    private static void SelectLead(PlanRow row, TimeSpan? before)
    {
        if (!row.Leads.Contains(before))
        {
            row.Lead.AddItem(Agenda.Describe(before!.Value));   // what she set, though the list has no such choice
            row.Leads.Add(before);
        }
        row.Lead.Selected = row.Leads.IndexOf(before);
    }

    /// <summary>
    /// One item, on two lines: when ("2026-10-09 15:00", or a date alone for a whole day) and what; then its reminder
    /// (how long before) and whether it repeats. A new one (null) is saved once it says something and when.
    /// </summary>
    private void AddPlanRow(Plan? plan)
    {
        var now = DateTimeOffset.Now;
        var at = plan?.At ?? now.Date.AddDays(1).AddHours(9);
        var row = new PlanRow { Shown = plan, Box = new VBoxContainer() };
        row.Box.AddThemeConstantOverride("separation", 4);

        var first = new HBoxContainer();
        first.AddThemeConstantOverride("separation", 6);
        var when = row.When = new LineEdit
        {
            Text = at.ToString(plan?.AllDay == true ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            CustomMinimumSize = new Vector2(150, 0),
            TooltipText = "YYYY-MM-DD HH:MM, or YYYY-MM-DD for a whole day",
        };
        when.TextChanged += t => when.Modulate = Agenda.ParseWhen(t, DateTimeOffset.Now, TimeZoneInfo.Local) is null ? new Color(1, 0.5f, 0.5f) : Colors.White;
        var text = row.Text = new LineEdit { Text = plan?.Text ?? "", PlaceholderText = "What it is", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Take it off the agenda" };
        foreach (var field in new[] { when, text })
        {
            field.TextSubmitted += _ => SavePlan(row);
            field.FocusExited += () => SavePlan(row);
        }
        first.AddChild(when);
        first.AddChild(text);
        first.AddChild(delete);
        row.Box.AddChild(first);

        var second = new HBoxContainer();
        second.AddThemeConstantOverride("separation", 6);
        second.AddChild(new Control { CustomMinimumSize = new Vector2(150, 0) });         // under the time: the what, and how it reminds
        var lead = row.Lead = new OptionButton { TooltipText = Words.Wrap("When its reminder goes off. For a whole day, counted from 09:00 that day.") };
        foreach (var (label, before) in Leads)
        {
            lead.AddItem(label);
            row.Leads.Add(before);
        }
        SelectLead(row, plan is null ? TimeSpan.Zero : plan.Before);
        var repeat = row.Repeat = new OptionButton { TooltipText = Words.Wrap("Comes round again: it moves on to its next time once it is over") };
        foreach (var (label, _) in Repeats)
            repeat.AddItem(label);
        repeat.Selected = Array.FindIndex(Repeats, r => r.Repeat == (plan?.Repeat ?? Repeat.Once));
        lead.ItemSelected += i => Choose(row, p => p with { Before = row.Leads[(int)i] });
        repeat.ItemSelected += i => Choose(row, p => p with { Repeat = Repeats[(int)i].Repeat });
        second.AddChild(lead);
        second.AddChild(repeat);
        if (plan?.Source is { } source)
        {
            // from a calendar: it says when and what; only her reminder for it is the player's to set here
            when.Editable = text.Editable = false;
            repeat.Disabled = true;
            delete.Visible = false;
            text.TooltipText = Words.Wrap($"From your calendar “{source}”: change it there (Settings → Calendars).");
            second.AddChild(new Label { Text = $"from {source}", ThemeTypeVariation = "Hint" });
        }
        if (plan is not null && plan.End <= now && plan.Repeat == Repeat.Once)
        {
            text.Modulate = new Color(1, 1, 1, 0.5f);       // just past: she'll ask how it went, then it leaves the agenda
            text.TooltipText = Words.Wrap($"Just past: {_name} may ask how it went. It leaves the agenda two days after.");
            second.AddChild(new Label { Text = "just past", ThemeTypeVariation = "Hint" });
        }
        row.Box.AddChild(second);
        row.Box.AddChild(new HSeparator());

        delete.Pressed += () =>
        {
            if (row.Shown is { } shown)
            {
                _store.DeletePlan(shown.Id);
                Saved("Taken off the agenda");
            }
            DropPlan(row);
            Refresh();
        };
        _planList.AddChild(row.Box);
        _plans.Add(row);
        ShowNoPlans();
        if (plan is null)
            text.GrabFocus();
    }

    private void DropPlan(PlanRow row)
    {
        _plans.Remove(row);
        if (!_closing)
            _planList.RemoveChild(row.Box);
        row.Box.QueueFree();
        ShowNoPlans();
    }

    /// <summary>A reminder or a repeat chosen: saved at once, onto the item as it is now. A new item takes it when it is first saved.</summary>
    private void Choose(PlanRow row, Func<Plan, Plan> edit)
    {
        if (row.Shown is null)
            return;
        if (MemoryEdits.PlanChoice(_store, row.Shown.Id, edit) != Kept.Gone)
        {
            row.Shown = edit(row.Shown);
            Saved("Agenda item saved");
        }
        Refresh();
    }

    private void SavePlan(PlanRow row)
    {
        if (!_plans.Contains(row))
            return;
        var now = DateTimeOffset.Now;
        var text = row.Text.Text.Trim();
        var when = Agenda.ParseWhen(row.When.Text, now, TimeZoneInfo.Local);
        if (row.Shown is null)
        {
            if (text.Length < 2)
                return;                                     // nothing said yet
            if (when is not { } first)
            {
                Unreadable();
                return;
            }
            var id = _store.AddPlan(text, first.At, first.AllDay, row.Leads[row.Lead.Selected], now, null, Repeats[row.Repeat.Selected].Repeat);
            row.Shown = _store.Agenda().FirstOrDefault(p => p.Id == id);
            Saved("Added to the agenda");
            Refresh();
            return;
        }
        if (TextChanged(row) && !Asked(row, row.Text, text))
        {
            if (text.Length < 2)
                row.Text.Text = row.Shown.Text;             // an item needs a few words: left as it was
            else
                Outcome(row, row.Text, text, MemoryEdits.PlanText(_store, row.Shown, text), p => p with { Text = text });
        }
        if (WhenChanged(row) && !Asked(row, row.When, row.When.Text.Trim()))
        {
            if (when is not { } moved)
                Unreadable();
            else
                Outcome(row, row.When, row.When.Text.Trim(), MemoryEdits.PlanWhen(_store, row.Shown!, moved.At, moved.AllDay),
                        p => p with { At = moved.At, AllDay = moved.AllDay });
        }
        Refresh();
    }

    private void Unreadable()
    {
        _saved.Text = "Not saved yet: write the time as YYYY-MM-DD HH:MM, or a date alone for a whole day.";
        GD.Print("Memory book: a time it can't read, not saved");
    }

    /// <summary>What became of one field of an agenda item: saved, or a question when she changed the same part, or it left the agenda meanwhile.</summary>
    private void Outcome(PlanRow row, LineEdit field, string edited, Kept kept, Func<Plan, Plan> mine)
    {
        var text = field == row.Text;
        switch (kept)
        {
            case Kept.Clash when _store.Agenda().FirstOrDefault(p => p.Id == row.Shown!.Id) is { } hers:
                Ask(row, field, edited, text ? $"{_name} changed this meanwhile, to: “{hers.Text}”" : $"{_name} moved this meanwhile, to {hers.Label}.",
                    ("Keep yours", () =>
                    {
                        if (row.Shown is not null && MemoryEdits.PlanChoice(_store, row.Shown.Id, mine) != Kept.Gone)
                        {
                            row.Shown = mine(row.Shown);
                            Saved("Agenda item saved");
                        }
                    }),
                    ($"Keep {_name}'s", () =>
                    {
                        if (row.Shown is not null && _store.Agenda().FirstOrDefault(p => p.Id == row.Shown.Id) is { } latest)
                        {
                            field.Text = text ? latest.Text : When(latest);
                            row.Shown = text ? row.Shown with { Text = latest.Text } : row.Shown with { At = latest.At, AllDay = latest.AllDay };
                            if (!text)
                                field.Modulate = Colors.White;
                            Follow(row, latest);
                        }
                    }));
                break;
            case Kept.Clash or Kept.Gone:
                Ask(row, field, edited, "This left the agenda meanwhile.",
                    ("Keep yours", () =>
                    {
                        row.Shown = null;                   // put on the agenda again, as the player has it
                        SavePlan(row);
                    }),
                    ("Let it go", () => DropPlan(row)));
                break;
            default:
                row.Shown = mine(row.Shown!);
                Settle(row, field);
                Saved("Agenda item saved");
                break;
        }
    }

    // ---- The diary and the memos ------------------------------------------------------------------------------------

    private void ShowDiary()
    {
        foreach (var child in _noteList.GetChildren())
            child.QueueFree();
        var notes = _store.Observations(_character).Where(o => !o.Condensed).Reverse().ToList();
        if (notes.Count == 0)
            _noteList.AddChild(Hint("Nothing yet."));
        foreach (var note in notes)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = Log.Heading(note.Span, note.At), ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(78, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            row.AddChild(new Label { Text = (note.Priority == Priority.High ? "! " : "") + note.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            delete.Pressed += () =>
            {
                _store.DeleteObservation(note.Id);
                Saved("Note forgotten");
                Refresh();
            };
            row.AddChild(delete);
            _noteList.AddChild(row);
        }
    }

    /// <summary>The memos, newest first: where each is, whose, and Read and Pin (or Take down).</summary>
    private void ShowMemos()
    {
        foreach (var child in _memoList.GetChildren())
            child.QueueFree();
        var memos = _memos.All;
        if (memos.Count == 0)
            _memoList.AddChild(Hint($"None yet. Ask {_name} to write something down for you, or write one yourself."));
        foreach (var memo in memos)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            words.AddThemeConstantOverride("separation", 0);
            words.AddChild(new Label { Text = memo.Title, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            var where = memo.Place switch { MemoPlace.Hanging => "hanging from the slot", MemoPlace.Pinned => "pinned on the cabinet", _ => "kept" };
            words.AddChild(new Label { Text = $"{_memos.From(memo)} · {Day(memo.At)} · {where}", ThemeTypeVariation = "Hint", ClipText = true,
                                       TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            row.AddChild(words);
            var read = new Button { Text = "Read", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            read.Pressed += () => _memos.Open(memo.Id);
            var pin = new Button
            {
                Text = memo.Place == MemoPlace.Pinned ? "Take down" : "Pin", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = memo.Place == MemoPlace.Pinned ? "Off the cabinet's side; it stays here" : "On the cabinet's side",
            };
            pin.Pressed += () =>
            {
                if (memo.Place == MemoPlace.Pinned)
                    _memos.Archive(memo.Id);
                else if (!_memos.Pin(memo.Id))
                    pin.Text = "The side is full";
            };
            row.AddChild(read);
            row.AddChild(pin);
            _memoList.AddChild(row);
        }
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

    /// <summary>A list that fills the rest of its tab and scrolls.</summary>
    private static VBoxContainer List(VBoxContainer tab)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        tab.AddChild(scroll);
        return list;
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };

    private static string Day(DateTimeOffset at) => at.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
