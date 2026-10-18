using System.Text.Json.Serialization;
using InsertCoin.Adapters;

namespace InsertCoin.Game.App;

/// <summary>User preferences, saved next to the ledger state in %APPDATA%\InsertCoin.</summary>
public sealed record AppSettings
{
    public string? Username { get; init; }
    public string? CallMe { get; init; }                // the name she calls the player by (Getting started); unset: none, not the LeetCode name
    public string[] StartLater { get; init; } = [];     // Getting started's steps put off for later, by page
    public bool StartHidden { get; init; }              // Getting started hidden: out of Settings and the tray
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
    public InsertCoin.Mind.Elsewhere ReminderElsewhere { get; init; } = new();   // where her texts go while the player is away (secrets apart, encrypted)
    public int AwayMinutes { get; init; } = InsertCoin.Mind.Reaching.AwayMinutes;   // no input this long and the player is away (2 to 30)
    public bool AlsoAtThePc { get; init; }              // reminders reach the phone too while the player is at the PC, as a plain line (the old way)
    public InsertCoin.Mind.Reach Reach { get; init; } = InsertCoin.Mind.Reach.LikeARoommate;   // how much she reaches out to their phone
    public string Companion { get; init; } = "shino";   // character folder under res://characters
    public bool ShowCompanion { get; init; } = true;    // false leaves the stool empty
    public Dictionary<string, InsertCoin.Game.Companion.Manner> Manners { get; init; } = new() { ["fumiriya"] = InsertCoin.Game.Companion.Manner.Relaxed };   // how each sits, by character; unset: neat
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
    public Privacy Privacy { get; init; } = new();      // what she is told in the background (Settings → Privacy; what she looks at is in ToolPermissions)
    public AddOns AddOns { get; init; } = new();        // optional extras the player turned on (Settings → Add-ons)
    public string[] SkillFolders { get; init; } = [];   // skills folders elsewhere the player linked, read where they are (Settings → Skills)
    public string[] SkillsOff { get; init; } = [];      // skills switched off, by their folder; the rest are on
    public Dictionary<string, string[]> SkillGrants { get; init; } = [];   // the folders each skill works in, by skill as SkillsOff names them (granted on its page)
    public string[] CrtApps { get; init; } = ["focus", "done", "music", "memos", "agenda"];      // the order of the screen's apps, as the player arranged them
    public int FocusMinutes { get; init; } = 25;        // a focus session's parts (the screen's FOCUS app)
    public int BreakMinutes { get; init; } = 5;
    public int LongBreakMinutes { get; init; } = 15;
    public int LongBreakEvery { get; init; } = 4;       // focus parts before the long break
    public bool FocusMusic { get; init; }               // their music plays while focusing, and pauses at breaks
    public string? Playlist { get; init; }              // one link, kept before playlists could be saved by name: moved into Playlists
    public SavedPlaylist[] Playlists { get; init; } = [];        // playlists saved in the MUSIC app, with the names Spotify gives them
    public string? FocusPlaylist { get; init; }         // the link of the one a focus session starts with; unset: what played last carries on
    public int StandUpMinutes { get; init; } = 50;      // she suggests standing up and some water after this long at the PC; 0: never
    public bool SetUp { get; init; }                    // the first-run setup is done (the LeetCode name in it is optional)
    public int WeekdayCapMinutes { get; init; } = 90;   // the most play a day can earn, Monday to Thursday…
    public int FridayCapMinutes { get; init; } = 120;   // …on a Friday…
    public int WeekendCapMinutes { get; init; } = 150;  // …and on Saturday and Sunday (a change counts from the next day)
    public string? Bedtime { get; init; }               // "23:30": play after it costs more until 04:00; unset: none
    public int FocusPerPlay { get; init; } = 4;         // minutes of focus for each minute of play
    public string[] UnseeingModels { get; init; } = [];  // models found not to take pictures or PDFs: none is sent to them
    public string? VoiceEngine { get; init; }           // the folder of the player's GPT-SoVITS (its Windows package)
    public string? EngineInstallTo { get; init; }       // where they last chose to have GPT-SoVITS installed (a download there carries on)
    public string[] VoicesGiven { get; init; } = [];     // built-in characters given their own voice, once: one the player removed stays removed
    public double? TrainingPace { get; init; }          // how this PC trained a voice against the estimate (see VoiceTraining.Pace)
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }

    /// <summary><see cref="ReminderLeadMinutes"/> as a span of time, or null.</summary>
    [JsonIgnore]
    public TimeSpan? ReminderLead => ReminderLeadMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null;
}

/// <summary>What she is told in the background, as the player chose (Settings → Privacy): all of it goes to their AI provider.</summary>
public sealed record Privacy
{
    public bool Music { get; init; } = true;            // what is playing in their music app (track and artist)
    public bool Days { get; init; } = true;             // what the record of days shows: nights going on late, days without play
}

/// <summary>Optional extras, off until the player turns them on (Settings → Add-ons).</summary>
public sealed record AddOns
{
    public bool SearXng { get; init; }                  // she searches the web through the player's own SearXNG, in place of the provider's search
    public string SearXngAddress { get; init; } = "http://localhost:8888";

    /// <summary>The SearXNG she searches through, or null when the add-on is off (or its address isn't one).</summary>
    [JsonIgnore]
    public Uri? Search => SearXng ? Mind.SearXng.Parse(SearXngAddress) : null;
}

/// <summary>A playlist saved in the screen's MUSIC app: its link (Spotify, YouTube Music…), and its name as Spotify shows it.</summary>
public sealed record SavedPlaylist(string Link, string Name);

/// <summary>
/// How reminders reach the player in the cabinet (Settings → Reminders), as chosen together. Where else they go is
/// apart (Settings → Your phone), its addresses and tokens encrypted.
/// </summary>
public sealed record ReminderChoices(bool Spoken, bool Sound, bool ChimeWhenMuted, bool Windows, int TicketSeconds, int SnoozeMinutes, int? LeadMinutes)
{
    public static ReminderChoices Of(AppSettings s) =>
        new(s.ReminderSpoken, s.ReminderSound, s.ReminderChimeWhenMuted, s.ReminderWindows, s.ReminderTicketSeconds, s.ReminderSnoozeMinutes, s.ReminderLeadMinutes);

    public AppSettings Into(AppSettings s) => s with
    {
        ReminderSpoken = Spoken, ReminderSound = Sound, ReminderChimeWhenMuted = ChimeWhenMuted, ReminderWindows = Windows,
        ReminderTicketSeconds = TicketSeconds, ReminderSnoozeMinutes = SnoozeMinutes, ReminderLeadMinutes = LeadMinutes,
    };
}

/// <summary>Her asking how things went, the hours she doesn't, and her suggesting standing up (Settings → Check-ins), as chosen together.</summary>
public sealed record CheckInChoices(string CheckIns, int QuietFrom, int QuietUntil, int StandUpMinutes)
{
    public static CheckInChoices Of(AppSettings s) => new(s.CheckIns, s.QuietFrom, s.QuietUntil, s.StandUpMinutes);

    public AppSettings Into(AppSettings s) => s with { CheckIns = CheckIns, QuietFrom = QuietFrom, QuietUntil = QuietUntil, StandUpMinutes = StandUpMinutes };
}

/// <summary>The settings as saved in settings.json, shared by the parts of the app that change them: each change is saved at once.</summary>
public sealed class SettingsFile
{
    private readonly JsonFileStore<AppSettings> store;

    /// <summary>A test run's defaults keep it muted: a test never makes a sound, even on settings it couldn't read.</summary>
    public SettingsFile(JsonFileStore<AppSettings> store, bool testRun = false)
    {
        this.store = store;
        var there = File.Exists(store.Path);
        var loaded = store.Load();
        Unreadable = there && loaded is null;
        Now = loaded ?? new AppSettings { Muted = testRun };
    }

    public AppSettings Now { get; private set; }

    /// <summary>settings.json was there but couldn't be read (nor its backup): it was set aside and the defaults used.</summary>
    public bool Unreadable { get; }

    /// <summary>A change was saved, by any part of the app: for what shows a setting in two places (the tray's Voice, and Settings).</summary>
    public event Action? Changed;

    public void Save(AppSettings settings)
    {
        Now = settings;
        store.Save(settings);
        Changed?.Invoke();
    }
}
