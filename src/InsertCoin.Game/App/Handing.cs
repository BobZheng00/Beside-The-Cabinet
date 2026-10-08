using Godot;
using InsertCoin.Mind;

namespace InsertCoin.Game.App;

/// <summary>
/// Gets what the player hands her ready for the model (see <see cref="Attachment"/>): a picture turned upright as its
/// camera says, and made no larger than models read in full (its long side at most 1568 px; a photo as JPEG, anything
/// else as PNG, so a screenshot's text stays sharp); a PDF or a text file as it is, within limits. Each comes with a
/// little picture for its chip on the slip.
/// </summary>
public static class Handing
{
    /// <summary>The largest file taken (a PDF: providers take no more than this).</summary>
    public const int MaxBytes = 20 * 1024 * 1024, MaxPdfBytes = 10 * 1024 * 1024;
    private const int Longest = 1568, ThumbSize = 56, MaxPng = 3_500_000;

    /// <summary>A file dropped, pasted or picked: ready to hand to her, or why not, in the screen's words.</summary>
    public static (Attachment? Thing, Texture2D? Thumb, string? Why) From(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            return (null, null, Directory.Exists(path) ? "FILES ONLY, NOT FOLDERS" : "FILE NOT FOUND");
        if (file.Length > MaxBytes)
            return (null, null, "TOO BIG: 20 MB AT MOST");
        return From(file.Name, File.ReadAllBytes(path));
    }

    /// <summary>A file's contents under its name (a photo sent from a phone): ready to hand to her, or why not.</summary>
    public static (Attachment? Thing, Texture2D? Thumb, string? Why) From(string name, byte[] data)
    {
        if (data.Length > MaxBytes)
            return (null, null, "TOO BIG: 20 MB AT MOST");
        var type = Attachment.TypeOf(name, data.AsSpan(0, Math.Min(data.Length, 8192)));
        if (type is null)
            return (null, null, $"SHE CAN'T TAKE {Path.GetExtension(name).TrimStart('.').ToUpperInvariant()} FILES");
        if (type == "application/pdf")
            return data.Length <= MaxPdfBytes ? (new Attachment(name, type, data), null, null) : (null, null, "PDF TOO BIG: 10 MB AT MOST");
        if (!type.StartsWith("image/", StringComparison.Ordinal))
            return (new Attachment(name, type, data), null, null);

        var (orientation, taken) = type == "image/jpeg" ? Exif.Read(data) : (1, null);
        if (Load(data, type) is not { } image)
            return type == "image/gif" ? (new Attachment(name, type, data) { Taken = taken }, null, null) : (null, null, "CAN'T OPEN THAT PICTURE");
        Upright(image, orientation);
        var (thing, thumb) = Picture(image, name, photo: type == "image/jpeg");
        return (thing with { Taken = taken }, thumb, null);
    }

    /// <summary>A picture in hand (pasted, a screenshot, a photo): shrunk to what models read in full, a photo as JPEG and anything else as PNG.</summary>
    public static (Attachment Thing, Texture2D Thumb) Picture(Image image, string name, bool photo)
    {
        if (image.GetFormat() is not (Image.Format.Rgb8 or Image.Format.Rgba8))
            image.Convert(Image.Format.Rgba8);
        Shrink(image, Longest);
        var opaque = image.DetectAlpha() == Image.AlphaMode.None;
        var jpeg = photo && opaque;
        var data = jpeg ? image.SaveJpgToBuffer(0.85f) : image.SavePngToBuffer();
        if (!jpeg && opaque && data.Length > MaxPng)
        {
            data = image.SaveJpgToBuffer(0.9f);             // a busy screenshot: JPEG keeps it within what providers take
            jpeg = true;
        }
        var thumb = (Image)image.Duplicate();
        Shrink(thumb, ThumbSize);
        return (new Attachment(Path.GetFileNameWithoutExtension(name) + (jpeg ? ".jpg" : ".png"), jpeg ? "image/jpeg" : "image/png", data),
                ImageTexture.CreateFromImage(thumb));
    }

    private static Image? Load(byte[] data, string type)
    {
        var image = new Image();
        var loaded = type switch
        {
            "image/png" => image.LoadPngFromBuffer(data),
            "image/jpeg" => image.LoadJpgFromBuffer(data),
            "image/webp" => image.LoadWebpFromBuffer(data),
            _ => Error.Unavailable,
        };
        return loaded == Error.Ok && !image.IsEmpty() ? image : null;
    }

    /// <summary>Turns a photo the way its camera says it was held (EXIF orientation 2 to 8).</summary>
    private static void Upright(Image image, int orientation)
    {
        switch (orientation)
        {
            case 2: image.FlipX(); break;
            case 3: image.Rotate180(); break;
            case 4: image.FlipY(); break;
            case 5: image.Rotate90(ClockDirection.Clockwise); image.FlipX(); break;
            case 6: image.Rotate90(ClockDirection.Clockwise); break;
            case 7: image.Rotate90(ClockDirection.Clockwise); image.FlipY(); break;
            case 8: image.Rotate90(ClockDirection.Counterclockwise); break;
        }
    }

    private static void Shrink(Image image, int longest)
    {
        var size = image.GetSize();
        var scale = (float)longest / Math.Max(size.X, size.Y);
        if (scale < 1)
            image.Resize(Math.Max(1, (int)(size.X * scale)), Math.Max(1, (int)(size.Y * scale)), Image.Interpolation.Lanczos);
    }
}
