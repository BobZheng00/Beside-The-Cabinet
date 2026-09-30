using InsertCoin.Core;

namespace InsertCoin.Core.Tests;

/// <summary>Test driver: holds ledger state, a controllable clock, and every event emitted.</summary>
internal sealed class Machine
{
    // Wednesday 2026-09-30 10:00 local (+08:00). The week started Monday 2026-09-28.
    public static readonly DateTimeOffset T0 = new(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(8));
    public static readonly TimeSpan Check = TimeSpan.FromSeconds(5);

    public Rules Rules { get; } = new();
    public Ledger Ledger { get; }
    public LedgerState State { get; set; }
    public DateTimeOffset Now { get; set; }
    public List<LedgerEvent> Events { get; } = new();

    public Machine(DateTimeOffset? start = null)
    {
        Now = start ?? T0;
        Ledger = new Ledger(Rules);
        State = LedgerState.New(Now, Rules);
    }

    public List<LedgerEvent> Send(LedgerInput input)
    {
        var result = Ledger.Step(State, input, Now);
        State = result.State;
        Events.AddRange(result.Events);
        return result.Events.ToList();
    }

    public List<LedgerEvent> Solve(string id, Difficulty? difficulty = Difficulty.Easy, DateTimeOffset? acceptedAt = null) =>
        Send(new TasksObserved([new SolvedTask(id, id, difficulty, acceptedAt ?? Now)]));

    public void GiveTokens(int tokens)
    {
        for (var i = 0; i < tokens; i++)
            Solve($"given-{Guid.NewGuid()}", Difficulty.Easy);
    }

    /// <summary>Starts (or continues) a session and plays for <paramref name="duration"/> in 5 s checks.</summary>
    public List<LedgerEvent> Play(TimeSpan duration, string game = "Graveyard Keeper 2", TimeSpan? idle = null)
    {
        var events = new List<LedgerEvent>();
        var games = new[] { new RunningGame("1", game) };
        if (State.Session is null)
            events.AddRange(Tick(games, Check, idle ?? TimeSpan.Zero));
        for (var t = TimeSpan.Zero; t < duration; t += Check)
            events.AddRange(Tick(games, Check, idle ?? TimeSpan.Zero));
        return events;
    }

    public List<LedgerEvent> Tick(IReadOnlyList<RunningGame> games, TimeSpan elapsed, TimeSpan idle)
    {
        Now += elapsed;
        return Send(new Tick(games, elapsed, idle));
    }

    public List<LedgerEvent> Quit()
    {
        var events = Tick([], Check, TimeSpan.Zero);
        events.AddRange(Tick([], Check, TimeSpan.Zero));
        return events;
    }

    public static long Min(double minutes) => (long)(minutes * 60_000);
}
