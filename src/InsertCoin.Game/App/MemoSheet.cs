using System.Globalization;
using Godot;
using InsertCoin.Game.Cabinet;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// A memo read and written on the paper itself: picked up off the cabinet it grows into a sheet that flies out beside
/// the cabinet, onto the desktop (no frame, no title bar: its own small transparent window, the paper all there is of
/// it), its colour, sticker and torn edge as on the cabinet. Dragged by the paper it goes anywhere on the screen, and
/// several can be open at once, to read side by side. Read, it scrolls; small drawn tools along its foot copy it, pin
/// it, tear it off or take it down, edit it, or throw it away. Edited, it is written on where it is, with its papers
/// and stickers in a strip. A new one rises blank out of the slot, and printing sends it back in. Its × (or Esc) puts
/// it back where it came from on the cabinet.
/// </summary>
public partial class MemoSheet : Node2D
{
    public const float Width = 348, Height = 452;
    private const float Pad = 18, Tooth = 6, Margin = 6;

    /// <summary>The size of its window, in the sheet's own units: the paper, a margin round it and room for its shadow.</summary>
    public static readonly Vector2 Outer = new(Width + 2 * Margin + 8, Height + 2 * Margin + 10);
    private static readonly Font Mono = new SystemFont { FontNames = ["Cascadia Mono", "Consolas", "Courier New"] };
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");

    /// <summary>What a new memo has so far: a click away keeps it, to carry on with next time.</summary>
    public sealed record Draft(string Title, string Text, string Colour, string? Sticker, bool Tell);

    private Memos _memos = null!;
    private Memo? _memo;                                    // null while a new one is written
    private Window _window = null!;
    private Vector2 _from;                                  // where it came from on the cabinet (a canvas point)
    private Vector2I? _grip;                                // dragged: where the pointer holds it, from its window's corner
    private bool _leaving;
    private float _fromTilt;
    private Control _root = null!;
    private Label _title = null!, _byline = null!, _note = null!;
    private LineEdit _titleField = null!;
    private RichTextLabel _text = null!;
    private TextEdit _textField = null!;
    private HBoxContainer _tools = null!, _palette = null!;
    private CheckBox _tell = null!;
    private Tool _copy = null!, _pin = null!, _off = null!, _edit = null!, _delete = null!, _done = null!, _print = null!, _close = null!;
    private readonly List<(string Colour, Swatch Button)> _swatches = [];
    private readonly List<(string? Sticker, Tool Button)> _stickers = [];
    private string _colour = Memo.Yellow;
    private string? _sticker;
    private bool _editing, _deleting;
    private Tween? _tween;

    /// <summary>Builds it in the window it was put in (see <see cref="Memos"/>), hidden until a memo is read or written on it.</summary>
    public void Build(Memos memos)
    {
        _memos = memos;
        _window = GetWindow();
        _window.CloseRequested += () => Finish(keep: true);
        Visible = false;

        _root = new Control { Size = new Vector2(Width, Height), Theme = Paper.WindowTheme(), MouseFilter = Control.MouseFilterEnum.Stop };
        _root.GuiInput += Drag;
        AddChild(_root);
        _title = new Label
        {
            Position = new Vector2(Pad, 34), Size = new Vector2(Width - 2 * Pad - 40, 56), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 2, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, ClipText = true,
            LabelSettings = new LabelSettings { Font = Paper.Font(650), FontSize = 22, FontColor = Paper.Ink, LineSpacing = -3 },
        };
        _titleField = new LineEdit
        {
            Position = new Vector2(Pad - 4, 36), Size = new Vector2(Width - 2 * Pad - 36, 34), PlaceholderText = "A title", MaxLength = Memo.TitleLength,
            Visible = false,
        };
        _titleField.AddThemeFontOverride("font", Paper.Font(650));
        _titleField.AddThemeFontSizeOverride("font_size", 20);
        _byline = new Label { Position = new Vector2(Pad, 92), Size = new Vector2(Width - 2 * Pad, 18), ThemeTypeVariation = "Hint" };
        _text = new RichTextLabel
        {
            Position = new Vector2(Pad, 116), Size = new Vector2(Width - 2 * Pad, Height - 116 - 74), BbcodeEnabled = true, ScrollActive = true,
            SelectionEnabled = true, ContextMenuEnabled = true, FocusMode = Control.FocusModeEnum.Click,
        };
        _text.AddThemeFontOverride("normal_font", Paper.Font(450));
        _text.AddThemeFontOverride("bold_font", Paper.Font(620));
        _text.AddThemeFontOverride("mono_font", Mono);
        _text.AddThemeFontSizeOverride("normal_font_size", 16);
        _text.AddThemeFontSizeOverride("bold_font_size", 16);
        _text.AddThemeFontSizeOverride("mono_font_size", 14);
        _text.AddThemeColorOverride("default_color", Paper.Letters);
        _text.AddThemeConstantOverride("line_separation", 3);
        _text.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _textField = new TextEdit
        {
            Position = new Vector2(Pad - 4, 128), Size = new Vector2(Width - 2 * Pad + 8, Height - 128 - 78), WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "What to keep: a list, a plan, something to remember…", Visible = false,
        };
        _textField.AddThemeFontOverride("font", Paper.Font(450));
        _textField.AddThemeFontSizeOverride("font_size", 16);
        foreach (var field in new Control[] { _titleField, _textField })
        {
            // written on the paper itself: no box, only a faint rule under it while it has the caret
            field.AddThemeStyleboxOverride("normal", Rule(new Color(0.24f, 0.18f, 0.37f, 0.18f)));
            field.AddThemeStyleboxOverride("focus", Rule(Paper.Accent));
            field.AddThemeColorOverride("background_color", Colors.Transparent);
        }

        // in the strip while editing: its paper and sticker
        _palette = new HBoxContainer { Position = new Vector2(Pad - 2, 92), Size = new Vector2(Width - 2 * Pad, 28), Visible = false };
        _palette.AddThemeConstantOverride("separation", 3);
        foreach (var colour in Memo.Colours)
        {
            var swatch = new Swatch(colour) { CustomMinimumSize = new Vector2(20, 22), TooltipText = colour };
            swatch.Pressed += () => Choose(colour, _sticker);
            _palette.AddChild(swatch);
            _swatches.Add((colour, swatch));
        }
        _palette.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
        foreach (var sticker in Memo.Stickers.Prepend(null))
        {
            var button = new Tool(sticker ?? "none", sticker is null ? "No sticker" : sticker) { CustomMinimumSize = new Vector2(21, 22) };
            button.Pressed += () => Choose(_colour, sticker);
            _palette.AddChild(button);
            _stickers.Add((sticker, button));
        }

        _note = new Label { Position = new Vector2(Pad, Height - 70), Size = new Vector2(Width - 2 * Pad, 18), ThemeTypeVariation = "Hint", ClipText = true };
        _tell = new CheckBox { ButtonPressed = true, Visible = false, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _tell.AddThemeFontSizeOverride("font_size", 14);
        _tools = new HBoxContainer { Position = new Vector2(Pad - 4, Height - 48), Size = new Vector2(Width - 2 * Pad + 8, 34) };
        _tools.AddThemeConstantOverride("separation", 6);
        _tools.AddChild(_tell);                             // writing a new one: whether she reads it, where the tools for reading go
        _copy = Add("copy", "Copy it", () =>
        {
            DisplayServer.ClipboardSet(_memo?.Text ?? "");
            Say("Copied.");
        });
        _edit = Add("edit", "Write on it", () => Editing(true));
        _delete = Add("delete", "Throw it away", Delete);
        _tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _off = Add("off", "Take it down", () =>
        {
            if (_memo is not { } memo)
                return;
            _memos.Archive(memo.Id);
            PutAway(_from + new Vector2(0, 160));
        });
        _pin = Add("pin", "Pin it on the cabinet's side", () =>
        {
            if (_memo is not { } memo)
                return;
            if (_memos.Pin(memo.Id))
                PutAway(_memos.Where(memo.Id) ?? _from);
            else
                Say($"The side is full ({Memo.PinnedAtMost}): take one down first.");
        });
        _done = Add("done", "Done", () =>
        {
            Keep();
            if (_memo is { } memo)
                Show(memo, _memos.From(memo));                      // read again, as it now is
        });
        _print = Add("print", "Print it", Print);
        _close = new Tool("close", "Put it back") { Position = new Vector2(Width - 32, 4), Size = new Vector2(28, 28) };
        _close.Pressed += () => Finish(keep: true);
        _root.AddChild(_close);
        _root.AddChild(_title);
        _root.AddChild(_titleField);
        _root.AddChild(_byline);
        _root.AddChild(_palette);
        _root.AddChild(_text);
        _root.AddChild(_textField);
        _root.AddChild(_note);
        _root.AddChild(_tools);

        Tool Add(string icon, string tip, Action pressed)
        {
            var tool = new Tool(icon, tip) { CustomMinimumSize = new Vector2(30, 30) };
            tool.Pressed += pressed;
            _tools.AddChild(tool);
            return tool;
        }
    }

    /// <summary>The memo read on it (null while a new one is written).</summary>
    public long? MemoId => _memo?.Id;

    /// <summary>A new memo is being written on it.</summary>
    public bool WritingNew => _memo is null && !_leaving;

    /// <summary>It is going back to the cabinet.</summary>
    public bool Leaving => _leaving;

    /// <summary>The screen point is on its window.</summary>
    public bool Covers(Vector2I screenPoint) => new Rect2I(_window.Position, _window.Size).HasPoint(screenPoint);

    /// <summary>Opened again while open: it comes to the front.</summary>
    public void Raise()
    {
        if (Paper.OffScreen)
            return;                                         // a test run never takes focus

        _window.GrabFocus();
    }

    /// <summary>Opens a memo to read, grown out of where it is on the cabinet (<paramref name="from"/>, its tilt).</summary>
    public void Read(Memo memo, string from, Vector2 at, float tilt)
    {
        _from = at;
        Show(memo, from);
        Lift(at, tilt);
    }

    /// <summary>What it says, to read.</summary>
    private void Show(Memo memo, string from)
    {
        _memo = memo;
        _colour = memo.Colour;
        _sticker = memo.Sticker;
        _window.Title = $"{memo.Title} - Beside the Cabinet memo";
        _title.Text = memo.Title;
        _byline.Text = $"{from} · {memo.At.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture)}";
        _text.Text = Memo.ToBbcode(memo.Text);
        _text.ScrollToLine(0);
        Editing(false);
    }

    /// <summary>A new memo of the player's own: a blank sheet (or the draft left last time) rising out of the slot.</summary>
    public void Write(Draft? draft, Vector2 slot)
    {
        _memo = null;
        _window.Title = "New memo - Beside the Cabinet memo";
        _colour = draft?.Colour ?? Memo.Yellow;
        _sticker = draft?.Sticker;
        _titleField.Text = draft?.Title ?? "";
        _textField.Text = draft?.Text ?? "";
        _tell.ButtonPressed = draft?.Tell ?? true;
        Editing(true);
        Lift(slot, 0);
        Focus(_titleField.Text.Length == 0 ? _titleField : _textField);
    }

    /// <summary>Its ×: written on, it keeps what was written (<paramref name="keep"/>); then it goes back.</summary>
    public void Finish(bool keep)
    {
        if (_leaving)
            return;
        if (_editing && keep)
        {
            if (_memo is not null)
                Keep();
            else
                _memos.KeepDraft(_textField.Text.Trim().Length > 0 || _titleField.Text.Trim().Length > 0
                                     ? new Draft(_titleField.Text, _textField.Text, _colour, _sticker, _tell.ButtonPressed) : null);
        }
        PutAway(_memo is { } shown ? _memos.Where(shown.Id) ?? _from : _from);
    }

    /// <summary>What was written on it is kept (a memo with nothing left on it keeps what it said).</summary>
    private void Keep()
    {
        if (_memo is not { } memo || _textField.Text.Trim().Length == 0)
            return;
        _memos.Save(memo.Id, TitleOf(), _textField.Text.Trim());
        _memo = memo with { Title = TitleOf(), Text = _textField.Text.Trim() };
    }

    private void Print()
    {
        var text = _textField.Text.Trim();
        if (text.Length == 0)
        {
            Say("Write something first.");
            return;
        }
        var title = TitleOf();
        var (colour, sticker, tell) = (_colour, _sticker, _tell.Visible && _tell.ButtonPressed);
        _memos.KeepDraft(null);
        // back into the slot it goes, and out it prints
        PutAway(_from, () => _memos.Print(title, text, by: null, colour: colour, sticker: sticker, tell: tell));
    }

    private string TitleOf()
    {
        var title = _titleField.Text.Trim();
        if (title.Length > 0)
            return title;
        var first = _textField.Text.Trim().Split('\n')[0];
        return first.Length > 40 ? first[..40].TrimEnd() + "…" : first;
    }

    /// <summary>Throwing it away asks once more: a second press within a few seconds.</summary>
    private void Delete()
    {
        if (_memo is not { } memo)
            return;
        if (!_deleting)
        {
            _deleting = true;
            _delete.Warn = true;
            Say("Press again to throw it away.");
            GetTree().CreateTimer(4).Timeout += () =>
            {
                if (!IsInstanceValid(this))
                    return;
                _deleting = false;
                _delete.Warn = false;
            };
            return;
        }
        PutAway(_from + new Vector2(0, 200));
        _memos.Delete(memo.Id);
    }

    private void Editing(bool editing)
    {
        _editing = editing;
        if (editing && _memo is { } memo)
        {
            _titleField.Text = memo.Title;
            _textField.Text = memo.Text;
            Focus(_textField);
        }
        _title.Visible = _text.Visible = _byline.Visible = !editing;
        _titleField.Visible = _textField.Visible = _palette.Visible = editing;
        _copy.Visible = _edit.Visible = _delete.Visible = !editing;
        _pin.Visible = !editing && _memo is { Place: not MemoPlace.Pinned };
        _off.Visible = !editing && _memo is { Place: not MemoPlace.Archived };
        _off.Tip = _memo is { Place: MemoPlace.Hanging } ? "Tear it off (it stays in the memory book)" : "Take it down (it stays in the memory book)";
        _done.Visible = editing && _memo is not null;
        _print.Visible = editing && _memo is null;
        _tell.Visible = editing && _memo is null && _memos.HerName is not null;
        _tell.Text = $"Let {_memos.HerName} read it";
        _tell.TooltipText = Words.Wrap("Your companion reads it and may act on it (a reminder, a date on the agenda), or let it be. Untick to keep it to yourself.");
        _deleting = false;
        _delete.Warn = false;
        Say(editing && _memo is null ? "Print it, and it comes out of the cabinet's slot." : "");
        ShowLook();
    }

    /// <summary>A paper or sticker chosen: a memo being read or edited changes at once; a new one takes it when printed.</summary>
    private void Choose(string colour, string? sticker)
    {
        _colour = colour;
        _sticker = sticker;
        if (_memo is { } memo)
        {
            _memos.Style(memo.Id, colour, sticker);
            _memo = memo with { Colour = colour, Sticker = sticker };
        }
        ShowLook();
    }

    private void ShowLook()
    {
        foreach (var (colour, swatch) in _swatches)
            swatch.Chosen = colour == _colour;
        foreach (var (sticker, button) in _stickers)
            button.Chosen = sticker == _sticker;
        QueueRedraw();
    }

    private void Say(string text) => _note.Text = text;

    private void Focus(Control field)
    {
        if (Paper.OffScreen)
            return;                                         // a test run never takes focus
        _window.GrabFocus();
        field.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>
    /// Esc: written on, a memo goes back to being read as it was (what was written is dropped); otherwise it is put
    /// back, a new one's writing kept for next time.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || _leaving || !Visible)
            return;
        GetViewport().SetInputAsHandled();
        if (_editing && _memo is { } memo)
            Show(memo, _memos.From(memo));
        else
            Finish(keep: true);
    }

    /// <summary>Held by the paper (anywhere a tool or its text doesn't take the pointer), it is dragged across the desktop.</summary>
    private void Drag(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _grip = button.Pressed && !_leaving ? DisplayServer.MouseGetPosition() - _window.Position : null;
                break;
            case InputEventMouseMotion when _grip is { } grip:
                _window.Position = DisplayServer.MouseGetPosition() - grip;
                break;
        }
    }

    // ---- Coming and going --------------------------------------------------------------------------------------------

    /// <summary>
    /// It comes up off the cabinet: from small and tilted where the memo was (<paramref name="at"/>, a canvas point),
    /// its window flying out to rest beside the cabinet as the paper grows to full size.
    /// </summary>
    private void Lift(Vector2 at, float tilt)
    {
        _from = at;
        _fromTilt = tilt;
        var from = (Vector2)(_memos.OnScreen(at) - _window.Size / 2);
        var home = (Vector2)_memos.Home(this, _window.Size);
        _window.Position = (Vector2I)from;
        Pose(0);
        Visible = true;
        _window.Show();
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(t =>
        {
            _window.Position = (Vector2I)from.Lerp(home, t).Round();
            Pose(t);
        }), 0f, 1f, 0.38).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>It flies back to the cabinet (or into the slot), shrinking to where it belongs, and its window closes.</summary>
    private void PutAway(Vector2 to, Action? then = null)
    {
        _leaving = true;
        _grip = null;
        var from = (Vector2)_window.Position;
        var target = (Vector2)(_memos.OnScreen(to) - _window.Size / 2);
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(t =>
        {
            _window.Position = (Vector2I)target.Lerp(from, t).Round();
            Pose(t);
        }), 1f, 0f, 0.3).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() =>
        {
            then?.Invoke();
            _window.QueueFree();
        }));
    }

    /// <summary>How far out it is (0: small and tilted where it came from, 1: full size): its size, tilt and how clearly it shows.</summary>
    private void Pose(float t)
    {
        var scale = Mathf.Lerp(0.14f, 1f, t);
        Rotation = Mathf.DegToRad(Mathf.Lerp(_fromTilt, 0, Mathf.Clamp(t, 0, 1)));
        Scale = Vector2.One * scale;
        Position = Outer / 2 - (new Vector2(Width, Height) / 2 * scale).Rotated(Rotation);
        Modulate = new Color(1, 1, 1, Mathf.Clamp(0.4f + t * 1.5f, 0, 1));
    }

    // ---- The paper ----------------------------------------------------------------------------------------------------

    public override void _Draw()
    {
        var shape = new List<Vector2> { new(0, 0), new(Width, 0), new(Width, Height - Tooth) };
        var teeth = (int)(Width / 10);
        for (var i = teeth; i >= 1; i--)
        {
            shape.Add(new Vector2(Width * (i - 0.5f) / teeth, Height));
            shape.Add(new Vector2(Width * (i - 1f) / teeth, Height - Tooth));
        }
        DrawColoredPolygon([.. shape.Select(p => p + new Vector2(6, 8))], new Color(0.12f, 0.08f, 0.2f, 0.22f));        // its shadow on what's below
        DrawColoredPolygon([.. shape], MemoLook.Paper(_colour));
        DrawPolyline([.. shape, shape[0]], MemoLook.Ink, 2.6f, true);
        DrawString(Pixel, new Vector2(Pad, 24), "MEMO", HorizontalAlignment.Left, -1, 11, new Color("c2185b"));
        if (_sticker is { } sticker)
            MemoLook.Sticker(this, sticker, new Vector2(Width - 34, 52), 34);           // under its ×
    }

    private static StyleBoxFlat Rule(Color colour) => new()
    {
        BgColor = Colors.Transparent, BorderColor = colour, BorderWidthBottom = 2,
        ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 4,
    };

    // ---- Its little tools --------------------------------------------------------------------------------------------

    /// <summary>A small drawn tool on the paper (or a sticker to choose): ink, pink under the pointer.</summary>
    private partial class Tool : Button
    {
        private readonly string _icon;
        private bool _chosen, _warn, _over;

        public Tool(string icon, string tip)
        {
            _icon = icon;
            TooltipText = tip;
            FocusMode = FocusModeEnum.None;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            Flat = true;
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" })
                AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            MouseEntered += () => { _over = true; QueueRedraw(); };
            MouseExited += () => { _over = false; QueueRedraw(); };
        }

        public string Tip
        {
            set => TooltipText = value;
        }

        public bool Chosen
        {
            set { _chosen = value; QueueRedraw(); }
        }

        public bool Warn
        {
            set { _warn = value; QueueRedraw(); }
        }

        public override void _Draw()
        {
            var c = Size / 2;
            var r = Mathf.Min(Size.X, Size.Y) / 2;
            if (_over || _chosen)
                DrawCircle(c, r, new Color(1, 1, 1, 0.55f));
            if (_chosen)
                DrawArc(c, r - 1, 0, Mathf.Tau, 28, Paper.Accent, 2, true);
            var ink = _warn ? Paper.Bad : _over ? Paper.Accent : MemoLook.Ink;
            var s = r * 0.55f;
            const float w = 2f;
            switch (_icon)
            {
                case "copy":
                    DrawRect(new Rect2(c + new Vector2(-s, -s * 0.6f), new Vector2(s * 1.3f, s * 1.6f)), ink, false, w);
                    DrawRect(new Rect2(c + new Vector2(-s * 0.4f, -s * 1.1f), new Vector2(s * 1.3f, s * 1.6f)), ink, false, w);
                    break;
                case "edit":
                    DrawLine(c + new Vector2(-s, s), c + new Vector2(s * 0.8f, -s * 0.8f), ink, w * 2.2f, true);
                    DrawColoredPolygon([c + new Vector2(-s * 1.15f, s * 1.15f), c + new Vector2(-s * 0.95f, s * 0.45f), c + new Vector2(-s * 0.45f, s * 0.95f)], ink);
                    break;
                case "delete":
                    DrawRect(new Rect2(c + new Vector2(-s * 0.75f, -s * 0.55f), new Vector2(s * 1.5f, s * 1.6f)), ink, false, w);
                    DrawLine(c + new Vector2(-s, -s * 0.8f), c + new Vector2(s, -s * 0.8f), ink, w, true);
                    DrawLine(c + new Vector2(-s * 0.3f, -s * 1.1f), c + new Vector2(s * 0.3f, -s * 1.1f), ink, w, true);
                    DrawLine(c + new Vector2(-s * 0.25f, -s * 0.25f), c + new Vector2(-s * 0.25f, s * 0.75f), ink, w * 0.8f, true);
                    DrawLine(c + new Vector2(s * 0.25f, -s * 0.25f), c + new Vector2(s * 0.25f, s * 0.75f), ink, w * 0.8f, true);
                    break;
                case "pin":
                    DrawLine(c + new Vector2(0, -s * 0.1f), c + new Vector2(0, s * 1.2f), ink, w, true);
                    DrawCircle(c + new Vector2(0, -s * 0.55f), s * 0.6f, new Color("FF7AB0"));
                    DrawArc(c + new Vector2(0, -s * 0.55f), s * 0.6f, 0, Mathf.Tau, 20, ink, w, true);
                    break;
                case "off":
                    DrawRect(new Rect2(c + new Vector2(-s, -s * 1.1f), new Vector2(s * 1.2f, s * 1.5f)), ink, false, w);
                    DrawLine(c + new Vector2(s * 0.75f, -s * 0.4f), c + new Vector2(s * 0.75f, s * 1.1f), ink, w, true);
                    DrawPolyline([c + new Vector2(s * 0.35f, s * 0.7f), c + new Vector2(s * 0.75f, s * 1.15f), c + new Vector2(s * 1.15f, s * 0.7f)], ink, w, true);
                    break;
                case "done":
                    DrawPolyline([c + new Vector2(-s, 0), c + new Vector2(-s * 0.25f, s * 0.75f), c + new Vector2(s * 1.05f, -s * 0.8f)], ink, w * 1.4f, true);
                    break;
                case "print":
                    DrawRect(new Rect2(c + new Vector2(-s * 1.1f, -s * 0.2f), new Vector2(s * 2.2f, s * 0.9f)), ink, false, w);
                    DrawRect(new Rect2(c + new Vector2(-s * 0.6f, -s * 1.1f), new Vector2(s * 1.2f, s * 0.9f)), ink, false, w);
                    DrawRect(new Rect2(c + new Vector2(-s * 0.6f, s * 0.55f), new Vector2(s * 1.2f, s * 0.7f)), ink, false, w);
                    break;
                case "close":
                    DrawLine(c + new Vector2(-s * 0.8f, -s * 0.8f), c + new Vector2(s * 0.8f, s * 0.8f), ink, w * 1.2f, true);
                    DrawLine(c + new Vector2(-s * 0.8f, s * 0.8f), c + new Vector2(s * 0.8f, -s * 0.8f), ink, w * 1.2f, true);
                    break;
                case "none":
                    DrawArc(c, s, 0, Mathf.Tau, 20, Paper.Faint, 1.6f, true);
                    DrawLine(c + new Vector2(-s * 0.7f, s * 0.7f), c + new Vector2(s * 0.7f, -s * 0.7f), Paper.Faint, 1.6f, true);
                    break;
                default:
                    MemoLook.Sticker(this, _icon, c, r * 1.4f);
                    break;
            }
        }
    }

    /// <summary>A paper to choose: a little square of it, ringed when chosen.</summary>
    private partial class Swatch : Button
    {
        private readonly Color _paper;
        private bool _chosen;

        public Swatch(string colour)
        {
            _paper = MemoLook.Paper(colour);
            FocusMode = FocusModeEnum.None;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            Flat = true;
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" })
                AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        public bool Chosen
        {
            set { _chosen = value; QueueRedraw(); }
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.One * 2, Size - Vector2.One * 4);
            DrawRect(box, _paper);
            DrawRect(box, _chosen ? Paper.Accent : MemoLook.Ink with { A = 0.35f }, false, _chosen ? 2.5f : 1.2f);
        }
    }

    // ---- Dev --------------------------------------------------------------------------------------------------------------

    /// <summary>Dev: its tools by name (copy is left out: it would take the clipboard), choices and fields; move:x:y drags it.</summary>
    public void DevPress(string what)
    {
        if (what.StartsWith("move:"))                       // move:dx:dy, in screen pixels, as dragging it would
        {
            var parts = what[5..].Split(':');
            _window.Position += new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
        }
        else if (what.StartsWith("colour:"))
            Choose(what[7..], _sticker);
        else if (what.StartsWith("sticker:"))
            Choose(_colour, what[8..] is { Length: > 0 } sticker ? sticker : null);
        else if (what.StartsWith("write:"))                 // write:title:text fills its fields
        {
            var parts = what[6..].Split(':', 2);
            _titleField.Text = parts[0];
            _textField.Text = parts.Length > 1 ? parts[1] : "";
        }
        else if (what == "tell")
            _tell.ButtonPressed = !_tell.ButtonPressed;
        else if (what is "away" or "close")
            Finish(keep: true);
        else if (what == "esc")
            _Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        else if ((what switch { "pin" => _pin, "off" => _off, "edit" => _edit, "delete" => _delete, "done" => _done, "print" => _print, _ => null }) is { } tool)
            tool.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
