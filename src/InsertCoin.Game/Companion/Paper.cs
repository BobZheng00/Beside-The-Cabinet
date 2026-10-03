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

    /// <summary>The cabinet's paper and ink for a plain window: cream page, purple buttons, Fredoka.</summary>
    public static Theme WindowTheme()
    {
        var theme = new Theme { DefaultFont = Font(450), DefaultFontSize = 15 };
        theme.SetStylebox("panel", "PanelContainer", Box(Sheet, Sheet, 0, 0, 22, 18));
        theme.SetColor("font_color", "Label", Letters);

        theme.SetTypeVariation("Heading", "Label");
        theme.SetFont("font", "Heading", Font(620));
        theme.SetFontSize("font_size", "Heading", 22);
        theme.SetColor("font_color", "Heading", Ink);
        theme.SetTypeVariation("Subheading", "Label");
        theme.SetFont("font", "Subheading", Font(580));
        theme.SetColor("font_color", "Subheading", Ink);
        theme.SetTypeVariation("Hint", "Label");
        theme.SetColor("font_color", "Hint", Faint);
        theme.SetFontSize("font_size", "Hint", 13);

        var line = new Color("C9BFE6");
        foreach (var field in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", field, Box(Colors.White, line, 1, 8, 10, 6));
            theme.SetStylebox("focus", field, Box(Colors.Transparent, Accent, 2, 8, 10, 6));
            theme.SetStylebox("read_only", field, Box(Wash, line, 1, 8, 10, 6));
            theme.SetColor("font_color", field, Letters);
            theme.SetColor("font_placeholder_color", field, Faint);
            theme.SetColor("caret_color", field, Ink);
            theme.SetColor("selection_color", field, Pink);
        }
        theme.SetColor("background_color", "TextEdit", Colors.White);

        Button("Button", Accent, AccentLight, AccentDark, Colors.White);
        Button("OptionButton", Wash, new Color("E2DAF7"), new Color("D3C8F2"), Letters);
        theme.SetTypeVariation("Quiet", "Button");
        Button("Quiet", Wash, new Color("E2DAF7"), new Color("D3C8F2"), Letters);
        void Button(string type, Color fill, Color hover, Color pressed, Color text)
        {
            theme.SetStylebox("normal", type, Box(fill, fill, 0, 10, 14, 6));
            theme.SetStylebox("hover", type, Box(hover, hover, 0, 10, 14, 6));
            theme.SetStylebox("pressed", type, Box(pressed, pressed, 0, 10, 14, 6));
            theme.SetStylebox("disabled", type, Box(Wash, Wash, 0, 10, 14, 6));
            theme.SetStylebox("focus", type, new StyleBoxEmpty());
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
                theme.SetColor(state, type, text);
            theme.SetColor("font_disabled_color", type, Faint);
        }
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());          // a plain box and label, not a purple button
        theme.SetColor("font_color", "CheckBox", Letters);
        theme.SetColor("font_hover_color", "CheckBox", Ink);
        theme.SetColor("font_pressed_color", "CheckBox", Accent);
        theme.SetColor("font_hover_pressed_color", "CheckBox", Accent);
        theme.SetStylebox("focus", "CheckBox", new StyleBoxEmpty());

        theme.SetStylebox("panel", "PopupMenu", Box(Colors.White, line, 1, 8, 6, 6));
        theme.SetStylebox("hover", "PopupMenu", Box(Wash, Wash, 0, 6, 8, 4));
        theme.SetColor("font_color", "PopupMenu", Letters);
        theme.SetColor("font_hover_color", "PopupMenu", Ink);
        theme.SetStylebox("separator", "HSeparator", Box(new Color("E2DAF7"), new Color("E2DAF7"), 0, 0, 0, 1));
        theme.SetStylebox("panel", "ScrollContainer", Box(new Color("FBF6EE"), new Color("E2DAF7"), 1, 8, 8, 8));
        return theme;
    }

    /// <summary>Dev (--offscreen): windows open off every monitor and leave focus alone, so tests never get in the way.</summary>
    public static bool OffScreen { get; set; }

    /// <summary>A window in the cabinet's paper style, sized for the screen's DPI and opened in the middle of it.</summary>
    public static void Open(Window window, Vector2 size)
    {
        var scale = DisplayServer.ScreenGetDpi(DisplayServer.WindowGetCurrentScreen()) / 96f;
        window.ContentScaleFactor = scale;
        window.Size = (Vector2I)(size * scale);
        window.MinSize = window.Size;
        window.Theme = WindowTheme();
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        window.Position = OffScreen ? new Vector2I(-6000, 100) : screen.Position + (screen.Size - window.Size) / 2;
        window.Show();
        if (!OffScreen)
            window.GrabFocus();
    }

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
