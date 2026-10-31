using InsertCoin.Core;
using static InsertCoin.Core.Tests.Machine;

namespace InsertCoin.Core.Tests;

/// <summary>
/// Programs the player picks count as play while in front, are held quietly behind other windows, and stop when the
/// program closes or the player has left the PC (spec 2026-10-27-what-earns-play-time.md).
/// </summary>
public class PlayProgramTests
{
    private static readonly PlayProgram Vlc = new(@"C:\Program Files\VideoLAN\VLC\vlc.exe", "VLC media player");
    private static readonly RunningGame Picked = new(Vlc.Path, Vlc.Name, Picked: true);
    private static readonly RunningGame Behind = Picked with { Held = true };
    private static readonly RunningGame Steam = new("1", "Graveyard Keeper 2");

    private static IReadOnlyList<RunningGame> Seen(string? front, bool away = false, params string[] running) =>
        PlayProgram.Seen([Vlc], front, () => running, away);

    [Fact]
    public void A_picked_program_counts_in_front_is_held_behind_another_window_and_is_gone_once_closed_or_the_player_has_left()
    {
        Assert.Equal([Picked], Seen(@"c:\program files\videolan\vlc\VLC.EXE"));
        Assert.Equal([Behind], Seen(@"C:\Windows\explorer.exe", running: Vlc.Path.ToUpperInvariant()));
        Assert.Empty(Seen(@"C:\Windows\explorer.exe"));                         // closed
        Assert.Empty(Seen(null));
        Assert.Empty(Seen(Vlc.Path, away: true));                               // the screen locked or dark, or they said they were off
        Assert.Empty(Seen(null, away: true, Vlc.Path));
        Assert.Empty(PlayProgram.Seen([], Vlc.Path, () => [Vlc.Path], away: false));
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
    public void Behind_another_window_a_picked_program_holds_its_session_quietly_and_goes_on_in_front_with_its_time_adding_up()
    {
        var m = new Machine();
        m.GiveTokens(1);
        for (var i = 0; i <= 12; i++)                       // a minute in front
            m.Tick([Picked], Check, TimeSpan.Zero);
        m.Events.Clear();
        var startedAt = m.State.Session!.StartedAt;
        var fuel = m.State.FuelMs;

        for (var i = 0; i < 24; i++)                        // two minutes answering a message
            m.Tick([Behind], Check, TimeSpan.Zero);

        Assert.True(m.State.Held);
        Assert.Equal(fuel, m.State.FuelMs);
        Assert.DoesNotContain(m.Events, e => e is SessionEnded or SessionStarted or Paused or Resumed);

        for (var i = 0; i < 12; i++)
            m.Tick([Picked], Check, TimeSpan.Zero);

        Assert.False(m.State.Held);
        Assert.Equal(startedAt, m.State.Session!.StartedAt);   // the same session: the long-session break keeps adding up
        Assert.DoesNotContain(m.Events, e => e is SessionEnded or SessionStarted or Paused or Resumed);
        Assert.Equal(fuel - Min(1), m.State.FuelMs);
    }

    [Fact]
    public void A_picked_program_closed_or_left_behind_ends_its_session_as_a_game_closing_does()
    {
        var m = new Machine();
        m.GiveTokens(1);
        m.Tick([Picked], Check, TimeSpan.Zero);
        m.Tick([Behind], Check, TimeSpan.Zero);
        m.Tick([Picked], Check, TimeSpan.Zero);

        m.Tick(Seen(null, away: true, Vlc.Path), Check, TimeSpan.Zero);
        var ended = Assert.Single(m.Tick(Seen(null, away: true, Vlc.Path), Check, TimeSpan.Zero).OfType<SessionEnded>());

        Assert.Null(m.State.Session);
        Assert.False(m.State.Held);
        Assert.Equal(new[] { "VLC media player" }, ended.Receipt.GameNames);
        Assert.Equal(5_000, ended.Receipt.PlayedMs);
    }

    [Fact]
    public void A_picked_program_running_behind_other_windows_starts_nothing_and_keeps_no_other_game_s_session_going()
    {
        var m = new Machine();
        m.GiveTokens(1);

        m.Tick([Behind], Check, TimeSpan.Zero);
        Assert.Null(m.State.Session);

        m.Play(TimeSpan.FromMinutes(1));                    // a Steam game, while the program sits in the background
        m.Tick([Behind], Check, TimeSpan.Zero);
        Assert.Single(m.Tick([Behind], Check, TimeSpan.Zero).OfType<SessionEnded>());
    }

    [Fact]
    public void A_session_held_behind_another_window_doesn_t_keep_the_day_from_turning_at_four()
    {
        var m = new Machine();
        m.GiveTokens(4);
        m.Now = new DateTimeOffset(2026, 10, 1, 3, 59, 0, T0.Offset);
        m.Tick([Picked], Check, TimeSpan.Zero);
        m.Tick([Picked], Check, TimeSpan.Zero);
        m.Tick([Behind], Check, TimeSpan.Zero);

        for (var i = 0; i < 12; i++)                        // past 04:00, still held
            m.Tick([Behind], Check, TimeSpan.Zero);

        Assert.Single(m.Events.OfType<Faded>());
        Assert.Equal(new DateOnly(2026, 10, 1), m.State.Today!.Day);
        Assert.NotNull(m.State.Session);
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

        m.Tick([Steam, Behind], Check, idle);               // another window in front: the game's own pause
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
