using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// An app on the CRT's small system (see <see cref="CrtOs"/>): its icon on the home screen, and its own screen,
/// below the bar the system draws (back, and the app's name). Points are in the app's own pixels, from its top left.
/// </summary>
public abstract partial class CrtApp : Control
{
    protected static readonly Color Ink = new("6a1c5e"), Accent = new("e0457f"), Glow = new(1, 1, 1, 0.42f);
    protected static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
    protected static readonly Font Round = Companion.Paper.Font(650);

    /// <summary>Its name in settings (the home screen's order).</summary>
    public abstract string Id { get; }

    /// <summary>Its name under its icon and in the bar, in capitals.</summary>
    public abstract string Label { get; }

    /// <summary>The colour of its icon's tile, and of the screen it opens out of.</summary>
    public abstract Color Tint { get; }

    /// <summary>Draws its icon's picture inside the tile's box.</summary>
    public abstract void DrawGlyph(CanvasItem on, Rect2 box);

    /// <summary>It is about to be shown: whatever it shows is read afresh.</summary>
    public virtual void Opened() { }

    /// <summary>A click on its screen.</summary>
    public virtual void Click(Vector2 at) { }

    /// <summary>The pointer is over its screen (null: it left).</summary>
    public virtual void Hover(Vector2? at) { }

    /// <summary>A swipe to its next page (+1) or the one before (-1); false when there is none.</summary>
    public virtual bool Swipe(int step) => false;

    /// <summary>How many pages it has, and which one shows (the system draws the dots).</summary>
    public virtual (int Page, int Pages) Paging => (0, 1);

    /// <summary>Text centred in a box, in one line, cut with "…" when it doesn't fit.</summary>
    protected void Centred(string text, Font font, int size, Color colour, Rect2 box)
    {
        var fitted = Fit(text, font, size, box.Size.X);
        var y = box.Position.Y + (box.Size.Y + font.GetAscent(size) - font.GetDescent(size)) / 2;
        DrawString(font, new Vector2(box.Position.X, y), fitted, HorizontalAlignment.Center, box.Size.X, size, colour);
    }

    /// <summary>The longest start of <paramref name="text"/> that fits a width, with "…" when cut.</summary>
    protected static string Fit(string text, Font font, int size, float width)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width)
            return text;
        for (var n = text.Length - 1; n > 0; n--)
            if (font.GetStringSize(text[..n].TrimEnd() + "…", HorizontalAlignment.Left, -1, size).X <= width)
                return text[..n].TrimEnd() + "…";
        return "…";
    }

    /// <summary>A rounded box: fill, ink outline.</summary>
    protected void Box(Rect2 box, Color fill, Color line, float width, int corner)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = line, AntiAliasing = true };
        style.SetBorderWidthAll((int)width);
        style.SetCornerRadiusAll(corner);
        style.Draw(GetCanvasItem(), box);
    }
}
