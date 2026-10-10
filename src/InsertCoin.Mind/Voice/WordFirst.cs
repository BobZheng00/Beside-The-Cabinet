namespace InsertCoin.Mind;

/// <summary>A piece of her reply as it came: words shown, or a stage direction (a [say] among them) with its payload.</summary>
public readonly record struct Piece(string? Words, string? Direction = null, string? Payload = null);

/// <summary>
/// What she brings up herself with nothing else to show it (a check-in, a word on rest, standing up) comes after a word
/// first, as people do: she calls the player (their name, say) and goes on once they answer ("yes?"), so what she says
/// reaches someone who is listening, not someone who has only just noticed she is talking. Her call is asked for on its
/// own, alongside her remark, which is written as any other and kept whole until they answer: one reply asked to carry
/// both came back as the call alone on the user's model (2026-10-10; docs/specs/2026-10-09-a-word-first.md).
/// </summary>
public static class WordFirst
{
    /// <summary>What her mind is asked for her call, on its own: only that, so that writing only that is right.</summary>
    public const string Ask = "In a moment you will bring something up with the player on your own. First, only get their attention: " +
                              "a few words of your own, in character (their name, say). Nothing else yet.";

    /// <summary>
    /// What her mind is told with the remark itself, so that she doesn't call them a second time: told only to go
    /// straight to it, the user's model still opened with their name again ("BobSmith? BobSmith, stand up…", 2026-10-10).
    /// </summary>
    public const string Called = "You have just called them by name to get their attention, and they answered: go straight to it, " +
                                 "without their name or a greeting.";

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
    /// What her mind wrote for her call, cut after its first sentence, as it may have written more than a call: before
    /// its second [say] when it gives each sentence one, else after the first sentence it shows. Then is what was cut
    /// away, empty when it was a single sentence.
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

    /// <summary>The pieces as her mind writes them, directions in brackets (how her last call is kept).</summary>
    public static string Written(IEnumerable<Piece> pieces) =>
        string.Concat(pieces.Select(p => p.Words ?? (p.Payload is null ? $"[{p.Direction}]" : $"[{p.Direction}: {p.Payload}]")));

    /// <summary>What her mind wrote, as pieces: her words, and her directions where they fall.</summary>
    public static List<Piece> Pieces(string written)
    {
        var pieces = new List<Piece>();
        var stage = new StageDirections((name, payload) => pieces.Add(new Piece(null, name, payload)), words => pieces.Add(new Piece(words)));
        if ((stage.Feed(written) + stage.Finish()) is { Length: > 0 } rest)
            pieces.Add(new Piece(rest));
        return pieces;
    }
}
