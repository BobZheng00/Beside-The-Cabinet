namespace InsertCoin.Core;

// ---- Inputs ----

public abstract record LedgerInput;

/// <summary>Sets the player. Every task currently visible is marked seen without reward (first run / username change).</summary>
public sealed record PlayerSet(string Username, IReadOnlyList<SolvedTask> VisibleTasks) : LedgerInput;

/// <summary>The latest accepted tasks from the task source.</summary>
public sealed record TasksObserved(IReadOnlyList<SolvedTask> Tasks) : LedgerInput;

/// <summary>Time spent in a focus part, as it ended or was skipped or stopped (paused time and time with a game running left out).</summary>
public sealed record Focused(TimeSpan Time) : LedgerInput;

/// <summary>
/// An activity the player did (see the balance spec, "Activities you tell her about"): it pays <paramref name="Share"/>
/// of its worth (1 in full, 0.5 when the proof was half convincing), once a day; a share paid before tops up.
/// </summary>
public sealed record ActivityDone(string Name, Size Size, double Share) : LedgerInput;

/// <summary>One detector check. Elapsed is monotonic time since the previous tick; Idle is time since the last user input.</summary>
public sealed record Tick(IReadOnlyList<RunningGame> Games, TimeSpan Elapsed, TimeSpan Idle) : LedgerInput;

// ---- Events ----

public abstract record LedgerEvent;

/// <summary>
/// How something earned was paid: debt first, then new play up to what was left of today's cap; the rest was over the
/// cap. <see cref="Coins"/> is how many whole credits it added (a partial amount fills the fuel bar, and a coin drops
/// when that completes one).
/// </summary>
public sealed record Payout(long EarnedMs, long DebtPaidMs, long CreditedMs, long OverCapMs, int Coins);

public sealed record CoinsEarned(SolvedTask Task, int Tokens, bool StarToken, int Streak, Payout Paid) : LedgerEvent;

/// <summary>A focus part paid for its minutes.</summary>
public sealed record FocusPaid(long FocusedMs, Payout Paid) : LedgerEvent;

/// <summary>An activity paid: <paramref name="Share"/> of its worth just now, <paramref name="Total"/> of it today in all.</summary>
public sealed record ActivityPaid(string Name, Size Size, double Share, double Total, Payout Paid) : LedgerEvent;

public sealed record DebtPaid(long PaidMs, int TicketsBefore, int TicketsAfter, long DebtLeftMs) : LedgerEvent;

/// <summary>Today's cap is reached: nothing more is paid until tomorrow.</summary>
public sealed record CapReached(long CapMs) : LedgerEvent;

public sealed record TokenInserted(int CreditsLeft) : LedgerEvent;
public enum Warning { FiveMinutes, OneMinute }
public sealed record LowTime(Warning Level, long TimeLeftMs) : LedgerEvent;
public sealed record CreditsOut : LedgerEvent;
public sealed record IouPrinted(int TicketNumber, long DebtMs) : LedgerEvent;
public sealed record SessionStarted(IReadOnlyList<string> GameNames) : LedgerEvent;
public sealed record SessionEnded(Session Receipt) : LedgerEvent;
public sealed record Paused : LedgerEvent;
public sealed record Resumed : LedgerEvent;

/// <summary>Play has gone past the player's bedtime: until the game day ends it costs more.</summary>
public sealed record LateNight(TimeSpan Bedtime, double Multiplier) : LedgerEvent;

/// <summary>A new game day: part of the unplayed time faded overnight (each night that passed took its share).</summary>
public sealed record Faded(long FadedMs, long LeftMs) : LedgerEvent;
