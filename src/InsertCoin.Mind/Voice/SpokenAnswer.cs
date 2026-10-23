namespace InsertCoin.Mind;

/// <summary>
/// An answer to her card said aloud: while the card asks (may she open this, look at the screen, count that), a short
/// "yes", "always" or "no" answers it as its buttons would. Anything longer is something else to say to her, so it
/// stays a message (and, as before, counts as no answer to the card). The same words typed answer her questions by
/// text (see <see cref="Questions"/>).
/// </summary>
public static class SpokenAnswer
{
    private static readonly HashSet<string> Yes =
    [
        "yes", "yeah", "yep", "yup", "sure", "ok", "okay", "go ahead", "go for it", "do it", "go on", "allow", "allow it", "allow once",
        "just this once", "this once", "once", "of course", "alright", "all right", "fine", "please do", "sounds good", "yes go ahead",
        "好", "好的", "好啊", "好吧", "可以", "可以的", "可以啊", "行", "行啊", "嗯", "嗯嗯", "是", "是的", "对", "去吧", "允许", "打开吧", "看吧",
        "はい", "いいよ", "うん", "お願い", "おねがい", "どうぞ", "いいね",
        "네", "응", "좋아", "그래",
    ];

    private static readonly HashSet<string> Always =
    [
        "always", "always allow", "allow always", "yes always", "always yes", "from now on", "every time", "yes from now on",
        "总是", "总是允许", "总是可以", "一直", "一直可以", "以后都可以", "以后都行", "永远允许",
        "いつも", "いつもいいよ", "いつでもいいよ", "これからずっと",
        "항상", "항상 허용",
    ];

    private static readonly HashSet<string> No =
    [
        "no", "nope", "nah", "no thanks", "no thank you", "dont", "do not", "not now", "not yet", "cancel", "stop", "deny", "never mind",
        "nevermind", "no dont",
        "不", "不要", "不用", "不用了", "别", "算了", "不行", "不了", "还没", "还没有",
        "いいえ", "いや", "だめ", "ダメ", "やめて", "まだ",
        "아니", "아니요", "아니야", "싫어", "아직",
    ];

    // words that may come before or after an answer without changing it
    private static readonly string[] Around = ["um", "uh", "oh", "well", "please", "thanks", "thank you", "谢谢", "吧", "啊", "ね"];

    /// <summary>The card's answer <paramref name="heard"/> gives, or null when it is something else.</summary>
    public static Answer? Of(string heard)
    {
        var said = Plain(heard);
        for (var trimmed = true; trimmed;)
        {
            trimmed = false;
            foreach (var word in Around)
                if (said != word && said.StartsWith(word + " ", StringComparison.Ordinal))
                    (said, trimmed) = (said[(word.Length + 1)..], true);
                else if (said != word && said.EndsWith(" " + word, StringComparison.Ordinal))
                    (said, trimmed) = (said[..^(word.Length + 1)], true);
        }
        var words = said.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is > 0 and <= MostWords ? Phrases(words, 0) : null;
    }

    /// <summary>Longer than this, it's something to say to her, not an answer.</summary>
    private const int MostWords = 6;

    /// <summary>
    /// The answer the words from <paramref name="from"/> on make, as one or more answer phrases that agree ("yeah, go
    /// ahead"; "yes, always" is always); null when they don't all answer, or disagree.
    /// </summary>
    private static Answer? Phrases(string[] words, int from)
    {
        for (var to = words.Length; to > from; to--)
        {
            if (One(string.Join(' ', words[from..to])) is not { } first)
                continue;
            if (to == words.Length)
                return first;
            if (Phrases(words, to) is { } rest && Agree(first, rest) is { } both)
                return both;
        }
        return null;
    }

    private static Answer? Agree(Answer a, Answer b) =>
        a == b ? a : (a, b) is (Answer.Once, Answer.Always) or (Answer.Always, Answer.Once) ? Answer.Always : null;

    private static Answer? One(string said) => Match(said) ?? (said.Length > 1 && Particles.Contains(said[^1]) ? Match(said[..^1]) : null);

    // what Chinese and Japanese put at the end of a short answer ("好的吧", "いいよね")
    private static readonly char[] Particles = ['吧', '啊', '呀', '哦', '呢', 'ね', 'よ', 'な'];

    private static Answer? Match(string said) =>
        Always.Contains(said) ? Answer.Always : Yes.Contains(said) ? Answer.Once : No.Contains(said) ? Answer.No : null;

    /// <summary>Lower case, without punctuation, one space between words.</summary>
    internal static string Plain(string heard)
    {
        var kept = new string([.. heard.ToLowerInvariant().Select(c => char.IsPunctuation(c) || char.IsSymbol(c) ? (c == '\'' || c == '’' ? '\0' : ' ') : c).Where(c => c != '\0')]);
        return string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
