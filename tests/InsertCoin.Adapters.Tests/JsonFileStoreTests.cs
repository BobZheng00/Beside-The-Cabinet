using InsertCoin.Core;

namespace InsertCoin.Adapters.Tests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("insertcoin-test-").FullName;
    private string StatePath => Path.Combine(_dir, "state.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static LedgerState SampleState()
    {
        var rules = new Rules();
        var now = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(8));
        var result = new Ledger(rules).Step(LedgerState.New(now, rules),
            new TasksObserved([new SolvedTask("two-sum", "Two Sum", Difficulty.Hard, now)]), now);
        return result.State with { Username = "bob" };
    }

    [Fact]
    public void Round_trips_ledger_state()
    {
        var store = new JsonFileStore<LedgerState>(StatePath);
        var state = SampleState();

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(state.Credits, loaded.Credits);
        Assert.Equal(state.SeenTaskIds, loaded.SeenTaskIds);
        Assert.Equal(state.SolveDays, loaded.SolveDays);
        Assert.Equal(state.WeekStart, loaded.WeekStart);
        Assert.Equal(state.Week, loaded.Week);
        Assert.Equal(state.InstalledAt, loaded.InstalledAt);
    }

    [Fact]
    public void Missing_file_loads_null()
    {
        Assert.Null(new JsonFileStore<LedgerState>(StatePath).Load());
    }

    [Fact]
    public void Second_save_keeps_the_previous_version_as_backup()
    {
        var store = new JsonFileStore<LedgerState>(StatePath);
        store.Save(SampleState());
        store.Save(SampleState() with { Credits = 99 });

        Assert.True(File.Exists(store.BackupPath));
        Assert.Equal(99, store.Load()!.Credits);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_backup()
    {
        var store = new JsonFileStore<LedgerState>(StatePath);
        store.Save(SampleState());
        store.Save(SampleState() with { Credits = 99 });
        File.WriteAllText(StatePath, "{ not json");

        Assert.Equal(SampleState().Credits, store.Load()!.Credits);
    }

    [Fact]
    public void Corrupt_file_without_backup_is_set_aside()
    {
        File.WriteAllText(StatePath, "{ not json");
        var store = new JsonFileStore<LedgerState>(StatePath);

        var loaded = store.Load(() => new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(loaded);
        Assert.False(File.Exists(StatePath));
        Assert.True(File.Exists(Path.Combine(_dir, "state.corrupt-20260930-120000.json")));
    }
}
