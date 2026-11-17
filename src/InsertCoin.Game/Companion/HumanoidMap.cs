using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace InsertCoin.Game.Companion;

/// <summary>
/// Works out which bone is which in a skeleton that does not say (a plain glTF/GLB instead of a VRM), from the
/// usual naming habits (Mixamo, Unreal, Blender, 3ds Max Biped, Daz, VRoid, MikuMikuDance) and the skeleton's shape.
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

        var bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => new Bone(i, skeleton.GetBoneName(i).Normalize(NormalizationForm.FormKC))).ToList();
        var map = new Dictionary<string, int>();
        int Parent(int bone)
        {
            // twist, roll and cancel helpers sit between the real joints in some rigs
            var parent = skeleton.GetBoneParent(bone);
            while (parent >= 0 && new[] { "twist", "roll", "cancel" }.Any(bones[parent].Core.Contains))
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
        else if (map.TryGetValue("LeftUpperLeg", out var leg) && map.TryGetValue("Head", out var above))
        {
            // where the legs and the spine meet: some rigs hang the legs from a lower body beside the spine (MMD's 下半身)
            var meet = Parent(leg);
            while (meet >= 0 && !Holds(skeleton, meet, above))
                meet = skeleton.GetBoneParent(meet);
            if (meet >= 0)
                map["Hips"] = meet;
        }

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

    private static bool Holds(Skeleton3D skeleton, int bone, int below)
    {
        while (below >= 0 && below != bone)
            below = skeleton.GetBoneParent(below);
        return below == bone;
    }

    /// <summary>
    /// 3ds Max Biped numbers its fingers instead of naming them: Finger0 is the thumb, Finger1 to Finger4 the index
    /// to the little finger, and Finger11, Finger12 the next joints ("Bip001 L Finger2", "Bip001LFinger2_078").
    /// </summary>
    private static readonly string[] BipedFingers = ["Thumb", "Index", "Middle", "Ring", "Little"];

    private static void Fingers(Skeleton3D skeleton, List<Bone> bones, int hand, string side, Dictionary<string, int> map)
    {
        foreach (var root in skeleton.GetBoneChildren(hand))
            foreach (var (finger, words) in FingerWords)
                if (words.Any(bones[root].Core.Contains)
                    || BipedFinger().Match(bones[root].Name) is { Success: true } numbered && BipedFingers[int.Parse(numbered.Groups[1].Value)] == finger)
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
            plain = CentreWord().Replace(plain, " ");
            plain = MmdName().Replace(plain, m => MmdWords[m.Groups[1].Value]);
            return NotLetter().Replace(plain, "").ToLowerInvariant();
        }
    }

    // "Left"/"Right" as a word or camel-case part, or a lone L/R set off by separators or case ("upperarm_l", "L UpperArm", "lShldr").
    [GeneratedRegex(@"(?i:left)|(?<![A-Za-z])[Ll](?![a-z])|(?<=[a-z0-9])L(?![a-z])|(?<![A-Za-z])l(?=[A-Z])")]
    private static partial Regex LeftWord();
    [GeneratedRegex(@"(?i:right)|(?<![A-Za-z])[Rr](?![a-z])|(?<=[a-z0-9])R(?![a-z])|(?<![A-Za-z])r(?=[A-Z])")]
    private static partial Regex RightWord();
    // VRoid's centre mark, a lone C between separators ("J_Bip_C_Spine"), not one inside a word ("Clavicle", "肩C")
    [GeneratedRegex(@"(?<![^\s_.:|-])C(?=[\s_.:|-])")]
    private static partial Regex CentreWord();

    /// <summary>
    /// MikuMikuDance's standard Japanese names, as Blender's MMD Tools keeps them, read only as a whole name with its
    /// side and number ("頭", "左手首", "人指1.L"). Where a model has the legs' D bones, those are what the skin
    /// follows, so they take the plain name and the plain bones a longer one, found only when there are no D bones.
    /// The rest (腕, ひじ, 足 and ひざ, found as the parents of hands and feet; IK bones; MMD Tools' _dummy_ and
    /// _shadow_ helpers) is left as it is, which matches nothing.
    /// </summary>
    private static readonly Dictionary<string, string> MmdWords = new()
    {
        ["頭"] = "head", ["首"] = "neck", ["腰"] = "hips", ["目"] = "eye", ["手首"] = "hand",
        ["手捩"] = "handtwist", ["腕捩"] = "armtwist", ["肩C"] = "shouldercancel", ["腰キャンセル"] = "hipcancel",
        ["足首D"] = "foot", ["足首"] = "footplain", ["足先EX"] = "toe", ["つま先"] = "toe",
        ["親指"] = "thumb", ["人指"] = "index", ["中指"] = "middle", ["薬指"] = "ring", ["小指"] = "little",
    };
    // longer names first, so that 手首 is not read as 首
    [GeneratedRegex(@"^[左右]?(腰キャンセル|足首D|足先EX|つま先|手首|手捩|腕捩|肩C|足首|親指|人指|中指|薬指|小指|頭|首|腰|目)(?=[\d\s._-]*$)")]
    private static partial Regex MmdName();

    [GeneratedRegex(@"(?i)^(mixamorig\d*|bip\d+|valvebiped|bip|def|armature|j_bip|j_adj|j_sec)[\s:_\.\-|]*")]
    private static partial Regex Prefix();
    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex NotLetter();
    // a finger's first bone: "Finger" and one digit, not two ("Finger21" is the middle finger's second joint)
    [GeneratedRegex(@"(?i)finger[\s_]?([0-4])(?!\d)")]
    private static partial Regex BipedFinger();
}
