using System.Diagnostics;
using System.Runtime.Versioning;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Installs GPT-SoVITS for the player (see <see cref="EngineInstall"/>): fetches its Windows package into the folder
/// they chose, checks it, unpacks it there and hands over the folder. One at a time, going on with Settings closed; a
/// download stopped, or cut short by the cabinet closing, carries on from where it got to next time. It also looks for
/// one already on the PC, and asks the driver which graphics card there is.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class EngineInstaller : Node
{
    private sealed class Heard<T>(Action<T> heard) : IProgress<T>
    {
        public void Report(T value) => heard(value);
    }

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private CancellationTokenSource? _cancel;
    private EnginePackage _package = EnginePackage.Standard;
    private long _bytes;                                // downloaded, or checked
    private double _share;                              // unpacked
    private readonly Queue<(double At, double Done)> _pace = new();      // how far the step was, the last few seconds
    private double _clock, _sinceShown;
    private bool _looking;

    public enum Step { Downloading, Checking, Unpacking }

    /// <summary>Its progress changed, it ended, or what it looked for on the PC came in.</summary>
    public event Action? Changed;
    /// <summary>GPT-SoVITS is installed, in this folder.</summary>
    public event Action<string>? Installed;

    public bool Running => _cancel is not null;
    public Step Now { get; private set; }
    /// <summary>Why the last try ended without installing it (stopped by the player, too little room, …).</summary>
    public string? Problem { get; private set; }
    /// <summary>The last try was stopped by the player, not a failure.</summary>
    public bool Stopped { get; private set; }
    /// <summary>GPT-SoVITS installs already on this PC, once <see cref="Look"/> has looked.</summary>
    public IReadOnlyList<string>? Found { get; private set; }
    /// <summary>The NVIDIA graphics card, as its driver names it, once <see cref="Look"/> has asked (null: none found).</summary>
    public string? Card { get; private set; }

    /// <summary>How far the step under way is, 0 to 1.</summary>
    public double Done => Now == Step.Unpacking ? _share : _package.Size == 0 ? 0 : (double)_bytes / _package.Size;

    /// <summary>The step under way for the player: how much, how fast, how long left (unpacking takes a while too).</summary>
    public string Saying
    {
        get
        {
            var rate = Rate();
            var left = rate > 0 ? $", about {Minutes((1 - Done) / rate)} left" : "";
            return Now switch
            {
                Step.Downloading => $"Downloading: {Gb(_bytes)} of {Gb(_package.Size)} GB" + (rate > 0 ? $", {rate * _package.Size / 1e6:0.0} MB/s" : "") + left,
                Step.Checking => "Checking the download against the published one",
                _ => "Unpacking" + left,
            };
        }
    }

    /// <summary>Looks, once, for GPT-SoVITS already on this PC and for an NVIDIA card, without holding up the cabinet.</summary>
    public async void Look()
    {
        if (_looking || Found is not null)
            return;
        _looking = true;
        var (found, card) = await Task.Run(() => (EngineInstall.Installed(Roots()), GraphicsCard()));
        if (!IsInstanceValid(this))
            return;
        (Found, Card) = (found, card);
        Changed?.Invoke();
    }

    /// <summary>A folder to suggest installing into: the root of the fixed drive with the most room.</summary>
    public static string Suggested() =>
        DriveInfo.GetDrives().Where(d => d is { DriveType: DriveType.Fixed, IsReady: true }).MaxBy(d => d.AvailableFreeSpace)?.RootDirectory.FullName ?? @"C:\";

    /// <summary>What it takes to install into <paramref name="parent"/>: the download, the room, and the room there is (null: no such drive).</summary>
    public (EnginePackage Package, long Needs, long? Free) Plan(string parent)
    {
        var package = EnginePackage.For(Card);
        var archive = Path.Combine(parent, package.Name + ".7z");
        var have = File.Exists(archive) ? Math.Min(new FileInfo(archive).Length, package.Size) : 0;
        long? free = null;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(parent))!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
        return (package, package.Size - have + package.Unpacked, free);
    }

    /// <summary>Downloads, checks and unpacks GPT-SoVITS into a folder of its own inside <paramref name="parent"/>.</summary>
    public async void Start(string parent, EngineSource source)
    {
        if (Running)
            return;
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        (Problem, Now, _bytes, _share) = (null, Step.Downloading, 0, 0);
        _pace.Clear();
        Changed?.Invoke();
        if (Found is null)
            Card = await Task.Run(GraphicsCard);            // the build depends on the card: asked before anything else
        var (package, needs, free) = Plan(parent);
        if (free is null || free < needs || Directory.Exists(Path.Combine(parent, package.Name)))
        {
            End(free is null ? "That isn't a folder on this PC's drives."
                : free < needs ? $"Not enough room there: it needs about {Gb(needs)} GB and {Gb(free.Value)} GB is free."
                : $"There is a {package.Name} folder there already: use it (the GPT-SoVITS field above), or choose another folder.");
            return;
        }
        _package = package;
        var archive = Path.Combine(parent, package.Name + ".7z");
        try
        {
            Directory.CreateDirectory(parent);
            await Task.Run(() => EngineInstall.Fetch(Http, package.From(source), archive, package.Size, new Heard<long>(b => _bytes = b), cancel), cancel);
            (Now, _bytes) = (Step.Checking, 0);
            if (!await Task.Run(() => EngineInstall.Matches(archive, package.Sha256, new Heard<long>(b => _bytes = b), cancel), cancel))
            {
                File.Delete(archive);
                throw new InvalidDataException("The download didn't match the published package (damaged on the way?), so it was deleted. Install starts it again.");
            }
            Now = Step.Unpacking;
            var folder = await Task.Run(() => EngineInstall.Unpack(archive, parent, package.Name, new Heard<double>(s => _share = s), cancel), cancel);
            File.Delete(archive);
            End(null);
            Installed?.Invoke(folder);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            End(Now == Step.Downloading ? "Stopped. Install carries on from where it got to." : "Stopped. Install takes it up again.", stopped: true);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Voice: installing GPT-SoVITS: {e}");
            End(e is InvalidDataException or IOException ? e.Message : $"It couldn't be installed: {e.Message}");
        }
    }

    public void Stop() => _cancel?.Cancel();

    private void End(string? problem, bool stopped = false)
    {
        Stopped = stopped;
        _cancel?.Dispose();
        _cancel = null;
        Problem = problem;
        if (IsInstanceValid(this))
            Changed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!Running)
            return;
        _clock += delta;
        if (_pace.Count > 0 && Done < _pace.Peek().Done)
            _pace.Clear();                                  // a new step began
        _pace.Enqueue((_clock, Done));
        while (_pace.Count > 2 && _clock - _pace.Peek().At > 8)
            _pace.Dequeue();
        if ((_sinceShown += delta) < 0.25)
            return;
        _sinceShown = 0;
        Changed?.Invoke();
    }

    /// <summary>How much of the step gets done a second, over the last few seconds, once it has been going a while.</summary>
    private double Rate()
    {
        var (at, done) = _pace.Count > 0 ? _pace.Peek() : (0, 0);
        return _clock - at < 2 ? 0 : (Done - done) / (_clock - at);
    }

    /// <summary>Where people put GPT-SoVITS: each fixed drive, and their Downloads, Desktop and Documents.</summary>
    private static IEnumerable<string> Roots()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return DriveInfo.GetDrives().Where(d => d is { DriveType: DriveType.Fixed, IsReady: true }).Select(d => d.RootDirectory.FullName)
            .Concat([Path.Combine(home, "Downloads"), System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
                     System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)]);
    }

    /// <summary>The NVIDIA graphics card's name from its driver, or null without one (GPT-SoVITS needs one to run well).</summary>
    private static string? GraphicsCard()
    {
        try
        {
            using var smi = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=name --format=csv,noheader")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            });
            var name = smi!.StandardOutput.ReadToEnd().Split('\n')[0].Trim();
            smi.WaitForExit(5000);
            return name.Length > 0 ? name : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    public static string Gb(long bytes) => (bytes / 1e9).ToString("0.0");

    private static string Minutes(double seconds) => Math.Max(1, Math.Round(seconds / 60)) is var m && m == 1 ? "a minute" : $"{m} minutes";
}
