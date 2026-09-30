using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>What the CRT should show right now. Built by the app from ledger state.</summary>
public sealed record ScreenState
{
    public long TimeLeftMs { get; init; }
    public long DebtMs { get; init; }
    public int Credits { get; init; }
    public float Fuel { get; init; }              // 0..1 of the token currently in the slot
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public bool Playing { get; init; }
    public string Status { get; init; } = "";     // "GAME: …", "PAUSED", "LINK DOWN", "NO STEAM", …
    public int WeekSolves { get; init; }
    public int WeekTokens { get; init; }
    public long WeekPlayedMs { get; init; }
}

/// <summary>
/// The live CRT: an offscreen UI rendered into a viewport and projected onto the cabinet's screen quad.
/// Shows the main readout, cycles attract-mode pages when idle, and can flash short messages.
/// </summary>
public partial class ScreenView : Node2D
{
    private static readonly Color Ink = new("6a1c5e");
    private static readonly Color Accent = new("e0457f");
    private static readonly Color Debt = new("c2185b");
    private const int PxPerUnit = 2;
    // Placeholder until an OFL arcade font is bundled: a bold system monospace (Windows ships Consolas).
    private static readonly Font ScreenFont = new SystemFont { FontNames = ["Consolas", "Lucida Console", "monospace"], FontWeight = 800 };

    private SubViewport _viewport = null!;
    private Label _top = null!, _topRight = null!, _big = null!, _caption = null!, _status = null!;
    private ColorRect _fuelBack = null!, _fuelFill = null!;
    private ScreenState _state = new();
    private (string Big, string Caption)? _flash;
    private int _attractPage;
    private int _flashId;
    private ShaderMaterial _material = null!;

    public void Build(IReadOnlyList<Vector2> quad, Vector2 sizeUnits)
    {
        var size = (Vector2I)(sizeUnits * PxPerUnit);
        _viewport = new SubViewport
        {
            Size = size,
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(_viewport);

        var root = new Control { Size = size };
        _viewport.AddChild(root);
        _top = AddLabel(root, 30, HorizontalAlignment.Left, new Vector2(34, 18), new Vector2(size.X - 68, 38));
        _topRight = AddLabel(root, 30, HorizontalAlignment.Right, new Vector2(34, 18), new Vector2(size.X - 68, 38));
        _big = AddLabel(root, 118, HorizontalAlignment.Center, new Vector2(0, 66), new Vector2(size.X, 132));
        _caption = AddLabel(root, 26, HorizontalAlignment.Center, new Vector2(10, 196), new Vector2(size.X - 20, 34));
        _status = AddLabel(root, 24, HorizontalAlignment.Center, new Vector2(30, 298), new Vector2(size.X - 60, 32));
        _fuelBack = new ColorRect { Position = new Vector2(56, 256), Size = new Vector2(size.X - 112, 20), Color = Ink with { A = 0.22f } };
        _fuelFill = new ColorRect { Position = _fuelBack.Position, Size = _fuelBack.Size, Color = Ink };
        root.AddChild(_fuelBack);
        root.AddChild(_fuelFill);

        var (r0, r1, r2) = Homography.QuadToUnit(quad);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/screen_quad.gdshader") };
        _material.SetShaderParameter("content", _viewport.GetTexture());
        _material.SetShaderParameter("inv_h_row0", r0);
        _material.SetShaderParameter("inv_h_row1", r1);
        _material.SetShaderParameter("inv_h_row2", r2);
        AddChild(new Polygon2D { Polygon = [.. quad], Material = _material });

        var attract = new Godot.Timer { WaitTime = 4.0, Autostart = true };
        attract.Timeout += () => { _attractPage = (_attractPage + 1) % 4; Redraw(); };
        AddChild(attract);
        Redraw();
    }

    public void Show(ScreenState state)
    {
        _state = state;
        if (state.Playing)
            _attractPage = 0;
        Redraw();
    }

    /// <summary>Takes over the screen for a moment, e.g. "+2 CREDITS" or "CONTINUE?".</summary>
    public void Flash(string big, string caption, double seconds)
    {
        _flash = (big, caption);
        var id = ++_flashId;
        Redraw();
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (id != _flashId)
                return;             // a newer flash owns the screen
            _flash = null;
            Redraw();
        };
    }

    public void SetBrightness(float brightness) => _material.SetShaderParameter("brightness", brightness);

    private void Redraw()
    {
        var s = _state;
        var inDebt = s.DebtMs > 0 && s.TimeLeftMs == 0;
        var page = s.Playing || _flash is not null ? 0 : _attractPage;

        _top.Text = $"CREDIT {s.Credits:00}";
        _topRight.Text = s.Streak > 0 ? $"♥ x{s.Streak}" : "";
        _topRight.LabelSettings.FontColor = Accent;
        _fuelBack.Visible = _fuelFill.Visible = page == 0 && _flash is null && !inDebt;
        _fuelFill.Size = _fuelBack.Size with { X = _fuelBack.Size.X * Mathf.Clamp(s.Fuel, 0, 1) };
        _status.Text = s.Status;
        _big.LabelSettings.FontColor = inDebt ? Debt : Ink;

        (_big.Text, _caption.Text) = _flash ?? page switch
        {
            1 => ($"{s.BestStreak}", "HI-SCORE · BEST STREAK"),
            2 => ($"{s.WeekSolves} / {s.WeekTokens}", $"THIS WEEK · SOLVES / TOKENS · {Clock(s.WeekPlayedMs)} PLAYED"),
            3 => ("INSERT", "COIN · SOLVE A PROBLEM"),
            _ when inDebt => ($"-{Clock(s.DebtMs)}", $"OWED · {s.DebtMs / (15 * 60_000)} IOU"),
            _ => (Clock(s.TimeLeftMs), "TIME LEFT"),
        };
        _big.LabelSettings.FontSize = _big.Text.Length > 7 ? 70 : _big.Text.Length > 5 ? 92 : 118;
        _caption.LabelSettings.FontSize = _caption.Text.Length > 26 ? 18 : 26;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private static string Clock(long ms)
    {
        var total = (int)(ms / 1000);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    }

    private static Label AddLabel(Control parent, int size, HorizontalAlignment align, Vector2 position, Vector2 box)
    {
        var label = new Label
        {
            Position = position,
            Size = box,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            LabelSettings = new LabelSettings { Font = ScreenFont, FontSize = size, FontColor = Ink },
        };
        parent.AddChild(label);
        return label;
    }
}
