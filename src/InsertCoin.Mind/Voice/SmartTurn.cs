using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace InsertCoin.Mind;

/// <summary>
/// Pipecat's Smart Turn v3.2 (BSD-2): whether someone has finished their turn, judged from the last 8 s of their voice
/// (wording and intonation, not only silence). A port of its inference: the audio front-padded with silence to 8 s,
/// Whisper's log-mel features (as transformers' WhisperFeatureExtractor with chunk_length=8 and do_normalize=True),
/// then the model, which gives the probability that the turn is complete. Checked against transformers to 0.000025
/// (the agent spike, 2026-10-05).
/// </summary>
/// <remarks>
/// The full-precision build, not the 8-bit one: the 8-bit build's answers on borderline turns shift with ONNX
/// Runtime's version and optimisation level; this one gives the same answer everywhere. About 50 ms a decision on
/// the processor, features included.
/// </remarks>
public sealed class SmartTurn : IDisposable
{
    public const string ModelFile = "smart-turn-v3.2.onnx";

    /// <summary>
    /// How sure it must be that the player has finished for the turn to end at a pause; below it, the turn waits for
    /// the long quiet instead. Measured on the player's own voice (2026-10-06, eleven English turns, the agent note §2):
    /// at 0.5 it cut them off at three thinking pauses ("…binary trees is …", "let me see, um …"; 0.79 to 0.90); at
    /// 0.95 at none, and two of their endings (0.92, 0.66) waited for the quiet instead.
    /// </summary>
    public const float Sure = 0.95f;
    public const int Rate = 16000, Seconds = 8, Samples = Rate * Seconds;
    private const int Fft = 400, Hop = 160, Mels = 80, Frames = Samples / Hop, Bins = Fft / 2 + 1;

    private static readonly double[] Window = [.. Enumerable.Range(0, Fft).Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / Fft))];
    private static readonly float[,] Cos = new float[Bins, Fft], Sin = new float[Bins, Fft];
    private static readonly double[,] Filters = MelFilters();          // [mel, bin]
    private readonly InferenceSession _session;

    static SmartTurn()
    {
        for (var k = 0; k < Bins; k++)
            for (var n = 0; n < Fft; n++)
            {
                Cos[k, n] = (float)(Math.Cos(2 * Math.PI * k * n / Fft) * Window[n]);
                Sin[k, n] = (float)(Math.Sin(2 * Math.PI * k * n / Fft) * Window[n]);
            }
    }

    public SmartTurn(string model) =>
        _session = new InferenceSession(model, new SessionOptions { ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, InterOpNumThreads = 1, IntraOpNumThreads = 2 });

    /// <summary>The probability that the turn in <paramref name="audio"/> (16 kHz, up to now) is complete.</summary>
    public float Complete(ReadOnlySpan<float> audio)
    {
        var input = NamedOnnxValue.CreateFromTensor("input_features", new DenseTensor<float>(Features(audio), [1, Mels, Frames]));
        using var output = _session.Run([input]);
        return output.First().AsEnumerable<float>().First();
    }

    /// <summary>Whisper's log-mel features of the last 8 s, front-padded with silence: [mel * frames + frame].</summary>
    public static float[] Features(ReadOnlySpan<float> audio)
    {
        if (audio.Length > Samples)
            audio = audio[^Samples..];
        var x = new double[Samples];
        for (var i = 0; i < audio.Length; i++)
            x[Samples - audio.Length + i] = audio[i];
        // zero mean, unit variance over the whole 8 s, the padding included (the reference pads before extracting)
        var mean = x.Average();
        var variance = x.Sum(v => (v - mean) * (v - mean)) / x.Length;
        var scale = 1 / Math.Sqrt(variance + 1e-7);
        for (var i = 0; i < x.Length; i++)
            x[i] = (x[i] - mean) * scale;

        // centred frames, reflect-padded by half a window at each end; Frames + 1 of them, the last dropped
        var padded = new float[Samples + Fft];
        for (var i = 0; i < Samples; i++)
            padded[Fft / 2 + i] = (float)x[i];
        for (var i = 1; i <= Fft / 2; i++)
        {
            padded[Fft / 2 - i] = (float)x[i];
            padded[Fft / 2 + Samples - 1 + i] = (float)x[Samples - 1 - i];
        }
        var logMel = new double[Mels * Frames];
        Parallel.For(0, Frames, () => new double[Bins], (t, _, power) =>
        {
            var frame = padded.AsSpan(t * Hop, Fft);
            for (var k = 0; k < Bins; k++)
            {
                double re = 0, im = 0;
                for (var n = 0; n < Fft; n++)
                {
                    re += frame[n] * Cos[k, n];
                    im -= frame[n] * Sin[k, n];
                }
                power[k] = re * re + im * im;
            }
            for (var m = 0; m < Mels; m++)
            {
                double sum = 0;
                for (var k = 0; k < Bins; k++)
                    sum += Filters[m, k] * power[k];
                logMel[m * Frames + t] = Math.Log10(Math.Max(sum, 1e-10));
            }
            return power;
        }, _ => { });
        var floor = logMel.Max() - 8;
        var features = new float[logMel.Length];
        for (var i = 0; i < logMel.Length; i++)
            features[i] = (float)((Math.Max(logMel[i], floor) + 4) / 4);
        return features;
    }

    /// <summary>transformers' mel_filter_bank(201, 80, 0, 8000, 16000, norm="slaney", mel_scale="slaney").</summary>
    private static double[,] MelFilters()
    {
        static double ToMel(double hz) => hz >= 1000 ? 15 + Math.Log(hz / 1000) * (27 / Math.Log(6.4)) : 3 * hz / 200;
        static double ToHz(double mel) => mel >= 15 ? 1000 * Math.Exp(Math.Log(6.4) / 27 * (mel - 15)) : 200 * mel / 3;
        var low = ToMel(0);
        var high = ToMel(8000);
        var edges = Enumerable.Range(0, Mels + 2).Select(i => ToHz(low + (high - low) * i / (Mels + 1))).ToArray();
        var fft = Enumerable.Range(0, Bins).Select(i => 8000.0 * i / (Bins - 1)).ToArray();
        var filters = new double[Mels, Bins];
        for (var m = 0; m < Mels; m++)
        {
            var norm = 2 / (edges[m + 2] - edges[m]);
            for (var k = 0; k < Bins; k++)
            {
                var down = (fft[k] - edges[m]) / (edges[m + 1] - edges[m]);
                var up = (edges[m + 2] - fft[k]) / (edges[m + 2] - edges[m + 1]);
                filters[m, k] = Math.Max(0, Math.Min(down, up)) * norm;
            }
        }
        return filters;
    }

    public void Dispose() => _session.Dispose();
}
