using Godot;
using InsertCoin.Core;
using InsertCoin.Game.Companion;

namespace InsertCoin.Game.App;

/// <summary>
/// Settings → Missions, at the top of the list until they're done (in Getting started's old place): each mission with
/// how it stands and a way to do it, what they pay while play time is earned, and Hide, which General undoes (spec
/// 2026-11-08-a-new-player-gets-it.md, "Missions").
/// </summary>
public partial class MissionSettings : VBoxContainer
{
    /// <summary>A mission's button: the page that does it ("mind", "memory", "voice"), or "agenda" (Memory's Agenda tab).</summary>
    public event Action<string>? Open;
    public event Action? HideAsked;

    /// <param name="voiceExtra">Their voice takes a download (Chinese and Japanese): hearing it is an extra.</param>
    public void Build(string her, MissionProgress progress, Earning earns, bool voiceExtra, long rewardMs)
    {
        Form.Page(this);
        Form.Hint(this, earns.Any ? $"A few things to try with {her}. Each pays {rewardMs / 60_000} minutes of play time, once, on top of the day's limit."
                                  : $"A few things to try with {her}.");
        foreach (var mission in Missions.Listed(earns))
        {
            var (name, how, button, page) = mission.Id switch
            {
                Missions.ConnectAnAi => ("Connect an AI", $"{her} talks with you through an AI model you choose.", "Choose one…", "mind"),
                Missions.SayHello => ("Say hello", $"Click {her} and type, or talk to them aloud.", null, null),
                Missions.EarnACredit => ("Earn a credit", Earn(earns), null, null),
                Missions.AskForAReminder => ("Ask for a reminder", $"Ask {her} to remind you of something, or add one on the agenda.", "Agenda…", "agenda"),
                Missions.OpenMemory => ("Open Memory", $"What {her} remembers of you, in Settings.", "Open it…", "memory"),
                _ => ("Hear their voice", $"{her} answers you aloud once their voice is set up." +
                                          (voiceExtra ? " An extra: it takes a download, and the list is done without it." : ""), "Voice…", "voice"),
            };
            var done = progress.Done.Contains(mission.Id);
            AddChild(new HSeparator());
            var row = Form.Buttons(this);
            Form.Subheading(row, name);
            var mark = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            row.AddChild(mark);
            if (done)
                Form.Say(mark, "Done", Paper.Good);
            else if (voiceExtra && mission.Id == Missions.HearTheirVoice)
                Form.Say(mark, "Extra", Paper.Faint);
            if (!done && button is not null)
                Form.Button(row, button, () => Open?.Invoke(page!));
            Form.Hint(this, how);
        }

        AddChild(new HSeparator());
        Form.Button(Form.Buttons(this), "Hide missions", () => HideAsked?.Invoke());
        Form.Hint(this, "General in Settings brings them back.");
    }

    /// <summary>How a credit is earned, naming only the ways that earn now.</summary>
    private static string Earn(Earning earns) => Mind.Words.Either([
        .. earns.Focus ? ["focus for five minutes on the screen's FOCUS app"] : Array.Empty<string>(),
        .. earns.Activities ? ["log something you've done on the DONE app"] : Array.Empty<string>(),
        .. earns.LeetCode ? ["solve a LeetCode problem"] : Array.Empty<string>()]) is { Length: > 0 } ways ? $"{char.ToUpperInvariant(ways[0])}{ways[1..]}." : "";
}
