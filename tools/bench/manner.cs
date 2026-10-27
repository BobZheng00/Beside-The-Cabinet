// How she talks, measured (docs/specs/2026-10-24-how-she-talks.md; spike #125, built in #127): about 20 moments in
// English and Chinese put to her through her real prompt, and the habits that make her sound like an assistant reporting
// its work counted in code (a self-check of the counting runs first). Run it when her instructions or tools change.
//
//     dotnet run tools/bench/manner.cs -- --out <folder> [--mock <base url> --protocol openai|anthropic | --from <profile folder>]
//                                         [--variant <name>] [--cards shino,plain] [--cases id,id] [--again]
//                                         [--max-requests 140] [--max-input 500000] [--card-files name=path,name-zh=path]
//
// --variant names the build's base text in the results ("this" by default). To compare two texts, run the bench built
// from each (say main's commit and a branch's: `dotnet build tools/bench/manner.cs -o <bin folder>` in each, then
// `dotnet <bin folder>/manner.dll`) into the same --out folder: the side by side and the counts take in every variant
// there. --mock runs on a stand-in (tools/bench/agenda_mock.py); --from runs on that profile's provider, its key read
// where it is kept, encrypted, and billed to whoever owns it: only when they offer (default: the demo profile). Each reply
// starts from a fresh memory (with the moment's earlier lines and agenda items), at Wednesday 21 October 2026 19:30, US
// Eastern, with the app's in-app tools and the agenda tool; the focus tool records what it was asked to do. It stops
// before a reply once the requests or input tokens spent in the folder (everything in manner.jsonl, every variant) reach
// the limits. Results go to <out>/manner.jsonl (what is there already is not asked again unless --again); the base text
// to <out>/prompt-<variant>.txt; the replies side by side and the counts to <out>/manner.md, from everything in the file.
// --card-files puts draft cards beside the shipped ones (name=path; "<name>-zh" is used for <name>'s Chinese moments).
#:property PublishAot=false
#:property NoWarn=CA1416
#:project ../../src/InsertCoin.Mind/InsertCoin.Mind.csproj
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsertCoin.Mind;
using Microsoft.Extensions.AI;

Console.OutputEncoding = Encoding.UTF8;
var options = Args(args);
var output = Directory.CreateDirectory(options["out"]).FullName;
var results = Path.Combine(output, "manner.jsonl");
var json = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var variant = options.GetValueOrDefault("variant", "this");
var cards = options.GetValueOrDefault("cards", "shino,plain").Split(',');
var maxRequests = int.Parse(options.GetValueOrDefault("max-requests", "140"));
var maxInput = long.Parse(options.GetValueOrDefault("max-input", "500000"));

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
// --card-files: Shino's draft cards to compare, {{user}} filled as no name given; "<name>-zh" is used for <name>'s Chinese moments
foreach (var pair in options.GetValueOrDefault("card-files", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
{
    var (name, path) = (pair[..pair.IndexOf('=')], pair[(pair.IndexOf('=') + 1)..]);
    personas[name] = new("Shino", File.ReadAllText(path).Replace("{{user}}", name.EndsWith("-zh") ? "玩家" : "the player").Trim());
}

// the base text as sent (with the plain card), for the reader
File.WriteAllText(Path.Combine(output, $"prompt-{variant}.txt"),
                  Prompt.Build(personas["plain"], "", "", null, [], "", "hi", now: now, tools: true, asking: ["open_link"], known: ["cabinet_numbers"])[0].Text);

// the counting checked on lines whose habits are known
if (string.Join(",", Habits.Of("I've got a job interview next Thursday at 2pm.", "[cheer] Got it! A job interview next Thursday at 2pm, I've added it. Want me to help you prep?"))
        != string.Join(",", Habits.Names) || Habits.Of("我拿到实习offer了！！！", "[cheer] 太棒了，我拿到实习offer了的感觉一定很好。").SequenceEqual(["echoes the player"]) is false
    || Habits.Of("lol", "Poor chat, right?").SequenceEqual(["ends with a question"]) is false
    // 一点 is "a little" unless the time of day or the minutes make it one o'clock
    || Habits.Of("番茄钟一般是多少分钟来着？", "一般是专注 25 分钟，再休息 5 分钟；做满几轮后休息长一点。").Count != 0
    || !Habits.Of("明天", "下午一点见。").Contains(Habits.Names[1]) || !Habits.Of("明天", "十一点见。").Contains(Habits.Names[1])
    || Habits.Words("One two.\n\nThree") != 3)
    throw new InvalidOperationException("The habits aren't counted as they should be.");

// what is said, what came just before (the player's lines and hers, a minute apart), and the agenda before it
Moment[] moments =
[
    new("vent", "en", "Ugh. My manager pulled me into a meeting just to tell me the feature I shipped last week has to be redone. Two weeks of work, gone."),
    new("vent-zh", "zh", "今天真的好累，组会上导师当着所有人的面把我的方案批了一顿。"),
    new("story", "en", "Okay so you won't believe my afternoon. I went to the café to study, right, and the guy at the next table starts " +
        "doing a LeetCode problem out loud, like whispering to himself. And it's Two Sum. And he's stuck. I sat there for twenty minutes " +
        "trying not to say anything, and then he packed up and left and I realised I'd done zero studying myself because I was so busy " +
        "rooting for him."),
    new("quick", "en", "How long do you boil an egg for a runny yolk?"),
    new("quick-zh", "zh", "番茄钟一般是多少分钟来着？"),
    new("start", "en", "Start a focus session.", Asked: ["focus_session"]),
    new("good", "en", "I finally solved Median of Two Sorted Arrays! The hard one. Took me the whole evening but I didn't look at the answer once."),
    new("good-zh", "zh", "我拿到实习offer了！！！"),
    new("bad", "en", "Didn't get the job. They went with someone internal."),
    new("lost", "en", "Just lost three ranked matches in a row. I'm done for tonight."),
    new("ahead", "en", "I've got a job interview next Thursday at 2pm.", Asked: ["agenda"]),
    new("ahead-zh", "zh", "下周三下午三点我要去看牙医。", Asked: ["agenda"]),
    new("remind", "en", "Remind me to take my vitamins at 9 tomorrow morning.", Asked: ["agenda"]),
    new("remind-zh", "zh", "明天下午四点提醒我给妈妈打电话。", Asked: ["agenda"]),
    new("moved", "en", "The dentist moved me to next Monday, same time.", Agenda: [("Dentist", "2026-10-23 15:30", 60)], Asked: ["agenda"]),
    new("cancelled", "en", "Dinner with Alex is off, he cancelled on me. Again.", Agenda: [("Dinner with Alex", "2026-10-24 19:00", null)], Asked: ["agenda"]),
    new("opinion", "en", "What do you think of Hollow Knight? Is it worth playing before Silksong?"),
    new("opinion-zh", "zh", "你觉得我应该先学Rust还是Go？"),
    new("lol", "en", "lol",
        Before: ["My cat just walked across my keyboard and sent my group chat \"jjjjjjjjjj;;;\"", "Honestly, that's the most anyone's said in that chat all week."]),
    new("listening", "en", "So I get to the station and the train's cancelled, fine, I get the bus. Bus breaks down. Halfway. In the rain. " +
        "And that's not even the worst part.",
        Before: ["This weekend was a mess, let me tell you.", "Go on, what happened?"]),
    new("talk-to-me", "en", "I'm wiped. Just talk to me about something, anything, I don't want to think."),
];
var allMoments = moments;
if (options.TryGetValue("cases", out var only))
    moments = [.. moments.Where(m => only.Split(',').Contains(m.Id))];

var earlier = File.Exists(results) ? File.ReadLines(results).Select(l => JsonSerializer.Deserialize<Result>(l)!).ToList() : [];
var done = options.ContainsKey("again") ? [] : earlier.Where(r => r.Error is null).Select(r => $"{r.Card}/{r.Variant}/{r.Moment}").ToHashSet();
// spent in this folder, every variant and earlier run included, for the limits
var (requests, input) = (earlier.Sum(r => r.Requests.Count), earlier.Sum(r => r.Input));
var (requestsBefore, inputBefore) = (requests, input);
foreach (var card in cards)
    foreach (var m in moments)
    {
        if (done.Contains($"{card}/{variant}/{m.Id}"))
            continue;
        if (requests >= maxRequests || input >= maxInput)
        {
            Console.WriteLine($"Stopped at the limits: {requests} requests, {input} input tokens spent in {output}.");
            goto summary;
        }
        var result = await Run(card, m);
        requests += result.Requests.Count;
        input += result.Input;
        File.AppendAllText(results, JsonSerializer.Serialize(result, json) + "\n");
        Console.WriteLine($"{card,-6} {variant,-6} {m.Id,-11} {result.Requests.Count} req, {result.Input} in: {result.Answer ?? result.Error}");
    }
summary:
Console.WriteLine($"This run: {requests - requestsBefore} requests, {input - inputBefore} input tokens; the folder: {requests}, {input}.");
Summary();

async Task<Result> Run(string card, Moment m)
{
    var folder = Directory.CreateDirectory(Path.Combine(output, "stores", $"{card}-{variant}-{m.Id}")).FullName;
    foreach (var file in Directory.GetFiles(folder))
        File.Delete(file);
    using var store = new MindStore(Path.Combine(folder, "memory.db"));
    foreach (var (text, when, minutesBefore) in m.Agenda ?? [])
        store.AddPlan(text, Agenda.ParseWhen(when, now, zone)!.Value.At, false, minutesBefore is { } b ? TimeSpan.FromMinutes(b) : null, now.AddDays(-2), card);
    var said = m.Before ?? [];
    for (var i = 0; i < said.Length; i++)
        store.Add(card, i % 2 == 0 ? Speaker.User : Speaker.Her, said[i], now.AddMinutes(i - said.Length));
    var persona = m.Language == "zh" && personas.TryGetValue($"{card}-zh", out var chinese) ? chinese : personas[card];
    var conversation = new Conversation(store, card, persona, new Clock(now, zone));
    var counting = new Counting(model);
    var focus = new List<string>();
    string? answer = null, error = null;
    try
    {
        answer = await conversation.Answer(counting, m.Said, "", _ => { }, (_, _) => { }, tools: Tools(focus),
                                           cancel: new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token);
    }
    catch (Exception e)
    {
        error = $"{e.GetType().Name}: {e.Message}";
    }
    return new Result(card, variant, m.Id, m.Language, m.Said, answer, error,
                      [.. store.Agenda().Select(p => $"#{p.Id} {p.Label}{(p.AllDay ? " (whole day)" : "")}: {p.Text}" +
                                                    (p.Before is { } b ? $", reminder {(b == TimeSpan.Zero ? "at the time" : Agenda.Describe(b))}" : "") +
                                                    (p.Repeat != Repeat.Once ? $", {Agenda.Describe(p.Repeat)}" : ""))],
                      counting.Requests, counting.Requests.Sum(r => r.Input), counting.Requests.Sum(r => r.Cached), counting.Requests.Sum(r => r.Output), focus);
}

// the app's in-app tools, doing nothing, so that what she is offered (and what it costs) is as in the app; the focus
// tool keeps what it was asked to do, so a session started is counted, not only a call made (#145)
static Toolbox Tools(List<string> focus) => new([BuiltIn.OpenLink(_ => { }), BuiltIn.PrintMemo((_, _) => "Printed."),
                               BuiltIn.Focus((action, minutes, rest) =>
                               {
                                   focus.Add($"{action}{(minutes is { } f ? $" focus {f} min" : "")}{(rest is { } b ? $" break {b} min" : "")}");
                                   return action == "start"
                                       ? "Started. Focusing (round 1): 25 min left of 25. The screen counts down; you'll be told when the part ends."
                                       : "Done.";
                               }),
                               BuiltIn.Music((_, _) => Task.FromResult("Done."), _ => "Focus"), BuiltIn.LookAtScreen(_ => "Nothing on screen.", () => null),
                               BuiltIn.LogActivity((_, _, _, _, _) => Task.FromResult("Logged.")), BuiltIn.JudgeProof((_, _, _) => "Judged."),
                               BuiltIn.SetBedtime((_, _) => Task.FromResult("Set.")), BuiltIn.WindowTitles(() => []),
                               BuiltIn.ReadPage((_, _) => Task.FromResult("")), BuiltIn.LeetCodeProblem((_, _) => Task.FromResult("")),
                               BuiltIn.OpenApp(() => [], _ => { }), BuiltIn.Cabinet(_ => "The cabinet has no numbers yet.")]);

void Summary()
{
    if (!File.Exists(results))
    {
        Console.WriteLine($"Nothing in {results} yet.");
        return;
    }
    var all = File.ReadLines(results).Select(l => JsonSerializer.Deserialize<Result>(l)!).Where(r => r.Answer is not null).ToList();
    var variants = all.Select(r => r.Variant).Distinct().ToList();      // in the order they were run
    var moments = all.Select(r => r.Moment).Distinct().Select(id => allMoments.First(m => m.Id == id)).ToArray();
    var md = new StringBuilder();
    md.AppendLine("## The habits, per variant and card\n");
    md.AppendLine("| | " + string.Join(" | ", Habits.Names) + " | words (en) | characters (zh) | ! per reply | tools used unasked | focus started |");
    md.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", Habits.Names.Length + 5)));
    foreach (var g in all.GroupBy(r => (r.Variant, r.Card)).OrderBy(g => variants.IndexOf(g.Key.Variant)).ThenBy(g => g.Key.Card))
    {
        var n = g.Count();
        var counts = Habits.Names.Select(name => g.Count(r => Habits.Of(r.Said, r.Answer!).Contains(name)));
        var en = g.Where(r => r.Language == "en").Select(r => Habits.Words(r.Answer!)).ToList();
        var zh = g.Where(r => r.Language == "zh").Select(r => Habits.Characters(r.Answer!)).ToList();
        md.AppendLine($"| {g.Key.Variant}, {g.Key.Card} ({n}) | {string.Join(" | ", counts)} | median {Median(en)}, max {en.DefaultIfEmpty().Max()} | " +
                      $"median {Median(zh)}, max {zh.DefaultIfEmpty().Max()} | {g.Average(r => Habits.Bangs(r.Answer!)):0.00} | " +
                      $"{g.Count(r => Unasked(r).Any())} | {g.Count(Started)} |");
    }
    var spent = File.ReadLines(results).Select(l => JsonSerializer.Deserialize<Result>(l)!).ToList();
    md.AppendLine($"\nSpent: {spent.Sum(r => r.Requests.Count)} requests, {spent.Sum(r => r.Input)} input tokens ({spent.Sum(r => r.Cached)} cached), {spent.Sum(r => r.Output)} output.");
    md.AppendLine("\nTools used unasked: a reply that called a tool its moment didn't ask for (cabinet_numbers, which only reads, aside). " +
                  "Focus started: a reply whose focus tool actually started a session (only the start moment asks for one).");
    md.AppendLine("\n## Focus started, per moment\n");
    md.AppendLine("| | " + string.Join(" | ", moments.Select(m => m.Id)) + " |");
    md.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", moments.Length)));
    foreach (var g in all.GroupBy(r => (r.Variant, r.Card)).OrderBy(g => variants.IndexOf(g.Key.Variant)).ThenBy(g => g.Key.Card))
        md.AppendLine($"| {g.Key.Variant}, {g.Key.Card} | " +
                      string.Join(" | ", moments.Select(m => g.FirstOrDefault(r => r.Moment == m.Id) is { } r ? (Started(r) ? "**yes**" : "no") : "")) + " |");
    md.AppendLine("\n## Side by side\n");
    foreach (var g in all.GroupBy(r => r.Moment))
    {
        md.AppendLine($"**{g.Key}**: \"{g.First().Said}\"\n");
        foreach (var r in g.OrderBy(r => r.Card).ThenBy(r => variants.IndexOf(r.Variant)))
        {
            var calls = string.Concat(r.Requests.SelectMany(q => q.Calls).Select(c => $" `{c}`"));
            var habits = Habits.Of(r.Said, r.Answer!);
            md.AppendLine($"- {r.Card}, {r.Variant}: {r.Answer!.Replace("\n", " ")}{calls}" +
                          (habits.Count == 0 ? "" : $" *({string.Join(", ", habits)})*") +
                          (r.Agenda.Count == 0 ? "" : $" Agenda: {string.Join("; ", r.Agenda)}.") +
                          (r.Focus is { Count: > 0 } f ? $" Focus: {string.Join("; ", f)}." : ""));
        }
        md.AppendLine();
    }
    File.WriteAllText(Path.Combine(output, "manner.md"), md.ToString());
    Console.WriteLine(md.ToString());
}

// the tools a reply called that its moment didn't ask for; cabinet_numbers only reads what she would simply know
IEnumerable<string> Unasked(Result r) => r.Requests.SelectMany(q => q.Calls).Select(c => c[..c.IndexOf('(')])
                                          .Where(name => name != "cabinet_numbers" && allMoments.First(m => m.Id == r.Moment).Asked?.Contains(name) != true);

static bool Started(Result r) => r.Focus?.Any(f => f.StartsWith("start")) == true;

static int Median(List<int> values) => values.Count == 0 ? 0 : values.Order().ElementAt(values.Count / 2);

static string Here([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

static Dictionary<string, string> Args(string[] args)
{
    var options = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
        if (args[i].StartsWith("--"))
            options[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
    return options;
}

/// <summary>The habits #106 counted in real replies, found in code: what makes her sound like an assistant reporting its work.</summary>
static class Habits
{
    public static readonly string[] Names = ["stock opener", "repeats a date or time", "echoes the player", "narrates the bookkeeping", "offers help", "ends with a question"];

    private static readonly Regex Opener = new(@"^\W*(got it|sure( thing)?|okay|ok|noted|alright|of course|absolutely|done|perfect|great|好的|收到|没问题|明白|了解)\b", RegexOptions.IgnoreCase);
    private static readonly Regex DateOrTime = new(@"(\b\d{1,2}(:\d{2})?\s?(am|pm)\b|\b\d{1,2}:\d{2}\b|\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday|tomorrow)\b|\d+点|(?<![零一二两三四五六七八九十])(?:(?!一点)[零一二两三四五六七八九十]+点|一点(?=钟|半|[零一二三四五六七八九十]+分)|(?<=上午|下午|中午|晚上|早上|凌晨|傍晚)一点)|周[一二三四五六日]|星期[一二三四五六日]|明天)", RegexOptions.IgnoreCase);
    private static readonly Regex Bookkeeping = new(@"(i'?ve (added|jotted|noted|set|moved|cancel|put|updated|scheduled|marked|got)|i'?ll remind|jott|noted|on (your|the) agenda|in (your|the) agenda|off (your|the) agenda|reminder('s| is)? (set|on)|(it'?s|that'?s) (down|in)|cabinet'?s? (updated|got|will)|已(经)?(记|加|设|帮你|取消|改)|记下|记好|加到|设好|提醒你)", RegexOptions.IgnoreCase);
    private static readonly Regex Help = new(@"(want me to|let me know|anything else|need anything|if you need|if you want|shall i|should i|i can help|happy to help|要不要|需要我|我可以帮)", RegexOptions.IgnoreCase);
    private static readonly Regex Question = new(@"[?？]\W*$");
    private static readonly Regex Directions = new(@"\[[^\]]*\]");

    /// <summary>What she says without her stage directions.</summary>
    public static string Spoken(string answer) => Directions.Replace(answer, "").Trim();

    public static List<string> Of(string said, string answer)
    {
        var spoken = Spoken(answer);
        var found = new List<string>();
        if (Opener.IsMatch(spoken)) found.Add(Names[0]);
        if (DateOrTime.IsMatch(spoken)) found.Add(Names[1]);
        if (Echoes(said, spoken)) found.Add(Names[2]);
        if (Bookkeeping.IsMatch(spoken)) found.Add(Names[3]);
        if (Help.IsMatch(spoken)) found.Add(Names[4]);
        if (Question.IsMatch(spoken)) found.Add(Names[5]);
        return found;
    }

    /// <summary>Four words of the player's in a row in her reply (or six characters of Chinese): their words said back.</summary>
    public static bool Echoes(string said, string spoken)
    {
        if (Regex.IsMatch(said, @"\p{IsCJKUnifiedIdeographs}"))
        {
            var a = Regex.Replace(said, @"[\p{P}\s]", "");
            var b = Regex.Replace(spoken, @"[\p{P}\s]", "");
            return Enumerable.Range(0, Math.Max(0, a.Length - 5)).Any(i => b.Contains(a.Substring(i, 6)));
        }
        string[] Words(string s) => Regex.Matches(s.ToLowerInvariant(), @"[\p{L}\d']+").Select(w => w.Value).ToArray();
        var mine = Words(spoken);
        var theirs = Words(said);
        var spans = Enumerable.Range(0, Math.Max(0, mine.Length - 3)).Select(i => string.Join(' ', mine[i..(i + 4)])).ToHashSet();
        return Enumerable.Range(0, Math.Max(0, theirs.Length - 3)).Any(i => spans.Contains(string.Join(' ', theirs[i..(i + 4)])));
    }

    public static int Words(string answer) => Spoken(answer).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    public static int Characters(string answer) => Regex.Replace(Spoken(answer), @"[\p{P}\s]", "").Length;
    public static int Bangs(string answer) => Spoken(answer).Count(c => c is '!' or '！');
}

/// <summary>
/// What the player says, what came just before, the agenda before it (with a reminder so many minutes ahead, so a change
/// that drops one shows in the reply's agenda), and the tools it asks for (any other is used unasked).
/// </summary>
record Moment(string Id, string Language, string Said, string[]? Before = null, (string Text, string When, int? MinutesBefore)[]? Agenda = null, string[]? Asked = null);

record Request(string Text, List<string> Calls, long Input, long Cached, long Output, long Ms);

record Result(string Card, string Variant, string Moment, string Language, string Said, string? Answer, string? Error,
              List<string> Agenda, List<Request> Requests, long Input, long Cached, long Output, List<string>? Focus = null);

/// <summary>Each request to the model: its words, the calls it made, its tokens and how long it took.</summary>
sealed class Counting(IChatClient inner) : DelegatingChatClient(inner)
{
    public List<Request> Requests { get; } = [];

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        var text = new StringBuilder();
        var calls = new List<string>();
        UsageDetails usage = new();
        // a request that fails part way is still counted, with what its usage said so far (none if it never came)
        try
        {
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
        }
        finally
        {
            Requests.Add(new Request(text.ToString(), calls, usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0, usage.OutputTokenCount ?? 0, clock.ElapsedMilliseconds));
        }
    }
}

sealed class Clock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => zone;
}
