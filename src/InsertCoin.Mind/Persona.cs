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

    /// <summary>For a character nobody has described: friendly, and nothing assumed about who they are.</summary>
    public static Persona Plain(string name) => new(name,
        $"{name} is a friendly companion who keeps the player company: warm, a little playful, honest, and glad when " +
        "the player does well. They enjoy games as much as the player does, and want the player to get their practice done.");

    /// <summary>Lorebook entries that apply now: the constant ones, and those whose key appears in what was just said.</summary>
    public IEnumerable<LoreEntry> LoreFor(string text) =>
        Lore.Where(entry => entry.Constant || entry.Keys.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase)));
}
