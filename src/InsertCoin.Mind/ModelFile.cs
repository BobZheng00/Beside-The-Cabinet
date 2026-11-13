using System.Buffers.Binary;
using System.Text.Json;

namespace InsertCoin.Mind;

/// <summary>
/// Whether a model file of the player's (.vrm, .glb, .gltf) is whole before it is read for their portrait: a download
/// cut off, or a file that only starts like a model, is caught here rather than by the loader, which logs errors.
/// </summary>
public static class ModelFile
{
    private const uint Json = 0x4E4F534A, Magic = 0x46546C67;     // "JSON" and "glTF", little-endian

    public static bool LooksWhole(string path)
    {
        try
        {
            return path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ? GltfWhole(path) : GlbWhole(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>A binary glTF (a .vrm is one): its length as declared, chunks that end where the file does, the first its JSON.</summary>
    private static bool GlbWhole(byte[] file)
    {
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
        if (file.Length < 20 || Word(0) != Magic || Word(4) != 2 || Word(8) != file.Length)
            return false;
        var at = 12;
        var first = true;
        while (at < file.Length)
        {
            if (file.Length - at < 8)
                return false;
            var length = Word(at);
            if (length > file.Length - at - 8)
                return false;
            if (first && (Word(at + 4) != Json || !HasAsset(file.AsSpan(at + 8, (int)length).ToArray())))
                return false;
            first = false;
            at += 8 + (int)length;
        }
        return !first;
    }

    /// <summary>A text glTF: JSON with its <c>asset</c>, and the buffers it names beside it, as long as it says.</summary>
    private static bool GltfWhole(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!HasAsset(json.RootElement))
            return false;
        if (!json.RootElement.TryGetProperty("buffers", out var buffers) || buffers.ValueKind != JsonValueKind.Array)
            return true;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (var buffer in buffers.EnumerateArray())
        {
            if (!buffer.TryGetProperty("uri", out var uri) || uri.GetString() is not { } name || name.StartsWith("data:"))
                continue;
            var file = new FileInfo(Path.Combine(folder, Uri.UnescapeDataString(name)));
            if (!file.Exists || buffer.TryGetProperty("byteLength", out var length) && length.TryGetInt64(out var bytes) && file.Length < bytes)
                return false;
        }
        return true;
    }

    private static bool HasAsset(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return HasAsset(document.RootElement);
    }

    private static bool HasAsset(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("asset", out var asset) && asset.ValueKind == JsonValueKind.Object;
}
