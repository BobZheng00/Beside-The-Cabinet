using System.Text;
using System.Text.Json;

namespace InsertCoin.Mind;

/// <summary>A lorebook entry: background that comes up when one of its keys is mentioned, or always if constant.</summary>
public sealed record LoreEntry(IReadOnlyList<string> Keys, string Content, bool Constant = false);

/// <summary>
/// A character card as the SillyTavern family of chat apps share them (V1, V2 "chara_card_v2", V3 "chara_card_v3"):
/// JSON, either on its own or base64 in a PNG's text chunk ("chara", or "ccv3" for V3). People share these with
/// a picture, so a card gives a model they import a personality, a way of talking and a lorebook.
/// </summary>
public sealed record CharacterCard(string Name, string Description, string Personality, string Scenario, string Greeting,
                                   string Examples, string AuthorNotes, IReadOnlyList<LoreEntry> Lore)
{
    /// <summary>Longest persona taken from a card (characters); some cards run to many thousands of words.</summary>
    public const int PersonaLimit = 8000;
    /// <summary>Longest text kept from a card's post-history instructions and character's note together.</summary>
    public const int SteadyLimit = 600;

    /// <summary>The card's post-history instructions: what its author wants read after the conversation, just before each reply.</summary>
    public string PostHistory { get; init; } = "";
    /// <summary>The card's character's note (<c>extensions.depth_prompt</c>), which its apps put a few messages from the end.</summary>
    public string DepthNote { get; init; } = "";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads a card from a .png or .json file's bytes; null when there is no card in it.</summary>
    public static CharacterCard? Read(byte[] file)
    {
        try
        {
            if (file.AsSpan().StartsWith(PngSignature))
                return FromPng(file);
            return FromJson(Encoding.UTF8.GetString(file).TrimStart('﻿'));
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The card as a persona, with its {{char}} and {{user}} placeholders filled in. Description, personality and
    /// scenario become the description; the example dialogue and the author's notes follow; the lorebook comes along.
    /// </summary>
    public Persona ToPersona(string? playerName)
    {
        string Fill(string text) => text.Replace("{{char}}", Name, StringComparison.OrdinalIgnoreCase)
            .Replace("<BOT>", Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{{user}}", playerName ?? "the player", StringComparison.OrdinalIgnoreCase)
            .Replace("<USER>", playerName ?? "the player", StringComparison.OrdinalIgnoreCase).Trim();
        var parts = new List<string> { Fill(Description) };
        if (Personality.Trim().Length > 0)
            parts.Add($"Personality: {Fill(Personality)}");
        if (Scenario.Trim().Length > 0)
            parts.Add($"Background: {Fill(Scenario)}");
        if (Examples.Trim().Length > 0)
            parts.Add($"How {Name} talks, for example:\n{Fill(Examples.Replace("<START>", "", StringComparison.OrdinalIgnoreCase))}");
        if (AuthorNotes.Trim().Length > 0)
            parts.Add($"Notes from the card's author: {Fill(AuthorNotes)}");
        var description = Prompt.Fit(string.Join("\n\n", parts.Where(p => p.Length > 0)), PersonaLimit);     // at a whole sentence
        var steady = string.Join('\n', new[] { PostHistory, DepthNote }.Where(t => t.Trim().Length > 0).Select(Fill));
        return new Persona(Name, description)
        {
            Lore = [.. Lore.Select(l => l with { Content = Fill(l.Content) })],
            Greeting = Greeting.Trim().Length > 0 ? Fill(Greeting) : null,
            Steady = steady.Length == 0 ? null : Prompt.Fit(steady, SteadyLimit),
        };
    }

    private static CharacterCard? FromPng(byte[] png)
    {
        string? v2 = null, v3 = null;
        var pos = 8;
        while (pos + 8 <= png.Length)
        {
            var length = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            var kind = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (length < 0 || pos + 12 + length > png.Length)
                break;
            var body = png.AsSpan(pos + 8, length);
            if (kind is "tEXt" or "iTXt" && body.IndexOf((byte)0) is var nul and > 0)
            {
                var keyword = Encoding.Latin1.GetString(body[..nul]).ToLowerInvariant();
                var text = body[(nul + 1)..];
                if (kind == "iTXt")
                {
                    // compression flag, method, then language and translated keyword, each ending in a zero byte
                    text = text[2..];
                    for (var skip = 0; skip < 2; skip++)
                        text = text[(text.IndexOf((byte)0) + 1)..];
                }
                var value = Encoding.UTF8.GetString(text).Trim();
                if (keyword == "ccv3")
                    v3 = value;
                else if (keyword == "chara")
                    v2 = value;
            }
            if (kind == "IEND")
                break;
            pos += 12 + length;
        }
        var base64 = v3 ?? v2;
        return base64 is null ? null : FromJson(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
    }

    private static CharacterCard? FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;   // V2/V3 nest it, V1 does not
        var name = Text(data, "name");
        if (name.Length == 0)
            return null;
        var lore = new List<LoreEntry>();
        if (data.TryGetProperty("character_book", out var book) && book.ValueKind == JsonValueKind.Object
            && book.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var entry in entries.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                if (entry.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                    continue;
                var keys = entry.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.Array
                    ? k.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).ToList()
                    : [];
                var constant = entry.TryGetProperty("constant", out var c) && c.ValueKind == JsonValueKind.True;
                var content = Text(entry, "content");
                if (content.Length > 0 && (keys.Count > 0 || constant))
                    lore.Add(new LoreEntry(keys, content, constant));
            }
        var depth = data.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object
                    && extensions.TryGetProperty("depth_prompt", out var note) && note.ValueKind == JsonValueKind.Object
            ? Text(note, "prompt")
            : "";
        return new CharacterCard(name, Text(data, "description"), Text(data, "personality"), Text(data, "scenario"), Text(data, "first_mes"),
                                 Text(data, "mes_example"), Text(data, "system_prompt"), lore)
        {
            PostHistory = Text(data, "post_history_instructions"),
            DepthNote = depth,
        };
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
}
