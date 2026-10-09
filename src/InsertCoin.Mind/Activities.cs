using System.Globalization;
using InsertCoin.Core;

namespace InsertCoin.Mind;

/// <summary>An activity that counts, as she sized it the first time the player told her about it.</summary>
/// <param name="Provable">It can be shown in a photo or a screenshot (a walk, the gym); not, say, meditating or a call home.</param>
public sealed record Activity(string Name, Size Size, bool Provable, DateTimeOffset Added);

/// <summary>How sure she is that a picture shows the activity it is proof of, done today.</summary>
public enum Conviction { Sure, Unsure, Doubtful }

/// <summary>
/// The good things the player does away from LeetCode and focus sessions, which count towards their play time (the
/// balance spec, "Activities you tell her about"). What one is worth isn't the player's to set, nor how much proof it
/// needs: the first time they tell her about one she sizes it (small, medium, big: it pays like a problem of that size)
/// and they confirm, and from then on it pays the same, once a day. Whether the cabinet wants proof is its own rule,
/// which she applies: never for what can't be photographed, always when it doesn't add up, otherwise by its size and
/// her record of the player's proofs. She judges a picture by how sure she is; the cabinet pays by that.
/// </summary>
public sealed class Activities(MindStore store, TimeProvider? clock = null, Random? random = null)
{
    /// <summary>How many of her latest verdicts make the player's record.</summary>
    public const int Record = 10;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Random _random = random ?? Random.Shared;

    /// <summary>The player confirms a claim on her card (what, detail): yes, no, or null when they didn't answer.</summary>
    public Func<string, string, CancellationToken, Task<bool?>> Confirm { get; set; } = (_, _, _) => Task.FromResult<bool?>(null);

    /// <summary>The cabinet pays an activity (its name, size and the share of its worth) and says what came of it; null when nothing was owed.</summary>
    public Func<string, Size, double, Payout?> Pay { get; set; } = (_, _, _) => null;

    /// <summary>How much of an activity's worth the cabinet has paid today (0 to 1).</summary>
    public Func<string, double> PaidToday { get; set; } = _ => 0;

    /// <summary>The game day of a moment (04:00 to 04:00).</summary>
    public Func<DateTimeOffset, DateOnly> DayOf { get; set; } = moment => DateOnly.FromDateTime(moment.DateTime.AddHours(-4));

    /// <summary>How long the player has been at the PC without a break, just now.</summary>
    public Func<TimeSpan> AtThePc { get; set; } = () => TimeSpan.Zero;

    /// <summary>Her model can't see pictures: no proof is asked for, and the player's word counts.</summary>
    public Func<bool> Blind { get; set; } = () => false;

    /// <summary>The pictures in front of her in the reply under way.</summary>
    public Func<IReadOnlyList<Attachment>> InFront { get; set; } = () => [];

    /// <summary>Dev: the roll that decides whether proof is wanted (0 always, 1 never), instead of chance.</summary>
    public double? Roll { get; set; }

    /// <summary>The activities she knows, as she sized them, the oldest first.</summary>
    public IReadOnlyList<Activity> Known => store.Activities();

    /// <summary>The activities waiting for proof today.</summary>
    public IReadOnlyList<string> Waiting => store.Claims(DayOf(_clock.GetLocalNow()));

    /// <summary>
    /// The player did something that counts: confirmed on her card (unless they logged it themselves on the screen), it
    /// is kept if it is new, then paid, or, when the cabinet wants proof this time, waits for a picture.
    /// </summary>
    /// <param name="size">Hers, for one she doesn't know yet; one she knows keeps its size.</param>
    /// <param name="minutes">How long it took, when the player said: longer than they have been away from the PC doesn't add up.</param>
    /// <param name="confirmed">The player logged it themselves (on the screen): no card.</param>
    /// <returns>What she is told.</returns>
    public async Task<string> Log(string name, Size? size, int? minutes, bool? provable, bool confirmed = false, CancellationToken cancel = default)
    {
        name = name.Trim();
        if (name.Length == 0)
            return "Say what the activity was.";
        var known = Find(name);
        if (known is null && size is null)
            return "It's new: say how big it is (small, medium or big), then log it again.";
        var activity = known ?? new Activity(Shorten(name), size!.Value, provable ?? true, _clock.GetLocalNow());
        var worth = Coins(activity.Size);
        if (PaidToday(activity.Name) >= 1)
            return $"{activity.Name} already counted today.";
        if (Waiting.Contains(activity.Name))
            return $"{activity.Name} is already waiting for proof today: it counts once they show you a picture of it.";
        if (!confirmed)
        {
            var answer = await Confirm($"Count it: {activity.Name}?", known is null ? $"New: {Words(activity.Size)}, {worth} from now on" : $"{worth}, once today", cancel);
            if (answer != true)
                return answer is false
                    ? "The player said not yet, so it doesn't count. Take that lightly."
                    : "The player didn't answer, so it doesn't count yet.";
        }
        if (known is null)
            store.KeepActivity(activity);
        var addsUp = minutes is not { } length || AtThePc() < TimeSpan.FromMinutes(length);
        if (!Blind() && Wanted(activity, store.Convictions(Record), addsUp, Roll ?? _random.NextDouble()))
        {
            store.Claim(DayOf(_clock.GetLocalNow()), activity.Name, _clock.GetLocalNow());
            return $"It counts ({worth}) once it's shown: this time the cabinet wants a photo or a screenshot of it (whatever would show it: " +
                   "the place, a step count, a watch, the result). Ask for one in your own way, briefly, and don't ask again after that. " +
                   "When they show you a picture of it, use judge_proof.";
        }
        return Paid(activity, 1) is { } paid
            ? $"Counted: {activity.Name}, {worth}{(paid.OverCapMs > 0 ? ", though today's cap held some of it back" : "")}."
            : $"{activity.Name} already counted today.";
    }

    /// <summary>
    /// Her verdict on a picture of an activity waiting for proof: sure pays it in full, unsure half (a better picture
    /// today pays the rest), doubtful nothing yet. A photo that says it was taken on another day can be half at most.
    /// </summary>
    /// <returns>What she is told.</returns>
    public string Judge(string name, Conviction verdict, string saw)
    {
        var activity = Find(name);
        var today = DayOf(_clock.GetLocalNow());
        if (activity is null || !store.Claims(today).Contains(activity.Name))
            return "Nothing by that name is waiting for proof today: log it first.";
        var pictures = InFront().Where(a => a.Picture).ToList();
        if (pictures.Count == 0)
            return "There's no picture in front of you: it counts once they show you one.";
        var otherDay = pictures.Any(p => p.Taken is { } taken && DayOf(new DateTimeOffset(taken, _clock.GetLocalNow().Offset)) != today);
        if (otherDay && verdict == Conviction.Sure)
            verdict = Conviction.Unsure;
        store.AddConviction(activity.Name, verdict, _clock.GetLocalNow());
        var share = verdict switch { Conviction.Sure => 1, Conviction.Unsure => 0.5, _ => 0.0 };
        var before = PaidToday(activity.Name);
        if (share >= 1)
            store.Settle(today, activity.Name);
        if (share > before)
            Paid(activity, share);
        var why = otherDay ? " The photo says it was taken on another day, so it counts as half at most." : "";
        return verdict switch
        {
            Conviction.Sure => $"Stamped: {activity.Name} counts in full ({Coins(activity.Size)}).",
            Conviction.Unsure => $"Half of it counts for now; a clearer picture today pays the rest.{why}",
            _ => "It doesn't count yet; a better picture today still pays. Tell them so briefly, without lecturing, and don't ask again: it's up to them.",
        };
    }

    /// <summary>The player takes an activity off the list (the screen's DONE app): it no longer counts.</summary>
    public void Forget(string name)
    {
        if (Find(name) is { } activity)
            store.ForgetActivity(activity.Name);
    }

    /// <summary>
    /// Whether the cabinet wants proof this time: never for what can't be photographed, always when it doesn't add up
    /// (longer than they have been away from the PC), otherwise by chance: about one in seven small things, one in three
    /// medium, seven in ten big; half as often for a player whose proofs have convinced her, twice as often after ones
    /// that didn't.
    /// </summary>
    /// <param name="roll">A number from 0 to 1, by chance.</param>
    public static bool Wanted(Activity activity, IReadOnlyList<Conviction> record, bool addsUp, double roll)
    {
        if (!activity.Provable)
            return false;
        if (!addsUp)
            return true;
        var chance = activity.Size switch { Size.Small => 0.15, Size.Medium => 0.35, _ => 0.7 };
        var trust = record.Take(Record).Sum(v => v switch { Conviction.Sure => 1, Conviction.Unsure => 0, _ => -2 });
        if (trust >= 5)
            chance /= 2;
        else if (trust < 0)
            chance = Math.Min(1, chance * 2);
        return roll < chance;
    }

    /// <summary>The cabinet block's lines: what counts, what counted today, what waits for proof.</summary>
    public string Describe()
    {
        var known = Known;
        if (known.Count == 0)
            return "Activities that count: none yet (you size each the first time the player tells you about one).";
        var lines = new List<string>
        {
            "Activities that count (as you sized them; once a day each): " + string.Join(", ", known.Select(a => $"{a.Name} ({Words(a.Size)}{(a.Provable ? "" : ", no proof")})")) + ".",
        };
        var counted = known.Where(a => PaidToday(a.Name) > 0).Select(a => PaidToday(a.Name) >= 1 ? a.Name : $"{a.Name} (half so far)").ToList();
        if (counted.Count > 0)
            lines.Add("Counted today: " + string.Join(", ", counted) + ".");
        if (Waiting is { Count: > 0 } waiting)
            lines.Add("Waiting for a picture as proof today: " + string.Join(", ", waiting) + ".");
        return string.Join("\n", lines);
    }

    public static Size? ParseSize(string? size) => size?.Trim().ToLowerInvariant() switch
    {
        "small" or "s" => Size.Small,
        "medium" or "m" => Size.Medium,
        "big" or "large" or "l" or "b" => Size.Big,
        _ => null,
    };

    public static Conviction ParseConviction(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "sure" => Conviction.Sure,
        "unsure" => Conviction.Unsure,
        _ => Conviction.Doubtful,
    };

    private Payout? Paid(Activity activity, double share) => Pay(activity.Name, activity.Size, share);

    /// <summary>
    /// The activity she knows that something on the agenda is ("Go to the gym" is the gym, "Walk with Sam" a walk): every
    /// word of its name is among the plan's words; the one with the most words, when more than one is.
    /// </summary>
    public Activity? Within(string plan)
    {
        var words = Key(plan).Split(' ').Select(w => w.Trim(',', '.', '!', '?', ';', ':', '(', ')')).ToHashSet();
        return store.Activities().Where(a => Key(a.Name).Split(' ').All(words.Contains)).MaxBy(a => Key(a.Name).Split(' ').Length);
    }

    /// <summary>The one she knows by that name: the same words, whatever the case, the spacing or a leading "the", "a" or "my".</summary>
    private Activity? Find(string name) => store.Activities().FirstOrDefault(a => Key(a.Name) == Key(name));

    private static string Key(string name)
    {
        var words = name.Trim().ToLower(CultureInfo.InvariantCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] is "the" or "a" or "an" or "my")
            words.RemoveAt(0);
        return string.Join(' ', words);
    }

    private static string Shorten(string name) => name.Length <= 40 ? name : name[..40].TrimEnd();

    private static string Words(Size size) => size switch { Size.Small => "small", Size.Medium => "medium", _ => "big" };

    private static string Coins(Size size) => size switch { Size.Small => "1 coin", Size.Medium => "2 coins", _ => "4 coins" };
}
