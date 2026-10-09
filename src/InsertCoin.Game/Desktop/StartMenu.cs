using Godot;
using Environment = System.Environment;
using InsertCoin.Game.Companion;
using InsertCoin.Mind;

namespace InsertCoin.Game.Desktop;

/// <summary>
/// The apps in the player's Start menu (theirs and every user's), for her to open when asked: shortcuts only (a game's
/// Steam link is a .url, left out), without uninstallers and help pages. Read again after a few minutes.
/// </summary>
public static class StartMenu
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);
    private static IReadOnlyList<AppShortcut> _apps = [];
    private static DateTimeOffset _read;

    public static IReadOnlyList<AppShortcut> Apps()
    {
        if (DateTimeOffset.Now - _read < Fresh)
            return _apps;
        var found = new List<AppShortcut>();
        foreach (var start in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
        {
            var programs = Path.Combine(Environment.GetFolderPath(start), "Programs");
            if (!Directory.Exists(programs))
                continue;
            foreach (var shortcut in Directory.EnumerateFiles(programs, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                if (Path.GetFileNameWithoutExtension(shortcut) is var name && Mind.Apps.Useful(name))
                    found.Add(new AppShortcut(name, shortcut));
        }
        _read = DateTimeOffset.Now;
        return _apps = found;
    }

    /// <summary>Starts an app through its shortcut, as the Start menu would. A test run only says so.</summary>
    public static void Open(AppShortcut app)
    {
        if (Paper.OffScreen)
        {
            GD.Print($"Tools: would open {app.Name} ({app.Shortcut}) (a test run opens nothing)");
            return;
        }
        OS.ShellOpen(app.Shortcut);
    }
}
