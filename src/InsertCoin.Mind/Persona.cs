namespace InsertCoin.Mind;

/// <summary>
/// Who the character is: their name and, in their author's words, how they are and how they talk; from a character
/// card also a lorebook (background brought up by keywords) and a greeting.
/// </summary>
public sealed record Persona(string Name, string Description)
{
    public IReadOnlyList<LoreEntry> Lore { get; init; } = [];
    public string? Greeting { get; init; }
    /// <summary>
    /// What keeps the character steady late in a long conversation: the card's post-history instructions and
    /// character's note, read at the end of every turn (see <see cref="Prompt.Build"/>); null for the plain reminder.
    /// </summary>
    public string? Steady { get; init; }
    /// <summary>The name the player asked to be called by (Getting started), or null when they haven't given one.</summary>
    public string? Player { get; init; }
    /// <summary>
    /// The character's own cards in other languages (<c>persona.zh.md</c>), by <see cref="SpokenLanguage"/>'s codes: the
    /// one she uses while she talks in that language (see <see cref="Conversation.CardLanguage"/>). None for a card the
    /// player gave her, which stands for her in every language.
    /// </summary>
    public IReadOnlyDictionary<string, Persona> Languages { get; init; } = new Dictionary<string, Persona>();

    /// <summary>For a character nobody has described: friendly, and nothing assumed about who they are.</summary>
    public static Persona Plain(string name) => new(name,
        $"{name} is a friendly companion who keeps the player company: warm, a little playful, honest, and glad when " +
        "the player does well. They enjoy games as much as the player does, and want the player to get their practice done.");

    /// <summary>
    /// A persona file's text (<c>persona.md</c>) as a persona, its {{user}} and {{char}} filled in as a card's are
    /// (<see cref="CharacterCard.Fill"/>): with the name the player asked to be called by, or "the player" ("玩家" in a
    /// card in Chinese).
    /// </summary>
    /// <param name="language">The language the file is written in (<c>persona.zh.md</c>), or null for <c>persona.md</c>.</param>
    public static Persona Written(string name, string text, string? playerName, string? language = null) =>
        new(name, CharacterCard.Fill(text, name, playerName, language == "zh" ? "玩家" : "the player")) { Player = playerName };

    /// <summary>
    /// The first sentence of who they are, for the first run's choice of who keeps the player company: up to a full
    /// stop before a space or a line break, or a 。; empty when there is none in the first 240 characters.
    /// </summary>
    public static string FirstSentence(string text) =>
        System.Text.RegularExpressions.Regex.Match(text, @"\.(?=\s)|。") is { Success: true, Index: > 0 and < 240 } end ? text[..(end.Index + 1)] : "";

    /// <summary>Lorebook entries that apply now: the constant ones, and those whose key appears in what was just said.</summary>
    public IEnumerable<LoreEntry> LoreFor(string text) =>
        Lore.Where(entry => entry.Constant || entry.Keys.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase)));
}
