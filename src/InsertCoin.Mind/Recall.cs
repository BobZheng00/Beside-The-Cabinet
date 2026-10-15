using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InsertCoin.Mind;

/// <summary>Something remembered for the moment, as it is put in front of her, and where it came from (for her reply to keep).</summary>
public sealed record Memory(string Text, long? FactId = null, Origin? From = null);

/// <summary>What the player said and what her facts and notes mean, as vectors from one embedding model.</summary>
public sealed record Meaning(float[] Query, EmbeddingModel Model, IReadOnlyDictionary<long, float[]> Facts, IReadOnlyDictionary<long, float[]>? Observations = null);

/// <summary>
/// Picks what she remembers when the player says something: facts (and what used to be true), her notes out of view and
/// old lines of the conversation,
/// ranked by relevance + retention + importance (Generative Agents, with MemoryBank's forgetting curve in place of
/// plain recency). Relevance is the stronger of shared words (Chinese and Japanese, which have no spaces, as
/// overlapping pairs of characters) and, when a local model is loaded, closeness in meaning. Faded facts are left out.
/// </summary>
public static class Recall
{
    // tokens of memories per turn at most, and passages of old conversation among them: measured on LoCoMo (2026-10-11),
    // the evidence reached her for 31% of questions at 450 and 2 (with a line and the next), 56% at 2,000 and 6 (whole
    // passages, 10 found); 2,500 and 8 gave 58%, not worth another thousand tokens on every message
    public const int Budget = 2000;
    private const int MaxOldLines = 6;
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly HashSet<string> Common =
    [
        "the", "and", "you", "your", "for", "are", "was", "but", "not", "with", "this", "that", "have", "has", "had", "what",
        "when", "how", "can", "just", "will", "about", "from", "they", "she", "him", "her", "his", "its", "our", "out", "too",
        "did", "does", "doing", "get", "got", "now", "yes", "yeah", "okay", "really", "there", "then", "than", "them", "who",
        "why", "all", "any", "some", "one", "player",
    ];

    /// <param name="excerpts">Lines from the conversation before the window that may relate (see <see cref="History.Search"/>).</param>
    /// <param name="observations">Notes of hers out of view: older than what she sees of her log, or condensed (see <see cref="Log.Newest"/>).</param>
    /// <param name="formerly">Facts that are no longer true (changed, or ended), shown with when they stopped being: what used to be.</param>
    public static IReadOnlyList<Memory> Find(string said, IReadOnlyList<Fact> facts, IReadOnlyList<Excerpt> excerpts, string name, DateTimeOffset now,
                                             Meaning? meaning = null, IReadOnlyList<Observation>? observations = null, IReadOnlyList<Fact>? formerly = null)
    {
        var query = Terms(said).ToHashSet();
        query.ExceptWith(Terms(name));                      // her own name says nothing about what to remember
        var candidates = new List<(Memory Memory, HashSet<string> Terms, float[]? Vector, double Retention, int Importance, bool Old, bool Pinned)>();
        foreach (var fact in facts.Where(f => !f.Faded(now)))
            candidates.Add((new Memory(Prompt.Dated(fact), fact.Id, new(Origin.Fact, fact.Id)), Terms(fact.Text).ToHashSet(), meaning?.Facts.GetValueOrDefault(fact.Id),
                            fact.Retention(now), fact.Importance, false, fact.Pinned));
        foreach (var fact in formerly ?? [])
            candidates.Add((new Memory($"Until {fact.Retired?.ToString("d MMM yyyy", English)}: {fact.Text}", From: new(Origin.Fact, fact.Id)), Terms(fact.Text).ToHashSet(),
                            meaning?.Facts.GetValueOrDefault(fact.Id), Math.Exp(-(now - (fact.Retired ?? now)).TotalDays / 60), fact.Importance / 2, false, false));
        foreach (var excerpt in excerpts)
            candidates.Add((new Memory($"Said on {excerpt.At.ToString("ddd d MMM", English)} ({When.Ago(excerpt.At, now)}): {excerpt.Text.Replace('\n', ' ')}",
                                       From: excerpt.Passage > 0 ? new(Origin.Passage, excerpt.Passage) : null),
                            Terms(excerpt.Text).ToHashSet(), excerpt.Vector, 1.0, 3, true, false));        // old talk comes back by relevance, whatever its age
        foreach (var o in observations ?? [])
            candidates.Add((new Memory($"{o.At.ToString("ddd d MMM yyyy", English)} ({When.Ago(o.At, now)}): {o.Text}", From: new(Origin.Note, o.Id)), Terms(o.Text).ToHashSet(),
                            meaning?.Observations?.GetValueOrDefault(o.Id), Math.Exp(-(now - o.At).TotalDays / 60),
                            o.Priority switch { Priority.High => 8, Priority.Medium => 5, _ => 3 }, false, false));
        if (candidates.Count == 0)
            return [];

        // shared words: rarer words count for more, scored against the best match
        var documents = candidates.Count;
        var seenIn = new Dictionary<string, int>();
        foreach (var term in candidates.SelectMany(c => c.Terms))
            seenIn[term] = seenIn.GetValueOrDefault(term) + 1;
        double Weight(string term) => Math.Log(1 + documents / (double)seenIn.GetValueOrDefault(term, 1));
        var overlaps = candidates.Select(c => c.Terms.Where(query.Contains).Sum(Weight) / Math.Sqrt(Math.Max(c.Terms.Count, 1))).ToList();
        var bestOverlap = overlaps.Max();
        // meaning: closeness to the query, read against the best match (see EmbeddingModel)
        var cosines = candidates.Select(c => c.Vector is { } v && meaning is not null ? LocalEmbedder.Cosine(meaning.Query, v) : (float?)null).ToList();
        var bestCosine = cosines.Max() ?? 0;

        var scored = candidates.Select((c, i) =>
        {
            var words = bestOverlap > 0 ? overlaps[i] / bestOverlap : 0;
            var meant = cosines[i] is { } cos ? meaning!.Model.Relevance(cos, bestCosine) : 0;
            return (c.Memory, c.Old, c.Pinned, Relevance: Math.Max(words, meant), c.Retention, c.Importance);
        }).ToList();
        var chosen = new List<Memory>();
        var used = 0;
        var quoted = 0;
        foreach (var s in scored
                     .Where(s => s.Pinned || s.Relevance >= 0.3)
                     .OrderByDescending(s => (s.Pinned ? 2 : 0) + s.Relevance + 0.5 * s.Retention + 0.3 * s.Importance / 10.0))
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

    /// <summary>
    /// The facts that relate to <paramref name="text"/> (a stretch of conversation), for upkeep to reconcile it against
    /// instead of everything she knows: the most related by shared words or meaning, and the pinned and most important
    /// ones whatever the topic, <paramref name="count"/> in all.
    /// </summary>
    public static IReadOnlyList<Fact> Related(string text, IReadOnlyList<Fact> facts, int count, Meaning? meaning = null)
    {
        if (facts.Count <= count)
            return facts;
        var core = facts.Where(f => f.Pinned || f.Importance >= 8).OrderByDescending(f => f.Pinned).ThenByDescending(f => f.Importance)
                        .ThenByDescending(f => f.Learned).Take(count / 3).ToList();
        var query = Terms(text).ToHashSet();
        double Words(Fact f)
        {
            var terms = Terms(f.Text).ToHashSet();
            return terms.Count == 0 ? 0 : terms.Count(query.Contains) / Math.Sqrt(terms.Count);
        }
        double Meant(Fact f) => meaning?.Facts.GetValueOrDefault(f.Id) is { } v ? LocalEmbedder.Cosine(meaning.Query, v) : 0;
        var bestWords = Math.Max(facts.Max(Words), 1e-9);
        var bestMeaning = Math.Max(facts.Max(Meant), 1e-9);
        var related = facts.Except(core).OrderByDescending(f => Math.Max(Words(f) / bestWords, Meant(f) / bestMeaning)).Take(count - core.Count);
        return [.. core, .. related];
    }

    /// <summary>
    /// Whether two facts say the same thing in nearly the same words (so a second telling is not kept twice), with the
    /// same names and numbers: "a dog named Luna" and "a dog named Oliver" share most of their words but are two facts,
    /// and an update that turns one into the other is a change, kept with its history (LoCoMo, 2026-10-11: Luna was
    /// written over, and a second cat dropped as a copy of the first).
    /// </summary>
    public static bool Same(string a, string b)
    {
        var x = Terms(a).ToHashSet();
        var y = Terms(b).ToHashSet();
        if (x.Count == 0 || y.Count == 0)
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        return x.Intersect(y).Count() / (double)x.Union(y).Count() >= 0.75 && Within(a, b) && Within(b, a);
    }

    private static readonly HashSet<string> Counts = ["two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    // shortcut: names are capitalised words, so Chinese and Japanese names aren't told apart here (numbers are); upgrade if facts in them get merged
    /// <summary>Whether every name and number in <paramref name="a"/> is somewhere in <paramref name="b"/>, in any case.</summary>
    public static bool Within(string a, string b)
    {
        var words = Regex.Matches(b, @"\p{L}+|\d+").Select(m => m.Value.ToLowerInvariant()).ToHashSet();
        return Regex.Matches(a, @"\p{L}+|\d+").Select(m => m.Value)
            .Where(w => char.IsUpper(w[0]) || char.IsDigit(w[0]) || Counts.Contains(w.ToLowerInvariant()))
            .All(w => words.Contains(w.ToLowerInvariant()));
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
