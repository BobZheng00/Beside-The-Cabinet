using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>A stage of fetching GPT-SoVITS or its files, for the page that shows how far it has got.</summary>
public enum EngineStep { Downloading, Checking, Unpacking }

/// <summary>
/// GPT-SoVITS v5 fast ("v5turbo" upstream), which a copy of a voice speaks with: it starts sooner than v2ProPlus and
/// sounds closer (0.8 s and 0.85 against 1.2 s and 0.78, measured 2026-11-09), and is newer. It runs on the standard
/// package's own Python, from its newer code kept in a folder of its own inside the engine (<see cref="Code"/>), which
/// shares the package's models through links; its weights join those models. That code also runs v2ProPlus and the
/// voices already trained, so one server serves them all.
/// </summary>
public static class V5Fast
{
    public const string Version = "v5turbo";

    /// <summary>
    /// The code it runs on. Don't move this forward without trying it: semantic streaming is only connected up to this
    /// commit. 04a44eb (2026-10-31) took it out of TTS.py, while api_v2.py still accepts gpt_streaming and silently makes
    /// whole segments instead, so a later version loses it with no error.
    /// </summary>
    public const string Commit = "b4570b876f5c086819ba84e3215472733c6a7b70";

    private const string WeightsCommit = "8f8857563c040d542de108a2c18b0425abd6c9c5";
    private const string WeightsFolder = "gsv-v5-pretrained";

    /// <summary>What <see cref="ContentHash"/> gives for the code at <see cref="Commit"/>: GitHub doesn't promise a zip byte for byte.</summary>
    public const string CodeHash = "30c263b719765ae6c5b044a251e5211dec5e572a8e34fcd80dbc08fd2752ea5d";

    private const long CodeZip = 6_850_000;             // about: GitHub doesn't say, and may pack it differently
    private const long CodeUnpacked = 37_500_000;

    public static readonly IReadOnlyList<Weight> Weights =
    [
        new("s2Gv5turbo.pth", 766_406_144, "287b32a06b7d486f5303ec520468b0728fb01aac8b0f5d86fd5455c9a1d3e022"),
        new("vocoder.pth", 57_761_163, "b7543c83222aa2105bb4efb56845086bc5f2eeba61243d723ffa062052b3221f"),
    ];

    /// <summary>A file of its weights.</summary>
    public sealed record Weight(string Name, long Size, string Sha256);

    /// <summary>All it fetches, for the player to agree to (the one place this is added up).</summary>
    public static long Download => Weights.Sum(w => w.Size) + CodeZip;

    /// <summary>The room it takes while being fetched and unpacked.</summary>
    public static long Room => Download + CodeUnpacked;

    /// <summary>Its code, inside the engine's folder.</summary>
    public static string Code(string engine) => Path.Combine(engine, "gsv-v5");

    private static string WeightsIn(string engine) => Path.Combine(engine, "GPT_SoVITS", "pretrained_models", WeightsFolder);

    /// <summary>The engine has all of it: nothing is put in place until it has been checked.</summary>
    public static bool Has(string? engine) => Has(engine, Weights);

    public static bool Has(string? engine, IReadOnlyList<Weight> weights) =>
        !string.IsNullOrWhiteSpace(engine) && File.Exists(Path.Combine(Code(engine), "api_v2.py"))
        && Shared(engine, Code(engine)).All(s => Leads(s.Link))
        && weights.All(w => Whole(Path.Combine(WeightsIn(engine), w.Name), w.Size));

    /// <summary>The package's Python, models and G2PW, which its code shares rather than copies: they are gigabytes.</summary>
    private static (string Link, string Target)[] Shared(string engine, string code) =>
    [
        (Path.Combine(code, "GPT_SoVITS", "pretrained_models"), Path.Combine(engine, "GPT_SoVITS", "pretrained_models")),
        (Path.Combine(code, "GPT_SoVITS", "text", "G2PWModel"), Path.Combine(engine, "GPT_SoVITS", "text", "G2PWModel")),
        (Path.Combine(code, "runtime"), Path.Combine(engine, "runtime")),      // its tools.portable_runtime looks beside itself for ffmpeg and DLLs
    ];

    /// <summary>A link still leads somewhere: moving the package leaves them pointing at where it was, and Directory.Exists says yes to such a link itself.</summary>
    private static bool Leads(string link) => new DirectoryInfo(link).LinkTarget is { } target && Directory.Exists(target);

    /// <summary>
    /// It can be added to this engine: the standard package the cabinet installs, the only one it was tried on (the RTX 50
    /// build has its own Python and torch, and a folder the player chose may be any version).
    /// </summary>
    public static bool CanAdd(string? engine) =>
        EngineInstall.Problem(engine) is null && !Has(engine)
        && Path.GetFileName(Path.TrimEndingDirectorySeparator(engine!)).Equals(EnginePackage.Standard.Name, StringComparison.OrdinalIgnoreCase);

    private static bool Whole(string path, long size) => File.Exists(path) && new FileInfo(path).Length == size;

    private static Uri From(EngineSource source, string name) =>
        new($"https://{(source == EngineSource.HfMirror ? "hf-mirror.com" : "huggingface.co")}/lj1995/GPT-SoVITS/resolve/{WeightsCommit}/{WeightsFolder}/{name}");

    /// <summary>Fetches, checks and puts in place what <paramref name="engine"/> lacks.</summary>
    public static Task Install(HttpClient http, string engine, EngineSource source, IProgress<(EngineStep Step, long Done, long Of)> progress, CancellationToken cancel) =>
        Install(http, engine, name => From(source, name), Weights, new Uri($"https://github.com/RVC-Boss/GPT-SoVITS/archive/{Commit}.zip"), CodeHash, progress, cancel);

    /// <summary>
    /// Each weight comes down beside where it goes, carrying on from what an earlier try left, and is moved into place
    /// once it matches. The code unpacks beside the engine's other folders and takes its place, links and all, only
    /// when its files match, so anything cut short leaves the engine speaking as before and is carried on next time.
    /// </summary>
    public static async Task Install(HttpClient http, string engine, Func<string, Uri> weightFrom, IReadOnlyList<Weight> weights, Uri code, string codeHash,
                                     IProgress<(EngineStep Step, long Done, long Of)> progress, CancellationToken cancel, TimeSpan? backOff = null)
    {
        var folder = WeightsIn(engine);
        Directory.CreateDirectory(folder);
        var total = weights.Sum(w => w.Size) + CodeZip;
        long before = 0;
        foreach (var weight in weights)
        {
            var path = Path.Combine(folder, weight.Name);
            var from = before;
            if (!Whole(path, weight.Size))
            {
                var part = path + ".part";
                await EngineInstall.Fetch(http, weightFrom(weight.Name), part, weight.Size, new Heard(b => progress.Report((EngineStep.Downloading, from + b, total))), cancel, backOff: backOff);
                if (!await EngineInstall.Matches(part, weight.Sha256, new Heard(b => progress.Report((EngineStep.Checking, b, weight.Size))), cancel))
                {
                    File.Delete(part);
                    throw new InvalidDataException($"{weight.Name} didn't match the published file (damaged on the way?), so it was deleted. Try again.");
                }
                File.Move(part, path, overwrite: true);
            }
            before += weight.Size;
        }

        var target = Code(engine);
        if (Directory.Exists(target) && !Shared(engine, target).All(s => Leads(s.Link)))
            Remove(target);                                         // the package was moved: its links lead nowhere now
        if (File.Exists(Path.Combine(target, "api_v2.py")))
            return;
        var zip = target + ".zip.part";
        var staging = target + ".unpacking";
        await EngineInstall.Fetch(http, code, zip, null, new Heard(b => progress.Report((EngineStep.Downloading, Math.Min(before + b, total), total))), cancel, backOff: backOff);
        progress.Report((EngineStep.Unpacking, 0, 1));
        await Task.Run(() => Unpack(engine, zip, staging, target, codeHash), cancel);
        File.Delete(zip);
    }

    private sealed class Heard(Action<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard(value);
    }

    private static void Unpack(string engine, string zip, string staging, string target, string codeHash)
    {
        if (Directory.Exists(staging))
            Remove(staging);
        try
        {
            ZipFile.ExtractToDirectory(zip, staging);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("v5 fast's code came through damaged. Try again.");
        }
        var top = Directory.GetDirectories(staging) is [var only] && Directory.GetFiles(staging).Length == 0 ? only : staging;
        if (!ContentHash(top).Equals(codeHash, StringComparison.OrdinalIgnoreCase))
        {
            Remove(staging);
            throw new InvalidDataException("v5 fast's code didn't match the published version (damaged on the way?). Try again.");
        }
        Directory.Delete(Path.Combine(top, "GPT_SoVITS", "pretrained_models"), recursive: true);   // only a .gitignore here until it's a link
        foreach (var (link, shared) in Shared(engine, top))
            Link(link, shared);
        Directory.Move(top, target);
        if (Directory.Exists(staging))
            Remove(staging);
    }

    /// <summary>
    /// Deletes a folder, taking its links away and never touching what they lead to. A plain recursive delete refuses a
    /// junction inside the folder (and a careless one elsewhere would empty the player's engine).
    /// </summary>
    public static void Remove(string folder)
    {
        foreach (var sub in Directory.GetDirectories(folder))
            if (new DirectoryInfo(sub).LinkTarget is not null)
                Directory.Delete(sub);
            else
                Remove(sub);
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>A junction: unlike a symbolic link, Windows makes one without administrator rights.</summary>
    private static void Link(string link, string target)
    {
        // quoted by hand: .NET leaves a path with no space bare, and cmd would split it at an &
        var start = new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var mklink = Process.Start(start) ?? throw new IOException("couldn't start cmd");
        var said = mklink.StandardError.ReadToEnd();
        mklink.StandardOutput.ReadToEnd();
        mklink.WaitForExit();
        if (mklink.ExitCode != 0)
            throw new IOException($"Couldn't link {Path.GetFileName(link)} (is the engine on a drive that isn't NTFS?): {said.Trim()}");
    }

    /// <summary>
    /// A hash of the files under a folder and their names, each file's line endings made LF first, so a checkout and
    /// GitHub's zip of the same commit hash alike.
    /// </summary>
    public static string ContentHash(string folder)
    {
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, file) in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                     .Select(f => (Name: Path.GetRelativePath(folder, f).Replace('\\', '/'), File: f))
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
            all.AppendData(Encoding.UTF8.GetBytes($"{name}\0{Convert.ToHexString(SHA256.HashData(LineFeeds(File.ReadAllBytes(file)))).ToLowerInvariant()}\n"));
        return Convert.ToHexString(all.GetHashAndReset()).ToLowerInvariant();
    }

    private static byte[] LineFeeds(byte[] bytes)
    {
        var kept = new byte[bytes.Length];
        var count = 0;
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] != '\r' || i + 1 == bytes.Length || bytes[i + 1] != '\n')
                kept[count++] = bytes[i];
        return kept[..count];
    }
}
