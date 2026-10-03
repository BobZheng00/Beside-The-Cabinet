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
    private readonly Dictionary<string, float> _resting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _until = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(MeshInstance3D, int), float> _shapes = [];
    private readonly RandomNumberGenerator _random = new();
    private double _time, _nextBlink = 2, _blinkStarted = -1;
    private float _blinkDepth = 1, _blinkPace = 1;

    /// <summary>Eyes held shut (dozing, a long stretch).</summary>
    public bool EyesClosed { get; set; }

    /// <summary>How far her mouth is open while she speaks aloud (0–1), on top of any expression's.</summary>
    public float Mouth { get; set; }

    // What blend shapes tend to be called in models that are not VRM (ARKit, VRChat, hand-named).
    private static readonly (string Expression, string[] Words)[] ShapeWords =
    [
        ("blink", ["blink", "eyesclosed", "eyeclose", "eyesclose"]),
        ("happy", ["smile", "happy", "joy"]),
        ("sad", ["sad", "sorrow"]),
        ("surprised", ["surprise"]),
        ("aa", ["jawopen", "mouthopen", "vrc.v_aa"]),
    ];

    public Face(Node3D character)
    {
        ReadVrmExpressions(character);
        if (_expressions.Count == 0)
            GuessExpressions(character);
    }

    private void GuessExpressions(Node3D character)
    {
        foreach (var mesh in character.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            for (var shape = 0; shape < mesh.GetBlendShapeCount(); shape++)
            {
                var name = (mesh.Mesh as ArrayMesh)?.GetBlendShapeName(shape).ToString().ToLowerInvariant() ?? "";
                foreach (var (expression, words) in ShapeWords)
                    if (words.Any(name.Contains))
                        _expressions[expression] = [.. _expressions.GetValueOrDefault(expression, []), (mesh, shape, 1f)];
            }
    }

    private void ReadVrmExpressions(Node3D character)
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

    /// <summary>The expression she wears when nothing else is asked for.</summary>
    public void Rest(string expression, float weight)
    {
        _resting[expression] = weight;
        _target.TryAdd(expression, 0);
    }

    /// <summary>Blinks now (people often blink as their gaze jumps).</summary>
    public void Blink()
    {
        if (_blinkStarted < 0)
            StartBlink();
    }

    public void Update(double delta)
    {
        _time += delta;
        foreach (var name in _until.Where(u => _time > u.Value).Select(u => u.Key).ToList())
        {
            _target[name] = 0;
            _until.Remove(name);
        }
        var shown = _target.Where(t => !_resting.ContainsKey(t.Key)).Sum(t => t.Value);
        foreach (var name in _target.Keys)
        {
            // the resting expression gives way while another one is showing
            var goal = Mathf.Max(_target[name], _resting.GetValueOrDefault(name) * Mathf.Clamp(1 - shown, 0, 1));
            var now = _weight.GetValueOrDefault(name);
            _weight[name] = now + (goal - now) * (1 - Mathf.Exp(-(float)delta * 9f));
        }
        _weight["blink"] = BlinkValue();
        if (Mouth > 0.001f || _weight.ContainsKey("aa"))
            _weight["aa"] = Mathf.Max(_target.GetValueOrDefault("aa") > 0 ? _weight.GetValueOrDefault("aa") : 0, Mouth);

        foreach (var key in _shapes.Keys.ToList())
            _shapes[key] = 0;
        foreach (var (name, weight) in _weight)
            if (weight > 0.001f && _expressions.TryGetValue(name, out var parts))
                foreach (var (mesh, shape, value) in parts)
                    _shapes[(mesh, shape)] = Mathf.Max(_shapes.GetValueOrDefault((mesh, shape)), weight * value);
        foreach (var ((mesh, shape), value) in _shapes)
            mesh.SetBlendShapeValue(shape, value);
    }

    private void StartBlink()
    {
        _blinkStarted = _time;
        _blinkDepth = _random.Randf() < 0.1f ? 0.55f : 1f;          // now and then only half a blink
        _blinkPace = _random.RandfRange(0.85f, 1.15f);
    }

    private float BlinkValue()
    {
        if (EyesClosed)
            return 1;
        if (_weight.GetValueOrDefault("happy") > 0.4f)       // a smile already narrows the eyes
            return 0;
        if (_blinkStarted < 0 && _time >= _nextBlink)
            StartBlink();
        if (_blinkStarted < 0)
            return 0;

        // lids snap shut, rest a moment, open more slowly
        double close = 0.07 * _blinkPace, hold = 0.035 * _blinkPace, open = 0.16 * _blinkPace;
        var t = _time - _blinkStarted;
        if (t >= close + hold + open)
        {
            _blinkStarted = -1;
            // gaps between blinks are skewed: mostly 2-5 s, sometimes long; one in eight is a double blink
            var gap = _random.Randf() < 0.12f ? _random.RandfRange(0.12f, 0.3f) : Mathf.Clamp(Mathf.Exp(_random.Randfn(1.15f, 0.45f)), 1.2f, 8f);
            _nextBlink = _time + gap;
            return 0;
        }
        var lid = t < close ? (float)(t / close) : t < close + hold ? 1f : 1 - (float)((t - close - hold) / open);
        return _blinkDepth * Mathf.SmoothStep(0, 1, lid);
    }
}
