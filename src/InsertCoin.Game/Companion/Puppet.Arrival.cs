using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// A new companion's arrival: hidden behind the cabinet, they peek out from its left edge, look for the player, find
/// them, wave, and spring out onto the stool. Everything is keyed in the character's space (x = their left, y = up,
/// z = their front, the origin at the middle of the seat). The feet are never seen on the floor: they stay behind the
/// cabinet until the jump, so no walk has to be faked.
/// </summary>
internal sealed partial class Puppet
{
    /// <summary>Keyed values between which the curve never overshoots (flat at every turning point).</summary>
    private sealed class Track
    {
        private readonly float[] _t;
        private readonly Vector3[] _v, _m;

        public Track(params (float T, Vector3 V)[] keys)
        {
            _t = [.. keys.Select(k => k.T)];
            _v = [.. keys.Select(k => k.V)];
            _m = new Vector3[keys.Length];
            for (var i = 1; i < keys.Length - 1; i++)
                _m[i] = new Vector3(Slope(i, 0), Slope(i, 1), Slope(i, 2));
        }

        private float Slope(int i, int axis)
        {
            float a = (_v[i][axis] - _v[i - 1][axis]) / (_t[i] - _t[i - 1]), b = (_v[i + 1][axis] - _v[i][axis]) / (_t[i + 1] - _t[i]);
            return a * b <= 0 ? 0 : 2 * a * b / (a + b);
        }

        public Vector3 At(float t)
        {
            if (t <= _t[0])
                return _v[0];
            if (t >= _t[^1])
                return _v[^1];
            var i = Array.FindLastIndex(_t, k => k <= t);
            var h = _t[i + 1] - _t[i];
            var s = (t - _t[i]) / h;
            var (s2, s3) = (s * s, s * s * s);
            return _v[i] * (2 * s3 - 3 * s2 + 1) + _m[i] * (h * (s3 - 2 * s2 + s)) + _v[i + 1] * (-2 * s3 + 3 * s2) + _m[i + 1] * (h * (s3 - s2));
        }
    }

    private sealed class ArrivalPlan
    {
        public double Since;
        public float Land, End;
        public Track Pelvis = null!;
        public readonly Dictionary<string, Track> Body = [], Ankle = [], Pitch = [], Hand = [], Fingers = [], Palms = [], Knee = [];
        public readonly Dictionary<string, Quaternion> Seated = [];
        public Track Seating = null!, Waving = null!, Resting = null!, Direct = null!;
        public Hand Wave;
    }

    private ArrivalPlan? _arrival;
    private float _legLife = 1;                         // how much of the legs' idle life is back since the arrival ended

    /// <summary>How far below the seat's top the floor is, metres (negative).</summary>
    public float Floor { get; init; }

    public bool Arriving => _arrival is not null;

    /// <summary>Whether the arrival can pose them: it bends a neck and a chest, and needs a person's proportions.</summary>
    public bool CanArrive => _rig.Has("Neck") && _rig.Has("Chest") && InsertCoin.Core.ArrivalFit.Fits(
        _rig.RestJoint("Head").Y, _rig.RestJoint("Hips").Y, _rig.RestJoint("LeftFoot").Y, _rig.Leg,
        _rig.RestOffset("RightUpperArm", "LeftUpperArm").Length());

    /// <summary>Not on the stool yet: behind the cabinet or in the air.</summary>
    public bool Away => _arrival is not null && ArrivalNow < _arrival.Land;

    /// <summary>Seconds of the arrival still to come.</summary>
    public double ArrivalLeft => _arrival is null ? 0 : _arrival.End - ArrivalNow;

    /// <summary>They were wanted before they landed and are on the stool now: the view covers it with a flash.</summary>
    public Action? Appeared { get; set; }

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    /// <summary>
    /// Something needs them before they have landed: they are seated on the stool at once. From the landing on, the
    /// arrival is already settling into the seat and simply finishes.
    /// </summary>
    public void Needed()
    {
        if (!Away)
            return;
        _arrival = null;
        _waveAt = -100;
        Interrupt(0);
        _springs.Clear();                   // nothing to swing in from
        Appeared?.Invoke();
        Update(0);                          // posed on the stool now: what asked for them goes by where they are
    }

    /// <summary>Starts the arrival from the top: they vanish behind the cabinet at once.</summary>
    public void Arrive()
    {
        // the moments of it, seconds from the start
        const float peek = 0.5f, out_ = 1.05f, seen = 2.3f, waveUp = 2.6f, waveAt = 2.8f, crouch = 4.5f, push = 4.74f, off = 4.94f, flight = 0.43f, land = off + flight, still = land + 0.75f;

        var plan = new ArrivalPlan { Since = _time, Land = land, End = still };
        var (size, floor) = (_size, Floor);
        var standY = _rig.RestJoint("Hips").Y + floor;
        var ankleY = _rig.RestJoint("LeftFoot").Y + floor;
        var dip = Mathf.Max(0, _rig.RestJoint("Head").Y - 1.33f);        // a tall character keeps their knees bent behind the cabinet, so their head stays below its top

        var hide = V(0.62f, standY - 0.02f * size - dip, -0.34f);
        var peekAt = V(0.44f, standY - 0.03f * size - dip, -0.33f);
        var crouched = peekAt + V(0.02f, -0.10f * size, 0);                     // gathering: down, and a little away from where they are going
        var takeoff = V(0.315f, standY + 0.02f * size - dip * 0.45f, -0.275f);  // the hips well ahead of the feet: the push sends them up and across

        // the flight: a parabola from take-off to the seat
        var keys = new List<(float, Vector3)>
        {
            (0, hide), (peek, hide), (out_, peekAt), (seen, peekAt + V(-0.012f, 0, 0)), (waveUp, peekAt + V(-0.02f, 0.008f, 0)), (crouch, peekAt + V(-0.012f, 0, 0)),
            (push, crouched), (off, takeoff),
        };
        const float gravity = 9.8f;
        var rise = (_seat.Y - takeoff.Y) / flight + gravity * flight / 2;
        foreach (var u in new[] { 0.15f, 0.3f, 0.5f, 0.7f, 0.85f, 0.94f })
        {
            var time = u * flight;
            var at = takeoff.Lerp(_seat, u);
            at.Y = takeoff.Y + rise * time - gravity * time * time / 2;
            keys.Add((off + time, at));
        }
        keys.Add((land, _seat + V(0, 0.006f, 0)));
        keys.Add((land + 0.07f, _seat + V(0, -0.024f, 0)));     // the seat gives under them
        keys.Add((land + 0.2f, _seat + V(0, 0.005f, 0)));
        keys.Add((land + 0.42f, _seat));
        keys.Add((still, _seat));
        plan.Pelvis = new Track([.. keys]);

        // pitch forward, turn to their left, roll to their right (x, y, z), each relative to the bone above it.
        // A jump towards the stool leans into it: the head leads, the body stretched out behind it from the feet
        // (rolling right and pitching forward lean them the way they are going), then it comes upright at the top,
        // and sits back a little to land.
        (float, Vector3)[] Keyed(Vector3 hidden, Vector3 peeking, Vector3 waving, Vector3 crouching, Vector3 launching, Vector3 top, Vector3 landing, Vector3 landed, Vector3 seated) =>
        [
            (0, hidden), (peek, hidden), (out_, peeking), (waveUp, peeking), (waveAt + 0.2f, waving), (crouch, waving), (push, crouching), (off, launching),
            (off + flight * 0.5f, top), (land - 0.02f, landing), (land + 0.1f, landed), (still, seated),
        ];
        // the last key of each is the seated pose of the manner they sit in, so the handover needs nothing from the springs
        Vector3 Sat(string bone) => Manner == Manner.Relaxed && Loose.TryGetValue(bone, out var loose) ? loose : _sit.GetValueOrDefault(bone);
        plan.Body["Hips"] = new Track(Keyed(V(2, 8, 0), V(5, 6, 5), V(3, 10, 5), V(20, -14, 2), V(8, -26, 12), V(2, -16, 4), V(-6, -5, -2), V(12, 0, 0), Sat("Hips")));
        plan.Body["Spine"] = new Track(Keyed(V(2, 0, 0), V(4, 0, 9), V(1, 0, 9), V(12, 0, 3), V(0, 2, 5), V(0, 2, 2), V(4, 0, -2), V(10, 0, 0), Sat("Spine")));
        plan.Body["Chest"] = new Track(Keyed(V(1, 0, 0), V(3, 0, 9), V(0, 0, 8), V(7, 0, 2), V(-2, 2, 3), V(-1, 1, 1), V(3, 0, -1), V(6, 0, 0), Sat("Chest")));
        plan.Body["UpperChest"] = new Track(Keyed(V(0, 0, 0), V(1, 0, 5), V(0, 0, 4), V(3, 0, 1), V(-1, 1, 1), V(-1, 0, 0), V(1, 0, 0), V(2, 0, 0), Sat("UpperChest")));
        plan.Body["Neck"] = new Track(Keyed(V(-2, 0, 0), V(-3, 0, -9), V(-3, 0, -8), V(-10, 0, -2), V(-4, 0, -4), V(-3, 0, -1), V(-5, 0, 1), V(-8, 0, 0), Sat("Neck")));
        plan.Body["Head"] = new Track(Keyed(V(-2, 0, 0), V(-3, 0, -10), V(-3, 0, -6), V(-12, 0, -2), V(-4, 0, -5), V(-3, 0, -1), V(-6, 0, 1), V(-9, 0, 0), Sat("Head")));
        // through the jump the body follows its keys exactly: springs would leave the head behind
        plan.Direct = new Track((0, Vector3.Zero), (crouch - 0.05f, Vector3.Zero), (crouch + 0.12f, Vector3.One), (land + 0.1f, Vector3.One), (land + 0.4f, Vector3.Zero));

        // they land with both feet on the ring, however their legs were before; letting one down comes later, once settled
        foreach (var side in Sides)
            _hang[side] = _hangGoal[side] = _dangling ? 1 : 0;

        // the seated pose they end in
        var seated = new Dictionary<string, Quaternion>();
        foreach (var (bone, degrees) in _sit)
            seated[bone] = Rig.Euler(degrees);
        if (Manner == Manner.Relaxed)
            foreach (var (bone, degrees) in Loose)
                seated[bone] = Rig.Euler(degrees);
        foreach (var side in Sides)
        {
            seated[side + "LowerLeg"] = Rig.Euler(Mathf.Lerp(_kneeOnRing, KneeHanging, _hang[side]), 0, 0);
            seated[side + "Foot"] = Rig.Euler(Mathf.Lerp(30, 20, _hang[side]), 0, 0);
        }

        foreach (var side in Sides)
        {
            var s = side == "Left" ? 1f : -1f;
            var late = side == "Left" ? 0f : 0.03f;                 // one foot leaves a moment after the other, and arrives a moment later
            var ring = _rig.Joint(side + "Foot", seated, _seat);
            plan.Seated[side] = _rig.Total(side + "Foot", seated);
            Vector3 Under(Vector3 pelvis) => V(pelvis.X + 0.06f + s * 0.085f * size, ankleY, pelvis.Z + 0.03f);     // towards the cabinet, out of sight
            var plant = Under(peekAt);
            var leg = _rig.RestOffset(side + "UpperLeg", side + "LowerLeg").Length() + _rig.RestOffset(side + "LowerLeg", side + "Foot").Length();
            // in the air: where the ankle is from the hips at that moment; the legs trail at first, then swing under the body
            Vector3 Air(float u, float back, float below, float fore, float toRing)
            {
                var at = plan.Pelvis.At(off + u * flight) + V(s * 0.085f * size + back, -below * leg, fore);
                at.Y = Mathf.Max(at.Y, ankleY + 0.01f);
                return at.Lerp(ring, toRing);
            }
            plan.Ankle[side] = new Track(
                (0, Under(hide)), (peek, Under(hide)), (out_, plant), (off - 0.03f + late, plant),
                (off + flight * 0.16f + late, Air(0.16f, 0.24f, 0.97f, -0.12f, 0)),
                (off + flight * 0.48f + late, Air(0.48f, 0.12f, 0.80f, 0.02f, 0.15f)),
                (off + flight * 0.78f + late, Air(0.78f, 0.02f, 0.66f, 0.14f, 0.7f)),
                (land + 0.12f + late, ring), (still, ring));
            plan.Pitch[side] = new Track((0, Vector3.Zero), (push, Vector3.Zero), (off - 0.02f + late, V(45, 0, 0)), (off + 0.18f + late, V(32, 0, 0)), (still, V(26, 0, 0)));
            // the knees: towards the cabinet while they are out of sight, forward for the jump
            plan.Knee[side] = new Track((0, V(1, 0, 0.25f)), (crouch, V(1, 0, 0.25f)), (push, V(0.5f, 0, 0.85f)), (off, V(s * 0.16f, 0, 1)), (still, V(s * 0.16f, 0, 1)));
            plan.Hand[side] = new Track(
                (0, V(s * 0.215f, -0.10f, 0.03f) * size), (crouch, V(s * 0.215f, -0.10f, 0.03f) * size),
                (push, V(s * 0.20f, -0.08f, -0.20f) * size),                                         // drawn back to spring
                (off, V(s * 0.20f, 0.16f, 0.20f) * size),                                            // and swung forward and up with the jump
                (off + flight * 0.5f + late, V(s * 0.23f, 0.08f, 0.15f) * size),
                (land + late, V(s * 0.22f, -0.02f, 0.14f) * size), (still, V(s * 0.20f, -0.02f, 0.14f) * size));
            plan.Fingers[side] = new Track((0, V(0, -1, 0.2f)), (crouch, V(0, -1, 0.2f)), (push, V(s * 0.1f, -1, -0.3f)), (off, V(s * 0.15f, -0.1f, 1)),
                (off + flight * 0.5f + late, V(s * 0.3f, -0.35f, 0.85f)), (land + late, V(0, -0.8f, 0.6f)), (still, V(0, -0.8f, 0.6f)));
            plan.Palms[side] = new Track((0, V(-s, 0, 0.15f)), (crouch, V(-s, 0, 0.15f)), (off, V(-s * 0.3f, -0.9f, 0.2f)), (land + late, V(-s * 0.3f, -0.9f, 0.2f)), (still, V(-s * 0.3f, -0.9f, 0.2f)));
        }
        plan.Wave = new Hand(V(-0.45f, 0.58f, 0.17f) * size, V(-0.1f, 1, 0), V(0.5f, 0, 1), V(-0.5f, -1, -0.2f));
        plan.Waving = new Track((0, Vector3.Zero), (waveUp - 0.1f, Vector3.Zero), (waveAt + 0.05f, Vector3.One), (crouch - 0.3f, Vector3.One), (crouch + 0.05f, Vector3.Zero));
        plan.Resting = new Track((0, Vector3.Zero), (land + 0.02f, Vector3.Zero), (still, Vector3.One));
        plan.Seating = new Track((0, Vector3.Zero), (off - 0.02f, Vector3.Zero), (land + 0.1f, Vector3.One));

        Interrupt(still);
        _nextLegChange = _time + still + 8;
        _nextFidget = _time + still + 4;
        _arrival = plan;

        // looking for the player: off to their right, back across, then there they are
        Vector3 Around(float yaw, float pitch)
        {
            var toViewer = Viewer - Head;
            var direction = new Quaternion(Vector3.Up, Mathf.DegToRad(yaw)) * toViewer.Normalized();
            direction = new Quaternion(direction.Cross(Vector3.Up).Normalized(), Mathf.DegToRad(pitch)) * direction;
            return Head + direction * toViewer.Length();
        }
        _face.Show("relaxed", 0.5f, seen);
        After(peek + 0.1, () => Look(Around(-42, -4), 0.8));
        After(peek + 0.95, () => { _face.Blink(); Look(Around(-16, 3), 0.8); });
        After(seen - 0.05, () => { _face.Blink(); Look(Viewer, still); });
        After(seen + 0.1, () => _face.Show("surprised", 0.3f, 0.3));
        After(seen + 0.35, () => _face.Show("happy", 0.62f, land - seen));
        After(waveAt, () => _waveAt = _time);
        After(crouch + 0.02f, () => Look(new Vector3(-0.9f, 0.7f, 0.9f), off + 0.16f - crouch));     // where they are going, at eye height
        After(off + 0.18f, () => Look(Viewer, still));
        After(land + 0.2, () => { _face.Show("happy", 0.55f, 1.4); Look(Viewer, 2.5); });
        After(land + 0.75, () => Nod(1, 3));
    }

    private float ArrivalNow => (float)(_time - _arrival!.Since);

    /// <summary>Through the jump the upper body is on its keys: blends from what the springs made towards them.</summary>
    private void ArrivalDirect()
    {
        var weight = _arrival!.Direct.At(ArrivalNow).X;
        if (weight <= 0)
            return;
        foreach (var bone in _arrival.Body.Keys)
            if (bone != "Hips")
                _frame[bone] = _frame[bone].Slerp(_goal[bone], weight);
    }

    /// <summary>The upper body's goals, keyed; the springs give them their give.</summary>
    private void ArrivalGoals()
    {
        var now = ArrivalNow;
        foreach (var (bone, track) in _arrival!.Body)
            _goal[bone] = Rig.Euler(track.At(now));
    }

    /// <summary>Places the hips and reaches the feet to where they go; returns where the hips are.</summary>
    /// <param name="breath">The seated hips' breathing, faded in as they settle, so the handover doesn't jump by it.</param>
    private Vector3 ArrivalBody(Vector3 breath)
    {
        var plan = _arrival!;
        var now = ArrivalNow;
        _frame["Hips"] = Rig.Euler(plan.Body["Hips"].At(now));
        Spring("Hips").Snap(_frame["Hips"]);        // or the seated pose would take over from wherever the spring lagged to
        var hips = plan.Pelvis.At(now) + breath * plan.Resting.At(now).X;
        foreach (var side in Sides)
        {
            var yaw = plan.Body["Hips"].At(now).Y;
            var flat = Rig.Euler(plan.Pitch[side].At(now) + new Vector3(0, yaw * 0.7f, 0));
            var foot = flat.Slerp(plan.Seated[side], plan.Seating.At(now).X);
            Reach2(side, plan.Ankle[side].At(now), foot, new Quaternion(Vector3.Up, Mathf.DegToRad(yaw)) * plan.Knee[side].At(now), hips);
        }
        return hips;
    }

    /// <summary>Two-bone reach for a leg: the ankle lands on the target, the knee bends towards the hint, the foot lies as asked.</summary>
    private void Reach2(string side, Vector3 ankle, Quaternion foot, Vector3 knee, Vector3 hips)
    {
        string upper = side + "UpperLeg", lower = side + "LowerLeg", heel = side + "Foot";
        var hip = _rig.Joint(upper, _frame, hips);
        var along = _rig.RestOffset(upper, lower);
        float a = along.Length(), b = _rig.RestOffset(lower, heel).Length();
        var to = ankle - hip;
        var reach = Mathf.Clamp(to.Length(), 0.05f, a + b - 0.002f);
        var aim = to.Normalized();
        var cos = Mathf.Clamp((a * a + reach * reach - b * b) / (2 * a * reach), -1, 1);
        var bend = (knee - aim * knee.Dot(aim)).Normalized();
        var upperDir = aim * cos + bend * Mathf.Sqrt(1 - cos * cos);
        var lowerDir = (aim * reach - upperDir * a).Normalized();
        var upperTotal = new Quaternion(along.Normalized(), upperDir).Normalized();
        var lowerTotal = (new Quaternion(upperDir, lowerDir) * upperTotal).Normalized();
        _frame[upper] = _rig.ParentTotal(upper, _frame).Inverse() * upperTotal;
        _frame[lower] = upperTotal.Inverse() * lowerTotal;
        _frame[heel] = lowerTotal.Inverse() * foot;
        // the last of the settling hands the legs over to the seated pose exactly, so nothing jumps when the arrival ends
        var seated = _arrival!.Resting.At(ArrivalNow).X;
        foreach (var bone in new[] { upper, lower, heel })
        {
            _frame[bone] = _frame[bone].Slerp(_goal[bone], seated);
            Spring(bone).Snap(_frame[bone]);
        }
    }

    /// <summary>Where a hand goes during the arrival: hanging, up to wave, thrown with the jump, then into the lap.</summary>
    private Hand ArrivalHand(string side, Vector3 hips, Hand rest)
    {
        var plan = _arrival!;
        var now = ArrivalNow;
        var s = side == "Left" ? 1f : -1f;
        var hanging = new Hand(hips + plan.Hand[side].At(now), plan.Fingers[side].At(now).Normalized(), plan.Palms[side].At(now).Normalized(), V(s * 0.35f, -0.6f, -0.7f));
        var place = hanging;
        if (side == "Right")
            place = place.Towards(plan.Wave with { Wrist = hips + plan.Wave.Wrist - _seat }, plan.Waving.At(now).X);
        return place.Towards(rest, plan.Resting.At(now).X);
    }

    private void ArrivalEnds()
    {
        if (_arrival is not null && ArrivalNow >= _arrival.End)
            _arrival = null;
    }
}
