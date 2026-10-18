using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The conversation with whoever is on the stool, as it is stored: what the player said, what she said (her
/// replies and her remarks), and what happened at the cabinet, by day, newest at the bottom. The part that has left
/// her window is still here in full; the notes she works from instead (her log, see <see cref="Log"/>) are above it,
/// the part she sees of them. The talk itself is read only; a click on one of her replies opens what she had in mind for
/// it (her lists and what recall brought, see <see cref="MindStore.InMind"/>), each to forget, and facts to edit.
/// While it is open it follows the conversation: new lines come in at the bottom, and a line rewritten or taken back
/// since (her reply cut off, a cabinet line she never answered) shows as it is now. What the app tells her to do (see
/// <see cref="Line.Instruction"/>) is left out unless the player ticks to see it, and then is boxed and marked as the app's.
/// </summary>
public partial class ChatHistory : Window
{
    private const int AtOnce = 200;
    private const int Recheck = 20;                     // the newest lines shown, read again each second for rewrites

    private MindStore _store = null!;
    private string _character = "", _name = "";
    private VBoxContainer _lines = null!;
    private ScrollContainer _scroll = null!;
    private Button _earlier = null!, _below = null!;
    private long _oldestRead = long.MaxValue;           // the oldest line read, shown or not: "Show earlier" goes on from there
    // the newest line read while none is shown (an empty history, or only what the app told her): new lines are read from
    // there, and if it is taken back or cleared, the history is read afresh
    private Line? _newestRead;
    private DateOnly? _topDay;
    private Label? _topHeading;                         // the heading of the oldest day shown
    private Label? _empty;                              // "Nothing yet."
    private Label _howTo = null!;
    private const string InstructionsMark = "chathistory.instructions";
    private bool _instructions;                         // the app's instructions to her shown (see Line.Instruction)
    private CheckBox _showInstructions = null!;
    // every line shown: what it said when shown, its row, its words, and what she had in mind for it (hidden until clicked)
    private readonly Dictionary<long, (Line Line, Control Row, Label Words, Control? Mind)> _shown = [];

    /// <param name="window">Her window now, in tokens: how much of her log she sees (see <see cref="Log.Budget"/>).</param>
    public void Build(MindStore store, string characterId, string characterName, int window)
    {
        _store = store;
        _character = characterId;
        _name = characterName;
        Title = $"Chat with {characterName}";
        Visible = false;
        Transient = true;
        CloseRequested += QueueFree;

        var page = new PanelContainer();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(page);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        page.AddChild(column);
        column.AddChild(new Label { Text = $"Chat with {characterName}", ThemeTypeVariation = "Heading" });
        // shown once a reply that kept something is
        _howTo = Text($"Click one of {characterName}'s replies to see what {characterName} had in mind for it.", Paper.Faint, hint: true);
        _howTo.Visible = false;
        column.AddChild(_howTo);
        // off unless the player asks: what the app tells her to do is for her, not part of the talk (#20)
        _instructions = store.Mark(InstructionsMark) == "1";
        _showInstructions = Form.Tick(column, $"Show what the app tells {characterName}", _instructions, on =>
        {
            _instructions = on;
            _store.SetMark(InstructionsMark, on ? "1" : "0");
            Rebuild();
        });
        _showInstructions.TooltipText = Words.Wrap($"The app's own instructions to {characterName}, such as a reminder to pass on, each marked \"App to {characterName}\".");

        var log = Log.Left(store.Observations(characterId), store.Folded(characterId));
        if (log.Count > 0)
        {
            var seen = Log.Newest(log, Log.Budget(window));
            column.AddChild(new Label
            {
                Text = seen.Count < log.Count ? $"Notes from earlier: what replies work from, the newest {seen.Count} of {log.Count}" : "Notes from earlier (what replies work from)",
                ThemeTypeVariation = "Subheading",
            });
            // a few lines high: the conversation below is what the window is for
            var notes = new ScrollContainer { CustomMinimumSize = new Vector2(0, 150), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var box = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            box.AddChild(Text(Log.Laid(seen, DateTimeOffset.Now), Paper.Letters));
            notes.AddChild(box);
            column.AddChild(notes);
        }

        _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _lines.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_lines);
        column.AddChild(_scroll);
        _earlier = new Button { Text = "Show earlier", ThemeTypeVariation = "Quiet", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _earlier.Pressed += () => ShowEarlier(keepPlace: true);
        _lines.AddChild(_earlier);
        // shown when lines come in while the player has scrolled up, so their place stays put
        _below = new Button { Text = "New lines below", ThemeTypeVariation = "Quiet", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, Visible = false };
        _below.Pressed += () =>
        {
            _below.Visible = false;
            ToEnd();
        };
        column.AddChild(_below);
        _scroll.GetVScrollBar().ValueChanged += _ =>
        {
            if (AtEnd())
                _below.Visible = false;
        };

        var buttons = new HBoxContainer();
        buttons.AddChild(Text("Everything said is kept on this PC. Clear it in the memory book.", Paper.Faint, hint: true));
        var close = new Button { Text = "Close" };
        close.Pressed += QueueFree;
        buttons.AddChild(close);
        column.AddChild(buttons);

        ShowEarlier(keepPlace: false);
        var check = new Godot.Timer { WaitTime = 1, Autostart = true };
        check.Timeout += () =>
        {
            if (Visible)
                Follow();
        };
        AddChild(check);
    }

    public void Open() => Paper.Open(this, new Vector2(560, 760));

    /// <summary>The next older stretch of the conversation, above what is shown.</summary>
    private void ShowEarlier(bool keepPlace)
    {
        // a stretch of only what the app told her shows nothing: read on to the next, so "Show earlier" always shows something
        IReadOnlyList<Line> read, lines = [];
        do
        {
            read = _store.Through(_character, _oldestRead == long.MaxValue ? long.MaxValue : _oldestRead - 1, AtOnce);
            if (read.Count == 0)
                break;
            if (_oldestRead == long.MaxValue)
                _newestRead = read[^1];
            _oldestRead = read[0].Id;
            lines = Shown(read);
        } while (lines.Count == 0 && read.Count == AtOnce);
        _earlier.Visible = read.Count == AtOnce;
        if (lines.Count == 0)
        {
            if (_shown.Count == 0 && !IsInstanceValid(_empty))
                _lines.AddChild(_empty = Text("Nothing yet.", Paper.Faint, hint: true));
            return;
        }
        NotEmpty();
        var height = _scroll.GetVScrollBar().MaxValue;
        var at = 1;                                         // after the "Show earlier" button
        var first = Place(lines, ref at, day: null);
        // the block shown before began on the day this one ends with: that day continues, without a second heading
        if (_topDay == DateOnly.FromDateTime(lines[^1].At.LocalDateTime) && IsInstanceValid(_topHeading))
            _topHeading!.QueueFree();
        _topDay = DateOnly.FromDateTime(lines[0].At.LocalDateTime);
        _topHeading = first;
        Callable.From(() =>
        {
            var bar = _scroll.GetVScrollBar();
            _scroll.ScrollVertical = keepPlace ? (int)(bar.MaxValue - height) : (int)bar.MaxValue;   // stay where you were, or start at the newest
        }).CallDeferred();
    }

    /// <summary>
    /// Lines placed in the history from <paramref name="at"/>, each day under its heading (none for <paramref name="day"/>,
    /// the day already showing just above), with what she had in mind under her replies.
    /// </summary>
    /// <returns>The first heading placed.</returns>
    private Label? Place(IReadOnlyList<Line> lines, ref int at, DateOnly? day)
    {
        var minded = _store.WithInMind(lines[0].Id, lines[^1].Id);
        Label? first = null;
        foreach (var line in lines)
        {
            var date = DateOnly.FromDateTime(line.At.LocalDateTime);
            if (date != day)
            {
                day = date;
                var heading = new Label { Text = line.At.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture), ThemeTypeVariation = "Subheading" };
                first ??= heading;
                Insert(ref at, heading);
            }
            var row = Row(line, _name, out var words);
            Insert(ref at, row);
            Control? mind = null;
            if (minded.Contains(line.Id))
            {
                Insert(ref at, mind = InMind(line, row));
                _howTo.Visible = true;
            }
            _shown[line.Id] = (line, row, words, mind);
        }
        return first;
    }

    /// <summary>
    /// Each second while open: new lines at the bottom, staying at the end if the player was there, else leaving their
    /// place and saying new lines are below; and the newest lines shown as they are now, rewritten or taken back. A
    /// panel open stays open.
    /// </summary>
    private void Follow()
    {
        if (_shown.Count == 0)
        {
            // nothing shown: the newest line read, taken back or cleared since, leaves nothing to go on from
            if (_newestRead is { } last && !Same(_store.Between(_character, last.Id, last.Id).FirstOrDefault(), last))
            {
                Rebuild();
                return;
            }
        }
        else
        {
            var now = _store.Through(_character, _shown.Keys.Max(), Recheck);
            if (now.Count == 0)
            {
                Rebuild();                                  // the chat was cleared
                return;
            }
            var since = now.Count == Recheck ? now[0].Id : long.MinValue;
            foreach (var id in _shown.Keys.Where(id => id >= since).ToList())
            {
                var shown = _shown[id];
                // a line taken back leaves its number free for the next one, which is said by someone, or at a time, of its own
                if (now.FirstOrDefault(l => l.Id == id) is not { } line || !Same(line, shown.Line))
                    TakeBack(id);
                else if (line.Text != shown.Line.Text)
                {
                    shown.Words.Text = line.Text;
                    _shown[id] = shown with { Line = line };
                    GD.Print($"Chat history: line {id} shown as rewritten");
                }
            }
            if (_shown.Count == 0)
            {
                Rebuild();                                  // every line shown taken back: the chat was cleared, lines may have come in since
                return;
            }
        }

        // read on from the newest line shown, so what the app told her after it is read again each time (and left out):
        // a number it leaves free when taken back is the next line's
        var read = _store.After(_character, _shown.Count > 0 ? _shown.Keys.Max() : _newestRead?.Id ?? 0);
        if (read.Count == 0)
            return;
        if (_shown.Count == 0)
            _newestRead = read[^1];
        var added = Shown(read);
        if (added.Count == 0)                               // only what the app told her: nothing new for the player
            return;
        var atEnd = AtEnd();
        NotEmpty();
        var day = _shown.Count > 0 ? DateOnly.FromDateTime(_shown.MaxBy(s => s.Key).Value.Line.At.LocalDateTime) : (DateOnly?)null;
        var at = _lines.GetChildCount();
        var first = Place(added, ref at, day);
        if (_oldestRead == long.MaxValue)                 // the history was empty when opened
            (_oldestRead, _topDay, _topHeading) = (read[0].Id, DateOnly.FromDateTime(added[0].At.LocalDateTime), first);
        if (atEnd)
            ToEnd();
        else
            _below.Visible = true;
        GD.Print($"Chat history: {added.Count} new line{(added.Count == 1 ? "" : "s")} shown, {(atEnd ? "at the end" : "below the player's place")}");
    }

    /// <summary>A line no longer in the conversation leaves the history, and its day's heading if nothing is left under it.</summary>
    private void TakeBack(long id)
    {
        var (_, row, _, mind) = _shown[id];
        _shown.Remove(id);
        _replies.RemoveAll(r => r.Id == id);
        if (mind is { Visible: true })
            _closeOpen = null;
        foreach (var gone in new[] { row, mind })
            if (gone is not null)
            {
                _lines.RemoveChild(gone);
                gone.QueueFree();
            }
        // only the newest lines are taken back, so only the bottom day can be left empty
        if (_lines.GetChildCount() > 1 && _lines.GetChild(-1) is Label heading && heading.ThemeTypeVariation == "Subheading")
        {
            _lines.RemoveChild(heading);
            heading.QueueFree();
        }
        GD.Print($"Chat history: line {id} taken back");
    }

    /// <summary>The history afresh, from the newest line.</summary>
    private void Rebuild()
    {
        foreach (var child in _lines.GetChildren())
            if (child != _earlier)
            {
                _lines.RemoveChild(child);
                child.QueueFree();
            }
        _shown.Clear();
        _replies.Clear();
        _empty = null;                                      // freed with the rest, at the end of the frame
        (_closeOpen, _howTo.Visible, _below.Visible) = (null, false, false);
        (_oldestRead, _newestRead, _topDay, _topHeading) = (long.MaxValue, null, null, null);
        ShowEarlier(keepPlace: false);
        GD.Print("Chat history: shown afresh");
    }

    /// <summary>Once laid out, scrolls to the newest line (a panel under it stays shut, so the last thing showing).</summary>
    private void ToEnd() => Reveal(_lines.GetChildren().OfType<Control>().LastOrDefault(c => c.Visible) ?? _lines);

    /// <summary>Whether the player is at the end of the history, the newest line in view.</summary>
    private bool AtEnd()
    {
        var bar = _scroll.GetVScrollBar();
        return bar.Value >= bar.MaxValue - bar.Page - 8;
    }

    /// <summary>The same line still: said by the same one at the same time (a number taken back is reused).</summary>
    private static bool Same(Line? now, Line was) => now is not null && now.Speaker == was.Speaker && now.At == was.At;

    /// <summary>The lines the history shows of these: the app's instructions to her only when the player asked to see them.</summary>
    private IReadOnlyList<Line> Shown(IReadOnlyList<Line> lines) => _instructions ? lines : [.. lines.Where(l => !l.Instruction)];

    /// <summary>"Nothing yet." goes once there is something.</summary>
    private void NotEmpty()
    {
        if (!IsInstanceValid(_empty))
            return;
        _lines.RemoveChild(_empty);
        _empty!.QueueFree();
        _empty = null;
    }

    private void Insert(ref int at, Control item)
    {
        _lines.AddChild(item);
        _lines.MoveChild(item, at++);
    }

    private static PanelContainer Row(Line line, string name, out Label words)
    {
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        // the app's instruction to her, in a box of its own, so it is never taken for something the player said
        frame.AddThemeStyleboxOverride("panel", line.Instruction ? Instruction : Plain);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = line.At.ToString("HH:mm", CultureInfo.InvariantCulture), ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(40, 0),
                                 SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
        var (who, colour) = line.Speaker switch
        {
            Speaker.User => ("You", Paper.Letters),
            Speaker.Her => (name, Paper.Ink),
            _ when line.Instruction => ($"App to {name}", Paper.Faint),
            _ => ("Cabinet", Paper.Faint),
        };
        row.AddChild(new Label { Text = who, CustomMinimumSize = new Vector2(70, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                                 ThemeTypeVariation = line.Speaker == Speaker.Her ? "Subheading" : "Hint" });
        row.AddChild(words = Text(line.Text, colour, hint: line.Speaker == Speaker.Cabinet));
        frame.AddChild(row);
        return frame;
    }

    // what the panel showing has, for the dev actions: each memory's words and its row's buttons
    private readonly List<(string Text, Button Forget, Button? Edit, LineEdit? Field)> _shownInMind = [];
    // her replies shown that kept something, for the dev actions: their line, words and what a click does
    private readonly List<(long Id, string Text, Action Click)> _replies = [];
    private Action? _closeOpen;                         // closes the one panel open

    private static readonly StyleBoxFlat Plain = Paper.Box(Colors.Transparent, Colors.Transparent, 0, 6, 4, 2),
        Hovered = Paper.Box(new Color("F4EFFC"), new Color("F4EFFC"), 0, 6, 4, 2), Opened = Paper.Box(Paper.Wash, Paper.Wash, 0, 6, 4, 2),
        Instruction = Paper.Box(Colors.Transparent, new Color("C9BFE6"), 1, 6, 4, 2);

    /// <summary>
    /// Under one of her replies, hidden until the reply is clicked (it lights under the mouse): what she had in mind
    /// for it, read afresh each time, in two groups, her lists then what recall brought, each memory with its kind and
    /// date. One open at a time. Forget acts at once; Edit changes a fact still true. What was said stays as it was:
    /// forgetting old talk only stops it being recalled.
    /// </summary>
    private Control InMind(Line said, PanelContainer reply)
    {
        var line = said.Id;
        var holder = new MarginContainer { Visible = false };
        holder.AddThemeConstantOverride("margin_left", 52);            // under the name, past the time
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", Paper.Box(new Color("FBF6EE"), new Color("C9BFE6"), 1, 8, 12, 10));
        var panel = new VBoxContainer();
        panel.AddThemeConstantOverride("separation", 12);
        frame.AddChild(panel);
        holder.AddChild(frame);

        var hovered = false;
        void Look() => reply.AddThemeStyleboxOverride("panel", holder.Visible ? Opened : hovered ? Hovered : Plain);
        // drawn afresh on opening and after each Forget, so a group shows what is left
        void Fill(bool on)
        {
            if (on && !holder.Visible)
            {
                _closeOpen?.Invoke();
                _closeOpen = () => Fill(false);
                // the panel in view (the newest reply sits at the bottom edge), and the reply above it too, which moves
                // up when a tall panel above it closes, and wins when both don't fit
                Reveal(holder, top: reply);
            }
            else if (!on)
                _closeOpen = null;
            holder.Visible = on;
            Look();
            foreach (var child in panel.GetChildren())
                child.QueueFree();
            if (!on)
                return;
            var kept = _store.InMind(line);
            GD.Print($"Chat history: {kept.Count} in mind for line {line} ({kept.Count(k => k.Listed)} for every reply, {kept.Count(k => !k.Listed)} recalled)");
            var top = new HBoxContainer();
            top.AddChild(new Label { Text = $"What {_name} had in mind for this reply", ThemeTypeVariation = "Subheading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                                     AutowrapMode = TextServer.AutowrapMode.WordSmart });
            var close = new Button { Text = "Close", ThemeTypeVariation = "Quiet", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            close.Pressed += () => Fill(false);
            top.AddChild(close);
            panel.AddChild(top);
            foreach (var (heading, what, listed) in new[]
            {
                ("Kept in mind for every reply", $"{_name}'s most important facts, there for every reply.", true),
                ("Recalled for this reply", "Brought back by what was just said.", false),
            })
            {
                var some = kept.Where(k => k.Listed == listed).ToList();
                var group = new VBoxContainer();
                group.AddThemeConstantOverride("separation", 4);
                group.AddChild(new Label { Text = $"{heading} ({some.Count})", ThemeTypeVariation = "Subheading" });
                group.AddChild(Text(what, Paper.Faint, hint: true));
                var indent = new MarginContainer();
                indent.AddThemeConstantOverride("margin_left", 14);
                indent.AddThemeConstantOverride("margin_top", 4);
                var items = new VBoxContainer();
                items.AddThemeConstantOverride("separation", 10);
                indent.AddChild(items);
                group.AddChild(indent);
                if (some.Count == 0)
                    items.AddChild(Text("Nothing here now.", Paper.Faint, hint: true));
                foreach (var memory in some)
                    items.AddChild(Remembered(line, memory, () => Fill(true)));
                panel.AddChild(group);
            }
        }
        void Click() => Fill(!holder.Visible);

        reply.MouseFilter = Control.MouseFilterEnum.Pass;              // the wheel still scrolls the history
        reply.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        reply.MouseEntered += () => { hovered = true; Look(); };
        reply.MouseExited += () => { hovered = false; Look(); };
        reply.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Click();
                reply.AcceptEvent();
            }
        };
        _replies.Add((said.Id, said.Text, Click));
        return holder;
    }

    /// <summary>What one memory is, in words, with its date: whose fact and when it was noted, until when one was true, or where it is from.</summary>
    private string Kind(InMind memory)
    {
        var date = memory.At.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        return memory.Kind switch
        {
            Minded.You => $"About you · noted {date}",
            Minded.Us => $"About you two · noted {date}",
            Minded.Me => $"About {_name} · noted {date}",
            Minded.Was => $"No longer true · until {date}",
            Minded.Note => $"{_name}'s diary · {date}",
            _ => $"From an earlier talk · {date}",
        };
    }

    /// <summary>One thing she had in mind: what it is and its date, then its words, with Edit (a fact) and Forget (then <paramref name="forgotten"/>).</summary>
    private Control Remembered(long line, InMind memory, Action forgotten)
    {
        var item = new VBoxContainer();
        item.AddThemeConstantOverride("separation", 2);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        top.AddChild(new Label { Text = Kind(memory), ThemeTypeVariation = "Hint", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var words = Text(memory.Text, Paper.Letters);
        var field = memory.Editable ? new LineEdit { Text = memory.Text, Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill } : null;
        // still what that reply had: a forgotten fact's id can be taken by a new one, which this row must leave alone
        bool Still() => _store.InMind(line).Any(m => m.Kind == memory.Kind && m.Id == memory.Id && m.At == memory.At);
        Button? editing = null;
        if (field is not null)
        {
            var edit = editing = new Button { Text = "Edit", ThemeTypeVariation = "Quiet", TooltipText = "Correct this fact" };
            edit.Pressed += () =>
            {
                if (!field.Visible)
                {
                    (field.Visible, words.Visible, edit.Text) = (true, false, "Save");
                    field.GrabFocus();
                    return;
                }
                var text = field.Text.Trim();
                if (text.Length > 0 && text != words.Text && Still())
                {
                    _store.UpdateFact(memory.Id, text);
                    words.Text = text;
                    GD.Print($"Chat history: fact {memory.Id} edited");
                    Reveal(item);                                   // a longer fact can wrap past the bottom edge
                }
                (field.Visible, words.Visible, edit.Text) = (false, true, "Edit");
            };
            field.TextSubmitted += _ => edit.EmitSignal(BaseButton.SignalName.Pressed);
            top.AddChild(edit);
        }
        var forget = new Button
        {
            Text = "Forget", ThemeTypeVariation = "Quiet",
            TooltipText = Words.Wrap(memory.Kind == Minded.Said ? $"{_name} stops recalling this. It stays in the chat as it was." : $"{_name} forgets this, from every conversation after"),
        };
        forget.Pressed += () =>
        {
            if (Still())
                _store.ForgetInMind(memory);
            GD.Print($"Chat history: {memory.Kind} {memory.Id} forgotten");
            forgotten();
        };
        top.AddChild(forget);
        item.AddChild(top);
        item.AddChild(words);
        if (field is not null)
            item.AddChild(field);
        _shownInMind.Add((memory.Text, forget, editing, field));
        item.TreeExiting += () => _shownInMind.RemoveAll(s => s.Forget == forget);
        return item;
    }

    /// <summary>Once laid out, scrolls the history so that <paramref name="control"/> is in view, never past the top of <paramref name="top"/>.</summary>
    private async void Reveal(Control control, Control? top = null)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(control) || !control.IsVisibleInTree())
            return;
        _scroll.EnsureControlVisible(control);
        // the lines only move a frame after a scroll, so top is read by its place among them, not on screen
        if (top is not null)
            _scroll.ScrollVertical = Math.Min(_scroll.ScrollVertical, (int)top.Position.Y);
    }

    /// <summary>
    /// Dev: "Reply" clicks her newest reply shown that kept something ("Reply:words", the newest with those words in
    /// it; "Reply:2", the second newest), opening or closing what she had in mind; "Forget:words" and "Edit:words:new words" act on the memory
    /// showing whose words have those in them, as the buttons would; "Top" scrolls to the top, as the player might;
    /// "Below" presses "New lines below"; "Instructions" flips "Show what the app tells …"; "Close" closes the window.
    /// </summary>
    public void DevPress(string what)
    {
        var parts = what.Split(':', 3);
        if (parts[0] == "Close")
        {
            QueueFree();
            return;
        }
        if (parts[0] == "Instructions")
        {
            _showInstructions.ButtonPressed = !_showInstructions.ButtonPressed;
            return;
        }
        if (parts[0] == "Top")
        {
            _scroll.ScrollVertical = 0;
            return;
        }
        if (parts[0] == "Below")
        {
            if (_below.Visible)
                _below.EmitSignal(BaseButton.SignalName.Pressed);
            else
                GD.PrintErr("Chat history: no new lines below");
            return;
        }
        if (parts[0] == "Reply")
        {
            var words = parts.Length > 1 ? parts[1] : "";
            var newestFirst = _replies.OrderByDescending(r => r.Id).ToList();
            var pick = int.TryParse(words, out var back) ? newestFirst.ElementAtOrDefault(back - 1)
                : newestFirst.FirstOrDefault(r => r.Text.Contains(words, StringComparison.OrdinalIgnoreCase));
            if (pick is { Click: not null } reply)
                reply.Click();
            else
                GD.PrintErr($"Chat history: no reply with what was in mind{(words.Length > 0 ? $" saying \"{words}\"" : "")}");
            return;
        }
        if (parts.Length < 2 || _shownInMind.FirstOrDefault(s => s.Text.Contains(parts[1], StringComparison.OrdinalIgnoreCase)) is not { Forget: not null } shown)
        {
            GD.PrintErr($"Chat history: nothing showing for \"{what}\"");
            return;
        }
        if (parts[0] == "Forget")
            shown.Forget.EmitSignal(BaseButton.SignalName.Pressed);
        else if (parts[0] == "Edit" && shown is { Edit: { } edit, Field: { } field } && parts.Length == 3)
        {
            edit.EmitSignal(BaseButton.SignalName.Pressed);
            field.Text = parts[2];
            edit.EmitSignal(BaseButton.SignalName.Pressed);
        }
        else
            GD.PrintErr($"Chat history: can't \"{what}\"");
    }

    private static Label Text(string text, Color colour, bool hint = false)
    {
        var label = new Label
        {
            Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ThemeTypeVariation = hint ? "Hint" : "",
        };
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
}
