using Godot;
using InsertCoin.Game.Cabinet;

namespace InsertCoin.Game.App;

/// <summary>
/// The CRT's own small system, like a phone's (the user's idea, 2026-10-06). A click on the screen folds the readout
/// away and the home screen's apps pop in, four to a page, the pages a swipe apart. Holding an app picks it up, to
/// put it somewhere else (the order is kept). An app opens out of its icon and closes back into it; the system closes
/// by itself after a while untouched, or from the power mark, and the readout unfolds again.
/// </summary>
public partial class CrtOs : Node
{
    private enum State { Closed, Moving, Home, App }

    private const float Bar = 54, Tile = 100;
    private static readonly Vector2 Cell = new(164, 140), Grid = new(36, 56);
    private const double HoldToMove = 0.45, HomeIdle = 15, AppIdle = 40, EdgeTurn = 0.55;
    private static readonly Color Ink = new("6a1c5e"), Accent = new("e0457f");
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");

    private ScreenView _screen = null!;
    private readonly List<CrtApp> _apps = [];
    private Action<string[]> _keepOrder = null!;
    private Home _home = null!;
    private Panel _zoom = null!;
    private CrtApp? _open;
    private State _state;
    private int _page, _hovered = -1;
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

    /// <param name="apps">Every app there is.</param>
    /// <param name="order">The home screen's order, by app id, as kept; apps not in it come after.</param>
    /// <param name="keepOrder">Keeps the order after the player has moved an app.</param>
    public void Build(ScreenView screen, IEnumerable<CrtApp> apps, IReadOnlyList<string> order, Action<string[]> keepOrder)
    {
        _screen = screen;
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

    /// <summary>The readout folds away and the home screen's apps pop in.</summary>
    private void OpenHome()
    {
        _state = State.Moving;
        _untouched = 0;
        _screen.Live = true;
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
        _home.Leave(() =>
        {
            _screen.Fold(away: false, 0.32, () =>
            {
                _state = State.Closed;
                _screen.Live = false;
            });
        });
    }

    /// <summary>An app opens out of its icon: its tile grows to fill the screen, then the app shows.</summary>
    private void Launch(int index)
    {
        var app = _apps[index];
        _state = State.Moving;
        _open = app;
        app.Opened();
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

    /// <summary>Back to the home screen: the app goes, its tile shrinks back into its icon.</summary>
    private void Return()
    {
        if (_open is not { } app)
            return;
        _state = State.Moving;
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

    /// <summary>A click on the cabinet: on the screen it opens the system, or is the system's. False when not on the screen.</summary>
    public bool Clicked(Vector2 canvasPoint)
    {
        if (_screen.ScreenPoint(canvasPoint) is not { } at || _screen.Prompting)
            return false;
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

    /// <summary>The pointer moved over the cabinet (null: off it).</summary>
    public void Hover(Vector2? canvasPoint)
    {
        var at = canvasPoint is { } p ? _screen.ScreenPoint(p) : null;
        if (at is not null && _state != State.Closed)
            _untouched = 0;
        _hovered = _state == State.Home && at is { } h ? _home.AppAt(h) ?? -1 : -1;
        if (_state == State.App && _open is { } app)
            app.Hover(at is { } a ? a - app.Position : null);
    }

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
            GetTree().CreateTimer(_state == State.Home ? 0.05 : 0.8).Timeout += () => Launch(index);
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

    /// <summary>The bar along the top: the time and the power mark on the home screen; back and the app's name in an app.</summary>
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
                // the power mark: back to the readout
                var c = new Vector2(Size.X - 40, 27);
                DrawArc(c, 11, Mathf.DegToRad(-60), Mathf.DegToRad(240), 20, Ink, 4, true);
                DrawLine(c + new Vector2(0, -15), c + new Vector2(0, -2), Ink, 4, true);
            }
        }
    }
}
