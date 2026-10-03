using System.Globalization;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Training a voice's own GPT-SoVITS model on its clips, the way GPT-SoVITS's web UI does: v2ProPlus, SoVITS for 8
/// epochs then GPT for 15, its defaults. Hana's 55 clips (306 s) took 5.4 minutes on the development laptop's 8 GB
/// card, which SoVITS training all but filled (2026-10-03), and made a model of 313 MB. The cabinet runs
/// <c>train_voice.py</c> (carried inside, see <see cref="Script"/>) with GPT-SoVITS's own Python; this holds what it
/// needs: whether a voice can be trained, how long it should take, the list of clips to hand over, and what the
/// script's progress lines say.
/// </summary>
public static class VoiceTraining
{
    public const string Version = "v2ProPlus";
    public const int SovitsEpochs = 8, GptEpochs = 15;
    /// <summary>GPT-SoVITS asks for a minute of speech at least; five minutes or more sounds better.</summary>
    public const double LeastSeconds = 60, GoodSeconds = 300;
    /// <summary>Clips it learns from: with their words, and not so long that training runs out of graphics memory.</summary>
    public const double ShortestClip = 1, LongestClip = 15;

    public static IReadOnlyList<VoiceClip> Clips(VoiceProfile profile) =>
        profile.Clips.Where(c => c.Text.Trim().Length > 0 && c.Seconds is null or (>= ShortestClip and <= LongestClip)).ToList();

    /// <summary>Seconds of speech it would learn from (a clip of unknown length counts as a typical one).</summary>
    public static double Seconds(VoiceProfile profile) => Clips(profile).Sum(c => c.Seconds ?? VoiceProfile.IdealClipSeconds);

    /// <summary>What stops the voice from being trained, or null when it can be.</summary>
    public static string? Needs(VoiceProfile profile)
    {
        if (profile.Language.Length == 0)
            return "the language it speaks";
        var seconds = Seconds(profile);
        if (seconds < LeastSeconds)
            return $"at least a minute of clips with their words (it has {seconds:0} s in {Clips(profile).Count} {(Clips(profile).Count == 1 ? "clip" : "clips")})";
        return null;
    }

    /// <summary>
    /// About how long training takes: on the development laptop's card a minute of loading and preparing, then about a
    /// second per second of speech. <paramref name="pace"/> scales that to this PC once it has trained a voice.
    /// </summary>
    public static TimeSpan Estimate(double seconds, double? pace = null) => TimeSpan.FromSeconds((60 + seconds) * (pace ?? 1));

    /// <summary>How this PC trains against <see cref="Estimate"/>: 2 when it took twice as long.</summary>
    public static double Pace(TimeSpan took, double seconds) => took.TotalSeconds / Estimate(seconds).TotalSeconds;

    /// <summary>
    /// Time left, at the pace of the step under way: <paramref name="taken"/> to do <paramref name="stepDone"/> of the
    /// whole since it began, with <paramref name="done"/> of the whole done. Null until the step has reported progress
    /// of its own (the steps before training report none, and go at a pace of their own).
    /// </summary>
    public static TimeSpan? Left(TimeSpan taken, double stepDone, double done) =>
        stepDone <= 0 || done >= 1 ? null : taken * ((1 - done) / stepDone);

    /// <summary>
    /// Graphics memory SoVITS training takes, measured with Hana's clips (up to 14 s) on the development laptop: 5.6 GB
    /// at a batch of 2 clips, 6.35 GB at 3 (2026-10-03); and what is left spare for the cabinet and the desktop to grow into.
    /// </summary>
    private const int BaseMb = 4100, ClipMb = 750, SpareMb = 300;

    /// <summary>
    /// Clips trained on at once: as many as fit in the memory other apps leave free, and no more than GPT-SoVITS's web
    /// UI takes (half the card's gigabytes rounded its way: an "8 GB" card reports 8188 MB, so 4). The web UI's choice
    /// alone only fits a card nothing else uses: with 1.8 GB taken by the desktop's apps and the cabinet, training at 4
    /// (or 3) overflowed the card into system memory and slowed to a crawl, 100 s an epoch instead of 25 (2026-10-03).
    /// </summary>
    public static int BatchSize(int totalMb, int usedMb)
    {
        var fits = (totalMb - usedMb - BaseMb - SpareMb) / ClipMb;
        var webUi = (int)(totalMb / 1024.0 + 0.4) / 2;
        return Math.Clamp(Math.Min(fits, webUi), 1, 8);
    }

    /// <summary>Other apps leave too little of the card for even one clip at a time: training will spill into system memory and crawl.</summary>
    public static bool Crowded(int totalMb, int usedMb) => totalMb - usedMb < BaseMb + ClipMb + SpareMb;

    /// <summary>The clips to learn from as GPT-SoVITS's training list: "path|speaker|language|words", one per line.</summary>
    public static string List(VoiceProfile profile, string folder)
    {
        var list = new StringBuilder();
        foreach (var clip in Clips(profile))
        {
            var language = clip.Language.Length > 0 ? clip.Language : profile.Language;
            var words = clip.Text.Replace('|', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
            list.Append(Path.GetFullPath(Path.Combine(folder, clip.File))).Append("|voice|").Append(language).Append('|').Append(words).Append('\n');
        }
        return list.ToString();
    }

    /// <summary>
    /// What a line of the script's output says: "@stage …" names what it is doing, "@progress 0.42" how much of the
    /// whole is done, "@done gpt sovits" the trained files, "@failed …" why it stopped. Other lines are GPT-SoVITS's
    /// own, for the log.
    /// </summary>
    public static (string Kind, string Text)? Read(string line)
    {
        if (!line.StartsWith('@'))
            return null;
        var space = line.IndexOf(' ');
        return space < 0 ? (line[1..], "") : (line[1..space], line[(space + 1)..].Trim());
    }

    public static double Fraction(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var done) ? Math.Clamp(done, 0, 1) : 0;

    /// <summary>
    /// The voice once trained: speaking with its new model (any older trained files are deleted from the folder).
    /// </summary>
    public static VoiceProfile Trained(VoiceProfile profile, string folder, string gpt, string sovits)
    {
        foreach (var old in new[] { profile.Gpt, profile.Sovits })
            if (old is not null && old != gpt && old != sovits && File.Exists(Path.Combine(folder, old)))
                File.Delete(Path.Combine(folder, old));
        return profile with { Gpt = gpt, Sovits = sovits, Version = Version, CloneWith = null };
    }

    /// <summary>The training script, carried inside the cabinet and written out when a voice is trained.</summary>
    public static string Script()
    {
        using var stream = typeof(VoiceTraining).Assembly.GetManifestResourceStream("train_voice.py")
                           ?? throw new InvalidOperationException("train_voice.py is missing from the build");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
