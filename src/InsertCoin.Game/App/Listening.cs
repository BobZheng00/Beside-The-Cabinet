using System.Runtime.Versioning;
using Godot;
using InsertCoin.Game.Companion;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The player talking to her aloud: holding the talk key or her, into the slip's microphone, or hands-free. Hands-free
/// is the player's choice (see <see cref="HandsFree"/>): a few seconds of listening after she has said something,
/// and conversations, switched on and off, in which she listens after everything she says. She hears what was said
/// on this PC and hands it on: answered at once, or written into the slip. The microphone is open only while she
/// listens, and the cabinet's screen shows MIC meanwhile.
/// </summary>
/// <remarks>
/// She never listens hands-free while she speaks: her own voice would come back through the microphone. Talking over
/// her by voice waits for echo cancellation (the agent spike).
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class Listening : Node
{
    /// <summary>A press on her (or of the talk key) held this long is talking to her, not a click.</summary>
    private const double HoldSeconds = 0.35;
    /// <summary>Two taps of the talk key this close together start or end a conversation.</summary>
    private const double DoubleTap = 0.45;
    private const string EarsModel = "res://models/sense-voice/", TurnModels = "res://models/turn-taking/";

    /// <summary>How she is being talked to: held on her or by the talk key, by the slip's microphone (written into it), or hands-free.</summary>
    private enum Talk { None, Hold, Key, Mic, Open }

    private Stool _stool = null!;
    private SettingsFile _settings = null!;
    private ChatBox _chatBox = null!;
    private Ears _ears = null!;
    private Task<Hearing?>? _hearing;
    private Task<(VoiceDetector Detector, SmartTurn Turn)?>? _turns;
    private Talk _talking;
    private TurnTaking? _turn;                              // the hands-free turn under way
    private HandsFree.Said? _after;                         // what she is saying now, to listen after once she has finished
    private Backchannel _nods = new();                      // when she nods as the player talks
    private bool _understanding, _pretend;                  // _pretend (dev): she hears recordings, never the microphone
    private (float[] Samples, int At)? _recording;          // dev: a recording played into her ears as if said now
    private bool _pressedOnHer, _keyUsed, _keyWithOthers;
    private double _pressedFor, _keyFor, _clock, _lastTap = -10, _checked, _starved;

    /// <summary>The microphone opened or closed (the cabinet's MIC light).</summary>
    public event Action<bool>? MicChanged;
    /// <summary>A conversation started or ended (the tray's switch).</summary>
    public event Action<bool>? ConversingChanged;

    /// <summary>In a conversation: she listens after everything she says until the player has been quiet a while.</summary>
    public bool Conversing { get; private set; }

    /// <summary>She is listening, or hearing what was just said.</summary>
    public bool Busy => _talking != Talk.None || _understanding;

    /// <summary>The press on her was a hold: the release is the end of talking, not a click.</summary>
    public bool HeldToTalk { get; private set; }

    /// <summary>Whether she has ears in this build (SenseVoice, fetched by tools/models/fetch.py).</summary>
    public static bool HasEars => ModelFiles.Present(EarsModel + Hearing.ModelFile);

    /// <summary>Her ears, loaded once and shared (also for writing down the words in a voice's clips); null when they won't load.</summary>
    public Task<Hearing?> SharedEars() => _hearing ??= Task.Run(LoadHearing);

    /// <summary>Whether she can listen hands-free in this build (the turn-taking models, fetched the same way).</summary>
    public static bool HasTurns => ModelFiles.Present(TurnModels + VoiceDetector.ModelFile) && ModelFiles.Present(TurnModels + SmartTurn.ModelFile);

    public void Build(Stool stool, SettingsFile settings, DesktopWindow window, ChatBox chatBox)
    {
        _stool = stool;
        _settings = settings;
        _chatBox = chatBox;
        _ears = new Ears();
        AddChild(_ears);
        _ears.Full += () =>
        {
            if (_talking != Talk.Open)                      // hands-free turns end themselves at the same length
                _ = Heard();
        };
        window.Pressed += at =>
        {
            _pressedOnHer = stool.Her?.Hits(at) == true;
            _pressedFor = 0;
        };
        window.DragStarted += () =>
        {
            _pressedOnHer = false;
            if (_talking == Talk.Hold)
                StopListening();                            // she was being dragged, not talked to
        };
        window.Released += () =>
        {
            _pressedOnHer = false;
            if (_talking == Talk.Hold)
                _ = Heard();
            HeldToTalk = false;
        };
        chatBox.MicPressed += () =>
        {
            if (_talking == Talk.Mic)
                _ = Heard();
            else if (_talking is Talk.None or Talk.Open)
                Listen(Talk.Mic);
        };
    }

    // ---- What she says, and listening after it ---------------------------------------------------------------------

    /// <summary>
    /// She has said <paramref name="line"/>, an answer to the player or something of her own: once she has finished
    /// saying it, she listens for a while if the player has asked her to after lines like it, or in a conversation.
    /// </summary>
    public void Said(bool answer, string line) => _after = HandsFree.Kind(answer, line);

    /// <summary>
    /// Her card asks something: once she has finished saying it, she listens for the answer as after a question of hers,
    /// if the player listens hands-free after questions (or they are in a conversation).
    /// </summary>
    public void CardShown() => _after = HandsFree.Said.Question;

    /// <summary>
    /// She has called the player and waits for their answer (see <see cref="WordFirst"/>): once her call is said, she
    /// listens for it as after a question of hers, if the player listens hands-free after questions.
    /// </summary>
    public void Called() => _after = HandsFree.Said.Question;

    /// <summary>Her card was answered, or went: a microphone opened for it closes, unless the player is talking.</summary>
    public void CardGone() => Drop();

    /// <summary>The player did something else (clicked her, typed): nothing hands-free now.</summary>
    public void Drop()
    {
        _after = null;
        if (_talking == Talk.Open && _turn?.Talking != true)
            Close();
    }

    /// <summary>Starts or ends a conversation.</summary>
    public void Converse(bool on)
    {
        if (on == Conversing)
            return;
        if (on && !MayListen(out var why))
        {
            GD.Print($"Listening: no conversation now ({why})");
            ConversingChanged?.Invoke(false);               // the tray's switch goes back off
            return;
        }
        Conversing = on;
        GD.Print(on ? "Listening: a conversation starts" : "Listening: the conversation ends");
        ConversingChanged?.Invoke(on);
        if (!on)
        {
            Drop();
            return;
        }
        _turns ??= Task.Run(LoadTurns);
        _hearing ??= Task.Run(LoadHearing);
        _after = HandsFree.Said.Remark;                    // she listens as soon as she is free
    }

    /// <summary>Whether she may listen on her own now, and if not, why.</summary>
    private bool MayListen(out string why)
    {
        why = !HasEars || !HasTurns ? "this build has no ears or turn-taking models"
            : !_stool.CanAnswer ? "she isn't connected"
            : !_settings.Now.HandsFree.Allows(Presence.InCall, Presence.DoNotDisturb, _stool.GameRunning) ? "a call, full screen or a game"
            : "";
        return why.Length == 0;
    }

    // ---- Every frame ------------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        _clock += delta;
        Held(delta);
        Key(delta);
        if (_after is { } after && _talking == Talk.None && !_understanding && (!_stool.Replying || _stool.Waits && !_stool.Speaking))
        {
            _after = null;
            if (_settings.Now.HandsFree.After(after, Conversing) is { } seconds && MayListen(out _))
                Open(seconds);
        }
        Recording(delta);
        if (_talking == Talk.Open)
            HandsFreeTurn();
        if (_talking != Talk.None && (_talking != Talk.Open || _turn?.Talking == true))
        {
            _chatBox.SetLevel(_ears.Level);
            if (_nods.Hear(_ears.Level, delta))
            {
                GD.Print("Ears: she nods at a pause");
                _stool.Her?.Play(Moment.Nod);
            }
        }
    }

    /// <summary>A press on her held long enough is talking to her.</summary>
    private void Held(double delta)
    {
        if (!_pressedOnHer || HeldToTalk || _talking is not (Talk.None or Talk.Open))
            return;
        _pressedFor += delta;
        if (_pressedFor < HoldSeconds)
            return;
        HeldToTalk = true;                                  // the release is the end of talking, not a click
        Listen(Talk.Hold);
    }

    /// <summary>The talk key: held, she listens until it is let go; tapped twice, a conversation starts or ends.</summary>
    private void Key(double delta)
    {
        var key = _settings.Now.TalkKey;
        if (key == TalkKey.Off)
            return;
        if (TalkKey.IsDown(key))
        {
            _keyFor += delta;
            _keyWithOthers |= TalkKey.OthersDown(key);
            if (_keyFor >= HoldSeconds && !_keyUsed && _talking is (Talk.None or Talk.Open))
            {
                _keyUsed = true;                            // once per hold, whether she could listen or not
                Listen(Talk.Key);
            }
            return;
        }
        if (_keyFor > 0 && _keyFor < HoldSeconds && !_keyWithOthers && _settings.Now.HandsFree.ConversationKey)
        {
            if (_clock - _lastTap < DoubleTap)
            {
                _lastTap = -10;
                Converse(!Conversing);
            }
            else
                _lastTap = _clock;
        }
        _keyFor = 0;
        _keyUsed = _keyWithOthers = false;
        if (_talking == Talk.Key)
            _ = Heard();
    }

    // ---- Listening with the key, by holding her, or into the slip -------------------------------------------------------

    /// <summary>She stops talking, leans in and listens: the microphone opens until the player lets go (or clicks the microphone again).</summary>
    private void Listen(Talk how)
    {
        if (_stool.Her is not { } her)
            return;
        if (!_stool.CanAnswer || !HasEars)
        {
            if (how != Talk.Key)                            // a key held for something else in another app says nothing
                _stool.Say(!_stool.CanAnswer ? "I can only answer once I'm connected: tray → Settings → Mind." : "I can't hear in this build (no speech model).");
            return;
        }
        if (_talking == Talk.Open)
            Close();                                        // the key or a hold takes over from hands-free
        _after = null;
        _hearing ??= Task.Run(LoadHearing);                 // loads while the player talks
        if (_stool.Waits)
            _stool.Hush();                                  // her card or her call waits for an answer: she stops talking, the question stands
        else
            _ = _stool.Interrupt();                         // talking over her stops her
        _talking = how;
        OpenMicrophone();
        _nods = new Backchannel();
        _stool.Lively(Ears.Longest + 10);
        her.Play(Moment.Listen);
        _stool.OpenChat(focus: how != Talk.Key);            // the talk key is held in another app: leave its focus alone
        _chatBox.SetListening(true);
    }

    /// <summary>The microphone closes without anything being heard (she was dragged instead).</summary>
    private void StopListening()
    {
        _talking = Talk.None;
        CloseMicrophone();
        _chatBox.SetListening(false);
        _stool.Her?.Play(Moment.Heard);
    }

    /// <summary>The player has finished: what they said is heard, then answered, or written into the slip.</summary>
    private async Task Heard()
    {
        if (_talking == Talk.None)
            return;
        var how = _talking;
        _talking = Talk.None;
        _turn = null;
        var seconds = _ears.Seconds;
        var silent = _ears.Peak == 0;
        var said = CloseMicrophone();
        _chatBox.SetListening(false);
        _stool.Her?.Play(Moment.Heard);
        if (silent)
        {
            _stool.Say("I can't hear you. Is the microphone allowed? Windows Settings → Privacy & security → Microphone.");
            return;
        }
        if (seconds < 0.3)                                  // less is a slip of the finger
            return;
        _stool.Her?.Play(Moment.Nod);                       // got it
        await Understand(said, how);
    }

    /// <summary>Hears <paramref name="said"/> (16 kHz) and answers it, or writes it into the slip.</summary>
    private async Task Understand(float[] said, Talk how)
    {
        if (await (_hearing ??= Task.Run(LoadHearing)) is not { } hearing)
        {
            _stool.Say("I can't hear right now: my ears didn't load (see the log).");
            return;
        }
        _understanding = true;                              // a reminder waits: the player has just spoken
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var words = await Task.Run(() => hearing.Hear(said));
            GD.Print($"Ears: {said.Length / (double)Hearing.SampleRate:0.0} s heard in {clock.ElapsedMilliseconds} ms");   // never what was said
            if (!IsInsideTree() || words.Length == 0)
                return;
            if (how == Talk.Mic)
                _chatBox.Insert(words);
            else
            {
                if (_stool.CardOpen)
                {
                    if (SpokenAnswer.Of(words) is { } answer)
                    {
                        GD.Print($"Ears: her card answered aloud: {answer}");
                        _stool.AnswerCard(answer);
                        return;
                    }
                    await _stool.Interrupt();               // something else to say: the card goes unanswered, as with a new message
                }
                else if (_stool.Calling)
                {
                    switch (WordFirst.Of(words))
                    {
                        case WordFirst.Reply.GoOn:
                            GD.Print("Ears: her call answered aloud: she goes on");
                            _stool.GoOn();
                            return;
                        case WordFirst.Reply.NotNow:
                            await _stool.NotNow();
                            return;
                    }
                    await _stool.Interrupt();               // something else to say: she stops after her call, as when cut off
                }
                if (!_stool.Thinking)
                {
                    _chatBox.ShowHeard(words);
                    await _stool.Ask(words);
                }
            }
        }
        finally
        {
            _understanding = false;
        }
    }

    // ---- Hands-free ----------------------------------------------------------------------------------------------------

    /// <summary>Opens the microphone for a hands-free turn: she waits up to <paramref name="seconds"/> for the player to start.</summary>
    private void Open(double seconds)
    {
        _turns ??= Task.Run(LoadTurns);
        _hearing ??= Task.Run(LoadHearing);
        _talking = Talk.Open;
        _turn = new TurnTaking(seconds);
        if (_turns is { IsCompletedSuccessfully: true, Result: { } turns })
            turns.Detector.Reset();
        OpenMicrophone();
        _stool.Lively(seconds + 2);
        GD.Print($"Listening: hands-free for {seconds:0} s{(Conversing ? " (a conversation)" : "")}");
    }

    /// <summary>Closes a hands-free window: nothing was said, or something else came first.</summary>
    private void Close()
    {
        _talking = Talk.None;
        _turn = null;
        CloseMicrophone();
        _chatBox.SetListening(false);
    }

    /// <summary>What the voice detector makes of the sound since the last frame, and what that means for the turn.</summary>
    private void HandsFreeTurn()
    {
        if (_turn is not { } turn)
            return;
        // while waiting: she started speaking (a remark of the cabinet's), or a call, full screen or a game began (looked
        // at once a second): not now. Her card or her call, waiting quietly on this answer, isn't her speaking.
        if (!turn.Talking)
        {
            var stop = _stool.Replying && !(_stool.Waits && !_stool.Speaking);
            if ((_checked += GetProcessDeltaTime()) > 1)
            {
                _checked = 0;
                stop |= !MayListen(out _);
            }
            if (stop)
            {
                Close();
                if (Conversing && !_stool.Replying)
                    Converse(false);
                return;
            }
        }
        var fresh = _ears.Fresh();
        if (fresh.Length == 0)
        {
            // the turn goes by the sound that comes in: a microphone that gives none would keep her listening for ever
            if ((_starved += GetProcessDeltaTime()) > 2)
            {
                GD.PrintErr("Listening: the microphone gives no sound; it closes");
                Close();
                if (Conversing)
                    Converse(false);
            }
            return;
        }
        _starved = 0;
        if (_turns is not { IsCompletedSuccessfully: true, Result: { } models })
            return;                                         // still loading: the time it takes is not heard
        var heard = turn.Hear(models.Detector.Hear(fresh), fresh.Length / (double)Hearing.SampleRate);
        if (!turn.Talking && heard != TurnTaking.Heard.Finished)
            _ears.Keep(0.6);                                // only the moment before they start is kept
        switch (heard)
        {
            case TurnTaking.Heard.Started:
                GD.Print("Listening: the player started talking");
                _nods = new Backchannel();
                _stool.Lively(Ears.Longest + 10);
                _stool.Her?.Play(Moment.Listen);
                _stool.OpenChat(focus: false);
                _chatBox.SetListening(true);
                break;
            case TurnTaking.Heard.AskModel:
                _ = AskModel(turn, turn.Asking, models.Turn, _ears.Last(SmartTurn.Seconds));
                break;
            case TurnTaking.Heard.Finished:
                _ = Heard();
                break;
            case TurnTaking.Heard.Expired:
                GD.Print("Listening: nothing was said; the microphone closes");
                Close();
                if (Conversing)
                    Converse(false);                        // the player has gone quiet: the conversation is over
                break;
        }
    }

    /// <summary>Asks the end-of-turn model whether the player has finished, off the main thread.</summary>
    private static async Task AskModel(TurnTaking turn, int asking, SmartTurn model, float[] audio)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var complete = await Task.Run(() => model.Complete(audio));
        GD.Print($"Listening: finished? {complete:0.00} ({clock.ElapsedMilliseconds} ms)");
        turn.Verdict(asking, complete >= SmartTurn.Sure);
    }

    // ---- The microphone ----------------------------------------------------------------------------------------------

    private void OpenMicrophone()
    {
        if (_pretend || _recording is not null)
            _ears.Pretend();
        else
            _ears.Start();
        MicChanged?.Invoke(true);
    }

    private float[] CloseMicrophone()
    {
        var said = _ears.Stop();
        MicChanged?.Invoke(false);
        return said;
    }

    // ---- Models --------------------------------------------------------------------------------------------------------

    private static Hearing? LoadHearing()
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var model = ModelFiles.OnDisk(EarsModel + Hearing.ModelFile, "sense-voice");
            ModelFiles.OnDisk(EarsModel + Hearing.TokensFile, "sense-voice");
            var hearing = new Hearing(Path.GetDirectoryName(model)!);
            GD.Print($"Ears: SenseVoice loaded in {clock.ElapsedMilliseconds} ms");
            return hearing;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Ears: {e.Message}");
            return null;
        }
    }

    private static (VoiceDetector, SmartTurn)? LoadTurns()
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var detector = new VoiceDetector(ModelFiles.OnDisk(TurnModels + VoiceDetector.ModelFile, "turn-taking"));
            var turn = new SmartTurn(ModelFiles.OnDisk(TurnModels + SmartTurn.ModelFile, "turn-taking"));
            GD.Print($"Listening: Silero and Smart Turn loaded in {clock.ElapsedMilliseconds} ms");
            return (detector, turn);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Listening: {e.Message}");
            return null;
        }
    }

    public override void _ExitTree()
    {
        if (_hearing is { IsCompletedSuccessfully: true, Result: { } hearing })
            hearing.Dispose();
        if (_turns is { IsCompletedSuccessfully: true, Result: { } turns })
        {
            turns.Detector.Dispose();
            turns.Turn.Dispose();
        }
    }

    // ---- Dev -----------------------------------------------------------------------------------------------------------

    /// <summary>Her listening dev actions (see tools/dev/README.md); false when <paramref name="name"/> isn't one of them.</summary>
    public bool Dev(string name, string rest)
    {
        switch (name)
        {
            case "hear": _ = HearFile(rest, Talk.Key); break;            // hear:path.wav as if said holding the talk key (no microphone)
            case "dictate": _ = HearFile(rest, Talk.Mic); break;         // dictate:path.wav as if said into the slip's microphone
            case "talk": Play(rest, Talk.Hold); break;      // talk:path.wav the same, as it is said: she listens (and nods) for its length first
            case "fakemic": _pretend = true; break;         // from now on, hands-free listening hears only recordings (mictalk), never the microphone
            case "mictalk": Play(rest, Talk.Open); break;   // mictalk:path.wav said into the open hands-free microphone (after fakemic)
            case "converse": Converse(rest switch { "on" => true, "off" => false, _ => !Conversing }); break;
            case "mic": _chatBox.DevMic(); break;           // clicks the slip's microphone (opens the real microphone)
            case "listen": Listen(Talk.Hold); break;        // listens as if held on her (opens the real microphone)…
            case "listened": _ = Heard(); break;            // …until this
            default: return false;
        }
        return true;
    }

    /// <summary>Dev: a recording heard as if said holding the talk key, or into the slip's microphone (no microphone opened).</summary>
    private async Task HearFile(string path, Talk how)
    {
        if (_stool.Her is null || !_stool.CanAnswer || !HasEars)
        {
            GD.PrintErr("Ears: hear: needs her on the stool, a chat provider and the speech model");
            return;
        }
        _stool.OpenChat(focus: false);                      // as listening does
        await Understand(Read(path), how);
    }

    /// <summary>
    /// Dev: a recording played into her ears as if the player were saying it now, held on her (she listens for as long
    /// as it lasts, then hears it) or into the open hands-free microphone (she notices them start and finish).
    /// </summary>
    private void Play(string path, Talk how)
    {
        if (how == Talk.Open && (_talking != Talk.Open || !_pretend))
        {
            GD.PrintErr("Listening: mictalk: needs fakemic first, and her listening hands-free");
            return;
        }
        _recording = (Read(path), 0);
        if (how == Talk.Hold)
            Listen(Talk.Hold);
    }

    /// <summary>Dev: the next <paramref name="delta"/> seconds of the recording into her ears; silence once it is over, until she stops listening.</summary>
    private void Recording(double delta)
    {
        if (_talking == Talk.None)
            _recording = null;
        if (_talking == Talk.None || !_ears.Pretending)
            return;
        var sound = new float[(int)(delta * Hearing.SampleRate)];
        if (_recording is { } recording)
        {
            var count = Math.Clamp(recording.Samples.Length - recording.At, 0, sound.Length);
            recording.Samples.AsSpan(recording.At, count).CopyTo(sound);
            _recording = recording.At + count < recording.Samples.Length ? (recording.Samples, recording.At + count) : null;
            if (_recording is null && _talking == Talk.Hold)
                _ = Heard();                                // held on her: the end of the recording is the release
        }
        _ears.Push(sound);
    }

    private static float[] Read(string path)
    {
        var wav = new Wav();
        var samples = wav.Feed(File.ReadAllBytes(path));
        return Hearing.To16k(samples, wav.SampleRate);
    }
}
