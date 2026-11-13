using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace InsertCoin.Mind;

/// <summary>
/// Kyutai's Pocket TTS (CC BY 4.0) on the processor, in the app itself, with ONNX Runtime: English with nothing to set up.
/// It copies a voice from about 30 seconds of it, and makes speech a frame (80 ms) at a time: the main model gives the next
/// frame's conditioning, the flow turns noise into that frame's latent in four steps, and the decoder turns the latent into
/// sound, which plays as it comes. The files are tools/models/pocket_export.py's export of the English model (#280), run as
/// Pocket TTS's own loop runs them, at the settings #280 found: 4 threads, temperature 0.3, four flow steps.
/// </summary>
public sealed partial class PocketTts : IDisposable
{
    public const int Rate = 24000;
    /// <summary>The export's files, as tools/models/fetch.py puts them in its folder.</summary>
    public static readonly string[] Files =
        ["text_conditioner.onnx", "encoder.onnx", "lm_main.int8.onnx", "lm_flow.int8.onnx", "decoder.onnx", "vocab.json", "token_scores.json"];

    private const int Threads = 4, FlowSteps = 4, Latent = 32, Width = 1024;
    private const double FrameRate = 12.5;
    private const float Temperature = 0.3f, EndThreshold = -4f;
    private const float Loud = 0.01f;                   // a line whose loudest sample stays under this came out silent
    private const double LongestSample = 35;            // seconds of a recording it reads (Pocket TTS's own loader keeps 10, too few for a close copy)
    private static readonly RunOptions Run = new();

    private readonly string _folder;
    private readonly InferenceSession _text, _main, _flow, _decoder;
    private readonly Dictionary<string, (int Id, float Score)> _pieces;
    private readonly Func<TimeSpan> _now;
    private readonly object _gate = new();              // one step at a time, and never while the models are let go
    private bool _disposed;
    private (string File, OrtValue[] State)? _voice;    // the main model's state after the recording: read once, kept for each line

    /// <summary>What this PC has shown it can do. Until a line is heard, what 4 fast laptop cores took (#280).</summary>
    public HoldBack Held { get; } = new(0.45);

    /// <summary>Loads the models in <paramref name="folder"/>; throws when one is missing or won't load.</summary>
    public PocketTts(string folder, Func<TimeSpan>? clock = null)
    {
        _folder = folder;
        _now = clock ?? (() => TimeSpan.FromTicks(System.Diagnostics.Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency));
        _pieces = Pieces(folder);
        _text = Session("text_conditioner.onnx");
        _main = Session("lm_main.int8.onnx");
        _flow = Session("lm_flow.int8.onnx");
        _decoder = Session("decoder.onnx");
    }

    /// <summary>What of the export is missing from <paramref name="folder"/>, or null when it is all there.</summary>
    public static string? Missing(string folder) =>
        Files.FirstOrDefault(f => !File.Exists(Path.Combine(folder, f))) is { } file ? $"{file} is missing" : null;

    /// <summary>
    /// The voice a character speaks with in English: Eve for Shino and George for Fumiriya, two VCTK speakers (CC BY 4.0) as
    /// Pocket TTS speaks them, chosen by ear by the owner (#278). Each is a recording of about 30 seconds that comes with the
    /// character, named for the speaker. Null for anyone else, or another language.
    /// </summary>
    public static VoiceProfile? Suggested(string character, string language)
    {
        var name = (character, language) switch
        {
            ("shino", "en") => "Eve",
            ("fumiriya", "en") => "George",
            _ => null,
        };
        if (name is null)
            return null;
        var file = name.ToLowerInvariant() + ".wav";
        return new VoiceProfile
        {
            Engine = VoiceProfile.Pocket, Name = name, Speaker = name.ToLowerInvariant(), Language = language, Clips = [new VoiceClip(file, "", language)],
            About = $"{name}, a speaker of the VCTK corpus (CC BY 4.0), as Kyutai's Pocket TTS (CC BY 4.0) speaks them.",
        };
    }

    private InferenceSession Session(string file)
    {
        using var options = new SessionOptions { IntraOpNumThreads = Threads, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        return new InferenceSession(Path.Combine(_folder, file), options);
    }

    // ---- The text ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The line as Pocket TTS prepares it (prepare_text_prompt, English): its replace_characters, spaces tidied, a sentence mark
    /// kept over a comma after it, the first letter a capital, and a full stop at the end when it has no sentence mark. With it,
    /// how many frames to make after the model hears the end: more for a line of four words or fewer, which it ends too soon.
    /// </summary>
    public static (string Text, int FramesAfterEnd) Prepared(string line)
    {
        var text = string.Join(' ', line.Replace('’', '\'').Replace('‘', '\'').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        text = MarkThenPause().Replace(text, "$1");
        if (text.Length == 0)
            return ("", 0);
        var after = text.Split(' ').Length <= 4 ? 5 : 3;
        text = char.ToUpperInvariant(text[0]) + text[1..];
        var core = text.TrimEnd('"', '\'', '”', '’', ')', ']', '»', ' ');
        if (core.Length > 0 && !".!?…".Contains(core[^1]))
            text = ",;:-–—".Contains(core[^1]) ? core.TrimEnd(',', ';', ':', '-', '–', '—', ' ') + "." + text[core.Length..].Trim() : text + ".";
        return (text, after);
    }

    [GeneratedRegex(@"([.!?…])\s*[,;:]")]
    private static partial Regex MarkThenPause();

    /// <summary>The model's word pieces, with their ids and scores, from the export's vocab.json and token_scores.json.</summary>
    public static Dictionary<string, (int Id, float Score)> Pieces(string folder)
    {
        var ids = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(folder, "vocab.json")))!;
        var scores = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(Path.Combine(folder, "token_scores.json")))!;
        return ids.ToDictionary(p => p.Key, p => (p.Value, scores[p.Key]));
    }

    /// <summary>
    /// The text in the model's word pieces, as its tokenizer splits it (a unigram model): ▁ for each space and in front, the
    /// likeliest split of each word, and a character it has no piece for as the pieces of its UTF-8 bytes.
    /// </summary>
    public static long[] Tokens(string text, IReadOnlyDictionary<string, (int Id, float Score)> pieces)
    {
        var tokens = new List<long>();
        var unknown = pieces.Values.Min(p => p.Score) - 10;            // as the tokenizer scores a character it has no piece for
        var longest = pieces.Keys.Max(k => k.Length);
        foreach (var word in Regex.Split("▁" + text.Replace(' ', '▁'), "(?=▁)").Where(w => w.Length > 0))
        {
            var best = new float[word.Length + 1];
            var from = new (int Start, bool Bytes)[word.Length + 1];
            Array.Fill(best, float.NegativeInfinity);
            best[0] = 0;
            for (var start = 0; start < word.Length; start++)
            {
                if (float.IsNegativeInfinity(best[start]))
                    continue;
                var single = false;
                for (var length = 1; length <= Math.Min(longest, word.Length - start); length++)
                {
                    if (!pieces.TryGetValue(word.Substring(start, length), out var piece) || Special(piece.Id))
                        continue;
                    single |= length == 1;
                    if (best[start] + piece.Score > best[start + length])
                        (best[start + length], from[start + length]) = (best[start] + piece.Score, (start, false));
                }
                var character = char.IsHighSurrogate(word[start]) && start + 1 < word.Length ? 2 : 1;
                if (!single && best[start] + unknown > best[start + character])
                    (best[start + character], from[start + character]) = (best[start] + unknown, (start, true));
            }
            var split = new List<long>();
            for (var end = word.Length; end > 0; end = from[end].Start)
            {
                var part = word[from[end].Start..end];
                split.InsertRange(0, from[end].Bytes
                    ? Encoding.UTF8.GetBytes(part).Select(b => (long)pieces[$"<0x{b:X2}>"].Id)
                    : [pieces[part].Id]);
            }
            tokens.AddRange(split);
        }
        return [.. tokens];

        static bool Special(int id) => id < 4 + 256;               // <unk>, <s>, </s>, <pad>, then the bytes: never matched as text
    }

    // ---- Speaking ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Says <paramref name="text"/> in this voice: pieces of speech as they are made, once enough is made to play without a gap.
    /// A line that comes out silent is made once more, with another seed; silent again, it throws <see cref="VoiceException"/>.
    /// </summary>
    /// <param name="folder">The voice's folder, which holds its recording.</param>
    /// <param name="limit">The most frames to make (12.5 a second), for something that must stay short; null: as long as the line takes.</param>
    public async IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile voice, string folder, [EnumeratorCancellation] CancellationToken cancel = default,
                                                    int? limit = null)
    {
        var started = _now();
        await foreach (var audio in Held.Through(Made(text, Path.Combine(folder, voice.Clips[0].File), limit, cancel), Rate, text.Length, started, _now))
            yield return audio;
    }

    private async IAsyncEnumerable<float[]> Made(string line, string recording, int? limit, [EnumeratorCancellation] CancellationToken cancel)
    {
        var (text, after) = Prepared(line);
        if (text.Length == 0)
            yield break;
        for (var seed = 0; seed < 2; seed++)
        {
            using var take = await Task.Run(() => Begin(text, recording, after, limit, seed), cancel);
            var quiet = new List<float[]>();            // held until it makes a sound: a silent take is let go unheard
            var loud = false;
            while (await Task.Run(take.Next, cancel) is { } frame)
            {
                cancel.ThrowIfCancellationRequested();
                if (!loud && !frame.Any(s => Math.Abs(s) >= Loud))
                {
                    quiet.Add(frame);
                    continue;
                }
                if (!loud)
                    foreach (var held in quiet)
                        yield return held;
                loud = true;
                yield return frame;
            }
            if (loud)
                yield break;
        }
        throw new VoiceException($"\"{line}\" came out silent twice");
    }

    /// <summary>A line's state: the voice's, then its words read; its frames come from <see cref="Take.Next"/>.</summary>
    private Take Begin(string text, string recording, int after, int? limit, int seed)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var tokens = Tokens(text, _pieces);
            using var ids = OrtValue.CreateTensorValueFromMemory(tokens, [1, tokens.Length]);
            using var embedded = _text.Run(Run, _text.InputNames, [ids], _text.OutputNames);
            using var none = Empty(Latent);
            var state = Step(_main, [none, embedded.First(), .. Voice(recording)], out var unused);
            Free(unused);
            var frames = (int)Math.Ceiling((tokens.Length / 3.0 + 2) * FrameRate);      // Pocket TTS's own ceiling: 3 tokens a second and 2 s more
            return new Take(this, state, Zeros(_decoder, 1), after, Math.Min(frames, limit ?? int.MaxValue), seed);
        }
    }

    /// <summary>The main model's state once it has heard the recording, read the first time this voice speaks.</summary>
    private OrtValue[] Voice(string recording)
    {
        if (_voice is { } known && known.File == recording)
            return known.State;
        var wav = new Wav();
        var samples = wav.Feed(File.ReadAllBytes(recording));
        // shortcut: no resampling, as the recordings that come with the characters are 24 kHz; the guided recorder (#265) brings its own
        if (wav.SampleRate != Rate)
            throw new VoiceException($"its recording is {wav.SampleRate} Hz, not {Rate}");
        samples = samples[..Math.Min(samples.Length, (int)(LongestSample * Rate))];
        using var encoder = Session("encoder.onnx");                // needed once a voice: let go after, with what it took to read the recording
        using var audio = OrtValue.CreateTensorValueFromMemory(samples, [1, 1, samples.Length]);
        using var latents = encoder.Run(Run, encoder.InputNames, [audio], encoder.OutputNames);
        using var none = Empty(Latent);
        var zeros = Zeros(_main, 2);
        var state = Step(_main, [none, latents.First(), .. zeros], out var unused);
        Free(zeros);
        Free(unused);
        if (_voice is { } old)
            Free(old.State);
        _voice = (recording, state);
        return state;
    }

    /// <summary>One frame after another of a line, until the model has ended it.</summary>
    private sealed class Take(PocketTts engine, OrtValue[] state, OrtValue[] decoder, int after, int frames, int seed) : IDisposable
    {
        private readonly Random _random = new(seed);
        private readonly float[] _latent = Enumerable.Repeat(float.NaN, Latent).ToArray();     // NaN: the start, as Pocket TTS begins
        private int _step;
        private int? _end;

        /// <summary>The next frame's sound, or null when the line is over.</summary>
        public float[]? Next()
        {
            lock (engine._gate)
            {
                ObjectDisposedException.ThrowIf(engine._disposed, engine);
                if (_step >= frames)
                    return null;
                using var sequence = OrtValue.CreateTensorValueFromMemory(_latent, [1, 1, Latent]);
                using var none = Empty(Width);
                var next = Step(engine._main, [sequence, none, .. state], out var outputs);
                Free(state);
                state = next;
                using var conditioning = outputs[0];
                using (outputs[1])
                    if (_end is null && outputs[1].GetTensorDataAsSpan<float>()[0] > EndThreshold)
                        _end = _step;                   // the model holds this back itself for its first frames
                if (_end is { } end && _step >= end + after)
                    return null;

                // the flow, from noise to this frame's latent (lsd_decode)
                var x = new float[Latent];
                var spread = MathF.Sqrt(Temperature);
                for (var i = 0; i < Latent; i++)
                    x[i] = spread * MathF.Sqrt(-2 * MathF.Log(1 - _random.NextSingle())) * MathF.Cos(2 * MathF.PI * _random.NextSingle());
                for (var i = 0; i < FlowSteps; i++)
                {
                    using var s = OrtValue.CreateTensorValueFromMemory(new[] { (float)i / FlowSteps }, [1, 1]);
                    using var t = OrtValue.CreateTensorValueFromMemory(new[] { (float)(i + 1) / FlowSteps }, [1, 1]);
                    using var current = OrtValue.CreateTensorValueFromMemory(x, [1, Latent]);
                    using var direction = engine._flow.Run(Run, engine._flow.InputNames, [conditioning, s, t, current], engine._flow.OutputNames);
                    var flow = direction.First().GetTensorDataAsSpan<float>();
                    for (var k = 0; k < Latent; k++)
                        x[k] += flow[k] / FlowSteps;
                }
                x.CopyTo(_latent, 0);

                using var latent = OrtValue.CreateTensorValueFromMemory(x, [1, 1, Latent]);
                var decoded = Step(engine._decoder, [latent, .. decoder], out var sound);
                Free(decoder);
                decoder = decoded;
                var frame = sound[0].GetTensorDataAsSpan<float>().ToArray();
                Free(sound);
                if (_step++ == 0)
                    for (var i = 0; i < Math.Min(frame.Length, Rate / 200); i++)
                        frame[i] *= (float)i / (Rate / 200 - 1);    // a fresh decoder clicks: its first 5 ms faded in, as Pocket TTS does
                return frame;
            }
        }

        public void Dispose()
        {
            lock (engine._gate)
            {
                Free(state);
                Free(decoder);
            }
        }
    }

    // ---- ONNX Runtime --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a model whose last inputs are its state and whose outputs end with the state after: returns that, and puts the
    /// other outputs (in front) in <paramref name="outputs"/>. The caller frees both.
    /// </summary>
    private static OrtValue[] Step(InferenceSession session, OrtValue[] inputs, out OrtValue[] outputs)
    {
        var all = session.Run(Run, session.InputNames, inputs, session.OutputNames).ToArray();
        var front = all.Length - (session.InputNames.Count - FirstState(session));
        outputs = all[..front];
        return all[front..];
    }

    /// <summary>Where a model's state begins among its inputs: after the latent (decoder) or the sequence and the text (main).</summary>
    private static int FirstState(InferenceSession session) => session.InputNames.Count(n => !n.StartsWith("state_", StringComparison.Ordinal));

    /// <summary>A model's state as it starts: zeros, with any size not fixed as 1 (the export drops those first zeros itself).</summary>
    private static OrtValue[] Zeros(InferenceSession session, int skip) =>
    [
        .. session.InputNames.Skip(skip).Select(name =>
        {
            var input = session.InputMetadata[name];
            var shape = input.Dimensions.Select(d => d < 0 ? 1L : d).ToArray();
            var count = (int)shape.Aggregate(1L, (a, b) => a * b);
            return input.ElementDataType switch
            {
                TensorElementType.Float => OrtValue.CreateTensorValueFromMemory(new float[count], shape),
                TensorElementType.Int64 => OrtValue.CreateTensorValueFromMemory(new long[count], shape),
                TensorElementType.Bool => OrtValue.CreateTensorValueFromMemory(new bool[count], shape),
                var other => throw new InvalidDataException($"{name} is {other}, which the export never has"),
            };
        }),
    ];

    /// <summary>Nothing of a sequence: a model run on the conditioning alone.</summary>
    private static OrtValue Empty(int width) => OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, [1, 0, width]);

    private static void Free(IEnumerable<OrtValue> values)
    {
        foreach (var value in values)
            value.Dispose();
    }

    /// <summary>Lets the models go, once any step under way has finished.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_voice is { } voice)
                Free(voice.State);
            _text.Dispose();
            _main.Dispose();
            _flow.Dispose();
            _decoder.Dispose();
        }
    }
}
