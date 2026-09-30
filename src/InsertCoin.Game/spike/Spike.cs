using Godot;

namespace InsertCoin.Game.Spike;

/// <summary>
/// THROWAWAY spike (build step 1). Verifies on the real machine: transparent borderless window, SVG art,
/// 2D HDR glow, mouse passthrough outside the cabinet, an unfocusable topmost popup, and idle cost.
/// User args: --measure=SECONDS (quit after), --pos=X,Y (window position), --no-popup.
/// </summary>
public partial class Spike : Node2D
{
    private const float WindowScale = 0.6f; // viewport 600x900 → window 360x540
    private Vector2I _dragOffset;
    private bool _dragging;

    public override void _Ready()
    {
        var args = ParseArgs();
        GD.Print($"renderer={RenderingServer.GetCurrentRenderingMethod()} driver={RenderingServer.GetCurrentRenderingDriverName()} " +
                 $"transparent={DisplayServer.IsWindowTransparencyAvailable()} lowcpu={OS.LowProcessorUsageMode}");

        // Base art: SVG pre-rendered by Chrome at 2x (keeps text, blur shadows and baked glow).
        AddChild(new Sprite2D { Texture = GD.Load<Texture2D>("res://skins/cozy/cabinet@2x.png"), Centered = false, Scale = new Vector2(0.5f, 0.5f) });

        // Animated glow: additive radial halo over the CRT (Compatibility renderer has no usable 2D bloom here).
        var halo = new Sprite2D
        {
            Texture = new GradientTexture2D
            {
                Width = 420, Height = 380, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0.0f),
                Gradient = new Gradient { Colors = [new Color(1f, 0.45f, 0.85f, 0.55f), new Color(1f, 0.45f, 0.85f, 0f)], Offsets = [0f, 1f] },
            },
            Position = new Vector2(300, 345),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(halo);
        if (!args.ContainsKey("static"))
        {
            var pulse = CreateTween().SetLoops();
            pulse.TweenProperty(halo, "modulate:a", 0.45f, 1.6).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(halo, "modulate:a", 1.0f, 1.6).SetTrans(Tween.TransitionType.Sine);
        }

        // Click-through everywhere except the cabinet silhouette (window pixels).
        Vector2[] silhouette = [new(160, 90), new(464, 90), new(464, 170), new(508, 170), new(508, 810), new(470, 848), new(144, 848),
                                new(126, 826), new(126, 562), new(106, 562), new(106, 516), new(126, 516), new(126, 188), new(160, 188)];
        DisplayServer.WindowSetMousePassthrough([.. silhouette.Select(p => p * WindowScale)]);

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

    private static Dictionary<string, string> ParseArgs() =>
        OS.GetCmdlineUserArgs()
            .Select(a => a.TrimStart('-').Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
}
