using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>
/// A clip of the voice and the words spoken in it: the engine takes the voice's tone from it. <see cref="Mood"/> says
/// which stage direction it suits (happy for [cheer], sad for [worry]…); null for her everyday voice.
/// </summary>
public sealed record VoiceClip(string File, string Text, string Language, string? Mood = null, double? Seconds = null);

/// <summary>A base model a clip can be cloned with: an engine (see <see cref="VoiceEngines"/>) and which of its models.</summary>
public sealed record CloneModel(string Engine, string Version);

/// <summary>
/// A character's voice, kept as <c>voice.json</c> in a folder of its own beside the character: either a model someone
/// trained on the character's lines (GPT-SoVITS weights plus clips), or just a clip, which an engine's own base model
/// clones. Files are named relative to the folder.
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
    /// The base models a clip can be cloned with, the first of each engine its default. GPT-SoVITS v4 is clear but
    /// makes each sentence (or each part between commas) whole before it speaks: 1.5–3.5 s; v2ProPlus streams, about
    /// 0.4 s. Qwen3-TTS 1.7B streams too, about 0.35 s, and read hard Japanese lines more surely (measured
    /// 2026-10-03). Its 0.6B sounded like a different person from line to line to the player, so it isn't offered.
    /// </summary>
    public static readonly CloneModel[] CloneModels =
    [
        new(VoiceEngines.GptSoVits, "v4"), new(VoiceEngines.GptSoVits, "v2ProPlus"), new(VoiceEngines.Qwen, "1.7B"),
    ];

    /// <summary>The engine a clone speaks with (see <see cref="VoiceEngines"/>); a trained model always speaks with GPT-SoVITS.</summary>
    public string Engine { get; init; } = VoiceEngines.GptSoVits;
    public string Name { get; init; } = "";
    /// <summary>The language the voice speaks (see <see cref="SpokenLanguage"/>); empty until known.</summary>
    public string Language { get; init; } = "";
    /// <summary>GPT-SoVITS weights in the folder; both null for a voice cloned from a clip with the engine's base model.</summary>
    public string? Gpt { get; init; }
    public string? Sovits { get; init; }
    /// <summary>
    /// The GPT-SoVITS version the weights were trained for (v2, v2Pro, v2ProPlus, v3, v4), when the files say; for a
    /// clone, the base model it is cloned with (see <see cref="CloneModels"/>; null is its engine's first).
    /// </summary>
    public string? Version { get; init; }
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

    /// <summary>The engine it speaks with: a trained model's is GPT-SoVITS; a clone's, the one chosen.</summary>
    [JsonIgnore]
    public string SpeaksWith => Gpt is null && VoiceEngines.All.Contains(Engine) ? Engine : VoiceEngines.GptSoVits;

    /// <summary>The model it runs on: its weights' version when known, or for a clone the base model chosen.</summary>
    [JsonIgnore]
    public string? Runs => ClonedWith?.Version ?? Version;

    /// <summary>For a clone, the base model it is cloned with (its engine's first when none of its models is chosen).</summary>
    [JsonIgnore]
    public CloneModel? ClonedWith => Gpt is not null ? null
        : CloneModels.FirstOrDefault(m => m.Engine == SpeaksWith && m.Version == Version) ?? CloneModels.First(m => m.Engine == SpeaksWith);

    /// <summary>GPT-SoVITS v3 and v4 (and clones made with v4): clearer, and slower to start each sentence, which they don't stream.</summary>
    [JsonIgnore]
    public bool Slow => SpeaksWith == VoiceEngines.GptSoVits && Runs is "v3" or "v4";

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
            return JsonSerializer.Deserialize<VoiceProfile>(File.ReadAllText(file), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
    /// Brings a voice into <paramref name="folder"/>. A downloaded GPT-SoVITS model (a folder or .zip with a .ckpt
    /// and a .pth) replaces whatever voice was there; its best clips come along with their words when a training
    /// list (<c>.list</c>), a <c>.lab</c> beside the clip or the clip's file name says them. A folder of clips without
    /// weights (a dataset) becomes a voice cloned from its best clips. A clip on its own is added to a model that has
    /// no clip yet, or else becomes a voice cloned from that clip. What is still missing (the
    /// words, the language) is listed in <see cref="Missing"/> for the player to fill in.
    /// </summary>
    public static VoiceProfile Import(string source, string folder)
    {
        if (Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var unpacked = Path.Combine(Path.GetTempPath(), "insert-coin-import", "voice-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(source, unpacked);
            try
            {
                return FromFolder(unpacked, folder, Path.GetFileNameWithoutExtension(source));
            }
            finally
            {
                Directory.Delete(unpacked, recursive: true);
            }
        }
        if (Directory.Exists(source))
            return FromFolder(source, folder, Path.GetFileName(Path.GetFullPath(source).TrimEnd('\\', '/')));
        if (IsAudio(source))
            return FromClip(source, folder);
        throw new InvalidDataException("not a voice: drop a GPT-SoVITS model (a folder or .zip with .ckpt and .pth files) or a clip");
    }

    private static VoiceProfile FromFolder(string from, string folder, string name)
    {
        var files = Directory.EnumerateFiles(from, "*.*", SearchOption.AllDirectories).ToList();
        var gpt = Latest(files.Where(f => Path.GetExtension(f).Equals(".ckpt", StringComparison.OrdinalIgnoreCase)));
        var sovits = Latest(files.Where(f => Path.GetExtension(f).Equals(".pth", StringComparison.OrdinalIgnoreCase) && !TrainingOnly(f)));
        if (gpt is null != sovits is null)
        {
            if (sovits is not null && files.Any(f => f.EndsWith(".index", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("this is an RVC voice model; she speaks with GPT-SoVITS voices (a .ckpt and a .pth)");
            throw new InvalidDataException("a GPT-SoVITS voice needs both its .ckpt (GPT) and .pth (SoVITS) files");
        }
        if (gpt is null && !files.Any(IsAudio))
            throw new InvalidDataException("no voice in it: no GPT-SoVITS model (.ckpt and .pth) and no clips");
        // no weights, only clips (a dataset, say): cloned with the engine's base model from its best clips

        var transcripts = Transcripts(files);
        (string Text, string? Language, string? Mood) Said(string clip) =>
            transcripts.TryGetValue(Key(clip), out var said) || transcripts.TryGetValue(Number(clip), out said) ? said : FromName(clip);
        var clips = files.Where(IsAudio)
            .Select(f => (File: f, Said: Said(f), Seconds: Wav.Seconds(f)))
            .OrderByDescending(c => c.Said.Text.Length > 0)
            .ThenByDescending(c => c.Seconds is >= 3 and <= 10)
            .ThenBy(c => Math.Abs((c.Seconds ?? 6) - 6))
            .Take(6)
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
        var said = FromName(clip);
        var keepModel = existing is { Gpt: not null, Sovits: not null };
        if (!keepModel)
            Clear(folder);
        Directory.CreateDirectory(Path.Combine(folder, "clips"));
        var target = Unique(Path.Combine(folder, "clips", Path.GetFileName(clip)));
        File.Copy(clip, target);
        var added = new VoiceClip(Path.GetRelativePath(folder, target), said.Text, said.Language ?? SpokenLanguage.Of(said.Text) ?? existing?.Language ?? "",
                                  said.Mood, Wav.Seconds(target));
        var profile = keepModel
            ? existing! with { Clips = [added, .. existing!.Clips] }
            : new VoiceProfile { Name = Tidy(Path.GetFileNameWithoutExtension(clip)), Language = added.Language, Clips = [added] };
        profile.Save(folder);
        return profile;
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
