namespace InsertCoin.Mind.Tests;

/// <summary>Whether she listens over her own voice: echo cancelled on the microphone, and her voice on the output it listens to.</summary>
public class TalkingOverTests
{
    [Fact]
    public void Echo_cancelled_and_her_voice_on_the_output_for_calls_she_can_be_talked_over() =>
        Assert.Null(TalkingOver.WhyNot(cancelsEcho: true, callsOutput: "speakers", voiceOutput: "speakers"));

    [Fact]
    public void A_microphone_without_echo_cancelling_keeps_her_from_listening_while_she_speaks() =>
        Assert.Contains("doesn't cancel echo", TalkingOver.WhyNot(cancelsEcho: false, callsOutput: "speakers", voiceOutput: "speakers"));

    [Theory]
    [InlineData("headset", "speakers")]
    [InlineData(null, "speakers")]
    [InlineData("speakers", null)]
    [InlineData("", "")]
    public void Her_voice_anywhere_but_the_output_for_calls_keeps_her_from_listening_while_she_speaks(string? calls, string? voice) =>
        Assert.Contains("another output", TalkingOver.WhyNot(cancelsEcho: true, calls, voice));
}
