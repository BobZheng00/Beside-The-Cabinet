using Godot;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Focus sessions on the cabinet (see <see cref="FocusSession"/>), started from the screen's FOCUS app or by her, and
/// ticking every second. What the player does themselves, and each part running out, is told to her to say (see
/// <see cref="Told"/>); what she does with her tool, she says in her reply. When the player wants it, their music plays
/// while they focus and pauses at breaks, while paused, and when the session ends.
/// </summary>
public partial class Focus : Node
{
    private SettingsFile _settings = null!;
    private Music _music = null!;

    /// <summary>The session under way, or null.</summary>
    public FocusSession? Session { get; private set; }

    /// <summary>Every second while a session runs, and whenever it changes: the screen follows.</summary>
    public event Action? Changed;

    /// <summary>Something for her to say, in her own words (a note for her mind), and the line she says without one.</summary>
    public event Action<string, string>? Told;

    public void Build(SettingsFile settings, Music music)
    {
        _settings = settings;
        _music = music;
        var clock = new Godot.Timer { WaitTime = 1, Autostart = true };
        clock.Timeout += Tick;
        AddChild(clock);
    }

    /// <summary>The player's lengths (the FOCUS app, settings.json).</summary>
    public FocusPlan Plan
    {
        get
        {
            var s = _settings.Now;
            return new FocusPlan(s.FocusMinutes, s.BreakMinutes, s.LongBreakMinutes, s.LongBreakEvery).Checked();
        }
    }

    /// <summary>The saved playlist a session starts with (link, name), or null: what played last carries on.</summary>
    public (string Link, string Name)? FocusPlaylist =>
        _settings.Now.Playlists.FirstOrDefault(p => p.Link == _settings.Now.FocusPlaylist) is { } playlist ? (playlist.Link, playlist.Name) : null;

    /// <summary>Their music plays while they focus (the MUSIC and FOCUS apps' switch).</summary>
    public bool WithMusic
    {
        get => _settings.Now.FocusMusic;
        set => _settings.Save(_settings.Now with { FocusMusic = value });
    }

    /// <summary>The player's lengths for focus parts and short breaks, kept for the next session.</summary>
    public void SetLengths(int focus, int rest)
    {
        _settings.Save(_settings.Now with { FocusMinutes = focus, BreakMinutes = rest });
        Changed?.Invoke();
    }

    /// <summary>Starts a session, with these lengths for this one if given; <paramref name="byPlayer"/>: from the screen, so she is told.</summary>
    public void Start(int? focus = null, int? rest = null, bool byPlayer = true)
    {
        var plan = Plan with { Focus = focus ?? Plan.Focus, Break = rest ?? Plan.Break };
        var session = Session = new FocusSession(plan, DateTimeOffset.Now);
        GD.Print($"Focus: a session starts ({session.Plan}){(byPlayer ? "" : ", by her")}");
        Music(FocusPart.Focus, fromStart: true);
        if (byPlayer)
            Told?.Invoke(session.NoteOnStart(), Lines.FocusStarted(session.Plan));
        Changed?.Invoke();
    }

    public void Stop(bool byPlayer = true)
    {
        if (Session is not { } session)
            return;
        Session = null;
        GD.Print($"Focus: the session ends after {Words.Count(session.Done, "focus part")}");
        if (WithMusic)
            _ = _music.Pause();
        if (byPlayer)
            Told?.Invoke(session.NoteOnStop(DateTimeOffset.Now), Lines.FocusStopped(session.Done));
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (Session is not { Paused: false } session)
            return;
        session.Pause(DateTimeOffset.Now);
        if (WithMusic && session.Part == FocusPart.Focus)
            _ = _music.Pause();
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (Session is not { Paused: true } session)
            return;
        session.Resume(DateTimeOffset.Now);
        Music(session.Part);
        Changed?.Invoke();
    }

    /// <summary>Ends the part under way now; <paramref name="byPlayer"/>: from the screen, so she says what comes next.</summary>
    public FocusTurn? Skip(bool byPlayer = true)
    {
        if (Session is not { } session)
            return null;
        var turn = session.Skip(DateTimeOffset.Now);
        Turned(session, turn, tell: byPlayer);
        return turn;
    }

    private void Tick()
    {
        if (Session is not { } session)
            return;
        if (session.Tick(DateTimeOffset.Now) is { } turn)
            Turned(session, turn, tell: true);
        else
            Changed?.Invoke();
    }

    private void Turned(FocusSession session, FocusTurn turn, bool tell)
    {
        GD.Print($"Focus: {turn.Ended} over, {turn.Began} begins (round {turn.Round})");
        Music(turn.Began);
        if (tell)
            Told?.Invoke(session.NoteOn(turn), Lines.Focus(turn, session.Plan));
        Changed?.Invoke();
    }

    /// <summary>
    /// With the music on: it plays while focusing (a session's first part starts their focus playlist from its
    /// beginning, if they starred one; after a break what was playing carries on) and pauses at breaks.
    /// </summary>
    private void Music(FocusPart part, bool fromStart = false)
    {
        if (!WithMusic)
            return;
        if (part == FocusPart.Focus)
            _ = _music.PlayFocus(fromStart ? FocusPlaylist : null);
        else
            _ = _music.Pause();
    }

    /// <summary>For the screen's readout: the time left as a clock, what part, and how much of it is behind.</summary>
    public (string Big, string Caption, float Done)? Readout()
    {
        if (Session is not { } session)
            return null;
        var now = DateTimeOffset.Now;
        var left = (int)Math.Ceiling(session.Left(now).TotalSeconds);
        var part = session.Paused ? "PAUSED" : session.Part switch
        {
            FocusPart.Focus => $"FOCUS - ROUND {session.Round}",
            FocusPart.Break => "BREAK - STAND UP",
            _ => "LONG BREAK",
        };
        return ($"{left / 60:00}:{left % 60:00}", part, (float)session.Through(now));
    }
}
