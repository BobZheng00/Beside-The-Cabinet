namespace InsertCoin.Core;

// ---- Inputs ----

public abstract record LedgerInput;

/// <summary>Sets the player. Every task currently visible is marked seen without reward (first run / username change).</summary>
public sealed record PlayerSet(string Username, IReadOnlyList<SolvedTask> VisibleTasks) : LedgerInput;

/// <summary>The latest accepted tasks from the task source.</summary>
public sealed record TasksObserved(IReadOnlyList<SolvedTask> Tasks) : LedgerInput;

/// <summary>One detector check. Elapsed is monotonic time since the previous tick; Idle is time since the last user input.</summary>
public sealed record Tick(IReadOnlyList<RunningGame> Games, TimeSpan Elapsed, TimeSpan Idle) : LedgerInput;

// ---- Events ----

public abstract record LedgerEvent;

public sealed record CoinsEarned(SolvedTask Task, int Tokens, bool StarToken, int Streak) : LedgerEvent;
public sealed record DebtPaid(long PaidMs, int TicketsBefore, int TicketsAfter, long DebtLeftMs) : LedgerEvent;
public sealed record TokenInserted(int CreditsLeft) : LedgerEvent;
public enum Warning { FiveMinutes, OneMinute }
public sealed record LowTime(Warning Level, long TimeLeftMs) : LedgerEvent;
public sealed record CreditsOut : LedgerEvent;
public sealed record IouPrinted(int TicketNumber, long DebtMs) : LedgerEvent;
public sealed record SessionStarted(IReadOnlyList<string> GameNames) : LedgerEvent;
public sealed record SessionEnded(Session Receipt) : LedgerEvent;
public sealed record Paused : LedgerEvent;
public sealed record Resumed : LedgerEvent;
public sealed record WeeklyReset(long ExpiredMs, long DebtCarriedMs) : LedgerEvent;
