using Godot;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Where you type to her: a paper slip at the foot of the stool, written on in the cabinet's ink. Enter sends,
/// Escape (or clicking away) puts it away. The microphone at its end writes what you say into it, to change before
/// sending; when you talk to her by holding on her or a key, what she heard shows here while she answers. The paper
/// clip beside it hands her things with what you say (a picture or a file, your screen, a photo from your phone), and
/// so do pasting and dropping them on her: each waits as a chip above the slip until the message goes.
/// </summary>
public partial class ChatBox : Node2D
{
    public const float Width = 300, Height = 40;
    private const float MicSize = 30, ClipSize = 26, RowHeight = 34;

    /// <summary>The most things handed over with one message.</summary>
    public const int MostHanded = 4;

    /// <summary>A line the user typed, trimmed (empty when only things are handed over).</summary>
    public event Action<string>? Submitted;
    /// <summary>The microphone was clicked: start listening, or stop and write what was said.</summary>
    public event Action? MicPressed;
    /// <summary>One of the clip's ways was chosen: "file" (a picture or a file), "screen" (their screen now), "phone" (a photo from their phone).</summary>
    public event Action<string>? Attaching;
    /// <summary>Ctrl+V with a picture on the clipboard (and no text).</summary>
    public event Action<Image>? PastedPicture;
    /// <summary>Ctrl+V with files copied in Explorer on the clipboard.</summary>
    public event Action<string[]>? PastedFiles;

    private LineEdit _field = null!;
    private Button _mic = null!, _clip = null!;
    private MicIcon _icon = null!;
    private HBoxContainer _row = null!, _ways = null!;
    private readonly List<(Attachment Thing, Control Chip)> _handed = [];
    private Tween? _tween;
    private string _placeholder = "";
    private bool _showingHeard;

    public bool IsOpen => Visible;

    /// <summary>How much of the foot of the stool it takes up: the slip, and the row of chips or ways above it when there is one.</summary>
    public float Tall => Height + (_ways.Visible || _row.Visible && _row.GetChildCount() > 0 ? RowHeight + 6 : 0);
    public bool IsEmpty => _field.Text.Trim().Length == 0 && _handed.Count == 0;

    /// <summary>What waits to go with the next message.</summary>
    public IReadOnlyList<Attachment> Handed => [.. _handed.Select(h => h.Thing)];

    public override void _Ready()
    {
        var slip = Paper.Box(Paper.Sheet, Paper.Ink, 2, 12, 12, 4);
        slip.ContentMarginRight = 8 + MicSize + ClipSize;       // the text stops short of the clip and the microphone
        _field = new LineEdit
        {
            Size = new Vector2(Width, Height),
            MaxLength = 400,
            ContextMenuEnabled = false,
            CaretBlink = true,
        };
        _field.AddThemeStyleboxOverride("normal", slip);
        _field.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _field.AddThemeStyleboxOverride("read_only", slip);
        _field.AddThemeFontOverride("font", Paper.Font(500));
        _field.AddThemeFontSizeOverride("font_size", 17);
        _field.AddThemeColorOverride("font_color", Paper.Letters);
        _field.AddThemeColorOverride("font_uneditable_color", Paper.Faint);
        _field.AddThemeColorOverride("font_placeholder_color", Paper.Faint);
        _field.AddThemeColorOverride("caret_color", Paper.Ink);
        _field.AddThemeColorOverride("selection_color", Paper.Pink);
        _field.TextSubmitted += text =>
        {
            if (text.Trim().Length == 0 && _handed.Count == 0 || !_field.Editable)
                return;
            _field.Clear();
            Submitted?.Invoke(text.Trim());
        };
        _field.GuiInput += Paste;
        AddChild(_field);

        _clip = IconButton(new ClipIcon(), Width - MicSize - ClipSize - 8, ClipSize, "Hand something over with what you say: a picture or a file, your screen, a photo from your phone");
        _clip.Pressed += () => ShowWays(!_ways.Visible);
        _mic = IconButton(_icon = new MicIcon(), Width - MicSize - 6, MicSize, "Talk: click, say it, click again. What you say is written here, to change before sending.");
        _mic.Pressed += () => MicPressed?.Invoke();

        // above the slip: the chips of what waits to go, or the clip's ways to choose from
        _row = new HBoxContainer { Position = new Vector2(0, -RowHeight - 6) };
        _row.AddThemeConstantOverride("separation", 6);
        AddChild(_row);
        _ways = new HBoxContainer { Position = _row.Position, Visible = false };
        _ways.AddThemeConstantOverride("separation", 6);
        foreach (var (label, how) in new[] { ("Picture or file…", "file"), ("Your screen", "screen"), ("From your phone…", "phone") })
        {
            var way = Tag(label);
            way.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            way.GuiInput += input =>
            {
                if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    return;
                ShowWays(false);
                Attaching?.Invoke(how);
            };
            _ways.AddChild(way);
        }
        AddChild(_ways);
        Visible = false;
    }

    /// <param name="at">Canvas point of the slip's top-left corner.</param>
    /// <param name="focus">Put the caret in it, for typing (not when she was talked to with a key from another app).</param>
    public void Open(Vector2 at, string placeholder, bool focus = true)
    {
        Position = at;
        _placeholder = placeholder;
        if (!_icon.Listening)
            _field.PlaceholderText = placeholder;
        if (!Visible)
        {
            _tween?.Kill();
            Visible = true;
            Scale = new Vector2(1, 0.4f);
            Modulate = new Color(1, 1, 1, 0);
            _tween = CreateTween();
            _tween.TweenProperty(this, "scale", Vector2.One, 0.2).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _tween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);
        }
        if (focus)
            _field.GrabFocus();
    }

    public void Close()
    {
        if (!Visible)
            return;
        ShowWays(false);
        _field.ReleaseFocus();
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(this, "modulate:a", 0f, 0.15);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    /// <summary>While she answers the slip stays, greyed, so a second message does not cut her off.</summary>
    public void SetWaiting(bool waiting)
    {
        _field.Editable = !waiting;
        _mic.Disabled = waiting;
        _clip.Disabled = waiting;
        if (waiting)
        {
            ShowWays(false);
            return;
        }
        if (_showingHeard)
        {
            _field.Clear();                                     // what she heard has been answered
            _showingHeard = false;
        }
        if (Visible)
            _field.GrabFocus();
    }

    /// <summary>She is listening (the microphone lights up, with how loud it is), or has stopped.</summary>
    public void SetListening(bool listening)
    {
        _icon.Listening = listening;
        _icon.Level = 0;
        _field.PlaceholderText = listening ? "Listening…" : _placeholder;
    }

    public void SetLevel(float level) => _icon.Level = level;

    /// <summary>Words said into the microphone, written in where the caret is, to change before sending.</summary>
    public void Insert(string words)
    {
        var before = _field.Text[..Math.Min(_field.CaretColumn, _field.Text.Length)];
        _field.InsertTextAtCaret(before.Length > 0 && !before.EndsWith(' ') && char.IsAsciiLetterOrDigit(before[^1]) ? " " + words : words);
        _field.GrabFocus();
    }

    /// <summary>What she heard (by holding on her, or the talk key), shown greyed while she answers it.</summary>
    public void ShowHeard(string words)
    {
        _field.Text = words;
        _showingHeard = true;
    }

    /// <summary>Something to go with the next message, as a chip above the slip (with a little picture of it, if it is one); false when there are already <see cref="MostHanded"/>.</summary>
    public bool Hand(Attachment thing, Texture2D? thumb)
    {
        if (_handed.Count >= MostHanded)
            return false;
        ShowWays(false);
        var chip = Tag(thing.Name.Length > 16 ? thing.Name[..13] + "…" : thing.Name, thumb ?? null, thumb is null ? Badge(thing) : null);
        var close = new Button { Text = "×", Flat = true, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = "Take it back" };
        close.AddThemeFontOverride("font", Paper.Font(600));
        close.AddThemeFontSizeOverride("font_size", 16);
        close.AddThemeColorOverride("font_color", Paper.Faint);
        close.AddThemeColorOverride("font_hover_color", Paper.Ink);
        chip.GetChild(0).AddChild(close);
        _row.AddChild(chip);
        _handed.Add((thing, chip));
        close.Pressed += () =>
        {
            _handed.RemoveAll(h => h.Chip == chip);
            chip.QueueFree();
        };
        return true;
    }

    /// <summary>What waits to go, now going with a message: the chips leave the slip.</summary>
    public IReadOnlyList<Attachment> TakeHanded()
    {
        var handed = Handed;
        foreach (var (_, chip) in _handed)
            chip.QueueFree();
        _handed.Clear();
        return handed;
    }

    /// <summary>Dev: types a line and sends it, as Enter would.</summary>
    public void DevSend(string text)
    {
        _field.Text = text;
        _field.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
    }

    /// <summary>Dev: clicks the microphone.</summary>
    public void DevMic() => MicPressed?.Invoke();

    /// <summary>Dev: clicks the clip (its ways show, or hide).</summary>
    public void DevClip() => ShowWays(!_ways.Visible);

    public bool Hits(Vector2 canvasPoint)
    {
        if (!Visible)
            return false;
        if (new Rect2(Position, new Vector2(Width, Height)).Grow(4).HasPoint(canvasPoint))
            return true;
        var row = _ways.Visible ? _ways : _row;
        return row.GetChildCount() > 0 && new Rect2(Position + row.Position, row.Size).Grow(4).HasPoint(canvasPoint);
    }

    /// <summary>Ctrl+V: text pastes as usual; with no text on the clipboard, a picture or files copied in Explorer are handed over.</summary>
    private void Paste(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Keycode: Key.V } key || !key.IsCommandOrControlPressed() || !_field.Editable
            || DisplayServer.ClipboardHas())
            return;
        if (DisplayServer.ClipboardHasImage() && DisplayServer.ClipboardGetImage() is { } picture && !picture.IsEmpty())
            PastedPicture?.Invoke(picture);
        else if (ClipboardFiles.Get() is { Length: > 0 } files)
            PastedFiles?.Invoke(files);
        else
            return;
        _field.AcceptEvent();
    }

    private void ShowWays(bool show)
    {
        _ways.Visible = show;
        _row.Visible = !show;
    }

    private Button IconButton(Control icon, float x, float size, string tooltip)
    {
        var button = new Button
        {
            Position = new Vector2(x, (Height - size) / 2),
            Size = new Vector2(size, size),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = tooltip,
        };
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        icon.Size = button.Size;
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        button.AddChild(icon);
        AddChild(button);
        return button;
    }

    /// <summary>A small paper tag: a little picture or a badge, then words.</summary>
    private static PanelContainer Tag(string words, Texture2D? picture = null, string? badge = null)
    {
        var tag = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        tag.AddThemeStyleboxOverride("panel", Paper.Box(Paper.Sheet, Paper.Ink, 2, 10, 8, 2));
        var inside = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        inside.AddThemeConstantOverride("separation", 5);
        tag.AddChild(inside);
        if (picture is not null)
            inside.AddChild(new TextureRect
            {
                Texture = picture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(26, 26), MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        else if (badge is not null)
        {
            var mark = new Label { Text = badge, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            mark.AddThemeFontOverride("font", Paper.Font(700));
            mark.AddThemeFontSizeOverride("font_size", 11);
            mark.AddThemeColorOverride("font_color", Paper.Accent);
            inside.AddChild(mark);
        }
        var label = new Label { Text = words, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Paper.Font(500));
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", Paper.Letters);
        inside.AddChild(label);
        return tag;
    }

    /// <summary>What a chip without a little picture says it is: "PDF", "PY", "TXT".</summary>
    private static string Badge(Attachment thing) =>
        thing.Pdf ? "PDF" : Path.GetExtension(thing.Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 and <= 4 } ending ? ending : "TXT";

    /// <summary>A paper clip in the cabinet's ink.</summary>
    private partial class ClipIcon : Control
    {
        public override void _Draw()
        {
            var c = Size / 2;
            var ink = Paper.Letters;
            // an outer loop, and an inner one inside it, as a paper clip is bent (upright, slightly turned)
            DrawSetTransform(c, -0.5f, Vector2.One);
            DrawArc(new Vector2(0, -5), 4f, Mathf.Pi, Mathf.Tau, 12, ink, 1.6f, true);
            DrawLine(new Vector2(-4, -5), new Vector2(-4, 6), ink, 1.6f, true);
            DrawLine(new Vector2(4, -5), new Vector2(4, 4), ink, 1.6f, true);
            DrawArc(new Vector2(-1, 6), 3f, 0, Mathf.Pi, 10, ink, 1.6f, true);
            DrawLine(new Vector2(2, 6), new Vector2(2, -3), ink, 1.6f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>A microphone in the cabinet's ink; lit pink, with a ring as loud as the voice, while she listens.</summary>
    private partial class MicIcon : Control
    {
        private bool _listening;
        private float _level;

        public bool Listening
        {
            get => _listening;
            set
            {
                _listening = value;
                QueueRedraw();
            }
        }

        public float Level
        {
            get => _level;
            set
            {
                _level = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            var c = Size / 2;
            if (_listening)
                DrawCircle(c, 9 + 5 * _level, Paper.Pink);
            var ink = _listening ? Paper.Ink : Paper.Letters;
            // the head: a capsule
            DrawCircle(new Vector2(c.X, c.Y - 6.5f), 3.5f, ink);
            DrawRect(new Rect2(c.X - 3.5f, c.Y - 6.5f, 7, 4), ink);
            DrawCircle(new Vector2(c.X, c.Y - 2.5f), 3.5f, ink);
            // the holder, the stem and the foot
            DrawArc(new Vector2(c.X, c.Y - 2.5f), 6.5f, 0, Mathf.Pi, 16, ink, 1.6f, true);
            DrawLine(new Vector2(c.X, c.Y + 4), new Vector2(c.X, c.Y + 8), ink, 1.6f, true);
            DrawLine(new Vector2(c.X - 3.5f, c.Y + 8), new Vector2(c.X + 3.5f, c.Y + 8), ink, 1.6f, true);
        }
    }
}
