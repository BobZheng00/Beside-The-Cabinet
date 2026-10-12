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
            theme.SetColor("font_uneditable_color", field, Letters);      // read-only (a calendar's entry) yet easy to read
            theme.SetColor("caret_color", field, Ink);
            theme.SetColor("selection_color", field, Pink);
        }
        theme.SetColor("background_color", "TextEdit", Colors.White);

        Button("Button", Accent, AccentLight, AccentDark, Colors.White);
        Button("OptionButton", Wash, new Color("E2DAF7"), new Color("D3C8F2"), Letters);
        theme.SetTypeVariation("Quiet", "Button");
        Button("Quiet", Wash, new Color("E2DAF7"), new Color("D3C8F2"), Letters);
        // small ones, as the answers on her bubble
        theme.SetTypeVariation("Small", "Button");
        Button("Small", Accent, AccentLight, AccentDark, Colors.White, small: true);
        theme.SetTypeVariation("QuietSmall", "Button");
        Button("QuietSmall", Wash, new Color("E2DAF7"), new Color("D3C8F2"), Letters, small: true);
        void Button(string type, Color fill, Color hover, Color pressed, Color text, bool small = false)
        {
            var (x, y) = small ? (10, 2) : (14, 6);
            theme.SetStylebox("normal", type, Box(fill, fill, 0, 10, x, y));
            theme.SetStylebox("hover", type, Box(hover, hover, 0, 10, x, y));
            theme.SetStylebox("pressed", type, Box(pressed, pressed, 0, 10, x, y));
            theme.SetStylebox("disabled", type, Box(Wash, Wash, 0, 10, x, y));
            theme.SetStylebox("focus", type, new StyleBoxEmpty());
            if (small)
                theme.SetFontSize("font_size", type, 13);
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

        // tabs like index cards: the chosen one is part of the page, the others washed out behind it
        theme.SetStylebox("panel", "TabContainer", Box(Sheet, line, 1, 8, 12, 12));
        StyleBoxFlat Tab(Color fill, Color border)
        {
            var tab = Box(fill, border, 1, 8, 14, 6);
            tab.BorderWidthBottom = 0;
            tab.CornerRadiusBottomLeft = tab.CornerRadiusBottomRight = 0;
            return tab;
        }
        theme.SetStylebox("tab_selected", "TabContainer", Tab(Sheet, line));
        theme.SetStylebox("tab_unselected", "TabContainer", Tab(Wash, Wash));
        theme.SetStylebox("tab_hovered", "TabContainer", Tab(new Color("E2DAF7"), new Color("E2DAF7")));
        theme.SetStylebox("tab_focus", "TabContainer", new StyleBoxEmpty());
        theme.SetColor("font_selected_color", "TabContainer", Ink);
        theme.SetColor("font_unselected_color", "TabContainer", Letters);
        theme.SetColor("font_hovered_color", "TabContainer", Ink);
        theme.SetConstant("side_margin", "TabContainer", 0);

        // the settings' list of pages: plain words, the one showing on a wash, under small group names
        theme.SetTypeVariation("Side", "Button");
        var hover = new Color("F4EFFC");
        theme.SetStylebox("normal", "Side", Box(Colors.Transparent, Colors.Transparent, 0, 8, 12, 6));
        theme.SetStylebox("hover", "Side", Box(hover, hover, 0, 8, 12, 6));
        theme.SetStylebox("pressed", "Side", Box(Wash, Wash, 0, 8, 12, 6));
        theme.SetStylebox("hover_pressed", "Side", Box(Wash, Wash, 0, 8, 12, 6));
        theme.SetStylebox("focus", "Side", new StyleBoxEmpty());
        theme.SetColor("font_color", "Side", Letters);
        theme.SetColor("font_focus_color", "Side", Letters);
        theme.SetColor("font_hover_color", "Side", Ink);
        theme.SetColor("font_pressed_color", "Side", AccentDark);
        theme.SetColor("font_hover_pressed_color", "Side", AccentDark);
        theme.SetTypeVariation("Group", "Label");
        theme.SetFont("font", "Group", Font(600));
        theme.SetFontSize("font_size", "Group", 12);
        theme.SetColor("font_color", "Group", Faint);

        // hover tips: a slip of the same paper, not Godot's dark strip (their text is wrapped where set: tips don't wrap)
        theme.SetStylebox("panel", "TooltipPanel", Box(Colors.White, line, 1, 6, 10, 6));
        theme.SetColor("font_color", "TooltipLabel", Letters);
        theme.SetColor("font_shadow_color", "TooltipLabel", Colors.Transparent);
        theme.SetFontSize("font_size", "TooltipLabel", 13);

        // a slider: a soft track, filled in purple up to a round knob
        StyleBoxFlat Track(Color fill) => Box(fill, fill, 0, 4, 0, 3);
        theme.SetStylebox("slider", "HSlider", Track(new Color("E2DAF7")));
        theme.SetStylebox("grabber_area", "HSlider", Track(Accent));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Track(AccentLight));
        theme.SetIcon("grabber", "HSlider", Knob(AccentDark));
        theme.SetIcon("grabber_highlight", "HSlider", Knob(Accent));
        return theme;
    }

    /// <summary>A slider's round knob, drawn in code: <paramref name="fill"/> inside a white ring.</summary>
    private static Texture2D Knob(Color fill)
    {
        const int size = 18;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var centre = new Vector2((size - 1) / 2f, (size - 1) / 2f);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = new Vector2(x, y).DistanceTo(centre);
            var colour = d <= 6.5f ? fill : d <= 8.5f ? Colors.White : Colors.Transparent;
            if (d is > 8.5f and < 9.5f)
                colour = new Color(Colors.White, 9.5f - d);     // a soft edge
            image.SetPixel(x, y, colour);
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Dev (--offscreen): windows open off every monitor and leave focus alone, so tests never get in the way.</summary>
    public static bool OffScreen { get; set; }

    /// <summary>
    /// A window in the cabinet's paper style, sized for the screen's DPI (no bigger than the screen leaves room for: at
    /// 150% a tall one would run under the taskbar) and opened in the middle of it, or <paramref name="besideCabinet"/>
    /// where whoever is on the stool stays in view (left of the cabinet if there is room, else right of it).
    /// </summary>
    public static void Open(Window window, Vector2 size, bool besideCabinet = false)
    {
        var scale = DisplayServer.ScreenGetDpi(DisplayServer.WindowGetCurrentScreen()) / 96f;
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var wanted = (Vector2I)(size * scale);
        window.ContentScaleFactor = scale;
        window.Size = new Vector2I(Math.Min(wanted.X, screen.Size.X - 16), Math.Min(wanted.Y, screen.Size.Y - 48));     // the title bar is outside it
        window.MinSize = window.Size;
        window.Theme = WindowTheme();
        window.Position = OffScreen ? new Vector2I(-6000, 100) : besideCabinet ? Beside(window.Size, screen) : screen.Position + (screen.Size - window.Size) / 2;
        // shown, Windows makes a window the active one: off screen it would take the keys someone types elsewhere
        // (an Enter pressing its buttons), so it never takes focus there
        window.Unfocusable = OffScreen;
        window.Show();
        if (!OffScreen)
            window.GrabFocus();
    }

    private static Vector2I Beside(Vector2I size, Rect2I screen)
    {
        var cabinet = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        var top = Math.Clamp(cabinet.Position.Y + 40, screen.Position.Y + 32, Math.Max(screen.Position.Y + 32, screen.End.Y - size.Y));   // the title bar above it
        return cabinet.Position.X - size.X - 12 >= screen.Position.X ? new Vector2I(cabinet.Position.X - size.X - 12, top)
             : cabinet.End.X + 12 + size.X <= screen.End.X ? new Vector2I(cabinet.End.X + 12, top)
             : screen.Position + (screen.Size - size) / 2;
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
