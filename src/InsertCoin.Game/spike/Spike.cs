using Godot;

namespace InsertCoin.Game.Spike;

/// <summary>
/// THROWAWAY spike (build step 1). Verifies on the real machine: transparent borderless window, SVG art,
/// 2D HDR glow, mouse passthrough outside the cabinet, an unfocusable topmost popup, and idle cost.
/// User args: --measure=SECONDS (quit after), --pos=X,Y (window position), --no-popup.
/// </summary>
public partial class Spike : Node2D
{
    private const float WindowScale = 0.6f; // viewport 600x920 → window 360x552
    private Vector2I _dragOffset;
    private bool _dragging;

    public override void _Ready()
    {
        var args = ParseArgs();
        GD.Print($"renderer={RenderingServer.GetCurrentRenderingMethod()} driver={RenderingServer.GetCurrentRenderingDriverName()} " +
                 $"transparent={DisplayServer.IsWindowTransparencyAvailable()} lowcpu={OS.LowProcessorUsageMode}");

        // Base art: SVG pre-rendered by Chrome at 2x (keeps text, blur shadows and baked glow).
        var art = GD.Load<Texture2D>("res://skins/cozy/cabinet@2x.png");
        AddChild(new Sprite2D { Texture = art, Centered = false, Scale = new Vector2(0.5f, 0.5f) });

        // On Windows the passthrough polygon is a window region: it clips drawing too. So it must enclose every
        // visible pixel, soft shadows included — take the convex hull of the art's alpha instead of a hand-made shape.
        DisplayServer.WindowSetMousePassthrough(VisibleHull(art.GetImage(), 0.5f * WindowScale));

        if (args.TryGetValue("pos", out var pos) && pos.Split(',') is [var x, var y])
            DisplayServer.WindowSetPosition(new Vector2I(int.Parse(x), int.Parse(y)));

        if (!args.ContainsKey("no-popup"))
            ShowTicketPopup();

        if (args.TryGetValue("measure", out var seconds))
            GetTree().CreateTimer(double.Parse(seconds)).Timeout += () => GetTree().Quit();
    }

    private void ShowTicketPopup()
    {
        var popup = new Window
        {
            Borderless = true,
            Transparent = true,
            TransparentBg = true,
            AlwaysOnTop = true,
            Unfocusable = true,
            Size = new Vector2I(260, 84),
            Title = "INSERT COIN ticket",
        };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("fff4e6"),
            BorderColor = new Color("231a33"),
            BorderWidthLeft = 4, BorderWidthTop = 4, BorderWidthRight = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
        });
        panel.AddChild(new Label
        {
            Text = "5:00 LEFT",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings { FontSize = 28, FontColor = new Color("c2185b") },
        });
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        popup.AddChild(panel);
        AddChild(popup);

        var usable = DisplayServer.ScreenGetUsableRect();
        GD.Print($"usable={usable} screen={DisplayServer.ScreenGetSize()} scale={DisplayServer.ScreenGetScale()} dpi={DisplayServer.ScreenGetDpi()}");
        // Windows reserves no work area for an auto-hidden taskbar; keep clear of it anyway.
        var bottomMargin = usable.Size == DisplayServer.ScreenGetSize() ? 72 : 24;
        popup.Position = usable.End - popup.Size - new Vector2I(24, bottomMargin);
        popup.Show();
        GetTree().CreateTimer(8).Timeout += popup.QueueFree;
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _dragging = b.Pressed;
                _dragOffset = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
                break;
            case InputEventMouseMotion when _dragging:
                DisplayServer.WindowSetPosition(DisplayServer.MouseGetPosition() - _dragOffset);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                GetTree().Quit();
                break;
        }
    }

    private static Vector2[] VisibleHull(Image image, float toWindow)
    {
        var alpha = new Bitmap();
        alpha.CreateFromImageAlpha(image, 0.004f);
        var points = alpha.OpaqueToPolygons(new Rect2I(Vector2I.Zero, image.GetSize()), 2f).SelectMany(p => p);
        return [.. Geometry2D.ConvexHull([.. points]).Select(p => (p * toWindow).Round())];
    }

    private static Dictionary<string, string> ParseArgs() =>
        OS.GetCmdlineUserArgs()
            .Select(a => a.TrimStart('-').Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
}
