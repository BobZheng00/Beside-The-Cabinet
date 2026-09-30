using Godot;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Cabinet;

public enum CoinKind { Bronze, Silver, Gold, Star }

/// <summary>The recessed coin tray. Shows one coin per credit (up to the tray's capacity), dropping new ones in.</summary>
public partial class CoinTray : Node2D
{
    private const int Levels = 4;
    private readonly List<(Sprite2D Sprite, CoinKind Kind)> _coins = [];
    private IReadOnlyDictionary<CoinKind, SkinSprite> _sprites = null!;
    private SkinAnchors _anchors = null!;
    private float _textureScale;
    private Node2D _clip = null!;
    private ShaderMaterial _clipMaterial = null!;

    public int Capacity => _anchors.TraySpots.Length * Levels;

    public void Build(CabinetSkin skin)
    {
        _anchors = skin.Anchors;
        _textureScale = 1f / skin.Scale;
        _sprites = new Dictionary<CoinKind, SkinSprite>
        {
            [CoinKind.Bronze] = skin.Sprites["coin-bronze"],
            [CoinKind.Silver] = skin.Sprites["coin-silver"],
            [CoinKind.Gold] = skin.Sprites["coin-gold"],
            [CoinKind.Star] = skin.Sprites["coin-star"],
        };
        // Coins are clipped to the tray opening (by shader), so they fall "into" the recess.
        _clipMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Cabinet/quad_clip.gdshader") };
        for (var i = 0; i < 4; i++)
            _clipMaterial.SetShaderParameter($"q{i}", _anchors.TrayOpening[i]);
        _clip = new Node2D();
        AddChild(_clip);
    }

    /// <summary>Shows exactly <paramref name="count"/> coins without animation (startup, weekly reset).</summary>
    public void Set(int count, CoinKind kind = CoinKind.Gold)
    {
        while (_coins.Count > Math.Min(count, Capacity))
            RemoveTop(animate: false);
        while (_coins.Count < Math.Min(count, Capacity))
            Place(kind, animate: false);
    }

    /// <summary>Drops coins in one after another.</summary>
    public void Drop(IReadOnlyList<CoinKind> kinds)
    {
        for (var i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            GetTree().CreateTimer(0.18 * i).Timeout += () => { if (_coins.Count < Capacity) Place(kind, animate: true); };
        }
    }

    /// <summary>Takes the top coin away (it went into the slot).</summary>
    public void TakeOne() => RemoveTop(animate: true);

    private void Place(CoinKind kind, bool animate)
    {
        var index = _coins.Count;
        var spot = _anchors.TraySpots[index % _anchors.TraySpots.Length];
        var level = index / _anchors.TraySpots.Length;
        var front = index % _anchors.TraySpots.Length < 4;
        var target = spot + _anchors.TrayStack * level;
        var art = _sprites[kind];
        var sprite = new Sprite2D
        {
            Texture = art.Texture,
            Centered = false,
            Offset = -art.Origin / _textureScale,
            Scale = Vector2.One * _textureScale,
            Position = animate ? _anchors.TrayChute : target,
            ZIndex = level * 2 + (front ? 1 : 0),
            Material = _clipMaterial,
        };
        _clip.AddChild(sprite);
        _coins.Add((sprite, kind));
        if (!animate)
            return;
        var tween = CreateTween();
        tween.TweenProperty(sprite, "position", target, 0.55).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
    }

    private void RemoveTop(bool animate)
    {
        if (_coins.Count == 0)
            return;
        var (sprite, _) = _coins[^1];
        _coins.RemoveAt(_coins.Count - 1);
        if (!animate)
        {
            sprite.QueueFree();
            return;
        }
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(sprite, "position", sprite.Position + new Vector2(0, -26), 0.35).SetEase(Tween.EaseType.In);
        tween.TweenProperty(sprite, "modulate:a", 0f, 0.35);
        tween.Chain().TweenCallback(Callable.From(sprite.QueueFree));
    }
}
