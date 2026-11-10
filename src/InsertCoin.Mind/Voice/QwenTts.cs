using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace InsertCoin.Mind;

/// <summary>
/// How much of a line to hold back so it plays without a gap on this PC. A line of L seconds made at a pace of P seconds
/// of work per second of speech is done after L×P seconds, so it can start L×(P-1) seconds in: by then L×(P-1)/P seconds of
/// it are made. Holding back an amount of audio, rather than a time, keeps it right when the PC is slower than it was.
/// The pace and how long a character takes are learned from the lines this PC has said.
/// </summary>
public sealed class HoldBack
{
    private const double Fade = 0.8;                    // older lines count for less: the PC may have got busier
    private double _work, _speech, _chars;

    /// <summary>Seconds of work for a second of speech. Until a line is heard, what 4 fast cores measured.</summary>
    public double Pace => _speech > 0 ? _work / _speech : 1.4;

    /// <summary>Roughly how long a character takes to say.</summary>
    public double SecondsPerCharacter => _chars > 0 ? _speech / _chars : 0.2;

    /// <summary>Seconds of the line's audio to gather before any of it plays.</summary>
    public double Seconds(int characters)
    {
        var line = characters * SecondsPerCharacter;
        return Pace <= 1 ? 0 : line * (Pace - 1) / Pace;
    }

    /// <summary>A whole line was made: <paramref name="work"/> seconds from asking to its end, for <paramref name="speech"/> seconds of audio.</summary>
    public void Learn(double work, double speech, int characters)
    {
        if (speech <= 0)
            return;
        _work = _work * Fade + work;
        _speech = _speech * Fade + speech;
        _chars = _chars * Fade + characters;
    }
}

/// <summary>
/// Talks to Qwen TTS's own server (qwentts.cpp's <c>tts-server</c>, on this PC): one sentence at a time, in one of its
/// built-in speakers, the speech streaming back as it is made and held back just long enough to play without a gap.
/// </summary>
public sealed class QwenTts(HttpClient http, TimeSpan? stall = null, Func<TimeSpan>? clock = null)
{
    private const int Rate = 24000;
    private static readonly System.Text.Json.JsonSerializerOptions Unset = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };   // the server refuses a field sent as null
    private readonly TimeSpan _stall = stall ?? TimeSpan.FromSeconds(60);
    private readonly Func<TimeSpan> _now = clock ?? (() => TimeSpan.FromTicks(System.Diagnostics.Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency));

    /// <summary>What this PC has shown it can do.</summary>
    public HoldBack Held { get; } = new();

    /// <summary>
    /// The voice a character speaks with in Chinese or Japanese: the player's to change later (#283), chosen by ear by
    /// the owner for the two built-in characters. Null for anyone else, or another language.
    /// </summary>
    public static VoiceProfile? Suggested(string character, string language)
    {
        var (name, speaker) = (character, language) switch
        {
            ("shino", "zh") => ("Serena", "serena"),
            ("shino", "ja") => ("Ono Anna", "ono_anna"),
            ("fumiriya", "zh" or "ja") => ("Aiden", "aiden"),
            _ => ((string?)null, (string?)null),
        };
        return name is null ? null : new VoiceProfile { Engine = VoiceProfile.Qwen, Name = name, Speaker = speaker, Language = language };
    }

    /// <summary>
    /// Says <paramref name="text"/> as this voice, in its language: pieces of speech as they are made, once enough is
    /// made to play without a gap. Throws <see cref="VoiceException"/> when the server turns it down or goes quiet.
    /// </summary>
    /// <param name="limit">The most frames to make (12.5 a second), for something that must stay short; null: as long as the line takes.</param>
    public async IAsyncEnumerable<VoiceAudio> Speak(string text, VoiceProfile voice, [EnumeratorCancellation] CancellationToken cancel = default, int? limit = null)
    {
        var started = _now();
        // seed 0: one of the takes the voices were chosen on; the same line comes out the same
        var body = new { input = text, voice = voice.Speaker, language = SpokenLanguage.Name(voice.Language), response_format = "pcm", seed = 0, max_new_tokens = limit };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech") { Content = JsonContent.Create(body, options: Unset) };
        using var response = await Quietly(() => http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel), cancel);
        if (!response.IsSuccessStatusCode)
            throw VoiceException.From(await response.Content.ReadAsStringAsync(cancel));
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);

        var hold = Held.Seconds(text.Length);
        var held = new List<float[]>();
        var heldSeconds = 0.0;
        var made = 0L;
        var flowing = hold <= 0;
        var buffer = new byte[16384];
        var left = -1;                                  // a sample cut in two between reads: its first byte
        while (true)
        {
            var read = await Quietly(() => stream.ReadAsync(buffer, cancel).AsTask(), cancel);
            if (read == 0)
                break;
            var samples = Samples(buffer.AsSpan(0, read), ref left);
            if (samples.Length == 0)
                continue;
            made += samples.Length;
            if (flowing)
            {
                yield return new VoiceAudio(Rate, samples);
                continue;
            }
            held.Add(samples);
            heldSeconds += (double)samples.Length / Rate;
            if (heldSeconds < hold)
                continue;
            flowing = true;
            foreach (var piece in held)
                yield return new VoiceAudio(Rate, piece);
            held.Clear();
        }
        foreach (var piece in held)
            yield return new VoiceAudio(Rate, piece);   // a line shorter than guessed
        Held.Learn((_now() - started).TotalSeconds, (double)made / Rate, text.Length);
    }

    /// <summary>A step that may never finish if the server has hung: nothing for <see cref="_stall"/> is a failure, not a wait.</summary>
    private async Task<T> Quietly<T>(Func<Task<T>> step, CancellationToken cancel)
    {
        var task = step();
        if (await Task.WhenAny(task, Task.Delay(_stall, cancel)) == task)
            return await task;
        cancel.ThrowIfCancellationRequested();
        throw new VoiceException("Qwen TTS stopped answering");
    }

    /// <summary>Little-endian 16-bit samples as -1..1, carrying a byte cut off at the end of one read into the next.</summary>
    private static float[] Samples(ReadOnlySpan<byte> bytes, ref int left)
    {
        var count = (bytes.Length + (left >= 0 ? 1 : 0)) / 2;
        var samples = new float[count];
        var at = 0;
        for (var i = 0; i < count; i++)
        {
            var low = left >= 0 ? left : bytes[at++];
            left = -1;
            samples[i] = (short)(low | bytes[at++] << 8) / 32768f;
        }
        if (at < bytes.Length)
            left = bytes[at];
        return samples;
    }
}

/// <summary>
/// The two files of Qwen TTS's weights the player fetches on Their voice: ldov's GGUF conversion of Qwen3-TTS (Apache-2.0)
/// at a fixed commit. Their sizes and SHA-256 are the ones Hugging Face publishes.
/// </summary>
public static class QwenWeights
{
    public const string Talker = "qwen-talker-0.6b-customvoice-Q8_0.gguf";
    public const string Codec = "qwen-tokenizer-12hz-Q8_0.gguf";
    private const string Commit = "4164e851b0ab6b13b77d6b5fe4fc2ed5cae40652";

    private static readonly (string Name, long Size, string Sha256)[] Files =
    [
        (Talker, 968_588_544, "4eb38675c736ed6ac72012846ac8d6ef80e5af8bc05726870f0b3a6569588519"),
        (Codec, 291_150_624, "1883beeed99348fc35e23dd225e9082f93f6f8c109330a33d935baa8acdbfd94"),
    ];

    /// <summary>The whole download, in bytes.</summary>
    public static long Size => Files.Sum(f => f.Size);

    public static Uri From(EngineSource source, string file) =>
        new($"https://{(source == EngineSource.HfMirror ? "hf-mirror.com" : "huggingface.co")}/ldov/Qwen3-TTS-GGUF/resolve/{Commit}/{file}");

    /// <summary>Both files are there, whole.</summary>
    public static bool Present(string folder) =>
        Files.All(f => new FileInfo(Path.Combine(folder, f.Name)) is { Exists: true } info && info.Length == f.Size);

    /// <summary>The room still needed: what isn't there yet, counting what an earlier try left.</summary>
    public static long Needed(string folder) => Files.Sum(f =>
    {
        var whole = new FileInfo(Path.Combine(folder, f.Name));
        var part = new FileInfo(Path.Combine(folder, f.Name + ".part"));
        return whole.Exists && whole.Length == f.Size ? 0 : f.Size - (part.Exists ? Math.Min(part.Length, f.Size) : 0);
    });

    /// <summary>
    /// Fetches what isn't there yet into <paramref name="folder"/>, carrying on from what an earlier try left, and checks
    /// each file against its published SHA-256 before it takes its name, so a file with the name is a good one.
    /// <paramref name="progress"/> hears the bytes done of the whole download; <paramref name="checking"/> says the check began.
    /// </summary>
    public static async Task Fetch(HttpClient http, EngineSource source, string folder, IProgress<long> progress, Action? checking, CancellationToken cancel,
                                   TimeSpan? stall = null, int tries = 6, TimeSpan? backOff = null, (string Name, long Size, string Sha256)[]? files = null)
    {
        Directory.CreateDirectory(folder);
        long before = 0;
        foreach (var (name, size, sha256) in files ?? Files)
        {
            var path = Path.Combine(folder, name);
            if (!(new FileInfo(path) is { Exists: true } done && done.Length == size))
            {
                var part = path + ".part";
                var offset = before;
                var heard = new Progress(bytes => progress.Report(offset + bytes));
                await EngineInstall.Fetch(http, From(source, name), part, size, heard, cancel, stall, tries, backOff);
                checking?.Invoke();
                if (!await EngineInstall.Matches(part, sha256, new Progress(_ => { }), cancel))
                {
                    File.Delete(part);
                    throw new InvalidDataException("The download didn't match the published one (damaged on the way?), so it was deleted. Download starts it again.");
                }
                File.Move(part, path, overwrite: true);
            }
            progress.Report(before += size);
        }
    }

    /// <summary>Run on the caller's thread, unlike <see cref="Progress{T}"/>: the tests read it straight after.</summary>
    private sealed class Progress(Action<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard(value);
    }
}
