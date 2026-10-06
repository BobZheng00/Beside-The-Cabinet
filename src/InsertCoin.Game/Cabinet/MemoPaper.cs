using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// One memo's paper on the cabinet: cream, with a torn bottom edge, "MEMO" in the cabinet's pixel letters, its title
/// and its first words; taped up with a strip of pink tape when on the side. The paper is drawn once into a texture
/// with mipmaps and projected onto its quad, like the receipt; its outline and tape are drawn on the cabinet itself,
/// through the same projection, so the ink line is as even as the art's however steep the face.
/// </summary>
public partial class MemoPaper : Node2D
{
    private const int PxPerUnit = 3;
    private const float Tape = 6, ToothDepth = 2.6f, ToothWidth = 4.4f;       // face units
    private const float InkWidth = 2.4f;                                     // canvas units: the art's thin line
    private static readonly Color Ink = new("1e1630"), TapePink = new(1f, 0.66f, 0.8f, 0.78f);

    private readonly double[,] _toQuad;
    private readonly Vector2 _size;
    private readonly bool _taped;
    private readonly SubViewport _viewport;
    private readonly Sheet _sheet;
    private readonly Polygon2D _paper, _tape;
    private readonly Line2D _outline;
    private readonly ShaderMaterial _material;
    private Tween? _tween;
    private int _drawing;

    /// <param name="quad">Where it goes on the canvas (TL, TR, BR, BL), tape included.</param>
    /// <param name="size">Its size in the face's own units, tape included.</param>
    public MemoPaper(Vector2[] quad, Vector2 size, bool taped)
    {
        Quad = quad;
        _size = size;
        _taped = taped;
        _toQuad = Homography.UnitToQuad(quad);
        var pixels = (Vector2I)(size * PxPerUnit);
        _viewport = new SubViewport { Size = pixels, TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(_viewport);
        _sheet = new Sheet(this) { Size = pixels };
        _viewport.AddChild(_sheet);
        var (r0, r1, r2) = Homography.QuadToUnit(quad);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/memo_quad.gdshader") };
        _material.SetShaderParameter("inv_h_row0", r0);
        _material.SetShaderParameter("inv_h_row1", r1);
        _material.SetShaderParameter("inv_h_row2", r2);
        _paper = new Polygon2D { Polygon = [.. quad], Material = _material };
        _outline = new Line2D { Width = InkWidth, DefaultColor = Ink, JointMode = Line2D.LineJointMode.Round, Antialiased = true, Closed = taped };
        _tape = new Polygon2D { Color = TapePink, Antialiased = true, Visible = taped };
        AddChild(_paper);
        AddChild(_outline);
        AddChild(_tape);
        _tape.Polygon = [.. new[] { new Vector2(0.22f, 0.01f), new Vector2(0.80f, 0.04f), new Vector2(0.78f, Tape * 2.1f / size.Y), new Vector2(0.20f, Tape * 1.9f / size.Y) }
                            .Select(OnCanvas)];
        SetReveal(1);
        Visible = false;
    }

    public Vector2[] Quad { get; }

    /// <summary>The memo on it, or null.</summary>
    public long? Memo { get; private set; }

    public async void Show(MemoBoard.Shown memo)
    {
        var again = Memo == memo.Id && Visible;
        Memo = memo.Id;
        _tween?.Kill();
        Position = Vector2.Zero;
        Modulate = Colors.White;
        SetReveal(1);
        Visible = true;
        if (again && _sheet.Says(memo))
            return;
        _sheet.Fill(memo);
        // drawn once, then kept as a picture with mipmaps: shrunk onto the steep side, its letters stay smooth
        var drawing = ++_drawing;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        if (drawing != _drawing || !IsInstanceValid(this))
            return;
        var image = _viewport.GetTexture().GetImage();
        image.GenerateMipmaps();
        _material.SetShaderParameter("content", ImageTexture.CreateFromImage(image));
    }

    /// <summary>Nothing on it.</summary>
    public void Clear()
    {
        _tween?.Kill();
        Memo = null;
        Visible = false;
    }

    /// <summary>It comes out of the slot, bottom edge first.</summary>
    public void PrintOut(double seconds)
    {
        _tween?.Kill();
        SetReveal(0);
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(SetReveal), 0f, 1f, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Torn off: it drops and fades, as the receipt does.</summary>
    public void TearOff()
    {
        _tween?.Kill();
        Memo = null;
        _tween = CreateTween();
        _tween.TweenProperty(this, "position", new Vector2(8, 110), 0.7).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        _tween.Parallel().TweenProperty(this, "modulate:a", 0f, 0.7);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    /// <summary>How much of it is out of the slot: the paper shows its bottom part, and the outline follows its edge.</summary>
    private void SetReveal(float reveal)
    {
        _material.SetShaderParameter("reveal", reveal);
        _outline.Points = [.. Edge(reveal).Select(OnCanvas)];
    }

    /// <summary>
    /// The paper's edge in the unit square, as much as is out: down the left side, along the torn bottom, up the right;
    /// across the top too when taped up (hanging, the top is in the slot).
    /// </summary>
    private List<Vector2> Edge(float reveal)
    {
        var top = _taped ? Tape / _size.Y : 0;
        var tooth = ToothDepth / _size.Y;
        if (reveal <= top + tooth)
            return [];
        var teeth = Teeth(_size.X);
        var edge = new List<Vector2> { new(0, top), new(0, reveal - tooth) };
        for (var i = 0; i < teeth; i++)
        {
            edge.Add(new Vector2((i + 0.5f) / teeth, reveal));
            edge.Add(new Vector2((i + 1f) / teeth, reveal - tooth));
        }
        edge.Add(new Vector2(1, top));
        return edge;
    }

    private static int Teeth(float width) => Math.Max(3, (int)MathF.Round(width / ToothWidth));

    private Vector2 OnCanvas(Vector2 unit) => Homography.Apply(_toQuad, unit);

    /// <summary>The paper drawn flat: its cream with the torn edge, and what is printed on it.</summary>
    private partial class Sheet(MemoPaper paper) : Control
    {
        private static readonly Color Cream = new("fff7ec"), Pink = new("c2185b"), Letters = new("3b2e5e");
        private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
        private readonly List<Label> _labels = [];
        private MemoBoard.Shown _memo;

        private float Top => paper._taped ? Tape * PxPerUnit : 0;

        public bool Says(MemoBoard.Shown memo) => _memo == memo;

        public void Fill(MemoBoard.Shown memo)
        {
            _memo = memo;
            foreach (var label in _labels)
                label.QueueFree();
            _labels.Clear();
            var taped = paper._taped;
            var y = Top + (taped ? 14 : 10);
            y = Line("MEMO", Pixel, 13, Pink, y, lines: 1) + 4;
            y = Line(memo.Title, Companion.Paper.Font(650), taped ? 22 : 19, new Color("1e1630"), y, lines: 3) + 6;
            Line(memo.Glimpse, Companion.Paper.Font(450), taped ? 15 : 14, Letters, y, lines: 0);
            QueueRedraw();
        }

        /// <summary>A label from <paramref name="y"/> down, at most so many lines (0: as many as fit); returns where it ends.</summary>
        private float Line(string text, Font font, int size, Color colour, float y, int lines)
        {
            var settings = new LabelSettings { Font = font, FontSize = size, FontColor = colour, LineSpacing = -2 };
            var room = Size.Y - ToothDepth * PxPerUnit - 8 - y;
            var lineHeight = font.GetHeight(size) - 2;
            var fit = Math.Max(1, (int)(room / lineHeight));
            var label = new Label
            {
                Text = text,
                Position = new Vector2(10, y),
                CustomMinimumSize = new Vector2(Size.X - 20, Math.Max(lineHeight, room)),      // a label's own size is reset when it is added
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MaxLinesVisible = lines > 0 ? Math.Min(lines, fit) : fit,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                ClipText = true,
                LabelSettings = settings,
            };
            AddChild(label);
            _labels.Add(label);
            return y + Math.Min(label.GetLineCount(), label.MaxLinesVisible) * lineHeight;
        }

        public override void _Draw()
        {
            var tooth = ToothDepth * PxPerUnit;
            var (w, top, bottom) = (Size.X, Top, Size.Y - tooth);
            var shape = new List<Vector2> { new(0, top), new(w, top), new(w, bottom) };
            var teeth = Teeth(paper._size.X);
            for (var i = teeth; i >= 1; i--)
            {
                shape.Add(new Vector2(w * (i - 0.5f) / teeth, Size.Y));
                shape.Add(new Vector2(w * (i - 1f) / teeth, bottom));
            }
            DrawColoredPolygon([.. shape], Cream);
        }
    }
}
