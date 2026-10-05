using SherpaOnnx;

namespace InsertCoin.Mind;

/// <summary>
/// Silero's voice detector (MIT) through sherpa-onnx: whether someone is speaking, judged 32 ms at a time, on the
/// processor (well under a millisecond each). Used while she listens hands-free, to notice the player start and
/// pause (see <see cref="TurnTaking"/>).
/// </summary>
public sealed class VoiceDetector : IDisposable
{
    public const string ModelFile = "silero_vad.onnx";
    private const int Step = 512;               // samples at 16 kHz the model takes at a time

    private readonly VoiceActivityDetector _vad;
    private readonly List<float> _pending = [];

    public VoiceDetector(string model)
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = model;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = 0.1f;     // every pause is seen; TurnTaking decides what a pause means
        config.SileroVad.MinSpeechDuration = 0.1f;
        config.SileroVad.WindowSize = Step;
        config.SampleRate = Hearing.SampleRate;
        config.NumThreads = 1;
        _vad = new VoiceActivityDetector(config, 60);
    }

    /// <summary>Takes the next sound (16 kHz): whether someone is speaking now.</summary>
    public bool Hear(ReadOnlySpan<float> samples)
    {
        _pending.AddRange(samples);
        var taken = 0;
        for (; taken + Step <= _pending.Count; taken += Step)
            _vad.AcceptWaveform(_pending.GetRange(taken, Step).ToArray());
        _pending.RemoveRange(0, taken);
        while (!_vad.IsEmpty())
            _vad.Pop();                 // it gathers the speech it heard; only whether there is speech now matters here
        return _vad.IsSpeechDetected();
    }

    /// <summary>Forgets what it heard: a new window of listening starts afresh.</summary>
    public void Reset()
    {
        _vad.Reset();
        _pending.Clear();
    }

    public void Dispose() => _vad.Dispose();
}
