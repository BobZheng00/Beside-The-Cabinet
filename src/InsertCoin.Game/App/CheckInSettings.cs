using Godot;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Notifications → Check-ins: their asking how something went once the player is back and free, and how
/// often; and their suggesting standing up after a long stretch at the PC. Each change is used at once.
/// </summary>
public partial class CheckInSettings : VBoxContainer
{
    private static readonly (string Label, string Mode)[] Modes =
        [("Off", "Off"), ("Gentle: one every three hours at most", "Gentle"), ("Chatty: whenever there is something", "Chatty")];
    private static readonly (string Label, int Minutes)[] StandUps =
        [("Never", 0), ("After 30 min at the PC", 30), ("After 45 min", 45), ("After 50 min", 50), ("After 1 hour", 60), ("After 90 min", 90)];

    /// <summary>The choices as they are now.</summary>
    public event Action<CheckInChoices>? Chosen;

    private OptionButton _mode = null!, _standUp = null!;

    public void Build(CheckInChoices choices)
    {
        Form.Page(this);
        Form.Subheading(this, "How did it go?");
        Form.Hint(this, "After something you mentioned (not a reminder, not your calendar's entries), your companion asks how it went, once you are " +
                        "back at the PC and free: not in a call, not in a full-screen game or presentation, not when Windows is holding " +
                        "notifications back. A meeting that runs over keeps the call going, so the question waits.");
        _mode = Form.Choice(Form.Grid(this), "Asking", Modes.Select(m => m.Label), Array.FindIndex(Modes, m => m.Mode == choices.CheckIns), _ => Changed());

        Form.Subheading(this, "Stand up, have some water");
        Form.Hint(this, "After a long stretch at the PC with no real break (five minutes away resets it), your companion suggests standing up and some " +
                        "water. Never in a call or the quiet hours; in a full-screen game only a ticket; in a focus session, at its breaks.");
        var known = Array.FindIndex(StandUps, s => s.Minutes == choices.StandUpMinutes);
        _standUp = Form.Choice(Form.Grid(this), "Suggested", StandUps.Select(s => s.Label), known >= 0 ? known : 3, _ => Changed());
    }

    private void Changed() => Chosen?.Invoke(new CheckInChoices(Modes[_mode.Selected].Mode, StandUps[_standUp.Selected].Minutes));
}
