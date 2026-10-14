using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The conversation with whoever is on the stool, as it is stored: what the player said, what she said (her
/// replies and her remarks), and what happened at the cabinet, by day, newest at the bottom. The part that has left
/// her window is still here in full; the notes she works from instead (her log, see <see cref="Log"/>) are above it,
/// the part she sees of them. The talk itself is read only; under each of her replies, what she had in mind for it (her
/// lists and what recall brought, see <see cref="MindStore.InMind"/>), each to forget, and facts to edit.
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
            Insert(ref at, Row(line));
            if (minded.Contains(line.Id))
                Insert(ref at, InMind(line.Id));
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

    private Control Row(Line line)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = line.At.ToString("HH:mm", CultureInfo.InvariantCulture), ThemeTypeVariation = "Hint", CustomMinimumSize = new Vector2(40, 0),
                                 SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
        var (who, colour) = line.Speaker switch
        {
            Speaker.User => ("You", Paper.Letters),
            Speaker.Her => (_name, Paper.Ink),
            _ => ("Cabinet", Paper.Faint),
        };
        row.AddChild(new Label { Text = who, CustomMinimumSize = new Vector2(70, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                                 ThemeTypeVariation = line.Speaker == Speaker.Her ? "Subheading" : "Hint" });
        row.AddChild(Text(line.Text, colour, hint: line.Speaker == Speaker.Cabinet));
        return row;
    }

    // what the panels showing have, for the dev actions: each memory's words and its row's buttons
    private readonly List<(string Text, Button Forget, Button? Edit, LineEdit? Field)> _shownInMind = [];

    /// <summary>
    /// Under one of her replies: a button that opens what she had in mind for it, read afresh each time, her lists first,
    /// then what recall brought, each with its kind and date. Forget acts at once; Edit changes a fact still true. What
    /// was said stays as it was: forgetting old talk only stops it being recalled.
    /// </summary>
    private Control InMind(long line)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        var open = new Button { Text = $"From {_name}'s memory", ThemeTypeVariation = "Quiet", ToggleMode = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                                TooltipText = Words.Wrap($"What {_name} had in mind for this reply: forget any of it, or correct a fact.") };
        var panel = new VBoxContainer { Visible = false };
        panel.AddThemeConstantOverride("separation", 6);
        column.AddChild(open);
        column.AddChild(panel);
        // drawn afresh on opening and after each Forget, so no heading stays with nothing under it
        void Fill(bool on)
        {
            panel.Visible = on;
            foreach (var child in panel.GetChildren())
                child.QueueFree();
            if (!on)
                return;
            var kept = _store.InMind(line);
            GD.Print($"Chat history: {kept.Count} in mind for line {line} ({string.Join(", ", kept.Select(k => (k.Listed ? "listed " : "") + Kind(k.Kind)))})");
            if (kept.Count == 0)
                panel.AddChild(Text("Nothing left: all of it was forgotten since.", Paper.Faint, hint: true));
            foreach (var (heading, listed) in new[] { ("Always in mind", true), ("Recalled for this reply", false) })
            {
                var some = kept.Where(k => k.Listed == listed).ToList();
                if (some.Count == 0)
                    continue;
                panel.AddChild(new Label { Text = heading, ThemeTypeVariation = "Hint" });
                foreach (var memory in some)
                    panel.AddChild(Remembered(line, memory, () => Fill(true)));
            }
        }
        open.Toggled += Fill;
        // under her words, past the time and the name
        var indent = new HBoxContainer();
        indent.AddChild(new Control { CustomMinimumSize = new Vector2(126, 0) });
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        indent.AddChild(column);
        return indent;
    }

    private static string Kind(Minded kind) => kind switch
    {
        Minded.You => "you", Minded.Us => "us", Minded.Me => "me", Minded.Was => "until", Minded.Note => "diary", _ => "chat",
    };

    /// <summary>One thing she had in mind: whose or what it is and its date, then its words, with Edit (a fact) and Forget (then <paramref name="forgotten"/>).</summary>
    private Control Remembered(long line, InMind memory, Action forgotten)
    {
        var item = new VBoxContainer();
        item.AddThemeConstantOverride("separation", 2);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        var about = memory.Kind switch
        {
            Minded.You => "A fact about you",
            Minded.Us => "About the two of you",
            Minded.Me => $"From {_name}'s own story, as told to you",
            Minded.Was => "No longer true since this date",
            Minded.Note => $"A note from {_name}'s diary",
            _ => $"Said then, recalled word for word. It stays in the chat as it was: Forget only stops {_name} recalling it.",
        };
        top.AddChild(new Label { Text = $"{Kind(memory.Kind)} · {memory.At.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}", ThemeTypeVariation = "Hint",
                                 SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass, TooltipText = Words.Wrap(about) });
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
                }
                (field.Visible, words.Visible, edit.Text) = (false, true, "Edit");
            };
            field.TextSubmitted += _ => edit.EmitSignal(BaseButton.SignalName.Pressed);
            top.AddChild(edit);
        }
        var forget = new Button { Text = "Forget", ThemeTypeVariation = "Quiet", TooltipText = $"{_name} forgets this, from every conversation after" };
        forget.Pressed += () =>
        {
            if (Still())
                _store.ForgetInMind(memory);
            GD.Print($"Chat history: {Kind(memory.Kind)} {memory.Id} forgotten");
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

    /// <summary>
    /// Dev: "From" opens what she had in mind for her newest reply shown; "Forget:words" and "Edit:words:new words" act
    /// on the memory showing whose words have those in them, as the buttons would; "Close" closes the window.
    /// </summary>
    public void DevPress(string what)
    {
        var parts = what.Split(':', 3);
        if (parts[0] == "Close")
        {
            QueueFree();
            return;
        }
        if (parts[0] == "From")
        {
            if (FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().LastOrDefault(b => b.Text.StartsWith("From ")) is { } from)
                from.ButtonPressed = !from.ButtonPressed;
            else
                GD.PrintErr("Chat history: no reply with what was in mind");
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
