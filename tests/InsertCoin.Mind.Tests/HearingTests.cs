namespace InsertCoin.Mind.Tests;

public class HearingTests
{
    [Fact]
    public void Japanese_comes_out_unspaced_and_English_keeps_its_spaces()
    {
        Assert.Equal("今日はあと何分遊べるの？", Hearing.Tidy("今日は あと 何分 遊べる の？"));
        Assert.Equal("明日の面接、ちょっと緊張してる。", Hearing.Tidy("明日 の 面接、ちょっと 緊張 し てる。"));
        Assert.Equal("How much time do I have left today?", Hearing.Tidy(" How much time do I have left today? "));
        Assert.Equal("我刚做完LeetCode 的题。", Hearing.Tidy("我刚做完LeetCode 的题。"));    // a space by an English word stays
    }

    private static float[] Tone(double hz, int rate, double seconds) =>
        [.. Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)Math.Sin(2 * Math.PI * hz * i / rate))];

    private static double Loudness(float[] samples) => Math.Sqrt(samples.Skip(100).SkipLast(100).Average(s => s * (double)s));

    [Fact]
    public void Godot_s_48_kHz_comes_down_to_16_with_speech_kept_and_what_is_too_high_for_it_taken_out()
    {
        var speech = Hearing.To16k(Tone(1000, 48000, 1), 48000);
        Assert.Equal(16000, speech.Length);
        Assert.InRange(Loudness(speech), 0.68, 0.73);                       // a sine's loudness is 0.707: kept
        Assert.InRange(Loudness(Hearing.To16k(Tone(12000, 48000, 1), 48000)), 0, 0.02);   // would fold back to 4 kHz: gone

        var fromCd = Hearing.To16k(Tone(440, 44100, 0.5), 44100);
        Assert.Equal(8000, fromCd.Length);
        Assert.InRange(Loudness(fromCd), 0.68, 0.73);
        Assert.Equal([0.5f, -0.25f], Hearing.To16k([0.5f, -0.25f], 16000));
    }
}
