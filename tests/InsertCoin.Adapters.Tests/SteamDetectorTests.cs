using InsertCoin.Core;

namespace InsertCoin.Adapters.Tests;

public class SteamDetectorTests
{
    private sealed class FakeRegistry(IReadOnlyList<SteamApp>? apps) : ISteamRegistry
    {
        public IReadOnlyList<SteamApp>? ReadApps() => apps;
    }

    [Fact]
    public void Reports_only_apps_flagged_running_and_not_ignored()
    {
        var registry = new FakeRegistry([
            new("4358690", "Graveyard Keeper 2", Running: true),
            new(SteamDetector.WallpaperEngine, "Wallpaper Engine", Running: true),
            new("570", "Dota 2", Running: false),
        ]);

        var games = new SteamDetector(registry, [SteamDetector.WallpaperEngine]).RunningGames();

        Assert.Equal(new[] { new RunningGame("4358690", "Graveyard Keeper 2") }, games);
    }

    [Fact]
    public void Missing_name_gets_a_placeholder()
    {
        var games = new SteamDetector(new FakeRegistry([new("42", null, true)]), []).RunningGames();

        Assert.Equal("App 42", Assert.Single(games!).Name);
    }

    [Fact]
    public void No_steam_is_null_not_empty()
    {
        Assert.Null(new SteamDetector(new FakeRegistry(null), []).RunningGames());
    }
}
