using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The engine of her voice run as a hidden server, on a free port on this PC only: the player's GPT-SoVITS (the
/// official Windows package, its own api_v2.py), or the player's Qwen3-TTS (a Python with faster-qwen3-tts, running
/// the cabinet's qwen_server.py). It is tied to the cabinet: if the cabinet closes or crashes, Windows ends the server
/// too. Windows' background throttling is switched off for it, since nobody looks at its window (it has none).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VoiceServer : IDisposable
{
    /// <summary>A first start loads several models from disk; on the development laptop it took 14–30 s.</summary>
    private static readonly TimeSpan StartLimit = TimeSpan.FromMinutes(3);

    private readonly string _folder, _log;
    private Process? _process;
    private IntPtr _job;
    private StreamWriter? _logWriter;

    /// <param name="engine">One of <see cref="VoiceEngines"/>.</param>
    public VoiceServer(string engine, string folder, string logFile)
    {
        Engine = engine;
        _folder = folder;
        _log = logFile;
    }

    private bool _answered;

    public string Engine { get; }
    public string Folder => _folder;
    public Uri? Url { get; private set; }
    public bool Running => _process is { HasExited: false };
    /// <summary>Running and has answered: voices can be loaded.</summary>
    public bool Ready => _answered && Running;

    /// <summary>What is wrong with <paramref name="folder"/> as an install of <paramref name="engine"/>, or null when it will do.</summary>
    public static string? Check(string engine, string? folder)
    {
        var name = VoiceEngines.Name(engine);
        if (string.IsNullOrWhiteSpace(folder))
            return $"Choose the folder {name} is in.";
        if (!Directory.Exists(folder))
            return "That folder doesn't exist.";
        if (engine == VoiceEngines.Qwen)
        {
            if (QwenPython(folder) is not { } python)
                return "No Python there: choose the Qwen3-TTS folder (the one with venv in it).";
            var home = Path.GetDirectoryName(python)!;
            if (Path.GetFileName(home) == "Scripts")
                home = Path.GetDirectoryName(home)!;                                        // a virtual environment's
            if (!Directory.Exists(Path.Combine(home, "Lib", "site-packages", "faster_qwen3_tts")))
                return "That Python has no faster-qwen3-tts installed.";
            return null;
        }
        if (!File.Exists(Path.Combine(folder, "api_v2.py")))
            return "No api_v2.py there: choose the GPT-SoVITS folder itself (the one with go-webui.bat).";
        if (!File.Exists(Path.Combine(folder, "runtime", "python.exe")))
            return "This isn't GPT-SoVITS's Windows package (no runtime\\python.exe).";
        return null;
    }

    /// <summary>The Python of a Qwen3-TTS folder: a virtual environment in it, or a Python of its own.</summary>
    private static string? QwenPython(string folder) =>
        new[] { Path.Combine(folder, "venv", "Scripts", "python.exe"), Path.Combine(folder, ".venv", "Scripts", "python.exe"),
                Path.Combine(folder, "runtime", "python.exe"), Path.Combine(folder, "python.exe") }.FirstOrDefault(File.Exists);

    /// <summary>
    /// Starts the server, or keeps waiting for one already starting, until it answers; false (with the reason in the
    /// log) if it never does. Cancelling stops the waiting, not the server.
    /// </summary>
    public async Task<bool> Start(CancellationToken cancel)
    {
        if (Ready)
            return true;
        if (!Running)
            Launch();
        using var http = new HttpClient { BaseAddress = Url, Timeout = TimeSpan.FromSeconds(2) };
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StartLimit && _process is { HasExited: false })
        {
            try
            {
                using var response = await http.GetAsync(Engine == VoiceEngines.Qwen ? "health" : "docs", cancel);
                if (response.IsSuccessStatusCode)
                    return _answered = true;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
            {
            }
            await Task.Delay(500, cancel);
        }
        Stop();
        return false;
    }

    private void Launch()
    {
        Stop();
        var port = FreePort();
        Url = new Uri($"http://127.0.0.1:{port}/");
        Directory.CreateDirectory(Path.GetDirectoryName(_log)!);
        _logWriter = new StreamWriter(_log, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        var qwen = Engine == VoiceEngines.Qwen;
        var start = new ProcessStartInfo(qwen ? QwenPython(_folder)! : Path.Combine(_folder, "runtime", "python.exe"))
        {
            WorkingDirectory = _folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        string[] args = ["api_v2.py", "-a", "127.0.0.1", "-p", port.ToString()];
        if (qwen)
        {
            var script = Path.Combine(Path.GetDirectoryName(_log)!, "qwen_server.py");      // the cabinet's own, written out fresh
            File.WriteAllText(script, QwenTts.ServerScript(), new UTF8Encoding(false));
            args = [script, "--host", "127.0.0.1", "--port", port.ToString()];
            start.Environment["HF_HOME"] = Path.Combine(_folder, "hf");                   // its models stay with it
            start.Environment["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1";
        }
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        var process = _process = Process.Start(start) ?? throw new InvalidOperationException($"{VoiceEngines.Name(Engine)} did not start");
        TieToCabinet(process);
        var writer = _logWriter;
        void Log(string? line)
        {
            if (line is null)
                return;
            lock (writer)
                try
                {
                    writer.WriteLine(line);
                }
                catch (ObjectDisposedException)
                {
                }
        }
        process.OutputDataReceived += (_, e) => Log(e.Data);
        process.ErrorDataReceived += (_, e) => Log(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>Ends the server (and anything it started), giving its graphics memory back.</summary>
    public void Stop()
    {
        _answered = false;
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            process.Dispose();
            _process = null;
        }
        if (_logWriter is { } writer)
            lock (writer)
                writer.Dispose();
        _logWriter = null;
    }

    public void Dispose()
    {
        Stop();
        if (_job != IntPtr.Zero)
            CloseHandle(_job);
        _job = IntPtr.Zero;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A job that ends the server when the cabinet's handle to it closes, and no background throttling.</summary>
    private void TieToCabinet(Process process)
    {
        try
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
                SetInformationJobObject(_job, ExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimits>());
            }
            AssignProcessToJobObject(_job, process.Handle);
            var throttling = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 0 };    // execution speed: not throttled
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref throttling, Marshal.SizeOf<PowerThrottling>());
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Godot.GD.PrintErr($"Voice: could not tie {VoiceEngines.Name(Engine)} to the cabinet: {e.Message}");
        }
    }

    private const int ExtendedLimitInformation = 9, ProcessPowerThrottling = 4;
    private const uint KillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottling
    {
        public uint Version, ControlMask, StateMask;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int length);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
