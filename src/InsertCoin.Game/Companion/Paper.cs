using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>The look of everything she writes on and you write to her on: cream paper, the cabinet's ink, Fredoka.</summary>
public static class Paper
{
    public static readonly Color Sheet = new("FFF7EC"), Ink = new("1E1630"), Letters = new("3B2E5E"), Faint = new("9C90BC"),
        Accent = new("6B59A7"), AccentLight = new("8674C4"), AccentDark = new("4F3F8C"), Pink = new("FFB3D1"), Wash = new("EDE7FA"),
        Good = new("2E8B57"), Bad = new("C23F5A");

    private static readonly FontFile Fredoka = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf");

    /// <summary>Fredoka at a weight (300–700). Letters it lacks (Chinese, Japanese…) come from the system's fonts.</summary>
    public static Font Font(int weight) => new FontVariation
    {
        BaseFont = Fredoka,
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = weight },
    };

    /// <summary>A rounded box: fill, outline, corner radius and inner margins.</summary>
    public static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int corner, int marginX, int marginY)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill, BorderColor = border, AntiAliasing = true,
            ContentMarginLeft = marginX, ContentMarginRight = marginX, ContentMarginTop = marginY, ContentMarginBottom = marginY,
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(corner);
        return box;
    }
}
