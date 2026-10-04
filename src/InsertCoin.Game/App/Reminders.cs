using Godot;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The agenda's reminders going off. Every few seconds, any whose time has come is printed on a ticket at the screen
/// corner (always), said by her in her bubble and voice (unless she has been asked to be quiet), chimed if the player
/// chose the sound, and sent on to the other places the player picked. One that came due while the cabinet was
/// closed (or the PC asleep) goes off when it next runs, saying so. Past items leave the agenda now and then.
/// </summary>
public sealed class Reminders(MindStore store, AlertTicket ticket, AudioBank audio, Func<AppSettings> settings,
                              Action<IReadOnlyList<Plan>, DateTimeOffset> say)
{
    private DateTimeOffset _closed = DateTimeOffset.MinValue;

    /// <summary>Somewhere else a reminder goes (a Windows notification, the player's phone…); see <see cref="Elsewhere"/>.</summary>
    public event Action<IReadOnlyList<Plan>, DateTimeOffset>? Sent;

    public void Tick(DateTimeOffset now)
    {
        if (now - _closed > TimeSpan.FromHours(1))
        {
            _closed = now;
            store.ClosePast(now);
        }
        var due = Agenda.Due(store.Agenda(), now);
        if (due.Count == 0)
            return;
        foreach (var plan in due)
            store.Reminded(plan.Id, now);               // first, so a failure below never repeats it every few seconds
        GD.Print($"Reminders: {due.Count} going off");
        Show(due, now);
        if (settings().ReminderSound)
            audio.Alert("remind");
        say(due, now);
        Sent?.Invoke(due, now);
    }

    /// <summary>Goes off as if due now: the settings window's "Try it", and dev actions.</summary>
    public void Try(string text, DateTimeOffset now)
    {
        var plan = new Plan(0, text, now, false, true, null, now, null);
        Show([plan], now);
        if (settings().ReminderSound)
            audio.Alert("remind");
        say([plan], now);
        Sent?.Invoke([plan], now);
    }

    private void Show(IReadOnlyList<Plan> due, DateTimeOffset now)
    {
        var missed = due.All(p => now - p.At > Agenda.Missed);
        var first = due[0];
        var when = first.At.Date == now.Date ? first.At.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : first.Label;
        var more = due.Count > 1 ? $"  (+{due.Count - 1} more)" : "";
        ticket.Show(missed ? "MISSED" : "REMINDER", $"{when}  {first.Text}{more}", seconds: 12, tall: true);
    }
}
