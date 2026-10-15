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
/// </summary>
public partial class ChatHistory : Window
{
    private const int AtOnce = 200;

    private MindStore _store = null!;
    private string _character = "", _name = "";
    private VBoxContainer _lines = null!;
    private ScrollContainer _scroll = null!;
    private Button _earlier = null!;
    private long _oldestShown = long.MaxValue;
    private DateOnly? _topDay;
    private Label? _topHeading;                         // the heading of the oldest day shown
    private Label _howTo = null!;

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

        var buttons = new HBoxContainer();
        buttons.AddChild(Text("Everything said is kept on this PC. Clear it in the memory book.", Paper.Faint, hint: true));
        var close = new Button { Text = "Close" };
        close.Pressed += QueueFree;
        buttons.AddChild(close);
        column.AddChild(buttons);

        ShowEarlier(keepPlace: false);
    }

    public void Open() => Paper.Open(this, new Vector2(560, 760));

    /// <summary>The next older stretch of the conversation, above what is shown.</summary>
    private void ShowEarlier(bool keepPlace)
    {
        var lines = _store.Through(_character, _oldestShown == long.MaxValue ? long.MaxValue : _oldestShown - 1, AtOnce);
        _earlier.Visible = lines.Count == AtOnce;
        if (lines.Count == 0)
        {
            if (_oldestShown == long.MaxValue)
                _lines.AddChild(Text("Nothing yet.", Paper.Faint, hint: true));
            return;
        }
        _oldestShown = lines[0].Id;
        var minded = _store.WithInMind(lines[0].Id, lines[^1].Id);
        var height = _scroll.GetVScrollBar().MaxValue;
        var at = 1;                                         // after the "Show earlier" button
        var day = (DateOnly?)null;
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
            var row = Row(line, _name);
            Insert(ref at, row);
            if (minded.Contains(line.Id))
            {
                Insert(ref at, InMind(line, row));
                _howTo.Visible = true;
            }
        }
        // the block shown before began on the day this one ends with: that day continues, without a second heading
        if (_topDay == day && IsInstanceValid(_topHeading))
            _topHeading!.QueueFree();
        _topDay = DateOnly.FromDateTime(lines[0].At.LocalDateTime);
        _topHeading = first;
        Callable.From(() =>
        {
            var bar = _scroll.GetVScrollBar();
            _scroll.ScrollVertical = keepPlace ? (int)(bar.MaxValue - height) : (int)bar.MaxValue;   // stay where you were, or start at the newest
        }).CallDeferred();
    }

    private void Insert(ref int at, Control item)
    {
        _lines.AddChild(item);
        _lines.MoveChild(item, at++);
    }

    private static PanelContainer Row(Line line, string name)
    {
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", Plain);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = line.At.ToString("HH:mm", CultureInfo.InvariantCulture), ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(40, 0),
                                 SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
        var (who, colour) = line.Speaker switch
        {
            Speaker.User => ("You", Paper.Letters),
            Speaker.Her => (name, Paper.Ink),
            _ => ("Cabinet", Paper.Faint),
        };
        row.AddChild(new Label { Text = who, CustomMinimumSize = new Vector2(70, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                                 ThemeTypeVariation = line.Speaker == Speaker.Her ? "Subheading" : "Hint" });
        row.AddChild(Text(line.Text, colour, hint: line.Speaker == Speaker.Cabinet));
        frame.AddChild(row);
        return frame;
    }

    // what the panel showing has, for the dev actions: each memory's words and its row's buttons
    private readonly List<(string Text, Button Forget, Button? Edit, LineEdit? Field)> _shownInMind = [];
    // her replies shown that kept something, for the dev actions: their line, words and what a click does
    private readonly List<(long Id, string Text, Action Click)> _replies = [];
    private Action? _closeOpen;                         // closes the one panel open

    private static readonly StyleBoxFlat Plain = Paper.Box(Colors.Transparent, Colors.Transparent, 0, 6, 4, 2),
        Hovered = Paper.Box(new Color("F4EFFC"), new Color("F4EFFC"), 0, 6, 4, 2), Opened = Paper.Box(Paper.Wash, Paper.Wash, 0, 6, 4, 2);

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
    /// showing whose words have those in them, as the buttons would; "Close" closes the window.
    /// </summary>
    public void DevPress(string what)
    {
        var parts = what.Split(':', 3);
        if (parts[0] == "Close")
        {
            QueueFree();
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
