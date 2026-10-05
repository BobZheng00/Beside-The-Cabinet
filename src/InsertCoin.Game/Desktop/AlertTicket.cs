using Godot;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The in-game channel: a small ticket that slides in at the screen corner above a (borderless) game and never
/// takes focus. Exclusive-fullscreen games cover it; those get the alert sounds only. A reminder's ticket is taller
/// and has buttons (Snooze, OK): a window that cannot take focus still takes clicks.
/// </summary>
public partial class AlertTicket : Node
{
    private static readonly Vector2I Size = new(300, 96);
    private static readonly Font Round = new FontVariation
    {
        BaseFont = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf"),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 650 },
    };
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
    private static readonly Color Paper = new("fff4e6"), Ink = new("1e1630"), Title = new("c2185b"), Words = new("6a1c5e");

    private Window? _window;
    private Tween? _tween;
    private Vector2I _hidden;
    private readonly List<Button> _buttons = [];

    /// <summary>Shows a ticket, replacing any still showing.</summary>
    /// <param name="seconds">How long it stays; 0 or less: until one of its <paramref name="buttons"/> is pressed.</param>
    /// <param name="buttons">Buttons along its foot; each closes the ticket after doing what it does.</param>
    public void Show(string title, string detail, double seconds = 5, IReadOnlyList<(string Label, Action Pressed)>? buttons = null)
    {
        var reminder = buttons is { Count: > 0 };
        var size = reminder ? new Vector2I(360, 176) : Size;
        // A newer ticket replaces the current one: stop its animation before freeing its window.
        _tween?.Kill();
        if (_window is not null && IsInstanceValid(_window))
            _window.QueueFree();
        _buttons.Clear();
        var window = new Window
        {
            Borderless = true,
            Transparent = true,
            TransparentBg = true,
            AlwaysOnTop = true,
            Unfocusable = true,
            Size = size,
            Title = "INSERT COIN ticket",
        };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(Paper, 4, 12, 14, 8));
        var rows = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        rows.AddThemeConstantOverride("separation", 6);
        rows.AddChild(Line(title, Pixel, 20, Title));
        var text = Line(detail, Round, reminder ? 17 : 20, Words);
        if (reminder)
        {
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.MaxLinesVisible = 3;
            text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.CustomMinimumSize = new Vector2(size.X - 36, 0);
        }
        rows.AddChild(text);
        if (buttons is { Count: > 0 })
        {
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 10);
            foreach (var (label, pressed) in buttons)
            {
                var button = new Button { Text = label, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                button.AddThemeFontOverride("font", Round);
                button.AddThemeFontSizeOverride("font_size", 15);
                foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
                    button.AddThemeColorOverride(state, Words);
                button.AddThemeStyleboxOverride("normal", Box(new Color("fde4ef"), 2, 8, 12, 3));
                button.AddThemeStyleboxOverride("hover", Box(new Color("fbd0e3"), 2, 8, 12, 3));
                button.AddThemeStyleboxOverride("pressed", Box(new Color("f7b8d4"), 2, 8, 12, 3));
                button.Pressed += () =>
                {
                    pressed();
                    Close();
                };
                row.AddChild(button);
                _buttons.Add(button);
            }
            rows.AddChild(row);
        }
        panel.AddChild(rows);
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        window.AddChild(panel);
        AddChild(window);
        _window = window;

        // Windows reserves no work area for an auto-hidden taskbar; keep clear of it anyway.
        var screen = DisplayServer.GetPrimaryScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var bottomMargin = usable.Size == DisplayServer.ScreenGetSize(screen) ? 72 : 24;
        var shown = usable.End - size - new Vector2I(24, bottomMargin);
        _hidden = new Vector2I(usable.End.X + 8, shown.Y);
        if (Companion.Paper.OffScreen)
            (shown, _hidden) = (new Vector2I(-6000, 900), new Vector2I(-5600, 900));     // a test run kept off every screen (--offscreen): the ticket too
        window.Position = _hidden;
        window.Show();

        _tween = CreateTween();
        _tween.TweenProperty(window, "position", shown, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (seconds > 0)
        {
            _tween.TweenInterval(seconds);
            SlideOut(_tween, window);
        }
    }

    /// <summary>Dev: presses the showing ticket's button at <paramref name="index"/>.</summary>
    public void DevPress(int index)
    {
        if (index < _buttons.Count && IsInstanceValid(_buttons[index]))
            _buttons[index].EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void Close()
    {
        if (_window is not { } window || !IsInstanceValid(window))
            return;
        _tween?.Kill();
        _tween = CreateTween();
        SlideOut(_tween, window);
    }

    private void SlideOut(Tween tween, Window window)
    {
        tween.TweenProperty(window, "position", _hidden, 0.25).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_window == window)
            {
                _window = null;
                _buttons.Clear();
            }
            window.QueueFree();
        }));
    }

    private static StyleBoxFlat Box(Color colour, int border, int corner, int marginX, int marginY) => new()
    {
        BgColor = colour,
        BorderColor = Ink,
        BorderWidthLeft = border, BorderWidthTop = border, BorderWidthRight = border, BorderWidthBottom = border,
        CornerRadiusTopLeft = corner, CornerRadiusTopRight = corner, CornerRadiusBottomLeft = corner, CornerRadiusBottomRight = corner,
        ContentMarginLeft = marginX, ContentMarginRight = marginX, ContentMarginTop = marginY, ContentMarginBottom = marginY,
    };

    private static Label Line(string text, Font font, int size, Color colour) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = colour },
    };
}
