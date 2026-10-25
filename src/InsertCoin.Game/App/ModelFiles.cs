using Godot;

namespace InsertCoin.Game.App;

/// <summary>The local models packed into the app (under <c>res://models</c>): her memory search, her ears.</summary>
public static class ModelFiles
{
    /// <summary>Whether this build carries <paramref name="resource"/>.</summary>
    public static bool Present(string resource) => Godot.FileAccess.FileExists(resource) || Borrowed(resource) is not null;

    /// <summary>
    /// A real file for a model packed into the app: run from source it is the project's own file (or the main
    /// checkout's, in a worktree that has none); in the built app, which keeps it inside its package, a copy in the
    /// local cache (%LOCALAPPDATA%\InsertCoin\models), made once.
    /// </summary>
    public static string OnDisk(string resource, string name)
    {
        var project = ProjectSettings.GlobalizePath(resource);
        if (File.Exists(project))
            return project;
        if (Borrowed(resource) is { } borrowed)
            return borrowed;
        var cached = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "InsertCoin", "models", name,
                                  Path.GetFileName(resource));
        using var source = Godot.FileAccess.Open(resource, Godot.FileAccess.ModeFlags.Read)
                           ?? throw new FileNotFoundException($"{resource} is not in this build");
        if (File.Exists(cached) && new FileInfo(cached).Length == (long)source.GetLength())
            return cached;
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        var partial = cached + ".part";
        using (var copy = File.Create(partial))
            while (source.GetPosition() < source.GetLength())
                copy.Write(source.GetBuffer(1 << 20));                  // a megabyte at a time, never the whole model in memory
        File.Move(partial, cached, overwrite: true);
        return cached;
    }

    /// <summary>A small model file (a tokeniser) to read, wherever <see cref="OnDisk"/> would find it.</summary>
    public static Stream Open(string resource) =>
        Borrowed(resource) is { } borrowed ? File.OpenRead(borrowed) : new MemoryStream(Godot.FileAccess.GetFileAsBytes(resource));

    /// <summary>
    /// The main checkout's copy of a model, when this is a git worktree run from source with no models of its own:
    /// worktrees borrow them this way rather than through a link, which removing the worktree could follow and empty
    /// the main checkout's models (#122). Null in the built app, in the main checkout, or when it isn't there either.
    /// </summary>
    private static string? Borrowed(string resource)
    {
        if (MainProject.Value is not { } main || !resource.StartsWith("res://models/", StringComparison.Ordinal)
            || Godot.FileAccess.FileExists(resource))
            return null;
        var file = Path.Combine(main, resource["res://".Length..]);
        return File.Exists(file) ? file : null;
    }

    /// <summary>The game's folder in the main checkout, when this is a worktree run from source.</summary>
    private static readonly Lazy<string?> MainProject = new(() =>
    {
        if (!OS.HasFeature("editor"))
            return null;                                                // the built app carries its own, or none
        var project = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"));
        for (var folder = new DirectoryInfo(project); folder is not null; folder = folder.Parent)
        {
            var git = Path.Combine(folder.FullName, ".git");
            if (Directory.Exists(git))
                return null;                                            // the main checkout itself
            if (!File.Exists(git))
                continue;
            // a worktree's .git is a file: "gitdir: <main checkout>/.git/worktrees/<name>" (a submodule's points elsewhere)
            try
            {
                var line = File.ReadLines(git).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
                var worktrees = line is null ? null : Directory.GetParent(Path.GetFullPath(line["gitdir:".Length..].Trim(), folder.FullName));
                if (worktrees is not { Name: "worktrees", Parent.Parent: { } main })
                    return null;
                return Path.Combine(main.FullName, Path.GetRelativePath(folder.FullName, project));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;                                            // unreadable: no borrowing, never a crash
            }
        }
        return null;
    });
}
