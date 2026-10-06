using Godot;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The whole cabinet, assembled from a skin: base art, live screen, glass, glows, lights, coins, printer, memos, CHECK button.
/// Knows nothing about rules; the app tells it what to show and plays its moments.
/// </summary>
public partial class CabinetView : Node2D
{
    public event Action? CheckPressed;

    public CabinetSkin Skin { get; private set; } = null!;
    public ScreenView Screen { get; private set; } = null!;
    public MarqueeLights Lights { get; private set; } = null!;
    public CoinTray Tray { get; private set; } = null!;
    public Printer Printer { get; private set; } = null!;
    public Receipt Receipt { get; private set; } = null!;
    public MemoBoard Memos { get; private set; } = null!;

    private Sprite2D _checkCap = null!, _screenGlow = null!, _slotGlow = null!, _marqueeGlow = null!;
    private Tween? _checkTween;

    public void Build(CabinetSkin skin)
    {
        Skin = skin;
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

        AddLayer("base");
        Screen = new ScreenView();
        AddChild(Screen);
        Screen.Build(skin.Anchors.ScreenQuad, skin.Anchors.ScreenSize);
        AddLayer("screen-glass");
        _screenGlow = AddLayer("screen-glow", add);
        _marqueeGlow = AddLayer("marquee-glow", add);
        _slotGlow = AddLayer("slot-glow", add);
        _checkCap = AddLayer("check-cap");

        Lights = new MarqueeLights();
        AddChild(Lights);
        Lights.Build(skin.Anchors.Bulbs);
        Tray = new CoinTray();
        AddChild(Tray);
        Tray.Build(skin);
        Memos = new MemoBoard();                         // under the IOU ticket and the receipt, which come out of the same slot
        AddChild(Memos);
        Memos.Build(skin);
        Printer = new Printer();
        AddChild(Printer);
        Printer.Build(skin);
        Receipt = new Receipt();
        AddChild(Receipt);
        Receipt.Build(skin);

        _screenGlow.Modulate = new Color(1, 1, 1, 0.8f);
        _slotGlow.Modulate = new Color(1, 1, 1, 0.55f);
        Breathe(_marqueeGlow, 0.55f, 1f, 2.4);
    }

    /// <summary>True when the canvas point is on the CHECK button.</summary>
    public bool HitsCheck(Vector2 canvasPoint) => Geometry2D.IsPointInPolygon(canvasPoint, Skin.Anchors.CheckButton);

    public void PressCheck()
    {
        _checkTween?.Kill();
        _checkCap.Position = Skin.Anchors.CheckPress;
        _checkTween = CreateTween();
        _checkTween.TweenInterval(0.12);
        _checkTween.TweenProperty(_checkCap, "position", Vector2.Zero, 0.12);
        CheckPressed?.Invoke();
    }

    public void HoverCheck(bool hovering) => _checkCap.Modulate = hovering ? new Color(1.15f, 1.15f, 1.15f) : Colors.White;

    /// <summary>The coin slot lights up as a token goes in.</summary>
    public void FlashSlot()
    {
        var tween = CreateTween();
        tween.TweenProperty(_slotGlow, "modulate:a", 1.6f, 0.08);
        tween.TweenProperty(_slotGlow, "modulate:a", 0.55f, 0.6);
    }

    /// <summary>Screen power: dims while paused/idle, full while playing.</summary>
    public void SetScreenPower(float power)
    {
        Screen.SetBrightness(Mathf.Lerp(0.75f, 1f, power));
        _screenGlow.Modulate = new Color(1, 1, 1, Mathf.Lerp(0.35f, 0.9f, power));
    }

    private Sprite2D AddLayer(string name, Material? material = null)
    {
        var sprite = new Sprite2D
        {
            Texture = Skin.Layers[name],
            Centered = false,
            Scale = Vector2.One / Skin.Scale,
            Material = material,
        };
        AddChild(sprite);
        return sprite;
    }

    private void Breathe(CanvasItem item, float low, float high, double period)
    {
        var tween = CreateTween().SetLoops();
        tween.TweenProperty(item, "modulate:a", low, period / 2).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(item, "modulate:a", high, period / 2).SetTrans(Tween.TransitionType.Sine);
    }
}
