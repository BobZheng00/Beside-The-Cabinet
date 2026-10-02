using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>Things the companion reacts to. The app decides when; the puppet decides how.</summary>
public enum Moment { Cheer, Pump, Greet, Glance, Alarm, Worry, Doze, Wake }

/// <summary>
/// The companion's body language. Three layers, in this order every frame:
/// poses (seated, plus reaction poses switched on and off by short timelines), springs (every joint follows its
/// pose like a damped spring, so moves accelerate, overshoot and settle, the hand arriving after the arm), and
/// life (breathing, postural sway, a restless head, fidgets, eyes that jump and hold) added on top.
/// </summary>
internal sealed class Puppet
{
    /// <summary>Where a hand should be: wrist position (metres from the seat centre), finger direction, the way
    /// the palm faces, and the side the elbow sticks out to.</summary>
    private readonly record struct Hand(Vector3 Wrist, Vector3 Fingers, Vector3 Palm, Vector3 Elbow)
    {
        public Hand Mirrored => new(Flip(Wrist), Flip(Fingers), Flip(Palm), Flip(Elbow));
        public Hand Sized(float scale) => this with { Wrist = Wrist * scale };
        public Hand Towards(Hand other, float weight) => new(Wrist.Lerp(other.Wrist, weight), Fingers.Normalized().Lerp(other.Fingers.Normalized(), weight),
            Palm.Normalized().Lerp(other.Palm.Normalized(), weight), Elbow.Normalized().Lerp(other.Elbow.Normalized(), weight));
        private static Vector3 Flip(Vector3 v) => new(-v.X, v.Y, v.Z);
    }

    /// <summary>A pose laid over the seated one: body rotations, optional hand placements, finger curl (degrees).</summary>
    private sealed record Overlay(Pose Body, Hand? Left = null, Hand? Right = null, float Curl = -1);

    // ---- Poses (degrees; x = pitch forward, y = turn to her left, z = roll; left side given, right mirrored) -----
    private static readonly Pose Sit = new Pose()
        .Set("Hips", 4, 0, 0).Set("Spine", 4, 0, 0).Set("Chest", 2, 0, 0).Set("UpperChest", 0, 0, 0)
        .Set("Neck", -3, 0, 0).Set("Head", -4, 0, 0)
        .Both("UpperLeg", -(90 - ThighSlope), -2, 0).Both("LowerLeg", 114, 0, 0).Both("Foot", 30, 0, 0)   // knee bend is refitted per character
        .Both("Shoulder", 0, 0, -2);

    // Hands at rest in her lap, the left lying on the right.
    private static readonly Hand LeftRest = new(new(0.030f, 0.165f, 0.135f), new(-0.80f, -0.05f, 0.60f), new(0, -1, 0.1f), new(0.7f, -0.3f, -0.6f));
    private static readonly Hand RightRest = new(new(-0.055f, 0.148f, 0.125f), new(0.80f, -0.05f, 0.60f), new(0, -1, 0.1f), new(-0.7f, -0.3f, -0.6f));

    private static readonly Hand FistAtChest = new(new(0.12f, 0.40f, 0.22f), new(0, 1, 0.2f), new(-1, 0, 0), new(0.5f, -1, -0.5f));

    private static readonly Dictionary<string, Overlay> Overlays = new()
    {
        // wind-up before a cheer: a quick crouch with fists drawn in
        ["crouch"] = new(new Pose().Set("Spine", 7, 0, 0).Set("Chest", 4, 0, 0).Set("Head", 5, 0, 0).Both("Shoulder", 0, 0, -6),
            FistAtChest, FistAtChest.Mirrored, Curl: 70),
        // both arms thrown up, a little uneven
        ["cheer"] = new(new Pose().Set("Spine", -4, 0, 0).Set("Chest", -5, 0, 0).Set("Neck", -2, 0, 0).Set("Head", -7, 0, 4).Both("Shoulder", 0, 0, 12),
            new(new(0.268f, 0.836f, 0.09f), new(0.3f, 1, 0), new(0, 0, 1), new(1, -0.2f, -0.3f)),
            new(new(-0.240f, 0.845f, 0.11f), new(-0.25f, 1, 0), new(0, 0, 1), new(-1, -0.2f, -0.3f)), Curl: 3),
        // "yes!": fist up beside the head, then pulled down
        ["pump"] = new(new Pose().Set("Spine", -3, 0, 0).Set("Chest", -3, 0, 0).Set("Head", -6, 0, 0).Set("RightShoulder", 0, 0, -8),
            Right: new(new(-0.20f, 0.66f, 0.20f), new(0, 1, 0), new(1, 0, 0), new(-0.4f, -1, -0.1f)), Curl: 75),
        ["yes"] = new(new Pose().Set("Spine", 6, 0, 0).Set("Chest", 4, 0, 0).Set("Head", 3, 0, 0).Set("RightShoulder", 0, 0, 4),
            Right: new(new(-0.17f, 0.42f, 0.19f), new(0, 1, 0.1f), new(1, 0, 0), new(-0.5f, -1, -0.6f)), Curl: 75),
        ["wave"] = new(new Pose().Set("Spine", 0, 0, 2).Set("Head", -3, 0, 6).Set("RightShoulder", 0, 0, -8),
            Right: new(new(-0.34f, 0.70f, 0.12f), new(-0.1f, 1, 0), new(0.5f, 0, 1), new(-0.5f, -1, -0.2f)), Curl: 3),
        ["worry"] = new(new Pose().Set("Spine", 6, 0, 0).Set("Chest", 4, 0, 0).Set("Neck", 4, 0, 0).Set("Head", 5, 0, 4).Both("Shoulder", 0, 0, -5)),
        ["doze"] = new(new Pose().Set("Spine", 3, 0, 2).Set("Chest", 2, 0, 2).Set("Neck", 4, 0, 5).Set("Head", 7, 0, 11)),
        // idle fidgets
        ["tiltLeft"] = new(new Pose().Set("Neck", -3, 0, -3).Set("Head", -4, 0, -9)),
        ["tiltRight"] = new(new Pose().Set("Neck", -3, 0, 3).Set("Head", -4, 0, 9)),
        ["shift"] = new(new Pose().Set("Spine", 4, 0, 5).Set("Chest", 2, 0, -3).Set("Head", -4, 0, -3)),
        // a deep breath in: chest and shoulders up, let go again by the springs
        ["sigh"] = new(new Pose().Set("Spine", 1, 0, 0).Set("Chest", -3, 0, 0).Set("Neck", -4, 0, 0).Set("Head", -6, 0, 0).Both("Shoulder", 0, 0, 7)),
    };

    private static readonly string[] Gestures = ["crouch", "cheer", "pump", "yes", "wave", "tiltLeft", "tiltRight", "shift", "sigh"];
    private static readonly string[] Sides = ["Left", "Right"];
    private static readonly string[] Fingers = ["Index", "Middle", "Ring", "Little"];
    private static readonly string[] Joints = ["Proximal", "Intermediate", "Distal"];

    // Hand placements and the hip position below are written for one figure and scaled to the character at hand
    // (see Rig.Size).
    private const float HipJointHeight = 0.045f, HipJointBack = -0.04f;   // hip joints above / behind the seat centre
    private const float ThighSlope = 5;                            // degrees below level
    private const float RestCurl = 11;

    // ---- State ---------------------------------------------------------------------------------------------------
    private readonly Rig _rig;
    private readonly Face _face;
    private readonly Pose _sit;
    private readonly float _size;
    private readonly Vector3 _seat;                       // where the hips bone goes, from the seat centre
    private readonly bool _dangling;                      // her feet do not reach the stool's ring
    private readonly Dictionary<string, Quaternion> _goal = [], _frame = [];
    private readonly Dictionary<string, RotationSpring> _springs = [];
    private readonly Dictionary<string, float> _on = [], _shown = [], _shownSpeed = [];
    private readonly Dictionary<string, float> _curl = new() { ["Left"] = RestCurl, ["Right"] = RestCurl };
    private readonly List<(double At, Action Do)> _cues = [];
    private readonly RandomNumberGenerator _random = new();

    private double _time, _busyUntil, _gazeUntil, _nextWander = 7, _nextFidget = 5, _nextSaccade;
    private double _cheerAt = -100, _waveAt = -100, _kickAt = -100, _swingAt = -100, _humAt = -100;
    private string _kickSide = "Left";
    private Vector3 _gaze;
    private Vector2 _eye, _jitter;
    private float _headYaw, _headPitch, _breath, _awake = 1;

    /// <summary>Points she can look at, in the skeleton's space.</summary>
    public Vector3 Viewer { get; set; }
    public Vector3 Screen { get; set; }
    /// <summary>Where the mouse pointer is, when it moved recently.</summary>
    public Vector3? Pointer { get; set; }

    /// <summary>Where her head is, in the skeleton's space.</summary>
    public Vector3 Head => _rig.PositionOf("Head");

    /// <param name="footRestDrop">How far below the seat the stool's foot ring is, metres.</param>
    public Puppet(Rig rig, Face face, float footRestDrop)
    {
        _rig = rig;
        _face = face;
        _size = rig.Size;

        // Bend the knees so the toes reach the foot ring, whatever the length of her legs.
        var thigh = rig.RestOffset("LeftUpperLeg", "LeftLowerLeg").Length();
        var shin = rig.RestOffset("LeftLowerLeg", "LeftFoot").Length();
        var knee = HipJointHeight * _size - thigh * Mathf.Sin(Mathf.DegToRad(ThighSlope));
        var toes = 0.34f * shin;                                    // how far below the ankle pointed toes reach
        var reach = (knee + footRestDrop - toes) / shin;
        _dangling = reach > 1;                                      // legs too short for the ring: they hang, and swing
        var back = _dangling ? 9 : Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(reach, 0.6f, 1f)));
        _sit = new Pose();
        foreach (var (bone, degrees) in Sit)
            _sit[bone] = degrees;
        _sit.Both("LowerLeg", 90 - ThighSlope + back, 0, 0);
        // She is seated by her hip joints; where the hips bone sits relative to them differs from rig to rig.
        var fromJoints = rig.RestOffset("LeftUpperLeg", "Hips");
        _seat = new Vector3(0, HipJointHeight * _size + fromJoints.Y, HipJointBack * _size + fromJoints.Z);

        foreach (var name in Overlays.Keys)
            _on[name] = _shown[name] = _shownSpeed[name] = 0;
        _face.Rest("relaxed", 0.1f);        // the hint of a smile; more and her eyes look sleepy
    }

    // ---- Reactions -----------------------------------------------------------------------------------------------

    public void Play(Moment moment)
    {
        switch (moment)
        {
            case Moment.Cheer:
                Interrupt(2.7);
                _on["crouch"] = 1;                                   // anticipation: down before up
                After(0.17, () => { _on["crouch"] = 0; _on["cheer"] = 1; _cheerAt = _time; _face.Show("happy", 1, 2.3); });
                After(1.95, () => _on["cheer"] = 0);
                Look(Viewer, 2.8);
                break;
            case Moment.Pump:
                Interrupt(1.9);
                _on["pump"] = 1;
                _face.Show("happy", 1, 1.7);
                After(0.32, () => { _on["pump"] = 0; _on["yes"] = 1; });
                After(1.0, () => _on["yes"] = 0);
                Look(Viewer, 2.2);
                break;
            case Moment.Greet:
                Interrupt(2.3);
                _on["wave"] = 1;
                _waveAt = _time;
                _face.Show("happy", 0.9f, 2.3);
                After(1.9, () => _on["wave"] = 0);
                Look(Viewer, 3);
                break;
            case Moment.Glance:
                Look(Screen, 2.5);
                break;
            case Moment.Alarm:
                Interrupt(2);
                Look(Screen, 3.5);
                _face.Show("surprised", 0.8f, 1.6);
                break;
            case Moment.Worry:
                Interrupt(5);
                _on["worry"] = 1;
                After(5, () => _on["worry"] = 0);
                _face.Show("sad", 0.9f, 5);
                Look(Screen, 2.5);
                break;
            case Moment.Doze:
                Interrupt(0);
                _on["doze"] = 1;
                _face.EyesClosed = true;
                break;
            case Moment.Wake:
                _on["doze"] = 0;
                _face.EyesClosed = false;
                _face.Show("surprised", 0.5f, 0.8);
                Look(Viewer, 2);
                break;
        }
    }

    /// <summary>Drops whatever gesture is in progress and keeps fidgets away for a while.</summary>
    private void Interrupt(double busySeconds)
    {
        _cues.Clear();
        foreach (var name in Gestures)
            _on[name] = 0;
        _on["worry"] = 0;
        if (_on["doze"] == 0)
            _face.EyesClosed = false;
        _busyUntil = _time + busySeconds;
    }

    private void After(double seconds, Action action) => _cues.Add((_time + seconds, action));

    // ---- Frame ---------------------------------------------------------------------------------------------------

    public void Update(double delta)
    {
        _time += delta;
        var dt = Mathf.Min((float)delta, 0.1f);          // the energy-saving pace is 15 frames a second
        var t = (float)_time;
        foreach (var cue in _cues.Where(c => _time >= c.At).ToList())
        {
            _cues.Remove(cue);
            cue.Do();
        }
        _awake += (1 - _on["doze"] - _awake) * (1 - Mathf.Exp(-dt * 3f));
        // Poses come in and go out smoothly (a critically damped ease of about a quarter second) ...
        const float rate = Mathf.Tau * 2.6f;
        foreach (var name in Overlays.Keys)
        {
            _shownSpeed[name] += dt * (rate * rate * (_on[name] - _shown[name]) - 2 * rate * _shownSpeed[name]);
            _shown[name] = Mathf.Clamp(_shown[name] + dt * _shownSpeed[name], 0, 1);
        }
        Fidget();

        // 1. Poses: seated, reaction poses over it, head and torso turned to what she is looking at.
        _goal.Clear();
        foreach (var (bone, degrees) in _sit)
            _goal[bone] = Rig.Euler(degrees);
        foreach (var (name, overlay) in Overlays)
            if (_shown[name] > 0.001f)
                foreach (var (bone, degrees) in overlay.Body)
                    _goal[bone] = _goal.GetValueOrDefault(bone, Quaternion.Identity).Slerp(Rig.Euler(degrees), _shown[name]);
        Gaze(dt, t);

        // 2. Springs: ... and each joint then chases its pose with some give.
        _frame.Clear();
        foreach (var (bone, rotation) in _goal)
            _frame[bone] = Spring(bone).Follow(rotation, dt);

        // 3. Life on top of the settled pose.
        var lift = Breathe(dt);
        Sway(t);
        Legs(t);
        var hips = _seat + new Vector3(0, 0.002f * lift + Bounce(), 0);

        // Arms last: they hang off the finished torso, so the hands stay in her lap while she breathes.
        foreach (var side in Sides)
            Arm(side, hips, dt);
        Wave(t);
        Eyes(dt);

        _rig.PlaceHips(hips);
        foreach (var (bone, rotation) in _frame)
            _rig.Rotate(bone, rotation);
        _face.Update(delta);
    }

    private RotationSpring Spring(string bone)
    {
        if (_springs.TryGetValue(bone, out var spring))
            return spring;
        // (frequency Hz, damping): further down a limb is slower and looser, so it trails and swings past
        var (frequency, damping) = bone switch
        {
            _ when bone.EndsWith("Hand") => (2.6f, 0.40f),
            _ when bone.EndsWith("LowerArm") => (3.0f, 0.48f),
            _ when bone.EndsWith("UpperArm") => (3.4f, 0.58f),
            _ when bone.EndsWith("Shoulder") => (3.4f, 0.70f),
            "Head" or "Neck" => (2.2f, 0.80f),
            _ when bone.Contains("Leg") || bone.EndsWith("Foot") => (3.0f, 0.70f),
            _ => (2.4f, 0.80f),
        };
        return _springs[bone] = new RotationSpring(frequency, damping);
    }

    // ---- Looking -------------------------------------------------------------------------------------------------

    private void Gaze(float dt, float t)
    {
        // What to look at: a reaction's target, else the pointer while it moves, else mostly the viewer with
        // glances at the screen or off to the side.
        if (_time > _gazeUntil)
        {
            if (Pointer is { } pointer)
                _gaze = pointer;
            else if (_time > _nextWander && _awake > 0.9f)
            {
                _nextWander = _time + _random.RandfRange(7, 16);
                var away = Viewer + new Vector3(_random.RandfRange(-1.6f, 1.6f), _random.RandfRange(-0.7f, 0.5f), 0);
                Look(_random.Randf() < 0.55f ? Screen : away, _random.RandfRange(1.2f, 3.2f));
            }
            else
                Retarget(Viewer);
        }

        var chest = _rig.Total(_rig.Has("Chest") ? "Chest" : "Spine", _goal);
        var direction = (chest.Inverse() * (_gaze - Head)).Normalized();
        var yaw = Mathf.Clamp(Mathf.Atan2(direction.X, direction.Z), -1.2f, 1.2f) * _awake;
        var pitch = Mathf.Clamp(Mathf.Asin(direction.Y), -0.2f, 0.45f) * _awake;     // a bowed head hides her face
        // The head sets off a moment after the eyes (they are handled in Eyes) and only goes most of the way.
        var follow = 1 - Mathf.Exp(-dt * 9f);
        _headYaw += (yaw - _headYaw) * follow;
        _headPitch += (pitch - _headPitch) * follow;

        // A head is never still: slow drift on three axes with periods that never line up.
        var calm = (Pointer is null && _time > _busyUntil ? 1f : 0.45f) * _awake;
        var driftYaw = Mathf.DegToRad(3.6f) * Wobble(t, 6.5f, 11.3f, 0) * calm;
        var driftPitch = Mathf.DegToRad(2.0f) * Wobble(t, 3.9f, 7.1f, 2) * calm;
        var roll = 3.2f * Wobble(t, 5.5f, 9.7f, 4) * calm;

        Turn("Spine", _headYaw * 0.05f, 0, 0);
        Turn("Chest", _headYaw * 0.06f, 0, 0);
        Turn("UpperChest", _headYaw * 0.10f, 0, 0);
        Turn("Neck", _headYaw * 0.28f + driftYaw * 0.3f, _headPitch * 0.35f + driftPitch * 0.3f, roll * 0.3f);
        Turn("Head", _headYaw * 0.42f + driftYaw * 0.7f, _headPitch * 0.55f + driftPitch * 0.7f, roll * 0.7f);
    }

    /// <summary>
    /// Eyes go straight to the target and hold it while the head catches up or drifts, with small jumps between
    /// fixation points instead of a fixed stare.
    /// </summary>
    private void Eyes(float dt)
    {
        var want = _rig.Total("Head", _frame).Inverse() * (_gaze - Head).Normalized();
        var yaw = Mathf.Clamp(Mathf.Atan2(want.X, want.Z), -0.24f, 0.24f);
        var pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(want.Y, -1, 1)), -0.14f, 0.14f);
        if (_time > _nextSaccade)
        {
            _nextSaccade = _time + _random.RandfRange(0.5f, 2.2f);
            _jitter = new Vector2(_random.Randfn(0, 0.03f), _random.Randfn(0, 0.014f));
        }
        var target = (new Vector2(yaw, pitch) + _jitter) * _awake;
        _eye += (target - _eye) * (1 - Mathf.Exp(-dt * 45f));            // a jump takes a few hundredths of a second
        _frame["LeftEye"] = _frame["RightEye"] = new Quaternion(Vector3.Up, _eye.X) * new Quaternion(Vector3.Right, -_eye.Y);
    }

    private void Look(Vector3 point, double seconds)
    {
        Retarget(point);
        _gazeUntil = _time + seconds;
    }

    private void Retarget(Vector3 point)
    {
        // people tend to blink when their gaze jumps a long way
        if ((point - Head).Normalized().AngleTo((_gaze - Head).Normalized()) > 0.35f && _random.Randf() < 0.4f)
            _face.Blink();
        _gaze = point;
    }

    // ---- Life ----------------------------------------------------------------------------------------------------

    /// <summary>Breathing: in faster than out, the chest rises and the shoulders with it, the head stays level.</summary>
    private float Breathe(float dt)
    {
        _breath = (_breath + dt / Mathf.Lerp(5.2f, 3.7f, _awake)) % 1f;
        const float inhale = 0.42f;
        var wave = _breath < inhale ? -Mathf.Cos(Mathf.Pi * _breath / inhale) : Mathf.Cos(Mathf.Pi * (_breath - inhale) / (1 - inhale));
        var depth = Mathf.Lerp(1.5f, 1f, _awake);                       // asleep: slower and deeper
        var b = wave * depth;
        Add("Spine", -1.0f * b, 0, 0);
        Add("Chest", -1.7f * b, 0, 0);
        Add("UpperChest", -0.8f * b, 0, 0);
        Add("Neck", 1.7f * b, 0, 0);
        Add("Head", 1.6f * b, 0, 0);
        Add("LeftShoulder", 0, 0, 2.2f * b);
        Add("RightShoulder", 0, 0, -2.2f * b);
        var lift = (wave + 1) / 2 * depth;
        _rig.Shift("Spine", new Vector3(0, 0.003f * lift, 0));
        _rig.Shift("Chest", new Vector3(0, 0.004f * lift, 0.001f * lift));
        _rig.Shift("UpperChest", new Vector3(0, 0.002f * lift, 0.002f * lift));
        return lift;
    }

    /// <summary>Balance is never perfect: the torso leans and turns a little all the time; humming sways it more.</summary>
    private void Sway(float t)
    {
        var roll = 1.4f * Wobble(t, 9.1f, 4.3f, 1);
        Add("Spine", 0.5f * Wobble(t, 7.7f, 12.9f, 3), 1.1f * Wobble(t, 13.1f, 8.3f, 5), roll);
        Add("Chest", 0, 0, -0.6f * roll);

        var hum = (float)(_time - _humAt);
        if (hum is >= 0 and < 3.4f)
        {
            var swing = Envelope(hum, 0.5f, 2.7f, 3.4f) * Mathf.Sin(hum * Mathf.Tau * 0.85f);
            Add("Spine", 0, 0, 3.2f * swing);
            Add("Chest", 0, 0, 1.5f * swing);
            Add("Head", 0, 0, -3.5f * Envelope(hum, 0.5f, 2.7f, 3.4f) * Mathf.Sin(hum * Mathf.Tau * 0.85f - 0.6f));
        }
        var cheer = (float)(_time - _cheerAt) - 0.25f;
        if (cheer is >= 0 and < 1.9f)
        {
            // cheering rocks her from side to side in time with her legs
            var rock = Envelope(cheer, 0.3f, 1.3f, 1.9f) * Mathf.Sin(cheer * Mathf.Tau * 1.45f);
            Add("Spine", 0, 0, 4.5f * rock);
            Add("Chest", 0, 0, 2.5f * rock);
            Add("Head", 0, 0, -3.5f * rock);
        }
        if (_shown["doze"] > 0.001f)
        {
            // nodding off: the head sinks slowly, then jerks back up
            var nod = (float)(_time % 5.5) / 5.5f;
            Add("Head", 9 * (nod < 0.88f ? Mathf.SmoothStep(0, 1, nod / 0.88f) : 1 - Mathf.SmoothStep(0, 1, (nod - 0.88f) / 0.12f)) * (1 - _awake), 0, 0);
        }
    }

    /// <summary>Feet rest on the stool's ring; a heel bobs, and now and then a leg (or both) swings out and back.</summary>
    private void Legs(float t)
    {
        if (_dangling)
        {
            // legs that do not reach the ring swing gently, one after the other
            Add("LeftLowerLeg", 7 * Mathf.Sin(t * 1.5f) * _awake, 0, 0);
            Add("RightLowerLeg", 7 * Mathf.Sin(t * 1.5f + 2.4f) * _awake, 0, 0);
        }
        else
            Add("LeftFoot", 3.5f * Mathf.Sin(t * 1.9f) * _awake, 0, 0);

        var kick = (float)(_time - _kickAt);
        if (kick is >= 0 and < 2.6f)
        {
            var swing = Envelope(kick, 0.6f, 1.9f, 2.6f) * (0.5f - 0.5f * Mathf.Cos(kick / 2.6f * Mathf.Tau * 2));
            Add(_kickSide + "LowerLeg", -34 * swing, 0, 0);
            Add(_kickSide + "Foot", -14 * swing, 0, 0);
        }
        var both = (float)(_time - _swingAt);
        if (both is >= 0 and < 4.2f)
            Kicks(both, Envelope(both, 0.6f, 3.4f, 4.2f), 26, 0.75f);
        var cheer = (float)(_time - _cheerAt);
        if (cheer is >= 0 and < 2.2f)
            Kicks(cheer, Envelope(cheer, 0.15f, 1.5f, 2.2f), 30, 1.45f);
    }

    private void Kicks(float time, float strength, float degrees, float perSecond)
    {
        var left = 0.5f - 0.5f * Mathf.Cos(time * Mathf.Tau * perSecond);
        var right = 0.5f - 0.5f * Mathf.Cos(time * Mathf.Tau * perSecond + Mathf.Pi);
        Add("LeftLowerLeg", -degrees * left * strength, 0, 0);
        Add("RightLowerLeg", -degrees * right * strength, 0, 0);
        Add("LeftFoot", -12 * left * strength, 0, 0);
        Add("RightFoot", -12 * right * strength, 0, 0);
    }

    /// <summary>A cheer bounces her on the seat a few times.</summary>
    private float Bounce()
    {
        var cheer = (float)(_time - _cheerAt);
        return cheer is >= 0 and < 2 ? 0.02f * Mathf.Abs(Mathf.Sin(cheer * Mathf.Pi * 2.9f)) * Mathf.Exp(-cheer * 1.4f) : 0;
    }

    /// <summary>Small things people do when sitting around.</summary>
    private void Fidget()
    {
        if (_time < _nextFidget || _time < _busyUntil || _awake < 0.9f)
            return;
        _nextFidget = _time + _random.RandfRange(5.5f, 12);
        var pick = _random.Randf();
        if (pick < 0.22f)
        {
            _kickAt = _time;
            _kickSide = _random.Randf() < 0.5f ? "Left" : "Right";
        }
        else if (pick < 0.36f)
            _swingAt = _time;
        else if (pick < 0.58f)
        {
            var tilt = _random.Randf() < 0.5f ? "tiltLeft" : "tiltRight";
            _on[tilt] = 1;
            _face.Show("happy", 0.3f, 2.2);
            After(_random.RandfRange(1.6f, 3), () => _on[tilt] = 0);
        }
        else if (pick < 0.74f)
        {
            _on["shift"] = 1;                   // re-settling on the seat: a quick lean, then the springs wobble it out
            After(0.5, () => _on["shift"] = 0);
        }
        else if (pick < 0.88f)
            _humAt = _time;
        else
        {
            _on["sigh"] = 1;
            After(1.1, () => { _on["sigh"] = 0; _face.Blink(); });
        }
    }

    // ---- Arms ----------------------------------------------------------------------------------------------------

    /// <summary>Hands planted in her lap whatever the torso does, reaction poses blended over that, then sprung.</summary>
    private void Arm(string side, Vector3 hips, float dt)
    {
        var left = side == "Left";
        // The hand travels through space from one placement to the next, so the elbow stays bent on the way.
        var place = (left ? LeftRest : RightRest).Sized(_size);
        var curl = RestCurl;
        foreach (var (name, overlay) in Overlays)
        {
            if (_shown[name] <= 0.001f || (left ? overlay.Left : overlay.Right) is not { } target)
                continue;
            place = place.Towards(target.Sized(_size), _shown[name]);
            if (overlay.Curl >= 0)
                curl = Mathf.Lerp(curl, overlay.Curl, _shown[name]);
        }
        var (upper, lower, hand) = Reach(side, place, hips);
        _frame[side + "UpperArm"] = Spring(side + "UpperArm").Follow(upper, dt);
        _frame[side + "LowerArm"] = Spring(side + "LowerArm").Follow(lower, dt);
        _frame[side + "Hand"] = Spring(side + "Hand").Follow(hand, dt);

        // arms pump while cheering, out of step with each other
        var cheer = (float)(_time - _cheerAt) - 0.35f;
        if (cheer is >= 0 and < 1.6f)
            Add(side + "UpperArm", 0, 0, (left ? 1 : -1) * 8 * Mathf.Sin(cheer * Mathf.Tau * 2.9f + (left ? 0 : 0.7f)) * Mathf.Exp(-cheer * 1.5f));

        _curl[side] += (curl - _curl[side]) * (1 - Mathf.Exp(-dt * 14f));
        foreach (var finger in Fingers)
            foreach (var joint in Joints)
                _frame[side + finger + joint] = Rig.Euler(0, 0, (left ? -1 : 1) * _curl[side] * (joint == "Proximal" ? 0.75f : 1.05f));
    }

    /// <summary>Two-bone reach: bends the arm so the wrist lands on the target and lays the hand as asked.</summary>
    private (Quaternion Upper, Quaternion Lower, Quaternion Hand) Reach(string side, Hand hand, Vector3 hips)
    {
        string upper = side + "UpperArm", lower = side + "LowerArm", wrist = side + "Hand";
        var shoulder = _rig.Joint(upper, _frame, hips);
        var along = _rig.RestOffset(upper, lower);
        float a = along.Length(), b = _rig.RestOffset(lower, wrist).Length();
        var to = hand.Wrist - shoulder;
        var reach = Mathf.Clamp(to.Length(), 0.05f, a + b - 0.002f);
        var aim = to.Normalized();
        var cos = Mathf.Clamp((a * a + reach * reach - b * b) / (2 * a * reach), -1, 1);
        var bend = (hand.Elbow - aim * hand.Elbow.Dot(aim)).Normalized();
        var upperDir = aim * cos + bend * Mathf.Sqrt(1 - cos * cos);
        var lowerDir = (aim * reach - upperDir * a).Normalized();

        var upperTotal = new Quaternion(along.Normalized(), upperDir).Normalized();
        var lowerTotal = (new Quaternion(upperDir, lowerDir) * upperTotal).Normalized();
        var fingers = hand.Fingers.Normalized();
        var palm = (hand.Palm - fingers * hand.Palm.Dot(fingers)).Normalized();
        var x = side == "Left" ? fingers : -fingers;                 // at rest the left hand's fingers point along +x
        var handTotal = new Basis(x, -palm, x.Cross(-palm)).GetRotationQuaternion();

        return (_rig.ParentTotal(upper, _frame).Inverse() * upperTotal, upperTotal.Inverse() * lowerTotal, lowerTotal.Inverse() * handTotal);
    }

    /// <summary>
    /// The wave itself: the forearm swings from the elbow, the upper arm gives a little the other way, and the
    /// hand trails behind the forearm, so the wrist bends back and forth instead of the arm moving as one stick.
    /// </summary>
    private void Wave(float t)
    {
        var since = (float)(_time - _waveAt);
        if (since is < 0 or > 2.2f)
            return;
        var strength = Envelope(since, 0.3f, 1.45f, 1.95f);
        var towards = Viewer - Head;
        var axis = new Vector3(towards.X, 0, towards.Z).Normalized();   // swing across the viewer's line of sight
        var phase = since * Mathf.Tau * 2.3f;
        Swing("RightUpperArm", axis, 3.5f * Mathf.Sin(phase + 0.5f) * strength);
        Swing("RightLowerArm", axis, 15 * Mathf.Sin(phase) * strength);
        Swing("RightHand", axis, 17 * Mathf.Sin(phase - 1.2f) * strength);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------

    /// <summary>Rotates a bone about an axis given in the character's space, whatever its parents are doing.</summary>
    private void Swing(string bone, Vector3 axis, float degrees)
    {
        var parent = _rig.ParentTotal(bone, _frame);
        _frame[bone] = (parent.Inverse() * new Quaternion(axis, Mathf.DegToRad(degrees)) * parent * _frame[bone]).Normalized();
    }

    /// <summary>Layers a head-style turn (radians: yaw to her left, pitch up; roll in degrees) on a pose bone.</summary>
    private void Turn(string bone, float yaw, float pitch, float rollDegrees)
    {
        var turn = new Quaternion(Vector3.Up, yaw) * new Quaternion(Vector3.Right, -pitch) * new Quaternion(Vector3.Back, Mathf.DegToRad(rollDegrees));
        _goal[bone] = (turn * _goal.GetValueOrDefault(bone, Quaternion.Identity)).Normalized();
    }

    private void Add(string bone, float x, float y, float z) =>
        _frame[bone] = (Rig.Euler(x, y, z) * _frame.GetValueOrDefault(bone, Quaternion.Identity)).Normalized();

    /// <summary>Two sines with periods that never line up: smooth, and it does not visibly repeat.</summary>
    private static float Wobble(float t, float period, float otherPeriod, float phase) =>
        0.6f * Mathf.Sin(t * Mathf.Tau / period + phase) + 0.4f * Mathf.Sin(t * Mathf.Tau / otherPeriod + phase * 1.7f);

    /// <summary>0 → 1 over the attack, 1 until release, back to 0 at the end.</summary>
    private static float Envelope(float time, float attack, float release, float end) =>
        Mathf.SmoothStep(0, 1, time / attack) * (1 - Mathf.SmoothStep(0, 1, (time - release) / (end - release)));
}
