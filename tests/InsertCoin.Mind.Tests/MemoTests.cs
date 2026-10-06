using Microsoft.Extensions.AI;
using OpenAI.Chat;
using static InsertCoin.Mind.Tests.FakeToolModel;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace InsertCoin.Mind.Tests;

/// <summary>Memos: long text she prints from the cabinet, or the player writes, kept until they throw it away.</summary>
public class MemoTests
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 19, 30, 0, TimeSpan.FromHours(8));
    private static readonly Persona Shino = new("Shino", "Shino is cheerful.");

    [Fact]
    public void A_new_memo_hangs_from_the_slot_and_the_one_hanging_before_goes_to_the_archive()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var first = store.AddMemo("Two Sum hint", "Think about what you've already seen.", "shino", Friday);
        var second = store.AddMemo("Shopping", "- milk\n- eggs", null, Friday.AddMinutes(5));

        var memos = store.Memos();
        Assert.Equal([second, first], memos.Select(m => m.Id));                             // newest first
        Assert.Equal([MemoPlace.Hanging, MemoPlace.Archived], memos.Select(m => m.Place));
        Assert.Equal([null, "shino"], memos.Select(m => m.By));
        var own = store.AddMemo("Later", "something", null, Friday.AddMinutes(9), MemoPlace.Archived);
        Assert.Equal(MemoPlace.Hanging, store.Memos().Single(m => m.Id == second).Place);  // written into the archive: the hanging one stays
        Assert.Equal(MemoPlace.Archived, store.Memos().Single(m => m.Id == own).Place);
    }

    [Fact]
    public void Pinned_memos_take_the_first_free_place_on_the_side_until_it_is_full()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var ids = Enumerable.Range(0, Memo.PinnedAtMost + 1).Select(i => store.AddMemo($"Memo {i}", "text", null, Friday.AddMinutes(i))).ToList();

        foreach (var id in ids.Take(Memo.PinnedAtMost))
            Assert.True(store.MoveMemo(id, MemoPlace.Pinned));
        Assert.False(store.MoveMemo(ids[^1], MemoPlace.Pinned));                                // the side is full
        Assert.Equal(MemoPlace.Hanging, store.Memos().Single(m => m.Id == ids[^1]).Place);

        store.MoveMemo(ids[1], MemoPlace.Archived);                                           // taken off: its place is free again
        Assert.True(store.MoveMemo(ids[^1], MemoPlace.Pinned));
        Assert.Equal(1, store.Memos().Single(m => m.Id == ids[^1]).Pin);
        Assert.Equal(Enumerable.Range(0, Memo.PinnedAtMost), store.Memos().Where(m => m.Place == MemoPlace.Pinned).Select(m => m.Pin).Order());
    }

    [Fact]
    public void A_memo_too_long_is_cut_and_one_can_be_changed_or_thrown_away()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var id = store.AddMemo(new string('t', 200), new string('x', Memo.TextLength + 50), "shino", Friday);
        var memo = store.Memos().Single();
        Assert.Equal((Memo.TitleLength, Memo.TextLength), (memo.Title.Length, memo.Text.Length));

        store.UpdateMemo(id, " Groceries ", "milk ");
        Assert.Equal(("Groceries", "milk"), (store.Memos().Single().Title, store.Memos().Single().Text));
        store.DeleteMemo(id);
        Assert.Empty(store.Memos());
    }

    [Fact]
    public void A_memo_reads_as_rich_text_with_its_code_and_lists_and_brackets_kept()
    {
        const string text = "## The idea\nKeep a **map** of what you've seen: `seen[x]`.\n- one pass\n2. look up `target - x`\n```\nfor x in nums:\n    seen[x] = i\n```";

        Assert.Equal("[b]The idea[/b]\nKeep a [b]map[/b] of what you've seen: [code]seen[lb]x][/code].\n[ul]one pass[/ul]\n[ol type=1]look up [code]target - x[/code][/ol]\n" +
                     "[code]for x in nums:\n    seen[lb]x] = i\n[/code]", Memo.ToBbcode(text));
        Assert.Equal("[ul]milk\neggs[/ul]\nthat's all", Memo.ToBbcode("- milk\n- eggs\nthat's all"));          // one list, an item a line
        Assert.Equal("[b]x[/b] [lb]not a tag]", Memo.ToBbcode("**x** [not a tag]"));
        Assert.Equal("[code]open", Memo.ToBbcode("```\nopen")[..10]);                           // a fence never closed still closes
        Assert.EndsWith("[/code]", Memo.ToBbcode("```\nopen"));
    }

    [Fact]
    public void The_paper_hanging_from_the_slot_shows_the_first_words_plainly()
    {
        var memo = new Memo(1, "Hint", "- Think about pairs.\n```\ncode here\n```\nThen use a map, and look up what you need as you go along the list.", null, Friday, MemoPlace.Hanging);

        Assert.Equal("Think about pairs. code here Then use a map, and look up what you…", memo.Glimpse(70));
        Assert.Equal("Think about…", memo.Glimpse(18));                                          // cut at a word
    }

    [Fact]
    public async Task She_prints_a_memo_without_asking_and_says_only_the_gist()
    {
        using var temp = new TempStore();
        var printed = new List<(string, string)>();
        var asked = 0;
        var tools = new Toolbox([BuiltIn.PrintMemo((title, text) => { printed.Add((title, text)); return "Printed."; })])
        {
            AskPlayer = (_, _) => { asked++; return Task.FromResult(Answer.No); },
        };
        var model = new FakeToolModel(["Here, I'll write it down.", Call("print_memo", "title", "Two Sum hint")], ["It's on the cabinet."]);
        // the model's call carries both arguments: the helper gives one, so add the text
        ((FunctionCallContent)model.Rounds[0][1]).Arguments!["text"] = "Use a map.";
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        var answer = await conversation.Answer(model, "give me a hint", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal([("Two Sum hint", "Use a map.")], printed);
        Assert.Equal(0, asked);
        Assert.Equal("Here, I'll write it down. It's on the cabinet.", answer);
    }

    [Fact]
    public async Task She_is_seen_starting_a_memo_while_its_words_are_still_coming()
    {
        using var temp = new TempStore();
        var tools = new Toolbox([BuiltIn.PrintMemo((_, _) => "Printed.")]);
        var events = new List<string>();
        tools.Starting += tool => events.Add($"starting {tool.Name}");
        tools.Using += tool => events.Add($"using {tool.Name}");
        // OpenAI's client hands over the call's first piece (with its name) long before the whole call
#pragma warning disable OPENAI001                       // its model factory is marked for evaluation
        var first = new ChatResponseUpdate(ChatRole.Assistant, "")
        {
            RawRepresentation = OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                toolCallUpdates: [OpenAIChatModelFactory.StreamingChatToolCallUpdate(0, "call-1", ChatToolCallKind.Function, "print_memo", BinaryData.FromString(""))]),
        };
#pragma warning restore OPENAI001
        var call = new FunctionCallContent("call-1", "print_memo", new Dictionary<string, object?> { ["title"] = "Hint", ["text"] = "Use a map." });
        var model = new FakeToolModel(["Writing it.", first, call], ["There."]);
        var conversation = new Conversation(temp.Store, "shino", Shino, new FakeClock(Friday));

        await conversation.Answer(model, "a hint please", "", _ => { }, (_, _) => { }, tools: tools);

        Assert.Equal(["starting print_memo", "using print_memo"], events);
        Assert.Null(Provider.ToolStarting(new ChatResponseUpdate(ChatRole.Assistant, "words")));
        tools.Begun("no_such_tool");
        Assert.Equal(2, events.Count);
    }
}
