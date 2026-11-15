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

    // ---- Buttons, for apps made of them ------------------------------------------------------------------------------

    private readonly List<(Rect2 Box, Action Pressed)> _buttons = [];
    private Vector2? _pointer;

    /// <summary>Forgets last frame's buttons: an app made of buttons calls it first thing as it draws.</summary>
    protected void ClearButtons() => _buttons.Clear();

    /// <summary>
    /// A button drawn this frame: a rounded box with its label, lit under the pointer, filled when <paramref name="chosen"/>;
    /// a click on it does <paramref name="pressed"/> (see <see cref="Press"/>).
    /// </summary>
    protected void Button(Rect2 box, string label, Action pressed, bool chosen = false, Font? font = null, int size = 15)
    {
        _buttons.Add((box, pressed));
        var over = _pointer is { } at && box.HasPoint(at);
        Box(box, chosen ? Accent : over ? Glow with { A = 0.75f } : Glow, Ink, 4, 14);
        Centred(label, font ?? Pixel, size, chosen ? Colors.White : Ink, box);
    }

    /// <summary>A round button with a drawn picture (play, pause, skip): the same, as a circle; faded and doing nothing unless <paramref name="enabled"/>.</summary>
    protected void RoundButton(Vector2 centre, float radius, Action<Vector2, float, Color> picture, Action pressed, bool enabled = true)
    {
        if (!enabled)
        {
            DrawCircle(centre, radius, Glow with { A = 0.2f });
            DrawArc(centre, radius, 0, Mathf.Tau, 40, Ink with { A = 0.25f }, 4, true);
            picture(centre, radius, Ink with { A = 0.25f });
            return;
        }
        var box = new Rect2(centre - Vector2.One * radius, Vector2.One * radius * 2);
        _buttons.Add((box, pressed));
        var over = _pointer is { } at && at.DistanceTo(centre) <= radius;
        DrawCircle(centre, radius, over ? Accent : Glow);
        DrawArc(centre, radius, 0, Mathf.Tau, 40, Ink, 4, true);
        picture(centre, radius, over ? Colors.White : Ink);
    }

    /// <summary>Does what the button under the point does; false when there is none.</summary>
    protected bool Press(Vector2 at)
    {
        foreach (var (box, pressed) in _buttons)
            if (box.HasPoint(at))
            {
                pressed();
                QueueRedraw();
                return true;
            }
        return false;
    }

    /// <summary>Keeps the pointer, for lighting the button under it.</summary>
    protected void Point(Vector2? at)
    {
        if (at == _pointer)
            return;
        _pointer = at;
        QueueRedraw();
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
