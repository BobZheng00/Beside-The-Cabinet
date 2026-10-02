using System.Text.RegularExpressions;
using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Works out which bone is which in a skeleton that does not say (a plain glTF/GLB instead of a VRM), from the
/// usual naming habits (Mixamo, Unreal, Blender, 3ds Max Biped, Daz) and the skeleton's shape.
/// The result maps VRM humanoid names ("LeftUpperArm") to bone indices.
/// </summary>
internal static partial class HumanoidMap
{
    /// <summary>Bones the companion cannot do without.</summary>
    public static readonly string[] Required =
    [
        "Hips", "Spine", "Head",
        "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    ];

    private static readonly (string Finger, string[] Words)[] FingerWords =
    [
        ("Thumb", ["thumb"]), ("Index", ["index"]), ("Middle", ["middle", "mid"]), ("Ring", ["ring"]), ("Little", ["pinky", "little", "pinkie"]),
    ];

    /// <summary>Null when the skeleton already uses humanoid names (a VRM); otherwise the best guess, possibly incomplete.</summary>
    public static Dictionary<string, int>? Guess(Skeleton3D skeleton)
    {
        if (skeleton.FindBone("Hips") >= 0 && skeleton.FindBone("LeftUpperArm") >= 0)
            return null;

        var bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => new Bone(i, skeleton.GetBoneName(i))).ToList();
        var map = new Dictionary<string, int>();
        int Parent(int bone)
        {
            // twist and roll helpers sit between the real joints in some rigs
            var parent = skeleton.GetBoneParent(bone);
            while (parent >= 0 && (bones[parent].Core.Contains("twist") || bones[parent].Core.Contains("roll")))
                parent = skeleton.GetBoneParent(parent);
            return parent;
        }
        Bone? Find(string? side, params string[] words) =>
            bones.FirstOrDefault(b => b.Side == side && words.Any(w => b.Core == w))
            ?? bones.FirstOrDefault(b => b.Side == side && words.Any(w => b.Core.StartsWith(w) && !b.Core.Contains("twist") && !b.Core.Contains("end")));

        if (Find(null, "head") is { } head)
            map["Head"] = head.Index;
        foreach (var side in new[] { "Left", "Right" })
        {
            // Limbs from their ends inwards: the names of hands and feet are the ones rigs agree on.
            if (Find(side, "hand", "wrist") is { } hand)
            {
                map[side + "Hand"] = hand.Index;
                var lower = Parent(hand.Index);
                var upper = lower >= 0 ? Parent(lower) : -1;
                if (lower >= 0) map[side + "LowerArm"] = lower;
                if (upper >= 0) map[side + "UpperArm"] = upper;
                var shoulder = upper >= 0 ? Parent(upper) : -1;
                if (shoulder >= 0 && bones[shoulder].Side == side)
                    map[side + "Shoulder"] = shoulder;
                Fingers(skeleton, bones, hand.Index, side, map);
            }
            if (Find(side, "foot", "ankle") is { } foot)
            {
                map[side + "Foot"] = foot.Index;
                var lower = Parent(foot.Index);
                var upper = lower >= 0 ? Parent(lower) : -1;
                if (lower >= 0) map[side + "LowerLeg"] = lower;
                if (upper >= 0) map[side + "UpperLeg"] = upper;
                if (skeleton.GetBoneChildren(foot.Index).Select(c => bones[c]).FirstOrDefault(b => b.Core.Contains("toe") || b.Core.Contains("ball")) is { } toes)
                    map[side + "Toes"] = toes.Index;
            }
            if (Find(side, "eye") is { } eye)
                map[side + "Eye"] = eye.Index;
        }

        // Hips: by name, else whatever the legs hang from.
        if (Find(null, "hips", "hip", "pelvis") is { } hips)
            map["Hips"] = hips.Index;
        else if (map.TryGetValue("LeftUpperLeg", out var leg) && Parent(leg) >= 0)
            map["Hips"] = Parent(leg);

        // The spine is whatever lies between hips and head, however many bones and whatever they are called.
        if (map.TryGetValue("Hips", out var bottom) && map.TryGetValue("Head", out var top))
        {
            var chain = new List<int>();
            for (var bone = skeleton.GetBoneParent(top); bone >= 0 && bone != bottom; bone = skeleton.GetBoneParent(bone))
                chain.Insert(0, bone);
            if (chain.Count > 1 && bones[chain[^1]].Core.Contains("neck") || chain.Count > 3)
            {
                map["Neck"] = chain[^1];
                chain.RemoveAt(chain.Count - 1);
            }
            foreach (var (name, bone) in new[] { "Spine", "Chest", "UpperChest" }.Zip(chain))
                map[name] = bone;
        }
        return map;
    }

    private static void Fingers(Skeleton3D skeleton, List<Bone> bones, int hand, string side, Dictionary<string, int> map)
    {
        foreach (var root in skeleton.GetBoneChildren(hand))
            foreach (var (finger, words) in FingerWords)
                if (words.Any(bones[root].Core.Contains))
                {
                    var bone = root;
                    foreach (var joint in finger == "Thumb" ? new[] { "Metacarpal", "Proximal", "Distal" } : ["Proximal", "Intermediate", "Distal"])
                    {
                        map[side + finger + joint] = bone;
                        var next = skeleton.GetBoneChildren(bone);
                        if (next.Length == 0)
                            break;
                        bone = next[0];
                    }
                }
    }

    /// <summary>A bone's name split into which side it is on and what is left, lower-case letters and digits only.</summary>
    private sealed record Bone(int Index, string Name)
    {
        public string? Side { get; } = SideOf(Name);
        public string Core { get; } = CoreOf(Name);

        private static string? SideOf(string name)
        {
            if (LeftWord().IsMatch(name) || name.Contains('左')) return "Left";
            if (RightWord().IsMatch(name) || name.Contains('右')) return "Right";
            return null;
        }

        private static string CoreOf(string name)
        {
            var plain = Prefix().Replace(name, "");
            plain = LeftWord().Replace(plain, " ");
            plain = RightWord().Replace(plain, " ");
            return NotLetter().Replace(plain, "").ToLowerInvariant();
        }
    }

    // "Left"/"Right" as a word or camel-case part, or a lone L/R set off by separators or case ("upperarm_l", "L UpperArm", "lShldr").
    [GeneratedRegex(@"(?i:left)|(?<![A-Za-z])[Ll](?![a-z])|(?<=[a-z0-9])L(?![a-z])|(?<![A-Za-z])l(?=[A-Z])")]
    private static partial Regex LeftWord();
    [GeneratedRegex(@"(?i:right)|(?<![A-Za-z])[Rr](?![a-z])|(?<=[a-z0-9])R(?![a-z])|(?<![A-Za-z])r(?=[A-Z])")]
    private static partial Regex RightWord();
    [GeneratedRegex(@"(?i)^(mixamorig\d*|bip\d+|valvebiped|bip|def|armature|j_bip|j_adj|j_sec)[\s:_\.\-|]*")]
    private static partial Regex Prefix();
    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex NotLetter();
}
