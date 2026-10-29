using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>What the CRT should show right now. Built by the app from ledger state.</summary>
public sealed record ScreenState
{
    public long TimeLeftMs { get; init; }
    public long DebtMs { get; init; }
    public int Credits { get; init; }
    public float Fuel { get; init; }              // 0..1 of the token currently in the slot
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public bool Playing { get; init; }
    public string Status { get; init; } = "";     // "GAME: …", "PAUSED", "LINK DOWN", "NO STEAM", …
    public long TodayEarnedMs { get; init; }      // play earned today, towards its cap
    public long TodayCapMs { get; init; }
    public long TodayFocusedMs { get; init; }
    public long TodayPlayedMs { get; init; }
    public string Initials { get; init; } = "";
    public string Earn { get; init; } = "FOCUS OR SOLVE";  // the attract screen's line under INSERT COIN: how play is earned, of what earns
}

/// <summary>
/// The live CRT: an offscreen UI rendered into a viewport and projected onto the cabinet's screen quad, in three
/// layers: the readout (a prompt for text entry over a flash over the readout / attract pages), the desk above it
/// where the screen's own small system draws its apps (see App.CrtOs), and over both, while her microphone is open,
/// "MIC" with a blinking dot in the top right corner. The readout folds away like a CRT switching off when the
/// system opens, and unfolds when it closes. With an app open (on a small or medium cabinet, from the system opening)
/// the screen lifts out of the cabinet: its corners glide
/// from the cabinet's perspective to a flat screen facing the player, on a little bezel, large enough to read whatever
/// size the cabinet is, and it looks like a CRT up close (scanlines, rounded glass).
/// </summary>
public partial class ScreenView : Node2D
{
    private static readonly Color Ink = new("6a1c5e");
    private static readonly Color Accent = new("e0457f");
    private static readonly Color Debt = new("c2185b");
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
    // Small text uses a rounded proportional face: the pixel font is too wide to stay legible on a ~100 px screen.
    private static readonly Font Round = new FontVariation
    {
        BaseFont = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf"),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 650 },
    };
    private const int PxPerUnit = 2;

    /// <summary>How wide the lifted screen is, at least, in the window's own pixels: its letters stay readable on the small cabinet.</summary>
    public const float LiftedWidth = 340;

    private const float Room = 14;

    private SubViewport _viewport = null!;
    private Vector2[] _quad = [], _cabinetQuad = [];
    private Polygon2D _surface = null!;
    private Bezel _bezel = null!;
    private Vector2[] _flat = [];
    private float _lift;
    private Tween? _lifting;
    private (Vector3 Row0, Vector3 Row1, Vector3 Row2) _toScreen;
    private Vector2 _pixels;
    private Control _readout = null!;
    private ColorRect _fold = null!;
    private bool _live;
    private Tween? _folding;
    private Label _top = null!, _topRight = null!, _big = null!, _caption = null!, _status = null!;
    private ColorRect _fuelBack = null!, _fuelFill = null!, _micDot = null!;
    private Label _mic = null!;
    private bool _listening;
    private ShaderMaterial _material = null!;
    private ScreenState _state = new();
    private (string Big, string Caption)? _flash;
    private (string Big, string Caption, string Status)? _prompt;
    private (string Big, string Caption, float Done)? _focus;
    private int _attractPage, _flashId;
    private float _width;

    public void Build(IReadOnlyList<Vector2> quad, Vector2 sizeUnits)
    {
        var size = (Vector2I)(sizeUnits * PxPerUnit);
        _width = size.X;
        _viewport = new SubViewport
        {
            Size = size,
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(_viewport);

        var screen = new Control { Size = size };
        _viewport.AddChild(screen);
        var root = _readout = new Control { Size = size, PivotOffset = size / 2 };
        screen.AddChild(root);
        Desk = new Control { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        screen.AddChild(Desk);
        var overlay = new Control { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        screen.AddChild(overlay);
        // the bright line a CRT folds down to
        _fold = new ColorRect { Size = new Vector2(size.X, 6), Position = new Vector2(0, size.Y / 2f - 3), Color = new Color(1, 0.94f, 0.98f), Visible = false };
        screen.AddChild(_fold);
        _quad = [.. quad];
        _cabinetQuad = [.. quad];
        _pixels = size;
        _top = AddLabel(root, Pixel, 21, HorizontalAlignment.Left, new Vector2(38, 26), new Vector2(size.X - 76, 28));
        _topRight = AddLabel(root, Pixel, 21, HorizontalAlignment.Right, new Vector2(38, 26), new Vector2(size.X - 76, 28));
        _big = AddLabel(root, Pixel, 64, HorizontalAlignment.Center, new Vector2(0, 76), new Vector2(size.X, 84));
        _caption = AddLabel(root, Round, 30, HorizontalAlignment.Center, new Vector2(14, 166), new Vector2(size.X - 28, 76));
        _caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _caption.LabelSettings.LineSpacing = -4;
        _status = AddLabel(root, Round, 27, HorizontalAlignment.Center, new Vector2(40, 290), new Vector2(size.X - 80, 34));
        _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _topRight.LabelSettings.FontColor = Accent;
        _mic = AddLabel(overlay, Pixel, 21, HorizontalAlignment.Right, new Vector2(38, 26), new Vector2(size.X - 76, 28));
        _mic.Text = "MIC";
        _mic.LabelSettings.FontColor = Accent;
        _micDot = new ColorRect { Position = new Vector2(size.X - 38 - 3 * 21 - 24, 33), Size = new Vector2(14, 14), Color = Accent, Visible = false };     // lit only while the microphone is open
        overlay.AddChild(_micDot);
        _fuelBack = new ColorRect { Position = new Vector2(60, 258), Size = new Vector2(size.X - 120, 20), Color = Ink with { A = 0.22f } };
        _fuelFill = new ColorRect { Position = _fuelBack.Position, Size = _fuelBack.Size, Color = Ink };
        root.AddChild(_fuelBack);
        root.AddChild(_fuelFill);

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/screen_quad.gdshader") };
        _material.SetShaderParameter("content", _viewport.GetTexture());
        _bezel = new Bezel { Visible = false, ZIndex = 64, ZAsRelative = false };      // over her and the cabinet; a memo's sheet comes over it
        AddChild(_bezel);
        _surface = new Polygon2D { Material = _material };
        AddChild(_surface);
        Place(_cabinetQuad);

        var attract = new Godot.Timer { WaitTime = 4.0, Autostart = true };
        attract.Timeout += () => { _attractPage = (_attractPage + 1) % 4; Redraw(); };
        AddChild(attract);
        var blink = new Godot.Timer { WaitTime = 0.6, Autostart = true };
        blink.Timeout += () =>
        {
            if (!_listening)
                return;
            _micDot.Visible = !_micDot.Visible;             // like a recording light
            Refresh();
        };
        AddChild(blink);
        Redraw();
    }

    public void Show(ScreenState state)
    {
        _state = state;
        if (state.Playing)
            _attractPage = 0;
        Redraw();
    }

    /// <summary>Takes over the screen for a moment, e.g. "+2" or "CONTINUE?".</summary>
    public void Flash(string big, string caption, double seconds)
    {
        _flash = (big, caption);
        var id = ++_flashId;
        Redraw();
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (id != _flashId)
                return;             // a newer flash owns the screen
            _flash = null;
            Redraw();
        };
    }

    /// <summary>A <see cref="Flash"/> whose big number counts from one time to another, as the overnight fade takes play time off.</summary>
    public void FlashCount(long fromMs, long toMs, string caption, double seconds)
    {
        Flash(Clock(fromMs), caption, seconds);
        var id = _flashId;
        var count = CreateTween();
        count.TweenInterval(0.8);                           // read the old time first
        count.TweenMethod(Callable.From<double>(ms =>
        {
            if (id != _flashId)
                return;                                     // a newer flash owns the screen
            _flash = (Clock((long)ms), caption);
            Redraw();
        }), (double)fromMs, (double)toMs, 1.6).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Holds the screen for text entry until <see cref="ClearPrompt"/>.</summary>
    public void Prompt(string big, string caption, string status)
    {
        _prompt = (big, caption, status);
        Redraw();
    }

    public void ClearPrompt()
    {
        _prompt = null;
        Redraw();
    }

    /// <summary>A focus session runs: the readout counts down its part (the bar shows how much is behind) instead of its pages.</summary>
    public (string Big, string Caption, float Done)? Focus
    {
        set
        {
            if (value == _focus)
                return;
            _focus = value;
            Redraw();
        }
    }

    /// <summary>Text entry holds the screen (a player's initials): nothing else can take it.</summary>
    public bool Prompting => _prompt is not null;

    /// <summary>Where the screen's own small system draws (in the screen's pixels, over the readout).</summary>
    public Control Desk { get; private set; } = null!;

    /// <summary>The screen's size in its own pixels.</summary>
    public Vector2 Pixels => _pixels;

    /// <summary>The canvas point is on the screen.</summary>
    public bool Hits(Vector2 canvasPoint) => Geometry2D.IsPointInPolygon(canvasPoint, _quad);

    /// <summary>
    /// A canvas point in the screen's own pixels (through its perspective), or null when it is off the screen
    /// (<paramref name="anywhere"/>: wherever it is, for something carried off its edge).
    /// </summary>
    public Vector2? ScreenPoint(Vector2 canvasPoint, bool anywhere = false)
    {
        if (!anywhere && !Hits(canvasPoint))
            return null;
        var p = new Vector3(canvasPoint.X, canvasPoint.Y, 1);
        var w = _toScreen.Row2.Dot(p);
        return new Vector2(_toScreen.Row0.Dot(p) / w * _pixels.X, _toScreen.Row1.Dot(p) / w * _pixels.Y);
    }

    /// <summary>
    /// The screen redraws every frame (its system is open: things move, and answer the pointer), or only when what
    /// it shows changes.
    /// </summary>
    public bool Live
    {
        set
        {
            _live = value;
            Refresh();
        }
    }

    /// <summary>
    /// The readout folds away as a CRT switching off does, to a bright line and out (<paramref name="away"/>), or
    /// unfolds back from it; <paramref name="then"/> once it has.
    /// </summary>
    public void Fold(bool away, double seconds, Action? then = null)
    {
        _folding?.Kill();
        _readout.Visible = true;
        _fold.Visible = true;
        _fold.Modulate = new Color(1, 1, 1, away ? 0 : 1);
        _fold.Scale = new Vector2(away ? 1 : 0.15f, 1);
        _fold.PivotOffset = _fold.Size / 2;
        _readout.Scale = new Vector2(1, away ? 1 : 0.02f);
        var live = _live;
        Live = true;
        _folding = CreateTween();
        if (away)
        {
            _folding.TweenProperty(_readout, "scale", new Vector2(1, 0.02f), seconds * 0.6).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            _folding.Parallel().TweenProperty(_fold, "modulate:a", 1f, seconds * 0.5);
            _folding.TweenCallback(Callable.From(() => _readout.Visible = false));
            _folding.TweenProperty(_fold, "scale", new Vector2(0.15f, 1), seconds * 0.4).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            _folding.Parallel().TweenProperty(_fold, "modulate:a", 0f, seconds * 0.4);
        }
        else
        {
            _folding.TweenProperty(_fold, "scale", Vector2.One, seconds * 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            _folding.TweenProperty(_readout, "scale", Vector2.One, seconds * 0.65).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _folding.Parallel().TweenProperty(_fold, "modulate:a", 0f, seconds * 0.5);
        }
        _folding.TweenCallback(Callable.From(() =>
        {
            _fold.Visible = false;
            Live = live;
            then?.Invoke();
        }));
    }

    /// <summary>
    /// The screen lifts out of the cabinet to face the player (<paramref name="up"/>), or settles back into it;
    /// <paramref name="then"/> once it has.
    /// </summary>
    /// <param name="windowScale">The window's pixels per canvas unit: the lifted screen is <see cref="LiftedWidth"/> of them wide at least.</param>
    public void Lift(bool up, float windowScale, Rect2 canvas, Action? then = null)
    {
        _lifting?.Kill();
        Lifted = up;
        if (up)
        {
            // flat, a little bigger than the cabinet's screen, centred on it and kept inside the window
            var bounds = Bounds(_cabinetQuad);
            // its bezel's rim (a twentieth of its width each side) and a little room stay in the window too
            var room = Mathf.Min(canvas.Size.X, canvas.Size.Y * _pixels.X / _pixels.Y) - 2 * Room;
            var width = Mathf.Min(Mathf.Max(bounds.Size.X * 1.3f, LiftedWidth / windowScale), room / 1.1f);
            var size = new Vector2(width, width * _pixels.Y / _pixels.X);
            var margin = Vector2.One * (width * 0.05f + Room);
            var centre = bounds.GetCenter().Clamp(canvas.Position + size / 2 + margin, canvas.End - size / 2 - margin);
            var flat = new Rect2(centre - size / 2, size);
            _flat = [flat.Position, new Vector2(flat.End.X, flat.Position.Y), flat.End, new Vector2(flat.Position.X, flat.End.Y)];
        }
        var live = _live;
        Live = true;
        _lifting = CreateTween();
        _lifting.TweenMethod(Callable.From<float>(SetLift), _lift, up ? 1f : 0f, up ? 0.34 : 0.28)
            .SetTrans(up ? Tween.TransitionType.Back : Tween.TransitionType.Cubic).SetEase(up ? Tween.EaseType.Out : Tween.EaseType.InOut);
        _lifting.TweenCallback(Callable.From(() =>
        {
            Live = live || up;
            then?.Invoke();
        }));
    }

    /// <summary>It is lifted out of the cabinet, or on its way out.</summary>
    public bool Lifted { get; private set; }

    /// <summary>The canvas point is on the lifted screen or its bezel (which are over whatever is below them).</summary>
    public bool Covers(Vector2 canvasPoint)
    {
        if (!Lifted)
            return false;
        var bounds = Bounds(_quad);
        return bounds.Grow(bounds.Size.X * 0.05f).HasPoint(canvasPoint);
    }

    /// <summary>The canvas rectangle is partly under the lifted screen or its bezel.</summary>
    public bool Covers(Rect2 canvasRect)
    {
        if (!Lifted)
            return false;
        var bounds = Bounds(_quad);
        return bounds.Grow(bounds.Size.X * 0.05f).Intersects(canvasRect);
    }

    /// <summary>How far out of the cabinet it is (0: in it, 1: lifted).</summary>
    private void SetLift(float lift)
    {
        _lift = lift;
        _material.SetShaderParameter("lifted", lift);
        Place(lift <= 0.001f ? _cabinetQuad : [.. _cabinetQuad.Select((p, i) => p.Lerp(_flat[i], lift))]);
        var lifted = lift > 0.001f;
        _surface.ZIndex = lifted ? 65 : 0;
        _surface.ZAsRelative = !lifted;
        _bezel.Visible = lifted;
        _bezel.Modulate = new Color(1, 1, 1, Mathf.Clamp(lift * 1.6f, 0, 1));
        _bezel.Around = Bounds(_quad);
    }

    /// <summary>The screen's content goes onto this quad (TL, TR, BR, BL), and the pointer is read through it.</summary>
    private void Place(Vector2[] quad)
    {
        _quad = quad;
        var (r0, r1, r2) = _toScreen = Homography.QuadToUnit(quad);
        _material.SetShaderParameter("inv_h_row0", r0);
        _material.SetShaderParameter("inv_h_row1", r1);
        _material.SetShaderParameter("inv_h_row2", r2);
        _surface.Polygon = [.. quad];
    }

    private static Rect2 Bounds(Vector2[] quad)
    {
        var min = quad.Aggregate((a, b) => a.Min(b));
        var max = quad.Aggregate((a, b) => a.Max(b));
        return new Rect2(min, max - min);
    }

    /// <summary>
    /// What the lifted screen sits in: a soft shadow, the cabinet's dark bezel outlined in its ink, and the CRT's pink
    /// glow behind the content (on the cabinet the art draws the glow; lifted out, the screen brings its own).
    /// </summary>
    private partial class Bezel : Node2D
    {
        private Rect2 _around;

        public Rect2 Around
        {
            set
            {
                _around = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            var rim = _around.Size.X * 0.05f;
            var frame = _around.Grow(rim);
            DrawStyleBox(Box(new Color(0.12f, 0.08f, 0.2f, 0.25f), Colors.Transparent, 0, rim * 1.6f), new Rect2(frame.Position + new Vector2(8, 10), frame.Size));
            DrawStyleBox(Box(new Color("3B2E5E"), new Color("1E1630"), 3, rim * 1.6f), frame);
            DrawStyleBox(Box(new Color("F6A8DA"), new Color("1E1630"), 2, rim * 0.9f), _around);
            DrawCircle(_around.GetCenter(), _around.Size.X * 0.36f, new Color(1, 0.92f, 0.98f, 0.35f));       // the glow in its middle
        }

        private static StyleBoxFlat Box(Color fill, Color line, int width, float corner)
        {
            var box = new StyleBoxFlat { BgColor = fill, BorderColor = line, AntiAliasing = true };
            box.SetBorderWidthAll(width);
            box.SetCornerRadiusAll((int)corner);
            return box;
        }
    }

    private void Refresh() => _viewport.RenderTargetUpdateMode = _live ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Once;

    public void SetBrightness(float brightness) => _material.SetShaderParameter("brightness", brightness);

    /// <summary>Her microphone is open: MIC shows in the top right corner.</summary>
    public bool Mic
    {
        set
        {
            _listening = value;
            _micDot.Visible = value;
            Redraw();
        }
    }

    private void Redraw()
    {
        var s = _state;
        var inDebt = s.DebtMs > 0 && s.TimeLeftMs == 0;
        var takenOver = _prompt is not null || _flash is not null;
        var focus = s.Playing ? null : _focus;              // a game's clock comes first
        var page = s.Playing || takenOver || focus is not null ? 0 : _attractPage;

        _top.Text = $"CREDIT {s.Credits:00}";
        _topRight.Text = s.Streak > 0 ? $"x{s.Streak}" : "";
        _top.Visible = _prompt is null;
        _topRight.Visible = _prompt is null && !_listening;
        _mic.Visible = _listening;
        _fuelBack.Visible = _fuelFill.Visible = page == 0 && !takenOver && (!inDebt || focus is not null);
        _fuelFill.Size = _fuelBack.Size with { X = _fuelBack.Size.X * Mathf.Clamp(focus?.Done ?? s.Fuel, 0, 1) };
        _status.Text = _prompt?.Status ?? s.Status;
        _big.LabelSettings.FontColor = inDebt && !takenOver && focus is null ? Debt : Ink;

        (_big.Text, _caption.Text) = _prompt is { } p ? (p.Big, p.Caption) : _flash ?? (focus is { } f ? (f.Big, f.Caption) : page switch
        {
            1 => ($"{s.BestStreak}", $"HI-SCORE {s.Initials}\nBEST STREAK"),
            2 => ($"{Hours(s.TodayEarnedMs)}/{Hours(s.TodayCapMs)}",
                  (s.TodayEarnedMs >= s.TodayCapMs ? "TODAY'S PLAY EARNED" : "EARNED TODAY / CAP") + $"\nFOCUS {Hours(s.TodayFocusedMs)} - PLAYED {Hours(s.TodayPlayedMs)}"),
            3 => ("INSERT", s.Earn.Length > 0 ? $"COIN\n{s.Earn}" : "COIN"),
            _ when inDebt => ($"-{Clock(s.DebtMs)}", $"OWED - {s.DebtMs / (15 * 60_000)} IOU"),
            _ => (Clock(s.TimeLeftMs), "TIME LEFT"),
        });
        // Press Start 2P is square: fit the big line to the screen width.
        _big.LabelSettings.FontSize = Math.Clamp((int)((_width - 44) / Math.Max(_big.Text.Length, 1)), 16, 64);
        // Captions sit above the fuel bar; two-line captions use the bar's space when it is hidden.
        _caption.LabelSettings.FontSize = _caption.Text.Length > 34 ? 24 : 30;
        Refresh();
    }

    /// <summary>"1:30", "0:45": hours and minutes.</summary>
    private static string Hours(long ms) => $"{ms / 3_600_000}:{ms / 60_000 % 60:00}";

    private static string Clock(long ms)
    {
        var total = (int)(ms / 1000);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    }

    private static Label AddLabel(Control parent, Font font, int size, HorizontalAlignment align, Vector2 position, Vector2 box)
    {
        var label = new Label
        {
            Position = position,
            Size = box,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = Ink },
        };
        parent.AddChild(label);
        return label;
    }
}
