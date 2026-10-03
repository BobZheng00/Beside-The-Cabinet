using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace InsertCoin.Mind;

/// <summary>
/// A text embedding model that can run on this PC: its files, how it reads text, and how to read its similarity
/// scores. Absolute scores move a lot from one query to the next (Chinese pairs score high with the multilingual
/// model whatever they say), so what counts is the distance from the best match: within <see cref="Spread"/> of it,
/// and above <see cref="Floor"/>. Both were measured on related and unrelated pairs in English and Chinese.
/// </summary>
/// <param name="Name">Folder name under models/, and the tag stored with each vector.</param>
/// <param name="SentencePiece">XLM-R style SentencePiece tokens; otherwise BERT WordPiece (vocab.txt).</param>
/// <param name="MeanPooling">Average every token's output; otherwise take the first ([CLS]) token's.</param>
public sealed record EmbeddingModel(string Name, string Label, string TokenizerFile, bool SentencePiece, bool MeanPooling,
                                    string QueryPrefix, string PassagePrefix, float Floor, float Spread)
{
    /// <summary>multilingual-e5-small: about 100 languages, 113 MB, about 3.5 ms a text.</summary>
    public static readonly EmbeddingModel Multilingual = new("multilingual-e5-small", "Multilingual (113 MB, about 100 languages)",
        "sentencepiece.bpe.model", SentencePiece: true, MeanPooling: true, "query: ", "passage: ", Floor: 0.72f, Spread: 0.035f);
    /// <summary>bge-small-zh-v1.5: Chinese first, fair English, 23 MB, about 1.3 ms a text.</summary>
    public static readonly EmbeddingModel Small = new("bge-small-zh-v1.5", "Small (23 MB, Chinese and English)",
        "vocab.txt", SentencePiece: false, MeanPooling: false, "", "", Floor: 0.40f, Spread: 0.06f);
    public static readonly EmbeddingModel[] All = [Multilingual, Small];

    /// <summary>How relevant a candidate is, 0 to 1, from its score and the best score among the candidates.</summary>
    public double Relevance(float cosine, float best) => cosine < Floor ? 0 : Math.Clamp(1 - (best - cosine) / Spread, 0, 1);
}

/// <summary>
/// Turns text into a vector of its meaning, on this PC, with ONNX Runtime. Texts that mean the same land close
/// together (with the multilingual model across languages too), so "nervous about tomorrow" finds "interview on
/// Tuesday". Vectors are unit length: their dot product is the cosine. Thread-safe; a few milliseconds a text.
/// </summary>
public sealed class LocalEmbedder : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int MaxTokens = 256;

    private readonly InferenceSession _session;
    private readonly Func<string, IReadOnlyList<long>> _tokens;
    private readonly bool _typeIds;

    public EmbeddingModel Model { get; }

    /// <param name="onnxFile">The ONNX model's file (loaded from disk: a byte array would keep a second copy alive).</param>
    /// <param name="tokenizer">The model's tokenizer file (<see cref="EmbeddingModel.TokenizerFile"/>).</param>
    public LocalEmbedder(EmbeddingModel model, string onnxFile, Stream tokenizer)
    {
        Model = model;
        // Memory over the last bit of speed (measured: the multilingual model costs about its own size, 130 MB, this
        // way, against 250 MB with full graph optimisation and prepacked weights); it still takes a few ms a text.
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 2, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
            EnableCpuMemArena = false, EnableMemoryPattern = false,
        };
        options.AddSessionConfigEntry("session.disable_prepacking", "1");
        _session = new InferenceSession(onnxFile, options);
        _typeIds = _session.InputMetadata.ContainsKey("token_type_ids");
        if (model.SentencePiece)
        {
            // XLM-R: <s> 0, </s> 2, <unk> 3, and every SentencePiece id one higher than SentencePiece's own
            var pieces = SentencePieceTokenizer.Create(tokenizer, false, false);
            _tokens = text => [0, .. pieces.EncodeToIds(text, false, false).Take(MaxTokens - 2).Select(id => id == 0 ? 3L : id + 1L), 2];
        }
        else
        {
            var words = BertTokenizer.Create(tokenizer, new BertOptions { LowerCaseBeforeTokenization = true, IndividuallyTokenizeCjk = true });
            _tokens = text =>
            {
                var ids = words.EncodeToIds(text, true, true, true);                 // [CLS] ... [SEP]
                return ids.Count <= MaxTokens ? [.. ids.Select(i => (long)i)] : [.. ids.Take(MaxTokens - 1).Select(i => (long)i), ids[^1]];
            };
        }
    }

    /// <summary>The vector for something searched with (a query) or something searched in.</summary>
    public float[] Embed(string text, bool query)
    {
        var ids = _tokens((query ? Model.QueryPrefix : Model.PassagePrefix) + text).ToArray();
        var shape = new[] { 1, ids.Length };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape)),
        };
        if (_typeIds)
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[ids.Length], shape)));

        using var results = _session.Run(inputs);
        var hidden = results.First(r => r.Name == "last_hidden_state").AsTensor<float>();      // [1, tokens, width]
        var width = hidden.Dimensions[2];
        var vector = new float[width];
        var used = Model.MeanPooling ? ids.Length : 1;
        for (var t = 0; t < used; t++)
            for (var d = 0; d < width; d++)
                vector[d] += hidden[0, t, d];
        var length = MathF.Sqrt(vector.Sum(v => v * v));
        for (var d = 0; d < width; d++)
            vector[d] /= length;
        return vector;
    }

    public static float Cosine(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            sum += a[i] * b[i];
        return sum;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
                                                                     CancellationToken cancellationToken = default) =>
        Task.Run(() => new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v, query: false)))), cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(EmbeddingGeneratorMetadata) ? new EmbeddingGeneratorMetadata("onnx", defaultModelId: Model.Name) : null;

    public void Dispose() => _session.Dispose();
}
