using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// Something the player hands her with what they say: a picture (a photo, a screenshot), a PDF, or a text file (notes,
/// code, data). The model takes pictures and PDFs as they are, when it can; a text file's words go in with the message.
/// None of it is kept: in its place her line about what she saw is (see <see cref="Conversation"/>, <c>[saw]</c>), and
/// a short text file word for word.
/// </summary>
/// <param name="Name">The file's name as the player knows it ("steps.jpg", "solution.py").</param>
/// <param name="MediaType">"image/jpeg", "application/pdf", "text/plain"…</param>
public sealed record Attachment(string Name, string MediaType, byte[] Data)
{
    /// <summary>The most of a text file that goes in: about 15,000 tokens of English.</summary>
    public const int TextLimit = 60_000;

    /// <summary>A text file this short is kept in the conversation word for word; a longer one only as her line about it.</summary>
    public const int KeptWordForWord = 2_000;

    /// <summary>When a photo was taken, as its EXIF block says (see <see cref="Exif"/>); null when it doesn't say.</summary>
    public DateTime? Taken { get; init; }

    public bool Picture => MediaType.StartsWith("image/", StringComparison.Ordinal);
    public bool Pdf => MediaType == "application/pdf";

    /// <summary>Taken as text: its words go in with the message.</summary>
    public bool Text => !Picture && !Pdf;

    /// <summary>What it is, in a word: "picture", "PDF", "file".</summary>
    public string Kind => Picture ? "picture" : Pdf ? "PDF" : "file";

    /// <summary>A text file's words (UTF-8, or UTF-16 with its mark), cut at <see cref="TextLimit"/>.</summary>
    public string Words()
    {
        var text = Data is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(Data, 2, Data.Length - 2)
            : Data is [0xFE, 0xFF, ..] ? Encoding.BigEndianUnicode.GetString(Data, 2, Data.Length - 2)
            : Encoding.UTF8.GetString(Data).TrimStart('﻿');
        text = text.Replace("\r\n", "\n");
        return text.Length <= TextLimit ? text : text[..TextLimit] + "\n[… cut here: the file goes on]";
    }

    /// <summary>
    /// What the file is to her, by its name and, for one with an unknown ending, its first bytes: a picture, a PDF,
    /// text, or null for something she can't take (a program, an archive, a video).
    /// </summary>
    public static string? TypeOf(string name, ReadOnlySpan<byte> start) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        ".exe" or ".dll" or ".zip" or ".7z" or ".rar" or ".mp4" or ".mkv" or ".mov" or ".mp3" or ".wav" or ".flac" or ".ogg" or ".heic" or ".avif"
            or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".vrm" or ".glb" or ".onnx" or ".ckpt" or ".pth" => null,
        _ => LooksLikeText(start) ? "text/plain" : null,
    };

    /// <summary>Text, as far as its start shows: no NUL bytes, and readable as UTF-8 (or UTF-16 with its mark).</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> start)
    {
        if (start is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
            return true;
        if (start.IndexOf((byte)0) >= 0)
            return false;
        var cut = start.Length;
        while (cut > 0 && cut > start.Length - 4 && (start[cut - 1] & 0xC0) == 0x80)
            cut--;                                          // a character split at the end of what was read is fine
        if (cut > 0 && start[cut - 1] >= 0xC0)
            cut--;
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(start[..cut]);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
