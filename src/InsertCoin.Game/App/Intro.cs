using Godot;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// The first run's intro (spec 2026-11-08-a-new-player-gets-it.md, "The first run"). A dark, neglected cabinet seen
/// from a way off, in one beam of light with dust in it. The view walks up to it; the slots pulse until the player
/// clicks them and a coin goes in; the tube strikes on, the dust lifts and the marquee sputters into life. The view leans
/// in until the screen faces the player, and the first run's questions are asked there (today's Welcome, beside the
/// screen). Then it steps back, the companion arrives, and at the landing every light comes on; the dark gives way to the
/// desktop as the cabinet settles where its own window is. Skip, in the corner throughout, goes straight to the desktop.
///
/// It plays in a window of its own across the screen, sharing the cabinet's picture (the same 2D world, its own camera),
/// so the cabinet's window never resizes: hidden under the dark, it draws nothing while the dark fades (or the cabinet
/// would be seen twice), and at the end it draws again a moment before the other closes, so one of them always shows it. The camera is that window's canvas transform: the art, the
/// live screen and the companion move as one. A clip of it (clip.ps1 -Resolution) plays it in the cabinet's own window
/// instead, made the size of a screen, as Godot's movie writer records only that one.
/// </summary>
/// <param name="cabinet">The cabinet's picture, lit by the room as the intro goes.</param>
/// <param name="her">Whoever is on the stool now (someone else once they are chosen), or null with nobody shown.</param>
/// <param name="ask">Opens today's Welcome at the close-up and returns it; the intro goes on once it closes. Null on a replay: nothing is asked.</param>
/// <param name="initials">The player's initials, shown on the screen at a replay's close-up.</param>
/// <param name="resting">Where the cabinet's own window is on the desktop (or would be, in a clip), in screen pixels.</param>
/// <param name="recording">A clip: played in the cabinet's own window, made the size of a screen.</param>
public partial class Intro(CabinetView cabinet, Func<CompanionView?> her, AudioBank audio, Func<Welcome>? ask, string initials, Rect2I resting, bool recording) : Node
{
    /// <summary>Every light came on: the companion has landed (or Skip was pressed before).</summary>
    public event Action? Landed;
    /// <summary>The cabinet's own window shows it again, and the intro's has gone.</summary>
    public event Action? Done;

    // the moments, seconds from the start: it holds at the coin until the slots are clicked, and at the close-up until the
    // questions are answered; what follows the arrival is timed from the landing
    private const float WalkFrom = 1.8f, WalkTo = 5.2f, PulseFrom = WalkTo - 0.6f, CoinAt = 5.9f, CoinIn = 6.5f, Strike = 6.85f,
        LeanFrom = 8.2f, LeanTo = 9.8f, Asked = 10.0f, BackFrom = Asked + 0.6f, BackTo = BackFrom + 2.0f, ArriveAt = BackTo + 0.2f;
    private const float Steps = 4;
    private const double ReplayHold = 1.6;          // a replay shows the close-up this long, with nothing to ask

    // how lit the room leaves the art: in the dark; by the screen's own light (linear light)
    private static readonly Color Dark = new(0.03f, 0.025f, 0.055f), Dim = new(0.3f, 0.24f, 0.42f);
    private static readonly Vector2 BeamFrom = new(-320, -560), BeamLands = new(440, 735);     // canvas units: it lands at the cabinet's foot

    /// <summary>Where the camera looks (canvas units), how close it is, how far it has turned to face the screen, and how far the picture sits off the middle (view units).</summary>
    private readonly record struct Pose(Vector2 Focus, float Zoom, float Face, Vector2 Aside = default);

    private readonly List<(float At, Action Do)> _cues = [];
    private Pose _far, _front, _close, _wide, _desk;
    private Vector2 _faceX, _faceY, _view;
    private float _t, _now, _land = 1e6f, _ui, _aside;       // _aside: the room kept beside the close-up for the Welcome (view units)
    private int _stepsHeard;
    private bool _coined, _answered, _arrived, _landed, _skipped, _finished;
    private Viewport _camera = null!;               // whose canvas transform is the camera: the intro's window, or the cabinet's in a clip
    private Window? _window;                        // the intro's own window (none in a clip)
    private Window? _welcome;
    private Rect2 _slots;
    private Vector2I _cabinetSize;
    private ShaderMaterial _art = null!, _beam = null!, _room = null!;
    private ColorRect _beamRect = null!;
    private Control _skip = null!;
    private Motes _motes = null!;

    /// <summary>The intro is showing (from the start until its window has gone).</summary>
    public bool Playing => !_finished;

    public void Start()
    {
        var main = GetWindow();
        var skin = cabinet.Skin;
        _cabinetSize = DisplayServer.WindowGetSize();
        Node host;
        Vector2I origin;                            // the view's top left corner, in screen pixels
        float pixel;                                // view units per screen pixel
        var screen = DisplayServer.WindowGetCurrentScreen();
        if (recording)
        {
            _camera = main;
            host = this;
            origin = Vector2I.Zero;
            _view = main.GetVisibleRect().Size;
            pixel = _view.X / DisplayServer.WindowGetSize().X;
            _ui = _view.Y / 1080;                   // Skip as big against the picture as on a 1080p screen at 100%
        }
        else
        {
            // the screen less the taskbar: a window exactly the screen's size can be taken for a full-screen game, and drawn without its transparency
            var area = DisplayServer.ScreenGetUsableRect(screen);
            _window = new Window
            {
                Title = "Beside the Cabinet intro", Borderless = true, Transparent = true, TransparentBg = true, Unfocusable = true,
                Position = area.Position, Size = area.Size, World2D = main.World2D, UseHdr2D = ProjectSettings.GetSetting("rendering/viewport/hdr_2d").AsBool(),     // the root takes it from the project, not its property
            };
            AddChild(_window);
            _window.WindowInput += Clicked;
            _camera = _window;
            host = _window;
            origin = area.Position;
            _view = area.Size;
            pixel = 1;
            _ui = DisplayServer.ScreenGetDpi(screen) / 96f;
        }

        // the affine map that turns the screen's slanted quad to face the viewer: for the plane of the cabinet's front, as
        // turning the camera would be
        var q = skin.Anchors.ScreenQuad;
        Vector2 u = (q[1] - q[0] + q[2] - q[3]) / 2, v = (q[3] - q[0] + q[2] - q[1]) / 2;
        var det = u.X * v.Y - u.Y * v.X;
        var height = v.Length();
        var width = height * skin.Anchors.ScreenSize.X / skin.Anchors.ScreenSize.Y;
        _faceX = new Vector2(width * v.Y / det, -height * u.Y / det);
        _faceY = new Vector2(-width * v.X / det, height * u.X / det);

        Pose Fit(float x, float y, float w, float h) => new(new Vector2(x + w / 2, y + h / 2), Mathf.Min(_view.X / w, _view.Y / h), 0);
        _far = Fit(-430, -250, 1500, 1330);
        _front = Fit(262, 104, 336, 476);
        // close up, the screen in the middle of what the Welcome beside it leaves
        // (on a narrow screen it goes over the screen instead)
        _aside = ask is null || recording ? 0 : (WelcomeWidth + 2 * Margin) * _ui * pixel;
        if (_view.X - _aside < _view.X / 2)
            _aside = 0;
        _close = new Pose(q.Aggregate((a, b) => a + b) / 4, Mathf.Min(0.86f * _view.Y / height, 0.9f * (_view.X - _aside) / width), 1, new Vector2(-_aside / 2, 0));
        _wide = Fit(-40, 50, 720, 780);
        // the end: the cabinet just where its own window is, at its size
        var zoom = (float)resting.Size.X / skin.Canvas.X * pixel;
        _desk = new Pose((_view / 2 - (Vector2)(resting.Position - origin) * pixel) / zoom, zoom, 0);

        // the room
        var behind = new CanvasLayer { Layer = -1 };
        var front = new CanvasLayer { Layer = 1 };
        host.AddChild(behind);
        host.AddChild(front);
        _room = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/intro_night.gdshader") };
        _room.SetShaderParameter("aspect", _view.X / _view.Y);
        var night = new ColorRect { Material = _room, MouseFilter = Control.MouseFilterEnum.Ignore };
        behind.AddChild(night);
        night.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _beam = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/intro_beam.gdshader") };
        _beam.SetShaderParameter("aspect", _view.X / _view.Y);
        _beamRect = new ColorRect { Material = _beam, MouseFilter = Control.MouseFilterEnum.Ignore };
        front.AddChild(_beamRect);
        _beamRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _motes = new Motes { View = _view };
        front.AddChild(_motes);
        _skip = SkipButton(front);

        // the slots, to click: where their glow is drawn
        var glow = skin.Layers["slot-glow"].GetImage().GetUsedRect();
        _slots = new Rect2((Vector2)glow.Position / skin.Scale, (Vector2)glow.Size / skin.Scale).Grow(14);

        // the cabinet in the dark
        _art = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/intro_cabinet.gdshader") };
        var (r0, r1, r2) = Homography.QuadToUnit(q);
        _art.SetShaderParameter("canvas", skin.Canvas);
        _art.SetShaderParameter("inv_h_row0", r0);
        _art.SetShaderParameter("inv_h_row1", r1);
        _art.SetShaderParameter("inv_h_row2", r2);
        cabinet.Art["base"].Material = _art;
        cabinet.Screen.Visible = cabinet.Lights.Visible = cabinet.Marquee.Visible = false;
        Aim(_far, Vector2.Zero, 0);

        Cue(CoinAt, DropCoin);
        Cue(CoinIn, () =>
        {
            cabinet.FlashSlot();
            audio.Play("token");
        });
        Cue(Strike, () =>
        {
            cabinet.Screen.Visible = true;
            cabinet.Screen.Prompt("PLAYER 1", "ENTER YOUR NAME", ask is null ? initials : "");
            cabinet.Screen.Fold(false, 0.9);
            audio.Play("power_on");
            _motes.Puff(_camera.CanvasTransform * new Vector2(425, 420) / _view, 170 * _front.Zoom / _view.X, 310 * _front.Zoom / _view.Y);
        });
        Cue(Asked, AskQuestions);
        Cue(BackFrom + 0.2f, () => cabinet.Screen.Prompt("PLAYER 2", "JOINED", ""));
        Cue(ArriveAt, Arrive);
        _Process(0);                                // the dark from the first frame: no frame of the cabinet lit, or of them on the stool
        GD.Print($"Intro: the room is dark ({(recording ? "in the cabinet's own window, for a clip" : $"in a window of its own across the screen, {_view.X}x{_view.Y}")}); " +
                 $"the cabinet's window is {_cabinetSize.X}x{_cabinetSize.Y} at {DisplayServer.WindowGetPosition()}");
    }

    private const float WelcomeWidth = 640, Margin = 40;       // as Paper.Open sizes the Welcome, before the screen's scaling

    private void Cue(float at, Action then) => _cues.Add((at, then));

    public override void _Process(double delta)
    {
        if (_finished)
            return;
        var dt = (float)delta;
        _now += dt;
        // the clock holds at the coin until the slots are clicked, and at the close-up until the questions are answered
        _t = Mathf.Min(_t + dt, !_coined ? CoinAt : !_answered ? Asked : float.MaxValue);
        var t = _t;
        foreach (var cue in _cues.Where(c => c.At <= t).OrderBy(c => c.At).ToList())
        {
            _cues.Remove(cue);
            cue.Do();
            if (_finished)
                return;
        }

        // the camera, with a footfall at each step of the walk
        var walk = Mathf.Clamp((t - WalkFrom) / (WalkTo - WalkFrom), 0, 1);
        if ((int)(walk * Steps) > _stepsHeard)
        {
            _stepsHeard = (int)(walk * Steps);
            audio.Play("step", 0.9f + 0.08f * (_stepsHeard % 2));
        }
        var stepping = Mathf.Sin(walk * Mathf.Pi);                  // in and out of the walk
        var stride = Mathf.Sin(walk * Mathf.Pi * Steps);
        var pose = PoseAt(t, Mathf.SmoothStep(0, 1, walk) + 0.012f * stepping * Mathf.Sin(walk * Mathf.Tau * Steps));
        var shift = new Vector2(_view.X * 0.003f * stepping * stride, -_view.Y * 0.012f * stepping * Mathf.Abs(stride));
        Aim(pose, shift, 0.004f * stepping * stride);

        // the light
        var land = _land;
        var light = Keyed(t, (Strike + 0.3f, 0), (Strike + 1.6f, 1), (land - 0.02f, 1), (land + 0.5f, 2));
        var room = light < 1 ? Dark.Lerp(Dim, light) : Dim.Lerp(Colors.White, light - 1);
        var tube = t < Strike ? 0 : Keyed(t, (Strike, 0.012f), (Strike + 0.3f, 0.012f), (Strike + 0.58f, 1));
        var flare = t < Strike ? 0 : Mathf.Max(Keyed(t, (Strike, 3.2f), (Strike + 0.3f, 2.2f), (Strike + 1.1f, 0)), t < land ? 0 : Keyed(t, (land, 0.7f), (land + 0.6f, 0)));
        var dust = Keyed(t, (Strike + 0.1f, 1), (Strike + 1.9f, 0));
        var dark = Keyed(t, (land + 0.6f, 1), (land + 3.0f, 0));
        var beam = Keyed(t, (Strike, 1), (LeanFrom, 0.55f), (LeanTo, 0), (BackFrom + 0.7f, 0), (BackTo, 0.5f), (land - 0.1f, 0.5f), (land + 0.5f, 0));
        var to = _camera.CanvasTransform;

        _art.SetShaderParameter("tube", tube);
        _art.SetShaderParameter("flare", flare);
        _art.SetShaderParameter("scan", pose.Face);
        _art.SetShaderParameter("dust", dust);
        _beam.SetShaderParameter("origin", to * BeamFrom / _view);
        _beam.SetShaderParameter("strength", beam);
        _beam.SetShaderParameter("reach", (BeamLands - BeamFrom).Length() * pose.Zoom / _view.Y);
        _room.SetShaderParameter("fade", dark);
        _room.SetShaderParameter("glow_at", to * _close.Focus / _view);
        _room.SetShaderParameter("glow_size", 760 * pose.Zoom / _view.Y);
        _room.SetShaderParameter("glow", tube * (2 - Mathf.Max(light, 1)));
        _room.SetShaderParameter("pool", beam);
        _room.SetShaderParameter("pool_at", to * BeamLands / _view);
        _room.SetShaderParameter("pool_size", new Vector2(390, 66) * pose.Zoom / _view.Y);
        _beamRect.Visible = beam > 0.002f;
        _motes.Amount = beam;
        _motes.Zoom = pose.Zoom / _far.Zoom;
        _motes.Beam = to * BeamFrom / _view;

        var on = Marquee(t, _now);
        var art = cabinet.Art;
        art["base"].SelfModulate = art["check-cap"].SelfModulate = room;
        cabinet.Tray.Modulate = cabinet.Printer.Modulate = cabinet.Receipt.Modulate = cabinet.Memos.Modulate = room;
        art["screen-glass"].SelfModulate = new Color(1, 1, 1, Mathf.Lerp(0.06f, 1, tube));
        art["screen-glow"].SelfModulate = new Color(1, 1, 1, tube * (1 - 0.9f * pose.Face));       // close up, the words keep their ink
        art["marquee-glow"].SelfModulate = new Color(1, 1, 1, on ? 1 : 0);
        art["slot-glow"].SelfModulate = new Color(1, 1, 1, t < PulseFrom ? 0 : !_coined || t < CoinIn ? 0.5f + 0.5f * Mathf.Sin(_now * 4.2f) : 1);
        cabinet.Marquee.Visible = on;
        if (her() is { } shown)
        {
            shown.Visible = _arrived;               // someone chosen meanwhile is seated, and arrives in their turn
            shown.Modulate = room;
        }
        // under the dark the cabinet's own window can't be seen; as the dark fades it draws nothing until the end, so the
        // cabinet isn't seen twice while the view settles onto it
        if (_window is not null && t >= land + 0.6f && GetWindow().CanvasCullMask != 0)
            GetWindow().CanvasCullMask = 0;
        if (t >= land + 3.45f)                      // the dark gone, and the cabinet where its own window is
            Finish();
    }

    /// <summary>The slots clicked (or the dev action "coin"): a coin goes in, once the view has walked up to them.</summary>
    public void Coin()
    {
        if (_coined || _finished)
            return;
        _coined = true;
        GD.Print("Intro: a coin goes in");
    }

    /// <summary>Skip (its button, Esc, or the dev action "introskip"): straight to the desktop, the questions closed with what was answered kept.</summary>
    public void Skip()
    {
        if (_finished)
            return;
        GD.Print("Intro: skipped");
        _skipped = true;
        if (IsInstanceValid(_welcome))
            _welcome!.EmitSignal(Window.SignalName.CloseRequested);     // what was answered is kept, as closing it keeps it
        if (her() is { } settling)
        {
            if (!_arrived)
                settling.Arrive(pose: false);       // on the stool under a flash
            else
                settling.SeatNow();
        }
        _arrived = true;
        if (!_landed)
            Land();
        Aim(_desk, Vector2.Zero, 0);
        Finish();
    }

    private void Clicked(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && _slots.HasPoint(_camera.CanvasTransform.AffineInverse() * click.Position))
            Coin();
    }

    /// <summary>At the close-up: today's Welcome asks its questions beside the screen, which names each page as it comes.</summary>
    private void AskQuestions()
    {
        if (ask is null)
        {
            GetTree().CreateTimer(ReplayHold).Timeout += Answered;
            return;
        }
        var welcome = ask();
        _welcome = welcome;
        welcome.Paged += Page;
        welcome.Done += _ => Answered();
        Page(0);
        if (!Paper.OffScreen && _window is not null)       // beside the screen, or over it on a narrow one
            welcome.Position = _window.Position + new Vector2I(_aside > 0 ? (int)(_view.X - Margin * _ui) - welcome.Size.X : ((int)_view.X - welcome.Size.X) / 2,
                                                               ((int)_view.Y - welcome.Size.Y) / 2);
        GD.Print("Intro: the questions, at the close-up");
    }

    private void Page(int page)
    {
        var (big, caption) = page switch
        {
            0 => ("PLAYER 1", "ENTER YOUR NAME"),
            1 => ("PLAYER 2", "SELECT PLAYER 2"),
            2 => ("MODE", "SELECT MODE"),
            _ => ("AI", "CONNECT AN AI"),
        };
        cabinet.Screen.Prompt(big, caption, "");
    }

    private void Answered()
    {
        if (_answered || _finished || _skipped)
            return;
        _answered = true;
        _welcome = null;
        GD.Print("Intro: answered; the view steps back");
    }

    /// <summary>Whoever is on the stool arrives; what follows is timed from their landing.</summary>
    private void Arrive()
    {
        var arriving = her();
        _arrived = true;
        if (arriving is not null)
        {
            arriving.Visible = true;
            arriving.Arrive();
        }
        _land = _t + Mathf.Max((float)(arriving?.Arriving ?? 0), 0.4f);
        Cue(_land, Land);
    }

    private void Land()
    {
        if (_landed)
            return;
        _landed = true;
        cabinet.Lights.Visible = true;
        cabinet.Lights.Burst(LightMode.Celebrate, 2.4);
        cabinet.Screen.ClearPrompt();
        _skip.Visible = false;
        GD.Print("Intro: they have landed, and every light comes on");
        Landed?.Invoke();
    }

    private Pose PoseAt(float t, float walked)
    {
        float Eased(float from, float to) => Mathf.SmoothStep(from, to, t);
        float deskFrom = _land + 0.85f, deskTo = _land + 3.35f;
        return t < WalkFrom ? _far
            : t < WalkTo ? Move(_far, _front, walked)
            : t < LeanFrom ? _front
            : t < LeanTo ? Move(_front, _close, Eased(LeanFrom, LeanTo))
            : t < BackFrom ? _close
            : t < BackTo ? Move(_close, _wide, Eased(BackFrom, BackTo))
            : t < deskFrom ? _wide
            : t < deskTo ? Move(_wide, _desk, Eased(deskFrom, deskTo))
            : _desk;
    }

    /// <summary>A camera going in a straight line: its distance and what it looks at change evenly, so things grow faster as it nears them.</summary>
    private static Pose Move(Pose a, Pose b, float s) =>
        new(a.Focus.Lerp(b.Focus, s), 1 / Mathf.Lerp(1 / a.Zoom, 1 / b.Zoom, s), Mathf.Lerp(a.Face, b.Face, s), a.Aside.Lerp(b.Aside, s));

    private void Aim(Pose pose, Vector2 shift, float roll)
    {
        var look = new Transform2D(Vector2.Right.Lerp(_faceX, pose.Face) * pose.Zoom, Vector2.Down.Lerp(_faceY, pose.Face) * pose.Zoom, Vector2.Zero);
        if (roll != 0)
            look = new Transform2D(roll, Vector2.Zero) * look;
        look.Origin = _view / 2 + pose.Aside + shift - look.BasisXform(pose.Focus);
        _camera.CanvasTransform = look;
    }

    /// <summary>Nearly dead until the tube strikes, a flash now and then; it sputters into life after it.</summary>
    private static bool Marquee(float t, float now)
    {
        if (t >= Strike + 1.15f)
            return true;
        if (t >= Strike + 0.5f)
            return Mathf.PosMod(t * 11, 1) < 0.55f;
        var beat = Mathf.PosMod(now, 3.4f) / 3.4f;
        return beat is > 0.84f and < 0.862f or > 0.878f and < 0.895f;
    }

    private static float Keyed(float t, params (float T, float V)[] keys)
    {
        if (t <= keys[0].T)
            return keys[0].V;
        for (var i = 1; i < keys.Length; i++)
            if (t < keys[i].T)
                return Mathf.Lerp(keys[i - 1].V, keys[i].V, Mathf.SmoothStep(keys[i - 1].T, keys[i].T, t));
        return keys[^1].V;
    }

    private Control SkipButton(CanvasLayer layer)
    {
        var corner = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(corner);
        corner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            corner.AddThemeConstantOverride($"margin_{side}", (int)(24 * _ui));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, SizeFlagsVertical = Control.SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore };
        corner.AddChild(row);
        var skip = new Button { Text = "Skip", Theme = Paper.WindowTheme(), ThemeTypeVariation = "Quiet", FocusMode = Control.FocusModeEnum.None };
        skip.AddThemeFontSizeOverride("font_size", (int)(15 * _ui));
        skip.Pressed += Skip;
        row.AddChild(skip);
        return skip;
    }

    private void DropCoin()
    {
        var at = cabinet.Skin.Anchors.CoinSlot;
        var gold = cabinet.Skin.Sprites["coin-gold"];
        var coin = new Sprite2D
        {
            Texture = gold.Texture,
            Centered = false,
            Offset = -gold.Origin * cabinet.Skin.Scale,
            Position = at + new Vector2(46, 78),
            Scale = Vector2.One * 1.05f,
            Rotation = -0.5f,
            Modulate = new Color(0.82f, 0.78f, 0.72f),
        };
        cabinet.AddChild(coin);
        var go = cabinet.CreateTween().SetParallel();
        go.TweenProperty(coin, "position", at, CoinIn - CoinAt).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        go.TweenProperty(coin, "scale", new Vector2(0.16f, 0.52f), CoinIn - CoinAt).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        go.TweenProperty(coin, "rotation", 0f, CoinIn - CoinAt);
        go.Chain().TweenProperty(coin, "modulate:a", 0f, 0.05);
        go.Chain().TweenCallback(Callable.From(coin.QueueFree));
    }

    /// <summary>
    /// The cabinet as it always is, and its own window drawing it again: the intro's window, showing the same picture in
    /// the same place, closes a moment later. A test run compares the two windows' pictures there.
    /// </summary>
    private async void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        foreach (var sprite in cabinet.Art.Values)
            sprite.SelfModulate = Colors.White;
        cabinet.Art["base"].Material = null;
        cabinet.Tray.Modulate = cabinet.Printer.Modulate = cabinet.Receipt.Modulate = cabinet.Memos.Modulate = Colors.White;
        cabinet.Screen.Visible = cabinet.Lights.Visible = cabinet.Marquee.Visible = true;
        if (her() is { } settled)
        {
            settled.Visible = true;
            settled.Modulate = Colors.White;
        }
        foreach (var layer in (_window as Node ?? this).GetChildren().OfType<CanvasLayer>())
            layer.Visible = false;
        if (recording)
        {
            GD.Print("Intro: done");
            Done?.Invoke();
            QueueFree();
            return;
        }
        var main = GetWindow();
        main.CanvasCullMask = uint.MaxValue;
        if (Paper.OffScreen)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Compare(main);
        }
        GetTree().CreateTimer(0.1).Timeout += () =>
        {
            GD.Print("Intro: done");
            Done?.Invoke();
            QueueFree();
        };
    }

    /// <summary>
    /// Test runs: the intro's window and the cabinet's, both drawn in the same frame at the handover, should show the
    /// cabinet alike in the same place (no closer match a pixel off), and the cabinet's window should be the size it was.
    /// </summary>
    private void Compare(Window main)
    {
        static Image Picture(Viewport viewport)
        {
            var image = viewport.GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            return image;
        }
        var view = Picture(_window!);
        var own = Picture(main);
        byte[] a = own.GetData(), b = view.GetData();
        int w = own.GetWidth(), h = own.GetHeight(), vw = view.GetWidth(), vh = view.GetHeight();
        var at = DisplayServer.WindowGetPosition() - _window!.Position;
        double Differ(int dx, int dy)
        {
            double sum = 0;
            var count = 0;
            for (var y = 0; y < h; y += 2)
            for (var x = 0; x < w; x += 2)
            {
                var i = (y * w + x) * 4;
                int vx = x + at.X + dx, vy = y + at.Y + dy;
                if (a[i + 3] < 200 || vx < 0 || vy < 0 || vx >= vw || vy >= vh)
                    continue;
                var j = (vy * vw + vx) * 4;
                sum += Math.Abs(a[i] - b[j]) + Math.Abs(a[i + 1] - b[j + 1]) + Math.Abs(a[i + 2] - b[j + 2]);
                count++;
            }
            return count == 0 ? 100 : sum / count / 3 / 255 * 100;
        }
        var here = Differ(0, 0);
        var off = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Min(d => Differ(d.Item1, d.Item2));
        var size = DisplayServer.WindowGetSize();
        var fine = here < 3 && here < off && size == _cabinetSize;
        GD.Print($"Intro: the handover {(fine ? "lines up" : "is out")}: the two windows' pictures differ by {here:F2} % (a pixel off, {off:F2} %); " +
                 $"the cabinet's window is {size.X}x{size.Y}, as it was {_cabinetSize.X}x{_cabinetSize.Y}");
    }

    /// <summary>The dust in the air, drawn over everything in the view's own space: bright inside the beam, faint outside it.</summary>
    private sealed partial class Motes : Node2D
    {
        private sealed class Mote
        {
            public Vector2 At;
            public float Depth, Size, Fall, Drift, Phase, Alpha, Age;
            public bool Puffed;
        }

        private readonly List<Mote> _all = [];
        private readonly RandomNumberGenerator _random = new() { Seed = 255 };
        public Vector2 View, Beam;
        public float Amount, Zoom = 1;

        public override void _Ready()
        {
            for (var i = 0; i < 130; i++)
                _all.Add(new Mote
                {
                    At = new Vector2(_random.Randf(), _random.Randf()),
                    Depth = _random.RandfRange(0.25f, 1.5f),
                    Size = _random.RandfRange(0.7f, 1.7f),
                    Fall = _random.RandfRange(0.002f, 0.013f),
                    Drift = _random.RandfRange(-0.006f, 0.006f),
                    Phase = _random.RandfRange(0, 6.28f),
                    Alpha = _random.RandfRange(0.3f, 0.85f),
                });
        }

        /// <summary>The dust coming off the cabinet as it first lights.</summary>
        public void Puff(Vector2 centre, float across, float high)
        {
            for (var i = 0; i < 60; i++)
                _all.Add(new Mote
                {
                    At = centre + new Vector2(_random.RandfRange(-across, across), _random.RandfRange(-high, high)),
                    Depth = 0,
                    Size = _random.RandfRange(0.8f, 2.1f),
                    Fall = _random.RandfRange(-0.07f, -0.015f),
                    Drift = _random.RandfRange(-0.03f, 0.03f),
                    Phase = _random.RandfRange(0, 6.28f),
                    Alpha = _random.RandfRange(0.45f, 0.9f),
                    Puffed = true,
                });
        }

        public override void _Process(double delta)
        {
            var dt = (float)delta;
            foreach (var m in _all)
            {
                m.Phase += dt * 1.2f;
                m.At += new Vector2(m.Drift + 0.004f * Mathf.Sin(m.Phase), m.Fall) * dt;
                m.Age += dt;
            }
            _all.RemoveAll(m => m.Puffed && m.Age > 2.6f);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var aspect = View.X / View.Y;
            var size = View.Y / 820;                    // as big against the screen whatever its size
            foreach (var m in _all)
            {
                var at = m.At;
                var alpha = m.Alpha;
                if (m.Puffed)
                    alpha *= 0.5f * (1 - Mathf.Clamp(m.Age / 2.6f, 0, 1));
                else
                {
                    // nearer motes rush past as the view comes closer; wrapped round, faded at the edges so none is seen to jump
                    at = (((at - Vector2.One / 2) * Mathf.Pow(Zoom, 0.55f * m.Depth)) + Vector2.One / 2).PosMod(1);
                    alpha *= Mathf.SmoothStep(0, 0.07f, at.X) * Mathf.SmoothStep(1, 0.93f, at.X) * Mathf.SmoothStep(0, 0.07f, at.Y) * Mathf.SmoothStep(1, 0.93f, at.Y) * Amount;
                    var d = (at - Beam) * new Vector2(aspect, 1);
                    alpha *= Mathf.Lerp(0.006f, 0.55f, Mathf.SmoothStep(0.17f, 0.05f, Mathf.Abs(Mathf.Atan2(d.X, d.Y) - 0.53f)));
                }
                if (alpha < 0.004f)
                    continue;
                var radius = m.Size * (0.5f + m.Depth * 0.45f) * size;
                DrawCircle(at * View, radius * 2.2f, new Color(1, 0.94f, 0.98f, alpha * 0.16f), true, -1, true);      // its soft edge
                DrawCircle(at * View, radius, new Color(1, 0.94f, 0.98f, alpha), true, -1, true);
            }
        }
    }
}
