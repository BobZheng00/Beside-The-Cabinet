using System.Text.Json;

namespace InsertCoin.Adapters;

/// <summary>
/// Saves one JSON document atomically (temp file + replace, previous version kept as .bak).
/// Loading falls back to the backup; if both are unreadable the bad file is set aside and null is returned.
/// </summary>
public sealed class JsonFileStore<T>(string path, JsonSerializerOptions? options = null) where T : class
{
    private readonly JsonSerializerOptions _options = options ?? new() { WriteIndented = true };

    public string Path { get; } = path;
    public string BackupPath => Path + ".bak";

    public T? Load(Func<DateTimeOffset>? clock = null)
    {
        if (TryRead(Path, out var value) || TryRead(BackupPath, out value))
            return value;

        if (File.Exists(Path))
        {
            var stamp = (clock ?? (() => DateTimeOffset.Now))().ToString("yyyyMMdd-HHmmss");
            var aside = System.IO.Path.ChangeExtension(Path, $"corrupt-{stamp}.json");
            File.Move(Path, aside, overwrite: true);
        }
        return null;
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, _options));
        if (File.Exists(Path))
            File.Replace(temp, Path, BackupPath);
        else
            File.Move(temp, Path);
    }

    private bool TryRead(string file, out T? value)
    {
        value = null;
        if (!File.Exists(file))
            return false;
        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(file), _options);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
