using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// What the companion says: a paper bubble over her head, drawn in the cabinet's ink. The text types itself out,
/// stays long enough to read, then fades.
/// </summary>
public partial class SpeechBubble : Node2D
{
    private static readonly Color Paper = new("FFF7EC"), Ink = new("1E1630"), Letters = new("3B2E5E");
    private static readonly Font Face = new FontVariation
    {
        BaseFont = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf"),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 560 },
    };
    private const int FontSize = 17;
    private const float MaxTextWidth = 190, PadX = 12, PadY = 8, Corner = 12, TailHeight = 13, TailHalf = 7, Stroke = 2;

    private Label _label = null!;
    private Vector2[] _shape = [];
    private Tween? _tween;

    public override void _Ready()
    {
        _label = new Label
        {
            LabelSettings = new LabelSettings { Font = Face, FontSize = FontSize, FontColor = Letters },
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(_label);
        Visible = false;
    }

    /// <param name="tip">Canvas point the tail points at (just above her head).</param>
    /// <param name="canvas">Canvas size; the bubble stays inside it.</param>
    public void Say(string text, Vector2 tip, Vector2 canvas)
    {
        var size = Face.GetMultilineStringSize(text, HorizontalAlignment.Left, MaxTextWidth, FontSize);
        var box = size + new Vector2(2 * PadX, 2 * PadY);
        // Above her head and mostly to the left of it: the cabinet is on her other side.
        var left = Mathf.Clamp(tip.X - box.X * 0.62f, 6, canvas.X - box.X - 6);
        var top = Mathf.Max(tip.Y - TailHeight - box.Y, 4);
        var rect = new Rect2(new Vector2(left, top) - tip, box);       // local: the node sits on the tail's tip

        Position = tip;
        _label.Text = text;
        _label.Position = rect.Position + new Vector2(PadX, PadY - 1);
        _label.Size = new Vector2(MaxTextWidth, size.Y);
        _shape = Outline(rect, Mathf.Clamp(-6, rect.Position.X + Corner + TailHalf, rect.End.X - Corner - TailHalf));
        QueueRedraw();

        _tween?.Kill();
        Visible = true;
        Scale = Vector2.One * 0.6f;
        Modulate = new Color(1, 1, 1, 0);
        _label.VisibleRatio = 0;
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.24).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);
        _tween.Parallel().TweenProperty(_label, "visible_ratio", 1f, 0.15 + text.Length / 55.0);
        _tween.TweenInterval(2.4 + text.Length * 0.055);
        _tween.TweenProperty(this, "modulate:a", 0f, 0.3);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    public override void _Draw()
    {
        if (_shape.Length == 0)
            return;
        DrawColoredPolygon(_shape, Paper);
        DrawPolyline([.. _shape, _shape[0]], Ink, Stroke, antialiased: true);
    }

    /// <summary>A rounded box with a tail from its bottom edge down to the origin, as one clockwise outline.</summary>
    private static Vector2[] Outline(Rect2 box, float tailX)
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
        points.Add(new Vector2(tailX + TailHalf, box.End.Y));
        points.Add(new Vector2(0, -2));
        points.Add(new Vector2(tailX - TailHalf, box.End.Y));
        Round(new Vector2(box.Position.X + Corner, box.End.Y - Corner), 90);
        return [.. points];
    }
}
