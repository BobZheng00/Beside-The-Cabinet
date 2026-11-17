using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;

namespace InsertCoin.Game.App;

/// <summary>
/// The chalkboard sign: a small A-frame on the desk beside the cabinet, on its other side from the stool (beyond the
/// stool when there's no room there), in a window of its own that never takes the keyboard. It moves with the cabinet
/// and chalks a list (the missions, with what each pays, ticked as each pays); the player slides it along the bottom,
/// clicks a line to go and do it, and puts it away by lifting it off the desk or with a right click (spec
/// 2026-11-16-the-chalkboard-sign.md). What it shows, and whether it stands, is the cabinet's to say (see Main).
/// </summary>
public partial class ChalkboardSign : Node
{
    /// <summary>A line chalked on the board: its words, what's beside them, whether it's ticked, and what a click on it opens (null: nothing).</summary>
    public sealed record Line(string Text, string Note, bool Ticked, string? Opens);

    // the picture (tools/art/chalkboard_sign.py), in its own units: its size, the slate, where its feet stand
    private static readonly Vector2 Picture = new(236, 256);
    private static readonly Rect2 Slate = new(28, 22, 180, 166);
    private const float Feet = 246;
    // where it stands, in the cabinet's canvas units: the desk under the cabinet's front, past the cabinet's right side
    // (its cord's plug), and past the stool's left; how much nearer it slides than where it stands at first
    private const float Desk = 760, PastCabinet = 620, PastStool = 70, Nearest = -36;
    private const float LeastScale = 0.85f;                // on a small cabinet (S draws it at 0.6) the chalk still reads
    private const float LiftAway = 0.35f;                   // lifted this much of its height off the desk, it's put away

    /// <summary>A line was clicked: what it opens.</summary>
    public event Action<string>? Opened;
    /// <summary>The player put it away: lifted off the desk, or from its right-click menu (how, for the log).</summary>
    public event Action<string>? PutAway;

    /// <summary>Where it stands on the screen while up, for windows that open beside the cabinet to keep clear of; null while it's away.</summary>
    public static Rect2I? Standing { get; private set; }

    /// <summary>The cabinet's window with the sign beside it, while it stands.</summary>
    public static Rect2I WithCabinet(Rect2I cabinet) => Standing is { } sign ? cabinet.Merge(sign) : cabinet;

    private Window _window = null!;
    private Board _board = null!;
    private Bitmap _solid = null!;
    private PopupMenu _menu = null!;
    private readonly Node2D _sizer = new();             // the menu takes its scale from the sign's window times this
    private Vector2 _canvas;
    private Func<float> _slide = null!;
    private Action<float> _slid = null!;
    private string _heading = "", _headingNote = "";
    private IReadOnlyList<Line> _lines = [];
    private readonly Dictionary<string, double> _tickedAt = [];   // lines ticked while it stood, as they're chalked
    private bool _up, _onLeft;
    private float _shown;                                   // 0 folded away, 1 standing
    private float _lift;                                    // pixels it's held up off the desk
    private float? _dragSlide;
    private bool _pressed, _dragging;
    private Vector2I _pressAt;
    private float _slideAtPress, _unit = 1, _farthest;
    private bool? _clickThrough;
    private Tween? _tween;

    /// <summary>Whether it stands now (or is standing up).</summary>
    public bool Up => _up;

    /// <param name="canvas">The cabinet's canvas, for its window's scale.</param>
    /// <param name="slide">How far it was slid from where it first stands, in the cabinet's canvas units (further off is more).</param>
    /// <param name="slid">Keeps where the player slid it.</param>
    public void Build(Vector2 canvas, Func<float> slide, Action<float> slid)
    {
        _canvas = canvas;
        _slide = slide;
        _slid = slid;
        // shortcut: the Cozy skin's sign, the only skin; a second skin would bring its own
        var picture = GD.Load<Texture2D>("res://skins/cozy/sign.png");
        _solid = new Bitmap();
        _solid.CreateFromImageAlpha(picture.GetImage(), 0.35f);
        _window = new Window
        {
            Title = "Beside the Cabinet sign", Borderless = true, Transparent = true, TransparentBg = true, Transient = true,
            Unfocusable = true, Visible = false,           // it never takes the keyboard
        };
        _board = new Board(this, picture) { Size = Picture };
        _window.AddChild(_board);
        _menu = new PopupMenu();
        _menu.AddItem("Put away");
        _menu.IdPressed += _ => Away("from its menu");
        _window.AddChild(_sizer);
        _sizer.AddChild(_menu);
        AddChild(_window);
    }

    /// <summary>Stands it up (if it isn't) with these lines chalked; lines newly ticked get their tick chalked.</summary>
    public void Stand(string heading, string headingNote, IReadOnlyList<Line> lines)
    {
        var same = heading == _heading && headingNote == _headingNote && lines.SequenceEqual(_lines);
        if (same && _up)
            return;
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var line in lines.Where(l => l.Ticked && _up && _lines.Any(o => o.Text == l.Text && !o.Ticked)))
            _tickedAt[line.Text] = now;
        (_heading, _headingNote, _lines) = (heading, headingNote, lines);
        GD.Print($"Sign: {(_up ? "chalked" : "stands")}: {heading}{(headingNote.Length > 0 ? $" ({headingNote})" : "")}: " +
                 string.Join(" | ", lines.Select(l => (l.Ticked ? "[x] " : "") + l.Text + (l.Note.Length > 0 ? " " + l.Note : ""))));
        _board.QueueRedraw();
        if (_up)
            return;
        _up = true;
        _lift = 0;
        _clickThrough = null;                               // a window shown again is a new one in Windows' terms
        Place();
        _window.Visible = true;
        Place();                                            // shown, a window goes back to where it was: here, by the cabinet, from its first frame
        DesktopWindow.JustAboveCabinet(Handle);
        GD.Print($"Sign: at {_window.Position.X},{_window.Position.Y}, {_window.Size.X}x{_window.Size.Y}, {(_onLeft ? "beyond the stool" : "beside the cabinet")}");
        Animate(1, 0.35);
    }

    /// <summary>Folds it away, if it stands.</summary>
    public void Fold()
    {
        if (!_up)
            return;
        _up = false;
        GD.Print("Sign: folds away");
        Animate(0, 0.3);
    }

    private void Animate(float to, double seconds)
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(v => { _shown = v; _board.QueueRedraw(); }), _shown, to, seconds)
              .SetTrans(Tween.TransitionType.Cubic).SetEase(to > 0 ? Tween.EaseType.Out : Tween.EaseType.In);
        if (to == 0)
            _tween.TweenCallback(Callable.From(() =>
            {
                _window.Visible = false;
                Standing = null;
                _tickedAt.Clear();
            }));
    }

    /// <summary>Put away by the player: it goes, and the cabinet keeps it away (see <see cref="PutAway"/>).</summary>
    private void Away(string how)
    {
        if (!_up)
            return;
        GD.Print($"Sign: put away, {how}");
        PutAway?.Invoke(how);
        Fold();
    }

    private nint Handle => (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, _window.GetWindowId());

    public override void _Process(double delta)
    {
        if (!_window.Visible)
            return;
        Place();
        if (Paper.OffScreen)
            return;                                         // a test run never follows the player's pointer
        var through = !_pressed && !Solid(DisplayServer.MouseGetPosition());
        if (through == _clickThrough)
            return;
        _clickThrough = through;
        DesktopWindow.ClickThrough(Handle, through);
    }

    /// <summary>
    /// Stands it by the cabinet as it is now: on the desk under the cabinet's front, right of the cabinet where it fits
    /// on the screen, else left of the stool; slid as far as the player put it, and no further than the screen goes.
    /// </summary>
    private void Place()
    {
        var cabinet = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        _unit = cabinet.Size.X / _canvas.X;
        var scale = Mathf.Max(_unit, LeastScale);
        var size = (Vector2I)(Picture * scale).Ceil();
        if (!Mathf.IsEqualApprox(_window.ContentScaleFactor, scale))
            _window.ContentScaleFactor = scale;
        if (_window.Size != size)
            _window.Size = size;
        var right = cabinet.Position.X + PastCabinet * _unit;
        var left = cabinet.Position.X + PastStool * _unit - size.X;
        var x = right;
        _onLeft = false;
        _farthest = float.MaxValue;
        if (!Paper.OffScreen)                               // a test run: off every screen, with the cabinet
        {
            var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            _onLeft = right + size.X > screen.End.X && left >= screen.Position.X;
            _farthest = Math.Max(0, _onLeft ? (left - screen.Position.X) / _unit : (screen.End.X - size.X - right) / _unit);
            x = _onLeft ? left : Math.Min(right, screen.End.X - size.X);
        }
        var slide = Math.Clamp(_dragSlide ?? _slide(), Nearest, Math.Max(Nearest, _farthest)) * _unit;
        x += _onLeft ? -slide : slide;
        var at = new Vector2I(Mathf.RoundToInt(x), Mathf.RoundToInt(cabinet.Position.Y + Desk * _unit - Feet * scale - _lift));
        if (_window.Position != at)
            _window.Position = at;
        Standing = new Rect2I(at, size);
    }

    /// <summary>A screen point is on the sign's picture (not the clear space around its legs).</summary>
    private bool Solid(Vector2I screenPoint)
    {
        var local = (Vector2)(screenPoint - _window.Position) / _window.ContentScaleFactor;
        var px = local * (_solid.GetSize().X / Picture.X);
        var size = _solid.GetSize();
        return px.X >= 0 && px.Y >= 0 && px.X < size.X && px.Y < size.Y && _solid.GetBit((int)px.X, (int)px.Y);
    }

    // ---- By hand -----------------------------------------------------------------------------------------------------

    private void Input(InputEvent e, Vector2 local)
    {
        if (!_up)
            return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _pressed = true;
                _dragging = false;
                _pressAt = DisplayServer.MouseGetPosition();
                _slideAtPress = _slide();
                break;
            case InputEventMouseMotion when _pressed:
                var moved = DisplayServer.MouseGetPosition() - _pressAt;
                if (!_dragging && moved.LengthSquared() > 16)
                    _dragging = true;
                if (_dragging)
                    Drag(moved);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _pressed:
                _pressed = false;
                if (!_dragging)
                    Click(local);
                else
                    LetGo();
                break;
            // on the button coming up, as Windows' own context menus open
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } when !_pressed:
                ShowMenu();
                break;
        }
    }

    /// <summary>Along the bottom it slides nearer the cabinet or further off; up, it comes off the desk.</summary>
    private void Drag(Vector2I moved)
    {
        _dragSlide = Math.Clamp(_slideAtPress + moved.X / _unit * (_onLeft ? -1 : 1), Nearest, Math.Max(Nearest, _farthest));
        _lift = Math.Max(0, -moved.Y);
        Place();
    }

    /// <summary>Let go: lifted far enough it's put away; otherwise it's set down where it was slid, and that's kept.</summary>
    private void LetGo()
    {
        if (_lift > LiftAway * _window.Size.Y)
        {
            Away("lifted off the desk");
            return;
        }
        if (_dragSlide is { } slide)
        {
            _slid(slide);
            GD.Print($"Sign: slid to {slide:0} along the desk");
        }
        _dragSlide = null;
        var settle = CreateTween();
        settle.TweenMethod(Callable.From<float>(v => { _lift = v; Place(); }), _lift, 0f, 0.15).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    private void Click(Vector2 local)
    {
        if (_board.LineAt(local) is { Opens: { } opens } line)
        {
            GD.Print($"Sign: {line.Text} clicked");
            Opened?.Invoke(opens);
        }
    }

    /// <summary>
    /// Its menu at the pointer, as large in inches on any screen as the cabinet's own; a test run logs it instead, as
    /// it would take focus.
    /// </summary>
    private void ShowMenu()
    {
        var at = DisplayServer.MouseGetPosition();
        if (Paper.OffScreen)
        {
            GD.Print($"Sign: its menu at {at.X},{at.Y}: Put away");
            return;
        }
        var dpi = DisplayServer.ScreenGetDpi(DisplayServer.GetScreenFromRect(new Rect2(at, Vector2.One)));
        _sizer.Scale = Vector2.One * (dpi / 96f / 1.25f / _window.ContentScaleFactor);
        _menu.Popup(new Rect2I(at, Vector2I.Zero));
    }

    // ---- Dev ---------------------------------------------------------------------------------------------------------

    /// <summary>Dev: what is chalked, and where it stands, for the log.</summary>
    public void DevLog() =>
        GD.Print(_up ? $"Sign: stands at {_window.Position.X},{_window.Position.Y}, {_window.Size.X}x{_window.Size.Y}, {(_onLeft ? "beyond the stool" : "beside the cabinet")}, " +
                       $"slid {_slide():0}: {string.Join(" | ", _lines.Select(l => (l.Ticked ? "[x] " : "") + l.Text + (l.Note.Length > 0 ? " " + l.Note : "")))}"
                     : "Sign: away");

    /// <summary>Dev: a click on the line that opens this (a mission's id).</summary>
    public void DevClick(string opens)
    {
        if (_lines.FirstOrDefault(l => l.Opens == opens) is { } line)
        {
            GD.Print($"Sign: {line.Text} clicked");
            Opened?.Invoke(opens);
        }
        else
            GD.Print($"Sign: no line opens {opens}");
    }

    /// <summary>Dev: a right click, then Put away.</summary>
    public void DevMenu()
    {
        ShowMenu();
        Away("from its menu");
    }

    /// <summary>Dev: dragged by hand from its middle, this far across and up (screen pixels, up is less), in steps, then let go.</summary>
    public async void DevDrag(Vector2I by)
    {
        if (!_up)
            return;
        _pressed = _dragging = true;
        _slideAtPress = _slide();
        for (var step = 1; step <= 12; step++)
        {
            Drag((Vector2I)((Vector2)by * (step / 12f)).Round());
            await ToSignal(GetTree().CreateTimer(0.04), SceneTreeTimer.SignalName.Timeout);
        }
        _pressed = false;
        LetGo();
    }

    /// <summary>Dev: chalks test lines in Chinese or Japanese, to see they fit.</summary>
    public void DevWords(string language)
    {
        string[] words = language == "ja"
            ? ["AIをつなぐ", "あいさつする", "クレジットを稼ぐ", "リマインダーを頼む", "メモリーを開く", "声を聞く"]
            : ["连接一个 AI", "打个招呼", "赚一个币", "请他们提醒你一件事", "打开记忆", "听听他们的声音"];
        Stand(language == "ja" ? "ミッション" : "任务", language == "ja" ? "分" : "分钟",
              [.. words.Select((w, i) => new Line(w, i == 1 ? "+15" : "15", i == 1, null))]);
    }

    // ---- The board ---------------------------------------------------------------------------------------------------

    /// <summary>The sign's picture and the chalk on it, drawn in the picture's units (the window scales them).</summary>
    private partial class Board(ChalkboardSign sign, Texture2D picture) : Control
    {
        private static readonly Font Chalk = new FontVariation
        {
            BaseFont = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf"),
            VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 560 },
        };
        private static readonly Color White = new(0.95f, 0.96f, 0.93f, 0.92f), Mint = new("bff5dd"), Faint = new(0.95f, 0.96f, 0.93f, 0.5f);
        private const int HeadingSize = 15, LineSize = 13, LeastSize = 9;
        private const float Pad = 9, TickColumn = 13;
        private int _hovered = -1;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Stop;

        private static Rect2 Area => Slate.Grow(-Pad);

        /// <summary>Where each line sits: its row's top, and its height.</summary>
        private (float Top, float Height) Row(int i)
        {
            var first = Area.Position.Y + 27;
            var height = Mathf.Min(20, (Area.End.Y - first) / Math.Max(1, sign._lines.Count));
            return (first + i * height, height);
        }

        public Line? LineAt(Vector2 local)
        {
            for (var i = 0; i < sign._lines.Count; i++)
            {
                var (top, height) = Row(i);
                if (local.Y >= top && local.Y < top + height && local.X >= Area.Position.X && local.X <= Area.End.X)
                    return sign._lines[i];
            }
            return null;
        }

        public override void _GuiInput(InputEvent e)
        {
            sign.Input(e, e is InputEventMouse m ? m.Position : Vector2.Zero);
            if (e is InputEventMouseMotion motion && !sign._pressed)
            {
                var line = LineAt(motion.Position);
                var at = line is null ? -1 : sign._lines.ToList().IndexOf(line);
                MouseDefaultCursorShape = line?.Opens is not null ? CursorShape.PointingHand : CursorShape.Arrow;
                if (at != _hovered)
                {
                    _hovered = at;
                    QueueRedraw();
                }
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationMouseExit && _hovered >= 0)
            {
                _hovered = -1;
                QueueRedraw();
            }
        }

        public override void _Process(double delta)
        {
            if (sign._tickedAt.Count > 0 && Time.GetTicksMsec() / 1000.0 - sign._tickedAt.Values.Max() < 1)
                QueueRedraw();                              // a tick being chalked
        }

        public override void _Draw()
        {
            // standing up and folding away: it opens up from its feet, and fades
            DrawSetTransform(new Vector2(0, Feet * (1 - sign._shown)), 0, new Vector2(1, Mathf.Max(sign._shown, 0.001f)));
            var alpha = Mathf.Clamp(sign._shown * 1.4f, 0, 1);
            DrawTextureRect(picture, new Rect2(Vector2.Zero, Picture), false, new Color(1, 1, 1, alpha));
            var text = Area;
            var baseline = text.Position.Y + 15;
            Chalked(sign._heading, new Vector2(text.Position.X, baseline), HeadingSize, White, alpha);
            if (sign._headingNote.Length > 0)
                Chalked(sign._headingNote, new Vector2(text.End.X - Width(sign._headingNote, HeadingSize - 3), baseline), HeadingSize - 3, Faint, alpha);
            // a wavy chalk rule under the heading
            var rule = Enumerable.Range(0, 17).Select(i => new Vector2(text.Position.X + i * text.Size.X / 16, baseline + 6 + Mathf.Sin(i * 1.7f) * 0.7f)).ToArray();
            DrawPolyline(rule, White with { A = 0.5f * alpha }, 1.2f, true);

            var now = Time.GetTicksMsec() / 1000.0;
            for (var i = 0; i < sign._lines.Count; i++)
            {
                var line = sign._lines[i];
                var (top, height) = Row(i);
                var y = top + height * 0.72f;
                var colour = line.Ticked ? Faint : White;
                if (i == _hovered && line.Opens is not null)
                    colour = colour with { A = 1 };
                var drawn = line.Ticked ? Mathf.Clamp((float)(now - sign._tickedAt.GetValueOrDefault(line.Text, now - 1)) / 0.45f, 0, 1) : 0;
                if (line.Ticked)
                    Tick(new Vector2(text.Position.X + 1, y - 9), drawn, alpha);
                else
                    DrawCircle(new Vector2(text.Position.X + 4, y - 4), 1.4f, White with { A = 0.7f * alpha });
                var note = line.Ticked && drawn < 1 ? "" : line.Note;     // the minutes follow the tick
                var noteSize = LineSize - 1;
                var noteWidth = note.Length > 0 ? Width(note, noteSize) : 0;
                var room = text.Size.X - TickColumn - (noteWidth > 0 ? noteWidth + 6 : 0);
                var size = Math.Max(LeastSize, Math.Min(LineSize, (int)(LineSize * room / Math.Max(1, Width(line.Text, LineSize)))));
                Chalked(line.Text, new Vector2(text.Position.X + TickColumn, y), size, colour, alpha);
                if (i == _hovered && line.Opens is not null)
                    DrawLine(new Vector2(text.Position.X + TickColumn, y + 2.5f), new Vector2(text.Position.X + TickColumn + Math.Min(room, Width(line.Text, size)), y + 2.5f),
                             White with { A = 0.6f * alpha }, 1);
                if (note.Length > 0)
                    Chalked(note, new Vector2(text.End.X - noteWidth, y), noteSize, line.Ticked ? Mint : White, alpha);
            }
        }

        private static float Width(string s, int size) => Chalk.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

        /// <summary>Words in chalk: a dusty second pass a hair off the first.</summary>
        private void Chalked(string s, Vector2 at, int size, Color colour, float alpha)
        {
            DrawString(Chalk, at + new Vector2(0.45f, 0.35f), s, HorizontalAlignment.Left, -1, size, colour with { A = colour.A * 0.35f * alpha });
            DrawString(Chalk, at, s, HorizontalAlignment.Left, -1, size, colour with { A = colour.A * alpha });
        }

        /// <summary>A chalk tick, drawn as far as <paramref name="drawn"/> (0 to 1): down the short stroke, then up the long one.</summary>
        private void Tick(Vector2 at, float drawn, float alpha)
        {
            Vector2 a = at + new Vector2(0, 5), b = at + new Vector2(3.5f, 9), c = at + new Vector2(10, 0);
            var colour = Mint with { A = alpha };
            var first = Mathf.Min(drawn / 0.35f, 1);
            DrawLine(a, a.Lerp(b, first), colour, 1.8f, true);
            if (drawn > 0.35f)
                DrawLine(b, b.Lerp(c, (drawn - 0.35f) / 0.65f), colour, 1.8f, true);
        }
    }
}
