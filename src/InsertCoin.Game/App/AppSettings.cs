namespace InsertCoin.Game.App;

/// <summary>User preferences, saved next to the ledger state in %APPDATA%\InsertCoin.</summary>
public sealed record AppSettings
{
    public string? Username { get; init; }
    public string Initials { get; init; } = "AAA";
    public string Skin { get; init; } = "cozy";
    public string[] IgnoredSteamAppIds { get; init; } = ["431960"];   // Wallpaper Engine
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
}
