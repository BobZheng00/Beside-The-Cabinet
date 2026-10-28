using Godot;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The memos on the cabinet: the one hanging from the printer's slot, just printed, and those stuck on the side panel,
/// each where the player put it, a little tilted. Each is paper projected onto its place, as the receipt is.
/// Pointing at one shows it flat beside the pointer, large enough to read; a memo being carried is flat in the hand,
/// and over the side panel it shows where it would land, in perspective. Knows nothing of where memos are kept;
/// the app says what to show.
/// </summary>
public partial class MemoBoard : Node2D
{
    /// <summary>What a memo's paper shows: its title, its first words (and more of them on the card), its paper and its sticker.</summary>
    public readonly record struct Shown(long Id, string Title, string Glimpse, string Colour, string? Sticker, string Read);

    private const double HoverDelay = 0.35;
    private const float InHand = 0.72f;                 // the card's size while carried

    private MemoSide? _side;
    private double[,] _sideToCanvas = null!;
    private (Vector3 Row0, Vector3 Row1, Vector3 Row2) _canvasToSide;
    private Vector2 _canvas;
    private MemoPaper _hanging = null!, _carried = null!;
    private readonly Dictionary<long, MemoPaper> _pinned = [];
    private readonly Dictionary<long, Shown> _shown = [];
    private MemoCard _card = null!;
    private MemoPaper? _hovered;
    private long? _hoverId, _lifted;
    private double _hoverSince, _time;
    private float _carryTilt;

    public void Build(CabinetSkin skin)
    {
        _canvas = skin.Canvas;
        _side = skin.Anchors.Memos;
        CabinetMiddle = skin.Anchors.ScreenQuad.Average(p => p.X);
        SlotPoint = (skin.Anchors.PrinterSlot[0] + skin.Anchors.PrinterSlot[1] + skin.Anchors.PrinterSlot[2] + skin.Anchors.PrinterSlot[3]) / 4;
        if (_side is { } side)
        {
            _sideToCanvas = Homography.UnitToQuad(side.Quad);
            _canvasToSide = Homography.QuadToUnit(side.Quad);
        }
        var paper = _side?.Paper ?? new Vector2(44, 58);
        var tape = _side?.Tape ?? 6;
        _hanging = new MemoPaper(skin.Anchors.ReceiptQuad, skin.Anchors.ReceiptSize, tape: 0);
        AddChild(_hanging);
        _carried = new MemoPaper(skin.Anchors.ReceiptQuad, paper + new Vector2(0, tape), tape);
        AddChild(_carried);
        _card = new MemoCard { ZIndex = 60, ZAsRelative = false, Visible = false };      // over her and everything on the cabinet
        AddChild(_card);
    }

    public override void _Process(double delta) => _time += delta;

    /// <summary>The canvas rectangle is partly under a memo's card, shown over everything on the cabinet.</summary>
    public bool CardCovers(Rect2 canvasRect) => _card.Visible && new Rect2(_card.Position, _card.Size * _card.Scale).Intersects(canvasRect);

    /// <summary>The canvas's size, and the middle of the cabinet's front across it.</summary>
    public Vector2 Canvas => _canvas;
    public float CabinetMiddle { get; private set; }

    /// <summary>The middle of the printer's slot, where a new memo comes out (and a written one goes in).</summary>
    public Vector2 SlotPoint { get; private set; }

    /// <summary>The window's pixels per canvas unit now (smaller on a small cabinet).</summary>
    public Func<float> WindowScale { get; set; } = () => 1;

    /// <summary>
    /// How much bigger than its canvas size anything meant to be read is drawn (the card beside the pointer), so that on a
    /// small cabinet its letters are still as large as on the medium one.
    /// </summary>
    public float Readable => Mathf.Max(1, 0.82f / WindowScale());

    /// <summary>Memos can be stuck on this cabinet's side.</summary>
    public bool CanPin => _side is not null;

    /// <summary>A memo hangs from the slot; <paramref name="print"/>: it prints out of it first.</summary>
    public void Hang(Shown memo, bool print)
    {
        _shown[memo.Id] = memo;
        _hanging.Show(memo);
        if (print)
            _hanging.PrintOut(1.6);
    }

    /// <summary>Nothing hangs from the slot any more; <paramref name="tear"/>: it is torn off and falls away.</summary>
    public void Unhang(bool tear)
    {
        if (tear && _hanging.Visible && _hanging.Memo != _lifted)
            _hanging.TearOff();
        else
            _hanging.Clear();
    }

    /// <summary>The memos stuck on the side, bottom of the pile first: each at its spot (side panel units) and tilt (degrees).</summary>
    public void Pin(IReadOnlyList<(Shown Memo, Vector2 Spot, float Tilt)> pinned)
    {
        if (_side is not { } side)
            return;
        foreach (var gone in _pinned.Keys.Where(id => pinned.All(p => p.Memo.Id != id)).ToList())
        {
            _pinned[gone].QueueFree();
            _pinned.Remove(gone);
        }
        foreach (var (memo, spot, tilt) in pinned)
        {
            _shown[memo.Id] = memo;
            if (!_pinned.TryGetValue(memo.Id, out var paper))
            {
                paper = new MemoPaper(SideQuad(spot, tilt), side.Paper + new Vector2(0, side.Tape), side.Tape);
                AddChild(paper);
                _pinned[memo.Id] = paper;
            }
            paper.Place(SideQuad(spot, tilt));
            paper.Show(memo);
            MoveChild(paper, -1);                                                   // in the order given: the last on top
        }
        MoveChild(_hanging, -1);
        MoveChild(_carried, -1);
        MoveChild(_card, -1);
    }

    /// <summary>The memo under a canvas point (the one on top), or null.</summary>
    public long? Hit(Vector2 canvasPoint) => Paper(canvasPoint)?.Memo;

    private MemoPaper? Paper(Vector2 canvasPoint) =>
        _pinned.Values.OrderBy(p => p.GetIndex()).Append(_hanging)
            .LastOrDefault(p => p.Visible && p.Memo is not null && p.Memo != _lifted && Geometry2D.IsPointInPolygon(canvasPoint, p.Quad));

    /// <summary>
    /// The pointer is over a memo, or not (null): it brightens, as the CHECK button does, and after a moment the
    /// memo shows flat beside the pointer, large enough to read.
    /// </summary>
    public void Hover(Vector2? canvasPoint)
    {
        var paper = canvasPoint is { } p && _lifted is null ? Paper(p) : null;
        if (paper != _hovered)
        {
            if (_hovered is not null && IsInstanceValid(_hovered))
                _hovered.Modulate = Colors.White;
            _hovered = paper;
            if (paper is not null)
                paper.Modulate = new Color(1.08f, 1.08f, 1.12f);
        }
        var id = paper?.Memo;
        if (id != _hoverId)
        {
            _hoverId = id;
            _hoverSince = _time;
        }
        if (_lifted is not null)
            return;
        if (id is { } shown && _time - _hoverSince >= HoverDelay && canvasPoint is { } at && _shown.TryGetValue(shown, out var memo))
        {
            _card.Show(memo, tilt: 0);
            _card.Scale = Vector2.One * Readable;
            _card.Position = Beside(at, _card.Size * Readable);
        }
        else
            _card.Visible = false;
    }

    /// <summary>Where the card goes for a pointer: below and to the right of it, or wherever it fits on the canvas.</summary>
    private Vector2 Beside(Vector2 pointer, Vector2 size)
    {
        var x = pointer.X + 16 + size.X <= _canvas.X ? pointer.X + 16 : pointer.X - 16 - size.X;
        var y = Mathf.Clamp(pointer.Y + 12, 4, _canvas.Y - size.Y - 4);
        return new Vector2(Mathf.Max(4, x), y);
    }

    // ---- Carrying a memo by hand ------------------------------------------------------------------------------------

    /// <summary>
    /// Where on the side a memo let go at this canvas point would land (side panel units, kept within its area) and
    /// how it would tilt; null when the point is not on the side panel.
    /// </summary>
    public (Vector2 Spot, float Tilt)? SpotAt(Vector2 canvasPoint)
    {
        if (_side is not { } side)
            return null;
        var p = new Vector3(canvasPoint.X, canvasPoint.Y, 1);
        var w = _canvasToSide.Row2.Dot(p);
        if (w <= 0)
            return null;
        var local = new Vector2(_canvasToSide.Row0.Dot(p) / w * side.Size.X, _canvasToSide.Row1.Dot(p) / w * side.Size.Y);
        if (local.X < 0 || local.Y < 0 || local.X > side.Size.X || local.Y > side.Size.Y)
            return null;
        // held by its top (where the pointer took it): the paper hangs below the pointer
        var centre = local + new Vector2(0, side.Paper.Y / 2 - 6);
        return (centre.Clamp(side.Area.Position, side.Area.End), _carryTilt);
    }

    /// <summary>A free spot for a memo pinned by a button: one of the side's spots not taken, else one near the others.</summary>
    public (Vector2 Spot, float Tilt) FreeSpot(IReadOnlyCollection<Vector2> taken)
    {
        var side = _side!;
        foreach (var (at, tilt) in side.Spots)
            if (taken.All(t => t.DistanceTo(at) > side.Paper.X * 0.6f))
                return (at, tilt);
        var random = new RandomNumberGenerator();
        return (new Vector2(random.RandfRange(side.Area.Position.X, side.Area.End.X), random.RandfRange(side.Area.Position.Y, side.Area.End.Y)),
                random.RandfRange(-4, 4));
    }

    /// <summary>A memo comes off the cabinet into the hand: its paper there goes, and it follows the pointer.</summary>
    public void Lift(long id, Vector2 at)
    {
        if (!_shown.TryGetValue(id, out var memo))
            return;
        _lifted = id;
        _card.Visible = false;
        if (_hanging.Memo == id)
            _hanging.Visible = false;
        if (_pinned.TryGetValue(id, out var pinned))
            pinned.Visible = false;
        _carryTilt = new RandomNumberGenerator().RandfRange(-4, 4);
        _carried.Show(memo);
        _carried.Visible = false;
        Carry(at);
    }

    /// <summary>The carried memo follows the pointer: on the side panel where it would land, elsewhere flat in the hand.</summary>
    public void Carry(Vector2 at)
    {
        if (_lifted is not { } id || !_shown.TryGetValue(id, out var memo))
            return;
        if (SpotAt(at) is { } landing)
        {
            _carried.Place(SideQuad(landing.Spot, landing.Tilt));
            _carried.Visible = true;
            _card.Visible = false;
        }
        else
        {
            _carried.Visible = false;
            _card.Show(memo, tilt: -3);
            _card.Scale = Vector2.One * InHand * Readable;
            _card.Position = at - new Vector2(_card.Size.X / 2, 10) * InHand * Readable;     // held by its top, a little smaller: the cabinet stays in view
        }
    }

    /// <summary>Let go: what was carried goes from the hand (the app shows the memo wherever it now is).</summary>
    public void PutDown()
    {
        _lifted = null;
        _carried.Visible = false;
        _card.Visible = false;
    }

    /// <summary>A pinned memo's quad on the canvas: its paper and tape around its spot, turned by its tilt, on the side panel.</summary>
    private Vector2[] SideQuad(Vector2 spot, float tilt)
    {
        var side = _side!;
        var (w, h) = (side.Paper.X, side.Paper.Y);
        var turn = Mathf.DegToRad(tilt);
        return [.. new[] { new Vector2(-w / 2, -h / 2 - side.Tape), new Vector2(w / 2, -h / 2 - side.Tape), new Vector2(w / 2, h / 2), new Vector2(-w / 2, h / 2) }
                    .Select(c => Homography.Apply(_sideToCanvas, (spot + c.Rotated(turn)) / side.Size))];
    }

    /// <summary>Dev: a canvas point on a memo (where it shows now), or on the side panel at a spot.</summary>
    public Vector2 DevPoint(long? id, Vector2? spot = null)
    {
        if (spot is { } s)
            return Homography.Apply(_sideToCanvas, s / _side!.Size);
        var paper = id is { } i && _pinned.TryGetValue(i, out var p) ? p : _hanging;
        return paper.Quad.Aggregate(Vector2.Zero, (sum, q) => sum + q) / 4;
    }
}

/// <summary>
/// A memo flat and large enough to read: its paper and sticker, "MEMO", its title and the start of what it says.
/// Shown beside the pointer when pointing at a memo, and in the hand while carrying one.
/// </summary>
public partial class MemoCard : Node2D
{
    private const float Width = 196, Margin = 12;
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
    private readonly Label _title, _text;
    private MemoBoard.Shown? _memo;

    public MemoCard()
    {
        _title = new Label
        {
            Position = new Vector2(Margin, 26), CustomMinimumSize = new Vector2(Width - 2 * Margin - 26, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 3, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            LabelSettings = new LabelSettings { Font = Companion.Paper.Font(650), FontSize = 17, FontColor = MemoLook.Ink, LineSpacing = -2 },
        };
        _text = new Label
        {
            CustomMinimumSize = new Vector2(Width - 2 * Margin, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 8, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            LabelSettings = new LabelSettings { Font = Companion.Paper.Font(480), FontSize = 14, FontColor = new Color("3b2e5e"), LineSpacing = -1 },
        };
        AddChild(_title);
        AddChild(_text);
    }

    public Vector2 Size { get; private set; } = new(Width, 120);

    public void Show(MemoBoard.Shown memo, float tilt)
    {
        Rotation = Mathf.DegToRad(tilt);
        Visible = true;
        if (_memo == memo)
            return;
        _memo = memo;
        _title.Text = memo.Title;
        _text.Text = memo.Read;
        _text.Position = new Vector2(Margin, 26 + _title.GetLineCount() * 20 + 6);
        var lines = Math.Min(_text.GetLineCount(), _text.MaxLinesVisible);
        Size = new Vector2(Width, _text.Position.Y + lines * 17.5f + Margin + 6);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_memo is not { } memo)
            return;
        // a soft shadow, the paper with its torn bottom, and the cabinet's ink round it
        const float tooth = 5;
        var shape = new List<Vector2> { new(0, 0), new(Size.X, 0), new(Size.X, Size.Y - tooth) };
        var teeth = (int)(Size.X / 9);
        for (var i = teeth; i >= 1; i--)
        {
            shape.Add(new Vector2(Size.X * (i - 0.5f) / teeth, Size.Y));
            shape.Add(new Vector2(Size.X * (i - 1f) / teeth, Size.Y - tooth));
        }
        DrawColoredPolygon([.. shape.Select(p => p + new Vector2(4, 5))], new Color(0.12f, 0.08f, 0.2f, 0.18f));
        DrawColoredPolygon([.. shape], MemoLook.Paper(memo.Colour));
        DrawPolyline([.. shape, shape[0]], MemoLook.Ink, 2.4f, true);
        DrawString(Pixel, new Vector2(Margin, 19), "MEMO", HorizontalAlignment.Left, -1, 10, new Color("c2185b"));
        if (memo.Sticker is { } sticker)
            MemoLook.Sticker(this, sticker, new Vector2(Size.X - 22, 22), 26);
    }
}
