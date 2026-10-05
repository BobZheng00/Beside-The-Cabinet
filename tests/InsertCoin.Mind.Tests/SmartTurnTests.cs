namespace InsertCoin.Mind.Tests;

/// <summary>
/// Smart Turn's input: Whisper's log-mel features of the last 8 s (the port was checked against transformers in the
/// agent spike; these keep its shape and behaviour from drifting).
/// </summary>
public class SmartTurnTests
{
    private const int Mels = 80, Frames = 800;

    private static float[] Tone(double seconds, double hz) =>
        [.. Enumerable.Range(0, (int)(seconds * SmartTurn.Rate)).Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / SmartTurn.Rate)))];

    private static float At(float[] features, int mel, int frame) => features[mel * Frames + frame];

    [Fact]
    public void Short_audio_is_padded_with_silence_in_front_and_long_audio_keeps_its_last_eight_seconds()
    {
        var features = SmartTurn.Features(Tone(2, 440));

        Assert.Equal(Mels * Frames, features.Length);
        Assert.True(At(features, 10, 100) < At(features, 10, 700));       // silence first, the tone at the end
        var longer = SmartTurn.Features([.. new float[SmartTurn.Rate * 4], .. Tone(8, 440)]);
        Assert.Equal(SmartTurn.Features(Tone(8, 440)), longer);           // what came before the last 8 s makes no difference
    }

    [Fact]
    public void A_tone_lights_the_mel_band_it_falls_in()
    {
        var low = SmartTurn.Features(Tone(8, 300));
        var high = SmartTurn.Features(Tone(8, 3000));

        int Loudest(float[] features) => Enumerable.Range(0, Mels).MaxBy(m => At(features, m, 400));
        Assert.True(Loudest(low) < Loudest(high));
        Assert.InRange(Loudest(low), 5, 20);
        Assert.InRange(Loudest(high), 45, 65);
    }
}
