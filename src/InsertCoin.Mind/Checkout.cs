namespace InsertCoin.Mind;

/// <summary>
/// The app run from source in a git worktree: what is built or fetched once (the models, the program that speaks Chinese
/// and Japanese) lives in the main checkout, and a worktree borrows it from there rather than through a link, which
/// removing the worktree could follow and empty (#122).
/// </summary>
public static class Checkout
{
    /// <summary>
    /// <paramref name="path"/> (a file or a folder) where it is; in a worktree without it, the main checkout's when that
    /// has it; else where it is, missing.
    /// </summary>
    public static string Borrowing(string path)
    {
        path = Path.GetFullPath(path);
        return !Path.Exists(path) && InMain(path) is { } there && Path.Exists(there) ? there : path;
    }

    /// <summary>
    /// The same folder in the main checkout, when <paramref name="folder"/> is inside a git worktree: null in the main
    /// checkout itself, outside git, in a submodule, or when the worktree's .git file can't be read.
    /// </summary>
    public static string? InMain(string folder)
    {
        folder = Path.GetFullPath(folder);
        for (var at = new DirectoryInfo(folder); at is not null; at = at.Parent)
        {
            var git = Path.Combine(at.FullName, ".git");
            if (Directory.Exists(git))
                return null;                                            // the main checkout itself
            if (!File.Exists(git))
                continue;
            // a worktree's .git is a file: "gitdir: <main checkout>/.git/worktrees/<name>" (a submodule's points elsewhere)
            try
            {
                var line = File.ReadLines(git).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
                var worktrees = line is null ? null : Directory.GetParent(Path.GetFullPath(line["gitdir:".Length..].Trim(), at.FullName));
                if (worktrees is not { Name: "worktrees", Parent.Parent: { } main })
                    return null;
                return Path.Combine(main.FullName, Path.GetRelativePath(at.FullName, folder));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;                                            // unreadable: no borrowing, never a crash
            }
        }
        return null;
    }
}
