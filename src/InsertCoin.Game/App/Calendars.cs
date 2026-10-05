using System.Runtime.Versioning;
using System.Text.Json;
using Godot;
using InsertCoin.Mind;
using HttpClient = System.Net.Http.HttpClient;

namespace InsertCoin.Game.App;

/// <summary>
/// One calendar the player linked: what they call it, how it is read ("link": a private iCalendar address; signing
/// in with Google or Microsoft would be other kinds), and the address, which is a secret (anyone with it can read
/// the calendar): kept encrypted with the rest of the list, never in settings.json.
/// </summary>
public sealed record CalendarLink(string Name, string Kind, string Url);

/// <summary>What the last read of a calendar came to.</summary>
public sealed record CalendarStatus(DateTimeOffset At, int Events, string? Error);

/// <summary>
/// The player's calendars on the agenda: each read every fifteen minutes (and when linked), its events for the next
/// two weeks put on the agenda and kept in step (see <see cref="CalendarSync"/>). They get the player's reminder
/// before timed items like anything else, and she knows what is coming up; she does not ask how each meeting went.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Calendars(MindStore store, KeyStore keys)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Dictionary<string, CalendarStatus> _status = [];
    private bool _syncing;

    /// <summary>A calendar was read (or failed to be): the window showing them can say so.</summary>
    public event Action? Changed;

    public IReadOnlyList<CalendarLink> Links
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<List<CalendarLink>>(keys.Load() ?? "[]") ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    public CalendarStatus? Status(string name) => _status.GetValueOrDefault(name);

    /// <summary>Links a calendar (replacing one of the same name) and reads it now.</summary>
    public void Add(CalendarLink link)
    {
        Save([.. Links.Where(l => l.Name != link.Name), link]);
        _ = Sync();
    }

    /// <summary>Unlinks a calendar; its events leave the agenda.</summary>
    public void Remove(string name)
    {
        Save([.. Links.Where(l => l.Name != name)]);
        store.DeletePlansFrom(name);
        _status.Remove(name);
        Changed?.Invoke();
    }

    /// <summary>Reads every linked calendar and brings the agenda in step; one failing (offline, say) leaves its items as they were.</summary>
    public async Task Sync()
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            foreach (var link in Links)
            {
                var now = DateTimeOffset.Now;
                var from = now.AddDays(-1);                 // yesterday's are kept as they were, for "how did it go" and "just past"
                try
                {
                    // a private link ("link"); signing in with Google or Microsoft would be another source, chosen by its kind
                    ICalendarSource source = new IcsCalendar(Http, link.Name, link.Url);
                    var events = await source.Read(from, now + CalendarSync.Ahead);
                    var (added, changed, removed) = CalendarSync.Apply(store, link.Name, events, from, now);
                    _status[link.Name] = new CalendarStatus(now, events.Count, null);
                    GD.Print($"Calendars: {link.Name}: {events.Count} events in the next two weeks ({added} new, {changed} changed, {removed} gone)");
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException or InvalidOperationException or UriFormatException)
                {
                    _status[link.Name] = new CalendarStatus(now, 0, e.GetBaseException().Message);
                    GD.PrintErr($"Calendars: {link.Name} could not be read: {e.GetBaseException().Message}");   // never the address: it is a secret
                }
            }
        }
        finally
        {
            _syncing = false;
            Changed?.Invoke();
        }
    }

    private void Save(IReadOnlyList<CalendarLink> links) => keys.Save(links.Count == 0 ? null : JsonSerializer.Serialize(links));
}
