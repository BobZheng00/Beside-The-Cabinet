using System.Text.Json.Serialization;
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
    public bool ReminderSound { get; init; }            // reminders chime too (the ticket and her bubble always show)
    public bool ReminderChimeWhenMuted { get; init; } = true;    // the chime even with "Mute sounds" on: it was asked for
    public bool ReminderSpoken { get; init; } = true;   // she tells you in her own words (and voice) when her mind is connected
    public int ReminderTicketSeconds { get; init; } = 60;        // how long the reminder ticket stays; 0: until closed
    public int ReminderSnoozeMinutes { get; init; } = 10;
    public int? ReminderLeadMinutes { get; init; }      // a reminder this long before every timed item on the agenda; unset: only those asked for
    public Dictionary<string, InsertCoin.Mind.Prices> ChatPrices { get; init; } = [];   // per model, per million tokens, as the player's provider charges
    public string Currency { get; init; } = "$";
    public string CheckIns { get; init; } = "Gentle";   // she asks how things went: Off, Gentle (one every three hours at most) or Chatty
    public int QuietFrom { get; init; } = 22;           // no check-ins from this hour…
    public int QuietUntil { get; init; } = 8;           // …until this one
    public bool ReminderWindows { get; init; }          // reminders also raise a Windows notification
    public InsertCoin.Mind.Elsewhere ReminderElsewhere { get; init; } = new();   // and go to the phone or another app (secrets apart, encrypted)
    public string Companion { get; init; } = "shino";   // character folder under res://characters
    public bool ShowCompanion { get; init; } = true;    // false leaves the stool empty
    public bool SpeechBubbles { get; init; } = true;    // false: she still reacts, but says nothing
    public bool EnergySaver { get; init; }              // draw at a low frame rate all the time, not only while a game runs
    public string? ChatProtocol { get; init; }          // OpenAi or Anthropic: how to talk to the user's provider
    public string? ChatBaseUrl { get; init; }           // the provider's server; the API key is stored apart, encrypted
    public string? ChatModel { get; init; }
    public string? MemorySearch { get; init; }         // a local model's name, "words" for shared words only; unset: the best one present
    public int? ChatWindow { get; init; }               // tokens of the conversation she keeps word for word; unset: what suits the provider
    public bool Voice { get; init; } = true;            // false: she stays text-only even with a voice
    public string TalkKey { get; init; } = "RightCtrl"; // held in any app to talk to her aloud (see App.TalkKey), or "Off"
    public InsertCoin.Mind.HandsFree HandsFree { get; init; } = new();    // when she listens without the talk key: off until chosen
    public Dictionary<string, InsertCoin.Mind.Permission> ToolPermissions { get; init; } = [];   // by tool, or "tool:site" allowed always; unset: its standard
    public bool ToolsAuto { get; init; }                // auto: she goes ahead where she would ask first (tools switched off stay off)
    public string[] CrtApps { get; init; } = ["memos", "agenda"];      // the order of the screen's apps, as the player arranged them
    public string? VoiceEngine { get; init; }           // the folder of the player's GPT-SoVITS (its Windows package)
    public double? TrainingPace { get; init; }          // how this PC trained a voice against the estimate (see VoiceTraining.Pace)
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }

    /// <summary><see cref="ReminderLeadMinutes"/> as a span of time, or null.</summary>
    [JsonIgnore]
    public TimeSpan? ReminderLead => ReminderLeadMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null;
}

/// <summary>How reminders reach the player (tray → Reminders…), as chosen together; addresses and tokens are kept apart, encrypted.</summary>
public sealed record ReminderChoices(bool Spoken, bool Sound, bool ChimeWhenMuted, bool Windows, int TicketSeconds, int SnoozeMinutes, int? LeadMinutes,
                                     InsertCoin.Mind.Elsewhere Elsewhere, string CheckIns, int QuietFrom, int QuietUntil)
{
    public static ReminderChoices Of(AppSettings s) =>
        new(s.ReminderSpoken, s.ReminderSound, s.ReminderChimeWhenMuted, s.ReminderWindows, s.ReminderTicketSeconds, s.ReminderSnoozeMinutes,
            s.ReminderLeadMinutes, s.ReminderElsewhere, s.CheckIns, s.QuietFrom, s.QuietUntil);

    public AppSettings Into(AppSettings s) => s with
    {
        ReminderSpoken = Spoken, ReminderSound = Sound, ReminderChimeWhenMuted = ChimeWhenMuted, ReminderWindows = Windows,
        ReminderTicketSeconds = TicketSeconds, ReminderSnoozeMinutes = SnoozeMinutes, ReminderLeadMinutes = LeadMinutes, ReminderElsewhere = Elsewhere,
        CheckIns = CheckIns, QuietFrom = QuietFrom, QuietUntil = QuietUntil,
    };
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
