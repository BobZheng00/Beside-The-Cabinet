namespace InsertCoin.Mind;

/// <summary>How much something she has to tell the player matters (spec 2026-10-10-reaching-the-player.md, "Something to tell").</summary>
public enum Matters
{
    /// <summary>Something the player set going with a time: a reminder, the agenda's lead, a focus session's turn.</summary>
    Must,
    /// <summary>Something of hers that matters: how did it go, a word at bedtime, a break after long play.</summary>
    Should,
    /// <summary>A suggestion that may pass unnoticed: standing up, what the record of days shows.</summary>
    Could,
}

/// <summary>Where the player is, as the cabinet makes it out (see <see cref="Reaching.Where"/>).</summary>
public enum Whereabouts
{
    /// <summary>At the PC: input in the last minute, talking with her, or playing.</summary>
    Here,
    /// <summary>No input for a minute or more, but not yet the away time.</summary>
    Unsure,
    /// <summary>No input for the away time, the screen locked, or they said they were heading out.</summary>
    Away,
    /// <summary>They said goodnight, or are away in the quiet hours.</summary>
    Asleep,
}

/// <summary>How something to tell reaches the player now (see <see cref="Reaching.Route"/>).</summary>
public enum Way
{
    /// <summary>Said at the PC at once, with no word first.</summary>
    Say,
    /// <summary>Said at the PC after a word first (see <see cref="WordFirst"/>).</summary>
    Call,
    /// <summary>A text to their phone, at once.</summary>
    Text,
    /// <summary>Kept for when they are back at the PC, while it is still worth saying.</summary>
    Wait,
    /// <summary>Let go.</summary>
    Lapse,
}

/// <summary>
/// The way, and what goes with it: a ticket above every window if her call goes unanswered, and a text to their phone
/// if, once it is said or her call goes unanswered, the away time passes with no input (they had gone after all).
/// </summary>
public sealed record Route(Way Way, bool Ticket = false, bool TextIfGone = false);

/// <summary>How something said at the PC comes this moment, from what the player is doing (see <see cref="Reaching.AtThePc"/>).</summary>
public enum Saying
{
    /// <summary>Aloud and in her bubble, as usual.</summary>
    Aloud,
    /// <summary>In her bubble only: they are in a call, where her voice would be heard by everyone in it.</summary>
    Bubble,
    /// <summary>Only a ticket with its line, or nothing more when it showed its own: they can't be spoken to now (full screen, a game).</summary>
    Ticket,
    /// <summary>Not yet: it can wait for a better moment (they have only just sat down, or are in a call or a game).</summary>
    Later,
}

/// <summary>
/// Something to tell the player, as every feature hands it over: the note for her mind, the plain line said when her
/// mind can't be reached, when it came and how long it stays worth saying, how much it matters, and whether it makes
/// sense away from the PC. A feature decides nothing else: where the player is, and so how she reaches them, is worked
/// out in one place (see <see cref="Reaching"/>).
/// </summary>
/// <param name="Title">What a ticket for it says at the top: REMINDER, FOCUS, BEDTIME…</param>
/// <param name="Note">What her mind is asked to get across, in her own words; empty when only the plain line is ever said.</param>
/// <param name="Line">The plain line, said when her mind can't be reached; empty when only her mind says it.</param>
/// <param name="Anywhere">It makes sense away from the PC (a reminder); false for what only makes sense there (standing up).</param>
/// <param name="Fresh">How long after it came it is still worth saying.</param>
/// <param name="Ticketed">It showed its own ticket as it came (a reminder, a focus session's turn): nothing more is needed for it to be seen.</param>
/// <param name="Plain">The plain line is said even when her mind could say it (the player unticked "She tells you, in her own words").</param>
/// <param name="Greet">She turns to them with a wave as she starts (a reminder, asking how something went).</param>
public sealed record Telling(string Title, string Note, string Line, Matters Matters, bool Anywhere, DateTimeOffset At, TimeSpan Fresh,
                             bool Ticketed = false, bool Plain = false, bool Greet = false)
{
    /// <summary>Still worth saying at <paramref name="now"/>.</summary>
    public bool FreshAt(DateTimeOffset now) => now - At <= Fresh;

    /// <summary>Its title as a heading on the player's phone: Reminder, Focus, Stand up.</summary>
    public string Heading => Title.Length == 0 ? "" : Title[..1].ToUpperInvariant() + Title[1..].ToLowerInvariant();
}

/// <summary>What the cabinet watches, at a moment, to make out where the player is (see <see cref="Reaching.Where"/>).</summary>
/// <param name="Idle">How long since their last input: keys, the mouse, a game pad.</param>
/// <param name="Locked">The screen is locked, or its saver runs.</param>
/// <param name="Busy">They are with her or in a game: talking with her (a conversation, her listening or answering), or playing.</param>
/// <param name="Quiet">It is the quiet hours.</param>
/// <param name="Said">What they last told her: heading out, or going to sleep, and when (see <see cref="Reaching.Said"/>); null for neither.</param>
public sealed record Watched(TimeSpan Idle, bool Locked, bool Busy, bool Quiet, Leaving? Said = null);

/// <summary>The player told her they were heading out, or going to sleep (<paramref name="Asleep"/>), at <paramref name="At"/>.</summary>
public sealed record Leaving(bool Asleep, DateTimeOffset At);

/// <summary>
/// Reaching the player as a roommate would (spec 2026-10-10-reaching-the-player.md): where they are, worked out in one
/// place from what the cabinet watches, and one table for how she reaches them with something to tell. At the PC she
/// calls them; away, what matters goes to their phone as a text; what only makes sense at the PC waits for them, or
/// lets go.
/// </summary>
public static class Reaching
{
    /// <summary>No input for this long, and the player may have stepped away: unsure.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(1);

    /// <summary>The away time, unless the player sets another on its slider (the user, 2026-10-10).</summary>
    public const int AwayMinutes = 5, FewestAwayMinutes = 2, MostAwayMinutes = 30;

    /// <summary>
    /// Input this soon after the player said they were off still counts as leaving (closing things down, picking up
    /// keys); after it, they are back, or never went.
    /// </summary>
    public static readonly TimeSpan Leaving = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What stays worth saying at least this long can wait for a better moment at the PC (once they have settled in,
    /// out of a call or a game) rather than come now: asking how something went, what the record of days shows.
    /// </summary>
    public static readonly TimeSpan CanWait = TimeSpan.FromHours(1);

    /// <summary>How long a text that must reach the player waits on her words before the plain line goes instead (to tune).</summary>
    public static readonly TimeSpan HerWordsWithin = TimeSpan.FromSeconds(30);

    /// <summary>The mark kept in her memory while the player is out or asleep by their own word (see <see cref="Said"/>).</summary>
    public const string Mark = "player.leaving";

    /// <summary>What her mind is told with something to tell that goes to the player's phone, kept with it in the conversation.</summary>
    public const string AsText = "The player is away from the PC, so this goes to their phone as a text message: write it as one.";

    /// <summary>
    /// What her mind is told with what the player texted her: she answers where they spoke to her. What needs them at the
    /// PC, she is told if she tries it (see <see cref="Toolbox.Away"/>).
    /// </summary>
    public const string Texted = "They texted you this from their phone, away from the PC: answer with a text.";

    /// <summary>What the conversation keeps where texting begins and ends, so she knows later which lines were texted.</summary>
    public const string TextingBegins = "From here the player is texting from their phone, away from the PC.",
                        TextingEnds = "The player is back at the PC.";

    /// <summary>
    /// Where the player is. What they said comes first, until they are back at the PC; then a locked screen means away
    /// (a game left running behind it too); then a game or her company means here; then the away time with no input
    /// means away (asleep in the quiet hours, as a locked screen is); then a minute with no input means unsure.
    /// </summary>
    public static Whereabouts Where(Watched watched, TimeSpan awayAfter, DateTimeOffset now)
    {
        if (watched.Said is { } said && !Back(watched, now))
            return said.Asleep ? Whereabouts.Asleep : Whereabouts.Away;
        if (watched.Locked)
            return watched.Quiet ? Whereabouts.Asleep : Whereabouts.Away;
        if (watched.Busy)
            return Whereabouts.Here;
        if (watched.Idle >= awayAfter)
            return watched.Quiet ? Whereabouts.Asleep : Whereabouts.Away;
        return watched.Idle >= Pause ? Whereabouts.Unsure : Whereabouts.Here;
    }

    /// <summary>They said they were off, and have used the PC since, beyond the minute it takes to leave: what they said no longer holds.</summary>
    public static bool Back(Watched watched, DateTimeOffset now) => watched.Said is { } said && now - watched.Idle > said.At + Leaving;

    /// <summary>
    /// The roommate, as one table: at the PC, a must is said at once, a should after a word first with a ticket if
    /// unanswered, a could after a word first with nothing more; unsure, the same, and a must (or a should whose call
    /// goes unanswered) is texted too if they turn out to be gone. Away, a must is texted, and so is a should that makes
    /// sense away from the PC; asleep, only a must, as they set it for then. What makes sense only at the PC waits for
    /// them, a could lets go. With nowhere to text them (<paramref name="canText"/> false), what would be texted waits
    /// for them instead.
    /// </summary>
    public static Route Route(Matters matters, bool anywhere, Whereabouts where, bool canText = true)
    {
        Route route = (where, matters) switch
        {
            (Whereabouts.Here, Matters.Must) => new(Way.Say),
            (Whereabouts.Unsure, Matters.Must) => new(Way.Say, TextIfGone: anywhere),
            (Whereabouts.Here or Whereabouts.Unsure, Matters.Should) => new(Way.Call, Ticket: true, TextIfGone: anywhere),
            (Whereabouts.Here or Whereabouts.Unsure, _) => new(Way.Call),
            (_, Matters.Must) => new(anywhere ? Way.Text : Way.Wait),
            (Whereabouts.Away, Matters.Should) => new(anywhere ? Way.Text : Way.Wait),
            (Whereabouts.Asleep, Matters.Should) => new(Way.Wait),
            _ => new(Way.Lapse),
        };
        return canText ? route : route with { Way = route.Way == Way.Text ? Way.Wait : route.Way, TextIfGone = false };
    }

    /// <summary>
    /// At the PC, how it comes this moment. In full screen or a game they can't be spoken to: what can wait does, the
    /// rest is only a ticket. In a call, what can wait does, the rest is her bubble only. Just sat down (not yet
    /// settled), what can wait does, so it doesn't meet them as they sit down.
    /// </summary>
    public static Saying AtThePc(Telling telling, Whereabouts where, bool settled, bool fullScreen, bool inCall)
    {
        var canWait = telling.Fresh >= CanWait;
        if (fullScreen)
            return canWait ? Saying.Later : Saying.Ticket;
        if (inCall)
            return canWait ? Saying.Later : Saying.Bubble;
        return where == Whereabouts.Here && !settled && canWait ? Saying.Later : Saying.Aloud;
    }

    /// <summary>What is kept in her memory when the player says they are heading out, or going to sleep (see <see cref="Said"/>).</summary>
    public static string Marked(bool asleep, DateTimeOffset at) => $"{(asleep ? "sleep" : "out")}|{at:O}";

    /// <summary>What the player last said about leaving, as kept (see <see cref="Marked"/>); null for nothing, or nothing readable.</summary>
    public static Leaving? Said(string? mark) =>
        mark?.Split('|') is [var what and ("out" or "sleep"), var when] && DateTimeOffset.TryParse(when, Prompt.English, System.Globalization.DateTimeStyles.None, out var at)
            ? new Leaving(what == "sleep", at)
            : null;
}
