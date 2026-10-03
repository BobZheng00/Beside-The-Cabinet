using System.Globalization;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>Where a training run is: what it is doing now, and how much of the whole is done (0 to 1).</summary>
public sealed record TrainingProgress(string Stage, double Done);

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

    /// <summary>About how long training takes: a minute of loading and preparing, then about a second per second of speech.</summary>
    public static TimeSpan Estimate(double seconds) => TimeSpan.FromSeconds(60 + seconds);

    /// <summary>
    /// Clips trained on at once: GPT-SoVITS's web UI takes half the card's gigabytes, rounded its way (an "8 GB" card
    /// reports 8188 MB: 4).
    /// </summary>
    public static int BatchSize(int graphicsMemoryMb) => Math.Clamp((int)(graphicsMemoryMb / 1024.0 + 0.4) / 2, 1, 8);

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
