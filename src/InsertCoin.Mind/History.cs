namespace InsertCoin.Mind;

/// <summary>A stretch of the conversation from one sitting, kept for searching (see <see cref="History"/>).</summary>
public sealed record Passage(long Id, long First, long Last, DateTimeOffset At, string Text);

/// <summary>A few lines from an old passage that relate to what the player just said, with the passage's vector.</summary>
public sealed record Excerpt(string Text, DateTimeOffset At, float[]? Vector);

/// <summary>
/// The whole conversation, searchable (what Letta, once MemGPT, calls recall memory). Lines before the window are
/// grouped into passages, one sitting and about 250 tokens each, which the local model gives a vector during upkeep;
/// each reply searches them by shared words and by meaning, and the best few come to <see cref="Recall"/> as
/// excerpts. Rolled-up moments lose detail by design; the conversation itself is where it can still be found.
/// Scored in memory: a year of talk is some thousands of passages, a few milliseconds to search.
/// </summary>
public sealed class History(MindStore store, string character, string name)
{
    public const int PassageTokens = 250;
    private static readonly TimeSpan Sitting = TimeSpan.FromMinutes(30);

    private List<Passage>? _passages;
    private readonly Dictionary<long, HashSet<string>> _terms = [];
    private string? _model;
    private Dictionary<long, float[]> _vectors = [];

    private int _seen = -1;

    private List<Passage> Passages
    {
        get
        {
            if (_seen != store.Changes)                     // the conversation was cleared or forgotten: start again
            {
                _seen = store.Changes;
                _passages = null;
                _terms.Clear();
                _model = null;
                _vectors = [];
            }
            return _passages ??= [.. store.Passages(character)];
        }
    }

    /// <summary>Groups the lines up to <paramref name="through"/> that are not in a passage yet into passages (lines from the cabinet are left out).</summary>
    public void CatchUp(long through)
    {
        var from = Passages.Count > 0 ? Passages[^1].Last : 0;
        if (through <= from)
            return;
        var lines = store.Between(character, from + 1, through);
        var made = new List<Passage>();
        var group = new List<Line>();
        var tokens = 0;
        void Close()
        {
            var spoken = group.Where(l => l.Speaker != Speaker.Cabinet).ToList();
            if (spoken.Count > 0)
                made.Add(new Passage(0, group[0].Id, group[^1].Id, spoken[0].At, string.Join("\n", spoken.Select(Said))));
            group.Clear();
            tokens = 0;
        }
        foreach (var line in lines)
        {
            if (group.Count > 0 && (line.At - group[^1].At >= Sitting || tokens + Prompt.Tokens(line.Text) > PassageTokens))
                Close();
            group.Add(line);
            tokens += Prompt.Tokens(line.Text) + 4;
        }
        Close();
        Passages.AddRange(store.AddPassages(character, made));     // a long history the first time: one transaction
    }

    /// <summary>Passages <paramref name="model"/> has not read yet, for upkeep to give vectors.</summary>
    public IEnumerable<Passage> Unread(string model)
    {
        UseModel(model);
        return Passages.Where(p => !_vectors.ContainsKey(p.Id));
    }

    private void UseModel(string model)
    {
        _ = Passages;                                       // after a clear, before the vectors are read
        if (_model == model)
            return;
        _model = model;
        _vectors = store.Vectors(MindStore.PassageKind, model);
    }

    /// <summary>A vector upkeep has just made, so the next search has it without reading them all again.</summary>
    public void Learned(string model, long passage, float[] vector)
    {
        if (_model == model)
            _vectors[passage] = vector;
    }

    /// <summary>
    /// The passages before <paramref name="through"/> that best match <paramref name="said"/>, by shared words or by
    /// meaning (with <paramref name="query"/> from <paramref name="model"/>), as excerpts: each passage whole (see <see cref="Excerpt"/>).
    /// </summary>
    public IReadOnlyList<Excerpt> Search(string said, long through, float[]? query = null, EmbeddingModel? model = null, int count = 10)
    {
        var candidates = Passages.Where(p => p.Last <= through).ToList();
        if (candidates.Count == 0)
            return [];
        if (model is not null)
            UseModel(model.Name);
        var words = Recall.Terms(said).ToHashSet();
        words.ExceptWith(Recall.Terms(name));
        double Words(Passage p)
        {
            var terms = _terms.TryGetValue(p.Id, out var known) ? known : _terms[p.Id] = Recall.Terms(p.Text).ToHashSet();
            return terms.Count == 0 ? 0 : terms.Count(words.Contains) / Math.Sqrt(terms.Count);
        }
        float Meant(Passage p) => query is not null && model is not null && _vectors.TryGetValue(p.Id, out var v) ? LocalEmbedder.Cosine(query, v) : 0;
        var scored = candidates.Select(p => (Passage: p, Words: Words(p), Meant: Meant(p))).ToList();
        var bestWords = scored.Max(s => s.Words);
        var bestMeant = scored.Max(s => s.Meant);
        return scored
            .Select(s => (s.Passage, Score: Math.Max(bestWords > 0 ? s.Words / bestWords : 0, model is not null ? model.Relevance(s.Meant, bestMeant) : 0)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .Take(count)
            .Select(s => Excerpt(s.Passage))
            .ToList();
    }

    /// <summary>
    /// What she is given of a passage that matched: the passage itself, whole lines up to about 1,200 characters. The line
    /// that answers is often not the one that matched best (measured on LoCoMo, 2026-10-11: the evidence reached her for
    /// 31% of questions given the best line and the next, 54% given whole passages).
    /// </summary>
    private Excerpt Excerpt(Passage passage)
    {
        var lines = store.Between(character, passage.First, passage.Last).Where(l => l.Speaker != Speaker.Cabinet).ToList();
        if (lines.Count == 0)
            return new Excerpt(passage.Text.Length > 400 ? passage.Text[..400] + "…" : passage.Text, passage.At, _vectors.GetValueOrDefault(passage.Id));
        var kept = new List<Line>();
        var length = 0;
        foreach (var line in lines)
        {
            if (kept.Count > 0 && length + Said(line).Length > 1200)
                break;
            kept.Add(line);
            length += Said(line).Length + 1;
        }
        return new Excerpt(string.Join("\n", kept.Select(Said)), kept[0].At, _vectors.GetValueOrDefault(passage.Id));
    }

    private string Said(Line line) => $"{(line.Speaker == Speaker.User ? "Player" : name)}: {line.Text}";
}
