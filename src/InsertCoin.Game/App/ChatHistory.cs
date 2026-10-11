using System.Globalization;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The conversation with whoever is on the stool, as it is stored: what the player said, what she said (her
/// replies and her remarks), and what happened at the cabinet, by day, newest at the bottom. The part that has left
/// her window is still here in full; the notes she works from instead (her log, see <see cref="Log"/>) are above it,
/// the part she sees of them. Read only.
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
                Text = seen.Count < log.Count ? $"Her notes from earlier: what she works from, the newest {seen.Count} of {log.Count}" : "Her notes from earlier (what she works from)",
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
