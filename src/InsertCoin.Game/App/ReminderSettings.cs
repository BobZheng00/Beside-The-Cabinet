using Godot;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Reminders: how a reminder reaches the player in the cabinet (the ticket at the screen corner, how long it
/// stays and how long a snooze lasts, her telling them in her own words, a chime even when muted, a Windows
/// notification), and a reminder before every timed thing on the agenda. Where else reminders go is on Your phone (see
/// <see cref="PhoneSettings"/>). Each change is used at once; "Try it" sends a reminder everywhere one would go.
/// </summary>
public partial class ReminderSettings : VBoxContainer
{
    private static readonly (string Label, int Seconds)[] Stays = [("15 seconds", 15), ("1 minute", 60), ("5 minutes", 300), ("Until I close it", 0)];
    private static readonly int[] Snoozes = [5, 10, 15, 30, 60];
    private static readonly (string Label, int? Minutes)[] Leads =
        [("Only when I ask", null), ("5 min before", 5), ("10 min before", 10), ("15 min before", 15), ("30 min before", 30), ("1 hour before", 60)];

    /// <summary>The choices as they are now.</summary>
    public event Action<ReminderChoices>? Chosen;

    /// <summary>"Try it": a reminder goes off now, everywhere one would; what came of it, to show, and whether all of it went.</summary>
    public Func<Task<(string Said, bool Fine)>> TryIt { get; set; } = () => Task.FromResult(("", true));

    private CheckBox _spoken = null!, _sound = null!, _evenMuted = null!, _windows = null!;
    private OptionButton _stays = null!, _snooze = null!, _lead = null!;
    private Label _result = null!;

    public void Build(ReminderChoices choices)
    {
        Form.Page(this, "Reminders");
        Form.Hint(this, "Ask for a reminder and it goes off at that time, always on a ticket at the corner of the screen, " +
                        "with Snooze and OK. While you're away from the PC, it reaches your phone instead (see Your phone).");
        var ticket = Form.Grid(this);
        _stays = Form.Choice(ticket, "Ticket stays", Stays.Select(s => s.Label), Array.FindIndex(Stays, s => s.Seconds == choices.TicketSeconds), _ => Changed());
        _snooze = Form.Choice(ticket, "Snooze for", Snoozes.Select(m => $"{m} minutes"), Array.IndexOf(Snoozes, choices.SnoozeMinutes), _ => Changed());

        _spoken = Tick("Told in your companion's own words", choices.Spoken,
                       "Once your companion has finished speaking, and in your language, when the mind is connected. Otherwise a plain line in the speech bubble.");
        _sound = Tick("Chime", choices.Sound, "A soft two-note chime, like a doorbell.");
        _evenMuted = Form.Tick(Form.Indented(this), "Even when the cabinet's sounds are muted", choices.ChimeWhenMuted, _ => Changed());
        Form.ShowWhile(_sound, _evenMuted.GetParent<Control>());
        _windows = Tick("Windows notification", choices.Windows, "Also kept in Windows' notification centre, so you see it when you come back.");

        Form.Subheading(this, "Before things on your agenda");
        _lead = Form.Choice(Form.Grid(this), "Remind me", Leads.Select(l => l.Label), Math.Max(0, Array.FindIndex(Leads, l => l.Minutes == choices.LeadMinutes)), _ => Changed());
        Form.Hint(this, "For every timed item on your agenda without a reminder of its own: an interview, a call, a class.");

        var test = Form.Buttons(this);
        Form.Button(test, "Try it", () => _ = Try());
        _result = Form.Result(test);
    }

    /// <summary>Dev: as if "Try it" were clicked.</summary>
    public void DevTry() => _ = Try();

    private CheckBox Tick(string label, bool on, string hint)
    {
        var tick = Form.Tick(this, label, on, _ => Changed());
        Form.Hint(this, hint);
        return tick;
    }

    private void Changed() => Chosen?.Invoke(new ReminderChoices(
        _spoken.ButtonPressed, _sound.ButtonPressed, _evenMuted.ButtonPressed, _windows.ButtonPressed, Stays[_stays.Selected].Seconds,
        Snoozes[_snooze.Selected], Leads[_lead.Selected].Minutes));

    private async Task Try()
    {
        Form.Say(_result, "Sending…", Paper.Faint);
        var (said, fine) = await TryIt();
        if (IsInstanceValid(this))
            Form.Say(_result, said, fine ? Paper.Good : Paper.Bad);
    }
}
