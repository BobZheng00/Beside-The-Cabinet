namespace InsertCoin.Game.App;

/// <summary>User preferences, saved next to the ledger state in %APPDATA%\InsertCoin.</summary>
public sealed record AppSettings
{
    public string? Username { get; init; }
    public string Initials { get; init; } = "AAA";
    public string Skin { get; init; } = "cozy";
    public string[] IgnoredSteamAppIds { get; init; } = ["431960"];   // Wallpaper Engine
    public bool AlwaysOnTop { get; init; }
    public string Size { get; init; } = "M";          // S, M or L
    public bool Muted { get; init; }
    public bool InGameTickets { get; init; } = true;
    public string Companion { get; init; } = "shino";   // character folder under res://characters
    public bool ShowCompanion { get; init; } = true;    // false leaves the stool empty
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
}
