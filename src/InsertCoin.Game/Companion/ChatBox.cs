using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Where you type to her: a paper slip at the foot of the stool, written on in the cabinet's ink. Enter sends,
/// Escape (or clicking away) puts it away. The microphone at its end writes what you say into it, to change before
/// sending; when you talk to her by holding on her or a key, what she heard shows here while she answers.
/// </summary>
public partial class ChatBox : Node2D
{
    public const float Width = 300, Height = 40;
    private const float MicSize = 30;

    /// <summary>A line the user typed, trimmed and not empty.</summary>
    public event Action<string>? Submitted;
    /// <summary>The microphone was clicked: start listening, or stop and write what was said.</summary>
    public event Action? MicPressed;

    private LineEdit _field = null!;
    private Button _mic = null!;
    private MicIcon _icon = null!;
    private Tween? _tween;
    private string _placeholder = "";
    private bool _showingHeard;

    public bool IsOpen => Visible;
    public bool IsEmpty => _field.Text.Trim().Length == 0;

    public override void _Ready()
    {
        var slip = Paper.Box(Paper.Sheet, Paper.Ink, 2, 12, 12, 4);
        slip.ContentMarginRight = 8 + MicSize;                  // the text stops short of the microphone
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
            if (text.Trim().Length == 0 || !_field.Editable)
                return;
            _field.Clear();
            Submitted?.Invoke(text.Trim());
        };
        AddChild(_field);

        _mic = new Button
        {
            Position = new Vector2(Width - MicSize - 6, (Height - MicSize) / 2),
            Size = new Vector2(MicSize, MicSize),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "Talk: click, say it, click again. What you say is written here, to change before sending.",
        };
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            _mic.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        _icon = new MicIcon { Size = _mic.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _mic.AddChild(_icon);
        _mic.Pressed += () => MicPressed?.Invoke();
        AddChild(_mic);
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
        if (waiting)
            return;
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

    /// <summary>Dev: types a line and sends it, as Enter would.</summary>
    public void DevSend(string text)
    {
        _field.Text = text;
        _field.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
    }

    /// <summary>Dev: clicks the microphone.</summary>
    public void DevMic() => MicPressed?.Invoke();

    public bool Hits(Vector2 canvasPoint) => Visible && new Rect2(Position, new Vector2(Width, Height)).Grow(4).HasPoint(canvasPoint);

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
