namespace InsertCoin.Mind;

/// <summary>
/// Her thinking sounds: short sounds in her own voice ("um…", "えっと…") while the player waits for her, so the wait
/// sounds like her thinking rather than nothing (decided with the user, 2026-10-09: varied, natural, the waiting
/// pleasant). Made once in her voice when it loads; one plays only when her words are slow to come (a model that
/// thinks, a tool running), never on a quick reply: when nothing new has come for a second and she is quiet, at most
/// twice a reply, a while apart, and never one of the last two again.
/// </summary>
public sealed class Thinking(Random? random = null)
{
    /// <summary>How long without new words, and without her making a sound, before one plays.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

    /// <summary>How long after the first before a second may play, if she is still thinking.</summary>
    public static readonly TimeSpan Between = TimeSpan.FromSeconds(6);

    public const int MostPerReply = 2;

    private readonly Random _random = random ?? Random.Shared;
    private readonly Queue<int> _recent = new();

    /// <summary>What she says to think aloud in her voice's language; none for a language not listed here.</summary>
    public static IReadOnlyList<string> Sounds(string language) => language switch
    {
        "en" => ["Um…", "Hmm…", "Let's see…", "Uh, let me think…", "Okay, um…", "Right…", "Oh, hmm…", "Well…"],
        "zh" => ["嗯……", "我想想……", "让我看看……", "唔……", "这个嘛……", "嗯，等一下……", "那个……"],
        "ja" => ["えっと…", "うーん…", "そうだなぁ…", "ちょっと待ってね…", "んー…", "えーとね…", "うーんと…"],
        "ko" => ["음…", "어디 보자…", "잠깐만…", "글쎄…", "음, 생각해 볼게…"],
        _ => [],
    };

    /// <summary>
    /// Whether one is due: a reply to the player is under way, she is quiet, nothing new has come for
    /// <see cref="Quiet"/>, and since her last sound it has been <see cref="Quiet"/> (before the first) or
    /// <see cref="Between"/> (after it); at most <see cref="MostPerReply"/>.
    /// </summary>
    /// <param name="sinceWords">Since the reply's last new words came in (since it was asked, before any).</param>
    /// <param name="sinceSound">Since she last made a sound (since it was asked, before any).</param>
    /// <param name="played">Thinking sounds already played in this reply.</param>
    public static bool Due(bool underway, bool speaking, TimeSpan sinceWords, TimeSpan sinceSound, int played) =>
        underway && !speaking && played < MostPerReply && sinceWords >= Quiet && sinceSound >= (played == 0 ? Quiet : Between);

    /// <summary>Which of <paramref name="count"/> sounds to play: at random, but not one of the last two played.</summary>
    public int Pick(int count)
    {
        var fresh = Enumerable.Range(0, count).Where(i => !_recent.Contains(i)).ToList();
        var pick = fresh.Count > 0 ? fresh[_random.Next(fresh.Count)] : _random.Next(count);
        _recent.Enqueue(pick);
        while (_recent.Count > Math.Min(2, count - 1))
            _recent.Dequeue();
        return pick;
    }
}
