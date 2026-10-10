using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace InsertCoin.Mind;

/// <summary>The tools built into the app: always there, nothing to set up (docs/specs/2026-10-05-tools-and-abilities.md).</summary>
public static class BuiltIn
{
    /// <summary>The web search's name, whichever runs it: the provider (<see cref="ProviderSearch"/>) or the player's SearXNG (<see cref="WebSearch"/>).</summary>
    public const string SearchName = "web_search";

    /// <summary>Opens a web page in the player's browser. It acts outside the app, so she asks first.</summary>
    /// <param name="open">Opens the page (the app's own way of doing it).</param>
    public static Tool OpenLink(Action<Uri> open) => new(
        AIFunctionFactory.Create(
            ([Description("The page's full address, starting with https://")] string url) =>
            {
                var page = Page(url) ?? throw new ArgumentException("only web pages (addresses starting with https:// or http://) can be opened");
                open(page);
                return "Opened in the player's browser.";
            },
            "open_link",
            "Opens a web page in the player's browser: a LeetCode problem, a playlist, something they asked for. The player is asked first."),
        "Open a web page",
        OutsideApp: true,
        arguments => Page(Text(arguments, "url")) is { } page
            ? ("Open this page in your browser?", Shown(page), Site(page))
            : ("Open this?", Text(arguments, "url") ?? "", null));

    /// <summary>
    /// Prints a memo from the cabinet (see <see cref="Memo"/>): long text for the player to read at their own pace and
    /// keep. It acts inside the app, so she needs no permission.
    /// </summary>
    /// <param name="print">Prints it (the app keeps it and shows it coming out of the slot); returns what she is told.</param>
    public static Tool PrintMemo(Func<string, string, string> print) => new(
        AIFunctionFactory.Create(
            ([Description("A short title, a few words, in the player's language")] string title,
             [Description("The memo itself, in the player's language. Plain text; lists start lines with \"- \"; code goes in ``` blocks. A page at most.")] string text) =>
                print(title, text),
            "print_memo",
            "Prints a memo from the cabinet: paper the player reads at their own pace and can pin on the cabinet's side. Use it for whatever is too long " +
            "to say in your bubble: a hint written out, steps, a list, a plan, what you found, code, or when they ask for a memo. Say only the gist aloud."),
        "Print a memo",
        OutsideApp: false);

    /// <summary>
    /// Runs focus sessions on the cabinet (see <see cref="FocusSession"/>): the screen counts down and she says when each
    /// part starts and ends. It acts inside the app, so she needs no permission.
    /// </summary>
    /// <param name="act">Does it (the action, and for a start the minutes of focus and of each break, if she gave them) and says how things stand.</param>
    public static Tool Focus(Func<string, int?, int?, string> act) => new(
        AIFunctionFactory.Create(
            ([Description("start, pause, resume, skip (ends the part under way and starts the next) or stop")] string action,
             [Description("When starting: minutes of each focus part, if the player asked for a length; otherwise leave it out")] int? focus_minutes = null,
             [Description("When starting: minutes of each short break, if the player asked for a length; otherwise leave it out")] int? break_minutes = null) =>
                act(action.Trim().ToLowerInvariant(), focus_minutes, break_minutes),
            "focus_session",
            "Runs a focus session (Pomodoro) on the cabinet: focus, then a short break, a long one after every few, until stopped. The screen counts " +
            "down, you are told when each part ends, and their music can play while they focus. Use it when the player wants to focus, study or " +
            "work for a while, or asks to pause, skip or stop one. Where a session stands is in the cabinet block."),
        "Focus sessions",
        OutsideApp: false);

    /// <summary>
    /// The player's music app, through Windows' media controls: one of their saved playlists from its beginning, play
    /// (what was playing carries on), pause, skip. It acts outside the app, so she asks first.
    /// </summary>
    /// <param name="act">Does it (the action, and the playlist she named) and says what came of it.</param>
    /// <param name="playlist">The saved playlist a name means, as the card shows it (no name: the one focus starts with).</param>
    public static Tool Music(Func<string, string?, Task<string>> act, Func<string?, string> playlist) => new(
        AIFunctionFactory.Create(
            async ([Description("start_playlist (one of their saved playlists, from its beginning), play (whatever was playing carries on), pause, next or previous")] string action,
                   [Description("For start_playlist: which saved playlist, by its name as the cabinet block lists them; leave it out for the one their focus sessions start with")] string? playlist = null) =>
                await act(action.Trim().ToLowerInvariant(), string.IsNullOrWhiteSpace(playlist) ? null : playlist.Trim()),
            "music",
            "Controls the player's music app (Spotify or another, through Windows' media controls; Spotify is started if it isn't open): starts one " +
            "of their saved playlists from its beginning, plays what was playing, pauses, skips. What is playing and the playlists they saved are in " +
            "the cabinet block. The player is asked first."),
        "Control the music",
        OutsideApp: true,
        arguments => Text(arguments, "action")?.Trim().ToLowerInvariant() switch
        {
            "start_playlist" or "play_playlist" => ("Start a playlist?", playlist(Text(arguments, "playlist")), "music"),
            "pause" => ("Pause your music?", "In your music app", "music"),
            "next" or "previous" => ("Skip a track?", "In your music app", "music"),
            _ => ("Play your music?", "What was playing carries on", "music"),
        });

    /// <summary>
    /// Looks at the player's screen: a screenshot, shown to her right after this reply, of the window they were working
    /// in (at full detail, for code, an error, an exercise) or of their whole screen. It is how she helps with what they
    /// see. A screenshot can show anything, so it is on Settings → Privacy and asks first unless they allowed it; the card
    /// names the window.
    /// </summary>
    /// <param name="take">Takes it, of the whole screen or not, to be shown to her next (the app's own way of doing it); returns what she is told.</param>
    /// <param name="working">The title of the window the player was working in, for the card; null when there is none.</param>
    public static Tool LookAtScreen(Func<bool, string> take, Func<string?> working) => new(
        AIFunctionFactory.Create(
            ([Description("window (the default): the window they were working in, at full detail; screen: their whole screen")] string? part = null) =>
                take(Whole(part)),
            "look_at_screen",
            "Takes a screenshot, shown to you right after this reply: of the window the player was working in (their editor, a browser tab, " +
            "a PDF), or of their whole screen. Use it when they ask you to look at their screen or something on it, or for help with what " +
            "they're working on when you need to see it: their code, an error, an exercise, a diagram (the window titles name it; this shows " +
            "it). In this reply say only a few words (that you'll have a look); you answer once you see it. The player is asked first, unless " +
            "they allowed it."),
        "Look at your screen",
        OutsideApp: true,
        arguments => Whole(Text(arguments, "part")) || working() is not { } title
            ? ("Look at your screen?", "A screenshot of the screen you're working on, for her to see", "screen")
            : ("Look at this window?", $"A screenshot of {title}, for her to see", "screen"))
    {
        Private = true,
    };

    /// <summary>Whether she asked for the whole screen rather than the window they were working in.</summary>
    private static bool Whole(string? part) => part?.Trim().ToLowerInvariant() is "screen" or "whole" or "full" or "whole screen";

    /// <summary>
    /// Reads the titles of the player's open windows, to see what they are working on when they ask for help with it.
    /// Titles are the player's own, so it is set on Settings → Privacy; it only reads, so it needs no asking unless they want that.
    /// </summary>
    /// <param name="titles">The titles, the window in front first (with the app each belongs to).</param>
    public static Tool WindowTitles(Func<IReadOnlyList<string>> titles) => new(
        AIFunctionFactory.Create(
            () => titles() is { Count: > 0 } open
                ? "The player's open windows, the one in front first:\n" + string.Join("\n", open.Select(t => "- " + t))
                : "No windows are open.",
            "window_titles",
            "Reads the titles of the player's open windows, the one in front first: a browser shows its tab's title (\"1. Two Sum - LeetCode\"), " +
            "an editor its file, a video player its video. Use it when they ask for help with what they're working on and haven't said what it is; " +
            "not otherwise. A title names it; to see it (their code, an error), look at their screen."),
        "Read your window titles",
        OutsideApp: false)
    {
        Private = true,
    };

    /// <summary>Reads one web page's words (see <see cref="Pages"/>). It only reads, so she needs no permission.</summary>
    /// <param name="read">Reads it; throws, saying why, when it can't.</param>
    public static Tool ReadPage(Func<Uri, CancellationToken, Task<string>> read) => new(
        AIFunctionFactory.Create(
            async ([Description("The page's full address, starting with https://")] string url, CancellationToken cancel = default) =>
                await read(Page(url) ?? throw new ArgumentException("only web pages (addresses starting with https:// or http://) can be read"), cancel),
            "read_page",
            "Reads a web page's words: an article, documentation, a page from search results, a link the player gave you. A page that only fills " +
            "in with scripts in a browser comes back empty (LeetCode's problems: use leetcode_problem)."),
        "Read web pages",
        OutsideApp: false)
    {
        FromWeb = true,
    };

    /// <summary>Web search through the player's own SearXNG (an add-on, see <see cref="SearXng"/>). It only reads.</summary>
    /// <param name="search">Searches; throws, saying what to fix, when SearXNG can't.</param>
    public static Tool WebSearch(Func<string, CancellationToken, Task<string>> search) => new(
        AIFunctionFactory.Create(
            async ([Description("What to search for, as you would type it into a search engine")] string query, CancellationToken cancel = default) =>
                await search(query, cancel),
            SearchName,
            "Searches the web: for what is current or that you don't know (news, opening times, documentation, an explanation to point them to). " +
            "Gives the first results' titles, addresses and snippets; read_page reads one in full."),
        "Search the web",
        OutsideApp: false)
    {
        FromWeb = true,
    };

    /// <summary>
    /// The provider's own web search (Anthropic's): run by the provider in the middle of her reply, its results seen by
    /// her alone. Offered in place of a function, so it is never asked about.
    /// </summary>
    public static Tool ProviderSearch() => new(
        AIFunctionFactory.Create(() => "", SearchName, "Searches the web, run by the player's AI provider itself."),
        "Search the web",
        OutsideApp: false)
    {
        Hosted = new HostedWebSearchTool(),
        FromWeb = true,
    };

    /// <summary>A LeetCode problem's public data, for hints (see <see cref="LeetCodeProblems"/>). It only reads.</summary>
    /// <param name="describe">Looks it up; throws, saying why, when it can't.</param>
    public static Tool LeetCodeProblem(Func<string, CancellationToken, Task<string>> describe) => new(
        AIFunctionFactory.Create(
            async ([Description("The problem: its link, number (\"1\"), name (\"Two Sum\") or a browser tab's title (\"1. Two Sum - LeetCode\")")] string problem,
                   CancellationToken cancel = default) =>
                await describe(problem, cancel),
            "leetcode_problem",
            "Looks up a LeetCode problem: its statement, difficulty, topics and LeetCode's own hints, to help the player with it a step at a time. " +
            "To see which problem they are on, read their window titles first, if you can."),
        "Look up LeetCode problems",
        OutsideApp: false);

    /// <summary>Opens an app from the player's Start menu (see <see cref="Apps"/>). It acts outside the app, so she asks first.</summary>
    /// <param name="apps">The apps in the player's Start menu.</param>
    /// <param name="open">Opens one (the app's own way of doing it).</param>
    public static Tool OpenApp(Func<IReadOnlyList<AppShortcut>> apps, Action<AppShortcut> open) => new(
        AIFunctionFactory.Create(
            ([Description("The app's name, as their Start menu shows it (\"Visual Studio Code\", \"Anki\", \"Notion\")")] string app) =>
            {
                var (found, maybe) = Apps.Find(apps(), app);
                if (found is null)
                    return maybe.Count > 0 ? $"Several apps match: {string.Join(", ", maybe.Select(a => a.Name))}. Ask which one they mean."
                                           : $"There's no app called \"{app}\" in their Start menu.";
                open(found);
                return $"Opened {found.Name}.";
            },
            "open_app",
            "Opens an app on the player's PC from their Start menu: an editor, a study app, a notes app. Not games: those are started from the cabinet. " +
            "The player is asked first."),
        "Open an app",
        OutsideApp: true,
        arguments => Apps.Find(apps(), Text(arguments, "app") ?? "").Found is { } found ? Asked("Open this app?", found.Name, found.Name) : null);

    /// <summary>
    /// Counts something good the player did towards their play time (see <see cref="Activities"/>): she names it and,
    /// for a new one, sizes it; the player confirms on her card; the cabinet pays, or wants proof first. Inside the app.
    /// </summary>
    /// <param name="log">Logs it (name, size for a new one, minutes if they said, whether it can be photographed) and says what came of it.</param>
    public static Tool LogActivity(Func<string, Core.Size?, int?, bool?, CancellationToken, Task<string>> log) => new(
        AIFunctionFactory.Create(
            async ([Description("The activity: as the cabinet block's list names it if it is there; otherwise a few words (\"the gym\", \"a walk\", \"cooking dinner\")")] string activity,
                   [Description("Only for one not on the list yet: small (short or light: a walk, tidying up), medium (an hour or so, or hard: the gym, a class), big (half a day, or a lot of effort: a hike, a long run)")] string? size = null,
                   [Description("How long it took in minutes, if they said")] int? minutes = null,
                   [Description("Only for a new one: false if it couldn't be shown in a photo (meditating, a call home)")] bool? provable = null,
                   CancellationToken cancel = default) =>
                await log(activity, Activities.ParseSize(size), minutes, provable, cancel),
            "log_activity",
            "Counts something good the player has done towards their play time: exercise, a walk, chores, studying away from the PC, cooking, " +
            "seeing friends. It pays like a solved problem (small 1 coin, medium 2, big 4), once a day. Only when they tell you they did it, " +
            "not that they will; not for ordinary life (eating, a shower), nor what focus sessions or LeetCode already pay; and only for what " +
            "they would want more of. The player confirms on a card. Sometimes the cabinet wants proof first: the result says so."),
        "Count an activity",
        OutsideApp: false);

    /// <summary>
    /// Her verdict on a picture the player showed as proof of an activity (see <see cref="Activities.Judge"/>): the
    /// cabinet pays by how sure she is. Inside the app.
    /// </summary>
    public static Tool JudgeProof(Func<string, Conviction, string, string> judge) => new(
        AIFunctionFactory.Create(
            ([Description("The activity the picture is proof of, as it was logged")] string activity,
             [Description("sure (it clearly shows it, done today), unsure (it might, but not clearly), or doubtful (it doesn't show it, or not today)")] string verdict,
             [Description("What the picture shows, in a few words")] string saw) =>
                judge(activity, Activities.ParseConviction(verdict), saw),
            "judge_proof",
            "When the cabinet wanted proof of an activity and the player shows you a picture of it: how sure you are that it shows the activity, " +
            "done today. The cabinet pays by it: sure in full, unsure half (a clearer picture today pays the rest), doubtful nothing yet."),
        "Judge proof",
        OutsideApp: false);

    /// <summary>
    /// Sets the player's bedtime (see <see cref="Rest"/>) when they tell her one or agree to one she suggested; they
    /// confirm it on her card, and it counts from the next day. Inside the app.
    /// </summary>
    public static Tool SetBedtime(Func<string, CancellationToken, Task<string>> set) => new(
        AIFunctionFactory.Create(
            async ([Description("The bedtime, 24-hour HH:MM, from 20:00 to 03:30 (\"23:30\")")] string time, CancellationToken cancel = default) =>
                await set(time, cancel),
            "set_bedtime",
            "Sets the player's bedtime, when they tell you one or agree to one you suggested: half an hour before it you give them a word to wind " +
            "down, at it time to sleep, and play after it costs more until 04:00. The player confirms on a card; it counts from tomorrow."),
        "Set the bedtime",
        OutsideApp: false);

    private static (string What, string Detail, string? Scope)? Asked(string what, string detail, string? scope) => (what, detail, scope);

    /// <summary>A web address she may open: http or https only, never a file, a program or a script.</summary>
    public static Uri? Page(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var page) && (page.Scheme == Uri.UriSchemeHttps || page.Scheme == Uri.UriSchemeHttp) && page.Host.Length > 0
            ? page : null;

    /// <summary>The site "always" covers: its host as the browser sees it, without "www.".</summary>
    public static string Site(Uri page) => page.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.IdnHost[4..] : page.IdnHost;

    /// <summary>An address as the card shows it: no "https://", the host as the browser will see it (so a look-alike shows as one).</summary>
    public static string Shown(Uri page) => page.IdnHost + (page.IsDefaultPort ? "" : $":{page.Port}") + (page.PathAndQuery == "/" ? "" : page.PathAndQuery);

    private static string? Text(AIFunctionArguments arguments, string name) => arguments.TryGetValue(name, out var value) ? value?.ToString() : null;
}
