using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Everything she remembers, in the open, a tab each: the two notes she always has in mind; the agenda (plans and
/// reminders, each with how long before to remind and whether it repeats); the facts upkeep and her own notes have
/// collected (edit, pin to keep it always in mind, delete, and bring back what her review retired); the dated
/// moments of past conversations; the player's memos; and the forget buttons. Nothing changes until Save, except
/// the memos (each acts at once, as on the cabinet); the forget buttons ask twice and act at once, and the page
/// then shows what is left, keeping unsaved changes to the rest.
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

    private sealed record PlanRow(Plan? Plan, LineEdit When, LineEdit Text, OptionButton Lead, List<TimeSpan?> Leads, OptionButton Repeat, Control Row);

    private MindStore _store = null!;
    private string _character = "", _name = "";
    private TextEdit _about = null!, _us = null!;
    private TabContainer _tabs = null!;
    private readonly List<(Fact Fact, LineEdit Text, CheckBox Pin, Control Row)> _facts = [];
    private readonly HashSet<long> _deletedFacts = [], _deletedNotes = [], _deletedPlans = [];
    private readonly List<PlanRow> _plans = [];
    private VBoxContainer _planList = null!, _factList = null!, _noteList = null!, _retiredList = null!, _memoList = null!;
    private Memos _memos = null!;
    private Button _retiredToggle = null!;
    private Label? _noFacts;
    private readonly Dictionary<string, Action> _forget = [];

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
        CloseRequested += QueueFree;

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
        notes.AddChild(Hint("Yours to write: she keeps these in mind as you wrote them and never rewrites them. What she learns " +
                            "herself is on Facts, and the most important of it is always in mind too."));
        _about = Note(notes, "About you (every character knows this)", "What to call you, what you're working towards…",
                      store.Note(MindStore.Shared, MindStore.AboutUser));
        _us = Note(notes, $"You and {characterName}", "How you two get along, running jokes…", store.Note(characterId, MindStore.Us));

        var agenda = Tab("Agenda");
        var top = new HBoxContainer();
        top.AddChild(new Label
        {
            Text = "Plans, dates and reminders. Every reminder shows on a ticket and in her bubble (Settings → Reminders for more).",
            ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        var add = new Button { Text = "+ Add", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Something on your agenda, or a reminder" };
        add.Pressed += () => AddPlanRow(null);
        top.AddChild(add);
        agenda.AddChild(top);
        _planList = List(agenda);
        _threadList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        agenda.AddChild(_threadList);
        ShowPlans();
        ShowThreads();

        var facts = Tab("Facts");
        _factList = List(facts);
        foreach (var fact in store.Facts(characterId).OrderByDescending(f => f.Pinned).ThenByDescending(f => f.Importance).ThenByDescending(f => f.Learned))
            AddFactRow(fact);
        ShowNoFacts();
        // what her review moved to the background: kept, out of mind, and the player can bring it back
        _retiredToggle = new Button { ThemeTypeVariation = "Quiet", ToggleMode = true, Alignment = HorizontalAlignment.Left,
                                      TooltipText = Words.Wrap("Facts her weekly review retired: past plans, things since replaced, trivia long unmentioned. She no longer recalls them.") };
        facts.AddChild(_retiredToggle);
        var retiredScroll = new ScrollContainer { Visible = false, CustomMinimumSize = new Vector2(0, 180), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _retiredList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _retiredList.AddThemeConstantOverride("separation", 4);
        retiredScroll.AddChild(_retiredList);
        facts.AddChild(retiredScroll);
        _retiredToggle.Toggled += on => { retiredScroll.Visible = on; ShowRetired(); };
        ShowRetired();

        var moments = Tab("Diary");
        moments.AddChild(Hint("What she noted from your talks, day by day (\"!\" for what matters most). As they get older, a week of days is " +
                              "condensed into notes for the week, and weeks into months and years; the originals stay with her, out of sight."));
        _noteList = List(moments);
        ShowNotes();

        // the player's memos: theirs, not her memory, so they change at once and forgetting leaves them be
        var memoTab = Tab("Memos");
        var memoTop = new HBoxContainer();
        memoTop.AddChild(new Label
        {
            Text = "What she printed for you, and your own notes, newest first. They're yours: forgetting leaves them be.",
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
        Forget("chat", "Clear the chat", $"Starts the conversation with {characterName} afresh. What she remembers (notes, agenda, facts, moments) stays.",
               () => _store.ForgetConversation(_character));
        Forget("all", $"Forget all with {characterName}", "The conversation and everything about the two of you.", () =>
        {
            _store.Forget(_character);
            _us.Text = "";
            ShowWhatIsLeft();
        });
        Forget("me", "Forget all about me", "What every character knows about you, and your agenda. Conversations stay.", () =>
        {
            _store.ForgetPlayer();
            _about.Text = "";
            ShowWhatIsLeft();
            ShowPlans();
        });

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var cancel = new Button { Text = "Cancel", ThemeTypeVariation = "Quiet" };
        var save = new Button { Text = "Save" };
        cancel.Pressed += QueueFree;
        save.Pressed += Save;
        buttons.AddChild(cancel);
        buttons.AddChild(save);
        outer.AddChild(buttons);
    }

    public void Open() => Paper.Open(this, new Vector2(620, 680));

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

    private void AddFactRow(Fact fact)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var whose = new Label { Text = fact.Scope == MindStore.Shared ? "you" : MindStore.IsHerself(fact.Scope) ? "her" : "us", ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(26, 0) };
        var text = new LineEdit { Text = fact.Text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"Learned {Day(fact.Learned)}, importance {fact.Importance}/10" };
        var pin = new CheckBox { Text = "pin", ButtonPressed = fact.Pinned, TooltipText = "Always in mind, and upkeep leaves it alone" };
        if (fact.Faded(DateTimeOffset.Now))
        {
            // forgotten for now: it has not come up in a long time; pinning brings it back
            text.Modulate = new Color(1, 1, 1, 0.5f);
            text.TooltipText = Words.Wrap(text.TooltipText + ". Faded: it has not come up in a long time, so she no longer recalls it. Pin it to keep it.");
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

    private void ShowRetired()
    {
        var retired = _store.Retired(_character).OrderByDescending(f => f.Retired).ToList();
        _retiredToggle.Visible = retired.Count > 0;
        _retiredToggle.Text = $"{(_retiredToggle.ButtonPressed ? "▾" : "▸")} Retired by her review ({retired.Count})";
        foreach (var child in _retiredList.GetChildren())
            child.QueueFree();
        if (!_retiredToggle.ButtonPressed)
            return;
        foreach (var fact in retired)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = fact.Scope == MindStore.Shared ? "you" : MindStore.IsHerself(fact.Scope) ? "her" : "us", ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(26, 0) });
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = new Color(1, 1, 1, 0.6f) };
            text.AddChild(new Label { Text = fact.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            text.AddChild(new Label { Text = $"{Day(fact.Retired!.Value)}: {fact.Why}", ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            row.AddChild(text);
            var back = new Button { Text = "Bring back", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, TooltipText = "She keeps it in mind again" };
            back.Pressed += () =>
            {
                _store.Restore(fact.Id, DateTimeOffset.Now);
                if (IsInstanceValid(_noFacts))
                    _noFacts.QueueFree();
                AddFactRow(_store.Facts(_character).Single(f => f.Id == fact.Id));
                ShowRetired();
            };
            var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this for good", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            delete.Pressed += () => { _store.DeleteFact(fact.Id); ShowRetired(); };
            row.AddChild(back);
            row.AddChild(delete);
            _retiredList.AddChild(row);
        }
    }

    private VBoxContainer _threadList = null!;

    /// <summary>What is going on with no date, for her to ask about a couple of days after it came up (see <see cref="Threads"/>); Settled closes one.</summary>
    private void ShowThreads()
    {
        foreach (var child in _threadList.GetChildren())
            child.QueueFree();
        var threads = _store.Threads();
        if (threads.Count > 0)
            _threadList.AddChild(Hint("Going on: she asks how it is going a couple of days after it comes up, once."));
        foreach (var thread in threads)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = $"{thread.Text} (since {Day(thread.Opened)})", AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                     SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var settled = new Button { Text = "Settled", ThemeTypeVariation = "Quiet", TooltipText = "It's over: she won't ask about it" };
            settled.Pressed += () => { _store.CloseThread(thread.Id, "settled in the memory book", DateTimeOffset.Now); row.QueueFree(); };
            row.AddChild(settled);
            _threadList.AddChild(row);
        }
    }

    private void ShowPlans()
    {
        foreach (var child in _planList.GetChildren())
            child.QueueFree();
        _plans.Clear();
        _deletedPlans.Clear();
        var plans = _store.Agenda();
        if (plans.Count == 0)
            _planList.AddChild(Hint("Nothing yet. Tell her about plans and dates, or ask her to remind you of something."));
        foreach (var plan in plans)
            AddPlanRow(plan);
    }

    /// <summary>
    /// One item, on two lines: when ("2026-10-09 15:00", or a date alone for a whole day) and what; then its reminder
    /// (how long before) and whether it repeats.
    /// </summary>
    private void AddPlanRow(Plan? plan)
    {
        if (plan is null && _plans.Count == 0)
            foreach (var child in _planList.GetChildren())
                child.QueueFree();                          // the "nothing yet" hint
        var now = DateTimeOffset.Now;
        var at = plan?.At ?? now.Date.AddDays(1).AddHours(9);
        var item = new VBoxContainer();
        item.AddThemeConstantOverride("separation", 4);

        var first = new HBoxContainer();
        first.AddThemeConstantOverride("separation", 6);
        var when = new LineEdit
        {
            Text = at.ToString(plan?.AllDay == true ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            CustomMinimumSize = new Vector2(150, 0),
            TooltipText = "YYYY-MM-DD HH:MM, or YYYY-MM-DD for a whole day",
        };
        when.TextChanged += t => when.Modulate = Agenda.ParseWhen(t, now, TimeZoneInfo.Local) is null ? new Color(1, 0.5f, 0.5f) : Colors.White;
        var text = new LineEdit { Text = plan?.Text ?? "", PlaceholderText = "What it is", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Take it off the agenda" };
        first.AddChild(when);
        first.AddChild(text);
        first.AddChild(delete);
        item.AddChild(first);

        var second = new HBoxContainer();
        second.AddThemeConstantOverride("separation", 6);
        second.AddChild(new Control { CustomMinimumSize = new Vector2(150, 0) });         // under the time: the what, and how it reminds
        var lead = new OptionButton { TooltipText = Words.Wrap("When its reminder goes off. For a whole day, counted from 09:00 that day.") };
        var leads = Leads.Select(l => l.Before).ToList();
        foreach (var (label, _) in Leads)
            lead.AddItem(label);
        var before = plan is null ? TimeSpan.Zero : plan.Before;
        if (!leads.Contains(before))
        {
            lead.AddItem(Agenda.Describe(before!.Value));    // what she set, though the list has no such choice
            leads.Add(before);
        }
        lead.Selected = leads.IndexOf(before);
        var repeat = new OptionButton { TooltipText = Words.Wrap("Comes round again: it moves on to its next time once it is over") };
        foreach (var (label, _) in Repeats)
            repeat.AddItem(label);
        repeat.Selected = Array.FindIndex(Repeats, r => r.Repeat == (plan?.Repeat ?? Repeat.Once));
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
            text.TooltipText = Words.Wrap("Just past: she may ask how it went. It leaves the agenda two days after.");
            second.AddChild(new Label { Text = "just past", ThemeTypeVariation = "Hint" });
        }
        item.AddChild(second);
        item.AddChild(new HSeparator());

        delete.Pressed += () =>
        {
            if (plan is not null)
                _deletedPlans.Add(plan.Id);
            _plans.RemoveAll(p => p.Row == item);
            item.QueueFree();
        };
        _planList.AddChild(item);
        _plans.Add(new PlanRow(plan, when, text, lead, leads, repeat, item));
        if (plan is null)
            text.GrabFocus();
    }

    private void AddNoteRow(Observation note)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new Label { Text = Log.Heading(note.Span, note.At), ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(78, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(new Label { Text = (note.Priority == Priority.High ? "! " : "") + note.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var delete = new Button { Text = "×", ThemeTypeVariation = "Quiet", TooltipText = "Forget this", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        delete.Pressed += () => { _deletedNotes.Add(note.Id); row.QueueFree(); };
        row.AddChild(delete);
        _noteList.AddChild(row);
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
        ShowNotes();
        ShowRetired();
    }

    private void ShowNoFacts()
    {
        if (_facts.Any(f => !_deletedFacts.Contains(f.Fact.Id)) || IsInstanceValid(_noFacts))
            return;
        _noFacts = Hint("Nothing yet. Facts appear here after you have talked a while.");
        _factList.AddChild(_noFacts);
    }

    /// <summary>The memos, newest first: where each is, whose, and Read and Pin (or Take down).</summary>
    private void ShowMemos()
    {
        foreach (var child in _memoList.GetChildren())
            child.QueueFree();
        var memos = _memos.All;
        if (memos.Count == 0)
            _memoList.AddChild(Hint("None yet. Ask her to write something down for you, or write one yourself."));
        foreach (var memo in memos)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            words.AddThemeConstantOverride("separation", 0);
            words.AddChild(new Label { Text = memo.Title, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            var where = memo.Place switch { MemoPlace.Hanging => "hanging from the slot", MemoPlace.Pinned => "pinned on the cabinet", _ => "kept" };
            words.AddChild(Hint($"{_memos.From(memo)} · {Day(memo.At)} · {where}"));
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

    private void ShowNotes()
    {
        foreach (var child in _noteList.GetChildren())
            child.QueueFree();
        var notes = _store.Observations(_character).Where(o => !o.Condensed && !_deletedNotes.Contains(o.Id)).Reverse().ToList();
        if (notes.Count == 0)
            _noteList.AddChild(Hint("Nothing yet."));
        foreach (var note in notes)
            AddNoteRow(note);
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
        foreach (var id in _deletedNotes)
            _store.DeleteObservation(id);
        var now = DateTimeOffset.Now;
        foreach (var row in _plans)
        {
            // a time that cannot be read, or nothing said: that item is left as it was
            if (Agenda.ParseWhen(row.When.Text, now, TimeZoneInfo.Local) is not { } when || row.Text.Text.Trim().Length < 2)
                continue;
            var before = row.Leads[row.Lead.Selected];
            var repeat = Repeats[row.Repeat.Selected].Repeat;
            if (row.Plan is not { } plan)
                _store.AddPlan(row.Text.Text, when.At, when.AllDay, before, now, null, repeat);
            else if (row.Text.Text.Trim() != plan.Text || when.At != plan.At || when.AllDay != plan.AllDay || before != plan.Before || repeat != plan.Repeat)
                _store.UpdatePlan(plan.Id, row.Text.Text, when.At, when.AllDay, before, repeat);
        }
        foreach (var id in _deletedPlans)
            _store.DeletePlan(id);
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

    private static TextEdit Note(VBoxContainer column, string title, string placeholder, string text)
    {
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = title, ThemeTypeVariation = "Subheading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var count = new Label { ThemeTypeVariation = "Hint" };
        head.AddChild(count);
        column.AddChild(head);
        var field = new TextEdit
        {
            Text = text, PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary,
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
        Count();
        column.AddChild(field);
        return field;
    }

    private static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };

    private static string Day(DateTimeOffset at) => at.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
