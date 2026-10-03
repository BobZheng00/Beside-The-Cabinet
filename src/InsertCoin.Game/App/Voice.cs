using System.Diagnostics;
using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// Her voice. Keeps the player's GPT-SoVITS in step with what is wanted: started, with the voice of whoever is on the
/// stool loaded and warmed up, while she has a voice that can talk; stopped (its graphics memory given back) when the
/// voice is switched off, she has none, or a game runs. Speaks a sentence at a time as a reply streams in, plays the
/// audio as it arrives, and opens her mouth with its loudness. Without a working voice she simply stays text-only.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class Voice : Node
{
    public enum Phase { NotSetUp, Off, Resting, NoVoice, Starting, Loading, Ready, Failed }

    private const float BufferSeconds = 0.3f;
    private const int LevelFrames = 800;                // loudness measured every ~20 ms of audio, for her mouth
    private static readonly TimeSpan ReadyWait = TimeSpan.FromMinutes(3);

    /// <summary>What Test says, and a few words said once after loading (the first sentence afterwards is slow).</summary>
    private static string Sample(string language) => language switch
    {
        "ja" => "やっほー！声が出るようになったよ。もう一問、解いてみる？",
        "zh" => "嗨！我现在可以说话了。要不要再做一道题？",
        "ko" => "안녕! 이제 말할 수 있어. 한 문제 더 풀어 볼까?",
        _ => "Hi! I can talk now. Shall we solve one more problem?",
    };

    private static string WarmUp(string language) => language switch
    {
        "ja" => "こんにちは。",
        "zh" => "你好。",
        "ko" => "안녕.",
        _ => "Hello.",
    };

    private Func<CompanionView?> _view = () => null;
    private string _log = "";
    private VoiceServer? _server;
    private HttpClient? _http;
    private GptSoVits? _engine;
    private VoiceProfile? _profile, _warm;
    private string _folder = "";
    private bool _enabled = true, _gameRunning;
    private int _refreshes;
    private CancellationTokenSource? _refreshing;
    private string? _problem;

    private readonly Queue<(string Text, string? Mood)> _toSay = new();
    private CancellationTokenSource _speaking = new();
    private bool _working;
    private CancellationToken _workingFor;
    private readonly Queue<float[]> _audio = new();
    private int _audioOffset;
    private AudioStreamPlayer _player = null!;
    private AudioStreamGeneratorPlayback? _playback;
    private int _rate, _capacity;
    private long _pushed;
    private readonly Queue<(long End, float Level)> _levels = new();
    private float _mouth;

    /// <summary>The phase changed (for the settings window's status line).</summary>
    public event Action? Changed;
    /// <summary>Test is saying its sample line: the line for her bubble, in English like the rest of the cabinet (a subtitle when she speaks another language).</summary>
    public event Action<string>? Testing;

    public Phase Now { get; private set; } = Phase.NotSetUp;
    public VoiceProfile? Profile => _profile;
    /// <summary>The folder of the voice of whoever is on the stool (it may not exist yet).</summary>
    public string Folder => _folder;

    public string Status => Now switch
    {
        Phase.NotSetUp => "Not set up: choose where GPT-SoVITS is.",
        Phase.Off => "Switched off (tray menu: Voice).",
        Phase.Resting => "Resting while your game runs, to leave it the graphics card.",
        Phase.NoVoice => _profile is null ? "No voice yet." : $"The voice still needs {_profile.Missing}.",
        Phase.Starting => "Starting GPT-SoVITS… (a first start can take a minute)",
        Phase.Loading => $"Loading {_profile?.Name}…",
        Phase.Ready => "Ready.",
        Phase.Failed => $"Not working: {_problem}",
        _ => "",
    };

    public void Build(string logFile, Func<CompanionView?> view)
    {
        _log = logFile;
        _view = view;
        _player = new AudioStreamPlayer();
        AddChild(_player);
    }

    /// <summary>The tray's Voice switch.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Refresh();
        }
    }

    /// <summary>A game is running: the server is stopped so the game has the graphics card to itself.</summary>
    public bool GameRunning
    {
        set
        {
            if (_gameRunning == value)
                return;
            _gameRunning = value;
            Refresh();
        }
    }

    /// <summary>Mute sounds mutes her too; her mouth still moves.</summary>
    public bool Muted
    {
        set => _player.VolumeDb = value ? -80 : 0;
    }

    /// <summary>The GPT-SoVITS folder to run (see <see cref="VoiceServer.Check"/>).</summary>
    public void UseEngine(string? folder)
    {
        Shutdown();
        _server = VoiceServer.Check(folder) is null ? new VoiceServer(folder!, _log) : null;
        Refresh();
    }

    /// <summary>Someone else is on the stool, or their voice changed: reads the voice in <paramref name="voiceFolder"/>.</summary>
    public void Seat(string voiceFolder)
    {
        Stop();
        _folder = voiceFolder;
        _profile = VoiceProfile.Load(voiceFolder);
        Refresh();
    }

    private bool Wanted => _enabled && !_gameRunning && _server is not null && _profile is { Missing: null };

    /// <summary>She can say something right now.</summary>
    public bool CanSpeak => Now == Phase.Ready && Wanted;

    /// <summary>Brings the server and the loaded voice in line with what is wanted.</summary>
    private async void Refresh()
    {
        var refresh = ++_refreshes;
        _refreshing?.Cancel();
        if (!Wanted)
        {
            Stop();
            _server?.Stop();
            _engine?.Forget();
            Set(_server is null ? Phase.NotSetUp : !_enabled ? Phase.Off : _gameRunning ? Phase.Resting : Phase.NoVoice);
            return;
        }
        if (Now == Phase.Ready && _server!.Ready && ReferenceEquals(_warm, _profile))
            return;                                 // already loaded and warmed up
        var cancel = (_refreshing = new CancellationTokenSource()).Token;
        var profile = _profile!;
        try
        {
            if (!_server!.Ready)
            {
                Set(Phase.Starting);
                if (!await _server.Start(cancel))
                {
                    if (refresh == _refreshes)
                        Fail("GPT-SoVITS didn't start (voice-server.log in the companion folder says why).");
                    return;
                }
                _http?.Dispose();
                _http = new HttpClient { BaseAddress = _server.Url, Timeout = TimeSpan.FromMinutes(5) };
                _engine = new GptSoVits(_http);
            }
            if (refresh != _refreshes)
                return;
            Set(Phase.Loading);
            var clock = Stopwatch.StartNew();
            var (gpt, sovits) = Weights(profile);
            await _engine!.Load(gpt, sovits, cancel);
            var clip = profile.ClipFor(null)!;
            await foreach (var _ in _engine.Speak(WarmUp(profile.Language), profile.Language, ClipPath(clip), clip, profile.Steps, cancel))
            {
            }
            GD.Print($"Voice: {profile.Name} loaded and warmed up in {clock.ElapsedMilliseconds} ms");
            if (refresh == _refreshes)
            {
                _warm = profile;
                Set(Phase.Ready);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (refresh == _refreshes)
                Fail(e.Message);
        }
    }

    /// <summary>A reply is starting: whatever she was saying stops, and the reply goes to her voice as it streams in (null when she can't speak now).</summary>
    public Speech? Begin()
    {
        Stop();
        if (!CanSpeak)
            return null;
        Speech? speech = null;
        var clock = Stopwatch.StartNew();
        var first = true;
        speech = new Speech(_profile!.Language, sentence =>
        {
            if (first)
                GD.Print($"Voice: the reply's first sentence to say came {clock.ElapsedMilliseconds} ms after asking");
            first = false;
            Say(sentence, speech!.Mood);
        });
        return speech;
    }

    /// <summary>A whole line she says on her own (an event remark, a greeting): spoken when it is in her voice's language.</summary>
    public void Line(string line)
    {
        if (!CanSpeak || !Speech.Speaks(_profile!.Language, line))
            return;
        Stop();
        Say(line, null);
    }

    /// <summary>Says a sample line (the settings' Test button), waiting while the voice starts; the reason if she can't.</summary>
    public async Task<string?> Test()
    {
        var clock = Stopwatch.StartNew();
        while (Now is Phase.Starting or Phase.Loading && clock.Elapsed < ReadyWait)
            await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        if (!CanSpeak)
            return Status;
        Stop();
        Say(Sample(_profile!.Language), "happy");
        Testing?.Invoke(Sample("en"));
        return null;
    }

    /// <summary>Stops talking at once: nothing more is said, what is playing is cut off.</summary>
    public void Stop()
    {
        _speaking.Cancel();
        _speaking = new CancellationTokenSource();
        _toSay.Clear();
        _audio.Clear();
        _audioOffset = 0;
        _levels.Clear();
        _player.Stop();                             // a playing buffer cannot be cleared: the next line opens a fresh stream
        _playback = null;
        _pushed = 0;
        ReportMouth("cut off: ");
    }

    /// <summary>Stops talking and ends the server (the cabinet closing, or another GPT-SoVITS chosen).</summary>
    public void Shutdown()
    {
        _refreshing?.Cancel();
        Stop();
        _server?.Dispose();
        _server = null;
        _http?.Dispose();
        _http = null;
        _engine = null;
    }

    public override void _ExitTree() => Shutdown();

    private void Say(string text, string? mood)
    {
        _toSay.Enqueue((text, mood));
        if (_working && _workingFor == _speaking.Token)
            return;
        _working = true;
        _workingFor = _speaking.Token;
        _ = Work(_speaking.Token);
    }

    /// <summary>Turns what is queued into audio, a sentence at a time, until it is all said or she is stopped.</summary>
    private async Task Work(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested && _engine is { } engine && _profile is { } profile && _toSay.TryDequeue(out var line))
            {
                var clip = profile.ClipFor(line.Mood)!;
                var clock = Stopwatch.StartNew();
                var first = true;
                await foreach (var audio in engine.Speak(line.Text, profile.Language, ClipPath(clip), clip, profile.Steps, stop))
                {
                    if (first)
                        GD.Print($"Voice: first audio of \"{(line.Text.Length > 24 ? line.Text[..24] + "…" : line.Text)}\" after {clock.ElapsedMilliseconds} ms");
                    first = false;
                    Queue(audio);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            GD.PrintErr($"Voice: {e.Message}");
            if (_engine is { } engine && !await engine.Alive())
            {
                _server?.Stop();
                Fail("GPT-SoVITS stopped answering; trying again in 30 seconds.");
                GetTree().CreateTimer(30).Timeout += Refresh;
            }
        }
        finally
        {
            if (_workingFor == stop)
                _working = false;
        }
    }

    private (string Gpt, string Sovits) Weights(VoiceProfile profile) =>
        profile.Gpt is { } gpt && profile.Sovits is { } sovits
            ? (Path.GetFullPath(Path.Combine(_folder, gpt)), Path.GetFullPath(Path.Combine(_folder, sovits)))
            : (GptSoVits.BaseGpt, GptSoVits.BaseSovits);

    private string ClipPath(VoiceClip clip) => Path.GetFullPath(Path.Combine(_folder, clip.File));

    private void Set(Phase phase)
    {
        if (Now == phase && phase != Phase.Failed)
            return;
        Now = phase;
        GD.Print($"Voice: {Status}");
        Changed?.Invoke();
    }

    private void Fail(string problem)
    {
        _problem = problem;
        GD.PrintErr($"Voice: {problem}");
        Set(Phase.Failed);
    }

    // ---- Playing it ----------------------------------------------------------------------------------------------------

    private void Queue(VoiceAudio audio)
    {
        if (audio.SampleRate != _rate || _playback is null)
            Open(audio.SampleRate);
        _audio.Enqueue(audio.Samples);
    }

    /// <summary>A stream at the voice's own sample rate (32 kHz for v2 models, 48 kHz for v4).</summary>
    private void Open(int rate)
    {
        _rate = rate;
        _player.Stop();
        _player.Stream = new AudioStreamGenerator { MixRate = rate, BufferLength = BufferSeconds };
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _capacity = _playback.GetFramesAvailable();
        _pushed = 0;
        _levels.Clear();
    }

    public override void _Process(double delta)
    {
        if (_playback is null)
            return;
        var room = _playback.GetFramesAvailable();
        while (room > 0 && _audio.Count > 0)
        {
            var block = _audio.Peek();
            var take = Math.Min(Math.Min(room, block.Length - _audioOffset), LevelFrames);
            var frames = new Vector2[take];
            var sum = 0f;
            for (var i = 0; i < take; i++)
            {
                var s = block[_audioOffset + i];
                frames[i] = new Vector2(s, s);
                sum += s * s;
            }
            _playback.PushBuffer(frames);
            _pushed += take;
            _levels.Enqueue((_pushed, MathF.Sqrt(sum / take)));
            room -= take;
            _audioOffset += take;
            if (_audioOffset >= block.Length)
            {
                _audio.Dequeue();
                _audioOffset = 0;
            }
        }

        // her mouth follows what is heard now: what was pushed, less what still waits in the buffer
        var playing = _pushed - (_capacity - _playback.GetFramesAvailable());
        while (_levels.Count > 0 && _levels.Peek().End <= playing)
            _levels.Dequeue();
        var level = _levels.Count > 0 ? _levels.Peek().Level : 0;
        var open = Mathf.Clamp((level - 0.01f) * 7f, 0, 1);
        _mouth += (open - _mouth) * (1 - Mathf.Exp(-(float)delta * (open > _mouth ? 30 : 14)));
        if (_view() is { } view)
            view.Mouth = _mouth < 0.02f ? 0 : _mouth;

        // a line in the log after each stretch of speech: how much her mouth moved (lip sync can't be heard in tests)
        if (_levels.Count > 0)
        {
            _spokenFrames++;
            _openFrames += _mouth > 0.2f ? 1 : 0;
            _widest = Mathf.Max(_widest, _mouth);
        }
        else
            ReportMouth("");
    }

    private int _spokenFrames, _openFrames;
    private float _widest;

    private void ReportMouth(string how)
    {
        if (_spokenFrames == 0)
            return;
        GD.Print($"Voice: {how}mouth open {100 * _openFrames / _spokenFrames}% of {_spokenFrames} frames, widest {_widest:0.00}");
        _spokenFrames = _openFrames = 0;
        _widest = 0;
    }
}
