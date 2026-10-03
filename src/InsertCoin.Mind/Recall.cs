using System.Globalization;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>Something remembered for the moment, as it is put in front of her.</summary>
public sealed record Memory(string Text, long? FactId = null);

/// <summary>
/// Picks what she remembers when the player says something: facts, episodes and old lines of the conversation that
/// share words with it, ranked by relevance + recency + importance (Generative Agents). Words are compared as
/// they are written; Chinese and Japanese, which have no spaces, as overlapping pairs of characters. Search by
/// meaning (embeddings) can replace the relevance part later without changing the rest.
/// </summary>
public static class Recall
{
    public const int Budget = 450;              // tokens of memories per turn at most
    private const int MaxOldLines = 2;
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly HashSet<string> Common =
    [
        "the", "and", "you", "your", "for", "are", "was", "but", "not", "with", "this", "that", "have", "has", "had", "what",
        "when", "how", "can", "just", "will", "about", "from", "they", "she", "him", "her", "his", "its", "our", "out", "too",
        "did", "does", "doing", "get", "got", "now", "yes", "yeah", "okay", "really", "there", "then", "than", "them", "who",
        "why", "all", "any", "some", "one", "player",
    ];

    public static IReadOnlyList<Memory> Find(string said, IReadOnlyList<Fact> facts, IReadOnlyList<Episode> episodes, IReadOnlyList<Line> oldLines,
                                             string name, DateTimeOffset now)
    {
        var query = Terms(said).ToHashSet();
        query.ExceptWith(Terms(name));                      // her own name says nothing about what to remember
        var candidates = new List<(Memory Memory, HashSet<string> Terms, DateTimeOffset At, int Importance, bool Old, bool Pinned)>();
        foreach (var fact in facts)
            candidates.Add((new Memory(fact.Text, fact.Id), Terms(fact.Text).ToHashSet(), fact.Recalled ?? fact.Learned, fact.Importance, false, fact.Pinned));
        foreach (var episode in episodes)
            candidates.Add((new Memory($"{episode.At.ToString("ddd d MMM", English)}: {episode.Text}"), Terms(episode.Text).ToHashSet(), episode.At, 5, false, false));
        foreach (var line in oldLines.Where(l => l.Speaker != Speaker.Cabinet))
            candidates.Add((new Memory($"On {line.At.ToString("ddd d MMM", English)} {(line.Speaker == Speaker.User ? "the player" : name)} said: \"{line.Text}\""),
                            Terms(line.Text).ToHashSet(), line.At, 3, true, false));
        if (candidates.Count == 0)
            return [];

        // rarer words count for more
        var documents = candidates.Count;
        var seenIn = new Dictionary<string, int>();
        foreach (var term in candidates.SelectMany(c => c.Terms))
            seenIn[term] = seenIn.GetValueOrDefault(term) + 1;
        double Weight(string term) => Math.Log(1 + documents / (double)seenIn.GetValueOrDefault(term, 1));

        var scored = candidates.Select(c =>
        {
            var overlap = c.Terms.Where(query.Contains).Sum(Weight) / Math.Sqrt(Math.Max(c.Terms.Count, 1));
            return (c.Memory, c.Old, c.Pinned, Overlap: overlap, c.At, c.Importance);
        }).ToList();
        var best = scored.Max(s => s.Overlap);
        var chosen = new List<Memory>();
        var used = 0;
        var quoted = 0;
        foreach (var s in scored
                     .Where(s => s.Pinned || (best > 0 && s.Overlap / best >= 0.3))
                     .OrderByDescending(s => (s.Pinned ? 2 : 0) + (best > 0 ? s.Overlap / best : 0)
                                             + 0.5 * Math.Exp(-(now - s.At).TotalDays / 30) + 0.3 * s.Importance / 10.0))
        {
            if (s.Old && ++quoted > MaxOldLines)
                continue;
            var cost = Prompt.Tokens(s.Memory.Text) + 2;
            if (used + cost > Budget)
                continue;
            used += cost;
            chosen.Add(s.Memory);
        }
        return chosen;
    }

    /// <summary>Whether two facts say the same thing in nearly the same words (so a second telling is not kept twice).</summary>
    public static bool Same(string a, string b)
    {
        var x = Terms(a).ToHashSet();
        var y = Terms(b).ToHashSet();
        if (x.Count == 0 || y.Count == 0)
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        return x.Intersect(y).Count() / (double)x.Union(y).Count() >= 0.75;
    }

    /// <summary>The words of a text, lower-cased: Latin-script words of three letters or more, and pairs of CJK characters.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        var word = new StringBuilder();
        char? previousCjk = null;
        foreach (var c in text.ToLowerInvariant())
        {
            if (IsCjk(c))
            {
                if (previousCjk is { } p)
                    yield return $"{p}{c}";
                previousCjk = c;
                if (Flush(word) is { } w)
                    yield return w;
                continue;
            }
            previousCjk = null;
            if (char.IsLetterOrDigit(c))
                word.Append(c);
            else if (Flush(word) is { } w)
                yield return w;
        }
        if (Flush(word) is { } last)
            yield return last;
    }

    private static string? Flush(StringBuilder word)
    {
        var w = word.ToString();
        word.Clear();
        return w.Length >= 3 && !Common.Contains(w) ? Stem(w) : null;
    }

    /// <summary>A light English stem, so "exams" finds "exam" and "solving", "solved" and "solve" find each other.</summary>
    private static string Stem(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing"))
            w = w[..^3];
        else if (w.Length > 4 && (w.EndsWith("ed") || w.EndsWith("es")))
            w = w[..^2];
        else if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss"))
            w = w[..^1];
        return w.Length > 3 && w.EndsWith('e') ? w[..^1] : w;
    }

    private static bool IsCjk(char c) => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '가' and <= '힯';
}
