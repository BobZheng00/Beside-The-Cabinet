using System.Collections.Immutable;

namespace InsertCoin.Core;

/// <summary>A goal ticked by what the player does, which teaches the app after the first run: its id, and its name on the cabinet's screen.</summary>
public sealed record Mission(string Id, string Title);

/// <summary>
/// The missions a profile has: those done, and of them those paid. A mission done under FREE PLAY waits unpaid until
/// play time is earned again; one paid stays paid, however often the mode is switched.
/// </summary>
public sealed record MissionProgress
{
    public ImmutableSortedSet<string> Done { get; init; } = ImmutableSortedSet<string>.Empty;
    public ImmutableSortedSet<string> Paid { get; init; } = ImmutableSortedSet<string>.Empty;
}

/// <summary>
/// The six missions, kept as data rather than tied to the screen's apps, so a new app doesn't change them (spec
/// 2026-11-08-a-new-player-gets-it.md, "Missions"). The ledger ticks EARN A CREDIT itself; the app ticks the others.
/// </summary>
public static class Missions
{
    public const string ConnectAnAi = "ai", SayHello = "hello", EarnACredit = "credit", AskForAReminder = "reminder", OpenMemory = "memory",
        HearTheirVoice = "voice";

    public static readonly IReadOnlyList<Mission> All =
    [
        new(ConnectAnAi, "CONNECT AN AI"),
        new(SayHello, "SAY HELLO"),
        new(EarnACredit, "EARN A CREDIT"),
        new(AskForAReminder, "ASK FOR A REMINDER"),
        new(OpenMemory, "OPEN MEMORY"),
        new(HearTheirVoice, "HEAR THEIR VOICE"),
    ];

    /// <summary>The missions on the list: all six with CREDITS; FREE PLAY (nothing earns) has no EARN A CREDIT.</summary>
    public static IEnumerable<Mission> Listed(Earning earns) => All.Where(m => m.Id != EarnACredit || earns.Any);

    /// <summary>
    /// The missions on the list still to do. HEAR THEIR VOICE is an extra where the voice takes a download
    /// (<paramref name="voiceExtra"/>: Chinese and Japanese): it pays if done, and the list is done without it. None
    /// for a profile without missions (one set up before them).
    /// </summary>
    public static IEnumerable<Mission> Left(MissionProgress? progress, Earning earns, bool voiceExtra) =>
        progress is null ? [] : Listed(earns).Where(m => !progress.Done.Contains(m.Id) && !(voiceExtra && m.Id == HearTheirVoice));
}
