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
    public void A_pinned_memo_stays_where_it_was_put_the_latest_on_top_until_the_side_is_full()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var ids = Enumerable.Range(0, Memo.PinnedAtMost + 1).Select(i => store.AddMemo($"Memo {i}", "text", null, Friday.AddMinutes(i))).ToList();

        foreach (var (id, i) in ids.Take(Memo.PinnedAtMost).Select((id, i) => (id, i)))
            Assert.True(store.PinMemo(id, 40 + i, 100 + 10 * i, -2));
        var first = store.Memos().Single(m => m.Id == ids[0]);
        Assert.Equal((MemoPlace.Pinned, (40.0, 100.0), -2.0), (first.Place, first.Spot!.Value, first.Tilt));
        Assert.False(store.PinMemo(ids[^1], 60, 300, 0));                                        // the side is full
        Assert.Equal(MemoPlace.Hanging, store.Memos().Single(m => m.Id == ids[^1]).Place);

        Assert.True(store.PinMemo(ids[0], 90, 400, 3));                                           // moved: full or not, and now on top
        var moved = store.Memos().Single(m => m.Id == ids[0]);
        Assert.Equal((90.0, 400.0), moved.Spot!.Value);
        Assert.Equal(store.Memos().Where(m => m.Place == MemoPlace.Pinned).Max(m => m.Pin), moved.Pin);

        store.MoveMemo(ids[1], MemoPlace.Archived);                                              // taken down: room again
        Assert.True(store.PinMemo(ids[^1], 60, 300, 0));
        Assert.Throws<ArgumentException>(() => store.MoveMemo(ids[2], MemoPlace.Pinned));        // pinning takes a spot
    }

    [Fact]
    public void A_memo_has_a_paper_and_maybe_a_sticker()
    {
        using var temp = new TempStore();
        var store = temp.Store;
        var hers = store.AddMemo("Hint", "text", "shino", Friday);
        var own = store.AddMemo("Note", "text", null, Friday, MemoPlace.Archived, "mint", "star");

        Assert.Equal((Memo.Cream, (string?)null), (store.Memos().Single(m => m.Id == hers).Colour, store.Memos().Single(m => m.Id == hers).Sticker));
        Assert.Equal(("mint", (string?)"star"), (store.Memos().Single(m => m.Id == own).Colour, store.Memos().Single(m => m.Id == own).Sticker));
        Assert.Equal(Memo.Yellow, Memo.ColourFor(null));

        store.StyleMemo(hers, "pink", "heart");
        store.StyleMemo(own, "tartan", null);                                                     // an unknown paper is ignored; no sticker takes it off
        Assert.Equal(("pink", (string?)"heart"), (store.Memos().Single(m => m.Id == hers).Colour, store.Memos().Single(m => m.Id == hers).Sticker));
        Assert.Equal(("mint", (string?)null), (store.Memos().Single(m => m.Id == own).Colour, store.Memos().Single(m => m.Id == own).Sticker));
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
    public async Task A_memo_the_player_wrote_is_hers_to_act_on_or_let_be()
    {
        using var temp = new TempStore();
        var clock = new FakeClock(Friday);
        var conversation = new Conversation(temp.Store, "shino", Shino, clock);
        var note = Memo.Written("Groceries", "milk, eggs, rice");

        Assert.Contains("\"Groceries\"", note);
        Assert.Contains("milk, eggs, rice", note);
        Assert.Contains("[quiet]", note);
        Assert.EndsWith("…", Memo.Written("Long", new string('x', Memo.ToldAtMost + 10)).Split("\n\n")[0]);   // she is told the start of a long one

        // nothing in it for her: she says nothing, and the memo stays in the conversation for later
        Assert.Equal("", await conversation.Announce(new FakeModel("[quiet]"), note, "", _ => { }, (_, _) => { }));
        Assert.Equal([Speaker.Cabinet], temp.Store.After("shino", 0).Select(l => l.Speaker));

        // something for her: a reminder goes on the agenda, and she says so
        clock.Now = Friday.AddMinutes(5);
        var answer = await conversation.Announce(new FakeModel("[remind: 2026-10-03 08:00 | Pack the charger] I'll remind you in the morning."),
                                                 Memo.Written("Trip", "remember to pack the charger"), "", _ => { }, (_, _) => { });
        Assert.Equal("I'll remind you in the morning.", answer);
        Assert.Contains(temp.Store.Agenda(), p => p.Text == "Pack the charger");
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
