using System.Text.RegularExpressions;
using Godot;
using Windows.Media.Control;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The player's music app, through Windows' media controls (the system's media sessions, the same as the volume
/// flyout's): what is playing, and play, pause and skip on the music app alone, not on a game, a video or a recorder.
/// Spotify's session is the music app's whenever Spotify is open; otherwise another music app's (NetEase Cloud Music,
/// QQ Music, Apple Music…); a browser's only when their playlist is a web one (YouTube Music). Their focus
/// playlist opens by its link: a Spotify link in the Spotify app, which shows the playlist but doesn't start it (it
/// can't be told to), so after that first time the music app is resumed instead, and it carries on where it left off.
/// A test run (off screen) never touches the player's music: it only says what it would do.
/// </summary>
public partial class Music : Node
{
    /// <summary>What the music app is doing: its name, the track, and whether it is playing.</summary>
    public sealed record Track(string App, string Title, string Artist, bool Playing);

    private static readonly string[] MusicApps =
        ["zunemusic", "applemusic", "tidal", "deezer", "amazonmusic", "musicbee", "foobar2000", "aimp", "cloudmusic", "qqmusic", "kugou", "kuwo"];
    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera"];

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _refreshing;

    /// <summary>Their focus playlist's link, or null: a web one (not Spotify's) lets a browser's session count as the music app.</summary>
    public Func<string?> Playlist { get; set; } = () => null;

    /// <summary>What is on now, or null when no music app has a session.</summary>
    public Track? Now { get; private set; }

    /// <summary>What is on changed (the screen's MUSIC app follows).</summary>
    public event Action? Changed;

    public override async void _Ready()
    {
        if (Companion.Paper.OffScreen)
            return;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception e)
        {
            GD.PrintErr($"Music: Windows' media controls aren't there ({e.Message})");
            return;
        }
        var poll = new Godot.Timer { WaitTime = 2, Autostart = true };
        poll.Timeout += () => _ = Refresh();
        AddChild(poll);
        _ = Refresh();
    }

    /// <summary>The music app's session: Spotify's, another music app's, or with a web playlist a browser's; null when none is open.</summary>
    private GlobalSystemMediaTransportControlsSession? Session()
    {
        if (_manager is null)
            return null;
        var sessions = _manager.GetSessions();
        bool From(GlobalSystemMediaTransportControlsSession s, string app) => s.SourceAppUserModelId.Contains(app, StringComparison.OrdinalIgnoreCase);
        var web = Playlist() is { } link && !link.Contains("spotify", StringComparison.OrdinalIgnoreCase);
        return sessions.FirstOrDefault(s => From(s, "spotify")) ?? sessions.FirstOrDefault(s => MusicApps.Any(app => From(s, app)))
               ?? (web ? sessions.FirstOrDefault(s => Browsers.Any(app => From(s, app))) : null);
    }

    private async Task Refresh()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            Track? now = null;
            if (Session() is { } session)
            {
                var about = await session.TryGetMediaPropertiesAsync();
                var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                now = new Track(AppName(session.SourceAppUserModelId), about?.Title ?? "", about?.Artist ?? "", playing);
            }
            Show(now);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Music: couldn't read what's playing ({e.Message})");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Show(Track? now)
    {
        if (now == Now)
            return;
        Now = now;
        Changed?.Invoke();
    }

    /// <summary>"Spotify", "Chrome", "Msedge"…: the app as the player knows it, from the session's app id.</summary>
    private static string AppName(string id)
    {
        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            return "Spotify";
        // a Store app's id is its package's ("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"), a desktop app's its program ("msedge.exe")
        var name = id.Contains('!') ? id.Split('!')[0].Split('_')[0].Split('.').Last() : id.Split('\\').Last();
        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "Music";
    }

    /// <summary>What is on, for her: "Music: Spotify is playing "Song" by Someone."</summary>
    public string Describe() => Now switch
    {
        null => "Music: no music app is open.",
        { Title.Length: 0 } now => $"Music: {now.App} is open, {(now.Playing ? "playing" : "paused")}.",
        var now => $"Music: {now.App} {(now.Playing ? "is playing" : "is paused on")} \"{now.Title}\"{(now.Artist.Length > 0 ? $" by {now.Artist}" : "")}.",
    };

    // ---- Doing things ------------------------------------------------------------------------------------------------

    public Task<bool> Play() => Act("play", s => s.TryPlayAsync().AsTask());
    public Task<bool> Pause() => Act("pause", s => s.TryPauseAsync().AsTask());
    public Task<bool> Toggle() => Act("toggle", s => s.TryTogglePlayPauseAsync().AsTask());
    public Task<bool> Next() => Act("next", s => s.TrySkipNextAsync().AsTask());
    public Task<bool> Previous() => Act("previous", s => s.TrySkipPreviousAsync().AsTask());

    /// <summary>Does it on the music app's session; false when there is none, or it said no.</summary>
    private async Task<bool> Act(string what, Func<GlobalSystemMediaTransportControlsSession, Task<bool>> act)
    {
        if (Companion.Paper.OffScreen)
        {
            GD.Print($"Music: would {what} (a test run leaves the player's music alone)");
            if (Now is { } pretend && what is "play" or "pause" or "toggle")
                Show(pretend with { Playing = what == "play" || what == "toggle" && !pretend.Playing });
            return Now is not null;
        }
        if (Session() is not { } session)
            return false;
        try
        {
            var done = await act(session);
            GetTree().CreateTimer(0.4).Timeout += () => _ = Refresh();      // the app takes a moment to say so
            return done;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Music: couldn't {what} ({e.Message})");
            return false;
        }
    }

    /// <summary>
    /// Plays the focus playlist: when the music app is open it is resumed (it carries on where it left off); when it
    /// isn't, the playlist opens by its link, and once the app is up it is told to play. False when there is no link
    /// and no music app either.
    /// </summary>
    public async Task<bool> PlayPlaylist(string? link)
    {
        if (Now is { Playing: true })
            return true;
        if (Now is not null)
            return await Play();
        if (Address(link) is not { } address)
            return false;
        if (Companion.Paper.OffScreen)
        {
            GD.Print($"Music: would open {address} (a test run opens nothing)");
            return true;
        }
        OS.ShellOpen(address);
        // the app takes a few seconds to start and show up among the sessions
        for (var i = 0; i < 20 && Now is null; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            await Refresh();
        }
        return Now is not null && await Play();
    }

    /// <summary>
    /// What a playlist link opens: a Spotify web link becomes the Spotify app's own (spotify:playlist:…), straight
    /// into the app; another web link (YouTube Music, say) opens in the browser as it is. Null when it is neither.
    /// </summary>
    public static string? Address(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        link = link.Trim();
        if (Regex.Match(link, @"^https?://open\.spotify\.com/(?:intl-[a-z-]+/)?(playlist|album|artist|track|show)/([A-Za-z0-9]+)") is { Success: true } web)
            return $"spotify:{web.Groups[1].Value}:{web.Groups[2].Value}";
        if (Regex.IsMatch(link, @"^spotify:(playlist|album|artist|track|show):[A-Za-z0-9]+$"))
            return link;
        return InsertCoin.Mind.BuiltIn.Page(link)?.AbsoluteUri;
    }

    /// <summary>A link as the player saved it, shorter: "open.spotify.com/playlist/37i9dQ…".</summary>
    public static string Shown(string link)
    {
        var shown = Regex.Replace(link.Trim(), @"^https?://", "");
        shown = shown.Split('?')[0];
        return shown.Length > 42 ? shown[..41] + "…" : shown;
    }

    // ---- Dev ---------------------------------------------------------------------------------------------------------

    /// <summary>Dev: checks Windows' media controls answer, saying only how many sessions there are and whose (no titles), even in a test run.</summary>
    public async void DevProbe()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var apps = manager.GetSessions().Select(s => AppName(s.SourceAppUserModelId)).ToList();
            GD.Print($"Music: Windows' media controls answer: {apps.Count} sessions ({string.Join(", ", apps)})");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Music: Windows' media controls failed: {e}");
        }
    }

    /// <summary>Dev: pretends a music app is playing (title, artist), or none (null), so a test never reads the player's own.</summary>
    public void DevPlaying(string? title, string artist = "", bool playing = true) =>
        Show(title is null ? null : new Track("Spotify", title, artist, playing));
}
