using System.Runtime.Versioning;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Fetches Qwen TTS's weights when the player asks on Their voice (see <see cref="QwenWeights"/>): room checked first,
/// one at a time, going on with Settings closed. One stopped, or cut short by the cabinet closing, carries on from where
/// it got to next time.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class QwenInstaller : Node
{
    private sealed class Heard(Action<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard(value);
    }

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private CancellationTokenSource? _cancel;
    private long _bytes;
    private bool _checking;
    private double _sinceShown;

    /// <summary>Its progress changed, or it ended.</summary>
    public event Action? Changed;
    /// <summary>The weights are there.</summary>
    public event Action? Finished;

    /// <summary>Where the weights go.</summary>
    public string Folder { get; set; } = "";
    public bool Running => _cancel is not null;
    /// <summary>Why the last try ended without them, for the page.</summary>
    public string? Problem { get; private set; }
    public double Done => (double)_bytes / QwenWeights.Size;
    public string Saying => _checking ? "Checking the download" : $"Downloading: {Gb(_bytes)} of {Gb(QwenWeights.Size)} GB. It carries on with this window closed.";

    public static string Gb(long bytes) => (bytes / 1e9).ToString("0.00");

    public async void Start(EngineSource source)
    {
        if (Running)
            return;
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        (Problem, _bytes, _checking) = (null, 0, false);
        Changed?.Invoke();
        try
        {
            var needed = QwenWeights.Needed(Folder);
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Folder))!).AvailableFreeSpace;
            if (free < needed)
            {
                End($"Not enough room on that drive: it needs about {Gb(needed)} GB and {Gb(free)} GB is free.");
                return;
            }
            await Task.Run(() => QwenWeights.Fetch(Http, source, Folder, new Heard(b => (_bytes, _checking) = (b, false)), () => _checking = true, cancel), cancel);
            End(null);
            Finished?.Invoke();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            End("Stopped. Download carries on from where it got to.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Voice: fetching Qwen TTS: {e}");
            End(e is InvalidDataException or IOException ? e.Message : $"It couldn't be downloaded: {e.Message}");
        }
    }

    public void Stop() => _cancel?.Cancel();

    private void End(string? problem)
    {
        _cancel?.Dispose();
        _cancel = null;
        Problem = problem;
        if (IsInstanceValid(this))
            Changed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!Running || (_sinceShown += delta) < 0.25)
            return;
        _sinceShown = 0;
        Changed?.Invoke();
    }
}
