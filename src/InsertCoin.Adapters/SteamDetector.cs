using System.Runtime.Versioning;
using InsertCoin.Core;
using Microsoft.Win32;

namespace InsertCoin.Adapters;

public sealed record SteamApp(string AppId, string? Name, bool Running);

/// <summary>Reads Steam's per-app entries under HKCU\Software\Valve\Steam\Apps. Null when Steam isn't installed.</summary>
public interface ISteamRegistry
{
    IReadOnlyList<SteamApp>? ReadApps();
}

/// <summary>
/// Uses the per-app Running flag rather than RunningAppID or process scanning: it tracks several apps at once and
/// is 0 for tools like Wallpaper Engine that run from steamapps\common outside Steam's play tracking.
/// </summary>
public sealed class SteamDetector(ISteamRegistry registry, IEnumerable<string> ignoredAppIds) : IGameDetector
{
    public const string WallpaperEngine = "431960";

    private readonly HashSet<string> _ignored = [.. ignoredAppIds];

    public IReadOnlyList<RunningGame>? RunningGames() =>
        registry.ReadApps()?
            .Where(app => app.Running && !_ignored.Contains(app.AppId))
            .Select(app => new RunningGame(app.AppId, app.Name ?? $"App {app.AppId}"))
            .ToList();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSteamRegistry : ISteamRegistry
{
    private const string AppsKey = @"Software\Valve\Steam\Apps";

    public IReadOnlyList<SteamApp>? ReadApps()
    {
        using var apps = Registry.CurrentUser.OpenSubKey(AppsKey);
        if (apps is null)
            return null;

        var result = new List<SteamApp>();
        foreach (var id in apps.GetSubKeyNames())
        {
            using var app = apps.OpenSubKey(id);
            if (app is null)
                continue;
            result.Add(new SteamApp(id, app.GetValue("Name") as string, app.GetValue("Running") is int running && running != 0));
        }
        return result;
    }
}
