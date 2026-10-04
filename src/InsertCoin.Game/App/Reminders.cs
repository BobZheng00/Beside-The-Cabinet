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

    /// <summary>
    /// Goes off as if due now (dev actions). With <paramref name="chime"/> (the settings window's "Try it", which
    /// sends elsewhere itself, as filled in), only in the cabinet, chiming or not as given.
    /// </summary>
    public void Try(string text, DateTimeOffset now, bool? chime = null)
    {
        var plan = new Plan(0, text, now, false, true, null, now, null);
        Show([plan], now);
        if (chime ?? settings().ReminderSound)
            audio.Alert("remind");
        say([plan], now);
        if (chime is null)
            Sent?.Invoke([plan], now);
    }

    /// <summary>What goes out of the cabinet: "Reminder" (or "Missed reminder") and each one with its time, a line each.</summary>
    public static Notice NoticeFor(IReadOnlyList<Plan> due, DateTimeOffset now)
    {
        var missed = due.All(p => now - p.At > Agenda.Missed);
        var title = (missed ? "Missed reminder" : "Reminder") + (due.Count > 1 ? "s" : "");
        return new Notice(title, string.Join("\n", due.Select(p => $"{When(p, now)}  {p.Text}")));
    }

    private static string When(Plan plan, DateTimeOffset now) =>
        plan.At.Date == now.Date ? plan.At.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : plan.Label;

    private void Show(IReadOnlyList<Plan> due, DateTimeOffset now)
    {
        var missed = due.All(p => now - p.At > Agenda.Missed);
        var more = due.Count > 1 ? $"  (+{due.Count - 1} more)" : "";
        ticket.Show(missed ? "MISSED" : "REMINDER", $"{When(due[0], now)}  {due[0].Text}{more}", seconds: 12, tall: true);
    }
}
