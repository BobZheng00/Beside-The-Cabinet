namespace InsertCoin.Mind;

/// <summary>An app the player has: its name as the Start menu shows it, and the shortcut that starts it.</summary>
public sealed record AppShortcut(string Name, string Shortcut);

/// <summary>
/// Finding the app she was asked to open (the open_app tool) among the player's Start menu shortcuts, by the name they
/// used: the same name, else the one name that starts with it, else the one that contains it. Games are started from the
/// cabinet, not by her; uninstallers and help pages aren't apps.
/// </summary>
public static class Apps
{
    private static readonly string[] NotApps = ["uninstall", "卸载", "readme", "read me", "help", "manual", "documentation", "release notes", "website", "license"];

    /// <summary>Whether a Start menu shortcut's name is an app to open (not an uninstaller, a help page or the like).</summary>
    public static bool Useful(string name) => !NotApps.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>The app <paramref name="asked"/> means, or, when it is unclear, the ones it might mean (none: no app is called that).</summary>
    public static (AppShortcut? Found, IReadOnlyList<AppShortcut> Maybe) Find(IReadOnlyList<AppShortcut> apps, string asked)
    {
        var wanted = Plain(asked);
        if (wanted.Length == 0)
            return (null, []);
        var named = apps.GroupBy(a => Plain(a.Name)).Select(g => g.First()).ToList();          // the same app in two Start menus is one
        foreach (var match in new Func<string, bool>[] { n => n == wanted, n => n.StartsWith(wanted, StringComparison.Ordinal), n => n.Contains(wanted, StringComparison.Ordinal) })
        {
            var found = named.Where(a => match(Plain(a.Name))).ToList();
            if (found.Count == 1)
                return (found[0], []);
            if (found.Count > 1)
                return (null, [.. found.OrderBy(a => a.Name.Length).Take(5)]);
        }
        return (null, []);
    }

    /// <summary>A name to compare: lower case, letters and digits only ("Visual Studio Code" and "visual-studio code" are one).</summary>
    private static string Plain(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
