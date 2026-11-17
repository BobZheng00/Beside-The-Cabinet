namespace InsertCoin.Mind;

/// <summary>The languages a voice can speak (GPT-SoVITS's codes), and a rough guess at which one a piece of text is in.</summary>
public static class SpokenLanguage
{
    /// <summary>The ones offered for a voice: what the player asked for, and what GPT-SoVITS speaks best.</summary>
    public static readonly string[] Offered = ["en", "ja", "zh"];

    /// <summary>The ones <see cref="Of"/> tells apart: a character's card in one of them is <c>persona.&lt;code&gt;.md</c>.</summary>
    public static readonly string[] Told = ["en", "ja", "ko", "zh"];

    public static string Name(string code) => code switch
    {
        "en" => "English",
        "ja" => "Japanese",
        "zh" => "Chinese",
        "ko" => "Korean",
        "yue" => "Cantonese",
        _ => code,
    };

    /// <summary>
    /// The language a character's voice speaks: the one a voice they already have speaks; else the Language setting;
    /// on Auto (null) the PC's display language (Chinese, Japanese, otherwise English). It is worked out once, when the
    /// voice is given, and kept until the player changes it. What the player writes never changes it.
    /// </summary>
    /// <param name="display">The PC's display language, as Windows names it ("zh", "zh-CN", "ja").</param>
    public static string ForVoice(string? existing, string? setting, string display)
    {
        if (existing is { Length: > 0 })
            return existing;
        if (setting is { Length: > 0 })
            return setting;
        return display.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh"
            : display.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja"
            : "en";
    }

    /// <summary>
    /// "ja" when the text has kana, "ko" for Hangul, "zh" for Chinese characters, "en" for Latin letters, null when
    /// it has none of them (numbers, punctuation). A few Latin letters inside Chinese or Japanese ("我在玩Hades") do
    /// not make it English; a Chinese name inside an English sentence does not make it Chinese.
    /// </summary>
    public static string? Of(string text)
    {
        var (kana, hangul, han, latin) = Scripts(text);
        var asian = kana + hangul + han;
        if (asian == 0)
            return latin > 0 ? "en" : null;
        if (asian * 2 < latin)                       // a character is roughly a word; a word is several letters
            return "en";
        return kana > 0 ? "ja" : hangul > 0 ? "ko" : "zh";
    }

    /// <summary>
    /// The script in a line that a voice of <paramref name="voice"/> can't read aloud, or null when it can say the
    /// line. A line is left unsaid when it holds a script foreign to the voice (Hangul for a Japanese voice, kana for a
    /// Chinese one) or letters but none of the voice's own (only Latin letters for a Japanese voice). Latin letters
    /// mixed into a line in the voice's own script are fine ("Hades IIやる？"), and so is kanji alone for a Japanese
    /// voice ("大丈夫。"). A line with no letters, or a voice in another language, can always be said.
    /// </summary>
    public static string? Unreadable(string voice, string line)
    {
        var (kana, hangul, han, latin) = Scripts(line);
        if (kana + hangul + han + latin == 0)
            return null;
        var (foreign, own) = voice switch
        {
            "ja" => (hangul > 0 ? "Hangul" : null, kana + han),
            "zh" or "yue" => (kana > 0 ? "kana" : hangul > 0 ? "Hangul" : null, han),
            "ko" => (kana > 0 ? "kana" : null, hangul),
            "en" => (kana > 0 ? "kana" : hangul > 0 ? "Hangul" : han > 0 ? "Chinese characters" : null, latin),
            _ => (null, 1),
        };
        return foreign ?? (own > 0 ? null : han > 0 ? "Chinese characters" : "Latin letters");
    }

    private static (int Kana, int Hangul, int Han, int Latin) Scripts(string text)
    {
        int kana = 0, hangul = 0, han = 0, latin = 0;
        foreach (var c in text)
        {
            if (c is >= '぀' and <= 'ヿ' or >= 'ㇰ' and <= 'ㇿ' or >= 'ｦ' and <= 'ﾟ')
                kana++;
            else if (c is >= '가' and <= '힯' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏')
                hangul++;
            else if (c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '豈' and <= '﫿')
                han++;
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'À' and <= 'ɏ')
                latin++;
        }
        return (kana, hangul, han, latin);
    }
}
