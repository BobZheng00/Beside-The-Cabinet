using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>Things the companion reacts to. The app decides when; the puppet decides how.</summary>
public enum Moment
{
    Cheer, Pump, Greet, Glance, Alarm, Worry, Doze, Wake, Note,
    /// <summary>The player starts talking to her; <see cref="Heard"/> when they have finished, <see cref="Nod"/> in their pauses.</summary>
    Listen, Heard, Nod,
    /// <summary>A reply is on its way: she thinks until <see cref="Speak"/> (its words have begun, or it is over).</summary>
    Think, Speak,
    /// <summary>The player cut her off mid-reply.</summary>
    CutOff,
    /// <summary>She writes a memo on her phone until <see cref="Send"/>: it goes to the cabinet, which prints it.</summary>
    Type, Send,
    /// <summary>Something came out of the printer's slot: she glances at it.</summary>
    Paper,
}

/// <summary>
/// How a character carries themselves on the stool, chosen per character by the player (Settings → Characters, the
/// first run, "Meet …"), whoever they are: neat, knees together and hands folded in the lap, the head tilting as they
/// listen and react, a leg swung now and then, a tune in their head swaying them; or relaxed, as people sit at ease on a
/// stool: knees a little apart, one foot often down off the ring, a hand on one thigh and the other by the hip, the back a
/// little rounded, the head tilting about a third as much (a nod more than a tilt), the legs still, a tune showing as a
/// nod on its beat. Their gestures are the same.
/// </summary>
public enum Manner { Neat, Relaxed }

/// <summary>
/// The companion's body language. Three layers, in this order every frame:
/// poses (seated, plus reaction poses switched on and off by short timelines), springs (every joint follows its
/// pose like a damped spring, so moves accelerate, overshoot and settle, the hand arriving after the arm), and
/// life (breathing, postural sway, a restless head, fidgets, eyes that jump and hold) added on top.
/// </summary>
/// <remarks>
/// She is a grown-up: her moves are small, few and asymmetric, and what she feels shows in her face and head more
/// than in her limbs. Conversation follows what people do (docs/research/2026-10-05-companion-agent.md §7): she
/// looks away to think and back to answer, looks at whoever is talking with now and then a brief glance aside, and
/// nods in their pauses.
/// </remarks>
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

    /// <summary>
    /// How a hand's fingers bend: degrees at each joint of the four fingers (a little less at the knuckle), and how
    /// far the thumb folds in towards the palm (0 to 1).
    /// </summary>
    private readonly record struct Grip(float Index, float Middle, float Ring, float Little, float Thumb)
    {
        /// <summary>All four fingers alike, the thumb folding in as they close (a fist's thumb lies over the fingers).</summary>
        public static Grip Even(float curl) => new(curl, curl, curl, curl, Mathf.Clamp((curl - RestCurl) / 60f, 0, 1));
        public float Of(string finger) => finger switch { "Index" => Index, "Middle" => Middle, "Ring" => Ring, "Little" => Little, _ => Thumb };
        public Grip Towards(Grip other, float weight) => new(Mathf.Lerp(Index, other.Index, weight), Mathf.Lerp(Middle, other.Middle, weight),
            Mathf.Lerp(Ring, other.Ring, weight), Mathf.Lerp(Little, other.Little, weight), Mathf.Lerp(Thumb, other.Thumb, weight));
    }

    /// <summary>
    /// A pose laid over the seated one: body rotations, optional hand placements, and how the fingers bend: all alike
    /// (<paramref name="Curl"/>, degrees) or finger by finger (a grip per hand). A hand <paramref name="AtChin"/> is
    /// placed by her face instead of her seat: its <see cref="Hand.Wrist"/> is where the index fingertip goes from
    /// her chin, and every direction is in her head's axes, so the hand stays there as her head moves.
    /// </summary>
    private sealed record Overlay(Pose Body, Hand? Left = null, Hand? Right = null, float Curl = -1, Grip? LeftGrip = null, Grip? RightGrip = null,
                                  bool AtChin = false);

    // ---- Poses (degrees; x = pitch forward, y = turn to her left, z = roll; left side given, right mirrored) -----
    private static readonly Pose Sit = new Pose()
        .Set("Hips", 4, 0, 0).Set("Spine", 4, 0, 0).Set("Chest", 2, 0, 0).Set("UpperChest", 0, 0, 0)
        .Set("Neck", -3, 0, 0).Set("Head", -4, 0, 0)
        .Both("UpperLeg", -(90 - ThighSlope), -2, 0).Both("LowerLeg", 114, 0, 0).Both("Foot", 30, 0, 0)   // knee bend is refitted per character
        .Both("Shoulder", 0, 0, -2);

    // Relaxed: the knees a little apart (degrees each thigh turns out; at 12 they sprawled), the back a little rounded
    // with the head kept level, the head's tilts a third as much.
    private const float KneesApart = 6, RelaxedTilt = 0.35f;
    private static readonly Pose Loose = new Pose()
        .Set("Spine", 6, 0, 0).Set("Chest", 4, 0, 0).Set("Neck", -5, 0, 0).Set("Head", -6, 0, 0)
        .Both("UpperLeg", -(90 - ThighSlope), KneesApart, 0);

    // Hands at rest in her lap, the left lying on the right.
    private static readonly Hand LeftRest = new(new(0.030f, 0.165f, 0.135f), new(-0.80f, -0.05f, 0.60f), new(0, -1, 0.1f), new(0.7f, -0.3f, -0.6f));
    private static readonly Hand RightRest = new(new(-0.055f, 0.148f, 0.125f), new(0.80f, -0.05f, 0.60f), new(0, -1, 0.1f), new(-0.7f, -0.3f, -0.6f));

    // Applause the way most people clap (Repp, 1987): the hands at an angle, not flat palm to palm. The left hand waits
    // in front of her chest, palm turned up towards her right, fingers forward; the right comes down onto it from
    // above, its fingers slanting across the left palm, and does nearly all the moving (see Clap). ClapMeet is where
    // the palms meet, ClapFacing the way the left palm faces.
    private static readonly Vector3 ClapMeet = new(-0.01f, 0.43f, 0.27f), ClapFacing = new Vector3(-1, 1, 0).Normalized();
    private static readonly Hand ClapLeft = Palm(ClapMeet - ClapFacing * 0.012f, new(0.2f, 0.2f, 0.96f), ClapFacing, new(0.8f, -1, -0.3f));
    private static readonly Hand ClapRight = Palm(ClapMeet + ClapFacing * 0.012f, new(0.61f, 0.61f, 0.5f), -ClapFacing, new(-0.8f, -1, -0.2f));

    /// <summary>A hand placed by the middle of its palm (about 4.5 cm from the wrist, up the fingers).</summary>
    private static Hand Palm(Vector3 middle, Vector3 fingers, Vector3 palm, Vector3 elbow) =>
        new(middle - fingers.Normalized() * 0.045f, fingers, palm, elbow);

    // Thinking: the right index finger up against her chin, the other fingers curled, the hand's back to the viewer.
    // Placed from her chin (Overlay.AtChin): the fingertip just below and in front of it, so it never sinks in.
    private static readonly Hand ChinHand = new(new(-0.004f, -0.012f, 0.012f), new(0.2f, 0.9f, -0.35f), new(1, 0, -0.2f), new(-0.5f, -1, 0.2f));
    private static readonly Grip ChinGrip = new(6, 55, 70, 78, 0.45f);

    // Taking a note, as people hold a pocket notebook and write in it. The left hand carries the pad on its palm,
    // palm up, fingers straight beneath it and the thumb over its edge, its top away from her. The right hand
    // writes with the dynamic tripod grip (the grip handwriting is taught with): the pencil pinched between the pads
    // of thumb and index finger and resting on the middle finger, ring and little fingers tucked into the palm, the
    // palm turned down to the page. The page lies almost flat, tipped only about 25° towards the viewer: tipped
    // further, the writing hand would have to come at it from the viewer's side.
    private static readonly Hand PadHand = new(new(0.13f, 0.30f, 0.20f), new(-0.91f, 0, 0.41f), new(0.14f, 0.94f, 0.31f), new(0.8f, -0.6f, -0.2f));
    private static readonly Hand PenHand = new(new(-0.045f, 0.39f, 0.21f), new(0.55f, -0.15f, 0.82f), new(0.30f, -0.90f, -0.30f), new(-0.8f, -0.6f, -0.1f));
    private static readonly Grip PadGrip = new(-4, -4, -3, -2, 0.5f);
    private static readonly Grip PenGrip = new(32, 45, 75, 80, 0.55f);

    // Texting with both thumbs, as people hold a phone to type on it sitting down: in both hands at lower-chest height,
    // fairly upright (its length about 58° up from flat), the screen turned up and back to her face. Each hand cradles
    // a lower corner: the palm against the phone's edge and back, the fingers wrapped behind it towards the other hand,
    // the thumb free over the screen. In the phone's own axes (a across to her left, u up its length, n out of the
    // screen towards her) a hand's palm is at a·4 cm − u·3.5 cm − n·1 cm from its centre, its fingers along
    // −0.6a + 0.65u − 0.45n and its palm facing −a + 0.6n. Placed by the middle of the palm (see Palm); the phone
    // itself is placed between the palms every frame (CompanionView), and PhoneAt is where it is, for her eyes.
    private static readonly Hand PhoneLeft = Palm(new(0.040f, 0.315f, 0.230f), new(-0.604f, 0.316f, 0.732f), new(-0.855f, 0.272f, -0.436f), new(0.8f, -0.6f, -0.2f));
    private static readonly Hand PhoneRight = Palm(new(-0.040f, 0.315f, 0.230f), new(0.604f, 0.316f, 0.732f), new(0.855f, 0.272f, -0.436f), new(-0.8f, -0.6f, -0.2f));
    private static readonly Grip PhoneGrip = new(35, 45, 55, 60, 0.15f);
    private static readonly Vector3 PhoneAt = new(0, 0.35f, 0.24f);

    private static readonly Dictionary<string, Overlay> Overlays = new()
    {
        // delighted for the player: chest lifted, leaning a little their way, head tilted, hands together to clap
        ["clap"] = new(new Pose().Set("Spine", 3, 0, 0).Set("Chest", -2, -3, 0).Set("Neck", 1, 0, 2).Set("Head", 2, 0, 5),
            ClapLeft, ClapRight, Curl: 12),
        // "yes!": a fist brought up to the shoulder, then pulled down
        ["pump"] = new(new Pose().Set("Spine", -2, 0, 0).Set("Chest", -2, 0, 0).Set("Head", -3, 0, 0).Set("RightShoulder", 0, 0, -5),
            Right: new(new(-0.19f, 0.55f, 0.22f), new(0, 1, 0.1f), new(1, 0, 0), new(-0.4f, -1, -0.2f)), Curl: 75),
        ["yes"] = new(new Pose().Set("Spine", 6, 0, 0).Set("Chest", 4, 0, 0).Set("Head", 3, 0, 0).Set("RightShoulder", 0, 0, 4),
            Right: new(new(-0.17f, 0.42f, 0.19f), new(0, 1, 0.1f), new(1, 0, 0), new(-0.5f, -1, -0.6f)), Curl: 75),
        ["wave"] = new(new Pose().Set("Spine", 0, 0, 2).Set("Head", -3, 0, 6).Set("RightShoulder", 0, 0, -8),
            Right: new(new(-0.34f, 0.70f, 0.12f), new(-0.1f, 1, 0), new(0.5f, 0, 1), new(-0.5f, -1, -0.2f)), Curl: 3),
        ["worry"] = new(new Pose().Set("Spine", 6, 0, 0).Set("Chest", 4, 0, 0).Set("Neck", 4, 0, 0).Set("Head", 5, 0, 4).Both("Shoulder", 0, 0, -5)),
        // head a little bowed over the pad and tilted, her eyes doing most of the looking down (as with the phone: bowed
        // further, her hair hides her face from the cabinet's camera while she says what she is writing), shoulders relaxed
        ["note"] = new(new Pose().Set("Spine", 3, 0, 0).Set("Chest", 2, 0, 0).Set("Neck", 2, 0, 1).Set("Head", 2, 0, 5).Both("Shoulder", 0, 0, -3),
            PadHand, PenHand, LeftGrip: PadGrip, RightGrip: PenGrip),
        // texting: head a little bowed and tilted, her eyes doing most of the looking down (bowed further, her hair would hide
        // her face from the cabinet's high camera), shoulders a little forward, the phone held up in both hands
        ["phone"] = new(new Pose().Set("Spine", 3, 0, 0).Set("Chest", 2, 0, 0).Set("Neck", 2, 0, 1).Set("Head", 2, 0, 4).Both("Shoulder", 0, 0, -3),
            PhoneLeft, PhoneRight, LeftGrip: PhoneGrip, RightGrip: PhoneGrip),
        // listening to the player: leaning in, head a little tilted towards them
        ["listen"] = new(new Pose().Set("Spine", 5, 0, 0).Set("Chest", 3, 0, 0).Set("Neck", 2, 0, 2).Set("Head", 3, 0, 5)),
        // thinking it over, finger to her chin, head a little forward and tilted to the hand
        ["ponder"] = new(new Pose().Set("Spine", 2, 0, 0).Set("Neck", 1, 0, 1).Set("Head", 2, 0, 3), Right: ChinHand, RightGrip: ChinGrip, AtChin: true),
        // cut off: a small start, head drawn back
        ["startle"] = new(new Pose().Set("Spine", -1, 0, 0).Set("Neck", -1, 0, 0).Set("Head", -3, 0, 0)),
        ["doze"] = new(new Pose().Set("Spine", 3, 0, 2).Set("Chest", 2, 0, 2).Set("Neck", 4, 0, 5).Set("Head", 7, 0, 11)),
        // idle fidgets
        ["tiltLeft"] = new(new Pose().Set("Neck", -3, 0, -3).Set("Head", -4, 0, -9)),
        ["tiltRight"] = new(new Pose().Set("Neck", -3, 0, 3).Set("Head", -4, 0, 9)),
        ["shift"] = new(new Pose().Set("Spine", 4, 0, 5).Set("Chest", 2, 0, -3).Set("Head", -4, 0, -3)),
        // a deep breath in: chest and shoulders up, let go again by the springs
        ["sigh"] = new(new Pose().Set("Spine", 1, 0, 0).Set("Chest", -3, 0, 0).Set("Neck", -4, 0, 0).Set("Head", -6, 0, 0).Both("Shoulder", 0, 0, 7)),
    };

    private static readonly string[] Gestures = ["clap", "pump", "yes", "wave", "note", "phone", "ponder", "startle", "tiltLeft", "tiltRight", "shift", "sigh", "listen"];
    private static readonly string[] Sides = ["Left", "Right"];
    private static readonly string[] Fingers = ["Index", "Middle", "Ring", "Little"];
    private static readonly string[] Joints = ["Proximal", "Intermediate", "Distal"];

    // Hand placements and the hip position below are written for one figure and scaled to the character at hand
    // (see Rig.Size).
    private const float HipJointHeight = 0.045f, HipJointBack = -0.04f;   // hip joints above / behind the seat centre
    private const float ThighSlope = 5;                            // degrees below level
    private const float KneeHanging = 90 - ThighSlope + 9;         // knee bend of a leg hanging free: the shin a little back of plumb
    private const float RestCurl = 11;

    // ---- State ---------------------------------------------------------------------------------------------------
    private readonly Rig _rig;
    private readonly Face _face;
    private readonly Pose _sit;
    private readonly float _size;
    private readonly Vector3 _seat;                       // where the hips bone goes, from the seat centre
    private readonly bool _dangling;                      // her feet do not reach the stool's ring
    private readonly float _kneeOnRing;                   // knee bend (degrees) that rests her toes on the ring
    private readonly Hand _onThigh, _byHip;               // relaxed hands, both given for the left side (the right takes _byHip mirrored)
    private readonly Dictionary<string, float> _hang = [], _hangGoal = [];   // per leg: 0 = foot on the ring, 1 = hanging free
    private double _nextLegChange = 10;
    private readonly Dictionary<string, Quaternion> _goal = [], _frame = [];
    private readonly Dictionary<string, RotationSpring> _springs = [];
    private readonly Dictionary<string, float> _on = [], _shown = [], _shownSpeed = [];
    private readonly Dictionary<string, Grip> _grip = new() { ["Left"] = Grip.Even(RestCurl), ["Right"] = Grip.Even(RestCurl) };
    private readonly List<(double At, Action Do)> _cues = [];
    private readonly RandomNumberGenerator _random = new();

    private double _time, _busyUntil, _gazeUntil, _nextWander = 7, _nextFidget = 5, _nextSaccade;
    private double _clapAt = -100, _waveAt = -100, _noteAt = -100, _kickAt = -100, _swingAt = -100, _humAt = -100;

    // Texting: since when, the taps (each thumb's last, the next, how many left in this burst of a word), a pause to
    // think with a glance up, and the lift of the phone as she sends it.
    private double? _typingSince;
    private readonly Dictionary<string, double> _tapAt = new() { ["Left"] = -100, ["Right"] = -100 };
    private double _nextTap, _thinkUntil = -100, _liftAt = -100, _firmAt = -100;
    private int _burst, _bursts;
    private string _thumb = "Right";
    private Vector3 _thinkSpot;
    private string _kickSide = "Left";
    private Vector3 _gaze;
    private Vector2 _eye, _jitter;
    private float _headYaw, _headPitch, _breath, _awake = 1;

    // In conversation: thinking since when, listening, the gaze going away and back, nods, her voice's loudness.
    private readonly Vector3? _chin;                      // her chin from her head's joint, in its axes (none without eye bones)
    private readonly float _fingerReach;                  // wrist to index fingertip
    private readonly float _side;                         // the side she looks to when thinking: people tend to keep to one
    private double? _thinkingSince;
    private bool _listening, _lookingAway, _wasTalking;
    private double _nextGazeTurn, _nodAt = -100, _lastVoice = -100;
    private Vector3 _awaySpot;
    private int _nodCycles;
    private float _nodSize, _voice, _voiceFast, _voiceSlow, _beatSide = 1;
    private bool _nodLifts;

    /// <summary>Points she can look at, in the skeleton's space.</summary>
    public Vector3 Viewer { get; set; }
    public Vector3 Screen { get; set; }
    /// <summary>Where the mouse pointer is, when it moved recently.</summary>
    public Vector3? Pointer { get; set; }

    /// <summary>
    /// Bones that swing forward with a thigh by the given share of its rotation: the panels of a skirt that is not
    /// attached to the legs, so that it lies over them when she sits.
    /// </summary>
    public IReadOnlyList<(string Bone, string Side, float Follow)> Followers { get; init; } = [];

    /// <summary>How they sit and carry themselves; changed, they move into it, feet too.</summary>
    public Manner Manner
    {
        get => _manner;
        set
        {
            if (value != _manner)
                _nextLegChange = Math.Min(_nextLegChange, _time + 0.8);
            _manner = value;
        }
    }
    private Manner _manner;

    private float Tilt => Manner == Manner.Relaxed ? RelaxedTilt : 1;

    /// <summary>Where her head is, in the skeleton's space.</summary>
    public Vector3 Head => _rig.PositionOf("Head");

    /// <summary>How far she is into taking a note (0: not at all, 1: pad up, writing), for showing the pad and pencil.</summary>
    public float Noting => _shown["note"];

    /// <summary>She is typing a memo on her phone.</summary>
    public bool Typing => _typingSince is not null;

    /// <summary>How far she has her phone out (0 to 1), for showing it.</summary>
    public float Phoning => _shown["phone"];

    /// <summary>The printer's slot, where a memo she sent comes out.</summary>
    public Vector3 Slot { get; set; }

    /// <summary>How loud her voice is now (her mouth's opening, 0–1), set every frame while she speaks.</summary>
    public float Voice
    {
        set => _voice = value;
    }

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
        _kneeOnRing = 90 - ThighSlope + back;
        foreach (var side in Sides)
            _hang[side] = _hangGoal[side] = _dangling ? 1 : 0;
        // She is seated by her hip joints; where the hips bone sits relative to them differs from rig to rig.
        var fromJoints = rig.RestOffset("LeftUpperLeg", "Hips");
        _seat = new Vector3(0, HipJointHeight * _size + fromJoints.Y, HipJointBack * _size + fromJoints.Z);
        // Relaxed hands, one unlike the other, each on its own thigh (turned in across the lap, a hand reads as covering
        // it): the left past halfway to the knee, fingers along the thigh; the right nearer its hip, turned in a little.
        // The elbows hang by the body. Found from this character's hip joints and thigh, as hips and legs differ more
        // than laps do.
        var (apart, slope) = (Mathf.DegToRad(KneesApart), Mathf.DegToRad(ThighSlope));
        var along = new Vector3(Mathf.Sin(apart) * Mathf.Cos(slope), -Mathf.Sin(slope), Mathf.Cos(apart) * Mathf.Cos(slope));
        var hipJoint = new Vector3(Mathf.Abs(fromJoints.X), HipJointHeight * _size, HipJointBack * _size);
        var onTop = Vector3.Up * (0.11f * _size);
        _onThigh = new Hand(hipJoint + along * (0.55f * thigh) + onTop, along, new(0, -1, 0.1f), new(0.25f, -0.85f, -0.45f));
        _byHip = new Hand(hipJoint + along * (0.3f * thigh) + onTop, (along + new Vector3(-0.3f, 0, 0)).Normalized(),
            new(0, -1, 0.1f), new(0.3f, -0.85f, -0.4f));

        foreach (var name in Overlays.Keys)
            _on[name] = _shown[name] = _shownSpeed[name] = 0;
        _face.Rest("relaxed", 0.1f);        // the hint of a smile; more and her eyes look sleepy

        // Her chin, found from her eyes: heads differ in size from legs, and faces from heads. On both built-in
        // characters it is about 8.5 cm below the eyes and 5.5 cm in front of them.
        string[] finger = ["RightHand", "RightIndexProximal", "RightIndexIntermediate", "RightIndexDistal"];
        if (rig.Has("LeftEye") && rig.Has("RightEye") && finger.All(rig.Has))
        {
            var eyes = (rig.RestJoint("LeftEye") + rig.RestJoint("RightEye")) / 2;
            _chin = eyes - rig.RestJoint("Head") + new Vector3(0, -0.085f, 0.055f);
            _fingerReach = rig.RestOffset(finger[0], finger[1]).Length() + rig.RestOffset(finger[1], finger[2]).Length()
                           + 1.9f * rig.RestOffset(finger[2], finger[3]).Length();       // the last bone carried on as far again
        }
        _side = _random.Randf() < 0.5f ? 1 : -1;
    }

    // ---- Reactions -----------------------------------------------------------------------------------------------

    public void Play(Moment moment)
    {
        switch (moment)
        {
            case Moment.Cheer:
                // she sees it ("oh!"), lights up and claps for the player, her body going with the claps; then holds her
                // hands together a moment and nods: well done
                Interrupt(3.0);
                _face.Show("surprised", 0.35f, 0.3);
                After(0.12, () => { _on["clap"] = 1; _clapAt = _time + 0.22; });       // the first clap opens as her hands arrive
                After(0.3, () => _face.Show("happy", 0.65f, 2.4));                   // a warm smile, not a laugh
                After(1.6, () => Nod(1, 5));
                After(2.2, () => _on["clap"] = 0);
                Look(Viewer, 3);
                break;
            case Moment.Pump:
                Interrupt(1.9);
                _on["pump"] = 1;
                _face.Show("happy", 0.6f, 1.7);
                After(0.32, () => { _on["pump"] = 0; _on["yes"] = 1; });
                After(1.0, () => { _on["yes"] = 0; Nod(1, 4); });
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
            case Moment.Listen:
                Interrupt(60);                                       // held until she has heard it
                _on["listen"] = 1;
                _face.Show("relaxed", 0.8f, 60);
                _listening = true;
                _gazeUntil = _time;                                  // her eyes on the player, glancing aside now and then (Conversing)
                TurnGaze(away: false, _random.RandfRange(4, 7));
                break;
            case Moment.Heard:
                _on["listen"] = 0;
                _listening = false;
                _face.Show("relaxed", 0.8f, 0);                     // her listening face goes
                _busyUntil = _time + 0.8;
                _face.Show("happy", 0.6f, 1.2);
                Look(Viewer, 0.6);
                break;
            case Moment.Nod:
                // mostly a single nod, sometimes two or three, each smaller than the one before (Kato and others, 2026)
                var cycles = _random.Randf() switch { < 0.51f => 1, < 0.84f => 2, _ => 3 };
                Nod(cycles, _random.RandfRange(4.5f, 6.5f));
                break;
            case Moment.Think:
                // a beat on the player, then away to think, up and to the side (Andrist and others, 2014)
                _thinkingSince = _time;
                TurnGaze(away: false, _random.RandfRange(0.25f, 0.45f));
                break;
            case Moment.Speak:
                if (_thinkingSince is null)
                    break;
                _thinkingSince = null;
                _on["ponder"] = 0;
                if (_lookingAway)
                    Look(_awaySpot, _random.RandfRange(0.5f, 1.0f));   // people start an answer still looking away, then look up at whoever asked
                TurnGaze(away: false, _random.RandfRange(3.5f, 6));
                break;
            case Moment.CutOff:
                // she stops, a little taken aback, and gives the player her attention
                Interrupt(1.5);
                _thinkingSince = null;
                _on["startle"] = 1;
                After(0.22, () => _on["startle"] = 0);
                _face.Show("surprised", 0.3f, 0.45);
                _face.Blink();
                Look(Viewer, 2);
                break;
            case Moment.Type:
                if (_typingSince is not null)
                    break;
                // the phone comes up into both hands, her head bows to it, and she starts typing
                Interrupt(60);
                _thinkingSince = null;
                _on["ponder"] = 0;
                _on["phone"] = 1;
                _typingSince = _time;
                _nextTap = _time + _random.RandfRange(0.55f, 0.75f);           // once the phone is up
                _burst = _random.RandiRange(3, 7);
                _bursts = _random.RandiRange(2, 4);
                _face.Show("relaxed", 0.5f, 60);
                break;
            case Moment.Send:
                // a last firm tap, a small lift of the phone: sent. Then a glance at the slot as it prints, and a small
                // smile: there you go. The phone goes away.
                if (_typingSince is null)
                {
                    Play(Moment.Type);                                          // she wasn't seen writing it: a moment of it first
                    After(SendAfterTyping, () => Play(Moment.Send));
                    break;
                }
                _typingSince = null;
                _thinkUntil = -100;
                _firmAt = _tapAt["Right"] = _time;
                _liftAt = _time + 0.12;
                _busyUntil = _time + 2.4;
                _face.Show("relaxed", 0.5f, 0);
                After(0.55, () => { Look(Slot, 1.7); _face.Show("happy", 0.55f, 1.6); });
                After(1.05, () => _on["phone"] = 0);
                After(2.25, () => Look(Viewer, 2));
                break;
            case Moment.Paper:
                Look(Slot, _random.RandfRange(1.2f, 1.8f));
                break;
            case Moment.Note:
                // pad up, eyes on it, a few quick strokes, then a satisfied look back up; a second note while she writes
                // (two things put on the agenda at once) goes on the same page
                if (_time - _noteAt < 2.55)
                    break;
                Interrupt(3.6);
                _on["note"] = 1;
                _noteAt = _time;
                _face.Show("relaxed", 0.7f, 2.6);
                Look(PadHand.Wrist * _size + new Vector3(-0.03f, 0.02f, 0.03f) * _size, 2.5);
                After(2.55, () => { _on["note"] = 0; _face.Show("happy", 0.45f, 1.2); Look(Viewer, 2); });
                break;
        }
    }

    /// <summary>How long she is seen typing a memo she wasn't seen start (see <see cref="Moment.Send"/>), before she sends it.</summary>
    public const double SendAfterTyping = 1.4;

    /// <summary>Drops whatever gesture is in progress and keeps fidgets away for a while.</summary>
    private void Interrupt(double busySeconds)
    {
        _cues.Clear();
        _typingSince = null;
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
        LegPosture(dt);
        // a long think: finger to her chin, once nothing else is going on
        if (_thinkingSince is { } since && _time - since > 2.4 && _time > _busyUntil && _chin is not null)
            _on["ponder"] = 1;

        // 1. Poses: seated, reaction poses over it, head and torso turned to what she is looking at.
        _goal.Clear();
        foreach (var (bone, degrees) in _sit)
            _goal[bone] = Rig.Euler(degrees);
        if (Manner == Manner.Relaxed)
            foreach (var (bone, degrees) in Loose)
                _goal[bone] = Rig.Euler(degrees);
        foreach (var side in Sides)
        {
            // a foot on the ring has the knee bent back to reach it; let down, the shin hangs and the foot relaxes
            _goal[side + "LowerLeg"] = Rig.Euler(Mathf.Lerp(_kneeOnRing, KneeHanging, _hang[side]), 0, 0);
            _goal[side + "Foot"] = Rig.Euler(Mathf.Lerp(30, 20, _hang[side]), 0, 0);
        }
        foreach (var (name, overlay) in Overlays)
            if (_shown[name] > 0.001f)
                foreach (var (bone, degrees) in overlay.Body)
                    _goal[bone] = _goal.GetValueOrDefault(bone, Quaternion.Identity)
                        .Slerp(Rig.Euler(bone is "Head" or "Neck" ? degrees with { Z = degrees.Z * Tilt } : degrees), _shown[name]);
        Gaze(dt, t);

        // 2. Springs: ... and each joint then chases its pose with some give.
        _frame.Clear();
        foreach (var (bone, rotation) in _goal)
            _frame[bone] = Spring(bone).Follow(rotation, dt);

        // 3. Life on top of the settled pose.
        var lift = Breathe(dt);
        Sway(t);
        Legs(t);
        Nodding();
        Emphasis(dt);
        var hips = _seat + new Vector3(0, 0.002f * lift, 0);

        foreach (var (bone, side, follow) in Followers)
            _frame[bone] = Quaternion.Identity.Slerp(_frame[side + "UpperLeg"], follow);

        // Arms last: they hang off the finished torso, so the hands stay in her lap while she breathes.
        foreach (var side in Sides)
            Arm(side, hips, dt);
        Wave(t);
        Write();
        Text();
        Clap(hips);
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
        // What to look at: a reaction's target, else the conversation's, else the pointer while it moves, else mostly
        // the viewer with glances at the screen or off to the side.
        if (_typingSince is not null || _time - _liftAt < 0.5)
            Retarget(_time < _thinkUntil ? _thinkSpot : PhoneAt * _size);     // eyes on the phone, but for a glance up to think
        else if (_time > _gazeUntil && !Conversing())
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
        var roll = 3.2f * Wobble(t, 5.5f, 9.7f, 4) * calm * Tilt;

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

    /// <summary>
    /// Where she looks while a conversation goes on, after what people do in one (Andrist and others, 2014; Kendon,
    /// 1967). Thinking of an answer: mostly away, up and to one side, three or four seconds at a time with a look back
    /// between. Listening: at the player, glancing aside for a second every seven or so. Talking: at the player,
    /// glancing aside for a second or two every five. False when she is doing none of these.
    /// </summary>
    private bool Conversing()
    {
        var talking = _time - _lastVoice < 0.8;
        if (talking && !_wasTalking && !_lookingAway)
            TurnGaze(away: false, _random.RandfRange(3.5f, 6));         // she starts speaking to the player
        _wasTalking = talking;
        var ((awayFrom, awayTo), (backFrom, backTo)) = _thinkingSince is not null ? ((2.8f, 4.2f), (0.8f, 1.4f))
            : _listening ? ((0.8f, 1.4f), (5.5f, 9f))
            : talking ? ((1.0f, 1.8f), (3.5f, 6f))
            : default;
        if (awayTo == 0)
            return false;
        if (_time >= _nextGazeTurn)
            TurnGaze(!_lookingAway, _lookingAway ? _random.RandfRange(backFrom, backTo) : _random.RandfRange(awayFrom, awayTo));
        Retarget(_lookingAway ? _awaySpot : Viewer);
        return true;
    }

    /// <summary>Looks away (to a new spot) or back at the player, until the next turn in <paramref name="seconds"/>.</summary>
    private void TurnGaze(bool away, double seconds)
    {
        _lookingAway = away;
        _nextGazeTurn = _time + seconds;
        if (!away)
            return;
        // thinking: well away, up or level, to her own side (looking down hides her face and reads as sad); otherwise a
        // smaller glance aside and a little down
        var thinking = _thinkingSince is not null;
        var side = _random.Randf() < 0.75f ? _side : -_side;
        var yaw = side * (thinking ? _random.RandfRange(18, 30) : _random.RandfRange(10, 18));
        var pitch = thinking ? (_random.Randf() < 0.65f ? _random.RandfRange(10, 16) : _random.RandfRange(0, 4)) : _random.RandfRange(-7, 1);
        var toViewer = Viewer - Head;
        var direction = new Quaternion(Vector3.Up, Mathf.DegToRad(yaw)) * toViewer.Normalized();
        direction = new Quaternion(direction.Cross(Vector3.Up).Normalized(), Mathf.DegToRad(pitch)) * direction;
        _awaySpot = Head + direction * toViewer.Length();
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

    /// <summary>
    /// Balance is never perfect: the torso leans and turns a little all the time; a tune in their head sways it more.
    /// At ease, the body rolls less from side to side and leans in and out a little more, and a tune shows as a nod.
    /// </summary>
    private void Sway(float t)
    {
        var relaxed = Manner == Manner.Relaxed;
        var roll = (relaxed ? 0.6f : 1.4f) * Wobble(t, 9.1f, 4.3f, 1);
        Add("Spine", (relaxed ? 1.2f : 0.5f) * Wobble(t, 7.7f, 12.9f, 3), 1.1f * Wobble(t, 13.1f, 8.3f, 5), roll);
        Add("Chest", 0, 0, -0.6f * roll);

        var hum = (float)(_time - _humAt);
        if (hum is >= 0 and < 3.4f && relaxed)
        {
            var beat = Envelope(hum, 0.5f, 2.7f, 3.4f) * (0.5f - 0.5f * Mathf.Cos(hum * Mathf.Tau * 1.6f));
            Add("Head", 3 * beat, 0, 0);
            Add("Neck", beat, 0, 0);
        }
        else if (hum is >= 0 and < 3.4f)
        {
            var swing = Envelope(hum, 0.5f, 2.7f, 3.4f) * Mathf.Sin(hum * Mathf.Tau * 0.85f);
            Add("Spine", 0, 0, 3.2f * swing);
            Add("Chest", 0, 0, 1.5f * swing);
            Add("Head", 0, 0, -3.5f * Envelope(hum, 0.5f, 2.7f, 3.4f) * Mathf.Sin(hum * Mathf.Tau * 0.85f - 0.6f));
        }
        if (_shown["doze"] > 0.001f)
        {
            // nodding off: the head sinks slowly, then jerks back up
            var nod = (float)(_time % 5.5) / 5.5f;
            Add("Head", 9 * (nod < 0.88f ? Mathf.SmoothStep(0, 1, nod / 0.88f) : 1 - Mathf.SmoothStep(0, 1, (nod - 0.88f) / 0.12f)) * (1 - _awake), 0, 0);
        }
    }

    /// <summary>
    /// Nobody keeps both feet on a stool's ring for long. Every so often she lets one foot down, or both, and
    /// later puts them back. A character whose legs do not reach the ring has them hanging all the time.
    /// </summary>
    private void LegPosture(float dt)
    {
        if (!_dangling && _time > _nextLegChange && _awake > 0.9f)
        {
            _nextLegChange = _time + _random.RandfRange(14, 38);
            var now = (_hangGoal["Left"], _hangGoal["Right"]);
            var next = now;
            // both on the ring, the left down, the right down, both down: at ease, one foot down most of the time
            var (both, left, right) = Manner == Manner.Relaxed ? (0.15f, 0.5f, 0.85f) : (0.4f, 0.58f, 0.76f);
            while (next == now)
            {
                var r = _random.Randf();
                next = r < both ? (0f, 0f) : r < left ? (1f, 0f) : r < right ? (0f, 1f) : (1f, 1f);
            }
            (_hangGoal["Left"], _hangGoal["Right"]) = next;
        }
        foreach (var side in Sides)
            _hang[side] += (_hangGoal[side] - _hang[side]) * (1 - Mathf.Exp(-dt * 1.6f));     // about a second to let a foot down
    }

    /// <summary>
    /// A few seconds of how they carry themselves, to see a manner as it is chosen: leaning in to listen, a nod, a
    /// tilt of the head, re-settling on the seat, and with the neat manner a leg swung.
    /// </summary>
    public void ShowManner()
    {
        Play(Moment.Listen);
        After(1.3, () => Play(Moment.Nod));
        After(2.6, () => Play(Moment.Heard));
        After(3.8, () => _on["tiltRight"] = 1);
        After(5.4, () => _on["tiltRight"] = 0);
        After(6.0, () => _on["shift"] = 1);
        After(6.5, () => _on["shift"] = 0);
        if (Manner == Manner.Neat && !_dangling)
            After(7.2, () => (_kickAt, _kickSide) = (_time, "Right"));
        _nextFidget = _time + 11;                           // nothing of their own meanwhile
    }

    /// <summary>Dev: put her feet where asked (true = hanging free) and hold that for a while.</summary>
    public void Hang(bool left, bool right)
    {
        (_hangGoal["Left"], _hangGoal["Right"]) = (left ? 1 : 0, right ? 1 : 0);
        _nextLegChange = _time + 30;
    }

    /// <summary>Dev: a tune in their head now.</summary>
    public void DevHum() => _humAt = _time;

    /// <summary>A hanging leg swings gently; a heel on the ring bobs; now and then a leg (or both) swings out and back.</summary>
    private void Legs(float t)
    {
        var drift = Manner == Manner.Relaxed ? 3 : 7;      // at ease, a hanging leg all but still
        Add("LeftLowerLeg", drift * Mathf.Sin(t * 1.5f) * _awake * _hang["Left"], 0, 0);
        Add("RightLowerLeg", drift * Mathf.Sin(t * 1.5f + 2.4f) * _awake * _hang["Right"], 0, 0);
        Add("LeftFoot", 3.5f * Mathf.Sin(t * 1.9f) * _awake * (1 - _hang["Left"]), 0, 0);
        Add("RightFoot", 2.5f * Mathf.Sin(t * 1.3f + 1) * _awake * (1 - _hang["Right"]), 0, 0);

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

    /// <summary>
    /// Nods: down and back up, the way down quicker than the way up; a second or third cycle quicker and smaller
    /// still. Now and then the head lifts a little first. Added on top of the pose, so it reads crisply.
    /// </summary>
    private void Nod(int cycles, float degrees)
    {
        _nodAt = _time;
        _nodCycles = cycles;
        _nodSize = degrees;
        _nodLifts = _random.Randf() < 0.24f;
    }

    private void Nodding()
    {
        var since = (float)(_time - _nodAt) - (_nodLifts ? 0.12f : 0);
        var angle = 0f;
        if (since < 0)
            angle = since > -0.12f ? -0.3f * _nodSize * Mathf.Sin(Mathf.Pi * -since / 0.12f) : 0;
        else
            for (var k = 0; k < _nodCycles; k++)
            {
                var length = k == 0 ? 0.5f : 0.36f;
                if (since < length)
                {
                    var u = since / length;
                    var size = _nodSize * Mathf.Pow(0.62f, k) * (k > 0 && k == _nodCycles - 1 ? 0.75f : 1);
                    angle = size * (u < 0.35f ? Mathf.SmoothStep(0, 1, u / 0.35f) : 1 - Mathf.SmoothStep(0, 1, (u - 0.35f) / 0.65f));
                    break;
                }
                since -= length;
            }
        if (angle == 0)
            return;
        Add("Neck", 0.3f * angle, 0, 0);
        Add("Head", 0.7f * angle, 0, 0);
    }

    /// <summary>
    /// While she speaks her head marks what she stresses, as speakers' heads do: it dips a little where her voice
    /// swells (a phrase starting, a stressed word) and comes back up, tilting to one side then the other from one
    /// phrase to the next. Phrase-paced, not syllable-paced: anything quicker looks like chattering.
    /// </summary>
    private void Emphasis(float dt)
    {
        if (_voice > 0.05f)
            _lastVoice = _time;
        var before = _voiceFast - _voiceSlow;
        _voiceFast += (_voice - _voiceFast) * (1 - Mathf.Exp(-dt * 4f));
        _voiceSlow += (_voice - _voiceSlow) * (1 - Mathf.Exp(-dt * 0.8f));
        var swell = Mathf.Clamp(_voiceFast - _voiceSlow, 0, 1);
        if (before < 0.04f && swell >= 0.04f)
            _beatSide = -_beatSide;
        Add("Neck", 2.5f * swell, 0, 0);
        Add("Head", 6.0f * swell, 0, 3.0f * _beatSide * swell);
    }

    // The claps: how long each takes (seconds, apart and back together) and how far the right hand opens (degrees).
    // The first is slower and bigger, the rest quicker and dying away, not quite evenly.
    private static readonly (float Length, float Open)[] Claps = [(0.34f, 21), (0.28f, 17), (0.27f, 15), (0.31f, 11)];

    /// <summary>
    /// Applause. The right forearm swings up off the left palm, quickly at first, and comes down faster and faster to
    /// strike it; the hand trails the forearm a little, bending back on the way up. The left hand gives a little to meet
    /// it. Each clap goes through her: shoulders dip, chest and head bob, a beat after the hands meet.
    /// </summary>
    private void Clap(Vector3 hips)
    {
        var since = (float)(_time - _clapAt);
        if (since < 0 || since > Claps.Sum(c => c.Length) + 0.3f)
            return;
        var weight = _shown["clap"];
        // Opening(t): how far apart the hands are t seconds in, 0 at each clap; Beat: the body's jolt after each one
        float Opening(float t)
        {
            foreach (var (length, open) in Claps)
            {
                if (t < length)
                {
                    var u = Mathf.Max(t, 0) / length;
                    return open * (u < 0.6f ? Mathf.Sin(Mathf.Pi / 2 * u / 0.6f) : Mathf.Cos(Mathf.Pi / 2 * (u - 0.6f) / 0.4f));
                }
                t -= length;
            }
            return 0;
        }
        var beat = 0f;
        var at = 0f;
        foreach (var (length, open) in Claps)
        {
            at += length;
            var after = (since - at) / 0.07f;                              // peaks 0.07 s after the hands meet
            if (after > 0)
                beat += open / Claps[0].Open * after * Mathf.Exp(1 - after);
        }
        beat *= weight;
        Add("Chest", 1.2f * beat, 0, 0);
        Add("Neck", 0.6f * beat, 0, 0);
        Add("Head", 2.0f * beat, 0, 0);
        Add("LeftShoulder", 0, 0, -1.5f * beat);
        Add("RightShoulder", 0, 0, 1.5f * beat);

        // swung about the axis that moves each wrist straight off the other palm
        Vector3 Axis(string side, Vector3 away)
        {
            var forearm = _rig.Joint(side + "Hand", _frame, hips) - _rig.Joint(side + "LowerArm", _frame, hips);
            return forearm.Cross(away).Normalized();
        }
        var right = Axis("Right", ClapFacing);
        var left = Axis("Left", -ClapFacing);
        Swing("RightLowerArm", right, Opening(since) * weight);
        Swing("RightHand", right, 0.5f * Opening(since - 0.05f) * weight);
        Swing("LeftLowerArm", left, 0.25f * Opening(since) * weight);
    }

    /// <summary>Small things people do when sitting around (not while in a conversation).</summary>
    private void Fidget()
    {
        if (_time < _nextFidget || _time < _busyUntil || _awake < 0.9f || _thinkingSince is not null || _listening || _time - _lastVoice < 3)
            return;
        _nextFidget = _time + _random.RandfRange(5.5f, 12);
        var pick = _random.Randf();
        if (Manner == Manner.Relaxed && pick < 0.36f)
            pick = _random.RandfRange(0.36f, 1);            // relaxed: no legs swung
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
        var place = Manner == Manner.Relaxed ? (left ? _onThigh : _byHip.Mirrored) : (left ? LeftRest : RightRest).Sized(_size);
        var grip = Grip.Even(Manner == Manner.Relaxed ? 24 : RestCurl);   // at ease, fingers curl over the thigh
        foreach (var (name, overlay) in Overlays)
        {
            if (_shown[name] <= 0.001f || (left ? overlay.Left : overlay.Right) is not { } target
                || (overlay.AtChin ? ByChin(target, hips) : target.Sized(_size)) is not { } placed)
                continue;
            place = place.Towards(placed, _shown[name]);
            var wanted = (left ? overlay.LeftGrip : overlay.RightGrip) ?? (overlay.Curl >= 0 ? (Grip?)Grip.Even(overlay.Curl) : null);
            if (wanted is { } bend)
                grip = grip.Towards(bend, _shown[name]);
        }
        // sending a memo: the phone lifts a little in both hands as the last tap goes in
        if (_shown["phone"] > 0.001f)
            place = place with { Wrist = place.Wrist + Vector3.Up * (0.016f * _size * Envelope((float)(_time - _liftAt), 0.08f, 0.16f, 0.45f) * _shown["phone"]) };
        var (upper, lower, hand) = Reach(side, place, hips);
        _frame[side + "UpperArm"] = Spring(side + "UpperArm").Follow(upper, dt);
        _frame[side + "LowerArm"] = Spring(side + "LowerArm").Follow(lower, dt);
        _frame[side + "Hand"] = Spring(side + "Hand").Follow(hand, dt);

        var now = _grip[side] = _grip[side].Towards(grip, 1 - Mathf.Exp(-dt * 14f));
        foreach (var finger in Fingers)
            foreach (var joint in Joints)
                _frame[side + finger + joint] = Rig.Euler(0, 0, (left ? -1 : 1) * now.Of(finger) * (joint == "Proximal" ? 0.75f : 1.05f));
        // the thumb folds in: about the hand's length, from its front edge down towards the palm
        var thumb = now.Thumb;
        _frame[side + "ThumbMetacarpal"] = Rig.Euler(22 * thumb, 0, 0);
        _frame[side + "ThumbProximal"] = Rig.Euler(28 * thumb, 0, 0);
        _frame[side + "ThumbDistal"] = Rig.Euler(38 * thumb, 0, 0);
    }

    /// <summary>A hand placed by her chin (see <see cref="Overlay"/>), wherever her head is now; null when she has no chin to find.</summary>
    private Hand? ByChin(Hand hand, Vector3 hips)
    {
        if (_chin is not { } chin)
            return null;
        var turn = _rig.Total("Head", _frame);
        var fingers = turn * hand.Fingers.Normalized();
        var tip = _rig.Joint("Head", _frame, hips) + turn * (chin + hand.Wrist);
        return new Hand(tip - fingers * _fingerReach, fingers, turn * hand.Palm, turn * hand.Elbow);
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

    /// <summary>
    /// Texting: the thumbs take turns tapping, a word at a time (a burst of three to seven taps a fifth of a second
    /// apart, a short gap between words), and every few words she stops, glances up to think, and goes back to it.
    /// A tap is quick down, slower up, the hand giving a little under it; the last, as she sends it, is firmer.
    /// </summary>
    private void Text()
    {
        if (_typingSince is { } since && _time >= _nextTap && _time >= _thinkUntil)
        {
            if (_time - since > MaxTyping)
            {
                Play(Moment.Send);                                              // the memo never came: she puts it away, done
                return;
            }
            // mostly the other thumb from the last; now and then the same one twice
            _thumb = _random.Randf() < 0.72f ? (_thumb == "Left" ? "Right" : "Left") : _thumb;
            _tapAt[_thumb] = _time;
            if (--_burst > 0)
                _nextTap = _time + _random.RandfRange(0.13f, 0.24f);
            else if (--_bursts > 0)
            {
                _burst = _random.RandiRange(3, 7);
                _nextTap = _time + _random.RandfRange(0.28f, 0.5f);           // between words
            }
            else
            {
                // a pause to think: she looks up, a little away, then back down to it
                _burst = _random.RandiRange(3, 7);
                _bursts = _random.RandiRange(3, 6);
                var pause = _random.RandfRange(0.9f, 1.6f);
                _thinkUntil = _time + pause;
                _nextTap = _thinkUntil + _random.RandfRange(0.2f, 0.35f);
                var toViewer = Viewer - Head;
                _thinkSpot = Head + new Quaternion(Vector3.Up, Mathf.DegToRad(_side * _random.RandfRange(8, 22))) * toViewer.Normalized() * toViewer.Length()
                             + Vector3.Up * _random.RandfRange(0.05f, 0.25f) * toViewer.Length();
            }
        }
        if (_shown["phone"] <= 0.001f)
            return;
        foreach (var side in Sides)
        {
            var sinceTap = (float)(_time - _tapAt[side]);
            if (sinceTap is < 0 or > 0.2f)
                continue;
            var firm = _time - _firmAt < 0.2 && side == "Right" ? 1.5f : 1f;
            // down in 50 ms, up in 150
            var tap = (sinceTap < 0.05f ? Mathf.SmoothStep(0, 1, sinceTap / 0.05f) : 1 - Mathf.SmoothStep(0, 1, (sinceTap - 0.05f) / 0.15f)) * firm * _shown["phone"];
            Add(side + "ThumbProximal", 13 * tap, 0, 0);
            Add(side + "ThumbDistal", 18 * tap, 0, 0);
            Swing(side + "Hand", Vector3.Right, 1.6f * tap);
        }
    }

    /// <summary>Longest she types before giving up on a memo that never comes (the reply stopped, say).</summary>
    private const double MaxTyping = 40;

    /// <summary>
    /// Writing: small quick strokes from the wrist, the forearm following a little behind, and every so often the
    /// pen lifts and moves along to the next word.
    /// </summary>
    private void Write()
    {
        var since = (float)(_time - _noteAt) - 0.45f;
        if (since is < 0 or > 2.0f)
            return;
        var strength = Envelope(since, 0.15f, 1.75f, 2.0f) * _shown["note"];
        var phase = since * Mathf.Tau * 3.4f;
        var lift = Mathf.Max(0, Mathf.Sin(since * Mathf.Tau * 1.1f));            // the pen leaves the paper between words
        Swing("RightHand", Vector3.Up, 7 * Mathf.Sin(phase) * strength);
        Swing("RightLowerArm", Vector3.Up, 2.5f * Mathf.Sin(phase - 0.7f) * strength);
        Swing("RightHand", Vector3.Right, (-3 - 4 * lift) * strength);
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
