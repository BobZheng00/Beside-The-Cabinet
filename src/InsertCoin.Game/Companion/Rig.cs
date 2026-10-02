using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// A pose: Euler degrees per humanoid bone, in the character's own axes (x = her left, y = up, z = her front) and
/// relative to the parent bone. Written against the VRM humanoid names, so it fits any VRM whatever its bone axes.
/// </summary>
internal sealed class Pose : Dictionary<string, Vector3>
{
    public Pose Set(string bone, float x, float y, float z)
    {
        this[bone] = new Vector3(x, y, z);
        return this;
    }

    /// <summary>The left bone as given, the right one mirrored.</summary>
    public Pose Both(string bone, float x, float y, float z)
    {
        this["Left" + bone] = new Vector3(x, y, z);
        this["Right" + bone] = new Vector3(x, -y, -z);
        return this;
    }
}

/// <summary>
/// Poses a skeleton in the character's own terms. Whatever the file's units, facing, bone names, bone axes or rest
/// pose (T or A), callers see a figure in metres that faces +z, with its arms straight out to the sides and its
/// legs straight down at rest, and give each bone's rotation from that rest, in the character's axes, relative to
/// its parent.
/// </summary>
internal sealed class Rig
{
    private const float ReferenceLeg = 0.79f;             // hip joint to ankle of the figure the poses were written for, metres

    private readonly Skeleton3D _skeleton;
    private readonly Dictionary<string, int> _index = [];
    private readonly Dictionary<int, Vector3> _shift = [];
    private readonly string[] _name;                      // each bone's humanoid name where it has one, else its own
    private readonly Transform3D _toCharacter;            // skeleton space to character space
    private readonly Quaternion _turn;                    // the rotation in that
    private readonly Quaternion[] _raw;                   // global rest rotation of each bone, as the file has it
    private readonly Quaternion[] _rest;                  // the same in character space, limbs straightened
    private readonly Vector3[] _joint;                    // rest joint positions in character space, limbs straightened

    /// <param name="humanoid">Humanoid bone names to bone indices, for a skeleton that does not use those names itself.</param>
    /// <param name="toCharacter">Takes the skeleton's space to character space (metres, facing +z).</param>
    public Rig(Skeleton3D skeleton, IReadOnlyDictionary<string, int>? humanoid, Transform3D toCharacter)
    {
        _skeleton = skeleton;
        _toCharacter = toCharacter;
        _turn = toCharacter.Basis.GetRotationQuaternion();
        var count = skeleton.GetBoneCount();
        _name = [.. Enumerable.Range(0, count).Select(skeleton.GetBoneName)];
        foreach (var (name, bone) in humanoid ?? new Dictionary<string, int>())
            _name[bone] = name;
        _raw = new Quaternion[count];
        _rest = new Quaternion[count];
        _joint = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            _index[_name[i]] = i;
            _raw[i] = skeleton.GetBoneGlobalRest(i).Basis.GetRotationQuaternion();
        }
        Straighten();
    }

    /// <summary>
    /// Works out the rest pose the poses are written against: arms along x, legs along -y. A model resting in an
    /// A-pose has its limbs turned there; everything hanging off them (fingers, twist bones) turns with them.
    /// </summary>
    private void Straighten()
    {
        var count = _raw.Length;
        var aims = new Dictionary<int, (int Child, Vector3 Direction)>();
        void Aim(string bone, string child, Vector3 direction)
        {
            if (_index.TryGetValue(bone, out var b) && _index.TryGetValue(child, out var c))
                aims[b] = (c, direction);
        }
        foreach (var (side, outwards) in new[] { ("Left", Vector3.Right), ("Right", Vector3.Left) })
        {
            Aim(side + "UpperArm", side + "LowerArm", outwards);
            Aim(side + "LowerArm", side + "Hand", outwards);
            Aim(side + "Hand", side + "MiddleProximal", outwards);
            Aim(side + "UpperLeg", side + "LowerLeg", Vector3.Down);
            Aim(side + "LowerLeg", side + "Foot", Vector3.Down);
        }

        var at = new Vector3[count];
        var depth = new int[count];
        for (var i = 0; i < count; i++)
        {
            at[i] = _toCharacter * _skeleton.GetBoneGlobalRest(i).Origin;
            for (var b = _skeleton.GetBoneParent(i); b >= 0; b = _skeleton.GetBoneParent(b))
                depth[i]++;
        }
        var turned = new Quaternion[count];                 // how far each bone is turned from the file's rest pose
        foreach (var i in Enumerable.Range(0, count).OrderBy(i => depth[i]))        // parents before children
        {
            var parent = _skeleton.GetBoneParent(i);
            var inherited = parent >= 0 ? turned[parent] : Quaternion.Identity;
            _joint[i] = parent >= 0 ? _joint[parent] + inherited * (at[i] - at[parent]) : at[i];
            turned[i] = inherited;
            if (aims.TryGetValue(i, out var aim))
            {
                var now = inherited * (at[aim.Child] - at[i]);
                if (now.Length() > 1e-5f && now.Normalized().Dot(aim.Direction) > -0.99f)
                    turned[i] = (new Quaternion(now.Normalized(), aim.Direction) * inherited).Normalized();
            }
            _rest[i] = (turned[i] * _turn * _raw[i]).Normalized();
        }
    }

    public static Quaternion Euler(Vector3 degrees) => Quaternion.FromEuler(degrees * (Mathf.Pi / 180f));
    public static Quaternion Euler(float x, float y, float z) => Euler(new Vector3(x, y, z));

    public bool Has(string bone) => _index.ContainsKey(bone);

    /// <summary>How big the figure is next to the one the poses were written for (by leg length).</summary>
    public float Size => (RestOffset("LeftUpperLeg", "LeftLowerLeg").Length() + RestOffset("LeftLowerLeg", "LeftFoot").Length()) / ReferenceLeg;

    /// <summary>Rotates a bone away from its rest direction, relative to its parent.</summary>
    public void Rotate(string bone, Quaternion rotation)
    {
        if (!_index.TryGetValue(bone, out var i))
            return;
        var parent = _skeleton.GetBoneParent(i);
        var parentRest = parent >= 0 ? _rest[parent] : _turn;
        _skeleton.SetBonePoseRotation(i, (parentRest.Inverse() * rotation * _rest[i]).Normalized());
    }

    /// <summary>Puts the hips at a point in character space.</summary>
    public void PlaceHips(Vector3 position)
    {
        var hips = _index["Hips"];
        var parent = _skeleton.GetBoneParent(hips);
        var frame = parent >= 0 ? _skeleton.GetBoneGlobalRest(parent) : Transform3D.Identity;
        _skeleton.SetBonePosePosition(hips, frame.AffineInverse() * (_toCharacter.AffineInverse() * position));
    }

    /// <summary>Moves a bone's joint away from its rest place (offset in the character's axes), as a chest rising.</summary>
    public void Shift(string bone, Vector3 offset)
    {
        if (!_index.TryGetValue(bone, out var i))
            return;
        _shift[i] = offset;
        var parent = _skeleton.GetBoneParent(i);
        var parentRest = parent >= 0 ? _raw[parent] : Quaternion.Identity;
        _skeleton.SetBonePosePosition(i, _skeleton.GetBoneRest(i).Origin + parentRest.Inverse() * (_toCharacter.Basis.Inverse() * offset));
    }

    /// <summary>A bone's total rotation from rest, given each bone's rotation relative to its parent.</summary>
    public Quaternion Total(string bone, IReadOnlyDictionary<string, Quaternion> frame) => Total(_index[bone], frame);

    /// <summary>The total rotation of a bone's parent: the frame the bone's own rotation is expressed in.</summary>
    public Quaternion ParentTotal(string bone, IReadOnlyDictionary<string, Quaternion> frame) =>
        Total(_skeleton.GetBoneParent(_index[bone]), frame);

    private Quaternion Total(int bone, IReadOnlyDictionary<string, Quaternion> frame)
    {
        var total = Quaternion.Identity;
        for (var i = bone; i >= 0; i = _skeleton.GetBoneParent(i))
            if (frame.TryGetValue(_name[i], out var rotation))
                total = rotation * total;
        return total.Normalized();
    }

    /// <summary>Where a bone's joint ends up for a pose that has not been applied yet (hips placed at the given point).</summary>
    public Vector3 Joint(string bone, IReadOnlyDictionary<string, Quaternion> frame, Vector3 hips)
    {
        var chain = new List<int>();
        var top = _index["Hips"];
        for (var i = _index[bone]; i >= 0 && i != top; i = _skeleton.GetBoneParent(i))
            chain.Add(i);
        var position = hips;
        for (var k = chain.Count - 1; k >= 0; k--)
        {
            var parent = _skeleton.GetBoneParent(chain[k]);
            position += Total(parent, frame) * (_joint[chain[k]] - _joint[parent] + _shift.GetValueOrDefault(chain[k]));
        }
        return position;
    }

    /// <summary>Where a bone's joint is in the rest pose, in character space.</summary>
    public Vector3 RestJoint(string bone) => _joint[_index[bone]];

    /// <summary>Rest-pose vector from a bone's joint to another's, in the character's axes.</summary>
    public Vector3 RestOffset(string from, string to) => _joint[_index[to]] - _joint[_index[from]];

    /// <summary>Where a bone currently is, in character space.</summary>
    public Vector3 PositionOf(string bone) => _toCharacter * _skeleton.GetBoneGlobalPose(_index[bone]).Origin;
}

/// <summary>
/// A rotation that follows a target like a damped spring instead of jumping to it: it accelerates, can swing past
/// and settles. Frequency sets how fast it gets there; damping below 1 lets it overshoot (follow-through).
/// </summary>
internal sealed class RotationSpring(float frequency, float damping)
{
    private readonly float _k1 = damping / (Mathf.Pi * frequency);
    private readonly float _k2 = 1 / (Mathf.Tau * frequency * Mathf.Tau * frequency);
    private Quaternion _now = Quaternion.Identity;
    private Vector3 _velocity;                  // rotation vector per second, in the parent's frame
    private bool _started;

    public Quaternion Follow(Quaternion target, float dt)
    {
        target = target.Normalized();
        if (!_started)
        {
            _started = true;
            return _now = target;
        }
        var speed = _velocity.Length();
        if (speed > 1e-6f)
            _now = (new Quaternion(_velocity / speed, speed * dt) * _now).Normalized();

        var delta = target * _now.Inverse();
        if (delta.W < 0)
            delta = -delta;                     // the short way round
        var half = Mathf.Acos(Mathf.Clamp(delta.W, -1, 1));
        var sin = Mathf.Sin(half);
        var error = sin > 1e-5f ? new Vector3(delta.X, delta.Y, delta.Z) / sin * (2 * half) : Vector3.Zero;

        var k2 = Mathf.Max(_k2, Mathf.Max(dt * dt / 2 + dt * _k1 / 2, dt * _k1));   // stays stable on a slow frame
        _velocity += dt * (error - _k1 * _velocity) / k2;
        return _now;
    }
}
