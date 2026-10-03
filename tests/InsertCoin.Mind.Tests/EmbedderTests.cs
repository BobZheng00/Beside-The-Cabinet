using System.Diagnostics;
using Xunit.Abstractions;

namespace InsertCoin.Mind.Tests;

/// <summary>Runs the real models when they have been fetched (python tools/models/fetch.py); otherwise these pass quietly.</summary>
public class EmbedderTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, LocalEmbedder?> Loaded = [];

    internal static LocalEmbedder? Load(EmbeddingModel model)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(model.Name, out var known))
                return known;
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "src", "InsertCoin.Game")))
                folder = folder.Parent;
            var files = folder is null ? null : Path.Combine(folder.FullName, "src", "InsertCoin.Game", "models", model.Name);
            LocalEmbedder? embedder = null;
            if (files is not null && File.Exists(Path.Combine(files, "model.onnx")))
            {
                using var tokenizer = File.OpenRead(Path.Combine(files, model.TokenizerFile));
                embedder = new LocalEmbedder(model, Path.Combine(files, "model.onnx"), tokenizer);
            }
            return Loaded[model.Name] = embedder;
        }
    }

    public static IEnumerable<object[]> Models => EmbeddingModel.All.Select(m => new object[] { m.Name });

    [Theory]
    [MemberData(nameof(Models))]
    public void Texts_that_mean_the_same_land_closer_than_unrelated_ones(string name)
    {
        var model = EmbeddingModel.All.Single(m => m.Name == name);
        if (Load(model) is not { } e)
            return;
        var pairs = new (string Query, string Related, string Unrelated)[]
        {
            ("I'm so nervous about tomorrow", "The player has a job interview on Tuesday 6 October.", "The player's cat is called Mochi."),
            ("any tips for my exam?", "The player has a calculus exam on Friday.", "The player likes playing Hades."),
            ("考试好紧张", "玩家下周五有数学考试。", "玩家喜欢玩《哈迪斯》。"),
            ("what was my pet's name again", "The player's cat is called Mochi.", "The player has a calculus exam on Friday."),
            ("明天面试，有点怕", "The player has a job interview on Tuesday 6 October.", "The player likes playing Hades."),
            ("我的猫叫什么来着", "玩家的猫叫麻糬。", "玩家下周五有数学考试。"),
        };
        var wrong = 0;
        foreach (var (query, related, unrelated) in pairs)
        {
            var q = e.Embed(query, query: true);
            var near = LocalEmbedder.Cosine(q, e.Embed(related, query: false));
            var far = LocalEmbedder.Cosine(q, e.Embed(unrelated, query: false));
            var best = Math.Max(near, far);
            output.WriteLine($"{name}: {near:0.000} vs {far:0.000}  (relevance {model.Relevance(near, best):0.00} vs {model.Relevance(far, best):0.00})  {query}");
            if (near <= far)
                wrong++;
        }
        // the small model is allowed one miss (it is weaker across languages); the multilingual one none
        Assert.True(wrong <= (model == EmbeddingModel.Small ? 1 : 0), $"{wrong} pairs ranked the wrong way");
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
            e.Embed("The player has a calculus exam on Friday 9 October.", query: false);
        output.WriteLine($"{name}: {clock.ElapsedMilliseconds / 20.0:0.0} ms per short text");
    }
}
