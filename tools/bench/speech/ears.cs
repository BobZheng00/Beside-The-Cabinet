// Her ears, measured (#346): today's SenseVoice-Small and the candidates, through sherpa-onnx as the app would ship
// them. One model a run, so its memory is its own.
//
//     dotnet run tools/bench/speech/ears.cs -- --model <name> --manifest <audio>/manifest.jsonl --out <file.jsonl>
//         [--provider cpu|cuda] [--threads 4] [--hints "Shino,Hana,..."] [--rooms clean,near,far]
//         [--timing <n>]   also hear each of n clips per length 5 times, for the median and spread
//         [--limit <n>]    only the first n clips
//
// Models (folders under --models, E:/Tools/speech-trial/models by default; SenseVoice from --sense-voice):
//   sense-voice      today's ears (Hearing.cs's settings)
//   qwen3-0.6b       Qwen3-ASR 0.6B, int8 (k2-fsa's export)
//   qwen3-1.7b       Qwen3-ASR 1.7B, int8 (a third party's export in k2-fsa's layout)
//   fun-asr-nano     Fun-ASR-Nano, int8
//   nemotron-560     Nemotron 3.5 ASR Streaming 0.6B, 560 ms chunks, int8; streamed as if live (see Stream)
//   nemotron-160     the same at 160 ms chunks
// Each line of --out is one clip: the words heard and how long hearing took after the turn ended. The first line is
// the model: load time and memory. Streaming models also give when words first appeared and when the last landed.

#:property PublishAot=false
#:property NoWarn=CA1416
#:project ../../../src/InsertCoin.Mind/InsertCoin.Mind.csproj

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using InsertCoin.Mind;
using SherpaOnnx;

var options = new Dictionary<string, string>();
for (var i = 0; i + 1 < args.Length; i += 2)
    options[args[i].TrimStart('-')] = args[i + 1];
string Option(string name, string fallback) => options.GetValueOrDefault(name, fallback);

var name = Option("model", "sense-voice");
var models = Option("models", "E:/Tools/speech-trial/models");
var senseVoice = Option("sense-voice", "D:/CS Projects/insert-coin/src/InsertCoin.Game/models/sense-voice");
var provider = Option("provider", "cpu");
var threads = int.Parse(Option("threads", "4"));
var hints = Option("hints", "");
var rooms = Option("rooms", "clean,near,far").Split(',');
var timing = int.Parse(Option("timing", "0"));
var limit = int.Parse(Option("limit", "100000"));

var clips = File.ReadLines(Option("manifest", "E:/Tools/speech-trial/audio/manifest.jsonl"))
    .Select(l => JsonNode.Parse(l)!.AsObject())
    .Where(c => rooms.Contains((string)c["room"]!))
    .Take(limit).ToList();
using var output = new StreamWriter(Option("out", $"E:/Tools/speech-trial/runs/{name}.jsonl"), append: false);
void Write(object o) { output.WriteLine(JsonSerializer.Serialize(o)); output.Flush(); }

var gpuBefore = GpuUsed();
var clock = Stopwatch.StartNew();
var streaming = name.StartsWith("nemotron");
OfflineRecognizer? offline = null;
OnlineRecognizer? online = null;
if (streaming)
    online = new OnlineRecognizer(OnlineConfig());
else
    offline = new OfflineRecognizer(OfflineConfig());
var loaded = clock.Elapsed.TotalMilliseconds;
// a first turn warms it up, as the app's would; it is not counted
var warm = Samples((string)clips[0]["path"]!);
var firstMs = streaming ? Stream(warm, (string)clips[0]["lang"]!).LastMs : Time(() => Hear(warm, (string)clips[0]["lang"]!));
using (var self = Process.GetCurrentProcess())
{
    self.Refresh();
    Write(new
    {
        model = name, provider, threads, hints, load_ms = Math.Round(loaded), first_turn_ms = Math.Round(firstMs),
        working_set_mb = self.WorkingSet64 >> 20, private_mb = self.PrivateMemorySize64 >> 20,
        gpu_used_mb = GpuUsed() - gpuBefore,
    });
}
Console.WriteLine($"{name} ({provider}): loaded in {loaded:0} ms");
if (Option("mode", "asr") == "loop")
{
    await Loop();
    return;
}
if (Option("mode", "asr") == "names")
{
    Names();
    return;
}

foreach (var clip in clips)
{
    var samples = Samples((string)clip["path"]!);
    var lang = (string)clip["lang"]!;
    if (streaming)
    {
        var s = Stream(samples, lang);
        Write(new { clip = (string)clip["clip"]!, room = (string)clip["room"]!, text = s.Text, ms = s.LastMs, first_word_s = s.FirstWord, compute_ms = s.ComputeMs });
    }
    else
    {
        var text = "";
        var ms = Time(() => text = Hear(samples, lang));
        Write(new { clip = (string)clip["clip"]!, room = (string)clip["room"]!, text, ms = Math.Round(ms, 1) });
    }
}

if (timing > 0)
{
    // the same clips for every model: the first n clean ones of each length, across the languages
    var picked = clips.Where(c => (string)c["room"]! == "clean").GroupBy(c => (string)c["len"]!)
        .SelectMany(g => g.GroupBy(c => (string)c["group"]!).SelectMany(l => l.Take((timing + 2) / 3)).Take(timing));
    foreach (var clip in picked)
    {
        var samples = Samples((string)clip["path"]!);
        var times = Enumerable.Range(0, 5).Select(_ => streaming
            ? Stream(samples, (string)clip["lang"]!).LastMs
            : Time(() => Hear(samples, (string)clip["lang"]!))).ToList();
        Write(new { timing = (string)clip["clip"]!, len = (string)clip["len"]!, seconds = (double)clip["seconds"]!, times });
    }
}
Console.WriteLine($"{name}: {clips.Count} clips heard");

// The whole loop, turn by turn (--mode loop): each clip as if said live and followed by quiet, through the app's own
// turn-taking (Silero, Smart Turn, TurnTaking), these ears, her mind (--mind: the mock, never a real provider), her
// sentences and her voice (--gsv, a GPT-SoVITS server already running, with the voice in --voice). Each stage is
// timed from the end of the player's words. The turn's end is on the pretend clock of the sound (Smart Turn's own
// time included), the rest on the wall clock.
async Task Loop()
{
    var turns = Option("turns", "D:/CS Projects/insert-coin/src/InsertCoin.Game/models/turn-taking");
    using var detector = new VoiceDetector(Path.Combine(turns, VoiceDetector.ModelFile));
    using var smart = new SmartTurn(Path.Combine(turns, SmartTurn.ModelFile));
    using var mindHttp = new HttpClient { BaseAddress = new Uri(Option("mind", "http://127.0.0.1:8797/v1/")) };
    var voiceFolder = Option("voice", "");
    using var gsvHttp = new HttpClient { BaseAddress = new Uri(Option("gsv", "http://127.0.0.1:9880/")), Timeout = TimeSpan.FromMinutes(2) };
    var voice = new GptSoVits(gsvHttp);
    var profile = voiceFolder == "" ? null : VoiceProfile.Load(voiceFolder);
    if (profile is not null)
    {
        await voice.Load(profile, voiceFolder);
        await foreach (var _ in voice.Speak("こんにちは。", profile, voiceFolder, profile.ClipFor(null)!)) { }   // warmed up
    }
    var repeat = int.Parse(Option("repeat", "3"));
    var picked = clips.Where(c => (string)c["group"]! != "accented").GroupBy(c => ((string)c["len"]!, (string)c["room"]!))
        .SelectMany(g => g.GroupBy(c => (string)c["group"]!).Select(l => l.First()));
    foreach (var clip in picked)
        for (var n = 0; n < repeat; n++)
        {
            var said = Samples((string)clip["path"]!);
            var endOfWords = said.Length / (double)Hearing.SampleRate - 0.3 - ((string)clip["room"]! == "clean" ? 0 : 0.25);
            var tail = said[^3200..];                     // the room's own quiet after the words, carried on
            var sound = said.Concat(Enumerable.Range(0, 16).SelectMany(_ => tail)).ToArray();
            var (judged, bySmart, smartMs) = TurnEnd(sound, endOfWords);
            var heardMs = 0.0;
            var text = "";
            var wordsAt = judged + (heardMs = Time(() => text = Hear(sound[..Math.Min(sound.Length, (int)((endOfWords + judged) * Hearing.SampleRate))], (string)clip["lang"]!))) / 1000;
            var wall = Stopwatch.StartNew();
            double? mindFirst = null, sentenceAt = null, voiceAt = null;
            string? first = null;
            var sentences = new Sentences();
            using (var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions"))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(new { model = "mock", stream = true, messages = new[] { new { role = "user", content = text } } }), System.Text.Encoding.UTF8, "application/json");
                using var response = await mindHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!line.StartsWith("data: ") || line == "data: [DONE]")
                        continue;
                    var piece = (string?)JsonNode.Parse(line[6..])?["choices"]?[0]?["delta"]?["content"];
                    if (string.IsNullOrEmpty(piece))
                        continue;
                    mindFirst ??= wall.Elapsed.TotalSeconds;
                    if (first is null && sentences.Feed(piece) is [var s, ..])
                    {
                        first = s;
                        sentenceAt = wall.Elapsed.TotalSeconds;
                        break;                            // her voice starts on it while the rest is still written
                    }
                }
            }
            first ??= sentences.Finish();
            sentenceAt ??= wall.Elapsed.TotalSeconds;
            if (profile is not null && first is not null)
                await foreach (var _ in voice.Speak(first, profile, voiceFolder, profile.ClipFor(null)!))
                {
                    voiceAt = wall.Elapsed.TotalSeconds;
                    break;
                }
            Write(new
            {
                loop = (string)clip["clip"]!, room = (string)clip["room"]!, len = (string)clip["len"]!,
                turn_over_s = Math.Round(judged, 3), by_smart_turn = bySmart, smart_turn_ms = smartMs,
                heard_ms = Math.Round(heardMs, 1), words_s = Math.Round(wordsAt, 3),
                mind_first_s = mindFirst is null ? (double?)null : Math.Round(wordsAt + mindFirst.Value, 3),
                first_sentence_s = Math.Round(wordsAt + sentenceAt.Value, 3),
                voice_first_s = voiceAt is null ? (double?)null : Math.Round(wordsAt + voiceAt.Value, 3),
                text,
            });
        }

    // (seconds after the end of the words that the turn was judged over, whether Smart Turn ended it, its times)
    (double, bool, List<double>) TurnEnd(float[] sound, double endOfWords)
    {
        detector.Reset();
        var turn = new TurnTaking(10);
        var times = new List<double>();
        (double At, int Asking, bool Finished)? verdict = null;
        const int piece = 512;
        for (var at = 0; at + piece <= sound.Length; at += piece)
        {
            var now = (at + piece) / (double)Hearing.SampleRate;
            if (verdict is { } v && now >= v.At)
            {
                turn.Verdict(v.Asking, v.Finished);
                verdict = null;
            }
            switch (turn.Hear(detector.Hear(sound.AsSpan(at, piece)), piece / (double)Hearing.SampleRate))
            {
                case TurnTaking.Heard.AskModel:
                    var from = Math.Max(0, at + piece - SmartTurn.Samples);
                    var took = 0.0;
                    var p = 0f;
                    took = Time(() => p = smart.Complete(sound.AsSpan(from, at + piece - from)));
                    times.Add(Math.Round(took, 1));
                    verdict = (now + took / 1000, turn.Asking, p >= SmartTurn.Sure);
                    break;
                case TurnTaking.Heard.Finished:
                    return (now - endOfWords, times.Count > 0 && now - endOfWords < TurnTaking.Quiet - 0.1, times);
            }
        }
        return (double.NaN, false, times);
    }
}

// Catching their name (--mode names): over the clips that start with a name (were they caught?) and over --streams,
// a folder of long recordings of other speech (how often a name was "heard" that nobody said to them). A whole-turn
// model hears the first 2 s of each stretch of speech Silero finds; a streaming model runs all the time, and a name
// counts when an utterance's words start with it. Both say how much processor time they took per second of sound.
void Names()
{
    var wanted = Option("names", "Shino,シノ,しの,诗乃,詩乃,Hana,ハナ,はな,花,Fumiriya,フミリヤ").Split(',');
    bool StartsWithName(string text)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text.Normalize(System.Text.NormalizationForm.FormKC), @"^[\s\p{P}]+", "");
        return wanted.Any(n => t.StartsWith(n, StringComparison.OrdinalIgnoreCase));
    }
    var vadModel = Path.Combine(Option("turns", "D:/CS Projects/insert-coin/src/InsertCoin.Game/models/turn-taking"), VoiceDetector.ModelFile);
    var computeTotal = 0.0;
    var soundTotal = 0.0;
    var watch = new Stopwatch();

    // what was caught in one recording: (seconds into it, the words) for each time a name started an utterance
    List<(double At, string Words)> Catch(float[] samples, string lang)
    {
        var caught = new List<(double, string)>();
        soundTotal += samples.Length / (double)Hearing.SampleRate;
        if (streaming)
        {
            using var stream = online!.CreateStream();
            stream.SetOption("language", lang);
            var said = false;
            for (var at = 0; at < samples.Length; at += 320)
            {
                stream.AcceptWaveform(Hearing.SampleRate, samples[at..Math.Min(at + 320, samples.Length)]);
                watch.Restart();
                while (online.IsReady(stream))
                    online.Decode(stream);
                var text = online.GetResult(stream).Text;
                var endpoint = online.IsEndpoint(stream);
                computeTotal += watch.Elapsed.TotalSeconds;
                if (!said && text.Trim() != "" && StartsWithName(text))
                {
                    caught.Add((Math.Round(at / (double)Hearing.SampleRate, 2), text));
                    said = true;
                }
                if (endpoint)
                {
                    online.Reset(stream);
                    said = false;
                }
            }
            return caught;
        }
        var config = new VadModelConfig();
        config.SileroVad.Model = vadModel;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = 0.25f;
        config.SileroVad.MinSpeechDuration = 0.25f;
        config.SileroVad.MaxSpeechDuration = 30;
        config.SileroVad.WindowSize = 512;
        config.SampleRate = Hearing.SampleRate;
        config.NumThreads = 1;
        using var vad = new VoiceActivityDetector(config, 60);
        void Take()
        {
            while (!vad.IsEmpty())
            {
                var segment = vad.Front();
                vad.Pop();
                var start = segment.Samples[..Math.Min(segment.Samples.Length, 2 * Hearing.SampleRate)];
                watch.Restart();
                var text = Hear(start, lang);
                computeTotal += watch.Elapsed.TotalSeconds;
                if (StartsWithName(text))
                    caught.Add((Math.Round(segment.Start / (double)Hearing.SampleRate, 2), text));
            }
        }
        for (var at = 0; at + 512 <= samples.Length; at += 512)
        {
            watch.Restart();
            vad.AcceptWaveform(samples[at..(at + 512)]);
            computeTotal += watch.Elapsed.TotalSeconds;
            Take();
        }
        vad.Flush();
        Take();
        return caught;
    }

    foreach (var clip in clips.Where(c => ((JsonArray)c["names"]!).Count > 0))
    {
        var first = (string)c0(clip)!;
        var startsWith = ((string)clip["text"]!).StartsWith(first) || first == "Hana" && ((string)clip["text"]!).StartsWith("花")
                         || first == "Shino" && ((string)clip["text"]!).StartsWith("诗乃");
        var lang = (string)clip["lang"]!;
        var samples = Samples((string)clip["path"]!).Concat(new float[Hearing.SampleRate]).ToArray();
        var caught = Catch(samples, lang);
        Write(new { clip = (string)clip["clip"]!, room = (string)clip["room"]!, starts_with_name = startsWith, caught = caught.Count > 0, at = caught.Count > 0 ? caught[0].At : (double?)null, words = caught.Count > 0 ? caught[0].Words : null });
    }
    static string? c0(JsonObject clip) => (string?)((JsonArray)clip["names"]!)[0];
    var clipsCompute = computeTotal / Math.Max(soundTotal, 1);

    computeTotal = soundTotal = 0;
    var streams = Option("streams", "");
    if (streams != "")
        foreach (var file in Directory.GetFiles(streams, "*.wav").Order())
        {
            var caught = Catch(Samples(file), "en");
            foreach (var (at, words) in caught)
                Write(new { stream = Path.GetFileName(file), false_start_at = at, words });
        }
    Write(new { summary = true, clips_compute_per_second = Math.Round(clipsCompute, 4), streams_minutes = Math.Round(soundTotal / 60, 1), streams_compute_per_second = soundTotal > 0 ? Math.Round(computeTotal / soundTotal, 4) : 0 });
}

string Hear(float[] samples, string lang)
{
    using var stream = offline!.CreateStream();
    if (hints != "" && name.StartsWith("qwen3"))
        stream.SetOption("hotwords", hints);
    stream.AcceptWaveform(Hearing.SampleRate, samples);
    offline.Decode(stream);
    // the third party's 1.7B export leaves Qwen's language tag in the text: sherpa-onnx's own exports don't
    return Hearing.Tidy(System.Text.RegularExpressions.Regex.Replace(stream.Result.Text, @"^language \w*<asr_text>", ""));
}

// The clip as if said live: it arrives in 20 ms pieces at the pace it was said, and each piece is heard as soon as the
// recogniser is free. Times are on that pretend clock: when the first words showed, and how long after the end of the
// clip the last of them landed (a quiet tail of one chunk is fed at once to flush it, as the end of a turn would).
(string Text, double LastMs, double? FirstWord, double ComputeMs) Stream(float[] samples, string lang)
{
    using var stream = online!.CreateStream();
    stream.SetOption("language", lang);
    const int piece = 320;
    double now = 0, compute = 0;
    double? first = null;
    var watch = new Stopwatch();
    void Decode(double arrived)
    {
        now = Math.Max(arrived, now);
        while (online.IsReady(stream))
        {
            watch.Restart();
            online.Decode(stream);
            var took = watch.Elapsed.TotalSeconds;
            compute += took;
            now += took;
        }
        if (first is null && online.GetResult(stream).Text.Trim() != "")
            first = now;
    }
    for (var at = 0; at < samples.Length; at += piece)
    {
        stream.AcceptWaveform(Hearing.SampleRate, samples[at..Math.Min(at + piece, samples.Length)]);
        Decode((at + piece) / (double)Hearing.SampleRate);
    }
    var end = samples.Length / (double)Hearing.SampleRate;
    stream.AcceptWaveform(Hearing.SampleRate, new float[Hearing.SampleRate * 6 / 5]);
    stream.InputFinished();
    Decode(end);
    return (Hearing.Tidy(online.GetResult(stream).Text), Math.Round((now - end) * 1000, 1), first is null ? null : Math.Round(first.Value, 2), Math.Round(compute * 1000, 1));
}

OfflineRecognizerConfig OfflineConfig()
{
    var config = new OfflineRecognizerConfig();
    var m = config.ModelConfig;
    m.NumThreads = threads;
    m.Provider = provider;
    m.Debug = 0;
    switch (name)
    {
        case "sense-voice":
            m.SenseVoice.Model = Path.Combine(senseVoice, Hearing.ModelFile);
            m.SenseVoice.Language = "auto";
            m.SenseVoice.UseInverseTextNormalization = 1;
            m.Tokens = Path.Combine(senseVoice, Hearing.TokensFile);
            break;
        case "qwen3-0.6b" or "qwen3-1.7b":
            var q = Path.Combine(models, name == "qwen3-0.6b" ? "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25" : "qwen3-asr-1.7B-int8");
            m.Qwen3Asr.ConvFrontend = Path.Combine(q, "conv_frontend.onnx");
            m.Qwen3Asr.Encoder = Path.Combine(q, "encoder.int8.onnx");
            m.Qwen3Asr.Decoder = Path.Combine(q, "decoder.int8.onnx");
            m.Qwen3Asr.Tokenizer = Path.Combine(q, "tokenizer");
            m.Qwen3Asr.MaxTotalLen = 512;
            m.Qwen3Asr.MaxNewTokens = 200;
            m.Qwen3Asr.Temperature = 1e-6f;
            m.Qwen3Asr.TopP = 1;
            m.Qwen3Asr.Seed = 42;
            break;
        case "fun-asr-nano":
            var f = Path.Combine(models, "sherpa-onnx-funasr-nano-int8-2025-12-30");
            m.FunAsrNano.EncoderAdaptor = Path.Combine(f, "encoder_adaptor.int8.onnx");
            m.FunAsrNano.LLM = Path.Combine(f, "llm.int8.onnx");
            m.FunAsrNano.Embedding = Path.Combine(f, "embedding.int8.onnx");
            m.FunAsrNano.Tokenizer = Path.Combine(f, "Qwen3-0.6B");
            m.FunAsrNano.MaxNewTokens = 200;
            m.FunAsrNano.Temperature = 1e-6f;
            m.FunAsrNano.TopP = 1;
            m.FunAsrNano.Seed = 42;
            m.FunAsrNano.Itn = 1;
            m.FunAsrNano.Hotwords = hints;
            break;
        default:
            throw new ArgumentException($"no model {name}");
    }
    config.ModelConfig = m;              // sherpa-onnx's configs are structs: m was a copy
    return config;
}

OnlineRecognizerConfig OnlineConfig()
{
    var folder = Path.Combine(models, $"sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-{name["nemotron-".Length..]}ms-int8-2026-06-11");
    var config = new OnlineRecognizerConfig();
    var m = config.ModelConfig;
    m.Transducer.Encoder = Path.Combine(folder, "encoder.int8.onnx");
    m.Transducer.Decoder = Path.Combine(folder, "decoder.int8.onnx");
    m.Transducer.Joiner = Path.Combine(folder, "joiner.int8.onnx");
    m.Tokens = Path.Combine(folder, "tokens.txt");
    m.NumThreads = threads;
    m.Provider = provider;
    config.DecodingMethod = "greedy_search";
    config.EnableEndpoint = 1;                      // an utterance ends at 0.6 s of quiet after words (names mode)
    config.Rule1MinTrailingSilence = 2.4f;
    config.Rule2MinTrailingSilence = 0.6f;
    config.Rule3MinUtteranceLength = 20;
    config.ModelConfig = m;
    return config;
}

static float[] Samples(string path)
{
    var bytes = File.ReadAllBytes(path);
    var data = 12;
    while (data + 8 <= bytes.Length && System.Text.Encoding.ASCII.GetString(bytes, data, 4) != "data")
        data += 8 + BitConverter.ToInt32(bytes, data + 4);
    var start = data + 8;
    var count = BitConverter.ToInt32(bytes, data + 4) / 2;
    var samples = new float[count];
    for (var i = 0; i < count; i++)
        samples[i] = BitConverter.ToInt16(bytes, start + 2 * i) / 32768f;
    return samples;
}

static double Time(Action act)
{
    var watch = Stopwatch.StartNew();
    act();
    return watch.Elapsed.TotalMilliseconds;
}

static long GpuUsed()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits") { RedirectStandardOutput = true })!;
        return long.Parse(p.StandardOutput.ReadToEnd().Trim());
    }
    catch { return -1; }
}
