// Spike #105: her agenda set three ways, the same requests put to her in each, side by side.
//   tags   [plan], [remind], [change], [cancel] in her words, read after (what is built)
//   tool   the agenda tool; its result goes back to her before she says more (B)
//   said   the agenda tool called with her line; a call that went through ends the reply (C)
//
//     dotnet run tools/bench/agenda.cs -- --out <folder> [--mock <base url> --protocol openai|anthropic | --from <profile folder>]
//                                         [--flows tags,tool,said] [--cards shino,plain] [--cases id,id] [--again]
//
// --mock runs on a stand-in (tools/bench/agenda_mock.py); --from runs on that profile's provider, its key read where it is
// kept, encrypted, and billed to whoever owns it: only when they offer. Each reply starts from a fresh memory (with the
// case's agenda items), at Wednesday 21 October 2026 19:30, US Eastern, with the app's in-app tools beside the agenda.
// Results go to <out>/agenda.jsonl (what is there already is not asked again unless --again); a side-by-side summary is
// printed at the end, from everything in that file.
#:property PublishAot=false
#:property NoWarn=CA1416
#:project ../../src/InsertCoin.Mind/InsertCoin.Mind.csproj
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var options = Args(args);
var output = Directory.CreateDirectory(options["out"]).FullName;
var results = Path.Combine(output, "agenda.jsonl");
var json = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var flows = options.GetValueOrDefault("flows", "tags,tool,said").Split(',');
var cards = options.GetValueOrDefault("cards", "shino,plain").Split(',');

Provider provider;
string? key;
if (options.TryGetValue("mock", out var mock))
    (provider, key) = (Provider.From(options.GetValueOrDefault("protocol", "openai"), mock, "mock")!, "none");
else
{
    var profile = options.GetValueOrDefault("from", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InsertCoin-demo"));
    var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "settings.json"))).RootElement;
    provider = Provider.From(settings.TryGetProperty("ChatProtocol", out var protocol) ? protocol.GetString() : null,
                             settings.GetProperty("ChatBaseUrl").GetString(), settings.GetProperty("ChatModel").GetString())
               ?? throw new InvalidOperationException("That profile has no provider set.");
    key = new KeyStore(Path.Combine(profile, "companion", "key.bin")).Load();
}
var model = provider.Connect(key);

var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
var now = new DateTimeOffset(2026, 10, 21, 19, 30, 0, TimeSpan.FromHours(-4));
var personas = new Dictionary<string, Persona>
{
    ["shino"] = new("Shino", File.ReadAllText(Path.Combine(Here(), "../../src/InsertCoin.Game/characters/shino/persona.md")).Trim()),
    ["plain"] = Persona.Plain("Robin"),
};

// what is said, the agenda before it, and what should come of it (for the reader: nothing checks it)
Case[] cases =
[
    new("interview", "en", "I've got a job interview next Thursday at 2pm.", [], "Thu 29 Oct 14:00, Job interview"),
    new("vitamins", "en", "Remind me to take my vitamins at 9 tomorrow morning.", [], "Thu 22 Oct 09:00, a reminder at the time"),
    new("monday", "en", "Gym every Monday at 7am from now on, remind me an hour before.", [], "Mon 26 Oct 07:00, every week, 1 hour before"),
    new("no-reminder", "en", "Put Mia's birthday on Saturday on the agenda. No reminder, I won't forget.", [], "Sat 24 Oct (whole day), no reminder"),
    new("ten-min", "en", "Dentist Friday at 3:30. Remind me, 10 min before.", [], "Fri 23 Oct 15:30, 10 min before"),
    new("vague", "en", "Remind me to call the bank sometime after they open on Monday.", [], "Mon 26 Oct, a morning time she picks (or asks)"),
    new("zh-dentist", "zh", "下周三下午三点我要去看牙医，提前一小时提醒我。", [], "Wed 28 Oct 15:00, 1 hour before"),
    new("zh-weekly", "zh", "以后每周一早上七点提醒我去健身房。", [], "Mon 26 Oct 07:00, every week, a reminder at the time"),
    new("change", "en", "The dentist moved me to next Monday, same time.", [("Dentist", "2026-10-23 15:30")], "the Dentist item moved to Mon 26 Oct 15:30"),
    new("cancel", "en", "Dinner with Alex is off, he cancelled on me.", [("Dinner with Alex", "2026-10-24 19:00")], "the Dinner item cancelled"),
];
if (options.TryGetValue("cases", out var only))
    cases = [.. cases.Where(c => only.Split(',').Contains(c.Id))];

var done = File.Exists(results) && !options.ContainsKey("again")
    ? File.ReadLines(results).Select(l => JsonDocument.Parse(l).RootElement).Where(r => !r.TryGetProperty("error", out var e) || e.ValueKind == JsonValueKind.Null)
          .Select(r => $"{r.GetProperty("card").GetString()}/{r.GetProperty("flow").GetString()}/{r.GetProperty("case").GetString()}").ToHashSet()
    : [];
foreach (var card in cards)
    foreach (var flow in flows)
        foreach (var c in cases)
        {
            if (done.Contains($"{card}/{flow}/{c.Id}"))
                continue;
            var result = await Run(card, flow, c);
            File.AppendAllText(results, JsonSerializer.Serialize(result, json) + "\n");
            Console.WriteLine($"{card,-6} {flow,-5} {c.Id,-12} {result.Requests.Count} req, {result.FirstWordMs,6} ms / {result.EndMs,6} ms: {result.Answer ?? result.Error}");
        }
Summary();

async Task<Result> Run(string card, string flow, Case c)
{
    var folder = Directory.CreateDirectory(Path.Combine(output, "stores", $"{card}-{flow}-{c.Id}")).FullName;
    foreach (var file in Directory.GetFiles(folder))
        File.Delete(file);
    using var store = new MindStore(Path.Combine(folder, "memory.db"));
    foreach (var (text, when) in c.Agenda)
        store.AddPlan(text, Agenda.ParseWhen(when, now, zone)!.Value.At, false, null, now.AddDays(-2), card);
    var conversation = new Conversation(store, card, personas[card], new Clock(now, zone))
    {
        AgendaFlow = flow switch { "tool" => AgendaFlow.ToolFirst, "said" => AgendaFlow.SaidAsWritten, _ => AgendaFlow.Tags },
    };
    var counting = new Counting(model);
    var clock = Stopwatch.StartNew();
    long? first = null;
    var shown = new List<string>();
    var directions = new List<string>();
    string? answer = null, error = null;
    try
    {
        answer = await conversation.Answer(counting, c.Said, "", piece =>
        {
            if (piece.Trim().Length > 0)
                first ??= clock.ElapsedMilliseconds;
            shown.Add(piece);
        }, (name, payload) => directions.Add(payload is null ? name : $"{name}: {payload}"), tools: Tools(), cancel: new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token);
    }
    catch (Exception e)
    {
        error = $"{e.GetType().Name}: {e.Message}";
    }
    var lines = store.After(card, 0);
    return new Result(card, flow, c.Id, c.Language, c.Said, c.Expected, answer, error, directions,
                      [.. lines.Where(l => l.Speaker == Speaker.Cabinet).Select(l => l.Text)],
                      [.. store.Agenda().Select(p => $"#{p.Id} {p.Label}{(p.AllDay ? " (whole day)" : "")}: {p.Text}" +
                                                    (p.Before is { } b ? $", reminder {(b == TimeSpan.Zero ? "at the time" : Agenda.Describe(b))}" : "") +
                                                    (p.Repeat != Repeat.Once ? $", {Agenda.Describe(p.Repeat)}" : ""))],
                      counting.Requests, first, clock.ElapsedMilliseconds,
                      counting.Requests.Sum(r => r.Input), counting.Requests.Sum(r => r.Cached), counting.Requests.Sum(r => r.Output),
                      answer is not null && Regex.IsMatch(answer.Trim(), "[?？]$"));
}

// the app's in-app tools, doing nothing, so that what she is offered (and what it costs) is as in the app
static Toolbox Tools() => new([BuiltIn.OpenLink(_ => { }), BuiltIn.PrintMemo((_, _) => "Printed."), BuiltIn.Focus((_, _, _) => "Started."),
                               BuiltIn.Music((_, _) => Task.FromResult("Done."), _ => "Focus"), BuiltIn.LookAtScreen(_ => "Nothing on screen.", () => null),
                               BuiltIn.LogActivity((_, _, _, _, _) => Task.FromResult("Logged.")), BuiltIn.JudgeProof((_, _, _) => "Judged."),
                               BuiltIn.SetBedtime((_, _) => Task.FromResult("Set.")), BuiltIn.WindowTitles(() => []),
                               BuiltIn.ReadPage((_, _) => Task.FromResult("")), BuiltIn.LeetCodeProblem((_, _) => Task.FromResult("")),
                               BuiltIn.OpenApp(() => [], _ => { }), BuiltIn.Cabinet(_ => "The cabinet has no numbers yet.")]);

void Summary()
{
    var all = File.ReadLines(results).Select(l => JsonSerializer.Deserialize<Result>(l)!).ToList();
    Console.WriteLine("\n== side by side ==");
    foreach (var group in all.GroupBy(r => (r.Card, r.Case)))
    {
        Console.WriteLine($"\n[{group.Key.Card}] {group.Key.Case}: \"{group.First().Said}\"  (wanted: {group.First().Expected})");
        foreach (var r in group.OrderBy(r => Array.IndexOf(flows, r.Flow)))
        {
            var spoken = string.Join(" / ", r.Requests.Select(q => (q.Text.Trim() is { Length: > 0 } t ? $"\"{t}\"" : "(no words)") + string.Concat(q.Calls.Select(call => $" -> {call}"))));
            Console.WriteLine($"  {r.Flow,-5} {spoken}");
            Console.WriteLine($"        agenda: {(r.Agenda.Count == 0 ? "(empty)" : string.Join("; ", r.Agenda))}{(r.Error is null ? "" : $"  ERROR {r.Error}")}");
        }
    }
    Console.WriteLine("\n== per flow ==");
    Console.WriteLine("flow   replies  requests  words-before-tool  words-after-tool  ends-in-?  first-word ms  end ms   tokens in (cached) / out");
    foreach (var f in all.GroupBy(r => r.Flow))
    {
        var n = f.Count();
        var tooled = f.Where(r => r.Requests.Any(q => q.Calls.Count > 0)).ToList();
        // words in the request that made her first call; words in any request after it
        int Before(Result r) => r.Requests.First(q => q.Calls.Count > 0).Text.Trim().Length > 0 ? 1 : 0;
        int After(Result r) => r.Requests.SkipWhile(q => q.Calls.Count == 0).Skip(1).Any(q => q.Text.Trim().Length > 0) ? 1 : 0;
        Console.WriteLine($"{f.Key,-6} {n,7}  {f.Sum(r => r.Requests.Count),8}  {tooled.Sum(Before),8}/{tooled.Count,-8}  {tooled.Sum(After),8}/{tooled.Count,-7}  {f.Count(r => r.EndsInQuestion),5}/{n,-3}" +
                          $"  {Median(f.Select(r => r.FirstWordMs ?? r.EndMs)),12}  {Median(f.Select(r => r.EndMs)),6}   {f.Sum(r => r.Input)} ({f.Sum(r => r.Cached)}) / {f.Sum(r => r.Output)}");
    }
    Console.WriteLine("(words-before/after-tool count only replies that called a tool; tags never do: read their words in the side-by-side)");
}

static long Median(IEnumerable<long> values) => values.Order().ToList() is { Count: > 0 } list ? list[list.Count / 2] : 0;

static string Here([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

static Dictionary<string, string> Args(string[] args)
{
    var options = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
        if (args[i].StartsWith("--"))
            options[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
    return options;
}

record Case(string Id, string Language, string Said, (string Text, string When)[] Agenda, string Expected);

record Request(string Text, List<string> Calls, long Input, long Cached, long Output, long Ms);

record Result(string Card, string Flow, string Case, string Language, string Said, string Expected, string? Answer, string? Error,
              List<string> Directions, List<string> Cabinet, List<string> Agenda, List<Request> Requests, long? FirstWordMs, long EndMs,
              long Input, long Cached, long Output, bool EndsInQuestion);

/// <summary>Each request to the model: its words, the calls it made, its tokens and how long it took.</summary>
sealed class Counting(IChatClient inner) : DelegatingChatClient(inner)
{
    public List<Request> Requests { get; } = [];

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        var text = new System.Text.StringBuilder();
        var calls = new List<string>();
        UsageDetails usage = new();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancel))
        {
            text.Append(update.Text);
            foreach (var content in update.Contents)
                if (content is FunctionCallContent call)
                    calls.Add($"{call.Name}({JsonSerializer.Serialize(call.Arguments, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })})");
                else if (content is UsageContent used)
                    usage.Add(used.Details);
            yield return update;
        }
        Requests.Add(new Request(text.ToString(), calls, usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0, usage.OutputTokenCount ?? 0, clock.ElapsedMilliseconds));
    }
}

sealed class Clock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => zone;
}
