namespace InsertCoin.Mind.Tests;

/// <summary>Hands-free turns: she notices the player start, asks the end-of-turn model at pauses, and ends the turn.</summary>
public class TurnTakingTests
{
    private const double Frame = 0.032;            // the voice detector's step: 512 samples at 16 kHz

    /// <summary>What she made of these stretches of sound (speech or not), with the seconds each event came at.</summary>
    private static List<(TurnTaking.Heard What, double At)> Hear(TurnTaking turn, params (double Seconds, bool Speech)[] stretches)
    {
        var events = new List<(TurnTaking.Heard, double)>();
        var time = 0.0;
        foreach (var (seconds, speech) in stretches)
            for (var end = time + seconds; time < end - 1e-9; time += Frame)
                if (turn.Hear(speech, Frame) is var heard and not TurnTaking.Heard.Nothing)
                    events.Add((heard, time + Frame));
        return events;
    }

    [Fact]
    public void Nothing_said_in_the_window_and_she_stops_listening()
    {
        var events = Hear(new TurnTaking(window: 6), (8, false));

        var (what, at) = Assert.Single(events);
        Assert.Equal(TurnTaking.Heard.Expired, what);
        Assert.InRange(at, 6, 6.1);
    }

    [Fact]
    public void A_click_or_a_cough_does_not_start_a_turn()
    {
        var events = Hear(new TurnTaking(window: 3), (1, false), (0.1, true), (1, false), (0.15, true), (2, false));

        Assert.Equal([TurnTaking.Heard.Expired], events.Select(e => e.What));
    }

    [Fact]
    public void A_pause_asks_the_model_and_its_yes_ends_the_turn()
    {
        var turn = new TurnTaking(window: 6);
        var events = Hear(turn, (1, false), (2, true), (0.3, false));
        Assert.Equal([TurnTaking.Heard.Started, TurnTaking.Heard.AskModel], events.Select(e => e.What));
        Assert.InRange(events[1].At, 3.25, 3.32);              // a quarter second into the quiet

        turn.Verdict(turn.Asking, finished: true);
        Assert.Equal(TurnTaking.Heard.Finished, turn.Hear(false, Frame));
        Assert.Equal(TurnTaking.Heard.Nothing, turn.Hear(false, Frame));      // once
    }

    [Fact]
    public void When_the_model_says_not_yet_she_waits_up_to_two_seconds_of_quiet()
    {
        var turn = new TurnTaking(window: 6);
        Hear(turn, (2, true), (0.3, false));
        turn.Verdict(turn.Asking, finished: false);

        var events = Hear(turn, (3, false));
        var (what, at) = Assert.Single(events);
        Assert.Equal(TurnTaking.Heard.Finished, what);
        Assert.InRange(at, 1.65, 1.75);                         // 2 s of quiet in all: 0.3 before the verdict, the rest after
    }

    [Fact]
    public void Talking_on_after_a_pause_drops_the_question_and_the_next_pause_asks_again()
    {
        var turn = new TurnTaking(window: 6);
        Hear(turn, (2, true), (0.3, false));
        var first = turn.Asking;
        var events = Hear(turn, (1, true), (0.3, false));
        turn.Verdict(first, finished: true);                      // the answer to the old pause comes late: no longer the question

        Assert.Equal([TurnTaking.Heard.AskModel], events.Select(e => e.What));
        Assert.Equal(TurnTaking.Heard.Nothing, turn.Hear(false, Frame));
        turn.Verdict(turn.Asking, finished: true);
        Assert.Equal(TurnTaking.Heard.Finished, turn.Hear(false, Frame));
    }

    [Fact]
    public void Once_they_are_talking_the_window_does_not_close_and_a_long_turn_ends_at_thirty_seconds()
    {
        var events = Hear(new TurnTaking(window: 3), (2, false), (40, true));

        Assert.Equal([TurnTaking.Heard.Started, TurnTaking.Heard.Finished], events.Select(e => e.What));
        Assert.InRange(events[1].At - events[0].At, 29.6, 30.1);
    }
}
