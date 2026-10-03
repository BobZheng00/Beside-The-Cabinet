using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Trains a voice's own model with the player's GPT-SoVITS (see <see cref="VoiceTraining"/>): runs the cabinet's
/// <c>train_voice.py</c> with GPT-SoVITS's Python, hidden and tied to the cabinet, follows its progress, and when it is
/// done gives the voice its new model. One voice at a time; it takes the whole graphics card, so her voice rests
/// meanwhile, and a game starting stops it.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class VoiceTrainer : Node
{
    private readonly ConcurrentQueue<(Process From, string Line)> _lines = new();
    private readonly Stopwatch _clock = new();
    private double _seconds;
    private (TimeSpan At, double Done)? _step;          // when the step under way first reported progress, and how far
    private Process? _process;
    private StreamWriter? _log;
    private string _companion = "", _work = "";
    private (string Gpt, string Sovits)? _made;

    /// <summary>Its stage or progress changed, or it ended.</summary>
    public event Action? Changed;
    /// <summary>Training ended: the voice's folder, and the voice now trained (null when it stopped or failed: <see cref="Problem"/> says why).</summary>
    public event Action<string, VoiceProfile?>? Finished;

    public bool Running => _process is not null;
    /// <summary>The folder of the voice being trained (or last trained).</summary>
    public string? Folder { get; private set; }
    public string Stage { get; private set; } = "";
    public double Done { get; private set; }
    public string? Problem { get; private set; }
    /// <summary>How this PC trains against the estimate (see <see cref="VoiceTraining.Pace"/>), once it has trained a voice.</summary>
    public double? Pace { get; private set; }
    /// <summary>Other apps took so much of the graphics card when it started that training will be slow (see <see cref="VoiceTraining.Crowded"/>).</summary>
    public bool Crowded { get; private set; }
    /// <summary>About how long the training running now has left, once it can tell.</summary>
    public TimeSpan? Left => Running && _step is { } step ? VoiceTraining.Left(_clock.Elapsed - step.At, Done - step.Done, Done) : null;

    /// <param name="companionFolder">Where its script, clip list and log go (the companion folder).</param>
    /// <param name="pace">How this PC trained last time, if it has.</param>
    public void Build(string companionFolder, double? pace)
    {
        _companion = companionFolder;
        Pace = pace;
    }

    /// <summary>About how long training <paramref name="profile"/> would take on this PC, and whether that was measured here.</summary>
    public (TimeSpan Time, bool Measured) Estimate(VoiceProfile profile) =>
        (VoiceTraining.Estimate(VoiceTraining.Seconds(profile), Pace), Pace is not null);

    /// <summary>
    /// The graphics card's memory and how much of it is in use, in MB, or null without an NVIDIA card (GPT-SoVITS
    /// trains on those only). Asked once her voice has rested: what is in use belongs to other apps.
    /// </summary>
    private static (int Total, int Used)? GraphicsMemory()
    {
        try
        {
            using var smi = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.total,memory.used --format=csv,noheader,nounits")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            });
            var text = smi!.StandardOutput.ReadToEnd();
            smi.WaitForExit(5000);
            return text.Split('\n')[0].Split(',') is [var total, var used] && int.TryParse(total.Trim(), out var t) && int.TryParse(used.Trim(), out var u)
                ? (t, u) : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>What stops <paramref name="profile"/> from being trained now, or null when nothing does.</summary>
    public string? Check(string? engine, VoiceProfile? profile, bool gameRunning) =>
        Running ? "Already training a voice."
        : VoiceServer.Check(engine) is { } engineProblem ? engineProblem
        : profile is null ? "No voice yet."
        : VoiceTraining.Needs(profile) is { } needs ? $"Training needs {needs}."
        : gameRunning ? "A game is running: train when it's closed (training takes the whole graphics card)."
        : null;

    /// <summary>Training the voice in <paramref name="folder"/> was asked for but can't happen now: says why (in the voice settings).</summary>
    public void Refuse(string folder, string problem)
    {
        Folder = folder;
        Problem = problem;
        Changed?.Invoke();
    }

    /// <summary>Starts training the voice in <paramref name="folder"/> with GPT-SoVITS in <paramref name="engine"/> (see <see cref="Check"/> first).</summary>
    public void Start(string engine, string folder, VoiceProfile profile)
    {
        Folder = folder;
        Problem = null;
        Done = 0;
        Stage = "starting";
        _made = null;
        _seconds = VoiceTraining.Seconds(profile);
        _step = null;
        _clock.Restart();
        if (GraphicsMemory() is not { } memory)
        {
            End("Training needs an NVIDIA graphics card (GPT-SoVITS trains on those only).");
            return;
        }
        Crowded = VoiceTraining.Crowded(memory.Total, memory.Used);
        var name = Regex.Replace(Path.GetFileNameWithoutExtension(folder.TrimEnd('\\', '/')), "[^A-Za-z0-9]+", "-").Trim('-');
        name = name.Length > 0 ? name : "voice";
        _work = Path.Combine(engine, "logs", $"insert-coin-{name}");
        Directory.CreateDirectory(_companion);
        var script = Path.Combine(_companion, "train_voice.py");
        var list = Path.Combine(_companion, "voice-training.list");
        File.WriteAllText(script, VoiceTraining.Script(), new UTF8Encoding(false));
        File.WriteAllText(list, VoiceTraining.List(profile, folder), new UTF8Encoding(false));
        _log = new StreamWriter(Path.Combine(_companion, "voice-training.log"), append: false, new UTF8Encoding(false)) { AutoFlush = true };

        var start = new ProcessStartInfo(Path.Combine(engine, "runtime", "python.exe"))
        {
            WorkingDirectory = engine,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-s", script, "--list", list, "--work", _work, "--out", folder, "--name", name,
                                    "--batch", VoiceTraining.BatchSize(memory.Total, memory.Used).ToString(), "--sovits-epochs", VoiceTraining.SovitsEpochs.ToString(),
                                    "--gpt-epochs", VoiceTraining.GptEpochs.ToString() })
            start.ArgumentList.Add(arg);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        try
        {
            var process = _process = Process.Start(start) ?? throw new InvalidOperationException("GPT-SoVITS's Python did not start");
            CabinetJob.Tie(process, "voice training");
            process.OutputDataReceived += (_, e) => Heard(process, e.Data);
            process.ErrorDataReceived += (_, e) => Heard(process, e.Data);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _lines.Enqueue((process, "@exit"));
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            GD.Print($"Voice: training {Path.GetFileName(folder)} on {VoiceTraining.Clips(profile).Count} clips ({VoiceTraining.Seconds(profile):0} s), " +
                     $"batch {VoiceTraining.BatchSize(memory.Total, memory.Used)} ({memory.Used} of {memory.Total} MB of graphics memory in use)");
            Changed?.Invoke();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _process = null;
            End($"Training didn't start: {e.Message}");
        }
    }

    /// <summary>Stops training now (the player stopped it, or a game started), throwing away what it had done.</summary>
    public void Stop(string why)
    {
        if (_process is not { } process)
            return;
        _process = null;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
        End(why);
    }

    private void Heard(Process from, string? line)
    {
        if (line is null)
            return;
        _lines.Enqueue((from, line));
        if (_log is { } log)
            lock (log)
                try
                {
                    log.WriteLine(line);
                }
                catch (ObjectDisposedException)
                {
                }
    }

    public override void _Process(double delta)
    {
        var changed = false;
        while (_lines.TryDequeue(out var heard))
        {
            // a run that was stopped may still be heard from: only the one running now counts
            if (heard.From != _process || VoiceTraining.Read(heard.Line) is not { } said)
                continue;
            var (kind, rest) = said;
            switch (kind)
            {
                case "stage":
                    Stage = rest;
                    _step = null;
                    GD.Print($"Voice: training: {rest}");
                    break;
                case "progress":
                    Done = VoiceTraining.Fraction(rest);
                    _step ??= (_clock.Elapsed, Done);      // its first report: where it starts from
                    break;
                case "done" when rest.Split(' ') is [var gpt, var sovits]:
                    _made = (gpt, sovits);
                    break;
                case "failed":
                    Problem = $"Training failed: {rest}";
                    break;
                case "exit":
                    heard.From.Dispose();
                    _process = null;
                    Finish();
                    return;
            }
            changed = true;
        }
        if (changed)
            Changed?.Invoke();
    }

    /// <summary>The script has ended: on success the voice gets its new model, and speaks with it.</summary>
    private void Finish()
    {
        if (_made is not { } made || Folder is not { } folder || VoiceProfile.Load(folder) is not { } profile)
        {
            End(Problem ?? "Training stopped (voice-training.log in the companion folder says why).");
            return;
        }
        var trained = VoiceTraining.Trained(profile, folder, made.Gpt, made.Sovits);
        trained.Save(folder);
        Pace = VoiceTraining.Pace(_clock.Elapsed, _seconds);
        GD.Print($"Voice: trained {Path.GetFileName(folder)} in {_clock.Elapsed.TotalMinutes:0.0} min (pace {Pace:0.00}): {made.Gpt}, {made.Sovits}");
        Close();
        Changed?.Invoke();
        Finished?.Invoke(folder, trained);
    }

    private void End(string problem)
    {
        Problem = problem;
        GD.PrintErr($"Voice: {problem}");
        Close();
        Changed?.Invoke();
        if (Folder is { } folder)
            Finished?.Invoke(folder, null);
    }

    /// <summary>Its working files go (mostly checkpoints to resume from, gigabytes of them), and the log is closed.</summary>
    private void Close()
    {
        if (_work.Length > 0 && Directory.Exists(_work))
            try
            {
                Directory.Delete(_work, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PrintErr($"Voice: could not delete training's working files in {_work}: {e.Message}");
            }
        if (_log is { } log)
            lock (log)
                log.Dispose();
        _log = null;
    }

    public override void _ExitTree() => Stop("The cabinet closed.");
}
