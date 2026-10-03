namespace InsertCoin.Mind;

/// <summary>The languages a voice can speak (GPT-SoVITS's codes), and a rough guess at which one a piece of text is in.</summary>
public static class SpokenLanguage
{
    /// <summary>The ones offered for a voice: what the player asked for, and what GPT-SoVITS speaks best.</summary>
    public static readonly string[] Offered = ["en", "ja", "zh"];

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
    /// "ja" when the text has kana, "ko" for Hangul, "zh" for Chinese characters, "en" for Latin letters, null when
    /// it has none of them (numbers, punctuation). A few Latin letters inside Chinese or Japanese ("我在玩Hades") do
    /// not make it English; a Chinese name inside an English sentence does not make it Chinese.
    /// </summary>
    public static string? Of(string text)
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
        var asian = kana + hangul + han;
        if (asian == 0)
            return latin > 0 ? "en" : null;
        if (asian * 2 < latin)                       // a character is roughly a word; a word is several letters
            return "en";
        return kana > 0 ? "ja" : hangul > 0 ? "ko" : "zh";
    }
}
