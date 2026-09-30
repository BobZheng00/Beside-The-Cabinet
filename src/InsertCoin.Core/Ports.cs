namespace InsertCoin.Core;

/// <summary>Where completed problems come from (LeetCode in v1).</summary>
public interface ITaskSource
{
    /// <summary>Recent accepted tasks, newest first. Difficulty may be null if it could not be looked up yet.</summary>
    Task<IReadOnlyList<SolvedTask>> FetchRecentAsync(string username, CancellationToken ct);

    /// <summary>False when the player does not exist on the source.</summary>
    Task<bool> PlayerExistsAsync(string username, CancellationToken ct);
}

/// <summary>Which games are running right now (Steam in v1).</summary>
public interface IGameDetector
{
    /// <summary>Null when the detector cannot see its platform at all (e.g. Steam not installed).</summary>
    IReadOnlyList<RunningGame>? RunningGames();
}
