using Godot;
using InsertCoin.Game.Desktop;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// The agenda's reminders going off. Every few seconds, any whose time has come is printed on a ticket at the screen
/// corner (always; with Snooze and OK), chimed if the player chose the sound, passed to her to tell them (said once she
/// has finished what she is saying, or texted to their phone while they are away), and kept in Windows' notification
/// centre if they chose. One that came due while the cabinet was closed (or the PC asleep) goes off when it next runs,
/// saying so. A repeating item moves on to its next time; past items leave the agenda now and then.
/// </summary>
public sealed class Reminders(MindStore store, AlertTicket ticket, AudioBank audio, Func<AppSettings> settings,
                              Action<IReadOnlyList<Plan>, DateTimeOffset> say)
{
    private DateTimeOffset _closed = DateTimeOffset.MinValue;

    /// <summary>A reminder went off: for Windows' notification centre, if the player chose it (their phone hears of it through her).</summary>
    public event Action<Notice>? Sent;

    /// <summary>The player's "remind me before things on my agenda", or null.</summary>
    private TimeSpan? Lead => settings().ReminderLead;

    public void Tick(DateTimeOffset now)
    {
        if (now - _closed > TimeSpan.FromHours(1))
        {
            _closed = now;
            store.ClosePast(now, TimeZoneInfo.Local);
        }
        var due = Agenda.Due(store.Agenda(), now, Lead);
        if (due.Count == 0)
            return;
        foreach (var plan in due)                           // first, so a failure below never repeats it every few seconds
            if (plan.Repeat == Repeat.Once)
                store.Reminded(plan.Id, now);
            else
                store.Advance(plan.Id, Agenda.NextAfter(plan, now, Lead, TimeZoneInfo.Local));
        GD.Print($"Reminders: {due.Count} going off");
        GoOff(due, now, settings().ReminderSound, elsewhere: true);
    }

    /// <summary>
    /// Goes off as if due now (dev actions). With <paramref name="chime"/> (the settings window's "Try it", which
    /// sends elsewhere itself, as filled in), only in the cabinet, chiming or not as given.
    /// </summary>
    public void Try(string text, DateTimeOffset now, bool? chime = null) =>
        GoOff([new Plan(0, text, now, false, TimeSpan.Zero, null, now, null)], now, chime ?? settings().ReminderSound, elsewhere: chime is null);

    private void GoOff(IReadOnlyList<Plan> due, DateTimeOffset now, bool chime, bool elsewhere)
    {
        Show(due, now);
        if (chime)
            audio.Chime(settings().ReminderChimeWhenMuted);
        say(due, now);
        if (elsewhere)
            Sent?.Invoke(NoticeFor(due, now, Lead));
    }

    /// <summary>What goes out of the cabinet: "Reminder" (or "Missed reminder") and each one with its time, a line each.</summary>
    public static Notice NoticeFor(IReadOnlyList<Plan> due, DateTimeOffset now, TimeSpan? lead = null)
    {
        var title = (Missed(due, now, lead) ? "Missed reminder" : "Reminder") + (due.Count > 1 ? "s" : "");
        return new Notice(title, string.Join("\n", due.Select(p => $"{When(p, now)}  {p.Text}")));
    }

    /// <summary>All of them came due a while ago: while the cabinet was closed.</summary>
    private static bool Missed(IReadOnlyList<Plan> due, DateTimeOffset now, TimeSpan? lead) =>
        due.All(p => now - (p.RemindAt(lead) ?? p.At) > Agenda.Missed);

    private static string When(Plan plan, DateTimeOffset now) =>
        plan.At.Date == now.Date && !plan.AllDay ? plan.At.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : plan.Label;

    private void Show(IReadOnlyList<Plan> due, DateTimeOffset now)
    {
        var more = due.Count > 1 ? $"  (+{due.Count - 1} more)" : "";
        var snooze = settings().ReminderSnoozeMinutes;
        ticket.Show(Missed(due, now, Lead) ? "MISSED" : "REMINDER", $"{When(due[0], now)}  {due[0].Text}{more}", seconds: settings().ReminderTicketSeconds,
                    buttons: [($"Snooze {snooze} min", () => Snooze(due, snooze)), ("OK", () => { })]);
    }

    /// <summary>Each one again in a while: a one-off reminder, saying the same.</summary>
    private void Snooze(IReadOnlyList<Plan> due, int minutes)
    {
        var now = DateTimeOffset.Now;
        foreach (var plan in due)
            store.AddPlan(plan.Text, now.AddMinutes(minutes), allDay: false, TimeSpan.Zero, now, plan.By);
        GD.Print($"Reminders: {due.Count} snoozed for {minutes} min");
    }
}
