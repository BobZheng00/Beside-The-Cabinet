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

/// <summary>Writes rotations given in the character's axes onto a skeleton whose bones have arbitrary rest axes.</summary>
internal sealed class Rig
{
    private readonly Skeleton3D _skeleton;
    private readonly Dictionary<string, int> _index = [];
    private readonly Dictionary<int, Vector3> _shift = [];
    private readonly Quaternion[] _rest;                  // global rest rotation of each bone

    public Rig(Skeleton3D skeleton)
    {
        _skeleton = skeleton;
        _rest = new Quaternion[skeleton.GetBoneCount()];
        for (var i = 0; i < _rest.Length; i++)
        {
            _index[skeleton.GetBoneName(i)] = i;
            _rest[i] = skeleton.GetBoneGlobalRest(i).Basis.GetRotationQuaternion();
        }
    }

    public static Quaternion Euler(Vector3 degrees) => Quaternion.FromEuler(degrees * (Mathf.Pi / 180f));
    public static Quaternion Euler(float x, float y, float z) => Euler(new Vector3(x, y, z));

    public bool Has(string bone) => _index.ContainsKey(bone);

    /// <summary>Rotates a bone away from its rest direction, relative to its parent.</summary>
    public void Rotate(string bone, Quaternion rotation)
    {
        if (!_index.TryGetValue(bone, out var i))
            return;
        var parent = _skeleton.GetBoneParent(i);
        var parentRest = parent >= 0 ? _rest[parent] : Quaternion.Identity;
        _skeleton.SetBonePoseRotation(i, (parentRest.Inverse() * rotation * _rest[i]).Normalized());
    }

    /// <summary>Puts the hips at a point in the skeleton's space.</summary>
    public void PlaceHips(Vector3 position)
    {
        var hips = _index["Hips"];
        var parent = _skeleton.GetBoneParent(hips);
        var frame = parent >= 0 ? _skeleton.GetBoneGlobalRest(parent) : Transform3D.Identity;
        _skeleton.SetBonePosePosition(hips, frame.AffineInverse() * position);
    }

    /// <summary>Moves a bone's joint away from its rest place (offset in the character's axes), as a chest rising.</summary>
    public void Shift(string bone, Vector3 offset)
    {
        if (!_index.TryGetValue(bone, out var i))
            return;
        _shift[i] = offset;
        var parent = _skeleton.GetBoneParent(i);
        var parentRest = parent >= 0 ? _rest[parent] : Quaternion.Identity;
        _skeleton.SetBonePosePosition(i, _skeleton.GetBoneRest(i).Origin + parentRest.Inverse() * offset);
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
            if (frame.TryGetValue(_skeleton.GetBoneName(i), out var rotation))
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
            position += Total(parent, frame) * (RestPosition(chain[k]) - RestPosition(parent) + _shift.GetValueOrDefault(chain[k]));
        }
        return position;
    }

    /// <summary>Where a bone's joint is in the rest pose, in the skeleton's space.</summary>
    public Vector3 RestJoint(string bone) => RestPosition(_index[bone]);

    /// <summary>Rest-pose vector from a bone's joint to another's, in the character's axes.</summary>
    public Vector3 RestOffset(string from, string to) => RestPosition(_index[to]) - RestPosition(_index[from]);

    private Vector3 RestPosition(int bone) => _skeleton.GetBoneGlobalRest(bone).Origin;

    /// <summary>Where a bone currently is, in the skeleton's space.</summary>
    public Vector3 PositionOf(string bone) => _skeleton.GetBoneGlobalPose(_index[bone]).Origin;
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
