using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

/// <summary>Programs the player picks count as play while in front, and stop when they've left the PC (spec 2026-10-27-what-earns-play-time.md).</summary>
public class PlayProgramTests
{
    private static readonly PlayProgram Vlc = new(@"C:\Program Files\VideoLAN\VLC\vlc.exe", "VLC media player");
    private static readonly RunningGame Picked = new(Vlc.Path, Vlc.Name, Picked: true);
    private static readonly RunningGame Steam = new("1", "Graveyard Keeper 2");

    [Fact]
    public void A_picked_program_counts_only_while_its_window_is_in_front_and_the_player_is_at_the_pc()
    {
        PlayProgram[] picked = [Vlc];

        Assert.Equal(Picked, PlayProgram.InFront(picked, @"c:\program files\videolan\vlc\VLC.EXE", away: false));
        Assert.Null(PlayProgram.InFront(picked, @"C:\Windows\explorer.exe", away: false));
        Assert.Null(PlayProgram.InFront(picked, null, away: false));
        Assert.Null(PlayProgram.InFront(picked, Vlc.Path, away: true));
        Assert.Null(PlayProgram.InFront([], Vlc.Path, away: false));
    }

    [Fact]
    public void A_picked_program_spends_play_time_by_its_name_and_never_pauses_while_the_player_sits_still()
    {
        var m = new Machine();
        m.GiveTokens(1);

        var started = Assert.Single(m.Tick([Picked], Check, TimeSpan.FromMinutes(30)).OfType<SessionStarted>());
        for (var t = TimeSpan.Zero; t < TimeSpan.FromMinutes(10); t += Check)
            m.Tick([Picked], Check, TimeSpan.FromMinutes(30));

        Assert.Equal(new[] { "VLC media player" }, started.GameNames);
        Assert.Empty(m.Events.OfType<Paused>());
        Assert.False(m.State.Paused);
        Assert.Equal(Min(5), m.State.FuelMs);
        Assert.Equal(Min(10), m.State.Session!.PlayedMs);
    }

    [Fact]
    public void A_picked_program_gone_from_the_front_or_left_behind_ends_its_session_as_a_game_closing_does()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Tick([Picked], Check, TimeSpan.Zero);
        m.Tick([Picked], Check, TimeSpan.Zero);

        Assert.Null(PlayProgram.InFront([Vlc], Vlc.Path, away: true));      // the screen locked, or they said they were off
        m.Tick([], Check, TimeSpan.Zero);
        var ended = Assert.Single(m.Tick([], Check, TimeSpan.Zero).OfType<SessionEnded>());

        Assert.Null(m.State.Session);
        Assert.Equal(new[] { "VLC media player" }, ended.Receipt.GameNames);
        Assert.Equal(5_000, ended.Receipt.PlayedMs);
    }

    [Fact]
    public void With_a_steam_game_running_too_the_picked_program_in_front_keeps_the_clock_going_and_without_it_the_game_pauses()
    {
        var m = new Machine();
        m.GiveTokens(1);
        var idle = TimeSpan.FromMinutes(20);

        m.Tick([Steam, Picked], Check, idle);
        for (var i = 0; i < 12; i++)
            m.Tick([Steam, Picked], Check, idle);
        Assert.Empty(m.Events.OfType<Paused>());
        Assert.Equal(Min(14), m.State.FuelMs);

        m.Tick([Steam], Check, idle);                       // another window in front: the game's own pause
        Assert.Single(m.Events.OfType<Paused>());
        Assert.True(m.State.Paused);
        Assert.Equal(Min(14), m.State.FuelMs);
    }

    [Fact]
    public void With_no_play_time_a_picked_program_is_still_noticed_and_spends_nothing()
    {
        var m = new Machine(rules: new Rules { Earns = new Earning(false, false, false) });

        m.Tick([Picked], Check, TimeSpan.FromMinutes(30));
        for (var i = 0; i < 12; i++)
            m.Tick([Picked], Check, TimeSpan.FromMinutes(30));

        Assert.Equal(Min(1), m.State.Session!.PlayedMs);
        Assert.Equal(0, m.State.DebtMs);
        Assert.Contains("VLC media player", m.State.Today!.Games);
    }
}
