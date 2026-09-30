using Godot;
using InsertCoin.Game.Skins;

namespace InsertCoin.Game.Cabinet;

/// <summary>
/// The IOU printer. While in debt a ticket sticks out of the slot, as long as the part of the debt that isn't a whole
/// ticket yet; each whole ticket prints out fully, tears off and falls. Paying debt stamps it and it retracts.
/// </summary>
public partial class Printer : Node2D
{
    private SkinSprite _art = null!;
    private float _textureScale;
    private Vector2 _slot;
    private Sprite2D _ticket = null!;
    private Label _stamp = null!;
    private float _shown;

    public void Build(CabinetSkin skin)
    {
        _art = skin.Sprites["ticket"];
        _textureScale = 1f / skin.Scale;
        _slot = skin.Anchors.TicketOrigin;
        _ticket = NewTicket();
        _stamp = new Label
        {
            Text = "PAID",
            Visible = false,
            Rotation = -0.35f,
            Position = _slot + new Vector2(-18, 12),
            LabelSettings = new LabelSettings { FontSize = 13, FontColor = new Color("c2185b"), OutlineSize = 4, OutlineColor = new Color("fff4e6") },
        };
        AddChild(_stamp);
        Reveal(_ticket, 0);
    }

    /// <summary>How much of the next ticket is out of the slot (0..1).</summary>
    public void SetPartial(float fraction, bool animate = false)
    {
        fraction = Mathf.Clamp(fraction, 0, 1);
        if (animate)
            CreateTween().TweenMethod(Callable.From<float>(f => Reveal(_ticket, f)), _shown, fraction, 0.4);
        else
            Reveal(_ticket, fraction);
    }

    /// <summary>A whole ticket prints out, tears off and drops away; the next one starts empty.</summary>
    public void PrintWhole()
    {
        var full = _ticket;
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(f => Reveal(full, f)), _shown, 1f, 0.6);
        tween.TweenInterval(0.25);
        tween.TweenCallback(Callable.From(() =>
        {
            _ticket = NewTicket();
            Reveal(_ticket, 0);
            var fall = full.CreateTween().SetParallel();
            fall.TweenProperty(full, "position", full.Position + new Vector2(8, 90), 0.7).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
            fall.TweenProperty(full, "rotation", 0.45f, 0.7);
            fall.TweenProperty(full, "modulate:a", 0f, 0.7);
            fall.Chain().TweenCallback(Callable.From(full.QueueFree));
        }));
    }

    /// <summary>Debt paid off: stamp PAID, then the ticket retracts.</summary>
    public void Paid()
    {
        if (_shown < 0.01f)
            return;
        _stamp.Visible = true;
        _stamp.Modulate = Colors.White;
        var tween = CreateTween();
        tween.TweenInterval(0.7);
        tween.TweenProperty(_stamp, "modulate:a", 0f, 0.35);
        tween.Parallel().TweenMethod(Callable.From<float>(f => Reveal(_ticket, f)), _shown, 0f, 0.35);
        tween.TweenCallback(Callable.From(() => _stamp.Visible = false));
    }

    private Sprite2D NewTicket()
    {
        var sprite = new Sprite2D
        {
            Texture = _art.Texture,
            Centered = false,
            RegionEnabled = true,
            Scale = Vector2.One * _textureScale,
            Position = _slot,
        };
        AddChild(sprite);
        MoveChild(sprite, 0);
        return sprite;
    }

    /// <summary>Shows the ticket from the slot row downwards; the texture above the slot stays hidden.</summary>
    private void Reveal(Sprite2D ticket, float fraction)
    {
        if (ticket == _ticket)
            _shown = fraction;
        var originPx = _art.Origin / _textureScale;
        var size = _art.Texture.GetSize();
        ticket.Visible = fraction > 0.01f;
        ticket.RegionRect = new Rect2(0, originPx.Y, size.X, (size.Y - originPx.Y) * fraction);
        ticket.Offset = new Vector2(-originPx.X, 0);
    }
}
