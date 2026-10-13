using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>
/// A clip of the voice and the words spoken in it: GPT-SoVITS takes the voice's tone from it. <see cref="Mood"/> says
/// which stage direction it suits (happy for [cheer], sad for [worry]…); null for her everyday voice.
/// <see cref="Heard"/>: its words were written down by her ears, not given with it, and nobody has checked them yet
/// (her ears can mishear, and a voice speaks and trains worse on wrong words).
/// </summary>
public sealed record VoiceClip(string File, string Text, string Language, string? Mood = null, double? Seconds = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Heard = false);

/// <summary>
/// A character's voice, kept as <c>voice.json</c> in a folder of its own beside the character: clips of the voice with
/// the words spoken in them, and maybe a model trained on them (GPT-SoVITS weights, someone's download or trained in
/// the cabinet). It speaks with the trained model, or as a clone: the engine's own base model copying the tone of its
/// clips. Files are named relative to the folder.
/// </summary>
public sealed partial record VoiceProfile
{
    public const string FileName = "voice.json";
    /// <summary>
    /// Sampling steps for v3 and v4 models. On the development laptop 16 starts speaking in about 0.9 s; 8 is faster
    /// but sounded metallic to the player (2026-10-03); 32 sounds best and takes about 1.4 s.
    /// </summary>
    public const int DefaultSteps = 16;
    public static readonly string[] AudioFormats = [".wav", ".mp3", ".ogg", ".flac"];
    /// <summary>
    /// The base models a clip can be cloned with: v4 (the default) is clear but makes each sentence (or each part
    /// between commas) whole before it speaks, 1.5–3.5 s; v2ProPlus streams, about 0.4 s, and blends in the tone of
    /// the voice's first four clips. (Qwen3-TTS was tried too, 2026-10-03, and dropped: she sounded like a different
    /// person from take to take, and it can't be trained on this kind of graphics card.)
    /// </summary>
    public static readonly string[] CloneVersions = ["v4", "v2ProPlus"];
    /// <summary>
    /// The clip length a voice's clips are ranked by for speaking with (GPT-SoVITS takes 3 to 10 s): the nearest first.
    /// </summary>
    public const double IdealClipSeconds = 6;

    public string Engine { get; init; } = "gpt-sovits";
    public string Name { get; init; } = "";
    /// <summary>The language the voice speaks (see <see cref="SpokenLanguage"/>); empty until known.</summary>
    public string Language { get; init; } = "";
    /// <summary>A trained model's GPT-SoVITS weights in the folder; both null when it has none.</summary>
    public string? Gpt { get; init; }
    public string? Sovits { get; init; }
    /// <summary>The GPT-SoVITS version the weights were trained for (v2, v2Pro, v2ProPlus, v3, v4), when known.</summary>
    public string? Version { get; init; }
    /// <summary>
    /// Speak as a clone made with this base model (one of <see cref="CloneVersions"/>) even when there is a trained
    /// model; null: with the trained model if there is one, else as a clone made with v4.
    /// </summary>
    public string? CloneWith { get; init; }
    /// <summary>Clips of the voice, the best first: the first everyday one speaks, and v2 models blend in the first few.</summary>
    public IReadOnlyList<VoiceClip> Clips { get; init; } = [];
    public int Steps { get; init; } = DefaultSteps;
    /// <summary>What the voice's author wrote about it (from a README), so its source and licence travel with it.</summary>
    public string? About { get; init; }

    /// <summary>What still stops the voice from speaking, or null when nothing does.</summary>
    [JsonIgnore]
    public string? Missing =>
        Clips.Count == 0 ? "a 3 to 10 second clip of the voice"
        : !Clips.Any(c => c.Text.Trim().Length > 0) ? "the words spoken in its clip"
        : Language.Length == 0 ? "the language it speaks"
        : null;

    /// <summary>It has a trained model of its own.</summary>
    [JsonIgnore]
    public bool Trained => Gpt is not null && Sovits is not null;

    /// <summary>It speaks with its trained model, not as a clone.</summary>
    [JsonIgnore]
    public bool SpeaksTrained => Trained && CloneWith is null;

    /// <summary>
    /// The version it runs on: its trained model's when it speaks with that (null when unknown), else the base model it
    /// is cloned with (v4 unless another of <see cref="CloneVersions"/> is chosen).
    /// </summary>
    [JsonIgnore]
    public string? Runs => SpeaksTrained ? Version : CloneVersions.Contains(CloneWith) ? CloneWith : CloneVersions[0];

    /// <summary>v3 and v4 (and clones made with v4): clearer, and slower to start each sentence, which they don't stream.</summary>
    [JsonIgnore]
    public bool Slow => Runs is "v3" or "v4";

    /// <summary>The clip to speak with for a mood: one labelled with it, else her everyday one.</summary>
    public VoiceClip? ClipFor(string? mood)
    {
        var usable = Clips.Where(c => c.Text.Trim().Length > 0).ToList();
        return usable.FirstOrDefault(c => mood is not null && c.Mood == mood)
               ?? usable.FirstOrDefault(c => c.Mood is null or "calm")
               ?? usable.FirstOrDefault();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,          // Chinese and Japanese stay readable in the file
    };

    public static VoiceProfile? Load(string folder)
    {
        var file = Path.Combine(folder, FileName);
        if (!File.Exists(file))
            return null;
        try
        {
            return JsonSerializer.Deserialize<VoiceProfile>(File.ReadAllText(file), Json) is { } profile ? Upgraded(profile) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Before a voice could have both, a clone kept its base model in Version (2026-10-03).</summary>
    private static VoiceProfile Upgraded(VoiceProfile profile) =>
        profile is { Gpt: null, CloneWith: null, Version: { } version } && CloneVersions.Contains(version)
            ? profile with { CloneWith = version, Version = null }
            : profile;

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, Json));
    }

    // ---- Bringing a voice in ------------------------------------------------------------------------------------------

    /// <summary>A file or folder that holds a voice: a clip, or GPT-SoVITS weights (loose, in a folder, or in a .zip).</summary>
    public static bool LooksLikeVoice(string path)
    {
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Any(f => IsWeights(f) || IsAudio(f));
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".zip")
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => IsWeights(e.FullName));
        }
        return IsAudio(path) || extension is ".ckpt" or ".pth";
    }

    /// <summary>
    /// Brings a voice into <paramref name="folder"/>. A folder or .zip is a whole voice and replaces whatever voice was
    /// there: maybe a downloaded GPT-SoVITS model (a .ckpt and a .pth), and clips, which come along with their words
    /// when a training list (<c>.list</c>), a <c>.lab</c> beside the clip or the clip's file name says them, the best to
    /// speak with first (see <see cref="IdealClipSeconds"/>). Clips without weights (a dataset) make a voice cloned
    /// from them, which can be trained. A clip on its own joins the voice there, keeping its model and clips (she
    /// speaks with it if it is 3 to 10 seconds long), or starts a voice cloned from it. What is still missing (the
    /// words, the language) is listed in <see cref="Missing"/> for the player to fill in. With
    /// <paramref name="hearLater"/>, her ears will write down the words of clips without any (<see cref="Hear"/>), so
    /// every clip of a folder is kept; without, only those with words, or the likeliest few when none has any.
    /// </summary>
    public static VoiceProfile Import(string source, string folder, bool hearLater = false)
    {
        if (Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var unpacked = Path.Combine(Path.GetTempPath(), "insert-coin-import", "voice-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(source, unpacked);
            try
            {
                return FromFolder(unpacked, folder, Path.GetFileNameWithoutExtension(source), hearLater);
            }
            finally
            {
                Directory.Delete(unpacked, recursive: true);
            }
        }
        if (Directory.Exists(source))
            return FromFolder(source, folder, Path.GetFileName(Path.GetFullPath(source).TrimEnd('\\', '/')), hearLater);
        if (IsAudio(source))
            return FromClip(source, folder);
        throw new InvalidDataException("not a voice: drop a GPT-SoVITS model (a folder or .zip with .ckpt and .pth files) or a clip");
    }

    private static VoiceProfile FromFolder(string from, string folder, string name, bool hearLater)
    {
        var files = Directory.EnumerateFiles(from, "*.*", SearchOption.AllDirectories).ToList();
        var gpt = Latest(files.Where(f => Path.GetExtension(f).Equals(".ckpt", StringComparison.OrdinalIgnoreCase)));
        var sovits = Latest(files.Where(f => Path.GetExtension(f).Equals(".pth", StringComparison.OrdinalIgnoreCase) && !TrainingOnly(f)));
        if (gpt is null != sovits is null)
        {
            if (sovits is not null && files.Any(f => f.EndsWith(".index", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("this is an RVC voice model; voices here are GPT-SoVITS ones (a .ckpt and a .pth)");
            throw new InvalidDataException("a GPT-SoVITS voice needs both its .ckpt (GPT) and .pth (SoVITS) files");
        }
        if (gpt is null && !files.Any(IsAudio))
            throw new InvalidDataException("no voice in it: no GPT-SoVITS model (.ckpt and .pth) and no clips");
        // no weights, only clips (a dataset, say): cloned with the engine's base model, and it can be trained

        var transcripts = Transcripts(files);
        // words from a list or a .lab, the mood still from a "[happy] 03.wav" name
        (string Text, string? Language, string? Mood) Said(string clip) =>
            transcripts.TryGetValue(Key(clip), out var said) || transcripts.TryGetValue(Number(clip), out said)
                ? (said.Text, said.Language, said.Mood ?? FromName(clip).Mood)
                : FromName(clip);
        // every clip with its words (training learns from them all), the best to speak with first; when none has its
        // words, the six likeliest, for the player to give one its words; every clip when her ears will write them down
        var heard = files.Where(IsAudio).Select(f => (File: f, Said: Said(f), Seconds: Wav.Seconds(f))).ToList();
        var worded = heard.Where(c => c.Said.Text.Length > 0).ToList();
        var clips = (worded.Count > 0 && !hearLater ? worded : heard)
            .OrderByDescending(c => c.Seconds is >= 3 and <= 10)
            .ThenBy(c => Math.Abs((c.Seconds ?? IdealClipSeconds) - IdealClipSeconds))
            .Take(worded.Count > 0 || hearLater ? int.MaxValue : 6)
            .ToList();

        Clear(folder);
        Directory.CreateDirectory(Path.Combine(folder, "clips"));
        if (gpt is not null && sovits is not null)
        {
            File.Copy(gpt, Path.Combine(folder, Path.GetFileName(gpt)));
            File.Copy(sovits, Path.Combine(folder, Path.GetFileName(sovits)));
        }
        var kept = new List<VoiceClip>();
        foreach (var (file, said, seconds) in clips)
        {
            var target = Unique(Path.Combine(folder, "clips", Path.GetFileName(file)));
            File.Copy(file, target);
            kept.Add(new VoiceClip(Path.GetRelativePath(folder, target), said.Text, said.Language ?? SpokenLanguage.Of(said.Text) ?? "", said.Mood, seconds));
        }
        string? about = null;
        var readme = files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("README", StringComparison.OrdinalIgnoreCase));
        if (readme is not null)
        {
            File.Copy(readme, Path.Combine(folder, Path.GetFileName(readme)));
            var text = File.ReadAllText(readme);
            about = Regex.Replace(text, @"(?s)^---.*?---\s*", "").Trim();          // without the model card's front matter
            about = about.Length > 600 ? about[..600] + "…" : about;
            if (Regex.Match(text, @"(?m)^license:\s*(\S+)") is { Success: true } licence)
                about = $"Licence: {licence.Groups[1].Value}\n{about}".Trim();
        }

        var profile = new VoiceProfile
        {
            Name = Tidy(name),
            Language = Majority(kept.Select(c => c.Language)) ?? "",
            Gpt = gpt is null ? null : Path.GetFileName(gpt),
            Sovits = sovits is null ? null : Path.GetFileName(sovits),
            Version = gpt is null || sovits is null ? null : VersionOf(Path.GetRelativePath(from, sovits)) ?? VersionOf(Path.GetRelativePath(from, gpt)),
            Clips = kept,
            About = about,
        };
        profile.Save(folder);
        return profile;
    }

    private static VoiceProfile FromClip(string clip, string folder)
    {
        var existing = Load(folder);
        if (existing is null)
            Clear(folder);                                  // whatever is there without a voice.json is no voice
        var said = FromName(clip);
        Directory.CreateDirectory(Path.Combine(folder, "clips"));
        var target = Unique(Path.Combine(folder, "clips", Path.GetFileName(clip)));
        File.Copy(clip, target);
        var added = new VoiceClip(Path.GetRelativePath(folder, target), said.Text, said.Language ?? SpokenLanguage.Of(said.Text) ?? existing?.Language ?? "",
                                  said.Mood, Wav.Seconds(target));
        // first, to speak with, when GPT-SoVITS will take it (3 to 10 s); else last, for training
        var speaks = added.Seconds is null or (>= 3 and <= 10);
        var profile = existing is null
            ? new VoiceProfile { Name = Tidy(Path.GetFileNameWithoutExtension(clip)), Language = added.Language, Clips = [added] }
            : existing with
            {
                Clips = speaks ? [added, .. existing.Clips] : [.. existing.Clips, added],
                Language = existing.Language.Length > 0 ? existing.Language : added.Language,
            };
        profile.Save(folder);
        return profile;
    }

    /// <summary>
    /// Writes down the words of the voice's clips that have none, with <paramref name="hear"/> (her ears: the words
    /// spoken in a clip, or null when it can't tell), marking them <see cref="VoiceClip.Heard"/>; the voice's language,
    /// when unknown, becomes the one most of them are in. The voice is read again before it is written, so what the
    /// player changed meanwhile stays. Returns how many clips it wrote down.
    /// </summary>
    public static int Hear(string folder, Func<string, string?> hear)
    {
        if (Load(folder) is not { } before)
            return 0;
        var said = new Dictionary<string, string>();
        foreach (var clip in before.Clips.Where(c => c.Text.Trim().Length == 0))
            if (hear(Path.Combine(folder, clip.File))?.Trim() is { Length: > 0 } words)
                said[clip.File] = words;
        if (said.Count == 0 || Load(folder) is not { } profile)
            return 0;
        var written = 0;
        var clips = profile.Clips.Select(clip =>
        {
            if (clip.Text.Trim().Length > 0 || !said.TryGetValue(clip.File, out var words))
                return clip;
            written++;
            return clip with { Text = words, Language = clip.Language.Length > 0 ? clip.Language : SpokenLanguage.Of(words) ?? "", Heard = true };
        }).ToList();
        (profile with { Clips = clips, Language = profile.Language.Length > 0 ? profile.Language : Majority(clips.Select(c => c.Language)) ?? "" }).Save(folder);
        return written;
    }

    private static bool IsAudio(string path) => AudioFormats.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static bool IsWeights(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ckpt" or ".pth";

    /// <summary>Checkpoints a training run leaves behind that are not the voice itself (discriminator, generator snapshots).</summary>
    private static bool TrainingOnly(string path) => Path.GetFileName(path).StartsWith("D_") || Path.GetFileName(path).StartsWith("G_")
                                                     || Path.GetFileName(path).StartsWith("s2D", StringComparison.OrdinalIgnoreCase);

    /// <summary>Of several saved epochs ("…-e10.ckpt", "…_e8_s200.pth"), the furthest trained.</summary>
    private static string? Latest(IEnumerable<string> files) =>
        files.OrderByDescending(f => Epoch().Match(Path.GetFileNameWithoutExtension(f)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : -1)
             .ThenByDescending(f => new FileInfo(f).Length)
             .FirstOrDefault();

    private static string? VersionOf(string path)
    {
        var lower = path.ToLowerInvariant();
        foreach (var (mark, version) in new[] { ("v2proplus", "v2ProPlus"), ("v2pro", "v2Pro"), ("v4", "v4"), ("v3", "v3"), ("v2", "v2") })
            if (Regex.IsMatch(lower, $@"(^|[^a-z0-9]){mark}($|[^a-z0-9])"))
                return version;
        return null;
    }

    /// <summary>The words of each clip from training lists ("path|speaker|LANG|words"), by clip name and by leading number.</summary>
    private static Dictionary<string, (string Text, string? Language, string? Mood)> Transcripts(List<string> files)
    {
        var said = new Dictionary<string, (string, string?, string?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in files.Where(f => Path.GetExtension(f).Equals(".list", StringComparison.OrdinalIgnoreCase)))
            foreach (var line in File.ReadLines(list))
            {
                var parts = line.Split('|');
                if (parts.Length < 4 || parts[3].Trim().Length == 0)
                    continue;
                var entry = (parts[3].Trim(), LanguageCode(parts[2]), (string?)null);
                said.TryAdd(Key(parts[0]), entry);
                if (Number(parts[0]) is { Length: > 0 } number)
                    said.TryAdd(number, entry);
            }
        foreach (var lab in files.Where(f => Path.GetExtension(f).ToLowerInvariant() is ".lab" or ".txt" && !Path.GetFileName(f).StartsWith("README", StringComparison.OrdinalIgnoreCase)))
            if (File.ReadAllText(lab).Trim() is { Length: > 0 and < 300 } text)
                said.TryAdd(Key(lab), (text, null, null));
        return said;
    }

    private static string Key(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);

    /// <summary>"5_vocals" and "5" are the same clip: slicers number them and add a suffix.</summary>
    private static string Number(string path) => LeadingNumber().Match(Key(path)).Value;

    /// <summary>
    /// Words from a clip's file name, the way shared reference packs often name them ("【开心】今天也要加油哦.wav",
    /// "[happy] Let's go!.wav"); a bracketed mood in front becomes the clip's mood.
    /// </summary>
    private static (string Text, string? Language, string? Mood) FromName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        string? mood = null;
        if (MoodPrefix().Match(name) is { Success: true } m)
        {
            mood = MoodOf(m.Groups[1].Value);
            name = name[m.Length..].Trim();
        }
        var words = SpokenLanguage.Of(name) is { } language && (language != "en" || name.Contains(' ')) && !Regex.IsMatch(name, @"^[\w-]*\d[\w-]*$");
        return (words ? name : "", null, mood);
    }

    private static string? MoodOf(string label) => label.Trim().ToLowerInvariant() switch
    {
        "开心" or "高兴" or "快乐" or "嬉しい" or "楽しい" or "happy" or "joy" => "happy",
        "难过" or "伤心" or "悲伤" or "悲しい" or "sad" => "sad",
        "惊讶" or "吃惊" or "驚き" or "surprised" or "surprise" => "surprised",
        "生气" or "愤怒" or "怒り" or "angry" => "angry",
        "默认" or "平静" or "普通" or "通常" or "calm" or "normal" or "default" or "neutral" => "calm",
        _ => null,
    };

    private static string? LanguageCode(string tag) => tag.Trim().ToLowerInvariant() switch
    {
        "zh" or "all_zh" or "zh-cn" or "chinese" => "zh",
        "ja" or "all_ja" or "jp" or "japanese" => "ja",
        "en" or "english" => "en",
        "ko" or "all_ko" or "korean" => "ko",
        "yue" or "all_yue" => "yue",
        _ => null,
    };

    private static string? Majority(IEnumerable<string> languages) =>
        languages.Where(l => l.Length > 0).GroupBy(l => l).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    private static string Tidy(string name) => Regex.Replace(Regex.Replace(name, @"(?i)gpt[-_ ]?so-?vits[-_ ]?", ""), @"[_]+", " ").Trim(' ', '-');

    private static string Unique(string path)
    {
        var candidate = path;
        for (var n = 2; File.Exists(candidate); n++)
            candidate = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)} ({n}){Path.GetExtension(path)}");
        return candidate;
    }

    private static void Clear(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [GeneratedRegex(@"[_-]e(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Epoch();

    [GeneratedRegex(@"^\d+")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^[【\[(（]([^】\])）]{1,12})[】\])）]")]
    private static partial Regex MoodPrefix();
}
