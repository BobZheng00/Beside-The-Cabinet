namespace InsertCoin.Core;

public sealed record LedgerResult(LedgerState State, IReadOnlyList<LedgerEvent> Events);

/// <summary>The whole rulebook (spec §3, and the balance spec) as a pure function: no I/O, no clock, no randomness.</summary>
public sealed class Ledger(Rules rules)
{
    /// <summary>Input this recent means the player is at the PC now.</summary>
    private static readonly TimeSpan ActiveWithin = TimeSpan.FromMinutes(1);

    public Rules Rules { get; } = rules;

    public LedgerResult Step(LedgerState state, LedgerInput input, DateTimeOffset now)
    {
        var events = new List<LedgerEvent>();
        state = input switch
        {
            PlayerSet p => SetPlayer(state, p),
            TasksObserved t => Observe(TurnDay(state, now, events, deferWhilePlaying: false), t, events),
            Focused f => Focus(TurnDay(state, now, events, deferWhilePlaying: false), f, events),
            ActivityDone a => Activity(TurnDay(state, now, events, deferWhilePlaying: false), a, events),
            Tick k => OnTick(TurnDay(state, now, events, deferWhilePlaying: true), k, now, events),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, null),
        };
        return new LedgerResult(state, events);
    }

    private static LedgerState SetPlayer(LedgerState state, PlayerSet p) => state with
    {
        Username = p.Username,
        SeenTaskIds = state.SeenTaskIds.Union(p.VisibleTasks.Select(t => t.Id)),
    };

    // ---- Days ----

    /// <summary>
    /// A new game day: of the unplayed time, <see cref="Rules.NightlyFade"/> fades for each night that passed (debt
    /// never does), today's record joins the past days, and the new day takes the cap and bedtime the rules hold now.
    /// While a game runs it waits for the session to end, or for the next earning, whichever comes first.
    /// </summary>
    private LedgerState TurnDay(LedgerState state, DateTimeOffset now, List<LedgerEvent> events, bool deferWhilePlaying)
    {
        var day = GameCalendar.DayOf(now, Rules);
        if (state.Today is not { } today)
            return NewWeek(state with { Today = DayRecord.Begin(day, Rules) }, day);  // a save from before days were kept
        if (day <= today.Day || (deferWhilePlaying && state.Session is not null))
            return state;

        var left = state.TimeLeftMs(Rules);
        var kept = (long)Math.Round(left * Math.Pow(1 - Rules.NightlyFade, day.DayNumber - today.Day.DayNumber));
        if (left > kept)
            events.Add(new Faded(left - kept, kept));

        var days = state.Days.Add(today);
        if (days.Count > Rules.DaysKept)
            days = days.RemoveRange(0, days.Count - Rules.DaysKept);
        return Rearm(NewWeek(state with
        {
            Credits = (int)(kept / Rules.TokenMs),
            FuelMs = kept % Rules.TokenMs,
            Today = DayRecord.Begin(day, Rules, left - kept),
            Days = days,
            LateNightGiven = false,
        }, day));
    }

    /// <summary>The week's numbers start again on the Monday game day.</summary>
    private static LedgerState NewWeek(LedgerState state, DateOnly day)
    {
        var weekStart = GameCalendar.WeekStartOf(day);
        return weekStart > state.WeekStart ? state with { WeekStart = weekStart, Week = new WeekStats() } : state;
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
            state = Solve(state, task, difficulty, events);
        }
        return state;
    }

    private LedgerState Solve(LedgerState state, SolvedTask task, Difficulty difficulty, List<LedgerEvent> events)
    {
        var day = GameCalendar.DayOf(task.AcceptedAt, Rules);
        var firstOfDay = !state.StreakDays().Contains(day);           // not a good day yet: the star comes with these coins
        var solveDays = state.SolveDays.Add(day);
        var streak = GameCalendar.StreakEndingOn(state.StreakDays().Add(day), day);
        var star = firstOfDay && streak >= Rules.StreakBonusFromDay;
        var tokens = Rules.TokensFor(difficulty) + (star ? 1 : 0);

        var (paid, payout, after) = Pay(state, tokens * Rules.TokenMs);
        events.Add(new CoinsEarned(task, tokens, star, streak, payout));
        events.AddRange(after);
        return paid with
        {
            SeenTaskIds = paid.SeenTaskIds.Add(task.Id),
            SolveDays = solveDays,
            GoodDays = paid.GoodDays.Add(day),
            BestStreak = Math.Max(paid.BestStreak, streak),
            Week = paid.Week with { Solves = paid.Week.Solves + 1 },
            Today = paid.Today! with { Solves = paid.Today.Solves + 1 },
        };
    }

    /// <summary>A focus part pays for its minutes at once, however short: <see cref="Rules.FocusPerPlay"/> minutes of focus for each minute of play.</summary>
    private LedgerState Focus(LedgerState state, Focused focused, List<LedgerEvent> events)
    {
        var focusedMs = (long)focused.Time.TotalMilliseconds;
        if (focusedMs <= 0)
            return state;

        var (paid, payout, after) = Pay(state, focusedMs / Rules.FocusPerPlay);
        events.Add(new FocusPaid(focusedMs, payout));
        events.AddRange(after);
        paid = paid with
        {
            Week = paid.Week with { FocusedMs = paid.Week.FocusedMs + focusedMs },
            Today = paid.Today! with { FocusedMs = paid.Today.FocusedMs + focusedMs },
        };
        return paid.Today.FocusedMs >= Rules.GoodDayFocusMs ? GoodDay(paid, paid.Today.Day, events) : paid;
    }

    /// <summary>
    /// An activity pays like a problem of its size, once a day: what is owed is the share given now less what it was
    /// paid today already (half, then the rest; nothing when it was paid in full).
    /// </summary>
    private LedgerState Activity(LedgerState state, ActivityDone done, List<LedgerEvent> events)
    {
        var today = state.Today!;
        var before = today.Activities.GetValueOrDefault(done.Name);
        var share = Math.Clamp(done.Share, 0, 1) - before;
        if (share <= 0)
            return state;
        var (paid, payout, after) = Pay(state, (long)Math.Round(Rules.TokensFor(done.Size) * Rules.TokenMs * share));
        events.Add(new ActivityPaid(done.Name, done.Size, share, before + share, payout));
        events.AddRange(after);
        return GoodDay(paid with
        {
            Week = before > 0 ? paid.Week : paid.Week with { Activities = paid.Week.Activities + 1 },
            Today = paid.Today! with { Activities = paid.Today.Activities.SetItem(done.Name, before + share) },
        }, paid.Today!.Day, events);
    }

    /// <summary>
    /// A day counts towards the streak through focus or an activity (a solve counts in <see cref="Solve"/>, its star
    /// in its coins): the first time it does, the streak grows, and from its <see cref="Rules.StreakBonusFromDay"/>th
    /// day a star token is paid, under the day's cap like everything else. Past the cap the day still counts.
    /// </summary>
    private LedgerState GoodDay(LedgerState state, DateOnly day, List<LedgerEvent> events)
    {
        if (state.StreakDays().Contains(day))
            return state;
        state = state with { GoodDays = state.GoodDays.Add(day) };
        var streak = GameCalendar.StreakEndingOn(state.StreakDays(), day);
        state = state with { BestStreak = Math.Max(state.BestStreak, streak) };
        if (streak < Rules.StreakBonusFromDay)
            return state;
        var (paid, payout, after) = Pay(state, Rules.TokenMs);
        events.Add(new StreakStar(streak, payout));
        events.AddRange(after);
        return paid;
    }

    /// <summary>
    /// Pays something earned: debt first, then new play up to what is left of today's cap (whole tokens to credits,
    /// the rest to fuel; a full tank becomes a credit, except the token in the slot while a game runs). Past the cap
    /// it is done but not paid. Also returns the events that follow the earning's own: debt paid, the cap reached.
    /// </summary>
    private (LedgerState State, Payout Payout, List<LedgerEvent> After) Pay(LedgerState state, long earnedMs)
    {
        var after = new List<LedgerEvent>();
        var today = state.Today!;

        var debtPaid = Math.Min(earnedMs, state.DebtMs);
        var debtMs = state.DebtMs - debtPaid;
        if (debtPaid > 0)
            after.Add(new DebtPaid(debtPaid, state.IouTickets(Rules), (int)(debtMs / Rules.TokenMs), debtMs));

        var rest = earnedMs - debtPaid;
        var credited = Math.Min(rest, today.RoomMs());
        var credits = state.Credits + (int)(credited / Rules.TokenMs);
        var fuelMs = state.FuelMs + credited % Rules.TokenMs;
        if (fuelMs > Rules.TokenMs || (fuelMs == Rules.TokenMs && state.Session is null))
        {
            credits++;
            fuelMs -= Rules.TokenMs;
        }
        if (credited > 0 && today.EarnedMs + credited >= today.CapMs)
            after.Add(new CapReached(today.CapMs));

        var payout = new Payout(earnedMs, debtPaid, credited, rest - credited, credits - state.Credits);
        state = state with
        {
            Credits = credits,
            FuelMs = fuelMs,
            DebtMs = debtMs,
            Week = state.Week with { EarnedMs = state.Week.EarnedMs + credited },
            Today = today with
            {
                EarnedMs = today.EarnedMs + credited,
                OverCapMs = today.OverCapMs + rest - credited,
                DebtPaidMs = today.DebtPaidMs + debtPaid,
            },
        };
        return (Rearm(state), payout, after);
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
        state = Seen(state, tick, now);
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
        return Warn(Charge(state, elapsedMs, now, events), events);
    }

    /// <summary>The player is using the PC (input in the last minute): when they were first and last seen today.</summary>
    private static LedgerState Seen(LedgerState state, Tick tick, DateTimeOffset now)
    {
        if (tick.Idle >= ActiveWithin || state.Today is not { } today)
            return state;
        var at = now - tick.Idle;
        return state with { Today = today with { FirstActive = today.FirstActive ?? at, LastActive = at } };
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
        return TurnDay(state with { Session = null, Paused = false }, now, events, deferWhilePlaying: false);
    }

    private LedgerState InsertIfEmpty(LedgerState state, List<LedgerEvent> events)
    {
        if (state.FuelMs > 0 || state.Credits == 0)
            return state;
        events.Add(new TokenInserted(state.Credits - 1));
        return state with { Credits = state.Credits - 1, FuelMs = Rules.TokenMs };
    }

    /// <summary>
    /// Charges play: fuel first, a new token going in as each runs out, then debt at <see cref="Rules.DebtMultiplier"/>.
    /// Past bedtime every minute costs <see cref="Rules.LateNightMultiplier"/> times as much, debt included.
    /// </summary>
    private LedgerState Charge(LedgerState state, long elapsedMs, DateTimeOffset now, List<LedgerEvent> events)
    {
        var late = GameCalendar.IsLateNight(state, now, Rules);
        if (late && !state.LateNightGiven)
        {
            events.Add(new LateNight(state.Today!.Bedtime!.Value, Rules.LateNightMultiplier));
            state = state with { LateNightGiven = true };
        }
        var costMs = late ? (long)Math.Round(elapsedMs * Rules.LateNightMultiplier) : elapsedMs;

        var session = state.Session!;
        var week = state.Week;
        var remaining = costMs;

        while (remaining > 0)
        {
            state = InsertIfEmpty(state, events);           // a tank emptied by a payout or the fade, with credits left
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

        var today = state.Today!;
        return state with
        {
            Session = session with { PlayedMs = session.PlayedMs + elapsedMs, LateExtraMs = session.LateExtraMs + costMs - elapsedMs },
            Week = week with { PlayedMs = week.PlayedMs + elapsedMs },
            Today = today with { PlayedMs = today.PlayedMs + elapsedMs, LateExtraMs = today.LateExtraMs + costMs - elapsedMs, LastPlayed = now },
        };
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
