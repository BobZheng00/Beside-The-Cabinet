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
    private readonly ConcurrentQueue<string> _lines = new();
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

    /// <param name="companionFolder">Where its script, clip list and log go (the companion folder).</param>
    public void Build(string companionFolder) => _companion = companionFolder;

    /// <summary>The graphics card's memory in MB, or null without an NVIDIA card (GPT-SoVITS trains on those only).</summary>
    private static int? GraphicsMemory()
    {
        try
        {
            using var smi = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            });
            var text = smi!.StandardOutput.ReadToEnd();
            smi.WaitForExit(5000);
            return int.TryParse(text.Split('\n')[0].Trim(), out var mb) ? mb : null;
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
        if (GraphicsMemory() is not { } memory)
        {
            End("Training needs an NVIDIA graphics card (GPT-SoVITS trains on those only).");
            return;
        }
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
                                    "--batch", VoiceTraining.BatchSize(memory).ToString(), "--sovits-epochs", VoiceTraining.SovitsEpochs.ToString(),
                                    "--gpt-epochs", VoiceTraining.GptEpochs.ToString() })
            start.ArgumentList.Add(arg);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        try
        {
            var process = _process = Process.Start(start) ?? throw new InvalidOperationException("GPT-SoVITS's Python did not start");
            CabinetJob.Tie(process, "voice training");
            process.OutputDataReceived += (_, e) => Heard(e.Data);
            process.ErrorDataReceived += (_, e) => Heard(e.Data);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _lines.Enqueue("@exit");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            GD.Print($"Voice: training {Path.GetFileName(folder)} on {VoiceTraining.Clips(profile).Count} clips ({VoiceTraining.Seconds(profile):0} s), batch {VoiceTraining.BatchSize(memory)}");
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

    private void Heard(string? line)
    {
        if (line is null)
            return;
        _lines.Enqueue(line);
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
        while (_lines.TryDequeue(out var line))
        {
            if (VoiceTraining.Read(line) is not { } said)
                continue;
            var (kind, rest) = said;
            switch (kind)
            {
                case "stage":
                    Stage = rest;
                    GD.Print($"Voice: training: {rest}");
                    break;
                case "progress":
                    Done = VoiceTraining.Fraction(rest);
                    break;
                case "done" when rest.Split(' ') is [var gpt, var sovits]:
                    _made = (gpt, sovits);
                    break;
                case "failed":
                    Problem = $"Training failed: {rest}";
                    break;
                case "exit" when _process is { } process:
                    _process = null;
                    process.Dispose();
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
        GD.Print($"Voice: trained {Path.GetFileName(folder)}: {made.Gpt}, {made.Sovits}");
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
