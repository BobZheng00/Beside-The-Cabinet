using System.Text.RegularExpressions;
using SherpaOnnx;

namespace InsertCoin.Mind;

/// <summary>
/// Her ears: SenseVoice-Small (Chinese, English, Japanese, Korean, Cantonese, told apart by itself) through
/// sherpa-onnx, on the CPU, so it never takes the graphics card from her voice or a game. On the development laptop
/// it loads in about a second and hears 3–4 s of speech in about 0.1 s (measured 2026-10-03). One utterance at a
/// time, whole: it is heard once the player stops talking.
/// </summary>
public sealed partial class Hearing : IDisposable
{
    public const string ModelFile = "model.int8.onnx", TokensFile = "tokens.txt";
    /// <summary>The rate it hears at; other audio is brought to it first (see <see cref="To16k"/>).</summary>
    public const int SampleRate = 16000;

    private readonly OfflineRecognizer _recognizer;
    private readonly object _gate = new();

    /// <param name="folder">Where <see cref="ModelFile"/> and <see cref="TokensFile"/> are.</param>
    public Hearing(string folder, int threads = 4)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.SenseVoice.Model = Path.Combine(folder, ModelFile);
        config.ModelConfig.SenseVoice.Language = "auto";
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;      // "11 o'clock", "２０２６年": digits, punctuation
        config.ModelConfig.Tokens = Path.Combine(folder, TokensFile);
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.Debug = 0;
        _recognizer = new OfflineRecognizer(config);
    }

    /// <summary>The words in <paramref name="samples"/> (mono, at <see cref="SampleRate"/>); empty when there are none.</summary>
    public string Hear(float[] samples)
    {
        if (samples.Length < SampleRate / 4)
            return "";
        lock (_gate)
        {
            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, samples);
            _recognizer.Decode(stream);
            return Tidy(stream.Result.Text);
        }
    }

    /// <summary>
    /// SenseVoice spaces Japanese out word by word ("今日は あと 何分 遊べる の？"): spaces between Chinese or Japanese
    /// characters go, those between English words stay.
    /// </summary>
    public static string Tidy(string text) => CjkSpaces().Replace(text, "").Trim();

    /// <summary>
    /// Mono audio at <paramref name="rate"/> brought to 16 kHz: smoothed below 8 kHz first (a windowed-sinc filter), so
    /// higher sounds don't fold back into speech, then sampled at the new rate. sherpa-onnx can do this itself, but at
    /// Godot's 48 kHz it took about 0.2 s an utterance, twice what hearing it takes.
    /// </summary>
    public static float[] To16k(ReadOnlySpan<float> samples, int rate)
    {
        if (rate == SampleRate)
            return samples.ToArray();
        var step = rate / (double)SampleRate;
        var cutoff = 0.45 / step;                             // of the input rate: 7.2 kHz, under the new Nyquist
        const int half = 16;
        var taps = new double[2 * half + 1];
        var sum = 0.0;
        for (var i = -half; i <= half; i++)
        {
            var sinc = i == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * i) / (Math.PI * i);
            var window = 0.54 + 0.46 * Math.Cos(Math.PI * i / half);   // Hamming
            sum += taps[i + half] = sinc * window;
        }
        var length = (int)(samples.Length / step);
        var output = new float[length];
        for (var n = 0; n < length; n++)
        {
            var centre = (int)Math.Round(n * step);
            var value = 0.0;
            for (var i = -half; i <= half; i++)
            {
                var at = centre + i;
                if (at >= 0 && at < samples.Length)
                    value += samples[at] * taps[i + half];
            }
            output[n] = (float)(value / sum);
        }
        return output;
    }

    public void Dispose() => _recognizer.Dispose();

    [GeneratedRegex(@"(?<=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}ー、。，！？「」])\s+(?=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}ー、。，！？「」])")]
    private static partial Regex CjkSpaces();
}
