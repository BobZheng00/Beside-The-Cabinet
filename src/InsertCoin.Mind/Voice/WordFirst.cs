namespace InsertCoin.Mind;

/// <summary>A piece of her reply as it came: words shown, or a stage direction (a [say] among them) with its payload.</summary>
public readonly record struct Piece(string? Words, string? Direction = null, string? Payload = null);

/// <summary>
/// What she brings up herself with nothing else to show it (a check-in, a word on rest, standing up) comes after a word
/// first, as people do: she calls the player (their name, say) and goes on once they answer ("yes?"), so what she says
/// reaches someone who is listening, not someone who has only just noticed she is talking. Her whole remark is written
/// first; its first sentence is her call, and the rest waits for the answer (docs/specs/2026-10-09-a-word-first.md).
/// </summary>
public static class WordFirst
{
    /// <summary>
    /// What her mind is told with the remark, so that it opens with a call of its own and writes the rest with it: told
    /// only that the rest came once they answered, a model wrote the call alone and stopped (2026-10-10).
    /// </summary>
    public const string Ask = "Write all of it in this reply, opening with a short sentence of its own that only gets their attention " +
                              "(their name, say). The cabinet says that one first and holds back the rest until they answer; you won't be asked again.";

    /// <summary>What the player's answer to her call means.</summary>
    public enum Reply { GoOn, NotNow }

    private static readonly HashSet<string> Later =
    [
        "later", "not now", "not right now", "wait", "hold on", "hang on", "one sec", "one second", "just a sec", "just a second",
        "just a moment", "a moment", "give me a sec", "give me a minute", "busy", "im busy", "i am busy",
        "等一下", "等等", "等会", "等会儿", "等一会", "待会", "待会儿", "稍等", "稍等一下", "一会儿", "一会儿再说", "回头再说", "以后再说",
        "晚点再说", "在忙", "我在忙", "忙着呢", "现在不行", "先别",
        "あとで", "後で", "あとにして", "後にして", "ちょっと待って", "待って", "まって", "今忙しい", "忙しい", "今はちょっと",
        "나중에", "잠깐만", "잠깐", "바빠",
    ];

    /// <summary>
    /// What <paramref name="heard"/> says to her call: go on (a short answer: "yes?", "what is it?", "嗯？"), not now
    /// ("not now", "later", "wait"), or null when it is something else to say to her, which stays a message.
    /// </summary>
    public static Reply? Of(string heard)
    {
        var plain = SpokenAnswer.Plain(heard);
        if (plain.Length == 0)
            return null;
        if (SpokenAnswer.Of(heard) == Answer.No || Later.Contains(plain))
            return Reply.NotNow;
        var words = plain.Split(' ').Length;
        var wide = plain.Count(c => c >= '⺀');                // Chinese, Japanese, Korean: a character is about a word
        return (wide > 0 ? wide <= 6 && words <= 2 : words <= 4) ? Reply.GoOn : null;
    }

    /// <summary>
    /// Her remark cut after its call: before its second [say] when it gives each sentence one, else after the first
    /// sentence it shows. Then is empty when the remark is a single sentence.
    /// </summary>
    public static (List<Piece> Call, List<Piece> Then) Split(IReadOnlyList<Piece> pieces)
    {
        var says = Enumerable.Range(0, pieces.Count).Where(i => pieces[i].Direction == "say").Take(2).ToList();
        if (says.Count == 2)
            return ([.. pieces.Take(says[1])], [.. pieces.Skip(says[1])]);
        var shown = string.Concat(pieces.Select(p => p.Words));
        if (new Sentences().Feed(shown).FirstOrDefault() is not { } first)
            return ([.. pieces], []);                       // one sentence (the last is only known at the end)
        var end = shown.IndexOf(first, StringComparison.Ordinal) + first.Length;
        List<Piece> call = [], rest = [];
        var at = 0;
        foreach (var piece in pieces)
        {
            if (piece.Words is not { } words)
            {
                (at < end ? call : rest).Add(piece);
                continue;
            }
            if (at + words.Length <= end)
                call.Add(piece);
            else if (at >= end)
                rest.Add(piece);
            else
            {
                call.Add(new Piece(words[..(end - at)]));
                rest.Add(new Piece(words[(end - at)..]));
            }
            at += words.Length;
        }
        return (call, rest);
    }

    /// <summary>The words the pieces show, together.</summary>
    public static string Shown(IEnumerable<Piece> pieces) => string.Concat(pieces.Select(p => p.Words)).Trim();
}
