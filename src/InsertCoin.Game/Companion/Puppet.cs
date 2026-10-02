using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>Things the companion reacts to. The app decides when; the puppet decides how.</summary>
public enum Moment { Cheer, Greet, Glance, Alarm, Worry, Doze, Wake }

/// <summary>
/// The companion's body language: a seated base pose with her hands resting in her lap, short poses layered over
/// it for reactions, and the small continuous motions that make her look alive (breathing, a restless foot,
/// following things with her head and eyes).
/// </summary>
internal sealed class Puppet
{
    // ---- Poses (degrees; x = pitch forward, y = turn to her left, z = roll; left side given, right mirrored) -----
    private static readonly Pose Sit = new Pose()
        .Set("Hips", 4, 0, 0).Set("Spine", 4, 0, 0).Set("Chest", 2, 0, 0).Set("Neck", -3, 0, 0).Set("Head", -4, 0, 0)
        .Both("UpperLeg", -85, -2, 0).Both("LowerLeg", 114, 0, 0).Both("Foot", 30, 0, 0)
        .Both("Shoulder", 0, 0, -2);

    private static readonly Dictionary<string, Pose> Overlays = new()
    {
        ["cheer"] = new Pose().Set("Spine", -3, 0, 0).Set("Chest", -4, 0, 0).Set("Head", -10, 0, 0)
            .Both("Shoulder", 0, 0, 10).Both("UpperArm", -8, 0, 58).Both("LowerArm", 0, -22, 0).Both("Hand", 0, 0, 0),
        ["worry"] = new Pose().Set("Spine", 6, 0, 0).Set("Chest", 4, 0, 0).Set("Neck", 4, 0, 0).Set("Head", 5, 0, 4)
            .Both("Shoulder", 0, 0, -5),
        ["doze"] = new Pose().Set("Spine", 3, 0, 2).Set("Chest", 2, 0, 2).Set("Neck", 4, 0, 5).Set("Head", 7, 0, 11),
        ["wave"] = new Pose().Set("RightShoulder", 0, 0, -8).Set("RightUpperArm", -20, 0, -12).Set("RightLowerArm", 0, 0, -84).Set("RightHand", 0, 0, 0)
            .Set("Head", -4, 0, -5),
    };

    // Hands at rest: wrist position (metres from the seat centre), where the fingers point, which way the palm
    // faces, and which way the elbow sticks out. The left hand lies on the right one.
    private static readonly Hand LeftRest = new(new(0.030f, 0.165f, 0.135f), new(-0.80f, -0.05f, 0.60f), new(0, -1, 0.1f), new(0.7f, -0.3f, -0.6f));
    private static readonly Hand RightRest = new(new(-0.055f, 0.148f, 0.125f), new(0.80f, -0.05f, 0.60f), new(0, -1, 0.1f), new(-0.7f, -0.3f, -0.6f));

    private static readonly string[] Sides = ["Left", "Right"];
    private static readonly string[] ArmBones = ["UpperArm", "LowerArm", "Hand"];
    private static readonly string[] Fingers = ["Index", "Middle", "Ring", "Little"];
    private static readonly string[] Joints = ["Proximal", "Intermediate", "Distal"];

    private const float HipHeight = 0.085f, HipBack = -0.035f;     // hips above / behind the seat centre, metres
    private const float BreathSeconds = 3.8f;

    // ---- State ---------------------------------------------------------------------------------------------------
    private readonly Rig _rig;
    private readonly Face _face;
    private readonly Dictionary<string, Quaternion> _frame = [];
    private readonly Dictionary<string, float> _weight = [], _target = [];
    private readonly Dictionary<string, double> _until = [];
    private readonly RandomNumberGenerator _random = new();
    private double _time, _gazeUntil, _nextWander = 8, _nextKick = 6, _kickStarted = -10;
    private string _kickSide = "Left";
    private Vector3 _gaze;
    private float _yaw, _pitch;

    /// <summary>Points she can look at, in the skeleton's space.</summary>
    public Vector3 Viewer { get; set; }
    public Vector3 Screen { get; set; }
    /// <summary>Where the mouse pointer is, when it moved recently.</summary>
    public Vector3? Pointer { get; set; }

    /// <summary>Where her head is, in the skeleton's space.</summary>
    public Vector3 Head => _rig.PositionOf("Head");

    public Puppet(Rig rig, Face face)
    {
        _rig = rig;
        _face = face;
        foreach (var name in Overlays.Keys)
            _weight[name] = _target[name] = 0;
    }

    public void Play(Moment moment)
    {
        switch (moment)
        {
            case Moment.Cheer:
                Hold("cheer", 1.5);
                _face.Show("happy", 1, 2.4);
                LookAt(Viewer, 2.6);
                break;
            case Moment.Greet:
                Hold("wave", 1.7);
                _face.Show("happy", 0.9f, 2.2);
                LookAt(Viewer, 3);
                break;
            case Moment.Glance:
                LookAt(Screen, 2.5);
                break;
            case Moment.Alarm:
                LookAt(Screen, 3.5);
                _face.Show("surprised", 0.8f, 1.6);
                break;
            case Moment.Worry:
                Hold("worry", 5);
                _face.Show("sad", 0.9f, 5);
                LookAt(Screen, 2.5);
                break;
            case Moment.Doze:
                _target["doze"] = 1;
                _face.EyesClosed = true;
                break;
            case Moment.Wake:
                _target["doze"] = 0;
                _face.EyesClosed = false;
                _face.Show("surprised", 0.5f, 0.8);
                LookAt(Viewer, 2);
                break;
        }
    }

    public void Update(double delta)
    {
        _time += delta;
        var dt = (float)delta;
        var t = (float)_time;
        foreach (var name in _until.Where(u => _time > u.Value).Select(u => u.Key).ToList())
        {
            _target[name] = 0;
            _until.Remove(name);
        }
        foreach (var name in Overlays.Keys)
            _weight[name] += (_target[name] - _weight[name]) * (1 - Mathf.Exp(-dt * 7f));
        var awake = 1 - _weight["doze"];

        // Body: the seated pose with reaction poses blended over it (arms come later: they hang off the result).
        _frame.Clear();
        foreach (var (bone, degrees) in Sit)
            _frame[bone] = Rig.Euler(degrees);
        Blend(arms: false);

        var breath = Mathf.Sin(t * Mathf.Tau / (BreathSeconds * (1 + 0.5f * _weight["doze"])));
        Add("Spine", 0.7f * breath, 0, 0.6f * Mathf.Sin(t * 0.41f));
        Add("Chest", -1.1f * breath, 0, 0);
        Add("LeftShoulder", 0, 0, 1.2f * breath);
        Add("RightShoulder", 0, 0, -1.2f * breath);
        Legs(t, awake);
        Gaze(dt, awake);

        var bounce = 0.018f * Mathf.Abs(Mathf.Sin(t * 9f)) * _weight["cheer"];
        var hips = new Vector3(0, HipHeight + 0.003f * breath + bounce, HipBack);

        // Arms: hands planted in her lap whatever the torso does, then reaction poses over that.
        Reach("Left", LeftRest, hips);
        Reach("Right", RightRest, hips);
        Blend(arms: true);
        Add("RightLowerArm", 0, 0, 16 * Mathf.Sin(t * 11f) * _weight["wave"]);
        foreach (var side in Sides)
            foreach (var finger in Fingers)
                foreach (var joint in Joints)
                    _frame[side + finger + joint] = Rig.Euler(0, 0, (side == "Left" ? -1 : 1) * (joint == "Proximal" ? 8 : 12));

        _rig.PlaceHips(hips);
        foreach (var (bone, rotation) in _frame)
            _rig.Rotate(bone, rotation);
        _face.Update(delta);
    }

    // ---- Parts ---------------------------------------------------------------------------------------------------

    private void Blend(bool arms)
    {
        foreach (var (name, pose) in Overlays)
            if (_weight[name] > 0.001f)
                foreach (var (bone, degrees) in pose)
                    if (IsArm(bone) == arms)
                        _frame[bone] = _frame.GetValueOrDefault(bone, Quaternion.Identity).Slerp(Rig.Euler(degrees), _weight[name]);
    }

    private static bool IsArm(string bone) => ArmBones.Any(bone.EndsWith);

    /// <summary>Feet rest on the stool's ring; one heel bobs, and now and then a leg swings out and back.</summary>
    private void Legs(float t, float awake)
    {
        Add("LeftFoot", 3.5f * Mathf.Sin(t * 1.9f) * awake, 0, 0);
        if (_time > _nextKick && awake > 0.9f)
        {
            _kickStarted = _time;
            _kickSide = _random.Randf() < 0.5f ? "Left" : "Right";
            _nextKick = _time + _random.RandfRange(9, 22);
        }
        const float kickSeconds = 2.6f;
        var k = (float)(_time - _kickStarted) / kickSeconds;
        if (k is >= 0 and < 1)
        {
            // two swings under one smooth envelope
            var swing = Mathf.Sin(k * Mathf.Pi) * (0.5f - 0.5f * Mathf.Cos(k * Mathf.Tau * 2));
            Add(_kickSide + "LowerLeg", -34 * swing, 0, 0);
            Add(_kickSide + "Foot", -14 * swing, 0, 0);
        }
    }

    private void Gaze(float dt, float awake)
    {
        // What to look at: a reaction's target, else the pointer while it moves, else the viewer with the odd
        // glance at the screen or around.
        if (_time > _gazeUntil)
        {
            if (Pointer is { } pointer)
                _gaze = pointer;
            else if (_time > _nextWander)
            {
                _nextWander = _time + _random.RandfRange(9, 20);
                LookAt(_random.Randf() < 0.6f ? Screen : Viewer + new Vector3(_random.RandfRange(-1.5f, 1.5f), _random.RandfRange(-0.4f, 0.6f), 0), _random.RandfRange(1.5f, 3.5f));
            }
            else
                _gaze = Viewer;
        }

        var chest = _rig.Total("Chest", _frame);
        var direction = (chest.Inverse() * (_gaze - _rig.PositionOf("Head"))).Normalized();
        var yaw = Mathf.Clamp(Mathf.Atan2(direction.X, direction.Z), -1.2f, 1.2f) * awake;
        var pitch = Mathf.Clamp(Mathf.Asin(direction.Y), -0.2f, 0.45f) * awake;     // a bowed head hides her face
        var follow = 1 - Mathf.Exp(-dt * 6f);
        _yaw += (yaw - _yaw) * follow;
        _pitch += (pitch - _pitch) * follow;

        var t = (float)_time;
        var drift = Mathf.DegToRad(1.6f) * Mathf.Sin(t * 0.37f) * awake;
        var roll = 1.4f * Mathf.Sin(t * 0.23f + 1f) * awake;
        Turn("UpperChest", _yaw * 0.12f, 0, 0);
        Turn("Neck", _yaw * 0.33f, _pitch * 0.35f, 0);
        Turn("Head", _yaw * 0.55f + drift, _pitch * 0.6f, roll);
        // The eyes get there first and make up what the head has not turned yet.
        var eyeYaw = Mathf.Clamp(yaw - _yaw + _yaw * 0.12f, -0.17f, 0.17f);
        var eyePitch = Mathf.Clamp(pitch - _pitch + _pitch * 0.1f, -0.1f, 0.1f);
        _frame["LeftEye"] = _frame["RightEye"] = new Quaternion(Vector3.Up, eyeYaw) * new Quaternion(Vector3.Right, -eyePitch);
    }

    /// <summary>Two-bone reach: bends the arm so the wrist lands on the target, then lays the hand as asked.</summary>
    private void Reach(string side, Hand hand, Vector3 hips)
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
        var lowerDir = (shoulder + aim * reach - (shoulder + upperDir * a)).Normalized();

        var upperTotal = new Quaternion(along.Normalized(), upperDir);
        var lowerTotal = new Quaternion(upperDir, lowerDir) * upperTotal;
        var fingers = hand.Fingers.Normalized();
        var palm = (hand.Palm - fingers * hand.Palm.Dot(fingers)).Normalized();
        var x = side == "Left" ? fingers : -fingers;                 // at rest the left hand's fingers point along +x
        var handTotal = new Basis(x, -palm, x.Cross(-palm)).GetRotationQuaternion();

        _frame[upper] = _rig.Total(side + "Shoulder", _frame).Inverse() * upperTotal;
        _frame[lower] = upperTotal.Inverse() * lowerTotal;
        _frame[wrist] = lowerTotal.Inverse() * handTotal;
    }

    /// <summary>Layers a head-style turn (radians: yaw to her left, pitch up; roll in degrees) on a bone.</summary>
    private void Turn(string bone, float yaw, float pitch, float rollDegrees)
    {
        var turn = new Quaternion(Vector3.Up, yaw) * new Quaternion(Vector3.Right, -pitch) * new Quaternion(Vector3.Back, Mathf.DegToRad(rollDegrees));
        _frame[bone] = turn * _frame.GetValueOrDefault(bone, Quaternion.Identity);
    }

    private void Add(string bone, float x, float y, float z) =>
        _frame[bone] = Rig.Euler(x, y, z) * _frame.GetValueOrDefault(bone, Quaternion.Identity);

    private void Hold(string overlay, double seconds)
    {
        _target[overlay] = 1;
        _until[overlay] = _time + seconds;
    }

    private void LookAt(Vector3 point, double seconds)
    {
        _gaze = point;
        _gazeUntil = _time + seconds;
    }

    private readonly record struct Hand(Vector3 Wrist, Vector3 Fingers, Vector3 Palm, Vector3 Elbow);
}
