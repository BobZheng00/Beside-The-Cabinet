using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// What a JPEG photo says about itself in its EXIF block: how it is turned (phones store photos sideways and say so)
/// and when it was taken. Read without a library: the APP1 segment's TIFF header, its first directory, and the EXIF
/// directory it points to. Messaging apps often strip all of it; then the photo is upright and its date unknown.
/// </summary>
public static class Exif
{
    private const ushort OrientationTag = 0x0112, ChangedTag = 0x0132, ExifTag = 0x8769, TakenTag = 0x9003;

    /// <summary>The orientation (1 is upright; 2 to 8 as the EXIF standard numbers them) and when it was taken, as far as the photo says.</summary>
    public static (int Orientation, DateTime? Taken) Read(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg is not [0xFF, 0xD8, ..])
            return (1, null);
        var at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF)
        {
            var marker = jpeg[at + 1];
            if (marker is 0xD9 or 0xDA)
                break;                                      // the end, or the picture itself: no more about it
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(at + 2)..]);
            if (length < 2 || at + 2 + length > jpeg.Length)
                break;
            if (marker == 0xE1 && length >= 16 && jpeg.Slice(at + 4, 6).SequenceEqual("Exif\0\0"u8))
                return Tiff(jpeg.Slice(at + 10, length - 8));
            at += 2 + length;
        }
        return (1, null);
    }

    private static (int, DateTime?) Tiff(ReadOnlySpan<byte> tiff)
    {
        var little = tiff is [(byte)'I', (byte)'I', ..];
        if (!little && tiff is not [(byte)'M', (byte)'M', ..])
            return (1, null);
        var orientation = 1;
        DateTime? taken = null, changed = null;
        uint? exif = null;
        foreach (var (tag, type, count, value) in Entries(tiff, U32(tiff, 4, little), little))
            switch (tag)
            {
                case OrientationTag when type == 3:
                    orientation = U16(tiff, value, little) is var o and >= 1 and <= 8 ? o : 1;
                    break;
                case ExifTag:
                    exif = U32(tiff, value, little);
                    break;
                case ChangedTag:
                    changed = When(tiff, type, count, value, little);
                    break;
            }
        if (exif is { } directory)
            foreach (var (tag, type, count, value) in Entries(tiff, directory, little))
                if (tag == TakenTag)
                    taken = When(tiff, type, count, value, little);
        return (orientation, taken ?? changed);
    }

    /// <summary>A directory's entries: tag, type, count, and where the value is (in the entry itself when it fits in four bytes).</summary>
    private static List<(ushort Tag, ushort Type, uint Count, int Value)> Entries(ReadOnlySpan<byte> tiff, uint offset, bool little)
    {
        var entries = new List<(ushort, ushort, uint, int)>();
        if (offset + 2 > tiff.Length)
            return entries;
        var count = U16(tiff, (int)offset, little);
        for (var i = 0; i < count; i++)
        {
            var at = (int)offset + 2 + 12 * i;
            if (at + 12 > tiff.Length)
                break;
            entries.Add(((ushort)U16(tiff, at, little), (ushort)U16(tiff, at + 2, little), U32(tiff, at + 4, little), at + 8));
        }
        return entries;
    }

    /// <summary>An EXIF date, "2026:10:07 14:32:05" in ASCII, kept elsewhere in the block (twenty bytes don't fit in an entry).</summary>
    private static DateTime? When(ReadOnlySpan<byte> tiff, ushort type, uint count, int value, bool little)
    {
        if (type != 2 || count < 19)
            return null;
        var at = (int)U32(tiff, value, little);
        if (at < 0 || at + 19 > tiff.Length)
            return null;
        return DateTime.TryParseExact(Encoding.ASCII.GetString(tiff.Slice(at, 19)), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var when) ? when : null;
    }

    private static int U16(ReadOnlySpan<byte> tiff, int at, bool little) =>
        at < 0 || at + 2 > tiff.Length ? 0 : little ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[at..]);

    private static uint U32(ReadOnlySpan<byte> tiff, int at, bool little) =>
        at < 0 || at + 4 > tiff.Length ? 0 : little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[at..]);
}
