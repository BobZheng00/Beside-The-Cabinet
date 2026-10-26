using Godot;
using InsertCoin.Game.Cabinet;

namespace InsertCoin.Game.App;

/// <summary>
/// The CRT's own small system, like a phone's (the user's idea, 2026-10-06). A click on the screen folds the readout
/// away and the home screen's apps pop in, right on the cabinet, four to a page, the pages a swipe apart. Holding an
/// app picks it up, to put it somewhere else (the order is kept). An app opens out of its icon while the screen lifts
/// out of the cabinet to face the player, large enough to read at any cabinet size; a press anywhere away from it
/// settles it back, the app closing into its icon. On a small or medium cabinet the home screen lifts out too, as the
/// system opens, and stays lifted with an app open or not until the system closes (#149). The system closes by itself after a while untouched, or from the
/// power mark, and the readout unfolds again. Along the home screen's bar, beside the power mark, the quick switches
/// (Mute sounds, Voice, Conversation): lit when on, a press flips one. The name of a switch or an app under the pointer
/// lights up on the marquee, big enough to read on any cabinet (see Cabinet.MarqueeDisplay).
/// </summary>
public partial class CrtOs : Node
{
    private enum State { Closed, Moving, Home, App }

    /// <summary>
    /// A quick switch on the home screen's bar: its name (lit on the marquee under the pointer), whether it is on,
    /// flipping it, and its picture (drawn at a centre, given whether it is on and the colour).
    /// </summary>
    public sealed record Switch(string Name, Func<bool> On, Action<bool> Set, Action<CanvasItem, Vector2, bool, Color> Glyph);

    /// <summary>Mute sounds: a speaker, crossed out when muted, sounding when not.</summary>
    public static void Speaker(CanvasItem on, Vector2 c, bool muted, Color ink)
    {
        on.DrawColoredPolygon([c + new Vector2(-10, -4), c + new Vector2(-6, -4), c + new Vector2(-1, -9), c + new Vector2(-1, 9),
                               c + new Vector2(-6, 4), c + new Vector2(-10, 4)], ink);
        if (muted)
        {
            on.DrawLine(c + new Vector2(3, -4), c + new Vector2(10, 4), ink, 3, true);
            on.DrawLine(c + new Vector2(3, 4), c + new Vector2(10, -4), ink, 3, true);
        }
        else
            foreach (var r in new[] { 5f, 10f })
                on.DrawArc(c + new Vector2(-1, 0), r, -0.8f, 0.8f, 10, ink, 2.5f, true);
    }

    /// <summary>Voice: a speech bubble with a sound in it.</summary>
    public static void Speech(CanvasItem on, Vector2 c, bool _, Color ink)
    {
        var bubble = new Rect2(c + new Vector2(-10, -9), new Vector2(20, 14));
        on.DrawRect(bubble, ink, filled: false, width: 2.5f);
        on.DrawColoredPolygon([c + new Vector2(-6, 5), c + new Vector2(0, 5), c + new Vector2(-7, 11)], ink);
        for (var i = -1; i <= 1; i++)
        {
            var half = i == 0 ? 4f : 2f;
            on.DrawLine(c + new Vector2(i * 5, -2 - half), c + new Vector2(i * 5, -2 + half), ink, 2.5f, true);
        }
    }

    /// <summary>Conversation (keeps listening): a microphone on its stand.</summary>
    public static void Microphone(CanvasItem on, Vector2 c, bool _, Color ink)
    {
        on.DrawRect(new Rect2(c + new Vector2(-3.5f, -8), new Vector2(7, 7)), ink);
        on.DrawCircle(c + new Vector2(0, -8), 3.5f, ink);
        on.DrawCircle(c + new Vector2(0, -1), 3.5f, ink);
        on.DrawArc(c + new Vector2(0, -2), 7.5f, 0, Mathf.Pi, 12, ink, 2.5f, true);
        on.DrawLine(c + new Vector2(0, 5.5f), c + new Vector2(0, 10), ink, 2.5f, true);
        on.DrawLine(c + new Vector2(-5, 10), c + new Vector2(5, 10), ink, 2.5f, true);
    }

    private const float Bar = 54, Tile = 100;
    private static readonly Vector2 Cell = new(164, 140), Grid = new(36, 56);
    private const double HoldToMove = 0.45, HomeIdle = 15, AppIdle = 40, EdgeTurn = 0.55;
    private static readonly Color Ink = new("6a1c5e"), Accent = new("e0457f");
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");

    private ScreenView _screen = null!;
    private Func<float> _windowScale = null!;
    private Func<bool> _homeLifts = null!;
    private Rect2 _canvas;
    private readonly List<CrtApp> _apps = [];
    private IReadOnlyList<Switch> _switches = [];
    private Action<string[]> _keepOrder = null!;
    private Home _home = null!;
    private Panel _zoom = null!;
    private CrtApp? _open;
    private State _state;
    private int _page, _hovered = -1, _switchHovered = -1;
    private double _untouched, _time;
    // the pointer: pressed where and how long, carried, the app picked up
    private Vector2? _pressedAt;
    private int _pressedOn = -1, _lifted = -1;
    private double _pressedFor, _atEdge;
    private bool _carrying;
    private float _swipe;
    private Vector2 _liftedAt;

    /// <summary>Whether the system is open (a click on the screen is its).</summary>
    public bool Open => _state != State.Closed;

    /// <summary>The screen is lifted out of the cabinet (an app is open, or the home screen on a small or medium cabinet): a click anywhere is its, on it or away from it.</summary>
    public bool Lifted => _screen.Lifted;

    /// <summary>The canvas point is on the lifted screen: it is over whatever is below.</summary>
    public bool Covers(Vector2 canvasPoint) => _screen.Covers(canvasPoint);

    /// <param name="apps">Every app there is.</param>
    /// <param name="order">The home screen's order, by app id, as kept; apps not in it come after.</param>
    /// <param name="keepOrder">Keeps the order after the player has moved an app.</param>
    /// <param name="switches">The quick switches along the home screen's bar, left to right.</param>
    /// <param name="windowScale">The window's pixels per canvas unit now (the screen lifts out larger on a small cabinet).</param>
    /// <param name="homeLifts">The home screen lifts out as the apps do: the cabinet is too small to read it on.</param>
    public void Build(ScreenView screen, IEnumerable<CrtApp> apps, IReadOnlyList<string> order, Action<string[]> keepOrder,
                      IReadOnlyList<Switch> switches, Func<float> windowScale, Func<bool> homeLifts, Vector2 canvas)
    {
        _screen = screen;
        _switches = switches;
        _windowScale = windowScale;
        _homeLifts = homeLifts;
        _canvas = new Rect2(Vector2.Zero, canvas);
        _keepOrder = keepOrder;
        var kept = order.ToList();
        _apps.AddRange(apps.OrderBy(a => kept.IndexOf(a.Id) is var i and >= 0 ? i : int.MaxValue));
        _home = new Home(this) { Size = screen.Pixels, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        screen.Desk.AddChild(_home);
        foreach (var app in _apps)
        {
            app.Position = new Vector2(0, Bar);
            app.Size = screen.Pixels - new Vector2(0, Bar);
            app.Visible = false;
            app.MouseFilter = Control.MouseFilterEnum.Ignore;
            screen.Desk.AddChild(app);
        }
        // the tile an app grows out of (and shrinks back into)
        _zoom = new Panel { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        screen.Desk.AddChild(_zoom);
        var bar = new BarView(this) { Size = new Vector2(screen.Pixels.X, Bar), MouseFilter = Control.MouseFilterEnum.Ignore };
        screen.Desk.AddChild(bar);
        _home.Bar = bar;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_state is State.Home or State.App && (_untouched += delta) > (_state == State.Home ? HomeIdle : AppIdle) && _lifted < 0)
            Close();
        // held still on an app: it comes up, to be moved
        if (_pressedAt is not null && !_carrying && _lifted < 0 && _pressedOn >= 0 && (_pressedFor += delta) > HoldToMove)
            Lift(_pressedOn);
        // carried to an edge of the screen and held there: on to the next page
        if (_lifted >= 0 && (_liftedAt.X < 30 || _liftedAt.X > _screen.Pixels.X - 30) && (_atEdge += delta) > EdgeTurn)
        {
            _atEdge = 0;
            Turn(_liftedAt.X < 30 ? -1 : 1);
        }
        else if (_lifted < 0 || _liftedAt.X is >= 30 and <= 370)
            _atEdge = 0;
    }

    // ---- Opening and closing ------------------------------------------------------------------------------------------

    /// <summary>The readout folds away and the home screen's apps pop in, on the cabinet, or lifted out of it on a small or medium one.</summary>
    private void OpenHome()
    {
        _state = State.Moving;
        _untouched = 0;
        _screen.Live = true;
        var lifts = _homeLifts();
        if (lifts)
            _screen.Lift(up: true, _windowScale(), _canvas);
        GD.Print(lifts ? "Crt: home, lifted out" : "Crt: home");
        _screen.Fold(away: true, 0.32, () =>
        {
            _state = State.Home;
            _home.Arrive(_time);
        });
    }

    /// <summary>Back to the readout: the apps go, and it unfolds.</summary>
    public void Close()
    {
        if (_state == State.Closed)
            return;
        if (_open is { } app)
            app.Visible = false;
        _open = null;
        _lifted = -1;
        _state = State.Moving;
        if (_screen.Lifted)
            _screen.Lift(up: false, _windowScale(), _canvas);
        _home.Leave(() =>
        {
            _screen.Fold(away: false, 0.32, () =>
            {
                _state = State.Closed;
                _screen.Live = false;
            });
        });
    }

    /// <summary>An app opens out of its icon (its tile grows to fill the screen, then the app shows) as the screen lifts out.</summary>
    private void Launch(int index)
    {
        var app = _apps[index];
        if (app.OpensWindow)
        {
            GD.Print($"Crt: {app.Label} opens its window");
            app.Opened();
            return;
        }
        _state = State.Moving;
        _open = app;
        app.Opened();
        _screen.Lift(up: true, _windowScale(), _canvas);
        Zoom(_home.TileRect(index), new Rect2(Vector2.Zero, _screen.Pixels), app.Tint, grow: true, () =>
        {
            _home.Visible = false;
            app.Visible = true;
            app.Modulate = new Color(1, 1, 1, 0);
            app.CreateTween().TweenProperty(app, "modulate:a", 1f, 0.14);
            _state = State.App;
            _untouched = 0;
        });
        _home.Fade(away: true);
    }

    /// <summary>Back to the home screen: the app goes, its tile shrinks back into its icon, and the screen settles into the cabinet (unless the home screen lifts too).</summary>
    private void Return()
    {
        if (_open is not { } app)
            return;
        _state = State.Moving;
        var lifts = _homeLifts();
        if (!lifts)
            _screen.Lift(up: false, _windowScale(), _canvas);
        GD.Print(lifts ? "Crt: back home, still lifted" : "Crt: back home, settled");
        var index = _apps.IndexOf(app);
        _page = index / 4;
        app.Visible = false;
        _home.Visible = true;
        _home.Fade(away: false);
        Zoom(new Rect2(Vector2.Zero, _screen.Pixels), _home.TileRect(index, settled: true), app.Tint, grow: false, () =>
        {
            _open = null;
            _state = State.Home;
            _untouched = 0;
        });
    }

    private void Zoom(Rect2 from, Rect2 to, Color tint, bool grow, Action then)
    {
        var style = new StyleBoxFlat { BgColor = tint, BorderColor = Ink, AntiAliasing = true };
        style.SetBorderWidthAll(4);
        style.SetCornerRadiusAll(18);
        _zoom.AddThemeStyleboxOverride("panel", style);
        _zoom.Position = from.Position;
        _zoom.Size = from.Size;
        _zoom.Visible = true;
        _zoom.Modulate = Colors.White;
        var tween = _zoom.CreateTween().SetParallel();
        tween.TweenProperty(_zoom, "position", to.Position, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(grow ? Tween.EaseType.Out : Tween.EaseType.InOut);
        tween.TweenProperty(_zoom, "size", to.Size, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(grow ? Tween.EaseType.Out : Tween.EaseType.InOut);
        if (grow)
            tween.TweenProperty(_zoom, "modulate:a", 0f, 0.12).SetDelay(0.2);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _zoom.Visible = false;
            then();
        }));
    }

    // ---- The pointer --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A click on the cabinet: on the screen it opens the system, or is the system's; away from the lifted screen it
    /// settles it back. False when it is neither.
    /// </summary>
    public bool Clicked(Vector2 canvasPoint)
    {
        if (_screen.Lifted && !_screen.Covers(canvasPoint))
        {
            Away();
            return true;
        }
        if (_screen.ScreenPoint(canvasPoint) is not { } at || _screen.Prompting)
            return _screen.Covers(canvasPoint);             // on the lifted screen's bezel: nothing
        _untouched = 0;
        if (_lifted >= 0 || _state == State.Moving)
            return true;                                    // putting an app down, or something already moving
        switch (_state)
        {
            case State.Closed:
                OpenHome();
                break;
            case State.Home when PowerMark(at):
                Close();
                break;
            case State.Home when SwitchAt(at) is { } flipped:
                Flip(flipped);
                break;
            case State.Home when _home.AppAt(at) is { } index:
                Launch(index);
                break;
            case State.App when at.Y < Bar && at.X < 90:
                Return();
                break;
            case State.App:
                _open!.Click(at - _open.Position);
                break;
        }
        return true;
    }

    private bool PowerMark(Vector2 at) => at.Y < Bar && at.X > _screen.Pixels.X - 70;

    private const float SwitchStep = 50, SwitchRadius = 19;

    /// <summary>Where a quick switch sits on the bar: leftwards from the power mark, the first one furthest left.</summary>
    private Vector2 SwitchCentre(int index) => new(_screen.Pixels.X - 40 - SwitchStep * (_switches.Count - index), Bar / 2);

    private int? SwitchAt(Vector2 at)
    {
        for (var i = 0; i < _switches.Count; i++)
            if (at.DistanceTo(SwitchCentre(i)) <= SwitchRadius + 5)
                return i;
        return null;
    }

    private void Flip(int index)
    {
        var flipped = _switches[index];
        flipped.Set(!flipped.On());
        GD.Print($"Crt: {flipped.Name} {(flipped.On() ? "on" : "off")}");
    }

    /// <summary>
    /// A press away from the lifted screen (on the cabinet, or anywhere else on the desktop): it settles back, to the
    /// home screen; where the home screen lifts too, the system closes.
    /// </summary>
    public void Away()
    {
        if (!_screen.Lifted || _lifted >= 0)
            return;
        if (_state == State.App && !_homeLifts())
            Return();
        else if (_state is State.Home or State.App)
            Close();
    }

    /// <summary>The pointer moved over the cabinet (null: off it).</summary>
    public void Hover(Vector2? canvasPoint)
    {
        var at = _devPointer ?? (canvasPoint is { } p ? _screen.ScreenPoint(p) : null);
        if (at is not null && _state != State.Closed)
            _untouched = 0;
        _hovered = _state == State.Home && at is { } h ? _home.AppAt(h) ?? -1 : -1;
        _switchHovered = _state == State.Home && at is { } s ? SwitchAt(s) ?? -1 : -1;
        if (_state == State.App && _open is { } app)
            app.Hover(at is { } a ? a - app.Position : null);
        Pointing?.Invoke(_switchHovered >= 0 ? _switches[_switchHovered].Name : _hovered >= 0 && _hovered < _apps.Count ? _apps[_hovered].Label : null);
    }

    /// <summary>The name of the button under the pointer on the home screen (a switch or an app), or null: shown on the marquee.</summary>
    public Action<string?>? Pointing { get; set; }

    private Vector2? _devPointer;

    /// <summary>Dev: a pretend pointer resting on the screen at a point in its own pixels, or none.</summary>
    public void DevHover(Vector2? at) => _devPointer = at;

    /// <summary>A press on the screen while the system is open is the system's (to swipe, or to move an app), not a drag of the window.</summary>
    public bool Grabs(Vector2 canvasPoint) => _state is State.Home or State.App && _screen.Hits(canvasPoint);

    public void Pressed(Vector2 canvasPoint)
    {
        _pressedAt = _screen.ScreenPoint(canvasPoint);
        _pressedFor = 0;
        _pressedOn = _state == State.Home && _pressedAt is { } at ? _home.AppAt(at) ?? -1 : -1;
    }

    public void CarryStarted(Vector2 canvasPoint)
    {
        _carrying = true;
        _swipe = 0;
    }

    public void Carried(Vector2 canvasPoint)
    {
        if (_screen.ScreenPoint(canvasPoint, anywhere: true) is not { } at)
            return;
        _untouched = 0;
        if (_lifted >= 0)
        {
            _liftedAt = at;
            _home.LiftedAt = at;
            // over another place on the page: the app moves there, and the others make room
            if (_home.SlotAt(at) is { } slot)
            {
                var to = Math.Min(_page * 4 + slot, _apps.Count - 1);
                if (to != _lifted)
                {
                    var app = _apps[_lifted];
                    _apps.RemoveAt(_lifted);
                    _apps.Insert(to, app);
                    _lifted = to;
                    _home.Lifted = to;
                }
            }
            return;
        }
        if (_pressedAt is { } from && _state == State.Home)
            _home.Swipe = _swipe = at.X - from.X;
    }

    public void Dropped(Vector2 canvasPoint)
    {
        if (_lifted >= 0)
        {
            PutDown();
            return;
        }
        var swipe = _swipe;
        _home.Swipe = _swipe = 0;
        if (Math.Abs(swipe) < 40)
            return;
        var step = swipe < 0 ? 1 : -1;
        if (_state == State.Home)
            Turn(step);
        else if (_open is { } app)
            app.Swipe(step);
    }

    public void Released()
    {
        if (_lifted >= 0 && !_carrying)
            PutDown();
        _pressedAt = null;
        _pressedOn = -1;
        _carrying = false;
    }

    private void Lift(int index)
    {
        _lifted = index;
        _liftedAt = _pressedAt ?? Vector2.Zero;
        _home.Lifted = index;
        _home.LiftedAt = _liftedAt;
    }

    private void PutDown()
    {
        _lifted = -1;
        _home.Lifted = -1;
        _keepOrder([.. _apps.Select(a => a.Id)]);
        GD.Print($"Crt: apps in order {string.Join(", ", _apps.Select(a => a.Id))}");
    }

    private void Turn(int step)
    {
        var pages = Math.Max(1, (_apps.Count + 3) / 4);
        _page = Math.Clamp(_page + step, 0, pages - 1);
    }

    // ---- Dev --------------------------------------------------------------------------------------------------------------

    /// <summary>Dev: opens the system, and an app by its id ("memos").</summary>
    public void DevOpen(string? app)
    {
        if (_state == State.Closed)
            OpenHome();
        if (app is null)
            return;
        var index = _apps.FindIndex(a => a.Id == app);
        if (index >= 0)
            GetTree().CreateTimer(_state == State.Home ? 0.05 : 0.8).Timeout += () => Launch(index);      // once home
    }

    /// <summary>Dev: clicks the screen at a point in its own pixels.</summary>
    public void DevClickScreen(Vector2 at)
    {
        _untouched = 0;
        if (_state == State.App && _open is { } app)
        {
            if (at.Y < Bar && at.X < 90)
                Return();
            else
                app.Click(at - app.Position);
        }
        else if (_state == State.Home && SwitchAt(at) is { } flipped)
            Flip(flipped);
        else if (_state == State.Home && _home.AppAt(at) is { } index)
            Launch(index);
    }

    /// <summary>Dev: moves the app at one place to another, as holding and carrying it would.</summary>
    public void DevMove(int from, int to)
    {
        Lift(from);
        var app = _apps[from];
        _apps.RemoveAt(from);
        _apps.Insert(Math.Min(to, _apps.Count), app);
        PutDown();
    }

    /// <summary>The home screen: the apps, four to a page, each a tile with a picture and its name; the page dots.</summary>
    private partial class Home(CrtOs os) : Control
    {
        private readonly Dictionary<CrtApp, Vector2> _place = [];   // where each tile is drawn now, easing to where it belongs
        private double _arrived = -100;
        private float _shown = 1;

        public BarView Bar { get; set; } = null!;
        public int Lifted { get; set; } = -1;
        public Vector2 LiftedAt { get; set; }
        public float Swipe { get; set; }

        public void Arrive(double now)
        {
            _arrived = now;
            Visible = true;
            Modulate = Colors.White;
            _place.Clear();
            Bar.Appear(true);
        }

        public void Leave(Action then)
        {
            Bar.Appear(false);
            var tween = CreateTween();
            tween.TweenProperty(this, "modulate:a", 0f, 0.18);
            tween.TweenCallback(Callable.From(() =>
            {
                Visible = false;
                then();
            }));
        }

        public void Fade(bool away)
        {
            Visible = true;
            CreateTween().TweenProperty(this, "modulate:a", away ? 0f : 1f, away ? 0.16 : 0.22);
        }

        /// <summary>Where an app's tile belongs (its page shifted by the page showing, and the swipe under way).</summary>
        private Vector2 Belongs(int index)
        {
            var slot = index % 4;
            var page = index / 4 - os._page;
            return Grid + new Vector2(slot % 2 * Cell.X, slot / 2 * Cell.Y) + new Vector2(page * os._screen.Pixels.X + Swipe, 0);
        }

        /// <summary>An app's tile on screen; <paramref name="settled"/>: where it belongs, not where it is drawn this moment.</summary>
        public Rect2 TileRect(int index, bool settled = false)
        {
            var app = os._apps[index];
            var cell = settled || !_place.TryGetValue(app, out var at) ? Belongs(index) : at;
            return new Rect2(cell + new Vector2((Cell.X - Tile) / 2, 4), new Vector2(Tile, Tile));
        }

        public int? AppAt(Vector2 at)
        {
            for (var i = 0; i < os._apps.Count; i++)
                if (TileRect(i, settled: true).Grow(10).HasPoint(at))
                    return i;
            return null;
        }

        /// <summary>The place on the page showing (0 to 3) under a point, or null.</summary>
        public int? SlotAt(Vector2 at)
        {
            var local = at - Grid;
            var (column, row) = ((int)Math.Floor(local.X / Cell.X), (int)Math.Floor(local.Y / Cell.Y));
            return column is 0 or 1 && row is 0 or 1 ? row * 2 + column : null;
        }

        public override void _Process(double delta)
        {
            if (!Visible)
                return;
            var ease = 1 - Mathf.Exp(-(float)delta * 14);
            for (var i = 0; i < os._apps.Count; i++)
            {
                var app = os._apps[i];
                var target = Belongs(i);
                _place[app] = _place.TryGetValue(app, out var at) ? at.Lerp(target, ease) : target;
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var now = os._time;
            for (var i = 0; i < os._apps.Count; i++)
            {
                if (i == Lifted)
                    continue;                               // drawn last, over the others
                DrawApp(i, now);
            }
            if (Lifted >= 0 && Lifted < os._apps.Count)
                DrawApp(Lifted, now);
            // the page dots, when there is more than one page
            var pages = (os._apps.Count + 3) / 4;
            if (pages > 1)
                for (var p = 0; p < pages; p++)
                {
                    var centre = new Vector2(Size.X / 2 + (p - (pages - 1) / 2f) * 20, Size.Y - 22);
                    DrawCircle(centre, p == os._page ? 6 : 4.5f, p == os._page ? Accent : Ink with { A = 0.35f });
                }
        }

        private void DrawApp(int i, double now)
        {
            var app = os._apps[i];
            var rect = TileRect(i);
            var centre = rect.GetCenter();
            // popping in one after another as the home screen arrives, a little bigger under the pointer, bigger still
            // and wobbling while carried
            var since = (float)(now - _arrived) - i % 4 * 0.06f;
            var pop = since >= 0.3f ? 1 : since <= 0 ? 0 : BackOut(since / 0.3f);
            var hovered = os._hovered == i && Lifted < 0;
            var scale = pop * (i == Lifted ? 1.16f : hovered ? 1.08f : 1);
            var turn = i == Lifted ? Mathf.Sin((float)now * 18) * 0.05f : 0;
            if (i == Lifted)
                centre = LiftedAt;
            if (scale <= 0.01f)
                return;
            DrawSetTransform(centre, turn, Vector2.One * scale);
            var box = new Rect2(-rect.Size / 2, rect.Size);
            if (i == Lifted)
                DrawStyleBox(Style(Ink with { A = 0.25f }, Colors.Transparent), new Rect2(box.Position + new Vector2(5, 7), box.Size));
            DrawStyleBox(Style(app.Tint, Ink), box);
            app.DrawGlyph(this, box.Grow(-16));
            DrawSetTransform(Vector2.Zero);
            if (i != Lifted)
                DrawString(Pixel, new Vector2(centre.X - Cell.X / 2, rect.End.Y + 25), app.Label, HorizontalAlignment.Center, Cell.X, 15,
                           (hovered ? Accent : Ink) with { A = pop });
        }

        private static StyleBoxFlat Style(Color fill, Color line)
        {
            var style = new StyleBoxFlat { BgColor = fill, BorderColor = line, AntiAliasing = true };
            style.SetBorderWidthAll(4);
            style.SetCornerRadiusAll(18);
            return style;
        }

        private static float BackOut(float t)
        {
            const float c = 1.7f;
            t -= 1;
            return 1 + (c + 1) * t * t * t + c * t * t;
        }
    }

    /// <summary>
    /// The bar along the top: on the home screen the time, the quick switches and the power mark; back and the app's
    /// name in an app.
    /// </summary>
    private partial class BarView(CrtOs os) : Control
    {
        public void Appear(bool shown)
        {
            Visible = true;
            CreateTween().TweenProperty(this, "modulate:a", shown ? 1f : 0f, 0.18);
        }

        public override void _Process(double delta)
        {
            if (Visible)
                QueueRedraw();
        }

        public override void _Draw()
        {
            if (os._state == State.Closed)
                return;
            if (os._open is { } app && os._state is State.App or State.Moving)
            {
                DrawPolyline([new Vector2(46, 15), new Vector2(32, 27), new Vector2(46, 39)], Ink, 5, true);
                DrawString(Pixel, new Vector2(70, 37), app.Label, HorizontalAlignment.Center, Size.X - 140, 17, Ink);
            }
            else
            {
                DrawString(Pixel, new Vector2(30, 37), DateTime.Now.ToString("HH:mm"), HorizontalAlignment.Left, -1, 17, Ink);
                for (var i = 0; i < os._switches.Count; i++)
                {
                    var (centre, on) = (os.SwitchCentre(i), os._switches[i].On());
                    var radius = SwitchRadius * (i == os._switchHovered ? 1.08f : 1);
                    DrawCircle(centre, radius, on ? Accent : new Color(1, 1, 1, 0.42f));
                    DrawArc(centre, radius, 0, Mathf.Tau, 32, Ink, 3, true);
                    os._switches[i].Glyph(this, centre, on, on ? Colors.White : Ink);
                }
                // the power mark: back to the readout
                var c = new Vector2(Size.X - 40, 27);
                DrawArc(c, 11, Mathf.DegToRad(-60), Mathf.DegToRad(240), 20, Ink, 4, true);
                DrawLine(c + new Vector2(0, -15), c + new Vector2(0, -2), Ink, 4, true);
            }
        }
    }
}
