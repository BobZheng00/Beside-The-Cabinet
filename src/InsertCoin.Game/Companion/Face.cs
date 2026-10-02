using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Facial expressions and blinking. Expressions are the VRM's own (happy, sad, surprised, blink…): the importer
/// stores each as a one-key animation over blend shapes, read here once and then driven directly.
/// </summary>
internal sealed class Face
{
    private readonly Dictionary<string, (MeshInstance3D Mesh, int Shape, float Value)[]> _expressions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _weight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _target = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _until = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(MeshInstance3D, int), float> _shapes = [];
    private readonly RandomNumberGenerator _random = new();
    private double _time, _nextBlink = 2, _blinkStarted = -1;

    /// <summary>Eyes held shut (dozing).</summary>
    public bool EyesClosed { get; set; }

    public Face(Node3D character)
    {
        var player = character.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        if (player is null)
            return;
        var root = player.GetNode(player.RootNode);
        foreach (var name in player.GetAnimationList())
        {
            var animation = player.GetAnimation(name);
            var parts = new List<(MeshInstance3D, int, float)>();
            for (var t = 0; t < animation.GetTrackCount(); t++)
            {
                if (animation.TrackGetType(t) != Animation.TrackType.BlendShape || animation.TrackGetKeyCount(t) == 0)
                    continue;
                var path = animation.TrackGetPath(t);
                if (root.GetNodeOrNull(path.GetConcatenatedNames()) is not MeshInstance3D mesh)
                    continue;
                var shape = mesh.FindBlendShapeByName(path.GetConcatenatedSubNames());
                var value = animation.TrackGetKeyValue(t, 0).AsSingle();
                if (shape >= 0 && value > 0)
                    parts.Add((mesh, shape, value));
            }
            if (parts.Count > 0)
                _expressions[name] = [.. parts];
        }
    }

    /// <summary>Shows an expression for a while, then lets it fade.</summary>
    public void Show(string expression, float weight, double seconds)
    {
        _target[expression] = weight;
        _until[expression] = _time + seconds;
    }

    public void Update(double delta)
    {
        _time += delta;
        foreach (var name in _until.Where(u => _time > u.Value).Select(u => u.Key).ToList())
        {
            _target[name] = 0;
            _until.Remove(name);
        }
        foreach (var name in _target.Keys)
        {
            var now = _weight.GetValueOrDefault(name);
            _weight[name] = now + (_target[name] - now) * (1 - Mathf.Exp(-(float)delta * 9f));
        }
        _weight["blink"] = Blink();

        foreach (var key in _shapes.Keys.ToList())
            _shapes[key] = 0;
        foreach (var (name, weight) in _weight)
            if (weight > 0.001f && _expressions.TryGetValue(name, out var parts))
                foreach (var (mesh, shape, value) in parts)
                    _shapes[(mesh, shape)] = Mathf.Max(_shapes.GetValueOrDefault((mesh, shape)), weight * value);
        foreach (var ((mesh, shape), value) in _shapes)
            mesh.SetBlendShapeValue(shape, value);
    }

    private float Blink()
    {
        if (EyesClosed)
            return 1;
        if (_weight.GetValueOrDefault("happy") > 0.4f)       // a smile already narrows the eyes
            return 0;
        if (_blinkStarted < 0 && _time >= _nextBlink)
            _blinkStarted = _time;
        if (_blinkStarted < 0)
            return 0;
        var t = _time - _blinkStarted;
        const double close = 0.07, open = 0.13;
        if (t >= close + open)
        {
            _blinkStarted = -1;
            _nextBlink = _time + (_random.Randf() < 0.18f ? 0.25 : _random.RandfRange(2.2f, 5.5f));   // sometimes a double blink
            return 0;
        }
        return t < close ? (float)(t / close) : 1 - (float)((t - close) / open);
    }
}
