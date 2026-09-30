using Godot;

namespace InsertCoin.Game.Cabinet;

public enum LightMode { Chase, Celebrate, Alarm, Off }

/// <summary>The hood bulbs: a slow chase when idle, a fast flash on wins, a red blink when out of time.</summary>
public partial class MarqueeLights : Node2D
{
    private static readonly Color Warm = new("ffe27a");
    private static readonly Color Alarm = new("ff5a8a");
    private readonly List<Sprite2D> _bulbs = [];
    private Godot.Timer _timer = null!;
    private LightMode _mode = LightMode.Chase;
    private int _step;

    public void Build(IReadOnlyList<(Vector2 Centre, float Radius)> bulbs)
    {
        var glow = new GradientTexture2D
        {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
            Gradient = new Gradient { Colors = [Colors.White, new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0)], Offsets = [0f, 0.3f, 1f] },
        };
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        foreach (var (centre, radius) in bulbs)
        {
            var bulb = new Sprite2D { Texture = glow, Position = centre, Scale = Vector2.One * (radius * 3.2f / 64f), Material = add };
            _bulbs.Add(bulb);
            AddChild(bulb);
        }
        _timer = new Godot.Timer { Autostart = true };
        _timer.Timeout += Step;
        AddChild(_timer);
        SetMode(LightMode.Chase);
    }

    public void SetMode(LightMode mode)
    {
        _mode = mode;
        _timer.WaitTime = mode switch { LightMode.Celebrate => 0.09, LightMode.Alarm => 0.5, _ => 0.32 };
        Step();
    }

    /// <summary>Switches mode unless already there; with <paramref name="onlyFrom"/>, only when currently in that mode.</summary>
    public void SetModeIfNot(LightMode mode, LightMode? onlyFrom = null)
    {
        if (_mode == mode || (onlyFrom is { } from && _mode != from))
            return;
        SetMode(mode);
    }

    /// <summary>Plays a mode for a while, then returns to the previous one.</summary>
    public void Burst(LightMode mode, double seconds)
    {
        var previous = _mode;
        SetMode(mode);
        GetTree().CreateTimer(seconds).Timeout += () => { if (_mode == mode) SetMode(previous); };
    }

    private void Step()
    {
        _step++;
        for (var i = 0; i < _bulbs.Count; i++)
        {
            var (on, colour) = _mode switch
            {
                LightMode.Chase => ((i + _step) % 3 == 0, Warm),
                LightMode.Celebrate => ((i + _step) % 2 == 0, Warm),
                LightMode.Alarm => (_step % 2 == 0, Alarm),
                _ => (false, Warm),
            };
            _bulbs[i].Visible = on;
            _bulbs[i].Modulate = colour;
        }
    }
}
