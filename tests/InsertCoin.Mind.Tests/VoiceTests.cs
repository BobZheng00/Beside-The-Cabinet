using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace InsertCoin.Mind.Tests;

public class VoiceTests
{
    private static List<string> Split(string text, int pieceLength)
    {
        var sentences = new Sentences();
        var done = new List<string>();
        for (var i = 0; i < text.Length; i += pieceLength)
            done.AddRange(sentences.Feed(text.Substring(i, Math.Min(pieceLength, text.Length - i))));
        if (sentences.Finish() is { } last)
            done.Add(last);
        return done;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    public void Streamed_text_is_cut_into_sentences_however_it_arrives(int pieceLength)
    {
        Assert.Equal(["Welcome back!", "You did great, two Hards.", "3.5 hours left…", "Go on!"],
                     Split("Welcome back! You did great, two Hards. 3.5 hours left… Go on!", pieceLength));
        Assert.Equal(["おかえり！", "今日もよく頑張ったね。", "「少し遊んでおいで。」", "待ってるから"],
                     Split("おかえり！今日もよく頑張ったね。「少し遊んでおいで。」待ってるから", pieceLength));
        Assert.Equal(["欢迎回来！！", "第一行", "去玩吧~"], Split("欢迎回来！！\n第一行\n…\n去玩吧~", pieceLength));
    }

    [Theory]
    [InlineData("Welcome back, Bob!", "en")]
    [InlineData("おかえりなさい！", "ja")]
    [InlineData("今日は頑張った", "ja")]
    [InlineData("欢迎回来！", "zh")]
    [InlineData("我今天玩了Hades两个小时", "zh")]
    [InlineData("I named my cat 麻糬 last year", "en")]
    [InlineData("어서 와!", "ko")]
    [InlineData("3:15 ...", null)]
    public void The_language_of_a_line_is_guessed_from_its_letters(string text, string? language) =>
        Assert.Equal(language, SpokenLanguage.Of(text));

    [Fact]
    public void A_subtitled_reply_speaks_only_what_is_in_say()
    {
        var spoken = new List<string>();
        var speech = new Speech("ja", spoken.Add);
        speech.Direction("cheer", null);
        speech.Direction("say", " おかえり！ ");
        speech.Text("Welcome back! ");
        speech.Direction("say", "よく頑張ったね。");
        speech.Text("You did well.");
        speech.Finish();
        Assert.Equal(["おかえり！", "よく頑張ったね。"], spoken);
        Assert.Equal(0, speech.Unheard);
    }

    [Fact]
    public void Sentences_shown_under_one_say_that_covers_only_the_first_go_unheard_and_are_counted()
    {
        var spoken = new List<string>();
        var speech = new Speech("ja", spoken.Add);
        speech.Direction("say", "おかえり、ボブ！");
        speech.Text("晚上好，回来啦！明早十点要去超市。顺便问一句，你希望每天几点前睡着？");
        speech.Finish();

        Assert.Equal(["おかえり、ボブ！"], spoken);
        Assert.Equal(2, speech.Unheard);
    }

    [Fact]
    public void A_reply_in_the_voices_language_is_spoken_sentence_by_sentence_and_one_in_another_stays_silent()
    {
        var spoken = new List<string>();
        var japanese = new Speech("ja", spoken.Add);
        japanese.Text("おかえり！今日も");
        Assert.Equal(["おかえり！"], spoken);              // the first sentence goes before the reply is finished
        japanese.Text("頑張ったね。");
        japanese.Finish();
        Assert.Equal(["おかえり！", "今日も頑張ったね。"], spoken);

        spoken.Clear();
        var english = new Speech("en", spoken.Add);
        english.Text("欢迎回来！你今天很棒。");             // the model forgot [say]: no English to speak
        english.Finish();
        Assert.Empty(spoken);
        Assert.True(Speech.Speaks("en", "Nice one!"));
        Assert.False(Speech.Speaks("ja", "Nice one!"));
    }

    [Fact]
    public async Task Her_reply_tells_the_voice_what_to_say_and_the_bubble_what_to_show()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(DateTimeOffset.Now))
        {
            VoiceLanguage = "ja",
        };
        var model = new FakeModel("[cheer] [say: おかえり", "！] Welcome back! [say: よく頑張", "ったね。] You did well.");
        var spoken = new List<string>();
        var shown = new StringBuilder();
        var speech = new Speech("ja", spoken.Add);

        await conversation.Answer(model, "I'm back", "", words => { shown.Append(words); speech.Text(words); }, speech.Direction);
        speech.Finish();

        Assert.Equal("Welcome back! You did well.", shown.ToString());
        Assert.Equal(["おかえり！", "よく頑張ったね。"], spoken);
        Assert.Contains("your voice speaks Japanese", model.Requests[0][0].Text);
        Assert.DoesNotContain("say", temp.Store.After("shino", 0).Last().Text);      // the log keeps what was shown
    }

    [Fact]
    public async Task A_plan_noted_at_the_start_of_her_reply_leaves_what_she_says_after_it_to_her_voice()
    {
        using var temp = new TempStore();
        var conversation = new Conversation(temp.Store, "shino", new Persona("Shino", "Shino is cheerful."), new FakeClock(DateTimeOffset.Now))
        {
            VoiceLanguage = "ja",
        };
        var model = new FakeModel("[plan: 2026-10-10 10:00 | 去超市 | 1 hour before] [say: わかった、覚えて", "おくね。] 好的，我记下了。");
        var spoken = new List<string>();
        var shown = new StringBuilder();
        var speech = new Speech("ja", spoken.Add);

        await conversation.Answer(model, "我这周六要去超市 早上十点吧", "", words => { shown.Append(words); speech.Text(words); }, speech.Direction);
        speech.Finish();

        Assert.Equal("好的，我记下了。", shown.ToString().Trim());
        Assert.Equal(["わかった、覚えておくね。"], spoken);
    }

    [Fact]
    public void A_japanese_voice_is_told_its_subtitles_translate_it_names_go_in_katakana_and_san_stays_in_the_japanese()
    {
        var ja = Prompt.Build(new Persona("Hana", "x"), "", "", null, [], "", "hi", voiceLanguage: "ja")[0].Text;
        var en = Prompt.Build(new Persona("Hana", "x"), "", "", null, [], "", "hi", voiceLanguage: "en")[0].Text;

        Assert.Contains("a faithful translation in the player's language: nothing added or left out, names included", ja);
        Assert.Contains("In Japanese, write names and other foreign words in katakana", ja);
        Assert.Contains("When you say the player's name in Japanese, add さん", ja);
        Assert.Contains("the subtitle, being a translation, has just their name: [say: サムさん？] Sam?", ja);
        Assert.DoesNotContain("katakana", en);
        Assert.DoesNotContain("さん", en);
    }

    [Fact]
    public void With_a_voice_her_turn_ends_with_a_reminder_of_her_language_and_of_say_since_her_lines_in_the_history_have_none()
    {
        var ja = Prompt.Build(new Persona("Hana", "x"), "", "", null, [], "", "hi", voiceLanguage: "ja")[^1].Text;
        var none = Prompt.Build(new Persona("Hana", "x"), "", "", null, [], "", "hi")[^1].Text;

        Assert.EndsWith("<voice>\nYour voice speaks Japanese, and you write in the language the player last wrote in. When that isn't Japanese, " +
                        "put a [say: ...] in front of every sentence, the last ones too, with the whole sentence in Japanese: only what is in it is heard.\n</voice>", ja);
        Assert.DoesNotContain("<voice>", none);
    }

    [Fact]
    public void Without_a_voice_the_prompt_says_nothing_about_one() =>
        Assert.DoesNotContain("[say", Prompt.Build(new Persona("Shino", "x"), "", "", null, [], "", "hi")[0].Text);

    // ---- WAV -------------------------------------------------------------------------------------------------------

    internal static byte[] WavBytes(short[] samples, int rate = 32000, int dataSize = -1)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataSize >= 0 ? dataSize : samples.Length * 2);
        foreach (var s in samples)
            w.Write(s);
        return stream.ToArray();
    }

    [Fact]
    public void A_wav_arriving_in_odd_pieces_gives_back_its_samples()
    {
        short[] samples = [0, 16384, -16384, 32767, -32768, 100, -100];
        var bytes = WavBytes(samples, rate: 48000, dataSize: 0);        // a stream's header does not know the length yet
        var wav = new Wav();
        var heard = new List<float>();
        for (var i = 0; i < bytes.Length; i += 5)
            heard.AddRange(wav.Feed(bytes.AsSpan(i, Math.Min(5, bytes.Length - i))));
        Assert.Equal(48000, wav.SampleRate);
        Assert.Equal(samples.Select(s => s / 32768f), heard);
    }

    // ---- Bringing a voice in ---------------------------------------------------------------------------------------

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "insert-coin-voice-tests", Guid.NewGuid().ToString("N"));
        public TempFolder() => Directory.CreateDirectory(Path);
        public string File(string name, byte[]? content = null)
        {
            var file = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllBytes(file, content ?? [1, 2, 3]);
            return file;
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static byte[] Silence(double seconds) => WavBytes(new short[(int)(32000 * seconds)]);

    /// <summary>How community GPT-SoVITS models come: weights by version, numbered clips and the training list.</summary>
    private static void CommunityModel(TempFolder download)
    {
        download.File("GPT_weights_v4/Kai_V4-e5.ckpt");
        download.File("GPT_weights_v4/Kai_V4-e10.ckpt");
        download.File("SoVITS_weights_v4/Kai_V4_e10_s164_l32.pth");
        download.File("logs/D_2333.pth");
        download.File("clips/1.wav", Silence(2));
        download.File("clips/5.wav", Silence(7));
        download.File("clips/8.wav", Silence(4.5));
        download.File("Li.list", Encoding.UTF8.GetBytes(
            "output/slicer_opt/1_vocals.wav|slicer_opt|ZH|今天天气很好\noutput/slicer_opt/5_vocals.wav|slicer_opt|ZH|我们明天见。\n"));
        download.File("README.md", Encoding.UTF8.GetBytes("---\nlicense: mit\n---\n示例角色 GPT-SoVITS模型\n声音归属：someone"));
    }

    [Fact]
    public void A_downloaded_model_comes_in_with_its_latest_weights_and_its_best_clip()
    {
        using var download = new TempFolder();
        using var home = new TempFolder();
        CommunityModel(download);
        var folder = System.IO.Path.Combine(home.Path, "kai.voice");

        var voice = VoiceProfile.Import(download.Path, folder);

        Assert.Equal(("Kai_V4-e10.ckpt", "Kai_V4_e10_s164_l32.pth", "v4", "zh"), (voice.Gpt, voice.Sovits, voice.Version, voice.Language));
        Assert.Equal("我们明天见。", voice.ClipFor(null)!.Text);       // 5.wav: 7 s and its words are known
        Assert.Equal(7, voice.ClipFor(null)!.Seconds!.Value, 1);
        Assert.Null(voice.Missing);
        Assert.True(voice.Slow);
        Assert.StartsWith("Licence: mit", voice.About);
        Assert.True(File.Exists(System.IO.Path.Combine(folder, voice.ClipFor(null)!.File)));
        Assert.Equal(voice, VoiceProfile.Load(folder)! with { Clips = voice.Clips });
        Assert.Equal(voice.Clips, VoiceProfile.Load(folder)!.Clips);
    }

    [Fact]
    public void A_zipped_model_imports_too_and_an_rvc_model_is_turned_away_with_a_reason()
    {
        using var download = new TempFolder();
        using var home = new TempFolder();
        CommunityModel(download);
        var zip = System.IO.Path.Combine(home.Path, "Li.zip");
        ZipFile.CreateFromDirectory(download.Path, zip);
        Assert.True(VoiceProfile.LooksLikeVoice(zip));
        Assert.Equal("v4", VoiceProfile.Import(zip, System.IO.Path.Combine(home.Path, "kai.voice")).Version);

        using var rvc = new TempFolder();
        rvc.File("Elowen/model.pth");
        rvc.File("Elowen/added_IVF256_Flat_nprobe_1.index");
        var error = Assert.Throws<InvalidDataException>(() => VoiceProfile.Import(rvc.Path, System.IO.Path.Combine(home.Path, "elowen.voice")));
        Assert.Contains("RVC", error.Message);
    }

    [Fact]
    public void A_clip_alone_is_cloned_and_its_name_can_carry_its_words_and_mood()
    {
        using var download = new TempFolder();
        using var home = new TempFolder();
        var folder = System.IO.Path.Combine(home.Path, "kai.voice");

        var voice = VoiceProfile.Import(download.File("【开心】今天也要加油哦.wav", Silence(4)), folder);
        Assert.Equal(("今天也要加油哦", "happy", "zh"), (voice.Clips[0].Text, voice.Clips[0].Mood, voice.Language));
        Assert.Null(voice.Gpt);
        Assert.True(voice.Slow);                                  // cloned with the base v4 model
        Assert.Null(voice.Missing);

        var unnamed = VoiceProfile.Import(download.File("sample01.wav", Silence(5)), System.IO.Path.Combine(home.Path, "other.voice"));
        Assert.Equal("the words spoken in its clip", unnamed.Missing);
    }

    [Fact]
    public void A_clip_added_to_a_voice_joins_it_and_she_speaks_with_it_if_it_can()
    {
        using var dataset = new TempFolder();
        using var download = new TempFolder();
        using var home = new TempFolder();
        dataset.File("clips/hana_001.wav", Silence(6));
        dataset.File("hana.list", Encoding.UTF8.GetBytes("clips/hana_001.wav|hana|JA|この道は、どこまでも続いています。\n"));
        var folder = System.IO.Path.Combine(home.Path, "hana.voice");
        var before = VoiceProfile.Import(dataset.Path, folder);

        var voice = VoiceProfile.Import(download.File("また明日ね.wav", Silence(4)), folder);
        Assert.Equal((before.Name, "ja"), (voice.Name, voice.Language));
        Assert.Equal(["また明日ね", "この道は、どこまでも続いています。"], voice.Clips.Select(c => c.Text));
        Assert.Equal("また明日ね", voice.ClipFor(null)!.Text);

        voice = VoiceProfile.Import(download.File("ずっと話していたいな、今日は本当に楽しかった.wav", Silence(12)), folder);
        Assert.Equal("また明日ね", voice.ClipFor(null)!.Text);       // too long to speak with: last, for training
        Assert.Equal(3, Directory.GetFiles(System.IO.Path.Combine(folder, "clips")).Length);
    }

    [Fact]
    public void A_folder_of_clips_and_their_list_is_cloned_from_its_best_clips()
    {
        using var dataset = new TempFolder();
        using var home = new TempFolder();
        dataset.File("clips/hana_001.wav", Silence(2.5));
        dataset.File("clips/hana_002.wav", Silence(6.2));
        dataset.File("clips/hana_003.wav", Silence(12));
        dataset.File("hana.list", Encoding.UTF8.GetBytes(
            "clips/hana_001.wav|hana|JA|はい。\nclips/hana_002.wav|hana|JA|この道は、どこまでも続いています。\nclips/hana_003.wav|hana|JA|長い。\n"));

        var voice = VoiceProfile.Import(dataset.Path, System.IO.Path.Combine(home.Path, "hana.voice"));

        Assert.Equal((null, null, null, "ja"), (voice.Gpt, voice.Sovits, voice.Version, voice.Language));
        Assert.Equal("この道は、どこまでも続いています。", voice.ClipFor(null)!.Text);
        Assert.Equal(3, voice.Clips.Count);
        Assert.True(voice.Slow);
        Assert.Null(voice.Missing);
    }

    [Fact]
    public void A_clone_runs_on_v4_unless_v2ProPlus_is_chosen_which_streams()
    {
        var clone = new VoiceProfile { Language = "ja", Clips = [new VoiceClip("clips/a.wav", "はい。", "ja")] };
        Assert.Equal("v4", clone.Runs);
        Assert.True(clone.Slow);
        Assert.EndsWith("s2Gv4.pth", GptSoVits.Base(clone.Runs).Sovits);

        var quick = clone with { CloneWith = "v2ProPlus" };
        Assert.False(quick.Slow);
        Assert.Equal(("GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"), GptSoVits.Base(quick.Runs));
        Assert.Equal("v4", (clone with { CloneWith = "1.7B" }).Runs);  // saved for Qwen3-TTS while it was offered

        var trained = clone with { Gpt = "a.ckpt", Sovits = "b.pth", Version = null };
        Assert.Null(trained.Runs);                                // a model of unknown version: not taken for v4
        Assert.False(trained.Slow);
    }

    [Fact]
    public void A_voice_with_a_trained_model_can_speak_as_a_clone_instead()
    {
        var voice = new VoiceProfile { Gpt = "hana-e15.ckpt", Sovits = "hana_e8_s224.pth", Version = "v2ProPlus", Language = "ja",
                                       Clips = [new VoiceClip("clips/a.wav", "はい。", "ja")] };
        Assert.Equal((true, true, "v2ProPlus"), (voice.Trained, voice.SpeaksTrained, voice.Runs));

        var clone = voice with { CloneWith = "v4" };
        Assert.Equal((true, false, "v4", true), (clone.Trained, clone.SpeaksTrained, clone.Runs, clone.Slow));
        Assert.Equal("v2ProPlus", (clone with { CloneWith = null }).Runs);     // and back
    }

    [Fact]
    public void A_clone_saved_before_a_voice_could_have_both_keeps_its_base_model()
    {
        using var old = new TempFolder();
        old.File("voice.json", Encoding.UTF8.GetBytes("""{ "name": "hana-ja", "language": "ja", "version": "v2ProPlus", "clips": [] }"""));
        using var trained = new TempFolder();
        trained.File("voice.json", Encoding.UTF8.GetBytes("""{ "name": "li", "gpt": "a.ckpt", "sovits": "b.pth", "version": "v2ProPlus" }"""));

        Assert.Equal(("v2ProPlus", null, "v2ProPlus"), (VoiceProfile.Load(old.Path)!.CloneWith, VoiceProfile.Load(old.Path)!.Version, VoiceProfile.Load(old.Path)!.Runs));
        Assert.Equal((null, "v2ProPlus", true), (VoiceProfile.Load(trained.Path)!.CloneWith, VoiceProfile.Load(trained.Path)!.Version, VoiceProfile.Load(trained.Path)!.SpeaksTrained));
    }

    [Fact]
    public void A_dataset_keeps_every_clip_with_its_words_the_best_to_speak_with_first()
    {
        using var dataset = new TempFolder();
        using var home = new TempFolder();
        var list = new StringBuilder();
        double[] lengths = [2, 4, 5.5, 6.5, 9, 11, 14, 3, 7];
        for (var i = 0; i < lengths.Length; i++)
        {
            dataset.File($"wavs/{i:000}.wav", Silence(lengths[i]));
            list.Append($"wavs/{i:000}.wav|hana|JA|せりふ{i}。\n");
        }
        dataset.File("wavs/no-words.wav", Silence(6));
        dataset.File("esd.list", Encoding.UTF8.GetBytes(list.ToString()));

        var voice = VoiceProfile.Import(dataset.Path, System.IO.Path.Combine(home.Path, "hana.voice"));

        Assert.Equal(lengths.Length, voice.Clips.Count);                        // all with words, none without
        Assert.Equal([5.5, 6.5, 7, 4, 9, 3, 2, 11, 14], voice.Clips.Select(c => Math.Round(c.Seconds!.Value, 1)));
        Assert.Equal("せりふ2。", voice.ClipFor(null)!.Text);
    }

    [Fact]
    public void A_clip_dropped_on_a_model_without_clips_joins_it()
    {
        using var download = new TempFolder();
        using var home = new TempFolder();
        download.File("GPT_weights_v2ProPlus/Sample_JA-e15.ckpt");
        download.File("SoVITS_weights_v2ProPlus/Sample_JA_e8_s400.pth");
        var folder = System.IO.Path.Combine(home.Path, "sample.voice");

        var model = VoiceProfile.Import(download.Path, folder);
        Assert.Equal("a 3 to 10 second clip of the voice", model.Missing);
        Assert.False(model.Slow);

        var voice = VoiceProfile.Import(download.File("[happy] こんにちは、いい朝だね.wav", Silence(5)), folder);
        Assert.Equal(("Sample_JA-e15.ckpt", "v2ProPlus", "ja"), (voice.Gpt, voice.Version, voice.Clips.Single().Language));
        Assert.Equal("happy", voice.ClipFor("happy")!.Mood);
        Assert.Equal(voice.Clips[0], voice.ClipFor("sad"));        // no sad clip: her only one
    }

    // ---- The engine --------------------------------------------------------------------------------------------------

    /// <summary>A GPT-SoVITS server that answers from a script and keeps what it was asked.</summary>
    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.PathAndQuery);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return answer(request);
        }
    }

    private static FakeServer Speaking(short[] samples, int rate) => new(request => request.RequestUri!.AbsolutePath == "/tts"
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(WavBytes(samples, rate, 0)) }
        : new HttpResponseMessage(HttpStatusCode.OK));

    private static string[] Blended(string body) =>
        [.. JsonDocument.Parse(body).RootElement.GetProperty("aux_ref_audio_paths").EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public async Task The_engine_loads_a_voice_once_prepares_its_clip_afresh_and_streams_its_speech()
    {
        using var home = new TempFolder();
        home.File("clips/5.wav", Silence(5));
        short[] samples = [.. Enumerable.Range(0, 1000).Select(i => (short)(i * 30))];
        var server = Speaking(samples, 48000);
        var engine = new GptSoVits(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:9880/") });
        var clip = new VoiceClip("clips/5.wav", "谁建造它？", "zh");
        var voice = new VoiceProfile { Gpt = "a.ckpt", Sovits = "b.pth", Language = "ja", Clips = [clip] };   // a model of unknown version

        await engine.Load(voice, home.Path);
        await engine.Load(voice, home.Path);
        var heard = new List<float>();
        await foreach (var audio in engine.Speak("おかえり！", voice, home.Path, clip))
        {
            Assert.Equal(48000, audio.SampleRate);
            heard.AddRange(audio.Samples);
        }
        await foreach (var _ in engine.Speak("ただいま！", voice, home.Path, clip)) { }

        string Escaped(string file) => Uri.EscapeDataString(System.IO.Path.Combine(home.Path, file));
        // new weights: the clip the server prepared for the old ones is prepared again, once
        Assert.Equal([$"/set_gpt_weights?weights_path={Escaped("a.ckpt")}", $"/set_sovits_weights?weights_path={Escaped("b.pth")}",
                      $"/set_refer_audio?refer_audio_path={Escaped(@"clips\5.wav")}", "/tts", "/tts"], server.Asked);
        Assert.Equal(samples.Length, heard.Count);
        var body = server.Bodies[^1];
        Assert.Contains("\"text_lang\":\"ja\"", body);
        Assert.Contains("\"prompt_lang\":\"zh\"", body);
        Assert.Contains("\"sample_steps\":16", body);
        Assert.Contains("\"streaming_mode\":2", body);
        Assert.Empty(Blended(body));                                   // unknown version: it may be v4, which mustn't get extra clips
    }

    [Fact]
    public async Task A_v2_voice_blends_the_tone_of_its_first_few_clips_and_a_v4_voice_speaks_from_one()
    {
        using var home = new TempFolder();
        string[] names = ["a", "gone", "b", "c", "d", "e", "f"];
        foreach (var name in names.Where(n => n != "gone"))
            home.File($"clips/{name}.wav", Silence(5));
        var clips = names.Select(n => new VoiceClip($"clips/{n}.wav", "はい。", "ja")).ToList();
        var clone = new VoiceProfile { Language = "ja", CloneWith = "v2ProPlus", Clips = clips };
        var server = Speaking([1, 2, 3], 32000);
        var engine = new GptSoVits(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:9880/") });

        await foreach (var _ in engine.Speak("やっほー！", clone, home.Path, clips[^1])) { }
        await foreach (var _ in engine.Speak("やっほー！", clone with { CloneWith = "v4" }, home.Path, clips[0])) { }

        // the same first few whichever clip speaks (so the server prepares them once), and only clips that are there
        Assert.Equal(GptSoVits.BlendClips, Blended(server.Bodies[0]).Length);
        Assert.Equal(["a", "b", "c", "d"], Blended(server.Bodies[0]).Select(System.IO.Path.GetFileNameWithoutExtension));
        Assert.Empty(Blended(server.Bodies[1]));
        Assert.True(GptSoVits.Blends("v2Pro"));
        Assert.False(GptSoVits.Blends("v3"));
        Assert.False(GptSoVits.Blends(null));
    }

    [Fact]
    public async Task What_the_engine_says_went_wrong_comes_through()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"message": "tts failed", "Exception": "ref_audio_path not exists"}"""),
        });
        var engine = new GptSoVits(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:9880/") });
        var clip = new VoiceClip("x.wav", "hello", "en");

        var error = await Assert.ThrowsAsync<VoiceException>(async () =>
        {
            await foreach (var _ in engine.Speak("hi", new VoiceProfile { Language = "en", Clips = [clip] }, ".", clip)) { }
        });
        Assert.Equal("tts failed: ref_audio_path not exists", error.Message);
    }

    [Fact]
    public async Task GPT_SoVITS_loads_a_trained_voice_from_its_folder_and_a_clone_with_its_base_model()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = new GptSoVits(new HttpClient(server) { BaseAddress = new Uri("http://127.0.0.1:9880/") });
        var trained = new VoiceProfile { Gpt = "kai-e15.ckpt", Sovits = "kai_e8.pth", Version = "v2ProPlus" };

        await engine.Load(trained, @"C:\voices\kai.voice");
        await engine.Load(new VoiceProfile { CloneWith = "v2ProPlus" }, @"C:\voices\hana.voice");
        await engine.Load(trained with { CloneWith = "v4" }, @"C:\voices\kai.voice");             // a trained voice speaking as a clone

        Assert.Equal(["/set_gpt_weights?weights_path=C%3A%5Cvoices%5Ckai.voice%5Ckai-e15.ckpt", "/set_sovits_weights?weights_path=C%3A%5Cvoices%5Ckai.voice%5Ckai_e8.pth",
                      "/set_gpt_weights?weights_path=GPT_SoVITS%2Fpretrained_models%2Fs1v3.ckpt",
                      "/set_sovits_weights?weights_path=GPT_SoVITS%2Fpretrained_models%2Fv2Pro%2Fs2Gv2ProPlus.pth",
                      "/set_gpt_weights?weights_path=GPT_SoVITS%2Fpretrained_models%2Fs1v3.ckpt",
                      "/set_sovits_weights?weights_path=GPT_SoVITS%2Fpretrained_models%2Fgsv-v4-pretrained%2Fs2Gv4.pth"], server.Asked);
    }
}
