using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// What the companion says: a paper bubble over her head, drawn in the cabinet's ink. A canned line types itself
/// out; a reply from her mind starts as thinking dots and grows as its words arrive. Either way it stays long
/// enough to read, then fades.
/// </summary>
public partial class SpeechBubble : Node2D
{
    private static readonly Font Face = Paper.Font(560);
    private const int FontSize = 17;
    private const float MaxTextWidth = 190, WidestText = 330, PadX = 12, PadY = 8, Corner = 12, TailHeight = 13, TailHalf = 7, Stroke = 2;
    private const string Dots = "...";

    private Label _label = null!;
    private Vector2[] _shape = [];
    private Tween? _tween;
    private Vector2 _tip, _canvas;
    private string _text = "";
    private bool _streaming;
    private double _thinking;

    public override void _Ready()
    {
        _label = new Label
        {
            LabelSettings = new LabelSettings { Font = Face, FontSize = FontSize, FontColor = Paper.Letters },
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(_label);
        Visible = false;
    }

    /// <param name="tip">Canvas point the tail points at (just above her head).</param>
    /// <param name="canvas">Canvas size; the bubble stays inside it.</param>
    public void Say(string text, Vector2 tip, Vector2 canvas)
    {
        _streaming = false;
        _tip = tip;
        _canvas = canvas;
        Place(text);
        Pop();
        _label.VisibleRatio = 0;
        _tween!.Parallel().TweenProperty(_label, "visible_ratio", 1f, 0.15 + text.Length / 55.0);
        HoldThenFade(text);
    }

    /// <summary>Opens a bubble with thinking dots; the reply's words follow through <see cref="Append"/>.</summary>
    public void Begin(Vector2 tip, Vector2 canvas)
    {
        _streaming = true;
        _text = "";
        _tip = tip;
        _canvas = canvas;
        _thinking = 0;
        Place(Dots);
        Pop();
    }

    public void Append(string piece)
    {
        if (!_streaming || piece.Length == 0)
            return;
        _text += piece;
        Place(_text);
    }

    /// <summary>The reply is complete: let it be read, then fade.</summary>
    public void End()
    {
        if (!_streaming)
            return;
        _streaming = false;
        _tween?.Kill();
        _tween = CreateTween();
        if (_text.Length > 0)
        {
            HoldThenFade(_text);
            return;
        }
        _tween.TweenProperty(this, "modulate:a", 0f, 0.3);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    /// <summary>Takes back whatever is showing, at once.</summary>
    public void Clear()
    {
        _streaming = false;
        _tween?.Kill();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_streaming || _text.Length > 0)
            return;
        _thinking += delta;
        _label.Text = Dots[..(1 + (int)(_thinking * 3) % 3)];        // . .. ... while she thinks
    }

    public override void _Draw()
    {
        if (_shape.Length == 0)
            return;
        DrawColoredPolygon(_shape, Paper.Sheet);
        DrawPolyline([.. _shape, _shape[0]], Paper.Ink, Stroke, antialiased: true);
    }

    /// <summary>Sizes the bubble around the text, above her head and mostly to the left of it (the cabinet is on her other side).</summary>
    private void Place(string text)
    {
        // A long reply first grows wider, so it stays above her head instead of covering it.
        var room = _tip.Y - TailHeight - 4 - 2 * PadY;
        var width = MaxTextWidth;
        var size = Measure(text, width);
        while (size.Y > room && width < WidestText)
        {
            width = Mathf.Min(width + 40, WidestText);
            size = Measure(text, width);
        }
        var box = size + new Vector2(2 * PadX, 2 * PadY);
        box.X = Mathf.Max(box.X, 2 * (Corner + TailHalf) + 10);      // room for the tail between the round corners, even for "..."
        var left = Mathf.Clamp(_tip.X - box.X * 0.62f, 6, _canvas.X - box.X - 6);
        var top = Mathf.Max(_tip.Y - TailHeight - box.Y, 4);
        var rect = new Rect2(new Vector2(left, top) - _tip, box);       // local: the node sits on the tail's tip

        Position = _tip;
        _label.Position = rect.Position + new Vector2(PadX, PadY - 1);
        _label.Size = new Vector2(width, size.Y);
        // no room above her head for the tail (the box had to stop at the top of the canvas): a plain box, not a folded tail
        var tail = rect.End.Y <= -TailHeight / 2 ? Mathf.Clamp(-6, rect.Position.X + Corner + TailHalf, rect.End.X - Corner - TailHalf) : (float?)null;
        _shape = Outline(rect, tail);
        QueueRedraw();
    }

    /// <summary>
    /// The text's size when wrapped at <paramref name="width"/>, as the label itself lays it out: its line count and
    /// line height, plus the spacing it puts between lines (which measuring with the font alone leaves out).
    /// </summary>
    private Vector2 Measure(string text, float width)
    {
        _label.Text = text;
        _label.Size = new Vector2(width, 0);
        var lines = _label.GetLineCount();
        var height = lines * _label.GetLineHeight() + (lines - 1) * _label.LabelSettings.LineSpacing;
        // widest line, for a snug bubble around short text (WordSmart: break between words, inside one only if it cannot fit)
        var widest = Face.GetMultilineStringSize(text, HorizontalAlignment.Left, width, FontSize,
            brkFlags: TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive).X;
        return new Vector2(Mathf.Min(widest, width), height);
    }

    private void Pop()
    {
        _tween?.Kill();
        Visible = true;
        Scale = Vector2.One * 0.6f;
        Modulate = new Color(1, 1, 1, 0);
        _label.VisibleRatio = 1;
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.24).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);
    }

    private void HoldThenFade(string text)
    {
        _tween!.TweenInterval(Math.Min(2.4 + text.Length * 0.055, 14));
        _tween.TweenProperty(this, "modulate:a", 0f, 0.3);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    /// <summary>A rounded box with a tail from its bottom edge down to the origin (unless there is none), as one clockwise outline.</summary>
    private static Vector2[] Outline(Rect2 box, float? tailX)
    {
        var points = new List<Vector2>();
        void Round(Vector2 centre, float fromDegrees)
        {
            for (var i = 0; i <= 6; i++)
            {
                var angle = Mathf.DegToRad(fromDegrees + i * 15);
                points.Add(centre + Corner * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }
        }
        Round(box.Position + new Vector2(Corner, Corner), 180);
        Round(new Vector2(box.End.X - Corner, box.Position.Y + Corner), 270);
        Round(box.End - new Vector2(Corner, Corner), 0);
        if (tailX is { } x)
        {
            points.Add(new Vector2(x + TailHalf, box.End.Y));
            points.Add(new Vector2(0, -2));
            points.Add(new Vector2(x - TailHalf, box.End.Y));
        }
        Round(new Vector2(box.Position.X + Corner, box.End.Y - Corner), 90);
        return [.. points];
    }
}
