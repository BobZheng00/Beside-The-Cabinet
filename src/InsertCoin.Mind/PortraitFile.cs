using System.Security.Cryptography;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Where a character's portrait is kept in the profile: a picture named for their model as it is now (its files'
/// sizes and times), so a model that changes has no portrait yet and gets one made again, and the old one goes.
/// </summary>
public static class PortraitFile
{
    /// <summary>The portrait's file for a character, whether made yet or not.</summary>
    /// <param name="folder">The profile's portraits folder.</param>
    /// <param name="model">The model's file. One in a folder named for the character counts every file there (a .gltf's
    /// textures, say), but not their card or persona, which don't change how they look.</param>
    /// <param name="also">Anything else that changes how they look (for one built into the app, the app's build).</param>
    public static string For(string folder, string id, string model, string also = "")
    {
        var own = Path.GetDirectoryName(Path.GetFullPath(model))!;
        var files = Path.GetFileName(own).Equals(id, StringComparison.OrdinalIgnoreCase) && Directory.Exists(own)
            ? Directory.GetFiles(own, "*", SearchOption.AllDirectories).Where(f => !IsAboutThem(f))
            : [model];
        var stamp = string.Join("\n", files.Order(StringComparer.OrdinalIgnoreCase).Select(f => new FileInfo(f))
            .Select(f => $"{f.Name}|{(f.Exists ? f.Length : -1)}|{(f.Exists ? f.LastWriteTimeUtc.Ticks : 0)}").Append(also));
        return Path.Combine(folder, $"{id}.{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..12].ToLowerInvariant()}.png");
    }

    /// <summary>A portrait was made and saved at <paramref name="file"/>: any older one of the same character goes.</summary>
    public static void Kept(string file)
    {
        foreach (var old in Directory.GetFiles(Path.GetDirectoryName(file)!, "*.png")
                     .Where(f => Owner(f) == Owner(file) && !Path.GetFileName(f).Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)))
            File.Delete(old);
    }

    private static string Owner(string file) => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));

    private static bool IsAboutThem(string file) =>
        Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(file).Equals("card", StringComparison.OrdinalIgnoreCase);
}
