using Godot;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The in-game channel: a small ticket that slides in at the screen corner above a (borderless) game and never
/// takes focus. Exclusive-fullscreen games cover it; those get the alert sounds only.
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

    private Window? _window;
    private Tween? _tween;

    /// <param name="tall">Room for a few lines of detail (a reminder), not one.</param>
    public void Show(string title, string detail, double seconds = 5, bool tall = false)
    {
        var size = tall ? new Vector2I(340, 132) : Size;
        // A newer ticket replaces the current one: stop its animation before freeing its window.
        _tween?.Kill();
        if (_window is not null && IsInstanceValid(_window))
            _window.QueueFree();
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
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("fff4e6"),
            BorderColor = new Color("1e1630"),
            BorderWidthLeft = 4, BorderWidthTop = 4, BorderWidthRight = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        var rows = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        rows.AddChild(Line(title, Pixel, 20, new Color("c2185b")));
        var text = Line(detail, Round, tall ? 17 : 20, new Color("6a1c5e"));
        if (tall)
        {
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.MaxLinesVisible = 3;
            text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.CustomMinimumSize = new Vector2(size.X - 36, 0);
        }
        rows.AddChild(text);
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
        var hidden = new Vector2I(usable.End.X + 8, shown.Y);
        if (Companion.Paper.OffScreen)
            (shown, hidden) = (new Vector2I(-6000, 900), new Vector2I(-5600, 900));     // a test run kept off every screen (--offscreen): the ticket too
        window.Position = hidden;
        window.Show();

        _tween = CreateTween();
        _tween.TweenProperty(window, "position", shown, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenInterval(seconds);
        _tween.TweenProperty(window, "position", hidden, 0.25).SetEase(Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() =>
        {
            _window = null;
            window.QueueFree();
        }));
    }

    private static Label Line(string text, Font font, int size, Color colour) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = colour },
    };
}
