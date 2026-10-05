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
    public string Initials { get; init; } = "";
}

/// <summary>
/// The live CRT: an offscreen UI rendered into a viewport and projected onto the cabinet's screen quad.
/// Priority: a prompt (text entry) over a flash (short message) over the readout / attract pages. While her
/// microphone is open, "MIC" with a blinking dot takes the top right corner, whatever else is showing.
/// </summary>
public partial class ScreenView : Node2D
{
    private static readonly Color Ink = new("6a1c5e");
    private static readonly Color Accent = new("e0457f");
    private static readonly Color Debt = new("c2185b");
    private static readonly Font Pixel = GD.Load<FontFile>("res://fonts/PressStart2P-Regular.ttf");
    // Small text uses a rounded proportional face: the pixel font is too wide to stay legible on a ~100 px screen.
    private static readonly Font Round = new FontVariation
    {
        BaseFont = GD.Load<FontFile>("res://fonts/Fredoka-Variable.ttf"),
        VariationOpentype = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 650 },
    };
    private const int PxPerUnit = 2;

    private SubViewport _viewport = null!;
    private Label _top = null!, _topRight = null!, _big = null!, _caption = null!, _status = null!;
    private ColorRect _fuelBack = null!, _fuelFill = null!, _micDot = null!;
    private Label _mic = null!;
    private bool _listening;
    private ShaderMaterial _material = null!;
    private ScreenState _state = new();
    private (string Big, string Caption)? _flash;
    private (string Big, string Caption, string Status)? _prompt;
    private int _attractPage, _flashId;
    private float _width;

    public void Build(IReadOnlyList<Vector2> quad, Vector2 sizeUnits)
    {
        var size = (Vector2I)(sizeUnits * PxPerUnit);
        _width = size.X;
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
        _top = AddLabel(root, Pixel, 21, HorizontalAlignment.Left, new Vector2(38, 26), new Vector2(size.X - 76, 28));
        _topRight = AddLabel(root, Pixel, 21, HorizontalAlignment.Right, new Vector2(38, 26), new Vector2(size.X - 76, 28));
        _big = AddLabel(root, Pixel, 64, HorizontalAlignment.Center, new Vector2(0, 76), new Vector2(size.X, 84));
        _caption = AddLabel(root, Round, 30, HorizontalAlignment.Center, new Vector2(14, 166), new Vector2(size.X - 28, 76));
        _caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _caption.LabelSettings.LineSpacing = -4;
        _status = AddLabel(root, Round, 27, HorizontalAlignment.Center, new Vector2(40, 290), new Vector2(size.X - 80, 34));
        _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _topRight.LabelSettings.FontColor = Accent;
        _mic = AddLabel(root, Pixel, 21, HorizontalAlignment.Right, new Vector2(38, 26), new Vector2(size.X - 76, 28));
        _mic.Text = "MIC";
        _mic.LabelSettings.FontColor = Accent;
        _micDot = new ColorRect { Position = new Vector2(size.X - 38 - 3 * 21 - 24, 33), Size = new Vector2(14, 14), Color = Accent };
        root.AddChild(_micDot);
        _fuelBack = new ColorRect { Position = new Vector2(60, 258), Size = new Vector2(size.X - 120, 20), Color = Ink with { A = 0.22f } };
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
        var blink = new Godot.Timer { WaitTime = 0.6, Autostart = true };
        blink.Timeout += () =>
        {
            if (!_listening)
                return;
            _micDot.Visible = !_micDot.Visible;             // like a recording light
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        };
        AddChild(blink);
        Redraw();
    }

    public void Show(ScreenState state)
    {
        _state = state;
        if (state.Playing)
            _attractPage = 0;
        Redraw();
    }

    /// <summary>Takes over the screen for a moment, e.g. "+2" or "CONTINUE?".</summary>
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

    /// <summary>Holds the screen for text entry until <see cref="ClearPrompt"/>.</summary>
    public void Prompt(string big, string caption, string status)
    {
        _prompt = (big, caption, status);
        Redraw();
    }

    public void ClearPrompt()
    {
        _prompt = null;
        Redraw();
    }

    public void SetBrightness(float brightness) => _material.SetShaderParameter("brightness", brightness);

    /// <summary>Her microphone is open: MIC shows in the top right corner.</summary>
    public bool Mic
    {
        set
        {
            _listening = value;
            _micDot.Visible = value;
            Redraw();
        }
    }

    private void Redraw()
    {
        var s = _state;
        var inDebt = s.DebtMs > 0 && s.TimeLeftMs == 0;
        var takenOver = _prompt is not null || _flash is not null;
        var page = s.Playing || takenOver ? 0 : _attractPage;

        _top.Text = $"CREDIT {s.Credits:00}";
        _topRight.Text = s.Streak > 0 ? $"x{s.Streak}" : "";
        _top.Visible = _prompt is null;
        _topRight.Visible = _prompt is null && !_listening;
        _mic.Visible = _listening;
        _fuelBack.Visible = _fuelFill.Visible = page == 0 && !takenOver && !inDebt;
        _fuelFill.Size = _fuelBack.Size with { X = _fuelBack.Size.X * Mathf.Clamp(s.Fuel, 0, 1) };
        _status.Text = _prompt?.Status ?? s.Status;
        _big.LabelSettings.FontColor = inDebt && !takenOver ? Debt : Ink;

        (_big.Text, _caption.Text) = _prompt is { } p ? (p.Big, p.Caption) : _flash ?? page switch
        {
            1 => ($"{s.BestStreak}", $"HI-SCORE {s.Initials}\nBEST STREAK"),
            2 => ($"{s.WeekSolves}/{s.WeekTokens}", $"SOLVES / TOKENS THIS WEEK\n{Clock(s.WeekPlayedMs)} PLAYED"),
            3 => ("INSERT", "COIN\nSOLVE A PROBLEM"),
            _ when inDebt => ($"-{Clock(s.DebtMs)}", $"OWED - {s.DebtMs / (15 * 60_000)} IOU"),
            _ => (Clock(s.TimeLeftMs), "TIME LEFT"),
        };
        // Press Start 2P is square: fit the big line to the screen width.
        _big.LabelSettings.FontSize = Math.Clamp((int)((_width - 44) / Math.Max(_big.Text.Length, 1)), 16, 64);
        // Captions sit above the fuel bar; two-line captions use the bar's space when it is hidden.
        _caption.LabelSettings.FontSize = _caption.Text.Length > 34 ? 24 : 30;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private static string Clock(long ms)
    {
        var total = (int)(ms / 1000);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    }

    private static Label AddLabel(Control parent, Font font, int size, HorizontalAlignment align, Vector2 position, Vector2 box)
    {
        var label = new Label
        {
            Position = position,
            Size = box,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = Ink },
        };
        parent.AddChild(label);
        return label;
    }
}
