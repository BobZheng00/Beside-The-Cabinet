namespace InsertCoin.Mind;

/// <summary>Who the character is: their name and, in their author's words, how they are and how they talk.</summary>
public sealed record Persona(string Name, string Description)
{
    /// <summary>For a character nobody has described: friendly, and nothing assumed about who they are.</summary>
    public static Persona Plain(string name) => new(name,
        $"{name} is a friendly companion who keeps the player company: warm, a little playful, honest, and glad when " +
        "the player does well. They enjoy games as much as the player does, and want the player to get their practice done.");
}
