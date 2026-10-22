using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// SETTINGS on the CRT: its icon opens the Settings window beside the cabinet, as the tray's Settings… does. Settings is
/// never drawn on the screen itself (decided 2026-10-17): the screen stays on the home screen.
/// </summary>
public partial class SettingsApp(Action open) : CrtApp
{
    public override string Id => "settings";
    public override string Label => "SETTINGS";
    public override Color Tint => new("F2D9F7");
    public override bool OpensWindow => true;

    public override void Opened() => open();

    public override void DrawGlyph(CanvasItem on, Rect2 box)
    {
        // a cog: eight teeth round a wheel, with its hole
        var c = box.GetCenter();
        var r = box.Size.X * 0.3f;
        var metal = new Color("B8AEE0");
        for (var i = 0; i < 8; i++)
        {
            var along = Vector2.Up.Rotated(i * Mathf.Tau / 8);
            var side = along.Orthogonal() * r * 0.26f;
            Vector2[] tooth = [c + along * r * 0.8f + side, c + along * r * 1.38f + side * 0.8f, c + along * r * 1.38f - side * 0.8f, c + along * r * 0.8f - side];
            on.DrawColoredPolygon(tooth, metal);
            on.DrawPolyline([.. tooth], Ink, 3, true);
        }
        on.DrawCircle(c, r, metal);
        on.DrawArc(c, r, 0, Mathf.Tau, 40, Ink, 3, true);
        on.DrawCircle(c, r * 0.38f, Tint);
        on.DrawArc(c, r * 0.38f, 0, Mathf.Tau, 24, Ink, 3, true);
    }
}
