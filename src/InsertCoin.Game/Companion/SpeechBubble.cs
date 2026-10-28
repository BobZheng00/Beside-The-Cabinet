using System.Text.RegularExpressions;
using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// What the companion says: a paper bubble over her head, drawn in the cabinet's ink. A canned line types itself
/// out; a reply from her mind starts as thinking dots and grows as its words arrive. It never grows past a few lines:
/// longer text is shown a page at a time, like subtitles, each page turning once her voice has said it (or, when she
/// is not speaking, once there has been time to read it). It fades when the last page has been read and she has
/// finished speaking. The whole reply is in the chat history.
/// It is drawn in a window of its own, a pixel per canvas unit, so it is the size it is by a large cabinet whatever the
/// cabinet's size (#172: by a small one it was too small to read), and it may reach past the cabinet's window: it sits
/// over her head, its right edge short of the marquee, so a full page leaves the marquee readable.
/// </summary>
public partial class SpeechBubble : Node2D
{
    private static readonly Font Face = Paper.Font(560);
    private const int FontSize = 17, PageFontSize = 12, MaxLines = 6;
    private const float MaxTextWidth = 190, WidestText = 330, PadX = 12, PadY = 8, Corner = 12, TailHeight = 13, TailHalf = 7, Stroke = 2;
    private const float PageMark = 14;                  // room under the text for "2/3"
    private const float Clearance = 8;                  // between the bubble and the marquee
    private const float Overshoot = 0.05f;              // how far past full size the pop goes, for room in the window
    private const double LeastOnPage = 1.2, AfterSpeech = 1.2;
    private const string Dots = "...";

    private Label _label = null!, _ruler = null!;          // the text shown; an unseen twin that measures, so measuring never changes what is shown
    private Vector2[] _shape = [];
    private Rect2 _box;
    private Tween? _tween;
    private Vector2 _tip, _canvas;                      // on the cabinet's canvas
    private Vector2I _offset, _cabinetSize;             // where its window is from the cabinet's, and the cabinet's size then
    private bool? _clickThrough;
    private string _text = "";
    private readonly List<int> _starts = [0];           // where each page starts in the text
    private int _page;
    private double _onPage, _quiet, _thinking;
    private bool _streaming, _fading, _reply;               // _reply: showing a reply (Begin), not a line (Say)
    private bool _waiting;
    private HBoxContainer _answers = null!;                 // "Yes?" and "Not now", while she waits on her call

    /// <summary>How far her voice is through what it is saying (0 to 1), or null when she is not speaking.</summary>
    public Func<double?> Spoken { get; set; } = () => null;

    /// <summary>Whether what it shows is a subtitle (her voice speaks another language): read to be understood, so each page waits for its reader.</summary>
    public Func<bool> Subtitled { get; set; } = () => false;

    /// <summary>
    /// She waits for the player to answer her call: the bubble keeps it, with two answers at its foot to click, "Yes?"
    /// and "Not now", as saying them would. A small blinking triangle there (a visual novel's "more to come") was too
    /// quiet: the user saw nothing saying it waited for them, nor that it could be clicked (2026-10-10).
    /// </summary>
    public bool Waiting
    {
        get => _waiting;
        set
        {
            if (_waiting == value)
                return;
            _waiting = value;
            if (Visible && _text.Length > 0)
                Place(Page(_page), single: _starts.Count == 1);     // room for the answers, or none
            if (!value)
                return;
            _answers.Modulate = new Color(1, 1, 1, 0);
            CreateTween().TweenProperty(_answers, "modulate:a", 1f, 0.2);
        }
    }

    /// <summary>One of the answers on the bubble was clicked while she waits on her call: true for "Yes?", false for "Not now".</summary>
    public Action<bool> Answered { get; set; } = _ => { };

    /// <summary>The bubble was clicked, not on an answer, while she waits on her call: the player's "yes?".</summary>
    public Action Clicked { get; set; } = () => { };

    /// <summary>The marquee, on the cabinet's canvas: a full page keeps clear of it.</summary>
    public Rect2 Marquee { get; set; }

    /// <summary>
    /// Something on the cabinet drawn over everything there (the lifted screen, a memo's card) is over this canvas
    /// rectangle: the bubble gives way to it until it is gone, as it was under them when drawn in the cabinet's window.
    /// </summary>
    public Func<Rect2, bool> Under { get; set; } = _ => false;

    /// <summary>Godot's movie writer is recording the cabinet's window (clip.ps1), which leaves this window out: it is mirrored into the cabinet's.</summary>
    public static readonly bool Recording = Engine.GetWriteMoviePath() != "";

    /// <summary>The bubble as it shows on the cabinet's canvas, or null while it doesn't (given way, or not showing).</summary>
    public Rect2? OnCanvas => GetWindow().Visible ? new Rect2(((Vector2)GetWindow().Position - DisplayServer.WindowGetPosition()) / CabinetScale, (Vector2)GetWindow().Size / CabinetScale) : null;

    /// <summary>Its window's handle in Windows, or 0 while it isn't showing.</summary>
    public nint Handle => GetWindow().Visible ? (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, GetWindow().GetWindowId()) : 0;

    /// <summary>Whether a screen point is on the bubble while it takes clicks (it waits on her call); otherwise clicks pass through it.</summary>
    public bool Covers(Vector2I screenPoint) => TakesClicks && OnScreen.HasPoint(screenPoint);

    private bool TakesClicks => _waiting && Visible && !_fading && GetWindow().Visible;

    private Rect2 OnScreen => new((Vector2)GetWindow().Position + Position + _box.Position, _box.Size);

    /// <summary>Dev: as if one of the answers on the bubble were clicked.</summary>
    public void DevAnswer(bool yes) => Answer(yes);

    /// <summary>Dev: as if the bubble's words were clicked.</summary>
    public void DevClick()
    {
        if (TakesClicks)
            Clicked();
    }

    private void Answer(bool yes)
    {
        if (_waiting)
            Answered(yes);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // the answers take their own clicks; anywhere else on the box is "yes?"
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click && TakesClicks && _box.HasPoint(ToLocal(click.Position)))
            Clicked();
    }

    public override void _Ready()
    {
        _label = new Label
        {
            LabelSettings = new LabelSettings { Font = Face, FontSize = FontSize, FontColor = Paper.Letters },
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(_label);
        _ruler = new Label { LabelSettings = _label.LabelSettings, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        AddChild(_ruler);
        _answers = new HBoxContainer { Theme = Paper.WindowTheme(), Visible = false };
        _answers.AddThemeConstantOverride("separation", 6);
        _answers.AddChild(Choice("Yes?", "Small", yes: true));
        _answers.AddChild(Choice("Not now", "QuietSmall", yes: false));
        AddChild(_answers);
        Visible = false;

        Button Choice(string text, string look, bool yes)
        {
            var button = new Button
            {
                Text = text, ThemeTypeVariation = look, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            button.Pressed += () => Answer(yes);
            return button;
        }
    }

    /// <param name="tip">Canvas point the tail points at (just above her head).</param>
    /// <param name="canvas">The cabinet's canvas size, for how large its window is drawn.</param>
    public void Say(string text, Vector2 tip, Vector2 canvas)
    {
        Start(tip, canvas, streaming: false);
        _reply = false;
        _text = text;
        Paginate();
        Place(Page(0), single: _starts.Count == 1);         // placed before it shows, so its window opens where it belongs
        Pop();
        ShowPage(0, typed: true);
    }

    /// <summary>Opens a bubble with thinking dots; the reply's words follow through <see cref="Append"/>.</summary>
    public void Begin(Vector2 tip, Vector2 canvas)
    {
        Start(tip, canvas, streaming: true);
        _reply = true;
        _thinking = 0;
        Place(Dots, single: true);
        Pop();
    }

    public void Append(string piece)
    {
        if (!_streaming || piece.Length == 0)
            return;
        _text += piece;
        Paginate();
        if (Page(_page) != _label.Text)
            ShowPage(_page, typed: false);                  // the page being written grows (or ends, when the next begins)
        QueueRedraw();                                      // the page count
    }

    /// <summary>The reply is complete: its pages are read (and said) out, then the bubble fades.</summary>
    public void End()
    {
        if (!_streaming)
            return;
        _streaming = false;
        if (_text.Length == 0)
            Fade();
    }

    /// <summary>
    /// How much of the reply showing the player has had in front of them: characters of it as it streamed in, to the
    /// end of the page showing. Null when they have seen all of it, or no reply is showing.
    /// </summary>
    public int? Seen => !Visible || _fading || !_reply ? null
        : _page < _starts.Count - 1 ? _starts[_page + 1]
        : _streaming ? _text.Length : null;

    /// <summary>The reply was cut off <paramref name="at"/> characters in: it ends there, with a dash, then fades as usual.</summary>
    public void Cut(int at)
    {
        if (!_reply || !Visible || _fading)
            return;
        var (heard, _) = InsertCoin.Mind.Conversation.Cut(_text, at);
        _streaming = false;
        if (heard == "—")
        {
            Fade();
            return;
        }
        _text = heard;
        _starts.Clear();
        _starts.Add(0);
        Paginate();
        ShowPage(_starts.Count - 1, typed: false);
    }

    /// <summary>Takes back whatever is showing, at once.</summary>
    public void Clear()
    {
        _streaming = false;
        _tween?.Kill();
        Show(false);
    }

    /// <summary>Shows or hides the bubble, placed already: its window shows at once, unless it gives way (see <see cref="Follow"/>).</summary>
    private void Show(bool on)
    {
        Visible = on;
        if (on)
            Follow();
        else
            GetWindow().Visible = false;
    }

    /// <summary>
    /// Shows the bubble's window where it was placed, keeps it over her head as the cabinet is dragged or resized, gives
    /// way to what is drawn over everything on the cabinet, and lets clicks through it except while it takes them.
    /// </summary>
    private void Follow()
    {
        var window = GetWindow();
        if (DisplayServer.WindowGetSize() != _cabinetSize)
            Place(_text.Length > 0 ? Page(_page) : _label.Text, single: _starts.Count == 1);     // the cabinet's size changed: her head moved
        var at = DisplayServer.WindowGetPosition() + _offset;
        if (window.Position != at)
            window.Position = at;
        var scale = CabinetScale;
        var show = !Under(new Rect2((OnScreen.Position - DisplayServer.WindowGetPosition()) / scale, OnScreen.Size / scale));
        if (window.Visible != show)
        {
            window.Visible = show;
            if (show)
            {
                _clickThrough = null;                       // a window shown again is a new one in Windows' terms
                Desktop.DesktopWindow.JustAboveCabinet(Handle);
            }
        }
        if (Paper.OffScreen || !show)
            return;                                         // a test run never follows the player's pointer
        var through = !(TakesClicks && OnScreen.HasPoint(DisplayServer.MouseGetPosition()));
        if (through == _clickThrough)
            return;
        _clickThrough = through;
        Desktop.DesktopWindow.ClickThrough(Handle, through);
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Follow();
        if (!Visible || _fading)
            return;
        if (_streaming && _text.Length == 0)
        {
            _thinking += delta;
            _label.Text = Dots[..(1 + (int)(_thinking * 3) % 3)];        // . .. ... while she thinks
            return;
        }
        _onPage += delta;
        var spoken = Spoken();
        _quiet = spoken is null ? _quiet + delta : 0;
        var last = _page == _starts.Count - 1;
        // a subtitle is read to be understood: each page waits for its reader, the last one after her voice has finished
        var subtitle = Subtitled();
        var reading = ReadingTime(Page(_page), last);
        if (!last)
        {
            // the next page once this one has been said (and, as a subtitle, read), or (without her voice) read
            var through = (double)_starts[_page + 1] / _text.Length;
            if (_onPage >= LeastOnPage && (spoken is { } heard ? heard >= through - 0.02 && (!subtitle || _onPage >= reading) : _onPage >= reading))
                ShowPage(_page + 1, typed: false);
            return;
        }
        if (!_streaming && spoken is null && _quiet >= (subtitle ? Math.Max(AfterSpeech, reading) : AfterSpeech) && _onPage >= reading)
            Fade();
    }

    public override void _Draw()
    {
        if (_shape.Length == 0)
            return;
        DrawColoredPolygon(_shape, Paper.Sheet);
        DrawPolyline([.. _shape, _shape[0]], Paper.Ink, Stroke, antialiased: true);
        if (_starts.Count > 1)
        {
            var mark = $"{_page + 1}/{_starts.Count}{(_streaming ? "…" : "")}";
            var width = Face.GetStringSize(mark, HorizontalAlignment.Left, -1, PageFontSize).X;
            DrawString(Face, new Vector2(_box.End.X - PadX - width, _box.End.Y - 6), mark, HorizontalAlignment.Left, -1, PageFontSize, Paper.Faint);
        }
    }

    private void Start(Vector2 tip, Vector2 canvas, bool streaming)
    {
        _streaming = streaming;
        _fading = false;
        _waiting = false;
        _tip = tip;
        _canvas = canvas;
        _text = "";
        _starts.Clear();
        _starts.Add(0);
        _page = 0;
        _onPage = _quiet = 0;
    }

    private string Page(int page) => _text[_starts[page]..(page + 1 < _starts.Count ? _starts[page + 1] : _text.Length)].Trim();

    /// <summary>
    /// How long a page takes to read: about 18 letters a second, a Chinese, Japanese or Korean character counting as
    /// three (about six a second); the last page a little longer, to take it in.
    /// </summary>
    private static double ReadingTime(string page, bool last)
    {
        var length = page.Sum(c => c >= '⺀' ? 3 : 1);
        return last ? Math.Min(2.4 + length * 0.055, 18) : 1.0 + length * 0.05;
    }

    private void ShowPage(int page, bool typed)
    {
        var changed = page != _page;
        _page = page;
        if (changed)
            _onPage = 0;
        var text = Page(page);
        Place(text, single: _starts.Count == 1);
        if (!typed)
            return;
        _label.VisibleRatio = 0;
        _tween!.Parallel().TweenProperty(_label, "visible_ratio", 1f, 0.15 + text.Length / 55.0);
    }

    /// <summary>
    /// Splits the text into pages that fit the bubble at its widest, at sentence ends where it can (then at commas,
    /// spaces or, in Chinese and Japanese, between characters). Pages before the last stay as they are, so a reply
    /// streaming in only ever adds to its last page or starts new ones.
    /// </summary>
    private void Paginate()
    {
        var limit = MaxHeight();
        bool Fits(string s) => Measure(s.Trim(), WidestText).Y <= limit;
        if (_starts.Count == 1 && Fits(_text))
            return;                                         // all on one page: it may be narrower (see Place)
        var start = _starts[^1];
        _starts.RemoveAt(_starts.Count - 1);
        while (true)
        {
            _starts.Add(start);
            var rest = _text[start..];
            if (Fits(rest))
                return;
            var end = Fill(rest, Fits);
            if (end <= 0 || end >= rest.Length)
                return;                                     // nothing fits (a single huge word): leave it whole
            start += end;
        }
    }

    /// <summary>The length of the longest start of <paramref name="text"/> that fits, ending at the best break before it.</summary>
    private static int Fill(string text, Func<string, bool> fits)
    {
        foreach (var breaks in new[] { SentenceEnd(), Pause(), Gap() })
        {
            var best = 0;
            foreach (Match m in breaks.Matches(text))
            {
                var end = m.Index + m.Length;
                if (end >= text.Length || !fits(text[..end]))
                    break;
                best = end;
            }
            if (best > 0)
                return best;
        }
        return 0;
    }

    /// <summary>How tall a page may be: a few lines, and no more than there is room for above her head.</summary>
    private float MaxHeight()
    {
        var room = Above(TipOnScreen) - TailHeight - 4 - 2 * PadY - PageMark;
        var lines = MaxLines * _label.GetLineHeight() + (MaxLines - 1) * _label.LabelSettings.LineSpacing;
        return Mathf.Max(Mathf.Min(room, lines), 2 * _label.GetLineHeight());
    }

    /// <summary>The tail's tip on the screen, in pixels.</summary>
    private Vector2 TipOnScreen => DisplayServer.WindowGetPosition() + _tip * CabinetScale;

    /// <summary>The cabinet window's pixels per canvas unit (1 by a large cabinet).</summary>
    private float CabinetScale => DisplayServer.WindowGetSize().X / _canvas.X;

    /// <summary>
    /// Where the bubble may be: the screen the cabinet is on, less the taskbar; in a clip, the cabinet's window (all
    /// the clip records); none in another test run, which is off every screen.
    /// </summary>
    private static Rect2? Screen() =>
        Recording ? new Rect2(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize())
        : Paper.OffScreen ? null
        : DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()) is var r ? new Rect2(r.Position, r.Size) : null;

    /// <summary>How far above her head the bubble may reach: as far as by a large cabinet, and no further than the screen's top.</summary>
    private float Above(Vector2 tip) => Screen() is { } screen ? Mathf.Min(_tip.Y, tip.Y - screen.Position.Y) : _tip.Y;

    /// <summary>
    /// Sizes the bubble around the text, above her head and mostly to the left of it (the cabinet is on her other side),
    /// its right edge short of the marquee; then fits its window around it, on the screen.
    /// </summary>
    private void Place(string text, bool single)
    {
        // A short reply first grows wider, so it stays above her head instead of covering it; pages are at the widest.
        var tip = TipOnScreen;
        var room = Above(tip) - TailHeight - 4 - 2 * PadY;
        var width = single ? MaxTextWidth : WidestText;
        var size = Measure(text, width);
        while (size.Y > room && width < WidestText)
        {
            width = Mathf.Min(width + 40, WidestText);
            size = Measure(text, width);
        }
        // under the words: the answers to her call while she waits on it, else the page count when there are pages
        _answers.Visible = _waiting;
        var answers = _waiting ? _answers.GetCombinedMinimumSize() : Vector2.Zero;
        var box = size + new Vector2(2 * PadX, 2 * PadY + (_waiting ? answers.Y + 6 : single ? 0 : PageMark));
        if (!single)
            box.X = WidestText + 2 * PadX;                   // pages keep one width, so the bubble doesn't jump about
        box.X = Mathf.Max(box.X, 2 * (Corner + TailHalf) + 10);      // room for the tail between the round corners, even for "..."
        box.X = Mathf.Max(box.X, answers.X + 2 * PadX);
        var scale = CabinetScale;
        var marquee = new Rect2(DisplayServer.WindowGetPosition() + Marquee.Position * scale, Marquee.Size * scale);
        var left = Mathf.Min(tip.X - box.X * 0.62f, marquee.Position.X - Clearance - box.X);
        var top = tip.Y - TailHeight - box.Y;
        if (Screen() is { } screen)
        {
            left = Mathf.Clamp(left, screen.Position.X + 6, screen.End.X - box.X - 6);
            if (left + box.X > marquee.Position.X - Clearance)
                top = Mathf.Min(top, marquee.Position.Y - Clearance - box.Y);      // no room beside the marquee on this screen: above it
            top = Mathf.Max(top, screen.Position.Y + 4);
        }
        var rect = _box = new Rect2(new Vector2(left, top) - tip, box);         // local: the node sits on the tail's tip

        // its window: the box and the tail, with room for the pop to overshoot
        var area = rect.Expand(Vector2.Zero).Grow(Overshoot * Mathf.Max(rect.Size.X, rect.Size.Y + TailHeight) + Stroke);
        var window = GetWindow();
        window.Position = (Vector2I)(tip + area.Position).Floor();
        window.Size = (Vector2I)area.Size.Ceil() + Vector2I.One;
        _offset = window.Position - DisplayServer.WindowGetPosition();
        _cabinetSize = DisplayServer.WindowGetSize();
        Position = tip - window.Position;
        _label.Text = text;
        _label.Position = rect.Position + new Vector2(PadX, PadY - 1);
        _label.Size = new Vector2(width, size.Y);
        _answers.Position = rect.Position + new Vector2(PadX, rect.Size.Y - PadY - answers.Y);
        _answers.Size = answers;
        // no room above her head for the tail (the box had to stop at the top of the canvas): a plain box, not a folded tail
        var tail = rect.End.Y <= -TailHeight / 2 ? Mathf.Clamp(-6, rect.Position.X + Corner + TailHalf, rect.End.X - Corner - TailHalf) : (float?)null;
        _shape = Outline(rect, tail);
        QueueRedraw();
    }

    /// <summary>
    /// The text's size when wrapped at <paramref name="width"/>, as a label lays it out: its line count and line
    /// height, plus the spacing it puts between lines (which measuring with the font alone leaves out). Measured on the
    /// unseen twin: measuring on the shown label left it holding the measured text, so a streaming reply looked
    /// unchanged and its bubble stayed the size of the thinking dots while the words spilled out of it.
    /// </summary>
    private Vector2 Measure(string text, float width)
    {
        _ruler.Text = text;
        _ruler.Size = new Vector2(width, 0);
        var lines = _ruler.GetLineCount();
        var height = lines * _ruler.GetLineHeight() + (lines - 1) * _ruler.LabelSettings.LineSpacing;
        // widest line, for a snug bubble around short text (WordSmart: break between words, inside one only if it cannot fit)
        var widest = Face.GetMultilineStringSize(text, HorizontalAlignment.Left, width, FontSize,
            brkFlags: TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive).X;
        return new Vector2(Mathf.Min(widest, width), height);
    }

    private void Pop()
    {
        _tween?.Kill();
        Show(true);
        Scale = Vector2.One * 0.6f;
        Modulate = new Color(1, 1, 1, 0);
        _label.VisibleRatio = 1;
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.24).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);
    }

    private void Fade()
    {
        _fading = true;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(this, "modulate:a", 0f, 0.3);
        _tween.TweenCallback(Callable.From(() => Show(false)));
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

    // where a page may end, best first: after a sentence, after a comma, between words (or CJK characters)
    [GeneratedRegex(@"[。！？!?…]+[」』）)""']*\s*|[.;:]+[""')]*\s+|\n+")]
    private static partial Regex SentenceEnd();
    [GeneratedRegex(@"[，、,；]\s*")]
    private static partial Regex Pause();
    [GeneratedRegex(@"\s+|(?<=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}])")]
    private static partial Regex Gap();
}
