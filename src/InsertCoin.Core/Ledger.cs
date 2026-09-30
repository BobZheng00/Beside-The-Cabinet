namespace InsertCoin.Core;

public sealed record LedgerResult(LedgerState State, IReadOnlyList<LedgerEvent> Events);

/// <summary>The whole rulebook (spec §3) as a pure function: no I/O, no clock, no randomness.</summary>
public sealed class Ledger(Rules rules)
{
    public Rules Rules { get; } = rules;

    public LedgerResult Step(LedgerState state, LedgerInput input, DateTimeOffset now)
    {
        var events = new List<LedgerEvent>();
        state = input switch
        {
            PlayerSet p => SetPlayer(state, p),
            TasksObserved t => Observe(RollWeek(state, now, events, deferWhilePlaying: false), t, events),
            Tick k => OnTick(RollWeek(state, now, events, deferWhilePlaying: true), k, now, events),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, null),
        };
        return new LedgerResult(state, events);
    }

    private static LedgerState SetPlayer(LedgerState state, PlayerSet p) => state with
    {
        Username = p.Username,
        SeenTaskIds = state.SeenTaskIds.Union(p.VisibleTasks.Select(t => t.Id)),
    };

    // ---- Weeks ----

    private LedgerState RollWeek(LedgerState state, DateTimeOffset now, List<LedgerEvent> events, bool deferWhilePlaying)
    {
        var weekStart = GameCalendar.WeekStartOf(GameCalendar.DayOf(now, Rules));
        if (weekStart <= state.WeekStart || (deferWhilePlaying && state.Session is not null))
            return state;

        events.Add(new WeeklyReset(state.TimeLeftMs(Rules), state.DebtMs));
        return state with { Credits = 0, FuelMs = 0, WeekStart = weekStart, Week = new WeekStats() };
    }

    // ---- Earning ----

    private LedgerState Observe(LedgerState state, TasksObserved observed, List<LedgerEvent> events)
    {
        foreach (var task in observed.Tasks.OrderBy(t => t.AcceptedAt))
        {
            if (task.Difficulty is not { } difficulty
                || task.AcceptedAt < state.InstalledAt
                || state.SeenTaskIds.Contains(task.Id))
                continue;
            state = Earn(state, task, difficulty, events);
        }
        return state;
    }

    private LedgerState Earn(LedgerState state, SolvedTask task, Difficulty difficulty, List<LedgerEvent> events)
    {
        var day = GameCalendar.DayOf(task.AcceptedAt, Rules);
        var firstOfDay = !state.SolveDays.Contains(day);
        var solveDays = state.SolveDays.Add(day);
        var streak = GameCalendar.StreakEndingOn(solveDays, day);
        var star = firstOfDay && streak >= Rules.StreakBonusFromDay;
        var tokens = Rules.TokensFor(difficulty) + (star ? 1 : 0);

        events.Add(new CoinsEarned(task, tokens, star, streak));

        var earnedMs = tokens * Rules.TokenMs;
        var paidMs = Math.Min(earnedMs, state.DebtMs);
        var debtMs = state.DebtMs - paidMs;
        if (paidMs > 0)
            events.Add(new DebtPaid(paidMs, state.IouTickets(Rules), (int)(debtMs / Rules.TokenMs), debtMs));

        var restMs = earnedMs - paidMs;
        var credits = state.Credits + (int)(restMs / Rules.TokenMs);
        var fuelMs = state.FuelMs + restMs % Rules.TokenMs;
        if (fuelMs > Rules.TokenMs)
        {
            credits++;
            fuelMs -= Rules.TokenMs;
        }

        state = state with
        {
            Credits = credits,
            FuelMs = fuelMs,
            DebtMs = debtMs,
            SeenTaskIds = state.SeenTaskIds.Add(task.Id),
            SolveDays = solveDays,
            BestStreak = Math.Max(state.BestStreak, streak),
            Week = state.Week with { Solves = state.Week.Solves + 1, TokensEarned = state.Week.TokensEarned + tokens },
        };
        return Rearm(state);
    }

    private LedgerState Rearm(LedgerState state)
    {
        var left = state.TimeLeftMs(Rules);
        return state with
        {
            FirstWarningGiven = state.FirstWarningGiven && left <= Rules.FirstWarningMs,
            FinalWarningGiven = state.FinalWarningGiven && left <= Rules.FinalWarningMs,
            CreditsOutGiven = state.CreditsOutGiven && left == 0,
        };
    }

    // ---- Spending ----

    private LedgerState OnTick(LedgerState state, Tick tick, DateTimeOffset now, List<LedgerEvent> events)
    {
        if (tick.Games.Count == 0)
            return state.Session is null ? state : MissGame(state, now, events);

        var names = tick.Games.Select(g => g.Name).Distinct().ToList();
        if (state.Session is null)
            return StartSession(state, names, now, events);

        var session = state.Session with
        {
            Misses = 0,
            GameNames = state.Session.GameNames.AddRange(names.Except(state.Session.GameNames)),
        };
        state = state with { Session = session };

        if (tick.Idle >= Rules.AfkAfter)
        {
            if (!state.Paused)
                events.Add(new Paused());
            return state with { Paused = true };
        }
        if (state.Paused)
        {
            events.Add(new Resumed());
            state = state with { Paused = false };
        }

        var elapsedMs = (long)Math.Clamp(tick.Elapsed.TotalMilliseconds, 0, Rules.MaxTick.TotalMilliseconds);
        return Warn(Charge(state, elapsedMs, events), events);
    }

    private LedgerState StartSession(LedgerState state, List<string> names, DateTimeOffset now, List<LedgerEvent> events)
    {
        events.Add(new SessionStarted(names));
        state = state with
        {
            Session = new Session { GameNames = [.. names], StartedAt = now },
            Paused = false,
            FirstWarningGiven = false,
            FinalWarningGiven = false,
            CreditsOutGiven = false,
        };
        return Warn(InsertIfEmpty(state, events), events);
    }

    private LedgerState MissGame(LedgerState state, DateTimeOffset now, List<LedgerEvent> events)
    {
        var session = state.Session! with { Misses = state.Session!.Misses + 1 };
        if (session.Misses < Rules.SessionEndMisses)
            return state with { Session = session };

        events.Add(new SessionEnded(session));
        return RollWeek(state with { Session = null, Paused = false }, now, events, deferWhilePlaying: false);
    }

    private LedgerState InsertIfEmpty(LedgerState state, List<LedgerEvent> events)
    {
        if (state.FuelMs > 0 || state.Credits == 0)
            return state;
        events.Add(new TokenInserted(state.Credits - 1));
        return state with { Credits = state.Credits - 1, FuelMs = Rules.TokenMs };
    }

    private LedgerState Charge(LedgerState state, long elapsedMs, List<LedgerEvent> events)
    {
        var session = state.Session!;
        var week = state.Week;
        var remaining = elapsedMs;

        while (remaining > 0)
        {
            if (state.FuelMs > 0)
            {
                var burn = Math.Min(state.FuelMs, remaining);
                state = state with { FuelMs = state.FuelMs - burn };
                session = session with { DrawnMs = session.DrawnMs + burn };
                remaining -= burn;
                state = InsertIfEmpty(state, events);
                continue;
            }

            // Out of time: every overtime millisecond costs DebtMultiplier.
            if (!state.CreditsOutGiven)
            {
                events.Add(new CreditsOut());
                state = state with { CreditsOutGiven = true, FirstWarningGiven = true, FinalWarningGiven = true };
            }
            var ticketsBefore = state.IouTickets(Rules);
            var added = (long)Math.Round(remaining * Rules.DebtMultiplier);
            state = state with { DebtMs = state.DebtMs + added };
            var ticketsAfter = state.IouTickets(Rules);
            for (var n = ticketsBefore + 1; n <= ticketsAfter; n++)
                events.Add(new IouPrinted(n, state.DebtMs));
            session = session with
            {
                DebtAddedMs = session.DebtAddedMs + added,
                IousPrinted = session.IousPrinted + ticketsAfter - ticketsBefore,
            };
            week = week with { IousPrinted = week.IousPrinted + ticketsAfter - ticketsBefore };
            remaining = 0;
        }

        session = session with { PlayedMs = session.PlayedMs + elapsedMs };
        week = week with { PlayedMs = week.PlayedMs + elapsedMs };
        return state with { Session = session, Week = week };
    }

    private LedgerState Warn(LedgerState state, List<LedgerEvent> events)
    {
        var left = state.TimeLeftMs(Rules);
        if (left == 0)
        {
            if (!state.CreditsOutGiven)
                events.Add(new CreditsOut());
            return state with { CreditsOutGiven = true, FirstWarningGiven = true, FinalWarningGiven = true };
        }
        if (left <= Rules.FinalWarningMs && !state.FinalWarningGiven)
        {
            events.Add(new LowTime(Warning.OneMinute, left));
            return state with { FinalWarningGiven = true, FirstWarningGiven = true };
        }
        if (left <= Rules.FirstWarningMs && !state.FirstWarningGiven)
        {
            events.Add(new LowTime(Warning.FiveMinutes, left));
            return state with { FirstWarningGiven = true };
        }
        return state;
    }
}
