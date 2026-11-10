using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The player's GPT-SoVITS (the official Windows package) run as a hidden server for her voice, on a free port on this
/// PC only. It is tied to the cabinet: if the cabinet closes or crashes, Windows ends the server too. Windows'
/// background throttling is switched off for it, since nobody looks at its window (it has none).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VoiceServer : IDisposable
{
    /// <summary>A first start loads several models from disk; on the development laptop it took 14–30 s.</summary>
    private static readonly TimeSpan StartLimit = TimeSpan.FromMinutes(3);

    private readonly string _log, _health, _name;
    private readonly Func<int, ProcessStartInfo> _start;
    private readonly ProcessPriorityClass _priority;
    private Process? _process;
    private StreamWriter? _logWriter;

    public VoiceServer(string folder, string logFile)
        : this(logFile, "docs", port =>
        {
            var start = new ProcessStartInfo(Path.Combine(folder, "runtime", "python.exe")) { WorkingDirectory = folder };
            foreach (var arg in new[] { "api_v2.py", "-a", "127.0.0.1", "-p", port.ToString() })
                start.ArgumentList.Add(arg);
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            start.Environment["PYTHONUNBUFFERED"] = "1";    // its log keeps up: every line she was given to say, as it came
            return start;
        }, ProcessPriorityClass.Normal, "GPT-SoVITS")
    {
    }

    /// <param name="health">The path that answers once the server is up.</param>
    /// <param name="start">What to run, on a free port of this PC.</param>
    public VoiceServer(string logFile, string health, Func<int, ProcessStartInfo> start, ProcessPriorityClass priority, string name)
    {
        _log = logFile;
        _health = health;
        _start = start;
        _priority = priority;
        _name = name;
    }

    private bool _answered;

    public Uri? Url { get; private set; }
    public bool Running => _process is { HasExited: false };
    /// <summary>Running and has answered: voices can be loaded.</summary>
    public bool Ready => _answered && Running;

    /// <summary>What is wrong with <paramref name="folder"/> as a GPT-SoVITS install, or null when it will do.</summary>
    public static string? Check(string? folder) => EngineInstall.Problem(folder);

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
                using var response = await http.GetAsync(_health, cancel);
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
        var start = _start(port);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        var process = _process = Process.Start(start) ?? throw new InvalidOperationException($"{_name} did not start");
        CabinetJob.Tie(process, _name);
        if (_priority != ProcessPriorityClass.Normal)
            process.PriorityClass = _priority;          // what it was measured at: the rest of the PC keeps going while it works
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
                process.WaitForExit(5000);                  // gone, and its graphics memory with it, before a game or training wants it
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

    public void Dispose() => Stop();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
