using Microsoft.Extensions.AI;

namespace InsertCoin.Mind.Tests;

/// <summary>Focus sessions (Pomodoro) on the cabinet, and her tools for them and for the player's music.</summary>
public class FocusTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void A_session_focuses_then_breaks_round_after_round_with_a_long_break_after_every_fourth()
    {
        var session = new FocusSession(new FocusPlan(), Nine);
        var now = Nine;
        var parts = new List<(FocusPart Part, int Round, TimeSpan Length)>();
        for (var i = 0; i < 9; i++)
        {
            parts.Add((session.Part, session.Round, session.Length));
            now += session.Length;
            Assert.Null(session.Tick(now - TimeSpan.FromSeconds(1)));          // not a second early
            Assert.NotNull(session.Tick(now));
        }

        Assert.Equal([
            (FocusPart.Focus, 1, TimeSpan.FromMinutes(25)), (FocusPart.Break, 1, TimeSpan.FromMinutes(5)),
            (FocusPart.Focus, 2, TimeSpan.FromMinutes(25)), (FocusPart.Break, 2, TimeSpan.FromMinutes(5)),
            (FocusPart.Focus, 3, TimeSpan.FromMinutes(25)), (FocusPart.Break, 3, TimeSpan.FromMinutes(5)),
            (FocusPart.Focus, 4, TimeSpan.FromMinutes(25)), (FocusPart.LongBreak, 4, TimeSpan.FromMinutes(15)),
            (FocusPart.Focus, 5, TimeSpan.FromMinutes(25)),
        ], parts);
        Assert.Equal(5, session.Done);
    }

    [Fact]
    public void Pausing_stops_the_clock_and_skipping_ends_the_part_at_once()
    {
        var session = new FocusSession(new FocusPlan(Focus: 30, Break: 10), Nine);
        Assert.Equal(TimeSpan.FromMinutes(20), session.Left(Nine.AddMinutes(10)));

        session.Pause(Nine.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(20), session.Left(Nine.AddHours(2)));
        Assert.Null(session.Tick(Nine.AddHours(2)));                          // paused, its time never runs out
        session.Resume(Nine.AddHours(2));
        Assert.Equal(TimeSpan.FromMinutes(15), session.Left(Nine.AddHours(2).AddMinutes(5)));

        session.Pause(Nine.AddHours(2).AddMinutes(5));
        var turn = session.Skip(Nine.AddHours(2).AddMinutes(6));
        Assert.Equal(new FocusTurn(FocusPart.Focus, FocusPart.Break, 1), turn);
        Assert.False(session.Paused);                                         // the break runs
        Assert.Equal(TimeSpan.FromMinutes(10), session.Left(Nine.AddHours(2).AddMinutes(6)));
        Assert.Equal(0.5, session.Through(Nine.AddHours(2).AddMinutes(11)), 3);
    }

    [Fact]
    public void Lengths_are_kept_sensible()
    {
        var plan = new FocusPlan(Focus: 0, Break: 500, LongBreak: -3, LongEvery: 1).Checked();
        Assert.Equal(new FocusPlan(1, 60, 1, 2), plan);
    }

    [Fact]
    public void She_is_told_where_a_session_stands_and_what_each_turn_means()
    {
        var session = new FocusSession(new FocusPlan(LongEvery: 2), Nine);
        Assert.Equal("Focus session since 09:00: focusing (round 1), 15 min left; 0 focus parts done. Lengths: 25 min focus, 5 min breaks, " +
                     "15 min after every 2 focus parts.", session.Describe(Nine.AddMinutes(10)));

        var toBreak = session.Tick(Nine.AddMinutes(25))!;
        Assert.Contains("5-minute break starts now", session.NoteOn(toBreak));
        Assert.Contains("water", session.NoteOn(toBreak));
        Assert.Contains("under a minute left", session.Describe(Nine.AddMinutes(29).AddSeconds(30)));

        var toFocus = session.Tick(Nine.AddMinutes(30))!;
        Assert.Equal(new FocusTurn(FocusPart.Break, FocusPart.Focus, 2), toFocus);
        Assert.Contains("focus part 2 starts now, 25 minutes", session.NoteOn(toFocus));

        var toLong = session.Tick(Nine.AddMinutes(55))!;
        Assert.Equal(FocusPart.LongBreak, toLong.Began);
        Assert.Contains("15-minute long break", session.NoteOn(toLong));
        session.Pause(Nine.AddMinutes(56));
        Assert.Contains("on a long break, 14 min left, paused; 2 focus parts done", session.Describe(Nine.AddMinutes(56)));
        Assert.Contains("2 focus parts done in 1 h", session.NoteOnStop(Nine.AddMinutes(60)));
        Assert.Contains("for 1 h 10 min without a real break", FocusSession.NoteOnSitting(TimeSpan.FromMinutes(70)));
    }

    [Fact]
    public async Task Her_focus_tool_passes_on_what_she_asked_for()
    {
        var asked = new List<(string, int?, int?)>();
        var tool = BuiltIn.Focus((action, focus, rest) => { asked.Add((action, focus, rest)); return "Started."; });

        var result = await tool.Function.InvokeAsync(new AIFunctionArguments { ["action"] = " Start ", ["focus_minutes"] = 50 });
        await tool.Function.InvokeAsync(new AIFunctionArguments { ["action"] = "stop" });

        Assert.Equal("Started.", result?.ToString());
        Assert.Equal([("start", 50, null), ("stop", null, null)], asked);
        Assert.False(tool.OutsideApp);
    }

    [Fact]
    public async Task Her_music_tool_acts_outside_the_app_and_says_what_she_would_do_on_the_card()
    {
        var done = new List<(string, string?)>();
        var tool = BuiltIn.Music((action, playlist) => { done.Add((action, playlist)); return Task.FromResult("Done."); },
                                 name => name is null ? "Deep Focus (the focus one)" : $"{name} in Spotify");

        var result = await tool.Function.InvokeAsync(new AIFunctionArguments { ["action"] = "PAUSE" });
        await tool.Function.InvokeAsync(new AIFunctionArguments { ["action"] = "start_playlist", ["playlist"] = " Peace " });
        await tool.Function.InvokeAsync(new AIFunctionArguments { ["action"] = "start_playlist", ["playlist"] = "" });

        Assert.Equal("Done.", result?.ToString());
        Assert.Equal([("pause", null), ("start_playlist", "Peace"), ("start_playlist", null)], done);
        Assert.True(tool.OutsideApp);
        Assert.Equal(("Start a playlist?", "Peace in Spotify", "music"), tool.Ask!(new AIFunctionArguments { ["action"] = "start_playlist", ["playlist"] = "Peace" })!.Value);
        Assert.Equal("Deep Focus (the focus one)", tool.Ask!(new AIFunctionArguments { ["action"] = "start_playlist" })!.Value.Detail);
        Assert.Equal("Pause your music?", tool.Ask!(new AIFunctionArguments { ["action"] = "pause" })!.Value.What);
    }
}
