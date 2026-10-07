using System.Diagnostics;
using System.Text.RegularExpressions;
using Godot;
using Microsoft.Win32;
using Windows.Media.Control;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The player's music app, through Windows' media controls (the system's media sessions, the same as the volume
/// flyout's): what is playing, and play, pause and skip on the music app alone, not on a game, a video or a recorder.
/// Spotify's session is the music app's whenever Spotify is open; otherwise another music app's (NetEase Cloud Music,
/// QQ Music, Apple Music…); a browser's only when their playlist is a web one (YouTube Music). When something should
/// play and no music app is open, Spotify is found on this PC and started (asked to start minimised, out of the way)
/// and, once it shows up, told to play: it carries on with what played last. A saved playlist starts from its
/// beginning: its link opens it in Spotify, and Spotify's own play button for it is pressed (see
/// <see cref="SpotifyButtons"/>: its links and Windows' media controls can't start a playlist; an album or a track its
/// link can); a web one (YouTube Music) starts in the browser. A test run (off screen) never touches the player's
/// music: it only says what it would do.
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
    private Task<bool>? _starting;                          // Spotify on its way up: anything else that wants it waits for the same

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Their saved playlists' links: a web one (not Spotify's) lets a browser's session count as the music app.</summary>
    public Func<IEnumerable<string>> Links { get; set; } = () => [];

    /// <summary>What is on now, or null when no music app has a session.</summary>
    public Track? Now { get; private set; }

    /// <summary>What is on changed (the screen's MUSIC app follows).</summary>
    public event Action? Changed;

    /// <summary>Spotify calls a saved playlist something else now (link, its name there): the name to keep.</summary>
    public event Action<string, string>? PlaylistRenamed;

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
        var web = Links().Any(link => !link.Contains("spotify", StringComparison.OrdinalIgnoreCase));
        return sessions.FirstOrDefault(s => From(s, "spotify")) ?? sessions.FirstOrDefault(s => MusicApps.Any(app => From(s, app)))
               ?? (web ? sessions.FirstOrDefault(s => Browsers.Any(app => From(s, app))) : null);
    }

    private async Task Refresh()
    {
        if (_refreshing || _manager is null)
            return;                                         // a test run has no sessions to read: what it pretends stays
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

    /// <summary>Plays: the music app open resumes; with none open, Spotify is started and then plays. False when nothing could.</summary>
    public async Task<bool> Play() => await Open() && await Act("play", s => s.TryPlayAsync().AsTask());

    public Task<bool> Pause() => Act("pause", s => s.TryPauseAsync().AsTask());

    /// <summary>Play or pause; with no music app open, as <see cref="Play"/>.</summary>
    public async Task<bool> Toggle() => Now is null ? await Play() : await Act("toggle", s => s.TryTogglePlayPauseAsync().AsTask());

    public async Task<bool> Next() => await Open() && await Act("next", s => s.TrySkipNextAsync().AsTask());
    public async Task<bool> Previous() => await Open() && await Act("previous", s => s.TrySkipPreviousAsync().AsTask());

    /// <summary>A music app is open, or Spotify has been started and has shown up.</summary>
    private async Task<bool> Open() => Now is not null || Session() is not null || await StartSpotify();

    /// <summary>Where Spotify is on this PC: its own installer's copy, the Store's, or whatever its links open; null when it isn't.</summary>
    public static string? SpotifyPath()
    {
        var own = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
        if (File.Exists(own))
            return own;
        var store = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe");
        if (File.Exists(store))
            return store;
        var command = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Classes\spotify\shell\open\command", "", null) as string
                      ?? Registry.GetValue(@"HKEY_CLASSES_ROOT\spotify\shell\open\command", "", null) as string;
        return command is not null && Regex.Match(command, "^\"([^\"]+\\.exe)\"", RegexOptions.IgnoreCase) is { Success: true } path && File.Exists(path.Groups[1].Value)
            ? path.Groups[1].Value : null;
    }

    /// <summary>Spotify is on this PC (its links open something).</summary>
    public static bool SpotifyHere => SpotifyPath() is not null || Registry.ClassesRoot.OpenSubKey("spotify") is not null;

    /// <summary>Starts Spotify (minimised, if it lets itself be) and waits for it to show up among the sessions; false when it isn't on this PC or never does.</summary>
    private Task<bool> StartSpotify() => _starting ??= Starting();

    private async Task<bool> Starting()
    {
        try
        {
            if (Companion.Paper.OffScreen)
            {
                GD.Print($"Music: would start Spotify ({SpotifyPath() ?? "not on this PC"}); a test run pretends it opens, paused");
                Show(new Track("Spotify", "", "", false));
                return true;
            }
            if (SpotifyPath() is { } path)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
            else if (SpotifyHere)
                OS.ShellOpen("spotify:");
            else
                return false;
            GD.Print("Music: starting Spotify");
            // it takes a few seconds to start, and a moment more to show up among the sessions
            for (var i = 0; i < 50 && Session() is null; i++)
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            await Refresh();
            return Session() is not null;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Music: couldn't start Spotify ({e.Message})");
            return false;
        }
        finally
        {
            _starting = null;
        }
    }

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
    /// Starts a saved playlist from its beginning (<paramref name="name"/> as Spotify shows it). A Spotify playlist's
    /// link opens it in Spotify (started for it if it wasn't open), then Spotify's own play button for it is pressed;
    /// started for this, Spotify is put down again once it plays. An album or a track starts by its link; a web
    /// playlist in the browser. False when Spotify's button never showed: then what played last carries on instead.
    /// </summary>
    public async Task<bool> StartPlaylist(string link, string name)
    {
        if (Address(link) is not { } address)
            return false;
        if (!address.StartsWith("spotify:"))
        {
            Launch(address);                                // the browser starts a web playlist itself
            return true;
        }
        if (Regex.IsMatch(address, "^spotify:(album|track):"))
        {
            Launch(address + ":play");
            return true;
        }
        // its button carries its name as Spotify has it now: ask (a name kept from before, or renamed since, would miss it)
        if (await NameOf(link) is { } current && current != name)
        {
            GD.Print($"Music: Spotify calls \"{name}\" \"{current}\"");
            PlaylistRenamed?.Invoke(link, current);
            name = current;
        }
        if (Companion.Paper.OffScreen)
        {
            GD.Print($"Music: would open {address} and press Spotify's \"Play {name}\" (a test run touches nothing)");
            Show(new Track("Spotify", name, "", true));
            return true;
        }
        var started = !SpotifyButtons.Running;
        Launch(address);
        // Spotify takes a moment to show the page (and longer to start): look for its button until it's there
        for (var i = 0; i < 40; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            var pressing = await Task.Run(() =>
            {
                try
                {
                    return SpotifyButtons.Play(name);
                }
                catch (Exception e)
                {
                    GD.PrintErr($"Music: Spotify's buttons couldn't be read ({e.Message})");
                    return SpotifyButtons.Pressing.Missing;
                }
            });
            if (pressing == SpotifyButtons.Pressing.Missing)
                continue;
            GD.Print(pressing == SpotifyButtons.Pressing.Pressed ? $"Music: pressed Spotify's \"Play {name}\"" : $"Music: \"{name}\" was playing already");
            if (started)
                SpotifyButtons.Minimise();
            await Refresh();
            return true;
        }
        GD.Print($"Music: Spotify never showed a play button for \"{name}\"; what played last carries on");
        await Play();
        return false;
    }

    /// <summary>
    /// Music for a focus part: <paramref name="playlist"/> (link, name) from its beginning when given; otherwise what
    /// is playing plays on, and the music app (or Spotify, started) resumes.
    /// </summary>
    public async Task<bool> PlayFocus((string Link, string Name)? playlist)
    {
        if (playlist is { } start)
            return await StartPlaylist(start.Link, start.Name);
        return Now is { Playing: true } || await Play();
    }

    /// <summary>A Spotify link's name, as Spotify shows it (from its public link preview); null when it can't say.</summary>
    public static async Task<string?> NameOf(string link)
    {
        if (!link.Contains("open.spotify.com", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            using var answer = await Http.GetAsync("https://open.spotify.com/oembed?url=" + Uri.EscapeDataString(link.Trim().Split('?')[0]));
            if (!answer.IsSuccessStatusCode)
                return null;
            using var json = System.Text.Json.JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
            return json.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } name ? name : null;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Music: couldn't ask Spotify the playlist's name ({e.Message})");
            return null;
        }
    }

    /// <summary>Opens a link in its app or the browser; a test run only says so.</summary>
    private static void Launch(string address)
    {
        if (Companion.Paper.OffScreen)
        {
            GD.Print($"Music: would open {address} (a test run opens nothing)");
            return;
        }
        GD.Print($"Music: opening {address}");
        OS.ShellOpen(address);
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

    /// <summary>Dev: what Spotify's window shows to press (how many play buttons, and whether one for <paramref name="playlist"/>); presses nothing.</summary>
    public async void DevSpotify(string? playlist)
    {
        var said = await Task.Run(() =>
        {
            try
            {
                return SpotifyButtons.Probe(playlist);
            }
            catch (Exception e)
            {
                return $"failed: {e}";
            }
        });
        GD.Print($"Music: Spotify's window: {said}");
    }

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
