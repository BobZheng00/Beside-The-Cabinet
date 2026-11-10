using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InsertCoin.Mind.Tests;

public class QwenTtsTests : IDisposable
{
    private const int Rate = 24000;
    private readonly string _dir = Directory.CreateTempSubdirectory("qwen-tts-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly VoiceProfile Serena = QwenTts.Suggested("shino", "zh")!;

    /// <summary>Stands in for the engine's server: answers every request with the same thing.</summary>
    private sealed class Server(Func<HttpContent> body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public JsonDocument? Asked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Path = request.RequestUri!.AbsolutePath;
            Asked = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancel));
            return new HttpResponseMessage(status) { Content = body() };
        }
    }

    /// <summary>Speech as the server streams it, counting how much has gone out so a test can see what was made by when.</summary>
    private sealed class Stream16(int samples) : MemoryStream(new byte[samples * 2])
    {
        public long Served => Position / 2;
    }

    /// <summary>A body that never sends a thing: a server that has hung.</summary>
    private sealed class Hung : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        }
    }

    private static QwenTts Engine(HttpMessageHandler server, TimeSpan? stall = null, Func<TimeSpan>? clock = null) =>
        new(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:1/") }, stall, clock);

    private static async Task<int> Spoken(IAsyncEnumerable<VoiceAudio> speech)
    {
        var count = 0;
        await foreach (var piece in speech)
        {
            Assert.Equal(Rate, piece.SampleRate);
            count += piece.Samples.Length;
        }
        return count;
    }

    [Fact]
    public async Task It_asks_the_engine_for_this_speaker_in_this_language_as_streamed_speech()
    {
        var server = new Server(() => new ByteArrayContent(new byte[Rate]));

        await Spoken(Engine(server).Speak("今天你做得很好。", Serena));

        Assert.Equal("/v1/audio/speech", server.Path);
        var asked = server.Asked!.RootElement;
        Assert.Equal("今天你做得很好。", asked.GetProperty("input").GetString());
        Assert.Equal("serena", asked.GetProperty("voice").GetString());
        Assert.Equal("Chinese", asked.GetProperty("language").GetString());
        Assert.Equal("pcm", asked.GetProperty("response_format").GetString());
        Assert.False(asked.TryGetProperty("max_new_tokens", out _));        // the server refuses a null, so no limit means no field
    }

    [Fact]
    public async Task A_short_sound_asks_the_engine_to_stop_at_its_limit()
    {
        var server = new Server(() => new ByteArrayContent(new byte[Rate]));

        await Spoken(Engine(server).Speak("うーん…", Serena, limit: 32));

        Assert.Equal(32, server.Asked!.RootElement.GetProperty("max_new_tokens").GetInt32());
    }

    [Fact]
    public async Task Speech_comes_back_as_samples_even_when_a_sample_is_cut_between_reads()
    {
        var stream = new MemoryStream([0x00, 0x40, 0x00, 0xC0, 0xFF, 0x7F]);    // 0.5, -0.5, nearly 1
        var server = new Server(() => new StreamContent(new OneByteAtATime(stream)));
        var engine = Engine(server);
        engine.Held.Learn(1, 1, 1);                     // at speech's own pace nothing is held back

        var samples = new List<float>();
        await foreach (var piece in engine.Speak("嗯。", Serena))
            samples.AddRange(piece.Samples);

        Assert.Equal([0.5f, -0.5f, 32767f / 32768f], samples);
    }

    private sealed class OneByteAtATime(Stream inner) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default) =>
            inner.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancel);
    }

    [Fact]
    public void A_five_second_line_at_one_point_four_seconds_of_work_a_second_waits_about_two_seconds()
    {
        var held = new HoldBack();
        held.Learn(work: 7, speech: 5, characters: 20);

        Assert.Equal(1.4, held.Pace, 3);
        Assert.Equal(2.0, held.Seconds(20) * held.Pace, 3);       // the audio to gather, at the pace it is made
    }

    [Fact]
    public void A_PC_that_keeps_up_holds_nothing_back()
    {
        var held = new HoldBack();
        held.Learn(work: 4, speech: 5, characters: 20);

        Assert.Equal(0, held.Seconds(20));
    }

    [Fact]
    public void The_pace_follows_this_PC_as_it_gets_busier()
    {
        var held = new HoldBack();
        held.Learn(5, 5, 20);
        var quiet = held.Pace;

        held.Learn(10, 5, 20);

        Assert.True(held.Pace > quiet);
    }

    [Fact]
    public async Task Nothing_plays_until_enough_of_the_line_is_made_to_play_without_a_gap()
    {
        var stream = new Stream16(5 * Rate);
        var server = new Server(() => new StreamContent(stream));
        var engine = Engine(server);
        engine.Held.Learn(work: 7, speech: 5, characters: 20);
        var needs = engine.Held.Seconds(20);

        long? servedAtFirstSound = null;
        var total = 0;
        await foreach (var piece in engine.Speak(new string('字', 20), Serena))
        {
            servedAtFirstSound ??= stream.Served;
            total += piece.Samples.Length;
        }

        Assert.True(servedAtFirstSound >= needs * Rate, $"only {servedAtFirstSound} samples were made before the first was played");
        Assert.Equal(5 * Rate, total);
    }

    [Fact]
    public async Task A_line_shorter_than_guessed_still_plays_when_it_ends()
    {
        var server = new Server(() => new ByteArrayContent(new byte[Rate / 5]));        // a tenth of a second, far less than the hold
        var engine = Engine(server);
        engine.Held.Learn(work: 7, speech: 5, characters: 20);

        Assert.Equal(Rate / 10, await Spoken(engine.Speak(new string('字', 20), Serena)));
    }

    [Fact]
    public async Task A_whole_line_teaches_it_this_PCs_pace()
    {
        var times = new Queue<TimeSpan>([TimeSpan.Zero, TimeSpan.FromSeconds(7)]);
        var server = new Server(() => new ByteArrayContent(new byte[5 * Rate * 2]));
        var engine = Engine(server, clock: () => times.Dequeue());

        await Spoken(engine.Speak(new string('字', 20), Serena));

        Assert.Equal(1.4, engine.Held.Pace, 3);
        Assert.Equal(0.25, engine.Held.SecondsPerCharacter, 3);
    }

    [Fact]
    public async Task A_line_the_server_refuses_says_why()
    {
        var server = new Server(() => new StringContent("{\"message\":\"unknown voice\"}", Encoding.UTF8, "application/json"), HttpStatusCode.BadRequest);

        var failed = await Assert.ThrowsAsync<VoiceException>(() => Spoken(Engine(server).Speak("嗯。", Serena)));

        Assert.Contains("unknown voice", failed.Message);
    }

    [Fact]
    public async Task A_server_that_stops_sending_is_a_failure_and_not_a_wait_for_ever()
    {
        var server = new Server(() => new StreamContent(new Hung()));

        await Assert.ThrowsAsync<VoiceException>(() => Spoken(Engine(server, stall: TimeSpan.FromMilliseconds(30)).Speak("嗯。", Serena)));
    }

    [Fact]
    public async Task Being_cut_off_stops_the_line_without_calling_it_a_failure()
    {
        var server = new Server(() => new StreamContent(new Hung()));
        using var cut = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Spoken(Engine(server, stall: TimeSpan.FromMinutes(1)).Speak("嗯。", Serena, cut.Token)));
    }

    [Theory]
    [InlineData("shino", "zh", "serena", "Serena")]
    [InlineData("shino", "ja", "ono_anna", "Ono Anna")]
    [InlineData("fumiriya", "zh", "aiden", "Aiden")]
    [InlineData("fumiriya", "ja", "aiden", "Aiden")]
    public void The_built_in_characters_have_the_voices_chosen_for_them(string character, string language, string speaker, string name)
    {
        var voice = QwenTts.Suggested(character, language)!;

        Assert.Equal(speaker, voice.Speaker);
        Assert.Equal(name, voice.Name);
        Assert.Equal(language, voice.Language);
        Assert.Equal(VoiceProfile.Qwen, voice.Engine);
        Assert.Null(voice.Missing);                     // no clips to ask for
    }

    [Theory]
    [InlineData("shino", "en")]
    [InlineData("someone-else", "zh")]
    public void Nobody_else_and_no_other_language_has_one(string character, string language) =>
        Assert.Null(QwenTts.Suggested(character, language));

    [Theory]
    [InlineData("ja", "zh", "zh-CN", "ja")]             // a voice they have already: its language, whatever else says
    [InlineData("en", "zh", "zh-CN", "en")]
    [InlineData(null, "zh", "en-US", "zh")]             // the Language setting
    [InlineData(null, "en", "ja-JP", "en")]
    [InlineData(null, null, "zh-CN", "zh")]             // Auto: the PC's display language
    [InlineData(null, null, "zh", "zh")]
    [InlineData(null, null, "ja-JP", "ja")]
    [InlineData(null, null, "fr-FR", "en")]             // otherwise English
    [InlineData("", null, "ja", "ja")]                  // a voice with no language yet doesn't count
    public void Which_language_a_voice_speaks(string? existing, string? setting, string display, string expected) =>
        Assert.Equal(expected, SpokenLanguage.ForVoice(existing, setting, display));

    [Fact]
    public void A_speakers_voice_comes_back_as_it_was_saved()
    {
        Serena.Save(_dir);

        var loaded = VoiceProfile.Load(_dir)!;

        Assert.Equal(VoiceProfile.Qwen, loaded.Engine);
        Assert.Equal("serena", loaded.Speaker);
        Assert.Equal("zh", loaded.Language);
    }

    [Fact]
    public void A_clip_added_to_a_speaker_starts_a_voice_of_its_own()
    {
        Serena.Save(_dir);
        var clip = Path.Combine(Directory.CreateTempSubdirectory("qwen-clip-").FullName, "今天也要加油哦.wav");
        File.WriteAllBytes(clip, new byte[16]);

        var voice = VoiceProfile.Import(clip, _dir);

        Assert.Equal("gpt-sovits", voice.Engine);
        Assert.Null(voice.Speaker);
        Assert.Single(voice.Clips);
        Directory.Delete(Path.GetDirectoryName(clip)!, recursive: true);
    }

    // ---- The download ----------------------------------------------------------------------------------------------

    private static (string, long, string)[] Files(params (string Name, byte[] Bytes)[] files) =>
        [.. files.Select(f => (f.Name, (long)f.Bytes.Length, Convert.ToHexString(SHA256.HashData(f.Bytes))))];

    /// <summary>A host with these files, honouring a resume.</summary>
    private sealed class Host(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<(string File, long? From)> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var name = request.RequestUri!.Segments[^1];
            var from = request.Headers.Range?.Ranges.Single().From;
            Asked.Add((name, from));
            var body = files[name][(int)(from ?? 0)..];
            return Task.FromResult(new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private sealed class Listener : IProgress<long>
    {
        public long Last { get; private set; }
        public void Report(long value) => Last = value;
    }

    [Fact]
    public async Task Both_files_are_fetched_checked_and_named_and_nothing_else_stays_behind()
    {
        var (a, b) = ("aaaaaa"u8.ToArray(), "bbbb"u8.ToArray());
        var host = new Host(new() { ["talker"] = a, ["codec"] = b });
        var heard = new Listener();
        var checks = 0;

        await QwenWeights.Fetch(new HttpClient(host), EngineSource.HuggingFace, _dir, heard, () => checks++, CancellationToken.None,
                                files: Files(("talker", a), ("codec", b)));

        Assert.Equal(a, File.ReadAllBytes(Path.Combine(_dir, "talker")));
        Assert.Equal(b, File.ReadAllBytes(Path.Combine(_dir, "codec")));
        Assert.Equal(10, heard.Last);
        Assert.Equal(2, checks);
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task A_download_cut_short_carries_on_from_where_it_got_to()
    {
        var a = "abcdefgh"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_dir, "talker.part"), "abc"u8.ToArray());
        var host = new Host(new() { ["talker"] = a });

        await QwenWeights.Fetch(new HttpClient(host), EngineSource.HuggingFace, _dir, new Listener(), null, CancellationToken.None, files: Files(("talker", a)));

        Assert.Equal(a, File.ReadAllBytes(Path.Combine(_dir, "talker")));
        Assert.Equal(3, host.Asked.Single().From);
    }

    [Fact]
    public async Task A_file_already_there_is_not_fetched_again()
    {
        var a = "abcd"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_dir, "talker"), a);
        var host = new Host(new() { ["talker"] = a });

        await QwenWeights.Fetch(new HttpClient(host), EngineSource.HuggingFace, _dir, new Listener(), null, CancellationToken.None, files: Files(("talker", a)));

        Assert.Empty(host.Asked);
    }

    [Fact]
    public async Task A_damaged_download_is_deleted_and_never_takes_the_files_name()
    {
        var host = new Host(new() { ["talker"] = "good"u8.ToArray() });
        (string, long, string)[] wanted = [("talker", 4, new string('0', 64))];

        var failed = await Assert.ThrowsAsync<InvalidDataException>(() =>
            QwenWeights.Fetch(new HttpClient(host), EngineSource.HuggingFace, _dir, new Listener(), null, CancellationToken.None, files: wanted));

        Assert.Contains("didn't match", failed.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData(EngineSource.HuggingFace, "huggingface.co")]
    [InlineData(EngineSource.HfMirror, "hf-mirror.com")]
    public void The_download_comes_from_the_host_asked_for_at_the_commit_that_was_tried(EngineSource source, string host)
    {
        var from = QwenWeights.From(source, QwenWeights.Talker);

        Assert.Equal(host, from.Host);
        Assert.Contains("/ldov/Qwen3-TTS-GGUF/resolve/4164e851b0ab6b13b77d6b5fe4fc2ed5cae40652/", from.AbsolutePath);
    }

    [Fact]
    public void Its_size_is_the_one_the_player_is_told() => Assert.Equal(1.26, QwenWeights.Size / 1e9, 2);

    [Fact]
    public void It_needs_all_of_it_when_nothing_is_there_and_less_once_some_is()
    {
        Assert.False(QwenWeights.Present(_dir));
        Assert.Equal(QwenWeights.Size, QwenWeights.Needed(_dir));

        File.WriteAllBytes(Path.Combine(_dir, QwenWeights.Codec + ".part"), new byte[1000]);

        Assert.Equal(QwenWeights.Size - 1000, QwenWeights.Needed(_dir));
    }
}
