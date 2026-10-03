using InsertCoin.Adapters;

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
    public bool SpeechBubbles { get; init; } = true;    // false: she still reacts, but says nothing
    public bool EnergySaver { get; init; }              // draw at a low frame rate all the time, not only while a game runs
    public string? ChatProtocol { get; init; }          // OpenAi or Anthropic: how to talk to the user's provider
    public string? ChatBaseUrl { get; init; }           // the provider's server; the API key is stored apart, encrypted
    public string? ChatModel { get; init; }
    public string? MemorySearch { get; init; }          // a local model's name, "words" for shared words only; unset: the best one present
    public bool Voice { get; init; } = true;            // false: she stays text-only even with a voice
    public string TalkKey { get; init; } = "RightCtrl"; // held in any app to talk to her aloud (see App.TalkKey), or "Off"
    public string? VoiceEngine { get; init; }           // the folder of the player's GPT-SoVITS (its Windows package)
    public double? TrainingPace { get; init; }          // how this PC trained a voice against the estimate (see VoiceTraining.Pace)
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
}

/// <summary>The settings as saved in settings.json, shared by the parts of the app that change them: each change is saved at once.</summary>
public sealed class SettingsFile(JsonFileStore<AppSettings> store)
{
    public AppSettings Now { get; private set; } = store.Load() ?? new AppSettings();

    public void Save(AppSettings settings)
    {
        Now = settings;
        store.Save(settings);
    }
}
