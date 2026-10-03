using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Where you type to her: a paper slip at the foot of the stool, written on in the cabinet's ink. Enter sends,
/// Escape (or clicking away) puts it away.
/// </summary>
public partial class ChatBox : Node2D
{
    public const float Width = 300, Height = 40;

    /// <summary>A line the user typed, trimmed and not empty.</summary>
    public event Action<string>? Submitted;

    private LineEdit _field = null!;
    private Tween? _tween;

    public bool IsOpen => Visible;
    public bool IsEmpty => _field.Text.Trim().Length == 0;

    public override void _Ready()
    {
        var slip = Paper.Box(Paper.Sheet, Paper.Ink, 2, 12, 12, 4);
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
        Visible = false;
    }

    /// <param name="at">Canvas point of the slip's top-left corner.</param>
    public void Open(Vector2 at, string placeholder)
    {
        Position = at;
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
        if (!waiting && Visible)
            _field.GrabFocus();
    }

    /// <summary>Dev: types a line and sends it, as Enter would.</summary>
    public void DevSend(string text)
    {
        _field.Text = text;
        _field.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
    }

    public bool Hits(Vector2 canvasPoint) => Visible && new Rect2(Position, new Vector2(Width, Height)).Grow(4).HasPoint(canvasPoint);
}
