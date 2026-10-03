namespace InsertCoin.Mind.Tests;

public class VoiceTrainingTests
{
    private static VoiceClip Clip(string file, string words, double? seconds) => new(file, words, "ja", null, seconds);

    private static VoiceProfile Voice(params VoiceClip[] clips) => new() { Name = "hana-ja", Language = "ja", Clips = clips };

    [Fact]
    public void A_voice_trains_on_its_clips_with_words_of_a_trainable_length()
    {
        var voice = Voice(Clip("clips/a.wav", "はい。", 6), Clip("clips/b.wav", "", 6), Clip("clips/c.wav", "長すぎる。", 30),
                          Clip("clips/d.wav", "ん。", 0.4), Clip("clips/e.mp3", "長さは分からない。", null));

        Assert.Equal(["clips/a.wav", "clips/e.mp3"], VoiceTraining.Clips(voice).Select(c => c.File));
        Assert.Equal(6 + VoiceProfile.IdealClipSeconds, VoiceTraining.Seconds(voice));     // a clip of unknown length counts as a typical one
    }

    [Fact]
    public void Training_needs_a_minute_of_speech_and_its_language()
    {
        var little = Voice(Clip("clips/a.wav", "はい。", 6), Clip("clips/b.wav", "いいえ。", 5));
        Assert.Equal("at least a minute of clips with their words (it has 11 s in 2 clips)", VoiceTraining.Needs(little));

        var enough = Voice([.. Enumerable.Range(0, 12).Select(i => Clip($"clips/{i}.wav", "せりふ。", 5.5))]);
        Assert.Null(VoiceTraining.Needs(enough));
        Assert.Equal("the language it speaks", VoiceTraining.Needs(enough with { Language = "" }));
    }

    [Fact]
    public void Hana_s_five_minutes_take_about_six()
    {
        Assert.Equal(TimeSpan.FromSeconds(366), VoiceTraining.Estimate(306));          // 5.4 min measured, 2026-10-03
        Assert.Equal(2, VoiceTraining.Pace(TimeSpan.FromSeconds(732), 306));           // a PC that took twice as long…
        Assert.Equal(TimeSpan.FromSeconds(240), VoiceTraining.Estimate(60, pace: 2));   // …is expected to again
        Assert.Equal(4, VoiceTraining.BatchSize(8188, 200));                           // the development laptop's "8 GB", nothing else on it
        Assert.Equal(3, VoiceTraining.BatchSize(8188, 1376));                          // its desktop apps open
        Assert.Equal(2, VoiceTraining.BatchSize(8188, 1848));                          // and the cabinet: 3 crawled
        Assert.Equal(1, VoiceTraining.BatchSize(4096, 3000));
        Assert.Equal(8, VoiceTraining.BatchSize(49152, 1000));
        Assert.True(VoiceTraining.Crowded(8188, 3500));
        Assert.False(VoiceTraining.Crowded(8188, 1848));
    }

    [Fact]
    public void The_time_left_goes_by_the_pace_of_the_step_under_way()
    {
        Assert.Null(VoiceTraining.Left(TimeSpan.FromSeconds(40), 0, 0.14));             // no progress of its own yet
        Assert.Equal(TimeSpan.FromSeconds(234), VoiceTraining.Left(TimeSpan.FromSeconds(26), 0.08, 0.28));    // an epoch of SoVITS: 26 s
        Assert.Null(VoiceTraining.Left(TimeSpan.FromSeconds(300), 0.2, 1));
    }

    [Fact]
    public void The_training_list_names_each_clip_in_full_with_its_language_and_words()
    {
        var voice = Voice(Clip("clips/a.wav", " はい|いいえ。\n", 6), new VoiceClip("clips/b.wav", "Hello.", "en", null, 5));
        var folder = Path.Combine(Path.GetTempPath(), "hana.voice");

        Assert.Equal($"{Path.Combine(folder, "clips", "a.wav")}|voice|ja|はい いいえ。\n{Path.Combine(folder, "clips", "b.wav")}|voice|en|Hello.\n",
                     VoiceTraining.List(voice, folder));
    }

    [Fact]
    public void The_script_s_own_lines_are_told_from_GPT_SoVITS_s()
    {
        Assert.Equal(("stage", "Training her tone (SoVITS)"), VoiceTraining.Read("@stage Training her tone (SoVITS)"));
        Assert.Equal(("done", "hana-e15.ckpt hana_e8_s224.pth"), VoiceTraining.Read("@done hana-e15.ckpt hana_e8_s224.pth"));
        Assert.Equal(("exit", ""), VoiceTraining.Read("@exit"));
        Assert.Null(VoiceTraining.Read("INFO:hana:====> Epoch: 3"));
        Assert.Equal(0.42, VoiceTraining.Fraction("0.420"));
        Assert.Equal(1, VoiceTraining.Fraction("1.3"));
        Assert.Equal(0, VoiceTraining.Fraction("soon"));
    }

    [Fact]
    public void Once_trained_the_voice_speaks_with_its_new_model_and_the_old_one_goes()
    {
        var folder = Directory.CreateTempSubdirectory("insert-coin-training-tests").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "old-e15.ckpt"), [1]);
            File.WriteAllBytes(Path.Combine(folder, "old_e8.pth"), [1]);
            var before = Voice(Clip("clips/a.wav", "はい。", 6)) with { Gpt = "old-e15.ckpt", Sovits = "old_e8.pth", Version = "v4", CloneWith = "v4" };

            var after = VoiceTraining.Trained(before, folder, "hana-e15.ckpt", "hana_e8_s224.pth");

            Assert.Equal(("hana-e15.ckpt", "hana_e8_s224.pth", "v2ProPlus", null, true), (after.Gpt, after.Sovits, after.Version, after.CloneWith, after.SpeaksTrained));
            Assert.False(File.Exists(Path.Combine(folder, "old-e15.ckpt")));
            Assert.False(File.Exists(Path.Combine(folder, "old_e8.pth")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_training_script_travels_inside_the_cabinet()
    {
        var script = VoiceTraining.Script();
        Assert.Contains("def main():", script);
        Assert.Contains("@done", script);
    }
}
