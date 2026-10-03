using Godot;

namespace InsertCoin.Game.App;

/// <summary>The local models packed into the app (under <c>res://models</c>): her memory search, her ears.</summary>
public static class ModelFiles
{
    /// <summary>Whether this build carries <paramref name="resource"/>.</summary>
    public static bool Present(string resource) => Godot.FileAccess.FileExists(resource);

    /// <summary>
    /// A real file for a model packed into the app: run from source it is the project's own file; in the built app,
    /// which keeps it inside its package, a copy in the local cache (%LOCALAPPDATA%\InsertCoin\models), made once.
    /// </summary>
    public static string OnDisk(string resource, string name)
    {
        var project = ProjectSettings.GlobalizePath(resource);
        if (File.Exists(project))
            return project;
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
}
