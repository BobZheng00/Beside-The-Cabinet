namespace InsertCoin.Mind.Tests;

/// <summary>She nods in the player's pauses, after they have said something, a few times a minute.</summary>
public class BackchannelTests
{
    private const double Frame = 1.0 / 60;

    /// <summary>Always under the chance: every pause that is due gets its nod.</summary>
    private sealed class Sure(double value = 0) : Random
    {
        public override double NextDouble() => value;
    }

    /// <summary>The seconds (from the start) at which she nodded, hearing these stretches of loudness one after another.</summary>
    private static List<double> Nods(Backchannel listener, params (double Seconds, float Level)[] stretches)
    {
        var nods = new List<double>();
        var time = 0.0;
        foreach (var (seconds, level) in stretches)
            for (var end = time + seconds; time < end - 1e-9; time += Frame)
                if (listener.Hear(level, Frame))
                    nods.Add(time);
        return nods;
    }

    [Fact]
    public void A_nod_comes_in_the_pause_after_a_stretch_of_speech()
    {
        var nods = Nods(new Backchannel(new Sure()), (0.5, 0.02f), (2, 0.5f), (2, 0.02f));

        var nod = Assert.Single(nods);
        Assert.InRange(nod, 2.5 + 0.28, 2.5 + 0.35);             // once the quiet has lasted long enough to be a pause
    }

    [Fact]
    public void Gaps_between_words_and_a_word_or_two_get_no_nod()
    {
        var talk = Enumerable.Repeat<(double, float)[]>([(0.6, 0.5f), (0.15, 0.02f)], 4).SelectMany(s => s);
        Assert.Empty(Nods(new Backchannel(new Sure()), [(0.5, 0.02f), .. talk, (0.5, 0.5f)]));
        Assert.Empty(Nods(new Backchannel(new Sure()), (0.5, 0.02f), (0.7, 0.5f), (2, 0.02f)));     // "yes": too short to nod at
    }

    [Fact]
    public void Nods_keep_apart_and_not_every_pause_gets_one()
    {
        // two pauses two seconds apart: only the first gets a nod; the next after a longer stretch does
        var nods = Nods(new Backchannel(new Sure()), (0.5, 0.02f), (1.5, 0.5f), (0.4, 0.02f), (1.5, 0.5f), (0.4, 0.02f), (2.5, 0.5f), (0.4, 0.02f));
        Assert.Equal(2, nods.Count);
        Assert.InRange(nods[1] - nods[0], 4, 5);

        Assert.Empty(Nods(new Backchannel(new Sure(0.9)), (0.5, 0.02f), (2, 0.5f), (2, 0.02f)));   // past the chance: no nod
    }

    [Fact]
    public void A_noisy_room_still_has_pauses()
    {
        var nods = Nods(new Backchannel(new Sure()), (1, 0.2f), (2, 0.9f), (1, 0.2f));

        Assert.Single(nods);
    }
}
