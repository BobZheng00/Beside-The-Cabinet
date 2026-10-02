using Godot;
using InsertCoin.Core;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The game-over receipt: when a session ends, a strip of paper prints out of the slot with what was played and what
/// it cost, hangs for a while, then tears off and falls.
/// </summary>
public partial class Receipt : Node2D
{
    private const int PxPerUnit = 3;
    private static readonly Color Ink = new("6a1c5e");
    private static readonly Color Faint = new("b89ab0");
    private static readonly Color Red = new("c2185b");
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
    private static readonly Font Round = new FontVariation
    {
        BaseFont = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf"),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 650 },
    };

    private SubViewport _viewport = null!;
    private Paper _paper = null!;
    private Polygon2D _strip = null!;
    private ShaderMaterial _material = null!;
    private Tween? _tween;

    public void Build(CabinetSkin skin)
    {
        var size = (Vector2I)(skin.Anchors.ReceiptSize * PxPerUnit);
        _viewport = new SubViewport { Size = size, TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(_viewport);
        _paper = new Paper { Size = size };
        _viewport.AddChild(_paper);

        var (r0, r1, r2) = Homography.QuadToUnit(skin.Anchors.ReceiptQuad);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/receipt_quad.gdshader") };
        _material.SetShaderParameter("content", _viewport.GetTexture());
        _material.SetShaderParameter("inv_h_row0", r0);
        _material.SetShaderParameter("inv_h_row1", r1);
        _material.SetShaderParameter("inv_h_row2", r2);
        _strip = new Polygon2D { Polygon = [.. skin.Anchors.ReceiptQuad], Material = _material, Visible = false };
        AddChild(_strip);
    }

    public void Print(Session session, Rules rules)
    {
        _paper.Fill(session, rules);
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        _tween?.Kill();
        _strip.Visible = true;
        _strip.Position = Vector2.Zero;
        _strip.Rotation = 0;
        _strip.Modulate = Colors.White;
        SetReveal(0);

        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(SetReveal), 0f, 1f, 1.3);
        _tween.TweenInterval(9.0);
        // tear off: fall and fade together, after the hold
        _tween.TweenProperty(_strip, "position", new Vector2(10, 120), 0.8).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        _tween.Parallel().TweenProperty(_strip, "modulate:a", 0f, 0.8);
        _tween.TweenCallback(Callable.From(() => _strip.Visible = false));
    }

    private void SetReveal(float reveal) => _material.SetShaderParameter("reveal", reveal);

    /// <summary>The paper itself: outline with a torn zigzag edge, and the printed lines.</summary>
    private partial class Paper : Control
    {
        private readonly List<Label> _lines = [];

        public void Fill(Session session, Rules rules)
        {
            foreach (var line in _lines)
                line.QueueFree();
            _lines.Clear();

            var played = (int)(session.PlayedMs / 60_000);
            var tokens = (int)Math.Ceiling(session.DrawnMs / (double)rules.TokenMs);
            Line(string.Join(" + ", session.GameNames), Round, 17, 10, Ink, clip: true);
            Line("PLAYED", Round, 17, 50, Faint);
            Line($"{played / 60}:{played % 60:00}", Pixel, 24, 70, Ink);
            Line("TOKENS", Round, 17, 114, Faint);
            Line($"x{tokens}", Pixel, 24, 134, Ink);
            Line("IOU", Round, 17, 178, Faint);
            Line($"x{session.IousPrinted}", Pixel, 24, 198, session.IousPrinted > 0 ? Red : Ink);
            Line("THANK YOU", Round, 15, 250, Faint);
            QueueRedraw();
        }

        private void Line(string text, Font font, int size, float y, Color colour, bool clip = false)
        {
            var label = new Label
            {
                Text = text,
                Position = new Vector2(8, y),
                Size = new Vector2(Size.X - 16, size + 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipText = clip,
                TextOverrunBehavior = clip ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming,
                LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = colour },
            };
            AddChild(label);
            _lines.Add(label);
        }

        public override void _Draw()
        {
            const float tooth = 9f;
            var w = Size.X;
            var bottom = Size.Y - tooth;
            var shape = new List<Vector2> { new(0, 0), new(w, 0), new(w, bottom) };
            var teeth = (int)Math.Round(w / (tooth * 1.6f));
            for (var i = teeth; i >= 1; i--)
            {
                shape.Add(new Vector2(w * (i - 0.5f) / teeth, Size.Y));
                shape.Add(new Vector2(w * (i - 1f) / teeth, bottom));
            }
            DrawColoredPolygon([.. shape], new Color("fff4e6"));
            shape.Add(shape[0]);
            DrawPolyline([.. shape], new Color("1e1630"), 5f, antialiased: true);
            foreach (var y in new[] { 40f, 240f })
                DrawDashedLine(new Vector2(12, y), new Vector2(w - 12, y), Faint, 2.5f, 7f);
        }
    }
}
