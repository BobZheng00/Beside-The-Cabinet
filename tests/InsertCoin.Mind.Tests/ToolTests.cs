using Microsoft.Extensions.AI;
using static InsertCoin.Mind.Tests.FakeToolModel;

namespace InsertCoin.Mind.Tests;

/// <summary>Her tools: used in the middle of a reply, asking the player first where they want that.</summary>
public class ToolTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    /// <summary>A tool that acts inside the app: she may use it without asking.</summary>
    private static Tool Jot(List<string> jotted) =>
        new(AIFunctionFactory.Create((string text) => { jotted.Add(text); return "Jotted down."; }, "jot", "Jots something down."), "Jot it down", OutsideApp: false);

    private static AIFunctionArguments Url(string url) => new() { ["url"] = url };

    [Fact]
    public async Task A_tool_she_may_use_runs_mid_reply_and_she_carries_on_after_it()
    {
        using var temp = new TempStore();
        var jotted = new List<string>();
        var asked = new List<Asking>();
        var tools = new Toolbox([Jot(jotted)]) { AskPlayer = (asking, _) => { asked.Add(asking); return Task.FromResult(Answer.Once); } };
        var used = new List<string>();
        tools.Using += tool => used.Add(tool.Name);
        var model = new FakeToolModel(["Milk?", " Got it,", Call("jot", "text", "buy milk")], ["writing it down."]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var shown = new List<string>();

        var answer = await conversation.Answer(model, "remind me to buy milk", "", shown.Add, (_, _) => { }, tools: tools);

        Assert.Equal("Milk? Got it, writing it down.", answer);
        Assert.Equal(answer, string.Concat(shown));
        Assert.Equal(["buy milk"], jotted);
        Assert.Equal(["jot"], used);
        Assert.Empty(asked);
        // the model was told what the tool did, and the tool use is logged between the player's line and hers
        Assert.Contains(model.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>(), r => r.Result?.ToString() == "Jotted down.");
        Assert.Equal([(Speaker.User, "remind me to buy milk"), (Speaker.Cabinet, "You used jot ({\"text\":\"buy milk\"}): Jotted down."), (Speaker.Her, answer)],
                     temp.Store.After("shino", 0).Select(l => (l.Speaker, l.Text)));
    }

    [Fact]
    public async Task Opening_a_page_asks_first_and_a_no_leaves_it_unopened()
    {
        using var temp = new TempStore();
        var opened = new List<Uri>();
        var asked = new List<Asking>();
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)]) { AskPlayer = (asking, _) => { asked.Add(asking); return Task.FromResult(Answer.No); } };
        var model = new FakeToolModel(["Want me to open it?", Call("open_link", "url", "https://www.leetcode.com/problems/two-sum/")], ["Okay, never mind."]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        var answer = await conversation.Answer(model, "where was that problem?", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Want me to open it? Okay, never mind.", answer);
        Assert.Empty(opened);
        var asking = Assert.Single(asked);
        Assert.Equal(("Open this page in your browser?", "www.leetcode.com/problems/two-sum/", "leetcode.com"), (asking.What, asking.Detail, asking.Scope));
        Assert.Contains(model.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>(), r => r.Result!.ToString()!.Contains("said no"));
        Assert.Contains(temp.Store.After("shino", 0), l => l.Speaker == Speaker.Cabinet && l.Text.Contains("the player said no"));
    }

    [Fact]
    public async Task Always_allowing_a_site_lets_her_open_its_pages_without_asking_again()
    {
        var opened = new List<Uri>();
        var asked = new List<string>();
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)]) { AskPlayer = (asking, _) => { asked.Add(asking.Detail); return Task.FromResult(Answer.Always); } };
        var changed = 0;
        tools.Changed += () => changed++;
        var open = tools.All.Single().Function;

        await tools.Use(open, Url("https://leetcode.com/problemset/"));
        await tools.Use(open, Url("https://leetcode.com/problems/two-sum/"));
        await tools.Use(open, Url("https://example.com/"));

        Assert.Equal(["leetcode.com/problemset/", "example.com"], asked);          // the second leetcode page was not asked about
        Assert.Equal(3, opened.Count);
        Assert.Equal(2, changed);
        Assert.Equal(Permission.Allowed, tools.Chosen["open_link:leetcode.com"]);
        Assert.Equal(Permission.Ask, tools.PermissionOf(tools.All.Single()));       // the tool itself still asks for other sites

        tools.Choose(new Dictionary<string, Permission>(tools.Chosen) { ["open_link"] = Permission.Off });
        Assert.Equal(Permission.Off, tools.PermissionOf(tools.All.Single(), "leetcode.com"));       // switched off is off, sites and all
    }

    [Fact]
    public async Task In_auto_she_goes_ahead_without_asking_but_what_is_off_stays_off()
    {
        var opened = new List<Uri>();
        var asked = 0;
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)]) { Auto = true, AskPlayer = (_, _) => { asked++; return Task.FromResult(Answer.No); } };
        var open = tools.All.Single().Function;

        var (_, record) = await tools.Use(open, Url("https://leetcode.com/problemset/"));

        Assert.Equal(0, asked);
        Assert.Single(opened);
        Assert.StartsWith("You used open_link", record);
        Assert.Empty(tools.Chosen);                                                     // nothing remembered: auto is a mode, not a choice per tool

        tools.Choose(new Dictionary<string, Permission> { ["open_link"] = Permission.Off });
        Assert.Null(tools.Offered);
        var (result, _) = await tools.Use(open, Url("https://leetcode.com/"));
        Assert.Single(opened);
        Assert.Contains("switched this off", result!.ToString());
    }

    [Fact]
    public async Task The_same_call_twice_in_a_reply_is_not_made_again()
    {
        using var temp = new TempStore();
        var opened = new List<Uri>();
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)]) { Auto = true };
        var call = Call("open_link", "url", "https://leetcode.com/");
        var model = new FakeToolModel(["Opening it.", call], ["Once more.", new FunctionCallContent("again", call.Name, call.Arguments)], ["There."]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(model, "open leetcode", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Single(opened);
        Assert.Contains(model.Requests[2].SelectMany(m => m.Contents).OfType<FunctionResultContent>(), r => r.Result!.ToString()!.Contains("already done"));
        Assert.Single(temp.Store.After("shino", 0), l => l.Speaker == Speaker.Cabinet);
    }

    [Fact]
    public async Task Only_web_pages_can_be_opened_and_a_look_alike_address_shows_as_one()
    {
        var opened = new List<Uri>();
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)]) { AskPlayer = (_, _) => Task.FromResult(Answer.Once) };

        var (result, record) = await tools.Use(tools.All.Single().Function, Url("file:///C:/Windows/System32/calc.exe"));

        Assert.Empty(opened);
        Assert.Contains("didn't work", result!.ToString());
        Assert.Contains("didn't work", record);
        Assert.Null(BuiltIn.Page("javascript:alert(1)"));
        Assert.Null(BuiltIn.Page("steam://rungameid/570"));
        Assert.StartsWith("xn--", BuiltIn.Shown(BuiltIn.Page("https://gооgle.com/login")!));        // Cyrillic o's: the browser's own spelling
        Assert.Equal("evil.example", BuiltIn.Shown(BuiltIn.Page("https://leetcode.com@evil.example/")!));
    }

    [Fact]
    public async Task The_tools_offered_are_one_sorted_list_without_those_switched_off()
    {
        using var temp = new TempStore();
        var tools = new Toolbox([BuiltIn.OpenLink(_ => { }), Jot([])]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeToolModel(["Hi!"], ["Hello again!"]);

        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);
        await conversation.Answer(model, "hi again", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(["agenda", "jot", "open_link"], model.Options[0]!.Tools!.Select(t => t.Name));
        Assert.Same(model.Options[0]!.Tools, model.Options[1]!.Tools);                  // the same list: the cached prompt start holds
        Assert.Contains("Your abilities", model.Requests[0][0].Text);
        tools.Choose(new Dictionary<string, Permission> { ["open_link"] = Permission.Off });
        Assert.Equal(["agenda", "jot"], tools.Offered!.Select(t => t.Name));

        // with every ability switched off she still keeps the player's agenda with its tool
        tools.Choose(new Dictionary<string, Permission> { ["open_link"] = Permission.Off, ["jot"] = Permission.Off });
        var plain = new FakeToolModel(["Hi!"]);
        await conversation.Answer(plain, "hi", "", _ => { }, (_, _) => { }, tools: tools);
        Assert.Equal(["agenda"], plain.Options[0]!.Tools!.Select(t => t.Name));
    }

    [Fact]
    public async Task A_tool_that_tells_her_what_she_would_simply_know_gets_no_line_of_hers_before_it()
    {
        using var temp = new TempStore();
        var tools = new Toolbox([BuiltIn.OpenLink(_ => { }), BuiltIn.Cabinet(_ => "Play time banked: 1 h.")]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeToolModel(["Hi!"]);

        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Contains("Before you use a tool (not cabinet_numbers: what it tells you, you simply know), say a short line first", model.Requests[0][0].Text);
        tools.Choose(new Dictionary<string, Permission> { ["cabinet_numbers"] = Permission.Off });
        Assert.Empty(tools.Known);                                                      // switched off: not named
    }

    [Fact]
    public async Task She_is_told_which_tools_will_ask_on_a_card_as_things_are_set_so_she_asks_in_words_only_for_those()
    {
        using var temp = new TempStore();
        var tools = new Toolbox([BuiltIn.OpenLink(_ => { }), Jot([])]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));
        var model = new FakeToolModel(["Hi!"], ["Hi!"], ["Hi!"]);

        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);
        Assert.Contains("These ask the player on a card by your stool before they act: open_link.", model.Requests[0][0].Text);

        tools.Choose(new Dictionary<string, Permission> { ["open_link"] = Permission.Allowed });   // Always, as the player set music
        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);
        Assert.Contains("None of them needs asking: just use them.", model.Requests[^1][0].Text);
        // nor do the tools' own words say so: music's "The player is asked first" had her ask "Shall I…?" when set to Always (2026-10-11)
        Assert.All(model.Options[^1]!.Tools!, t => Assert.DoesNotContain("asked first", t.Description));

        tools.Choose(new Dictionary<string, Permission>());
        tools.Auto = true;                                                                          // Auto goes ahead too
        await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);
        Assert.Contains("None of them needs asking: just use them.", model.Requests[^1][0].Text);
    }

    [Fact]
    public async Task A_model_without_tools_answers_without_them_from_then_on()
    {
        using var temp = new TempStore();
        var tools = new Toolbox([Jot([])]);
        var model = new FakeToolModel(["Hi!"]) { RefusesTools = new InvalidOperationException("registry.ollama.ai/library/gemma:2b does not support tools") };
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        var answer = await conversation.Answer(model, "hi", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal("Hi!", answer);
        Assert.True(tools.Refused);
        Assert.Null(tools.Offered);
        Assert.Equal(2, model.Requests.Count);
        Assert.DoesNotContain("Your abilities", model.Requests[1][0].Text);
        Assert.Equal([Speaker.User, Speaker.Her], temp.Store.After("shino", 0).Select(l => l.Speaker));     // the player's line once
        Assert.False(Toolbox.Refuses(new HttpRequestException("Connection refused")));
        Assert.True(Toolbox.Refuses(new InvalidOperationException("deepseek-reasoner does not support Function Calling")));
    }

    [Fact]
    public async Task Cut_off_while_she_asks_leaves_it_undone_and_keeps_what_she_said()
    {
        using var temp = new TempStore();
        var opened = new List<Uri>();
        using var cut = new CancellationTokenSource();
        var tools = new Toolbox([BuiltIn.OpenLink(opened.Add)])
        {
            AskPlayer = async (_, cancel) =>
            {
                cut.Cancel();                                       // the player clicks her instead of answering
                await Task.Delay(Timeout.Infinite, cancel);
                return Answer.Once;
            },
        };
        var model = new FakeToolModel(["Want me to open it?", Call("open_link", "url", "https://leetcode.com/")]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversation.Answer(model, "open leetcode", "", _ => { }, (_, _) => { }, cut.Token, tools));

        Assert.Empty(opened);
        Assert.Equal((Speaker.Her, "Want me to open it?"), temp.Store.After("shino", 0).Select(l => (l.Speaker, l.Text)).Last());
    }
}
