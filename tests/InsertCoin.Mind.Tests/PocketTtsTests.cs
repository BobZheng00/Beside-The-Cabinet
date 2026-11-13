namespace InsertCoin.Mind.Tests;

/// <summary>
/// Pocket TTS: the voice each character is given and what happens to older ones, the text as Pocket TTS prepares it, and its
/// word pieces. The real model runs when tools/models/fetch.py has fetched it (into this checkout or the one it is a worktree
/// of); otherwise those pass quietly.
/// </summary>
public class PocketTtsTests
{
    private static readonly string? Models = Find();

    private static string? Find()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (Path.Combine(folder.FullName, "src", "InsertCoin.Game", "models", "pocket-tts-en") is var models && PocketTts.Missing(models) is null)
                return models;
        return null;
    }

    // ---- Which voice ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("shino", "Eve")]
    [InlineData("fumiriya", "George")]
    public void In_English_the_built_in_characters_speak_with_Pocket_TTS_from_a_recording_named_for_their_speaker(string character, string name)
    {
        var voice = VoiceProfile.Suggested(character, "en")!;

        Assert.Equal(VoiceProfile.Pocket, voice.Engine);
        Assert.Equal(name, voice.Name);
        Assert.Equal("en", voice.Language);
        Assert.Equal(name.ToLowerInvariant() + ".wav", Assert.Single(voice.Clips).File);
        Assert.Null(voice.Missing);                             // copied from the sound alone: no words needed
        Assert.Contains("VCTK", voice.About);
    }

    [Theory]
    [InlineData("shino", "zh", VoiceProfile.Qwen)]
    [InlineData("fumiriya", "ja", VoiceProfile.Qwen)]
    [InlineData("hana", "en", null)]
    [InlineData("shino", "ko", null)]
    public void Each_language_has_its_own_engine_and_only_the_built_in_characters_have_a_suggested_voice(string character, string language, string? engine)
    {
        Assert.Equal(engine, VoiceProfile.Suggested(character, language)?.Engine);
        Assert.Null(PocketTts.Suggested(character, "zh"));
    }

    [Fact]
    public void A_Pocket_voice_without_its_recording_still_needs_one()
    {
        Assert.Equal("a recording of the voice", (VoiceProfile.Suggested("shino", "en")! with { Clips = [] }).Missing);
    }

    private static readonly VoiceProfile Kokoro = new()
    {
        Name = "Shino", Language = "en", Clips = [new VoiceClip("clips/01.wav", "Hey, you're back!", "en")],
        About = "Licence: apache-2.0\nShino's own voice, built into Beside the Cabinet: made with Kokoro-82M (hexgrad, Apache-2.0), its voice af_bella.",
    };

    private static readonly VoiceProfile Theirs = new() { Name = "Mine", Language = "zh", Clips = [new VoiceClip("clips/a.wav", "你好。", "zh")] };

    [Fact]
    public void A_new_profile_gives_each_character_the_voice_suggested_in_the_language_they_start_in()
    {
        Assert.Equal("Eve", VoiceProfile.ToGive("shino", null, givenBefore: false, setting: null, display: "en-GB")!.Name);
        Assert.Equal("George", VoiceProfile.ToGive("fumiriya", null, givenBefore: false, setting: "en", display: "zh-CN")!.Name);
        Assert.Equal("Serena", VoiceProfile.ToGive("shino", null, givenBefore: false, setting: null, display: "zh-CN")!.Name);
    }

    [Fact]
    public void An_old_voice_made_with_Kokoro_gives_way_to_the_suggested_one_in_its_own_language_wherever_the_PC_is()
    {
        Assert.True(Kokoro.MadeWithKokoro);
        var given = VoiceProfile.ToGive("shino", Kokoro, givenBefore: true, setting: "zh", display: "zh-CN")!;

        Assert.Equal(VoiceProfile.Pocket, given.Engine);
        Assert.Equal("Eve", given.Name);
    }

    [Fact]
    public void A_voice_the_player_made_added_or_trained_stays_and_one_they_removed_stays_removed()
    {
        Assert.Null(VoiceProfile.ToGive("shino", Theirs, givenBefore: true, setting: null, display: "en-US"));
        Assert.Null(VoiceProfile.ToGive("shino", Theirs, givenBefore: false, setting: null, display: "en-US"));
        var trained = Kokoro with { Gpt = "shino-e10.ckpt", Sovits = "shino_e8_s200.pth" };
        Assert.False(trained.MadeWithKokoro);
        Assert.Null(VoiceProfile.ToGive("shino", trained, givenBefore: true, setting: null, display: "en-US"));
        Assert.Null(VoiceProfile.ToGive("shino", null, givenBefore: true, setting: null, display: "en-US"));
        Assert.Null(VoiceProfile.ToGive("hana", Kokoro, givenBefore: true, setting: null, display: "en-US"));      // nothing to give a character of their own
    }

    [Fact]
    public void An_old_Kokoro_voice_the_player_added_a_clip_to_is_theirs_and_stays()
    {
        var added = Kokoro with { Clips = [.. Kokoro.Clips, new VoiceClip(@"clips\my take.wav", "Hello.", "en")] };

        Assert.False(added.MadeWithKokoro);
        Assert.Null(VoiceProfile.ToGive("shino", added, givenBefore: true, setting: null, display: "en-US"));
        Assert.True((Kokoro with { Clips = [new VoiceClip(@"clips\[sad] 07.wav", "Oh no.", "en")] }).MadeWithKokoro);
    }

    /// <summary>A voice folder holding this voice.json and these files (empty: only their names count).</summary>
    private static string Folder(VoiceProfile? voice, params string[] files)
    {
        var folder = Directory.CreateTempSubdirectory("voice-").FullName;
        voice?.Save(folder);
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.WriteAllText(Path.Combine(folder, file), "");
        }
        return folder;
    }

    [Theory]
    [InlineData(true, "Eve as given")]
    [InlineData(true, "Serena as given")]
    [InlineData(true, "the old Kokoro voice as it came")]
    [InlineData(false, "Eve with a file of the player's beside")]
    [InlineData(false, "the old Kokoro voice with a clip added")]
    [InlineData(false, "the player's own voice, its clips named as Kokoro's were")]
    [InlineData(false, "clips with no voice.json")]
    public void A_voice_folder_is_deleted_only_when_it_holds_a_voice_the_app_gave_and_nothing_else(bool safe, string what)
    {
        var folder = what switch
        {
            "Eve as given" => Folder(PocketTts.Suggested("shino", "en"), "README.md", "eve.wav"),
            "Serena as given" => Folder(QwenTts.Suggested("shino", "zh")),
            "the old Kokoro voice as it came" => Folder(Kokoro, "README.md", "clips/01.wav", "clips/[sad] 07.wav"),
            "Eve with a file of the player's beside" => Folder(PocketTts.Suggested("shino", "en"), "eve.wav", "clips/01.wav"),
            "the old Kokoro voice with a clip added" => Folder(Kokoro, "clips/01.wav", "clips/my take.wav"),
            "the player's own voice, its clips named as Kokoro's were" =>
                Folder(Theirs with { Clips = [new VoiceClip("clips/01.wav", "你好。", "zh"), new VoiceClip("clips/02.wav", "早上好。", "zh")] }, "clips/01.wav", "clips/02.wav"),
            _ => Folder(null, "clips/01.wav"),
        };
        try
        {
            Assert.True(safe == VoiceProfile.SafeToReplace(folder), what);
            Assert.True(VoiceProfile.SafeToReplace(Path.Combine(folder, "nowhere")));     // nothing there to lose
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- The text ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("you did great today, so go and have some fun", "You did great today, so go and have some fun.", 3)]
    [InlineData("  Good   morning.\nDid you sleep?  ", "Good morning. Did you sleep?", 3)]
    [InlineData("I’ll be right here", "I'll be right here.", 5)]
    [InlineData("Wait for it:", "Wait for it.", 5)]
    [InlineData("\"Hi?\", she said", "\"Hi?\", she said.", 5)]
    [InlineData("Well…", "Well…", 5)]
    [InlineData("\"Go on\"", "\"Go on\".", 5)]
    public void A_line_is_prepared_as_Pocket_TTS_prepares_one(string line, string prepared, int framesAfterEnd)
    {
        Assert.Equal((prepared, framesAfterEnd), PocketTts.Prepared(line));
    }

    [Fact]
    public void A_line_of_nothing_but_spaces_is_nothing_to_say()
    {
        Assert.Equal("", PocketTts.Prepared("  \n ").Text);
    }

    /// <summary>A few pieces, after the 4 special ones and the 256 bytes, as the export's vocabulary has them.</summary>
    private static Dictionary<string, (int Id, float Score)> Few()
    {
        var pieces = new Dictionary<string, (int, float)> { ["<unk>"] = (0, 0), ["<s>"] = (1, 0), ["</s>"] = (2, 0), ["<pad>"] = (3, 0) };
        for (var b = 0; b < 256; b++)
            pieces[$"<0x{b:X2}>"] = (4 + b, 0);
        string[] words = ["▁", "▁hi", "▁h", "i", "▁the", "re", "▁there", "e", "r", "h", "t", "!"];
        float[] scores = [-3, -4, -6, -5, -4, -6, -7, -5, -5, -6, -6, -4];
        for (var i = 0; i < words.Length; i++)
            pieces[words[i]] = (260 + i, scores[i]);
        return pieces;
    }

    [Fact]
    public void Words_split_into_their_likeliest_pieces_after_a_space_mark()
    {
        // "▁there" alone (-7) beats "▁the" + "re" (-10)
        Assert.Equal([261, 266, 271], PocketTts.Tokens("hi there!", Few()));
    }

    [Fact]
    public void A_character_without_a_piece_becomes_the_pieces_of_its_bytes()
    {
        Assert.Equal([261, 4 + 0xC3, 4 + 0xA9, 4 + 0xF0, 4 + 0x9F, 4 + 0x98, 4 + 0x80], PocketTts.Tokens("hié😀", Few()));
    }

    [Theory]
    [InlineData("Good morning. Did you sleep all right?", new long[] { 1892, 1220, 273, 263, 1718, 270, 1593, 317, 345, 292 })]
    [InlineData("You did great today, so go and have some fun. I'll be right here.",
                new long[] { 343, 432, 481, 630, 262, 294, 350, 269, 290, 301, 751, 263, 268, 264, 335, 279, 345, 375, 263 })]
    [InlineData("It's 3:45, café time — naïve 😀 idea!",
                new long[] { 333, 264, 261, 260, 450, 3244, 487, 437, 262, 331, 2250, 745, 361, 260, 3133, 913, 199, 179, 314, 260, 244, 163, 156, 132, 700, 682 })]
    [InlineData("Hmm…  two  spaces and\ttab.", new long[] { 993, 283, 283, 230, 132, 170, 260, 434, 260, 947, 261, 269, 13, 274, 1222, 263 })]
    [InlineData("Hana's 12,000th try: OK?", new long[] { 1033, 559, 264, 261, 260, 341, 365, 262, 316, 316, 316, 465, 616, 3244, 1183, 292 })]
    public void The_real_model_splits_text_as_Pocket_TTS_does(string text, long[] ids)
    {
        if (Models is null)
            return;
        Assert.Equal(ids, PocketTts.Tokens(text, PocketTts.Pieces(Models)));     // ids from Pocket TTS's own tokenizer
    }

    // ---- The real model ------------------------------------------------------------------------------------------------

    /// <summary>About 30 s of speech-like sound at 24 kHz, ending on a short pause, for a voice to copy (no one's real voice).</summary>
    private static string Recording(string folder)
    {
        var rate = PocketTts.Rate;
        var samples = Enumerable.Range(0, rate * 6).Select(i =>
        {
            var t = (double)i / rate;
            var syllable = Math.Max(0, Math.Sin(Math.PI * t * 4));
            return (float)(0.3 * syllable * (Math.Sin(2 * Math.PI * 140 * t) + 0.5 * Math.Sin(2 * Math.PI * 280 * t)));
        }).Concat(new float[rate / 10]).ToArray();
        var file = Path.Combine(folder, "eve.wav");
        using var writer = new BinaryWriter(File.Create(file));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var s in samples)
            writer.Write((short)(s * 32767));
        return file;
    }

    [Fact]
    public async Task The_real_model_says_a_line_as_it_is_made_and_a_short_sound_stops_at_its_limit()
    {
        if (Models is null)
            return;
        var folder = Directory.CreateTempSubdirectory("pocket-").FullName;
        try
        {
            Recording(folder);
            using var engine = new PocketTts(Models);
            var voice = PocketTts.Suggested("shino", "en")!;

            var pieces = new List<VoiceAudio>();
            await foreach (var piece in engine.Speak("Good morning. Did you sleep all right?", voice, folder))
                pieces.Add(piece);
            var samples = pieces.SelectMany(p => p.Samples).ToArray();

            Assert.All(pieces, p => Assert.Equal(PocketTts.Rate, p.SampleRate));
            Assert.True(pieces.Count > 5);                                      // a frame at a time, not the whole line at the end
            Assert.InRange(samples.Length / (double)PocketTts.Rate, 1, 6);
            Assert.Contains(samples, s => Math.Abs(s) > 0.05);

            var short_ = 0;
            await foreach (var piece in engine.Speak("Hmm, let me think about that for a moment, all right?", voice, folder, limit: 20))
                short_ += piece.Samples.Length;
            Assert.InRange(short_, 1, 20 * PocketTts.Rate / 12.5);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Missing_files_are_named_before_anything_loads()
    {
        Assert.Equal("text_conditioner.onnx is missing", PocketTts.Missing(Path.GetTempPath()));
    }
}
