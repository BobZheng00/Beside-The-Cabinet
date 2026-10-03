using System.IO.Compression;
using System.Text.Json;

namespace InsertCoin.Game.Companion;

/// <summary>Brings a character the user picked or dropped into their characters folder.</summary>
public static class CharacterFiles
{
    /// <summary>
    /// Copies the model at <paramref name="path"/> into <paramref name="userFolder"/>: a .vrm or .glb as it is, a .gltf
    /// into a folder of its own together with the files it refers to. A .zip (how Sketchfab's glTF downloads come) or
    /// a folder is searched for a model. A file of the same name already there is replaced.
    /// </summary>
    /// <returns>The id of the character added, or null when there was no model to add.</returns>
    public static string? Import(string path, string userFolder)
    {
        if (Directory.Exists(path))
            return FindModel(path) is { } inside ? Import(inside, userFolder) : null;
        Directory.CreateDirectory(userFolder);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".zip":
                // unpacked into a folder named after the zip, so a model called scene.gltf gets the zip's name
                var unpacked = Path.Combine(Path.GetTempPath(), "insert-coin-import", Path.GetFileNameWithoutExtension(path));
                if (Directory.Exists(unpacked))
                    Directory.Delete(unpacked, recursive: true);
                ZipFile.ExtractToDirectory(path, unpacked);
                try
                {
                    return FindModel(unpacked) is { } model ? Import(model, userFolder) : null;
                }
                finally
                {
                    Directory.Delete(unpacked, recursive: true);
                }
            case ".vrm" or ".glb":
                var target = Path.Combine(userFolder, Path.GetFileName(path));
                if (!Same(path, target))
                    File.Copy(path, target, overwrite: true);
                return Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            case ".gltf":
                var from = Path.GetDirectoryName(Path.GetFullPath(path))!;
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Equals("scene", StringComparison.OrdinalIgnoreCase))      // Sketchfab's name for every model
                    name = Path.GetFileName(from);
                var to = Path.Combine(userFolder, name);
                if (!Same(from, to))
                    foreach (var file in References(path).Prepend(Path.GetFileName(path)))
                    {
                        var source = Path.GetFullPath(Path.Combine(from, file));
                        if (!source.StartsWith(from + Path.DirectorySeparatorChar) || !File.Exists(source))
                            continue;                   // outside the model's folder, or missing: the model will say so when loaded
                        var copy = Path.Combine(to, Path.GetRelativePath(from, source));
                        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                        File.Copy(source, copy, overwrite: true);
                    }
                return name.ToLowerInvariant();
            default:
                return null;
        }
    }

    /// <summary>
    /// Gives a character a card (a SillyTavern-style .png or .json): copied into the characters folder as
    /// <c>&lt;id&gt;.card.png</c> or <c>.json</c>, replacing any card it had. Returns the card's name, or null when the
    /// file holds no card (a plain picture, say).
    /// </summary>
    public static string? ImportCard(string path, string userFolder, string characterId)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".json") || InsertCoin.Mind.CharacterCard.Read(File.ReadAllBytes(path)) is not { } card)
            return null;
        var target = Path.Combine(userFolder, characterId + ".card" + extension);
        if (Same(path, target))
            return card.Name;                       // that is the card she has already
        Directory.CreateDirectory(userFolder);
        foreach (var old in new[] { ".card.png", ".card.json" }.Select(e => Path.Combine(userFolder, characterId + e)).Where(File.Exists))
            File.Delete(old);                       // one card per character: the new one replaces it
        File.Copy(path, target, overwrite: true);
        return card.Name;
    }

    /// <summary>Whether a file, a folder or a .zip holds a 3D model (rather than, say, a voice).</summary>
    public static bool HoldsModel(string path)
    {
        if (Directory.Exists(path))
            return FindModel(path) is not null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".zip")
            return extension is ".vrm" or ".glb" or ".gltf";
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => CompanionView.Formats.Any(format => e.FullName.EndsWith(format.TrimStart('*'), StringComparison.OrdinalIgnoreCase)));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>The first model in a folder or below, preferring the formats that carry the most.</summary>
    private static string? FindModel(string folder) =>
        CompanionView.Formats.SelectMany(format => Directory.EnumerateFiles(folder, format, SearchOption.AllDirectories)).FirstOrDefault();

    /// <summary>Files a .gltf refers to (its buffers and textures), relative to its folder.</summary>
    private static List<string> References(string gltf)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(gltf));
        var files = new List<string>();
        foreach (var list in new[] { "buffers", "images" })
            if (json.RootElement.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                    if (item.TryGetProperty("uri", out var uri) && uri.GetString() is { } text && !text.StartsWith("data:"))
                        files.Add(Uri.UnescapeDataString(text));
        return files;
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
