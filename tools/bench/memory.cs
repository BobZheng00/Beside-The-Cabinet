// Her memory on two public benchmarks (docs/research/2026-10-11-memory-benchmark.md). A history goes into a fresh memory
// as the conversation between the player and her, with her upkeep run in the quiet between sittings as the app runs it
// (moments, facts, plans, folds into the summary, vectors). Then each question is asked as the player would ask it,
// through her real prompt and recall, and a judge grades her answer with LongMemEval's own prompts.
//
//     dotnet run tools/bench/memory.cs -- --data <json> --out <folder> [options]
//
// LongMemEval (Wu et al., ICLR 2025: https://github.com/xiaowu0162/LongMemEval), the default: each question has its own
// history of about 50 sittings (115k tokens in its S set), asked at its date.
//   --mode memory | window | oracle   memory: all of her memory, the window at --window tokens (default);
//          | audit                    window: the whole history word for word, no upkeep (how a long window holds up);
//                                     oracle: only the sittings that hold the answer, word for word (the ceiling);
//                                     audit: for what memory mode answered, where the answer's sittings ended up, whether
//                                     her search finds them, and a judge's reading of what her memory kept about it
//   --window <tokens>                 the window kept word for word in memory mode (32000, the app's default)
//   --per-type <n> [--seed <n>]       n questions of each kind, picked at random (1, seed 1); --types a,b to only take
//                                     those kinds; or --ids a,b,c
//   --budget <tokens>                 no new question starts once the run has used this many (3,000,000)
//
// LoCoMo (Maharana et al., ACL 2024: https://github.com/snap-research/locomo; CC BY-NC, never committed), --set locomo:
// ten conversations between two people over weeks, 11k-22k tokens each, the first as the player, the second as her.
// Each goes in once, then all its questions are asked a day after its last sitting, each on its own copy of the memory,
// with where the evidence is and whether her search and recall bring it back.
//   --window <tokens>                 8000 by default (her setting for local models): at 32000 the whole conversation
//                                     would still be word for word, and nothing would test her search
//   --ids conv-26,conv-30             only those conversations
//   --again                           ask again what was asked before
//
// Staying in character, --set persona --card <file> --name <name> [--player <name>] [--depths 0,8000,32000,100000]
// [--repeat 2] [--history <question id>]: her window filled to each depth from a LongMemEval history (--data), the same
// messages put to her at each, a judge's scores against her card.
//
//   --parallel <n>                    questions at once (2)
//   --from <profile folder>           whose provider and key to use (the demo's)
//
// It runs on that profile's provider, its key read where it is kept, encrypted, and billed to whoever owns it: use it
// only when they offer. Results are appended to <out>/results.jsonl or <out>/locomo.jsonl (what is there already is not
// asked again) and each memory is kept in <out>/stores to look into.
#:property PublishAot=false
#:property NoWarn=CA1416
#:project ../../src/InsertCoin.Mind/InsertCoin.Mind.csproj
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;
using OpenAI;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var options = Args(args);
var data = options["data"];
var output = Directory.CreateDirectory(options["out"]).FullName;
var mode = options.GetValueOrDefault("mode", "memory");
var window = int.Parse(options.GetValueOrDefault("window", Prompt.DefaultWindow.ToString()));
var parallel = int.Parse(options.GetValueOrDefault("parallel", "2"));
var budget = long.Parse(options.GetValueOrDefault("budget", "3000000"));
var profile = options.GetValueOrDefault("from", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InsertCoin-demo"));
if (mode is not ("memory" or "window" or "oracle" or "audit"))
    throw new ArgumentException($"--mode {mode}: memory, window, oracle or audit");

// her mind: the profile's provider, with more patience than the app's (a 120k-token request takes a while)
var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "settings.json"))).RootElement;
var provider = Provider.From(settings.TryGetProperty("ChatProtocol", out var protocol) ? protocol.GetString() : null,
                             settings.GetProperty("ChatBaseUrl").GetString(), settings.GetProperty("ChatModel").GetString())
               ?? throw new InvalidOperationException("That profile has no provider set.");
var key = new KeyStore(Path.Combine(profile, "companion", "key.bin")).Load();
IChatClient client = new Patient(provider.Protocol == Protocol.OpenAi
    ? new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(key) ? "none" : key),
                       new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl), NetworkTimeout = TimeSpan.FromMinutes(5), RetryPolicy = new ClientRetryPolicy(maxRetries: 3) })
        .GetChatClient(provider.Model).AsIChatClient()
    : provider.Connect(key));

// recall by meaning, as the app does with its default model
var models = Path.Combine(Here(), "../../src/InsertCoin.Game/models", EmbeddingModel.Multilingual.Name);
using var embedder = mode is "memory" or "audit"
    ? new LocalEmbedder(EmbeddingModel.Multilingual, Path.Combine(models, "model.onnx"), File.OpenRead(Path.Combine(models, EmbeddingModel.Multilingual.TokenizerFile)))
    : null;

Directory.CreateDirectory(Path.Combine(output, "stores"));
if (options.GetValueOrDefault("set") == "locomo")
{
    await LoCoMo();
    return;
}
if (options.GetValueOrDefault("set") == "persona")
{
    await StaysInCharacter();
    return;
}

var items = JsonSerializer.Deserialize<List<Item>>(File.OpenRead(data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
var types = options.GetValueOrDefault("types")?.Split(',');
var picked = options.TryGetValue("ids", out var ids)
    ? items.Where(i => ids.Split(',').Contains(i.QuestionId)).ToList()
    : PickPerKind([.. items.Where(i => types is null || types.Contains(i.QuestionType))], int.Parse(options.GetValueOrDefault("per-type", "1")),
                  int.Parse(options.GetValueOrDefault("seed", "1")));

var resultsFile = Path.Combine(output, "results.jsonl");
if (mode == "audit")
{
    await AuditAll();
    return;
}
var done = File.Exists(resultsFile)
    ? File.ReadLines(resultsFile).Select(l => JsonSerializer.Deserialize<Result>(l)!).Where(r => r.Mode == mode && r.Error is null).ToList()
    : [];
long used = done.Sum(r => r.Input + r.Output);
var gate = new object();
Console.WriteLine($"{picked.Count} questions, {mode} mode{(mode == "memory" ? $", a {window}-token window" : "")}, on {provider.Model}; {done.Count} done before");

await Parallel.ForEachAsync(picked.Where(i => !done.Any(r => r.Id == i.QuestionId)), new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (item, cancel) =>
{
    if (Interlocked.Read(ref used) >= budget)
        return;
    Result result;
    try
    {
        result = await Ask(item);
    }
    catch (Exception e)
    {
        result = new Result(item.QuestionId, Kind(item), mode, window, item.Question, Answer(item), "", false, 0, 0, 0, 0, 0, 0, 0, 0, e.Message);
    }
    Interlocked.Add(ref used, result.Input + result.Output);
    lock (gate)
    {
        File.AppendAllText(resultsFile, JsonSerializer.Serialize(result) + "\n");
        Console.WriteLine($"{(result.Error is not null ? "ERROR" : result.Correct ? "right" : "wrong")}  {result.Kind,-30} {result.Id,-14} " +
                          $"{result.Input / 1000,5}k in {result.Output / 1000,4}k out {result.Seconds,5:0}s  {result.Error}");
    }
});

// the tally, over everything this mode has in the results
var all = File.ReadLines(resultsFile).Select(l => JsonSerializer.Deserialize<Result>(l)!).Where(r => r.Mode == mode && r.Error is null)
    .GroupBy(r => r.Id).Select(g => g.Last()).ToList();
Console.WriteLine($"\n{"kind",-32} {"right",6} {"of",4} {"in/q",8} {"out/q",7}");
foreach (var kind in all.GroupBy(r => r.Kind).OrderBy(g => g.Key))
    Console.WriteLine($"{kind.Key,-32} {kind.Count(r => r.Correct),6} {kind.Count(),4} {kind.Average(r => r.Input) / 1000,7:0}k {kind.Average(r => r.Output) / 1000,6:0}k");
if (all.Count > 0)
    Console.WriteLine($"{"all",-32} {all.Count(r => r.Correct),6} {all.Count,4} {all.Average(r => r.Input) / 1000,7:0}k {all.Average(r => r.Output) / 1000,6:0}k" +
                      $"   ({Interlocked.Read(ref used) / 1000}k tokens this run and before)");

async Task<Result> Ask(Item item)
{
    var watch = Stopwatch.StartNew();
    var path = Path.Combine(output, "stores", $"{item.QuestionId}-{mode}.db");
    File.Delete(path);
    var asked = When(item.QuestionDate);
    var sittings = item.HaystackSessions.Select((turns, i) => (At: When(item.HaystackDates[i]), Id: item.HaystackSessionIds[i], Turns: turns))
        .Where(s => mode != "oracle" || item.AnswerSessionIds.Contains(s.Id)).OrderBy(s => s.At).ToList();
    using var store = new MindStore(path);
    var clock = new Clock(sittings.Count > 0 ? sittings[0].At : asked);
    var replies = new Metered(client, store, provider.Model, "replies", clock);
    var upkeep = new Metered(client, store, provider.Model, "upkeep", clock);
    var judge = new Metered(client, store, provider.Model, "judge", clock);
    var conversation = new Conversation(store, Character, Persona.Plain(Name), clock)
    {
        Embedder = embedder,
        Window = mode == "memory" ? window : 10_000_000,
    };

    for (var s = 0; s < sittings.Count; s++)
    {
        var (start, _, turns) = sittings[s];
        var next = s + 1 < sittings.Count ? sittings[s + 1].At : asked;
        // a turn every 30 seconds, closer when the next sitting comes sooner than that allows
        var step = TimeSpan.FromSeconds(Math.Clamp((next - start).TotalSeconds / (turns.Count + 1), 1, 30));
        var at = start;
        foreach (var turn in turns)
        {
            clock.Now = at;
            store.Add(Character, turn.Role == "user" ? Speaker.User : Speaker.Her, turn.Content, at);
            at += step;
            if (mode == "memory" && turn.Role != "user" && conversation.NeedsFolding)
                await conversation.Fold(upkeep);    // the window full mid-conversation: folded at once, as after a reply of hers
        }
        // her sleep time, when the quiet before the next sitting is long enough for it
        if (mode == "memory" && next - (at - step) >= Conversation.Quiet)
        {
            clock.Now = at - step + Conversation.Quiet;
            await Upkeep(conversation, upkeep);
        }
    }
    clock.Now = asked > clock.Now ? asked : clock.Now;
    if (mode == "memory")
        await Upkeep(conversation, upkeep);

    var hypothesis = await conversation.Answer(replies, item.Question, "", _ => { }, (_, _) => { });
    if (hypothesis.Length == 0)
        throw new InvalidOperationException(Empty);
    var verdict = await judge.GetResponseAsync([new ChatMessage(ChatRole.User, JudgePrompt(item, hypothesis))], new ChatOptions { MaxOutputTokens = 1000 });
    var rows = store.Usage(DateOnly.MinValue);
    long Sum(string purpose, Func<UsageRow, long> count) => rows.Where(r => purpose == "" || r.Purpose == purpose).Sum(count);
    return new Result(item.QuestionId, Kind(item), mode, window, item.Question, Answer(item), hypothesis,
                      verdict.Text.Contains("yes", StringComparison.OrdinalIgnoreCase),
                      Sum("", r => r.Input), Sum("", r => r.Cached), Sum("", r => r.Output), Sum("upkeep", r => r.Input), Sum("replies", r => r.Input),
                      store.Facts(Character).Count, store.Observations(Character).Count, (long)watch.Elapsed.TotalSeconds, null);
}

// as Chat.Upkeep runs each minute while the talk is quiet, over and over until there is nothing left to do
static async Task Upkeep(Conversation conversation, IChatClient client)
{
    for (var round = 0; round < 400; round++)
    {
        bool did;
        if (conversation.NeedsDigest(Conversation.Quiet))
        {
            await conversation.Digest(client);
            did = true;                                     // a stretch that fails three times is skipped by Digest itself
        }
        else
            did = await conversation.Condense(client) || (conversation.IsQuiet(Conversation.Quiet) && await conversation.Review(client));
        while (await conversation.Index() > 0)
        {
        }
        if (conversation.IsQuiet(Conversation.Quiet) && conversation.NeedsQuietFold)
            did |= await conversation.Fold(client, quiet: true);
        if (!did)
            return;
    }
}

// --mode audit: for each question memory mode answered, from its kept memory (no new upkeep): where the sittings that
// hold the answer ended up (still word for word in the window, or folded away), whether her search finds them again,
// and a judge's reading of what she keeps besides the window (notes, summary, facts, moments, agenda): does it still
// hold the answer, and does it hold anything false or outdated about it
async Task AuditAll()
{
    var byId = items.ToDictionary(i => i.QuestionId);
    var answered = File.ReadLines(resultsFile).Select(l => JsonSerializer.Deserialize<Result>(l)!)
        .Where(r => r.Mode == "memory" && r.Error is null && File.Exists(Path.Combine(output, "stores", $"{r.Id}-memory.db")))
        .GroupBy(r => r.Id).Select(g => g.Last()).ToList();
    var audits = new List<Audit>();
    foreach (var result in answered)
    {
        var audit = await Inspect(byId[result.Id], result);
        audits.Add(audit);
        File.AppendAllText(Path.Combine(output, "audit.jsonl"), JsonSerializer.Serialize(audit) + "\n");
        Console.WriteLine($"{(audit.Correct ? "right" : "wrong")}  {audit.Kind,-36} evidence {audit.Evidence,-7} found {audit.Found}/{audit.Searched}  kept {audit.Kept,-6} wrong: {audit.Wrong}");
    }
    Console.WriteLine($"\nevidence: {string.Join(", ", audits.GroupBy(a => a.Evidence).Select(g => $"{g.Count()} {g.Key}"))}; " +
                      $"folded and found again: {audits.Count(a => a.Evidence != "window" && a.Found > 0)} of {audits.Count(a => a.Evidence is "folded" or "both")}; " +
                      $"kept: {string.Join(", ", audits.GroupBy(a => a.Kept).Select(g => $"{g.Count()} {g.Key}"))}; " +
                      $"something wrong kept: {audits.Count(a => a.Wrong != "none")}; {audits.Sum(a => a.Input) / 1000}k tokens");
}

async Task<Audit> Inspect(Item item, Result result)
{
    using var store = new MindStore(Path.Combine(output, "stores", $"{item.QuestionId}-memory.db"));
    // the lines went in from id 1, a turn each, the sittings in date order: which ids the answer's sittings got
    var evidence = new List<(long From, long To, List<Turn> Turns)>();
    var next = 1L;
    foreach (var (_, id, turns) in item.HaystackSessions.Select((turns, i) => (At: When(item.HaystackDates[i]), Id: item.HaystackSessionIds[i], Turns: turns)).OrderBy(s => s.At))
    {
        if (item.AnswerSessionIds.Contains(id))
            evidence.Add((next, next + turns.Count - 1, turns));
        next += turns.Count;
    }
    var through = store.Folded(Character);
    var where = evidence.Count == 0 ? "none" : evidence.All(e => e.To <= through) ? "folded" : evidence.All(e => e.From > through) ? "window" : "both";

    // what the answer rests on: the turns marked as holding it, else what the player said in those sittings
    var marked = evidence.SelectMany(e => e.Turns).Where(t => t.HasAnswer == true).ToList();
    var key = marked.Count > 0 ? marked : [.. evidence.SelectMany(e => e.Turns).Where(t => t.Role == "user")];
    var excerpts = new History(store, Character, Name).Search(item.Question, through, embedder!.Embed(item.Question, query: true), embedder.Model);
    var found = excerpts.Count(x => key.Any(t => x.Text.Contains(t.Content.Trim()[..Math.Min(50, t.Content.Trim().Length)], StringComparison.Ordinal)));

    var kept = new System.Text.StringBuilder();
    kept.AppendLine($"Note about the player:\n{store.Note(MindStore.Shared, MindStore.AboutUser)}\n");
    kept.AppendLine($"Note about the two of them:\n{store.Note(Character, MindStore.Us)}\n");
    var log = Log.Left(store.Observations(Character), through);
    kept.AppendLine($"Her notes on the conversation before the window, the part she sees:\n{(log.Count > 0 ? Log.Laid(Log.Newest(log, Log.Budget(window)), null) : "(none)")}\n");
    kept.AppendLine("Facts:\n" + string.Join("\n", store.Facts(Character).Select(f => $"- {f.Text} (learned {f.Learned:yyyy-MM-dd})")) + "\n");
    kept.AppendLine("Her other notes, out of view:\n" + Log.Laid([.. store.Observations(Character).Except(Log.Newest(log, Log.Budget(window)))], null) + "\n");
    kept.AppendLine("Agenda:\n" + string.Join("\n", store.Agenda().Select(p => $"- {p.At:yyyy-MM-dd HH:mm} {p.Text}")));
    var told = string.Join("\n", key.Select(t => $"{(t.Role == "user" ? "Player" : "Mira")}: {(t.Content.Length > 700 ? t.Content[..700] + "…" : t.Content)}"));
    var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, $$"""
        A companion, Mira, keeps a long-term memory of her conversations with a player. Below is everything she keeps besides the most recent part of the conversation (which she has word for word), then a question the player asks later, its correct answer, and what was said that the answer rests on.

        Answer with JSON only: {"kept": "yes", "wrong": "none"}
        "kept": "yes" if her memory below holds what is needed to answer correctly, "partly" if only some of it, "no" if none. For a question whose correct answer is that it can't be answered, "yes" if her memory doesn't claim to know.
        "wrong": quote anything in her memory that bears on this question and is false, outdated or contradicts what was said, with a few words on why; or "none".

        === Her memory ===
        {{kept}}
        === The question, asked on {{item.QuestionDate}} ===
        {{item.Question}}

        === The correct answer ===
        {{Answer(item)}}

        === What was said ===
        {{told}}
        """)], new ChatOptions { MaxOutputTokens = 3000 });
    string Field(string name)
    {
        var text = response.Text;
        var json = text.IndexOf('{') is var start and >= 0 && text.LastIndexOf('}') is var end && end > start ? text[start..(end + 1)] : "{}";
        try
        {
            return JsonDocument.Parse(json).RootElement.TryGetProperty(name, out var value) ? value.ToString() : "?";
        }
        catch (JsonException)
        {
            return "?";
        }
    }
    return new Audit(item.QuestionId, Kind(item), Answer(item), result.Correct, where, found, excerpts.Count, Field("kept"), Field("wrong"),
                     (response.Usage?.InputTokenCount ?? 0) + (response.Usage?.OutputTokenCount ?? 0));
}

// --set locomo: each conversation goes in once (kept, and used again by a later run at the same window), then each of
// its questions is asked on a copy of that memory, so no question sees another's answer
async Task LoCoMo()
{
    var size = options.ContainsKey("window") ? window : Prompt.LocalWindow;
    var only = options.GetValueOrDefault("ids")?.Split(',');
    var file = Path.Combine(output, "locomo.jsonl");
    var asked = options.ContainsKey("again") || !File.Exists(file)
        ? []
        : File.ReadLines(file).Select(l => JsonSerializer.Deserialize<Asked>(l)!).Where(a => a.Window == size && a.Error is null).Select(a => a.Id).ToHashSet()
;
    var copies = Directory.CreateDirectory(Path.Combine(output, "copies")).FullName;
    var writing = new object();
    foreach (var sample in JsonDocument.Parse(File.ReadAllText(data)).RootElement.EnumerateArray())
    {
        var name = sample.GetProperty("sample_id").GetString()!;
        var questions = sample.GetProperty("qa").EnumerateArray().Select((q, i) => (Id: $"{name}#{i}", Q: q)).Where(q => !asked.Contains(q.Id)).ToList();
        if (only is not null && !only.Contains(name) || questions.Count == 0)
            continue;
        var conversation = sample.GetProperty("conversation");
        var player = conversation.GetProperty("speaker_a").GetString()!;
        var her = conversation.GetProperty("speaker_b").GetString()!;
        var character = her.ToLowerInvariant();
        var sittings = conversation.EnumerateObject().Where(p => Regex.IsMatch(p.Name, @"^session_\d+$"))
            .Select(p => (At: SessionTime(conversation.GetProperty($"{p.Name}_date_time").GetString()!), Turns: p.Value.EnumerateArray().ToList()))
            .OrderBy(s => s.At).ToList();
        // the lines go in from id 1, a turn each, in this order: which line each turn of the dataset became
        var lineOf = new Dictionary<string, (long Id, string Text)>();
        var next = 1L;
        foreach (var turn in sittings.SelectMany(s => s.Turns))
            lineOf[turn.GetProperty("dia_id").GetString()!] = (next++, Said(turn));
        var then = sittings[^1].At.AddDays(1);

        var path = Path.Combine(output, "stores", $"{name}-locomo-{size}.db");
        var watch = Stopwatch.StartNew();
        bool ingested;
        using (var kept = new MindStore(path))
            ingested = kept.Mark("bench.ingested") is not null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!ingested)
        {
            File.Delete(path);
            using (var fresh = new MindStore(path))
            {
                var clock = new Clock(sittings[0].At);
                var upkeep = new Metered(client, fresh, provider.Model, "upkeep", clock);
                var memory = new Conversation(fresh, character, Persona.Plain(her), clock) { Embedder = embedder, Window = size };
                for (var s = 0; s < sittings.Count; s++)
                {
                    var at = sittings[s].At;
                    foreach (var turn in sittings[s].Turns)
                    {
                        clock.Now = at;
                        var fromPlayer = turn.GetProperty("speaker").GetString() == player;
                        fresh.Add(character, fromPlayer ? Speaker.User : Speaker.Her, Said(turn), at);
                        at += TimeSpan.FromSeconds(30);
                        if (!fromPlayer && memory.NeedsFolding)
                            await memory.Fold(upkeep);
                    }
                    clock.Now = (s + 1 < sittings.Count ? Min(sittings[s + 1].At, at + Conversation.Quiet) : then);
                    await Upkeep(memory, upkeep);
                }
                fresh.SetMark("bench.ingested", $"{DateTimeOffset.Now:O}");
                var spent = fresh.Usage(DateOnly.MinValue).Sum(r => r.Input + r.Output);
                Console.WriteLine($"{name}: {player} and {her}, {sittings.Count} sittings, {lineOf.Count} turns in, {fresh.Facts(character).Count} facts, " +
                                  $"{fresh.Observations(character).Count} notes; {spent / 1000}k tokens, {watch.Elapsed.TotalSeconds:0}s");
            }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        await Parallel.ForEachAsync(questions, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (item, cancel) =>
        {
            var q = item.Q;
            var category = q.GetProperty("category").GetInt32();
            var question = q.GetProperty("question").GetString()!;
            var expected = q.TryGetProperty("answer", out var a) ? (a.ValueKind == JsonValueKind.String ? a.GetString()! : a.GetRawText()) : "";
            var evidence = q.TryGetProperty("evidence", out var e)
                ? e.EnumerateArray().Select(x => x.GetString()!).Where(lineOf.ContainsKey).Select(x => lineOf[x]).ToList()
                : [];
            Asked result;
            var copy = Path.Combine(copies, $"{Guid.NewGuid():N}.db");
            File.Copy(path, copy);
            try
            {
                using var store = new MindStore(copy);
                var clock = new Clock(then);
                var memory = new Conversation(store, character, Persona.Plain(her), clock) { Embedder = embedder, Window = size };
                // what her answer is about to search, done here first to see whether it brings the evidence back
                var through = store.Folded(character);
                var log = Log.Left(store.Observations(character), through);
                var where = evidence.Count == 0 ? "none" : evidence.All(x => x.Id <= through) ? "folded" : evidence.All(x => x.Id > through) ? "window" : "both";
                var query = embedder!.Embed(question, query: true);
                memory.History.CatchUp(through);
                var excerpts = memory.History.Search(question, through, query, embedder.Model);
                var meaning = new Meaning(query, embedder.Model, store.Vectors(MindStore.FactKind, embedder.Model.Name), store.Vectors(MindStore.ObservationKind, embedder.Model.Name));
                var recalled = Recall.Find(question, store.Facts(character), excerpts, her, then, meaning, [.. log.Except(Log.Newest(log, Log.Budget(size)))],
                                           [.. store.Retired(character).Where(f => f.Why is { } why && (why.StartsWith(Conversation.Changed) || why == Conversation.Ended))]);
                var snippets = evidence.Where(x => x.Id <= through).Select(x => x.Text[..Math.Min(50, x.Text.Length)]).ToList();
                var inSearch = excerpts.Any(x => snippets.Any(s => x.Text.Contains(s, StringComparison.Ordinal)));
                var inRecall = recalled.Any(m => snippets.Any(s => m.Text.Contains(s, StringComparison.Ordinal)));

                var answered = await memory.Answer(new Metered(client, store, provider.Model, "replies", clock), question, $"Player: {player}.", _ => { }, (_, _) => { });
                if (answered.Length == 0)
                    throw new InvalidOperationException(Empty);
                var verdict = await client.GetResponseAsync([new ChatMessage(ChatRole.User, category == 5
                    ? Grading("", true, question, "Nothing in the conversation says this.", answered)
                    : Grading("single-session-user", false, question, expected, answered))], new ChatOptions { MaxOutputTokens = 1000 });
                // and as Mem0's paper graded LoCoMo, the grade behind the published scores (Mem0, Zep, Letta): generous, "as long as
                // it touches on the same topic"; unanswerable questions were left out there
                bool? generous = null;
                if (category != 5)
                {
                    var label = await client.GetResponseAsync([new ChatMessage(ChatRole.User, string.Format(GenerousGrading, question, expected, answered))],
                                                              new ChatOptions { MaxOutputTokens = 1000 });
                    generous = Regex.IsMatch(label.Text, @"\bCORRECT\b") && !Regex.IsMatch(label.Text, @"\bWRONG\b");
                }
                var rows = store.Usage(DateOnly.MinValue).Where(r => r.Purpose == "replies").ToList();
                result = new Asked(item.Id, name, category, size, question, category == 5 ? "(not in the conversation)" : expected, answered,
                                   verdict.Text.Contains("yes", StringComparison.OrdinalIgnoreCase), where, inSearch, inRecall,
                                   rows.Sum(r => r.Input) + (verdict.Usage?.InputTokenCount ?? 0), rows.Sum(r => r.Cached), null) { Generous = generous };
            }
            catch (Exception x)
            {
                result = new Asked(item.Id, name, category, size, question, expected, "", false, "", false, false, 0, 0, x.Message);
            }
            lock (writing)
            {
                File.AppendAllText(file, JsonSerializer.Serialize(result) + "\n");
                if (result.Error is not null)
                    Console.WriteLine($"ERROR {result.Id}: {result.Error}");
            }
        });
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var copy in Directory.GetFiles(copies))
            try { File.Delete(copy); } catch (IOException) { }
    }

    // the tally, over everything at this window
    var tally = File.ReadLines(file).Select(l => JsonSerializer.Deserialize<Asked>(l)!).Where(r => r.Window == size && r.Error is null)
        .GroupBy(r => r.Id).Select(g => g.Last()).ToList();
    string[] kinds = ["", "multi-hop", "temporal", "open-domain", "single-hop", "unanswerable (adversarial)"];
    Console.WriteLine($"\nLoCoMo, a {size}-token window, {tally.Count} questions\n{"kind",-28} {"right",6} {"of",5}");
    foreach (var kind in tally.GroupBy(r => r.Category).OrderBy(g => g.Key))
        Console.WriteLine($"{kinds[kind.Key],-28} {kind.Count(r => r.Correct),6} {kind.Count(),5}");
    var known = tally.Where(r => r.Category != 5).ToList();
    if (known.Count > 0)
        Console.WriteLine($"{"all but unanswerable",-28} {known.Count(r => r.Correct),6} {known.Count,5}" +
                          (known.Any(r => r.Generous is not null) ? $"   by Mem0's generous grade: {known.Count(r => r.Generous == true)} of {known.Count(r => r.Generous is not null)}" : ""));
    foreach (var place in tally.Where(r => r.Category != 5).GroupBy(r => r.Evidence).OrderBy(g => g.Key))
        Console.WriteLine($"evidence {place.Key,-8}: {place.Count(),4} questions, {place.Count(r => r.Correct) * 100 / place.Count()}% right" +
                          (place.Key is "folded" or "both" ? $", her search found it for {place.Count(r => r.InSearch) * 100 / place.Count()}%, " +
                                                             $"and it reached her turn for {place.Count(r => r.InRecall) * 100 / place.Count()}%" : ""));
    Console.WriteLine($"{tally.Sum(r => r.Input) / 1000}k tokens read in answering ({tally.Sum(r => r.Cached) / 1000}k from the provider's cache)");
}

// --set persona: does she stay herself deep into a long conversation? Her window is filled to each depth with the newest
// lines of one LongMemEval history (--history, its first by default): someone asking an assistant about everything,
// her side of it written as a plain assistant would, so the conversation itself pulls her away from her card: the hard
// case, and a fair picture of hours of help with problems. At each depth the same few messages are put to her, with her
// voice on (--voice, Japanese by default), and a judge scores each reply against the card (--card, --name) for her
// manner, for nothing that contradicts it, and for reading as her rather than as an assistant
async Task StaysInCharacter()
{
    var card = File.ReadAllText(options["card"]).Trim();
    var name = options["name"];                           // whose card it is
    var character = name.ToLowerInvariant();
    var player = options.GetValueOrDefault("player", "Sam");
    var voice = options.GetValueOrDefault("voice", "ja");
    var depths = options.GetValueOrDefault("depths", "0,8000,32000,100000").Split(',').Select(int.Parse).ToList();
    var repeat = int.Parse(options.GetValueOrDefault("repeat", "2"));
    var histories = JsonSerializer.Deserialize<List<Item>>(File.OpenRead(data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
    var source = options.TryGetValue("history", out var wanted) ? histories.First(i => i.QuestionId == wanted) : histories[0];
    var turns = source.HaystackSessions.Select((t, i) => (At: When(source.HaystackDates[i]), Turns: t)).OrderBy(s => s.At)
        .SelectMany(s => s.Turns.Select((turn, k) => (At: s.At.AddSeconds(30 * k), Turn: turn))).ToList();
    string[] probes =
    [
        "hey, I'm back. long day.",
        "what do you even do all day while I'm away?",
        "I got two slices of strawberry cake, want one?",
        "I'm stuck on this binary search problem and I feel dumb",
        "be honest, was skipping the gym all week a bad idea?",
        "tell me something about yourself I don't know yet",
        "you're kind of hard to read sometimes, you know",
        "ok I'm going to bed",
    ];
    var file = Path.Combine(output, "persona.jsonl");
    var writing = new object();
    foreach (var depth in depths)
    {
        // the newest lines that add up to the depth
        var kept = new List<(DateTimeOffset At, Turn Turn)>();
        var tokens = 0;
        for (var i = turns.Count - 1; i >= 0 && tokens + Prompt.Tokens(turns[i].Turn.Content) + 4 <= depth; i--)
        {
            kept.Insert(0, turns[i]);
            tokens += Prompt.Tokens(turns[i].Turn.Content) + 4;
        }
        var path = Path.Combine(output, "stores", $"persona-{depth}.db");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        using (var store = new MindStore(path))
            foreach (var (at, turn) in kept)
                store.Add(character, turn.Role == "user" ? Speaker.User : Speaker.Her, turn.Content, at);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var now = kept.Count > 0 ? kept[^1].At.AddHours(2) : DateTimeOffset.Parse("2023-05-30T20:00:00Z");

        var asks = probes.SelectMany(p => Enumerable.Range(1, repeat).Select(trial => (Probe: p, Trial: trial))).ToList();
        await Parallel.ForEachAsync(asks, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (ask, cancel) =>
        {
            var copy = Path.Combine(output, "stores", $"persona-{depth}-{Guid.NewGuid():N}.db");
            File.Copy(path, copy);
            Played row;
            try
            {
                using var store = new MindStore(copy);
                var conversation = new Conversation(store, character, new Persona(name, card), new Clock(now)) { VoiceLanguage = voice, Window = depth + 4000 };
                var shown = new System.Text.StringBuilder();
                var answered = await conversation.Answer(client, ask.Probe, $"Player: {player} on LeetCode.", t => shown.Append(t), (_, _) => { });
                var reply = shown.ToString().Trim();
                if (reply.Length == 0)
                    throw new InvalidOperationException(Empty);
                var verdict = await client.GetResponseAsync([new ChatMessage(ChatRole.User, $$"""
                    You judge whether a companion character in an app stays in character. Her character card:

                    {{card}}

                    The player wrote to her: "{{ask.Probe}}"
                    Her reply (its English text, as shown): "{{reply}}"

                    Score from 1 to 5 and answer with JSON only: {"voice": 0, "consistent": 0, "herself": 0, "why": "a few words"}
                    "voice": 5 if this sounds like her (her temperament and way of speaking, as the card describes), 1 if nothing of her is left.
                    "consistent": 5 if nothing in it contradicts the card (who she is, her past, what she likes) and what she says of herself fits it, 1 if it plainly contradicts it.
                    "herself": 5 if it reads as her talking, 1 if it reads as a generic AI assistant (lists, headings, offers of help, customer-service phrasing).
                    """)], new ChatOptions { MaxOutputTokens = 1500 });
                int Score(string key)
                {
                    var text = verdict.Text;
                    var json = text.IndexOf('{') is var a and >= 0 && text.LastIndexOf('}') is var b && b > a ? text[a..(b + 1)] : "{}";
                    try { return JsonDocument.Parse(json).RootElement.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0; }
                    catch (JsonException) { return 0; }
                }
                string Why()
                {
                    var text = verdict.Text;
                    var json = text.IndexOf('{') is var a and >= 0 && text.LastIndexOf('}') is var b && b > a ? text[a..(b + 1)] : "{}";
                    try { return JsonDocument.Parse(json).RootElement.TryGetProperty("why", out var v) ? v.ToString() : ""; }
                    catch (JsonException) { return ""; }
                }
                row = new Played(depth, tokens, ask.Trial, ask.Probe, reply, Score("voice"), Score("consistent"), Score("herself"), Why(),
                                 reply.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                                 Regex.IsMatch(reply, @"(^|\n)\s*([-*•]|\d+\.|#+)\s"), null);
            }
            catch (Exception x)
            {
                row = new Played(depth, tokens, ask.Trial, ask.Probe, "", 0, 0, 0, "", 0, false, x.Message);
            }
            lock (writing)
            {
                File.AppendAllText(file, JsonSerializer.Serialize(row) + "\n");
                Console.WriteLine(row.Error is not null ? $"ERROR {depth} {ask.Probe}: {row.Error}"
                                                        : $"{depth,7} voice {row.Voice} consistent {row.Consistent} herself {row.Herself}  {ask.Probe}  ->  {row.Reply[..Math.Min(110, row.Reply.Length)]}");
            }
        });
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var copy in Directory.GetFiles(Path.Combine(output, "stores"), $"persona-{depth}-*.db*"))
            try { File.Delete(copy); } catch (IOException) { }
    }

    var all = File.ReadLines(file).Select(l => JsonSerializer.Deserialize<Played>(l)!).Where(r => r.Error is null).ToList();
    Console.WriteLine($"\n{"depth",8} {"replies",8} {"voice",6} {"consistent",11} {"herself",8} {"words",6} {"lists",6}");
    foreach (var d in all.GroupBy(r => r.Depth).OrderBy(g => g.Key))
        Console.WriteLine($"{d.Key,8} {d.Count(),8} {d.Average(r => r.Voice),6:0.0} {d.Average(r => r.Consistent),11:0.0} {d.Average(r => r.Herself),8:0.0} " +
                          $"{d.Average(r => r.Words),6:0} {d.Count(r => r.Lists),6}");
}

// a turn as it goes into her memory: what was said, and what a shared photo showed (LoCoMo's caption of it)
static string Said(JsonElement turn) =>
    turn.GetProperty("text").GetString() + (turn.TryGetProperty("blip_caption", out var caption) ? $" [shares a photo: {caption.GetString()}]" : "");

// "1:56 pm on 8 May, 2023", taken as UTC
static DateTimeOffset SessionTime(string text) =>
    new(DateTime.ParseExact(text.Replace(" am ", " AM ").Replace(" pm ", " PM "), "h:mm tt 'on' d MMMM, yyyy", System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero);

static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

// LongMemEval's own grading prompts (src/evaluation/evaluate_qa.py), word for word
static string JudgePrompt(Item item, string response) =>
    Grading(item.QuestionType, item.QuestionId.EndsWith("_abs"), item.Question, Answer(item), response);

static string Grading(string type, bool unanswerable, string question, string answer, string response)
{
    const string Plain = "I will give you a question, a correct answer, and a response from a model. Please answer yes if the response contains the correct answer. Otherwise, answer no. If the response is equivalent to the correct answer or contains all the intermediate steps to get the correct answer, you should also answer yes. If the response only contains a subset of the information required by the answer, answer no. ";
    var template = unanswerable
        ? "I will give you an unanswerable question, an explanation, and a response from a model. Please answer yes if the model correctly identifies the question as unanswerable. The model could say that the information is incomplete, or some other information is given but the asked information is not.\n\nQuestion: {0}\n\nExplanation: {1}\n\nModel Response: {2}\n\nDoes the model correctly identify the question as unanswerable? Answer yes or no only."
        : type switch
        {
            "single-session-user" or "single-session-assistant" or "multi-session" =>
                Plain + "\n\nQuestion: {0}\n\nCorrect Answer: {1}\n\nModel Response: {2}\n\nIs the model response correct? Answer yes or no only.",
            "temporal-reasoning" =>
                Plain + "In addition, do not penalize off-by-one errors for the number of days. If the question asks for the number of days/weeks/months, etc., and the model makes off-by-one errors (e.g., predicting 19 days when the answer is 18), the model's response is still correct. \n\nQuestion: {0}\n\nCorrect Answer: {1}\n\nModel Response: {2}\n\nIs the model response correct? Answer yes or no only.",
            "knowledge-update" =>
                "I will give you a question, a correct answer, and a response from a model. Please answer yes if the response contains the correct answer. Otherwise, answer no. If the response contains some previous information along with an updated answer, the response should be considered as correct as long as the updated answer is the required answer.\n\nQuestion: {0}\n\nCorrect Answer: {1}\n\nModel Response: {2}\n\nIs the model response correct? Answer yes or no only.",
            "single-session-preference" =>
                "I will give you a question, a rubric for desired personalized response, and a response from a model. Please answer yes if the response satisfies the desired response. Otherwise, answer no. The model does not need to reflect all the points in the rubric. The response is correct as long as it recalls and utilizes the user's personal information correctly.\n\nQuestion: {0}\n\nRubric: {1}\n\nModel Response: {2}\n\nIs the model response correct? Answer yes or no only.",
            var other => throw new NotSupportedException(other),
        };
    return string.Format(template, question, answer, response);
}

static List<Item> PickPerKind(List<Item> items, int count, int seed)
{
    var random = new Random(seed);
    return [.. items.GroupBy(Kind).OrderBy(g => g.Key).SelectMany(g => g.OrderBy(_ => random.Next()).Take(count))];
}

static string Kind(Item item) => item.QuestionType + (item.QuestionId.EndsWith("_abs") ? " (unanswerable)" : "");

static string Answer(Item item) => item.Answer.ValueKind == JsonValueKind.String ? item.Answer.GetString()! : item.Answer.GetRawText();

// "2023/05/20 (Sat) 02:21", taken as UTC
static DateTimeOffset When(string date) =>
    new(DateTime.ParseExact(Regex.Replace(date, @"\s*\(\w+\)", ""), "yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero);

static string Here([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

static Dictionary<string, string> Args(string[] args)
{
    var options = new Dictionary<string, string>();
    for (var i = 0; i + 1 < args.Length; i += 2)
        options[args[i].TrimStart('-')] = args[i + 1];
    return options;
}

partial class Program
{
    const string Character = "mira", Name = "Mira";
    // the provider sometimes ends a reply with nothing in it and no error (15 of 199 with about ten requests at once,
    // 2026-10-11): a failure to ask again, not a wrong answer
    const string Empty = "an empty reply from the provider";

    // Mem0's grading prompt for LoCoMo (Chhikara et al. 2025, arXiv 2504.19413, appendix A), word for word
    const string GenerousGrading = """
        Your task is to label an answer to a question as "CORRECT" or "WRONG". You will be given the following data:
        (1) a question (posed by one user to another user),
        (2) a 'gold' (ground truth) answer,
        (3) a generated answer
        which you will score as CORRECT/WRONG.

        The point of the question is to ask about something one user should know about the other user based on their prior conversations.
        The gold answer will usually be a concise and short answer that includes the referenced topic, for example:

        Question: Do you remember what I got the last time I went to Hawaii?

        Gold answer: A shell necklace

        The generated answer might be much longer, but you should be generous with your grading - as long as it touches on the same topic as the gold answer, it should be counted as CORRECT.

        For time related questions, the gold answer will be a specific date, month, year, etc. The generated answer might be much longer or use relative time references (like 'last Tuesday' or 'next month'), but you should be generous with your grading - as long as it refers to the same date or time period as the gold answer, it should be counted as CORRECT. Even if the format differs (e.g., 'May 7th' vs '7 May'), consider it CORRECT if it's the same date.

        Now it's time for the real question:

        Question: {0}

        Gold answer: {1}

        Generated answer: {2}

        First, provide a short (one sentence) explanation of your reasoning, then finish with CORRECT or WRONG.
        Do NOT include both CORRECT and WRONG in your response, or it will break the evaluation script.

        Just return the label CORRECT or WRONG in a json format with the key as "label".
        """;
}

sealed record Item(string QuestionId, string QuestionType, string Question, string QuestionDate, JsonElement Answer, List<string> AnswerSessionIds,
                   List<string> HaystackDates, List<string> HaystackSessionIds, List<List<Turn>> HaystackSessions);

sealed record Turn(string Role, string Content, bool? HasAnswer);

sealed record Asked(string Id, string Sample, int Category, int Window, string Question, string Expected, string Answered, bool Correct,
                    string Evidence, bool InSearch, bool InRecall, long Input, long Cached, string? Error)
{
    /// <summary>Right by Mem0's generous grade; null for unanswerable questions, and in runs from before it.</summary>
    public bool? Generous { get; init; }
}

sealed record Played(int Depth, int Tokens, int Trial, string Probe, string Reply, int Voice, int Consistent, int Herself, string Why, int Words,
                     bool Lists, string? Error);

sealed record Audit(string Id, string Kind, string Expected, bool Correct, string Evidence, int Found, int Searched, string Kept, string Wrong, long Input);

sealed record Result(string Id, string Kind, string Mode, int Window, string Question, string Expected, string Answered, bool Correct,
                     long Input, long Cached, long Output, long UpkeepInput, long ReplyInput, int Facts, int Moments, long Seconds, string? Error);

// A provider that fails for a while, as a busy provider does (HTTP 524: its gateway gave up after 100 s, four times on
// 2026-10-11, each costing a question all its upkeep), is asked again after a wait, up to five times; a streamed
// reply only before anything of it came
sealed class Patient(IChatClient inner) : DelegatingChatClient(inner)
{
    static bool Passing(Exception e) => e is System.ClientModel.ClientResultException { Status: 0 or 429 or >= 500 } or HttpRequestException or TimeoutException
                                        or TaskCanceledException { InnerException: TimeoutException };

    static Task Wait(int attempt) => Task.Delay(TimeSpan.FromSeconds(15 * Math.Pow(2, attempt)));

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancel = default)
    {
        for (var attempt = 0; ; attempt++)
            try
            {
                return await base.GetResponseAsync(messages, options, cancel);
            }
            catch (Exception e) when (attempt < 5 && Passing(e) && !cancel.IsCancellationRequested)
            {
                await Wait(attempt);
            }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                     [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var updates = base.GetStreamingResponseAsync(messages, options, cancel).GetAsyncEnumerator(cancel);
            ChatResponseUpdate? first = null;
            try
            {
                if (await updates.MoveNextAsync())
                    first = updates.Current;
            }
            catch (Exception e) when (attempt < 5 && Passing(e) && !cancel.IsCancellationRequested)
            {
                await updates.DisposeAsync();
                await Wait(attempt);
                continue;
            }
            if (first is not null)
            {
                yield return first;
                while (await updates.MoveNextAsync())
                    yield return updates.Current;
            }
            await updates.DisposeAsync();
            yield break;
        }
    }
}

sealed class Clock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
