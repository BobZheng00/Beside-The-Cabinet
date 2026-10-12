using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace InsertCoin.Mind;

/// <summary>Where GPT-SoVITS's package is fetched from: its packager's Hugging Face repository, or HF-Mirror, which
/// GPT-SoVITS's own installer offers for mainland China. Either way it is checked against the same SHA-256.</summary>
public enum EngineSource { HuggingFace, HfMirror }

/// <summary>
/// The GPT-SoVITS Windows package the cabinet installs for the player: the release her voice is tested with, in the
/// build for their graphics card. Its size and SHA-256 are the ones Hugging Face publishes; a newer release means
/// changing them here and trying it with her voice first (v5 came out 2026-10-05, not tried yet).
/// </summary>
/// <param name="Unpacked">About the room it takes unpacked, with a little to spare.</param>
public sealed record EnginePackage(string Name, long Size, string Sha256, long Unpacked)
{
    // Unpacked: 14.13 GB in 50,612 files, unpacked in 10 minutes on the development PC (2026-10-12).
    public static readonly EnginePackage Standard = new("GPT-SoVITS-v2pro-20250604", 8_185_086_602,
        "bd60d0796553ff05d8568136e199c13e0dc22ebe2ed24273134e34ed6f215cd6", 14_500_000_000);

    /// <summary>The build for NVIDIA's RTX 50 series (Blackwell), which the standard build's CUDA doesn't run on.</summary>
    /// <remarks>Its unpacked size is reckoned from its larger download, not measured.</remarks>
    public static readonly EnginePackage Blackwell = new("GPT-SoVITS-v2pro-20250604-nvidia50", 8_835_144_925,
        "97b4edcd451c42357db7e26e6c1c877ca5d85144fe97beaff6d7005d35bee008", 15_800_000_000);

    /// <summary>The build for this graphics card, by the name its driver gives it (null: no NVIDIA card found).</summary>
    public static EnginePackage For(string? card) =>
        card is not null && Regex.IsMatch(card, @"RTX\s*50\d\d|Blackwell", RegexOptions.IgnoreCase) ? Blackwell : Standard;

    public Uri From(EngineSource source) =>
        new($"https://{(source == EngineSource.HfMirror ? "hf-mirror.com" : "huggingface.co")}/lj1995/GPT-SoVITS-windows-package/resolve/main/{Name}.7z");
}

/// <summary>
/// Installing GPT-SoVITS's Windows package for the player: fetching it (carrying on from what an earlier try left),
/// checking it against its published SHA-256, unpacking it where they chose, and finding one already on the PC.
/// </summary>
public static class EngineInstall
{
    private const int Chunk = 1 << 20;

    /// <summary>What is wrong with <paramref name="folder"/> as a GPT-SoVITS install, or null when it will do.</summary>
    public static string? Problem(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "Choose the folder GPT-SoVITS is in.";
        if (!Directory.Exists(folder))
            return "That folder doesn't exist.";
        if (!File.Exists(Path.Combine(folder, "api_v2.py")))
            return "No api_v2.py there: choose the GPT-SoVITS folder itself (the one with go-webui.bat).";
        if (!File.Exists(Path.Combine(folder, "runtime", "python.exe")))
            return "This isn't GPT-SoVITS's Windows package (no runtime\\python.exe).";
        return null;
    }

    /// <summary>
    /// Fetches <paramref name="from"/> into <paramref name="path"/> until it holds <paramref name="size"/> bytes,
    /// carrying on from what is already there; a connection that drops or goes quiet for <paramref name="stall"/> is
    /// tried again from where it got to, after a wait that doubles from <paramref name="backOff"/> each time.
    /// <paramref name="progress"/> hears how many bytes are there. Throws when it keeps failing (or is cancelled),
    /// keeping what it has for next time.
    /// </summary>
    public static async Task Fetch(HttpClient http, Uri from, string path, long size, IProgress<long> progress, CancellationToken cancel,
                                   TimeSpan? stall = null, int tries = 6, TimeSpan? backOff = null)
    {
        var quiet = stall ?? TimeSpan.FromSeconds(60);
        var wait = backOff ?? TimeSpan.FromSeconds(1);
        var failed = 0;
        while (true)
        {
            var have = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (have > size)
            {
                File.Delete(path);
                have = 0;
            }
            progress.Report(have);
            if (have == size)
                return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, from);
                if (have > 0)
                    request.Headers.Range = new RangeHeaderValue(have, null);
                HttpResponseMessage response;
                using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    waiting.CancelAfter(quiet);
                    response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, waiting.Token);
                }
                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        File.Delete(path);                  // what is there doesn't fit the file any more: start again
                        throw new HttpRequestException("the server couldn't carry on from where it got to");
                    }
                    response.EnsureSuccessStatusCode();
                    var resumed = response.StatusCode == HttpStatusCode.PartialContent;
                    if (!resumed)
                        have = 0;                           // the whole file is coming again
                    await using var body = await response.Content.ReadAsStreamAsync(cancel);
                    await using var file = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, Chunk, useAsync: true);
                    var buffer = new byte[Chunk];
                    while (true)
                    {
                        int read;
                        using (var reading = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            reading.CancelAfter(quiet);
                            read = await body.ReadAsync(buffer, reading.Token);
                        }
                        if (read == 0)
                            break;
                        await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                        have += read;
                        progress.Report(have);
                        failed = 0;                         // getting somewhere
                    }
                }
            }
            catch (Exception e) when (!cancel.IsCancellationRequested && e is HttpRequestException or IOException or OperationCanceledException)
            {
                if (++failed >= tries)
                    throw new IOException($"The download kept failing ({(e is OperationCanceledException ? "nothing came for a while" : e.Message)}).", e);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, wait.TotalSeconds * (1 << failed))), cancel);
            }
        }
    }

    /// <summary>Whether the file at <paramref name="path"/> has this SHA-256 (hex); <paramref name="progress"/> hears the bytes read.</summary>
    public static async Task<bool> Matches(string path, string sha256, IProgress<long> progress, CancellationToken cancel)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var buffer = new byte[Chunk];
        long done = 0;
        int read;
        while ((read = await file.ReadAsync(buffer, cancel)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            progress.Report(done += read);
        }
        return string.Equals(Convert.ToHexString(hash.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unpacks a .7z into <paramref name="parent"/> and returns the folder it made: the archive's one top folder, or one
    /// called <paramref name="name"/> when it has none. It unpacks into a folder of its own beside it first, so one cut
    /// short leaves nothing that looks installed (and is started again next time). <paramref name="progress"/> hears the
    /// share unpacked, 0 to 1. Refuses to replace a folder already there.
    /// </summary>
    public static string Unpack(string archive, string parent, string name, IProgress<double> progress, CancellationToken cancel)
    {
        var staging = Path.GetFullPath(Path.Combine(parent, name + ".unpacking"));
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        var inside = staging + Path.DirectorySeparatorChar;
        using (var sevenZip = SevenZipArchive.OpenArchive(archive, new ReaderOptions()))
        using (var reader = sevenZip.ExtractAllEntries())
        {
            var total = Math.Max(1, sevenZip.TotalUncompressedSize);
            long done = 0;
            while (reader.MoveToNextEntry())
            {
                cancel.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                var target = Path.GetFullPath(Path.Combine(staging, entry.Key ?? ""));
                if (!target.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The package holds a file outside its folder: {entry.Key}");
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, Chunk))
                    reader.WriteEntryTo(file);
                progress.Report((double)(done += entry.Size) / total);
            }
        }
        var top = Directory.GetDirectories(staging) is [var only] && Directory.GetFiles(staging).Length == 0 ? only : staging;
        var folder = Path.Combine(Path.GetFullPath(parent), top == staging ? name : Path.GetFileName(top));
        if (Directory.Exists(folder))
            throw new IOException($"{folder} is already there: choose another folder, or use that one.");
        Directory.Move(top, folder);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        return folder;
    }

    // Windows' own folders and others never holding it, not looked in.
    private static readonly HashSet<string> NotThere = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "System Volume Information", "AppData", "node_modules",
    };

    /// <summary>
    /// GPT-SoVITS Windows packages already on this PC, in folders named for it (GPT-SoVITS-v2pro-20250604, gptsovits)
    /// up to <paramref name="depth"/> folders below <paramref name="roots"/>, for the player to use instead of installing
    /// another. Folders it may not read are passed over.
    /// </summary>
    public static List<string> Installed(IEnumerable<string> roots, int depth = 3)
    {
        var found = new List<string>();
        var waiting = new Queue<(string Folder, int Depth)>(roots.Where(Directory.Exists).Select(root => (root, 0)));
        while (waiting.TryDequeue(out var at))
        {
            List<string> children;
            try
            {
                children = Directory.EnumerateDirectories(at.Folder).ToList();
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.Contains("sovits", StringComparison.OrdinalIgnoreCase) && Problem(child) is null)
                    found.Add(child);
                else if (at.Depth + 1 < depth && !NotThere.Contains(name) && name[0] is not ('.' or '$'))
                    waiting.Enqueue((child, at.Depth + 1));
            }
        }
        return found;
    }
}
