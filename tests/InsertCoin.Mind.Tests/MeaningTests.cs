using Microsoft.Data.Sqlite;

namespace InsertCoin.Mind.Tests;

public class MeaningTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    private static Fact F(long id, string text, int importance = 5, DateTimeOffset? learned = null, bool pinned = false, int recalls = 0) =>
        new(id, MindStore.Shared, text, importance, learned ?? Friday.AddDays(-3), null, pinned, recalls);

    private static float[] V(params float[] v)
    {
        var length = MathF.Sqrt(v.Sum(x => x * x));
        return [.. v.Select(x => x / length)];
    }

    [Fact]
    public void Recall_finds_by_meaning_when_no_words_are_shared()
    {
        var facts = new[] { F(1, "The player has a job interview on Tuesday."), F(2, "The player's cat is called Mochi.") };
        var meaning = new Meaning(V(1, 0.1f, 0), EmbeddingModel.Small,
                                  new Dictionary<long, float[]> { [1] = V(0.9f, 0.2f, 0.1f), [2] = V(0.1f, 0.2f, 1) }, new Dictionary<long, float[]>());

        Assert.Empty(Recall.Find("I'm so nervous about tomorrow", facts, [], [], "Shino", Friday));
        Assert.Equal([1L], Recall.Find("I'm so nervous about tomorrow", facts, [], [], "Shino", Friday, meaning).Select(m => m.FactId!.Value));
    }

    [Fact]
    public void Facts_fade_unless_they_matter_or_keep_coming_up()
    {
        var old = Friday.AddDays(-120);
        Assert.True(F(1, "The player had toast for breakfast.", importance: 2, learned: old).Faded(Friday));
        Assert.False(F(2, "The player's name is Bob.", importance: 10, learned: old).Faded(Friday));
        Assert.False(F(3, "The player had toast.", importance: 2, learned: old, pinned: true).Faded(Friday));
        Assert.False(F(4, "The player's exam is on Friday.", importance: 7, learned: old, recalls: 3).Faded(Friday));

        var facts = new[] { F(1, "The player had toast for breakfast.", importance: 2, learned: old) };
        Assert.Empty(Recall.Find("toast for breakfast again", facts, [], [], "Shino", Friday));
    }

    [Fact]
    public void A_changed_fact_loses_its_vector_and_gets_a_new_one_when_indexed()
    {
        using var temp = new TempStore();
        var id = temp.Store.AddFact(MindStore.Shared, "The player likes Hades.", 5, Friday);
        temp.Store.SetVector(MindStore.FactKind, id, "m", V(1, 0));
        Assert.Single(temp.Store.Vectors(MindStore.FactKind, "m"));

        temp.Store.UpdateFact(id, "The player likes Celeste.");

        Assert.Empty(temp.Store.Vectors(MindStore.FactKind, "m"));
    }

    [Fact]
    public void A_memory_file_from_before_counts_recalls_after_opening()
    {
        var folder = Path.Combine(Path.GetTempPath(), "insert-coin-mind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "memory.db");
        using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE facts (id INTEGER PRIMARY KEY, scope TEXT NOT NULL, text TEXT NOT NULL, importance INTEGER NOT NULL,
                                    learned TEXT NOT NULL, recalled TEXT, pinned INTEGER NOT NULL DEFAULT 0);
                INSERT INTO facts (scope, text, importance, learned) VALUES ('*', 'The player likes Hades.', 5, '2026-10-01T10:00:00.0000000+08:00');
                PRAGMA user_version = 2;
                """;
            create.ExecuteNonQuery();
        }

        using (var store = new MindStore(path))
        {
            var fact = Assert.Single(store.Facts("shino"));
            store.Recalled([fact.Id], Friday);
            Assert.Equal(1, store.Facts("shino").Single().Recalls);
        }
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task Old_moments_are_combined_a_week_at_a_time()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var monday = new DateTimeOffset(2026, 9, 7, 20, 0, 0, TimeSpan.FromHours(8));
        var a = store.AddEpisode("shino", "They talked about exams.", 1, 4, monday);
        var b = store.AddEpisode("shino", "Bob solved two Hards and they celebrated.", 5, 9, monday.AddDays(2));
        var lonely = store.AddEpisode("shino", "A quiet chat.", 10, 12, monday.AddDays(7));        // the only one that week
        var recent = store.AddEpisode("shino", "Planning the interview.", 13, 20, Friday.AddDays(-2));
        var conversation = new Conversation(store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeModel("A week of exam talk and two Hard solves, celebrated together.");

        Assert.True(await conversation.Consolidate(model));
        Assert.False(await conversation.Consolidate(model));                // nothing left with two or more in a week

        var episodes = store.Episodes("shino");
        var week = Assert.Single(episodes, e => e.Week);
        Assert.Equal(("A week of exam talk and two Hard solves, celebrated together.", 1L, 9L, monday), (week.Text, week.First, week.Last, week.At));
        Assert.Equal([lonely, recent], episodes.Where(e => !e.Week).Select(e => e.Id));
        Assert.DoesNotContain(episodes, e => e.Id == a || e.Id == b);
        Assert.Contains("week starting Monday 7 September 2026", model.Requests[0][0].Text);
        Assert.Contains(Recall.Find("exam week", [], episodes, [], "Shino", Friday), m => m.Text.StartsWith("Week of 7 Sep: "));
    }

    [Fact]
    public async Task With_the_real_model_she_recalls_the_interview_when_told_about_nerves_in_chinese()
    {
        if (EmbedderTests.Load(EmbeddingModel.Multilingual) is not { } embedder)
            return;
        using var temp = new TempStore();
        temp.Store.AddFact(MindStore.Shared, "The player has a job interview on Tuesday 6 October.", 8, Friday.AddDays(-1));
        temp.Store.AddFact(MindStore.Shared, "The player's cat is called Mochi.", 5, Friday.AddDays(-1));
        temp.Store.AddFact(MindStore.Shared, "The player likes playing Hades.", 5, Friday.AddDays(-1));
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday)) { Embedder = embedder };

        Assert.Equal(3, await conversation.Index());
        Assert.Equal(0, await conversation.Index());
        var model = new FakeModel("加油！");
        await conversation.Answer(model, "明天面试，有点怕", "", _ => { }, (_, _) => { });

        var sent = model.Requests.Single()[^1].Text;
        Assert.Contains("job interview on Tuesday", sent);
        Assert.DoesNotContain("Mochi", sent);
    }
}
